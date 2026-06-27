#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        private const string DEFAULT_VPM_LISTING_URL = "https://pasocommate.github.io/AunCast/index.json";
        private const string TMP_FALLBACK_DEFAULT_FONT_GUID = "b0cf90c18247f154094021e2de9bf529";
        private const string TMP_FALLBACK_NOTO_FONT_GUID = "32134e5dc8c950c4cb5bb7deaae7d539";
        private const string TMP_FALLBACK_MENU_PATH = "Tools→TextMesh Pro VRC Fallback Font JPを設定";
        private const double VPM_VERSION_REQUEST_TIMEOUT_SEC = 8.0;
        private const string SESSION_KEY_VPM_CHECK_DONE = "AunCast.SettingsEditor.VpmCheckDone";
        private const string SESSION_KEY_VPM_HAS_UPDATE = "AunCast.SettingsEditor.VpmHasUpdate";
        private const string SESSION_KEY_VPM_LATEST_VERSION = "AunCast.SettingsEditor.VpmLatestVersion";
        private const string SPEAKER_COMPONENT_TYPE_NAME = "VRCAVProVideoSpeaker";
        private const string GENERATED_SPEAKER_CONTAINER_A_NAME = "AunCastSpeakerRefs_A";
        private const string GENERATED_SPEAKER_CONTAINER_B_NAME = "AunCastSpeakerRefs_B";
        private const string AUNCAST_EVENT_HUB_NAME = "AunCastEventHub";
        private const string AUNCAST_EVENT_BUS_ASSET_GUID = "86f742d2e8954336a9cd87f1e4527d80";
        private const string EDITOR_ONLY_TAG = "EditorOnly";

        private const double SPEAKER_CACHE_POLL_INTERVAL_SEC = 3.0;

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
        private string _vpmListingUrl;
        private bool _vpmSessionCacheLoaded;

        private bool _speakerCacheDirty = true;
        private double _speakerCacheTime;
        private SpeakerCandidate[] _cachedSpeakerCandidates;
        private List<string> _cachedSpeakerValidationErrors;

        private readonly struct SpeakerCandidate
        {
            public readonly GameObject gameObject;
            public readonly AudioSource audioSource;
            public readonly Component speaker;
            public readonly string hierarchyPath;

            public SpeakerCandidate(GameObject gameObject, AudioSource audioSource, Component speaker, string hierarchyPath)
            {
                this.gameObject = gameObject;
                this.audioSource = audioSource;
                this.speaker = speaker;
                this.hierarchyPath = hierarchyPath;
            }
        }

        private readonly struct SpeakerSetupContext
        {
            public readonly VideoPlayerManager managerA;
            public readonly VideoPlayerManager managerB;
            public readonly VRCAVProVideoPlayer playerA;
            public readonly VRCAVProVideoPlayer playerB;
            public readonly Transform playerRootA;
            public readonly Transform playerRootB;
            public readonly PlaybackSwitcher switcher;
            public readonly SyncDebugDisplay syncDebugDisplay;

            public SpeakerSetupContext(
                VideoPlayerManager managerA,
                VideoPlayerManager managerB,
                VRCAVProVideoPlayer playerA,
                VRCAVProVideoPlayer playerB,
                Transform playerRootA,
                Transform playerRootB,
                PlaybackSwitcher switcher,
                SyncDebugDisplay syncDebugDisplay)
            {
                this.managerA = managerA;
                this.managerB = managerB;
                this.playerA = playerA;
                this.playerB = playerB;
                this.playerRootA = playerRootA;
                this.playerRootB = playerRootB;
                this.switcher = switcher;
                this.syncDebugDisplay = syncDebugDisplay;
            }
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

        private static int ToggleBitFlag(int flags, int bit, GUIContent label)
        {
            bool on = EditorGUILayout.Toggle(label, (flags & bit) != 0);
            return on ? flags | bit : flags & ~bit;
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
                    "AunCastコンポーネントが見つかりません。AunCast ルート配下で設定してください。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(8);
            DrawWallPanelReferenceTools(root);

            EditorGUILayout.Space(8);
            DrawAvProSpeakerSetupTools(root, settings);

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

        private void OnEnable()
        {
            // Inspector が開かれた瞬間に一度だけ既存の EventBus / 壁パネル参照を自動再配線する。
            // OnInspectorGUI から呼ぶと毎フレーム走り、手動で変更した参照を
            // 上書きしてしまうので OnEnable に限定する。Hub の新規作成は手動ボタン時のみ行う。
            // SetObjectProperty / SetObjectArrayProperty は値が完全一致のときに false を返すので、
            // 既に整合している場合は ApplyUdonSerializedChanges が呼ばれず、ユーザーの手動編集を
            // 上書きしない。
            var settings = target as PasocomMate.AunCast.AunCastSettings;
            if (settings != null)
                RewireEventHubAndConsumers(settings.transform, recordUndo: false, writeLog: false);

            EditorSceneManager.sceneDirtied += OnSceneDirtied;
            Undo.postprocessModifications += OnPostprocessModifications;
            _speakerCacheDirty = true;
        }

        private void OnDisable()
        {
            StopVpmVersionCheck();
            EditorSceneManager.sceneDirtied -= OnSceneDirtied;
            Undo.postprocessModifications -= OnPostprocessModifications;
        }

        private void OnSceneDirtied(Scene scene)
        {
            _speakerCacheDirty = true;
        }

        private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            _speakerCacheDirty = true;
            return modifications;
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

        private static void DrawWallPanelReferenceTools(Transform root)
        {
            EditorGUILayout.LabelField("壁パネル配線", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "AunCast 配下の AunCastEventBus / WallControlPanel / UserStatusPanel / スクリーン購読者を再配線します。",
                MessageType.None);
            using (new EditorGUI.DisabledScope(root == null))
            {
                if (!GUILayout.Button("AunCastEventBus参照を再配線", GUILayout.Height(24)))
                    return;

                RewireEventHubAndConsumers(root, recordUndo: true, writeLog: true);
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
            var eventBus = FindOrCreateEventBus(root, createIfMissing: recordUndo, writeLog: writeLog);
            var switchers = root.GetComponentsInChildren<PlaybackSwitcher>(true);
            var meshScreens = root.GetComponentsInChildren<VideoMeshScreen>(true);
            var uiScreens = root.GetComponentsInChildren<VideoUiScreen>(true);
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
                    if (changed && ApplyUdonSerializedChanges(mesh, so, "Rewire VideoMeshScreen EventBus", recordUndo))
                        screenUpdated++;
                }
                foreach (var ui in uiScreens)
                {
                    if (ui == null) continue;
                    var so = new SerializedObject(ui);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);
                    // 停止中の固定画像を AunCastSettings から転写する
                    changed |= SetObjectProperty(so, "idleTexture", idleScreenTexture);
                    if (changed && ApplyUdonSerializedChanges(ui, so, "Rewire VideoUiScreen EventBus", recordUndo))
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

            if (writeLog)
                Debug.Log($"[AunCast] EventBus参照を再配線しました。Bus: {busUpdated}件 / Publisher: {publisherUpdated}件 / WallControlPanel: {wallUpdated}件 / UserStatusPanel: {userUpdated}件 / Screen: {screenUpdated}件 / 通知先: {notifyUpdated}件");
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
            VideoMeshScreen[] meshScreens,
            VideoUiScreen[] uiScreens)
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

        private void DrawAvProSpeakerSetupTools(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUILayout.LabelField("AVPro Speaker 配線", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シーン上の VRC AVPro Video Speaker + AudioSource を検出し、PlayerA/B 用に複製して参照を配線します。",
                MessageType.None);

            if (!TryResolveSpeakerSetupContext(root, out var context, out var resolveError))
            {
                EditorGUILayout.HelpBox(resolveError, MessageType.Warning);
                return;
            }

            RefreshSpeakerCacheIfNeeded(root, context);
            SpeakerCandidate[] candidates = _cachedSpeakerCandidates ?? Array.Empty<SpeakerCandidate>();
            DrawSpeakerCandidateList(candidates);

            List<string> validationErrors = _cachedSpeakerValidationErrors ?? new List<string>();
            if (validationErrors.Count == 0)
            {
                EditorGUILayout.HelpBox("現在の PlayerA/B AudioSource 配線に重複やルーティング不整合はありません。", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < validationErrors.Count; i++)
                    EditorGUILayout.HelpBox(validationErrors[i], MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(candidates.Length == 0))
            {
                if (!GUILayout.Button("AVPro Speaker 出力先セットアップを実行", GUILayout.Height(24)))
                    return;

                ExecuteSpeakerSetup(root, settings, context, candidates);
                _speakerCacheDirty = true;
            }
        }

        private void RefreshSpeakerCacheIfNeeded(Transform root, SpeakerSetupContext context)
        {
            double now = EditorApplication.timeSinceStartup;
            bool expired = now - _speakerCacheTime >= SPEAKER_CACHE_POLL_INTERVAL_SEC;
            if (!_speakerCacheDirty && !expired)
                return;

            _cachedSpeakerCandidates = CollectSpeakerCandidates(root, context);
            _cachedSpeakerValidationErrors = ValidateCurrentSpeakerRouting(context);
            _speakerCacheDirty = false;
            _speakerCacheTime = now;
        }

        private static void DrawSpeakerCandidateList(SpeakerCandidate[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
                return;

            EditorGUILayout.LabelField("検出対象", EditorStyles.miniBoldLabel);
            for (int i = 0; i < candidates.Length; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.ObjectField(candidates[i].gameObject, typeof(GameObject), true);
                EditorGUILayout.LabelField(candidates[i].hierarchyPath, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
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
                error = "AunCast ルートが見つかりません。";
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
                error = "VideoPlayerManager A/B が見つかりません。";
                return false;
            }
            if (managerA.avProPlayer == null || managerB.avProPlayer == null)
            {
                error = "VideoPlayerManager の avProPlayer 参照が不足しています。";
                return false;
            }

            var switcher = root.GetComponentInChildren<PlaybackSwitcher>(true);
            if (switcher == null)
            {
                error = "PlaybackSwitcher が見つかりません。";
                return false;
            }

            context = new SpeakerSetupContext(
                managerA,
                managerB,
                managerA.avProPlayer,
                managerB.avProPlayer,
                managerA.transform,
                managerB.transform,
                switcher,
                root.GetComponentInChildren<SyncDebugDisplay>(true));
            return true;
        }

        private static SpeakerCandidate[] CollectSpeakerCandidates(Transform root, SpeakerSetupContext context)
        {
            var list = new List<SpeakerCandidate>();
            AudioSource[] audioSources = UnityEngine.Object.FindObjectsOfType<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource audioSource = audioSources[i];
                if (audioSource == null) continue;
                GameObject go = audioSource.gameObject;
                if (go == null) continue;
                if (!go.activeInHierarchy) continue;
                if (!go.scene.IsValid() || go.scene != root.gameObject.scene) continue;
                if (IsUnderTransform(go.transform, context.playerRootA) || IsUnderTransform(go.transform, context.playerRootB))
                    continue;
                if (IsUnderGeneratedSpeakerContainer(go.transform))
                    continue;
                if (IsOriginalPrefabObjectUnderRoot(root, go))
                    continue;

                Component speaker = FindSpeakerComponent(go);
                if (speaker == null) continue;

                list.Add(new SpeakerCandidate(go, audioSource, speaker, GetHierarchyPath(go.transform)));
            }

            list.Sort((a, b) => string.Compare(a.hierarchyPath, b.hierarchyPath, StringComparison.Ordinal));
            return list.ToArray();
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
                        errors.Add($"PlayerA audioSources[{i}] が null です。");
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
                        errors.Add($"PlayerB audioSources[{i}] が null です。");
                        continue;
                    }
                    if (used.Contains(source))
                        errors.Add($"PlayerA/PlayerB で同一 AudioSource を共有しています: {GetHierarchyPath(source.transform)}");
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
                errors.Add($"{label}: 期待する VRCAVProVideoPlayer が null です。");
                return;
            }

            Component speaker = FindSpeakerComponent(source.gameObject);
            if (speaker == null)
            {
                errors.Add($"{label}: VRC AVPro Video Speaker がありません。({GetHierarchyPath(source.transform)})");
                return;
            }

            if (!IsSpeakerRoutedTo(speaker, expectedPlayer))
            {
                errors.Add($"{label}: Speaker の videoPlayer が想定先を向いていません。({GetHierarchyPath(source.transform)})");
            }
        }

        private static void ExecuteSpeakerSetup(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            SpeakerSetupContext context,
            SpeakerCandidate[] candidates)
        {
            if (candidates == null || candidates.Length == 0) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("AunCast AVPro Speaker Setup");
            try
            {
                AudioSource[] defaultA = context.managerA.audioSources;
                AudioSource[] defaultB = context.managerB.audioSources;

                ClearGeneratedSpeakerContainers(context.playerRootA, context.playerRootB);
                Transform containerA = GetOrCreateGeneratedSpeakerContainer(context.playerRootA, GENERATED_SPEAKER_CONTAINER_A_NAME);
                Transform containerB = GetOrCreateGeneratedSpeakerContainer(context.playerRootB, GENERATED_SPEAKER_CONTAINER_B_NAME);

                var newSourcesA = new List<AudioSource>();
                var newSourcesB = new List<AudioSource>();
                var newDetectorsA = new List<AudioSilenceDetector>();
                var newDetectorsB = new List<AudioSilenceDetector>();

                for (int i = 0; i < candidates.Length; i++)
                {
                    SpeakerCandidate candidate = candidates[i];
                    if (candidate.gameObject == null) continue;

                    GameObject cloneA = CloneSpeakerSource(candidate.gameObject, containerA);
                    GameObject cloneB = CloneSpeakerSource(candidate.gameObject, containerB);
                    if (cloneA == null || cloneB == null) continue;

                    AudioSource sourceA = cloneA.GetComponent<AudioSource>();
                    AudioSource sourceB = cloneB.GetComponent<AudioSource>();
                    if (sourceA == null || sourceB == null)
                    {
                        Debug.LogWarning($"[AunCast] AudioSource の複製に失敗したためスキップしました: {candidate.hierarchyPath}");
                        continue;
                    }

                    Component speakerA = FindSpeakerComponent(cloneA);
                    Component speakerB = FindSpeakerComponent(cloneB);
                    if (!TrySetSpeakerVideoPlayer(speakerA, context.playerA) ||
                        !TrySetSpeakerVideoPlayer(speakerB, context.playerB))
                    {
                        Debug.LogWarning($"[AunCast] Speaker の videoPlayer 再配線に失敗しました: {candidate.hierarchyPath}");
                        continue;
                    }

                    AudioSilenceDetector detectorA = EnsureAudioSilenceDetector(sourceA.gameObject, settings);
                    AudioSilenceDetector detectorB = EnsureAudioSilenceDetector(sourceB.gameObject, settings);

                    newSourcesA.Add(sourceA);
                    newSourcesB.Add(sourceB);
                    if (detectorA != null) newDetectorsA.Add(detectorA);
                    if (detectorB != null) newDetectorsB.Add(detectorB);

                    SetGameObjectDisabledAndEditorOnly(candidate.gameObject);
                }

                if (newSourcesA.Count == 0 || newSourcesB.Count == 0)
                {
                    Debug.LogWarning("[AunCast] 複製先 AudioSource を作成できなかったため、配線を中止しました。");
                    return;
                }
                if (newDetectorsA.Count == 0 || newDetectorsB.Count == 0)
                {
                    Debug.LogWarning("[AunCast] AudioSilenceDetector の生成に失敗したため、配線を中止しました。");
                    return;
                }

                DisableAudioSources(defaultA);
                DisableAudioSources(defaultB);

                ApplyAudioSourcesToManager(context.managerA, newSourcesA.ToArray());
                ApplyAudioSourcesToManager(context.managerB, newSourcesB.ToArray());

                AudioSilenceDetector detectorForA = newDetectorsA.Count > 0 ? newDetectorsA[0] : null;
                AudioSilenceDetector detectorForB = newDetectorsB.Count > 0 ? newDetectorsB[0] : null;
                AudioSource sourceForA = newSourcesA.Count > 0 ? newSourcesA[0] : null;
                AudioSource sourceForB = newSourcesB.Count > 0 ? newSourcesB[0] : null;

                ApplySilenceDetectorsToSwitcher(context.switcher, detectorForA, detectorForB);
                ApplySourcesToSyncDebugDisplay(context.syncDebugDisplay, sourceForA, sourceForB, detectorForA, detectorForB);

                EditorUtility.SetDirty(root.gameObject);
                Debug.Log($"[AunCast] AVPro Speaker 出力先セットアップを完了しました。対象: {candidates.Length}件 / A:{newSourcesA.Count} / B:{newSourcesB.Count}");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private static void ClearGeneratedSpeakerContainers(Transform playerRootA, Transform playerRootB)
        {
            DestroyContainerIfExists(playerRootA, GENERATED_SPEAKER_CONTAINER_A_NAME);
            DestroyContainerIfExists(playerRootB, GENERATED_SPEAKER_CONTAINER_B_NAME);
        }

        private static void DestroyContainerIfExists(Transform parent, string containerName)
        {
            if (parent == null) return;
            Transform found = parent.Find(containerName);
            if (found == null) return;
            Undo.DestroyObjectImmediate(found.gameObject);
        }

        private static Transform GetOrCreateGeneratedSpeakerContainer(Transform parent, string containerName)
        {
            Transform existing = parent.Find(containerName);
            if (existing != null) return existing;

            var go = new GameObject(containerName);
            Undo.RegisterCreatedObjectUndo(go, "Create AVPro Speaker Container");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject CloneSpeakerSource(GameObject source, Transform parent)
        {
            if (source == null || parent == null) return null;

            GameObject clone = UnityEngine.Object.Instantiate(source);
            Undo.RegisterCreatedObjectUndo(clone, "Duplicate AVPro Speaker Source");
            clone.transform.SetParent(parent, true);
            clone.name = source.name;
            if (string.Equals(clone.tag, EDITOR_ONLY_TAG, StringComparison.Ordinal))
                clone.tag = "Untagged";
            EditorUtility.SetDirty(clone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(clone);
            return clone;
        }

        private static void ApplyAudioSourcesToManager(VideoPlayerManager manager, AudioSource[] sources)
        {
            if (manager == null) return;
            var so = new SerializedObject(manager);
            if (SetObjectArrayProperty(so, nameof(VideoPlayerManager.audioSources), sources))
                ApplyUdonSerializedChanges(manager, so, "Apply AudioSources to VideoPlayerManager");
        }

        private static void ApplySilenceDetectorsToSwitcher(
            PlaybackSwitcher switcher,
            AudioSilenceDetector detectorA,
            AudioSilenceDetector detectorB)
        {
            if (switcher == null) return;

            var so = new SerializedObject(switcher);
            bool changed = false;
            changed |= SetObjectProperty(so, "silenceDetectorA", detectorA);
            changed |= SetObjectProperty(so, "silenceDetectorB", detectorB);
            if (changed)
                ApplyUdonSerializedChanges(switcher, so, "Apply Silence Detectors to PlaybackSwitcher");
        }

        private static void ApplySourcesToSyncDebugDisplay(
            SyncDebugDisplay syncDebugDisplay,
            AudioSource audioSourceA,
            AudioSource audioSourceB,
            AudioSilenceDetector detectorA,
            AudioSilenceDetector detectorB)
        {
            if (syncDebugDisplay == null) return;

            var so = new SerializedObject(syncDebugDisplay);
            bool changed = false;
            changed |= SetObjectProperty(so, "audioSourceA", audioSourceA);
            changed |= SetObjectProperty(so, "audioSourceB", audioSourceB);
            changed |= SetObjectProperty(so, "silenceDetectorA", detectorA);
            changed |= SetObjectProperty(so, "silenceDetectorB", detectorB);
            if (changed)
                ApplyUdonSerializedChanges(syncDebugDisplay, so, "Apply Audio Sources to SyncDebugDisplay");
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

        private static void DisableAudioSources(AudioSource[] audioSources)
        {
            if (audioSources == null) return;
            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource source = audioSources[i];
                if (source == null) continue;
                SetGameObjectDisabledAndEditorOnly(source.gameObject);
            }
        }

        private static AudioSilenceDetector EnsureAudioSilenceDetector(
            GameObject gameObject,
            PasocomMate.AunCast.AunCastSettings settings)
        {
            if (gameObject == null) return null;

            var detector = gameObject.GetComponent<AudioSilenceDetector>();
            if (detector == null)
                detector = Undo.AddComponent<AudioSilenceDetector>(gameObject);
            if (detector == null) return null;

            var so = new SerializedObject(detector);
            SetFloatProperty(so, "silenceRmsThresholdDbfs", settings != null ? settings.silenceRmsThresholdDbfs : -60f);
            SetFloatProperty(so, "silenceConsecutiveSec", settings != null ? settings.silenceConsecutiveSec : 2.0f);
            ApplyUdonSerializedChanges(detector, so, "Configure AudioSilenceDetector");
            return detector;
        }

        private static bool TrySetSpeakerVideoPlayer(Component speaker, VRCAVProVideoPlayer player)
        {
            if (speaker == null || player == null) return false;
            var so = new SerializedObject(speaker);
            SerializedProperty prop = so.FindProperty("videoPlayer");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;

            Undo.RecordObject(speaker, "Rewire AVPro Speaker videoPlayer");
            prop.objectReferenceValue = player;
            if (!so.ApplyModifiedProperties()) return false;

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

        private static bool IsUnderGeneratedSpeakerContainer(Transform target)
        {
            if (target == null) return false;
            Transform current = target;
            while (current != null)
            {
                if (current.name == GENERATED_SPEAKER_CONTAINER_A_NAME || current.name == GENERATED_SPEAKER_CONTAINER_B_NAME)
                    return true;
                current = current.parent;
            }

            return false;
        }

        private static bool IsOriginalPrefabObjectUnderRoot(Transform root, GameObject gameObject)
        {
            if (root == null || gameObject == null) return false;
            if (!IsUnderTransform(gameObject.transform, root)) return false;
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject)) return false;
            if (PrefabUtility.IsAddedGameObjectOverride(gameObject)) return false;
            return true;
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

        private static void SetGameObjectDisabledAndEditorOnly(GameObject gameObject)
        {
            if (gameObject == null) return;

            Undo.RecordObject(gameObject, "Disable and Tag EditorOnly");
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            if (!string.Equals(gameObject.tag, EDITOR_ONLY_TAG, StringComparison.Ordinal))
                gameObject.tag = EDITOR_ONLY_TAG;

            EditorUtility.SetDirty(gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
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
            var newIdleScreenTexture = (Texture2D)EditorGUILayout.ObjectField(
                L("停止中のスクリーン画像", "idleScreenTexture",
                    "再生停止中にスクリーンへ表示する固定画像。未指定なら初期割り当てのテクスチャへ復元する。"),
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

        // ── UI / 操作 ──

        private void DrawStaffNamesField(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            EditorGUI.BeginChangeCheck();
            var sp = serializedObject.FindProperty("staffAllowedUserNames");
            if (sp != null)
                EditorGUILayout.PropertyField(sp,
                    L("スタッフ許可ユーザー名", "staffAllowedUserNames", "パスコードなしでスタッフ権限を付与する VRChat ユーザー名リスト。"),
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
            EditorGUILayout.LabelField("UI / 操作", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float newDefaultVolume = SliderField("初期音量", "defaultVolume",
                "各ユーザーの起動時ローカル再生デフォルト音量（0〜1）。",
                settings.defaultVolume, 0f, 1f);
            string newDefaultUrl = TextField("デフォルト配信 URL", "defaultUrl",
                "Next URL 欄の初期値。インスタンス最初の Join 時の自動再生にも使用する。空欄で無効。",
                settings.defaultUrl != null ? settings.defaultUrl.Get() : "");
            bool newAutoPlayDefault = ToggleField("最初の Join で自動再生", "autoPlayDefaultOnFirstJoin",
                "インスタンスに最初のユーザーが Join した時点で、デフォルト配信 URL を自動再生する。",
                settings.autoPlayDefaultOnFirstJoin);

            EditorGUILayout.LabelField(L("VR 呼び出しジェスチャー初期値", "defaultSummonGesture",
                "VR モードで HUD を呼び出すジェスチャーの初期有効設定。"));
            CopyFieldNameMenu("defaultSummonGesture");
            int newSummonGesture = settings.defaultSummonGesture;
            EditorGUI.indentLevel++;
            newSummonGesture = ToggleBitFlag(newSummonGesture, 2, L("右スティック上ホールド", "Hold Right Stick Up", ""));
            newSummonGesture = ToggleBitFlag(newSummonGesture, 1, L("両手トリガー長押し", "Hold Both Triggers", ""));
            newSummonGesture = ToggleBitFlag(newSummonGesture, 4, L("ダブルトリガー (L)", "Double-tap Trigger (L)", ""));
            newSummonGesture = ToggleBitFlag(newSummonGesture, 8, L("ダブルトリガー (R)", "Double-tap Trigger (R)", ""));
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField(L("デスクトップ呼び出しジェスチャー初期値", "defaultDesktopSummonGesture",
                "デスクトップモードで HUD を呼び出すジェスチャーの初期有効設定。"));
            CopyFieldNameMenu("defaultDesktopSummonGesture");
            int newDesktopSummonGesture = settings.defaultDesktopSummonGesture;
            EditorGUI.indentLevel++;
            newDesktopSummonGesture = ToggleBitFlag(newDesktopSummonGesture, 1, L("Tab ダブルタップ", "Double-tap Tab", ""));
            newDesktopSummonGesture = ToggleBitFlag(newDesktopSummonGesture, 2, L("F5 ダブルタップ", "Double-tap F5", ""));
            newDesktopSummonGesture = ToggleBitFlag(newDesktopSummonGesture, 4, L("ESC 長押し", "Hold ESC", ""));
            EditorGUI.indentLevel--;
            float newHold = SliderField("ジェスチャー保持時間 [秒]", "gestureHoldDuration",
                "長押しジェスチャーの保持時間（秒）。VR両手トリガー / 右スティック上 / デスクトップESCに共通適用。",
                settings.gestureHoldDuration, 0.1f, 2f);
            float newHudThreshold = SliderField("ジェスチャーHUD表示猶予 [秒]", "gestureHudShowThreshold",
                "HUDプログレスを表示し始めるまでの猶予（秒）。",
                settings.gestureHudShowThreshold, 0f, 0.3f);
            Vector3 newHudVrOffset = EditorGUILayout.Vector3Field(
                L("HUD配置オフセット (VR)", "hudVrLocalOffset", "VR: 頭部ローカル座標における HUD オフセット（m）。(0,0,Z)で視界中央。"),
                settings.hudVrLocalOffset);
            CopyFieldNameMenu("hudVrLocalOffset");
            Vector3 newHudDesktopOffset = EditorGUILayout.Vector3Field(
                L("HUD配置オフセット (Desktop)", "hudDesktopLocalOffset", "デスクトップ: カメラローカル座標における HUD オフセット（m）。Z は前方距離。"),
                settings.hudDesktopLocalOffset);
            CopyFieldNameMenu("hudDesktopLocalOffset");

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
                "安定再生開始直後にドリフト積算を抑制する猶予時間（秒）。",
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
            EditorGUILayout.LabelField("Resync制御", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("同時接続制限");
            EditorGUI.indentLevel++;
            int newConcurrent = IntSliderField("同時Resync上限 [人]", "maxConcurrentResyncUsers",
                "同時Resync実行数の初期上限。",
                settings.maxConcurrentResyncUsers, 1, 100);
            int newConnLimit = IntSliderField("最大接続数", "maxConnectionLimit",
                "配信サーバへの総接続数上限の既定値。",
                settings.maxConnectionLimit, 1, 255);
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
                "リトライの基本待機時間（秒）。両系統の接続が失敗し続けると、この値からリトライ間隔倍率に従って増えていく（上限はリトライ間隔（上限））。レート制限による再接続待ちでは、倍増せずこの値で毎回待機する。",
                settings.baseCooldownSec, 5f, 180f);
            float newMultiplier = SliderField("リトライ間隔倍率", "retryCooldownMultiplier",
                "両系統失敗時に、連続失敗ごとのリトライ間隔へ掛ける倍率。1.0 で固定間隔、2.0 で倍々。",
                settings.retryCooldownMultiplier, 1f, 2f);
            float newMax = SliderField("リトライ間隔（上限） [秒]", "maxRetryCooldownSec",
                "倍率に従って増えるリトライ間隔の頭打ち値（秒）。両系統失敗時のバックオフがこの値を超えない。",
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

        private static void AutoAssignAudioLinkBehaviour(PlaybackSwitcher[] switchers)
        {
            if (switchers == null || switchers.Length == 0) return;

            GameObject audioLinkObject = GameObject.Find("AudioLink");
            if (audioLinkObject == null) return;

            UdonSharp.UdonSharpBehaviour[] candidates = audioLinkObject.GetComponents<UdonSharp.UdonSharpBehaviour>();
            UdonSharp.UdonSharpBehaviour audioLink = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == null) continue;
                if (candidates[i].GetType().Name == "AudioLink")
                {
                    audioLink = candidates[i];
                    break;
                }
                if (audioLink == null)
                    audioLink = candidates[i];
            }
            if (audioLink == null) return;

            for (int i = 0; i < switchers.Length; i++)
            {
                PlaybackSwitcher switcher = switchers[i];
                if (switcher == null) continue;

                var so = new SerializedObject(switcher);
                var prop = so.FindProperty("audioLinkBehaviour");
                if (prop == null || prop.objectReferenceValue != null) continue;

                Undo.RecordObject(switcher, "Auto Assign AudioLink Behaviour");
                prop.objectReferenceValue = audioLink;
                if (!so.ApplyModifiedProperties()) continue;

                UdonSharpEditorUtility.CopyProxyToUdon(switcher);
                EditorUtility.SetDirty(switcher);
                PrefabUtility.RecordPrefabInstancePropertyModifications(switcher);

                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(switcher);
                if (udon == null) continue;
                EditorUtility.SetDirty(udon);
                PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            }
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
