#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    internal static class AunCastManagedSettingsInspectorUtility
    {
        public readonly struct ManagedPropertyGroup
        {
            public readonly string FoldoutLabelJa;
            public readonly string FoldoutLabelEn;
            public readonly string[] PropertyNames;

            public ManagedPropertyGroup(string foldoutLabelJa, string foldoutLabelEn, string[] propertyNames)
            {
                FoldoutLabelJa = foldoutLabelJa;
                FoldoutLabelEn = foldoutLabelEn;
                PropertyNames = propertyNames;
            }
        }

        public static void DrawPropertiesWithManagedFoldouts(
            SerializedObject serializedObject,
            string[] excludedPropertyNames,
            ManagedPropertyGroup[] groups,
            bool[] showGroups)
        {
            var property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (Contains(excludedPropertyNames, property.name)) continue;
                if (FindGroupIndex(groups, property.name) >= 0) continue;

                DrawProperty(property, false);
            }

            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group.PropertyNames == null || group.PropertyNames.Length == 0) continue;
                if (!HasAnyProperty(serializedObject, group.PropertyNames)) continue;

                EditorGUILayout.Space(8f);
                DrawManagedGroupFoldout(
                    serializedObject,
                    group.FoldoutLabelJa,
                    group.FoldoutLabelEn,
                    group.PropertyNames,
                    ref showGroups[i]);
            }
        }

        /// <summary>
        /// 折りたたみ見出し＋読み取り専用（DisabledScope）で列挙プロパティを描画する。
        /// 明示的にプロパティ名を並べたい呼び出し側（例: AunCastWallControlPanelInspector）で共用する。
        /// </summary>
        public static void DrawManagedGroupFoldout(
            SerializedObject serializedObject,
            string foldoutLabelJa,
            string foldoutLabelEn,
            string[] propertyNames,
            ref bool show)
        {
            string foldLabel = AunCastEditorLocalization.Localize(foldoutLabelJa, foldoutLabelEn);
            show = EditorGUILayout.Foldout(show, foldLabel, true);
            if (!show) return;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.indentLevel++;
                for (int p = 0; p < propertyNames.Length; p++)
                {
                    var managedProperty = serializedObject.FindProperty(propertyNames[p]);
                    if (managedProperty == null) continue;
                    DrawProperty(managedProperty, true);
                }
                EditorGUI.indentLevel--;
            }
        }

        public static void DrawProperty(SerializedProperty property, bool managedBySettings)
        {
            using (new EditorGUI.DisabledScope(managedBySettings))
            {
                if (managedBySettings)
                {
                    var label = new GUIContent(property.displayName, property.tooltip);
                    EditorGUILayout.PropertyField(property, label, true);
                    return;
                }

                EditorGUILayout.PropertyField(property, true);
            }
        }

        private static bool Contains(string[] values, string value)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value) return true;
            }
            return false;
        }

        private static int FindGroupIndex(ManagedPropertyGroup[] groups, string propertyName)
        {
            if (groups == null) return -1;
            for (int i = 0; i < groups.Length; i++)
            {
                if (Contains(groups[i].PropertyNames, propertyName)) return i;
            }
            return -1;
        }

        private static bool HasAnyProperty(SerializedObject serializedObject, string[] propertyNames)
        {
            if (propertyNames == null) return false;
            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (serializedObject.FindProperty(propertyNames[i]) != null) return true;
            }
            return false;
        }
    }
}
#endif
