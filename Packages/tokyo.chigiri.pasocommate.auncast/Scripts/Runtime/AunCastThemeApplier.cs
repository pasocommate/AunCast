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
        /// テーマの見た目（マテリアル・フォント・色・ボタン遷移・HUD）を既存階層へ適用する。
        /// UdonSharp Proxy への SerializedField 反映はエディタ側のセットアップ処理が担当する。
        /// </summary>
        public void ApplyTheme(Transform root)
        {
            if (root == null || theme == null) return;

            ApplyMaterials(root);
            ApplyFonts(root);
            ApplyAudioLinkMaterials(root);

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
            SetThemeImageColor(root, staff + "/NowPlayingArea", theme.inputBackgroundColor);
            SetThemeImageColor(root, shared + "/HelpArea", theme.inputBackgroundColor);

            SetThemeImageColor(root, viewer + "/AutoResyncToggle/Background", theme.toggleBackgroundColor);
            SetThemeTextColor(root, viewer + "/AutoResyncToggle/Background/Checkmark", theme.toggleCheckmarkColor);

            SetThemeImageColor(root, shared + "/VolumeSlider/Background", theme.sliderBackgroundColor);
            SetThemeImageColor(root, shared + "/VolumeSlider/Fill Area/Fill", theme.sliderFillColor);
            SetThemeImageColor(root, shared + "/VolumeSlider/Handle Slide Area/Handle", theme.sliderHandleColor);

            SetThemeImageColor(root, viewer + "/HeadroomGauge/Background", theme.sliderBackgroundColor);
            SetThemeImageColor(root, viewer + "/HeadroomGauge/Fill Area/Fill", theme.sliderFillColor);
            SetThemeImageColor(root, viewer + "/SilenceGauge/Background", theme.sliderBackgroundColor);
            SetThemeImageColor(root, viewer + "/SilenceGauge/Fill Area/Fill", theme.sliderFillColor);
            SetThemeImageColor(root, viewer + "/SilenceGauge/Handle Slide Area/Handle", theme.sliderHandleColor);

            SetThemeImageColor(root, shared + "/RebootButton", theme.warningColor);
            SetThemeImageColor(root, shared + "/ResyncButton", theme.primaryColor);

            SetThemeImageColor(root, topBar + "/CloseButton", theme.secondaryColor);
            SetThemeImageColor(root, topBar + "/SwitchViewButton", theme.secondaryColor);

            ApplyDecalColors(root);
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

#if UNITY_EDITOR
            RecordPrefabOverrides(root);
#endif
        }

