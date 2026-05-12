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
        private SerializedProperty _audioLinkBehaviourProperty;

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
            DrawPropertiesExcluding(serializedObject, "m_Script");
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
