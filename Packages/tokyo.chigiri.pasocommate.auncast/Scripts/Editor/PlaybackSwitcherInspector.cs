#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// PlaybackSwitcher の Inspector カスタムエディタ。
    /// audioLinkBehaviour が未設定の場合、AudioLink を自動検出して配線する。
    /// </summary>
    [CustomEditor(typeof(PlaybackSwitcher))]
    internal class PlaybackSwitcherInspector : Editor
    {
        private static readonly string[] SETTINGS_PROPERTY_NAMES =
        {
            "crossfadeDurationSec",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "playerManagerA",
            "playerManagerB",
            "silenceDetectorA",
            "silenceDetectorB",
            "eventBus",
            "audioLinkBehaviour",
        };

        private SerializedProperty _audioLinkBehaviourProperty;
        private readonly bool[] _showManagedPropertyGroups = new bool[3];

        private void OnEnable()
        {
            _audioLinkBehaviourProperty = serializedObject.FindProperty("audioLinkBehaviour");
            TryAutoAssignAudioLinkBehaviour();
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawProgramSource(target, false)) return;

            serializedObject.Update();
            TryAutoAssignAudioLinkBehaviour();
            AunCastManagedSettingsInspectorUtility.DrawPropertiesWithManagedFoldouts(
                serializedObject,
                new[] { "m_Script" },
                new[]
                {
                    new AunCastManagedSettingsInspectorUtility.ManagedPropertyGroup(
                        "共通設定（AunCastSettings 管理下）",
                        "Common Settings (Managed by AunCastSettings)",
                        SETTINGS_PROPERTY_NAMES),
                    new AunCastManagedSettingsInspectorUtility.ManagedPropertyGroup(
                        "配線対象（変更不可）",
                        "Wiring References (Read Only)",
                        WIRING_PROPERTY_NAMES),
                },
                _showManagedPropertyGroups);
            serializedObject.ApplyModifiedProperties();
        }

        private void TryAutoAssignAudioLinkBehaviour()
        {
            if (_audioLinkBehaviourProperty == null) return;
            if (_audioLinkBehaviourProperty.objectReferenceValue != null) return;

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

            var switcher = (PlaybackSwitcher)target;
            Undo.RecordObject(switcher, "Auto Assign AudioLink Behaviour");
            _audioLinkBehaviourProperty.objectReferenceValue = audioLink;
            if (!serializedObject.ApplyModifiedProperties()) return;

            UdonSharpEditorUtility.CopyProxyToUdon(switcher);
            EditorUtility.SetDirty(switcher);
            PrefabUtility.RecordPrefabInstancePropertyModifications(switcher);

            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(switcher);
            if (udon == null) return;
            EditorUtility.SetDirty(udon);
            PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
        }
    }
}
#endif
