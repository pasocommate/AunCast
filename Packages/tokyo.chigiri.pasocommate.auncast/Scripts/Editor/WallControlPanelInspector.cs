using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;

namespace PasocomMate.AunCast.Internal
{
    [CustomEditor(typeof(WallControlPanel))]
    internal class WallControlPanelInspector : Editor
    {
        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "controller",
            "staffPanel",
            "portablePanel",
            "eventBus",
            "userCanvasGroup",
            "staffCanvasGroup",
            "sharedCanvasGroup",
            "resyncOnlyCanvasGroup",
            "informationCanvasGroup",
            "crossfadeDuration",
            "resyncOnlyButton",
            "switchViewButtonLabel",
            "switchViewButton",
            "spawnPanelButtonRect",
            "informationButtonLabel",
            "passcodeDisplay",
            "userResyncButton",
            "userRebootButton",
            "vrGestureGroup",
            "gestureDoubleTriggerLeftToggle",
            "gestureDoubleTriggerRightToggle",
            "gestureBothTriggersToggle",
            "gestureRightStickUpToggle",
            "desktopGestureGroup",
            "desktopTabDoubleTapToggle",
            "desktopF5DoubleTapToggle",
            "desktopEscHoldToggle",
        };

        private static readonly string[] SETTINGS_PROPERTY_NAMES =
        {
            "unlockPasscode",
            "wallNearDistance",
            "wallFarDistance",
        };

        private static readonly string[] THEME_APPLIER_PROPERTY_NAMES =
        {
            "disabledButtonLabelAlpha",
        };

        private SerializedProperty _disablePasscodeViewSwitchButtonProperty;
        private SerializedProperty _spawnPanelButtonRectProperty;
        private bool _showWiringProperties;
        private bool _showSettingsProperties;
        private bool _showThemeApplierProperties;

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
            DrawSettingsProperties();
            DrawThemeApplierProperties();
            DrawWiringProperties();

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

        private void DrawWiringProperties()
        {
            AunCastManagedSettingsInspectorUtility.DrawManagedGroupFoldout(
                serializedObject,
                "配線対象（変更不可）",
                "Wiring References (Read Only)",
                WIRING_PROPERTY_NAMES,
                ref _showWiringProperties);
        }

        private void DrawSettingsProperties()
        {
            AunCastManagedSettingsInspectorUtility.DrawManagedGroupFoldout(
                serializedObject,
                "共通設定（AunCastSettings 管理下）",
                "Common Settings (Managed by AunCastSettings)",
                SETTINGS_PROPERTY_NAMES,
                ref _showSettingsProperties);
        }

        private void DrawThemeApplierProperties()
        {
            AunCastManagedSettingsInspectorUtility.DrawManagedGroupFoldout(
                serializedObject,
                "テーマ設定（AunCastThemeApplier 管理下）",
                "Theme Settings (Managed by AunCastThemeApplier)",
                THEME_APPLIER_PROPERTY_NAMES,
                ref _showThemeApplierProperties);
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
