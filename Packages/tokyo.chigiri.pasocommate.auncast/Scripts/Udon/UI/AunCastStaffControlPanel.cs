using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// スタッフ向けの操作・モニタリング UI（Design Section 9.2-D, 22.2）。
    /// ワールドをセットアップする人がワールド内の適切な場所に設置する。
    /// パスコードによる解錠 UI は別の AunCastWallControlPanel に分離されており、
    /// このパネル自体は解錠状態のローカルフラグのみを保持する。
    /// </summary>
    [DefaultExecutionOrder(10)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastStaffControlPanel : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private AunCastDualPlayerController controller;
        [SerializeField] private AunCastResyncCoordinator coordinator;
        [SerializeField] private AunCastPortablePanel viewerStatusPanel;

        [Header("Now Playing")]
        [SerializeField] private TMP_Text nowPlayingText;

        [Header("Next URL")]
        [SerializeField] private VRCUrlInputField nextUrlField;
        [SerializeField] private TMP_Text nextUrlFieldPlaceholderText;

        [Header("Action Buttons")]
        [SerializeField] private Button stopButton;
        [SerializeField] private Button globalResyncButton;
        [SerializeField] private Button forceRebootButton;
        [SerializeField] private Button promoteNextButton;
        [SerializeField] private float disabledButtonLabelAlpha = 0.5f;

        [Header("Help Text")]
        [SerializeField] private TMP_Text helpTextField;

        [Header("Monitoring Display")]
        [SerializeField] private TMP_Text indicatorText;
        [SerializeField] private TMP_Text userCountText;
        [Header("CDN Concurrent Limit")]
        [SerializeField] private TMP_Text concurrentLimitDisplayText;
        [SerializeField] private GameObject concurrentDisplayGroup;
        [SerializeField] private GameObject concurrentEditGroup;
        [SerializeField] private TMP_InputField concurrentLimitInput;

        private bool _concurrentEditMode;
        private int _concurrentEditOriginal;

        [Header("CDN Connection Limit")]
        [SerializeField] private TMP_Text connectionLimitDisplayText;
        [SerializeField] private GameObject connectionDisplayGroup;
        [SerializeField] private GameObject connectionEditGroup;
        [SerializeField] private TMP_InputField connectionLimitInput;

        private bool _connectionEditMode;
        private int _connectionEditOriginal;

        [Header("Drift Resync Threshold")]
        [SerializeField] private TMP_Text driftThresholdDisplayText;
        [SerializeField] private GameObject driftThresholdDisplayGroup;
        [SerializeField] private GameObject driftThresholdEditGroup;
        [SerializeField] private TMP_Text driftThresholdEditValueText;

        private bool _driftThresholdEditMode;
        private int _driftThresholdEditOriginal;
        private int _driftThresholdEditValue;

        private float _globalEtaBase;
        private float _globalEtaCapturedAt;
        private string _nowPlayingUrl;
        private string _nowPlayingSubmitter;
        private bool _nowPlayingHovered;

        [Header("Access Control")]
        [Tooltip("操作可能なユーザー名リスト（空の場合はパスコード解錠時のみ操作可能）")]
        [SerializeField] private string[] allowedUserNames;

        // OnPlayerJoined で allowedUserNames 該当、またはパスコード入力で true。ローカルのみ・同期なし。
        private bool _isStaff;
        private string[] _indicatorHexColors;
        // インジケーター描画のたびに配列を確保しないための使い回しバッファ（長さは MAX_PLAYERS 固定）。
        private int[] _indicatorSortKeys;

        // インジケーター色インデックス。値がソートキーを兼ね、小さいほど上位（異常度高）に表示される。
        // 赤(エラー) → 橙(接続中) → 黄(Resync中) → 青(待機) → 白(正常) の順。
        private const int INDICATOR_COLOR_FAILED = 0;
        private const int INDICATOR_COLOR_CONNECTING = 1;
        private const int INDICATOR_COLOR_RUNNING = 2;
        private const int INDICATOR_COLOR_QUEUED = 3;
        private const int INDICATOR_COLOR_NORMAL = 4;

        // ヘルプテキストのキー定数。各 UI 要素ごとにホバー時に表示する説明文を
        // _helpTextsEn / _helpTextsJa 配列のインデックスとして参照する。
        private const int HELP_NONE = -1;
        private const int HELP_STOP_BUTTON = 0;
        private const int HELP_RESYNC_BUTTON = 1;
        private const int HELP_REBOOT_BUTTON = 2;
        private const int HELP_NEXT_URL_FIELD = 3;
        private const int HELP_PROMOTE_BUTTON = 4;
        private const int HELP_CONCURRENT_MAX = 5;
        private const int HELP_CONNECTION_MAX = 6;
        private const int HELP_NOW_PLAYING = 7;
        private const int HELP_INDICATOR = 8;
        private const int HELP_USER_COUNT = 9;
        private const int HELP_VOLUME = 10;
        private const int HELP_VIEWER_RESYNC = 11;
        private const int HELP_VIEWER_REBOOT = 12;
        private const int HELP_HELP_AREA = 13;
        private const int HELP_STATE_TEXT = 14;
        private const int HELP_DRIFT_GAUGE = 15;
        private const int HELP_SILENCE_GAUGE = 16;
        private const int HELP_AUTO_RESYNC = 17;
        private const int HELP_CLOSE_BUTTON = 18;
        private const int HELP_SWITCH_VIEW = 19;
        private const int HELP_TIMELINE_LOGGING = 20;
        private const int HELP_MANUAL_MODE = 21;
        private const int HELP_DRIFT_THRESHOLD = 22;

        private int _activeHelpKey = HELP_NONE;
        private bool _isJapanese;
        private string[] _helpTextsEn;
        private string[] _helpTextsJa;

        // 再描画制御: 通常は AunCastResyncCoordinator / AunCastPlaybackMonitor からの通知で
        // 再描画し、連続通知を吸収するために描画直後の一定時間はデバウンスする。
        // 紫→黄 の色遷移は同期変数の変化を伴わない時刻依存なので、周期フォールバックも残す。
        /// <summary>OnCoordinatorChanged() で立てられ、次の Update で消費される再描画要求フラグ。</summary>
        private bool _redrawDirty;
        /// <summary>最後に実際に描画した unscaledTime。デバウンス判定と周期フォールバックの基準。</summary>
        private float _lastRepaintTime;
        private const float REPAINT_DEBOUNCE_SEC = 0.2f;
        private const float PERIODIC_TICK_SEC = 1.0f;

        private void OnEnable()
        {
            _indicatorHexColors = new[]
            {
                "#FF4444", // INDICATOR_COLOR_FAILED
                "#FF8833", // INDICATOR_COLOR_CONNECTING
                "#FFCC33", // INDICATOR_COLOR_RUNNING
                "#5599FF", // INDICATOR_COLOR_QUEUED
                "#DDDDDD", // INDICATOR_COLOR_NORMAL
            };
            _helpTextsEn = new[]
            {
                "Stop all players immediately",
                "Re-sync all players (no silent gap)",
                "Disconnect and reconnect all players (emergency, causes silent gap; use when Resync fails)",
                "Enter the next stream URL",
                "Swap Playing URL with Next URL and start playback",
                "Max simultaneous resyncs. Limits burst connections to the streaming server to reduce load",
                "Max simultaneous connections to the streaming server",
                "Currently playing stream URL. Hover to show who entered it",
                "Connection status (■=playing □=stopped / white=ok blue=queued yellow=resyncing orange=connecting red=error)",
                "streaming (+connecting) count / In Instance: current count / Queued: waiting resync count",
                "Adjust local playback volume",
                "Re-sync local stream (no silent gap)",
                "Disconnect and reconnect the local stream (emergency, causes silent gap; use when Resync fails)",
                "Hover over controls for help [言語切替はここをクリック]",
                "Current playback state and error messages",
                "Detected playback delay. Automatic resync triggers when it exceeds the threshold",
                "Current RMS level meter with silence threshold and peak hold",
                "automatically resync when silence is detected. Silence detection pauses for a while after a resync",
                "Close this panel",
                "Switch between local controls and staff controls",
                "Output structured timeline logs for playback and resync diagnosis (heavy load; keep off unless diagnosing)",
                "Manual Mode: stop automatic Resync and Reboot while keeping local and staff manual actions available",
                "Automatic Drift Resync threshold. OFF disables only drift-triggered automatic Resync",
            };
            _helpTextsJa = new[]
            {
                "全ユーザーの再生を即座に停止します",
                "全ユーザーのストリームを再同期します（無音区間が発生しません）",
                "全ユーザーのストリームを切断・再接続します（無音区間が発生します／Resyncで解決しない場合の緊急用）",
                "次に再生するストリームURLを入力します",
                "Playing URL と Next URL を入れ替えて再生を開始します",
                "同時Resync上限。配信サーバへの連続的な新規接続を制限し、負荷を軽減します",
                "配信サーバへの同時接続上限",
                "現在再生中のストリームURL。ホバー中はURL入力者名を表示します",
                "接続状態（■=再生中 □=停止 / 白=正常 青=待機 黄=Resync中 橙=接続中 赤=エラー）",
                "再生中(+新規接続中)の人数 / In Instance: 現在の人数 / Queued: Resync待ち人数",
                "ローカルの再生音量を調整します",
                "ローカルのストリームを再同期します（無音区間が発生しません）",
                "ローカルのストリームを切断・再接続します（無音区間が発生します／Resyncで解決しない場合の緊急用）",
                "コントロールにホバーでヘルプ表示 [Click here to toggle language]",
                "現在の再生状態とエラーメッセージ",
                "検出された再生遅延時間。しきい値を超えると自動Resyncが発動します",
                "現在のRMSレベルメーター（無音閾値線とピークホールド付き）",
                "無音を検出した際に自動でResyncします。Resync実行後はしばらく無音検知を停止します",
                "パネルを閉じます",
                "ローカル操作パネルとスタッフ操作パネルを切り替えます",
                "再生・Resync診断用の構造化タイムラインログを出力します（負荷が高いため、診断時以外はオフにしてください）",
                "Manual Mode: 自動Resyncと自動Rebootを停止し、観客・スタッフの明示操作だけを有効にします",
                "ドリフトによる自動Resyncの閾値です。OFFではドリフト起因の自動Resyncのみ停止します",
            };

            string lang = VRCPlayerApi.GetCurrentLanguage();
            _isJapanese = lang != null && lang.StartsWith("ja");

            _concurrentEditMode = false;
            UpdateConcurrentEditVisibility();
            _driftThresholdEditMode = false;
            UpdateDriftThresholdEditVisibility();
            SyncUIFromState();
            UpdateNowPlayingDisplay();
            PrefillNextUrlIfEmpty();
            UpdateActionButtonsInteractable();
            _redrawDirty = true;
            _lastRepaintTime = 0f;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!player.isLocal) return;

            if (!_isStaff && allowedUserNames != null)
            {
                string displayName = player.displayName;
                bool eligible = false;
                for (int i = 0; i < allowedUserNames.Length; i++)
                {
                    if (allowedUserNames[i] == displayName)
                    {
                        eligible = true;
                        break;
                    }
                }

                if (eligible)
                {
                    // SDK Build & Test での同名重複: 最小 playerId のみ許可
                    int playerCount = VRCPlayerApi.GetPlayerCount();
                    bool overridden = false;
                    if (playerCount > 1)
                    {
                        int localId = player.playerId;
                        VRCPlayerApi[] players = new VRCPlayerApi[playerCount];
                        VRCPlayerApi.GetPlayers(players);
                        for (int i = 0; i < players.Length; i++)
                        {
                            VRCPlayerApi p = players[i];
                            if (p == null || !Utilities.IsValid(p)) continue;
                            if (p.playerId == localId) continue;
                            if (p.displayName == displayName && p.playerId < localId)
                            {
                                overridden = true;
                                break;
                            }
                        }
                    }
                    if (!overridden)
                    {
                        _isStaff = true;
                        UpdateLockUI();
                        // 解錠状態は USP がキャッシュするため、許可ユーザー自動解錠でも push が必須。
                        if (viewerStatusPanel != null)
                            viewerStatusPanel.SetStaffUnlocked(_isStaff);
                    }
                }
            }

            UpdateActionButtonsInteractable();
        }

        /// <summary>
        /// AunCastResyncCoordinator / AunCastPlaybackMonitor が同期変数を書き換えた / 受信したときに呼ばれる。
        /// 次の Update で（デバウンス経過後に）再描画する。
        /// </summary>
        public void OnCoordinatorChanged()
        {
            SyncUIFromState();
            UpdateActionButtonsInteractable();
            _redrawDirty = true;
        }

        /// <summary>AunCastDualPlayerController から URL 変更（再生開始・停止・受信反映）を通知される。</summary>
        public void OnUrlChanged()
        {
            UpdateNowPlayingDisplay();
            ConsumeNextUrlIfPlaying();
            UpdateActionButtonsInteractable();
        }

        /// <summary>nextUrlField の onValueChanged から呼ばれる。Next URL の有無で Promote ボタンを切替。</summary>
        public void OnNextUrlChanged()
        {
            UpdateActionButtonsInteractable();
        }

        /// <summary>パネルを閉じる/切り替える前に入力欄の選択状態を解除する。</summary>
        public void ClearInputFocus()
        {
            if (nextUrlField == null) return;
            Selectable selectable = nextUrlField.GetComponent<Selectable>();
            if (selectable == null) return;

            bool wasInteractable = selectable.interactable;
            selectable.interactable = false;
            if (wasInteractable)
                selectable.interactable = true;
        }

        /// <summary>
        /// Next URL 欄が空かつ未再生のとき、controller のデフォルト URL を初期表示する（入力補助・同期なし）。
        /// VRCUrl はランタイムで文字列から生成できないため、値は controller.defaultUrl（エディタ転写で
        /// シリアライズ済み）から取得し、SetUrl で欄へ反映する（Promote スワップと同じ機構）。
        /// </summary>
        private void PrefillNextUrlIfEmpty()
        {
            if (nextUrlField == null || controller == null) return;
            VRCUrl current = nextUrlField.GetUrl();
            if (current != null && !string.IsNullOrEmpty(current.Get())) return;
            VRCUrl def = controller.GetDefaultUrl();
            if (def == null || string.IsNullOrEmpty(def.Get())) return;
            // 何か再生中なら初期表示しない（「次」の提案として意味を持たないため）
            VRCUrl playing = controller.GetCurrentURL();
            if (playing != null && !string.IsNullOrEmpty(playing.Get())) return;
            nextUrlField.SetUrl(def);
        }

        /// <summary>
        /// 再生中に Next URL 欄の陳腐化した内容をクリアする。対象は次の2つ:
        /// (1) 再生中 URL と一致する内容（再生開始で「次」から「再生中」へ移ったとみなす）、
        /// (2) デフォルト URL と一致する内容（prefill の初期表示は、何かが再生中なら
        ///     もう「次」の提案として意味を持たない）。
        /// 手動 Promote で再生が始まると、各クライアントの OnUrlChanged 経由で消費される。
        /// 手入力された別 URL はどちらにも一致しないため保持される。
        /// </summary>
        private void ConsumeNextUrlIfPlaying()
        {
            if (nextUrlField == null || controller == null) return;
            VRCUrl next = nextUrlField.GetUrl();
            if (next == null || string.IsNullOrEmpty(next.Get())) return;
            VRCUrl playing = controller.GetCurrentURL();
            if (playing == null || string.IsNullOrEmpty(playing.Get())) return;

            string nextText = next.Get();
            bool consumedByPlayback = nextText == playing.Get();
            VRCUrl def = controller.GetDefaultUrl();
            bool stalePrefill = def != null && nextText == def.Get();
            if (consumedByPlayback || stalePrefill)
                nextUrlField.SetUrl(VRCUrl.Empty);
        }

        /// <summary>
        /// イベント駆動の再描画をデバウンス付きで実行し、時刻依存の表示更新のために周期フォールバックも行う。
        /// デバウンスにより短時間に複数の同期通知が来ても描画は 1 回にまとめられる。
        /// </summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            float sinceLast = now - _lastRepaintTime;
            bool eventDue = _redrawDirty && sinceLast >= REPAINT_DEBOUNCE_SEC;
            bool periodicDue = sinceLast >= PERIODIC_TICK_SEC;

            if (eventDue || periodicDue)
            {
                _redrawDirty = false;
                _lastRepaintTime = now;
                SyncUIFromState();
                UpdateActionButtonsInteractable();
                UpdateMonitoringDisplay();
            }

        }

        /// <summary>AunCastWallControlPanel から正解入力時に呼ばれる。ローカルのみ解錠扱いにする。</summary>
        public void SetLocalPasscodeUnlocked()
        {
            _isStaff = true;
            UpdateLockUI();
            SyncUIFromState();
            UpdateActionButtonsInteractable();
            if (viewerStatusPanel != null)
                viewerStatusPanel.SetStaffUnlocked(_isStaff);
        }

        /// <summary>統合パネル側が切替ボタンの可視判定などに使う。パスコード解錠済みか allowedUserNames 該当で true。</summary>
        public bool IsLocallyUnlocked() { return _isStaff; }

        /// <summary>Next URL を再生し、再生前に表示されていた Playing URL を Next URL 欄へ戻す。</summary>
        public void OnPromoteNextUrl()
        {
            if (!CanUseStaffControls()) return;
            if (controller == null || nextUrlField == null) return;

            VRCUrl parsedUrl = nextUrlField.GetUrl();
            string parsedUrlText = parsedUrl.Get();
            if (string.IsNullOrEmpty(parsedUrlText)) return;

            // 無効 URL は Next URL 欄を消費せずそのまま残す（判定は Controller と共通）
            if (!controller.IsValidStreamUrl(parsedUrlText))
                return;

            VRCUrl previousUrl = null;
            string previousUrlText = "";
            if (!string.IsNullOrEmpty(_nowPlayingUrl))
            {
                previousUrl = controller.GetCurrentURL();
                previousUrlText = previousUrl != null ? previousUrl.Get() : "";
            }
            controller.PlayVideoAsStaff(parsedUrl);
            nextUrlField.SetUrl(string.IsNullOrEmpty(previousUrlText) ? VRCUrl.Empty : previousUrl);
        }

        /// <summary>全ユーザーの再生を即座に停止する。</summary>
        public void OnStopButtonPress()
        {
            if (!CanUseStaffControls()) return;
            if (controller == null) return;
            if (!HasCurrentStream()) return;

            controller.StopVideoAsStaff();
        }

        /// <summary>全ユーザーの一斉 Resync をキューに投入する（手動トリガー）。</summary>
        public void OnGlobalResyncButtonPress()
        {
            if (!CanUseStaffControls()) return;
            if (!HasCurrentStream()) return;

            coordinator.TriggerGlobalResync();
        }

        /// <summary>全ユーザーの Active・Standby 両方を切断し Active で再接続する（緊急リブート）。</summary>
        public void OnForceRebootButtonPress()
        {
            if (!CanUseStaffControls()) return;
            if (!HasCurrentStream()) return;

            coordinator.TriggerGlobalForceReboot();
        }

        /// <summary>Concurrent Max の Change ボタン — 編集モードに入る。</summary>
        public void OnConcurrentLimitChangeButton()
        {
            if (!CanUseStaffControls()) return;

            _concurrentEditOriginal = coordinator.GetMaxConcurrentResyncUsers();
            _concurrentEditMode = true;
            if (concurrentLimitInput != null)
                concurrentLimitInput.text = _concurrentEditOriginal.ToString();
            UpdateConcurrentEditVisibility();
        }

        /// <summary>Concurrent Max の Apply ボタン — 編集中の値を確定する。</summary>
        public void OnConcurrentLimitApply()
        {
            if (!CanUseStaffControls()) return;

            if (concurrentLimitInput != null)
            {
                int value;
                if (int.TryParse(concurrentLimitInput.text, out value) && value > 0)
                {
                    int clamped = Mathf.Clamp(value, 1, 82);
                    coordinator.SetMaxConcurrentResyncUsersRuntime(clamped);
                }
            }
            _concurrentEditMode = false;
            SyncUIFromState();
            UpdateConcurrentEditVisibility();
        }

        /// <summary>Concurrent Max の Cancel ボタン — 元の値に戻す。</summary>
        public void OnConcurrentLimitCancel()
        {
            _concurrentEditMode = false;
            SyncUIFromState();
            UpdateConcurrentEditVisibility();
        }

        /// <summary>CDN 同時接続数上限を入力欄から変更する（編集モード中）。</summary>
        public void OnConcurrentLimitChanged()
        {
            if (!CanUseStaffControls()) return;
            if (coordinator == null || concurrentLimitInput == null) return;

            int value;
            if (int.TryParse(concurrentLimitInput.text, out value) && value > 0)
            {
                int clamped = Mathf.Clamp(value, 1, 82);
                concurrentLimitInput.text = clamped.ToString();
            }
        }

        public void OnConcurrentLimitAdd1() { AdjustConcurrentLimit(1); }
        public void OnConcurrentLimitSub1() { AdjustConcurrentLimit(-1); }
        public void OnConcurrentLimitAdd10() { AdjustConcurrentLimit(10); }
        public void OnConcurrentLimitSub10() { AdjustConcurrentLimit(-10); }

        /// <summary>編集モード中の同時 Resync 数上限を delta だけ増減する (+/- ボタン用)。</summary>
        private void AdjustConcurrentLimit(int delta)
        {
            if (!CanUseStaffControls()) return;
            if (coordinator == null || concurrentLimitInput == null) return;

            int current;
            if (!int.TryParse(concurrentLimitInput.text, out current))
                current = coordinator.GetMaxConcurrentResyncUsers();
            int next = Mathf.Clamp(current + delta, 1, 82);
            concurrentLimitInput.text = next.ToString();
        }

        private void UpdateConcurrentEditVisibility()
        {
            if (concurrentDisplayGroup != null)
                concurrentDisplayGroup.SetActive(!_concurrentEditMode);
            if (concurrentEditGroup != null)
                concurrentEditGroup.SetActive(_concurrentEditMode);
        }

        // =================================================================
        //  Connection Max 編集
        // =================================================================

        public void OnConnectionLimitChangeButton()
        {
            if (!CanUseStaffControls()) return;

            _connectionEditOriginal = coordinator.GetMaxConnectionLimit();
            _connectionEditMode = true;
            if (connectionLimitInput != null)
                connectionLimitInput.text = _connectionEditOriginal.ToString();
            UpdateConnectionEditVisibility();
        }

        public void OnConnectionLimitApply()
        {
            if (!CanUseStaffControls()) return;

            if (connectionLimitInput != null)
            {
                int value;
                if (int.TryParse(connectionLimitInput.text, out value))
                {
                    int clamped = Mathf.Clamp(value,
                        coordinator.GetMinConnectionLimit(),
                        coordinator.GetMaxConnectionLimitCap());
                    coordinator.SetMaxConnectionLimitRuntime(clamped);
                }
            }
            _connectionEditMode = false;
            SyncUIFromState();
            UpdateConnectionEditVisibility();
        }

        public void OnConnectionLimitCancel()
        {
            _connectionEditMode = false;
            SyncUIFromState();
            UpdateConnectionEditVisibility();
        }

        public void OnConnectionLimitChanged()
        {
            if (!CanUseStaffControls()) return;
            if (coordinator == null || connectionLimitInput == null) return;

            int value;
            if (int.TryParse(connectionLimitInput.text, out value))
            {
                int clamped = Mathf.Clamp(value,
                    coordinator.GetMinConnectionLimit(),
                    coordinator.GetMaxConnectionLimitCap());
                connectionLimitInput.text = clamped.ToString();
            }
        }

        public void OnConnectionLimitAdd1() { AdjustConnectionLimit(1); }
        public void OnConnectionLimitSub1() { AdjustConnectionLimit(-1); }
        public void OnConnectionLimitAdd10() { AdjustConnectionLimit(10); }
        public void OnConnectionLimitSub10() { AdjustConnectionLimit(-10); }

        /// <summary>編集モード中の同時接続数上限を delta だけ増減する (+/- ボタン用)。</summary>
        private void AdjustConnectionLimit(int delta)
        {
            if (!CanUseStaffControls()) return;
            if (coordinator == null || connectionLimitInput == null) return;

            int current;
            if (!int.TryParse(connectionLimitInput.text, out current))
                current = coordinator.GetMaxConnectionLimit();
            int next = Mathf.Clamp(current + delta,
                coordinator.GetMinConnectionLimit(),
                coordinator.GetMaxConnectionLimitCap());
            connectionLimitInput.text = next.ToString();
        }

        private void UpdateConnectionEditVisibility()
        {
            if (connectionDisplayGroup != null)
                connectionDisplayGroup.SetActive(!_connectionEditMode);
            if (connectionEditGroup != null)
                connectionEditGroup.SetActive(_connectionEditMode);
        }

        // =================================================================
        //  Drift Resync 閾値編集
        // =================================================================

        public void OnDriftThresholdChangeButton()
        {
            if (!CanUseStaffControls()) return;

            _driftThresholdEditOriginal = coordinator.GetDriftResyncThresholdIndex();
            _driftThresholdEditValue = _driftThresholdEditOriginal;
            _driftThresholdEditMode = true;
            UpdateDriftThresholdEditValueText();
            UpdateDriftThresholdEditVisibility();
        }

        public void OnDriftThresholdApply()
        {
            if (!CanUseStaffControls()) return;

            coordinator.SetDriftResyncThresholdIndexRuntime(_driftThresholdEditValue);
            _driftThresholdEditMode = false;
            SyncUIFromState();
            UpdateDriftThresholdEditVisibility();
        }

        public void OnDriftThresholdCancel()
        {
            _driftThresholdEditValue = _driftThresholdEditOriginal;
            _driftThresholdEditMode = false;
            SyncUIFromState();
            UpdateDriftThresholdEditVisibility();
        }

        public void OnDriftThresholdPrevious()
        {
            AdjustDriftThreshold(-1);
        }

        public void OnDriftThresholdNext()
        {
            AdjustDriftThreshold(1);
        }

        private void AdjustDriftThreshold(int delta)
        {
            if (!CanUseStaffControls() || !_driftThresholdEditMode) return;
            _driftThresholdEditValue = Mathf.Clamp(
                _driftThresholdEditValue + delta,
                AunCastResyncCoordinator.DRIFT_THRESHOLD_50_MS,
                AunCastResyncCoordinator.DRIFT_THRESHOLD_OFF);
            UpdateDriftThresholdEditValueText();
        }

        private void UpdateDriftThresholdEditValueText()
        {
            if (driftThresholdEditValueText != null)
                driftThresholdEditValueText.text = GetDriftThresholdDisplayText(_driftThresholdEditValue);
        }

        private string GetDriftThresholdDisplayText(int index)
        {
            switch (index)
            {
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_50_MS: return "50 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_100_MS: return "100 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_150_MS: return "150 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_200_MS: return "200 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_250_MS: return "250 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_300_MS: return "300 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_400_MS: return "400 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_500_MS: return "500 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_700_MS: return "700 ms";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_1_SEC: return "1 s";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_2_SEC: return "2 s";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_3_SEC: return "3 s";
                case AunCastResyncCoordinator.DRIFT_THRESHOLD_5_SEC: return "5 s";
                default: return "OFF";
            }
        }

        private void UpdateDriftThresholdEditVisibility()
        {
            if (driftThresholdDisplayGroup != null)
                driftThresholdDisplayGroup.SetActive(!_driftThresholdEditMode);
            if (driftThresholdEditGroup != null)
                driftThresholdEditGroup.SetActive(_driftThresholdEditMode);
        }

        // =================================================================

        /// <summary>
        /// coordinator が保持する現在値を UI テキストフィールドに一括反映する。
        /// 編集確定/キャンセル後や解錠時に呼び、表示と実値の一貫性を保証する。
        /// </summary>
        private void SyncUIFromState()
        {
            if (!HasSynchronizedCoordinatorState()) return;
            string concurrentVal = coordinator.GetMaxConcurrentResyncUsers().ToString();
            if (concurrentLimitDisplayText != null)
                concurrentLimitDisplayText.text = concurrentVal;
            if (concurrentLimitInput != null && !_concurrentEditMode)
                concurrentLimitInput.text = concurrentVal;

            int connLimit = coordinator.GetMaxConnectionLimit();
            string connectionVal = connLimit.ToString();
            if (connectionLimitDisplayText != null)
                connectionLimitDisplayText.text = connectionVal;
            if (connectionLimitInput != null && !_connectionEditMode)
                connectionLimitInput.text = connLimit.ToString();

            string driftThreshold = coordinator.GetDriftResyncThresholdDisplayText();
            if (driftThresholdDisplayText != null)
                driftThresholdDisplayText.text = driftThreshold;
            if (!_driftThresholdEditMode)
            {
                _driftThresholdEditValue = coordinator.GetDriftResyncThresholdIndex();
                UpdateDriftThresholdEditValueText();
            }
        }

        private void UpdateNowPlayingDisplay()
        {
            if (controller == null) return;
            VRCUrl current = controller.GetCurrentURL();
            string url = current != null ? current.Get() : null;
            if (string.IsNullOrEmpty(url))
            {
                _nowPlayingUrl = "";
                _nowPlayingSubmitter = "";
                ApplyNowPlayingDisplay();
                return;
            }
            string submitter = controller.GetUrlSubmitterName();
            _nowPlayingUrl = url;
            _nowPlayingSubmitter = submitter;
            ApplyNowPlayingDisplay();
        }

        private void ApplyNowPlayingDisplay()
        {
            if (string.IsNullOrEmpty(_nowPlayingUrl))
            {
                if (nowPlayingText != null)
                    nowPlayingText.text = "No stream";
                return;
            }

            if (nowPlayingText == null) return;
            if (_nowPlayingHovered && !string.IsNullOrEmpty(_nowPlayingSubmitter))
                nowPlayingText.text = $"by {EscapeRichText(_nowPlayingSubmitter)}";
            else
                nowPlayingText.text = EscapeRichText(_nowPlayingUrl);
        }

        private string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return $"<noparse>{value}</noparse>";
        }

        /// <summary>
        /// アクションボタンの有効/無効を現在のストリーム状態・Next URL の有無・スタッフロック状態の AND で更新する。
        /// AunCastPortablePanel のスタッフロック切替時にも呼ばれ、ロック中は全ボタンが無効化される。
        /// </summary>
        public void UpdateActionButtonsInteractable()
        {
            bool staffInteractable = viewerStatusPanel == null || viewerStatusPanel.IsStaffInteractable();
            bool baseEnabled = CanUseStaffControls() && staffInteractable;
            bool hasStream = HasCurrentStream();
            SetButtonInteractable(stopButton, baseEnabled && hasStream);
            SetButtonInteractable(globalResyncButton, baseEnabled && hasStream);
            SetButtonInteractable(forceRebootButton, baseEnabled && hasStream);

            bool hasNextUrl = false;
            if (nextUrlField != null)
            {
                VRCUrl next = nextUrlField.GetUrl();
                hasNextUrl = next != null && !string.IsNullOrEmpty(next.Get());
            }
            SetButtonInteractable(promoteNextButton, baseEnabled && hasNextUrl);
        }

        private bool HasSynchronizedCoordinatorState()
        {
            return coordinator != null && coordinator.IsInitialStateReady();
        }

        private bool CanUseStaffControls()
        {
            return _isStaff && HasSynchronizedCoordinatorState();
        }

        private bool HasCurrentStream()
        {
            if (!string.IsNullOrEmpty(_nowPlayingUrl)) return true;
            if (controller == null) return false;

            VRCUrl current = controller.GetCurrentURL();
            return current != null && !string.IsNullOrEmpty(current.Get());
        }

        private void SetButtonInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;
            float alpha = interactable ? 1f : disabledButtonLabelAlpha;
            var cg = button.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = alpha;
                cg.interactable = interactable;
                cg.blocksRaycasts = interactable;
                return;
            }
            var labels = button.GetComponentsInChildren<TMP_Text>(true);
            if (labels == null) return;
            foreach (var label in labels)
            {
                if (label == null) continue;
                var c = label.color;
                c.a = alpha;
                label.color = c;
            }
        }

        private void UpdateMonitoringDisplay()
        {
            if (coordinator == null) return;

            int playing = coordinator.GetPlayingEstimateCount();
            int connecting = coordinator.GetConnectingEstimateCount();
            UpdateIndicatorDisplay();
            UpdateUserCountDisplay(playing, connecting);
        }

        /// <summary>
        /// 再生中(+接続中)・インスタンス人数・収容上限の 3 指標を縦並びテキストで表示する。
        /// スタッフが配信の到達率とインスタンス収容状況を一目で把握するためのサマリ表示。
        /// 待機列がある場合は全体 ETA も追加表示する。
        /// </summary>
        private void UpdateUserCountDisplay(int playing, int connecting)
        {
            if (userCountText == null) return;
            string connectingSuffix = connecting > 0 ? $"+{connecting}" : "";
            int inInstance = VRCPlayerApi.GetPlayerCount();
            float now = Time.unscaledTime;
            float rawEta = coordinator != null ? coordinator.EstimateGlobalWaitTime() : 0f;
            if (rawEta > 0f)
            {
                if (rawEta > _globalEtaBase || _globalEtaBase <= 0f)
                {
                    _globalEtaBase = rawEta;
                    _globalEtaCapturedAt = now;
                }
            }
            else
            {
                _globalEtaBase = 0f;
            }
            float displayEta = _globalEtaBase > 0f
                ? Mathf.Max(0f, _globalEtaBase - (now - _globalEtaCapturedAt))
                : 0f;
            int queued = coordinator.GetQueuedCount();
            string etaSuffix = displayEta > 0f ? $" {displayEta:F0}s" : "";
            string thirdRow = $"Queued\n<size=28>{queued}</size>{etaSuffix}";
            userCountText.text =
                $"Playing\n<size=28>{playing}</size>{connectingSuffix}\n\nIn Instance\n<size=28>{inInstance}</size>\n\n{thirdRow}";
        }

        /// <summary>
        /// 接続状態インジケーターのリッチテキストを構築する。
        /// 表示色は Resync 状態、接続中、エラー、正常の順で決定する。
        /// そのうえで再生中/停止 × 最終色（赤/橙/黄/青/白）でソートし、スタッフが問題を即座に視認できるようにする。
        /// </summary>
        private void UpdateIndicatorDisplay()
        {
            if (indicatorText == null || coordinator == null) return;

            int coordSlots = coordinator.GetMaxPlayers();
            AunCastPlaybackMonitor pbm = coordinator.GetPlaybackMonitor();

            // ソートキー: スタイル（Playing=0, 停止=1）× 色（赤=0, 橙=1, 黄=2, 青=3, 白=4）
            int assigned = 0;
            if (_indicatorSortKeys == null || _indicatorSortKeys.Length != coordSlots)
                _indicatorSortKeys = new int[coordSlots];
            int[] sortKeys = _indicatorSortKeys;
            for (int i = 0; i < coordSlots; i++)
            {
                int playerId = coordinator.GetUserPlayerId(i);
                if (playerId == 0)
                {
                    sortKeys[i] = 999;
                    continue;
                }
                assigned++;

                bool playing = pbm != null && pbm.GetPlaybackActive(i) != 0;
                bool connecting = pbm != null && pbm.GetConnectingActive(i) != 0;
                bool error = pbm != null && pbm.GetErrorActive(i) != 0;
                int state = coordinator.GetResyncState(i);

                int colorOrder;
                if (state == AunCastResyncCoordinator.STATE_QUEUED)
                    colorOrder = INDICATOR_COLOR_QUEUED;
                else if (state == AunCastResyncCoordinator.STATE_GRANTED || state == AunCastResyncCoordinator.STATE_RUNNING)
                    colorOrder = INDICATOR_COLOR_RUNNING;
                else if (connecting)
                    colorOrder = INDICATOR_COLOR_CONNECTING;
                else if (error)
                    colorOrder = INDICATOR_COLOR_FAILED;
                else
                    colorOrder = INDICATOR_COLOR_NORMAL;

                int styleOrder = playing ? 0 : 1;
                sortKeys[i] = styleOrder * 10 + colorOrder;
            }

            int displaySlots = assigned;

            // 割当済みスロットのキーを先頭に詰め、その範囲だけ挿入ソート
            int writeIdx = 0;
            for (int i = 0; i < coordSlots; i++)
            {
                if (sortKeys[i] != 999)
                    sortKeys[writeIdx++] = sortKeys[i];
            }
            for (int i = 1; i < writeIdx; i++)
            {
                int keyI = sortKeys[i];
                int j = i - 1;
                while (j >= 0 && sortKeys[j] > keyI)
                {
                    sortKeys[j + 1] = sortKeys[j];
                    j--;
                }
                sortKeys[j + 1] = keyI;
            }

            if (displaySlots <= 0)
            {
                indicatorText.text = "";
                return;
            }

            // リッチテキスト組み立て（10 個ごとに改行）
            string result = "";
            for (int i = 0; i < displaySlots; i++)
            {
                if (i > 0 && i % 10 == 0) result += "\n";

                int key = sortKeys[i];
                bool playing = key < 10;
                int colorIdx = key % 10;

                string hex = _indicatorHexColors[colorIdx];

                string ch = playing ? "■" : "□";
                result += $"<color={hex}>{ch}</color>";
            }

            indicatorText.text = result;
        }

        /// <summary>
        /// スタッフ権限の有無に応じてロック依存の UI 要素を更新する。
        /// 未解錠時はヘルプテキストを非表示にし、URL 入力欄のプレースホルダーを空にして
        /// 操作不可であることを視覚的に示す。
        /// </summary>
        private void UpdateLockUI()
        {
            if (controller == null) return;

            if (helpTextField != null)
            {
                if (!_isStaff)
                {
                    helpTextField.text = string.Empty;
                    _activeHelpKey = HELP_NONE;
                }
                else if (_activeHelpKey == HELP_NONE)
                {
                    helpTextField.text = string.Empty;
                }
            }

            if (nextUrlFieldPlaceholderText != null)
                nextUrlFieldPlaceholderText.text = _isStaff ? "Next URL..." : string.Empty;
        }

        // =================================================================
        //  ヘルプテキスト（ホバー検出）
        // =================================================================

        /// <summary>
        /// 指定キーに対応するローカライズ済みヘルプ文字列をヘルプ欄に表示する。
        /// ユーザーの言語設定 (_isJapanese) に応じて日英を自動切替する。
        /// </summary>
        private void SetHelpText(int helpKey)
        {
            _activeHelpKey = helpKey;
            if (helpTextField == null || _helpTextsEn == null) return;
            if (helpKey < 0 || helpKey >= _helpTextsEn.Length)
            {
                helpTextField.text = string.Empty;
                return;
            }
            helpTextField.text = _isJapanese ? _helpTextsJa[helpKey] : _helpTextsEn[helpKey];
        }

        public override void OnLanguageChanged(string language)
        {
            if (_languageOverride) return;
            _isJapanese = language != null && language.StartsWith("ja");
            if (_activeHelpKey >= 0) SetHelpText(_activeHelpKey);
        }

        private bool _languageOverride;

        /// <summary>日本語・英語を手動でトグルする。以降は VRChat の言語変更を無視する。</summary>
        public void ToggleLanguage()
        {
            _languageOverride = true;
            _isJapanese = !_isJapanese;
            if (_activeHelpKey >= 0) SetHelpText(_activeHelpKey);
        }

        public void OnHoverStopButton() { SetHelpText(HELP_STOP_BUTTON); }
        public void OnHoverResyncButton() { SetHelpText(HELP_RESYNC_BUTTON); }
        public void OnHoverRebootButton() { SetHelpText(HELP_REBOOT_BUTTON); }
        public void OnHoverNextUrlField() { SetHelpText(HELP_NEXT_URL_FIELD); }
        public void OnHoverPromoteButton() { SetHelpText(HELP_PROMOTE_BUTTON); }
        public void OnHoverConcurrentMax() { SetHelpText(HELP_CONCURRENT_MAX); }
        public void OnHoverConnectionMax() { SetHelpText(HELP_CONNECTION_MAX); }
        public void OnHoverNowPlaying()
        {
            _nowPlayingHovered = true;
            ApplyNowPlayingDisplay();
            SetHelpText(HELP_NOW_PLAYING);
        }
        public void OnHoverIndicator() { SetHelpText(HELP_INDICATOR); }
        public void OnHoverUserCount() { SetHelpText(HELP_USER_COUNT); }
        public void OnHoverVolume() { SetHelpText(HELP_VOLUME); }
        public void OnHoverViewerResync() { SetHelpText(HELP_VIEWER_RESYNC); }
        public void OnHoverViewerReboot() { SetHelpText(HELP_VIEWER_REBOOT); }
        public void OnHoverHelpArea() { SetHelpText(HELP_HELP_AREA); }
        public void OnHoverStateText() { SetHelpText(HELP_STATE_TEXT); }
        public void OnHoverDriftGauge() { SetHelpText(HELP_DRIFT_GAUGE); }
        public void OnHoverSilenceGauge() { SetHelpText(HELP_SILENCE_GAUGE); }
        public void OnHoverAutoResync() { SetHelpText(HELP_AUTO_RESYNC); }
        public void OnHoverCloseButton() { SetHelpText(HELP_CLOSE_BUTTON); }
        public void OnHoverSwitchView() { SetHelpText(HELP_SWITCH_VIEW); }
        public void OnHoverTimelineLogging() { SetHelpText(HELP_TIMELINE_LOGGING); }
        public void OnHoverManualMode() { SetHelpText(HELP_MANUAL_MODE); }
        public void OnHoverDriftThreshold() { SetHelpText(HELP_DRIFT_THRESHOLD); }
        public void OnHoverClear()
        {
            _nowPlayingHovered = false;
            ApplyNowPlayingDisplay();
            SetHelpText(HELP_NONE);
        }

    }
}