#if UNITY_EDITOR
        private static void RecordPrefabOverrides(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(img);
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(raw);
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(tmp);
            foreach (var btn in root.GetComponentsInChildren<Button>(true))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(btn);
            var hudOverlay = root.Find("HudProgressOverlay");
            if (hudOverlay != null)
            {
                var quadTf = hudOverlay.Find("Quad");
                if (quadTf != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(quadTf);
            }
        }
#endif

        private void ApplyMaterials(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var mat = ResolveImageMaterial(img);
                if (mat != null)
                    img.material = mat;
            }
        }

        private Material ResolveImageMaterial(Image img)
        {
            var name = img.gameObject.name;

            if (name.EndsWith("_Inner"))
            {
                if (name == "TitleDecal_Inner")
                    return theme.decal2Material;
                if (name == "DecalDiamond_Inner")
                    return theme.decal1Material;
                if (name.Contains("Input") || name.Contains("Area"))
                    return theme.inputMaterial;
                if (name.StartsWith("CloseButton") || name.StartsWith("SwitchViewButton"))
                    return theme.buttonRoundMaterial;
                if (name.Contains("Button") || name.Contains("Key") || name.Contains("Backspace"))
                    return theme.buttonRectMaterial;
                return null;
            }

            if (name == "Background" && img.transform.parent != null
                && img.transform.parent.name == "ContentScaler")
            {
                var contentScaler = img.transform.parent;
                if (contentScaler.Find("WallContentArea") != null)
                    return theme.bgWallMaterial;
                return theme.bgPortableMaterial;
            }

            if (name == "Fill" && img.transform.parent != null
                && img.transform.parent.name == "Fill Area")
                return theme.buttonRectMaterial;

            if (name == "Handle")
                return theme.handleMaterial;

            if (name == "Background" && img.transform.parent != null)
            {
                var parentGo = img.transform.parent.gameObject;
                if (parentGo.GetComponent<Slider>() != null
                    || parentGo.GetComponent<Toggle>() != null)
                    return theme.inputMaterial;
            }

            return null;
        }


        private void ApplyFonts(Transform root)
        {
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var font = ResolveFont(tmp);
                if (font != null)
                    tmp.font = font;
            }
        }

        private TMP_FontAsset ResolveFont(TMP_Text tmp)
        {
            var name = tmp.gameObject.name;
            if (name.Contains("Indicator"))
                return theme.bodyFont;
            if (name.EndsWith("Label") || name == "Checkmark")
                return theme.displayFont;
            if (name.Contains("Display") || name == "UserCountText")
                return theme.displayFont;
            return theme.bodyFont;
        }

        private void ApplyDecalColors(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var name = img.gameObject.name;
                if (name == "TitleDecal_Inner" || name == "DecalDiamond_Inner")
                    img.color = theme.decalColor;
            }
        }

        private void ApplyAudioLinkMaterials(Transform root)
        {
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
            {
                var name = raw.gameObject.name;
                if (name == "AL4BandHistory" && theme.al4BandHistoryMaterial != null)
                    raw.material = theme.al4BandHistoryMaterial;
                else if (name == "ALAutoCorrelator" && theme.alAutoCorrelatorMaterial != null)
                    raw.material = theme.alAutoCorrelatorMaterial;
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

                string vrGroup = wallUser + "/VrGestureGroup";
                SetThemeImageColor(wallRoot, vrGroup + "/GestureRightStickUpToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, vrGroup + "/GestureRightStickUpToggle/Background/Checkmark", theme.toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, vrGroup + "/GestureDoubleTriggerToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, vrGroup + "/GestureDoubleTriggerToggle/Background/Checkmark", theme.toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, vrGroup + "/GestureBothTriggersToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, vrGroup + "/GestureBothTriggersToggle/Background/Checkmark", theme.toggleCheckmarkColor);

                string desktopGroup = wallUser + "/DesktopGestureGroup";
                SetThemeImageColor(wallRoot, desktopGroup + "/DesktopEscHoldToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, desktopGroup + "/DesktopEscHoldToggle/Background/Checkmark", theme.toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, desktopGroup + "/DesktopF5DoubleTapToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, desktopGroup + "/DesktopF5DoubleTapToggle/Background/Checkmark", theme.toggleCheckmarkColor);
                SetThemeImageColor(wallRoot, desktopGroup + "/DesktopTabDoubleTapToggle/Background", theme.toggleBackgroundColor);
                SetThemeTextColor(wallRoot, desktopGroup + "/DesktopTabDoubleTapToggle/Background/Checkmark", theme.toggleCheckmarkColor);

                SetThemeImageColor(wallRoot, wall + "/ResyncOnlyContent/ResyncOnlyButton", theme.primaryColor);
            }
        }

        private void ApplyWallPanelTextColors(Transform root)
        {
            const string wall = "ContentScaler/WallContentArea";
            foreach (var wallRoot in CollectWallPanelRoots(root))
            {
                SetThemeTextColor(wallRoot, wall + "/SharedContent/SpawnPanelButton/SpawnPanelButton_Inner/Label", theme.buttonLabelColor);
                SetThemeTextColor(wallRoot, wall + "/SharedContent/SwitchViewButton/SwitchViewButton_Inner/Label", theme.buttonLabelColor);

                string wallUser = wall + "/UserContent";
                SetThemeTextColor(wallRoot, wallUser + "/UserResyncButton/UserResyncButton_Inner/Label", theme.buttonLabelColor);
                SetThemeTextColor(wallRoot, wallUser + "/UserRebootButton/UserRebootButton_Inner/Label", theme.buttonLabelColor);

                string vrGroup = wallUser + "/VrGestureGroup";
                SetThemeTextColor(wallRoot, vrGroup + "/GestureRightStickUpToggle/Label", theme.headingTextColor);
                SetThemeTextColor(wallRoot, vrGroup + "/GestureDoubleTriggerToggle/Label", theme.headingTextColor);
                SetThemeTextColor(wallRoot, vrGroup + "/GestureBothTriggersToggle/Label", theme.headingTextColor);

                string desktopGroup = wallUser + "/DesktopGestureGroup";
                SetThemeTextColor(wallRoot, desktopGroup + "/DesktopEscHoldToggle/Label", theme.headingTextColor);
                SetThemeTextColor(wallRoot, desktopGroup + "/DesktopF5DoubleTapToggle/Label", theme.headingTextColor);
                SetThemeTextColor(wallRoot, desktopGroup + "/DesktopTabDoubleTapToggle/Label", theme.headingTextColor);

                string wallStaff = wall + "/StaffContent";
                SetThemeTextColor(wallRoot, wallStaff + "/PasscodeDisplay", theme.bodyTextColor);
                string keypad = wallStaff + "/PasscodeKeypad";
                for (int i = 0; i < 10; i++)
                    SetThemeTextColor(wallRoot, keypad + $"/PasscodeKey{i}/PasscodeKey{i}_Inner/Label", theme.buttonLabelColor);
                SetThemeTextColor(wallRoot, keypad + "/PasscodeBackspace/PasscodeBackspace_Inner/Label", theme.buttonLabelColor);

                string resyncOnly = wall + "/ResyncOnlyContent/ResyncOnlyButton/ResyncOnlyButton_Inner";
                SetThemeTextColor(wallRoot, resyncOnly + "/Label", theme.buttonLabelColor);
                SetThemeTextColor(wallRoot, resyncOnly + "/TextLabel", theme.bodyTextColor);
                SetThemeTextColor(wallRoot, wallUser + "/GestureLabel", theme.headingTextColor);
            }
        }

        private void ApplyThemeTextColors(Transform root)
        {
            const string pp = "PortablePanel/ContentScaler";
            const string staff = pp + "/PortableContentArea/StaffContent/StaffPadded";
            const string viewer = pp + "/PortableContentArea/UserContent/UserPadded";
            const string shared = pp + "/PortableContentArea/SharedContent/SharedPadded";
            const string topBar = pp + "/PortableContentArea/TopBarPadded";
            string ceg = staff + "/ConcurrentEditGroup";
            string cneg = staff + "/ConnectionEditGroup";
            string cdg = staff + "/ConcurrentDisplayGroup";
            string cndg = staff + "/ConnectionDisplayGroup";

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
                staff + "/PromoteNextButton/PromoteNextButton_Inner/Label",
                staff + "/StopButton/StopButton_Inner/Label",
                staff + "/GlobalResyncButton/GlobalResyncButton_Inner/Label",
                staff + "/ForceRebootButton/ForceRebootButton_Inner/Label",
                cdg + "/ConcurrentChangeButton/ConcurrentChangeButton_Inner/Label",
                ceg + "/ConcurrentSub10Button/ConcurrentSub10Button_Inner/Label",
                ceg + "/ConcurrentSub1Button/ConcurrentSub1Button_Inner/Label",
                ceg + "/ConcurrentAdd1Button/ConcurrentAdd1Button_Inner/Label",
                ceg + "/ConcurrentAdd10Button/ConcurrentAdd10Button_Inner/Label",
                ceg + "/ConcurrentApplyButton/ConcurrentApplyButton_Inner/Label",
                ceg + "/ConcurrentCancelButton/ConcurrentCancelButton_Inner/Label",
                cndg + "/ConnectionChangeButton/ConnectionChangeButton_Inner/Label",
                cneg + "/ConnectionSub10Button/ConnectionSub10Button_Inner/Label",
                cneg + "/ConnectionSub1Button/ConnectionSub1Button_Inner/Label",
                cneg + "/ConnectionAdd1Button/ConnectionAdd1Button_Inner/Label",
                cneg + "/ConnectionAdd10Button/ConnectionAdd10Button_Inner/Label",
                cneg + "/ConnectionApplyButton/ConnectionApplyButton_Inner/Label",
                cneg + "/ConnectionCancelButton/ConnectionCancelButton_Inner/Label",
                shared + "/RebootButton/RebootButton_Inner/Label",
                shared + "/ResyncButton/ResyncButton_Inner/Label",
                shared + "/ResyncButton/ResyncButton_Inner/CooldownLabel",
                topBar + "/CloseButton/CloseButton_Inner/Label",
                topBar + "/SwitchViewButton/SwitchViewButton_Inner/Label",
            };

            string[] bodyPaths =
            {
                staff + "/NowPlayingArea/NowPlayingArea_Inner/NowPlayingText",
                staff + "/UserCountText",
                staff + "/IndicatorText",
                cdg + "/ConcurrentLimitDisplayText",
                cndg + "/ConnectionLimitDisplayText",
                shared + "/HelpArea/HelpArea_Inner/HelpText",
                viewer + "/StateText",
                viewer + "/ErrorText",
            };

            string[] inputTextPaths =
            {
                staff + "/NextURLInputField/NextURLInputField_Inner/Viewport/Text",
                ceg + "/ConcurrentLimitInput/ConcurrentLimitInput_Inner/Viewport/Text",
                cneg + "/ConnectionLimitInput/ConnectionLimitInput_Inner/Viewport/Text",
            };

            string[] placeholderPaths =
            {
                staff + "/NextURLInputField/NextURLInputField_Inner/Placeholder",
                staff + "/NextURLInputField/NextURLInputField_Inner/Viewport/Placeholder",
                ceg + "/ConcurrentLimitInput/ConcurrentLimitInput_Inner/Placeholder",
                ceg + "/ConcurrentLimitInput/ConcurrentLimitInput_Inner/Viewport/Placeholder",
                cneg + "/ConnectionLimitInput/ConnectionLimitInput_Inner/Placeholder",
                cneg + "/ConnectionLimitInput/ConnectionLimitInput_Inner/Viewport/Placeholder",
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
