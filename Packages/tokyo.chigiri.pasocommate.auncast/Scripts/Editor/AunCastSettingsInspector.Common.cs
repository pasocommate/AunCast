#if UNITY_EDITOR
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// Migration / Rewire / SettingsApply から共通に使う汎用ヘルパー。
    /// 特定機能（マイグレーション・再配線）に固有でない処理をここへ集約する。
    /// </summary>
    public partial class AunCastSettingsInspector
    {
        private static bool IsInEditorOnlyHierarchy(GameObject gameObject)
        {
            if (gameObject == null) return false;
            Transform current = gameObject.transform;
            while (current != null)
            {
                if (current.gameObject.CompareTag("EditorOnly"))
                    return true;
                current = current.parent;
            }
            return false;
        }

        private static AudioSource ReadAudioSourceProperty(UnityEngine.Object owner, string fieldName)
        {
            if (owner == null || string.IsNullOrEmpty(fieldName)) return null;
            var so = new SerializedObject(owner);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return null;
            return prop.objectReferenceValue as AudioSource;
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

            if (!HasConfiguredBackingUdonBehaviour(component))
            {
                Debug.LogWarning($"[AunCast] {component.GetType().Name} の backing UdonBehaviour が未設定のため、Udon 側への反映をスキップしました。", component);
                return false;
            }

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

        private static bool HasConfiguredBackingUdonBehaviour(UdonSharp.UdonSharpBehaviour component)
        {
            if (component == null) return false;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(component);
            return udon != null && udon.programSource != null;
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
    }
}
#endif
