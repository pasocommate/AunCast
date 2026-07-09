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
    public partial class AunCastSettingsInspector
    {
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
            bool newAllowAudioOnlyFallback = ToggleField("音声のみ配信フォールバック", "Audio-only Fallback", "allowAudioOnlyFallback",
                "映像テクスチャが取得できなくても、再生時刻が前進している場合は音声のみ配信として切替を完了します。",
                "Completes the switch as audio-only when playback time advances but no video texture is available.",
                settings.allowAudioOnlyFallback);
            float newAudioOnlyFallbackDelay = SliderField("音声のみ判定待ち [秒]", "Audio-only Wait [s]", "audioOnlyFallbackDelaySec",
                "音声のみ配信として扱うまで、Standby の映像テクスチャ到着を待つ時間（秒）。",
                "Seconds to wait for the standby video texture before treating the stream as audio-only.",
                settings.audioOnlyFallbackDelaySec, 0f, 10f);
            float newAudioOnlyFallbackAdvance = SliderField("音声のみ前進判定 [秒]", "Audio-only Advance [s]", "audioOnlyFallbackMinAdvanceSec",
                "音声のみ配信として扱うために必要な Standby の再生時刻前進量（秒）。",
                "Required standby playback-time advance before treating the stream as audio-only.",
                settings.audioOnlyFallbackMinAdvanceSec, 0f, 3f);
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
            settings.allowAudioOnlyFallback = newAllowAudioOnlyFallback;
            settings.audioOnlyFallbackDelaySec = newAudioOnlyFallbackDelay;
            settings.audioOnlyFallbackMinAdvanceSec = newAudioOnlyFallbackAdvance;
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
                RewireEventBusAndConsumers(root, recordUndo: true, writeLog: false);
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
            var staffPanels = root.GetComponentsInChildren<AunCastStaffControlPanel>(true);
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
                settings.defaultUrl ?? "");
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
                "AunCastWallControlPanel の Staff ビュー解錠用 4 桁数字。空文字で無効。",
                "4-digit number to unlock the Staff view of the AunCastWallControlPanel. Empty to disable.",
                settings.wallUnlockPasscode);
            newUnlockPasscode = NormalizeWallUnlockPasscode(newUnlockPasscode);

            if (!EditorGUI.EndChangeCheck())
            {
                DrawStaffNamesField(root, settings);
                return;
            }

            Undo.RecordObject(settings, "Change AunCast UI Settings");
            settings.defaultVolume = newDefaultVolume;
            settings.defaultUrl = newDefaultUrl;
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

            EditorGUILayout.LabelField(AunCastEditorLocalization.Localize("無音Resync", "Silence Resync"));
            EditorGUI.indentLevel++;
            bool newAutoSilence = ToggleField("初期状態で有効", "Enabled by Default", "defaultAutoSilenceResync",
                "無音検知による自動Resync（各クライアントのローカルトグル）の初期値。オンで起動時に有効。",
                "Initial value of the silence-triggered auto Resync (each client's local toggle). On means enabled at startup.",
                settings.defaultAutoSilenceResync);
            EditorGUI.indentLevel--;

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
            settings.defaultAutoSilenceResync = newAutoSilence;
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
            var switchers = root.GetComponentsInChildren<AunCastPlaybackSwitcher>(true);
            ApplyToUdonComponents(switchers, so =>
            {
                SetFloatProperty(so, "crossfadeDurationSec", settings.crossfadeDurationSec);
                SetBoolProperty(so, "allowAudioOnlyFallback", settings.allowAudioOnlyFallback);
                SetFloatProperty(so, "audioOnlyFallbackDelaySec", settings.audioOnlyFallbackDelaySec);
                SetFloatProperty(so, "audioOnlyFallbackMinAdvanceSec", settings.audioOnlyFallbackMinAdvanceSec);
            });
        }

        internal static void ApplyUiSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var userPanels = root.GetComponentsInChildren<AunCastPortablePanel>(true);
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

            var overlays = root.GetComponentsInChildren<AunCastHudProgressOverlay>(true);
            ApplyToUdonComponents(overlays, so =>
            {
                SetFloatProperty(so, "showThreshold", settings.gestureHudShowThreshold);
                SetVector3Property(so, "vrLocalOffset", settings.hudVrLocalOffset);
                SetVector3Property(so, "desktopLocalOffset", settings.hudDesktopLocalOffset);
            });

            var wallPanels = FindSceneComponents<AunCastWallControlPanel>(root.gameObject.scene);
            ApplyToUdonComponents(wallPanels, so =>
            {
                SetFloatProperty(so, "wallNearDistance", settings.wallNearDistance);
                SetFloatProperty(so, "wallFarDistance", settings.wallFarDistance);
                SetStringProperty(so, "unlockPasscode", settings.wallUnlockPasscode);
            });

            var staffPanels = root.GetComponentsInChildren<AunCastStaffControlPanel>(true);
            ApplyToUdonComponents(staffPanels, so =>
            {
                SetStringArrayProperty(so, "allowedUserNames", settings.staffAllowedUserNames);
            });

            var controllers = root.GetComponentsInChildren<AunCastDualPlayerController>(true);
            ApplyToUdonComponents(controllers, so =>
            {
                SetFloatProperty(so, "defaultVolume", settings.defaultVolume);
                // VRCUrl は内部 string フィールド経由で設定する（VRCUrl は [Serializable] のインライン値型）
                var defaultUrlProp = so.FindProperty("defaultUrl");
                if (defaultUrlProp != null)
                {
                    var urlInner = defaultUrlProp.FindPropertyRelative("url");
                    if (urlInner != null)
                        urlInner.stringValue = settings.defaultUrl ?? "";
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

        internal static void ApplyPlaybackMonitorSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var detectors = FindSceneComponents<AunCastSpeaker>(root.gameObject.scene);
            ApplyToUdonComponents(detectors, so =>
            {
                SetFloatProperty(so, "silenceRmsThresholdDbfs", settings.silenceRmsThresholdDbfs);
                SetFloatProperty(so, "silenceConsecutiveSec", settings.silenceConsecutiveSec);
            });

            var monitors = root.GetComponentsInChildren<AunCastActivePlayerMonitor>(true);
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

            var clients = root.GetComponentsInChildren<AunCastResyncCoordinatorClient>(true);
            ApplyToUdonComponents(clients, so =>
            {
                SetFloatProperty(so, "silenceSuppressSec", settings.silenceSuppressSec);
            });

            var userPanels = root.GetComponentsInChildren<AunCastPortablePanel>(true);
            ApplyToUdonComponents(userPanels, so =>
            {
                SetFloatProperty(so, "silenceMeterPeakHoldSec", settings.silenceMeterPeakHoldSec);
                SetFloatProperty(so, "silenceMeterPeakDecayDbPerSec", settings.silenceMeterPeakDecayDbPerSec);
            });
        }

        internal static void ApplyResyncSettingsToScene(Transform root, PasocomMate.AunCast.AunCastSettings settings)
        {
            var controllers = root.GetComponentsInChildren<AunCastDualPlayerController>(true);
            ApplyToUdonComponents(controllers, so =>
            {
                SetBoolProperty(so, "_autoSilenceResyncEnabled", settings.defaultAutoSilenceResync);
            });

            var coordinators = root.GetComponentsInChildren<AunCastResyncCoordinator>(true);
            ApplyToUdonComponents(coordinators, so =>
            {
                SetByteProperty(so, "maxConcurrentResyncUsers", settings.maxConcurrentResyncUsers);
                SetByteProperty(so, "maxConnectionLimit", settings.maxConnectionLimit);
                SetFloatProperty(so, "grantTimeoutSec", settings.grantTimeoutSec);
                SetFloatProperty(so, "runningTimeoutSec", settings.runningTimeoutSec);
            });

            var clients = root.GetComponentsInChildren<AunCastResyncCoordinatorClient>(true);
            ApplyToUdonComponents(clients, so =>
            {
                SetFloatProperty(so, "resyncCycleTimeoutSec", settings.resyncCycleTimeoutSec);
                SetFloatProperty(so, "localCooldownSec", settings.localCooldownSec);
                SetFloatProperty(so, "baseCooldownSec", settings.baseCooldownSec);
                SetFloatProperty(so, "retryCooldownMultiplier", settings.retryCooldownMultiplier);
                SetFloatProperty(so, "maxRetryCooldownSec", settings.maxRetryCooldownSec);
            });

            // AunCastStaffControlPanel の数値表示/入力欄を実値へ揃える（Play せずとも見た目を一致させる）
            string concurrentVal = settings.maxConcurrentResyncUsers.ToString();
            string connectionVal = settings.maxConnectionLimit.ToString();
            var staffPanels = root.GetComponentsInChildren<AunCastStaffControlPanel>(true);
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
        // AunCastStaffControlPanel / AunCastPortablePanel / AunCastWallControlPanel が参照する素の UI
        // コンポーネント（TMP / Slider / Toggle）へ設定値を編集時にも反映し、Play せずとも
        // シーン上の表示を実値に揃える。

        // パネルが参照する UI コンポーネントを取得する。
        // プロキシ MonoBehaviour の SerializeField は AunCast.prefab にネストされた
        // AunCastWallControlPanel のようなネストプレハブのインスタンスでは編集時に参照が解決されず
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

            // フォールバック: 非ネストのプロキシ（AunCastPortablePanel 等）はこちらで取れる
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

        private static void SetBoolProperty(SerializedObject so, string fieldName, bool value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.boolValue = value;
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

        private static void AutoAssignAudioLinkBehaviour(AunCastPlaybackSwitcher[] switchers, bool recordUndo = true)
        {
            if (switchers == null || switchers.Length == 0) return;

            for (int i = 0; i < switchers.Length; i++)
            {
                AunCastPlaybackSwitcher switcher = switchers[i];
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

    }
}
#endif
