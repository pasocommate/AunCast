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
    /// ・Shared: 持ち運びパネル (UserStatusPanel) の Spawn ボタン / ビュー切替
    /// ・Resync Only ビュー: 遠距離で表示する全面 Resync ボタン
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class WallControlPanel : UdonSharpBehaviour
    {
        [Header("References")]
        [Tooltip("ローカル個人 Resync / Reboot を発行する LocalDualPlayerController。")]
        [SerializeField] private LocalDualPlayerController controller;
        [Tooltip("解錠対象の StaffControlPanel。正解時に SetLocalPasscodeUnlocked() を呼ぶ。")]
        [SerializeField] private StaffControlPanel staffPanel;
        [Tooltip("呼び出し対象の持ち運びパネル。Summon ボタンで表示する。")]
        [SerializeField] private UserStatusPanel portablePanel;

        [Header("View Crossfade")]
        [SerializeField] private CanvasGroup userCanvasGroup;
        [SerializeField] private CanvasGroup staffCanvasGroup;
        [SerializeField] private CanvasGroup sharedCanvasGroup;
        [SerializeField] private CanvasGroup resyncOnlyCanvasGroup;
        [Tooltip("クロスフェードの遷移時間（秒）")]
        [SerializeField] private float crossfadeDuration = 0.25f;

        [Header("View Switching")]
        [SerializeField] private Button resyncOnlyButton;
        [SerializeField] private TMP_Text switchViewButtonLabel;
        [SerializeField] private Button switchViewButton;

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
        [SerializeField] private Toggle gestureDoubleTriggerToggle;
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

        // ビュー定数
        private const int VIEW_USER = 0;
        private const int VIEW_STAFF = 1;
        private const int VIEW_RESYNC_ONLY = 2;

        private int _viewTarget = VIEW_USER;
        private float _userAlpha;
        private float _staffAlpha;
        private float _sharedAlpha;
        private float _resyncOnlyAlpha;

        private string _passcodeBuffer = "";
        private bool _passcodeUnlocked;
        private bool _crossfadeActive;
        private float _lastSlowUpdateTime;
        private const float SLOW_UPDATE_INTERVAL = 0.3f;
        private float _nearSqrDist;
        private float _farSqrDist;
        private bool _isNearWallPanel = true;

        private void Start()
        {
            _nearSqrDist = wallNearDistance * wallNearDistance;
            _farSqrDist = wallFarDistance * wallFarDistance;
            UpdatePasscodeDisplay();
            SetViewTarget(VIEW_USER, true);
            ApplyGestureGroupVisibility();
            ApplyGestureHighlight();
            UpdateUserButtonInteractable();
        }

        private void Update()
        {
            if (_crossfadeActive) UpdateCrossfade();

            float now = Time.time;
            if (now - _lastSlowUpdateTime < SLOW_UPDATE_INTERVAL) return;
            _lastSlowUpdateTime = now;

            UpdateUserButtonInteractable();
            CheckWallDistance();
        }

        // =================================================================
        //  クロスフェード
        // =================================================================

        private void SetViewTarget(int view, bool instant)
        {
            _viewTarget = view;
            if (!_passcodeUnlocked)
            {
                if (switchViewButtonLabel != null)
                    switchViewButtonLabel.text = view == VIEW_STAFF
                        ? SWITCH_ICON_TO_USER
                        : SWITCH_ICON_TO_STAFF;
            }
            if (instant)
            {
                float u = view == VIEW_USER ? 1f : 0f;
                float s = view == VIEW_STAFF ? 1f : 0f;
                float r = view == VIEW_RESYNC_ONLY ? 1f : 0f;
                float sh = view == VIEW_RESYNC_ONLY ? 0f : 1f;
                _userAlpha = u;
                _staffAlpha = s;
                _resyncOnlyAlpha = r;
                _sharedAlpha = sh;
                ApplyCanvasGroup(userCanvasGroup, u);
                ApplyCanvasGroup(staffCanvasGroup, s);
                ApplyCanvasGroup(resyncOnlyCanvasGroup, r);
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
            float tsh = _viewTarget == VIEW_RESYNC_ONLY ? 0f : 1f;

            bool changed = false;
            changed |= StepAlpha(ref _userAlpha, tu);
            changed |= StepAlpha(ref _staffAlpha, ts);
            changed |= StepAlpha(ref _resyncOnlyAlpha, tr);
            changed |= StepAlpha(ref _sharedAlpha, tsh);

            if (changed)
            {
                ApplyCanvasGroup(userCanvasGroup, _userAlpha);
                ApplyCanvasGroup(staffCanvasGroup, _staffAlpha);
                ApplyCanvasGroup(resyncOnlyCanvasGroup, _resyncOnlyAlpha);
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
            if (_passcodeUnlocked)
            {
                SetViewTarget(VIEW_USER, false);
                ApplyUnlockedSwitchButton();
                return;
            }
            SetViewTarget(_viewTarget == VIEW_STAFF ? VIEW_USER : VIEW_STAFF, false);
        }

        // =================================================================
        //  解錠後の自動切り替え
        // =================================================================

        /// <summary>UserStatusPanel の表示状態が変わったときに呼ばれる。</summary>
        /// <remarks>ポーリングではなく UserStatusPanel.SetMenuVisible から直接通知される。</remarks>
        public void OnPortablePanelVisibilityChanged(bool visible)
        {
            if (!_passcodeUnlocked) return;
            if (!visible) return;
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

        private void ApplyUnlockedSwitchButton()
        {
            if (switchViewButtonLabel != null)
                switchViewButtonLabel.text = SWITCH_ICON_UNLOCKED;
            SetButtonInteractable(switchViewButton, false);
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
            bool canResync = controller.GetLocalState() == LocalDualPlayerController.STATE_ACTIVE_PLAYING;
            SetButtonInteractable(userResyncButton, canResync);
            SetButtonInteractable(resyncOnlyButton, canResync);
            SetButtonInteractable(userRebootButton, controller.ShouldShowRebootButton());
        }

        private void SetButtonInteractable(Button button, bool interactable)
        {
            if (button == null || button.interactable == interactable) return;
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

        public void OnGestureDoubleTriggerToggleChanged()
        {
            if (gestureDoubleTriggerToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                UserStatusPanel.GESTURE_DOUBLE_TRIGGER, gestureDoubleTriggerToggle.isOn);
            SyncGestureToggles();
        }

        public void OnGestureBothTriggersToggleChanged()
        {
            if (gestureBothTriggersToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                UserStatusPanel.GESTURE_BOTH_TRIGGERS_HOLD, gestureBothTriggersToggle.isOn);
            SyncGestureToggles();
        }

        public void OnGestureRightStickUpToggleChanged()
        {
            if (gestureRightStickUpToggle == null || portablePanel == null) return;
            portablePanel.SetSummonGestureFlag(
                UserStatusPanel.GESTURE_RIGHT_STICK_UP_HOLD, gestureRightStickUpToggle.isOn);
            SyncGestureToggles();
        }

        public void OnDesktopTabDoubleTapToggleChanged()
        {
            if (desktopTabDoubleTapToggle == null || portablePanel == null) return;
            portablePanel.SetDesktopSummonGestureFlag(
                UserStatusPanel.DESKTOP_GESTURE_TAB_DOUBLE_TAP, desktopTabDoubleTapToggle.isOn);
            SyncGestureToggles();
        }

        public void OnDesktopF5DoubleTapToggleChanged()
        {
            if (desktopF5DoubleTapToggle == null || portablePanel == null) return;
            portablePanel.SetDesktopSummonGestureFlag(
                UserStatusPanel.DESKTOP_GESTURE_F5_DOUBLE_TAP, desktopF5DoubleTapToggle.isOn);
            SyncGestureToggles();
        }

        public void OnDesktopEscHoldToggleChanged()
        {
            if (desktopEscHoldToggle == null || portablePanel == null) return;
            portablePanel.SetDesktopSummonGestureFlag(
                UserStatusPanel.DESKTOP_GESTURE_ESC_HOLD, desktopEscHoldToggle.isOn);
            SyncGestureToggles();
        }

        private void SyncGestureToggles()
        {
            int vrCurrent = portablePanel != null
                ? portablePanel.GetSummonGesture()
                : UserStatusPanel.GESTURE_DOUBLE_TRIGGER;

            if (gestureDoubleTriggerToggle != null)
                gestureDoubleTriggerToggle.SetIsOnWithoutNotify(
                    (vrCurrent & UserStatusPanel.GESTURE_DOUBLE_TRIGGER) != 0);
            if (gestureBothTriggersToggle != null)
                gestureBothTriggersToggle.SetIsOnWithoutNotify(
                    (vrCurrent & UserStatusPanel.GESTURE_BOTH_TRIGGERS_HOLD) != 0);
            if (gestureRightStickUpToggle != null)
                gestureRightStickUpToggle.SetIsOnWithoutNotify(
                    (vrCurrent & UserStatusPanel.GESTURE_RIGHT_STICK_UP_HOLD) != 0);

            int deskCurrent = portablePanel != null
                ? portablePanel.GetDesktopSummonGesture()
                : UserStatusPanel.DESKTOP_GESTURE_TAB_DOUBLE_TAP;

            if (desktopTabDoubleTapToggle != null)
                desktopTabDoubleTapToggle.SetIsOnWithoutNotify(
                    (deskCurrent & UserStatusPanel.DESKTOP_GESTURE_TAB_DOUBLE_TAP) != 0);
            if (desktopF5DoubleTapToggle != null)
                desktopF5DoubleTapToggle.SetIsOnWithoutNotify(
                    (deskCurrent & UserStatusPanel.DESKTOP_GESTURE_F5_DOUBLE_TAP) != 0);
            if (desktopEscHoldToggle != null)
                desktopEscHoldToggle.SetIsOnWithoutNotify(
                    (deskCurrent & UserStatusPanel.DESKTOP_GESTURE_ESC_HOLD) != 0);
        }

        private void ApplyGestureHighlight()
        {
            SyncGestureToggles();
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
            if (_passcodeUnlocked || _passcodeBuffer.Length == 0) return;
            _passcodeBuffer = _passcodeBuffer.Substring(0, _passcodeBuffer.Length - 1);
            UpdatePasscodeDisplay();
        }

        public void OnPasscodeClear()
        {
            if (_passcodeUnlocked) return;
            _passcodeBuffer = "";
            UpdatePasscodeDisplay();
        }

        private void AppendPasscodeDigit(string digit)
        {
            if (_passcodeUnlocked || _passcodeBuffer.Length >= 4) return;
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
                _passcodeUnlocked = true;
                _passcodeBuffer = "";
                if (passcodeDisplay != null)
                    passcodeDisplay.text = "UNLOCKED";
                if (staffPanel != null)
                    staffPanel.SetLocalPasscodeUnlocked();
                ApplyUnlockedSwitchButton();
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
