using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>各ユーザーのローカル再生制御を行う中核 FSM。A/B 二重化再生の異常検知→切替を統括する。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LocalDualPlayerController : UdonSharpBehaviour
    {
        // =================================================================
        //  ローカル状態コード (Design Section 10.1)
        //  FSM 遷移: IDLE → ACTIVE_PLAYING → (異常検知) → REQUEST_PENDING →
        //  RESERVED → STANDBY_CONNECTING → STANDBY_VERIFYING → SWITCHING → COOLDOWN
        //  両系統失敗時: → RETRY_WAIT（指数バックオフで Active 直接リブートを試行）
        // =================================================================
        public const int STATE_IDLE = 0;
        public const int STATE_ACTIVE_PLAYING = 1;
        public const int STATE_REQUEST_PENDING = 2;
        public const int STATE_RESERVED = 3;
        public const int STATE_STANDBY_CONNECTING = 4;
        public const int STATE_STANDBY_VERIFYING = 5;
        public const int STATE_SWITCHING = 6;
        public const int STATE_COOLDOWN = 7;
        public const int STATE_RETRY_WAIT = 8;

        // =================================================================
        //  Inspector 参照
        // =================================================================
        [Header("Player Managers")]
        [SerializeField] private VideoPlayerManager playerManagerA;
        [SerializeField] private VideoPlayerManager playerManagerB;

        [Header("Playback Monitor")]
        [SerializeField] private PlaybackMonitor playbackMonitor;

        [Header("Sub-components")]
        [SerializeField] private PlaybackSwitcher switcher;
        [SerializeField] private ActivePlayerMonitor activeMonitor;
        [SerializeField] private ResyncCoordinatorClient resyncClient;

        // =================================================================
        //  Inspector パラメータ (Design Section 20)
        // =================================================================
        [Header("Standby Verification")]
        [Tooltip("Ready 待ちタイムアウト（秒）")]
        [SerializeField] private float readyTimeoutSec = 5.0f;

        [Tooltip("Play 待ちタイムアウト（秒）")]
        [SerializeField] private float playTimeoutSec = 3.0f;

        [Tooltip("Verify 待ちタイムアウト（秒）")]
        [SerializeField] private float verifyTimeoutSec = 2.0f;


        [Range(0f, 1f)]
        [Tooltip("デフォルト音量（x^2 と Dr. Lex 指数カーブの lerp。0.6 で約 -13dB）")]
        [SerializeField] private float defaultVolume = 0.6f;

        [Header("Reboot")]
        [Tooltip("リブートボタン表示条件: GetTime 停止超過時間（秒）")]
        [SerializeField] private float rebootStallSec = 10.0f;

        [Header("Debug")]
        [Tooltip("要所ログを詳細出力する")]
        [SerializeField] private bool verboseLogging = true;

        [Header("Timeline")]
        [Tooltip("タイムラインログを出力する")]
        [SerializeField] private bool _timelineLogging;

        // =================================================================
        //  同期変数 (Design Section 14)
        // =================================================================
        [UdonSynced] private VRCUrl _syncedURL = VRCUrl.Empty;
        [UdonSynced] private int _syncedVideoIdx;
        [UdonSynced] private bool _ownerPlaying;

        // =================================================================
        //  ローカル状態変数 (Design Section 13.4)
        // =================================================================

        // FSM 現在状態と A/B どちらが Active かの基本ロール
        private int _localState;
        private bool _activeIsA = true;
        private bool _autoSilenceResyncEnabled = true;
        private float _localVolume = 0.6f;
        private float _combinedSilenceDuration;

        // Standby Player 検証（Ready/Play 完了を待つためのフラグ群）
        private float _standbyConnectStartedAt;
        private bool _standbyReady;
        private bool _standbyPlayStarted;

        // リトライ（指数バックオフと Active 直接リブート）
        private float _retryWaitUntil;
        private bool _awaitingActiveReboot;
        private float _activeRebootStartedAt;

        // 同期（非オーナーが URL 変更を検知するためのインデックス比較）
        private int _currentVideoIdx;
        private bool _waitForSync;
        private bool _pendingConnectingReport;

        // PlaybackActive レポートのスロットル（PlaybackMonitor への過剰通知を防止）
        private float _lastPlaybackReportAt;
        private const float PLAYBACK_REPORT_MIN_INTERVAL = 10.0f;

        // VideoPlayerManager コールバック用（コールバック元を識別するための一時変数）
        [System.NonSerialized] public int _lastCallbackPlayerIndex;
        [System.NonSerialized] public VideoError _lastVideoError;

        // 直近のエラーメッセージ（UI 表示用）。空文字 = エラーなし。
        private string _lastErrorMessage = "";
        private float _lastErrorMessageAt;

        private bool _ranInit;
        private bool _hasReportedPlaybackActive;
        private bool _lastReportedPlaybackActive;

        // =================================================================
        //  Unity ライフサイクル
        // =================================================================

        /// <summary>初期音量設定と A/B ロールの確定。オーナーなら初期状態を同期配信する。</summary>
        private void Start()
        {
            if (_ranInit) return;
            _ranInit = true;

            _localVolume = defaultVolume;
            SetVolume(_localVolume);

            // Active は A、Standby は B。B のオーディオはミュート開始
            _activeIsA = true;
            if (switcher != null)
            {
                switcher.InitializeToA();
                switcher.EnsureAudioLinkBehaviourAssignedFromScene();
                switcher.SwitchAudioLinkSource();
            }

            QueueSerialize();
            LogMessage("LocalDualPlayerController initialized");
        }

        /// <summary>メインループ: グローバルリブート確認→スロット確保→Coordinator ポーリング→FSM→無音検知の順で毎フレーム駆動。</summary>
        private void Update()
        {
            float now = Time.time;

            // グローバル強制リブート指令の確認
            if (resyncClient.PollGlobalForceReboot())
            {
                Reboot();
                return;
            }
            if (!resyncClient.TryEnsureSlotAssigned()) return;

            // スロット確保直後の connecting レポート遅延送信
            if (_pendingConnectingReport)
            {
                _pendingConnectingReport = false;
                if (_localState == STATE_IDLE)
                    ReportConnecting(true);
            }

            if (_timelineLogging && !_tlClientIdentified)
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (local != null)
                {
                    TL($"e=CLIENT_INIT name={local.displayName} playerId={local.playerId} slot={resyncClient.GetMySlotIndex()}");
                    _tlClientIdentified = true;
                }
            }

            // Coordinator から Resync スロットの予約通知を受け取り FSM 状態を更新
            int pollResult = resyncClient.PollResyncCoordinator(now, _localState);
            if (pollResult >= 0)
            {
                if (pollResult == STATE_RESERVED)
                    resyncClient.MarkCycleStarted(now);
                _localState = pollResult;
            }

            PollSyncWait(now);

            if (PollActiveReboot(now)) return;

            PollActiveMonitoring(now);
            TickStateMachine(now);

            if (switcher != null) switcher.UpdateRenderTexture(_localState, _ownerPlaying);
            PollSilenceDetection(now);
            ReportPlaybackStateToCoordinator();
        }

        /// <summary>非オーナーがオーナーの _ownerPlaying=true 同期を待ち、到達したら再生を再開する。</summary>
        private void PollSyncWait(float now)
        {
            if (!_waitForSync || !_ownerPlaying) return;

            LogVerbose("Owner sync arrived; resuming active playback");
            switcher.GetActiveManager().Play();
            _waitForSync = false;
            _localState = STATE_ACTIVE_PLAYING;
            activeMonitor.BindRoles(_activeIsA);
            activeMonitor.InitializeForActive(now);
        }

        /// <summary>Active 直接リブートの完了を待機し、タイムアウトしたら失敗処理へ遷移する。</summary>
        private bool PollActiveReboot(float now)
        {
            if (!_awaitingActiveReboot) return false;

            if (now - _activeRebootStartedAt > readyTimeoutSec + playTimeoutSec)
            {
                _awaitingActiveReboot = false;
                HandleFailed(now);
            }
            ReportPlaybackStateToCoordinator();
            return true;
        }

        /// <summary>ActivePlayerMonitor に Active/Standby の時刻進行ポーリングを委譲する。</summary>
        private void PollActiveMonitoring(float now)
        {
            if (_localState == STATE_ACTIVE_PLAYING || _localState == STATE_REQUEST_PENDING)
                activeMonitor.PollActive(now);

            if (_localState == STATE_STANDBY_VERIFYING)
                activeMonitor.PollStandby(now);
        }

        /// <summary>FSM 本体。各状態の遷移条件を評価し、次状態への移行やアクションを実行する。</summary>
        private void TickStateMachine(float now)
        {
            switch (_localState)
            {
                case STATE_IDLE:
                    break;

                case STATE_ACTIVE_PLAYING:
                    if (activeMonitor.DetectActiveFailure(now) && resyncClient.TryRequestResync(now, ResyncCoordinatorClient.REQUEST_REASON_FAILURE))
                    {
                        LogWarning($"Active failure -> RequestPending (stall={activeMonitor.GetActiveStallDuration():F2}s, drift={activeMonitor.GetDriftAccumulator():F3}s)");
                        _tlAction = "ACTIVE_FAILURE";
                        _localState = STATE_REQUEST_PENDING;
                    }
                    break;

                case STATE_REQUEST_PENDING:
                    if (resyncClient.GetRequestReason() == ResyncCoordinatorClient.REQUEST_REASON_FAILURE
                             && activeMonitor.GetConsecutiveStallCount() == 0
                             && activeMonitor.GetConsecutiveAdvanceCount() >= activeMonitor.GetMinConsecutiveAdvances())
                    {
                        CancelResync();
                        LogMessage("Resync canceled: active recovered");
                        _localState = STATE_ACTIVE_PLAYING;
                        activeMonitor.BindRoles(_activeIsA);
                        activeMonitor.InitializeForActive(now);
                    }
                    else if (resyncClient.ShouldRetryResyncRequest(now))
                    {
                        resyncClient.MarkRetrySent(now);
                    }
                    break;

                case STATE_RESERVED:
                    StartStandbyConnectInternal(now);
                    break;

                case STATE_STANDBY_CONNECTING:
                    if (resyncClient.IsCycleTimedOut(now))
                    {
                        LogWarning("Resync cycle timed out (STANDBY_CONNECTING)");
                        CancelResync();
                        HandleFailed(now);
                    }
                    else if ((now - _standbyConnectStartedAt) > readyTimeoutSec)
                    {
                        HandleStandbyFailure(now);
                    }
                    else if (_standbyReady && _standbyPlayStarted)
                    {
                        activeMonitor.InitializeForStandby(now);
                        _localState = STATE_STANDBY_VERIFYING;
                    }
                    break;

                case STATE_STANDBY_VERIFYING:
                    if (resyncClient.IsCycleTimedOut(now))
                    {
                        LogWarning("Resync cycle timed out (STANDBY_VERIFYING)");
                        CancelResync();
                        HandleFailed(now);
                    }
                    else if ((now - _standbyConnectStartedAt) > readyTimeoutSec + playTimeoutSec + verifyTimeoutSec)
                    {
                        HandleStandbyFailure(now);
                    }
                    else if (activeMonitor.IsVerifySatisfied(now))
                    {
                        StartSwitch(now);
                    }
                    break;

                case STATE_SWITCHING:
                    if (resyncClient.IsCycleTimedOut(now))
                    {
                        LogWarning("Resync cycle timed out (SWITCHING)");
                        CancelResync();
                        HandleFailed(now);
                    }
                    else
                    {
                        switcher.TickCrossfade(now, switcher.GetCrossfadeDurationSec());
                        if (switcher.IsCrossfadeComplete(now, switcher.GetCrossfadeDurationSec()))
                        {
                            CompleteSwitch(now);
                        }
                    }
                    break;

                case STATE_COOLDOWN:
                    if (now >= resyncClient.GetLocalCooldownUntil())
                    {
                        LogVerbose("Cooldown complete -> ActivePlaying");
                        _localState = STATE_ACTIVE_PLAYING;
                        resyncClient.SetConsecutiveFailCount(0);
                        activeMonitor.BindRoles(switcher.GetActiveIsA());
                        activeMonitor.InitializeForActive(now);
                    }
                    break;

                case STATE_RETRY_WAIT:
                    if (now >= _retryWaitUntil && !_awaitingActiveReboot)
                    {
                        AttemptActiveReboot(now);
                    }
                    break;

            }
        }

        /// <summary>音声出力のある全プレイヤーが無音状態か判定し、一定時間継続したら自動 Resync を発行する。</summary>
        private void PollSilenceDetection(float now)
        {
            if (_localState != STATE_ACTIVE_PLAYING || !_autoSilenceResyncEnabled) return;
            if (!activeMonitor.HasSeenPlayerTimeAdvance()) { _combinedSilenceDuration = 0f; return; }
            if (!resyncClient.IsSilenceAutoResyncEligible(now)) { _combinedSilenceDuration = 0f; return; }

            AudioSilenceDetector activeDet = switcher.GetActiveSilenceDetector();
            AudioSilenceDetector standbyDet = switcher.GetStandbySilenceDetector();
            float threshold = activeDet != null ? activeDet.GetSilenceRmsThreshold() : 0.001f;
            float requiredSec = activeDet != null ? activeDet.GetSilenceConsecutiveSec() : 2f;

            VideoPlayerManager activeMgr = switcher.GetActiveManager();
            VideoPlayerManager standbyMgr = switcher.GetStandbyManager();
            bool anyAudible = false;
            anyAudible |= CheckPlayerAudible(activeMgr, activeDet, threshold);
            anyAudible |= CheckPlayerAudible(standbyMgr, standbyDet, threshold);

            if (anyAudible)
                _combinedSilenceDuration = 0f;
            else
                _combinedSilenceDuration += Time.deltaTime;

            if (_combinedSilenceDuration >= requiredSec
                && resyncClient.TryRequestResync(now, ResyncCoordinatorClient.REQUEST_REASON_SILENCE))
            {
                LogWarning("Silence detected on all audible players; requesting individual resync");
                _tlAction = "SILENCE_RESYNC";
                _combinedSilenceDuration = 0f;
                _localState = STATE_REQUEST_PENDING;
            }
        }

        /// <summary>指定プレイヤーの音量が閾値以上かを判定する（フェード中のミュート側を除外するため FadeGain も確認）。</summary>
        private bool CheckPlayerAudible(VideoPlayerManager mgr, AudioSilenceDetector det, float threshold)
        {
            if (mgr == null || det == null) return false;
            if (mgr.GetFadeGain() <= 0f) return false;
            return det.GetRms() >= threshold;
        }

        /// <summary>PlaybackMonitor に再生状態を定期報告する（全体ステータス UI 表示用）。</summary>
        private void ReportPlaybackStateToCoordinator()
        {
            if (playbackMonitor == null || resyncClient.GetMySlotIndex() < 0) return;

            bool isPlaying = activeMonitor != null && activeMonitor.IsAnyPlayerPlaying();
            bool valueChanged = !_hasReportedPlaybackActive || _lastReportedPlaybackActive != isPlaying;

            float now = Time.time;
            if (!valueChanged && now - _lastPlaybackReportAt < PLAYBACK_REPORT_MIN_INTERVAL) return;

            int slotIndex = resyncClient.GetMySlotIndex();
            playbackMonitor.ReportForSlot(slotIndex, isPlaying);

            // playing=true になったら connecting を解除
            // （OnVideoStart では同フレーム完了で同期前に消えるため、ここで遅延解除する）
            if (isPlaying && !_lastReportedPlaybackActive)
                ReportConnecting(false);

            _lastReportedPlaybackActive = isPlaying;
            _hasReportedPlaybackActive = true;
            _lastPlaybackReportAt = now;
            LogVerbose($"ReportPlaybackActive slot={slotIndex} active={isPlaying}");
        }

        private void ReportConnecting(bool isConnecting)
        {
            if (playbackMonitor == null || resyncClient.GetMySlotIndex() < 0) return;
            playbackMonitor.ReportConnectingForSlot(resyncClient.GetMySlotIndex(), isConnecting);
        }

        private void ReportError(bool isError)
        {
            if (playbackMonitor == null || resyncClient.GetMySlotIndex() < 0) return;
            playbackMonitor.ReportErrorForSlot(resyncClient.GetMySlotIndex(), isError);
        }

        /// <summary>進行中の Resync を中断し、Coordinator スロット解放・Standby 停止・UI 通知を行う。</summary>
        private void CancelResync()
        {
            resyncClient.CancelResync();
            _standbyReady = false;
            _standbyPlayStarted = false;
            if (_activeIsA) _tlLoadingB = false; else _tlLoadingA = false;
            switcher.StopStandbyOnFailure();
            ReportConnecting(false);
        }

        // =================================================================
        //  Standby 接続開始
        // =================================================================

        /// <summary>Standby プレイヤーの接続を開始する。Coordinator に RUNNING を通知し、Ready/Play 完了待ちに入る。</summary>
        private void StartStandbyConnectInternal(float now)
        {
            _standbyReady = false;
            _standbyPlayStarted = false;
            _standbyConnectStartedAt = now;

            activeMonitor.ResetTimeAdvanceForPlayer(!_activeIsA);
            if (_activeIsA) _tlLoadingB = true; else _tlLoadingA = true;
            switcher.StartStandbyConnect(now, _syncedURL);
            resyncClient.ReportRunning();
            ReportConnecting(true);

            _localState = STATE_STANDBY_CONNECTING;
            LogMessage($"Standby connect started (slot={resyncClient.GetMySlotIndex()}, url={_syncedURL.Get()})");
        }

        // =================================================================
        //  切替 (Design Section 16)
        // =================================================================

        /// <summary>クロスフェード開始。Standby 検証合格後に呼ばれ、音声を滑らかに移行する。</summary>
        private void StartSwitch(float now)
        {
            switcher.StartCrossfade(now);
            _localState = STATE_SWITCHING;
        }

        /// <summary>クロスフェード完了後のロール交換。旧 Active 停止→新 Active 確定→クールダウンに移行する。</summary>
        private void CompleteSwitch(float now)
        {
            // ロール交換: 旧 Active 停止 → _activeIsA トグル → 新 Active フル音量 → AudioLink 切替
            switcher.CompleteSwitchRoles();
            _activeIsA = switcher.GetActiveIsA();

            // 監視リセット（F-5: BindRoles → InitializeForActive の順が必須）
            activeMonitor.BindRoles(_activeIsA);
            activeMonitor.InitializeForActive(now);

            // クールダウン
            resyncClient.SetLocalCooldownUntil(now + resyncClient.GetLocalCooldownSec());
            resyncClient.SetConsecutiveFailCount(0);
            resyncClient.SetResyncRequested(false);
            resyncClient.SetRequestReason(ResyncCoordinatorClient.REQUEST_REASON_FAILURE);
            resyncClient.OnResyncCompleted(now);

            resyncClient.ReportResult(true);
            ReportError(false);
            ReportConnecting(false);

            _localState = STATE_COOLDOWN;
            LogMessage("Resync switch completed");
        }

        // =================================================================
        //  失敗処理 (Design Section 18)
        // =================================================================

        /// <summary>Standby 接続/検証失敗時の後処理。Standby を停止し、共通の失敗ハンドラに委譲する。</summary>
        private void HandleStandbyFailure(float now)
        {
            if (_activeIsA) _tlLoadingB = false; else _tlLoadingA = false;
            switcher.StopStandbyOnFailure();
            ReportConnecting(false);

            resyncClient.SetResyncRequested(false);
            resyncClient.SetRequestReason(ResyncCoordinatorClient.REQUEST_REASON_FAILURE);
            LogMessage("Standby connection failed");
            HandleFailed(now);
        }

        /// <summary>Resync 失敗の統合ハンドラ。Active 生存なら COOLDOWN、両系統失敗なら指数バックオフで RETRY_WAIT へ。</summary>
        private void HandleFailed(float now)
        {
            // 旧 Active がまだ生存しているか確認
            if (IsActiveAlive())
            {
                resyncClient.SetLocalCooldownUntil(now + resyncClient.GetLocalCooldownSec());
                _localState = STATE_COOLDOWN;
                resyncClient.ReportResult(false);
                LogMessage("Standby failed, cooldown before retry");
                return;
            }

            // 両系統失敗 → exponential backoff
            int failCount = resyncClient.GetConsecutiveFailCount() + 1;
            resyncClient.SetConsecutiveFailCount(failCount);
            float backoff = Mathf.Min(
                resyncClient.GetBaseCooldownSec() * Mathf.Pow(2, failCount - 1),
                resyncClient.GetMaxRetryCooldownSec());
            _retryWaitUntil = now + backoff;

            resyncClient.ReportResult(false);
            ReportError(true);

            _localState = STATE_RETRY_WAIT;
            LogMessage($"Both systems failed, retry in {backoff:F1}s (attempt {failCount})");
        }

        /// <summary>Active プレイヤーがまだフレームを生成しているか確認する（再生中かつ時刻が進んでいるか）。</summary>
        private bool IsActiveAlive()
        {
            VideoPlayerManager active = switcher.GetActiveManager();
            if (active == null) return false;
            return active.IsPlaying() && active.GetTime() > 0f;
        }

        /// <summary>最終手段: 両プレイヤーを停止し A に強制ロードする。Standby 切替が不可能な場合の復旧策。</summary>
        private void AttemptActiveReboot(float now)
        {
            _awaitingActiveReboot = true;
            _activeRebootStartedAt = now;
            _activeIsA = true;

            activeMonitor.ResetTimeAdvanceForPlayer(true);
            ReportError(false);
            ReportConnecting(true);
            _tlLoadingA = true;
            switcher.StartActiveDirectReboot(_syncedURL);
            LogMessage("Attempting Active direct reboot");
        }

        // =================================================================
        //  緊急リブート (Design FR-16b)
        // =================================================================

        /// <summary>ユーザー操作またはグローバル指令による緊急リブート。全状態をリセットし A で再ロードする。</summary>
        [PublicAPI]
        public void Reboot()
        {
            float now = Time.time;

            // 実行中の Resync をキャンセル（どの状態でも Coordinator 側のスロットを解放する）
            CancelResync();

            _awaitingActiveReboot = true;
            _activeRebootStartedAt = now;
            _activeIsA = true;

            activeMonitor.ResetTimeAdvanceForPlayer(true);
            ReportError(false);
            ReportConnecting(true);
            _tlLoadingA = true;
            switcher.StartActiveDirectReboot(_syncedURL);

            _tlAction = "EMERGENCY_REBOOT";
            _localState = STATE_RETRY_WAIT;
            LogMessage("Reboot initiated");
        }

        /// <summary>リブートボタンを表示すべきか判定する。Resync 予約が長時間取れない場合や長期スタル時に true。</summary>
        [PublicAPI]
        public bool ShouldShowRebootButton()
        {
            float now = Time.time;

            // Resync スロットの割当待ちが推定時間を超過したか
            bool grantTimeout = false;
            if (_localState == STATE_REQUEST_PENDING)
            {
                float waited = now - resyncClient.GetRequestStartedAt();
                float estimated = resyncClient.GetRebootWaitEstimate();
                grantTimeout = waited > estimated + resyncClient.GetRebootGrantMarginSec();
            }

            // Active の GetTime が長時間停止しているか
            float stallStartedAt = activeMonitor.GetStallStartedAt();
            bool stallTimeout = stallStartedAt > 0f
                && (now - stallStartedAt) > rebootStallSec;

            return grantTimeout || stallTimeout;
        }

        /// <summary>ユーザー操作による手動 Resync 要求。再生中のみ受け付け、Coordinator にスロットを申請する。</summary>
        [PublicAPI]
        public bool RequestManualResync()
        {
            float now = Time.time;
            if (_localState != STATE_ACTIVE_PLAYING) return false;
            if (!resyncClient.TryRequestResync(now, ResyncCoordinatorClient.REQUEST_REASON_MANUAL)) return false;

            _tlAction = "MANUAL_RESYNC";
            _localState = STATE_REQUEST_PENDING;
            LogMessage("Manual resync requested");
            return true;
        }

        // =================================================================
        //  VideoPlayerManager コールバック
        // =================================================================

        /// <summary>VideoPlayerManager からの Ready コールバック。Active なら即 Play、Standby なら検証フローへ進む。</summary>
        public void OnManagerVideoReady()
        {
            bool isActiveEvent = IsActiveEvent();
            LogVerbose($"OnVideoReady received (activeEvent={isActiveEvent}, callbackIndex={_lastCallbackPlayerIndex})");
            _tlAction = "VIDEO_READY";

            if (isActiveEvent)
            {
                switcher.GetActiveManager().Play();

                if (_awaitingActiveReboot)
                {
                    // Active 直接リブートの Ready
                    return;
                }
            }
            else
            {
                // Standby の Ready
                if (_localState == STATE_STANDBY_CONNECTING)
                {
                    _standbyReady = true;
                    LogVerbose("Standby ready -> Play standby");
                    switcher.GetStandbyManager().Play();
                }
            }
        }

        /// <summary>VideoPlayerManager からの Start コールバック。オーナーは _ownerPlaying を配信、非オーナーは同期待ちを判断する。</summary>
        public void OnManagerVideoStart()
        {
            bool isActiveEvent = IsActiveEvent();
            LogVerbose($"OnVideoStart received (activeEvent={isActiveEvent}, callbackIndex={_lastCallbackPlayerIndex})");
            _tlAction = "VIDEO_START";

            if (isActiveEvent)
            {
                ClearErrorMessage();
                ReportError(false);

                // Active 直接リブートの成功
                if (_awaitingActiveReboot)
                {
                    _awaitingActiveReboot = false;
                    resyncClient.SetConsecutiveFailCount(0);
                    resyncClient.SetResyncRequested(false);
                    resyncClient.SetLocalCooldownUntil(Time.time + resyncClient.GetLocalCooldownSec());
                    _localState = STATE_COOLDOWN;

                    if (Networking.IsOwner(gameObject))
                    {
                        _ownerPlaying = true;
                        QueueSerialize();
                    }

                    LogMessage("Active reboot succeeded");
                    return;
                }

                // 通常の Active 開始
                if (Networking.IsOwner(gameObject))
                {
                    _ownerPlaying = true;
                    QueueSerialize();

                    if (_localState == STATE_IDLE)
                    {
                        _localState = STATE_ACTIVE_PLAYING;
                        activeMonitor.BindRoles(_activeIsA);
                        activeMonitor.InitializeForActive(Time.time);
                    }
                }
                else if (!_ownerPlaying)
                {
                    LogVerbose("OnVideoStart while owner not playing; pausing and waiting sync");
                    switcher.GetActiveManager().Pause();
                    _waitForSync = true;
                }
                else
                {
                    if (_localState == STATE_IDLE)
                    {
                        _localState = STATE_ACTIVE_PLAYING;
                        activeMonitor.BindRoles(_activeIsA);
                        activeMonitor.InitializeForActive(Time.time);
                    }
                }

                switcher.SwitchAudioLinkSource();
                switcher.UpdateRenderTexture(_localState, _ownerPlaying);
            }
            else
            {
                // Standby の Play 開始
                if (_localState == STATE_STANDBY_CONNECTING)
                {
                    _standbyPlayStarted = true;
                    LogVerbose("Standby play started");
                }
            }
        }

        public void OnManagerVideoEnd() { }

        /// <summary>VideoPlayerManager からの Error コールバック。Active ならリブート失敗判定/Resync 要求、Standby なら切替断念。</summary>
        public void OnManagerVideoError()
        {
            bool isActiveEvent = IsActiveEvent();
            VideoError error = _lastVideoError;
            LogWarning($"OnVideoError received (activeEvent={isActiveEvent}, error={error})");
            _tlAction = "VIDEO_ERROR";

            if (isActiveEvent)
            {
                SetErrorMessage(MapVideoErrorToMessage(error));
                if (_awaitingActiveReboot)
                {
                    // Active 直接リブートの失敗
                    _awaitingActiveReboot = false;
                    if (error == VideoError.RateLimited)
                    {
                        // レート制限 → 少し待ってリトライ（connecting は維持）
                        _retryWaitUntil = Time.time + 6.0f;
                        LogMessage("Active reboot rate limited, waiting 6s");
                    }
                    else
                    {
                        ReportConnecting(false);
                        HandleFailed(Time.time);
                    }
                    return;
                }

                ReportConnecting(false);
                ReportError(true);

                // Active 再生中のエラー → Resync 要求
                if (_localState == STATE_ACTIVE_PLAYING
                    && resyncClient.TryRequestResync(Time.time, ResyncCoordinatorClient.REQUEST_REASON_FAILURE))
                {
                    _localState = STATE_REQUEST_PENDING;
                }
            }
            else
            {
                // Standby のエラー
                if (_localState == STATE_STANDBY_CONNECTING || _localState == STATE_STANDBY_VERIFYING)
                {
                    HandleStandbyFailure(Time.time);
                }
            }
        }

        public void OnManagerVideoLoop() { }

        // =================================================================
        //  URL 管理 (Design Section 14)
        // =================================================================

        /// <summary>スタッフによる配信 URL 設定。オーナーシップ取得→既存停止→新 URL で再生開始→全員に同期する。</summary>
        [PublicAPI]
        public void PlayVideoAsStaff(VRCUrl url)
        {
            string urlStr = url.Get();
            if (string.IsNullOrEmpty(urlStr)) return;

            // プロトコルチェック
            int idx = urlStr.IndexOf("://", System.StringComparison.Ordinal);
            if (idx < 1 || idx > 8) return;
            if (urlStr.Length > 4096) return;

            bool wasOwner = Networking.IsOwner(gameObject);
            if (!wasOwner)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            StopVideoInternal();

            _syncedURL = url;

            if (wasOwner)
                ++_syncedVideoIdx;
            else
                _syncedVideoIdx += 2;

            _currentVideoIdx = _syncedVideoIdx;
            _ownerPlaying = false;

            StartActivePlayback(url);
            _tlAction = "PLAY_VIDEO";
            LogMessage($"PlayVideo requested by local user (url={urlStr})");
            QueueSerialize();
        }

        /// <summary>スタッフによる配信停止。オーナーシップ取得後に全プレイヤーを停止し同期する。</summary>
        [PublicAPI]
        public void StopVideoAsStaff()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            StopVideoInternal();
        }

        /// <summary>内部停止処理。両プレイヤー停止・FSM リセット・Resync キャンセルを一括で行う。</summary>
        private void StopVideoInternal()
        {
            _tlAction = "STOP_VIDEO";
            LogMessage("StopVideo requested");
            _ownerPlaying = false;
            _syncedURL = VRCUrl.Empty;

            _tlLoadingA = false;
            _tlLoadingB = false;
            activeMonitor.ResetTimeAdvanceForPlayer(true);
            activeMonitor.ResetTimeAdvanceForPlayer(false);
            switcher.ResetBothPlayersToA();
            _activeIsA = true;

            // Resync 中ならキャンセル
            if (_localState >= STATE_REQUEST_PENDING && _localState <= STATE_SWITCHING)
                CancelResync();

            _localState = STATE_IDLE;
            _waitForSync = false;
            _awaitingActiveReboot = false;
            _pendingConnectingReport = false;
            ReportConnecting(false);
            ReportError(false);

            QueueSerialize();
        }

        /// <summary>現在の URL で Active を再ロードする（Resync ではなく単純なリロード）。</summary>
        [PublicAPI]
        public void Reload()
        {
            _tlAction = "RELOAD";
            if ((_ownerPlaying || Networking.IsOwner(gameObject))
                && _localState != STATE_IDLE)
            {
                // 実行中の Resync をキャンセル
                if (_localState >= STATE_REQUEST_PENDING && _localState <= STATE_SWITCHING)
                    CancelResync();

                switcher.GetActiveManager().Stop();
                StartActivePlayback(_syncedURL);
            }
        }

        /// <summary>Active プレイヤーに URL をロードし、接続中状態に入る共通ヘルパー。</summary>
        private void StartActivePlayback(VRCUrl url)
        {
            _localState = STATE_IDLE;
            activeMonitor.ResetTimeAdvanceForPlayer(_activeIsA);
            ReportError(false);
            ReportConnecting(true);
            if (resyncClient.GetMySlotIndex() < 0)
                _pendingConnectingReport = true;
            if (_activeIsA) _tlLoadingA = true; else _tlLoadingB = true;
            switcher.GetActiveManager().LoadURL(url);
            LogMessage($"Started playback: {url}");
        }

        /// <summary>非オーナーが同期変数の変更を受信する。URL 変更時は再ロード、停止時は全クリーンアップを行う。</summary>
        public override void OnDeserialization()
        {
            if (Networking.IsOwner(gameObject)) return;

            // 再生停止（非オーナーがオーナーの Stop を受信）
            if (!_ownerPlaying && _localState != STATE_IDLE)
            {
                _tlLoadingA = false;
                _tlLoadingB = false;
                playerManagerA.Stop();
                playerManagerB.Stop();

                if (_localState >= STATE_REQUEST_PENDING && _localState <= STATE_SWITCHING)
                    CancelResync();

                _localState = STATE_IDLE;
                _waitForSync = false;
                _awaitingActiveReboot = false;
                _pendingConnectingReport = false;
                ReportConnecting(false);
                ReportError(false);
            }

            // URL 変更の検知
            if (_currentVideoIdx != _syncedVideoIdx)
            {
                LogMessage($"OnDeserialization URL update detected: {_currentVideoIdx} -> {_syncedVideoIdx}");
                _currentVideoIdx = _syncedVideoIdx;

                // 実行中の Resync をキャンセル
                if (_localState >= STATE_REQUEST_PENDING && _localState <= STATE_SWITCHING)
                    CancelResync();

                switcher.ResetBothPlayersToA();
                _activeIsA = true;

                StartActivePlayback(_syncedURL);
                LogMessage($"Playing synced URL: {_syncedURL}");
            }
        }

        // 遅れて参加したプレイヤーにデータを送る
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!player.isLocal)
                QueueSerialize();
        }

        public void QueueSerialize()
        {
            if (!Networking.IsOwner(gameObject)) return;
            RequestSerialization();
        }

        // =================================================================
        //  音量
        // =================================================================

        /// <summary>現在の再生音量を返す（初期化前はデフォルト値を返す）。</summary>
        [PublicAPI]
        public float GetVolume()
        {
            if (!_ranInit)
                return defaultVolume;
            return playerManagerA != null ? playerManagerA.GetVolume() : 0f;
        }

        /// <summary>A/B 両プレイヤーの音量を設定する（同期配信はしない。ローカル適用のみ）。</summary>
        [PublicAPI]
        public void SetVolume(float volume)
        {
            volume = Mathf.Clamp01(volume);
            if (playerManagerA != null) playerManagerA.SetVolume(volume);
            if (playerManagerB != null) playerManagerB.SetVolume(volume);
        }

        /// <summary>ローカルユーザーの音量を記憶しつつ即座に両プレイヤーに反映する。</summary>
        [PublicAPI]
        public void SetVolumeLocal(float volume)
        {
            volume = Mathf.Clamp01(volume);
            _localVolume = volume;
            SetVolume(volume);
        }

        // =================================================================
        //  無音自動 Resync トグル
        // =================================================================

        /// <summary>無音検知による自動 Resync の有効/無効を切り替える（UI トグル用）。</summary>
        [PublicAPI]
        public void SetAutoSilenceResyncEnabled(bool enabled)
        {
            _autoSilenceResyncEnabled = enabled;
        }

        [PublicAPI]
        public bool GetAutoSilenceResyncEnabled() { return _autoSilenceResyncEnabled; }

        // =================================================================
        //  ヘルパー
        // =================================================================

        /// <summary>コールバックが Active 側プレイヤーからのものかを判定する。</summary>
        private bool IsActiveEvent()
        {
            return (_lastCallbackPlayerIndex == 0) == _activeIsA;
        }

        // =================================================================
        //  Public Getters（UI パネルから参照）
        // =================================================================

        [PublicAPI] public int GetLocalState() { return _localState; }
        [PublicAPI] public bool GetActiveIsA() { return _activeIsA; }

        /// <summary>クールダウン残り秒数（UI プログレス表示用）。</summary>
        [PublicAPI]
        public float GetCooldownRemaining()
        {
            if (_localState != STATE_COOLDOWN) return 0f;
            float remaining = resyncClient.GetLocalCooldownUntil() - Time.time;
            return remaining > 0f ? remaining : 0f;
        }
        [PublicAPI] public int GetMySlotIndex() { return resyncClient.GetMySlotIndex(); }
        [PublicAPI] public float GetDriftAccumulator() { return activeMonitor.GetDriftAccumulator(); }
        [PublicAPI]
        public float GetActivePlayerTime()
        {
            return activeMonitor.GetActivePlayerTime();
        }
        [PublicAPI] public VRCUrl GetCurrentURL() { return _syncedURL; }
        [PublicAPI] public bool GetOwnerPlaying() { return _ownerPlaying; }
        [PublicAPI] public int GetConsecutiveFailCount() { return resyncClient.GetConsecutiveFailCount(); }
        [PublicAPI] public int GetConsecutiveStallCount() { return activeMonitor.GetConsecutiveStallCount(); }
        [PublicAPI] public bool GetStandbyReady() { return _standbyReady; }
        [PublicAPI] public bool GetStandbyPlayStarted() { return _standbyPlayStarted; }

        /// <summary>全 audible プレイヤーの累積無音時間（秒）。</summary>
        [PublicAPI] public float GetSilenceDuration()
        {
            return _combinedSilenceDuration;
        }

        /// <summary>無音判定に必要な連続秒数の設定値。</summary>
        [PublicAPI] public float GetSilenceThresholdSec()
        {
            AudioSilenceDetector d = switcher != null ? switcher.GetActiveSilenceDetector() : null;
            return d != null ? d.GetSilenceConsecutiveSec() : 2f;
        }

        /// <summary>クールダウン等により無音 Resync が一時抑制されているか。</summary>
        [PublicAPI] public bool IsSilenceSuppressed()
        {
            return !resyncClient.IsSilenceAutoResyncEligible(Time.time);
        }

        /// <summary>メーター表示用の現在 RMS（dBFS）。無音Resyncの有効/無効に関係なく取得する。</summary>
        [PublicAPI]
        public float GetActiveRmsDbfsForMeter()
        {
            AudioSilenceDetector d = switcher != null ? switcher.GetActiveSilenceDetector() : null;
            return d != null ? d.GetLastRmsDbfs() : -96f;
        }

        /// <summary>メーター表示用の無音判定閾値（dBFS）。</summary>
        [PublicAPI]
        public float GetActiveSilenceThresholdDbfsForMeter()
        {
            AudioSilenceDetector d = switcher != null ? switcher.GetActiveSilenceDetector() : null;
            return d != null ? d.GetSilenceRmsThresholdDbfs() : -60f;
        }

        /// <summary>ドリフト累積がこの閾値を超えると Resync を発行する。</summary>
        [PublicAPI] public float GetDriftResyncThresholdSec()
        {
            return activeMonitor != null ? activeMonitor.GetDriftResyncThresholdSec() : 0.1f;
        }

        [PublicAPI] public string GetLastErrorMessage() { return _lastErrorMessage; }
        /// <summary>エラーメッセージ表示からの経過秒（フェードアウト制御用）。</summary>
        [PublicAPI]
        public float GetErrorMessageAge()
        {
            if (string.IsNullOrEmpty(_lastErrorMessage)) return 0f;
            return Time.time - _lastErrorMessageAt;
        }

        private void SetErrorMessage(string message)
        {
            _lastErrorMessage = message;
            _lastErrorMessageAt = Time.time;
        }

        private void ClearErrorMessage()
        {
            _lastErrorMessage = "";
            _lastErrorMessageAt = 0f;
        }

        private string MapVideoErrorToMessage(VideoError error)
        {
            switch (error)
            {
                case VideoError.RateLimited:
                    return "Rate limited, retrying...";
                case VideoError.InvalidURL:
                    return "Invalid URL";
                case VideoError.AccessDenied:
                    return "Video blocked: enable untrusted URLs";
                case VideoError.PlayerError:
                    return "Video player error, retrying...";
                default:
                    return "Failed to load video";
            }
        }

        /// <summary>Active プレイヤーの現在スタル継続時間（UI インジケータ用）。</summary>
        [PublicAPI]
        public float GetActiveStallDuration()
        {
            return activeMonitor.GetActiveStallDuration();
        }

        /// <summary>現在の FSM 状態を UI 表示用テキストに変換する。</summary>
        [PublicAPI]
        public string GetLocalStateText()
        {
            switch (_localState)
            {
                case STATE_IDLE: return "Idle";
                case STATE_ACTIVE_PLAYING: return "Playing";
                case STATE_REQUEST_PENDING: return "Resync Pending";
                case STATE_RESERVED: return "Reserved";
                case STATE_STANDBY_CONNECTING: return "Connecting...";
                case STATE_STANDBY_VERIFYING: return "Verifying...";
                case STATE_SWITCHING: return "Switching";
                case STATE_COOLDOWN: return "Cooldown";
                case STATE_RETRY_WAIT: return "Retry Wait";
                default: return "Unknown";
            }
        }

        // =================================================================
        //  ログ
        // =================================================================

        private void LogMessage(string message)
        {
            Debug.Log($"[AunCast/DualPlayer] {message}", this);
        }

        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            LogMessage(message);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/DualPlayer] {message}", this);
        }

        private bool _tlClientIdentified;
        private int _tlPrevFsm = -1;
        private bool _tlPrevActiveIsA;
        private int _tlPrevPA = -1;
        private int _tlPrevPB = -1;
        private string _tlAction;

        // pA/pB: 0=idle, 1=loading, 2=playing
        private bool _tlLoadingA;
        private bool _tlLoadingB;

        private int ObservePlayerState(VideoPlayerManager mgr, bool loading)
        {
            if (mgr != null && mgr.IsPlaying()) return 2;
            return loading ? 1 : 0;
        }

        private void LateUpdate()
        {
            if (!_timelineLogging || !_ranInit) return;

            string changes = "";
            if (_localState != _tlPrevFsm)
            {
                changes += $" fsm={_localState}";
                _tlPrevFsm = _localState;
            }
            if (_activeIsA != _tlPrevActiveIsA)
            {
                changes += $" activeIsA={(_activeIsA ? 1 : 0)}";
                _tlPrevActiveIsA = _activeIsA;
            }

            int pA = ObservePlayerState(switcher != null ? switcher.GetPlayerManagerA() : null, _tlLoadingA);
            int pB = ObservePlayerState(switcher != null ? switcher.GetPlayerManagerB() : null, _tlLoadingB);
            // IsPlaying が true になったら loading フラグをクリア
            if (pA == 2) _tlLoadingA = false;
            if (pB == 2) _tlLoadingB = false;
            if (pA != _tlPrevPA)
            {
                changes += $" pA={pA}";
                _tlPrevPA = pA;
            }
            if (pB != _tlPrevPB)
            {
                changes += $" pB={pB}";
                _tlPrevPB = pB;
            }

            if (_tlAction != null)
            {
                changes += $" a={_tlAction}";
                _tlAction = null;
            }
            if (changes.Length > 0)
                TL(changes.Substring(1));
        }

        private void TL(string eventAndData)
        {
            Debug.Log($"[AunCast:TL] st={Networking.GetServerTimeInMilliseconds()} c=LDPC {eventAndData}");
        }

    }
}
