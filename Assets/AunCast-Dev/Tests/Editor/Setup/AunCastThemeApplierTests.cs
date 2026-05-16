#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast.Tests
{
    [TestFixture]
    public class AunCastThemeApplierTests
    {
        private const string PREFAB_GUID = "0d617877712537147a584eb2a1ef735c";
        private const string THEME_GUID = "88cd0f2277ae3384d847b7bc9c6f405c";

        private GameObject _instance;
        private AunCastThemeApplier _applier;
        private AunCastTheme _theme;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var prefabPath = AssetDatabase.GUIDToAssetPath(PREFAB_GUID);
            Assert.IsNotNull(prefabPath, "プレハブ GUID が見つかりません");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, $"プレハブをロードできません: {prefabPath}");

            _instance = Object.Instantiate(prefab);
            _applier = _instance.GetComponent<AunCastThemeApplier>();
            Assert.IsNotNull(_applier, "AunCastThemeApplier がプレハブに存在しません");

            var themePath = AssetDatabase.GUIDToAssetPath(THEME_GUID);
            _theme = AssetDatabase.LoadAssetAtPath<AunCastTheme>(themePath);
            Assert.IsNotNull(_theme, $"テーマアセットをロードできません: {themePath}");

            _applier.theme = _theme;
            _applier.ApplyTheme(_instance.transform);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_instance != null)
                Object.DestroyImmediate(_instance);
        }

        // ── マテリアル適用テスト ──

        [Test]
        public void PortableBackground_HasBgPortableMaterial()
        {
            var bg = _instance.transform.Find("PortablePanel/ContentScaler/Background");
            Assert.IsNotNull(bg, "Background が見つかりません");
            var img = bg.GetComponent<Image>();
            Assert.IsNotNull(img);
            Assert.AreEqual(_theme.bgPortableMaterial, img.material,
                "PortablePanel の背景に bgPortableMaterial が適用されていません");
        }

        [Test]
        public void ButtonInner_HasButtonRectMaterial()
        {
            var paths = new[]
            {
                "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/PromoteNextButton/PromoteNextButton_Inner",
                "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/StopButton/StopButton_Inner",
                "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/GlobalResyncButton/GlobalResyncButton_Inner",
                "PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded/ResyncButton/ResyncButton_Inner",
                "PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded/RebootButton/RebootButton_Inner",
            };

            foreach (var path in paths)
            {
                var t = _instance.transform.Find(path);
                if (t == null) continue;
                var img = t.GetComponent<Image>();
                Assert.IsNotNull(img, $"Image が見つかりません: {path}");
                Assert.AreEqual(_theme.buttonRectMaterial, img.material,
                    $"buttonRectMaterial が適用されていません: {path}");
            }
        }

        [Test]
        public void RoundButtonInner_HasButtonRoundMaterial()
        {
            var paths = new[]
            {
                "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded/CloseButton/CloseButton_Inner",
                "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded/SwitchViewButton/SwitchViewButton_Inner",
            };

            foreach (var path in paths)
            {
                var t = _instance.transform.Find(path);
                if (t == null) continue;
                var img = t.GetComponent<Image>();
                Assert.IsNotNull(img, $"Image が見つかりません: {path}");
                Assert.AreEqual(_theme.buttonRoundMaterial, img.material,
                    $"buttonRoundMaterial が適用されていません: {path}");
            }
        }

        [Test]
        public void InputInner_HasInputMaterial()
        {
            var t = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/NextURLInputField/NextURLInputField_Inner");
            if (t == null) return;
            var img = t.GetComponent<Image>();
            Assert.IsNotNull(img);
            Assert.AreEqual(_theme.inputMaterial, img.material,
                "inputMaterial が適用されていません");
        }

        [Test]
        public void Decal_HasCorrectMaterials()
        {
            var titleDecal = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded/TitleDecal/TitleDecal_Inner");
            if (titleDecal != null)
            {
                var img = titleDecal.GetComponent<Image>();
                Assert.IsNotNull(img);
                Assert.AreEqual(_theme.decal2Material, img.material,
                    "TitleDecal_Inner に decal2Material が適用されていません");
            }

            var diamondDecal = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded/DecalDiamond/DecalDiamond_Inner");
            if (diamondDecal != null)
            {
                var img = diamondDecal.GetComponent<Image>();
                Assert.IsNotNull(img);
                Assert.AreEqual(_theme.decal1Material, img.material,
                    "DecalDiamond_Inner に decal1Material が適用されていません");
            }
        }

        [Test]
        public void HandleImage_HasHandleMaterial()
        {
            var handle = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded/VolumeSlider/Handle Slide Area/Handle");
            if (handle == null) return;
            var img = handle.GetComponent<Image>();
            Assert.IsNotNull(img);
            Assert.AreEqual(_theme.handleMaterial, img.material,
                "Handle に handleMaterial が適用されていません");
        }

        [Test]
        public void TextureGrabMeshRenderer_HasRtGrabMaterial()
        {
            foreach (var mr in _instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.gameObject.name != "TextureGrab") continue;
                Assert.AreEqual(_theme.rtGrabMaterial, mr.sharedMaterial,
                    "TextureGrab に rtGrabMaterial が適用されていません");
                return;
            }
        }

        [Test]
        public void VideoScreen_HasVideoScreenMaterial()
        {
            var screen = _instance.transform.Find("Screen");
            if (screen == null) return;
            var mr = screen.GetComponent<MeshRenderer>();
            Assert.IsNotNull(mr);
            Assert.AreEqual(_theme.videoScreenMaterial, mr.sharedMaterial,
                "Screen に videoScreenMaterial が適用されていません");
        }

        [Test]
        public void VideoScreenUi_HasVideoScreenUiMaterial()
        {
            var uiScreen = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/VideoScreenArea/VideoScreen");
            if (uiScreen == null) return;
            var raw = uiScreen.GetComponent<RawImage>();
            Assert.IsNotNull(raw);
            Assert.AreEqual(_theme.videoScreenUiMaterial, raw.material,
                "VideoScreen (UI) に videoScreenUiMaterial が適用されていません");
        }

        // ── フォント適用テスト ──

        [Test]
        public void LabelTexts_HaveDisplayFont()
        {
            if (_theme.displayFont == null) return;

            foreach (var tmp in _instance.GetComponentsInChildren<TMP_Text>(true))
            {
                var name = tmp.gameObject.name;
                if (name.EndsWith("Label") || name == "Checkmark")
                {
                    Assert.AreEqual(_theme.displayFont, tmp.font,
                        $"displayFont が適用されていません: {name}");
                }
            }
        }

        [Test]
        public void BodyTexts_HaveBodyFont()
        {
            if (_theme.bodyFont == null) return;

            var paths = new[]
            {
                "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/NowPlayingText",
                "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/StateText",
                "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/ErrorText",
            };

            foreach (var path in paths)
            {
                var t = _instance.transform.Find(path);
                if (t == null) continue;
                var tmp = t.GetComponent<TMP_Text>();
                Assert.IsNotNull(tmp, $"TMP_Text が見つかりません: {path}");
                Assert.AreEqual(_theme.bodyFont, tmp.font,
                    $"bodyFont が適用されていません: {path}");
            }
        }

        // ── 色適用テスト ──

        [Test]
        public void PortableBackground_HasUserBackgroundColor()
        {
            var bg = _instance.transform.Find("PortablePanel/ContentScaler/Background");
            Assert.IsNotNull(bg);
            var img = bg.GetComponent<Image>();
            Assert.IsNotNull(img);
            AssertColorEqual(_theme.userBackgroundColor, img.color, "Background の userBackgroundColor");
        }

        [Test]
        public void ButtonColors_Applied()
        {
            AssertImageColor("PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/PromoteNextButton",
                _theme.warningColor, "PromoteNextButton");
            AssertImageColor("PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/StopButton",
                _theme.dangerColor, "StopButton");
            AssertImageColor("PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/GlobalResyncButton",
                _theme.primaryColor, "GlobalResyncButton");
            AssertImageColor("PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded/ResyncButton",
                _theme.primaryColor, "ResyncButton");
            AssertImageColor("PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded/RebootButton",
                _theme.warningColor, "RebootButton");
        }

        [Test]
        public void DecalColor_Applied()
        {
            var titleDecal = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded/TitleDecal/TitleDecal_Inner");
            if (titleDecal != null)
            {
                var img = titleDecal.GetComponent<Image>();
                Assert.IsNotNull(img);
                AssertColorEqual(_theme.decalColor, img.color, "TitleDecal_Inner の decalColor");
            }

            var diamond = _instance.transform.Find(
                "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded/DecalDiamond/DecalDiamond_Inner");
            if (diamond != null)
            {
                var img = diamond.GetComponent<Image>();
                Assert.IsNotNull(img);
                AssertColorEqual(_theme.decalColor, img.color, "DecalDiamond_Inner の decalColor");
            }
        }

        [Test]
        public void SliderColors_Applied()
        {
            var shared = "PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded";
            AssertImageColor(shared + "/VolumeSlider/Background", _theme.sliderBackgroundColor, "Slider Background");
            AssertImageColor(shared + "/VolumeSlider/Fill Area/Fill", _theme.sliderFillColor, "Slider Fill");
            AssertImageColor(shared + "/VolumeSlider/Handle Slide Area/Handle", _theme.sliderHandleColor, "Slider Handle");
        }

        [Test]
        public void ToggleColors_Applied()
        {
            var viewer = "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded";
            AssertImageColor(viewer + "/AutoResyncToggle/Background", _theme.toggleBackgroundColor, "Toggle Background");
        }

        [Test]
        public void InputBackgroundColor_Applied()
        {
            var staff = "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded";
            AssertImageColor(staff + "/NextURLInputField", _theme.inputBackgroundColor, "NextURLInputField");
        }

        [Test]
        public void ButtonTransitionColors_Applied()
        {
            var buttons = _instance.GetComponentsInChildren<Button>(true);
            Assert.IsTrue(buttons.Length > 0, "Button が見つかりません");

            foreach (var btn in buttons)
            {
                Assert.AreEqual(_theme.buttonTransitionColors.normalColor, btn.colors.normalColor,
                    $"buttonTransitionColors.normalColor が不一致: {btn.gameObject.name}");
                Assert.AreEqual(_theme.buttonTransitionColors.pressedColor, btn.colors.pressedColor,
                    $"buttonTransitionColors.pressedColor が不一致: {btn.gameObject.name}");
            }
        }

        [Test]
        public void TextColors_Applied()
        {
            var staff = "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded";
            AssertTextColor(staff + "/PlayingLabel", _theme.headingTextColor, "PlayingLabel headingTextColor");
            AssertTextColor(staff + "/PromoteNextButton/Label", _theme.buttonLabelColor, "PromoteNextButton/Label buttonLabelColor");
            AssertTextColor(staff + "/NowPlayingText", _theme.bodyTextColor, "NowPlayingText bodyTextColor");
        }

        // ── 全テーマプロパティが使われていることの検証 ──

        [Test]
        public void AllThemeMaterials_AreApplied()
        {
            var usedMaterials = new HashSet<Material>();
            foreach (var img in _instance.GetComponentsInChildren<Image>(true))
            {
                if (img.material != null)
                    usedMaterials.Add(img.material);
            }
            foreach (var raw in _instance.GetComponentsInChildren<RawImage>(true))
            {
                if (raw.material != null)
                    usedMaterials.Add(raw.material);
            }
            foreach (var mr in _instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (var mat in mr.sharedMaterials)
                    if (mat != null) usedMaterials.Add(mat);
            }

            var expectedMaterials = new Dictionary<string, Material>
            {
                { "bgWallMaterial", _theme.bgWallMaterial },
                { "bgPortableMaterial", _theme.bgPortableMaterial },
                { "buttonRectMaterial", _theme.buttonRectMaterial },
                { "buttonRoundMaterial", _theme.buttonRoundMaterial },
                { "inputMaterial", _theme.inputMaterial },
                { "decal1Material", _theme.decal1Material },
                { "decal2Material", _theme.decal2Material },
                { "videoScreenMaterial", _theme.videoScreenMaterial },
                { "videoScreenUiMaterial", _theme.videoScreenUiMaterial },
                { "rtGrabMaterial", _theme.rtGrabMaterial },
                { "handleMaterial", _theme.handleMaterial },
                { "hudProgressMaterial", _theme.hudProgressMaterial },
            };

            foreach (var kvp in expectedMaterials)
            {
                if (kvp.Value == null) continue;
                Assert.IsTrue(usedMaterials.Contains(kvp.Value),
                    $"テーマの {kvp.Key} がどのコンポーネントにも適用されていません");
            }
        }

        [Test]
        public void AllThemeFonts_AreApplied()
        {
            var usedFonts = new HashSet<TMP_FontAsset>();
            foreach (var tmp in _instance.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp.font != null)
                    usedFonts.Add(tmp.font);
            }

            if (_theme.displayFont != null)
                Assert.IsTrue(usedFonts.Contains(_theme.displayFont),
                    "displayFont がどの TMP_Text にも適用されていません");
            if (_theme.bodyFont != null)
                Assert.IsTrue(usedFonts.Contains(_theme.bodyFont),
                    "bodyFont がどの TMP_Text にも適用されていません");
        }

        // ── ヘルパー ──

        private void AssertImageColor(string path, Color expected, string label)
        {
            var t = _instance.transform.Find(path);
            if (t == null) return;
            var img = t.GetComponent<Image>();
            if (img == null)
            {
                var inner = t.Find(t.name + "_Inner");
                if (inner != null)
                    img = inner.GetComponent<Image>();
            }
            Assert.IsNotNull(img, $"Image が見つかりません: {label}");
            AssertColorEqual(expected, img.color, label);
        }

        private void AssertTextColor(string path, Color expected, string label)
        {
            var t = _instance.transform.Find(path);
            if (t == null) return;
            var tmp = t.GetComponent<TMP_Text>();
            Assert.IsNotNull(tmp, $"TMP_Text が見つかりません: {label}");
            AssertColorEqual(expected, tmp.color, label);
        }

        private static void AssertColorEqual(Color expected, Color actual, string label)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f, $"{label} R 成分が不一致");
            Assert.AreEqual(expected.g, actual.g, 0.01f, $"{label} G 成分が不一致");
            Assert.AreEqual(expected.b, actual.b, 0.01f, $"{label} B 成分が不一致");
            Assert.AreEqual(expected.a, actual.a, 0.01f, $"{label} A 成分が不一致");
        }
    }
}
#endif
