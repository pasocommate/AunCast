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
                                "同一シーンの VRCAVProVideoScreen / VRCAVProVideoSpeaker を検出し､ 各行の変換ボタンで AunCastScreen / AunCastSpeaker に変換します｡\n変換を始める前にシーンのバックアップを取ることをおすすめします｡",
                                "Detects VRCAVProVideoScreen / VRCAVProVideoSpeaker components in the same scene and converts each row to AunCastScreen / AunCastSpeaker with its Convert button.\nWe recommend backing up your scene before starting the conversion."),
                            MessageType.None);

                        if (cache.detected)
                        {
                            EditorGUILayout.HelpBox(
                                AunCastEditorLocalization.Localize(
                                    "スピーカーは A/B 再生系統それぞれに 1 台ずつ必要です｡ 「設定済み (A)」と「設定済み (B)」は､ 同じ位置･同じ設定のスピーカーを 2 台用意し､ それぞれを PlayerA / PlayerB 用として設定済みであることを示します｡",
                                    "Speakers are needed once for each A/B playback path. Configured (A) and Configured (B) mean that two speakers with the same position and settings are prepared and assigned to Player A / Player B."),
                                MessageType.Info);

                            MigrationCandidate[] candidates = cache.candidates ?? Array.Empty<MigrationCandidate>();
                            DrawMigrationCandidateList(root, settings, context, cache, candidates);

                            List<MigrationValidationIssue> validationErrors = cache.validationErrors ?? new List<MigrationValidationIssue>();
                            for (int i = 0; i < validationErrors.Count; i++)
                                DrawSelectableValidationIssue(validationErrors[i]);
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
                        "変換候補では扱わない旧 VRCAVProVideoPlayer 本体です｡ AunCast への移行後に不要であれば手動で削除してください｡",
                        "Legacy VRCAVProVideoPlayer components that are not handled by conversion candidates. Delete them manually if they are no longer needed after migrating to AunCast."),
                    MessageType.Warning);
                for (int i = 0; i < residual.Length; i++)
                    DrawSelectableCleanupCandidate(residual[i]);
            }
        }

        private static void DrawSelectableCleanupCandidate(ResidualCleanupCandidate candidate)
        {
            DrawSelectableObjectPathWithComponentName(
                candidate.gameObject,
                candidate.componentName,
                candidate.hierarchyPath);
        }

        private static void DrawSelectableObjectPathWithComponentName(
            GameObject gameObject,
            string componentName,
            string hierarchyPath)
        {
            DrawSelectablePathWithComponentName(
                componentName,
                hierarchyPath,
                gameObject,
                gameObject,
                AunCastEditorLocalization.Localize(
                    "クリックでこのオブジェクトを選択",
                    "Click to select this object"));
        }

        private static void DrawSelectablePathWithComponentName(
            string componentName,
            string path,
            UnityEngine.Object selectionTarget,
            UnityEngine.Object pingTarget,
            string tooltip)
        {
            if (string.IsNullOrEmpty(componentName))
                componentName = AunCastEditorLocalization.Localize("コンポーネント", "Component");
            if (string.IsNullOrEmpty(path))
                path = "-";

            GUIStyle boldStyle = EditorStyles.boldLabel;
            GUIStyle linkStyle = LinkPathStyle();
            var pathContent = new GUIContent(path, tooltip);

            float labelWidth = boldStyle.CalcSize(new GUIContent(componentName)).x;
            float colonWidth = EditorStyles.label.CalcSize(new GUIContent(": ")).x;
            float width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 96f);
            float pathWidth = Mathf.Max(80f, width - labelWidth - colonWidth);
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight, linkStyle.CalcHeight(pathContent, pathWidth));
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect colonRect = new Rect(labelRect.xMax, rect.y, colonWidth, EditorGUIUtility.singleLineHeight);
            Rect linkRect = new Rect(colonRect.xMax, rect.y, Mathf.Max(80f, rect.xMax - colonRect.xMax), height);

            GUI.Label(labelRect, componentName, boldStyle);
            GUI.Label(colonRect, ": ", EditorStyles.label);
            EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);
            using (new EditorGUI.DisabledScope(selectionTarget == null))
            {
                if (GUI.Button(linkRect, pathContent, linkStyle))
                {
                    Selection.activeObject = selectionTarget;
                    EditorGUIUtility.PingObject(pingTarget != null ? pingTarget : selectionTarget);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private bool DrawReferenceWarning(
            Transform root,
            SpeakerSetupContext context,
            MigrationCandidateCache cache,
            ReferenceWarning warning)
        {
            if (!HasReferenceWarning(warning)) return false;

            if (warning.components == null || warning.components.Length == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(warning.message, WrappedMiniLabelStyle());
                    if (DrawClearAudioLinkReferencesButton(warning))
                    {
                        RefreshMigrationCandidates(root, context, cache);
                        Repaint();
                        GUIUtility.ExitGUI();
                        return true;
                    }
                }
                return false;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(warning.message, WrappedMiniLabelStyle());
                if (DrawClearAudioLinkReferencesButton(warning))
                {
                    RefreshMigrationCandidates(root, context, cache);
                    Repaint();
                    GUIUtility.ExitGUI();
                    return true;
                }

                Component[] components = warning.components;
                for (int i = 0; components != null && i < components.Length; i++)
                {
                    DrawSelectableComponentPath(components[i]);
                }
            }

            return false;
        }

        private static bool HasReferenceWarning(ReferenceWarning warning)
        {
            return !string.IsNullOrEmpty(warning.message);
        }

        private static bool DrawClearAudioLinkReferencesButton(ReferenceWarning warning)
        {
            if (warning.audioLinkComponents == null || warning.audioLinkComponents.Length == 0)
                return false;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        AunCastEditorLocalization.Localize("AudioLink参照を解除", "Clear AudioLink Reference"),
                        GUILayout.Width(150f),
                        GUILayout.Height(22)))
                {
                    ClearAudioLinkAudioSourceReferences(warning.audioLinkComponents);
                    return true;
                }
            }

            return false;
        }

        private static void DrawSelectableValidationIssue(MigrationValidationIssue issue)
        {
            if (string.IsNullOrEmpty(issue.message)) return;
            if (!issue.selectable || issue.selectionTarget == null)
            {
                EditorGUILayout.HelpBox(issue.message, MessageType.Error);
                return;
            }

            if (!string.IsNullOrEmpty(issue.linkText))
            {
                DrawValidationIssueWithLinkedPath(issue);
                return;
            }

            GUIStyle linkStyle = LinkPathStyle();
            float width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 72f);
            float textWidth = Mathf.Max(80f, width - 42f);
            float height = Mathf.Max(40f, linkStyle.CalcHeight(new GUIContent(issue.message), textWidth) + 18f);
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.HelpBox(rect, string.Empty, MessageType.Error);
            Rect textRect = new Rect(rect.x + 38f, rect.y + 9f, Mathf.Max(80f, rect.width - 46f), height - 12f);
            GUI.Label(textRect, issue.message, linkStyle);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !rect.Contains(current.mousePosition))
                return;

            Selection.activeObject = issue.selectionTarget;
            EditorGUIUtility.PingObject(issue.pingTarget != null ? issue.pingTarget : issue.selectionTarget);
            current.Use();
        }

        private static void DrawValidationIssueWithLinkedPath(MigrationValidationIssue issue)
        {
            GUIStyle messageStyle = WrappedMiniLabelStyle();
            GUIStyle linkStyle = LinkPathStyle();
            var messageContent = new GUIContent(issue.message);
            var linkContent = new GUIContent(issue.linkText);
            float width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 72f);
            float contentWidth = Mathf.Max(80f, width - 46f);
            float messageHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, messageStyle.CalcHeight(messageContent, contentWidth));
            float linkHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, linkStyle.CalcHeight(linkContent, contentWidth));
            float height = Mathf.Max(52f, messageHeight + linkHeight + 20f);
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.HelpBox(rect, string.Empty, MessageType.Error);

            Rect messageRect = new Rect(rect.x + 38f, rect.y + 8f, contentWidth, messageHeight);
            Rect linkRect = new Rect(rect.x + 38f, messageRect.yMax + 2f, contentWidth, linkHeight);
            GUI.Label(messageRect, issue.message, messageStyle);
            GUI.Label(linkRect, issue.linkText, linkStyle);
            EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !linkRect.Contains(current.mousePosition))
                return;

            Selection.activeObject = issue.selectionTarget;
            EditorGUIUtility.PingObject(issue.pingTarget != null ? issue.pingTarget : issue.selectionTarget);
            current.Use();
        }

        private static int ClearAudioLinkAudioSourceReferences(Component[] components)
        {
            if (components == null || components.Length == 0) return 0;

            int changed = 0;
            var used = new HashSet<int>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                if (!used.Add(component.GetInstanceID())) continue;
                if (ClearAudioLinkAudioSourceReference(component))
                    changed++;
            }

            return changed;
        }

        private static bool ClearAudioLinkAudioSourceReference(Component component)
        {
            if (!IsAudioLinkReferenceComponent(component)) return false;

            bool changed = false;
            changed |= ClearAudioLinkUdonPublicVariable(component);
            changed |= ClearAudioLinkSerializedProperty(component);
            return changed;
        }

        private static bool ClearAudioLinkUdonPublicVariable(Component component)
        {
            if (!(component is UdonSharp.UdonSharpBehaviour behaviour)) return false;

            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (udon == null || udon.publicVariables == null) return false;
            if (!udon.publicVariables.TryGetVariableValue(AUDIO_LINK_AUDIO_SOURCE_VARIABLE_NAME, out object value))
                return false;
            if (value == null) return false;

            Undo.RecordObject(udon, "Clear AudioLink AudioSource Reference");
            udon.publicVariables.TrySetVariableValue(AUDIO_LINK_AUDIO_SOURCE_VARIABLE_NAME, (AudioSource)null);
            EditorUtility.SetDirty(udon);
            PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            return true;
        }

        private static bool ClearAudioLinkSerializedProperty(Component component)
        {
            if (component == null) return false;

            var so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(AUDIO_LINK_AUDIO_SOURCE_VARIABLE_NAME);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            if (prop.objectReferenceValue == null) return false;

            Undo.RecordObject(component, "Clear AudioLink AudioSource Reference");
            prop.objectReferenceValue = null;
            if (!so.ApplyModifiedProperties()) return false;

            if (component is UdonSharp.UdonSharpBehaviour behaviour && HasConfiguredBackingUdonBehaviour(behaviour))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
                if (udon != null)
                {
                    EditorUtility.SetDirty(udon);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
                }
            }

            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            return true;
        }

        private static void DrawSelectableComponentPath(Component component)
        {
            if (component == null)
                return;

            DrawSelectablePathWithComponentName(
                component.GetType().Name,
                GetHierarchyPath(component.transform),
                component,
                component,
                AunCastEditorLocalization.Localize(
                    "クリックでこのコンポーネントを選択",
                    "Click to select this component"));
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
                if (candidate.kind == MigrationCandidateKind.Speaker
                    && !cache.speakerPlayerIndex.ContainsKey(candidate.selectionKey))
                    cache.speakerPlayerIndex[candidate.selectionKey] = candidate.inferredPlayerIndex;
                if (candidate.kind == MigrationCandidateKind.AudioOutputTunnel
                    && !cache.tunnelMigrationMode.ContainsKey(candidate.selectionKey))
                    cache.tunnelMigrationMode[candidate.selectionKey] = TUNNEL_MIGRATION_MODE_COMPAT_TUNNEL;
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
                        "変換対象の AVPro スクリーン / スピーカー / AudioOutputTunnel は見つかりませんでした｡",
                        "No AVPro screens / speakers / AudioOutputTunnel components were found for conversion."),
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
                    DrawMigrationCandidateHeader(candidate);
                    if (IsInEditorOnlyHierarchy(candidate.gameObject))
                    {
                        EditorGUILayout.HelpBox(
                            AunCastEditorLocalization.Localize(
                                "EditorOnly タグが付いたオブジェクト､ またはその配下の出力です｡ ビルド時に除外されるため､ 実際に AunCast の出力として使う場合は EditorOnly タグを外してください｡",
                                "This output is on an object tagged EditorOnly or under one. It will be stripped from builds; remove the EditorOnly tag if this output should be used by AunCast."),
                            MessageType.Warning);
                    }
                    bool isBundledAunCastOutput = IsBundledAunCastOutputCandidate(root, candidate.gameObject, candidate.hierarchyPath);
                    if (isBundledAunCastOutput)
                    {
                        EditorGUILayout.HelpBox(
                            AunCastEditorLocalization.Localize(
                                "AunCast プレハブに元から含まれる出力です｡ 既存ワールド側の出力を使う場合､ この出力は不要なことがあります｡ 不要であれば「削除」ボタンを押して GameObject を削除してください｡",
                                "This output is included in the AunCast prefab. If you use outputs from an existing world setup, this output may be unnecessary. Use the Delete button to delete the GameObject if you do not need it."),
                            MessageType.Info);
                    }
                    bool showStatusInBody = !string.IsNullOrEmpty(candidate.statusLabel)
                        && candidate.kind != MigrationCandidateKind.AudioOutputTunnel
                        && !(candidate.kind == MigrationCandidateKind.Speaker && candidate.isConfigured);
                    if (showStatusInBody)
                        GUILayout.Label(candidate.statusLabel, WrappedMiniLabelStyle());
                    if (candidate.kind != MigrationCandidateKind.AudioOutputTunnel)
                        DrawReferenceWarning(root, context, cache, candidate.referenceWarning);

                    if (candidate.kind == MigrationCandidateKind.AudioOutputTunnel && !candidate.isConfigured)
                    {
                        int currentMode = cache.tunnelMigrationMode.TryGetValue(candidate.selectionKey, out int mode)
                            ? mode
                            : TUNNEL_MIGRATION_MODE_COMPAT_TUNNEL;
                        int nextMode;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(
                                AunCastEditorLocalization.Localize("移行方法", "Migration"),
                                GUILayout.Width(72));
                            nextMode = EditorGUILayout.Popup(
                                currentMode,
                                new[]
                                {
                                    AunCastEditorLocalization.Localize("互換トンネルへ移行", "Migrate to compatibility tunnel"),
                                    AunCastEditorLocalization.Localize("出力AudioSourceをスピーカー化", "Convert output AudioSources"),
                                },
                                GUILayout.MinWidth(180));
                            cache.tunnelMigrationMode[candidate.selectionKey] = nextMode;
                            DrawMigrationActionButton(root, settings, context, cache, candidate);
                        }

                        EditorGUILayout.HelpBox(
                            nextMode == TUNNEL_MIGRATION_MODE_DIRECT_SPEAKERS
                                ? AunCastEditorLocalization.Localize(
                                    "旧 AudioOutputTunnel を使わず､ leftOutput / rightOutput / stereoOutput の AudioSource を通常の AunCast スピーカーとして変換します｡ 各出力は A/B 用に複製されます｡ ",
                                    "Does not use the old AudioOutputTunnel. Converts the leftOutput / rightOutput / stereoOutput AudioSources as normal AunCast speakers, duplicating each output for A/B.")
                                : AunCastEditorLocalization.Localize(
                                    "旧 AudioOutputTunnel は削除せず温存し､ その input を A/B の可聴側へ切替える AunCastAudioOutputTunnel を追加します｡ 入力側 AudioSource は A/B 用に複製して AunCastSpeaker として設定します｡ これは直結出力に比べて遅延がリングバッファ分だけ多い構成です｡ ",
                                    "Keeps the old AudioOutputTunnel in place and adds an AunCastAudioOutputTunnel that switches its input to the audible A/B side. The input AudioSource is duplicated as A/B AunCastSpeakers. This setup has one ring buffer's worth of latency compared with direct output."),
                            MessageType.None);

                        if (nextMode == TUNNEL_MIGRATION_MODE_DIRECT_SPEAKERS)
                        {
                            Component tunnelForFilter = GetAudioOutputTunnelCandidateComponent(candidate);
                            if (tunnelForFilter != null && HasAudioFiltersOnTunnelOutputs(tunnelForFilter))
                            {
                                EditorGUILayout.HelpBox(
                                    AunCastEditorLocalization.Localize(
                                        "出力 AudioSource にオーディオフィルター (ReverbFilter 等) が付いています｡ VRCAVProVideoSpeaker が非対応のため､ スピーカー化時に自動で削除されます｡",
                                        "Output AudioSources have audio filters (e.g. ReverbFilter). These are not supported by VRCAVProVideoSpeaker and will be removed automatically during conversion."),
                                    MessageType.Warning);
                            }

                            if (HasReferenceWarning(candidate.referenceWarning))
                                DrawReferenceWarning(root, context, cache, candidate.referenceWarning);
                        }
                    }
                    else if (candidate.kind == MigrationCandidateKind.AudioOutputTunnel && candidate.isConfigured)
                    {
                        AunCastAudioOutputTunnel migratedTunnel = GetAunCastAudioOutputTunnelCandidateComponent(candidate);
                        if (migratedTunnel != null && ResolveTunnelDelegate(migratedTunnel) == null)
                        {
                            EditorGUILayout.HelpBox(
                                AunCastEditorLocalization.Localize(
                                    "委譲先の AudioOutputTunnel が設定されていません｡ 同じ GameObject に AudioOutputTunnel (TopazChat Player 付属) を配置して「参照関係を再配線」を実行するか､ targetTunnel を手動で設定してください｡",
                                    "The delegation target AudioOutputTunnel is not assigned. Place an AudioOutputTunnel (bundled with TopazChat Player) on the same GameObject and run Rewire References, or assign targetTunnel manually."),
                                MessageType.Error);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            DrawConfiguredButton(candidate);
                            DrawMigratedTunnelDirectOutputButton(root, settings, context, cache, candidate);
                        }

                        EditorGUILayout.HelpBox(
                            AunCastEditorLocalization.Localize(
                                "直結化すると､ 委譲先 AudioOutputTunnel の出力先 AudioSource を A/B 用の AunCastSpeaker に変換し､ AunCastAudioOutputTunnel と委譲先 AudioOutputTunnel を削除します｡ トンネルによる出力合成機能は失われますが､ リングバッファ由来の遅延は解消されます｡ ",
                                "Direct Output converts the delegated AudioOutputTunnel output AudioSources into A/B AunCastSpeaker outputs and removes both AunCastAudioOutputTunnel and the delegated AudioOutputTunnel. Tunnel output mixing is lost, but ring-buffer latency is removed."),
                            MessageType.Warning);
                        {
                            Component tunnelForFilter = GetAunCastAudioOutputTunnelCandidateComponent(candidate);
                            if (tunnelForFilter != null && HasAudioFiltersOnTunnelOutputs(tunnelForFilter))
                            {
                                EditorGUILayout.HelpBox(
                                    AunCastEditorLocalization.Localize(
                                        "出力 AudioSource にオーディオフィルター (ReverbFilter 等) が付いています｡ VRCAVProVideoSpeaker が非対応のため､ 直結化時に自動で削除されます｡",
                                        "Output AudioSources have audio filters (e.g. ReverbFilter). These are not supported by VRCAVProVideoSpeaker and will be removed automatically during direct output conversion."),
                                    MessageType.Warning);
                            }
                        }
                        DrawReferenceWarning(root, context, cache, candidate.referenceWarning);
                    }
                    else if (candidate.kind == MigrationCandidateKind.Speaker && !candidate.isConfigured)
                    {
                        int current = cache.speakerPlayerIndex.TryGetValue(candidate.selectionKey, out int value)
                            ? value
                            : candidate.inferredPlayerIndex;
                        int next;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(
                                AunCastEditorLocalization.Localize("接続先", "Player"),
                                GUILayout.Width(72));
                            next = EditorGUILayout.Popup(
                                current,
                                new[]
                                {
                                    AunCastEditorLocalization.Localize("PlayerA", "Player A"),
                                    AunCastEditorLocalization.Localize("PlayerB", "Player B"),
                                    AunCastEditorLocalization.Localize("自動複製 (A/B)", "Auto-duplicate (A/B)"),
                                },
                                GUILayout.MinWidth(180));
                            cache.speakerPlayerIndex[candidate.selectionKey] = next;
                            DrawMigrationActionButton(root, settings, context, cache, candidate);
                        }

                        if (next == MIGRATION_OPTION_AUTO_DUPLICATE)
                        {
                            EditorGUILayout.HelpBox(
                                AunCastEditorLocalization.Localize(
                                    "この AudioSource を同じ階層の直後に複製し､ オリジナルを PlayerA､ 複製を PlayerB に割り当てます｡",
                                    "Duplicates this AudioSource right after itself in the same hierarchy, assigning the original to Player A and the copy to Player B."),
                                MessageType.None);
                        }
                    }
                    else if (candidate.sourceComponent != null)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            DrawMigrationActionButton(root, settings, context, cache, candidate);
                        }
                    }
                    else if (candidate.isConfigured)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            DrawConfiguredButton(candidate);
                            if (CanDeleteBundledConfiguredOutput(candidate, isBundledAunCastOutput))
                                DrawDeleteBundledConfiguredOutputButton(root, context, cache, candidate);
                        }

                        if (candidate.kind == MigrationCandidateKind.Speaker
                            && !string.IsNullOrEmpty(candidate.statusLabel))
                        {
                            GUILayout.Label(candidate.statusLabel, WrappedMiniLabelStyle());
                        }
                    }
                }
            }
        }

        private void DrawMigrationActionButton(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            SpeakerSetupContext context,
            MigrationCandidateCache cache,
            MigrationCandidate candidate)
        {
            using (new EditorGUI.DisabledScope(candidate.gameObject == null || candidate.sourceComponent == null))
            {
                string buttonLabel = GetMigrationButtonLabel(candidate, cache);
                float buttonWidth = candidate.kind == MigrationCandidateKind.AudioOutputTunnel ? 108f : 80f;
                if (GUILayout.Button(buttonLabel, GUILayout.Width(buttonWidth), GUILayout.Height(24)))
                {
                    ExecuteSingleMigration(root, settings, cache, candidate);
                    RefreshMigrationCandidates(root, context, cache);
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawMigratedTunnelDirectOutputButton(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            SpeakerSetupContext context,
            MigrationCandidateCache cache,
            MigrationCandidate candidate)
        {
            Component tunnel = GetAunCastAudioOutputTunnelCandidateComponent(candidate);
            bool canConvert = tunnel != null && CollectAudioOutputTunnelOutputs(tunnel).Length > 0;
            using (new EditorGUI.DisabledScope(!canConvert))
            {
                if (GUILayout.Button(
                        AunCastEditorLocalization.Localize("直結化", "Direct Output"),
                        GUILayout.Width(80f),
                        GUILayout.Height(24)))
                {
                    ExecuteAunCastAudioOutputTunnelDirectOutput(root, settings, candidate);
                    RefreshMigrationCandidates(root, context, cache);
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void DrawConfiguredButton(MigrationCandidate candidate)
        {
            string label = GetConfiguredButtonLabel(candidate);
            float buttonWidth = candidate.kind == MigrationCandidateKind.AudioOutputTunnel
                ? 108f
                : candidate.kind == MigrationCandidateKind.Speaker ? 96f : 80f;
            using (new EditorGUI.DisabledScope(true))
            {
                GUILayout.Button(
                    label,
                    GUILayout.Width(buttonWidth),
                    GUILayout.Height(24));
            }
        }

        private static bool CanDeleteBundledConfiguredOutput(MigrationCandidate candidate, bool isBundledAunCastOutput)
        {
            return isBundledAunCastOutput
                   && candidate.isConfigured
                   && candidate.gameObject != null
                   && (candidate.kind == MigrationCandidateKind.Screen || candidate.kind == MigrationCandidateKind.Speaker);
        }

        private void DrawDeleteBundledConfiguredOutputButton(
            Transform root,
            SpeakerSetupContext context,
            MigrationCandidateCache cache,
            MigrationCandidate candidate)
        {
            using (new EditorGUI.DisabledScope(candidate.gameObject == null))
            {
                if (GUILayout.Button(
                        AunCastEditorLocalization.Localize("削除", "Delete"),
                        GUILayout.Width(56f),
                        GUILayout.Height(24)))
                {
                    DeleteBundledConfiguredOutput(candidate);
                    RewireEventBusAndConsumers(root, recordUndo: true, writeLog: false);
                    RefreshMigrationCandidates(root, context, cache);
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void DeleteBundledConfiguredOutput(MigrationCandidate candidate)
        {
            if (candidate.gameObject == null) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Delete Bundled AunCast Output");
            Undo.DestroyObjectImmediate(candidate.gameObject);
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static string GetConfiguredButtonLabel(MigrationCandidate candidate)
        {
            if (candidate.kind == MigrationCandidateKind.AudioOutputTunnel)
                return AunCastEditorLocalization.Localize("移行済み", "Migrated");

            if (candidate.kind == MigrationCandidateKind.Speaker)
            {
                string suffix = GetSpeakerPlayerShortSuffix(candidate.inferredPlayerIndex);
                return AunCastEditorLocalization.Localize("設定済み " + suffix, "Configured " + suffix);
            }

            return AunCastEditorLocalization.Localize("設定済み", "Configured");
        }

        private static string GetSpeakerPlayerShortSuffix(int playerIndex)
        {
            return playerIndex == AunCastSpeaker.PLAYER_B ? "(B)" : "(A)";
        }

        private static string GetMigrationButtonLabel(MigrationCandidate candidate, MigrationCandidateCache cache)
        {
            if (candidate.isConfigured)
                return AunCastEditorLocalization.Localize("修正", "Fix");

            if (candidate.kind == MigrationCandidateKind.AudioOutputTunnel)
            {
                int tunnelMode = cache != null && cache.tunnelMigrationMode.TryGetValue(candidate.selectionKey, out int selectedTunnelMode)
                    ? selectedTunnelMode
                    : TUNNEL_MIGRATION_MODE_COMPAT_TUNNEL;
                return tunnelMode == TUNNEL_MIGRATION_MODE_DIRECT_SPEAKERS
                    ? AunCastEditorLocalization.Localize("スピーカー化", "Convert Speakers")
                    : AunCastEditorLocalization.Localize("トンネル移行", "Migrate Tunnel");
            }

            return AunCastEditorLocalization.Localize("変換", "Convert");
        }

        private static void DrawMigrationCandidateHeader(MigrationCandidate candidate)
        {
            string kindLabel = GetMigrationCandidateKindLabel(candidate.kind);
            string componentName = GetMigrationCandidateComponentName(candidate);
            DrawSelectableObjectPathWithKindAndComponent(
                candidate.gameObject,
                kindLabel,
                componentName,
                candidate.hierarchyPath);
        }

        private static void DrawSelectableObjectPathWithKindAndComponent(
            GameObject gameObject,
            string kindLabel,
            string componentName,
            string hierarchyPath)
        {
            if (string.IsNullOrEmpty(kindLabel))
                kindLabel = AunCastEditorLocalization.Localize("コンポーネント", "Component");
            if (string.IsNullOrEmpty(componentName))
                componentName = "-";

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(kindLabel, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                GUILayout.Label(
                    "(" + componentName + ")",
                    EditorStyles.label,
                    GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
            }

            DrawSelectableObjectPath(gameObject, hierarchyPath);
        }

        private static void DrawSelectableObjectPath(GameObject gameObject, string hierarchyPath)
        {
            DrawSelectablePathLink(
                hierarchyPath,
                gameObject,
                gameObject,
                AunCastEditorLocalization.Localize(
                    "クリックでこのオブジェクトを選択",
                    "Click to select this object"));
        }

        private static void DrawSelectablePathLink(
            string path,
            UnityEngine.Object selectionTarget,
            UnityEngine.Object pingTarget,
            string tooltip)
        {
            if (string.IsNullOrEmpty(path))
                path = "-";

            GUIStyle linkStyle = LinkPathStyle();
            var pathContent = new GUIContent(path, tooltip);
            float width = Mathf.Max(80f, EditorGUIUtility.currentViewWidth - 72f);
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight, linkStyle.CalcHeight(pathContent, width));
            Rect rect = EditorGUILayout.GetControlRect(false, height);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            using (new EditorGUI.DisabledScope(selectionTarget == null))
            {
                if (GUI.Button(rect, pathContent, linkStyle))
                {
                    Selection.activeObject = selectionTarget;
                    EditorGUIUtility.PingObject(pingTarget != null ? pingTarget : selectionTarget);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static string GetMigrationCandidateKindLabel(MigrationCandidateKind kind)
        {
            switch (kind)
            {
                case MigrationCandidateKind.Screen:
                    return AunCastEditorLocalization.Localize("スクリーン", "Screen");
                case MigrationCandidateKind.AudioOutputTunnel:
                    return AunCastEditorLocalization.Localize("トンネル", "Tunnel");
                default:
                    return AunCastEditorLocalization.Localize("スピーカー", "Speaker");
            }
        }

        private static string GetMigrationCandidateComponentName(MigrationCandidate candidate)
        {
            if (candidate.sourceComponent != null)
                return candidate.sourceComponent.GetType().Name;

            switch (candidate.kind)
            {
                case MigrationCandidateKind.Screen:
                    return nameof(AunCastScreen);
                case MigrationCandidateKind.AudioOutputTunnel:
                    return nameof(AunCastAudioOutputTunnel);
                default:
                    return nameof(AunCastSpeaker);
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
                            "旧 VRCAVProVideoScreen が残っています｡ 修正すると旧コンポーネントを削除します｡",
                            "Legacy VRCAVProVideoScreen remains. Fix removes the old component.")
                        : string.Empty,
                    "screen:" + path,
                    AunCastSpeaker.PLAYER_A,
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
                    true));
            }

            AunCastAudioOutputTunnel[] migratedAudioOutputTunnels = FindSceneComponents<AunCastAudioOutputTunnel>(scene);
            var delegatedTunnelIds = new HashSet<int>();
            for (int i = 0; i < migratedAudioOutputTunnels.Length; i++)
            {
                Component delegateTunnel = ResolveTunnelDelegate(migratedAudioOutputTunnels[i]);
                if (delegateTunnel != null)
                    delegatedTunnelIds.Add(delegateTunnel.GetInstanceID());
            }

            var tunnelInputSourceIds = new HashSet<int>();
            Component[] audioOutputTunnels = FindSceneComponentsByTypeName(scene, AUDIO_OUTPUT_TUNNEL_COMPONENT_TYPE_NAME);
            for (int i = 0; i < audioOutputTunnels.Length; i++)
            {
                Component tunnel = audioOutputTunnels[i];
                if (!IsAudioOutputTunnelComponent(tunnel)) continue;
                // 既に AunCastAudioOutputTunnel の委譲先になっている旧トンネルは移行済みのため列挙しない
                if (delegatedTunnelIds.Contains(tunnel.GetInstanceID())) continue;

                AudioSource inputSource = ReadAudioSourceProperty(tunnel, "input");
                if (inputSource != null)
                    tunnelInputSourceIds.Add(inputSource.GetInstanceID());
                string path = GetHierarchyPath(tunnel.transform);
                ReferenceWarning outputReferenceWarning = BuildTunnelOutputReferenceWarning(tunnel);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.AudioOutputTunnel,
                    tunnel.gameObject,
                    inputSource,
                    tunnel,
                    path,
                    string.Empty,
                    outputReferenceWarning,
                    "tunnel:" + path,
                    AunCastSpeaker.PLAYER_A,
                    false));
            }

            for (int i = 0; i < migratedAudioOutputTunnels.Length; i++)
            {
                AunCastAudioOutputTunnel tunnel = migratedAudioOutputTunnels[i];
                if (tunnel == null) continue;

                string path = GetHierarchyPath(tunnel.transform);
                ReferenceWarning outputReferenceWarning = BuildTunnelOutputReferenceWarning(tunnel);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.AudioOutputTunnel,
                    tunnel.gameObject,
                    null,
                    null,
                    path,
                    string.Empty,
                    outputReferenceWarning,
                    "tunnel:" + path,
                    AunCastSpeaker.PLAYER_A,
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
                if (tunnelInputSourceIds.Contains(source.GetInstanceID())) continue;

                AunCastSpeaker declaredSpeaker = source.gameObject.GetComponent<AunCastSpeaker>();
                bool isConfigured = declaredSpeaker != null;
                string path = GetHierarchyPath(source.transform);
                string status = isConfigured
                    ? BuildConfiguredSpeakerStatusLabel(source)
                    : BuildSpeakerStatusLabel(source);
                ReferenceWarning referenceWarning = BuildAudioSourceReferenceWarning(source);
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
                    referenceWarning,
                    "speaker:" + path,
                    inferredPlayerIndex,
                    isConfigured));
            }

            AunCastSpeaker[] declaredSpeakers = FindSceneComponents<AunCastSpeaker>(scene);
            for (int i = 0; i < declaredSpeakers.Length; i++)
            {
                AunCastSpeaker speaker = declaredSpeakers[i];
                if (speaker == null) continue;
                AudioSource source = speaker.GetComponent<AudioSource>();
                if (source == null) continue;
                if (tunnelInputSourceIds.Contains(source.GetInstanceID())) continue;

                string path = GetHierarchyPath(source.transform);
                AddMigrationCandidate(byKey, new MigrationCandidate(
                    MigrationCandidateKind.Speaker,
                    source.gameObject,
                    source,
                    null,
                    path,
                    BuildConfiguredSpeakerStatusLabel(source),
                    BuildAudioSourceReferenceWarning(source),
                    "speaker:" + path,
                    GetAunCastSpeakerPlayerIndex(speaker),
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

        private static string BuildConfiguredSpeakerStatusLabel(AudioSource source)
        {
            string status = BuildSpeakerStatusLabel(source);
            string normal = AunCastEditorLocalization.Localize("通常出力候補", "normal output candidate");
            if (string.IsNullOrEmpty(status) || status == FormatStatusTag(normal))
                return string.Empty;

            return status;
        }

        private static int GetAunCastSpeakerPlayerIndex(AunCastSpeaker speaker)
        {
            return speaker != null && speaker.playerIndex == AunCastSpeaker.PLAYER_B
                ? AunCastSpeaker.PLAYER_B
                : AunCastSpeaker.PLAYER_A;
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
            int convertedTunnels = 0;
            try
            {
                if (candidate.kind == MigrationCandidateKind.Screen)
                {
                    if (ConvertScreenCandidate(candidate))
                        convertedScreens++;
                }
                else if (candidate.kind == MigrationCandidateKind.AudioOutputTunnel)
                {
                    int tunnelMode = cache != null && cache.tunnelMigrationMode.TryGetValue(candidate.selectionKey, out int selectedTunnelMode)
                        ? selectedTunnelMode
                        : TUNNEL_MIGRATION_MODE_COMPAT_TUNNEL;
                    if (tunnelMode == TUNNEL_MIGRATION_MODE_DIRECT_SPEAKERS)
                    {
                        convertedSpeakers += ConvertAudioOutputTunnelOutputsToSpeakers(candidate, settings);
                    }
                    else if (ConvertAudioOutputTunnelCandidate(candidate, settings))
                    {
                        convertedTunnels++;
                    }
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

                RewireEventBusAndConsumers(root, recordUndo: true, writeLog: false);
                Debug.Log($"[AunCast] 出力変換を完了しました｡ Screen: {convertedScreens}件 / Speaker: {convertedSpeakers}件 / Tunnel: {convertedTunnels}件");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private void ExecuteAunCastAudioOutputTunnelDirectOutput(
            Transform root,
            PasocomMate.AunCast.AunCastSettings settings,
            MigrationCandidate candidate)
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("AunCast AudioOutputTunnel Direct Output");
            int convertedSpeakers = 0;
            try
            {
                convertedSpeakers = ConvertAunCastAudioOutputTunnelOutputsToSpeakers(candidate, settings);
                RewireEventBusAndConsumers(root, recordUndo: true, writeLog: false);
                Debug.Log($"[AunCast] AunCastAudioOutputTunnel の直結化を完了しました｡ Speaker: {convertedSpeakers}件");
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
            screen = EnsureUdonSharpComponent(candidate.gameObject, screen, recordUndo: true, nameof(AunCastScreen));
            if (screen == null) return false;

            var so = new SerializedObject(screen);
            SetStringProperty(so, "textureProperty", textureProperty);
            // textureStProperty は空のままにし､GetTextureStProperty() のフォールバックで textureProperty と同一扱いにする
            ApplyUdonSerializedChanges(screen, so, "Configure AunCastScreen");

            int removedLegacyScreens = DestroyComponentsByTypeName(candidate.gameObject, SCREEN_COMPONENT_TYPE_NAME);
            if (removedLegacyScreens == 0)
                Debug.LogWarning("[AunCast] VRCAVProVideoScreen の削除対象が見つかりませんでした｡");
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

                DestroyComponentWithUndo(component);
                removed++;
            }

            if (removed > 0)
            {
                EditorUtility.SetDirty(gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
            }

            return removed;
        }

        private static void DestroyComponentWithUndo(Component component)
        {
            if (component == null) return;

            if (component is UdonSharp.UdonSharpBehaviour udonSharpBehaviour)
            {
                UdonSharpUndo.DestroyImmediate(udonSharpBehaviour);
                return;
            }

            Undo.DestroyObjectImmediate(component);
        }

        private static bool RemoveLegacyTunnelInputSource(GameObject inputGameObject, GameObject tunnelGameObject)
        {
            if (inputGameObject == null) return false;

            if (inputGameObject != tunnelGameObject && IsSimpleLegacyAudioInputObject(inputGameObject))
            {
                if (!HasExternalSerializedReferences(inputGameObject))
                {
                    Undo.DestroyObjectImmediate(inputGameObject);
                    return true;
                }

                Debug.LogWarning(
                    $"[AunCast] AudioOutputTunnel の入力用 AudioSource に外部参照があるため､ GameObject は削除せず旧 VRCAVProVideoSpeaker のみ削除しました｡ {GetHierarchyPath(inputGameObject.transform)}",
                    inputGameObject);
            }

            return DestroyComponentsByTypeName(inputGameObject, SPEAKER_COMPONENT_TYPE_NAME) > 0;
        }

        private static bool IsSimpleLegacyAudioInputObject(GameObject gameObject)
        {
            if (gameObject == null || gameObject.transform.childCount > 0) return false;

            bool hasAudioSource = false;
            bool hasLegacySpeaker = false;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) return false;
                if (component is Transform) continue;
                if (component is AudioSource)
                {
                    hasAudioSource = true;
                    continue;
                }
                if (component.GetType().Name == SPEAKER_COMPONENT_TYPE_NAME)
                {
                    hasLegacySpeaker = true;
                    continue;
                }
                if (component.GetType().Name == VRC_SPATIAL_AUDIO_SOURCE_COMPONENT_TYPE_NAME)
                {
                    continue;
                }

                return false;
            }

            return hasAudioSource && hasLegacySpeaker;
        }

        private static bool HasExternalSerializedReferences(GameObject targetGameObject)
        {
            if (targetGameObject == null || !targetGameObject.scene.IsValid()) return true;

            var targets = new List<UnityEngine.Object>
            {
                targetGameObject,
                targetGameObject.transform
            };
            Component[] targetComponents = targetGameObject.GetComponents<Component>();
            for (int i = 0; i < targetComponents.Length; i++)
            {
                Component component = targetComponents[i];
                if (component != null)
                    targets.Add(component);
            }

            Component[] components = UnityEngine.Object.FindObjectsOfType<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != targetGameObject.scene) continue;
                if (component.gameObject == targetGameObject) continue;

                for (int j = 0; j < targets.Count; j++)
                {
                    if (SerializedObjectReferences(component, targets[j]))
                        return true;
                }
            }

            return false;
        }

        private static bool ConvertSpeakerCandidate(
            MigrationCandidate candidate,
            PasocomMate.AunCast.AunCastSettings settings,
            int playerIndex)
        {
            return ConvertSpeakerObject(candidate.gameObject, candidate.sourceComponent, candidate.audioSource, settings, playerIndex);
        }

        private static bool ConvertAudioOutputTunnelCandidate(
            MigrationCandidate candidate,
            PasocomMate.AunCast.AunCastSettings settings)
        {
            Component oldTunnel = GetAudioOutputTunnelCandidateComponent(candidate);
            if (oldTunnel == null)
            {
                Debug.LogWarning("[AunCast] AudioOutputTunnel の候補を特定できないため､ 自動移行を中止しました｡ 手動で AunCastAudioOutputTunnel を追加してください｡", candidate.gameObject);
                return false;
            }

            AudioSource inputSource = ReadAudioSourceProperty(oldTunnel, "input") ?? candidate.audioSource;
            AudioSource leftOutput = ReadAudioSourceProperty(oldTunnel, "leftOutput");
            AudioSource rightOutput = ReadAudioSourceProperty(oldTunnel, "rightOutput");
            AudioSource stereoOutput = ReadAudioSourceProperty(oldTunnel, "stereoOutput");
            if (leftOutput == null && rightOutput == null && stereoOutput == null)
            {
                Debug.LogWarning("[AunCast] AudioOutputTunnel の出力先を読み取れないため､ 自動移行を中止しました｡ AunCastAudioOutputTunnel の出力先を手動で設定してください｡", oldTunnel);
                return false;
            }

            AudioSource inputA = null;
            AudioSource inputB = null;
            if (inputSource != null)
            {
                int convertedInputs = ConvertTunnelInputToSpeakerPair(inputSource, settings, out inputA, out inputB);
                if (convertedInputs != 2)
                {
                    Debug.LogWarning($"[AunCast] AudioOutputTunnel の入力用 AudioSource を A/B 用に変換できなかったため､ 自動移行を中止しました｡ Converted: {convertedInputs}/2", inputSource);
                    return false;
                }
            }

            GameObject tunnelGameObject = oldTunnel.gameObject;
            AunCastAudioOutputTunnel tunnel = tunnelGameObject.GetComponent<AunCastAudioOutputTunnel>();
            tunnel = EnsureUdonSharpComponent(tunnelGameObject, tunnel, recordUndo: true, nameof(AunCastAudioOutputTunnel));
            if (tunnel == null) return false;

            var so = new SerializedObject(tunnel);
            bool changed = false;
            if (inputSource != null)
            {
                changed |= SetObjectProperty(so, "inputA", inputA);
                changed |= SetObjectProperty(so, "inputB", inputB);
            }
            // 旧 AudioOutputTunnel は削除せず温存し、委譲先として設定する。
            // PCM トンネル処理はすべて旧コンポーネント側が担い、AunCast 側は input の A/B 切替のみ行う。
            changed |= SetObjectProperty(so, "targetTunnel", oldTunnel);
            if (changed && !ApplyUdonSerializedChanges(tunnel, so, "Configure AunCastAudioOutputTunnel", recordUndo: true))
                return false;

            EditorUtility.SetDirty(tunnelGameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tunnelGameObject);
            Debug.Log($"[AunCast] AudioOutputTunnel を委譲先とする AunCastAudioOutputTunnel を設定しました｡ {GetHierarchyPath(tunnelGameObject.transform)}", tunnelGameObject);
            return true;
        }

        private static int ConvertTunnelInputToSpeakerPair(
            AudioSource inputSource,
            PasocomMate.AunCast.AunCastSettings settings,
            out AudioSource inputA,
            out AudioSource inputB)
        {
            inputA = inputSource;
            inputB = null;
            if (inputSource == null) return 0;

            GameObject original = inputSource.gameObject;
            GameObject duplicate = UnityEngine.Object.Instantiate(original, original.transform.parent);
            Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate AunCast Tunnel Input Speaker");
            duplicate.name = original.name + " (B)";
            duplicate.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(duplicate);
            DestroyComponentsByTypeName(duplicate, AUDIO_OUTPUT_TUNNEL_COMPONENT_TYPE_NAME);
            DestroyComponentsByTypeName(duplicate, nameof(AunCastAudioOutputTunnel));

            int converted = 0;
            Component sourceSpeakerComponent = FindSpeakerComponent(original);
            if (ConvertSpeakerObject(original, sourceSpeakerComponent, inputSource, settings, AunCastSpeaker.PLAYER_A))
                converted++;

            inputB = duplicate.GetComponent<AudioSource>();
            Component duplicateSpeakerComponent = FindSpeakerComponent(duplicate);
            if (ConvertSpeakerObject(duplicate, duplicateSpeakerComponent, inputB, settings, AunCastSpeaker.PLAYER_B))
                converted++;

            return converted;
        }

        private static int ConvertAunCastAudioOutputTunnelOutputsToSpeakers(
            MigrationCandidate candidate,
            PasocomMate.AunCast.AunCastSettings settings)
        {
            AunCastAudioOutputTunnel tunnel = GetAunCastAudioOutputTunnelCandidateComponent(candidate);
            if (tunnel == null)
            {
                Debug.LogWarning("[AunCast] AunCastAudioOutputTunnel の候補を特定できないため､ 直結化を中止しました｡", candidate.gameObject);
                return 0;
            }

            TunnelOutputSource[] outputs = CollectAudioOutputTunnelOutputs(tunnel);
            if (outputs.Length == 0)
            {
                Debug.LogWarning("[AunCast] AunCastAudioOutputTunnel の出力先を読み取れないため､ 直結化を中止しました｡", tunnel);
                return 0;
            }

            int converted = 0;
            for (int i = 0; i < outputs.Length; i++)
                converted += ConvertTunnelOutputToSpeakerPair(outputs[i], settings);

            int expected = outputs.Length * 2;
            if (converted != expected)
            {
                Debug.LogWarning($"[AunCast] AunCastAudioOutputTunnel の直結化が一部失敗したため､ AunCastAudioOutputTunnel は残しました｡ Converted: {converted}/{expected}", tunnel);
                return converted;
            }

            GameObject tunnelGameObject = tunnel.gameObject;
            // スピーカー化した出力へ委譲先の旧トンネルが書き込み続けないよう、旧トンネルも削除する
            Component delegateTunnel = ResolveTunnelDelegate(tunnel);
            if (delegateTunnel != null)
                DestroyComponentWithUndo(delegateTunnel);
            DestroyComponentWithUndo(tunnel);

            EditorUtility.SetDirty(tunnelGameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tunnelGameObject);
            Debug.Log($"[AunCast] AunCastAudioOutputTunnel を直結化しました｡ {GetHierarchyPath(tunnelGameObject.transform)}", tunnelGameObject);
            return converted;
        }

        private static int ConvertAudioOutputTunnelOutputsToSpeakers(
            MigrationCandidate candidate,
            PasocomMate.AunCast.AunCastSettings settings)
        {
            Component oldTunnel = GetAudioOutputTunnelCandidateComponent(candidate);
            if (oldTunnel == null)
            {
                Debug.LogWarning("[AunCast] AudioOutputTunnel の候補を特定できないため､ 出力 AudioSource のスピーカー化を中止しました｡", candidate.gameObject);
                return 0;
            }

            AudioSource inputSource = ReadAudioSourceProperty(oldTunnel, "input") ?? candidate.audioSource;
            TunnelOutputSource[] outputs = CollectAudioOutputTunnelOutputs(oldTunnel);
            if (outputs.Length == 0)
            {
                Debug.LogWarning("[AunCast] AudioOutputTunnel の出力先を読み取れないため､ 出力 AudioSource のスピーカー化を中止しました｡", oldTunnel);
                return 0;
            }

            int converted = 0;
            for (int i = 0; i < outputs.Length; i++)
                converted += ConvertTunnelOutputToSpeakerPair(outputs[i], settings);

            int expected = outputs.Length * 2;
            if (converted != expected)
            {
                Debug.LogWarning($"[AunCast] AudioOutputTunnel 出力のスピーカー化が一部失敗したため､ 旧 AudioOutputTunnel は残しました｡ Converted: {converted}/{expected}", oldTunnel);
                return converted;
            }

            GameObject tunnelGameObject = oldTunnel.gameObject;
            DestroyComponentWithUndo(oldTunnel);
            if (inputSource != null)
                RemoveLegacyTunnelInputSource(inputSource.gameObject, tunnelGameObject);

            EditorUtility.SetDirty(tunnelGameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tunnelGameObject);
            Debug.Log($"[AunCast] AudioOutputTunnel の出力先 AudioSource を AunCast スピーカーに変換しました｡ {GetHierarchyPath(tunnelGameObject.transform)}", tunnelGameObject);
            return converted;
        }

        private static Component GetAudioOutputTunnelCandidateComponent(MigrationCandidate candidate)
        {
            if (IsAudioOutputTunnelComponent(candidate.sourceComponent))
                return candidate.sourceComponent;

            return FindAudioOutputTunnelForSource(candidate.audioSource, requireInputReference: true);
        }

        private static AunCastAudioOutputTunnel GetAunCastAudioOutputTunnelCandidateComponent(MigrationCandidate candidate)
        {
            if (candidate.sourceComponent is AunCastAudioOutputTunnel tunnel)
                return tunnel;

            return candidate.gameObject != null
                ? candidate.gameObject.GetComponent<AunCastAudioOutputTunnel>()
                : null;
        }

        private static int ConvertTunnelOutputToSpeakerPair(
            TunnelOutputSource output,
            PasocomMate.AunCast.AunCastSettings settings)
        {
            if (output.source == null) return 0;

            GameObject original = output.source.gameObject;
            GameObject duplicate = UnityEngine.Object.Instantiate(original, original.transform.parent);
            Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate AunCast Tunnel Output Speaker");
            duplicate.name = original.name + " (B)";
            duplicate.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(duplicate);
            DestroyComponentsByTypeName(duplicate, AUDIO_OUTPUT_TUNNEL_COMPONENT_TYPE_NAME);
            DestroyComponentsByTypeName(duplicate, nameof(AunCastAudioOutputTunnel));

            int removedFilters = DestroyAudioFilters(original) + DestroyAudioFilters(duplicate);
            if (removedFilters > 0)
                Debug.Log($"[AunCast] VRCAVProVideoSpeaker 非対応のオーディオフィルターを {removedFilters} 件削除しました｡ {GetHierarchyPath(original.transform)}", original);

            int converted = 0;
            if (ConvertSpeakerObject(original, null, output.source, settings, AunCastSpeaker.PLAYER_A, output.mode))
                converted++;

            AudioSource duplicateAudio = duplicate.GetComponent<AudioSource>();
            if (ConvertSpeakerObject(duplicate, null, duplicateAudio, settings, AunCastSpeaker.PLAYER_B, output.mode))
                converted++;

            return converted;
        }

        /// <summary>
        /// AunCastAudioOutputTunnel が渡された場合は委譲先の旧 AudioOutputTunnel を返す。
        /// 出力先 (leftOutput / rightOutput / stereoOutput) は委譲先だけが保持するため、
        /// 出力を読むヘルパーはこの解決を経由する。
        /// </summary>
        private static Component ResolveTunnelDelegate(Component tunnel)
        {
            if (!(tunnel is AunCastAudioOutputTunnel)) return tunnel;

            var so = new SerializedObject(tunnel);
            SerializedProperty prop = so.FindProperty("targetTunnel");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return null;
            return prop.objectReferenceValue as Component;
        }

        private static TunnelOutputSource[] CollectAudioOutputTunnelOutputs(Component tunnel)
        {
            Component oldTunnel = ResolveTunnelDelegate(tunnel);
            if (oldTunnel == null) return Array.Empty<TunnelOutputSource>();

            var outputs = new List<TunnelOutputSource>();
            var used = new HashSet<int>();
            AddTunnelOutput(outputs, used, ReadAudioSourceProperty(oldTunnel, "leftOutput"), AunCastSpeaker.MODE_LEFT);
            AddTunnelOutput(outputs, used, ReadAudioSourceProperty(oldTunnel, "rightOutput"), AunCastSpeaker.MODE_RIGHT);
            AddTunnelOutput(outputs, used, ReadAudioSourceProperty(oldTunnel, "stereoOutput"), AunCastSpeaker.MODE_STEREO);
            return outputs.ToArray();
        }

        private static void AddTunnelOutput(
            List<TunnelOutputSource> outputs,
            HashSet<int> used,
            AudioSource source,
            int mode)
        {
            if (outputs == null || source == null) return;
            int instanceId = source.GetInstanceID();
            if (used != null && !used.Add(instanceId)) return;
            outputs.Add(new TunnelOutputSource(source, mode));
        }

        private static ReferenceWarning BuildTunnelOutputReferenceWarning(Component tunnel)
        {
            TunnelOutputSource[] outputs = CollectAudioOutputTunnelOutputs(tunnel);
            Component[] references = CollectExternalReferencesToTunnelOutputs(tunnel, outputs, out Component[] audioLinkReferences);
            if (references.Length == 0)
            {
                return audioLinkReferences.Length > 0
                    ? BuildAudioLinkManagedReferenceInfo(audioLinkReferences)
                    : default;
            }

            string prefix = audioLinkReferences.Length > 0
                ? AunCastEditorLocalization.Localize(
                    "AudioLink の参照は AunCast が自動管理します｡ その他に､ ",
                    "AudioLink references are managed automatically by AunCast. Other ")
                : string.Empty;
            string message = prefix + AunCastEditorLocalization.Localize(
                $"トンネルの出力 AudioSource を参照するコンポーネント: {references.Length} 件 (出力AudioSourceをスピーカー化すると A/B に分かれるため要確認) ",
                $"components referencing tunnel output AudioSources: {references.Length} (review before converting outputs into A/B speakers)");
            return new ReferenceWarning(message, references, audioLinkReferences, MessageType.Warning);
        }

        private static bool HasAudioFiltersOnTunnelOutputs(Component tunnel)
        {
            TunnelOutputSource[] outputs = CollectAudioOutputTunnelOutputs(tunnel);
            for (int i = 0; i < outputs.Length; i++)
            {
                if (outputs[i].source == null) continue;
                if (HasAudioFilters(outputs[i].source.gameObject))
                    return true;
            }
            return false;
        }

        private static bool HasAudioFilters(GameObject gameObject)
        {
            if (gameObject == null) return false;
            for (int i = 0; i < AUDIO_FILTER_TYPES.Length; i++)
            {
                if (gameObject.GetComponent(AUDIO_FILTER_TYPES[i]) != null)
                    return true;
            }
            return false;
        }

        private static int DestroyAudioFilters(GameObject gameObject)
        {
            if (gameObject == null) return 0;
            int removed = 0;
            for (int i = 0; i < AUDIO_FILTER_TYPES.Length; i++)
            {
                Component filter;
                while ((filter = gameObject.GetComponent(AUDIO_FILTER_TYPES[i])) != null)
                {
                    DestroyComponentWithUndo(filter);
                    removed++;
                }
            }
            return removed;
        }

        private static Component[] CollectExternalReferencesToTunnelOutputs(
            Component tunnel,
            TunnelOutputSource[] outputs,
            out Component[] audioLinkReferences)
        {
            audioLinkReferences = Array.Empty<Component>();
            if (tunnel == null || outputs == null || outputs.Length == 0) return Array.Empty<Component>();
            if (!tunnel.gameObject.scene.IsValid()) return Array.Empty<Component>();

            // 委譲先の旧 AudioOutputTunnel が出力を参照しているのは正常な構成のため除外する
            Component delegateTunnel = ResolveTunnelDelegate(tunnel);

            var referrers = new HashSet<int>();
            var audioLinkReferrers = new HashSet<int>();
            var audioLinkReferenceList = new List<Component>();
            var references = new List<Component>();
            Component[] components = UnityEngine.Object.FindObjectsOfType<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component == tunnel || component == delegateTunnel || component is Transform) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != tunnel.gameObject.scene) continue;

                for (int j = 0; j < outputs.Length; j++)
                {
                    AudioSource output = outputs[j].source;
                    if (output == null) continue;
                    if (component.gameObject == output.gameObject) continue;
                    if (SerializedObjectReferences(component, output))
                    {
                        int instanceId = component.GetInstanceID();
                        if (IsAudioLinkReferenceComponent(component))
                        {
                            if (audioLinkReferrers.Add(instanceId))
                                audioLinkReferenceList.Add(component);
                        }
                        else if (referrers.Add(instanceId))
                        {
                            references.Add(component);
                        }
                        break;
                    }
                }
            }

            audioLinkReferences = audioLinkReferenceList.ToArray();
            return references.ToArray();
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
            int mode = ReadIntProperty(sourceSpeakerComponent, "mode", AunCastSpeaker.MODE_STEREO);
            return ConvertSpeakerObject(gameObject, sourceSpeakerComponent, audioSource, settings, playerIndex, mode);
        }

        private static bool ConvertSpeakerObject(
            GameObject gameObject,
            Component sourceSpeakerComponent,
            AudioSource audioSource,
            PasocomMate.AunCast.AunCastSettings settings,
            int playerIndex,
            int mode)
        {
            if (gameObject == null || audioSource == null) return false;

            AunCastSpeaker speaker = EnsureAunCastSpeaker(gameObject, settings);
            if (speaker == null) return false;

            var so = new SerializedObject(speaker);
            SetIntProperty(so, "playerIndex", playerIndex == AunCastSpeaker.PLAYER_B ? AunCastSpeaker.PLAYER_B : AunCastSpeaker.PLAYER_A);
            SetIntProperty(so, "mode", Mathf.Clamp(mode, AunCastSpeaker.MODE_STEREO, AunCastSpeaker.MODE_RIGHT));
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

        private static bool IsManagerTextureRenderer(AunCastVideoPlayerManager manager, Renderer renderer)
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

        private static string BuildSpeakerStatusLabel(AudioSource source)
        {
            if (source == null) return string.Empty;
            var labels = new List<string>();
            if (IsInEditorOnlyHierarchy(source.gameObject))
                labels.Add(AunCastEditorLocalization.Localize("EditorOnly階層", "EditorOnly hierarchy"));
            if (!source.gameObject.activeInHierarchy)
                labels.Add(AunCastEditorLocalization.Localize("非アクティブ", "Inactive"));
            if (!source.enabled)
                labels.Add(AunCastEditorLocalization.Localize("AudioSource無効", "AudioSource disabled"));
            if (source.volume <= 0.0001f)
                labels.Add(AunCastEditorLocalization.Localize("volume 0", "volume 0"));
            if (IsCustomRolloffSilent(source))
                labels.Add(AunCastEditorLocalization.Localize("音が届かない設定", "inaudible attenuation"));

            return labels.Count == 0
                ? FormatStatusTag(AunCastEditorLocalization.Localize("通常出力候補", "normal output candidate"))
                : FormatStatusTags(labels);
        }

        private static string FormatStatusTags(List<string> labels)
        {
            if (labels == null || labels.Count == 0) return string.Empty;

            var tags = new string[labels.Count];
            for (int i = 0; i < labels.Count; i++)
                tags[i] = FormatStatusTag(labels[i]);
            return string.Join(" ", tags);
        }

        private static string FormatStatusTag(string label)
        {
            return string.IsNullOrEmpty(label) ? string.Empty : "[" + label + "]";
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

        private static Component FindAudioOutputTunnelForSource(AudioSource source, bool requireInputReference)
        {
            if (source == null) return null;

            var visited = new HashSet<int>();
            Transform current = source.transform;
            for (int depth = 0; current != null && depth < 3; depth++)
            {
                Component tunnel = FindAudioOutputTunnelInChildren(
                    current,
                    source,
                    requireInputReference,
                    visited);
                if (tunnel != null)
                    return tunnel;
                current = current.parent;
            }

            return null;
        }

        private static Component FindAudioOutputTunnelInChildren(
            Transform root,
            AudioSource source,
            bool requireInputReference,
            HashSet<int> visited)
        {
            if (root == null) return null;

            Component fallback = null;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                if (!IsAudioOutputTunnelComponent(component)) continue;

                int instanceId = component.GetInstanceID();
                if (visited != null && !visited.Add(instanceId)) continue;

                if (ReadAudioSourceProperty(component, "input") == source)
                    return component;

                if (!requireInputReference && fallback == null)
                    fallback = component;
            }

            return requireInputReference ? null : fallback;
        }

        private static bool IsAudioOutputTunnelComponent(Component component)
        {
            if (component == null || component.GetType().Name != AUDIO_OUTPUT_TUNNEL_COMPONENT_TYPE_NAME) return false;

            var so = new SerializedObject(component);
            bool hasInput = so.FindProperty("input") != null;
            bool hasOutput =
                so.FindProperty("leftOutput") != null
                || so.FindProperty("rightOutput") != null
                || so.FindProperty("stereoOutput") != null;
            return hasInput || hasOutput;
        }

        private static ReferenceWarning BuildAudioSourceReferenceWarning(AudioSource source)
        {
            if (source == null || !source.gameObject.scene.IsValid()) return default;

            var references = new List<Component>();
            var used = new HashSet<int>();
            var audioLinkUsed = new HashSet<int>();
            var audioLinkReferences = new List<Component>();
            Component[] components = UnityEngine.Object.FindObjectsOfType<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component == source) continue;
                if (!component.gameObject.scene.IsValid() || component.gameObject.scene != source.gameObject.scene) continue;
                if (!SerializedObjectReferences(component, source)) continue;
                int instanceId = component.GetInstanceID();
                if (IsAudioLinkReferenceComponent(component))
                {
                    if (audioLinkUsed.Add(instanceId))
                        audioLinkReferences.Add(component);
                }
                else if (used.Add(instanceId))
                {
                    references.Add(component);
                }
            }

            if (references.Count == 0)
            {
                return audioLinkUsed.Count > 0
                    ? BuildAudioLinkManagedReferenceInfo(audioLinkReferences.ToArray())
                    : default;
            }

            string prefix = audioLinkUsed.Count > 0
                ? AunCastEditorLocalization.Localize(
                    "AudioLink の参照は AunCast が自動管理します｡ その他に､ ",
                    "AudioLink references are managed automatically by AunCast. Other ")
                : string.Empty;
            string message = prefix + AunCastEditorLocalization.Localize(
                $"この AudioSource を参照するコンポーネント: {references.Count} 件 (A/B に分かれると単一入力の参照元は要対応) ",
                $"components referencing this AudioSource: {references.Count} (single-input referrers need attention once split into A/B)");
            return new ReferenceWarning(message, references.ToArray(), audioLinkReferences.ToArray(), MessageType.Warning);
        }

        private static ReferenceWarning BuildAudioLinkManagedReferenceInfo(Component[] audioLinkReferences)
        {
            string message = AunCastEditorLocalization.Localize(
                "AudioLink の AudioSource 参照は AunCast が自動管理します｡ この警告を消すには「AudioLink参照を解除」を押してください｡",
                "AudioLink AudioSource references are managed automatically by AunCast. Use Clear AudioLink Reference if you want to remove this notice.");
            return new ReferenceWarning(message, Array.Empty<Component>(), audioLinkReferences, MessageType.Info);
        }

        private static bool IsAudioLinkReferenceComponent(Component component)
        {
            return component != null
                   && (component.GetType().Name == "AudioLink"
                       || (component.gameObject.name == "AudioLink"
                           && component is UdonSharp.UdonSharpBehaviour));
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
                    "AunCast ルートが見つかりません｡", "AunCast root was not found.");
                return false;
            }

            AunCastVideoPlayerManager managerA = null;
            AunCastVideoPlayerManager managerB = null;
            AunCastVideoPlayerManager[] managers = root.GetComponentsInChildren<AunCastVideoPlayerManager>(true);
            for (int i = 0; i < managers.Length; i++)
            {
                AunCastVideoPlayerManager manager = managers[i];
                if (manager == null) continue;
                if (manager.playerIndex == 0 && managerA == null)
                    managerA = manager;
                else if (manager.playerIndex == 1 && managerB == null)
                    managerB = manager;
            }

            if (managerA == null || managerB == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "AunCastVideoPlayerManager A/B が見つかりません｡", "AunCastVideoPlayerManager A/B was not found.");
                return false;
            }
            if (managerA.avProPlayer == null || managerB.avProPlayer == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "AunCastVideoPlayerManager の avProPlayer 参照が不足しています｡",
                    "The avProPlayer reference on AunCastVideoPlayerManager is missing.");
                return false;
            }

            var switcher = root.GetComponentInChildren<AunCastPlaybackSwitcher>(true);
            if (switcher == null)
            {
                error = AunCastEditorLocalization.Localize(
                    "AunCastPlaybackSwitcher が見つかりません｡", "AunCastPlaybackSwitcher was not found.");
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

        private static List<MigrationValidationIssue> ValidateCurrentSpeakerRouting(SpeakerSetupContext context)
        {
            var errors = new List<MigrationValidationIssue>();
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
                        errors.Add(new MigrationValidationIssue(
                            AunCastEditorLocalization.Localize(
                                $"PlayerA audioSources[{i}] が null です｡",
                                $"PlayerA audioSources[{i}] is null."),
                            context.managerA,
                            context.managerA,
                            selectable: true,
                            GetValidationTargetPath(context.managerA)));
                        continue;
                    }
                    used.Add(source);
                    ValidateSpeakerBinding(errors, source, context.playerA, $"PlayerA audioSources[{i}]", context.managerA);
                }
            }

            if (bSources != null)
            {
                for (int i = 0; i < bSources.Length; i++)
                {
                    AudioSource source = bSources[i];
                    if (source == null)
                    {
                        errors.Add(new MigrationValidationIssue(
                            AunCastEditorLocalization.Localize(
                                $"PlayerB audioSources[{i}] が null です｡",
                                $"PlayerB audioSources[{i}] is null."),
                            context.managerB,
                            context.managerB,
                            selectable: true,
                            GetValidationTargetPath(context.managerB)));
                        continue;
                    }
                    if (used.Contains(source))
                        errors.Add(new MigrationValidationIssue(
                            AunCastEditorLocalization.Localize(
                                $"PlayerA/PlayerB で同一 AudioSource を共有しています: {GetHierarchyPath(source.transform)}",
                                $"Player A/B share the same AudioSource: {GetHierarchyPath(source.transform)}"),
                            source));
                    ValidateSpeakerBinding(errors, source, context.playerB, $"PlayerB audioSources[{i}]", context.managerB);
                }
            }

            return errors;
        }

        private static void ValidateSpeakerBinding(
            List<MigrationValidationIssue> errors,
            AudioSource source,
            VRCAVProVideoPlayer expectedPlayer,
            string label,
            UnityEngine.Object managerTarget)
        {
            if (source == null) return;
            if (expectedPlayer == null)
            {
                errors.Add(new MigrationValidationIssue(
                    AunCastEditorLocalization.Localize(
                        $"{label}: 期待する VRCAVProVideoPlayer が null です｡",
                        $"{label}: The expected VRCAVProVideoPlayer is null."),
                    managerTarget ?? source));
                return;
            }

            Component speaker = FindSpeakerComponent(source.gameObject);
            if (speaker == null)
            {
                errors.Add(new MigrationValidationIssue(
                    AunCastEditorLocalization.Localize(
                        $"{label}: VRC AVPro Video Speaker がありません｡ ({GetHierarchyPath(source.transform)})",
                        $"{label}: No VRC AVPro Video Speaker found. ({GetHierarchyPath(source.transform)})"),
                    source));
                return;
            }

            if (!IsSpeakerRoutedTo(speaker, expectedPlayer))
            {
                errors.Add(new MigrationValidationIssue(
                    AunCastEditorLocalization.Localize(
                        $"{label}: Speaker の videoPlayer が想定先を向いていません｡ ({GetHierarchyPath(source.transform)})",
                        $"{label}: The Speaker's videoPlayer does not point to the expected target. ({GetHierarchyPath(source.transform)})"),
                    speaker));
            }
        }

        private static string GetValidationTargetPath(UnityEngine.Object target)
        {
            if (target is Component component)
                return GetHierarchyPath(component.transform);
            if (target is GameObject gameObject)
                return GetHierarchyPath(gameObject.transform);
            return target != null ? target.name : string.Empty;
        }

        private static bool ApplyAudioSourcesToManager(AunCastVideoPlayerManager manager, AudioSource[] sources, bool recordUndo = true)
        {
            if (manager == null) return false;
            var so = new SerializedObject(manager);
            if (SetObjectArrayProperty(so, nameof(AunCastVideoPlayerManager.audioSources), sources))
                return ApplyUdonSerializedChanges(manager, so, "Apply AudioSources to AunCastVideoPlayerManager", recordUndo);
            return false;
        }

        private static bool ApplySilenceDetectorsToSwitcher(
            AunCastPlaybackSwitcher switcher,
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
                return ApplyUdonSerializedChanges(switcher, so, "Apply Silence Detectors to AunCastPlaybackSwitcher", recordUndo);
            return false;
        }

        private static T EnsureUdonSharpComponent<T>(
            GameObject gameObject,
            T component,
            bool recordUndo,
            string componentName) where T : UdonSharp.UdonSharpBehaviour
        {
            if (gameObject == null) return null;
            if (component != null && HasConfiguredBackingUdonBehaviour(component))
                return component;

            if (component != null)
            {
                Debug.LogWarning($"[AunCast] {componentName} の backing UdonBehaviour が未設定のため作り直します。", component);
                if (recordUndo)
                    UdonSharpUndo.DestroyImmediate(component);
                else
                    UdonSharpEditorUtility.DestroyImmediate(component);
            }

            return recordUndo
                ? UdonSharpUndo.AddComponent<T>(gameObject)
                : gameObject.AddUdonSharpComponent<T>();
        }

        private static AunCastSpeaker EnsureAunCastSpeaker(
            GameObject gameObject,
            PasocomMate.AunCast.AunCastSettings settings,
            bool recordUndo = true)
        {
            if (gameObject == null) return null;

            var detector = gameObject.GetComponent<AunCastSpeaker>();
            detector = EnsureUdonSharpComponent(gameObject, detector, recordUndo, nameof(AunCastSpeaker));
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

    }
}
#endif
