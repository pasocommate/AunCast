using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// 壁掛けで固定配置する制御パネル。
    /// ・User ビュー: Resync / Reboot / 持ち運びパネルの呼び出しジェスチャー選択
    /// ・Staff ビュー: スタッフ解錠用のパスコード入力 (ローカル解錠のみ、同期なし)
    /// ・Shared: 持ち運びパネル (AunCastPortablePanel) の Spawn ボタン / ビュー切替
    /// ・Resync Only ビュー: 遠距離で表示する全面 Resync ボタン
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastWallControlPanel : UdonSharpBehaviour
    {
        [Header("References")]
        [Tooltip("ローカル個人 Resync / Reboot を発行する AunCastDualPlayerController。")]
        [SerializeField] private AunCastDualPlayerController controller;
        [Tooltip("解錠対象の AunCastStaffControlPanel。正解時に SetLocalPasscodeUnlocked() を呼ぶ。")]
        [SerializeField] private AunCastStaffControlPanel staffPanel;
        [Tooltip("呼び出し対象の持ち運びパネル。Summon ボタンで表示する。")]
        [SerializeField] private AunCastPortablePanel portablePanel;
        [SerializeField] private AunCastEventBus eventBus;

        [Header("View Crossfade")]
        [SerializeField] private CanvasGroup userCanvasGroup;
        [SerializeField] private CanvasGroup staffCanvasGroup;
        [SerializeField] private CanvasGroup sharedCanvasGroup;
        [SerializeField] private CanvasGroup resyncOnlyCanvasGroup;
        [Tooltip("製品情報（Copyright / QR）を表示する InformationContent の CanvasGroup。")]
        [SerializeField] private CanvasGroup informationCanvasGroup;
        [Tooltip("クロスフェードの遷移時間（秒）")]
        [SerializeField] private float crossfadeDuration = 0.25f;

        [Header("View Switching")]
        [SerializeField] private Button resyncOnlyButton;
        [SerializeField] private TMP_Text switchViewButtonLabel;
        [SerializeField] private Button switchViewButton;
        [Tooltip("InformationButton のアイコン表示用ラベル。情報表示中は閉じるアイコンに切り替える。")]
        [SerializeField] private TMP_Text informationButtonLabel;

        [Header("Shared Buttons Layout")]
        [SerializeField] private bool disablePasscodeViewSwitchButton;
        [SerializeField] private RectTransform spawnPanelButtonRect;

        [Header("Passcode UI (Staff View)")]
        [SerializeField] private TMP_Text passcodeDisplay;
        [Tooltip("4 桁の数字パスコード。空文字にすると常に Not configured を返す。")]
        [SerializeField] private string unlockPasscode = "0000";

        [Header("User Buttons (interactable gating)")]
        [SerializeField] private Button userResyncButton;
        [SerializeField] private Button userRebootButton;
        [Tooltip("ボタン無効時にラベルへ適用するアルファ値")]
        [SerializeField] private float disabledButtonLabelAlpha = 0.5f;

        [Header("Gesture Selection (User View)")]
        [SerializeField] private GameObject vrGestureGroup;
        [SerializeField] private Toggle gestureDoubleTriggerLeftToggle;
        [SerializeField] private Toggle gestureDoubleTriggerRightToggle;
        [SerializeField] private Toggle gestureBothTriggersToggle;
        [SerializeField] private Toggle gestureRightStickUpToggle;
        [SerializeField] private GameObject desktopGestureGroup;
        [SerializeField] private Toggle desktopTabDoubleTapToggle;
        [SerializeField] private Toggle desktopF5DoubleTapToggle;
        [SerializeField] private Toggle desktopEscHoldToggle;

        [Header("Wall Distance View")]
        [Tooltip("この距離以内に近づくと UserContent に切り替える（シュミットトリガー内側閾値）")]
        [SerializeField] private float wallNearDistance = 2.5f;
        [Tooltip("この距離以上離れると ResyncOnly に切り替える（シュミットトリガー外側閾値）")]
        [SerializeField] private float wallFarDistance = 3f;

        private const string SWITCH_ICON_TO_STAFF = "\ue899";   // Lock
        private const string SWITCH_ICON_TO_USER  = "\uf20b";   // AccountCircle
        private const string SWITCH_ICON_UNLOCKED = "\ue898";   // LockOpen
        private const string INFO_ICON  = "\uf59b";           // InfoI（i のみ）
        private const string CLOSE_ICON = "\ue5cd";           // Close（閉じる）
        public const string PACKAGE_VERSION = "4.2.2";
        private const string COPYRIGHT_TEXT =
            "AunCast v" + PACKAGE_VERSION
            + "\nX: @chigiri_vrc"
            + "\nhttps://chigiri.tokyo/"
            + "\n"
            + "\n© 2026 Chigiri Tsutsumi";

        // プレハブ SpawnPanelButton の offsetMax.x に対応
        private const float SPAWN_PANEL_BUTTON_RIGHT_DEFAULT = 94f;
        private const float SPAWN_PANEL_BUTTON_RIGHT_EXPANDED = 0f;

        // ビュー定数
        private const int VIEW_USER = 0;
        private const int VIEW_STAFF = 1;
        private const int VIEW_RESYNC_ONLY = 2;
        private const int VIEW_INFORMATION = 3;

        private int _viewTarget = VIEW_USER;
        // InformationContent を閉じたときに戻すビュー
        private int _viewBeforeInformation = VIEW_USER;
        private float _userAlpha;
        private float _staffAlpha;
        private float _sharedAlpha;
        private float _resyncOnlyAlpha;
        private float _informationAlpha;

        private string _passcodeBuffer = "";
        private bool _isStaff;
        private bool _crossfadeActive;
        private float _lastSlowUpdateTime;
        private const float SLOW_UPDATE_INTERVAL = 0.3f;
        private float _nearSqrDist;
        private float _farSqrDist;
        private bool _isNearWallPanel = true;
        private bool _gestureRestorePending;

        private void Start()
        {
            _nearSqrDist = wallNearDistance * wallNearDistance;
            _farSqrDist = wallFarDistance * wallFarDistance;
            ApplySharedButtonsLayout();
            ApplyCopyrightText();
            UpdatePasscodeDisplay();
            SetViewTarget(VIEW_USER, true);
            ApplyGestureGroupVisibility();
            SyncGestureToggles();
            UpdateUserButtonInteractable();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            _gestureRestorePending = true;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            if (_isStaff) return;
            if (staffPanel != null && staffPanel.IsLocallyUnlocked())
            {
                _isStaff = true;
                UpdateSwitchViewButton();
            }
        }

        /// <summary>AunCastDualPlayerController から FSM 状態変化を Push 通知される。</summary>
        public void OnLocalStateChanged()
        {
            UpdateUserButtonInteractable();
        }

        private void Update()
        {
            if (_crossfadeActive) UpdateCrossfade();

            if (_gestureRestorePending)
            {
                _gestureRestorePending = false;
                SyncGestureToggles();
            }

            float now = Time.time;
            if (now - _lastSlowUpdateTime < SLOW_UPDATE_INTERVAL) return;
            _lastSlowUpdateTime = now;

            CheckWallDistance();
        }

        // =================================================================
        //  クロスフェード
        // =================================================================

        private void SetViewTarget(int view, bool instant)
        {
            _viewTarget = view;
            if (view == VIEW_USER)
                SyncGestureToggles();
            UpdateSwitchViewButton();
            UpdateInformationButtonIcon();
            if (instant)
            {
                float u = view == VIEW_USER ? 1f : 0f;
                float s = view == VIEW_STAFF ? 1f : 0f;
                float r = view == VIEW_RESYNC_ONLY ? 1f : 0f;
                float info = view == VIEW_INFORMATION ? 1f : 0f;
                // Shared は User / Staff のときだけ表示する（ResyncOnly / Information では非表示）
                float sh = view == VIEW_USER || view == VIEW_STAFF ? 1f : 0f;
                _userAlpha = u;
                _staffAlpha = s;
                _resyncOnlyAlpha = r;
                _informationAlpha = info;
                _sharedAlpha = sh;
                ApplyCanvasGroup(userCanvasGroup, u);
                ApplyCanvasGroup(staffCanvasGroup, s);
                ApplyCanvasGroup(resyncOnlyCanvasGroup, r);
                ApplyCanvasGroup(informationCanvasGroup, info);
                ApplyCanvasGroup(sharedCanvasGroup, sh);
            }
            else
            {
                _crossfadeActive = true;
            }
        }

        private void UpdateCrossfade()
        {
            float tu = _viewTarget == VIEW_USER ? 1f : 0f;
            float ts = _viewTarget == VIEW_STAFF ? 1f : 0f;
            float tr = _viewTarget == VIEW_RESYNC_ONLY ? 1f : 0f;
            float tinfo = _viewTarget == VIEW_INFORMATION ? 1f : 0f;
            float tsh = _viewTarget == VIEW_USER || _viewTarget == VIEW_STAFF ? 1f : 0f;

            bool changed = false;
            changed |= StepAlpha(ref _userAlpha, tu);
            changed |= StepAlpha(ref _staffAlpha, ts);
            changed |= StepAlpha(ref _resyncOnlyAlpha, tr);
            changed |= StepAlpha(ref _informationAlpha, tinfo);
            changed |= StepAlpha(ref _sharedAlpha, tsh);

            if (changed)
            {
                ApplyCanvasGroup(userCanvasGroup, _userAlpha);
                ApplyCanvasGroup(staffCanvasGroup, _staffAlpha);
                ApplyCanvasGroup(resyncOnlyCanvasGroup, _resyncOnlyAlpha);
                ApplyCanvasGroup(informationCanvasGroup, _informationAlpha);
                ApplyCanvasGroup(sharedCanvasGroup, _sharedAlpha);
            }
            else
            {
                _crossfadeActive = false;
            }
        }

        private bool StepAlpha(ref float current, float target)
        {
            if (Mathf.Approximately(current, target)) return false;
            float step = crossfadeDuration > 0f ? Time.deltaTime / crossfadeDuration : 1f;
            current = Mathf.MoveTowards(current, target, step);
            return true;
        }

        private void ApplyCanvasGroup(CanvasGroup cg, float alpha)
        {
            if (cg == null) return;
            cg.alpha = alpha;
            bool active = alpha >= 0.99f;
            cg.interactable = active;
            cg.blocksRaycasts = active;
        }

        // =================================================================
        //  ビュー切替
        // =================================================================

        public void OnSwitchViewButtonPress()
        {
            if (disablePasscodeViewSwitchButton) return;
            if (_isStaff)
            {
                SetViewTarget(VIEW_USER, false);
                UpdateSwitchViewButton();
                return;
            }
            SetViewTarget(_viewTarget == VIEW_STAFF ? VIEW_USER : VIEW_STAFF, false);
        }

        public void ApplySharedButtonsLayout()
        {
            UpdateSwitchViewButton();

            RectTransform spawnRect = GetSpawnPanelButtonRect();
            if (spawnRect == null) return;
            float right = disablePasscodeViewSwitchButton
                ? SPAWN_PANEL_BUTTON_RIGHT_EXPANDED
                : SPAWN_PANEL_BUTTON_RIGHT_DEFAULT;
            Vector2 offsetMax = spawnRect.offsetMax;
            offsetMax.x = -right;
            spawnRect.offsetMax = offsetMax;
        }

        private RectTransform GetSpawnPanelButtonRect()
        {
            if (spawnPanelButtonRect != null) return spawnPanelButtonRect;

            var rects = GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] == null) continue;
                if (rects[i].name != "SpawnPanelButton") continue;
                spawnPanelButtonRect = rects[i];
                break;
            }
            return spawnPanelButtonRect;
        }

        // =================================================================
        //  解錠後の自動切り替え
        // =================================================================

        /// <summary>AunCastPortablePanel が表示されたときに AunCastEventBus から呼ばれる。</summary>
        public void OnPortablePanelShown()
        {
            if (!_isStaff) return;
            _isNearWallPanel = true;
            SetViewTarget(VIEW_USER, false);
        }

        private void CheckWallDistance()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            float sqrDist = (transform.position - head.position).sqrMagnitude;

            if (_isNearWallPanel)
            {
                if (sqrDist > _farSqrDist)
                {
                    _isNearWallPanel = false;
                    SetViewTarget(VIEW_RESYNC_ONLY, false);
                }
            }
            else
            {
                if (sqrDist < _nearSqrDist)
                {
                    _isNearWallPanel = true;
                    SetViewTarget(VIEW_USER, false);
                }
            }
        }

        /// <summary>
        /// switchViewButton の active / interactable / label を _isStaff / _viewTarget /
        /// disablePasscodeViewSwitchButton の AND で一括設定する。状態変化箇所はここを呼ぶだけにする。
        /// </summary>
        private void UpdateSwitchViewButton()
        {
            if (switchViewButton != null)
                switchViewButton.gameObject.SetActive(!disablePasscodeViewSwitchButton);

            if (switchViewButtonLabel != null)
            {
                if (_isStaff)
                    switchViewButtonLabel.text = SWITCH_ICON_UNLOCKED;
                else
                    switchViewButtonLabel.text = _viewTarget == VIEW_STAFF
                        ? SWITCH_ICON_TO_USER
                        : SWITCH_ICON_TO_STAFF;
            }

            SetButtonInteractable(switchViewButton, !_isStaff);
        }

        // =================================================================
        //  Information ビュー（Copyright / QR の表示切替）
        // =================================================================

        /// <summary>
        /// InformationButton 押下。情報表示中なら元のビューへ戻し、そうでなければ
        /// 現在のビューを記憶して InformationContent を表示する。
        /// </summary>
        public void OnInformationButtonPress()
        {
            if (_viewTarget == VIEW_INFORMATION)
            {
                SetViewTarget(_viewBeforeInformation, false);
            }
            else
            {
                _viewBeforeInformation = _viewTarget;
                SetViewTarget(VIEW_INFORMATION, false);
            }
        }

        private void ApplyCopyrightText()
        {
            var tf = transform.Find("ContentScaler/WallContentArea/InformationContent/CopyrightText");
            if (tf == null) return;
            var tmp = tf.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = COPYRIGHT_TEXT;
        }

        /// <summary>情報表示中は閉じるアイコン、それ以外は i アイコンを表示する。</summary>
        private void UpdateInformationButtonIcon()
        {
            if (informationButtonLabel == null) return;
            informationButtonLabel.text = _viewTarget == VIEW_INFORMATION
                ? CLOSE_ICON
                : INFO_ICON;
        }

        public void OnResyncOnlyButtonPress()
        {
            if (controller == null) return;
            controller.RequestManualResync();
        }

        // =================================================================
        //  Spawn
        // =================================================================

        public void OnSpawnPanelButtonPress()
        {
            if (portablePanel == null) return;
            portablePanel.SummonInFrontOfLocalPlayer();
        }

        // =================================================================
        //  User ビュー: Resync / Reboot
        // =================================================================

        public void OnUserResyncButtonPress()
        {
            if (controller == null) return;
            controller.RequestManualResync();
        }

        public void OnUserRebootButtonPress()
        {
            if (controller == null) return;
            controller.Reboot();
        }

        // =================================================================
        //  ボタン interactable 制御
        // =================================================================

        private void UpdateUserButtonInteractable()
        {
            if (controller == null) return;
            bool canRequestResync = controller.CanRequestManualResync();
            bool canReboot = controller.CanRebootLocal();
            SetButtonInteractable(userResyncButton, canRequestResync);
            SetButtonInteractable(resyncOnlyButton, canRequestResync);
            SetButtonInteractable(userRebootButton, canReboot);
        }

        private void SetButtonInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;
            float alpha = interactable ? 1f : disabledButtonLabelAlpha;
            var labels = button.GetComponentsInChildren<TMP_Text>();
            foreach (var label in labels)
            {
                var c = label.color;
                c.a = alpha;
                label.color = c;
            }
        }

        // =================================================================
        //  User ビュー: 呼び出しジェスチャー選択
        // =================================================================

        private void ApplyGestureGroupVisibility()
        {
            bool isVr = false;
            var local = Networking.LocalPlayer;
            if (local != null) isVr = local.IsUserInVR();

            if (vrGestureGroup != null) vrGestureGroup.SetActive(isVr);
            if (desktopGestureGroup != null) desktopGestureGroup.SetActive(!isVr);
        }

        public void OnGestureDoubleTriggerLeftToggleChanged()
        {
            if (gestureDoubleTriggerLeftToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                AunCastPortablePanel.GESTURE_DOUBLE_TRIGGER_LEFT, gestureDoubleTriggerLeftToggle.isOn);
            SyncGestureToggles();
        }

        public void OnGestureDoubleTriggerRightToggleChanged()
        {
            if (gestureDoubleTriggerRightToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                AunCastPortablePanel.GESTURE_DOUBLE_TRIGGER_RIGHT, gestureDoubleTriggerRightToggle.isOn);
            SyncGestureToggles();
        }

        public void OnGestureBothTriggersToggleChanged()
        {
            if (gestureBothTriggersToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                AunCastPortablePanel.GESTURE_BOTH_TRIGGERS_HOLD, gestureBothTriggersToggle.isOn);
            SyncGestureToggles();
        }

        public void OnGestureRightStickUpToggleChanged()
        {
            if (gestureRightStickUpToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                AunCastPortablePanel.GESTURE_RIGHT_STICK_UP_HOLD, gestureRightStickUpToggle.isOn);
            SyncGestureToggles();
        }

        public void OnDesktopTabDoubleTapToggleChanged()
        {
            if (desktopTabDoubleTapToggle == null || portablePanel == null) return;
            portablePanel.SetDesktopSummonGestureFlag(
                AunCastPortablePanel.DESKTOP_GESTURE_TAB_DOUBLE_TAP, desktopTabDoubleTapToggle.isOn);
            SyncGestureToggles();
        }

        public void OnDesktopF5DoubleTapToggleChanged()
        {
            if (desktopF5DoubleTapToggle == null || portablePanel == null) return;
            portablePanel.SetDesktopSummonGestureFlag(
                AunCastPortablePanel.DESKTOP_GESTURE_F5_DOUBLE_TAP, desktopF5DoubleTapToggle.isOn);
            SyncGestureToggles();
        }

        public void OnDesktopEscHoldToggleChanged()
        {
            if (desktopEscHoldToggle == null || portablePanel == null) return;
            portablePanel.SetDesktopSummonGestureFlag(
                AunCastPortablePanel.DESKTOP_GESTURE_ESC_HOLD, desktopEscHoldToggle.isOn);
            SyncGestureToggles();
        }

        private void SyncGestureToggles()
        {
            int vrCurrent = portablePanel != null
                ? portablePanel.GetSummonGesture()
                : AunCastPortablePanel.GESTURE_RIGHT_STICK_UP_HOLD;

            if (gestureDoubleTriggerLeftToggle != null)
                gestureDoubleTriggerLeftToggle.SetIsOnWithoutNotify(
                    (vrCurrent & AunCastPortablePanel.GESTURE_DOUBLE_TRIGGER_LEFT) != 0);
            if (gestureDoubleTriggerRightToggle != null)
                gestureDoubleTriggerRightToggle.SetIsOnWithoutNotify(
                    (vrCurrent & AunCastPortablePanel.GESTURE_DOUBLE_TRIGGER_RIGHT) != 0);
            if (gestureBothTriggersToggle != null)
                gestureBothTriggersToggle.SetIsOnWithoutNotify(
                    (vrCurrent & AunCastPortablePanel.GESTURE_BOTH_TRIGGERS_HOLD) != 0);
            if (gestureRightStickUpToggle != null)
                gestureRightStickUpToggle.SetIsOnWithoutNotify(
                    (vrCurrent & AunCastPortablePanel.GESTURE_RIGHT_STICK_UP_HOLD) != 0);

            int deskCurrent = portablePanel != null
                ? portablePanel.GetDesktopSummonGesture()
                : AunCastPortablePanel.DESKTOP_GESTURE_TAB_DOUBLE_TAP;

            if (desktopTabDoubleTapToggle != null)
                desktopTabDoubleTapToggle.SetIsOnWithoutNotify(
                    (deskCurrent & AunCastPortablePanel.DESKTOP_GESTURE_TAB_DOUBLE_TAP) != 0);
            if (desktopF5DoubleTapToggle != null)
                desktopF5DoubleTapToggle.SetIsOnWithoutNotify(
                    (deskCurrent & AunCastPortablePanel.DESKTOP_GESTURE_F5_DOUBLE_TAP) != 0);
            if (desktopEscHoldToggle != null)
                desktopEscHoldToggle.SetIsOnWithoutNotify(
                    (deskCurrent & AunCastPortablePanel.DESKTOP_GESTURE_ESC_HOLD) != 0);
        }


        // =================================================================
        //  Staff ビュー: パスコード入力
        // =================================================================

        public void OnPasscodeKey0() { AppendPasscodeDigit("0"); }
        public void OnPasscodeKey1() { AppendPasscodeDigit("1"); }
        public void OnPasscodeKey2() { AppendPasscodeDigit("2"); }
        public void OnPasscodeKey3() { AppendPasscodeDigit("3"); }
        public void OnPasscodeKey4() { AppendPasscodeDigit("4"); }
        public void OnPasscodeKey5() { AppendPasscodeDigit("5"); }
        public void OnPasscodeKey6() { AppendPasscodeDigit("6"); }
        public void OnPasscodeKey7() { AppendPasscodeDigit("7"); }
        public void OnPasscodeKey8() { AppendPasscodeDigit("8"); }
        public void OnPasscodeKey9() { AppendPasscodeDigit("9"); }

        public void OnPasscodeBackspace()
        {
            if (_isStaff || _passcodeBuffer.Length == 0) return;
            _passcodeBuffer = _passcodeBuffer.Substring(0, _passcodeBuffer.Length - 1);
            UpdatePasscodeDisplay();
        }

        private void AppendPasscodeDigit(string digit)
        {
            if (_isStaff || _passcodeBuffer.Length >= 4) return;
            _passcodeBuffer += digit;
            UpdatePasscodeDisplay();

            if (_passcodeBuffer.Length == 4)
                ValidatePasscode();
        }

        private void ValidatePasscode()
        {
            if (string.IsNullOrEmpty(unlockPasscode))
            {
                _passcodeBuffer = "";
                UpdatePasscodeDisplay();
                return;
            }

            if (_passcodeBuffer == unlockPasscode)
            {
                _isStaff = true;
                _passcodeBuffer = "";
                if (passcodeDisplay != null)
                    passcodeDisplay.text = "UNLOCKED";
                if (staffPanel != null)
                    staffPanel.SetLocalPasscodeUnlocked();
                UpdateSwitchViewButton();
            }
            else
            {
                _passcodeBuffer = "";
                UpdatePasscodeDisplay();
            }
        }

        private void UpdatePasscodeDisplay()
        {
            if (passcodeDisplay == null) return;
            int len = _passcodeBuffer.Length;
            string dots = "";
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) dots += "  ";
                dots += i < len ? "●" : "―";
            }
            passcodeDisplay.text = dots;
        }
    }
}
