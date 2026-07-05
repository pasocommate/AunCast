using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PasocomMate.AunCast.Internal
{
    internal static class AunCastInspectorBanner
    {
        private const string LOGO_GUID = "0b03f41b908bc7d48b57b7f713e1e3f4";
        private const string BANNER_BG_GUID = "f1aa0bc1b96b1e14da2341097235a11a";

        private static readonly Color BANNER_FALLBACK_COLOR = new Color(0.518f, 0.624f, 0.82f);
        private static readonly Color UPDATE_BADGE_BG_COLOR = new Color(0.85f, 0.15f, 0.15f);

        private static Texture2D _logo;
        private static Texture2D _bannerBackground;
        private static GUIStyle _bannerVersionStyle;
        private static GUIStyle _bannerUpdateBadgeStyle;

        private static string _cachedScriptPath = string.Empty;
        private static string _cachedPackageName = string.Empty;
        private static string _cachedPackageVersion = string.Empty;

        [Serializable]
        private sealed class PackageJson
        {
            public string name;
            public string version;
        }

        internal static void Draw(Editor ownerEditor, bool hasVersionUpdate = false, string latestVersion = "")
        {
            if (ownerEditor == null) return;

            if (_bannerBackground == null)
            {
                string bgPath = AssetDatabase.GUIDToAssetPath(BANNER_BG_GUID);
                if (!string.IsNullOrEmpty(bgPath))
                    _bannerBackground = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);
            }

            if (_logo == null)
            {
                string logoPath = AssetDatabase.GUIDToAssetPath(LOGO_GUID);
                if (!string.IsNullOrEmpty(logoPath))
                    _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
                if (_logo == null) return;
            }

            const float padTop = 16f;
            const float padBottom = 10f;
            const float maxLogoWidth = 320f;
            const float versionGap = 8f;
            const float versionHeight = 16f;

            Rect rect = GUILayoutUtility.GetRect(0f, 0f);
            float fullWidth = EditorGUIUtility.currentViewWidth;
            float logoWidth = Mathf.Min(fullWidth - 80f, maxLogoWidth);
            float logoHeight = logoWidth * _logo.height / _logo.width;
            float totalHeight = padTop + logoHeight + versionGap + versionHeight + padBottom;
            Rect bgRect = new Rect(0f, rect.y, fullWidth, totalHeight);
            GUILayoutUtility.GetRect(fullWidth, totalHeight);

            if (_bannerBackground != null)
                GUI.DrawTexture(bgRect, _bannerBackground, ScaleMode.ScaleAndCrop);
            else
                EditorGUI.DrawRect(bgRect, BANNER_FALLBACK_COLOR);

            float logoX = (fullWidth - logoWidth) / 2f;
            Rect logoRect = new Rect(logoX, bgRect.y + padTop, logoWidth, logoHeight);
            GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);

            Rect versionRect = new Rect(0f, logoRect.yMax + versionGap, fullWidth, versionHeight);
            DrawVersionLabel(ownerEditor, versionRect, hasVersionUpdate, latestVersion);

            DrawLanguageSelector();
        }

        // 表示言語の手動トグル。Auto / 日本語 / English を切り替え、選択は EditorPrefs に保存される。
        private static readonly GUIContent[] LANGUAGE_OPTIONS =
        {
            new GUIContent("Auto"),
            new GUIContent("日本語"),
            new GUIContent("English"),
        };

        private static void DrawLanguageSelector()
        {
            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                // このラベル自体はどの言語設定でも読めるよう、常に両言語併記にする。
                var label = new GUIContent("表示言語 / Language");
                GUILayout.Label(label, EditorStyles.miniLabel,
                    GUILayout.Width(EditorStyles.miniLabel.CalcSize(label).x + 4f));

                int current = (int)AunCastEditorLocalization.Language;
                EditorGUI.BeginChangeCheck();
                int next = EditorGUILayout.Popup(current, LANGUAGE_OPTIONS, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck())
                {
                    AunCastEditorLocalization.Language = (AunCastEditorLocalization.EditorLanguage)next;
                    // 開いている全インスペクタへ即時反映する。
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                GUILayout.FlexibleSpace();
            }
        }

        internal static string GetCurrentPackageName(Editor ownerEditor)
        {
            EnsurePackageInfoCached(ownerEditor);
            return _cachedPackageName;
        }

        internal static string GetCurrentPackageVersion(Editor ownerEditor)
        {
            EnsurePackageInfoCached(ownerEditor);
            return string.IsNullOrEmpty(_cachedPackageVersion) ? "unknown" : _cachedPackageVersion;
        }

        private static void DrawVersionLabel(
            Editor ownerEditor,
            Rect versionRect,
            bool hasVersionUpdate,
            string latestVersion)
        {
            string currentVersion = GetCurrentPackageVersion(ownerEditor);
            string versionText = $"Version: <b>{currentVersion}</b>";
            if (!hasVersionUpdate || string.IsNullOrEmpty(latestVersion))
            {
                GUI.Label(versionRect, versionText, GetBannerVersionStyle());
                return;
            }

            string badgeText = $"更新があります ({latestVersion})";
            GUIContent versionContent = new GUIContent(versionText);
            GUIContent badgeContent = new GUIContent(badgeText);
            GUIStyle versionStyle = GetBannerVersionStyle();
            GUIStyle badgeStyle = GetBannerUpdateBadgeStyle();

            Vector2 versionSize = versionStyle.CalcSize(versionContent);
            Vector2 badgeTextSize = badgeStyle.CalcSize(badgeContent);
            const float badgePadX = 8f;
            const float badgePadY = 2f;
            const float gap = 8f;

            float badgeWidth = badgeTextSize.x + badgePadX * 2f;
            float badgeHeight = badgeTextSize.y + badgePadY * 2f;
            float totalWidth = versionSize.x + gap + badgeWidth;
            float startX = versionRect.x + (versionRect.width - totalWidth) * 0.5f;

            Rect textRect = new Rect(startX, versionRect.y, versionSize.x, versionRect.height);
            GUI.Label(textRect, versionContent, versionStyle);

            Rect badgeRect = new Rect(
                textRect.xMax + gap,
                versionRect.y + (versionRect.height - badgeHeight) * 0.5f,
                badgeWidth,
                badgeHeight);
            EditorGUI.DrawRect(badgeRect, UPDATE_BADGE_BG_COLOR);

            Rect badgeTextRect = new Rect(
                badgeRect.x + badgePadX,
                badgeRect.y + badgePadY,
                badgeRect.width - badgePadX * 2f,
                badgeRect.height - badgePadY * 2f);
            GUI.Label(badgeTextRect, badgeContent, badgeStyle);
        }

        private static GUIStyle GetBannerVersionStyle()
        {
            if (_bannerVersionStyle != null) return _bannerVersionStyle;

            _bannerVersionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                richText = true
            };
            _bannerVersionStyle.normal.textColor = new Color(1f, 1f, 1f);
            return _bannerVersionStyle;
        }

        private static GUIStyle GetBannerUpdateBadgeStyle()
        {
            if (_bannerUpdateBadgeStyle != null) return _bannerUpdateBadgeStyle;

            _bannerUpdateBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            _bannerUpdateBadgeStyle.normal.textColor = Color.white;
            return _bannerUpdateBadgeStyle;
        }

        private static void EnsurePackageInfoCached(Editor ownerEditor)
        {
            if (ownerEditor == null)
            {
                _cachedScriptPath = string.Empty;
                _cachedPackageName = string.Empty;
                _cachedPackageVersion = string.Empty;
                return;
            }

            MonoScript script = MonoScript.FromScriptableObject(ownerEditor);
            if (script == null)
            {
                _cachedScriptPath = string.Empty;
                _cachedPackageName = string.Empty;
                _cachedPackageVersion = string.Empty;
                return;
            }

            string scriptPath = AssetDatabase.GetAssetPath(script);
            if (!string.IsNullOrEmpty(_cachedScriptPath) && _cachedScriptPath == scriptPath)
                return;

            _cachedScriptPath = scriptPath;
            _cachedPackageName = string.Empty;
            _cachedPackageVersion = string.Empty;
            if (string.IsNullOrEmpty(scriptPath)) return;

            UpmPackageInfo packageInfo = UpmPackageInfo.FindForAssetPath(scriptPath);
            if (packageInfo != null)
            {
                _cachedPackageName = packageInfo.name ?? string.Empty;
                _cachedPackageVersion = packageInfo.version ?? string.Empty;
                return;
            }

            if (TryReadPackageJson(scriptPath, out string packageName, out string packageVersion))
            {
                _cachedPackageName = packageName;
                _cachedPackageVersion = packageVersion;
            }
        }

        private static bool TryReadPackageJson(string assetPath, out string packageName, out string packageVersion)
        {
            packageName = string.Empty;
            packageVersion = string.Empty;
            if (string.IsNullOrEmpty(assetPath)) return false;

            string dir = Path.GetDirectoryName(Path.GetFullPath(assetPath));
            while (!string.IsNullOrEmpty(dir))
            {
                string packageJsonPath = Path.Combine(dir, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    var json = JsonUtility.FromJson<PackageJson>(File.ReadAllText(packageJsonPath));
                    if (json == null) return false;
                    packageName = json.name ?? string.Empty;
                    packageVersion = json.version ?? string.Empty;
                    return !string.IsNullOrEmpty(packageVersion);
                }

                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null) return false;
                dir = parent.FullName;
            }

            return false;
        }
    }
}
