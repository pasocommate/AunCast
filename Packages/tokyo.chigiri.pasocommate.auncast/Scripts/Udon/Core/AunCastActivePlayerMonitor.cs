
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// Active Player の生存監視・ドリフト計測と、Standby Player の検証を担当するコンポーネント。
    /// AunCastDualPlayerController と同一 GameObject に配置される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastActivePlayerMonitor : UdonSharpBehaviour
    {
        // OnVideoStart 直後の GetTime() は 1〜2 秒ほど進まないことがあるため、
        // 通常の stallTimeoutSec より長く初回前進を待つ。
        // ただし無期限に待つと、IsPlaying が false に戻る静かな失敗を見逃す。
        private const float INITIAL_ADVANCE_TIMEOUT_SEC = 5.0f;

        // =================================================================
        //  Inspector 参照
        // =================================================================
        [SerializeField] private AunCastVideoPlayerManager playerManagerA;
        [SerializeField] private AunCastVideoPlayerManager playerManagerB;

        // =================================================================
        //  Inspector パラメータ
        // =================================================================
        [Header("Monitoring")]
        [Tooltip("Active Player の監視間隔（秒）")]
        [SerializeField] private float monitorIntervalSec = 0.1f;

        [Tooltip("GetTime() の最小前進量（秒）")]
        [SerializeField] private float minAdvanceThresholdSec = 0.01f;

        [Tooltip("生存確認に必要な連続前進回数")]
        [SerializeField] private int minConsecutiveAdvances = 5;

        [Tooltip("停止判定の継続時間（秒）")]
        [SerializeField] private float stalledTimeoutSec = 2.0f;

        [Header("Standby Verification")]
        [Tooltip("検証フェーズの最小時間（秒）")]
        [SerializeField] private float verifyMinDurationSec = 0.5f;

        [Header("Drift")]
        [Tooltip("ドリフト EMA の時定数（秒）。大きいほど緩やかに追従する")]
        [SerializeField] private float driftSmoothingTimeConstant = 1.5f;

        [Tooltip("安定再生開始直後にドリフト積算を抑制する猶予時間（秒）")]
        [SerializeField] private float driftWarmupSec = 5.0f;

        [Header("Timeline")]
        [Tooltip("タイムラインログを出力する")]
        [SerializeField] private bool _timelineLogging;

        // =================================================================
        //  Active Player 監視状態
        // =================================================================
        private bool _activeIsA = true;
        private float _lastActiveTime;
        private float _lastObservedAt;

        /// <summary>連続前進回数。ヒステリシスにより一時的なジッターで即障害判定しないためのカウンタ。</summary>
        private int _consecutiveAdvanceCount;
        /// <summary>連続停滞回数。閾値超過で障害判定に使用する。</summary>
        private int _consecutiveStallCount;
        private float _lastMonitorTime;
        /// <summary>停滞が始まった時刻。タイムアウト判定の起点として使用する。</summary>
        private float _stallStartedAt;
        /// <summary>Active 監視を開始した時刻。初回前進タイムアウトの起点として使用する。</summary>
        private float _activeMonitoringStartedAt = -1f;
        /// <summary>プレイヤー A が最初のフレームデコードを完了したか。未完了時は停滞誤検出を防ぐ。</summary>
        private bool _hasSeenTimeAdvanceA;
        /// <summary>プレイヤー B が最初のフレームデコードを完了したか。未完了時は停滞誤検出を防ぐ。</summary>
        private bool _hasSeenTimeAdvanceB;
        /// <summary>初回前進タイムアウトのログを監視セッションごとに一度だけ出すためのガード。</summary>
        private bool _initialAdvanceTimeoutLogged;

        // Standby Player 検証
        private float _verifyStartedAt;
        private int _standbyAdvanceCount;
        private float _lastStandbyTime;

        // ドリフト監視（絶対ドリフト方式）
        /// <summary>EMA 平滑化された絶対ドリフト値（秒）。ジッターを吸収しつつ累積ずれを検出する。</summary>
        private float _driftAccumulator;
        /// <summary>ドリフト基準点の実時間。計測が（再）有効化されるたびに現在時刻で確定し、以降の経過時間差を測る起点。計測無効時は 0 にクリアする。</summary>
        private float _baseWallTime;
        /// <summary>ドリフト基準点のプレイヤー時間。壁時間との差分で再生速度のずれを検出する。</summary>
        private float _basePlayerTime;
        /// <summary>安定再生を確認した時刻。未確認の間は 0。</summary>
        private float _stablePlaybackStartedAt;
        /// <summary>この時刻まではドリフト積算を抑制する。安定再生開始直後の偽陽性を防ぐ。</summary>
        private float _driftWarmupUntil;
        /// <summary>DRIFT_THRESHOLD ログの一回性ガード。監視セッションごとにリセットする。</summary>
        private bool _driftThresholdLogged;

        // =================================================================
        //  ロールバインド
        // =================================================================

        /// <summary>
        /// 現在どちらのプレイヤーが Active ロールかを設定する。
        /// AunCastDualPlayerController がロール変更を確定したタイミングで呼ばれ、以降の監視対象を決定する。
        /// </summary>
        public void BindRoles(bool activeIsA)
        {
            _activeIsA = activeIsA;
        }

        // =================================================================
        //  Active 監視初期化
        // =================================================================

        /// <summary>
        /// Active 監視セッションを新たに開始する。
        /// 停滞カウンタ・ドリフト基準点をリセットし、ウォームアップ期間を設定する。
        /// 切替直後やリロード後に呼ばれ、前回の残留状態による誤検出を防ぐ。
        /// </summary>
        public void InitializeForActive(float now)
        {
            AunCastVideoPlayerManager active = GetActiveManager();
            if (active != null)
                _lastActiveTime = active.GetTime();
            else
                _lastActiveTime = 0f;

            _lastObservedAt = now;
            _lastMonitorTime = now;
            _consecutiveAdvanceCount = 0;
            _consecutiveStallCount = 0;
            _stallStartedAt = 0f;
            _activeMonitoringStartedAt = now;
            _initialAdvanceTimeoutLogged = false;
            _driftAccumulator = 0f;
            _stablePlaybackStartedAt = 0f;
            _driftWarmupUntil = 0f;
            _baseWallTime = 0f;
            _basePlayerTime = 0f;
            _driftThresholdLogged = false;
            TL($"a=INIT_ACTIVE");
        }

        // =================================================================
        //  Standby 検証初期化
        // =================================================================

        /// <summary>
        /// Standby プレイヤーの検証状態をリセットする。
        /// 切替先が実際に再生可能かを確認するフェーズの開始時に呼ばれる。
        /// </summary>
        public void InitializeForStandby(float now)
        {
            _standbyAdvanceCount = 0;
            _lastStandbyTime = GetStandbyManager() != null ? GetStandbyManager().GetTime() : 0f;
            _verifyStartedAt = now;
        }

        // =================================================================
        //  Active Player ポーリング
        // =================================================================

        /// <summary>
        /// Active プレイヤーを定期的にサンプリングし、時間前進/停滞の判定とドリフト計測を行う。
        /// monitorIntervalSec 未満の呼び出しは早期リターンされる。
        /// 停滞が検出されると _stallStartedAt を記録し、DetectActiveFailure のタイムアウト判定に使われる。
        /// </summary>
        public void PollActive(float now)
        {
            if (now - _lastMonitorTime < monitorIntervalSec) return;
            _lastMonitorTime = now;

            AunCastVideoPlayerManager active = GetActiveManager();
            if (active == null) return;

            float currentPlayerTime = active.GetTime();
            bool isPlaying = active.IsPlaying();
            float delta = currentPlayerTime - _lastActiveTime;

            // 巻き戻り検出: シーク等で時間が後退した場合は全状態をリセットして再計測
            if (delta < 0)
            {
                _consecutiveAdvanceCount = 0;
                _consecutiveStallCount = 0;
                _driftAccumulator = 0f;
                _baseWallTime = 0f;
                _basePlayerTime = 0f;
                _stablePlaybackStartedAt = 0f;
                _driftWarmupUntil = 0f;
                _lastActiveTime = currentPlayerTime;
                _lastObservedAt = now;
                return;
            }

            // 最初のフレームデコード前は停滞判定を行わない（ロード中の偽陽性回避）
            bool hasSeenAdvance = _activeIsA ? _hasSeenTimeAdvanceA : _hasSeenTimeAdvanceB;
            if (!hasSeenAdvance)
            {
                if (delta > minAdvanceThresholdSec && isPlaying)
                {
                    if (_activeIsA) _hasSeenTimeAdvanceA = true;
                    else _hasSeenTimeAdvanceB = true;
                    TL($"a=FIRST_ADVANCE");
                }
                else
                {
                    _lastActiveTime = currentPlayerTime;
                    _lastObservedAt = now;
                    return;
                }
            }

            // 前進判定
            if (delta > minAdvanceThresholdSec && isPlaying)
            {
                _consecutiveAdvanceCount++;
                _consecutiveStallCount = 0;
                _stallStartedAt = 0f;
                if (_stablePlaybackStartedAt <= 0f && _consecutiveAdvanceCount >= minConsecutiveAdvances)
                    BeginStablePlayback(now);
            }
            else
            {
                _consecutiveStallCount++;
                if (_consecutiveAdvanceCount > 0)
                    _consecutiveAdvanceCount = 0;
                if (_stallStartedAt <= 0f)
                {
                    _stallStartedAt = now;
                    TL($"a=STALL_START");
                }
            }

            // ドリフト計算（絶対ドリフト方式 + EMA 平滑化）
            // 壁時間の経過とプレイヤー時間の経過の差分を測り、再生速度のずれを検出する。
            // 絶対方式によりサンプリングジッタの累積を避け、EMA で瞬間的な偏差を吸収する。
            // 基準点は計測無効時に 0 へクリアするため、（再）有効化の瞬間は必ず取り直され、
            // 平滑化前の rawDrift は 0 から始まる（計測中断中に生じたギャップを持ち越さない）。
            // 有効区間中は無制限に積分するため、緩やかな遅れも累積として確実に検出できる。
            bool canMeasureDrift = isPlaying && _stablePlaybackStartedAt > 0f && now >= _driftWarmupUntil;
            if (canMeasureDrift)
            {
                if (_baseWallTime <= 0f)
                {
                    // 計測有効化の瞬間: 現在時刻で基準点を確定（rawDrift = 0 を保証）
                    _baseWallTime = now;
                    _basePlayerTime = currentPlayerTime;
                    _driftAccumulator = 0f;
                }
                else
                {
                    // 基準点からの経過時間差 = 壁時間の進み - プレイヤー時間の進み
                    float rawDrift = now - _baseWallTime - (currentPlayerTime - _basePlayerTime);
                    float dt = now - _lastObservedAt;
                    float alpha = Mathf.Clamp01(1f - Mathf.Exp(-dt / driftSmoothingTimeConstant));
                    _driftAccumulator = Mathf.Lerp(_driftAccumulator, rawDrift, alpha);
                }
            }
            else
            {
                // 計測無効: 累積と基準点をクリアし、次の有効化で基準点を取り直す
                _driftAccumulator = 0f;
                _baseWallTime = 0f;
                _basePlayerTime = 0f;
            }

            _lastActiveTime = currentPlayerTime;
            _lastObservedAt = now;

            if (_timelineLogging)
            {
                _tlSnapshotCounter++;
                if (_tlSnapshotCounter >= TL_SNAPSHOT_INTERVAL)
                {
                    _tlSnapshotCounter = 0;
                    TL($"a=SNAPSHOT drift={_driftAccumulator:F4} stallCnt={_consecutiveStallCount} advCnt={_consecutiveAdvanceCount} time={currentPlayerTime:F2} playing={(isPlaying ? 1 : 0)}");
                }
            }
        }

        // =================================================================
        //  Standby Player ポーリング
        // =================================================================

        /// <summary>
        /// Standby プレイヤーの時間前進をカウントし、切替先として信頼できるか検証する。
        /// 十分な連続前進が確認されると IsVerifySatisfied が true を返すようになる。
        /// </summary>
        public void PollStandby(float now)
        {
            if (now - _lastMonitorTime < monitorIntervalSec) return;

            AunCastVideoPlayerManager standby = GetStandbyManager();
            if (standby == null) return;

            float currentTime = standby.GetTime();
            float delta = currentTime - _lastStandbyTime;

            if (delta < 0)
            {
                _standbyAdvanceCount = 0;
                _lastStandbyTime = currentTime;
                return;
            }

            if (delta > minAdvanceThresholdSec && standby.IsPlaying())
            {
                _standbyAdvanceCount++;
                if (_activeIsA) _hasSeenTimeAdvanceB = true;
                else _hasSeenTimeAdvanceA = true;
            }

            _lastStandbyTime = currentTime;
        }

        // =================================================================
        //  障害検出
        // =================================================================

        /// <summary>
        /// Active プレイヤーに障害が発生しているかを判定する。
        /// 初回前進タイムアウト、停滞タイムアウト超過、またはドリフト閾値超過のいずれかで true を返す。
        /// 呼び出し元（AunCastDualPlayerController）はこの結果を受けて Resync フローを起動する。
        /// </summary>
        public bool DetectActiveFailure(float now, bool driftResyncEnabled, float effectiveDriftThresholdSec)
        {
            // 初回前進前は残留時刻や起動直後の 0 秒を停滞扱いにしない。
            // ただし OnVideoStart 後に IsPlaying=false へ戻ると永久にここへ到達しないため、
            // 別枠の上限時間で静かな起動失敗を検出する。
            bool hasSeenAdvance = _activeIsA ? _hasSeenTimeAdvanceA : _hasSeenTimeAdvanceB;
            if (!hasSeenAdvance
                && _activeMonitoringStartedAt >= 0f
                && now - _activeMonitoringStartedAt >= INITIAL_ADVANCE_TIMEOUT_SEC)
            {
                if (!_initialAdvanceTimeoutLogged)
                {
                    _initialAdvanceTimeoutLogged = true;
                    TL($"a=INITIAL_ADVANCE_TIMEOUT elapsed={now - _activeMonitoringStartedAt:F2}");
                }
                return true;
            }

            if (_stallStartedAt > 0f && (now - _stallStartedAt) >= stalledTimeoutSec)
                return true;

            if (driftResyncEnabled
                && _stablePlaybackStartedAt > 0f
                && now >= _driftWarmupUntil
                && Mathf.Abs(_driftAccumulator) > effectiveDriftThresholdSec)
            {
                // Resync 要求が通らない間は毎ポーリング true が続くため、初回のみ記録する
                if (!_driftThresholdLogged)
                {
                    _driftThresholdLogged = true;
                    TL($"a=DRIFT_THRESHOLD drift={_driftAccumulator:F4}");
                }
                return true;
            }

            return false;
        }

        // =================================================================
        //  Standby 検証判定
        // =================================================================

        /// <summary>
        /// Standby プレイヤーが切替可能な状態かを判定する。
        /// 連続前進回数が閾値を満たし、かつ最低検証時間を経過していれば true。
        /// 両条件を課すことで、たまたま 1-2 フレーム進んだだけの不安定な状態を除外する。
        /// </summary>
        public bool IsVerifySatisfied(float now)
        {
            return _standbyAdvanceCount >= minConsecutiveAdvances
                && (now - _verifyStartedAt) >= verifyMinDurationSec;
        }


        // =================================================================
        //  Public Getters
        // =================================================================

        /// <summary>EMA 平滑化済みドリフト値（秒）を返す。</summary>
        public float GetDriftAccumulator() { return _driftAccumulator; }
        /// <summary>Active プレイヤーの現在再生時刻を返す。</summary>
        public float GetActivePlayerTime()
        {
            AunCastVideoPlayerManager active = GetActiveManager();
            return active != null ? active.GetTime() : _lastActiveTime;
        }
        /// <summary>現在の停滞継続時間（秒）を返す。停滞していなければ 0。</summary>
        public float GetActiveStallDuration()
        {
            if (_stallStartedAt <= 0f) return 0f;
            return Time.time - _stallStartedAt;
        }
        /// <summary>
        /// 現在の Active プレイヤーが最初のフレームデコードを完了しているかを返す。
        /// false の間は停滞検出が抑制されている。
        /// </summary>
        public bool HasSeenPlayerTimeAdvance()
        {
            return _activeIsA ? _hasSeenTimeAdvanceA : _hasSeenTimeAdvanceB;
        }

        /// <summary>
        /// A/B いずれかのプレイヤーが再生中かつ時間前進を確認済みかを返す。
        /// AunCastPlaybackMonitor がシステム全体の再生状態を外部に報告する際に使用する。
        /// </summary>
        public bool IsAnyPlayerPlaying()
        {
            bool a = playerManagerA != null && playerManagerA.IsPlaying() && _hasSeenTimeAdvanceA;
            bool b = playerManagerB != null && playerManagerB.IsPlaying() && _hasSeenTimeAdvanceB;
            return a || b;
        }

        /// <summary>
        /// 指定プレイヤーの「最初の時間前進を確認済み」フラグをクリアする。
        /// プレイヤーをリロードする前に呼び出し、再デコード待ち状態に戻す。
        /// </summary>
        public void ResetTimeAdvanceForPlayer(bool isA)
        {
            if (isA) _hasSeenTimeAdvanceA = false;
            else _hasSeenTimeAdvanceB = false;
        }
        /// <summary>連続前進カウントを返す（デバッグ/HUD 用）。</summary>
        public int GetConsecutiveAdvanceCount() { return _consecutiveAdvanceCount; }
        /// <summary>連続停滞カウントを返す（デバッグ/HUD 用）。</summary>
        public int GetConsecutiveStallCount() { return _consecutiveStallCount; }
        /// <summary>検証に必要な最低連続前進回数を返す（HUD 表示用）。</summary>
        public int GetMinConsecutiveAdvances() { return minConsecutiveAdvances; }

        private void BeginStablePlayback(float now)
        {
            _stablePlaybackStartedAt = now;
            // driftWarmupSec が Inspector で 0 に設定された場合のフォールバック
            _driftWarmupUntil = now + (driftWarmupSec > 0f ? driftWarmupSec : 5.0f);
            _driftAccumulator = 0f;
            _baseWallTime = 0f;
            _basePlayerTime = 0f;
            TL($"a=STABLE_PLAYBACK warmupUntil={_driftWarmupUntil:F3}");
        }

        // =================================================================
        //  内部ヘルパー
        // =================================================================

        /// <summary>現在 Active ロールのプレイヤーマネージャーを返す。</summary>
        private AunCastVideoPlayerManager GetActiveManager()
        {
            return _activeIsA ? playerManagerA : playerManagerB;
        }

        /// <summary>現在 Standby ロールのプレイヤーマネージャーを返す。</summary>
        private AunCastVideoPlayerManager GetStandbyManager()
        {
            return _activeIsA ? playerManagerB : playerManagerA;
        }

        /// <summary>タイムラインログをローカルのみ設定する。</summary>
        public void SetTimelineLoggingLocal(bool value)
        {
            _timelineLogging = value;
        }

        private int _tlSnapshotCounter;
        private const int TL_SNAPSHOT_INTERVAL = 50;

        private void TL(string eventAndData)
        {
            Debug.Log($"[AunCast:TL] st={VRC.SDKBase.Networking.GetServerTimeInMilliseconds()} c=APM {eventAndData}");
        }
    }
}
