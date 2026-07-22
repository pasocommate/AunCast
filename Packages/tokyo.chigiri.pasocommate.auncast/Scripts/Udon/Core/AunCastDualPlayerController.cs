using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>各ユーザーのローカル再生制御を行う中核 FSM。A/B 二重化再生の異常検知→切替を統括する。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AunCastDualPlayerController : UdonSharpBehaviour
    {
        // package.json の version と必ず一致させる。
        public const string PACKAGE_VERSION = "6.1.0";

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

        // 観客のローカル Mode に対するスタッフ強制値。None のときだけ各観客の選択を使う。
        public const int FORCE_MODE_NONE = 0;
        public const int FORCE_MODE_AUTO = 1;
        public const int FORCE_MODE_MANUAL = 2;

        // =================================================================
        //  Inspector 参照
        // =================================================================
        [Header("Player Managers")]
        [SerializeField] private AunCastVideoPlayerManager playerManagerA;
        [SerializeField] private AunCastVideoPlayerManager playerManagerB;

        [Header("Playback Monitor")]
        [SerializeField] private AunCastPlaybackMonitor playbackMonitor;

        [Header("Sub-components")]
        [SerializeField] private AunCastPlaybackSwitcher switcher;
        [SerializeField] private AunCastActivePlayerMonitor activeMonitor;
        [SerializeField] private AunCastResyncCoordinatorClient resyncClient;

        [Header("UI Notification")]
        [Tooltip("URL 変更を通知する先（AunCastStaffControlPanel を配線）。UI 具象型に依存しないため UdonSharpBehaviour で受ける。")]
        [SerializeField] private UdonSharpBehaviour staffNotifyTarget;
        [SerializeField] private AunCastEventBus eventBus;

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

        [Header("Next URL Prefill")]
        [Tooltip("Next URL 欄の初期値。再生はスタッフ操作でのみ開始する。")]
        [SerializeField] private VRCUrl defaultUrl = new VRCUrl("");

        [Header("Timeline")]
        [Tooltip("タイムラインログを出力する")]
        [SerializeField] private bool _timelineLogging;

        [Header("Silence Resync")]
        [Tooltip("無音検知による自動 Resync の初期有効状態。各クライアントのローカルトグルの初期値で、実行時に UI から切り替えられる。")]
        [SerializeField] private bool _autoSilenceResyncEnabled = true;

        [Header("Manual Mode")]
        [Tooltip("自動起因の Resync / Reboot を抑制し、明示操作だけを受け付けるローカルモード")]
        [SerializeField] private bool _manualModeEnabled;

        // =================================================================
        //  同期変数 (Design Section 14)
        // =================================================================
        [UdonSynced] private VRCUrl _syncedURL = VRCUrl.Empty;
        [UdonSynced] private string _syncedUrlSubmitterName = "";
        [UdonSynced] private int _syncedVideoIdx;
        [UdonSynced] private bool _ownerPlaying;
        [UdonSynced] private int _syncedForceMode = FORCE_MODE_NONE;

        // =================================================================
        //  PlayerData 永続化キー
        // =================================================================
        private const string PLAYERDATA_VOLUME = "AunCast-Volume";

        // =================================================================
        //  ローカル状態変数 (Design Section 13.4)
        // =================================================================

        // FSM 現在状態と A/B どちらが Active かの基本ロール
        private int _localState;
        private bool _activeIsA = true;
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
        private bool _loggedMissingResyncSlot;

        // PlaybackActive レポートのスロットル（AunCastPlaybackMonitor への過剰通知を防止）
        private float _lastPlaybackReportAt;
        private const float PLAYBACK_REPORT_MIN_INTERVAL = 10.0f;

        // AunCastVideoPlayerManager コールバック用（コールバック元を識別するための一時変数）
        [System.NonSerialized] public int _lastCallbackPlayerIndex;
        [System.NonSerialized] public VideoError _lastVideoError;

        // 直近のエラーメッセージ（UI 表示用）。空文字 = エラーなし。
        private string _lastErrorMessage = "";
        private float _lastErrorMessageAt;

        private bool _ranInit;
        private bool _hasReportedPlaybackActive;
        private bool _lastReportedPlaybackActive;
        private int _initializedPlaybackMonitorSlot = -1;

        // Connecting / Error レポートの重複送信抑止（-1=未送信, 0=false, 1=true）。
        // これらはイベント駆動で定期再送がないため、同値の連続送信は純粋な無駄になる。
        private int _lastReportedConnecting = -1;
        private int _lastReportedError = -1;

        // FSM 状態変化通知用 (-1 = 未通知)
        private int _lastReportedLocalState = -1;

        private bool _startupSyncResetFinished;
        /// <summary>settle 完了後に同期スナップショットから初期化済みか。false の間は同期に反応しない。</summary>
        private bool _syncInitialized;
        /// <summary>bunch を一度でも適用済みか。IsNetworkSettled だけでは適用完了を保証しない（Quest 実測）。</summary>
        private bool _syncApplied;
        /// <summary>settle の成立を観測済みか。_wasOwnerAtSettle を確定させたかの判定に使う。</summary>
        private bool _settleObserved;
        /// <summary>settle 成立時点で Owner だったか。新規インスタンスの初代 Owner だけが true になる。</summary>
        private bool _wasOwnerAtSettle;
        /// <summary>直前にローカル適用した強制 Mode。同期受信時の実効 Mode 遷移検知に使う。</summary>
        private int _appliedForceMode = FORCE_MODE_NONE;

        // =================================================================
        //  Unity ライフサイクル
        // =================================================================

        /// <summary>ローカル初期化のみを行う。同期への書き込みは settle 後の初期化（Update 先頭）で行う。</summary>
        private void Start()
        {
            if (_ranInit) return;
            _ranInit = true;

            SetVolume(defaultVolume);

            // Active は A、Standby は B。B のオーディオはミュート開始
            _activeIsA = true;
            if (switcher != null)
            {
                switcher.InitializeToA();
                switcher.EnsureAudioLinkBehaviourAssignedFromScene();
                switcher.SwitchAudioLinkSource();
            }

            LogMessage($"AunCast version={PACKAGE_VERSION} initialized");
        }

        /// <summary>
        /// settle の成立を観測し、その時点で Owner だったかを記録する。
        /// late-joiner は settle 時点では master が Owner なので false になり、
        /// あとから ownership が転がり込んでも初代 Owner と区別できる。
        /// </summary>
        private void ObserveSettle()
        {
            if (_settleObserved) return;
            if (!Networking.IsNetworkSettled) return;

            _settleObserved = true;
            _wasOwnerAtSettle = Networking.IsOwner(gameObject);
        }

        /// <summary>
        /// 新規インスタンスの初代 Owner が、エディタ編集で残留し得る同期値を既定値へ正規化する。
        /// settle 前は同期値が復元前の既定値である可能性があるため実行しない
        /// （時間窓ではなく settle を基準にすることで、Join 中に ownership が
        /// 転がり込んだクライアントが再生中の同期値を消して配信する事故を防ぐ）。
        /// settle 時点で Owner だったクライアントに限るのも同じ理由で、bunch 未受信のまま
        /// ownership が転がり込んだ late-joiner は、同期の既定値を現在値と誤認してしまう。
        /// </summary>
        private bool ResetPlaybackSyncStateForNewInstance()
        {
            if (_startupSyncResetFinished) return false;
            if (!Networking.IsNetworkSettled) return false;
            if (!Networking.IsOwner(gameObject)) return false;
            if (!_wasOwnerAtSettle) return false;

            _startupSyncResetFinished = true;
            if (_localState != STATE_IDLE || _ownerPlaying) return false;

            _syncedURL = VRCUrl.Empty;
            _syncedUrlSubmitterName = "";
            _syncedVideoIdx = 0;
            _currentVideoIdx = 0;
            _ownerPlaying = false;
            _syncedForceMode = FORCE_MODE_NONE;
            return true;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (ResetPlaybackSyncStateForNewInstance())
                QueueSerialize();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            if (PlayerData.HasKey(player, PLAYERDATA_VOLUME))
            {
                float vol = PlayerData.GetFloat(player, PLAYERDATA_VOLUME);
                SetVolume(vol);
            }
        }

        /// <summary>メインループ: グローバルリブート確認→スロット確保→Coordinator ポーリング→FSM→無音検知の順で毎フレーム駆動。</summary>
        private void Update()
        {
            // Join 時同期の完了までは一切動かさない。IsNetworkSettled が真になっても
            // bunch 適用（OnDeserialization）が遅れる実測があるため（Quest で確認）、
            // 非 Owner の初期化点は「settle と初回 bunch 適用の両方が済んだ時点」とする。
            if (!_syncInitialized)
            {
                ObserveSettle();
                if (!_settleObserved) return;

                if (_wasOwnerAtSettle && Networking.IsOwner(gameObject))
                {
                    _syncInitialized = true;
                    // 初代 Owner: エディタ編集で残留し得る同期値を正規化して現在値を配信する。
                    // これにより非 Owner へ必ず初回 bunch が届き、下記の初期化点が成立する。
                    ResetPlaybackSyncStateForNewInstance();
                    QueueSerialize();
                }
                else if (_syncApplied)
                {
                    // 既存インスタンスへの参加。適用済みの同期値から一括初期化する。
                    _syncInitialized = true;
                    _startupSyncResetFinished = true;
                    ApplySyncedState();
                }
                else
                {
                    // settle 済みだが bunch 未着。この間に ownership が転がり込んでも、
                    // 同期の既定値を現在値と誤認して配信しないよう、受信を待つ。
                    return;
                }
            }

            float now = Time.time;

            // グローバル強制リブート指令の確認
            if (resyncClient.PollGlobalForceReboot())
            {
                Reboot();
                return;
            }
            bool hasSlot = resyncClient.TryEnsureSlotAssigned();
            if (hasSlot)
            {
                if (_loggedMissingResyncSlot)
                {
                    _loggedMissingResyncSlot = false;
                    LogMessage($"Resync slot assigned; Coordinator reports resumed (slot={resyncClient.GetMySlotIndex()})");
                }

                InitializePlaybackMonitorSlotIfNeeded();

                // スロット確保直後の connecting レポート遅延送信
                if (_pendingConnectingReport)
                {
                    _pendingConnectingReport = false;
                    if (_localState == STATE_IDLE)
                        ReportConnecting(true);
                }
            }
            else if (!_loggedMissingResyncSlot)
            {
                _loggedMissingResyncSlot = true;
                TL($"a=SLOT_PENDING_LOCAL_PLAYBACK");
                LogMessage("Resync slot not assigned yet; continuing local playback without Coordinator reports");
            }

            if (!_tlClientIdentified)
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (local != null)
                {
                    TL($"e=CLIENT_INIT name={local.displayName} playerId={local.playerId} slot={resyncClient.GetMySlotIndex()}");
                    _tlClientIdentified = true;
                }
            }

            if (hasSlot)
            {
                // Coordinator から Resync スロットの予約通知を受け取り FSM 状態を更新
                // Manual Mode の手動復旧待ち中でも、スタッフの明示的な Global Resync は採用する。
                // CoordinatorClient 側では ACTIVE_PLAYING が外部要求の採用状態なので、判定時だけ読み替える。
                int coordinatorPollState = GetManualModeEnabled()
                    && _localState == STATE_RETRY_WAIT
                    && !_awaitingActiveReboot
                        ? STATE_ACTIVE_PLAYING
                        : _localState;
                int pollResult = resyncClient.PollResyncCoordinator(now, coordinatorPollState);
                if (pollResult >= 0)
                {
                    if (pollResult == STATE_RESERVED)
                        resyncClient.MarkCycleStarted(now);
                    _localState = pollResult;
                }
            }

            PollSyncWait(now);

            if (PollActiveReboot(now)) return;

            PollActiveMonitoring(now);
            TickStateMachine(now);

            if (switcher != null) switcher.UpdateRenderTexture(_localState, _ownerPlaying);
            PollSilenceDetection(now);
            if (hasSlot)
                ReportPlaybackStateToCoordinator();
            NotifyLocalStateChangeIfNeeded();
        }

        /// <summary>FSM 状態が前フレームから変化していれば購読者へ Push 通知する。</summary>
        private void NotifyLocalStateChangeIfNeeded()
        {
            if (_localState == _lastReportedLocalState) return;
            _lastReportedLocalState = _localState;
            if (eventBus != null)
                eventBus.PublishLocalStateChanged();
        }

        /// <summary>非オーナーがオーナーの _ownerPlaying=true 同期を待ち、到達したら再生を再開する。</summary>
        private void PollSyncWait(float now)
        {
            if (!_waitForSync || !_ownerPlaying) return;

            switcher.GetActiveManager().Play();
            _waitForSync = false;
            EnterActivePlaying(now);
        }

        /// <summary>
        /// ACTIVE_PLAYING へ遷移し、保留中のリトライ予約を破棄して監視を初期化する。
        /// BindRoles → InitializeForActive の順序制約（F-5）をここに集約する。
        /// </summary>
        private void EnterActivePlaying(float now)
        {
            _retryWaitUntil = 0f;
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

        /// <summary>AunCastActivePlayerMonitor に Active/Standby の時刻進行ポーリングを委譲する。</summary>
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
                    // 初回ロード失敗後も同期 URL が残っていればローカル再接続する。
                    // Stop は同期 URL の空で判定するため、Owner の再生開始同期を待つ必要はない。
                    if (!GetManualModeEnabled()
                        && _retryWaitUntil > 0f
                        && now >= _retryWaitUntil
                        && HasSyncedUrl())
                    {
                        _retryWaitUntil = 0f;
                        _localState = STATE_RETRY_WAIT;
                        AttemptActiveReboot(now);
                    }
                    break;

                case STATE_ACTIVE_PLAYING:
                    if (GetManualModeEnabled()) break;

                    // OnVideoError または異常検知時に Cooldown 等で要求できなかった場合、
                    // 条件が整うまで要求を保留する。初回時刻前進前のエラーも握り潰さない。
                    if (_retryWaitUntil > 0f)
                    {
                        // 遅延エラー後も現在の Active が再生まで到達した場合は、
                        // 新しいロードの成功を優先して保留要求を破棄する。
                        // IsActiveAlive はストール中（IsPlaying のまま時刻凍結）でも true を
                        // 返すため、障害検知（レベルトリガ）が解消していることも要求する。
                        // これを欠くと保留と破棄が毎フレーム往復し、ReportError の
                        // NetworkEvent とログを連打してしまう。
                        if (IsActiveAlive()
                            && !activeMonitor.DetectActiveFailure(now, resyncClient.IsDriftResyncEnabled(), resyncClient.GetDriftResyncThresholdSec()))
                        {
                            _retryWaitUntil = 0f;
                            ClearErrorMessage();
                            ReportError(false);
                            LogMessage("Deferred active failure canceled: active recovered");
                            break;
                        }

                        if (now >= _retryWaitUntil)
                        {
                            if (resyncClient.TryRequestResync(now, AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE))
                            {
                                _retryWaitUntil = 0f;
                                _localState = STATE_REQUEST_PENDING;
                                LogMessage("Deferred active failure Resync requested");
                            }
                            else
                            {
                                // スロット未割当や Coordinator 反映待ちの場合は短い間隔で再確認する。
                                _retryWaitUntil = now + 0.25f;
                            }
                        }
                        break;
                    }

                    if (activeMonitor.DetectActiveFailure(now, resyncClient.IsDriftResyncEnabled(), resyncClient.GetDriftResyncThresholdSec()))
                    {
                        if (resyncClient.TryRequestResync(now, AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE))
                        {
                            LogWarning($"Active failure -> RequestPending (stall={activeMonitor.GetActiveStallDuration():F2}s, drift={activeMonitor.GetDriftAccumulator():F3}s)");
                            _tlAction = "ACTIVE_FAILURE";
                            _localState = STATE_REQUEST_PENDING;
                        }
                        else
                        {
                            DeferActiveFailureResync(now);
                        }
                    }
                    break;

                case STATE_REQUEST_PENDING:
                    if (resyncClient.GetRequestReason() == AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE
                             && activeMonitor.GetConsecutiveStallCount() == 0
                             && activeMonitor.GetConsecutiveAdvanceCount() >= activeMonitor.GetMinConsecutiveAdvances())
                    {
                        CancelResync();
                        LogMessage("Resync canceled: active recovered");
                        EnterActivePlaying(now);
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
                        resyncClient.SetConsecutiveFailCount(0);
                        // 切替直後は switcher 側のロールが正なので取り込んでから遷移する
                        _activeIsA = switcher.GetActiveIsA();
                        EnterActivePlaying(now);
                    }
                    break;

                case STATE_RETRY_WAIT:
                    if (!GetManualModeEnabled() && now >= _retryWaitUntil && !_awaitingActiveReboot)
                    {
                        AttemptActiveReboot(now);
                    }
                    break;

            }
        }

        /// <summary>音声出力のある全プレイヤーが無音状態か判定し、一定時間継続したら自動 Resync を発行する。</summary>
        private void PollSilenceDetection(float now)
        {
            if (GetManualModeEnabled() || _localState != STATE_ACTIVE_PLAYING || !_autoSilenceResyncEnabled) return;
            if (!activeMonitor.HasSeenPlayerTimeAdvance()) { _combinedSilenceDuration = 0f; return; }
            if (!resyncClient.IsSilenceAutoResyncEligible(now)) { _combinedSilenceDuration = 0f; return; }

            AunCastSpeaker activeDet = switcher.GetActiveSilenceDetector();
            AunCastSpeaker standbyDet = switcher.GetStandbySilenceDetector();
            float threshold = activeDet != null ? activeDet.GetSilenceRmsThreshold() : 0.001f;
            float requiredSec = activeDet != null ? activeDet.GetSilenceConsecutiveSec() : 2f;

            AunCastVideoPlayerManager activeMgr = switcher.GetActiveManager();
            AunCastVideoPlayerManager standbyMgr = switcher.GetStandbyManager();

            // 両系統ともユーザー音量でミュート (slider=0) の場合は無音検知をスキップ。
            // ミュート中は GetOutputData が全ゼロを返し RMS 正規化が成立しないため。
            bool activeMuted = activeMgr == null || activeMgr.GetVolume() <= 0f;
            bool standbyMuted = standbyMgr == null || standbyMgr.GetVolume() <= 0f;
            if (activeMuted && standbyMuted) { _combinedSilenceDuration = 0f; return; }

            bool anyAudible = false;
            anyAudible |= CheckPlayerAudible(activeMgr, activeDet, threshold);
            anyAudible |= CheckPlayerAudible(standbyMgr, standbyDet, threshold);

            if (anyAudible)
                _combinedSilenceDuration = 0f;
            else
                _combinedSilenceDuration += Time.deltaTime;

            if (_combinedSilenceDuration >= requiredSec
                && resyncClient.TryRequestResync(now, AunCastResyncCoordinatorClient.REQUEST_REASON_SILENCE))
            {
                LogWarning("Silence detected on all audible players; requesting individual resync");
                _tlAction = "SILENCE_RESYNC";
                _combinedSilenceDuration = 0f;
                _localState = STATE_REQUEST_PENDING;
            }
        }

        /// <summary>指定プレイヤーの音量が閾値以上かを判定する（フェード中のミュート側を除外するため FadeGain も確認）。</summary>
        private bool CheckPlayerAudible(AunCastVideoPlayerManager mgr, AunCastSpeaker det, float threshold)
        {
            if (mgr == null || det == null) return false;
            if (mgr.GetFadeGain() <= 0f) return false;
            return det.GetRms() >= threshold;
        }

        /// <summary>AunCastPlaybackMonitor に再生状態を定期報告する（全体ステータス UI 表示用）。</summary>
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

            // 定期送信のタイミングでは connecting / error も現在値を再送する。
            // これらはイベント駆動で再送機会がないため、RPC ロストや Monitor の
            // owner 交代でビットが食い違うと表示が固着する。10 秒周期で自己修復する。
            if (!valueChanged)
            {
                if (_lastReportedConnecting >= 0)
                    playbackMonitor.ReportConnectingForSlot(slotIndex, _lastReportedConnecting == 1);
                if (_lastReportedError >= 0)
                    playbackMonitor.ReportErrorForSlot(slotIndex, _lastReportedError == 1);
            }

            _lastReportedPlaybackActive = isPlaying;
            _hasReportedPlaybackActive = true;
            _lastPlaybackReportAt = now;
        }

        /// <summary>スロット再利用時の残留ビットを引き継がないよう、割当直後に全状態を初期化する。</summary>
        private void InitializePlaybackMonitorSlotIfNeeded()
        {
            if (playbackMonitor == null) return;

            int slotIndex = resyncClient.GetMySlotIndex();
            if (slotIndex < 0 || slotIndex == _initializedPlaybackMonitorSlot) return;

            playbackMonitor.ReportForSlot(slotIndex, false);
            playbackMonitor.ReportConnectingForSlot(slotIndex, false);
            playbackMonitor.ReportErrorForSlot(slotIndex, false);
            _initializedPlaybackMonitorSlot = slotIndex;
            _lastReportedPlaybackActive = false;
            _hasReportedPlaybackActive = true;
            _lastReportedConnecting = 0;
            _lastReportedError = 0;
            _lastPlaybackReportAt = Time.time;
        }

        private void ReportConnecting(bool isConnecting)
        {
            if (playbackMonitor == null || resyncClient.GetMySlotIndex() < 0) return;
            int encoded = isConnecting ? 1 : 0;
            if (encoded == _lastReportedConnecting) return;
            _lastReportedConnecting = encoded;
            playbackMonitor.ReportConnectingForSlot(resyncClient.GetMySlotIndex(), isConnecting);
        }

        private void ReportError(bool isError)
        {
            if (playbackMonitor == null || resyncClient.GetMySlotIndex() < 0) return;
            int encoded = isError ? 1 : 0;
            if (encoded == _lastReportedError) return;
            _lastReportedError = encoded;
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
            MarkMeasLoad(_activeIsA ? 1 : 0);
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
            LogMeasSwitch("MEAS_SWITCH_START");
            switcher.StartCrossfade(now);
            _localState = STATE_SWITCHING;
        }

        /// <summary>クロスフェード完了後のロール交換。旧 Active 停止→新 Active 確定→クールダウンに移行する。</summary>
        private void CompleteSwitch(float now)
        {
            // 旧 Active 停止前に両プレイヤーの再生位置を記録する
            LogMeasSwitch("MEAS_SWITCH_DONE");

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
            resyncClient.SetRequestReason(AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE);
            resyncClient.OnResyncCompleted(now);
            _retryWaitUntil = 0f;

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
            resyncClient.SetRequestReason(AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE);
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

            if (GetManualModeEnabled())
            {
                resyncClient.ReportResult(false);
                ReportError(true);
                _retryWaitUntil = 0f;
                _localState = STATE_RETRY_WAIT;
                LogMessage("Both systems failed; waiting for manual recovery (Manual Mode)");
                return;
            }

            // 両系統失敗 → exponential backoff。直前の LoadURL によるローカル
            // Cooldown が残っている場合は、その終了前に再ロードしない。
            float retryDelay = EnterRetryWait(now);

            resyncClient.ReportResult(false);
            ReportError(true);

            LogMessage($"Both systems failed, retry in {retryDelay:F1}s (attempt {resyncClient.GetConsecutiveFailCount()})");
        }

        /// <summary>
        /// リトライ予約時刻を設定する共通ヘルパー。直前の LoadURL によるローカル Cooldown が
        /// 残っている場合は、その終了前に再ロードしないようクランプする。
        /// </summary>
        private float ScheduleRetryAt(float earliestAt)
        {
            _retryWaitUntil = Mathf.Max(earliestAt, resyncClient.GetLocalCooldownUntil());
            return _retryWaitUntil;
        }

        /// <summary>連続失敗数とローカル Cooldown を考慮して Active 直接再接続を予約する。</summary>
        private float EnterRetryWait(float now)
        {
            int failCount = resyncClient.GetConsecutiveFailCount() + 1;
            resyncClient.SetConsecutiveFailCount(failCount);
            float backoff = Mathf.Min(
                resyncClient.GetBaseCooldownSec() * Mathf.Pow(resyncClient.GetRetryCooldownMultiplier(), failCount - 1),
                resyncClient.GetMaxRetryCooldownSec());
            float retryAt = ScheduleRetryAt(now + backoff);
            _localState = STATE_RETRY_WAIT;
            return retryAt - now;
        }

        /// <summary>Active 障害の Resync 要求をローカル Cooldown 終了後（最短 0.25 秒後）まで保留する。</summary>
        private void DeferActiveFailureResync(float now)
        {
            ScheduleRetryAt(now + 0.25f);
            LogMessage($"Active failure Resync deferred until {_retryWaitUntil:F2}");
        }

        /// <summary>
        /// Active プレイヤーが再生状態で、再生時刻が 0 を超えているかを返す。
        /// 時刻の前進までは確認しないため、ストール中でも true になり得る。
        /// 前進の確認が必要な判定では AunCastActivePlayerMonitor を併用する。
        /// </summary>
        private bool IsActiveAlive()
        {
            AunCastVideoPlayerManager active = switcher.GetActiveManager();
            if (active == null) return false;
            return active.IsPlaying() && active.GetTime() > 0f;
        }

        /// <summary>最終手段: 両プレイヤーを停止し A に強制ロードする。Standby 切替が不可能な場合の復旧策。</summary>
        private void AttemptActiveReboot(float now)
        {
            BeginDirectReboot(now);
            LogMessage("Attempting Active direct reboot");
        }

        /// <summary>直接リブートの共通セットアップ。Reboot() と AttemptActiveReboot() で使う。</summary>
        private void BeginDirectReboot(float now)
        {
            _retryWaitUntil = 0f;
            _awaitingActiveReboot = true;
            _activeRebootStartedAt = now;
            _activeIsA = true;
            activeMonitor.ResetTimeAdvanceForPlayer(true);
            ReportError(false);
            ReportConnecting(true);
            _tlLoadingA = true;
            switcher.StartActiveDirectReboot(_syncedURL);
            MarkMeasLoad(0);
        }

        // =================================================================
        //  緊急リブート (Design FR-16b)
        // =================================================================

        /// <summary>ユーザー操作またはグローバル指令による緊急リブート。全状態をリセットし A で再ロードする。</summary>
        [PublicAPI]
        public void Reboot()
        {
            if (!HasPlayableSyncedUrl())
            {
                LogWarning("Reboot ignored: no synced URL");
                return;
            }

            float now = Time.time;
            // 実行中の Resync をキャンセル（どの状態でも Coordinator 側のスロットを解放する）
            CancelResync();
            BeginDirectReboot(now);
            _tlAction = "EMERGENCY_REBOOT";
            _localState = STATE_RETRY_WAIT;
            LogMessage("Reboot initiated");
        }

        /// <summary>ユーザー操作による手動 Resync 要求。再生中のみ予約制で受け付ける。</summary>
        [PublicAPI]
        public bool RequestManualResync()
        {
            if (!HasPlayableSyncedUrl()) return false;

            float now = Time.time;
            if (_localState != STATE_ACTIVE_PLAYING) return false;
            if (!resyncClient.TryRequestResync(now, AunCastResyncCoordinatorClient.REQUEST_REASON_MANUAL)) return false;

            _retryWaitUntil = 0f;
            _tlAction = "MANUAL_RESYNC";
            _localState = STATE_REQUEST_PENDING;
            LogMessage("Manual resync requested");
            return true;
        }

        /// <summary>Viewer/Wall UI が Resync ボタンを有効化してよいかを返す。</summary>
        [PublicAPI]
        public bool CanRequestManualResync()
        {
            if (!HasPlayableSyncedUrl()) return false;
            return _localState == STATE_ACTIVE_PLAYING
                && resyncClient.CanRequestResync(Time.time);
        }

        /// <summary>Viewer/Wall UI がローカル Reboot ボタンを有効化してよいかを返す。</summary>
        [PublicAPI]
        public bool CanRebootLocal()
        {
            return HasPlayableSyncedUrl();
        }

        private bool HasPlayableSyncedUrl()
        {
            return _ownerPlaying && HasSyncedUrl();
        }

        /// <summary>再生開始待ちを含め、同期対象として有効な URL が存在するかを返す。</summary>
        private bool HasSyncedUrl()
        {
            return _syncedURL != null && !string.IsNullOrEmpty(_syncedURL.Get());
        }

        // =================================================================
        //  AunCastVideoPlayerManager コールバック
        // =================================================================

        /// <summary>AunCastVideoPlayerManager からの Ready コールバック。Active なら即 Play、Standby なら検証フローへ進む。</summary>
        public void OnManagerVideoReady()
        {
            bool isActiveEvent = IsActiveEvent();
            _tlAction = "VIDEO_READY";
            LogMeasReady(_lastCallbackPlayerIndex);

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
                    switcher.GetStandbyManager().Play();
                }
            }
        }

        /// <summary>AunCastVideoPlayerManager からの Start コールバック。オーナーは _ownerPlaying を配信、非オーナーは同期待ちを判断する。</summary>
        public void OnManagerVideoStart()
        {
            bool isActiveEvent = IsActiveEvent();
            _tlAction = "VIDEO_START";
            LogMeasStart(_lastCallbackPlayerIndex);

            if (isActiveEvent)
            {
                _retryWaitUntil = 0f;
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
                        if (staffNotifyTarget != null)
                            staffNotifyTarget.SendCustomEvent("OnUrlChanged");
                    }

                    switcher.SwitchAudioLinkSource();
                    LogMessage("Active reboot succeeded");
                    return;
                }

                // 通常の Active 開始。オーナーは再生開始を配信してから共通遷移へ進む。
                bool isOwner = Networking.IsOwner(gameObject);
                if (isOwner)
                {
                    _ownerPlaying = true;
                    QueueSerialize();
                    if (staffNotifyTarget != null)
                        staffNotifyTarget.SendCustomEvent("OnUrlChanged");
                }

                if (!isOwner && !_ownerPlaying)
                {
                    // 非オーナーの初回ロード完了。オーナーの再生開始同期まで Pause で待つ。
                    switcher.GetActiveManager().Pause();
                    _waitForSync = true;
                    _localState = STATE_IDLE;
                }
                else if (_localState == STATE_IDLE || _localState == STATE_RETRY_WAIT)
                {
                    EnterActivePlaying(Time.time);
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
                }
            }
        }

        public void OnManagerVideoEnd() { }

        /// <summary>AunCastVideoPlayerManager からの Error コールバック。Active ならリブート失敗判定/Resync 要求、Standby なら切替断念。</summary>
        public void OnManagerVideoError()
        {
            bool isActiveEvent = IsActiveEvent();
            VideoError error = _lastVideoError;
            LogWarning($"OnVideoError received (activeEvent={isActiveEvent}, error={error})");
            _tlAction = "VIDEO_ERROR";

            if (!isActiveEvent)
            {
                // Standby のエラー
                if (_localState == STATE_STANDBY_CONNECTING || _localState == STATE_STANDBY_VERIFYING)
                    HandleStandbyFailure(Time.time);
                return;
            }

            // URL が既に停止済みなら、中断した古いロードから遅れて届いたエラーとして無視する。
            if (!_awaitingActiveReboot && !HasSyncedUrl())
            {
                LogMessage($"Ignored stale active error after stop ({error})");
                return;
            }

            // URL 差し替え前の遅延エラーが、新しい Active の健全な再生を乗っ取らないようにする。
            if (!_awaitingActiveReboot && _localState == STATE_ACTIVE_PLAYING && IsActiveAlive())
            {
                LogMessage($"Ignored stale active error while current Active is alive ({error})");
                return;
            }

            SetErrorMessage(MapVideoErrorToMessage(error));
            if (_awaitingActiveReboot)
            {
                HandleActiveRebootError(error);
                return;
            }

            ReportConnecting(false);
            ReportError(true);

            if (_localState == STATE_ACTIVE_PLAYING)
                HandleActivePlayingError();
            else if (_localState == STATE_COOLDOWN)
                HandleCooldownError();
            else if (_localState == STATE_IDLE)
                HandleInitialLoadError(error);
        }

        /// <summary>Active 直接リブート待機中のエラー。エラー種別に応じて中止/待機/失敗処理へ振り分ける。</summary>
        private void HandleActiveRebootError(VideoError error)
        {
            _awaitingActiveReboot = false;

            if (!IsRetryableVideoError(error))
            {
                ReportConnecting(false);
                ReportError(true);
                resyncClient.SetResyncRequested(false);
                _retryWaitUntil = 0f;
                _localState = STATE_IDLE;
                LogWarning($"Active reboot stopped after non-retryable error ({error})");
                return;
            }

            if (error == VideoError.RateLimited)
            {
                // レート制限 → 少し待ってリトライ（connecting は維持）
                float retryDelay = Mathf.Max(5.0f, resyncClient.GetBaseCooldownSec());
                ScheduleRetryAt(Time.time + retryDelay);
                LogMessage($"Active reboot rate limited, waiting {retryDelay:F1}s");
            }
            else
            {
                ReportConnecting(false);
                HandleFailed(Time.time);
            }
        }

        /// <summary>Active 再生中のエラー。Resync を要求し、Cooldown 等で要求できなければ保留する。</summary>
        private void HandleActivePlayingError()
        {
            if (GetManualModeEnabled()) return;

            float now = Time.time;
            if (resyncClient.TryRequestResync(now, AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE))
            {
                _retryWaitUntil = 0f;
                _localState = STATE_REQUEST_PENDING;
            }
            else
            {
                DeferActiveFailureResync(now);
            }
        }

        /// <summary>
        /// Active 直接再接続の成功直後（Cooldown 中）のエラー。Cooldown の終了を待ってから
        /// 再試行する。エラーを COOLDOWN 状態に取り残さない。
        /// </summary>
        private void HandleCooldownError()
        {
            if (GetManualModeEnabled())
            {
                _retryWaitUntil = 0f;
                _localState = STATE_RETRY_WAIT;
                LogMessage("Active failed during cooldown; waiting for manual recovery");
            }
            else
            {
                float retryDelay = EnterRetryWait(Time.time);
                LogMessage($"Active failed during cooldown, retry in {retryDelay:F1}s");
            }
        }

        /// <summary>
        /// 初回ロード（IDLE）中のエラー → 待機後にリブート再試行。
        /// 従来は Join 直後の誤検知グローバルリブートが偶発的に復旧させていたが、その誤検知を
        /// 止めたため、再試行可能な初回ロード失敗だけを明示的にリトライ経路へ載せる。
        /// </summary>
        private void HandleInitialLoadError(VideoError error)
        {
            if (!IsRetryableVideoError(error))
            {
                _retryWaitUntil = 0f;
                LogWarning($"Initial active load stopped after non-retryable error ({error})");
            }
            else if (GetManualModeEnabled())
            {
                _retryWaitUntil = 0f;
                _localState = STATE_RETRY_WAIT;
                LogMessage("Initial active load failed; waiting for manual recovery");
            }
            else
            {
                // 古いロードの遅延エラーが新ロードを即座に中断しないよう、通常の
                // Ready+Play タイムアウトまでは IDLE のまま成功コールバックを待つ。
                float retryDelay = Mathf.Max(
                    resyncClient.GetBaseCooldownSec(),
                    readyTimeoutSec + playTimeoutSec);
                ScheduleRetryAt(Time.time + retryDelay);
                LogMessage($"Initial active load failed ({error}), retry eligible in {retryDelay:F1}s");
            }
        }

        public void OnManagerVideoLoop() { }

        // =================================================================
        //  URL 管理 (Design Section 14)
        // =================================================================

        /// <summary>配信 URL として妥当な形式かを判定する（スキーム区切り `://` の位置 1〜8、長さ 4096 文字以内）。</summary>
        [PublicAPI]
        public bool IsValidStreamUrl(string urlStr)
        {
            if (string.IsNullOrEmpty(urlStr)) return false;
            int idx = urlStr.IndexOf("://", System.StringComparison.Ordinal);
            if (idx < 1 || idx > 8) return false;
            return urlStr.Length <= 4096;
        }

        /// <summary>スタッフによる配信 URL 設定。オーナーシップ取得→既存停止→新 URL で再生開始→全員に同期する。</summary>
        [PublicAPI]
        public void PlayVideoAsStaff(VRCUrl url)
        {
            if (!Networking.IsNetworkSettled) return;

            string urlStr = url.Get();
            if (!IsValidStreamUrl(urlStr)) return;

            bool wasOwner = Networking.IsOwner(gameObject);
            if (!wasOwner)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            StopVideoInternal();

            _syncedURL = url;
            _syncedUrlSubmitterName = Networking.LocalPlayer.displayName;

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
            if (staffNotifyTarget != null) staffNotifyTarget.SendCustomEvent("OnUrlChanged");
        }

        /// <summary>スタッフによる配信停止。オーナーシップ取得後に全プレイヤーを停止し同期する。</summary>
        [PublicAPI]
        public void StopVideoAsStaff()
        {
            if (!Networking.IsNetworkSettled) return;

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
            _syncedUrlSubmitterName = "";

            CleanupPlaybackToIdle();

            QueueSerialize();
            if (staffNotifyTarget != null) staffNotifyTarget.SendCustomEvent("OnUrlChanged");
            LogMessage("StopVideo completed: texture cleared");
        }

        /// <summary>両プレイヤー停止・テクスチャ解放・FSM リセットの共通停止クリーンアップ。</summary>
        private void CleanupPlaybackToIdle()
        {
            activeMonitor.ResetTimeAdvanceForPlayer(true);
            activeMonitor.ResetTimeAdvanceForPlayer(false);
            switcher.ResetBothPlayersToA();
            switcher.ClearVideoTexture();
            _activeIsA = true;
            ResetFsmToIdle();
        }

        /// <summary>FSM 状態・フラグをアイドルに一括リセットし、Coordinator への報告も行う。</summary>
        private void ResetFsmToIdle()
        {
            _tlLoadingA = false;
            _tlLoadingB = false;
            if (_localState >= STATE_REQUEST_PENDING && _localState <= STATE_SWITCHING)
                CancelResync();
            _localState = STATE_IDLE;
            _waitForSync = false;
            _awaitingActiveReboot = false;
            _retryWaitUntil = 0f;
            _pendingConnectingReport = false;
            // 停止/リセット時はバックオフをクリアし、次回再生の初回リトライを base 間隔から始める
            resyncClient.SetConsecutiveFailCount(0);
            ReportPlaybackInactive();
            ReportConnecting(false);
            ReportError(false);
        }

        /// <summary>停止・リセット時に、次回定期報告を待たず再生中ビットを即時解除する。</summary>
        private void ReportPlaybackInactive()
        {
            if (playbackMonitor == null || resyncClient.GetMySlotIndex() < 0) return;
            // 既に非再生を報告済みなら再送しない（停止中の反復クリーンアップで RPC を連打しない）
            if (_hasReportedPlaybackActive && !_lastReportedPlaybackActive) return;

            playbackMonitor.ReportForSlot(resyncClient.GetMySlotIndex(), false);
            _lastReportedPlaybackActive = false;
            _hasReportedPlaybackActive = true;
            _lastPlaybackReportAt = Time.time;
        }

        /// <summary>Active プレイヤーに URL をロードし、接続中状態に入る共通ヘルパー。</summary>
        private void StartActivePlayback(VRCUrl url)
        {
            _localState = STATE_IDLE;
            _retryWaitUntil = 0f;
            activeMonitor.ResetTimeAdvanceForPlayer(_activeIsA);
            ReportError(false);
            ReportConnecting(true);
            if (resyncClient.GetMySlotIndex() < 0)
                _pendingConnectingReport = true;
            if (_activeIsA) _tlLoadingA = true; else _tlLoadingB = true;
            switcher.GetActiveManager().LoadURL(url);
            MarkMeasLoad(_activeIsA ? 0 : 1);
            // 新規ロード直後は VRChat の URL レート制限窓（約5秒）内に Standby 再接続が走らないよう
            // ローカルクールダウンを張る。着地直後の stall 由来 Resync が窓内で LoadURL して
            // RateLimited になる（=不要な二重接続）のを防ぐ。直接リブートはクールダウン非依存で動く。
            resyncClient.SetLocalCooldownUntil(Time.time + resyncClient.GetLocalCooldownSec());
            LogMessage($"Started playback: {url}");
        }

        /// <summary>
        /// 非オーナーが同期変数の変更を受信する。settle 前の中間 bunch には反応せず、
        /// settle 後の最初の受信を初期化点として一括反映する（settle 一括初期化）。
        /// </summary>
        public override void OnDeserialization()
        {
            if (Networking.IsOwner(gameObject)) return;
            _syncApplied = true;
            if (!_syncInitialized)
            {
                // 通常順序（bunch → settle）。値は適用済みなので settle 時に Update 側で反映する。
                if (!Networking.IsNetworkSettled) return;
                _syncInitialized = true;
                // 既存インスタンスへの参加。起動時正規化は不要と確定する。
                _startupSyncResetFinished = true;
            }
            ApplySyncedState();
        }

        /// <summary>同期変数の現在値をローカル状態へ反映する。URL 変更時は再ロード、停止時は全クリーンアップを行う。</summary>
        private void ApplySyncedState()
        {
            ApplySyncedForceMode();

            // 再生停止（オーナーの Stop を受信）。
            // _ownerPlaying=false だけでは初回ロード中も該当するため、同期 URL の空も必須とする。
            bool stopReceived = !_ownerPlaying && !HasSyncedUrl();
            if (stopReceived)
            {
                CleanupPlaybackToIdle();
                LogMessage("Stop sync received: texture cleared");
            }

            // URL 変更の検知
            bool urlChanged = _currentVideoIdx != _syncedVideoIdx;
            if (urlChanged)
            {
                LogMessage($"Synced URL update detected: {_currentVideoIdx} -> {_syncedVideoIdx}");
                _currentVideoIdx = _syncedVideoIdx;

                // 実行中の Resync をキャンセル
                if (_localState >= STATE_REQUEST_PENDING && _localState <= STATE_SWITCHING)
                    CancelResync();

                if (HasSyncedUrl())
                {
                    switcher.ResetBothPlayersToA();
                    _activeIsA = true;
                    StartActivePlayback(_syncedURL);
                    LogMessage($"Playing synced URL: {_syncedURL}");
                }
                else
                {
                    CleanupPlaybackToIdle();
                    LogMessage("Empty URL sync received: playback remains stopped");
                }
            }

            // URL インデックスが同じでも _ownerPlaying のみ更新されることがあるため、
            // 各同期受信後に Playing 表示を同期状態へ追従させる。
            if (staffNotifyTarget != null)
                staffNotifyTarget.SendCustomEvent("OnUrlChanged");
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
            // settle 前の既定値を配信して運用中の同期状態を潰さない
            if (!Networking.IsNetworkSettled) return;
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

        /// <summary>ローカルユーザーの音量を即座に両プレイヤーに反映する。</summary>
        [PublicAPI]
        public void SetVolumeLocal(float volume)
        {
            SetVolume(volume);
            PlayerData.SetFloat(PLAYERDATA_VOLUME, Mathf.Clamp01(volume));
        }

        // =================================================================
        //  Silence Resync トグル
        // =================================================================

        /// <summary>Silence Resync の有効/無効を切り替える（UI トグル用）。</summary>
        [PublicAPI]
        public void SetAutoSilenceResyncEnabled(bool enabled)
        {
            _autoSilenceResyncEnabled = enabled;
        }

        [PublicAPI]
        public bool GetAutoSilenceResyncEnabled() { return _autoSilenceResyncEnabled; }

        /// <summary>自動 Resync / Reboot を抑制するローカル Manual Mode を切り替える。</summary>
        [PublicAPI]
        public void SetManualModeEnabled(bool enabled)
        {
            if (_manualModeEnabled == enabled) return;
            bool wasManualMode = GetManualModeEnabled();
            _manualModeEnabled = enabled;
            ApplyEffectiveManualModeChanged(wasManualMode, GetManualModeEnabled());
        }

        /// <summary>ローカル Mode とスタッフ強制値から実効 Manual Mode が変わったときの副作用を適用する。</summary>
        private void ApplyEffectiveManualModeChanged(bool wasManualMode, bool isManualMode)
        {
            if (wasManualMode == isManualMode) return;

            if (isManualMode)
            {
                _combinedSilenceDuration = 0f;
                int reason = resyncClient.GetRequestReason();
                bool automaticRequest = reason == AunCastResyncCoordinatorClient.REQUEST_REASON_FAILURE
                    || reason == AunCastResyncCoordinatorClient.REQUEST_REASON_SILENCE;
                if (automaticRequest
                    && _localState >= STATE_REQUEST_PENDING
                    && _localState < STATE_SWITCHING)
                {
                    CancelResync();
                    EnterActivePlaying(Time.time);
                    LogMessage("Automatic Resync canceled by Manual Mode");
                }
            }
            else if (_localState == STATE_RETRY_WAIT && !_awaitingActiveReboot)
            {
                _retryWaitUntil = Time.time;
            }
        }

        [PublicAPI]
        public bool GetManualModeEnabled()
        {
            int forceMode = GetForceMode();
            if (forceMode == FORCE_MODE_AUTO) return false;
            if (forceMode == FORCE_MODE_MANUAL) return true;
            return _manualModeEnabled;
        }

        /// <summary>スタッフの強制なしで使われる、観客ローカルの Mode 値を返す。</summary>
        [PublicAPI]
        public bool GetLocalManualModeEnabled() { return _manualModeEnabled; }

        /// <summary>現在のスタッフ強制 Mode を返す（None / Auto / Manual）。</summary>
        [PublicAPI]
        public int GetForceMode() { return NormalizeForceMode(_syncedForceMode); }

        /// <summary>スタッフが全観客の Mode を強制する。None に戻すと各観客のローカル値へ復帰する。</summary>
        [PublicAPI]
        public void SetForceModeAsStaff(int forceMode)
        {
            if (!Networking.IsNetworkSettled) return;

            forceMode = NormalizeForceMode(forceMode);
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            if (!Networking.IsOwner(gameObject)) return;
            if (_syncedForceMode == forceMode) return;

            _syncedForceMode = forceMode;
            ApplySyncedForceMode();
            QueueSerialize();
        }

        private int NormalizeForceMode(int forceMode)
        {
            if (forceMode == FORCE_MODE_AUTO || forceMode == FORCE_MODE_MANUAL)
                return forceMode;
            return FORCE_MODE_NONE;
        }

        /// <summary>同期された Force Mode を反映し、実効 Mode が変わった場合だけ UI へ通知する。</summary>
        private void ApplySyncedForceMode()
        {
            int forceMode = NormalizeForceMode(_syncedForceMode);
            if (_appliedForceMode == forceMode) return;

            bool wasManualMode = _appliedForceMode == FORCE_MODE_MANUAL
                || (_appliedForceMode == FORCE_MODE_NONE && _manualModeEnabled);
            _appliedForceMode = forceMode;
            bool isManualMode = GetManualModeEnabled();
            ApplyEffectiveManualModeChanged(wasManualMode, isManualMode);

            if (staffNotifyTarget != null)
                staffNotifyTarget.SendCustomEvent("OnForceModeChanged");
        }

        /// <summary>タイムラインログの現在値を返す。ローカル設定 UI 用。</summary>
        [PublicAPI]
        public bool GetTimelineLogging() { return _timelineLogging; }

        /// <summary>タイムラインログをローカルのみ設定する。</summary>
        [PublicAPI]
        public void SetTimelineLoggingLocal(bool value)
        {
            _timelineLogging = value;
            if (switcher != null) switcher.SetTimelineLoggingLocal(value);
            if (activeMonitor != null) activeMonitor.SetTimelineLoggingLocal(value);
            if (resyncClient != null) resyncClient.SetTimelineLoggingLocal(value);
        }

        /// <summary>現在の URL を送信したプレイヤー名を返す。配信停止時は空文字。</summary>
        [PublicAPI]
        public string GetUrlSubmitterName() { return _syncedUrlSubmitterName; }

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
        /// <summary>
        /// 現在の同期 URL を返す（ロード中を含む。未設定なら null）。
        /// スタッフが再生開始した時点で Playing 表示へ即時反映するため、
        /// _ownerPlaying（実再生開始）は要求しない。再生中判定には
        /// HasPlayableSyncedUrl 系（GetOwnerPlaying / CanRebootLocal）を使う。
        /// </summary>
        [PublicAPI] public VRCUrl GetCurrentURL() { return HasSyncedUrl() ? _syncedURL : null; }

        /// <summary>Next URL 欄の初期値に使うデフォルト URL を返す（未設定なら空 URL）。</summary>
        [PublicAPI] public VRCUrl GetDefaultUrl() { return defaultUrl; }
        [PublicAPI] public bool GetOwnerPlaying() { return _ownerPlaying; }
        [PublicAPI] public int GetConsecutiveFailCount() { return resyncClient.GetConsecutiveFailCount(); }
        [PublicAPI] public int GetConsecutiveStallCount() { return activeMonitor.GetConsecutiveStallCount(); }

        /// <summary>全 audible プレイヤーの累積無音時間（秒）。</summary>
        [PublicAPI] public float GetSilenceDuration()
        {
            return _combinedSilenceDuration;
        }

        /// <summary>無音判定に必要な連続秒数の設定値。</summary>
        [PublicAPI] public float GetSilenceThresholdSec()
        {
            AunCastSpeaker d = switcher != null ? switcher.GetActiveSilenceDetector() : null;
            return d != null ? d.GetSilenceConsecutiveSec() : 2f;
        }

        /// <summary>メーター表示用の現在 RMS（dBFS）。無音Resyncの有効/無効に関係なく取得する。</summary>
        [PublicAPI]
        public float GetActiveRmsDbfsForMeter()
        {
            AunCastSpeaker d = switcher != null ? switcher.GetActiveSilenceDetector() : null;
            if (d == null) return -96f;
            float dbfs = d.GetLastRmsDbfs();
            // GetOutputData は AudioSource.volume 適用後の出力を返すため、実適用ゲインで逆補正する (#10)
            AudioSource source = d.GetAudioSource();
            if (source != null)
            {
                float gain = source.volume;
                if (gain > 0.001f)
                    dbfs -= 20f * Mathf.Log10(gain);
            }
            return Mathf.Clamp(dbfs, -96f, 0f);
        }

        /// <summary>メーター表示用の無音判定閾値（dBFS）。</summary>
        [PublicAPI]
        public float GetActiveSilenceThresholdDbfsForMeter()
        {
            AunCastSpeaker d = switcher != null ? switcher.GetActiveSilenceDetector() : null;
            return d != null ? d.GetSilenceRmsThresholdDbfs() : -60f;
        }

        /// <summary>ドリフト累積がこの閾値を超えると Resync を発行する。</summary>
        [PublicAPI] public float GetDriftResyncThresholdSec()
        {
            return resyncClient != null ? resyncClient.GetDriftResyncThresholdSec() : 0.1f;
        }

        [PublicAPI]
        public bool IsDriftResyncEnabled()
        {
            return resyncClient == null || resyncClient.IsDriftResyncEnabled();
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

        /// <summary>自動再試行で改善し得る VideoError かを判定する。</summary>
        private bool IsRetryableVideoError(VideoError error)
        {
            return error != VideoError.InvalidURL && error != VideoError.AccessDenied;
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
                case STATE_RETRY_WAIT: return GetManualModeEnabled() ? "Manual Recovery Required" : "Retry Wait";
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

        private int ObservePlayerState(AunCastVideoPlayerManager mgr, bool loading)
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

        // =================================================================
        //  計測ログ (MEAS): 再生開始位置バラつきの定量化
        //  LoadURL → Ready → Start の所要時間・イベント時フレーム時間・再生位置を
        //  タイムラインログへ出力する。GOP 境界待ちやフレームヒッチとの相関分析に使う。
        //  接続試行・切替時にしか発火しない低頻度ログのため、_timelineLogging に
        //  かかわらず常時出力する。
        // =================================================================

        // LoadURL / Ready の発行時刻 (Time.realtimeSinceStartup)。0 = 未計測
        private float _measLoadAtA;
        private float _measLoadAtB;
        private float _measReadyAtA;
        private float _measReadyAtB;

        /// <summary>LoadURL 発行を記録する。以降の Ready/Start 計測の起点となる。</summary>
        private void MarkMeasLoad(int playerIndex)
        {
            float now = Time.realtimeSinceStartup;
            if (playerIndex == 0) { _measLoadAtA = now; _measReadyAtA = 0f; }
            else { _measLoadAtB = now; _measReadyAtB = 0f; }
            TL($"a=MEAS_LOAD p={playerIndex}");
        }

        /// <summary>OnVideoReady 時点の計測。load2ready = 接続確立〜デコード準備完了の所要秒。</summary>
        private void LogMeasReady(int playerIndex)
        {
            float now = Time.realtimeSinceStartup;
            float loadAt = playerIndex == 0 ? _measLoadAtA : _measLoadAtB;
            if (playerIndex == 0) _measReadyAtA = now; else _measReadyAtB = now;

            float load2Ready = loadAt > 0f ? now - loadAt : -1f;
            float time = GetMeasPlayerTime(playerIndex);
            float frameMs = Time.deltaTime * 1000f;
            TL($"a=MEAS_READY p={playerIndex} load2ready={load2Ready:F3} t={time:F3} dt={frameMs:F1}");
        }

        /// <summary>
        /// OnVideoStart 時点の計測。load2start の分布が GOP 長相当に広がるなら
        /// キーフレーム境界待ちが支配項、dt(フレーム時間) と相関するならヒッチ起因と切り分けられる。
        /// </summary>
        private void LogMeasStart(int playerIndex)
        {
            float now = Time.realtimeSinceStartup;
            float loadAt = playerIndex == 0 ? _measLoadAtA : _measLoadAtB;
            float readyAt = playerIndex == 0 ? _measReadyAtA : _measReadyAtB;

            float load2Start = loadAt > 0f ? now - loadAt : -1f;
            float ready2Start = readyAt > 0f ? now - readyAt : -1f;
            float time = GetMeasPlayerTime(playerIndex);
            float frameMs = Time.deltaTime * 1000f;
            TL($"a=MEAS_START p={playerIndex} load2start={load2Start:F3} ready2start={ready2Start:F3} t={time:F3} dt={frameMs:F1}");
        }

        /// <summary>切替前後の両プレイヤー再生位置を記録する。各接続の経過時間の突き合わせに使う。</summary>
        private void LogMeasSwitch(string action)
        {
            float timeA = GetMeasPlayerTime(0);
            float timeB = GetMeasPlayerTime(1);
            int activeIdx = _activeIsA ? 0 : 1;
            TL($"a={action} active={activeIdx} tA={timeA:F3} tB={timeB:F3}");
        }

        /// <summary>計測用の再生位置取得。未配線時は -1 を返す。</summary>
        private float GetMeasPlayerTime(int playerIndex)
        {
            AunCastVideoPlayerManager mgr = playerIndex == 0 ? playerManagerA : playerManagerB;
            return mgr != null ? mgr.GetTime() : -1f;
        }

    }
}
