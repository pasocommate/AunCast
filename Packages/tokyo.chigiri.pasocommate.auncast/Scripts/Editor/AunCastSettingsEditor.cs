#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Video.Components.AVPro;

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

        private bool _prevAlt;
        private Texture2D _logo;

        private void DrawLogo()
        {
            if (_logo == null)
            {
                var path = AssetDatabase.GUIDToAssetPath(LOGO_GUID);
                if (!string.IsNullOrEmpty(path))
                    _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (_logo == null) return;
            }

            const float padV = 16f;
            const float maxLogoWidth = 320f;
            var rect = GUILayoutUtility.GetRect(0f, 0f);
            float fullWidth = EditorGUIUtility.currentViewWidth;
            float logoWidth = Mathf.Min(fullWidth - 80f, maxLogoWidth);
            float logoHeight = logoWidth * _logo.height / _logo.width;
            float totalHeight = logoHeight + padV * 2f;
            var bgRect = new Rect(0f, rect.y, fullWidth, totalHeight);
            GUILayoutUtility.GetRect(fullWidth, totalHeight);

            EditorGUI.DrawRect(bgRect, new Color(0.15f, 0.15f, 0.15f));
            float logoX = (fullWidth - logoWidth) / 2f;
            var logoRect = new Rect(logoX, bgRect.y + padV, logoWidth, logoHeight);
            GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);
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

        public override void OnInspectorGUI()
        {
            bool alt = Event.current != null && Event.current.alt;
            if (alt != _prevAlt)
            {
                _prevAlt = alt;
                Repaint();
            }

            DrawLogo();

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

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(settings, "Change AunCast UI Settings");
            settings.gestureHoldDuration = newHold;
            settings.gestureHudShowThreshold = newHudThreshold;
            settings.panelAutoDismissDistance = newDist;
            settings.panelOutOfSightDismissSec = newSight;
            settings.wallNearDistance = newNear;
            settings.wallFarDistance = Mathf.Max(newNear, newFar);
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
