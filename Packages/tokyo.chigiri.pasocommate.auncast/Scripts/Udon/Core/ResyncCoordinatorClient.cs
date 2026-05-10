
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// ResyncCoordinator との通信を担当するクライアント側コンポーネント。
    /// スロット割当、Resync 要求・キャンセル・結果報告、グローバル Resync の採用を行う。
    /// LocalDualPlayerController と同一 GameObject に配置され、ネットワーク RPC を発行する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ResyncCoordinatorClient : UdonSharpBehaviour
    {
        // =================================================================
        //  定数 — Resync 要求の理由コード（ログ・UI 表示用）
        // =================================================================
        public const int REQUEST_REASON_FAILURE = 0;
        public const int REQUEST_REASON_MANUAL = 1;
        public const int REQUEST_REASON_SILENCE = 2;

        // =================================================================
        //  Inspector 参照
        // =================================================================
        /// <summary>Owner 側で全スロットを管理する Coordinator 本体。</summary>
        [SerializeField] private ResyncCoordinator coordinator;
        /// <summary>FSM 状態遷移の判断に使うローカルコントローラ。</summary>
        [SerializeField] private LocalDualPlayerController controller;

        // =================================================================
        //  Inspector パラメータ
        // =================================================================
        [Header("Resync Cycle")]
        [Tooltip("GRANTED 後、切替完了までの最大許容時間（秒）")]
        [SerializeField] private float resyncCycleTimeoutSec = 45.0f;

        [Header("Silence-Triggered Global Resync")]
        [Tooltip("最後の Resync から無音検知を再有効化するまでの時間（秒）")]
        [SerializeField] private float silenceSuppressSec = 150.0f;

        [Header("Cooldown")]
        [Tooltip("LoadURL 完了後のクールダウン（秒）")]
        [SerializeField] private float localCooldownSec = 5.0f;

        [Header("Retry")]
        [Tooltip("再試行の基本待機時間（秒）")]
        [SerializeField] private float baseCooldownSec = 15.0f;

        [Tooltip("再試行の最大待機時間（秒）")]
        [SerializeField] private float maxRetryCooldownSec = 120.0f;

        [Header("Reboot")]
        [Tooltip("リブートボタン有効化: 推定待ち時間を超えてさらに待つ猶予（秒）")]
        [SerializeField] private float rebootGrantMarginSec = 30.0f;

        [Header("Debug")]
        [Tooltip("要所ログを詳細出力する")]
        [SerializeField] private bool verboseLogging = true;

        [Header("Timeline")]
        [Tooltip("タイムラインログを出力する")]
        [SerializeField] private bool _timelineLogging;

        // =================================================================
        //  ローカル状態
        //  — Coordinator はネットワーク同期で遅延があるため、ローカル側でも
        //    要求中フラグやタイムスタンプを保持してタイムアウト判定等を行う。
        // =================================================================
        private int _mySlotIndex = -1;
        private bool _resyncRequested;
        private float _requestStartedAt;
        private int _requestReason = REQUEST_REASON_FAILURE;
        private float _localCooldownUntil;
        private int _consecutiveFailCount;

        private float _cycleStartedAt;
        private float _lastResyncCompletedAt;
        private int _lastGlobalForceRebootSeq = -1;

        // スロット割当フォールバック
        private float _lastSlotRequestAt;
        private const float SLOT_REQUEST_INTERVAL = 5.0f;

        // イベント再送
        private float _lastResyncRequestSentAt;
        private const float RESYNC_REQUEST_RETRY_SEC = 3.0f;

        // キャンセル送信〜Coordinator 反映までの Adoption 抑制
        private float _adoptionSuppressedUntil;
        private const float CANCEL_ADOPTION_SUPPRESS_SEC = 2.0f;

        // =================================================================
        //  スロット割当
        // =================================================================

        /// <summary>
        /// ローカルプレイヤーに Coordinator スロットが割り当てられているか確認し、
        /// 未割当なら Owner へ割当要求 RPC を送る。スロットがないと Resync 操作不可。
        /// </summary>
        public bool TryEnsureSlotAssigned()
        {
            if (_mySlotIndex >= 0) return true;
            if (coordinator == null) return false;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;

            // まず同期済み配列から自プレイヤー ID を検索
            _mySlotIndex = coordinator.FindSlotByPlayerId(local.playerId);
            if (_mySlotIndex >= 0)
            {
                if (_timelineLogging) TL($"a=SLOT_ASSIGNED slot={_mySlotIndex}");
                LogVerbose($"Assigned local slot: {_mySlotIndex}");
                return true;
            }

            // 見つからない場合は定期的に Owner へ割当を依頼
            float now = Time.time;
            if (now - _lastSlotRequestAt >= SLOT_REQUEST_INTERVAL)
            {
                _lastSlotRequestAt = now;
                coordinator.SendCustomNetworkEvent(
                    NetworkEventTarget.Owner, "OnRequestSlot", local.playerId);
            }
            return false;
        }

        /// <summary>割当済みスロットインデックス（未割当時は -1）。</summary>
        public int GetMySlotIndex() { return _mySlotIndex; }

        // =================================================================
        //  グローバル強制リブートポーリング
        // =================================================================

        /// <summary>
        /// Coordinator のグローバル強制リブートシーケンス番号を監視し、
        /// インクリメントを検出したら true を返す。スタッフ操作による全員一斉 Resync に対応。
        /// </summary>
        public bool PollGlobalForceReboot()
        {
            if (coordinator == null) return false;

            int forceRebootSeq = coordinator.GetGlobalForceRebootSeq();
            if (_lastGlobalForceRebootSeq < 0)
            {
                // 初回は現在値を記録するだけ（Join 直後のトリガ防止）
                _lastGlobalForceRebootSeq = forceRebootSeq;
            }
            else if (forceRebootSeq != _lastGlobalForceRebootSeq)
            {
                _lastGlobalForceRebootSeq = forceRebootSeq;
                if (_timelineLogging) TL($"a=GLOBAL_FORCE_REBOOT seq={forceRebootSeq}");
                LogWarning($"Global force reboot detected: seq={forceRebootSeq}");
                return true;
            }

            return false;
        }

        // =================================================================
        //  Resync 要求
        // =================================================================

        /// <summary>
        /// クールダウンや重複チェックを通過した場合に Resync 要求 RPC を送信する。
        /// 呼び出し元は障害検知・手動操作・無音検知など reason で理由を区別する。
        /// </summary>
        public bool TryRequestResync(float now, int reason)
        {
            if (_resyncRequested) return false;
            if (now < _localCooldownUntil) return false;
            if (coordinator == null) return false;
            if (_mySlotIndex < 0) return false;

            // Coordinator 側で既にキューイング中なら二重要求しない
            int coordState = coordinator.GetResyncState(_mySlotIndex);
            if (coordState != ResyncCoordinator.STATE_NONE) return false;

            coordinator.SendCustomNetworkEvent(
                NetworkEventTarget.Owner, "OnResyncRequest", _mySlotIndex);

            _resyncRequested = true;
            _requestStartedAt = now;
            _lastResyncRequestSentAt = now;
            _requestReason = reason;
            if (_timelineLogging) TL($"a=RESYNC_REQUEST reason={GetRequestReasonText(reason)} slot={_mySlotIndex}");
            LogMessage($"Requested Resync (reason={GetRequestReasonText(reason)}, slot={_mySlotIndex})");
            return true;
        }

        // =================================================================
        //  Coordinator ポーリング（統合）
        // =================================================================

        /// <summary>
        /// Coordinator の状態をポーリングし、遷移先 STATE を返す。-1 = 遷移なし。
        /// ACTIVE_PLAYING: 外部起因 Resync の採用検出（グローバル Resync 等）
        /// REQUEST_PENDING: 自己要求の Grant 検出
        /// </summary>
        public int PollResyncCoordinator(float now, int currentLocalState)
        {
            if (coordinator == null || _mySlotIndex < 0) return -1;

            int coordState = coordinator.GetResyncState(_mySlotIndex);

            switch (currentLocalState)
            {
                case LocalDualPlayerController.STATE_ACTIVE_PLAYING:
                    if (now < _localCooldownUntil) return -1;
                    // キャンセル送信後、Coordinator が NONE に戻るまで採用を抑制
                    if (now < _adoptionSuppressedUntil)
                    {
                        if (coordState == ResyncCoordinator.STATE_NONE)
                            _adoptionSuppressedUntil = 0f;
                        else
                            return -1;
                    }
                    if (coordState != ResyncCoordinator.STATE_QUEUED
                        && coordState != ResyncCoordinator.STATE_GRANTED) return -1;

                    _resyncRequested = true;
                    _requestStartedAt = now;
                    _requestReason = REQUEST_REASON_MANUAL;
                    if (_timelineLogging) TL($"a=ADOPTION coordState={coordState} slot={_mySlotIndex}");
                    LogMessage($"Adopted queued Resync request (coordState={coordState})");
                    return coordState == ResyncCoordinator.STATE_GRANTED
                        ? LocalDualPlayerController.STATE_RESERVED
                        : LocalDualPlayerController.STATE_REQUEST_PENDING;

                case LocalDualPlayerController.STATE_REQUEST_PENDING:
                    if (coordState == ResyncCoordinator.STATE_GRANTED)
                    {
                        if (_timelineLogging) TL($"a=RESYNC_GRANTED slot={_mySlotIndex}");
                        LogMessage($"Resync granted after {(now - _requestStartedAt):F2}s");
                        return LocalDualPlayerController.STATE_RESERVED;
                    }
                    return -1;

                default:
                    return -1;
            }
        }

        // =================================================================
        //  キャンセル
        // =================================================================

        /// <summary>
        /// Resync をキャンセルし Coordinator へ通知する。
        /// キャンセル後、Coordinator が NONE に戻るまでの間に同スロットの
        /// 再採用（Adoption）が発動しないよう一時的に抑制期間を設ける。
        /// </summary>
        public bool CancelResync()
        {
            if (_timelineLogging) TL($"a=RESYNC_CANCEL slot={_mySlotIndex}");
            if (_mySlotIndex >= 0 && coordinator != null)
            {
                int coordState = coordinator.GetResyncState(_mySlotIndex);
                LogVerbose($"CancelResync: slot={_mySlotIndex}, coordState={coordState}");
                if (coordState != ResyncCoordinator.STATE_NONE)
                {
                    coordinator.SendCustomNetworkEvent(
                        NetworkEventTarget.Owner, "OnCancelSlot", _mySlotIndex);
                    // Coordinator 反映までの間、QUEUED/GRANTED を再採用しないよう抑制
                    _adoptionSuppressedUntil = Time.time + CANCEL_ADOPTION_SUPPRESS_SEC;
                }
            }

            _resyncRequested = false;
            _requestReason = REQUEST_REASON_FAILURE;
            return true;
        }

        // =================================================================
        //  結果報告
        // =================================================================

        /// <summary>
        /// Resync サイクルの成否を Coordinator Owner へ報告する。
        /// Owner はこの結果を元にスロット状態を NONE に戻し、次の要求を受け付ける。
        /// </summary>
        public void ReportResult(bool success)
        {
            if (coordinator == null || _mySlotIndex < 0) return;
            if (_timelineLogging) TL($"a=RESYNC_RESULT success={(success ? 1 : 0)} slot={_mySlotIndex}");

            string eventName = success ? "OnReportSuccess" : "OnReportFail";
            coordinator.SendCustomNetworkEvent(
                NetworkEventTarget.Owner, eventName, _mySlotIndex);
        }

        // =================================================================
        //  リトライ・再送
        // =================================================================

        /// <summary>
        /// 要求 RPC がネットワーク上で消失した可能性を検出する。
        /// Coordinator 側が NONE のまま一定時間経過していれば再送が必要。
        /// </summary>
        public bool ShouldRetryResyncRequest(float now)
        {
            if (coordinator == null || _mySlotIndex < 0) return false;
            return coordinator.GetResyncState(_mySlotIndex) == ResyncCoordinator.STATE_NONE
                && (now - _lastResyncRequestSentAt) >= RESYNC_REQUEST_RETRY_SEC;
        }

        /// <summary>
        /// 要求 RPC を再送し、タイムスタンプを更新する（イベント消失フォールバック）。
        /// </summary>
        public void MarkRetrySent(float now)
        {
            _lastResyncRequestSentAt = now;
            coordinator.SendCustomNetworkEvent(
                NetworkEventTarget.Owner, "OnResyncRequest", _mySlotIndex);
            if (_timelineLogging) TL($"a=RESYNC_RETRY slot={_mySlotIndex}");
            LogVerbose("Resync request re-sent (event lost fallback)");
        }

        // =================================================================
        //  Standby Running 報告
        // =================================================================

        /// <summary>
        /// Standby プレイヤーの接続開始を Coordinator に通知する。
        /// Owner はこれを受けて GRANTED → RUNNING へ遷移させ、切替判定に使う。
        /// </summary>
        public void ReportRunning()
        {
            if (coordinator == null || _mySlotIndex < 0) return;
            if (_timelineLogging) TL($"a=REPORT_RUNNING slot={_mySlotIndex}");
            coordinator.SendCustomNetworkEvent(
                NetworkEventTarget.Owner, "OnReportRunning", _mySlotIndex);
        }

        // =================================================================
        //  状態アクセサ
        // =================================================================

        /// <summary>次の Resync 要求を受け付ける Time.time。</summary>
        public float GetLocalCooldownUntil() { return _localCooldownUntil; }
        public void SetLocalCooldownUntil(float value) { _localCooldownUntil = value; }
        /// <summary>連続失敗回数（指数バックオフの計算に使用）。</summary>
        public int GetConsecutiveFailCount() { return _consecutiveFailCount; }
        public void SetConsecutiveFailCount(int value) { _consecutiveFailCount = value; }
        /// <summary>現在 Resync 要求中かどうか。</summary>
        public bool IsResyncRequested() { return _resyncRequested; }
        public void SetResyncRequested(bool value) { _resyncRequested = value; }
        /// <summary>直近の要求理由コード（UI 表示・ログ用）。</summary>
        public int GetRequestReason() { return _requestReason; }
        public void SetRequestReason(int value) { _requestReason = value; }
        /// <summary>要求を送信した Time.time（待ち時間表示に使用）。</summary>
        public float GetRequestStartedAt() { return _requestStartedAt; }
        /// <summary>切替サイクル開始を記録（タイムアウト判定の起点）。</summary>
        public void MarkCycleStarted(float now) { _cycleStartedAt = now; }
        /// <summary>切替サイクルが許容時間を超過したか判定。</summary>
        public bool IsCycleTimedOut(float now) { return (now - _cycleStartedAt) > resyncCycleTimeoutSec; }
        /// <summary>LoadURL 完了後のローカルクールダウン秒数。</summary>
        public float GetLocalCooldownSec() { return localCooldownSec; }
        /// <summary>再試行の基本待機秒数。</summary>
        public float GetBaseCooldownSec() { return baseCooldownSec; }
        /// <summary>指数バックオフの上限秒数。</summary>
        public float GetMaxRetryCooldownSec() { return maxRetryCooldownSec; }
        /// <summary>無音検知による自動 Resync の抑制秒数。</summary>
        public float GetSilenceSuppressSec() { return silenceSuppressSec; }

        /// <summary>最後に Resync が完了した Time.time。</summary>
        public float GetLastResyncCompletedAt() { return _lastResyncCompletedAt; }
        /// <summary>Resync 完了時刻を記録する。無音検知の抑制判定に使う。</summary>
        public void OnResyncCompleted(float now) { _lastResyncCompletedAt = now; }

        /// <summary>Coordinator に問い合わせた推定待ち時間（リブートボタン表示判定用）。</summary>
        public float GetRebootWaitEstimate()
        {
            if (coordinator == null || _mySlotIndex < 0) return 0f;
            return coordinator.EstimateWaitTime(_mySlotIndex);
        }

        /// <summary>推定待ち時間に加算する猶予秒数（リブートボタン有効化閾値）。</summary>
        public float GetRebootGrantMarginSec() { return rebootGrantMarginSec; }

        /// <summary>
        /// 前回 Resync 完了から十分な時間が経過し、無音検知による自動 Resync を
        /// 許可してよいか判定する。短時間での連続 Resync を防ぐためのガード。
        /// </summary>
        public bool IsSilenceAutoResyncEligible(float now)
        {
            return (now - _lastResyncCompletedAt) > silenceSuppressSec;
        }

        /// <summary>Coordinator 参照（外部からの状態確認用）。</summary>
        public ResyncCoordinator GetCoordinator() { return coordinator; }

        // =================================================================
        //  ログ
        // =================================================================

        private void LogMessage(string message)
        {
            Debug.Log($"[AunCast/ResyncClient] {message}", this);
        }

        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            LogMessage(message);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/ResyncClient] {message}", this);
        }

        private string GetRequestReasonText(int reason)
        {
            switch (reason)
            {
                case REQUEST_REASON_FAILURE: return "Failure";
                case REQUEST_REASON_MANUAL: return "Manual";
                case REQUEST_REASON_SILENCE: return "Silence";
                default: return "Unknown";
            }
        }

        private void TL(string eventAndData)
        {
            Debug.Log($"[AunCast:TL] st={Networking.GetServerTimeInMilliseconds()} c=RCC {eventAndData}");
        }
    }
}
