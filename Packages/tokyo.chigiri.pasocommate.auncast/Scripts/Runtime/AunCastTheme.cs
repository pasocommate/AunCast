using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// エディタ時専用のテーマ定義。セットアップツールがシーン生成時に参照し、
    /// 生成される全 UI のマテリアル・フォント・配色を決定する。IEditorOnly のためビルドには含まれない。
    /// </summary>
    [DisallowMultipleComponent]
    public class AunCastTheme : MonoBehaviour, IEditorOnly
    {
        // UI パネル・ボタン・スクリーン等の各パーツに割り当てるマテリアル
        [Header("Materials")]
        public Material bgWallMaterial;
        public Material bgPortableMaterial;
        public Material buttonRectMaterial;
        public Material buttonRoundMaterial;
        public Material inputMaterial;
        public Material decal1Material;
        public Material decal2Material;
        public Material videoScreenMaterial;
        public Material videoScreenUiMaterial;
        public Material rtGrabMaterial;
        public Material handleMaterial;
        public Material hudProgressMaterial;

        // 映像未受信時にスクリーンに表示するデフォルト画像
        [Header("Textures")]
        public Texture2D videoScreenDefaultTexture;

        // 見出し用と本文用のフォント
        [Header("Fonts")]
        public TMP_FontAsset displayFont;
        public TMP_FontAsset bodyFont;

        // Viewer/Staff/Wall パネル背景色
        [Header("Panel Background")]
        public Color userBackgroundColor = new Color(0.23f, 0.38f, 0.45f);
        public Color staffBackgroundColor = new Color(0.56f, 0.48f, 0.77f);
        public Color wallBackgroundColor = new Color(0.23f, 0.38f, 0.45f);

        [Header("Decal")]
        public Color decalColor = Color.white;

        // ボタン種別ごとの色テーマ
        [Header("Buttons")]
        public Color primaryColor = new Color(0f, 0.33f, 1f);
        public Color secondaryColor = new Color(0.38f, 0.38f, 0.42f);
        public Color warningColor = new Color(1f, 0.37f, 0f);
        public Color dangerColor = new Color(1f, 0f, 0f);

        [Header("Input")]
        public Color inputBackgroundColor = new Color(0.156862f, 0.156862f, 0.156862f, 1f);

        [Header("Toggle")]
        public Color toggleBackgroundColor = new Color(0.156862f, 0.156862f, 0.156862f, 1f);
        public Color toggleCheckmarkColor = Color.white;

        [Header("Slider")]
        public Color sliderBackgroundColor = new Color(0.156862f, 0.156862f, 0.156862f, 1f);
        public Color sliderFillColor = new Color(0f, 0.33f, 1f);
        public Color sliderHandleColor = new Color(0.5f, 0.5f, 0.5f);

        [Header("Disabled State")]
        [Tooltip("ボタンが無効のときにラベルへ適用するアルファ値 (0〜1)")]
        [Range(0f, 1f)]
        public float disabledButtonLabelAlpha = 0.5f;

        [Header("Button Transition")]
        public ColorBlock buttonTransitionColors = new ColorBlock
        {
            normalColor = new Color(1f, 1f, 1f, 1f),
            highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
            pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f),
            selectedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
            disabledColor = new Color(0.50f, 0.50f, 0.50f, 0.8f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f,
        };

        [Header("Text")]
        public Color headingTextColor = Color.white;
        public Color buttonLabelColor = Color.white;
        public Color bodyTextColor = new Color(0.75f, 0.75f, 0.78f);
        public Color inputTextColor = Color.white;
        public Color placeholderTextColor = new Color(0.60f, 0.60f, 0.60f, 1f);

        // VR ジェスチャー長押し時に視界に出すプログレス表示の形状・色
        [Header("HUD Progress (gesture long-press)")]
        [Tooltip("プログレス HUD の表示モード。false=バー、true=パイ。")]
        public bool hudProgressUsePieMode = true;

        [Tooltip("頭部ローカル座標における HUD の配置オフセット（メートル）。Z は前方距離、Y は高さ、X は左右ずらし。")]
        public Vector3 hudProgressLocalOffset = new Vector3(0f, -0.18f, 0.6f);

        [Tooltip("バーのサイズ（メートル）。X=幅、Y=高さ。Pie モードでは無視される。")]
        public Vector2 hudProgressBarSize = new Vector2(0.18f, 0.018f);

        [Tooltip("パイの直径（メートル）。Bar モードでは無視される。")]
        public float hudProgressPieDiameter = 0.09f;

        [Tooltip("リング太さ（0..1）。0 で塗りつぶし円、1 で極細リング。Pie モード専用。")]
        [Range(0f, 1f)] public float hudProgressPieRingThickness = 0.1f;

        [Tooltip("Pie 中央に表示する MSDF デカールテクスチャ。null で無効。")]
        public Texture2D hudProgressDecalTexture;

        [Tooltip("デカール色（HDR・アルファ可）")]
        [ColorUsage(true, true)] public Color hudProgressDecalColor = Color.white;

        [Tooltip("デカールの表示スケール（Pie 直径に対する比率）。X=幅、Y=高さ。")]
        public Vector2 hudProgressDecalScale = new Vector2(0.8f, 0.1f);

        [Tooltip("塗り色（HDR・アルファ可）")]
        [ColorUsage(true, true)] public Color hudProgressFillColor = new Color(0f, 0.75f, 1f, 0.5f);

        [Tooltip("ベース色（HDR・アルファ可）。進捗未到達部分の背景。")]
        [ColorUsage(true, true)] public Color hudProgressBaseColor = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>
        /// テーマの見た目（色・ボタン遷移・HUDマテリアル）を既存階層へ適用する。
        /// UdonSharp Proxy への SerializedField 反映はエディタ側のセットアップ処理が担当する。
        /// </summary>
        public void ApplyTheme(Transform root)
        {
            if (root == null) return;

            ApplyVideoScreenAssets(root);

            const string pp = "PortablePanel/ContentScaler";
            const string staff = pp + "/PortableContentArea/StaffContent/StaffPadded";
            const string viewer = pp + "/PortableContentArea/UserContent/UserPadded";
            const string shared = pp + "/PortableContentArea/SharedContent/SharedPadded";
            const string topBar = pp + "/PortableContentArea/TopBarPadded";

            SetThemeImageColor(root, pp + "/Background", userBackgroundColor);

            SetThemeImageColor(root, staff + "/PromoteNextButton", warningColor);
            SetThemeImageColor(root, staff + "/StopButton", dangerColor);
            SetThemeImageColor(root, staff + "/GlobalResyncButton", primaryColor);
            SetThemeImageColor(root, staff + "/ForceRebootButton", warningColor);
            string cdg = staff + "/ConcurrentDisplayGroup";
            SetThemeImageColor(root, cdg + "/ConcurrentChangeButton", warningColor);
            string ceg = staff + "/ConcurrentEditGroup";
            SetThemeImageColor(root, ceg + "/ConcurrentSub10Button", primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentSub1Button", primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentAdd1Button", primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentAdd10Button", primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentApplyButton", dangerColor);
            SetThemeImageColor(root, ceg + "/ConcurrentCancelButton", secondaryColor);
            string cndg = staff + "/ConnectionDisplayGroup";
            SetThemeImageColor(root, cndg + "/ConnectionChangeButton", warningColor);
            string cneg = staff + "/ConnectionEditGroup";
            SetThemeImageColor(root, cneg + "/ConnectionSub10Button", primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionSub1Button", primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionAdd1Button", primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionAdd10Button", primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionApplyButton", dangerColor);
            SetThemeImageColor(root, cneg + "/ConnectionCancelButton", secondaryColor);
            SetThemeImageColor(root, staff + "/NextURLInputField", inputBackgroundColor);
            SetThemeImageColor(root, ceg + "/ConcurrentLimitInput", inputBackgroundColor);
            SetThemeImageColor(root, cneg + "/ConnectionLimitInput", inputBackgroundColor);

            SetThemeImageColor(root, viewer + "/AutoResyncToggle/Background", toggleBackgroundColor);
            SetThemeTextColor(root, viewer + "/AutoResyncToggle/Background/Checkmark", toggleCheckmarkColor);

            SetThemeImageColor(root, shared + "/VolumeSlider/Background", sliderBackgroundColor);
            SetThemeImageColor(root, shared + "/VolumeSlider/Fill Area/Fill", sliderFillColor);
            SetThemeImageColor(root, shared + "/VolumeSlider/Handle Slide Area/Handle", sliderHandleColor);
            SetThemeImageColor(root, shared + "/RebootButton", warningColor);
            SetThemeImageColor(root, shared + "/ResyncButton", primaryColor);

            SetThemeImageColor(root, topBar + "/CloseButton", secondaryColor);
            SetThemeImageColor(root, topBar + "/SwitchViewButton", secondaryColor);

            ApplyWallPanelTheme(root);

            if (hudProgressMaterial != null)
                ApplyHudProgressMaterialFromTheme(hudProgressMaterial);

            var hudOverlay = root.Find("HudProgressOverlay");
            if (hudOverlay != null)
            {
                var quadTf = hudOverlay.Find("Quad");
                if (quadTf != null)
                {
                    Vector3 scale = hudProgressUsePieMode
                        ? new Vector3(hudProgressPieDiameter, hudProgressPieDiameter, 1f)
                        : new Vector3(hudProgressBarSize.x, hudProgressBarSize.y, 1f);
                    quadTf.localScale = scale;
                }
            }

            foreach (var btn in root.GetComponentsInChildren<Button>(true))
                btn.colors = buttonTransitionColors;

            ApplyThemeTextColors(root);
        }

        /// <summary>
        /// 3D スクリーン（MeshRenderer）と UI RawImage に、テーマで指定された
        /// ビデオ用マテリアル・デフォルトテクスチャを適用する。
        /// VideoMeshScreen / VideoUiScreen 自体のパラメータは別経路で更新される。
        /// </summary>
        private void ApplyVideoScreenAssets(Transform root)
        {
            // 3D スクリーン側: MeshRenderer の sharedMaterial を差し替え、
            // 同マテリアルを共有する全スクリーンへ自動反映する
            var screen = root.Find("Screen");
            if (screen != null)
            {
                var renderer = screen.GetComponent<MeshRenderer>();
                if (renderer != null && videoScreenMaterial != null)
                    renderer.sharedMaterial = videoScreenMaterial;
            }

            // 映像未受信時のデフォルト絵柄をマテリアル側にも書き込んでおく。
            // 再生開始時に VideoMeshScreen が上書きするため副作用は許容範囲
            if (videoScreenMaterial != null && videoScreenDefaultTexture != null)
            {
                if (videoScreenMaterial.HasProperty("_EmissionMap"))
                    videoScreenMaterial.SetTexture("_EmissionMap", videoScreenDefaultTexture);
                if (videoScreenMaterial.HasProperty("_MainTex"))
                    videoScreenMaterial.SetTexture("_MainTex", videoScreenDefaultTexture);
            }

            // UI RawImage 側: マテリアルと初期テクスチャを設定
            const string uiScreenPath = "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/VideoScreenArea/VideoScreen";
            var uiScreen = root.Find(uiScreenPath);
            if (uiScreen != null)
            {
                var rawImage = uiScreen.GetComponent<RawImage>();
                if (rawImage != null)
                {
                    if (videoScreenUiMaterial != null)
                        rawImage.material = videoScreenUiMaterial;
                    if (videoScreenDefaultTexture != null)
                        rawImage.texture = videoScreenDefaultTexture;
                }
            }
        }

        private static void SetThemeImageColor(Transform root, string path, Color color)
        {
            var t = root.Find(path);
            if (t == null) return;
            var image = t.GetComponent<Image>();
            if (image == null)
            {
                var inner = t.Find(t.name + "_Inner");
                if (inner != null)
                    image = inner.GetComponent<Image>();
            }
            if (image != null)
                image.color = color;
        }

        private static void SetThemeTextColor(Transform root, string path, Color color)
        {
            var t = root.Find(path);
            if (t == null) return;
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
                tmp.color = color;
        }

        private static List<Transform> CollectWallPanelRoots(Transform root)
        {
            var roots = new List<Transform>();
            if (root == null) return roots;

            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                if (!HasWallControlPanelProxy(t)) continue;
                if (t.Find("ContentScaler/WallContentArea") == null) continue;
                if (t.Find("ContentScaler/WallContentArea/SharedContent/SpawnPanelButton") == null) continue;
                roots.Add(t);
            }
            return roots;
        }

        private static bool HasWallControlPanelProxy(Transform root)
        {
            // Runtime asmdef は Udon asmdef を参照しないため WallControlPanel 型を
            // 直接参照できず、型名・名前空間の文字列一致で判定する。
            // WallControlPanel をリネームしたらここも合わせて更新する。
            var behaviours = root.GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null) continue;
                var type = behaviour.GetType();
                if (type.Name != "WallControlPanel") continue;
                if (type.Namespace != "PasocomMate.AunCast") continue;
                return true;
            }
            return false;
        }

        private void ApplyWallPanelTheme(Transform root)
        {
            const string wall = "ContentScaler/WallContentArea";
            foreach (var wallRoot in CollectWallPanelRoots(root))
            {
                SetThemeImageColor(wallRoot, "ContentScaler/Background", wallBackgroundColor);

                string keypad = wall + "/StaffContent/PasscodeKeypad";
                for (int i = 0; i < 10; i++)
                    SetThemeImageColor(wallRoot, keypad + $"/PasscodeKey{i}", primaryColor);
                SetThemeImageColor(wallRoot, keypad + "/PasscodeBackspace", warningColor);

                SetThemeImageColor(wallRoot, wall + "/SharedContent/SpawnPanelButton", primaryColor);
                SetThemeImageColor(wallRoot, wall + "/SharedContent/SwitchViewButton", secondaryColor);

                string wallUser = wall + "/UserContent";
                SetThemeImageColor(wallRoot, wallUser + "/UserResyncButton", primaryColor);
                SetThemeImageColor(wallRoot, wallUser + "/UserRebootButton", warningColor);
                SetThemeImageColor(wallRoot, wallUser + "/GestureRightStickUpToggle/Background", toggleBackgroundColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureRightStickUpToggle/Background/Checkmark", toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, wallUser + "/GestureDoubleTriggerToggle/Background", toggleBackgroundColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureDoubleTriggerToggle/Background/Checkmark", toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, wallUser + "/GestureBothTriggersToggle/Background", toggleBackgroundColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureBothTriggersToggle/Background/Checkmark", toggleCheckmarkColor);
            }
        }

        private void ApplyWallPanelTextColors(Transform root)
        {
            const string wall = "ContentScaler/WallContentArea";
            foreach (var wallRoot in CollectWallPanelRoots(root))
            {
                SetThemeTextColor(wallRoot, wall + "/SharedContent/SpawnPanelButton/Label", buttonLabelColor);
                SetThemeTextColor(wallRoot, wall + "/SharedContent/SwitchViewButton/Label", buttonLabelColor);
            }
        }

        private void ApplyThemeTextColors(Transform root)
        {
            const string pp = "PortablePanel/ContentScaler";
            const string staff = pp + "/PortableContentArea/StaffContent/StaffPadded";
            const string viewer = pp + "/PortableContentArea/UserContent/UserPadded";
            const string shared = pp + "/PortableContentArea/SharedContent/SharedPadded";
            const string topBar = pp + "/PortableContentArea/TopBarPadded";
            string clc = staff + "/ConcurrentLimitControls";

            string[] headingPaths =
            {
                staff + "/PlayingLabel",
                staff + "/NextURLLabel",
                staff + "/ConcurrentMaxLabel",
                staff + "/ConnectionMaxLabel",
                viewer + "/HeadroomGaugeLabel",
                viewer + "/SilenceGaugeLabel",
                viewer + "/AutoResyncToggle/Label",
                shared + "/VolumeLabel",
            };

            string[] buttonLabelPaths =
            {
                staff + "/PromoteNextButton/Label",
                staff + "/StopButton/Label",
                staff + "/GlobalResyncButton/Label",
                staff + "/ForceRebootButton/Label",
                clc + "/ConcurrentSub10Button/Label",
                clc + "/ConcurrentSub1Button/Label",
                clc + "/ConcurrentAdd1Button/Label",
                clc + "/ConcurrentAdd10Button/Label",
                shared + "/RebootButton/Label",
                shared + "/ResyncButton/Label",
                topBar + "/CloseButton/Label",
                topBar + "/SwitchViewButton/Label",
            };

            string[] bodyPaths =
            {
                staff + "/NowPlayingText",
                staff + "/HelpArea/HelpText",
                staff + "/MonitoringArea/MonitoringText",
                staff + "/IndicatorText",
                viewer + "/StateText",
                viewer + "/ErrorText",
            };

            string[] inputTextPaths =
            {
                staff + "/NextURLInputField/Viewport/Text",
                clc + "/ConcurrentLimitInput/Viewport/Text",
            };

            string[] placeholderPaths =
            {
                staff + "/NextURLInputField/Viewport/Placeholder",
                clc + "/ConcurrentLimitInput/Viewport/Placeholder",
            };

            foreach (var path in headingPaths)
                SetThemeTextColor(root, path, headingTextColor);
            foreach (var path in buttonLabelPaths)
                SetThemeTextColor(root, path, buttonLabelColor);
            foreach (var path in bodyPaths)
                SetThemeTextColor(root, path, bodyTextColor);
            foreach (var path in inputTextPaths)
                SetThemeTextColor(root, path, inputTextColor);
            foreach (var path in placeholderPaths)
                SetThemeTextColor(root, path, placeholderTextColor);

            ApplyWallPanelTextColors(root);
        }

        private void ApplyHudProgressMaterialFromTheme(Material mat)
        {
            if (mat == null) return;
            mat.SetColor("_BaseColor", hudProgressBaseColor);
            mat.SetColor("_FillColor", hudProgressFillColor);
            mat.SetFloat("_MODE", hudProgressUsePieMode ? 1f : 0f);
            mat.SetFloat("_RingThickness", hudProgressPieRingThickness);
            mat.DisableKeyword("_MODE_BAR");
            mat.DisableKeyword("_MODE_PIE");
            mat.EnableKeyword(hudProgressUsePieMode ? "_MODE_PIE" : "_MODE_BAR");

            bool hasDecal = hudProgressDecalTexture != null;
            mat.SetFloat("_DecalOn", hasDecal ? 1f : 0f);
            if (hasDecal)
            {
                mat.EnableKeyword("_DECAL_ON");
                mat.SetTexture("_DecalTex", hudProgressDecalTexture);
                mat.SetColor("_DecalColor", hudProgressDecalColor);
                mat.SetVector("_DecalScale", hudProgressDecalScale);
            }
            else
            {
                mat.DisableKeyword("_DECAL_ON");
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(mat);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(mat);
#endif
        }
    }
}
