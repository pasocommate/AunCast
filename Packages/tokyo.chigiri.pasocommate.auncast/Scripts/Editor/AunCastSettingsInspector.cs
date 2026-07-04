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
    public class AunCastSettingsInspector : Editor
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
        private const string SCREEN_COMPONENT_TYPE_NAME = "VRCAVProVideoScreen";
        private const string SPEAKER_COMPONENT_TYPE_NAME = "VRCAVProVideoSpeaker";
        private const string AUNCAST_EVENT_HUB_NAME = "AunCastEventHub";
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

        // 接続先ポップアップの選択値。0/1 は playerIndex そのもの、2 は「自動複製」。
        private const int MIGRATION_OPTION_AUTO_DUPLICATE = 2;

        private enum MigrationCandidateKind
        {
            Screen,
            Speaker
        }

        private readonly struct MigrationCandidate
        {
            public readonly MigrationCandidateKind kind;
            public readonly GameObject gameObject;
            public readonly AudioSource audioSource;
            public readonly Component sourceComponent;
            public readonly string hierarchyPath;
            public readonly string statusLabel;
            public readonly string selectionKey;
            public readonly int inferredPlayerIndex;
            public readonly bool isTunnelLike;
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
                bool isTunnelLike,
                bool isConfigured)
            {
                this.kind = kind;
                this.gameObject = gameObject;
                this.audioSource = audioSource;
                this.sourceComponent = sourceComponent;
                this.hierarchyPath = hierarchyPath;
                this.statusLabel = statusLabel;
                this.selectionKey = selectionKey;
                this.inferredPlayerIndex = inferredPlayerIndex;
                this.isTunnelLike = isTunnelLike;
                this.isConfigured = isConfigured;
            }
        }

        private readonly struct ResidualCleanupCandidate
        {
            public readonly GameObject gameObject;
            public readonly string label;

            public ResidualCleanupCandidate(Component component, string typeLabel)
            {
                gameObject = component != null ? component.gameObject : null;
                label = component != null
                    ? typeLabel + ": " + GetHierarchyPath(component.transform)
                    : typeLabel;
            }
        }

        private sealed class MigrationCandidateCache
        {
            public MigrationCandidate[] candidates = Array.Empty<MigrationCandidate>();
            public ResidualCleanupCandidate[] residualCleanupCandidates = Array.Empty<ResidualCleanupCandidate>();
            public List<string> validationErrors = new List<string>();
            public readonly Dictionary<string, int> speakerPlayerIndex = new Dictionary<string, int>();
            public bool detected;
        }

        private readonly struct SpeakerSetupContext
        {
            public readonly VideoPlayerManager managerA;
            public readonly VideoPlayerManager managerB;
            public readonly VRCAVProVideoPlayer playerA;
            public readonly VRCAVProVideoPlayer playerB;
            public readonly PlaybackSwitcher switcher;

            public SpeakerSetupContext(
                VideoPlayerManager managerA,
                VideoPlayerManager managerB,
                VRCAVProVideoPlayer playerA,
                VRCAVProVideoPlayer playerB,
                PlaybackSwitcher switcher)
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

            var ldpcList = root.GetComponentsInChildren<LocalDualPlayerController>(true);
            var apmList = root.GetComponentsInChildren<ActivePlayerMonitor>(true);
            var rccList = root.GetComponentsInChildren<ResyncCoordinatorClient>(true);
            var pbsList = root.GetComponentsInChildren<PlaybackSwitcher>(true);
            var rcList = root.GetComponentsInChildren<ResyncCoordinator>(true);
            AutoAssignAudioLinkBehaviour(pbsList);

            int totalCount = ldpcList.Length + apmList.Length + rccList.Length + pbsList.Length + rcList.Length;

            if (totalCount == 0)
            {
                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        "AunCastコンポーネントが見つかりません。AunCast ルート配下で設定してください。",
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
            var avProPlayers = root.GetComponentsInChildren<VRCAVProVideoPlayer>(true);
            DrawVideoPlayerSettings(root, settings, avProPlayers);

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

        // ── 利用規約 同意ゲート ──

        /// <summary>
        /// 利用規約に同意済みなら true（設定 UI を続行）。未同意なら同意ゲートを描画して false。
        /// </summary>
        private bool DrawConsentGateIfNeeded()
        {
            string version = GetCurrentPackageVersion();
            int major = GetMajorVersion(version);
            if (AunCastConsentStore.HasConsented(major))
                return true;

            DrawConsentGate(version);
            return false;
        }

        private static int GetMajorVersion(string version)
        {
            return TryParseVersion(version, out var parsed) ? parsed.Major : -1;
        }

        private void DrawConsentGate(string version)
        {
            EditorGUILayout.Space(8);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("利用規約への同意", "Terms of Use"),
                titleStyle);

            EditorGUILayout.HelpBox(
                AunCastEditorLocalization.Localize(
                    "AunCast を使用するには利用規約（VN3 ライセンス）への同意が必要です。下のボタンから規約全文を開いて内容を確認し、同意のうえ設定を続けてください。同意するまで設定項目は表示されません。",
                    "Using AunCast requires agreement to the Terms of Use (VN3 License). Open the full terms with the buttons below, review them, then agree to continue. Settings stay hidden until you agree."),
                MessageType.Warning);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                    AunCastEditorLocalization.Localize("利用規約（日本語）を開く", "Open Terms (Japanese)"),
                    GUILayout.Height(24)))
                {
                    OpenLicensePdf(VN3_LICENSE_JA_GUID);
                }
                if (GUILayout.Button(
                    AunCastEditorLocalization.Localize("利用規約（English）を開く", "Open Terms (English)"),
                    GUILayout.Height(24)))
                {
                    OpenLicensePdf(VN3_LICENSE_EN_GUID);
                }
            }

            EditorGUILayout.Space(6);

            // 規約を読んだうえでチェック→同意ボタン有効化、の二段階で誤クリックを防ぐ。
            _consentCheckbox = EditorGUILayout.ToggleLeft(
                AunCastEditorLocalization.Localize(
                    "利用規約の内容を確認し、同意します。",
                    "I have read and agree to the Terms of Use."),
                _consentCheckbox);

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(!_consentCheckbox))
            {
                if (GUILayout.Button(
                    AunCastEditorLocalization.Localize("同意して続行", "Agree and Continue"),
                    GUILayout.Height(28)))
                {
                    AunCastConsentStore.SetConsented(GetMajorVersion(version), version);
                    _consentCheckbox = false;
                    // 描画する UI の構成が変わるため、現フレームの GUI を一旦やり直す。
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space(8);
        }

        private static void OpenLicensePdf(string guid)
        {
            var asset = LoadAssetByGuid<UnityEngine.Object>(guid);
            if (asset == null)
            {
                EditorUtility.DisplayDialog(
                    "AunCast",
                    AunCastEditorLocalization.Localize(
                        "利用規約ファイルが見つかりませんでした。",
                        "Terms of Use file was not found."),
                    "OK");
                return;
            }

            AssetDatabase.OpenAsset(asset);
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
                return AunCastEditorLocalization.Localize(
                    "TMP Settings が見つかりません。Edit→Project Settings→TextMesh Pro から TMP Essentials を先にインポートしてください。",
                    "TMP Settings was not found. Open Edit > Project Settings > TextMesh Pro and import TMP Essentials first.");
            }

            var defaultFontAsset = LoadAssetByGuid<TMP_FontAsset>(TMP_FALLBACK_DEFAULT_FONT_GUID);
            var fallbackFontAsset = LoadAssetByGuid<TMP_FontAsset>(TMP_FALLBACK_NOTO_FONT_GUID);
            if (defaultFontAsset == null || fallbackFontAsset == null)
            {
                return AunCastEditorLocalization.Localize(
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
            return AunCastEditorLocalization.Localize(
                $"TMP フォールバック設定が未適用です。{TMP_FALLBACK_MENU_PATH} を実行してください。実行後はシーンを開き直してください。",
                $"TMP fallback font settings are not applied. Run {TMP_FALLBACK_MENU_PATH}. After that, reopen the scene.");
        }

        private void DrawWallPanelReferenceTools(Transform root)
        {
            using (new EditorGUILayout.VerticalScope(PaddedHelpBoxStyle()))
            {
                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        "AunCast 配下の中核参照と、同一シーン全体の AunCastScreen / AunCastUiScreen / AunCastSpeaker / AunCastAudioOutputTunnel を再スキャンして再配線します。",
                        "Re-scans and re-wires core AunCast references plus all AunCastScreen / AunCastUiScreen / AunCastSpeaker / AunCastAudioOutputTunnel components in the same scene."),
                    MessageType.None);
                using (new EditorGUI.DisabledScope(root == null))
                {
                    if (!GUILayout.Button(
                        AunCastEditorLocalization.Localize("AunCast参照を再配線", "Re-wire AunCast References"),
                        GUILayout.Height(24)))
                        return;

                    RewireEventHubAndConsumers(root, recordUndo: true, writeLog: true);
                }
            }
        }

        internal static void RewireEventHubAndConsumers(Transform root, bool recordUndo, bool writeLog)
        {
            if (root == null) return;

            var controller = root.GetComponentInChildren<LocalDualPlayerController>(true);
            var staffPanel = root.GetComponentInChildren<StaffControlPanel>(true);
            var portablePanel = root.GetComponentInChildren<UserStatusPanel>(true);
            var settings = root.GetComponent<PasocomMate.AunCast.AunCastSettings>();
            var idleScreenTexture = settings != null ? settings.idleScreenTexture : null;
            Scene scene = root.gameObject.scene;
            var eventBus = FindOrCreateEventBus(root, createIfMissing: recordUndo, writeLog: writeLog);
            var switchers = root.GetComponentsInChildren<PlaybackSwitcher>(true);
            AutoAssignAudioLinkBehaviour(switchers, recordUndo);
            // AudioLink 付属の内蔵スピーカー（AudioLinkInput）を無効化＋EditorOnly 化する。
            // AunCast が AudioLink 入力をランタイムで差し替えるため付属スピーカーは不要（冪等）。
            int audioLinkInputNeutralized = NeutralizeAudioLinkInputs(scene, recordUndo);
            var meshScreens = FindSceneComponents<AunCastScreen>(scene);
            var uiScreens = FindSceneComponents<AunCastUiScreen>(scene);
            var speakers = FindSceneComponents<AunCastSpeaker>(scene);
            var audioOutputTunnels = FindSceneComponents<AunCastAudioOutputTunnel>(scene);
            var wallPanels = root.GetComponentsInChildren<WallControlPanel>(true);
            var userPanels = root.GetComponentsInChildren<UserStatusPanel>(true);

            if (controller == null || staffPanel == null || portablePanel == null)
            {
                if (writeLog)
                    Debug.LogWarning("[AunCast] 再配線を中止しました。LocalDualPlayerController / StaffControlPanel / UserStatusPanel のいずれかが見つかりません。");
                return;
            }

            int busUpdated = 0;
            if (eventBus != null)
            {
                // subscriber は Bus 側フィールド型 (UdonBehaviour[]) に合わせて
                // backing UdonBehaviour 配列へ変換してから注入する。
                var videoTextureSubscribers = BuildVideoTextureSubscribers(meshScreens, uiScreens);
                var localStateSubscribers = ToUdonSubscribers(wallPanels);
                var portablePanelShownSubscribers = ToUdonSubscribers(wallPanels);
                var so = new SerializedObject(eventBus);
                bool changed = false;
                changed |= SetObjectArrayProperty(so, "videoTextureSubscribers", videoTextureSubscribers);
                changed |= SetObjectArrayProperty(so, "localStateSubscribers", localStateSubscribers);
                changed |= SetObjectArrayProperty(so, "portablePanelShownSubscribers", portablePanelShownSubscribers);
                if (changed && ApplyUdonSerializedChanges(eventBus, so, "Rewire AunCastEventBus Subscribers", recordUndo))
                    busUpdated++;
            }

            int publisherUpdated = 0;
            if (eventBus != null)
            {
                foreach (var switcher in switchers)
                {
                    if (switcher == null) continue;
                    var so = new SerializedObject(switcher);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);
                    if (changed && ApplyUdonSerializedChanges(switcher, so, "Rewire PlaybackSwitcher EventBus", recordUndo))
                        publisherUpdated++;
                }

                var controllers = root.GetComponentsInChildren<LocalDualPlayerController>(true);
                foreach (var ctrl in controllers)
                {
                    if (ctrl == null) continue;
                    var so = new SerializedObject(ctrl);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);
                    if (changed && ApplyUdonSerializedChanges(ctrl, so, "Rewire LocalDualPlayerController EventBus", recordUndo))
                        publisherUpdated++;
                }
            }

            int wallUpdated = 0;
            foreach (var wall in wallPanels)
            {
                if (wall == null) continue;
                var so = new SerializedObject(wall);
                bool changed = false;
                changed |= SetObjectProperty(so, "controller", controller);
                changed |= SetObjectProperty(so, "staffPanel", staffPanel);
                changed |= SetObjectProperty(so, "portablePanel", portablePanel);
                if (eventBus != null)
                    changed |= SetObjectProperty(so, "eventBus", eventBus);

                if (changed && ApplyUdonSerializedChanges(wall, so, "Rewire WallControlPanel References", recordUndo))
                    wallUpdated++;
            }

            int userUpdated = 0;
            if (eventBus != null)
            {
                foreach (var user in userPanels)
                {
                    if (user == null) continue;
                    var so = new SerializedObject(user);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);

                    if (changed && ApplyUdonSerializedChanges(user, so, "Rewire UserStatusPanel EventBus", recordUndo))
                        userUpdated++;
                }
            }

            int screenUpdated = 0;
            if (eventBus != null)
            {
                foreach (var mesh in meshScreens)
                {
                    if (mesh == null) continue;
                    var so = new SerializedObject(mesh);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);
                    // 停止中の固定画像を AunCastSettings から転写する
                    changed |= SetObjectProperty(so, "idleTexture", idleScreenTexture);
                    if (changed && ApplyUdonSerializedChanges(mesh, so, "Rewire AunCastScreen EventBus", recordUndo))
                        screenUpdated++;
                }
                foreach (var ui in uiScreens)
                {
                    if (ui == null) continue;
                    var so = new SerializedObject(ui);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);
                    // 停止中の固定画像を AunCastSettings から転写する
                    changed |= SetObjectProperty(so, "idleTexture", idleScreenTexture);
                    if (changed && ApplyUdonSerializedChanges(ui, so, "Rewire AunCastUiScreen EventBus", recordUndo))
                        screenUpdated++;
                }
            }

            // Core→UI の通知先（staffNotifyTarget）を StaffControlPanel へ配線する。
            // SendCustomEvent による通知のため、フィールドは UdonSharpBehaviour 基底型で受ける。
            int notifyUpdated = 0;
            if (staffPanel != null)
            {
                foreach (var ctrl in root.GetComponentsInChildren<LocalDualPlayerController>(true))
                {
                    if (ctrl == null) continue;
                    var so = new SerializedObject(ctrl);
                    if (SetObjectProperty(so, "staffNotifyTarget", staffPanel)
                        && ApplyUdonSerializedChanges(ctrl, so, "Rewire LocalDualPlayerController StaffNotifyTarget", recordUndo))
                        notifyUpdated++;
                }
                foreach (var coord in root.GetComponentsInChildren<ResyncCoordinator>(true))
                {
                    if (coord == null) continue;
                    var so = new SerializedObject(coord);
                    if (SetObjectProperty(so, "staffNotifyTarget", staffPanel)
                        && ApplyUdonSerializedChanges(coord, so, "Rewire ResyncCoordinator StaffNotifyTarget", recordUndo))
                        notifyUpdated++;
                }
                foreach (var monitor in root.GetComponentsInChildren<PlaybackMonitor>(true))
                {
                    if (monitor == null) continue;
                    var so = new SerializedObject(monitor);
                    if (SetObjectProperty(so, "staffNotifyTarget", staffPanel)
                        && ApplyUdonSerializedChanges(monitor, so, "Rewire PlaybackMonitor StaffNotifyTarget", recordUndo))
                        notifyUpdated++;
                }
            }

            int speakerUpdated = 0;
            int tunnelUpdated = 0;
            int sinkMutedUpdated = 0;
            if (TryResolveSpeakerSetupContext(root, out var speakerContext, out _) && speakers != null)
            {
                speakerUpdated = RewireDeclaredSpeakers(settings, speakerContext, speakers, recordUndo, writeLog);
                AudioSource tunnelInputA = FindFirstSpeakerSource(speakers, AunCastSpeaker.PLAYER_A);
                AudioSource tunnelInputB = FindFirstSpeakerSource(speakers, AunCastSpeaker.PLAYER_B);
                tunnelUpdated = RewireAudioOutputTunnels(audioOutputTunnels, tunnelInputA, tunnelInputB, recordUndo);

                // トンネルがある場合、その入力になる内蔵シンクは「トンネル出力」経由でのみ鳴らすため、
                // シンク自身は不可聴化する。GetOutputData は volume の影響を受ける（＝トンネルが読む信号は
                // volume 反映済み）ため、volume ではなく空間ロールオフを 0 にして GetOutputData を保ったまま
                // 直接音だけを消す（参考実装のカスタムロールオフ手法）。
                if (audioOutputTunnels != null && audioOutputTunnels.Length > 0)
                {
                    if (MakeTunnelInputInaudible(tunnelInputA, recordUndo)) sinkMutedUpdated++;
                    if (MakeTunnelInputInaudible(tunnelInputB, recordUndo)) sinkMutedUpdated++;
                }
            }

            if (writeLog)
                Debug.Log($"[AunCast] EventBus参照を再配線しました。Bus: {busUpdated}件 / Publisher: {publisherUpdated}件 / WallControlPanel: {wallUpdated}件 / UserStatusPanel: {userUpdated}件 / Screen: {screenUpdated}件 / Speaker: {speakerUpdated}件 / Tunnel: {tunnelUpdated}件 / シンク不可聴化: {sinkMutedUpdated}件 / AudioLink入力無効化: {audioLinkInputNeutralized}件 / 通知先: {notifyUpdated}件");
        }

        private static AunCastEventBus FindOrCreateEventBus(Transform root, bool createIfMissing, bool writeLog)
        {
            if (root == null) return null;

            var eventBus = root.GetComponentInChildren<AunCastEventBus>(true);
            if (eventBus != null)
            {
                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(eventBus) != null)
                    return eventBus;

                if (!createIfMissing)
                    return null;

                if (writeLog)
                    Debug.LogWarning("[AunCast] backing UdonBehaviour のない AunCastEventBus を検出したため作り直します。", eventBus);
                UdonSharpUndo.DestroyImmediate(eventBus);
                eventBus = null;
            }
            if (!createIfMissing) return null;

            if (LoadAssetByGuid<UnityEngine.Object>(AUNCAST_EVENT_BUS_ASSET_GUID) == null)
            {
                if (writeLog)
                    Debug.LogWarning("[AunCast] AunCastEventBus.asset が見つからないため、AunCastEventHub を作成できません。");
                return null;
            }

            Transform hubTransform = root.Find(AUNCAST_EVENT_HUB_NAME);
            GameObject hubObject;
            if (hubTransform != null)
            {
                hubObject = hubTransform.gameObject;
            }
            else
            {
                hubObject = new GameObject(AUNCAST_EVENT_HUB_NAME);
                Undo.RegisterCreatedObjectUndo(hubObject, "Create AunCastEventHub");
                hubObject.transform.SetParent(root, false);
            }

            eventBus = UdonSharpUndo.AddComponent<AunCastEventBus>(hubObject);
            if (eventBus == null) return null;

            EditorUtility.SetDirty(eventBus);
            PrefabUtility.RecordPrefabInstancePropertyModifications(eventBus);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(eventBus);
            if (udon != null)
            {
                EditorUtility.SetDirty(udon);
                PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            }
            return eventBus;
        }

        private static VRC.Udon.UdonBehaviour[] BuildVideoTextureSubscribers(
            AunCastScreen[] meshScreens,
            AunCastUiScreen[] uiScreens)
        {
            var subscribers = new List<VRC.Udon.UdonBehaviour>();
            int meshCount = meshScreens != null ? meshScreens.Length : 0;
            for (int i = 0; i < meshCount; i++)
                AddBackingUdonBehaviour(subscribers, meshScreens[i]);

            int uiCount = uiScreens != null ? uiScreens.Length : 0;
            for (int i = 0; i < uiCount; i++)
                AddBackingUdonBehaviour(subscribers, uiScreens[i]);

            return subscribers.ToArray();
        }

        private static VRC.Udon.UdonBehaviour[] ToUdonSubscribers(WallControlPanel[] wallPanels)
        {
            var subscribers = new List<VRC.Udon.UdonBehaviour>();
            int count = wallPanels != null ? wallPanels.Length : 0;
            for (int i = 0; i < count; i++)
                AddBackingUdonBehaviour(subscribers, wallPanels[i]);
            return subscribers.ToArray();
        }

        private static void AddBackingUdonBehaviour(
            List<VRC.Udon.UdonBehaviour> subscribers,
            UdonSharp.UdonSharpBehaviour component)
        {
            if (subscribers == null || component == null) return;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(component);
            if (udon != null)
                subscribers.Add(udon);
        }

        private static T[] FindSceneComponents<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid()) return Array.Empty<T>();

            T[] all = UnityEngine.Object.FindObjectsOfType<T>(true);
            var list = new List<T>();
            for (int i = 0; i < all.Length; i++)
            {
                T component = all[i];
                if (component == null) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != scene) continue;
                list.Add(component);
            }

            list.Sort((a, b) => string.Compare(
                GetHierarchyPath(a != null ? a.transform : null),
                GetHierarchyPath(b != null ? b.transform : null),
                StringComparison.Ordinal));
            return list.ToArray();
        }

        private static int RewireDeclaredSpeakers(
            PasocomMate.AunCast.AunCastSettings settings,
            SpeakerSetupContext context,
            AunCastSpeaker[] speakers,
            bool recordUndo,
            bool writeLog)
        {
            var sourcesA = new List<AudioSource>();
            var sourcesB = new List<AudioSource>();
            AunCastSpeaker detectorA = null;
            AunCastSpeaker detectorB = null;
            // 同一系統の複数スピーカーは同じ波形（volume 比のみ差）を返すため、無音検知は 1 つで足りる。
            // volume が最大のものを基準にする（低音量ほど GetOutputData の SN 比が悪く正規化が不安定なため）。
            float bestVolumeA = -1f;
            float bestVolumeB = -1f;
            int updated = 0;

            for (int i = 0; i < speakers.Length; i++)
            {
                AunCastSpeaker speaker = speakers[i];
                if (speaker == null) continue;

                AudioSource source = speaker.GetComponent<AudioSource>();
                if (source == null)
                {
                    if (writeLog)
                        Debug.LogWarning($"[AunCast] AunCastSpeaker に AudioSource がありません: {GetHierarchyPath(speaker.transform)}", speaker);
                    continue;
                }

                updated += ConfigureAunCastSpeakerForSettings(speaker, settings, recordUndo) ? 1 : 0;

                int playerIndex = speaker.GetPlayerIndex();
                bool isPlayerB = playerIndex == AunCastSpeaker.PLAYER_B;
                VRCAVProVideoPlayer targetPlayer = isPlayerB ? context.playerB : context.playerA;
                Component avproSpeaker = EnsureSpeakerComponent(source.gameObject, recordUndo, writeLog);
                if (avproSpeaker != null)
                {
                    updated += TrySetSpeakerVideoPlayer(avproSpeaker, targetPlayer, recordUndo) ? 1 : 0;
                    updated += TrySetSpeakerMode(avproSpeaker, speaker.GetMode(), recordUndo) ? 1 : 0;
                }

                float speakerVolume = Mathf.Clamp01(speaker.baseVolume);
                if (isPlayerB)
                {
                    sourcesB.Add(source);
                    if (speakerVolume > bestVolumeB) { bestVolumeB = speakerVolume; detectorB = speaker; }
                }
                else
                {
                    sourcesA.Add(source);
                    if (speakerVolume > bestVolumeA) { bestVolumeA = speakerVolume; detectorA = speaker; }
                }
            }

            updated += ApplyAudioSourcesToManager(context.managerA, sourcesA.ToArray(), recordUndo) ? 1 : 0;
            updated += ApplyAudioSourcesToManager(context.managerB, sourcesB.ToArray(), recordUndo) ? 1 : 0;
            updated += ApplySilenceDetectorsToSwitcher(context.switcher, detectorA, detectorB, recordUndo) ? 1 : 0;
            return updated;
        }

        private static AudioSource FindFirstSpeakerSource(AunCastSpeaker[] speakers, int playerIndex)
        {
            if (speakers == null) return null;
            for (int i = 0; i < speakers.Length; i++)
            {
                AunCastSpeaker speaker = speakers[i];
                if (speaker == null || speaker.GetPlayerIndex() != playerIndex) continue;
                AudioSource source = speaker.GetComponent<AudioSource>();
                if (source != null)
                    return source;
            }
            return null;
        }

        private static int RewireAudioOutputTunnels(
            AunCastAudioOutputTunnel[] tunnels,
            AudioSource inputA,
            AudioSource inputB,
            bool recordUndo)
        {
            if (tunnels == null || tunnels.Length == 0) return 0;

            int updated = 0;
            for (int i = 0; i < tunnels.Length; i++)
            {
                AunCastAudioOutputTunnel tunnel = tunnels[i];
                if (tunnel == null) continue;

                var so = new SerializedObject(tunnel);
                bool changed = false;
                changed |= SetObjectProperty(so, "inputA", inputA);
                changed |= SetObjectProperty(so, "inputB", inputB);
                if (changed && ApplyUdonSerializedChanges(tunnel, so, "Rewire AunCastAudioOutputTunnel Inputs", recordUndo))
                    updated++;
            }

            return updated;
        }

        private void DrawAvProSpeakerSetupTools(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            _migrationSectionExpanded = SectionFoldout(
                _migrationSectionExpanded,
                "既存プレイヤー出力の変換",
                "Existing Player Output Migration");

            bool resolved = TryResolveSpeakerSetupContext(root, out var context, out var resolveError);

            if (_migrationSectionExpanded)
            {
                using (new EditorGUILayout.VerticalScope(PaddedHelpBoxStyle()))
                {
                    if (!resolved)
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.HelpBox(resolveError, MessageType.Warning);
                    }
                    else
                    {
                        MigrationCandidateCache cache = GetMigrationCache(root);
                        if (GUILayout.Button(
                            AunCastEditorLocalization.Localize("候補を再検出", "Redetect Candidates"),
                            GUILayout.Height(22)))
                        {
                            RefreshMigrationCandidates(root, context, cache);
                        }

                        EditorGUILayout.Space(4);
                        EditorGUILayout.HelpBox(
                            AunCastEditorLocalization.Localize(
                                "同一シーンの VRCAVProVideoScreen / VRCAVProVideoSpeaker を検出し、各行の変換ボタンで AunCastScreen / AunCastSpeaker に変換します。",
                                "Detects VRCAVProVideoScreen / VRCAVProVideoSpeaker components in the same scene and converts each row to AunCastScreen / AunCastSpeaker with its Convert button."),
                            MessageType.None);

                        if (!cache.detected)
                        {
                            EditorGUILayout.HelpBox(
                                AunCastEditorLocalization.Localize(
                                    "候補を表示するには「候補を再検出」を押してください。高負荷を避けるため、自動では実行しません。",
                                    "Click Redetect Candidates to show candidates. To avoid heavy processing, it does not run automatically."),
                                MessageType.Info);
                        }
                        else
                        {
                            MigrationCandidate[] candidates = cache.candidates ?? Array.Empty<MigrationCandidate>();
                            DrawMigrationCandidateList(root, settings, context, cache, candidates);

                            List<string> validationErrors = cache.validationErrors ?? new List<string>();
                            for (int i = 0; i < validationErrors.Count; i++)
                                EditorGUILayout.HelpBox(validationErrors[i], MessageType.Error);
                        }
                    }
                }
            }

            if (!resolved) return;
            DrawResidualCleanupGuidance(GetMigrationCache(root).residualCleanupCandidates);
        }

        /// <summary>
        /// 変換候補では扱わない旧プレイヤー本体を一覧提示する。
        /// AunCast は自動削除せず、ユーザーに手動削除を案内するにとどめる。
        /// </summary>
        private static ResidualCleanupCandidate[] CollectResidualCleanupCandidates(
            Transform root,
            SpeakerSetupContext context)
        {
            if (root == null) return Array.Empty<ResidualCleanupCandidate>();
            Scene scene = root.gameObject.scene;
            if (!scene.IsValid()) return Array.Empty<ResidualCleanupCandidate>();

            var residual = new List<ResidualCleanupCandidate>();

            // AunCast 内蔵の PlayerA/B を除いた VRCAVProVideoPlayer（旧プレイヤー本体の可能性）
            Component[] players = FindSceneComponentsByTypeName(scene, "VRCAVProVideoPlayer");
            for (int i = 0; i < players.Length; i++)
            {
                Component player = players[i];
                if (player == null) continue;
                if (context.playerA != null && player == (Component)context.playerA) continue;
                if (context.playerB != null && player == (Component)context.playerB) continue;
                residual.Add(new ResidualCleanupCandidate(player, "VRCAVProVideoPlayer"));
            }

            return residual.ToArray();
        }

        private void DrawResidualCleanupGuidance(ResidualCleanupCandidate[] residual)
        {
            if (residual == null || residual.Length == 0) return;

            EditorGUILayout.Space(8);
            _cleanupSectionExpanded = SectionFoldout(
                _cleanupSectionExpanded,
                "旧プレイヤー本体の手動削除候補",
                "Legacy Player Cleanup Candidates");
            if (!_cleanupSectionExpanded) return;

            using (new EditorGUILayout.VerticalScope(PaddedHelpBoxStyle()))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        "変換候補では扱わない旧 VRCAVProVideoPlayer 本体です。AunCast への移行後に不要であれば手動で削除してください。",
                        "Legacy VRCAVProVideoPlayer components that are not handled by conversion candidates. Delete them manually if they are no longer needed after migrating to AunCast."),
                    MessageType.Warning);
                for (int i = 0; i < residual.Length; i++)
                    DrawSelectableCleanupCandidate(residual[i]);
            }
        }

        private static void DrawSelectableCleanupCandidate(ResidualCleanupCandidate candidate)
        {
            var content = new GUIContent(
                "• " + candidate.label,
                AunCastEditorLocalization.Localize(
                    "クリックでこのオブジェクトを選択",
                    "Click to select this object"));

            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            using (new EditorGUI.DisabledScope(candidate.gameObject == null))
            {
                if (GUI.Button(rect, content, LinkPathStyle()))
                {
                    Selection.activeGameObject = candidate.gameObject;
                    EditorGUIUtility.PingObject(candidate.gameObject);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void DrawSelectableObjectPath(GameObject gameObject, string hierarchyPath)
        {
            var content = new GUIContent(
                hierarchyPath,
                AunCastEditorLocalization.Localize(
                    "クリックでこのオブジェクトを選択",
                    "Click to select this object"));

            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            using (new EditorGUI.DisabledScope(gameObject == null))
            {
                if (GUI.Button(rect, content, LinkPathStyle()))
                {
                    Selection.activeGameObject = gameObject;
                    EditorGUIUtility.PingObject(gameObject);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private MigrationCandidateCache GetMigrationCache(Transform root)
        {
            int rootId = root != null ? root.GetInstanceID() : 0;
            if (_migrationCache != null && _migrationCacheRootId == rootId)
                return _migrationCache;

            _migrationCacheRootId = rootId;
            if (!MigrationCaches.TryGetValue(rootId, out _migrationCache))
            {
                _migrationCache = new MigrationCandidateCache();
                MigrationCaches[rootId] = _migrationCache;
            }

            return _migrationCache;
        }

        private void RefreshMigrationCandidates(
            Transform root,
            SpeakerSetupContext context,
            MigrationCandidateCache cache)
        {
            if (cache == null) return;

            cache.candidates = CollectMigrationCandidates(root, context);
            cache.residualCleanupCandidates = CollectResidualCleanupCandidates(root, context);
            cache.validationErrors = ValidateCurrentSpeakerRouting(context);
            cache.detected = true;
            for (int i = 0; i < cache.candidates.Length; i++)
            {
                MigrationCandidate candidate = cache.candidates[i];
                if (string.IsNullOrEmpty(candidate.selectionKey)) continue;
                if (!cache.speakerPlayerIndex.ContainsKey(candidate.selectionKey))
                    cache.speakerPlayerIndex[candidate.selectionKey] = candidate.inferredPlayerIndex;
            }
        }

        private void DrawMigrationCandidateList(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            SpeakerSetupContext context,
            MigrationCandidateCache cache,
            MigrationCandidate[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        "変換対象の AVPro スクリーン / スピーカーは見つかりませんでした。",
                        "No AVPro screens / speakers were found for conversion."),
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("変換候補", "Conversion Candidates"),
                EditorStyles.miniBoldLabel);

            for (int i = 0; i < candidates.Length; i++)
            {
                MigrationCandidate candidate = candidates[i];

                using (new EditorGUILayout.VerticalScope(CandidateHelpBoxStyle()))
                {
                    string label = candidate.kind == MigrationCandidateKind.Screen
                        ? AunCastEditorLocalization.Localize("スクリーン", "Screen")
                        : AunCastEditorLocalization.Localize("スピーカー", "Speaker");

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
                            DrawSelectableObjectPath(candidate.gameObject, candidate.hierarchyPath);
                            if (IsBundledAunCastOutputCandidate(root, candidate.gameObject, candidate.hierarchyPath))
                            {
                                EditorGUILayout.HelpBox(
                                    AunCastEditorLocalization.Localize(
                                        "AunCast プレハブに元から含まれる出力です。既存ワールド側の出力を使う場合、この出力は不要なことがあります。不要であれば GameObject を手動で削除してください。",
                                        "This output is included in the AunCast prefab. If you use outputs from an existing world setup, this output may be unnecessary. Delete the GameObject manually if you do not need it."),
                                    MessageType.Warning);
                            }
                            bool showStatusInBody = !string.IsNullOrEmpty(candidate.statusLabel)
                                && !(candidate.kind == MigrationCandidateKind.Speaker && candidate.isConfigured);
                            if (showStatusInBody)
                                EditorGUILayout.LabelField(candidate.statusLabel, EditorStyles.miniLabel);

                            if (candidate.kind == MigrationCandidateKind.Speaker && !candidate.isConfigured)
                            {
                                int current = cache.speakerPlayerIndex.TryGetValue(candidate.selectionKey, out int value)
                                    ? value
                                    : candidate.inferredPlayerIndex;
                                int next = EditorGUILayout.Popup(
                                    AunCastEditorLocalization.Localize("接続先", "Player"),
                                    current,
                                    new[]
                                    {
                                        AunCastEditorLocalization.Localize("PlayerA", "Player A"),
                                        AunCastEditorLocalization.Localize("PlayerB", "Player B"),
                                        AunCastEditorLocalization.Localize("自動複製 (A/B)", "Auto-duplicate (A/B)"),
                                    });
                                cache.speakerPlayerIndex[candidate.selectionKey] = next;

                                if (next == MIGRATION_OPTION_AUTO_DUPLICATE)
                                {
                                    EditorGUILayout.HelpBox(
                                        AunCastEditorLocalization.Localize(
                                            "この AudioSource を同じ階層の直後に複製し、オリジナルを PlayerA、複製を PlayerB に割り当てます。",
                                            "Duplicates this AudioSource right after itself in the same hierarchy, assigning the original to Player A and the copy to Player B."),
                                        MessageType.None);
                                }

                                if (candidate.isTunnelLike)
                                {
                                    EditorGUILayout.HelpBox(
                                        AunCastEditorLocalization.Localize(
                                            "AudioOutputTunnel らしき構成を検出しました。ダミーシンクを通常スピーカーとして変換せず、AunCastAudioOutputTunnel へ差し替える前提で確認してください。直結出力よりリングバッファ分の遅延が増えます。",
                                            "An AudioOutputTunnel-like setup was detected. Do not convert the dummy sink as a normal speaker; review it as an AunCastAudioOutputTunnel migration. It adds ring-buffer latency compared with direct output."),
                                        MessageType.Warning);
                                }
                            }
                        }

                        if (candidate.sourceComponent != null)
                        {
                            using (new EditorGUI.DisabledScope(candidate.gameObject == null || candidate.sourceComponent == null))
                            {
                                string buttonLabel = candidate.isConfigured
                                    ? AunCastEditorLocalization.Localize("修正", "Fix")
                                    : AunCastEditorLocalization.Localize("変換", "Convert");
                                if (GUILayout.Button(
                                    buttonLabel,
                                    GUILayout.Width(72),
                                    GUILayout.Height(24)))
                                {
                                    ExecuteSingleMigration(root, settings, cache, candidate);
                                    RefreshMigrationCandidates(root, context, cache);
                                    Repaint();
                                    GUIUtility.ExitGUI();
                                }
                            }
                        }
                        else if (candidate.isConfigured)
                        {
                            using (new EditorGUILayout.VerticalScope(GUILayout.Width(112)))
                            {
                                GUILayout.Label(
                                    AunCastEditorLocalization.Localize("設定済み", "Configured"),
                                    EditorStyles.boldLabel,
                                    GUILayout.Height(20));

                                if (candidate.kind == MigrationCandidateKind.Speaker
                                    && !string.IsNullOrEmpty(candidate.statusLabel))
                                {
                                    GUILayout.Label(candidate.statusLabel, EditorStyles.miniLabel);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static MigrationCandidate[] CollectMigrationCandidates(Transform root, SpeakerSetupContext context)
        {
            var byKey = new Dictionary<string, MigrationCandidate>();
            if (root == null || !root.gameObject.scene.IsValid()) return Array.Empty<MigrationCandidate>();

            Scene scene = root.gameObject.scene;
            Component[] screens = FindSceneComponentsByTypeName(scene, SCREEN_COMPONENT_TYPE_NAME);
            for (int i = 0; i < screens.Length; i++)
            {
                Component screen = screens[i];
                if (screen == null) continue;
                if (IsInternalTextureGrabScreen(screen, context)) continue;

                AunCastScreen declaredScreen = screen.gameObject.GetComponent<AunCastScreen>();
                bool isConfigured = declaredScreen != null;
                string path = GetHierarchyPath(screen.transform);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.Screen,
                    screen.gameObject,
                    null,
                    screen,
                    path,
                    isConfigured
                        ? AunCastEditorLocalization.Localize(
                            "旧 VRCAVProVideoScreen が残っています。修正すると旧コンポーネントを削除します。",
                            "Legacy VRCAVProVideoScreen remains. Fix removes the old component.")
                        : string.Empty,
                    "screen:" + path,
                    AunCastSpeaker.PLAYER_A,
                    false,
                    isConfigured));
            }

            AunCastScreen[] declaredScreens = FindSceneComponents<AunCastScreen>(scene);
            for (int i = 0; i < declaredScreens.Length; i++)
            {
                AunCastScreen screen = declaredScreens[i];
                if (screen == null) continue;
                string path = GetHierarchyPath(screen.transform);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.Screen,
                    screen.gameObject,
                    null,
                    null,
                    path,
                    string.Empty,
                    "screen:" + path,
                    AunCastSpeaker.PLAYER_A,
                    false,
                    true));
            }

            Component[] speakers = FindSceneComponentsByTypeName(scene, SPEAKER_COMPONENT_TYPE_NAME);
            for (int i = 0; i < speakers.Length; i++)
            {
                Component speaker = speakers[i];
                if (speaker == null) continue;
                AudioSource source = speaker.GetComponent<AudioSource>();
                if (source == null) continue;
                // AudioLink 付属の内蔵スピーカー（AudioLinkInput）は変換対象ではない。
                // AunCast がランタイムで AudioLink 入力を Active スピーカーへ差し替えるため、
                // 再配線が無効化＋EditorOnly 化して扱う（NeutralizeAudioLinkInputs 参照）。
                if (IsAudioLinkOwnedSource(speaker)) continue;

                AunCastSpeaker declaredSpeaker = source.gameObject.GetComponent<AunCastSpeaker>();
                bool isConfigured = declaredSpeaker != null;
                string path = GetHierarchyPath(source.transform);
                bool isTunnelLike = IsAudioOutputTunnelLike(source);
                string status = isConfigured
                    ? BuildConfiguredSpeakerStatusLabel(source, declaredSpeaker, isTunnelLike)
                    : BuildSpeakerStatusLabel(source, isTunnelLike);
                int inferredPlayerIndex = isConfigured
                    ? GetAunCastSpeakerPlayerIndex(declaredSpeaker)
                    : InferSpeakerPlayerIndex(speaker, context);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.Speaker,
                    source.gameObject,
                    source,
                    isConfigured ? null : speaker,
                    path,
                    status,
                    "speaker:" + path,
                    inferredPlayerIndex,
                    isTunnelLike,
                    isConfigured));
            }

            AunCastSpeaker[] declaredSpeakers = FindSceneComponents<AunCastSpeaker>(scene);
            for (int i = 0; i < declaredSpeakers.Length; i++)
            {
                AunCastSpeaker speaker = declaredSpeakers[i];
                if (speaker == null) continue;
                AudioSource source = speaker.GetComponent<AudioSource>();
                if (source == null) continue;

                string path = GetHierarchyPath(source.transform);
                bool isTunnelLike = IsAudioOutputTunnelLike(source);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.Speaker,
                    source.gameObject,
                    source,
                    null,
                    path,
                    BuildConfiguredSpeakerStatusLabel(source, speaker, isTunnelLike),
                    "speaker:" + path,
                    GetAunCastSpeakerPlayerIndex(speaker),
                    isTunnelLike,
                    true));
            }

            var list = new List<MigrationCandidate>(byKey.Values);
            list.Sort(CompareMigrationCandidates);
            return list.ToArray();
        }

        private static int CompareMigrationCandidates(MigrationCandidate a, MigrationCandidate b)
        {
            int order = string.Compare(
                GetHierarchyOrderKey(a.gameObject),
                GetHierarchyOrderKey(b.gameObject),
                StringComparison.Ordinal);
            if (order != 0) return order;

            order = string.Compare(a.hierarchyPath, b.hierarchyPath, StringComparison.Ordinal);
            if (order != 0) return order;

            return a.kind.CompareTo(b.kind);
        }

        private static string GetHierarchyOrderKey(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;

            var indices = new List<int>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            indices.Reverse();
            string[] parts = new string[indices.Count];
            for (int i = 0; i < indices.Count; i++)
                parts[i] = indices[i].ToString("D6");
            return string.Join("/", parts);
        }

        private static bool IsBundledAunCastOutputCandidate(
            Transform root,
            GameObject gameObject,
            string hierarchyPath)
        {
            if (root == null || gameObject == null) return false;
            if (!gameObject.transform.IsChildOf(root)) return false;

            if (!string.IsNullOrEmpty(hierarchyPath)
                && (hierarchyPath.Contains("/ExampleOutput/")
                    || hierarchyPath.Contains("/PlayerA/")
                    || hierarchyPath.Contains("/PlayerB/")))
                return true;

            return PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) == root.gameObject
                   && PrefabUtility.GetCorrespondingObjectFromSource(gameObject) != null;
        }

        private static void AddMigrationCandidate(Dictionary<string, MigrationCandidate> byKey, MigrationCandidate candidate)
        {
            if (byKey == null || string.IsNullOrEmpty(candidate.selectionKey)) return;
            if (byKey.TryGetValue(candidate.selectionKey, out var existing))
            {
                if (existing.sourceComponent != null && candidate.sourceComponent == null)
                    return;
                if (existing.isConfigured && !candidate.isConfigured)
                    return;
            }

            byKey[candidate.selectionKey] = candidate;
        }

        private static string BuildConfiguredSpeakerStatusLabel(AudioSource source, AunCastSpeaker speaker, bool isTunnelLike)
        {
            string player = GetSpeakerPlayerLabel(GetAunCastSpeakerPlayerIndex(speaker));
            string status = BuildSpeakerStatusLabel(source, isTunnelLike);
            string normal = AunCastEditorLocalization.Localize("通常出力候補", "normal output candidate");
            if (string.IsNullOrEmpty(status) || status == normal)
                return player;

            return player + " / " + status;
        }

        private static int GetAunCastSpeakerPlayerIndex(AunCastSpeaker speaker)
        {
            return speaker != null && speaker.playerIndex == AunCastSpeaker.PLAYER_B
                ? AunCastSpeaker.PLAYER_B
                : AunCastSpeaker.PLAYER_A;
        }

        private static string GetSpeakerPlayerLabel(int playerIndex)
        {
            return playerIndex == AunCastSpeaker.PLAYER_B
                ? AunCastEditorLocalization.Localize("PlayerB", "Player B")
                : AunCastEditorLocalization.Localize("PlayerA", "Player A");
        }

        private void ExecuteSingleMigration(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            MigrationCandidateCache cache,
            MigrationCandidate candidate)
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("AunCast Output Migration");
            int convertedScreens = 0;
            int convertedSpeakers = 0;
            try
            {
                if (candidate.kind == MigrationCandidateKind.Screen)
                {
                    if (ConvertScreenCandidate(candidate))
                        convertedScreens++;
                }
                else if (candidate.kind == MigrationCandidateKind.Speaker)
                {
                    int selection = cache != null && cache.speakerPlayerIndex.TryGetValue(candidate.selectionKey, out int selected)
                        ? selected
                        : candidate.inferredPlayerIndex;
                    if (selection == MIGRATION_OPTION_AUTO_DUPLICATE)
                        convertedSpeakers += ConvertSpeakerCandidateWithDuplicate(candidate, settings);
                    else if (ConvertSpeakerCandidate(candidate, settings, selection))
                        convertedSpeakers++;
                }

                RewireEventHubAndConsumers(root, recordUndo: true, writeLog: false);
                Debug.Log($"[AunCast] 出力変換を完了しました。Screen: {convertedScreens}件 / Speaker: {convertedSpeakers}件");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private static bool ConvertScreenCandidate(MigrationCandidate candidate)
        {
            if (candidate.gameObject == null || candidate.sourceComponent == null) return false;

            string textureProperty = ReadStringProperty(candidate.sourceComponent, "textureProperty");
            if (string.IsNullOrEmpty(textureProperty))
                textureProperty = GuessTextureProperty(candidate.gameObject);

            AunCastScreen screen = candidate.gameObject.GetComponent<AunCastScreen>();
            if (screen == null)
                screen = Undo.AddComponent<AunCastScreen>(candidate.gameObject);
            if (screen == null) return false;

            var so = new SerializedObject(screen);
            SetStringProperty(so, "textureProperty", textureProperty);
            ApplyUdonSerializedChanges(screen, so, "Configure AunCastScreen");

            int removedLegacyScreens = DestroyComponentsByTypeName(candidate.gameObject, SCREEN_COMPONENT_TYPE_NAME);
            if (removedLegacyScreens == 0)
                Debug.LogWarning("[AunCast] VRCAVProVideoScreen の削除対象が見つかりませんでした。");
            return removedLegacyScreens > 0;
        }

        private static int DestroyComponentsByTypeName(GameObject gameObject, string typeName)
        {
            if (gameObject == null || string.IsNullOrEmpty(typeName)) return 0;

            Component[] components = gameObject.GetComponents<Component>();
            int removed = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                if (component.GetType().Name != typeName) continue;

                Undo.DestroyObjectImmediate(component);
                removed++;
            }

            if (removed > 0)
            {
                EditorUtility.SetDirty(gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
            }

            return removed;
        }

        private static bool ConvertSpeakerCandidate(
            MigrationCandidate candidate,
            PasocomMate.AunCast.AunCastSettings settings,
            int playerIndex)
        {
            return ConvertSpeakerObject(candidate.gameObject, candidate.sourceComponent, candidate.audioSource, settings, playerIndex);
        }

        /// <summary>
        /// 候補 AudioSource を同じ階層の直後に複製し、オリジナルを PlayerA・複製を PlayerB に割り当てる。
        /// 同じ位置に同じ設定の AudioSource を A/B 用に用意したいケース向け。変換できた件数（0〜2）を返す。
        /// </summary>
        private static int ConvertSpeakerCandidateWithDuplicate(
            MigrationCandidate candidate,
            PasocomMate.AunCast.AunCastSettings settings)
        {
            GameObject original = candidate.gameObject;
            if (original == null || candidate.audioSource == null) return 0;

            GameObject duplicate = UnityEngine.Object.Instantiate(original, original.transform.parent);
            Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate AunCast Speaker");
            duplicate.name = original.name + " (B)";
            duplicate.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(duplicate);

            int converted = 0;
            if (ConvertSpeakerObject(original, candidate.sourceComponent, candidate.audioSource, settings, AunCastSpeaker.PLAYER_A))
                converted++;

            AudioSource duplicateAudio = duplicate.GetComponent<AudioSource>();
            Component duplicateSpeakerComponent = FindSpeakerComponent(duplicate);
            if (ConvertSpeakerObject(duplicate, duplicateSpeakerComponent, duplicateAudio, settings, AunCastSpeaker.PLAYER_B))
                converted++;

            return converted;
        }

        private static bool ConvertSpeakerObject(
            GameObject gameObject,
            Component sourceSpeakerComponent,
            AudioSource audioSource,
            PasocomMate.AunCast.AunCastSettings settings,
            int playerIndex)
        {
            if (gameObject == null || audioSource == null) return false;

            AunCastSpeaker speaker = EnsureAunCastSpeaker(gameObject, settings);
            if (speaker == null) return false;

            var so = new SerializedObject(speaker);
            SetIntProperty(so, "playerIndex", playerIndex == AunCastSpeaker.PLAYER_B ? AunCastSpeaker.PLAYER_B : AunCastSpeaker.PLAYER_A);
            SetIntProperty(so, "mode", ReadIntProperty(sourceSpeakerComponent, "mode", AunCastSpeaker.MODE_STEREO));
            SetFloatProperty(so, "baseVolume", Mathf.Clamp01(audioSource.volume));
            SetFloatProperty(so, "silenceRmsThresholdDbfs", settings != null ? settings.silenceRmsThresholdDbfs : -60f);
            SetFloatProperty(so, "silenceConsecutiveSec", settings != null ? settings.silenceConsecutiveSec : 2.0f);
            ApplyUdonSerializedChanges(speaker, so, "Configure AunCastSpeaker");
            return true;
        }

        private static Component[] FindSceneComponentsByTypeName(Scene scene, string typeName)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(typeName)) return Array.Empty<Component>();

            Component[] all = UnityEngine.Object.FindObjectsOfType<Component>(true);
            var list = new List<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                Component component = all[i];
                if (component == null) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != scene) continue;
                if (component.GetType().Name == typeName)
                    list.Add(component);
            }

            return list.ToArray();
        }

        private static bool IsInternalTextureGrabScreen(Component screen, SpeakerSetupContext context)
        {
            if (screen == null) return false;
            Renderer renderer = screen.GetComponent<Renderer>();
            if (renderer == null) return false;
            return IsManagerTextureRenderer(context.managerA, renderer)
                   || IsManagerTextureRenderer(context.managerB, renderer);
        }

        private static bool IsManagerTextureRenderer(VideoPlayerManager manager, Renderer renderer)
        {
            return manager != null
                   && renderer != null
                   && manager.avProTextureRenderer == renderer;
        }

        private static string ReadStringProperty(Component component, string propertyName)
        {
            if (component == null) return string.Empty;
            var so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(propertyName);
            return prop != null && prop.propertyType == SerializedPropertyType.String
                ? prop.stringValue
                : string.Empty;
        }

        private static int ReadIntProperty(Component component, string propertyName, int fallback)
        {
            if (component == null) return fallback;
            var so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null) return fallback;
            if (prop.propertyType == SerializedPropertyType.Integer) return prop.intValue;
            if (prop.propertyType == SerializedPropertyType.Enum) return prop.enumValueIndex;
            return fallback;
        }

        private static string GuessTextureProperty(GameObject gameObject)
        {
            if (gameObject == null) return "_MainTex";
            Renderer renderer = gameObject.GetComponent<Renderer>();
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null) return "_MainTex";
            if (material.HasProperty("_MainTex")) return "_MainTex";
            if (material.HasProperty("_EmissionMap")) return "_EmissionMap";
            if (material.HasProperty("_BaseMap")) return "_BaseMap";
            if (material.HasProperty("_BaseColorMap")) return "_BaseColorMap";
            return "_MainTex";
        }

        private static int InferSpeakerPlayerIndex(Component speaker, SpeakerSetupContext context)
        {
            if (speaker == null) return AunCastSpeaker.PLAYER_A;
            var so = new SerializedObject(speaker);
            SerializedProperty prop = so.FindProperty("videoPlayer");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return AunCastSpeaker.PLAYER_A;
            if (prop.objectReferenceValue == context.playerB)
                return AunCastSpeaker.PLAYER_B;
            return AunCastSpeaker.PLAYER_A;
        }

        private static string BuildSpeakerStatusLabel(AudioSource source, bool isTunnelLike)
        {
            if (source == null) return string.Empty;
            var labels = new List<string>();
            if (!source.gameObject.activeInHierarchy)
                labels.Add(AunCastEditorLocalization.Localize("非アクティブ", "Inactive"));
            if (!source.enabled)
                labels.Add(AunCastEditorLocalization.Localize("AudioSource無効", "AudioSource disabled"));
            if (source.volume <= 0.0001f)
                labels.Add(AunCastEditorLocalization.Localize("volume 0", "volume 0"));
            if (IsCustomRolloffSilent(source))
                labels.Add(AunCastEditorLocalization.Localize("不可聴ロールオフ", "silent rolloff"));
            if (isTunnelLike)
                labels.Add(AunCastEditorLocalization.Localize("AudioOutputTunnel検知", "AudioOutputTunnel detected"));

            string references = BuildAudioSourceReferenceLabel(source);
            if (!string.IsNullOrEmpty(references))
                labels.Add(references);

            return labels.Count == 0
                ? AunCastEditorLocalization.Localize("通常出力候補", "normal output candidate")
                : string.Join(" / ", labels.ToArray());
        }

        private static bool IsCustomRolloffSilent(AudioSource source)
        {
            if (source == null) return false;
            AnimationCurve curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            if (curve == null || curve.length == 0) return false;
            for (int i = 0; i < curve.length; i++)
            {
                if (curve.keys[i].value > 0.0001f)
                    return false;
            }
            return true;
        }

        private static bool IsAudioOutputTunnelLike(AudioSource source)
        {
            if (source == null) return false;
            Transform current = source.transform;
            for (int depth = 0; current != null && depth < 3; depth++)
            {
                if (HasAudioOutputTunnelInChildren(current))
                    return true;
                current = current.parent;
            }

            return false;
        }

        private static bool HasAudioOutputTunnelInChildren(Transform root)
        {
            if (root == null) return false;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                if (IsAudioOutputTunnelComponent(component))
                    return true;
            }
            return false;
        }

        private static bool IsAudioOutputTunnelComponent(Component component)
        {
            if (component == null || component.GetType().Name != "AudioOutputTunnel") return false;

            var so = new SerializedObject(component);
            bool hasInput = so.FindProperty("input") != null;
            bool hasOutput =
                so.FindProperty("leftOutput") != null
                || so.FindProperty("rightOutput") != null
                || so.FindProperty("stereoOutput") != null;
            return hasInput || hasOutput;
        }

        private static string BuildAudioSourceReferenceLabel(AudioSource source)
        {
            if (source == null || !source.gameObject.scene.IsValid()) return string.Empty;

            int references = 0;
            Component[] components = UnityEngine.Object.FindObjectsOfType<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component == source) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != source.gameObject.scene) continue;
                if (!SerializedObjectReferences(component, source)) continue;
                references++;
            }

            if (references == 0) return string.Empty;
            return AunCastEditorLocalization.Localize(
                $"この AudioSource を参照するコンポーネント: {references} 件（A/B に分かれると単一入力の参照元は要対応）",
                $"components referencing this AudioSource: {references} (single-input referrers need attention once split into A/B)");
        }

        private static bool SerializedObjectReferences(UnityEngine.Object owner, UnityEngine.Object target)
        {
            if (owner == null || target == null) return false;
            try
            {
                var so = new SerializedObject(owner);
                SerializedProperty prop = so.GetIterator();
                bool enterChildren = true;
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.propertyType == SerializedPropertyType.ObjectReference
                        && prop.objectReferenceValue == target)
                        return true;
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private static bool TryResolveSpeakerSetupContext(
            Transform root,
            out SpeakerSetupContext context,
            out string error)
        {
            context = default;
            error = string.Empty;
            if (root == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "AunCast ルートが見つかりません。", "AunCast root was not found.");
                return false;
            }

            VideoPlayerManager managerA = null;
            VideoPlayerManager managerB = null;
            VideoPlayerManager[] managers = root.GetComponentsInChildren<VideoPlayerManager>(true);
            for (int i = 0; i < managers.Length; i++)
            {
                VideoPlayerManager manager = managers[i];
                if (manager == null) continue;
                if (manager.playerIndex == 0 && managerA == null)
                    managerA = manager;
                else if (manager.playerIndex == 1 && managerB == null)
                    managerB = manager;
            }

            if (managerA == null || managerB == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "VideoPlayerManager A/B が見つかりません。", "VideoPlayerManager A/B was not found.");
                return false;
            }
            if (managerA.avProPlayer == null || managerB.avProPlayer == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "VideoPlayerManager の avProPlayer 参照が不足しています。",
                    "The avProPlayer reference on VideoPlayerManager is missing.");
                return false;
            }

            var switcher = root.GetComponentInChildren<PlaybackSwitcher>(true);
            if (switcher == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "PlaybackSwitcher が見つかりません。", "PlaybackSwitcher was not found.");
                return false;
            }

            context = new SpeakerSetupContext(
                managerA,
                managerB,
                managerA.avProPlayer,
                managerB.avProPlayer,
                switcher);
            return true;
        }

        private static List<string> ValidateCurrentSpeakerRouting(SpeakerSetupContext context)
        {
            var errors = new List<string>();
            AudioSource[] aSources = context.managerA != null ? context.managerA.audioSources : null;
            AudioSource[] bSources = context.managerB != null ? context.managerB.audioSources : null;

            var used = new HashSet<AudioSource>();
            if (aSources != null)
            {
                for (int i = 0; i < aSources.Length; i++)
                {
                    AudioSource source = aSources[i];
                    if (source == null)
                    {
                        errors.Add(AunCastEditorLocalization.Localize(
                            $"PlayerA audioSources[{i}] が null です。",
                            $"PlayerA audioSources[{i}] is null."));
                        continue;
                    }
                    used.Add(source);
                    ValidateSpeakerBinding(errors, source, context.playerA, $"PlayerA audioSources[{i}]");
                }
            }

            if (bSources != null)
            {
                for (int i = 0; i < bSources.Length; i++)
                {
                    AudioSource source = bSources[i];
                    if (source == null)
                    {
                        errors.Add(AunCastEditorLocalization.Localize(
                            $"PlayerB audioSources[{i}] が null です。",
                            $"PlayerB audioSources[{i}] is null."));
                        continue;
                    }
                    if (used.Contains(source))
                        errors.Add(AunCastEditorLocalization.Localize(
                            $"PlayerA/PlayerB で同一 AudioSource を共有しています: {GetHierarchyPath(source.transform)}",
                            $"Player A/B share the same AudioSource: {GetHierarchyPath(source.transform)}"));
                    ValidateSpeakerBinding(errors, source, context.playerB, $"PlayerB audioSources[{i}]");
                }
            }

            return errors;
        }

        private static void ValidateSpeakerBinding(
            List<string> errors,
            AudioSource source,
            VRCAVProVideoPlayer expectedPlayer,
            string label)
        {
            if (source == null) return;
            if (expectedPlayer == null)
            {
                errors.Add(AunCastEditorLocalization.Localize(
                    $"{label}: 期待する VRCAVProVideoPlayer が null です。",
                    $"{label}: The expected VRCAVProVideoPlayer is null."));
                return;
            }

            Component speaker = FindSpeakerComponent(source.gameObject);
            if (speaker == null)
            {
                errors.Add(AunCastEditorLocalization.Localize(
                    $"{label}: VRC AVPro Video Speaker がありません。({GetHierarchyPath(source.transform)})",
                    $"{label}: No VRC AVPro Video Speaker found. ({GetHierarchyPath(source.transform)})"));
                return;
            }

            if (!IsSpeakerRoutedTo(speaker, expectedPlayer))
            {
                errors.Add(AunCastEditorLocalization.Localize(
                    $"{label}: Speaker の videoPlayer が想定先を向いていません。({GetHierarchyPath(source.transform)})",
                    $"{label}: The Speaker's videoPlayer does not point to the expected target. ({GetHierarchyPath(source.transform)})"));
            }
        }

        private static bool ApplyAudioSourcesToManager(VideoPlayerManager manager, AudioSource[] sources, bool recordUndo = true)
        {
            if (manager == null) return false;
            var so = new SerializedObject(manager);
            if (SetObjectArrayProperty(so, nameof(VideoPlayerManager.audioSources), sources))
                return ApplyUdonSerializedChanges(manager, so, "Apply AudioSources to VideoPlayerManager", recordUndo);
            return false;
        }

        private static bool ApplySilenceDetectorsToSwitcher(
            PlaybackSwitcher switcher,
            AunCastSpeaker detectorA,
            AunCastSpeaker detectorB,
            bool recordUndo = true)
        {
            if (switcher == null) return false;

            var so = new SerializedObject(switcher);
            bool changed = false;
            changed |= SetObjectProperty(so, "silenceDetectorA", detectorA);
            changed |= SetObjectProperty(so, "silenceDetectorB", detectorB);
            if (changed)
                return ApplyUdonSerializedChanges(switcher, so, "Apply Silence Detectors to PlaybackSwitcher", recordUndo);
            return false;
        }

        private static void ApplyUdonSerializedChanges(
            UdonSharp.UdonSharpBehaviour component,
            SerializedObject so,
            string undoName)
        {
            ApplyUdonSerializedChanges(component, so, undoName, recordUndo: true);
        }

        private static bool ApplyUdonSerializedChanges(
            UdonSharp.UdonSharpBehaviour component,
            SerializedObject so,
            string undoName,
            bool recordUndo)
        {
            if (component == null || so == null) return false;

            if (recordUndo)
                Undo.RecordObject(component, undoName);
            bool applied = recordUndo
                ? so.ApplyModifiedProperties()
                : so.ApplyModifiedPropertiesWithoutUndo();
            if (!applied) return false;

            UdonSharpEditorUtility.CopyProxyToUdon(component);
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(component);
            if (udon != null)
            {
                EditorUtility.SetDirty(udon);
                PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            }
            return true;
        }

        private static AunCastSpeaker EnsureAunCastSpeaker(
            GameObject gameObject,
            PasocomMate.AunCast.AunCastSettings settings,
            bool recordUndo = true)
        {
            if (gameObject == null) return null;

            var detector = gameObject.GetComponent<AunCastSpeaker>();
            if (detector == null)
                detector = recordUndo ? Undo.AddComponent<AunCastSpeaker>(gameObject) : gameObject.AddComponent<AunCastSpeaker>();
            if (detector == null) return null;

            var so = new SerializedObject(detector);
            SetFloatProperty(so, "silenceRmsThresholdDbfs", settings != null ? settings.silenceRmsThresholdDbfs : -60f);
            SetFloatProperty(so, "silenceConsecutiveSec", settings != null ? settings.silenceConsecutiveSec : 2.0f);
            ApplyUdonSerializedChanges(detector, so, "Configure AunCastSpeaker", recordUndo);
            return detector;
        }

        private static bool ConfigureAunCastSpeakerForSettings(
            AunCastSpeaker speaker,
            PasocomMate.AunCast.AunCastSettings settings,
            bool recordUndo)
        {
            if (speaker == null || settings == null) return false;
            var so = new SerializedObject(speaker);
            bool changed = false;
            changed |= SetFloatPropertyIfChanged(so, "silenceRmsThresholdDbfs", settings.silenceRmsThresholdDbfs);
            changed |= SetFloatPropertyIfChanged(so, "silenceConsecutiveSec", settings.silenceConsecutiveSec);
            return changed && ApplyUdonSerializedChanges(speaker, so, "Configure AunCastSpeaker", recordUndo);
        }

        private static Component EnsureSpeakerComponent(GameObject gameObject, bool recordUndo, bool writeLog)
        {
            if (gameObject == null) return null;

            Component speaker = FindSpeakerComponent(gameObject);
            if (speaker != null) return speaker;

            Type speakerType = FindComponentTypeByName(SPEAKER_COMPONENT_TYPE_NAME);
            if (speakerType == null || !typeof(Component).IsAssignableFrom(speakerType))
            {
                if (writeLog)
                    Debug.LogWarning("[AunCast] VRCAVProVideoSpeaker 型が見つからないため、AudioSource への Speaker 追加をスキップしました。");
                return null;
            }

            Component added = recordUndo
                ? Undo.AddComponent(gameObject, speakerType)
                : gameObject.AddComponent(speakerType);
            if (added == null) return null;

            EditorUtility.SetDirty(added);
            PrefabUtility.RecordPrefabInstancePropertyModifications(added);
            return added;
        }

        /// <summary>
        /// トンネル入力になった内蔵シンクを不可聴化する。volume は GetOutputData に影響する（＝トンネルが読む
        /// 信号を消してしまう）ため触らず、空間ブレンドを 3D にしてカスタムロールオフを全域 0 にすることで
        /// 直接音のみを消す。既に不可聴なら何もしない（冪等）。
        /// </summary>
        private static bool MakeTunnelInputInaudible(AudioSource source, bool recordUndo)
        {
            if (source == null) return false;

            bool alreadyInaudible = source.spatialBlend >= 0.999f
                && source.rolloffMode == AudioRolloffMode.Custom
                && IsCustomRolloffSilent(source);
            if (alreadyInaudible) return false;

            if (recordUndo)
                Undo.RecordObject(source, "Mute AunCast Tunnel Input Sink");

            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 0f));

            EditorUtility.SetDirty(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            return true;
        }

        private static Type FindComponentTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch
                {
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];
                    if (type == null) continue;
                    if (type.Name == typeName)
                        return type;
                }
            }

            return null;
        }

        private static bool TrySetSpeakerVideoPlayer(Component speaker, VRCAVProVideoPlayer player, bool recordUndo = true)
        {
            if (speaker == null || player == null) return false;
            var so = new SerializedObject(speaker);
            SerializedProperty prop = so.FindProperty("videoPlayer");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            if (prop.objectReferenceValue == player)
                return false;

            if (recordUndo)
                Undo.RecordObject(speaker, "Rewire AVPro Speaker videoPlayer");
            prop.objectReferenceValue = player;
            bool applied = recordUndo ? so.ApplyModifiedProperties() : so.ApplyModifiedPropertiesWithoutUndo();
            if (!applied) return false;

            EditorUtility.SetDirty(speaker);
            PrefabUtility.RecordPrefabInstancePropertyModifications(speaker);
            return true;
        }

        private static bool TrySetSpeakerMode(Component speaker, int mode, bool recordUndo)
        {
            if (speaker == null) return false;
            var so = new SerializedObject(speaker);
            SerializedProperty prop = so.FindProperty("mode");
            if (prop == null) return false;

            int clamped = Mathf.Clamp(mode, AunCastSpeaker.MODE_STEREO, AunCastSpeaker.MODE_RIGHT);
            bool changed = false;
            if (prop.propertyType == SerializedPropertyType.Enum)
            {
                if (prop.enumValueIndex != clamped)
                {
                    prop.enumValueIndex = clamped;
                    changed = true;
                }
            }
            else if (prop.propertyType == SerializedPropertyType.Integer)
            {
                if (prop.intValue != clamped)
                {
                    prop.intValue = clamped;
                    changed = true;
                }
            }
            if (!changed) return false;

            if (recordUndo)
                Undo.RecordObject(speaker, "Rewire AVPro Speaker mode");
            bool applied = recordUndo ? so.ApplyModifiedProperties() : so.ApplyModifiedPropertiesWithoutUndo();
            if (!applied) return false;

            EditorUtility.SetDirty(speaker);
            PrefabUtility.RecordPrefabInstancePropertyModifications(speaker);
            return true;
        }

        private static bool IsSpeakerRoutedTo(Component speaker, VRCAVProVideoPlayer expected)
        {
            if (speaker == null || expected == null) return false;
            var so = new SerializedObject(speaker);
            SerializedProperty prop = so.FindProperty("videoPlayer");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            return prop.objectReferenceValue == expected;
        }

        private static Component FindSpeakerComponent(GameObject gameObject)
        {
            if (gameObject == null) return null;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                if (component.GetType().Name == SPEAKER_COMPONENT_TYPE_NAME)
                    return component;
            }

            return null;
        }

        private static bool IsUnderTransform(Transform target, Transform root)
        {
            if (target == null || root == null) return false;
            Transform current = target;
            while (current != null)
            {
                if (current == root) return true;
                current = current.parent;
            }

            return false;
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null) return "<null>";
            var stack = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack.ToArray());
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
            return DEFAULT_VPM_LISTING_URL;
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
            return AunCastInspectorBanner.GetCurrentPackageName(this);
        }

        private string GetCurrentPackageVersion()
        {
            return AunCastInspectorBanner.GetCurrentPackageVersion(this);
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
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("映像プレイヤー", "Video Player"),
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            int newResolution = EditorGUILayout.IntPopup(
                L("最大解像度", "Maximum Resolution", "maximumResolution",
                    "AVProプレイヤーの最大解像度。", "Maximum resolution for the AVPro player."),
                settings.maximumResolution,
                new[] {
                    new GUIContent("360p"), new GUIContent("480p"), new GUIContent("720p"),
                    new GUIContent("1080p"), new GUIContent("1440p"), new GUIContent("2160p"),
                },
                new[] { 360, 480, 720, 1080, 1440, 2160 });
            CopyFieldNameMenu("maximumResolution");

            bool newLowLatency = ToggleField("低遅延モード", "Low Latency Mode", "useLowLatency",
                "AVProの低遅延モードを有効にする。", "Enables AVPro's low-latency mode.", settings.useLowLatency);

            float newCrossfade = SliderField("クロスフェード時間 [秒]", "Crossfade Duration [s]", "crossfadeDurationSec",
                "Active/Standby切替時のクロスフェード時間（秒）。", "Crossfade duration (seconds) when switching Active/Standby.",
                settings.crossfadeDurationSec, 0f, 1f);
            var newIdleScreenTexture = (Texture2D)EditorGUILayout.ObjectField(
                L("停止中のスクリーン画像", "Idle Screen Image", "idleScreenTexture",
                    "再生停止中にスクリーンへ表示する固定画像。未指定なら初期割り当てのテクスチャへ復元する。",
                    "Static image shown on the screen while playback is stopped. If unset, the originally assigned texture is restored."),
                settings.idleScreenTexture, typeof(Texture2D), false);
            CopyFieldNameMenu("idleScreenTexture");
            bool idleScreenTextureChanged = settings.idleScreenTexture != newIdleScreenTexture;

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(settings, "Change AunCast Video Player Settings");
            settings.maximumResolution = newResolution;
            settings.useLowLatency = newLowLatency;
            settings.crossfadeDurationSec = newCrossfade;
            settings.idleScreenTexture = newIdleScreenTexture;
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
            if (idleScreenTextureChanged)
                RewireEventHubAndConsumers(root, recordUndo: true, writeLog: false);
        }

        // ── UI/操作 ──

        private void DrawStaffNamesField(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUI.BeginChangeCheck();
            var sp = serializedObject.FindProperty("staffAllowedUserNames");
            if (sp != null)
                EditorGUILayout.PropertyField(sp,
                    L("スタッフ許可ユーザー名", "Staff Allowed User Names", "staffAllowedUserNames",
                        "パスコードなしでスタッフ権限を付与する VRChat ユーザー名リスト。",
                        "List of VRChat user names granted staff permission without a passcode."),
                    true);
            if (!EditorGUI.EndChangeCheck()) return;
            serializedObject.ApplyModifiedProperties();
            ApplyStaffNamesToScene(root, settings);
        }

        private static void ApplyStaffNamesToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var staffPanels = root.GetComponentsInChildren<StaffControlPanel>(true);
            ApplyToUdonComponents(staffPanels, so =>
                SetStringArrayProperty(so, "allowedUserNames", settings.staffAllowedUserNames));
        }

        private void DrawUiSettings(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("UI/操作", "UI / Controls"),
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float newDefaultVolume = SliderField("初期音量", "Default Volume", "defaultVolume",
                "各ユーザーの起動時ローカル再生デフォルト音量（0〜1）。",
                "Default local playback volume (0-1) for each user at startup.",
                settings.defaultVolume, 0f, 1f);
            string newDefaultUrl = TextField("デフォルト配信URL", "Default Stream URL", "defaultUrl",
                "Next URL欄の初期値。インスタンス最初のJoin時の自動再生にも使用する。空欄で無効。",
                "Initial value of the Next URL field. Also used for auto-play on the first join to the instance. Empty to disable.",
                settings.defaultUrl != null ? settings.defaultUrl.Get() : "");
            bool newAutoPlayDefault = ToggleField("最初のJoinで自動再生", "Auto-play on First Join", "autoPlayDefaultOnFirstJoin",
                "インスタンスに最初のユーザーがJoinした時点で、デフォルト配信URLを自動再生する。",
                "Auto-plays the default stream URL when the first user joins the instance.",
                settings.autoPlayDefaultOnFirstJoin);

            EditorGUILayout.LabelField(L("VR呼び出しジェスチャー初期値", "Default VR Summon Gesture", "defaultSummonGesture",
                "VRモードでHUDを呼び出すジェスチャーの初期有効設定。",
                "Default enabled gestures for summoning the HUD in VR mode."));
            CopyFieldNameMenu("defaultSummonGesture");
            int newSummonGesture = settings.defaultSummonGesture;
            EditorGUI.indentLevel++;
            newSummonGesture = ToggleBitFlag(newSummonGesture, 2, L("右スティック上ホールド", "Hold Right Stick Up", "", "", ""));
            newSummonGesture = ToggleBitFlag(newSummonGesture, 1, L("両手トリガー長押し", "Hold Both Triggers", "", "", ""));
            newSummonGesture = ToggleBitFlag(newSummonGesture, 4, L("ダブルトリガー (L)", "Double-tap Trigger (L)", "", "", ""));
            newSummonGesture = ToggleBitFlag(newSummonGesture, 8, L("ダブルトリガー (R)", "Double-tap Trigger (R)", "", "", ""));
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField(L("デスクトップ呼び出しジェスチャー初期値", "Default Desktop Summon Gesture", "defaultDesktopSummonGesture",
                "デスクトップモードでHUDを呼び出すジェスチャーの初期有効設定。",
                "Default enabled gestures for summoning the HUD in desktop mode."));
            CopyFieldNameMenu("defaultDesktopSummonGesture");
            int newDesktopSummonGesture = settings.defaultDesktopSummonGesture;
            EditorGUI.indentLevel++;
            newDesktopSummonGesture = ToggleBitFlag(newDesktopSummonGesture, 1, L("Tabダブルタップ", "Double-tap Tab", "", "", ""));
            newDesktopSummonGesture = ToggleBitFlag(newDesktopSummonGesture, 2, L("F5ダブルタップ", "Double-tap F5", "", "", ""));
            newDesktopSummonGesture = ToggleBitFlag(newDesktopSummonGesture, 4, L("ESC長押し", "Hold ESC", "", "", ""));
            EditorGUI.indentLevel--;
            float newHold = SliderField("ジェスチャー保持時間 [秒]", "Gesture Hold Duration [s]", "gestureHoldDuration",
                "長押しジェスチャーの保持時間（秒）。VR両手トリガー / 右スティック上 / デスクトップESCに共通適用。",
                "Hold duration (seconds) for hold gestures. Applies to VR both-triggers / right-stick-up / desktop ESC.",
                settings.gestureHoldDuration, 0.1f, 2f);
            float newHudThreshold = SliderField("ジェスチャーHUD表示猶予 [秒]", "Gesture HUD Show Delay [s]", "gestureHudShowThreshold",
                "HUDプログレスを表示し始めるまでの猶予（秒）。",
                "Delay (seconds) before the HUD progress starts to appear.",
                settings.gestureHudShowThreshold, 0f, 0.3f);
            Vector3 newHudVrOffset = EditorGUILayout.Vector3Field(
                L("HUD配置オフセット (VR)", "HUD Offset (VR)", "hudVrLocalOffset",
                    "VR: 頭部ローカル座標におけるHUDオフセット（m）。(0,0,Z)で視界中央。",
                    "VR: HUD offset (m) in head-local coordinates. (0,0,Z) is the center of view."),
                settings.hudVrLocalOffset);
            CopyFieldNameMenu("hudVrLocalOffset");
            Vector3 newHudDesktopOffset = EditorGUILayout.Vector3Field(
                L("HUD配置オフセット (Desktop)", "HUD Offset (Desktop)", "hudDesktopLocalOffset",
                    "デスクトップ: カメラローカル座標におけるHUDオフセット（m）。Zは前方距離。",
                    "Desktop: HUD offset (m) in camera-local coordinates. Z is the forward distance."),
                settings.hudDesktopLocalOffset);
            CopyFieldNameMenu("hudDesktopLocalOffset");

            float newDist = SliderField("パネル自動閉じ距離 [m]", "Panel Auto-dismiss Distance [m]", "panelAutoDismissDistance",
                "ポータブルパネルからこの距離（m）以上離れると自動的に閉じる。0 で無効。",
                "Auto-closes the portable panel when you move farther than this distance (m). 0 to disable.",
                settings.panelAutoDismissDistance, 0f, 10f);
            float newSight = SliderField("パネル視界外閉じ [秒]", "Panel Out-of-sight Dismiss [s]", "panelOutOfSightDismissSec",
                "ポータブルパネルが視界外に出てからこの秒数経過で自動的に閉じる。0 で無効。",
                "Auto-closes the portable panel this many seconds after it leaves your field of view. 0 to disable.",
                settings.panelOutOfSightDismissSec, 0f, 60f);

            float newNear = SliderField("壁パネル近距離 [m]", "Wall Panel Near Distance [m]", "wallNearDistance",
                "この距離（m）以内に近づくとフルコンテンツ表示に切り替える（内側閾値）。",
                "Switches to full-content display when you come within this distance (m) (inner threshold).",
                settings.wallNearDistance, 0f, 10f);
            float newFar = SliderField("壁パネル遠距離 [m]", "Wall Panel Far Distance [m]", "wallFarDistance",
                "この距離（m）以上離れるとResyncのみ表示に切り替える（外側閾値）。",
                "Switches to Resync-only display when you move farther than this distance (m) (outer threshold).",
                settings.wallFarDistance, 0f, 10f);
            string newUnlockPasscode = TextField("壁パネル解錠パスコード", "Wall Panel Unlock Passcode", "wallUnlockPasscode",
                "WallControlPanel の Staff ビュー解錠用 4 桁数字。空文字で無効。",
                "4-digit number to unlock the Staff view of the WallControlPanel. Empty to disable.",
                settings.wallUnlockPasscode);
            newUnlockPasscode = NormalizeWallUnlockPasscode(newUnlockPasscode);

            if (!EditorGUI.EndChangeCheck())
            {
                DrawStaffNamesField(root, settings);
                return;
            }

            Undo.RecordObject(settings, "Change AunCast UI Settings");
            settings.defaultVolume = newDefaultVolume;
            settings.defaultUrl = new VRC.SDKBase.VRCUrl(newDefaultUrl);
            settings.autoPlayDefaultOnFirstJoin = newAutoPlayDefault;
            settings.defaultSummonGesture = newSummonGesture;
            settings.defaultDesktopSummonGesture = newDesktopSummonGesture;
            settings.gestureHoldDuration = newHold;
            settings.gestureHudShowThreshold = newHudThreshold;
            settings.hudVrLocalOffset = newHudVrOffset;
            settings.hudDesktopLocalOffset = newHudDesktopOffset;
            settings.panelAutoDismissDistance = newDist;
            settings.panelOutOfSightDismissSec = newSight;
            settings.wallNearDistance = newNear;
            settings.wallFarDistance = Mathf.Max(newNear, newFar);
            settings.wallUnlockPasscode = newUnlockPasscode;
            EditorUtility.SetDirty(settings);

            ApplyUiSettingsToScene(root, settings);
            DrawStaffNamesField(root, settings);
        }

        // ── 再生監視 ──

        private void DrawPlaybackMonitorSettings(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("再生監視", "Playback Monitoring"),
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField(AunCastEditorLocalization.Localize("無音検知", "Silence Detection"));
            EditorGUI.indentLevel++;
            float newRms = SliderField("RMS閾値 [dBFS]", "RMS Threshold [dBFS]", "silenceRmsThresholdDbfs",
                "無音判定RMS閾値。0dBFS = フルスケール。",
                "RMS threshold for silence detection. 0 dBFS = full scale.",
                settings.silenceRmsThresholdDbfs, -96f, 0f);
            float newSilenceConsec = SliderField("継続秒数 [秒]", "Consecutive Duration [s]", "silenceConsecutiveSec",
                "無音がこの秒数継続したらResyncを発火する。",
                "Fires a Resync when silence continues for this many seconds.",
                settings.silenceConsecutiveSec, 0.5f, 30f);
            float newSuppress = SliderField("抑止時間 [秒]", "Suppress Duration [s]", "silenceSuppressSec",
                "Resync後に無音検知を再有効化するまでの抑止時間（秒）。",
                "Suppression time (seconds) before re-enabling silence detection after a Resync.",
                settings.silenceSuppressSec, 0f, 600f);
            float newPeakHold = SliderField("ピーク保持 [秒]", "Peak Hold [s]", "silenceMeterPeakHoldSec",
                "RMSメーターのピーク値を保持する時間（秒）。",
                "Time (seconds) the RMS meter holds its peak value.",
                settings.silenceMeterPeakHoldSec, 0f, 5f);
            float newPeakDecay = SliderField("ピーク減衰 [dB/秒]", "Peak Decay [dB/s]", "silenceMeterPeakDecayDbPerSec",
                "ピーク保持後にピークラインが下がる速度（dB/秒）。",
                "Rate (dB/s) at which the peak line falls after the peak hold.",
                settings.silenceMeterPeakDecayDbPerSec, 0f, 60f);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField(AunCastEditorLocalization.Localize("停止検知", "Stall Detection"));
            EditorGUI.indentLevel++;
            float newStalled = SliderField("タイムアウト [秒]", "Timeout [s]", "stalledTimeoutSec",
                "停止判定の継続時間（秒）。",
                "Duration (seconds) used to determine a stall.",
                settings.stalledTimeoutSec, 0.5f, 30f);
            float newInterval = SliderField("ポーリング間隔 [秒]", "Polling Interval [s]", "monitorIntervalSec",
                "Active Playerの監視ポーリング間隔（秒）。",
                "Polling interval (seconds) for monitoring the Active player.",
                settings.monitorIntervalSec, 0.01f, 1f);
            float newAdvance = SliderField("前進判定閾値 [秒]", "Advance Threshold [s]", "minAdvanceThresholdSec",
                "ポーリング間隔ごとの再生位置の変化量がこの値を超えたら「再生が前進した」と判定する。",
                "If the playback position changes by more than this per polling interval, playback is considered to have advanced.",
                settings.minAdvanceThresholdSec, 0f, 0.1f);
            int newMinConsec = IntSliderField("最小連続前進回数 [回]", "Min Consecutive Advances", "minConsecutiveAdvances",
                "生存確認に必要な連続前進回数。",
                "Number of consecutive advances required to confirm liveness.",
                settings.minConsecutiveAdvances, 1, 30);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField(AunCastEditorLocalization.Localize("ドリフト", "Drift"));
            EditorGUI.indentLevel++;
            float newDriftThreshold = SliderField("Resync閾値 [秒]", "Resync Threshold [s]", "driftResyncThresholdSec",
                "蓄積ドリフトがこの値を超えたら自動Resync。",
                "Automatically Resyncs when accumulated drift exceeds this value.",
                settings.driftResyncThresholdSec, 0.01f, 1f);
            float newSmoothing = SliderField("平滑化時定数 [秒]", "Smoothing Time Constant [s]", "driftSmoothingTimeConstant",
                "ドリフトEMAの時定数（秒）。大きいほど緩やかに追従する。",
                "Time constant (seconds) for the drift EMA. Larger values track more gradually.",
                settings.driftSmoothingTimeConstant, 0.1f, 10f);
            float newWarmup = SliderField("猶予時間 [秒]", "Warm-up Time [s]", "driftWarmupSec",
                "安定再生開始直後にドリフト積算を抑制する猶予時間（秒）。",
                "Grace time (seconds) that suppresses drift accumulation right after stable playback begins.",
                settings.driftWarmupSec, 0f, 30f);
            EditorGUI.indentLevel--;

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
            EditorUtility.SetDirty(settings);

            ApplyPlaybackMonitorSettingsToScene(root, settings);
        }

        // ── Resync制御 ──

        private void DrawResyncSettings(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("Resync制御", "Resync Control"),
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField(AunCastEditorLocalization.Localize("同時接続制限", "Concurrent Connection Limit"));
            EditorGUI.indentLevel++;
            int newConcurrent = IntSliderField("同時Resync上限 [人]", "Max Concurrent Resyncs", "maxConcurrentResyncUsers",
                "同時Resync実行数の初期上限。",
                "Initial upper limit on the number of concurrent Resyncs.",
                settings.maxConcurrentResyncUsers, 1, 100);
            int newConnLimit = IntSliderField("同時接続上限", "Max Connections", "maxConnectionLimit",
                "配信サーバへの同時接続上限の既定値。",
                "Default upper limit on simultaneous connections to the streaming server.",
                settings.maxConnectionLimit, 1, 255);
            float newGrant = SliderField("接続開始待ち [秒]", "Connection Start Wait [s]", "grantTimeoutSec",
                "Resync許可後、接続が始まるまでの最大待機時間（秒）。",
                "Maximum wait time (seconds) for a connection to start after a Resync is granted.",
                settings.grantTimeoutSec, 1f, 60f);
            float newRunning = SliderField("実行時間の上限 [秒]", "Max Run Time [s]", "runningTimeoutSec",
                "1回のResync実行が許される最大時間（秒）。",
                "Maximum time (seconds) allowed for a single Resync run.",
                settings.runningTimeoutSec, 1f, 120f);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField(AunCastEditorLocalization.Localize("リトライ / クールダウン", "Retry / Cooldown"));
            EditorGUI.indentLevel++;
            float newCycle = SliderField("切替完了タイムアウト [秒]", "Switch Completion Timeout [s]", "resyncCycleTimeoutSec",
                "GRANTED後、Active/Standby切替が完了するまでの最大許容時間（秒）。",
                "Maximum allowed time (seconds) for the Active/Standby switch to complete after GRANTED.",
                settings.resyncCycleTimeoutSec, 1f, 120f);
            float newLocal = SliderField("読込後の待機 [秒]", "Post-load Wait [s]", "localCooldownSec",
                "LoadURL完了後、次のResyncを受け付けるまでの待機時間（秒）。",
                "Wait time (seconds) after LoadURL completes before the next Resync is accepted.",
                settings.localCooldownSec, 0f, 60f);
            float newBase = SliderField("リトライ間隔（初回） [秒]", "Retry Interval (Initial) [s]", "baseCooldownSec",
                "リトライの基本待機時間（秒）。両系統の接続が失敗し続けると、この値からリトライ間隔倍率に従って増えていく（上限はリトライ間隔（上限））。レート制限による再接続待ちでは、倍増せずこの値で毎回待機する。",
                "Base retry wait time (seconds). If both pipelines keep failing to connect, it grows from this value by the retry interval multiplier (capped by Retry Interval (Max)). For rate-limited reconnection waits, it waits this value each time without multiplying.",
                settings.baseCooldownSec, 5f, 180f);
            float newMultiplier = SliderField("リトライ間隔倍率", "Retry Interval Multiplier", "retryCooldownMultiplier",
                "両系統失敗時に、連続失敗ごとのリトライ間隔へ掛ける倍率。1.0 で固定間隔、2.0 で倍々。",
                "Multiplier applied to the retry interval per consecutive failure when both pipelines fail. 1.0 keeps a fixed interval, 2.0 doubles each time.",
                settings.retryCooldownMultiplier, 1f, 2f);
            float newMax = SliderField("リトライ間隔（上限） [秒]", "Retry Interval (Max) [s]", "maxRetryCooldownSec",
                "倍率に従って増えるリトライ間隔の頭打ち値（秒）。両系統失敗時のバックオフがこの値を超えない。",
                "Cap (seconds) for the retry interval that grows by the multiplier. The backoff on both-pipeline failure does not exceed this value.",
                settings.maxRetryCooldownSec, 5f, 180f);
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
            settings.retryCooldownMultiplier = newMultiplier;
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
                SetIntProperty(so, "summonGesture", settings.defaultSummonGesture);
                SetIntProperty(so, "desktopSummonGesture", settings.defaultDesktopSummonGesture);
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
                SetVector3Property(so, "vrLocalOffset", settings.hudVrLocalOffset);
                SetVector3Property(so, "desktopLocalOffset", settings.hudDesktopLocalOffset);
            });

            var wallPanels = root.GetComponentsInChildren<WallControlPanel>(true);
            ApplyToUdonComponents(wallPanels, so =>
            {
                SetFloatProperty(so, "wallNearDistance", settings.wallNearDistance);
                SetFloatProperty(so, "wallFarDistance", settings.wallFarDistance);
                SetStringProperty(so, "unlockPasscode", settings.wallUnlockPasscode);
            });

            var staffPanels = root.GetComponentsInChildren<StaffControlPanel>(true);
            ApplyToUdonComponents(staffPanels, so =>
            {
                SetStringArrayProperty(so, "allowedUserNames", settings.staffAllowedUserNames);
            });

            var controllers = root.GetComponentsInChildren<LocalDualPlayerController>(true);
            ApplyToUdonComponents(controllers, so =>
            {
                SetFloatProperty(so, "defaultVolume", settings.defaultVolume);
                // VRCUrl は内部 string フィールド経由で設定する（VRCUrl は [Serializable] のインライン値型）
                var defaultUrlProp = so.FindProperty("defaultUrl");
                if (defaultUrlProp != null)
                {
                    var urlInner = defaultUrlProp.FindPropertyRelative("url");
                    if (urlInner != null)
                        urlInner.stringValue = settings.defaultUrl != null ? settings.defaultUrl.Get() : "";
                }
                var autoPlayProp = so.FindProperty("autoPlayDefaultOnFirstJoin");
                if (autoPlayProp != null) autoPlayProp.boolValue = settings.autoPlayDefaultOnFirstJoin;
            });

            // 表示用 UI を実値へ揃える（Play せずとも見た目を一致させる）。
            // 注: ウォールパネルのジェスチャートグルは編集時プレビュー同期の対象外。
            // チェックマークがカスタム Graphic で編集時に再描画されにくく、かつ実行時は
            // SyncGestureToggles が summonGesture から上書きするため、設定値自体は
            // ApplyToUdonComponents の summonGesture 転写で正しく反映される。
            bool changed = false;
            foreach (var panel in userPanels)
            {
                if (panel == null) continue;
                changed |= SyncSlider(panel, "volumeSlider", settings.defaultVolume);
            }

            if (changed) RepaintUiViews();
        }

        private static void ApplyPlaybackMonitorSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var detectors = FindSceneComponents<AunCastSpeaker>(root.gameObject.scene);
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

            var userPanels = root.GetComponentsInChildren<UserStatusPanel>(true);
            ApplyToUdonComponents(userPanels, so =>
            {
                SetFloatProperty(so, "silenceMeterPeakHoldSec", settings.silenceMeterPeakHoldSec);
                SetFloatProperty(so, "silenceMeterPeakDecayDbPerSec", settings.silenceMeterPeakDecayDbPerSec);
            });
        }

        internal static void ApplyResyncSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
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
                SetFloatProperty(so, "retryCooldownMultiplier", settings.retryCooldownMultiplier);
                SetFloatProperty(so, "maxRetryCooldownSec", settings.maxRetryCooldownSec);
            });

            // StaffControlPanel の数値表示/入力欄を実値へ揃える（Play せずとも見た目を一致させる）
            string concurrentVal = settings.maxConcurrentResyncUsers.ToString();
            string connectionVal = settings.maxConnectionLimit.ToString();
            var staffPanels = root.GetComponentsInChildren<StaffControlPanel>(true);
            bool changed = false;
            foreach (var panel in staffPanels)
            {
                if (panel == null) continue;
                changed |= SyncTextDisplay(panel, "concurrentLimitDisplayText", concurrentVal);
                changed |= SyncInputField(panel, "concurrentLimitInput", concurrentVal);
                changed |= SyncTextDisplay(panel, "connectionLimitDisplayText", connectionVal);
                changed |= SyncInputField(panel, "connectionLimitInput", connectionVal);
            }

            if (changed) RepaintUiViews();
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

        // ── AunCastSettings → 表示用 UI への反映 ──
        // StaffControlPanel / UserStatusPanel / WallControlPanel が参照する素の UI
        // コンポーネント（TMP / Slider / Toggle）へ設定値を編集時にも反映し、Play せずとも
        // シーン上の表示を実値に揃える。

        // パネルが参照する UI コンポーネントを取得する。
        // プロキシ MonoBehaviour の SerializeField は AunCast.prefab にネストされた
        // WallControlPanel のようなネストプレハブのインスタンスでは編集時に参照が解決されず
        // null になることがある。実行時が参照を解決するのと同じ backing UdonBehaviour の
        // publicVariables を優先して読み、取れない場合のみプロキシ側へフォールバックする。
        private static T GetReferencedUiComponent<T>(UdonSharp.UdonSharpBehaviour panel, string fieldName)
            where T : UnityEngine.Object
        {
            if (panel == null) return null;

            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
            if (udon != null && udon.publicVariables != null
                && udon.publicVariables.TryGetVariableValue(fieldName, out object value)
                && value is T fromUdon)
                return fromUdon;

            // フォールバック: 非ネストのプロキシ（UserStatusPanel 等）はこちらで取れる
            var so = new SerializedObject(panel);
            var prop = so.FindProperty(fieldName);
            if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
                return prop.objectReferenceValue as T;
            return null;
        }

        private static void MarkUiComponentDirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        // UI 反映で値変更があったとき、シーン/ゲームビューを明示的に再描画して
        // Play せずとも見た目を反映させる（TMP テキスト等は自動再描画されないことがある）。
        private static void RepaintUiViews()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static bool SyncTextDisplay(UdonSharp.UdonSharpBehaviour panel, string fieldName, string value)
        {
            var text = GetReferencedUiComponent<TMP_Text>(panel, fieldName);
            if (text == null || text.text == value) return false;
            Undo.RecordObject(text, "Sync AunCast UI Display");
            text.text = value;
            MarkUiComponentDirty(text);
            return true;
        }

        private static bool SyncInputField(UdonSharp.UdonSharpBehaviour panel, string fieldName, string value)
        {
            var input = GetReferencedUiComponent<TMP_InputField>(panel, fieldName);
            if (input == null || input.text == value) return false;
            Undo.RecordObject(input, "Sync AunCast UI Display");
            input.SetTextWithoutNotify(value);
            MarkUiComponentDirty(input);
            return true;
        }

        private static bool SyncSlider(UdonSharp.UdonSharpBehaviour panel, string fieldName, float value)
        {
            var slider = GetReferencedUiComponent<UnityEngine.UI.Slider>(panel, fieldName);
            if (slider == null || Mathf.Approximately(slider.value, value)) return false;
            Undo.RecordObject(slider, "Sync AunCast UI Display");
            slider.SetValueWithoutNotify(value);
            MarkUiComponentDirty(slider);
            return true;
        }

        private static void SetFloatProperty(SerializedObject so, string fieldName, float value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.floatValue = value;
        }

        private static bool SetFloatPropertyIfChanged(SerializedObject so, string fieldName, float value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null) return false;
            if (Mathf.Approximately(prop.floatValue, value)) return false;
            prop.floatValue = value;
            return true;
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

        private static bool SetObjectProperty(SerializedObject so, string fieldName, UnityEngine.Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            if (prop.objectReferenceValue == value)
                return false;

            prop.objectReferenceValue = value;
            return true;
        }

        private static void SetVector3Property(SerializedObject so, string fieldName, Vector3 value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.vector3Value = value;
        }

        private static void SetStringArrayProperty(SerializedObject so, string fieldName, string[] values)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null || !prop.isArray) return;
            int count = values != null ? values.Length : 0;
            prop.arraySize = count;
            for (int i = 0; i < count; i++)
                prop.GetArrayElementAtIndex(i).stringValue = values[i] ?? string.Empty;
        }

        private static bool SetObjectArrayProperty<T>(SerializedObject so, string fieldName, T[] values)
            where T : UnityEngine.Object
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null || !prop.isArray)
                return false;

            int count = values != null ? values.Length : 0;
            bool changed = prop.arraySize != count;
            prop.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                var value = values[i];
                if (element.objectReferenceValue == value) continue;
                element.objectReferenceValue = value;
                changed = true;
            }

            return changed;
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

        private static void AutoAssignAudioLinkBehaviour(PlaybackSwitcher[] switchers, bool recordUndo = true)
        {
            if (switchers == null || switchers.Length == 0) return;

            for (int i = 0; i < switchers.Length; i++)
            {
                PlaybackSwitcher switcher = switchers[i];
                if (switcher == null) continue;
                UdonSharp.UdonSharpBehaviour audioLink = FindAudioLinkBehaviour(switcher.gameObject.scene);
                if (audioLink == null) continue;

                var so = new SerializedObject(switcher);
                var prop = so.FindProperty("audioLinkBehaviour");
                if (prop == null || prop.objectReferenceValue != null) continue;

                if (recordUndo)
                    Undo.RecordObject(switcher, "Auto Assign AudioLink Behaviour");
                prop.objectReferenceValue = audioLink;
                bool applied = recordUndo ? so.ApplyModifiedProperties() : so.ApplyModifiedPropertiesWithoutUndo();
                if (!applied) continue;

                UdonSharpEditorUtility.CopyProxyToUdon(switcher);
                EditorUtility.SetDirty(switcher);
                PrefabUtility.RecordPrefabInstancePropertyModifications(switcher);

                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(switcher);
                if (udon == null) continue;
                EditorUtility.SetDirty(udon);
                PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            }
        }

        private static UdonSharp.UdonSharpBehaviour FindAudioLinkBehaviour(Scene scene)
        {
            if (!scene.IsValid()) return null;
            UdonSharp.UdonSharpBehaviour fallback = null;
            UdonSharp.UdonSharpBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<UdonSharp.UdonSharpBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                UdonSharp.UdonSharpBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (!behaviour.gameObject.scene.IsValid() || behaviour.gameObject.scene != scene) continue;
                if (behaviour.GetType().Name == "AudioLink")
                    return behaviour;
                if (fallback == null && behaviour.gameObject.name == "AudioLink")
                    fallback = behaviour;
            }
            return fallback;
        }

        /// <summary>
        /// 指定コンポーネントが AudioLink 付属（AudioLink を持つ GameObject の配下）かどうかを判定する。
        /// AudioLink.prefab の AudioLinkInput（AudioSource + VRCAVProVideoSpeaker）を、
        /// 一般のスピーカー変換候補・手動削除候補から区別するために使う。
        /// </summary>
        private static bool IsAudioLinkOwnedSource(Component component)
        {
            if (component == null) return false;
            Transform current = component.transform;
            while (current != null)
            {
                // GameObject 名フォールバック（FindAudioLinkBehaviour と同じ基準）
                if (current.gameObject.name == "AudioLink")
                    return true;
                Component[] behaviours = current.GetComponents<Component>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    Component behaviour = behaviours[i];
                    if (behaviour != null && behaviour.GetType().Name == "AudioLink")
                        return true;
                }
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// AudioLink 付属の内蔵スピーカー（AudioLinkInput）を無効化＋EditorOnly 化する。
        /// AunCast は AudioLink の入力をランタイムで Active スピーカーへ差し替えるため、
        /// 付属の VRCAVProVideoSpeaker + AudioSource は不要。削除は混乱のもとになるため、
        /// GameObject を非アクティブ化しビルド時に剥がれる EditorOnly タグを付ける（冪等）。
        /// 処理した件数を返す。
        /// </summary>
        private static int NeutralizeAudioLinkInputs(Scene scene, bool recordUndo)
        {
            if (!scene.IsValid()) return 0;
            int count = 0;
            Component[] speakers = FindSceneComponentsByTypeName(scene, SPEAKER_COMPONENT_TYPE_NAME);
            for (int i = 0; i < speakers.Length; i++)
            {
                Component speaker = speakers[i];
                if (speaker == null) continue;
                if (!IsAudioLinkOwnedSource(speaker)) continue;
                GameObject input = speaker.gameObject;
                bool changed = NeutralizeGameObjectAsEditorOnly(input, recordUndo);
                changed |= EnsureAudioLinkInputNote(input, recordUndo);
                if (changed)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 無効化した AudioLinkInput のすぐ隣（同じ親・直後の兄弟）に、
        /// 「AudioLink の参照先 AudioSource は AunCast が自動管理する」旨を示す注記オブジェクトを
        /// EditorOnly・非アクティブ状態で作成する。既に存在すれば作成しない（冪等）。作成したら true。
        /// </summary>
        private static bool EnsureAudioLinkInputNote(GameObject input, bool recordUndo)
        {
            if (input == null) return false;
            Transform parent = input.transform.parent;

            // 既存の注記があれば作成しない（冪等）。注記が置かれる兄弟集合を走査する。
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i).name == AUDIOLINK_INPUT_NOTE_NAME)
                        return false;
            }
            else if (input.scene.IsValid())
            {
                GameObject[] roots = input.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    if (roots[i].name == AUDIOLINK_INPUT_NOTE_NAME)
                        return false;
            }

            var note = new GameObject(AUDIOLINK_INPUT_NOTE_NAME);
            if (recordUndo)
                Undo.RegisterCreatedObjectUndo(note, "Create AudioLink Input Note");

            if (parent != null)
                note.transform.SetParent(parent, false);
            else if (input.scene.IsValid())
                SceneManager.MoveGameObjectToScene(note, input.scene);
            // AudioSource（AudioLinkInput）の直後に並べる
            note.transform.SetSiblingIndex(input.transform.GetSiblingIndex() + 1);

            note.tag = "EditorOnly";
            note.SetActive(false);
            EditorUtility.SetDirty(note);
            PrefabUtility.RecordPrefabInstancePropertyModifications(note);
            return true;
        }

        /// <summary>
        /// GameObject を非アクティブ化し EditorOnly タグを付ける。既に両方満たしていれば何もしない（冪等）。
        /// 変更があれば true を返す。
        /// </summary>
        private static bool NeutralizeGameObjectAsEditorOnly(GameObject go, bool recordUndo)
        {
            if (go == null) return false;
            bool needsDisable = go.activeSelf;
            bool needsTag = !go.CompareTag("EditorOnly");
            if (!needsDisable && !needsTag) return false;

            if (recordUndo)
                Undo.RecordObject(go, "Neutralize AudioLink Input");
            if (needsDisable) go.SetActive(false);
            if (needsTag) go.tag = "EditorOnly";
            EditorUtility.SetDirty(go);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            return true;
        }

        private void DrawTimelineLoggingToggle(
            LocalDualPlayerController[] ldpcList,
            ActivePlayerMonitor[] apmList,
            ResyncCoordinatorClient[] rccList,
            PlaybackSwitcher[] pbsList,
            ResyncCoordinator[] rcList)
        {
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("デバッグ", "Debug"),
                EditorStyles.boldLabel);

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
            bool newValue = ToggleField("タイムラインログ", "Timeline Logging", "_timelineLogging",
                "全コンポーネントのタイムラインログ出力を一括で切り替える。",
                "Toggles timeline log output for all components at once.", currentValue);
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
