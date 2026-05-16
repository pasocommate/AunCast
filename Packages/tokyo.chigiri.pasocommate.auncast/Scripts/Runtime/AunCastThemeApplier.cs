using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// エディタ時専用のテーマ適用コンポーネント。AunCastTheme アセットを参照し、
    /// セットアップツールがシーン生成時に UI のマテリアル・フォント・配色を決定する。
    /// IEditorOnly のためビルドには含まれない。
    /// </summary>
    [DisallowMultipleComponent]
    public class AunCastThemeApplier : MonoBehaviour, IEditorOnly
    {
        public AunCastTheme theme;

        /// <summary>
        /// テーマの見た目（色・ボタン遷移・HUDマテリアル）を既存階層へ適用する。
        /// UdonSharp Proxy への SerializedField 反映はエディタ側のセットアップ処理が担当する。
        /// </summary>
        public void ApplyTheme(Transform root)
        {
            if (root == null || theme == null) return;

            ApplyVideoScreenAssets(root);

            const string pp = "PortablePanel/ContentScaler";
            const string staff = pp + "/PortableContentArea/StaffContent/StaffPadded";
            const string viewer = pp + "/PortableContentArea/UserContent/UserPadded";
            const string shared = pp + "/PortableContentArea/SharedContent/SharedPadded";
            const string topBar = pp + "/PortableContentArea/TopBarPadded";

            SetThemeImageColor(root, pp + "/Background", theme.userBackgroundColor);

            SetThemeImageColor(root, staff + "/PromoteNextButton", theme.warningColor);
            SetThemeImageColor(root, staff + "/StopButton", theme.dangerColor);
            SetThemeImageColor(root, staff + "/GlobalResyncButton", theme.primaryColor);
            SetThemeImageColor(root, staff + "/ForceRebootButton", theme.warningColor);
            string cdg = staff + "/ConcurrentDisplayGroup";
            SetThemeImageColor(root, cdg + "/ConcurrentChangeButton", theme.warningColor);
            string ceg = staff + "/ConcurrentEditGroup";
            SetThemeImageColor(root, ceg + "/ConcurrentSub10Button", theme.primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentSub1Button", theme.primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentAdd1Button", theme.primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentAdd10Button", theme.primaryColor);
            SetThemeImageColor(root, ceg + "/ConcurrentApplyButton", theme.dangerColor);
            SetThemeImageColor(root, ceg + "/ConcurrentCancelButton", theme.secondaryColor);
            string cndg = staff + "/ConnectionDisplayGroup";
            SetThemeImageColor(root, cndg + "/ConnectionChangeButton", theme.warningColor);
            string cneg = staff + "/ConnectionEditGroup";
            SetThemeImageColor(root, cneg + "/ConnectionSub10Button", theme.primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionSub1Button", theme.primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionAdd1Button", theme.primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionAdd10Button", theme.primaryColor);
            SetThemeImageColor(root, cneg + "/ConnectionApplyButton", theme.dangerColor);
            SetThemeImageColor(root, cneg + "/ConnectionCancelButton", theme.secondaryColor);
            SetThemeImageColor(root, staff + "/NextURLInputField", theme.inputBackgroundColor);
            SetThemeImageColor(root, ceg + "/ConcurrentLimitInput", theme.inputBackgroundColor);
            SetThemeImageColor(root, cneg + "/ConnectionLimitInput", theme.inputBackgroundColor);

            SetThemeImageColor(root, viewer + "/AutoResyncToggle/Background", theme.toggleBackgroundColor);
            SetThemeTextColor(root, viewer + "/AutoResyncToggle/Background/Checkmark", theme.toggleCheckmarkColor);

            SetThemeImageColor(root, shared + "/VolumeSlider/Background", theme.sliderBackgroundColor);
            SetThemeImageColor(root, shared + "/VolumeSlider/Fill Area/Fill", theme.sliderFillColor);
            SetThemeImageColor(root, shared + "/VolumeSlider/Handle Slide Area/Handle", theme.sliderHandleColor);
            SetThemeImageColor(root, shared + "/RebootButton", theme.warningColor);
            SetThemeImageColor(root, shared + "/ResyncButton", theme.primaryColor);

            SetThemeImageColor(root, topBar + "/CloseButton", theme.secondaryColor);
            SetThemeImageColor(root, topBar + "/SwitchViewButton", theme.secondaryColor);

            ApplyWallPanelTheme(root);

            if (theme.hudProgressMaterial != null)
                ApplyHudProgressMaterialFromTheme(theme.hudProgressMaterial);

            var hudOverlay = root.Find("HudProgressOverlay");
            if (hudOverlay != null)
            {
                var quadTf = hudOverlay.Find("Quad");
                if (quadTf != null)
                {
                    Vector3 scale = theme.hudProgressUsePieMode
                        ? new Vector3(theme.hudProgressPieDiameter, theme.hudProgressPieDiameter, 1f)
                        : new Vector3(theme.hudProgressBarSize.x, theme.hudProgressBarSize.y, 1f);
                    quadTf.localScale = scale;
                }
            }

            foreach (var btn in root.GetComponentsInChildren<Button>(true))
                btn.colors = theme.buttonTransitionColors;

            ApplyThemeTextColors(root);
        }

        private void ApplyVideoScreenAssets(Transform root)
        {
            var screen = root.Find("Screen");
            if (screen != null)
            {
                var renderer = screen.GetComponent<MeshRenderer>();
                if (renderer != null && theme.videoScreenMaterial != null)
                    renderer.sharedMaterial = theme.videoScreenMaterial;
            }

            if (theme.videoScreenMaterial != null && theme.videoScreenDefaultTexture != null)
            {
                if (theme.videoScreenMaterial.HasProperty("_EmissionMap"))
                    theme.videoScreenMaterial.SetTexture("_EmissionMap", theme.videoScreenDefaultTexture);
                if (theme.videoScreenMaterial.HasProperty("_MainTex"))
                    theme.videoScreenMaterial.SetTexture("_MainTex", theme.videoScreenDefaultTexture);
            }

            const string uiScreenPath = "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/VideoScreenArea/VideoScreen";
            var uiScreen = root.Find(uiScreenPath);
            if (uiScreen != null)
            {
                var rawImage = uiScreen.GetComponent<RawImage>();
                if (rawImage != null)
                {
                    if (theme.videoScreenUiMaterial != null)
                        rawImage.material = theme.videoScreenUiMaterial;
                    if (theme.videoScreenDefaultTexture != null)
                        rawImage.texture = theme.videoScreenDefaultTexture;
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
                SetThemeImageColor(wallRoot, "ContentScaler/Background", theme.wallBackgroundColor);

                string keypad = wall + "/StaffContent/PasscodeKeypad";
                for (int i = 0; i < 10; i++)
                    SetThemeImageColor(wallRoot, keypad + $"/PasscodeKey{i}", theme.primaryColor);
                SetThemeImageColor(wallRoot, keypad + "/PasscodeBackspace", theme.warningColor);

                SetThemeImageColor(wallRoot, wall + "/SharedContent/SpawnPanelButton", theme.primaryColor);
                SetThemeImageColor(wallRoot, wall + "/SharedContent/SwitchViewButton", theme.secondaryColor);

                string wallUser = wall + "/UserContent";
                SetThemeImageColor(wallRoot, wallUser + "/UserResyncButton", theme.primaryColor);
                SetThemeImageColor(wallRoot, wallUser + "/UserRebootButton", theme.warningColor);
                SetThemeImageColor(wallRoot, wallUser + "/GestureRightStickUpToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureRightStickUpToggle/Background/Checkmark", theme.toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, wallUser + "/GestureDoubleTriggerToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureDoubleTriggerToggle/Background/Checkmark", theme.toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, wallUser + "/GestureBothTriggersToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureBothTriggersToggle/Background/Checkmark", theme.toggleCheckmarkColor);
            }
        }

        private void ApplyWallPanelTextColors(Transform root)
        {
            const string wall = "ContentScaler/WallContentArea";
            foreach (var wallRoot in CollectWallPanelRoots(root))
            {
                SetThemeTextColor(wallRoot, wall + "/SharedContent/SpawnPanelButton/Label", theme.buttonLabelColor);
                SetThemeTextColor(wallRoot, wall + "/SharedContent/SwitchViewButton/Label", theme.buttonLabelColor);
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
                SetThemeTextColor(root, path, theme.headingTextColor);
            foreach (var path in buttonLabelPaths)
                SetThemeTextColor(root, path, theme.buttonLabelColor);
            foreach (var path in bodyPaths)
                SetThemeTextColor(root, path, theme.bodyTextColor);
            foreach (var path in inputTextPaths)
                SetThemeTextColor(root, path, theme.inputTextColor);
            foreach (var path in placeholderPaths)
                SetThemeTextColor(root, path, theme.placeholderTextColor);

            ApplyWallPanelTextColors(root);
        }

        private void ApplyHudProgressMaterialFromTheme(Material mat)
        {
            if (mat == null) return;
            mat.SetColor("_BaseColor", theme.hudProgressBaseColor);
            mat.SetColor("_FillColor", theme.hudProgressFillColor);
            mat.SetFloat("_MODE", theme.hudProgressUsePieMode ? 1f : 0f);
            mat.SetFloat("_RingThickness", theme.hudProgressPieRingThickness);
            mat.DisableKeyword("_MODE_BAR");
            mat.DisableKeyword("_MODE_PIE");
            mat.EnableKeyword(theme.hudProgressUsePieMode ? "_MODE_PIE" : "_MODE_BAR");

            bool hasDecal = theme.hudProgressDecalTexture != null;
            mat.SetFloat("_DecalOn", hasDecal ? 1f : 0f);
            if (hasDecal)
            {
                mat.EnableKeyword("_DECAL_ON");
                mat.SetTexture("_DecalTex", theme.hudProgressDecalTexture);
                mat.SetColor("_DecalColor", theme.hudProgressDecalColor);
                mat.SetVector("_DecalScale", theme.hudProgressDecalScale);
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
