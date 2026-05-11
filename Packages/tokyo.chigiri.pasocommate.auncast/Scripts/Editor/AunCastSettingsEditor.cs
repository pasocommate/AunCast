#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VRC.SDK3.Video.Components.AVPro;
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCastSettings の Inspector カスタムエディタ。
    /// ScriptableObject では表現しにくい設定項目を、ヘルプ付きの専用 GUI で編集できるようにする。
    /// </summary>
    [CustomEditor(typeof(PasocomMate.AunCast.AunCastSettings))]
    public class AunCastSettingsEditor : Editor
    {
        private const string LOGO_GUID = "0b03f41b908bc7d48b57b7f713e1e3f4";
        private const string BANNER_BG_GUID = "113e351a1b05afd48b1027675bb3bf15";
        private const string DEFAULT_VPM_LISTING_URL = "https://pasocommate.github.io/AunCast/index.json";
        private const string TMP_FALLBACK_DEFAULT_FONT_GUID = "b0cf90c18247f154094021e2de9bf529";
        private const string TMP_FALLBACK_NOTO_FONT_GUID = "32134e5dc8c950c4cb5bb7deaae7d539";
        private const string TMP_FALLBACK_MENU_PATH = "Tools→TextMesh Pro VRC Fallback Font JPを設定";
        private const double VPM_VERSION_REQUEST_TIMEOUT_SEC = 8.0;
        private const string SESSION_KEY_VPM_CHECK_DONE = "AunCast.SettingsEditor.VpmCheckDone";
        private const string SESSION_KEY_VPM_HAS_UPDATE = "AunCast.SettingsEditor.VpmHasUpdate";
        private const string SESSION_KEY_VPM_LATEST_VERSION = "AunCast.SettingsEditor.VpmLatestVersion";
        private static readonly Color BANNER_FALLBACK_COLOR = new Color(0.518f, 0.624f, 0.82f);
        private static readonly Color UPDATE_BADGE_BG_COLOR = new Color(0.85f, 0.15f, 0.15f);

        private bool _prevAlt;
        private Texture2D _logo;
        private Texture2D _bannerBackground;
        private GUIStyle _bannerVersionStyle;
        private GUIStyle _bannerUpdateBadgeStyle;
        private string _packageName;
        private string _packageVersion;
        private bool _vpmVersionCheckRequested;
        private bool _vpmVersionCheckInProgress;
        private UnityWebRequest _vpmVersionRequest;
        private double _vpmVersionRequestStartTime;
        private bool _hasVersionUpdate;
        private string _latestVersion;
        private string _vpmListingUrl;
        private bool _vpmSessionCacheLoaded;

        private void DrawLogo()
        {
            if (_bannerBackground == null)
            {
                var bgPath = AssetDatabase.GUIDToAssetPath(BANNER_BG_GUID);
                if (!string.IsNullOrEmpty(bgPath))
                    _bannerBackground = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);
            }

            if (_logo == null)
            {
                var path = AssetDatabase.GUIDToAssetPath(LOGO_GUID);
                if (!string.IsNullOrEmpty(path))
                    _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (_logo == null) return;
            }

            const float padTop = 16f;
            const float padBottom = 10f;
            const float maxLogoWidth = 320f;
            const float versionGap = 8f;
            const float versionHeight = 16f;
            var rect = GUILayoutUtility.GetRect(0f, 0f);
            float fullWidth = EditorGUIUtility.currentViewWidth;
            float logoWidth = Mathf.Min(fullWidth - 80f, maxLogoWidth);
            float logoHeight = logoWidth * _logo.height / _logo.width;
            float totalHeight = padTop + logoHeight + versionGap + versionHeight + padBottom;
            var bgRect = new Rect(0f, rect.y, fullWidth, totalHeight);
            GUILayoutUtility.GetRect(fullWidth, totalHeight);

            if (_bannerBackground != null)
                GUI.DrawTexture(bgRect, _bannerBackground, ScaleMode.ScaleAndCrop);
            else
                EditorGUI.DrawRect(bgRect, BANNER_FALLBACK_COLOR);
            float logoX = (fullWidth - logoWidth) / 2f;
            var logoRect = new Rect(logoX, bgRect.y + padTop, logoWidth, logoHeight);
            GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);

            var versionRect = new Rect(0f, logoRect.yMax + versionGap, fullWidth, versionHeight);
            DrawVersionLabel(versionRect);
        }

        private void DrawVersionLabel(Rect versionRect)
        {
            string currentVersion = GetCurrentPackageVersion();
            string versionText = $"Version: <b>{currentVersion}</b>";
            if (!_hasVersionUpdate || string.IsNullOrEmpty(_latestVersion))
            {
                GUI.Label(versionRect, versionText, GetBannerVersionStyle());
                return;
            }

            string badgeText = $"更新があります ({_latestVersion})";

            var versionContent = new GUIContent(versionText);
            var badgeContent = new GUIContent(badgeText);
            var versionStyle = GetBannerVersionStyle();
            var badgeStyle = GetBannerUpdateBadgeStyle();

            Vector2 versionSize = versionStyle.CalcSize(versionContent);
            Vector2 badgeTextSize = badgeStyle.CalcSize(badgeContent);
            const float badgePadX = 8f;
            const float badgePadY = 2f;
            const float gap = 8f;

            float badgeWidth = badgeTextSize.x + badgePadX * 2f;
            float badgeHeight = badgeTextSize.y + badgePadY * 2f;
            float totalWidth = versionSize.x + gap + badgeWidth;
            float startX = versionRect.x + (versionRect.width - totalWidth) * 0.5f;

            var textRect = new Rect(startX, versionRect.y, versionSize.x, versionRect.height);
            GUI.Label(textRect, versionContent, versionStyle);

            var badgeRect = new Rect(
                textRect.xMax + gap,
                versionRect.y + (versionRect.height - badgeHeight) * 0.5f,
                badgeWidth,
                badgeHeight);
            EditorGUI.DrawRect(badgeRect, UPDATE_BADGE_BG_COLOR);

            var badgeTextRect = new Rect(
                badgeRect.x + badgePadX,
                badgeRect.y + badgePadY,
                badgeRect.width - badgePadX * 2f,
                badgeRect.height - badgePadY * 2f);
            GUI.Label(badgeTextRect, badgeContent, badgeStyle);
        }

        private GUIStyle GetBannerVersionStyle()
        {
            if (_bannerVersionStyle != null) return _bannerVersionStyle;

            _bannerVersionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                richText = true
            };
            _bannerVersionStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            return _bannerVersionStyle;
        }

        private GUIStyle GetBannerUpdateBadgeStyle()
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

        private static GUIContent L(string label, string fieldName, string tooltip)
        {
            bool alt = Event.current != null && Event.current.alt;
            return new GUIContent(alt ? fieldName : label, tooltip);
        }

        private static void CopyFieldNameMenu(string fieldName)
        {
            if (Event.current.type != EventType.ContextClick) return;
            var rect = GUILayoutUtility.GetLastRect();
            rect.width = EditorGUIUtility.labelWidth;
            if (!rect.Contains(Event.current.mousePosition)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"変数名をコピー: {fieldName}"), false,
                () => EditorGUIUtility.systemCopyBuffer = fieldName);
            menu.ShowAsContext();
            Event.current.Use();
        }

        private float SliderField(string label, string fieldName, string tooltip,
            float value, float min, float max)
        {
            float result = EditorGUILayout.Slider(L(label, fieldName, tooltip), value, min, max);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        private int IntSliderField(string label, string fieldName, string tooltip,
            int value, int min, int max)
        {
            int result = EditorGUILayout.IntSlider(L(label, fieldName, tooltip), value, min, max);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        private bool ToggleField(string label, string fieldName, string tooltip, bool value)
        {
            bool result = EditorGUILayout.Toggle(L(label, fieldName, tooltip), value);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        private string TextField(string label, string fieldName, string tooltip, string value)
        {
            string result = EditorGUILayout.TextField(L(label, fieldName, tooltip), value ?? string.Empty);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        public override void OnInspectorGUI()
        {
            bool alt = Event.current != null && Event.current.alt;
            if (alt != _prevAlt)
            {
                _prevAlt = alt;
                Repaint();
            }

            EnsureVpmVersionCheckStarted();
            PollVpmVersionCheck();
            DrawLogo();
            DrawTmpFallbackFontWarning();

            var settings = (PasocomMate.AunCast.AunCastSettings)target;
            var root = settings.transform;

            var ldpcList = root.GetComponentsInChildren<LocalDualPlayerController>(true);
            var apmList = root.GetComponentsInChildren<ActivePlayerMonitor>(true);
            var rccList = root.GetComponentsInChildren<ResyncCoordinatorClient>(true);
            var pbsList = root.GetComponentsInChildren<PlaybackSwitcher>(true);
            var rcList = root.GetComponentsInChildren<ResyncCoordinator>(true);

            int totalCount = ldpcList.Length + apmList.Length + rccList.Length + pbsList.Length + rcList.Length;

            if (totalCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "AunCastコンポーネントが見つかりません。AunCast ルート配下で設定してください。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(8);

            // ── 映像プレイヤー ──
            var avProPlayers = root.GetComponentsInChildren<VRCAVProVideoPlayer>(true);
            DrawVideoPlayerSettings(root, settings, avProPlayers);

            EditorGUILayout.Space(8);

            // ── UI / 操作 ──
            DrawUiSettings(root, settings);

            EditorGUILayout.Space(8);

            // ── 再生監視 ──
            DrawPlaybackMonitorSettings(root, settings);

            EditorGUILayout.Space(8);

            // ── Resync制御 ──
            DrawResyncSettings(root, settings);

            EditorGUILayout.Space(8);

            // ── デバッグ ──
            DrawTimelineLoggingToggle(ldpcList, apmList, rccList, pbsList, rcList);
        }

        private void OnDisable()
        {
            StopVpmVersionCheck();
        }

        private static void DrawTmpFallbackFontWarning()
        {
            string message = GetTmpFallbackFontWarningMessage();
            if (string.IsNullOrEmpty(message)) return;
            EditorGUILayout.HelpBox(message, MessageType.Warning);
            EditorGUILayout.Space(8);
        }

        private static string GetTmpFallbackFontWarningMessage()
        {
            var tmpSettings = TMP_Settings.instance;
            if (tmpSettings == null)
            {
                return Localize(
                    "TMP Settings が見つかりません。Edit→Project Settings→TextMesh Pro から TMP Essentials を先にインポートしてください。",
                    "TMP Settings was not found. Open Edit > Project Settings > TextMesh Pro and import TMP Essentials first.");
            }

            var defaultFontAsset = LoadAssetByGuid<TMP_FontAsset>(TMP_FALLBACK_DEFAULT_FONT_GUID);
            var fallbackFontAsset = LoadAssetByGuid<TMP_FontAsset>(TMP_FALLBACK_NOTO_FONT_GUID);
            if (defaultFontAsset == null || fallbackFontAsset == null)
            {
                return Localize(
                    "net.narazaka.vrchat.tmp-fallback-fonts-jp のフォントアセットが見つかりません。Manage Project で TextMesh Pro VRC Fallback Font JP を導入してください。",
                    "Font assets from net.narazaka.vrchat.tmp-fallback-fonts-jp were not found. Install TextMesh Pro VRC Fallback Font JP from Manage Project.");
            }

            bool hasDefault = TMP_Settings.defaultFontAsset == defaultFontAsset;
            bool hasFallback = false;
            var fallbackFontAssets = TMP_Settings.fallbackFontAssets;
            if (fallbackFontAssets != null)
            {
                for (int i = 0; i < fallbackFontAssets.Count; i++)
                {
                    if (fallbackFontAssets[i] == fallbackFontAsset)
                    {
                        hasFallback = true;
                        break;
                    }
                }
            }

            if (hasDefault && hasFallback) return null;
            return Localize(
                $"TMP フォールバック設定が未適用です。{TMP_FALLBACK_MENU_PATH} を実行してください。実行後はシーンを開き直してください。",
                $"TMP fallback font settings are not applied. Run {TMP_FALLBACK_MENU_PATH}. After that, reopen the scene.");
        }

        private static string Localize(string ja, string en)
        {
            return Application.systemLanguage == SystemLanguage.Japanese ? ja : en;
        }

        private void EnsureVpmVersionCheckStarted()
        {
            LoadVpmCheckResultFromSessionCache();
            if (_vpmVersionCheckRequested || _vpmVersionCheckInProgress) return;

            string packageName = GetCurrentPackageName();
            string currentVersion = GetCurrentPackageVersion();
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(currentVersion) || currentVersion == "unknown")
                return;

            string listingUrl = GetVpmListingUrl();
            if (string.IsNullOrEmpty(listingUrl))
                return;

            _vpmVersionCheckRequested = true;
            _vpmVersionCheckInProgress = true;
            _vpmVersionRequestStartTime = EditorApplication.timeSinceStartup;
            _vpmVersionRequest = UnityWebRequest.Get(listingUrl);
            _vpmVersionRequest.SendWebRequest();
            Repaint();
        }

        private void PollVpmVersionCheck()
        {
            if (!_vpmVersionCheckInProgress || _vpmVersionRequest == null) return;

            if (!HasVpmRequestTimedOut() && !_vpmVersionRequest.isDone)
            {
                Repaint();
                return;
            }

            string currentVersion = GetCurrentPackageVersion();
            string packageName = GetCurrentPackageName();

            if (HasVpmRequestTimedOut())
            {
                MarkVpmCheckCompletedForSession();
                StopVpmVersionCheck();
                return;
            }

#if UNITY_2020_2_OR_NEWER
            bool success = _vpmVersionRequest.result == UnityWebRequest.Result.Success;
#else
            bool success = !_vpmVersionRequest.isNetworkError && !_vpmVersionRequest.isHttpError;
#endif
            if (!success)
            {
                MarkVpmCheckCompletedForSession();
                StopVpmVersionCheck();
                return;
            }

            string json = _vpmVersionRequest.downloadHandler != null
                ? _vpmVersionRequest.downloadHandler.text
                : string.Empty;
            if (TryExtractLatestVersionFromVpmListing(json, packageName, out var latestVersion))
            {
                _latestVersion = latestVersion;
                _hasVersionUpdate = IsNewerVersion(latestVersion, currentVersion);
            }

            MarkVpmCheckCompletedForSession();
            StopVpmVersionCheck();
        }

        private bool HasVpmRequestTimedOut()
        {
            if (!_vpmVersionCheckInProgress) return false;
            return EditorApplication.timeSinceStartup - _vpmVersionRequestStartTime > VPM_VERSION_REQUEST_TIMEOUT_SEC;
        }

        private void StopVpmVersionCheck()
        {
            _vpmVersionCheckInProgress = false;
            if (_vpmVersionRequest != null)
            {
                if (!_vpmVersionRequest.isDone)
                    _vpmVersionRequest.Abort();
                _vpmVersionRequest.Dispose();
                _vpmVersionRequest = null;
            }
        }

        private void LoadVpmCheckResultFromSessionCache()
        {
            if (_vpmSessionCacheLoaded) return;
            _vpmSessionCacheLoaded = true;

            if (!SessionState.GetBool(SESSION_KEY_VPM_CHECK_DONE, false))
                return;

            _vpmVersionCheckRequested = true;
            _hasVersionUpdate = SessionState.GetBool(SESSION_KEY_VPM_HAS_UPDATE, false);
            _latestVersion = SessionState.GetString(SESSION_KEY_VPM_LATEST_VERSION, string.Empty);
        }

        private void MarkVpmCheckCompletedForSession()
        {
            _vpmVersionCheckRequested = true;
            SessionState.SetBool(SESSION_KEY_VPM_CHECK_DONE, true);
            SessionState.SetBool(SESSION_KEY_VPM_HAS_UPDATE, _hasVersionUpdate);
            SessionState.SetString(SESSION_KEY_VPM_LATEST_VERSION, _latestVersion ?? string.Empty);
        }

        private string GetVpmListingUrl()
        {
            if (!string.IsNullOrEmpty(_vpmListingUrl)) return _vpmListingUrl;

            _vpmListingUrl = DEFAULT_VPM_LISTING_URL;
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return _vpmListingUrl;

            string gitConfigPath = Path.Combine(projectRoot, ".git", "config");
            if (!File.Exists(gitConfigPath)) return _vpmListingUrl;

            string config = File.ReadAllText(gitConfigPath);
            int remoteSectionIndex = config.IndexOf("[remote \"origin\"]", StringComparison.Ordinal);
            if (remoteSectionIndex < 0) return _vpmListingUrl;

            int urlLineIndex = config.IndexOf("url =", remoteSectionIndex, StringComparison.Ordinal);
            if (urlLineIndex < 0) return _vpmListingUrl;

            int lineEndIndex = config.IndexOf('\n', urlLineIndex);
            if (lineEndIndex < 0) lineEndIndex = config.Length;
            string remoteLine = config.Substring(urlLineIndex, lineEndIndex - urlLineIndex).Trim();
            string remoteUrl = remoteLine.Substring("url =".Length).Trim();

            if (!TryBuildGithubPagesIndexUrl(remoteUrl, out var indexUrl))
                return _vpmListingUrl;

            _vpmListingUrl = indexUrl;
            return _vpmListingUrl;
        }

        private static bool TryBuildGithubPagesIndexUrl(string remoteUrl, out string indexUrl)
        {
            indexUrl = string.Empty;
            if (string.IsNullOrEmpty(remoteUrl)) return false;

            string normalized = remoteUrl.Trim();
            const string httpsPrefix = "https://github.com/";
            const string sshPrefix = "git@github.com:";
            if (normalized.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(httpsPrefix.Length);
            else if (normalized.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(sshPrefix.Length);
            else
                return false;

            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);

            string[] segments = normalized.Split('/');
            if (segments.Length < 2) return false;

            string owner = segments[0];
            string repo = segments[1];
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo)) return false;

            indexUrl = $"https://{owner}.github.io/{repo}/index.json";
            return true;
        }

        private static bool TryExtractLatestVersionFromVpmListing(
            string json,
            string packageName,
            out string latestVersion)
        {
            latestVersion = string.Empty;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(packageName)) return false;

            int packagesObjectStart = FindObjectStartByKey(json, "packages", 0);
            if (packagesObjectStart < 0) return false;
            if (!TryFindMatchingBrace(json, packagesObjectStart, out int packagesObjectEnd)) return false;

            int packageKeyIndex = json.IndexOf($"\"{packageName}\"", packagesObjectStart, StringComparison.Ordinal);
            if (packageKeyIndex < 0 || packageKeyIndex > packagesObjectEnd) return false;

            int packageObjectStart = FindObjectStartByKey(json, packageName, packageKeyIndex);
            if (packageObjectStart < 0 || packageObjectStart > packagesObjectEnd) return false;
            if (!TryFindMatchingBrace(json, packageObjectStart, out int packageObjectEnd)) return false;

            int versionsObjectStart = FindObjectStartByKey(json, "versions", packageObjectStart);
            if (versionsObjectStart < 0 || versionsObjectStart > packageObjectEnd) return false;
            if (!TryFindMatchingBrace(json, versionsObjectStart, out int versionsObjectEnd)) return false;

            return TryGetHighestSemverKey(json, versionsObjectStart, versionsObjectEnd, out latestVersion);
        }

        private static int FindObjectStartByKey(string json, string key, int searchStartIndex)
        {
            int keyIndex = json.IndexOf($"\"{key}\"", searchStartIndex, StringComparison.Ordinal);
            if (keyIndex < 0) return -1;

            int colonIndex = json.IndexOf(':', keyIndex);
            if (colonIndex < 0) return -1;

            for (int i = colonIndex + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (char.IsWhiteSpace(c)) continue;
                return c == '{' ? i : -1;
            }

            return -1;
        }

        private static bool TryFindMatchingBrace(string text, int objectStartIndex, out int objectEndIndex)
        {
            objectEndIndex = -1;
            if (objectStartIndex < 0 || objectStartIndex >= text.Length || text[objectStartIndex] != '{')
                return false;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = objectStartIndex; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    continue;
                }

                if (c != '}') continue;
                depth--;
                if (depth != 0) continue;

                objectEndIndex = i;
                return true;
            }

            return false;
        }

        private static bool TryGetHighestSemverKey(
            string json,
            int objectStartIndex,
            int objectEndIndex,
            out string highestVersion)
        {
            highestVersion = string.Empty;
            if (objectStartIndex < 0 || objectEndIndex <= objectStartIndex) return false;

            int i = objectStartIndex + 1;
            while (i < objectEndIndex)
            {
                if (json[i] != '"')
                {
                    i++;
                    continue;
                }

                int keyStart = i + 1;
                int keyEnd = keyStart;
                bool escaped = false;
                for (; keyEnd < objectEndIndex; keyEnd++)
                {
                    char c = json[keyEnd];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"') break;
                }

                if (keyEnd >= objectEndIndex) break;

                string key = json.Substring(keyStart, keyEnd - keyStart);
                i = keyEnd + 1;
                while (i < objectEndIndex && char.IsWhiteSpace(json[i])) i++;
                if (i >= objectEndIndex || json[i] != ':') continue;
                i++;

                if (!TryParseVersion(key, out _)) continue;
                if (string.IsNullOrEmpty(highestVersion) || IsNewerVersion(key, highestVersion))
                    highestVersion = key;
            }

            return !string.IsNullOrEmpty(highestVersion);
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrEmpty(latest) || string.IsNullOrEmpty(current)) return false;
            if (string.Equals(latest, current, StringComparison.Ordinal)) return false;

            if (TryParseVersion(latest, out var latestVersionObj) &&
                TryParseVersion(current, out var currentVersionObj))
            {
                return latestVersionObj > currentVersionObj;
            }

            return string.Compare(latest, current, StringComparison.Ordinal) > 0;
        }

        private static bool TryParseVersion(string raw, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(raw)) return false;

            string normalized = raw;
            int plusIndex = normalized.IndexOf('+');
            if (plusIndex >= 0)
                normalized = normalized.Substring(0, plusIndex);

            int dashIndex = normalized.IndexOf('-');
            if (dashIndex >= 0)
                normalized = normalized.Substring(0, dashIndex);

            return Version.TryParse(normalized, out version);
        }

        private string GetCurrentPackageName()
        {
            if (!string.IsNullOrEmpty(_packageName)) return _packageName;
            CachePackageInfo();
            return _packageName;
        }

        private string GetCurrentPackageVersion()
        {
            if (!string.IsNullOrEmpty(_packageVersion)) return _packageVersion;
            CachePackageInfo();
            return string.IsNullOrEmpty(_packageVersion) ? "unknown" : _packageVersion;
        }

        private void CachePackageInfo()
        {
            _packageName = string.Empty;
            _packageVersion = string.Empty;

            var script = MonoScript.FromScriptableObject(this);
            if (script == null) return;

            string scriptPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(scriptPath)) return;

            UpmPackageInfo packageInfo = UpmPackageInfo.FindForAssetPath(scriptPath);
            if (packageInfo == null) return;

            _packageName = packageInfo.name ?? string.Empty;
            _packageVersion = packageInfo.version ?? string.Empty;
        }

        private static T LoadAssetByGuid<T>(string guid) where T : UnityEngine.Object
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        // ── 映像プレイヤー ──

        private void DrawVideoPlayerSettings(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            VRCAVProVideoPlayer[] avProPlayers)
        {
            EditorGUILayout.LabelField("映像プレイヤー", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            int newResolution = EditorGUILayout.IntPopup(
                L("最大解像度", "maximumResolution", "AVProプレイヤーの最大解像度。"),
                settings.maximumResolution,
                new[] {
                    new GUIContent("360p"), new GUIContent("480p"), new GUIContent("720p"),
                    new GUIContent("1080p"), new GUIContent("1440p"), new GUIContent("2160p"),
                },
                new[] { 360, 480, 720, 1080, 1440, 2160 });
            CopyFieldNameMenu("maximumResolution");

            bool newLowLatency = ToggleField("低遅延モード", "useLowLatency",
                "AVProの低遅延モードを有効にする。", settings.useLowLatency);

            float newCrossfade = SliderField("クロスフェード時間 [秒]", "crossfadeDurationSec",
                "Active/Standby切替時のクロスフェード時間（秒）。",
                settings.crossfadeDurationSec, 0f, 1f);

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(settings, "Change AunCast Video Player Settings");
            settings.maximumResolution = newResolution;
            settings.useLowLatency = newLowLatency;
            settings.crossfadeDurationSec = newCrossfade;
            EditorUtility.SetDirty(settings);

            foreach (var avPro in avProPlayers)
            {
                var so = new SerializedObject(avPro);
                var resProp = so.FindProperty("maximumResolution");
                if (resProp != null)
                    resProp.intValue = newResolution;
                var latencyProp = so.FindProperty("useLowLatency");
                if (latencyProp != null)
                    latencyProp.boolValue = newLowLatency;
                so.ApplyModifiedProperties();
            }

            ApplyCrossfadeSettingsToScene(root, settings);
        }

        // ── UI / 操作 ──

        private void DrawUiSettings(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField("UI / 操作", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            float newHold = SliderField("ジェスチャー保持時間 [秒]", "gestureHoldDuration",
                "長押しジェスチャーの保持時間（秒）。VR両手トリガー / 右スティック上 / デスクトップESCに共通適用。",
                settings.gestureHoldDuration, 0.1f, 2f);
            float newHudThreshold = SliderField("ジェスチャーHUD表示猶予 [秒]", "gestureHudShowThreshold",
                "HUDプログレスを表示し始めるまでの猶予（秒）。",
                settings.gestureHudShowThreshold, 0f, 0.3f);

            float newDist = SliderField("パネル自動閉じ距離 [m]", "panelAutoDismissDistance",
                "ポータブルパネルからこの距離（m）以上離れると自動的に閉じる。0 で無効。",
                settings.panelAutoDismissDistance, 0f, 10f);
            float newSight = SliderField("パネル視界外閉じ [秒]", "panelOutOfSightDismissSec",
                "ポータブルパネルが視界外に出てからこの秒数経過で自動的に閉じる。0 で無効。",
                settings.panelOutOfSightDismissSec, 0f, 60f);

            float newNear = SliderField("壁パネル近距離 [m]", "wallNearDistance",
                "この距離（m）以内に近づくとフルコンテンツ表示に切り替える（内側閾値）。",
                settings.wallNearDistance, 0f, 10f);
            float newFar = SliderField("壁パネル遠距離 [m]", "wallFarDistance",
                "この距離（m）以上離れるとResyncのみ表示に切り替える（外側閾値）。",
                settings.wallFarDistance, 0f, 10f);
            string newUnlockPasscode = TextField("壁パネル解錠パスコード", "wallUnlockPasscode",
                "WallControlPanel の Staff ビュー解錠用 4 桁数字。空文字で無効。",
                settings.wallUnlockPasscode);
            newUnlockPasscode = NormalizeWallUnlockPasscode(newUnlockPasscode);

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(settings, "Change AunCast UI Settings");
            settings.gestureHoldDuration = newHold;
            settings.gestureHudShowThreshold = newHudThreshold;
            settings.panelAutoDismissDistance = newDist;
            settings.panelOutOfSightDismissSec = newSight;
            settings.wallNearDistance = newNear;
            settings.wallFarDistance = Mathf.Max(newNear, newFar);
            settings.wallUnlockPasscode = newUnlockPasscode;
            EditorUtility.SetDirty(settings);

            ApplyUiSettingsToScene(root, settings);
        }

        // ── 再生監視 ──

        private void DrawPlaybackMonitorSettings(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField("再生監視", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("無音検知");
            EditorGUI.indentLevel++;
            float newRms = SliderField("RMS 閾値 [dBFS]", "silenceRmsThresholdDbfs",
                "無音判定RMS閾値。0 dBFS = フルスケール。",
                settings.silenceRmsThresholdDbfs, -96f, 0f);
            float newSilenceConsec = SliderField("継続秒数 [秒]", "silenceConsecutiveSec",
                "無音がこの秒数継続したらResyncを発火する。",
                settings.silenceConsecutiveSec, 0.5f, 30f);
            float newSuppress = SliderField("抑止時間 [秒]", "silenceSuppressSec",
                "Resync後に無音検知を再有効化するまでの抑止時間（秒）。",
                settings.silenceSuppressSec, 0f, 600f);
            float newPeakHold = SliderField("ピーク保持 [秒]", "silenceMeterPeakHoldSec",
                "RMSメーターのピーク値を保持する時間（秒）。",
                settings.silenceMeterPeakHoldSec, 0f, 5f);
            float newPeakDecay = SliderField("ピーク減衰 [dB/秒]", "silenceMeterPeakDecayDbPerSec",
                "ピーク保持後にピークラインが下がる速度（dB/秒）。",
                settings.silenceMeterPeakDecayDbPerSec, 0f, 60f);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("停止検知");
            EditorGUI.indentLevel++;
            float newStalled = SliderField("タイムアウト [秒]", "stalledTimeoutSec",
                "停止判定の継続時間（秒）。",
                settings.stalledTimeoutSec, 0.5f, 30f);
            float newInterval = SliderField("ポーリング間隔 [秒]", "monitorIntervalSec",
                "Active Playerの監視ポーリング間隔（秒）。",
                settings.monitorIntervalSec, 0.01f, 1f);
            float newAdvance = SliderField("前進判定閾値 [秒]", "minAdvanceThresholdSec",
                "ポーリング間隔ごとの再生位置の変化量がこの値を超えたら「再生が前進した」と判定する。",
                settings.minAdvanceThresholdSec, 0f, 0.1f);
            int newMinConsec = IntSliderField("最小連続前進回数 [回]", "minConsecutiveAdvances",
                "生存確認に必要な連続前進回数。",
                settings.minConsecutiveAdvances, 1, 30);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("ドリフト");
            EditorGUI.indentLevel++;
            float newDriftThreshold = SliderField("Resync閾値 [秒]", "driftResyncThresholdSec",
                "蓄積ドリフトがこの値を超えたら自動Resync。",
                settings.driftResyncThresholdSec, 0.01f, 1f);
            float newSmoothing = SliderField("平滑化時定数 [秒]", "driftSmoothingTimeConstant",
                "ドリフトEMAの時定数（秒）。大きいほど緩やかに追従する。",
                settings.driftSmoothingTimeConstant, 0.1f, 10f);
            float newWarmup = SliderField("猶予時間 [秒]", "driftWarmupSec",
                "再生開始直後にドリフト積算を抑制する猶予時間（秒）。",
                settings.driftWarmupSec, 0f, 30f);
            EditorGUI.indentLevel--;

            float newStall = SliderField("リブート停止超過時間 [秒]", "rebootStallSec",
                "再生位置が動かなくなってからリブートボタンを表示するまでの時間（秒）。",
                settings.rebootStallSec, 1f, 60f);

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(settings, "Change AunCast Playback Monitor Settings");
            settings.silenceRmsThresholdDbfs = newRms;
            settings.silenceConsecutiveSec = newSilenceConsec;
            settings.silenceSuppressSec = newSuppress;
            settings.silenceMeterPeakHoldSec = newPeakHold;
            settings.silenceMeterPeakDecayDbPerSec = newPeakDecay;
            settings.stalledTimeoutSec = newStalled;
            settings.monitorIntervalSec = newInterval;
            settings.minAdvanceThresholdSec = newAdvance;
            settings.minConsecutiveAdvances = newMinConsec;
            settings.driftResyncThresholdSec = newDriftThreshold;
            settings.driftSmoothingTimeConstant = newSmoothing;
            settings.driftWarmupSec = newWarmup;
            settings.rebootStallSec = newStall;
            EditorUtility.SetDirty(settings);

            ApplyPlaybackMonitorSettingsToScene(root, settings);
        }

        // ── Resync制御 ──

        private void DrawResyncSettings(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField("Resync制御", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("同時接続制限");
            EditorGUI.indentLevel++;
            int newConcurrent = IntSliderField("同時Resync上限 [人]", "maxConcurrentResyncUsers",
                "同時Resync実行数の初期上限。",
                settings.maxConcurrentResyncUsers, 1, 100);
            int newConnLimit = IntSliderField("最大接続数", "maxConnectionLimit",
                "配信サーバへの総接続数の初期上限（0 = 無制限）。",
                settings.maxConnectionLimit, 0, 200);
            float newGrant = SliderField("接続開始待ち [秒]", "grantTimeoutSec",
                "Resync許可後、接続が始まるまでの最大待機時間（秒）。",
                settings.grantTimeoutSec, 1f, 60f);
            float newRunning = SliderField("実行時間の上限 [秒]", "runningTimeoutSec",
                "1回のResync実行が許される最大時間（秒）。",
                settings.runningTimeoutSec, 1f, 120f);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("リトライ / クールダウン");
            EditorGUI.indentLevel++;
            float newCycle = SliderField("切替完了タイムアウト [秒]", "resyncCycleTimeoutSec",
                "GRANTED後、Active/Standby切替が完了するまでの最大許容時間（秒）。",
                settings.resyncCycleTimeoutSec, 1f, 120f);
            float newLocal = SliderField("読込後の待機 [秒]", "localCooldownSec",
                "LoadURL完了後、次のResyncを受け付けるまでの待機時間（秒）。",
                settings.localCooldownSec, 0f, 60f);
            float newBase = SliderField("リトライ間隔（初回） [秒]", "baseCooldownSec",
                "再試行の基本待機時間（秒）。失敗ごとに倍増する。",
                settings.baseCooldownSec, 1f, 120f);
            float newMax = SliderField("リトライ間隔（上限） [秒]", "maxRetryCooldownSec",
                "再試行の最大待機時間（秒）。倍増がこの値で頭打ちになる。",
                settings.maxRetryCooldownSec, 1f, 300f);
            EditorGUI.indentLevel--;

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(settings, "Change AunCast Resync Settings");
            settings.maxConcurrentResyncUsers = (byte)newConcurrent;
            settings.maxConnectionLimit = (byte)newConnLimit;
            settings.grantTimeoutSec = newGrant;
            settings.runningTimeoutSec = newRunning;
            settings.resyncCycleTimeoutSec = newCycle;
            settings.localCooldownSec = newLocal;
            settings.baseCooldownSec = newBase;
            settings.maxRetryCooldownSec = Mathf.Max(newBase, newMax);
            EditorUtility.SetDirty(settings);

            ApplyResyncSettingsToScene(root, settings);
        }

        private static void ApplyCrossfadeSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var switchers = root.GetComponentsInChildren<PlaybackSwitcher>(true);
            ApplyToUdonComponents(switchers, so =>
            {
                SetFloatProperty(so, "crossfadeDurationSec", settings.crossfadeDurationSec);
            });
        }

        private static void ApplyUiSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var userPanels = root.GetComponentsInChildren<UserStatusPanel>(true);
            ApplyToUdonComponents(userPanels, so =>
            {
                SetFloatProperty(so, "vrBothTriggersHoldSec", settings.gestureHoldDuration);
                SetFloatProperty(so, "vrRightStickUpHoldSec", settings.gestureHoldDuration);
                SetFloatProperty(so, "desktopEscHoldSec", settings.gestureHoldDuration);
                SetFloatProperty(so, "autoDismissDistance", settings.panelAutoDismissDistance);
                SetFloatProperty(so, "outOfSightDismissSec", settings.panelOutOfSightDismissSec);
            });

            var overlays = root.GetComponentsInChildren<HudProgressOverlay>(true);
            ApplyToUdonComponents(overlays, so =>
            {
                SetFloatProperty(so, "showThreshold", settings.gestureHudShowThreshold);
            });

            var wallPanels = root.GetComponentsInChildren<WallControlPanel>(true);
            ApplyToUdonComponents(wallPanels, so =>
            {
                SetFloatProperty(so, "wallNearDistance", settings.wallNearDistance);
                SetFloatProperty(so, "wallFarDistance", settings.wallFarDistance);
                SetStringProperty(so, "unlockPasscode", settings.wallUnlockPasscode);
            });
        }

        private static void ApplyPlaybackMonitorSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var detectors = root.GetComponentsInChildren<AudioSilenceDetector>(true);
            ApplyToUdonComponents(detectors, so =>
            {
                SetFloatProperty(so, "silenceRmsThresholdDbfs", settings.silenceRmsThresholdDbfs);
                SetFloatProperty(so, "silenceConsecutiveSec", settings.silenceConsecutiveSec);
            });

            var monitors = root.GetComponentsInChildren<ActivePlayerMonitor>(true);
            ApplyToUdonComponents(monitors, so =>
            {
                SetFloatProperty(so, "stalledTimeoutSec", settings.stalledTimeoutSec);
                SetFloatProperty(so, "monitorIntervalSec", settings.monitorIntervalSec);
                SetFloatProperty(so, "minAdvanceThresholdSec", settings.minAdvanceThresholdSec);
                SetIntProperty(so, "minConsecutiveAdvances", settings.minConsecutiveAdvances);
                SetFloatProperty(so, "driftResyncThresholdSec", settings.driftResyncThresholdSec);
                SetFloatProperty(so, "driftSmoothingTimeConstant", settings.driftSmoothingTimeConstant);
                SetFloatProperty(so, "driftWarmupSec", settings.driftWarmupSec);
            });

            var clients = root.GetComponentsInChildren<ResyncCoordinatorClient>(true);
            ApplyToUdonComponents(clients, so =>
            {
                SetFloatProperty(so, "silenceSuppressSec", settings.silenceSuppressSec);
            });

            var controllers = root.GetComponentsInChildren<LocalDualPlayerController>(true);
            ApplyToUdonComponents(controllers, so =>
            {
                SetFloatProperty(so, "rebootStallSec", settings.rebootStallSec);
            });

            var userPanels = root.GetComponentsInChildren<UserStatusPanel>(true);
            ApplyToUdonComponents(userPanels, so =>
            {
                SetFloatProperty(so, "silenceMeterPeakHoldSec", settings.silenceMeterPeakHoldSec);
                SetFloatProperty(so, "silenceMeterPeakDecayDbPerSec", settings.silenceMeterPeakDecayDbPerSec);
            });
        }

        private static void ApplyResyncSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var coordinators = root.GetComponentsInChildren<ResyncCoordinator>(true);
            ApplyToUdonComponents(coordinators, so =>
            {
                SetByteProperty(so, "maxConcurrentResyncUsers", settings.maxConcurrentResyncUsers);
                SetByteProperty(so, "maxConnectionLimit", settings.maxConnectionLimit);
                SetFloatProperty(so, "grantTimeoutSec", settings.grantTimeoutSec);
                SetFloatProperty(so, "runningTimeoutSec", settings.runningTimeoutSec);
            });

            var clients = root.GetComponentsInChildren<ResyncCoordinatorClient>(true);
            ApplyToUdonComponents(clients, so =>
            {
                SetFloatProperty(so, "resyncCycleTimeoutSec", settings.resyncCycleTimeoutSec);
                SetFloatProperty(so, "localCooldownSec", settings.localCooldownSec);
                SetFloatProperty(so, "baseCooldownSec", settings.baseCooldownSec);
                SetFloatProperty(so, "maxRetryCooldownSec", settings.maxRetryCooldownSec);
            });
        }

        private static void ApplyToUdonComponents<T>(T[] components, Action<SerializedObject> apply)
            where T : UdonSharp.UdonSharpBehaviour
        {
            foreach (var comp in components)
            {
                var so = new SerializedObject(comp);
                apply(so);
                if (!so.ApplyModifiedProperties()) continue;

                UdonSharpEditorUtility.CopyProxyToUdon(comp);
                EditorUtility.SetDirty(comp);
                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(comp);
                if (udon != null)
                    EditorUtility.SetDirty(udon);
            }
        }

        private static void SetFloatProperty(SerializedObject so, string fieldName, float value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.floatValue = value;
        }

        private static void SetIntProperty(SerializedObject so, string fieldName, int value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.intValue = value;
        }

        private static void SetByteProperty(SerializedObject so, string fieldName, byte value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.intValue = value;
        }

        private static void SetStringProperty(SerializedObject so, string fieldName, string value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.stringValue = value ?? string.Empty;
        }

        private static string NormalizeWallUnlockPasscode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            char[] buffer = new char[4];
            int count = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (!char.IsDigit(raw[i])) continue;
                buffer[count] = raw[i];
                count++;
                if (count >= 4) break;
            }

            return new string(buffer, 0, count);
        }

        private void DrawTimelineLoggingToggle(
            LocalDualPlayerController[] ldpcList,
            ActivePlayerMonitor[] apmList,
            ResyncCoordinatorClient[] rccList,
            PlaybackSwitcher[] pbsList,
            ResyncCoordinator[] rcList)
        {
            EditorGUILayout.LabelField("デバッグ", EditorStyles.boldLabel);

            bool anyOn = false;
            bool anyOff = false;

            CheckField(ldpcList, "_timelineLogging", ref anyOn, ref anyOff);
            CheckField(apmList, "_timelineLogging", ref anyOn, ref anyOff);
            CheckField(rccList, "_timelineLogging", ref anyOn, ref anyOff);
            CheckField(pbsList, "_timelineLogging", ref anyOn, ref anyOff);
            CheckField(rcList, "_timelineLogging", ref anyOn, ref anyOff);

            bool isMixed = anyOn && anyOff;
            bool currentValue = anyOn && !anyOff;

            EditorGUI.showMixedValue = isMixed;
            bool newValue = ToggleField("タイムラインログ", "_timelineLogging",
                "全コンポーネントのタイムラインログ出力を一括で切り替える。", currentValue);
            EditorGUI.showMixedValue = false;

            if (newValue != currentValue || (isMixed && !newValue))
            {
                SetField(ldpcList, "_timelineLogging", newValue);
                SetField(apmList, "_timelineLogging", newValue);
                SetField(rccList, "_timelineLogging", newValue);
                SetField(pbsList, "_timelineLogging", newValue);
                SetField(rcList, "_timelineLogging", newValue);
            }
        }

        private static void CheckField<T>(T[] components, string fieldName,
            ref bool anyOn, ref bool anyOff) where T : UdonSharp.UdonSharpBehaviour
        {
            foreach (var comp in components)
            {
                var so = new SerializedObject(comp);
                var prop = so.FindProperty(fieldName);
                if (prop == null) continue;
                if (prop.boolValue) anyOn = true;
                else anyOff = true;
            }
        }

        private static void SetField<T>(T[] components, string fieldName, bool value)
            where T : UdonSharp.UdonSharpBehaviour
        {
            foreach (var comp in components)
            {
                var so = new SerializedObject(comp);
                var prop = so.FindProperty(fieldName);
                if (prop == null) continue;
                prop.boolValue = value;
                so.ApplyModifiedProperties();

                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(comp);
                if (udon != null)
                {
                    UdonSharpEditorUtility.CopyProxyToUdon(comp);
                    EditorUtility.SetDirty(udon);
                }
            }
        }
    }
}
#endif
