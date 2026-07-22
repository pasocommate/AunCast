#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using VRC.SDK3.Video.Components.AVPro;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCastSettings の Inspector カスタムエディタ。
    /// ScriptableObject では表現しにくい設定項目を、ヘルプ付きの専用 GUI で編集できるようにする。
    /// </summary>
    [CustomEditor(typeof(PasocomMate.AunCast.AunCastSettings))]
    public partial class AunCastSettingsInspector : Editor
    {
        // 配布元の VPM リスティング（PasocomMate）。エディタのバージョン更新チェックで参照する。
        private const string DEFAULT_VPM_LISTING_URL = "https://pasocommate.chigiri.tokyo/index.json";
        private const string TMP_FALLBACK_DEFAULT_FONT_GUID = "b0cf90c18247f154094021e2de9bf529";
        private const string TMP_FALLBACK_NOTO_FONT_GUID = "32134e5dc8c950c4cb5bb7deaae7d539";
        private const string TMP_FALLBACK_MENU_PATH = "Tools→TextMesh Pro VRC Fallback Font JPを設定";
        private const double VPM_VERSION_REQUEST_TIMEOUT_SEC = 8.0;
        private const string SESSION_KEY_VPM_CHECK_DONE = "AunCast.SettingsEditor.VpmCheckDone";
        private const string SESSION_KEY_VPM_HAS_UPDATE = "AunCast.SettingsEditor.VpmHasUpdate";
        private const string SESSION_KEY_VPM_LATEST_VERSION = "AunCast.SettingsEditor.VpmLatestVersion";
        // 折りたたみ状態は Editor インスタンスに持たせると選択切替で破棄されるため、SessionState に保存する。
        private const string SESSION_KEY_MIGRATION_EXPANDED = "AunCast.SettingsEditor.MigrationExpanded";
        private const string SESSION_KEY_CLEANUP_EXPANDED = "AunCast.SettingsEditor.CleanupExpanded";
        private const string SESSION_KEY_MIGRATION_INCLUDE_EDITOR_ONLY = "AunCast.SettingsEditor.MigrationIncludeEditorOnly";
        private const string SCREEN_COMPONENT_TYPE_NAME = "VRCAVProVideoScreen";
        private const string SPEAKER_COMPONENT_TYPE_NAME = "VRCAVProVideoSpeaker";
        private const string AUDIO_OUTPUT_TUNNEL_COMPONENT_TYPE_NAME = "AudioOutputTunnel";
        private const string VRC_SPATIAL_AUDIO_SOURCE_COMPONENT_TYPE_NAME = "VRCSpatialAudioSource";

        private static readonly Type[] AUDIO_FILTER_TYPES = new[]
        {
            typeof(AudioReverbFilter),
            typeof(AudioLowPassFilter),
            typeof(AudioHighPassFilter),
            typeof(AudioDistortionFilter),
            typeof(AudioEchoFilter),
            typeof(AudioChorusFilter),
        };
        private const string AUDIO_LINK_AUDIO_SOURCE_VARIABLE_NAME = "audioSource";
        private const string EVENT_BUS_OBJECT_NAME = "EventBus";
        private const string AUNCAST_EVENT_BUS_ASSET_GUID = "86f742d2e8954336a9cd87f1e4527d80";
        // 無効化した AudioLinkInput の隣に置く注記オブジェクト名（英語・エディタ上の説明用）
        private const string AUDIOLINK_INPUT_NOTE_NAME =
            "AudioLink's referenced audio source is managed automatically by AunCast";

        // 利用規約 PDF（VN3ライセンス）の GUID。パス文字列リテラルは使わず GUID で特定する。
        private const string VN3_LICENSE_JA_GUID = "63ab57b266732074988fcf3b95489e05";
        private const string VN3_LICENSE_EN_GUID = "10d1177e22c28e64bb6330fb48d1a183";

        private bool _consentCheckbox;

        private bool _prevAlt;
        private bool _vpmVersionCheckRequested;
        private bool _vpmVersionCheckInProgress;
        private UnityWebRequest _vpmVersionRequest;
        private double _vpmVersionRequestStartTime;
        private bool _hasVersionUpdate;
        private string _latestVersion;
        private bool _vpmSessionCacheLoaded;

        private static readonly Dictionary<int, MigrationCandidateCache> MigrationCaches =
            new Dictionary<int, MigrationCandidateCache>();
        private MigrationCandidateCache _migrationCache;
        private int _migrationCacheRootId;
        private static bool _migrationSectionExpanded
        {
            get => SessionState.GetBool(SESSION_KEY_MIGRATION_EXPANDED, true);
            set => SessionState.SetBool(SESSION_KEY_MIGRATION_EXPANDED, value);
        }

        private static bool _cleanupSectionExpanded
        {
            get => SessionState.GetBool(SESSION_KEY_CLEANUP_EXPANDED, true);
            set => SessionState.SetBool(SESSION_KEY_CLEANUP_EXPANDED, value);
        }

        // 「候補を再検出」で EditorOnly 配下の出力を候補に含めるか。負荷や誤変換を避けるため既定は OFF。
        private static bool _migrationIncludeEditorOnly
        {
            get => SessionState.GetBool(SESSION_KEY_MIGRATION_INCLUDE_EDITOR_ONLY, false);
            set => SessionState.SetBool(SESSION_KEY_MIGRATION_INCLUDE_EDITOR_ONLY, value);
        }

        // 接続先ポップアップの選択値。0/1 は playerIndex そのもの、2 は「自動複製」。
        private const int MIGRATION_OPTION_AUTO_DUPLICATE = 2;
        private const int TUNNEL_MIGRATION_MODE_COMPAT_TUNNEL = 0;
        private const int TUNNEL_MIGRATION_MODE_DIRECT_SPEAKERS = 1;

        private enum MigrationCandidateKind
        {
            Screen,
            Speaker,
            AudioOutputTunnel
        }

        private readonly struct MigrationCandidate
        {
            public readonly MigrationCandidateKind kind;
            public readonly GameObject gameObject;
            public readonly AudioSource audioSource;
            public readonly Component sourceComponent;
            public readonly string hierarchyPath;
            public readonly string statusLabel;
            public readonly ReferenceWarning referenceWarning;
            public readonly string selectionKey;
            public readonly int inferredPlayerIndex;
            public readonly bool isConfigured;

            public MigrationCandidate(
                MigrationCandidateKind kind,
                GameObject gameObject,
                AudioSource audioSource,
                Component sourceComponent,
                string hierarchyPath,
                string statusLabel,
                string selectionKey,
                int inferredPlayerIndex,
                bool isConfigured)
                : this(
                    kind,
                    gameObject,
                    audioSource,
                    sourceComponent,
                    hierarchyPath,
                    statusLabel,
                    default,
                    selectionKey,
                    inferredPlayerIndex,
                    isConfigured)
            {
            }

            public MigrationCandidate(
                MigrationCandidateKind kind,
                GameObject gameObject,
                AudioSource audioSource,
                Component sourceComponent,
                string hierarchyPath,
                string statusLabel,
                ReferenceWarning referenceWarning,
                string selectionKey,
                int inferredPlayerIndex,
                bool isConfigured)
            {
                this.kind = kind;
                this.gameObject = gameObject;
                this.audioSource = audioSource;
                this.sourceComponent = sourceComponent;
                this.hierarchyPath = hierarchyPath;
                this.statusLabel = statusLabel;
                this.referenceWarning = referenceWarning;
                this.selectionKey = selectionKey;
                this.inferredPlayerIndex = inferredPlayerIndex;
                this.isConfigured = isConfigured;
            }
        }

        private readonly struct ReferenceWarning
        {
            public readonly string message;
            public readonly Component[] components;
            public readonly Component[] audioLinkComponents;
            public readonly MessageType messageType;

            public ReferenceWarning(string message, Component[] components)
                : this(message, components, MessageType.Warning)
            {
            }

            public ReferenceWarning(string message, Component[] components, MessageType messageType)
                : this(message, components, Array.Empty<Component>(), messageType)
            {
            }

            public ReferenceWarning(string message, Component[] components, Component[] audioLinkComponents, MessageType messageType)
            {
                this.message = message;
                this.components = components ?? Array.Empty<Component>();
                this.audioLinkComponents = audioLinkComponents ?? Array.Empty<Component>();
                this.messageType = messageType;
            }
        }

        private readonly struct TunnelOutputSource
        {
            public readonly AudioSource source;
            public readonly int mode;

            public TunnelOutputSource(AudioSource source, int mode)
            {
                this.source = source;
                this.mode = mode;
            }
        }

        private readonly struct ResidualCleanupCandidate
        {
            public readonly GameObject gameObject;
            public readonly string componentName;
            public readonly string hierarchyPath;

            public ResidualCleanupCandidate(Component component, string typeLabel)
            {
                gameObject = component != null ? component.gameObject : null;
                componentName = typeLabel;
                hierarchyPath = component != null ? GetHierarchyPath(component.transform) : string.Empty;
            }
        }

        private readonly struct MigrationValidationIssue
        {
            public readonly string message;
            public readonly UnityEngine.Object selectionTarget;
            public readonly UnityEngine.Object pingTarget;
            public readonly bool selectable;
            public readonly string linkText;

            public MigrationValidationIssue(string message, UnityEngine.Object selectionTarget)
                : this(message, selectionTarget, selectionTarget, selectionTarget != null, string.Empty)
            {
            }

            public MigrationValidationIssue(
                string message,
                UnityEngine.Object selectionTarget,
                UnityEngine.Object pingTarget)
                : this(message, selectionTarget, pingTarget, selectionTarget != null, string.Empty)
            {
            }

            public MigrationValidationIssue(
                string message,
                UnityEngine.Object selectionTarget,
                UnityEngine.Object pingTarget,
                bool selectable)
                : this(message, selectionTarget, pingTarget, selectable, string.Empty)
            {
            }

            public MigrationValidationIssue(
                string message,
                UnityEngine.Object selectionTarget,
                UnityEngine.Object pingTarget,
                bool selectable,
                string linkText)
            {
                this.message = message;
                this.selectionTarget = selectionTarget;
                this.pingTarget = pingTarget;
                this.selectable = selectable;
                this.linkText = linkText ?? string.Empty;
            }
        }

        private sealed class MigrationCandidateCache
        {
            public MigrationCandidate[] candidates = Array.Empty<MigrationCandidate>();
            public ResidualCleanupCandidate[] residualCleanupCandidates = Array.Empty<ResidualCleanupCandidate>();
            public List<MigrationValidationIssue> validationErrors = new List<MigrationValidationIssue>();
            public readonly Dictionary<string, int> speakerPlayerIndex = new Dictionary<string, int>();
            public readonly Dictionary<string, int> tunnelMigrationMode = new Dictionary<string, int>();
            public bool detected;
        }

        private readonly struct SpeakerSetupContext
        {
            public readonly AunCastVideoPlayerManager managerA;
            public readonly AunCastVideoPlayerManager managerB;
            public readonly VRCAVProVideoPlayer playerA;
            public readonly VRCAVProVideoPlayer playerB;
            public readonly AunCastPlaybackSwitcher switcher;

            public SpeakerSetupContext(
                AunCastVideoPlayerManager managerA,
                AunCastVideoPlayerManager managerB,
                VRCAVProVideoPlayer playerA,
                VRCAVProVideoPlayer playerB,
                AunCastPlaybackSwitcher switcher)
            {
                this.managerA = managerA;
                this.managerB = managerB;
                this.playerA = playerA;
                this.playerB = playerB;
                this.switcher = switcher;
            }
        }

        // 言語に応じてラベル/ツールチップを切り替える。Alt 押下中は backing フィールド名を表示する。
        private static GUIContent L(string ja, string en, string fieldName, string tooltipJa, string tooltipEn)
        {
            bool alt = Event.current != null && Event.current.alt;
            string label = AunCastEditorLocalization.Localize(ja, en);
            string tooltip = AunCastEditorLocalization.Localize(tooltipJa, tooltipEn);
            return new GUIContent(alt ? fieldName : label, tooltip);
        }

        private static void CopyFieldNameMenu(string fieldName)
        {
            if (Event.current.type != EventType.ContextClick) return;
            var rect = GUILayoutUtility.GetLastRect();
            rect.width = EditorGUIUtility.labelWidth;
            if (!rect.Contains(Event.current.mousePosition)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(AunCastEditorLocalization.Localize(
                    $"変数名をコピー: {fieldName}", $"Copy field name: {fieldName}")), false,
                () => EditorGUIUtility.systemCopyBuffer = fieldName);
            menu.ShowAsContext();
            Event.current.Use();
        }

        private static GUIStyle PaddedHelpBoxStyle()
        {
            return new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(24, 24, 12, 14),
                margin = new RectOffset(0, 0, 4, 4)
            };
        }

        private static GUIStyle CandidateHelpBoxStyle()
        {
            return new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
        }

        private static GUIStyle WrappedMiniLabelStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };
        }

        private static GUIStyle SectionFoldoutStyle()
        {
            return new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold
            };
        }

        private static bool SectionFoldout(bool expanded, string ja, string en)
        {
            return EditorGUILayout.Foldout(
                expanded,
                AunCastEditorLocalization.Localize(ja, en),
                true,
                SectionFoldoutStyle());
        }

        // オブジェクト選択リンク（パス）用のリンク色スタイル。
        private static GUIStyle LinkPathStyle()
        {
            var link = new Color(0.30f, 0.56f, 1f);
            return new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = link },
                hover = { textColor = link },
                focused = { textColor = link },
                active = { textColor = link }
            };
        }

        private float SliderField(string ja, string en, string fieldName, string tooltipJa, string tooltipEn,
            float value, float min, float max)
        {
            float result = EditorGUILayout.Slider(L(ja, en, fieldName, tooltipJa, tooltipEn), value, min, max);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        private int IntSliderField(string ja, string en, string fieldName, string tooltipJa, string tooltipEn,
            int value, int min, int max)
        {
            int result = EditorGUILayout.IntSlider(L(ja, en, fieldName, tooltipJa, tooltipEn), value, min, max);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        private bool ToggleField(string ja, string en, string fieldName, string tooltipJa, string tooltipEn, bool value)
        {
            bool result = EditorGUILayout.Toggle(L(ja, en, fieldName, tooltipJa, tooltipEn), value);
            CopyFieldNameMenu(fieldName);
            return result;
        }

        private static int ToggleBitFlag(int flags, int bit, GUIContent label)
        {
            bool on = EditorGUILayout.Toggle(label, (flags & bit) != 0);
            return on ? flags | bit : flags & ~bit;
        }

        private string TextField(string ja, string en, string fieldName, string tooltipJa, string tooltipEn, string value)
        {
            string result = EditorGUILayout.TextField(L(ja, en, fieldName, tooltipJa, tooltipEn), value ?? string.Empty);
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
            AunCastInspectorBanner.Draw(this, _hasVersionUpdate, _latestVersion);

            // 利用規約に未同意の間は設定 UI を描画せず、同意ゲートのみを表示する。
            if (!DrawConsentGateIfNeeded())
                return;

            DrawTmpFallbackFontWarning();

            var settings = (PasocomMate.AunCast.AunCastSettings)target;
            var root = settings.transform;

            var ldpcList = root.GetComponentsInChildren<AunCastDualPlayerController>(true);
            var apmList = root.GetComponentsInChildren<AunCastActivePlayerMonitor>(true);
            var rccList = root.GetComponentsInChildren<AunCastResyncCoordinatorClient>(true);
            var pbsList = root.GetComponentsInChildren<AunCastPlaybackSwitcher>(true);
            var rcList = root.GetComponentsInChildren<AunCastResyncCoordinator>(true);
            AutoAssignAudioLinkBehaviour(pbsList);

            int totalCount = ldpcList.Length + apmList.Length + rccList.Length + pbsList.Length + rcList.Length;

            if (totalCount == 0)
            {
                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        "AunCastコンポーネントが見つかりません｡ AunCast ルート配下で設定してください｡",
                        "No AunCast components were found. Configure them under the AunCast root."),
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(8);
            DrawAvProSpeakerSetupTools(root, settings);

            EditorGUILayout.Space(8);
            DrawWallPanelReferenceTools(root);

            EditorGUILayout.Space(8);

            // ── 映像プレイヤー ──
            DrawVideoPlayerSettings(root, settings);

            EditorGUILayout.Space(8);

            // ── UI/操作 ──
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
                return AunCastEditorLocalization.Localize(
                    "TMP Settings が見つかりません｡ Edit→Project Settings→TextMesh Pro から TMP Essentials を先にインポートしてください｡",
                    "TMP Settings was not found. Open Edit > Project Settings > TextMesh Pro and import TMP Essentials first.");
            }

            var defaultFontAsset = AunCastEditorAssetUtility.LoadAssetByGuid<TMP_FontAsset>(TMP_FALLBACK_DEFAULT_FONT_GUID);
            var fallbackFontAsset = AunCastEditorAssetUtility.LoadAssetByGuid<TMP_FontAsset>(TMP_FALLBACK_NOTO_FONT_GUID);
            if (defaultFontAsset == null || fallbackFontAsset == null)
            {
                return AunCastEditorLocalization.Localize(
                    "net.narazaka.vrchat.tmp-fallback-fonts-jp のフォントアセットが見つかりません｡ Manage Project で TextMesh Pro VRC Fallback Font JP を導入してください｡",
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
            return AunCastEditorLocalization.Localize(
                $"TMP フォールバック設定が未適用です｡ {TMP_FALLBACK_MENU_PATH} を実行してください｡ 実行後はシーンを開き直してください｡",
                $"TMP fallback font settings are not applied. Run {TMP_FALLBACK_MENU_PATH}. After that, reopen the scene.");
        }

    }
}
#endif
