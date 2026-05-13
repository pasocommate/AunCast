using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;

namespace PasocomMate.AunCast.Internal
{
    [CustomEditor(typeof(WallControlPanel))]
    internal class WallControlPanelInspector : Editor
    {
        private static readonly string[] READONLY_PROPERTY_NAMES =
        {
            "controller",
            "staffPanel",
            "portablePanel",
            "userCanvasGroup",
            "staffCanvasGroup",
            "sharedCanvasGroup",
            "resyncOnlyCanvasGroup",
            "crossfadeDuration",
            "resyncOnlyButton",
            "switchViewButtonLabel",
            "switchViewButton",
            "spawnPanelButtonRect",
            "passcodeDisplay",
            "unlockPasscode",
            "userResyncButton",
            "userRebootButton",
            "disabledButtonLabelAlpha",
            "vrGestureGroup",
            "gestureDoubleTriggerToggle",
            "gestureBothTriggersToggle",
            "gestureRightStickUpToggle",
            "desktopGestureGroup",
            "desktopTabDoubleTapToggle",
            "desktopF5DoubleTapToggle",
            "desktopEscHoldToggle",
            "wallNearDistance",
            "wallFarDistance",
        };

        private SerializedProperty _disablePasscodeViewSwitchButtonProperty;
        private SerializedProperty _spawnPanelButtonRectProperty;
        private bool _showReadonlyProperties;

        private void OnEnable()
        {
            _disablePasscodeViewSwitchButtonProperty = serializedObject.FindProperty("disablePasscodeViewSwitchButton");
            _spawnPanelButtonRectProperty = serializedObject.FindProperty("spawnPanelButtonRect");
            TryAutoAssignSpawnPanelButtonRect();
        }

        public override void OnInspectorGUI()
        {
            AunCastInspectorBanner.Draw(this);
            if (UdonSharpGUI.DrawProgramSource(target, false)) return;

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                _disablePasscodeViewSwitchButtonProperty,
                new GUIContent(AunCastEditorLocalization.Localize(
                    "パスコード入力画面切替ボタンを無効化",
                    "Disable Passcode View Switch Button")));
            bool layoutToggleChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(8f);
            DrawReadonlyProperties();

            bool changed = serializedObject.ApplyModifiedProperties();
            if (!layoutToggleChanged && !changed) return;

            ApplyLayoutToTargets();
        }

        private void TryAutoAssignSpawnPanelButtonRect()
        {
            if (_spawnPanelButtonRectProperty == null) return;
            if (_spawnPanelButtonRectProperty.objectReferenceValue != null) return;
            if (serializedObject.isEditingMultipleObjects) return;

            var panel = target as WallControlPanel;
            if (panel == null) return;

            RectTransform[] rects = panel.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] == null) continue;
                if (rects[i].name != "SpawnPanelButton") continue;
                _spawnPanelButtonRectProperty.objectReferenceValue = rects[i];
                return;
            }
        }

        private void DrawReadonlyProperties()
        {
            string foldLabel = AunCastEditorLocalization.Localize(
                "内部プロパティ（変更不可）",
                "Internal Properties (Read Only)");
            _showReadonlyProperties = EditorGUILayout.Foldout(_showReadonlyProperties, foldLabel, true);
            if (!_showReadonlyProperties) return;

            string help = AunCastEditorLocalization.Localize(
                "AunCastSettings から反映される値と、通常直接変更しない配線項目を表示しています。",
                "This section shows values applied from AunCastSettings and wiring fields that are not usually edited directly.");
            EditorGUILayout.HelpBox(help, MessageType.None);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < READONLY_PROPERTY_NAMES.Length; i++)
                {
                    var property = serializedObject.FindProperty(READONLY_PROPERTY_NAMES[i]);
                    if (property == null) continue;
                    EditorGUILayout.PropertyField(property, true);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void ApplyLayoutToTargets()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var panel = targets[i] as WallControlPanel;
                if (panel == null) continue;

                var state = ReadLayoutState(panel);
                Undo.RecordObjects(state.RecordTargets, "Update WallControlPanel Shared Layout");

                panel.ApplySharedButtonsLayout();

                UdonSharpEditorUtility.CopyProxyToUdon(panel);
                EditorUtility.SetDirty(panel);
                PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
                if (udon != null)
                {
                    EditorUtility.SetDirty(udon);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
                }
                if (state.SwitchButton != null)
                {
                    EditorUtility.SetDirty(state.SwitchButton.gameObject);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(state.SwitchButton.gameObject);
                }
                if (state.SpawnPanelButtonRect != null)
                {
                    EditorUtility.SetDirty(state.SpawnPanelButtonRect);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(state.SpawnPanelButtonRect);
                }
            }
        }

        private static LayoutState ReadLayoutState(WallControlPanel panel)
        {
            var so = new SerializedObject(panel);
            var switchButtonProperty = so.FindProperty("switchViewButton");
            var spawnPanelButtonRectProperty = so.FindProperty("spawnPanelButtonRect");

            Button switchButton = switchButtonProperty != null
                ? switchButtonProperty.objectReferenceValue as Button
                : null;
            RectTransform spawnPanelButtonRect = spawnPanelButtonRectProperty != null
                ? spawnPanelButtonRectProperty.objectReferenceValue as RectTransform
                : null;

            Object[] recordTargets;
            if (switchButton != null && spawnPanelButtonRect != null)
            {
                recordTargets = new Object[] { panel, switchButton.gameObject, spawnPanelButtonRect };
            }
            else if (switchButton != null)
            {
                recordTargets = new Object[] { panel, switchButton.gameObject };
            }
            else if (spawnPanelButtonRect != null)
            {
                recordTargets = new Object[] { panel, spawnPanelButtonRect };
            }
            else
            {
                recordTargets = new Object[] { panel };
            }

            return new LayoutState
            {
                SwitchButton = switchButton,
                SpawnPanelButtonRect = spawnPanelButtonRect,
                RecordTargets = recordTargets,
            };
        }

        private struct LayoutState
        {
            public Button SwitchButton;
            public RectTransform SpawnPanelButtonRect;
            public Object[] RecordTargets;
        }
    }
}
