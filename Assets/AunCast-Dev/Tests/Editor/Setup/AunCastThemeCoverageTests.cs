#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast.Tests
{
    /// <summary>
    /// ブラックボックス網羅性テスト。
    /// 2 つの全プロパティが異なるダミーテーマを順に適用し、
    /// すべての UI コンポーネントの色・マテリアル・フォントが変化することを検証する。
    /// 変化しなかったコンポーネントはテーマ適用漏れとして報告される。
    /// </summary>
    [TestFixture]
    public class AunCastThemeCoverageTests
    {
        private const string PREFAB_GUID = "0d617877712537147a584eb2a1ef735c";

        private GameObject _instance;
        private AunCastThemeApplier _applier;

        private AunCastTheme _themeA;
        private AunCastTheme _themeB;

        private readonly List<Object> _tempAssets = new List<Object>();
        private TMP_FontAsset[] _projectFonts;

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

            _projectFonts = FindProjectFonts();
            Assert.IsTrue(_projectFonts.Length >= 2,
                $"フォントが 2 つ以上必要ですが {_projectFonts.Length} 個しか見つかりません");

            _themeA = CreateDummyTheme("A", 0);
            _themeB = CreateDummyTheme("B", 1);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_instance != null)
                Object.DestroyImmediate(_instance);
            foreach (var asset in _tempAssets)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }
            _tempAssets.Clear();
        }

        // テーマ適用対象外のコンポーネント（理由付き）
        private static bool IsExcludedFromColor(string path)
        {
            // Silence ゲージのマーカー — ランタイム Udon がハードコードで色を設定
            if (path.EndsWith("SilenceThresholdMarker") || path.EndsWith("SilencePeakMarker"))
                return true;
            // DriftGraph — デバッグ専用 RawImage/テキスト、テーマ対象外
            if (path.Contains("/DriftGraph"))
                return true;
            return false;
        }

        private static bool IsExcludedFromMaterial(string path)
        {
            if (path.EndsWith("SilenceThresholdMarker") || path.EndsWith("SilencePeakMarker"))
                return true;
            // HUD Quad — プロパティのみ変更、マテリアル参照は固定
            if (path.EndsWith("/Quad"))
                return true;
            // DriftGraph — デバッグ専用 RawImage、テーマ対象外
            if (path.Contains("/DriftGraph"))
                return true;
            // VideoScreen / TextureGrab / Screen — テーマ対象外
            if (path.EndsWith("/VideoScreen") || path.EndsWith("/TextureGrab") || path.EndsWith("/Screen"))
                return true;
            return false;
        }

        // ── Image: color + material ──

        [Test]
        public void AllImages_ColorChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = SnapshotImageColors();

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var img in _instance.GetComponentsInChildren<Image>(true))
            {
                var path = GetPath(img.transform);
                if (IsExcludedFromColor(path)) continue;
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (ColorsEqual(prev, img.color))
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の Image の色がテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        [Test]
        public void AllImages_MaterialChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = SnapshotImageMaterials();

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var img in _instance.GetComponentsInChildren<Image>(true))
            {
                var path = GetPath(img.transform);
                if (IsExcludedFromMaterial(path)) continue;
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (img.material == prev && prev != null)
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の Image のマテリアルがテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        // ── TMP_Text: color + font ──

        [Test]
        public void AllTexts_ColorChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = SnapshotTextColors();

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var tmp in _instance.GetComponentsInChildren<TMP_Text>(true))
            {
                var path = GetPath(tmp.transform);
                if (IsExcludedFromColor(path)) continue;
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (ColorsEqual(prev, tmp.color))
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の TMP_Text の色がテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        [Test]
        public void AllTexts_FontChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = SnapshotTextFonts();

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var tmp in _instance.GetComponentsInChildren<TMP_Text>(true))
            {
                var path = GetPath(tmp.transform);
                if (IsExcludedFromColor(path)) continue;
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (tmp.font == prev && prev != null)
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の TMP_Text のフォントがテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        // ── RawImage: material ──

        [Test]
        public void AllRawImages_MaterialChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = new Dictionary<string, Material>();
            foreach (var raw in _instance.GetComponentsInChildren<RawImage>(true))
                snapshot[GetPath(raw.transform)] = raw.material;

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var raw in _instance.GetComponentsInChildren<RawImage>(true))
            {
                var path = GetPath(raw.transform);
                if (IsExcludedFromMaterial(path)) continue;
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (raw.material == prev && prev != null)
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の RawImage のマテリアルがテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        // ── MeshRenderer: material ──

        [Test]
        public void AllMeshRenderers_MaterialChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = new Dictionary<string, Material>();
            foreach (var mr in _instance.GetComponentsInChildren<MeshRenderer>(true))
                snapshot[GetPath(mr.transform)] = mr.sharedMaterial;

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var mr in _instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var path = GetPath(mr.transform);
                if (IsExcludedFromMaterial(path)) continue;
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (mr.sharedMaterial == prev && prev != null)
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の MeshRenderer のマテリアルがテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        // ── Button: transition colors ──

        [Test]
        public void AllButtons_TransitionColorChanges()
        {
            _applier.theme = _themeA;
            _applier.ApplyTheme(_instance.transform);
            var snapshot = new Dictionary<string, ColorBlock>();
            foreach (var btn in _instance.GetComponentsInChildren<Button>(true))
                snapshot[GetPath(btn.transform)] = btn.colors;

            _applier.theme = _themeB;
            _applier.ApplyTheme(_instance.transform);

            var unchanged = new List<string>();
            foreach (var btn in _instance.GetComponentsInChildren<Button>(true))
            {
                var path = GetPath(btn.transform);
                if (!snapshot.TryGetValue(path, out var prev)) continue;
                if (ColorsEqual(prev.normalColor, btn.colors.normalColor)
                    && ColorsEqual(prev.pressedColor, btn.colors.pressedColor))
                    unchanged.Add(path);
            }

            Assert.IsEmpty(unchanged,
                $"以下の Button の遷移色がテーマ切替で変化しませんでした:\n" +
                string.Join("\n", unchanged));
        }

        // ── ヘルパー: スナップショット ──

        private Dictionary<string, Color> SnapshotImageColors()
        {
            var dict = new Dictionary<string, Color>();
            foreach (var img in _instance.GetComponentsInChildren<Image>(true))
                dict[GetPath(img.transform)] = img.color;
            return dict;
        }

        private Dictionary<string, Material> SnapshotImageMaterials()
        {
            var dict = new Dictionary<string, Material>();
            foreach (var img in _instance.GetComponentsInChildren<Image>(true))
                dict[GetPath(img.transform)] = img.material;
            return dict;
        }

        private Dictionary<string, Color> SnapshotTextColors()
        {
            var dict = new Dictionary<string, Color>();
            foreach (var tmp in _instance.GetComponentsInChildren<TMP_Text>(true))
                dict[GetPath(tmp.transform)] = tmp.color;
            return dict;
        }

        private Dictionary<string, TMP_FontAsset> SnapshotTextFonts()
        {
            var dict = new Dictionary<string, TMP_FontAsset>();
            foreach (var tmp in _instance.GetComponentsInChildren<TMP_Text>(true))
                dict[GetPath(tmp.transform)] = tmp.font;
            return dict;
        }

        // ── ヘルパー: ダミーテーマ生成 ──

        private AunCastTheme CreateDummyTheme(string label, int seed)
        {
            var theme = ScriptableObject.CreateInstance<AunCastTheme>();
            theme.name = $"DummyTheme_{label}";
            _tempAssets.Add(theme);

            var shader = Shader.Find("UI/Default");

            theme.bgWallMaterial = CreateMat(shader, $"bgWall_{label}");
            theme.bgPortableMaterial = CreateMat(shader, $"bgPortable_{label}");
            theme.buttonRectMaterial = CreateMat(shader, $"btnRect_{label}");
            theme.buttonRoundMaterial = CreateMat(shader, $"btnRound_{label}");
            theme.inputMaterial = CreateMat(shader, $"input_{label}");
            theme.decal1Material = CreateMat(shader, $"decal1_{label}");
            theme.decal2Material = CreateMat(shader, $"decal2_{label}");
            theme.handleMaterial = CreateMat(shader, $"handle_{label}");
            theme.hudProgressMaterial = CreateMat(shader, $"hudProg_{label}");
            theme.al4BandHistoryMaterial = CreateMat(shader, $"al4Band_{label}");
            theme.alAutoCorrelatorMaterial = CreateMat(shader, $"alAutoCorr_{label}");

            theme.displayFont = _projectFonts[seed % _projectFonts.Length];
            theme.bodyFont = _projectFonts[(seed + 1) % _projectFonts.Length];

            float s = seed;
            theme.userBackgroundColor = Color.HSVToRGB((0.0f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.staffBackgroundColor = Color.HSVToRGB((0.05f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.wallBackgroundColor = Color.HSVToRGB((0.1f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.decalColor = Color.HSVToRGB((0.15f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.primaryColor = Color.HSVToRGB((0.2f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.secondaryColor = Color.HSVToRGB((0.25f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.warningColor = Color.HSVToRGB((0.3f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.dangerColor = Color.HSVToRGB((0.35f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.inputBackgroundColor = Color.HSVToRGB((0.4f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.toggleBackgroundColor = Color.HSVToRGB((0.45f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.toggleCheckmarkColor = Color.HSVToRGB((0.5f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.sliderBackgroundColor = Color.HSVToRGB((0.55f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.sliderFillColor = Color.HSVToRGB((0.6f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.sliderHandleColor = Color.HSVToRGB((0.65f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.headingTextColor = Color.HSVToRGB((0.7f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.buttonLabelColor = Color.HSVToRGB((0.75f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.bodyTextColor = Color.HSVToRGB((0.8f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.inputTextColor = Color.HSVToRGB((0.85f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.placeholderTextColor = Color.HSVToRGB((0.9f + s * 0.5f) % 1f, 0.8f, 0.8f);

            theme.buttonTransitionColors = new ColorBlock
            {
                normalColor = Color.HSVToRGB((0.12f + s * 0.5f) % 1f, 0.5f, 0.9f),
                highlightedColor = Color.HSVToRGB((0.17f + s * 0.5f) % 1f, 0.5f, 0.9f),
                pressedColor = Color.HSVToRGB((0.22f + s * 0.5f) % 1f, 0.5f, 0.9f),
                selectedColor = Color.HSVToRGB((0.27f + s * 0.5f) % 1f, 0.5f, 0.9f),
                disabledColor = Color.HSVToRGB((0.32f + s * 0.5f) % 1f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };

            theme.hudProgressUsePieMode = seed == 0;
            theme.hudProgressLocalOffset = new Vector3(seed, seed * 0.1f, seed * 0.5f);
            theme.hudProgressBarSize = new Vector2(0.1f + seed * 0.1f, 0.01f + seed * 0.01f);
            theme.hudProgressPieDiameter = 0.05f + seed * 0.05f;
            theme.hudProgressPieRingThickness = seed == 0 ? 0.1f : 0.5f;
            theme.hudProgressFillColor = Color.HSVToRGB((0.95f + s * 0.5f) % 1f, 0.8f, 0.8f);
            theme.hudProgressBaseColor = Color.HSVToRGB((0.98f + s * 0.5f) % 1f, 0.3f, 0.3f);

            return theme;
        }

        private Material CreateMat(Shader shader, string matName)
        {
            var mat = new Material(shader) { name = matName };
            _tempAssets.Add(mat);
            return mat;
        }

        private static TMP_FontAsset[] FindProjectFonts()
        {
            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            var fonts = new List<TMP_FontAsset>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null)
                    fonts.Add(font);
                if (fonts.Count >= 2) break;
            }
            return fonts.ToArray();
        }

        // ── ヘルパー: パス取得・色比較 ──

        private string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null && t != _instance.transform)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool ColorsEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.001f
                && Mathf.Abs(a.g - b.g) < 0.001f
                && Mathf.Abs(a.b - b.b) < 0.001f
                && Mathf.Abs(a.a - b.a) < 0.001f;
        }
    }
}
#endif
