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
        private void DrawWallPanelReferenceTools(Transform root)
        {
            using (new EditorGUILayout.VerticalScope(PaddedHelpBoxStyle()))
            {
                using (new EditorGUI.DisabledScope(root == null))
                {
                    if (GUILayout.Button(
                        AunCastEditorLocalization.Localize("参照関係を再配線", "Re-wire Reference Relationships"),
                        GUILayout.Height(24)))
                    {
                        RewireEventBusAndConsumers(root, recordUndo: true, writeLog: true);
                    }
                }

                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        "AunCast 配下の中核参照と､ 同一シーン全体の AunCastScreen / AunCastUiScreen / AunCastSpeaker / AunCastAudioOutputTunnel / AunCastWallControlPanel を再スキャンして再配線します｡",
                        "Re-scans and re-wires core AunCast references plus all AunCastScreen / AunCastUiScreen / AunCastSpeaker / AunCastAudioOutputTunnel / AunCastWallControlPanel components in the same scene."),
                    MessageType.None);
            }
        }

        internal static void RewireEventBusAndConsumers(Transform root, bool recordUndo, bool writeLog)
        {
            if (root == null) return;

            var controller = GetRuntimeComponentInChildren<AunCastDualPlayerController>(root);
            var staffPanel = GetRuntimeComponentInChildren<AunCastStaffControlPanel>(root);
            var portablePanel = GetRuntimeComponentInChildren<AunCastPortablePanel>(root);
            var settings = root.GetComponent<PasocomMate.AunCast.AunCastSettings>();
            var idleScreenTexture = settings != null ? settings.idleScreenTexture : null;
            Scene scene = root.gameObject.scene;
            var eventBus = FindOrCreateEventBus(root, createIfMissing: recordUndo, writeLog: writeLog);
            var switchers = GetRuntimeComponentsInChildren<AunCastPlaybackSwitcher>(root);
            AutoAssignAudioLinkBehaviour(switchers, recordUndo);
            // AudioLink 付属の内蔵スピーカー（AudioLinkInput）を無効化＋EditorOnly 化する。
            // AunCast が AudioLink 入力をランタイムで差し替えるため付属スピーカーは不要（冪等）。
            int audioLinkInputNeutralized = NeutralizeAudioLinkInputs(scene, recordUndo);
            int editorOnlySkipped = 0;
            int skippedMeshScreens;
            int skippedUiScreens;
            int skippedSpeakers;
            int skippedTunnels;
            int skippedWallPanels;
            var meshScreens = FindSceneComponents<AunCastScreen>(scene, true, out skippedMeshScreens);
            var uiScreens = FindSceneComponents<AunCastUiScreen>(scene, true, out skippedUiScreens);
            var speakers = FindSceneComponents<AunCastSpeaker>(scene, true, out skippedSpeakers);
            var audioOutputTunnels = FindSceneComponents<AunCastAudioOutputTunnel>(scene, true, out skippedTunnels);
            var wallPanels = FindSceneComponents<AunCastWallControlPanel>(scene, true, out skippedWallPanels);
            editorOnlySkipped += skippedMeshScreens + skippedUiScreens + skippedSpeakers + skippedTunnels + skippedWallPanels;
            var userPanels = GetRuntimeComponentsInChildren<AunCastPortablePanel>(root);

            if (controller == null || staffPanel == null || portablePanel == null)
            {
                if (writeLog)
                    Debug.LogWarning("[AunCast] 再配線を中止しました。AunCastDualPlayerController / AunCastStaffControlPanel / AunCastPortablePanel のいずれかが見つかりません。");
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
                    if (changed && ApplyUdonSerializedChanges(switcher, so, "Rewire AunCastPlaybackSwitcher EventBus", recordUndo))
                        publisherUpdated++;
                }

                var controllers = GetRuntimeComponentsInChildren<AunCastDualPlayerController>(root);
                foreach (var ctrl in controllers)
                {
                    if (ctrl == null) continue;
                    var so = new SerializedObject(ctrl);
                    bool changed = SetObjectProperty(so, "eventBus", eventBus);
                    if (changed && ApplyUdonSerializedChanges(ctrl, so, "Rewire AunCastDualPlayerController EventBus", recordUndo))
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

                if (changed && ApplyUdonSerializedChanges(wall, so, "Rewire AunCastWallControlPanel References", recordUndo))
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

                    if (changed && ApplyUdonSerializedChanges(user, so, "Rewire AunCastPortablePanel EventBus", recordUndo))
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

            // Core→UI の通知先（staffNotifyTarget）を AunCastStaffControlPanel へ配線する。
            // SendCustomEvent による通知のため、フィールドは UdonSharpBehaviour 基底型で受ける。
            int notifyUpdated = 0;
            if (staffPanel != null)
            {
                foreach (var ctrl in GetRuntimeComponentsInChildren<AunCastDualPlayerController>(root))
                {
                    if (ctrl == null) continue;
                    var so = new SerializedObject(ctrl);
                    if (SetObjectProperty(so, "staffNotifyTarget", staffPanel)
                        && ApplyUdonSerializedChanges(ctrl, so, "Rewire AunCastDualPlayerController StaffNotifyTarget", recordUndo))
                        notifyUpdated++;
                }
                foreach (var coord in GetRuntimeComponentsInChildren<AunCastResyncCoordinator>(root))
                {
                    if (coord == null) continue;
                    var so = new SerializedObject(coord);
                    if (SetObjectProperty(so, "staffNotifyTarget", staffPanel)
                        && ApplyUdonSerializedChanges(coord, so, "Rewire AunCastResyncCoordinator StaffNotifyTarget", recordUndo))
                        notifyUpdated++;
                }
                foreach (var monitor in GetRuntimeComponentsInChildren<AunCastPlaybackMonitor>(root))
                {
                    if (monitor == null) continue;
                    var so = new SerializedObject(monitor);
                    if (SetObjectProperty(so, "staffNotifyTarget", staffPanel)
                        && ApplyUdonSerializedChanges(monitor, so, "Rewire AunCastPlaybackMonitor StaffNotifyTarget", recordUndo))
                        notifyUpdated++;
                }
            }

            int speakerUpdated = 0;
            int tunnelUpdated = 0;
            int sinkMutedUpdated = 0;
            if (TryResolveSpeakerSetupContext(root, out var speakerContext, out _) && speakers != null)
            {
                speakerUpdated = RewireDeclaredSpeakers(settings, speakerContext, speakers, recordUndo, writeLog);
                AudioSource fallbackTunnelInputA = FindFirstSpeakerSource(speakers, AunCastSpeaker.PLAYER_A);
                AudioSource fallbackTunnelInputB = FindFirstSpeakerSource(speakers, AunCastSpeaker.PLAYER_B);
                tunnelUpdated = RewireAudioOutputTunnels(audioOutputTunnels, fallbackTunnelInputA, fallbackTunnelInputB, recordUndo);

                // トンネル入力は「トンネル出力」経由でのみ鳴らすため、シンク自身は不可聴化する。
                // GetOutputData は volume の影響を受ける（＝トンネルが読む信号は volume 反映済み）ため、
                // volume ではなく空間ロールオフを 0 にして GetOutputData を保ったまま直接音だけを消す。
                if (audioOutputTunnels != null && audioOutputTunnels.Length > 0)
                    sinkMutedUpdated = MakeAudioOutputTunnelInputsInaudible(audioOutputTunnels, recordUndo);
            }

            if (writeLog && editorOnlySkipped > 0)
                Debug.Log($"[AunCast] EditorOnly 階層のコンポーネント {editorOnlySkipped} 件は、ビルド時に除外されるため再配線対象から外しました。");

            if (writeLog)
                Debug.Log($"[AunCast] EventBus参照を再配線しました。Bus: {busUpdated}件 / Publisher: {publisherUpdated}件 / AunCastWallControlPanel: {wallUpdated}件 / AunCastPortablePanel: {userUpdated}件 / Screen: {screenUpdated}件 / Speaker: {speakerUpdated}件 / Tunnel: {tunnelUpdated}件 / シンク不可聴化: {sinkMutedUpdated}件 / AudioLink入力無効化: {audioLinkInputNeutralized}件 / 通知先: {notifyUpdated}件");
        }

        private static AunCastEventBus FindOrCreateEventBus(Transform root, bool createIfMissing, bool writeLog)
        {
            if (root == null) return null;

            // 正規名 "EventBus" 直下を最優先で探しつつ、見つからなければ型ベースでも探索し、
            // 旧名 "AunCastEventHub" や Unpack 後にリネーム/移動されたバスも拾う。
            // 名前一致のみに頼ると既存シーンで二重生成・旧バス孤立が起きるため、型で補完する。
            Transform eventBusTransform = root.Find(EVENT_BUS_OBJECT_NAME);
            bool namedEventBusIsRuntime = eventBusTransform != null
                && !IsInEditorOnlyHierarchy(eventBusTransform.gameObject);
            AunCastEventBus existingEventBus = namedEventBusIsRuntime
                ? eventBusTransform.gameObject.GetComponent<AunCastEventBus>()
                : null;
            if (existingEventBus == null)
                existingEventBus = GetRuntimeComponentInChildren<AunCastEventBus>(root);

            GameObject eventBusObject;
            if (existingEventBus != null)
                eventBusObject = existingEventBus.gameObject;
            else if (namedEventBusIsRuntime)
                eventBusObject = eventBusTransform.gameObject;
            else
            {
                if (!createIfMissing) return null;
                eventBusObject = new GameObject(EVENT_BUS_OBJECT_NAME);
                Undo.RegisterCreatedObjectUndo(eventBusObject, "Create EventBus");
                eventBusObject.transform.SetParent(root, false);
            }

            if (existingEventBus != null)
            {
                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(existingEventBus) != null)
                {
                    if (createIfMissing) EnsureCanonicalEventBusName(eventBusObject);
                    return existingEventBus;
                }

                if (!createIfMissing)
                    return null;

                if (writeLog)
                    Debug.LogWarning("[AunCast] backing UdonBehaviour のない AunCastEventBus を検出したため作り直します。", existingEventBus);
                UdonSharpUndo.DestroyImmediate(existingEventBus);
            }

            if (!createIfMissing) return null;

            EnsureCanonicalEventBusName(eventBusObject);

            if (AunCastEditorAssetUtility.LoadAssetByGuid<UnityEngine.Object>(AUNCAST_EVENT_BUS_ASSET_GUID) == null)
            {
                if (writeLog)
                    Debug.LogWarning("[AunCast] AunCastEventBus.asset が見つからないため、AunCastEventBus を作成できません。");
                return null;
            }

            var eventBus = UdonSharpUndo.AddComponent<AunCastEventBus>(eventBusObject);
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

        // 型ベースで拾った旧名/リネーム済みのバスを正規名へ揃える（次回以降の名前検索を安定させ、二重生成を防ぐ）。
        private static void EnsureCanonicalEventBusName(GameObject eventBusObject)
        {
            if (eventBusObject == null) return;
            if (eventBusObject.name == EVENT_BUS_OBJECT_NAME) return;
            Undo.RecordObject(eventBusObject, "Rename EventBus");
            eventBusObject.name = EVENT_BUS_OBJECT_NAME;
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

        private static VRC.Udon.UdonBehaviour[] ToUdonSubscribers(AunCastWallControlPanel[] wallPanels)
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
            if (IsInEditorOnlyHierarchy(component.gameObject)) return;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(component);
            if (udon != null)
                subscribers.Add(udon);
        }

        private static T[] FindSceneComponents<T>(Scene scene) where T : Component
        {
            int unused;
            return FindSceneComponents<T>(scene, false, out unused);
        }

        private static T[] FindSceneComponents<T>(
            Scene scene,
            bool excludeEditorOnly,
            out int excludedEditorOnly) where T : Component
        {
            excludedEditorOnly = 0;
            if (!scene.IsValid()) return Array.Empty<T>();

            T[] all = UnityEngine.Object.FindObjectsOfType<T>(true);
            var list = new List<T>();
            for (int i = 0; i < all.Length; i++)
            {
                T component = all[i];
                if (component == null) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != scene) continue;
                if (excludeEditorOnly && IsInEditorOnlyHierarchy(component.gameObject))
                {
                    excludedEditorOnly++;
                    continue;
                }
                list.Add(component);
            }

            list.Sort((a, b) => string.Compare(
                GetHierarchyPath(a != null ? a.transform : null),
                GetHierarchyPath(b != null ? b.transform : null),
                StringComparison.Ordinal));
            return list.ToArray();
        }

        private static T GetRuntimeComponentInChildren<T>(Transform root) where T : Component
        {
            T[] components = GetRuntimeComponentsInChildren<T>(root);
            return components.Length > 0 ? components[0] : null;
        }

        private static T[] GetRuntimeComponentsInChildren<T>(Transform root) where T : Component
        {
            if (root == null) return Array.Empty<T>();
            T[] all = root.GetComponentsInChildren<T>(true);
            var list = new List<T>();
            for (int i = 0; i < all.Length; i++)
            {
                T component = all[i];
                if (component == null) continue;
                if (IsInEditorOnlyHierarchy(component.gameObject)) continue;
                list.Add(component);
            }
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
                if (IsInEditorOnlyHierarchy(speaker.gameObject)) continue;

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
                if (IsInEditorOnlyHierarchy(speaker.gameObject)) continue;
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
                if (IsInEditorOnlyHierarchy(tunnel.gameObject)) continue;

                var so = new SerializedObject(tunnel);
                bool changed = false;
                if (ReadAudioSourceProperty(tunnel, "inputA") == null)
                    changed |= SetObjectProperty(so, "inputA", inputA);
                if (ReadAudioSourceProperty(tunnel, "inputB") == null)
                    changed |= SetObjectProperty(so, "inputB", inputB);
                if (ResolveTunnelDelegate(tunnel) == null)
                {
                    Component delegateTunnel = FindAudioOutputTunnelInAncestors(tunnel.gameObject);
                    if (delegateTunnel != null)
                        changed |= SetObjectProperty(so, "targetTunnel", delegateTunnel);
                    else
                        Debug.LogWarning(
                            "[AunCast] AunCastAudioOutputTunnel の委譲先 AudioOutputTunnel が見つかりません｡ AunCastAudioOutputTunnel を AudioOutputTunnel と同じ GameObject または子に配置するか､ targetTunnel を手動で設定してください｡",
                            tunnel);
                }
                if (changed && ApplyUdonSerializedChanges(tunnel, so, "Rewire AunCastAudioOutputTunnel Inputs", recordUndo))
                    updated++;
            }

            return updated;
        }

        /// <summary>
        /// 自身または祖先の GameObject 上から委譲先候補の旧 AudioOutputTunnel を探す。
        /// 移行処理はビルド済み Network ID の署名を変えないよう、旧トンネルの子 GameObject に
        /// AunCastAudioOutputTunnel を配置するため、親方向を探索する。
        /// </summary>
        private static Component FindAudioOutputTunnelInAncestors(GameObject gameObject)
        {
            Transform current = gameObject != null ? gameObject.transform : null;
            while (current != null)
            {
                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (IsAudioOutputTunnelComponent(components[i]))
                        return components[i];
                }
                current = current.parent;
            }
            return null;
        }

        private static int MakeAudioOutputTunnelInputsInaudible(
            AunCastAudioOutputTunnel[] tunnels,
            bool recordUndo)
        {
            if (tunnels == null || tunnels.Length == 0) return 0;

            int updated = 0;
            var used = new HashSet<int>();
            for (int i = 0; i < tunnels.Length; i++)
            {
                AunCastAudioOutputTunnel tunnel = tunnels[i];
                if (tunnel == null || IsInEditorOnlyHierarchy(tunnel.gameObject)) continue;
                updated += MakeUniqueTunnelInputInaudible(ReadAudioSourceProperty(tunnel, "inputA"), used, recordUndo);
                updated += MakeUniqueTunnelInputInaudible(ReadAudioSourceProperty(tunnel, "inputB"), used, recordUndo);
            }

            return updated;
        }

        private static int MakeUniqueTunnelInputInaudible(
            AudioSource source,
            HashSet<int> used,
            bool recordUndo)
        {
            if (source == null) return 0;
            int instanceId = source.GetInstanceID();
            if (used != null && !used.Add(instanceId)) return 0;
            return MakeTunnelInputInaudible(source, recordUndo) ? 1 : 0;
        }

    }
}
#endif
