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
        private static readonly string[] PortablePaddedPaths =
        {
            "PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded",
            "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded",
            "PortablePanel/ContentScaler/PortableContentArea/SharedContent/SharedPadded",
        };
        private const string PortableTopBarPaddedPath =
            "PortablePanel/ContentScaler/PortableContentArea/TopBarPadded";
        private static readonly string[] WallContentPaths =
        {
            "ContentScaler/WallContentArea/UserContent",
            "ContentScaler/WallContentArea/StaffContent",
            "ContentScaler/WallContentArea/SharedContent",
            "ContentScaler/WallContentArea/ResyncOnlyContent",
            "ContentScaler/WallContentArea/InformationContent",
        };
        private const string WallTopBarPaddedPath =
            "ContentScaler/WallContentArea/TopBarPadded";

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
            ApplyPortablePaddedMargins(root);
            ApplyPortableContentSize(root);
            ApplyWallPanelLayout(root);

            const string pp = "PortablePanel/ContentScaler";
            const string staff = pp + "/PortableContentArea/StaffContent/StaffPadded";
            const string viewer = pp + "/PortableContentArea/UserContent/UserPadded";
            const string shared = pp + "/PortableContentArea/SharedContent/SharedPadded";
            const string topBar = pp + "/PortableContentArea/TopBarPadded";

            // Background は現在のプレビュー表示（Viewer/Staff）に合わせた色を適用する。
            // SwitchContentView と整合させ、スタッフ表示中の編集で視聴者色へ戻らないようにする。
            Color backgroundColor = IsStaffViewVisible(root)
                ? theme.staffBackgroundColor
                : theme.userBackgroundColor;
            SetThemeImageColor(root, pp + "/Background", backgroundColor);

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
            SetThemeImageColor(root, staff + "/NowPlayingArea", theme.displayBackgroundColor);
            SetThemeImageColor(root, shared + "/HelpArea", theme.displayBackgroundColor);

            SetThemeImageColor(root, viewer + "/SilenceResyncToggle/Background", theme.toggleBackgroundColor);
            SetThemeTextColor(root, viewer + "/SilenceResyncToggle/Background/Checkmark", theme.toggleCheckmarkColor);
            ApplyToggleTheme(root, staff + "/TimelineLoggingToggle");

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
            SetThemeImageColor(root, topBar + "/StaffLockButton", theme.secondaryColor);

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
            SetThemeSelectableColors(root, staff + "/NextURLInputField");

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
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(selectable);
            var hudOverlay = root.Find("HudProgressOverlay");
            if (hudOverlay != null)
            {
                var quadTf = hudOverlay.Find("Quad");
                if (quadTf != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(quadTf);
            }

            // ApplyPortableContentSize で書き換えた RectTransform / Collider のオーバーライドを記録
            var panel = root.Find("PortablePanel");
            if (panel != null)
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
                var box = panel.GetComponent<BoxCollider>();
                if (box != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                var scaler = panel.Find("ContentScaler");
                if (scaler != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(scaler);
                // FitVideoScreenToArea で書き換えた VideoScreen の sizeDelta も記録する
                foreach (var path in PortablePaddedPaths)
                {
                    var padded = root.Find(path);
                    if (padded != null)
                        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(padded);
                }
                var topBarPadded = root.Find(PortableTopBarPaddedPath);
                if (topBarPadded != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(topBarPadded);
                var videoScreen = panel.Find(
                    "ContentScaler/PortableContentArea/UserContent/UserPadded/VideoScreenArea/VideoScreen");
                if (videoScreen != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(videoScreen);
            }

            foreach (var wallRoot in CollectWallPanelRoots(root))
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(wallRoot);
                var box = wallRoot.GetComponent<BoxCollider>();
                if (box != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                var scaler = wallRoot.Find("ContentScaler");
                if (scaler != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(scaler);
                foreach (var path in WallContentPaths)
                {
                    var content = wallRoot.Find(path);
                    if (content != null)
                        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(content);
                }
                var topBar = wallRoot.Find(WallTopBarPaddedPath);
                if (topBar != null)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(topBar);
            }
        }
#endif

        private void ApplyPortablePaddedMargins(Transform root)
        {
            foreach (var path in PortablePaddedPaths)
            {
                ApplyRectMargins(
                    root,
                    path,
                    theme.portableHorizontalMargin,
                    theme.portableVerticalMargin,
                    theme.portableVerticalMargin);
            }

            ApplyRectMargins(
                root,
                PortableTopBarPaddedPath,
                theme.portableHorizontalMargin,
                theme.portableVerticalMargin,
                theme.portableVerticalMargin);
        }

        private void ApplyWallPanelLayout(Transform root)
        {
            foreach (var wallRoot in CollectWallPanelRoots(root))
            {
                ApplyWallContentSize(wallRoot);
                ApplyWallContentMargins(wallRoot);
            }
        }

        private void ApplyWallContentSize(Transform wallRoot)
        {
            var scaler = wallRoot.Find("ContentScaler") as RectTransform;
            var panel = wallRoot as RectTransform;
            if (scaler == null || panel == null) return;

            Vector2 contentSize = theme.wallContentSize;
            if (contentSize.x <= 0f || contentSize.y <= 0f) return;

            scaler.sizeDelta = contentSize;

            Vector3 s = scaler.localScale;
            Vector2 panelSize = new Vector2(contentSize.x * s.x, contentSize.y * s.y);
            panel.sizeDelta = panelSize;

            var box = panel.GetComponent<BoxCollider>();
            if (box != null)
                box.size = new Vector3(panelSize.x, panelSize.y, box.size.z);
        }

        private void ApplyWallContentMargins(Transform wallRoot)
        {
            foreach (var path in WallContentPaths)
            {
                ApplyRectHorizontalAndBottomMargins(
                    wallRoot,
                    path,
                    theme.wallContentHorizontalMargin,
                    theme.wallVerticalMargin);
            }

            ApplyRectHorizontalMargin(
                wallRoot,
                WallTopBarPaddedPath,
                theme.wallContentHorizontalMargin);
            ApplyRectTopMarginPreserveHeight(
                wallRoot,
                WallTopBarPaddedPath,
                theme.wallVerticalMargin);
        }

        private static void ApplyRectMargins(
            Transform root,
            string path,
            float horizontalMargin,
            float topMargin,
            float bottomMargin)
        {
            var rect = root.Find(path) as RectTransform;
            if (rect == null) return;

            rect.offsetMin = new Vector2(horizontalMargin, bottomMargin);
            rect.offsetMax = new Vector2(-horizontalMargin, -topMargin);
        }

        private static void ApplyRectHorizontalAndBottomMargins(
            Transform root,
            string path,
            float horizontalMargin,
            float bottomMargin)
        {
            var rect = root.Find(path) as RectTransform;
            if (rect == null) return;

            rect.offsetMin = new Vector2(horizontalMargin, bottomMargin);
            rect.offsetMax = new Vector2(-horizontalMargin, rect.offsetMax.y);
        }

        private static void ApplyRectHorizontalMargin(
            Transform root,
            string path,
            float horizontalMargin)
        {
            var rect = root.Find(path) as RectTransform;
            if (rect == null) return;

            rect.offsetMin = new Vector2(horizontalMargin, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-horizontalMargin, rect.offsetMax.y);
        }

        private static void ApplyRectTopMarginPreserveHeight(
            Transform root,
            string path,
            float topMargin)
        {
            var rect = root.Find(path) as RectTransform;
            if (rect == null) return;

            float delta = -topMargin - rect.offsetMax.y;
            rect.offsetMax = new Vector2(rect.offsetMax.x, -topMargin);
            rect.offsetMin = new Vector2(rect.offsetMin.x, rect.offsetMin.y + delta);
        }

        /// <summary>
        /// PortablePanel のコンテンツ設計サイズ（ContentScaler.sizeDelta）をテーマ値で設定し、
        /// PortablePanel 本体とポインタ／グラブ判定コライダーをそれに追従させる。
        /// ContentScaler 配下の Background / PortableContentArea はストレッチ配置のため自動追従する。
        /// </summary>
        private void ApplyPortableContentSize(Transform root)
        {
            var scaler = root.Find("PortablePanel/ContentScaler") as RectTransform;
            var panel = root.Find("PortablePanel") as RectTransform;
            if (scaler == null || panel == null) return;

            Vector2 contentSize = theme.portableContentSize;
            if (contentSize.x <= 0f || contentSize.y <= 0f) return;

            scaler.sizeDelta = contentSize;

            // PortablePanel 本体は ContentScaler のローカルスケール分だけ縮めた値に追従させる
            Vector3 s = scaler.localScale;
            Vector2 panelSize = new Vector2(contentSize.x * s.x, contentSize.y * s.y);
            panel.sizeDelta = panelSize;

            // ポインタ／グラブ判定の BoxCollider もパネル矩形（ローカル単位）に合わせる。Z は据え置き
            var box = panel.GetComponent<BoxCollider>();
            if (box != null)
                box.size = new Vector3(panelSize.x, panelSize.y, box.size.z);

            // パネルサイズ変更で VideoScreenArea が非 16:9 になっても、停止中スクリーン画像が
            // 歪まないよう VideoScreen を内接させ直す（編集時プレビューを実行時と一致させる）。
            FitVideoScreenToArea(root, scaler, contentSize);
        }

        /// <summary>
        /// VideoScreen を「表示中テクスチャ（停止中は 16:9 の固定画像）の実アスペクト比」で
        /// VideoScreenArea に最大内接させる。実行時は VideoUiScreen.FitRawImageToAspect が
        /// 同じ処理を行うため、ここは ThemeApplier（IEditorOnly）のパネルリサイズ後に
        /// 編集時プレビューを実行時と揃える目的で呼ぶ。
        /// VideoScreen は中央（非ストレッチ）アンカー前提で sizeDelta を絶対サイズとして書く。
        /// </summary>
        private void FitVideoScreenToArea(Transform root, RectTransform scaler, Vector2 contentSize)
        {
            if (scaler == null) return;
            const string areaPath =
                "PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/VideoScreenArea";
            var area = root.Find(areaPath) as RectTransform;
            if (area == null) return;
            var screen = area.Find("VideoScreen") as RectTransform;
            if (screen == null) return;
            var raw = screen.GetComponent<RawImage>();
            if (raw == null || raw.texture == null) return;

            // VideoScreenArea のローカルサイズを階層の anchors/sizeDelta から解析的に算出する。
            // 編集時は rect の更新タイミングが不定なため rect.size に頼らない。
            // ContentScaler は中央アンカーなので rect.size == sizeDelta == contentSize。
            // 以降は子要素ごとに size = size * (anchorMax - anchorMin) + sizeDelta を適用する。
            var chain = new List<RectTransform>();
            for (var t = area; t != null && t != scaler; t = t.parent as RectTransform)
                chain.Add(t);
            chain.Reverse();

            Vector2 size = contentSize;
            foreach (var t in chain)
            {
                Vector2 aMin = t.anchorMin, aMax = t.anchorMax, sd = t.sizeDelta;
                size.x = size.x * (aMax.x - aMin.x) + sd.x;
                size.y = size.y * (aMax.y - aMin.y) + sd.y;
            }
            if (size.x <= 0f || size.y <= 0f) return;

            float texAspect = (float)raw.texture.width / raw.texture.height;
            float areaAspect = size.x / size.y;
            screen.sizeDelta = texAspect > areaAspect
                ? new Vector2(size.x, size.x / texAspect)
                : new Vector2(size.y * texAspect, size.y);
        }

        private void ApplyMaterials(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var mat = ResolveImageMaterial(img);
                if (mat != null)
                    img.material = mat;
            }

            foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
            {
                var mat = ResolveRawImageMaterial(raw);
                if (mat != null)
                    raw.material = mat;
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
                if (name.StartsWith("CloseButton") || name.StartsWith("SwitchViewButton")
                    || name.StartsWith("StaffLockButton") || name.StartsWith("InformationButton"))
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

        private Material ResolveRawImageMaterial(RawImage raw)
        {
            if (raw.gameObject.name == "VideoScreen")
                return theme.videoPreviewMaterial;
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
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var name = img.gameObject.name;
                if (name == "AL4BandHistory_Inner" && theme.al4BandHistoryMaterial != null)
                    img.material = theme.al4BandHistoryMaterial;
                else if (name == "ALAutoCorrelator" && theme.alAutoCorrelatorMaterial != null)
                    img.material = theme.alAutoCorrelatorMaterial;
            }
        }


        /// <summary>
        /// 現在シーンに表示中のビュー（viewer/staff）を読み取り、それに合った Background 色を適用する。
        /// Play 停止直後にエディタ側から呼ばれ、実行時のクロスフェードで書き換わった Background 色を
        /// 「表示中ビューにふさわしい色」へ整合させる目的で使う。
        /// </summary>
        public void SyncBackgroundColorToCurrentView()
        {
            if (theme == null) return;

            Transform root = transform;
            Color backgroundColor = IsStaffViewVisible(root)
                ? theme.staffBackgroundColor
                : theme.userBackgroundColor;
            SetThemeImageColor(root, "PortablePanel/ContentScaler/Background", backgroundColor);
        }

        /// <summary>
        /// 現在のプレビューがスタッフ表示かどうかを UserContent / StaffContent の
        /// CanvasGroup アルファから判定する。両方未設定（等値）の場合は視聴者表示扱い。
        /// </summary>
        private static bool IsStaffViewVisible(Transform root)
        {
            const string pp = "PortablePanel/ContentScaler/PortableContentArea";
            var userCg = root.Find(pp + "/UserContent")?.GetComponent<CanvasGroup>();
            var staffCg = root.Find(pp + "/StaffContent")?.GetComponent<CanvasGroup>();
            if (staffCg == null) return false;

            float userAlpha = userCg != null ? userCg.alpha : 0f;
            return staffCg.alpha > userAlpha;
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

        private void SetThemeSelectableColors(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null) return;
            var selectable = t.GetComponent<Selectable>();
            if (selectable == null)
            {
                var inner = t.Find(t.name + "_Inner");
                if (inner != null)
                    selectable = inner.GetComponent<Selectable>();
            }
            if (selectable != null)
                selectable.colors = theme.buttonTransitionColors;
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

                SetThemeImageColor(wallRoot, wall + "/TopBarPadded/InformationButton", theme.secondaryColor);

                string wallUser = wall + "/UserContent";
                SetThemeImageColor(wallRoot, wallUser + "/UserResyncButton", theme.primaryColor);
                SetThemeImageColor(wallRoot, wallUser + "/UserRebootButton", theme.warningColor);

                string vrGroup = wallUser + "/VrGestureGroup";
                ApplyToggleTheme(wallRoot, vrGroup + "/GestureRightStickUpToggle");
                ApplyToggleTheme(wallRoot, vrGroup + "/GestureDoubleTriggerLeftToggle");
                ApplyToggleTheme(wallRoot, vrGroup + "/GestureDoubleTriggerRightToggle");
                ApplyToggleTheme(wallRoot, vrGroup + "/GestureBothTriggersToggle");

                string desktopGroup = wallUser + "/DesktopGestureGroup";
                ApplyToggleTheme(wallRoot, desktopGroup + "/DesktopEscHoldToggle");
                ApplyToggleTheme(wallRoot, desktopGroup + "/DesktopF5DoubleTapToggle");
                ApplyToggleTheme(wallRoot, desktopGroup + "/DesktopTabDoubleTapToggle");

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
                ApplyToggleLabelColor(wallRoot, vrGroup + "/GestureRightStickUpToggle");
                ApplyToggleLabelColor(wallRoot, vrGroup + "/GestureDoubleTriggerLeftToggle");
                ApplyToggleLabelColor(wallRoot, vrGroup + "/GestureDoubleTriggerRightToggle");
                ApplyToggleLabelColor(wallRoot, vrGroup + "/GestureBothTriggersToggle");

                string desktopGroup = wallUser + "/DesktopGestureGroup";
                ApplyToggleLabelColor(wallRoot, desktopGroup + "/DesktopEscHoldToggle");
                ApplyToggleLabelColor(wallRoot, desktopGroup + "/DesktopF5DoubleTapToggle");
                ApplyToggleLabelColor(wallRoot, desktopGroup + "/DesktopTabDoubleTapToggle");

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

                SetThemeTextColor(wallRoot, wall + "/TopBarPadded/InformationButton/InformationButton_Inner/Label", theme.buttonLabelColor);
                SetThemeTextColor(wallRoot, wall + "/InformationContent/CopyrightText", theme.bodyTextColor);
            }
        }

        private void ApplyToggleTheme(Transform root, string togglePath)
        {
            SetThemeImageColor(root, togglePath + "/Background", theme.toggleBackgroundColor);
            SetThemeTextColor(root, togglePath + "/Background/Checkmark", theme.toggleCheckmarkColor);
        }

        private void ApplyToggleLabelColor(Transform root, string togglePath)
        {
            SetThemeTextColor(root, togglePath + "/Label", theme.headingTextColor);
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
                viewer + "/SilenceResyncToggle/Label",
                staff + "/TimelineLoggingToggle/Label",
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
                topBar + "/StaffLockButton/StaffLockButton_Inner/Label",
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
