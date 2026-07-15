using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PasocomMate.AunCast.Dev.Editor
{
    /// <summary>選択した RectTransform を指定した増分だけ移動するウィンドウ。</summary>
    public sealed class AunCastRectTransformMoveWindow : EditorWindow
    {
        private const string MENU_PATH = "Tools/PasocomMate/AunCast Dev/Rect Transform 一括移動";
        private const string UNDO_NAME = "Move Rect Transforms";

        private Vector2 _increment;

        [MenuItem(MENU_PATH)]
        private static void Open()
        {
            GetWindow<AunCastRectTransformMoveWindow>("Rect Transform 一括移動");
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            RectTransform[] targets = GetSelectedRectTransforms();

            EditorGUILayout.LabelField("選択した Rect Transform を移動", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "指定したX・Yの増分を、選択中の Rect Transform の Anchored Position に加算します。",
                MessageType.Info);

            _increment = EditorGUILayout.Vector2Field("増分 (X, Y)", _increment);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"対象: {targets.Length} 件");
            using (new EditorGUI.DisabledScope(targets.Length == 0))
            {
                if (GUILayout.Button($"{targets.Length} 件を移動"))
                    MoveTargets(targets);
            }

            if (targets.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Hierarchy または Scene 上で Rect Transform を1つ以上選択してください。",
                    MessageType.Warning);
            }
        }

        private static RectTransform[] GetSelectedRectTransforms()
        {
            var targets = new List<RectTransform>();
            foreach (Transform transform in Selection.transforms)
            {
                if (transform is RectTransform rectTransform)
                    targets.Add(rectTransform);
            }

            return targets.ToArray();
        }

        private void MoveTargets(RectTransform[] targets)
        {
            Undo.RecordObjects(targets, UNDO_NAME);
            foreach (RectTransform target in targets)
            {
                target.anchoredPosition += _increment;
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }

            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
