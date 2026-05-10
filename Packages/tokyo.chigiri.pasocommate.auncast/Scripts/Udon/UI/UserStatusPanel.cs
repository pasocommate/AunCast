using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// 観客向けの自己状態確認・Resync リクエスト UI（Design Section 9.2-E, 22.3）。
    /// VRChat メニュー近傍に表示される拡張メニュー型。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class UserStatusPanel : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private LocalDualPlayerController controller;
        [SerializeField] private ResyncCoordinator coordinator;
        [SerializeField] private WallControlPanel wallPanel;

        [Header("UI Elements")]
        [SerializeField] private TMP_Text stateText;
        [Tooltip("エラーメッセージを表示する最大秒数（再生中のみ適用。停止中はエラーを表示し続ける）")]
        [SerializeField] private float errorDisplayDurationSec = 10f;

        [Header("Gauges")]
        [Tooltip("ディレイバッファ残りゲージ (0〜absorptionLimit)")]
        [SerializeField] private Slider headroomGauge;
        [Tooltip("現在RMSレベルゲージ (dBFS)")]
        [SerializeField] private Slider silenceGauge;
        [Tooltip("無音判定閾値の位置を示す縦ライン")]
        [SerializeField] private Image silenceThresholdMarker;
        [Tooltip("ピークホールド値の位置を示す縦ライン")]
        [SerializeField] private Image silencePeakMarker;
        [Tooltip("ピークホールド保持時間（秒）")]
        [SerializeField] private float silenceMeterPeakHoldSec = 0.5f;
        [Tooltip("ピークホールド減衰速度（dB/秒）")]
        [SerializeField] private float silenceMeterPeakDecayDbPerSec = 12f;

        [Header("Volume")]
        [SerializeField] private Slider volumeSlider;

        [Header("Auto Silence Resync")]
        [SerializeField] private Toggle autoSilenceResyncToggle;

        [Header("Buttons")]
        [SerializeField] private Button resyncButton;
        [SerializeField] private GameObject resyncButtonObject;
        [SerializeField] private Button rebootButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject closeButtonObject;

        [Header("Background Dissolve")]
        [Tooltip("Background の Image。Dissolve シェーダーと Viewer/Staff 色補間に使用する")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Color userBackgroundColor = Color.white;
        [SerializeField] private Color staffBackgroundColor = Color.white;
        [Tooltip("UI 要素の親に付与した CanvasGroup（dissolve と同期してアルファをフェードする）")]
        [SerializeField] private CanvasGroup contentCanvasGroup;
        [Tooltip("Dissolve アニメーションの遷移時間（秒）")]
        [SerializeField] private float dissolveDuration = 0.3f;

        [Header("Portable Panel - Crossfade Views")]
        [Tooltip("User 側コンテンツの CanvasGroup")]
        [SerializeField] private CanvasGroup userContentCanvasGroup;
        [Tooltip("Staff 側コンテンツの CanvasGroup")]
        [SerializeField] private CanvasGroup staffContentCanvasGroup;
        [Tooltip("Staff ビューの UI ロジックを司る StaffControlPanel。解錠判定に使用する")]
        [SerializeField] private StaffControlPanel staffControlPanel;
        [Tooltip("Viewer/Staff 切替トグルボタン。解錠時のみ可視化される")]
        [SerializeField] private GameObject switchButton;
        [Tooltip("クロスフェードの遷移時間（秒）")]
        [SerializeField] private float crossfadeDuration = 0.25f;

        [Header("Menu Follow")]
        [Tooltip("メニュー表示時に一度だけローカルプレイヤーの頭部基準で配置する（VR/Desktop ともに追従はしない）")]
        [SerializeField] private bool followLocalHead = true;
        [Tooltip("配置時にプレイヤー正面を向く")]
        [SerializeField] private bool faceLocalHead = true;
        [Tooltip("表示の有効/無効")]
        [SerializeField] private bool menuVisible = false;

        [Header("Menu Placement (VR)")]
        [Tooltip("VR 時の頭部座標系での表示オフセット")]
        [SerializeField] private Vector3 menuOffset = new Vector3(0f, -0.08f, 0.42f);
        [Tooltip("VR 時の表示スケール倍率（セットアップ時の基準スケールに掛ける）")]
        [SerializeField] private float menuScale = 0.8f;

        [Header("Menu Placement (Desktop)")]
        [Tooltip("Desktop 時、パネルが画面に対して占める割合 (0-1)。FOV から距離を自動算出する。")]
        [SerializeField] [Range(0.3f, 1.0f)] private float desktopFillRatio = 0.67f;
        [Tooltip("Desktop 時の表示スケール倍率（セットアップ時の基準スケールに掛ける）")]
        [SerializeField] private float desktopMenuScale = 1.0f;

        [Header("Open Trigger")]
        [Tooltip("VR モード時の呼び出しジェスチャー種別 (ビットフラグ)。複数同時有効可。")]
        [SerializeField] private int summonGesture = GESTURE_DOUBLE_TRIGGER;
        [Tooltip("両手トリガー長押し ジェスチャーで、両手のトリガーを同時に長押しする秒数。AunCastSettings.gestureHoldDuration と同期される。")]
        [SerializeField] private float vrBothTriggersHoldSec = 0.8f;
        [Tooltip("右スティック上倒し続け ジェスチャーで、右スティックを上方向に倒し続ける秒数。AunCastSettings.gestureHoldDuration と同期される。")]
        [SerializeField] private float vrRightStickUpHoldSec = 0.8f;
        [Tooltip("右スティック上倒し続け ジェスチャーの上倒し検知しきい値 (0-1)")]
        [SerializeField] private float vrRightStickUpThreshold = 0.7f;
        [Tooltip("片手ダブルトリガー ジェスチャーで、ダブル判定とみなす連続トリガーの最大間隔 (秒)")]
        [SerializeField] private float vrDoubleTriggerWindowSec = 0.4f;
        [Tooltip("Desktop モード時の ESC 長押し秒数。AunCastSettings.gestureHoldDuration と同期される。")]
        [SerializeField] private float desktopEscHoldSec = 0.8f;
        [Tooltip("Desktop モード時の Tab ダブルタップ判定ウィンドウ (秒)")]
        [SerializeField] private float desktopTabDoubleTapWindowSec = 0.4f;
        [Tooltip("Desktop モード時の F5 ダブルタップ判定ウィンドウ (秒)")]
        [SerializeField] private float desktopF5DoubleTapWindowSec = 0.4f;
        [Tooltip("Desktop モード時の呼び出しジェスチャー種別 (ビットフラグ)")]
        [SerializeField] private int desktopSummonGesture = DESKTOP_GESTURE_TAB_DOUBLE_TAP;

        // VR ジェスチャー種別のビットフラグ。複数同時有効にできる。
        public const int GESTURE_BOTH_TRIGGERS_HOLD = 1;
        public const int GESTURE_RIGHT_STICK_UP_HOLD = 2;
        public const int GESTURE_DOUBLE_TRIGGER = 4;

        // Desktop ジェスチャー種別のビットフラグ。
        public const int DESKTOP_GESTURE_TAB_DOUBLE_TAP = 1;
        public const int DESKTOP_GESTURE_F5_DOUBLE_TAP = 2;
        public const int DESKTOP_GESTURE_ESC_HOLD = 4;

        [Header("Haptic Feedback (VR)")]
        [Tooltip("VR でメニューを開いたときに両手へ送るハプティクスの再生時間（秒）。0 で無効化。")]
        [SerializeField] private float openHapticDuration = 0.1f;
        [Tooltip("ハプティクスの振幅（0-1）")]
        [SerializeField] private float openHapticAmplitude = 0.5f;
        [Tooltip("ハプティクスの周波数（Hz）")]
        [SerializeField] private float openHapticFrequency = 180f;

        [Header("Disabled State")]
        [Tooltip("ボタン無効時にラベルへ適用するアルファ値")]
        [SerializeField] private float disabledButtonLabelAlpha = 0.5f;

        [Header("Grab Move (VR)")]
        [Tooltip("グリップボタンでパネルを掴んで移動できる判定ボリューム。パネル中心からの半サイズをワールド単位（メートル）で指定する。Z（法線方向）を短くしてポインタ操作の誤発火を防ぐ")]
        [SerializeField] private Vector3 grabHalfExtents = new Vector3(0.5f, 0.35f, 0.25f);
        [Tooltip("掴み開始時に鳴らす短いハプティクスの再生時間（秒）。0 で無効。")]
        [SerializeField] private float grabHapticDuration = 0.04f;
        [SerializeField] private float grabHapticAmplitude = 0.35f;
        [SerializeField] private float grabHapticFrequency = 180f;

        [Header("Auto Dismiss")]
        [Tooltip("パネルからこの距離（m）以上離れると自動的に閉じる。0 で無効。")]
        [SerializeField] private float autoDismissDistance = 5f;
        [Tooltip("パネルが視界外に出てからこの秒数経過で自動的に閉じる。0 で無効。")]
        [SerializeField] private float outOfSightDismissSec = 5f;

        [Header("HUD Overlay")]
        [Tooltip("VR ジェスチャー長押し中に視界へ表示するプログレス HUD。")]
        [SerializeField] private HudProgressOverlay hudProgress;


        // --- ジェスチャー検出状態 ---
        // VR の各種ジェスチャー（両手トリガー長押し / 右スティック上倒し / ダブルトリガー）を
        // ポーリングで検出するためのタイマーとエッジ検知フラグ群。

        private float _lastSlowUpdateTime;
        private float _lastSilenceMeterUpdateTime;
        private const float REFERENCE_EYE_HEIGHT = 1.3f;
        private const float UPDATE_INTERVAL = 0.5f;
        private const float SILENCE_METER_UPDATE_INTERVAL = 0.1f;
        private const float SILENCE_METER_MIN_DBFS = -96f;
        private const float SILENCE_METER_MAX_DBFS = 0f;
        private bool _volumeSliderInitialized;
        private float _lastVolumeSliderValue;
        private bool _autoSilenceToggleInitialized;
        private bool _lastAutoSilenceToggleState;
        private Canvas _canvas;
        private Collider _collider;
        private bool _vrLeftUsePressed;
        private bool _vrRightUsePressed;
        private float _vrBothHoldElapsed;
        private bool _vrHoldConsumed;
        // 右スティック上倒し続けジェスチャー: InputLookVertical の最新値と長押し計測
        private float _vrLookVertical;
        private float _vrStickUpHoldElapsed;
        private bool _vrStickHoldConsumed;
        // VRChat メニューの開閉状態（メニュー中はスティック系ジェスチャーを抑止する）
        private bool _isVRChatMenuOpen;
        // 片手ダブルトリガージェスチャー: 各手のトリガー前回 press-down 時刻
        private float _vrLeftLastPressTime = -10f;
        private float _vrRightLastPressTime = -10f;
        // Desktop ESC 長押し検出
        private float _desktopEscHoldElapsed;
        private bool _desktopEscHoldConsumed;
        // Desktop Tab / F5 ダブルタップ検出
        private float _desktopTabLastPressTime = -10f;
        private bool _desktopTabDoubleTapConsumed;
        private float _desktopF5LastPressTime = -10f;
        private bool _desktopF5DoubleTapConsumed;
        private float _outOfSightSince;
        private float _autoDismissSqrDist;
        private bool _isGrabbing;
        private HandType _grabbingHand;
        // 掴み開始時点での「手ローカル系でのパネル姿勢」。毎フレーム手の姿勢に合わせて再構築するだけで済む。
        private Vector3 _grabOffsetPos;
        private Quaternion _grabOffsetRot = Quaternion.identity;

        // --- Desktop メニュー追従状態 ---
        // メニューを開いた瞬間のカメラ前方をワールド方向として記録。
        // 毎フレーム、カメラ forward の接平面にこの方向を射影してパネル位置を決める。
        private Vector3 _menuAnchorDir;
        private float _menuDist;

        // --- クロスフェード状態 ---
        // Viewer ↔ Staff ビューの切替をアルファ補間で滑らかに見せるための進捗値。
        // 0 = Viewer 全面、1 = Staff 全面。_crossfadeTarget を切り替えると Update でアルファを滑らかに寄せる。
        private bool _crossfadeActive;
        private float _crossfadeCurrent;
        private float _crossfadeTarget;
        private Vector3 _baseLocalScale = Vector3.one;

        // --- ディゾルブ状態 ---
        private bool _dissolveHiding;
        private bool _dissolveActive;
        private float _dissolveT;
        private bool _dissolveShowing;
        private Material _bgMaterial;

        private TMP_Text _resyncButtonLabel;
        private TMP_Text _resyncCooldownLabel;
        private bool _debugAutoOpenDone;

        private Image _silenceGaugeFillImage;
        private Color _silenceGaugeActiveColor;
        private Color _silenceGaugeSuppressedColor;
        private float _silencePeakDbfs = SILENCE_METER_MIN_DBFS;
        private float _silencePeakHoldUntil;
        private bool _silencePeakInitialized;

        private void Start()
        {
            if (backgroundImage != null)
                _bgMaterial = backgroundImage.material;
            _canvas = GetComponent<Canvas>();
            _collider = GetComponent<Collider>();
            _baseLocalScale = transform.localScale;
            _autoDismissSqrDist = autoDismissDistance * autoDismissDistance;
            _silenceGaugeActiveColor = new Color(0.85f, 0.35f, 0.35f, 1f);
            _silenceGaugeSuppressedColor = new Color(0.35f, 0.35f, 0.38f, 0.5f);
            if (silenceThresholdMarker != null)
                silenceThresholdMarker.color = new Color(1f, 0.85f, 0.2f, 1f);
            if (silencePeakMarker != null)
                silencePeakMarker.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            if (silenceGauge != null && silenceGauge.fillRect != null)
                _silenceGaugeFillImage = silenceGauge.fillRect.GetComponent<Image>();
            SetMenuVisible(false);
            InitResyncButtonStyle();
            SyncLocalSettingsUI();
            // 起動時は Viewer ビューで開始。Staff 解錠状態に応じた切替ボタンの見せ方も確定させる。
            _crossfadeCurrent = 0f;
            _crossfadeTarget = 0f;
            ApplyCrossfade();
            OnStaffUnlockStateChanged();
        }

        /// <summary>
        /// Resync ボタンの無効色を設定し、ボタン内のサブラベル（メイン / クールダウン表示）を検索して保持する。
        /// ボタン一つに複数の TMP_Text を持たせ、状態に応じて出し分けるための初期化処理。
        /// </summary>
        private void InitResyncButtonStyle()
        {
            if (resyncButton == null) return;

            ColorBlock cb = resyncButton.colors;
            cb.disabledColor = new Color(0.35f, 0.35f, 0.38f, 1f);
            resyncButton.colors = cb;

            var labels = resyncButton.GetComponentsInChildren<TMP_Text>(true);
            if (labels != null && labels.Length >= 2)
            {
                _resyncButtonLabel = labels[0];
                _resyncCooldownLabel = labels[1];
            }
            else if (labels != null && labels.Length == 1)
            {
                _resyncButtonLabel = labels[0];
            }
        }

        /// <summary>
        /// ボタンの interactable 状態を設定し、無効時はラベルや CanvasGroup のアルファを下げて
        /// ユーザーに「押せない」ことを視覚的にフィードバックする。
        /// </summary>
        private void SetButtonInteractable(Button button, bool interactable)
        {
            if (button == null || button.interactable == interactable) return;
            button.interactable = interactable;
            float alpha = interactable ? 1f : disabledButtonLabelAlpha;
            var cg = button.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = alpha;
                return;
            }
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                var c = label.color;
                c.a = alpha;
                label.color = c;
            }
        }

        private void Update()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local != null)
            {
                if (local.IsUserInVR())
                {
                    PollVrSummonGesture(local);
                    UpdateGrabMove();
                }
                else
                {
                    PollDesktopMenuTrigger();
                    if (menuVisible) ApplyDesktopMenuTransform();
                }
            }
            if (_dissolveActive) TickDissolve();
            if (_crossfadeActive) UpdateCrossfade();

            if (!menuVisible) return;

            if (_crossfadeTarget < 0.5f)
            {
                PollVolumeSlider();
                PollAutoSilenceToggle();

                float nowMeter = Time.time;
                if (nowMeter - _lastSilenceMeterUpdateTime >= SILENCE_METER_UPDATE_INTERVAL)
                {
                    UpdateSilenceMeter(nowMeter);
                    _lastSilenceMeterUpdateTime = nowMeter;
                }
            }

            float now = Time.time;
            if (now - _lastSlowUpdateTime < UPDATE_INTERVAL) return;
            _lastSlowUpdateTime = now;

            if (CheckAutoDismiss()) return;

            UpdateDisplay();
        }

        /// <summary>切替ボタンから呼ぶ: Viewer/Staff をトグルする。Staff へは解錠時のみ遷移可。</summary>
        public void OnSwitchViewButtonPress()
        {
            if (_crossfadeTarget < 0.5f)
            {
                if (staffControlPanel == null || !staffControlPanel.IsLocallyUnlocked()) return;
                _crossfadeTarget = 1f;
            }
            else
            {
                _crossfadeTarget = 0f;
            }
            _crossfadeActive = true;
        }

        private void UpdateCrossfade()
        {
            float step = (crossfadeDuration <= 0f) ? 1f : Time.deltaTime / crossfadeDuration;
            _crossfadeCurrent = Mathf.MoveTowards(_crossfadeCurrent, _crossfadeTarget, step);
            ApplyCrossfade();
            if (Mathf.Approximately(_crossfadeCurrent, _crossfadeTarget))
                _crossfadeActive = false;
        }

        private void ApplyCrossfade()
        {
            // 0 = Viewer 全面、1 = Staff 全面。中間は双方とも非インタラクティブ。
            float viewerAlpha = 1f - _crossfadeCurrent;
            float staffAlpha = _crossfadeCurrent;
            bool viewerActive = _crossfadeCurrent <= 0.01f;
            bool staffActive = _crossfadeCurrent >= 0.99f;

            if (userContentCanvasGroup != null)
            {
                userContentCanvasGroup.alpha = viewerAlpha;
                userContentCanvasGroup.interactable = viewerActive;
                userContentCanvasGroup.blocksRaycasts = viewerActive;
            }
            if (staffContentCanvasGroup != null)
            {
                staffContentCanvasGroup.alpha = staffAlpha;
                staffContentCanvasGroup.interactable = staffActive;
                staffContentCanvasGroup.blocksRaycasts = staffActive;
            }

            if (backgroundImage != null)
                backgroundImage.color = Color.Lerp(userBackgroundColor, staffBackgroundColor, _crossfadeCurrent);
        }

        /// <summary>StaffControlPanel の解錠状態が変わったときに呼ばれる。切替ボタンの表示とクロスフェードを更新する。</summary>
        public void OnStaffUnlockStateChanged()
        {
            bool unlocked = staffControlPanel != null && staffControlPanel.IsLocallyUnlocked();

            if (switchButton != null && switchButton.activeSelf != unlocked)
                switchButton.SetActive(unlocked);

            // 未解錠に戻った場合は Viewer に強制復帰
            if (!unlocked && _crossfadeTarget > 0f)
            {
                _crossfadeTarget = 0f;
                _crossfadeActive = true;
            }
        }

        /// <summary>Desktop モードのメニュー呼び出しジェスチャーをポーリングする。</summary>
        /// <remarks>Update() の Desktop 分岐から呼ばれるためモード判定不要。</remarks>
        private void PollDesktopMenuTrigger()
        {
            if ((desktopSummonGesture & DESKTOP_GESTURE_TAB_DOUBLE_TAP) != 0)
                PollDesktopTabDoubleTap();
            if ((desktopSummonGesture & DESKTOP_GESTURE_F5_DOUBLE_TAP) != 0)
                PollDesktopF5DoubleTap();
            if ((desktopSummonGesture & DESKTOP_GESTURE_ESC_HOLD) != 0)
                PollDesktopEscHold();
        }

        private void PollDesktopTabDoubleTap()
        {
            if (!Input.GetKey(KeyCode.Tab))
            {
                _desktopTabDoubleTapConsumed = false;
                return;
            }

            if (!Input.GetKeyDown(KeyCode.Tab)) return;

            float now = Time.time;
            float prev = _desktopTabLastPressTime;
            _desktopTabLastPressTime = now;

            if (_desktopTabDoubleTapConsumed) return;
            if ((now - prev) > desktopTabDoubleTapWindowSec) return;

            _desktopTabDoubleTapConsumed = true;
            TriggerDesktopMenuToggle();
        }

        private void PollDesktopF5DoubleTap()
        {
            if (!Input.GetKey(KeyCode.F5))
            {
                _desktopF5DoubleTapConsumed = false;
                return;
            }

            if (!Input.GetKeyDown(KeyCode.F5)) return;

            float now = Time.time;
            float prev = _desktopF5LastPressTime;
            _desktopF5LastPressTime = now;

            if (_desktopF5DoubleTapConsumed) return;
            if ((now - prev) > desktopF5DoubleTapWindowSec) return;

            _desktopF5DoubleTapConsumed = true;
            TriggerDesktopMenuToggle();
        }

        private void PollDesktopEscHold()
        {
            if (!Input.GetKey(KeyCode.Escape))
            {
                if (_desktopEscHoldElapsed > 0f && hudProgress != null)
                    hudProgress.Hide();
                _desktopEscHoldElapsed = 0f;
                _desktopEscHoldConsumed = false;
                return;
            }

            if (_desktopEscHoldConsumed) return;

            _desktopEscHoldElapsed += Time.deltaTime;

            if (hudProgress != null)
                hudProgress.SetHoldProgress(_desktopEscHoldElapsed, desktopEscHoldSec);

            if (_desktopEscHoldElapsed < desktopEscHoldSec) return;

            _desktopEscHoldConsumed = true;
            if (hudProgress != null) hudProgress.Hide();
            TriggerDesktopMenuToggle();
        }

        private void TriggerDesktopMenuToggle()
        {
            if (!menuVisible)
            {
                _crossfadeTarget = 0f;
                _crossfadeCurrent = 0f;
                ApplyCrossfade();
                ShowMenu();
                return;
            }

            bool staffUnlocked = staffControlPanel != null && staffControlPanel.IsLocallyUnlocked();

            if (_crossfadeTarget < 0.5f && staffUnlocked)
            {
                _crossfadeTarget = 1f;
                _crossfadeActive = true;
            }
            else
            {
                SetMenuVisible(false);
            }
        }

        /// <summary>
        /// メニューを開いたときの初期配置。
        /// VR: 頭部ローカル座標にオフセット配置。
        /// Desktop: ScreenCamera 前方にアンカー方向を記録し、距離を FOV から逆算。
        /// </summary>
        private void UpdateMenuFollow()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            if (local.IsUserInVR())
            {
                float avatarScale = local.GetAvatarEyeHeightAsMeters() / REFERENCE_EYE_HEIGHT;
                VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                transform.position = head.position + head.rotation * (menuOffset * avatarScale);
                transform.localScale = _baseLocalScale * (menuScale * avatarScale);

                if (faceLocalHead)
                {
                    Vector3 toHead = head.position - transform.position;
                    if (toHead.sqrMagnitude > 0.0001f)
                        transform.rotation = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                }
                else
                {
                    transform.rotation = head.rotation;
                }
            }
            else
            {
                var cam = VRCCameraSettings.ScreenCamera;
                _menuAnchorDir = (cam.Rotation * Vector3.forward).normalized;
                RecalcDesktopMenuDist();
                ApplyDesktopMenuTransform();
            }
        }

        /// <summary>Desktop 時の表示距離を FOV とパネルサイズから算出して _menuDist に格納する。</summary>
        private void RecalcDesktopMenuDist()
        {
            float scale = desktopMenuScale;
            transform.localScale = _baseLocalScale * scale;

            RectTransform rt = (RectTransform)transform;
            float panelHalfH = rt.sizeDelta.y * _baseLocalScale.y * scale * 0.5f;
            float panelHalfW = rt.sizeDelta.x * _baseLocalScale.x * scale * 0.5f;

            var cam = VRCCameraSettings.ScreenCamera;
            float vFovRad = cam.FieldOfView * Mathf.Deg2Rad * 0.5f;
            float tanHalf = Mathf.Tan(vFovRad);
            float aspect = cam.Aspect;

            float distV = panelHalfH / (desktopFillRatio * tanHalf);
            float distH = panelHalfW / (desktopFillRatio * tanHalf * aspect);
            _menuDist = Mathf.Max(distV, distH);
        }

        /// <summary>
        /// Desktop 時の毎フレーム追従。
        /// カメラを中心とする半径 _menuDist の球面上で、カメラ forward が交わる点の
        /// 接平面にパネルを配置する。_menuAnchorDir 方向のレイと接平面の交点が
        /// パネル中心になるため、カメラが回転するとレティクルがパネル上を走査する。
        /// </summary>
        private void ApplyDesktopMenuTransform()
        {
            var cam = VRCCameraSettings.ScreenCamera;
            Vector3 camPos = cam.Position;
            Vector3 camFwd = cam.Rotation * Vector3.forward;

            float cosAngle = Vector3.Dot(_menuAnchorDir, camFwd);
            if (cosAngle < 0.15f) cosAngle = 0.15f;

            transform.position = camPos + _menuAnchorDir * (_menuDist / cosAngle);
            transform.rotation = Quaternion.LookRotation(camFwd, Vector3.up);
        }

        public bool IsMenuVisible() { return menuVisible; }

        /// <summary>外部から表示状態を切り替える。</summary>
        public void SetMenuVisible(bool visible)
        {
            menuVisible = visible;

            if (visible)
            {
                _dissolveHiding = false;
                if (_collider != null)
                    _collider.enabled = true;
                if (_canvas != null)
                    _canvas.enabled = true;
                StartDissolve(true);
            }
            else
            {
                // 閉じた直後に両手トリガーがまだ握られていても再オープンしないよう消費扱いにする
                _vrBothHoldElapsed = 0f;
                _vrHoldConsumed = true;
                _isGrabbing = false;
                if (_collider != null)
                    _collider.enabled = false;
                _dissolveHiding = true;
                StartDissolve(false);
                SendCustomEventDelayedSeconds(nameof(OnDissolveOutComplete), dissolveDuration);
            }

            if (wallPanel != null)
                wallPanel.OnPortablePanelVisibilityChanged(visible);
        }

        public void OnDissolveOutComplete()
        {
            if (!_dissolveHiding) return;
            _dissolveHiding = false;
            if (_canvas != null)
                _canvas.enabled = false;
        }

        private void StartDissolve(bool showing)
        {
            _dissolveT = 0f;
            _dissolveShowing = showing;
            _dissolveActive = true;
        }

        private void TickDissolve()
        {
            float step = dissolveDuration > 0f ? Time.deltaTime / dissolveDuration : 1f;
            _dissolveT = Mathf.Clamp01(_dissolveT + step);

            float alpha;
            float threshold;
            if (_dissolveShowing)
            {
                alpha = _dissolveT;
                threshold = _dissolveT - 1f;
            }
            else
            {
                alpha = 1f - _dissolveT;
                threshold = _dissolveT;
            }

            if (_bgMaterial != null)
                _bgMaterial.SetFloat("_DissolveThreshold", threshold);
            if (contentCanvasGroup != null)
                contentCanvasGroup.alpha = alpha;

            if (_dissolveT >= 1f)
                _dissolveActive = false;
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.LEFT)
            {
                bool wasPressed = _vrLeftUsePressed;
                _vrLeftUsePressed = value;
                // 片手ダブルトリガー検知用に press-down のみ前回時刻を更新する
                if (value && !wasPressed)
                    HandleDoubleTriggerEdge(true);
            }
            else if (args.handType == HandType.RIGHT)
            {
                bool wasPressed = _vrRightUsePressed;
                _vrRightUsePressed = value;
                if (value && !wasPressed)
                    HandleDoubleTriggerEdge(false);
            }
        }

        public override void InputLookVertical(float value, UdonInputEventArgs args)
        {
            // VR では右スティック Y 軸、Desktop ではマウス Y 軸の値が来る。
            // 右スティック上倒し続けジェスチャーの判定で参照する。
            _vrLookVertical = value;
        }

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            if (value) TryStartGrab(args.handType);
            else TryEndGrab(args.handType);
        }

        /// <summary>手がパネルの近傍にあるときだけ掴みを開始する。</summary>
        private void TryStartGrab(HandType hand)
        {
            if (_isGrabbing) return;
            if (!menuVisible) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !local.IsUserInVR()) return;

            VRCPlayerApi.TrackingData td = local.GetTrackingData(
                hand == HandType.LEFT
                    ? VRCPlayerApi.TrackingDataType.LeftHand
                    : VRCPlayerApi.TrackingDataType.RightHand);

            if (!IsHandInsideGrabVolume(td.position)) return;

            _isGrabbing = true;
            _grabbingHand = hand;
            // 掴んだ瞬間の「手ローカル系でのパネル姿勢」を記録。以降はこれを保ったまま手に追従させる。
            Quaternion invHand = Quaternion.Inverse(td.rotation);
            _grabOffsetPos = invHand * (transform.position - td.position);
            _grabOffsetRot = invHand * transform.rotation;

            if (grabHapticDuration > 0f)
            {
                VRC_Pickup.PickupHand pickupHand = hand == HandType.LEFT
                    ? VRC_Pickup.PickupHand.Left
                    : VRC_Pickup.PickupHand.Right;
                local.PlayHapticEventInHand(pickupHand, grabHapticDuration, grabHapticAmplitude, grabHapticFrequency);
            }
        }

        private void TryEndGrab(HandType hand)
        {
            if (!_isGrabbing) return;
            if (_grabbingHand != hand) return;
            _isGrabbing = false;
        }

        /// <summary>ワールド空間の手の位置がパネル中心から grabHalfExtents の範囲内にあるか判定。</summary>
        private bool IsHandInsideGrabVolume(Vector3 handWorldPos)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            float avatarScale = (local != null) ? local.GetAvatarEyeHeightAsMeters() / REFERENCE_EYE_HEIGHT : 1f;
            Vector3 offset = Quaternion.Inverse(transform.rotation) * (handWorldPos - transform.position);
            return Mathf.Abs(offset.x) <= grabHalfExtents.x * avatarScale
                && Mathf.Abs(offset.y) <= grabHalfExtents.y * avatarScale
                && Mathf.Abs(offset.z) <= grabHalfExtents.z * avatarScale;
        }

        /// <summary>掴んでいる間だけ毎フレーム手の姿勢にパネルを追従させる。掴んでいなければ即 return。</summary>
        /// <remarks>Update() の VR 分岐（local != null かつ IsUserInVR）から呼ばれるため local の null/VR チェック不要。</remarks>
        private void UpdateGrabMove()
        {
            if (!_isGrabbing) return;
            if (!menuVisible) { _isGrabbing = false; return; }

            VRCPlayerApi local = Networking.LocalPlayer;

            VRCPlayerApi.TrackingData td = local.GetTrackingData(
                _grabbingHand == HandType.LEFT
                    ? VRCPlayerApi.TrackingDataType.LeftHand
                    : VRCPlayerApi.TrackingDataType.RightHand);

            transform.position = td.position + td.rotation * _grabOffsetPos;
            transform.rotation = td.rotation * _grabOffsetRot;
        }


        /// <summary>選択中のジェスチャー種別に応じてメニュー開ジェスチャーをポーリングする。</summary>
        /// <remarks>Update() の VR 分岐（local != null かつ IsUserInVR）から呼ばれるため local の null/VR チェック不要。</remarks>
        private void PollVrSummonGesture(VRCPlayerApi local)
        {
            if ((summonGesture & GESTURE_BOTH_TRIGGERS_HOLD) != 0)
                PollVrBothTriggersHold(local);
            if ((summonGesture & GESTURE_RIGHT_STICK_UP_HOLD) != 0)
                PollVrRightStickUpHold(local);
            if ((summonGesture & GESTURE_DOUBLE_TRIGGER) != 0)
                PollVrDoubleTrigger(local);
        }

        /// <summary>両手トリガーの長押しを検知してメニューを開く。表示中は現在位置へ再配置する。</summary>
        private void PollVrBothTriggersHold(VRCPlayerApi local)
        {
            bool both = _vrLeftUsePressed && _vrRightUsePressed;
            if (!both)
            {
                if (_vrBothHoldElapsed > 0f && hudProgress != null)
                    hudProgress.Hide();
                _vrBothHoldElapsed = 0f;
                _vrHoldConsumed = false;
                return;
            }

            if (_vrHoldConsumed) return;

            _vrBothHoldElapsed += Time.deltaTime;

            if (hudProgress != null)
                hudProgress.SetHoldProgress(_vrBothHoldElapsed, vrBothTriggersHoldSec);

            if (_vrBothHoldElapsed < vrBothTriggersHoldSec) return;

            _vrHoldConsumed = true;
            _vrBothHoldElapsed = 0f;
            if (hudProgress != null) hudProgress.Hide();
            TriggerSummonByGesture(local);
        }

        /// <summary>右スティックを上方向へ閾値を超えて一定秒数倒し続けたらメニューを開く。</summary>
        private void PollVrRightStickUpHold(VRCPlayerApi local)
        {
            if (_isVRChatMenuOpen || _vrLookVertical < vrRightStickUpThreshold)
            {
                if (_vrStickUpHoldElapsed > 0f && hudProgress != null)
                    hudProgress.Hide();
                _vrStickUpHoldElapsed = 0f;
                _vrStickHoldConsumed = false;
                return;
            }

            if (_vrStickHoldConsumed) return;

            _vrStickUpHoldElapsed += Time.deltaTime;

            if (hudProgress != null)
                hudProgress.SetHoldProgress(_vrStickUpHoldElapsed, vrRightStickUpHoldSec);

            if (_vrStickUpHoldElapsed < vrRightStickUpHoldSec) return;

            _vrStickHoldConsumed = true;
            _vrStickUpHoldElapsed = 0f;
            if (hudProgress != null) hudProgress.Hide();
            TriggerSummonByGesture(local);
        }

        /// <summary>左右いずれかのトリガーを所定時間内に 2 回連続で押したらメニューを開く。</summary>
        private void PollVrDoubleTrigger(VRCPlayerApi local)
        {
            // 実際の検知は HandleDoubleTriggerEdge 内で行われ、_vrHoldConsumed をリセットする条件は
            // 「両手とも押されていない状態」になってから次のダブル発火を許可する。
            if (!_vrLeftUsePressed && !_vrRightUsePressed)
                _vrHoldConsumed = false;
        }

        /// <summary>InputUse の press-down エッジで呼ばれ、片手ダブルトリガー判定を行う。</summary>
        private void HandleDoubleTriggerEdge(bool isLeft)
        {
            if ((summonGesture & GESTURE_DOUBLE_TRIGGER) == 0) return;

            float now = Time.time;
            float prev = isLeft ? _vrLeftLastPressTime : _vrRightLastPressTime;
            if (isLeft) _vrLeftLastPressTime = now;
            else _vrRightLastPressTime = now;

            if (_vrHoldConsumed) return;
            if ((now - prev) > vrDoubleTriggerWindowSec) return;

            _vrHoldConsumed = true;
            // InputUse は VR 入力コールバックなので IsUserInVR チェック不要
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            TriggerSummonByGesture(local);
        }

        /// <summary>ジェスチャー検知時の共通処理。非表示なら開き、表示中はビュー切替または閉じる。</summary>
        private void TriggerSummonByGesture(VRCPlayerApi local)
        {
            PlayOpenHaptic(local);

            if (!menuVisible)
            {
                ShowMenu();
                return;
            }

            bool staffUnlocked = staffControlPanel != null && staffControlPanel.IsLocallyUnlocked();

            if (staffUnlocked && _crossfadeTarget < 0.5f)
            {
                _crossfadeTarget = 1f;
                _crossfadeActive = true;
            }
            else
            {
                SetMenuVisible(false);
                _crossfadeTarget = 0f;
                _crossfadeCurrent = 0f;
                _crossfadeActive = false;
                ApplyCrossfade();
            }
        }

        /// <summary>WallControlPanel から呼ばれ、ジェスチャーフラグを個別に設定する。ローカル設定のみで同期しない。</summary>
        public void SetSummonGestureFlag(int flag, bool enabled)
        {
            if (enabled)
                summonGesture |= flag;
            else
                summonGesture &= ~flag;
            // 全て無効になるのを防ぐ
            if (summonGesture == 0)
                summonGesture = flag;
            ResetGestureTimers();
        }

        public int GetSummonGesture() { return summonGesture; }

        public void SetDesktopSummonGestureFlag(int flag, bool enabled)
        {
            if (enabled)
                desktopSummonGesture |= flag;
            else
                desktopSummonGesture &= ~flag;
            if (desktopSummonGesture == 0)
                desktopSummonGesture = flag;
            ResetGestureTimers();
        }

        public int GetDesktopSummonGesture() { return desktopSummonGesture; }

        private void ResetGestureTimers()
        {
            _vrBothHoldElapsed = 0f;
            _vrStickUpHoldElapsed = 0f;
            _vrHoldConsumed = false;
            _vrStickHoldConsumed = false;
            _desktopEscHoldElapsed = 0f;
            _desktopEscHoldConsumed = false;
            _desktopTabDoubleTapConsumed = false;
            _desktopF5DoubleTapConsumed = false;
            if (hudProgress != null) hudProgress.Hide();
        }

        /// <summary>メニューを開いた合図として両手にハプティクスを送る。</summary>
        private void PlayOpenHaptic(VRCPlayerApi local)
        {
            if (local == null) return;
            if (openHapticDuration <= 0f) return;

            local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, openHapticDuration, openHapticAmplitude, openHapticFrequency);
            local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, openHapticDuration, openHapticAmplitude, openHapticFrequency);
        }

        /// <summary>Desktop モードでは TAB によるメニュー開イベントで表示する。</summary>
        public void OnMenuOpened()
        {
            _isVRChatMenuOpen = true;

            if (IsDesktopMode())
            {
                ShowMenu();
                return;
            }

            // VR モードでは VRChat メニュー表示中に干渉しないよう手元メニューを閉じる
            SetMenuVisible(false);
        }

        /// <summary>閉じるは Close ボタンに統一する。</summary>
        public void OnMenuClosed()
        {
            _isVRChatMenuOpen = false;
        }

        private bool IsDesktopMode()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return local != null && !local.IsUserInVR();
        }

        private void ShowMenu()
        {
            SetMenuVisible(true);
            if (followLocalHead || IsDesktopMode())
                UpdateMenuFollow();
            _lastSlowUpdateTime = 0f;
            _outOfSightSince = 0f;
            UpdateDisplay();
        }

        /// <summary>壁掛けパネルの Summon ボタンなど外部トリガーからローカルプレイヤーの前に出現させる。</summary>
        public void SummonInFrontOfLocalPlayer()
        {
            // followLocalHead が無効でも強制的に一度配置するため UpdateMenuFollow を明示呼び出しする。
            SetMenuVisible(true);
            UpdateMenuFollow();
            _lastSlowUpdateTime = 0f;
            UpdateDisplay();
        }

        /// <summary>
        /// パネル上の全 UI 要素を最新の controller 状態で再描画する。
        /// 状態テキスト、ゲージ、Resync/Reboot ボタンの有効/無効、クールダウン表示をまとめて更新する。
        /// UPDATE_INTERVAL ごとに呼ばれるため、高頻度の処理は避ける。
        /// </summary>
        private void UpdateDisplay()
        {
            if (controller == null) return;

            int localState = controller.GetLocalState();

            if (stateText != null)
            {
                string errorMsg = controller.GetLastErrorMessage();
                bool isPlaying = localState == LocalDualPlayerController.STATE_ACTIVE_PLAYING;
                bool hasError = !string.IsNullOrEmpty(errorMsg);
                bool showError = hasError && (!isPlaying || controller.GetErrorMessageAge() < errorDisplayDurationSec);

                string display;
                if (showError)
                    display = "Error: " + errorMsg;
                else
                    display = controller.GetLocalStateText();

                int stallCount = controller.GetConsecutiveStallCount();
                int failCount = controller.GetConsecutiveFailCount();
                if (stallCount > 0 || failCount > 0)
                {
                    display += " (";
                    if (stallCount > 0) display += $"Stall={stallCount}";
                    if (stallCount > 0 && failCount > 0) display += ", ";
                    if (failCount > 0) display += $"Fail={failCount}";
                    display += ")";
                }
                stateText.text = display;
            }

            UpdateGauges();

            if (resyncButton != null)
            {
                bool canRequest = localState == LocalDualPlayerController.STATE_ACTIVE_PLAYING;
                SetButtonInteractable(resyncButton, canRequest);
                bool isCooldown = localState == LocalDualPlayerController.STATE_COOLDOWN;
                bool isWaiting = localState == LocalDualPlayerController.STATE_REQUEST_PENDING
                    || localState == LocalDualPlayerController.STATE_RESERVED;
                bool showCountdown = isCooldown || isWaiting;
                if (_resyncButtonLabel != null)
                    _resyncButtonLabel.gameObject.SetActive(!showCountdown);
                if (_resyncCooldownLabel != null)
                {
                    _resyncCooldownLabel.gameObject.SetActive(showCountdown);
                    if (isCooldown)
                        _resyncCooldownLabel.text = $"\uE88B {controller.GetCooldownRemaining():F0}s";
                    else if (isWaiting)
                    {
                        int slotIndex = controller.GetMySlotIndex();
                        if (coordinator != null && slotIndex >= 0)
                        {
                            float waitSec = coordinator.EstimateWaitTime(slotIndex);
                            _resyncCooldownLabel.text = $"\uE88B ETA {waitSec:F0}s";
                        }
                        else
                        {
                            _resyncCooldownLabel.text = $"\uE88B ETA --";
                        }
                    }
                }
            }

            if (resyncButtonObject != null)
                resyncButtonObject.SetActive(true);

            SetButtonInteractable(rebootButton, controller.ShouldShowRebootButton());

            if (closeButtonObject != null)
                closeButtonObject.SetActive(true);
        }

        /// <summary>
        /// ドリフト残量ゲージを更新する。
        /// ドリフトゲージは「あとどれだけずれたら自動 Resync が発動するか」を、
        /// サイレンスメーターは 10Hz の専用更新パスで描画する。
        /// </summary>
        private void UpdateGauges()
        {
            if (controller == null) return;

            if (headroomGauge != null)
            {
                float thresholdMs = controller.GetDriftResyncThresholdSec() * 1000f;
                float driftMs = Mathf.Abs(controller.GetDriftAccumulator()) * 1000f;
                headroomGauge.maxValue = thresholdMs;
                headroomGauge.value = Mathf.Min(driftMs, thresholdMs);
            }
        }

        private void UpdateSilenceMeter(float now)
        {
            if (controller == null || silenceGauge == null) return;

            float rmsDbfs = Mathf.Clamp(controller.GetActiveRmsDbfsForMeter(),
                SILENCE_METER_MIN_DBFS, SILENCE_METER_MAX_DBFS);
            float thresholdDbfs = Mathf.Clamp(controller.GetActiveSilenceThresholdDbfsForMeter(),
                SILENCE_METER_MIN_DBFS, SILENCE_METER_MAX_DBFS);

            silenceGauge.minValue = SILENCE_METER_MIN_DBFS;
            silenceGauge.maxValue = SILENCE_METER_MAX_DBFS;
            silenceGauge.value = rmsDbfs;
            if (_silenceGaugeFillImage != null)
                _silenceGaugeFillImage.color = _silenceGaugeActiveColor;

            float holdSec = Mathf.Max(0f, silenceMeterPeakHoldSec);
            float decayDbPerSec = Mathf.Max(0f, silenceMeterPeakDecayDbPerSec);
            if (!_silencePeakInitialized || rmsDbfs >= _silencePeakDbfs)
            {
                _silencePeakDbfs = rmsDbfs;
                _silencePeakHoldUntil = now + holdSec;
                _silencePeakInitialized = true;
            }
            else if (now > _silencePeakHoldUntil)
            {
                float dt = Mathf.Max(0f, now - _lastSilenceMeterUpdateTime);
                if (decayDbPerSec <= 0f)
                    _silencePeakDbfs = rmsDbfs;
                else
                    _silencePeakDbfs = Mathf.Max(rmsDbfs, _silencePeakDbfs - decayDbPerSec * dt);
            }
            _silencePeakDbfs = Mathf.Clamp(_silencePeakDbfs, SILENCE_METER_MIN_DBFS, SILENCE_METER_MAX_DBFS);

            UpdateMeterMarker(silenceThresholdMarker, thresholdDbfs);
            UpdateMeterMarker(silencePeakMarker, _silencePeakDbfs);
        }

        private static void UpdateMeterMarker(Image marker, float dbfs)
        {
            if (marker == null) return;
            var rt = marker.rectTransform;
            float t = Mathf.InverseLerp(SILENCE_METER_MIN_DBFS, SILENCE_METER_MAX_DBFS, dbfs);
            rt.anchorMin = new Vector2(t, 0f);
            rt.anchorMax = new Vector2(t, 1f);
            rt.anchoredPosition = Vector2.zero;
        }

        // =================================================================
        //  ローカル設定 UI (Volume / Auto Silence Resync)
        // =================================================================

        private void SyncLocalSettingsUI()
        {
            if (volumeSlider != null && controller != null)
            {
                float vol = controller.GetVolume();
                volumeSlider.value = vol;
                _lastVolumeSliderValue = vol;
                _volumeSliderInitialized = true;
            }

            if (autoSilenceResyncToggle != null && controller != null)
            {
                bool enabled = controller.GetAutoSilenceResyncEnabled();
                autoSilenceResyncToggle.isOn = enabled;
                _lastAutoSilenceToggleState = enabled;
                _autoSilenceToggleInitialized = true;
            }
        }

        public void OnVolumeSliderChanged()
        {
            if (volumeSlider == null || controller == null) return;
            controller.SetVolumeLocal(volumeSlider.value);
            _lastVolumeSliderValue = volumeSlider.value;
            _volumeSliderInitialized = true;
        }

        private void PollVolumeSlider()
        {
            if (volumeSlider == null) return;
            if (!_volumeSliderInitialized)
            {
                _lastVolumeSliderValue = volumeSlider.value;
                _volumeSliderInitialized = true;
                return;
            }

            if (!Mathf.Approximately(volumeSlider.value, _lastVolumeSliderValue))
                OnVolumeSliderChanged();
        }

        public void OnAutoSilenceResyncToggleChanged()
        {
            if (controller == null || autoSilenceResyncToggle == null) return;
            controller.SetAutoSilenceResyncEnabled(autoSilenceResyncToggle.isOn);
            _lastAutoSilenceToggleState = autoSilenceResyncToggle.isOn;
            _autoSilenceToggleInitialized = true;
        }

        private void PollAutoSilenceToggle()
        {
            if (autoSilenceResyncToggle == null) return;
            if (!_autoSilenceToggleInitialized)
            {
                _lastAutoSilenceToggleState = autoSilenceResyncToggle.isOn;
                _autoSilenceToggleInitialized = true;
                return;
            }

            if (autoSilenceResyncToggle.isOn != _lastAutoSilenceToggleState)
                OnAutoSilenceResyncToggleChanged();
        }

        /// <summary>個人 Resync リクエスト (FR-16)。</summary>
        public void OnResyncButtonPress()
        {
            if (controller == null) return;
            controller.RequestManualResync();
        }

        /// <summary>緊急リブート (FR-16b)。</summary>
        public void OnRebootButtonPress()
        {
            if (controller == null) return;
            controller.Reboot();
        }

        /// <summary>
        /// 距離超過または視界外タイムアウトでパネルを自動的に閉じる。
        /// UPDATE_INTERVAL ごとに呼ばれる。掴み中は判定しない。
        /// </summary>
        private bool CheckAutoDismiss()
        {
            if (_isGrabbing) { _outOfSightSince = 0f; return false; }

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;

            Vector3 viewOrigin;
            Vector3 viewForward;

            if (local.IsUserInVR())
            {
                VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                viewOrigin = head.position;
                viewForward = head.rotation * Vector3.forward;
            }
            else
            {
                var cam = VRCCameraSettings.ScreenCamera;
                viewOrigin = cam.Position;
                viewForward = cam.Rotation * Vector3.forward;
            }

            Vector3 toPanel = transform.position - viewOrigin;

            // 距離判定
            if (_autoDismissSqrDist > 0f && toPanel.sqrMagnitude > _autoDismissSqrDist)
            {
                SetMenuVisible(false);
                return true;
            }

            // 視界判定: 正面方向とパネル方向の内積で大まかに判定（cos90° = 0 を閾値に）
            if (outOfSightDismissSec > 0f)
            {
                float dot = Vector3.Dot(viewForward, toPanel.normalized);
                if (dot < 0f)
                {
                    if (_outOfSightSince == 0f)
                        _outOfSightSince = Time.time;
                    if (Time.time - _outOfSightSince >= outOfSightDismissSec)
                    {
                        SetMenuVisible(false);
                        return true;
                    }
                }
                else
                {
                    _outOfSightSince = 0f;
                }
            }

            return false;
        }

        /// <summary>メニューを閉じる。</summary>
        public void OnCloseButtonPress()
        {
            SetMenuVisible(false);
        }

        // =================================================================
        //  デバッグ用: 同名ユーザー検出による自動パネルオープン
        // =================================================================

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            TryDebugAutoOpen();
        }

        private const float DEBUG_TELEPORT_OFFSET_X = 2f;
        private const float DEBUG_PANEL_DELAY_SEC = 3f;
        private bool _debugIsMinId;

        /// <summary>
        /// ワールド内に自分と同名のプレイヤーが他に存在する場合をテスト中と判定する。
        /// ID の順位から一意にテレポート先を決定し、数秒後にパネルを開く。
        /// - 最若 ID (rank 0): テレポートなし、スタッフ権限付与 + Staff ビュー
        /// - それ以外: rank × 2m だけ +X 方向にテレポート、デスクトップなら Viewer ビュー
        /// </summary>
        private void TryDebugAutoOpen()
        {
            if (_debugAutoOpenDone) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            string localName = local.displayName;
            int localId = local.playerId;

            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);

            bool hasDuplicate = false;
            bool isMinId = true;
            foreach (var p in players)
            {
                if (p == null) continue;
                if (p.playerId == localId) continue;
                if (p.displayName != localName) continue;
                hasDuplicate = true;
                if (p.playerId < localId)
                    isMinId = false;
            }

            if (!hasDuplicate) return;

            _debugAutoOpenDone = true;
            _debugIsMinId = isMinId;

            // ID から一意にテレポート先を決定（他プレイヤーの位置・人数に依存しない）
            Vector3 localPos = local.GetPosition();
            Vector3 teleportPos = localPos + new Vector3(DEBUG_TELEPORT_OFFSET_X * localId, 0f, 0f);
            local.TeleportTo(teleportPos, local.GetRotation());

            SendCustomEventDelayedSeconds(nameof(OnDebugAutoOpenDelayed), DEBUG_PANEL_DELAY_SEC);
        }

        public void OnDebugAutoOpenDelayed()
        {
            if (_debugIsMinId)
            {
                if (staffControlPanel != null)
                    staffControlPanel.SetLocalPasscodeUnlocked();
                _crossfadeTarget = 1f;
                _crossfadeCurrent = 1f;
                ApplyCrossfade();
                OnStaffUnlockStateChanged();
                SummonInFrontOfLocalPlayer();
            }
            else if (IsDesktopMode())
            {
                SummonInFrontOfLocalPlayer();
            }
        }
    }
}
