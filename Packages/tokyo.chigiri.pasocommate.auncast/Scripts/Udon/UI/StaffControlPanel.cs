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
    /// パスコードによる解錠 UI は別の WallControlPanel に分離されており、
    /// このパネル自体は解錠状態のローカルフラグのみを保持する。
    /// </summary>
    [DefaultExecutionOrder(10)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class StaffControlPanel : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private LocalDualPlayerController controller;
        [SerializeField] private ResyncCoordinator coordinator;
        [SerializeField] private UserStatusPanel viewerStatusPanel;

        [Header("Now Playing")]
        [SerializeField] private TMP_Text nowPlayingText;

        [Header("Next URL")]
        [SerializeField] private VRCUrlInputField nextUrlField;
        [SerializeField] private TMP_Text nextUrlFieldPlaceholderText;

        [Header("Help Text")]
        [SerializeField] private TMP_Text helpTextField;

        [Header("Monitoring Display")]
        [SerializeField] private TMP_Text indicatorText;
        [SerializeField] private TMP_Text userCountText;
        [Tooltip("インスタンスのユーザー数上限（0 = 自動）")]
        [SerializeField] private int instanceCapacity;

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

        private float _globalEtaBase;
        private float _globalEtaCapturedAt;

        [Header("Access Control")]
        [Tooltip("操作可能なユーザー名リスト（空の場合はパスコード解錠時のみ操作可能）")]
        [SerializeField] private string[] allowedUserNames;

        // WallControlPanel から呼ばれてローカルのみ true になる。同期なし。
        private bool _passcodeUnlocked;
        private int _indicatorMaxSlots;
        private string[] _indicatorHexColors;

        // インジケーター色インデックス。値がソートキーを兼ね、小さいほど上位（異常度高）に表示される。
        // 赤(エラー) → 青(待機) → 黄(Resync中) → 橙(接続中) → 白(正常) の順。
        private const int INDICATOR_COLOR_FAILED = 0;
        private const int INDICATOR_COLOR_QUEUED = 1;
        private const int INDICATOR_COLOR_RUNNING = 2;
        private const int INDICATOR_COLOR_CONNECTING = 3;
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

        private int _activeHelpKey = HELP_NONE;
        private bool _isJapanese;
        private string[] _helpTextsEn;
        private string[] _helpTextsJa;

        // 再描画制御: 通常は ResyncCoordinator / PlaybackMonitor からの通知で
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
            _indicatorMaxSlots = instanceCapacity;
            _indicatorHexColors = new[]
            {
                "#FF4444", // INDICATOR_COLOR_FAILED
                "#5599FF", // INDICATOR_COLOR_QUEUED
                "#FFCC33", // INDICATOR_COLOR_RUNNING
                "#FF8833", // INDICATOR_COLOR_CONNECTING
                "#DDDDDD", // INDICATOR_COLOR_NORMAL
            };
            _helpTextsEn = new[]
            {
                "Stop all players immediately",
                "Re-sync all players (no silent gap)",
                "Reboot all players (emergency, causes silent gap; use when Resync fails)",
                "Enter the next stream URL",
                "Start playback of the entered URL for all",
                "Max simultaneous resyncs. Limits burst connections to the streaming server to reduce load",
                "Max simultaneous connections to the streaming server (0: unlimited)",
                "Currently playing stream URL",
                "Connection status (■=playing □=stopped / white=ok blue=queued yellow=resyncing orange=connecting red=error)",
                "Playing: streaming (+connecting) count / In Instance: current count / Capacity: max capacity",
                "Adjust local playback volume",
                "Re-sync local stream (no silent gap)",
                "Reboot local stream (emergency, causes silent gap; use when Resync fails)",
                "Hover over controls for help [言語切替はここをクリック]",
                "Current playback state and error messages",
                "Detected playback delay. Auto Resync triggers when it exceeds the threshold",
                "Current RMS level meter with silence threshold and peak hold",
                "Automatically resync when playback delay or silence is detected",
                "Close this panel",
                "Switch between local controls and staff controls",
            };
            _helpTextsJa = new[]
            {
                "全ユーザーの再生を即座に停止します",
                "全ユーザーのストリームを再同期します（無音区間が発生しません）",
                "全ユーザーのストリームをリブートします（無音区間が発生します／Resyncで解決しない場合の緊急用）",
                "次に再生するストリームURLを入力します",
                "入力したURLの再生を全ユーザーに対して開始します",
                "同時Resync数の上限。配信サーバへの連続的な新規接続を制限し、負荷を軽減します",
                "配信サーバへの同時接続数の上限（0: 無制限）",
                "現在再生中のストリームURL",
                "接続状態（■=再生中 □=停止 / 白=正常 青=待機 黄=Resync中 橙=接続中 赤=エラー）",
                "Playing: 再生中(+新規接続中)の人数 / In Instance: 現在の人数 / Capacity: 収容上限",
                "ローカルの再生音量を調整します",
                "ローカルのストリームを再同期します（無音区間が発生しません）",
                "ローカルのストリームをリブートします（無音区間が発生します／Resyncで解決しない場合の緊急用）",
                "コントロールにホバーでヘルプ表示 [Click here to toggle language]",
                "現在の再生状態とエラーメッセージ",
                "検出された再生遅延時間。しきい値を超えるとAuto Resyncが発動します",
                "現在のRMSレベルメーター（無音閾値線とピークホールド付き）",
                "再生遅延や無音を検出した際に自動でResyncします",
                "パネルを閉じます",
                "ローカル操作パネルとスタッフ操作パネルを切り替えます",
            };

            string lang = VRCPlayerApi.GetCurrentLanguage();
            _isJapanese = lang != null && lang.StartsWith("ja");

            _concurrentEditMode = false;
            UpdateConcurrentEditVisibility();
            UpdateLockUI();
            SyncUIFromState();
            _redrawDirty = true;
            _lastRepaintTime = 0f;
        }

        /// <summary>
        /// ResyncCoordinator / PlaybackMonitor が同期変数を書き換えた / 受信したときに呼ばれる。
        /// 次の Update で（デバウンス経過後に）再描画する。
        /// </summary>
        public void OnCoordinatorChanged()
        {
            _redrawDirty = true;
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
                UpdateMonitoringDisplay();
                if (periodicDue) UpdateLockUI();
            }

        }

        /// <summary>WallControlPanel から正解入力時に呼ばれる。ローカルのみ解錠扱いにする。</summary>
        public void SetLocalPasscodeUnlocked()
        {
            _passcodeUnlocked = true;
            UpdateLockUI();
            SyncUIFromState();
            if (viewerStatusPanel != null)
                viewerStatusPanel.OnStaffUnlockStateChanged();
        }

        /// <summary>統合パネル側が切替ボタンの可視判定などに使う。ローカル解錠状態を返す。</summary>
        public bool IsLocallyUnlocked() { return _passcodeUnlocked; }

        /// <summary>Next URL 欄の URL を昇格させて再生を開始する。</summary>
        public void OnPromoteNextUrl()
        {
            if (!IsStaff())
            {
                return;
            }
            if (controller == null || nextUrlField == null) return;

            VRCUrl parsedUrl = nextUrlField.GetUrl();
            string parsedUrlText = parsedUrl.Get();
            if (string.IsNullOrEmpty(parsedUrlText)) return;

            int schemeIndex = parsedUrlText.IndexOf("://", System.StringComparison.Ordinal);
            if (schemeIndex < 1 || schemeIndex > 8 || parsedUrlText.Length > 4096)
                return;

            controller.PlayVideoAsStaff(parsedUrl);
            nextUrlField.SetUrl(VRCUrl.Empty);
        }

        /// <summary>全ユーザーの再生を即座に停止する。</summary>
        public void OnStopButtonPress()
        {
            if (!IsStaff())
            {
                return;
            }
            if (controller == null) return;

            controller.StopVideoAsStaff();
        }

        /// <summary>全ユーザーの一斉 Resync をキューに投入する（手動トリガー）。</summary>
        public void OnGlobalResyncButtonPress()
        {
            if (!IsStaff())
            {
                return;
            }
            if (coordinator == null) return;

            coordinator.TriggerGlobalResync();
        }

        /// <summary>全ユーザーの Active・Standby 両方を切断し Active で再接続する（緊急リブート）。</summary>
        public void OnForceRebootButtonPress()
        {
            if (!IsStaff())
            {
                return;
            }
            if (coordinator == null) return;

            coordinator.TriggerGlobalForceReboot();
        }

        /// <summary>Concurrent Max の Change ボタン — 編集モードに入る。</summary>
        public void OnConcurrentLimitChangeButton()
        {
            if (!IsStaff())
            {
                return;
            }
            if (coordinator == null) return;

            _concurrentEditOriginal = coordinator.GetMaxConcurrentResyncUsers();
            _concurrentEditMode = true;
            if (concurrentLimitInput != null)
                concurrentLimitInput.text = _concurrentEditOriginal.ToString();
            UpdateConcurrentEditVisibility();
        }

        /// <summary>Concurrent Max の Apply ボタン — 編集中の値を確定する。</summary>
        public void OnConcurrentLimitApply()
        {
            if (!IsStaff())
            {
                return;
            }
            if (coordinator == null) return;

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
            if (!IsStaff())
            {
                return;
            }
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
            if (!IsStaff())
            {
                return;
            }
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
            if (!IsStaff())
            {
                return;
            }
            if (coordinator == null) return;

            _connectionEditOriginal = coordinator.GetMaxConnectionLimit();
            _connectionEditMode = true;
            if (connectionLimitInput != null)
                connectionLimitInput.text = _connectionEditOriginal.ToString();
            UpdateConnectionEditVisibility();
        }

        public void OnConnectionLimitApply()
        {
            if (!IsStaff())
            {
                return;
            }
            if (coordinator == null) return;

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
            if (!IsStaff())
            {
                return;
            }
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
            if (!IsStaff())
            {
                return;
            }
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

        /// <summary>
        /// coordinator が保持する現在値を UI テキストフィールドに一括反映する。
        /// 編集確定/キャンセル後や解錠時に呼び、表示と実値の一貫性を保証する。
        /// </summary>
        private void SyncUIFromState()
        {
            if (coordinator == null) return;
            string concurrentVal = coordinator.GetMaxConcurrentResyncUsers().ToString();
            if (concurrentLimitDisplayText != null)
                concurrentLimitDisplayText.text = concurrentVal;
            if (concurrentLimitInput != null)
                concurrentLimitInput.text = concurrentVal;

            int connLimit = coordinator.GetMaxConnectionLimit();
            string connectionVal = connLimit.ToString();
            if (connectionLimitDisplayText != null)
                connectionLimitDisplayText.text = connectionVal;
            if (connectionLimitInput != null)
                connectionLimitInput.text = connLimit.ToString();
        }

        private void UpdateNowPlayingDisplay()
        {
            if (nowPlayingText == null || controller == null) return;
            VRCUrl current = controller.GetCurrentURL();
            string url = current != null ? current.Get() : null;
            nowPlayingText.text = string.IsNullOrEmpty(url) ? "No stream" : url;
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
            string thirdRow = displayEta > 0f
                ? $"Resync ETA\n<size=28>{displayEta:F0}s</size>"
                : $"Capacity\n<size=28>{(instanceCapacity > 0 ? instanceCapacity : 0)}</size>";
            userCountText.text =
                $"Playing\n<size=28>{playing}</size>{connectingSuffix}\n\nIn Instance\n<size=28>{inInstance}</size>\n\n{thirdRow}";
        }

        /// <summary>
        /// 接続状態インジケーターのリッチテキストを構築する。
        /// 各スロットを状態（再生中/停止）×色（エラー/待機/Resync/接続/正常）でソートし、
        /// 異常度の高いものが左上に来るよう並べることでスタッフが問題を即座に視認できるようにする。
        /// </summary>
        private void UpdateIndicatorDisplay()
        {
            if (indicatorText == null || coordinator == null) return;

            int coordSlots = coordinator.GetMaxPlayers();
            PlaybackMonitor pbm = coordinator.GetPlaybackMonitor();

            // ソートキー: スタイル（Playing=0, 停止=1）× 色（赤=0, 青=1, 黄=2, 橙=3, 白=4）
            int assigned = 0;
            int[] sortKeys = new int[coordSlots];
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
                if (state == ResyncCoordinator.STATE_QUEUED)
                    colorOrder = INDICATOR_COLOR_QUEUED;
                else if (state == ResyncCoordinator.STATE_GRANTED || state == ResyncCoordinator.STATE_RUNNING)
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

            if (assigned > _indicatorMaxSlots) _indicatorMaxSlots = assigned;
            int displaySlots = _indicatorMaxSlots;

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

            // リッチテキスト組み立て
            string result = "";
            int rendered = 0;
            for (int i = 0; i < displaySlots; i++)
            {
                if (rendered > 0 && rendered % 10 == 0) result += "\n";

                if (i < assigned)
                {
                    int key = sortKeys[i];
                    bool playing = key < 10;
                    int colorIdx = key % 10;

                    string hex = _indicatorHexColors[colorIdx];

                    string ch = playing ? "■" : "□";
                    result += $"<color={hex}>{ch}</color>";
                }
                else
                {
                    result += "<color=#000000>□</color>";
                }
                rendered++;
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

            bool isStaff = IsStaff();

            if (helpTextField != null)
            {
                if (!isStaff)
                {
                    helpTextField.text = string.Empty;
                    _activeHelpKey = HELP_NONE;
                }
                else if (_activeHelpKey == HELP_NONE)
                {
                    helpTextField.text = string.Empty;
                }
            }

            UpdateNowPlayingDisplay();

            if (nextUrlFieldPlaceholderText != null)
                nextUrlFieldPlaceholderText.text = isStaff ? "Next URL..." : string.Empty;
        }


        /// <summary>
        /// ローカルユーザーがスタッフ権限を持つか判定する。
        /// パスコード解錠済みか、allowedUserNames に displayName が含まれていれば true。
        /// SDK テスト時の同名重複問題に対処するため、同名が複数いる場合は最小 playerId のみ許可する。
        /// </summary>
        private bool IsStaff()
        {
            // パスコード解錠は同名重複チェックを経由しない（各クライアントが自分で入力する必要があるため）。
            if (_passcodeUnlocked) return true;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;

            bool eligible = false;
            if (allowedUserNames != null)
            {
                string displayName = local.displayName;
                for (int i = 0; i < allowedUserNames.Length; i++)
                {
                    if (allowedUserNames[i] == displayName)
                    {
                        eligible = true;
                        break;
                    }
                }
            }

            if (!eligible) return false;

            // SDK Build & Test で同一アカウントの複数クライアントが入ると displayName が衝突する。
            // 本番 VRChat では通常起きない異常なので、衝突を検出したときだけ最小 playerId を優先する。
            int playerCount = VRCPlayerApi.GetPlayerCount();
            if (playerCount <= 1) return true;

            VRCPlayerApi[] players = new VRCPlayerApi[playerCount];
            VRCPlayerApi.GetPlayers(players);

            string localName = local.displayName;
            int localId = local.playerId;
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi p = players[i];
                if (p == null || !Utilities.IsValid(p)) continue;
                if (p.playerId == localId) continue;
                if (p.displayName == localName && p.playerId < localId)
                    return false;
            }

            return true;
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
        public void OnHoverNowPlaying() { SetHelpText(HELP_NOW_PLAYING); }
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
        public void OnHoverClear() { SetHelpText(HELP_NONE); }

    }
}
