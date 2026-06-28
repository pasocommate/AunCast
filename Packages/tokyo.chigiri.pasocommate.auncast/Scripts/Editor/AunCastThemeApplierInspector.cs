#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCastThemeApplier の Inspector カスタムエディタ。
    /// テーマカラー変更時にシーン内 UI へ即時プレビュー反映するためのボタン等を提供する。
    /// </summary>
    [CustomEditor(typeof(PasocomMate.AunCast.AunCastThemeApplier))]
    public class AunCastThemeApplierInspector : Editor
    {
        private const string USER_CONTENT_PATH = "PortablePanel/ContentScaler/PortableContentArea/UserContent";
        private const string STAFF_CONTENT_PATH = "PortablePanel/ContentScaler/PortableContentArea/StaffContent";
        private const string USER_PADDED_PATH = USER_CONTENT_PATH + "/UserPadded";
        private const string STAFF_PADDED_PATH = STAFF_CONTENT_PATH + "/StaffPadded";

        // 自動適用トグルの状態。シーンを汚さないようコンポーネントではなく EditorPrefs に保持する
        private const string AUTO_APPLY_PREF_KEY = "AunCast.ThemeApplier.AutoApply";

        private static bool AutoApplyEnabled
        {
            get => EditorPrefs.GetBool(AUTO_APPLY_PREF_KEY, false);
            set => EditorPrefs.SetBool(AUTO_APPLY_PREF_KEY, value);
        }

        private Editor _themeEditor;
        private bool _themeEditorExpanded;

        private void OnDisable()
        {
            if (_themeEditor != null)
                DestroyImmediate(_themeEditor);
        }

        public override void OnInspectorGUI()
        {
            var applier = (PasocomMate.AunCast.AunCastThemeApplier)target;
            var root = applier != null ? applier.transform : null;

            using (new EditorGUI.DisabledScope(applier == null))
            {
                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool hasPortable = root != null && root.Find(USER_CONTENT_PATH) != null;
                    using (new EditorGUI.DisabledScope(!hasPortable))
                    {
                        if (GUILayout.Button(
                            AunCastEditorLocalization.Localize("視聴者表示", "Show Viewer"), GUILayout.Height(28)))
                            SwitchContentView(root, showStaff: false);
                        if (GUILayout.Button(
                            AunCastEditorLocalization.Localize("スタッフ表示", "Show Staff"), GUILayout.Height(28)))
                            SwitchContentView(root, showStaff: true);
                    }
                }

                EditorGUILayout.Space(4);

                using (new EditorGUI.DisabledScope(applier.theme == null))
                {
                    if (GUILayout.Button(
                        AunCastEditorLocalization.Localize("テーマを適用", "Apply Theme"), GUILayout.Height(32)))
                    {
                        RecordUndoTargets(applier.transform);
                        ApplyAll(applier);
                        Debug.Log("[AunCast] テーマを適用しました");
                    }
                }

                EditorGUILayout.Space(2);
                var autoApplyLabel = new GUIContent(
                    AunCastEditorLocalization.Localize(
                        "自動適用（編集時に即反映・Undo 不可）", "Auto Apply (on edit, no Undo)"),
                    AunCastEditorLocalization.Localize(
                        "ON にするとテーマ値の編集が即反映されますが、自動適用は Undo に記録されません。"
                        + "元に戻せる形で適用したい場合は「テーマを適用」ボタンを使ってください。",
                        "When enabled, theme edits apply instantly but auto-apply is NOT recorded in Undo. "
                        + "Use the \"Apply Theme\" button if you need an undoable apply."));
                EditorGUI.BeginChangeCheck();
                bool auto = EditorGUILayout.ToggleLeft(autoApplyLabel, AutoApplyEnabled);
                if (EditorGUI.EndChangeCheck())
                    AutoApplyEnabled = auto;
            }

            EditorGUILayout.Space();

            // テーマ参照やインライン編集の変更を検知し、自動適用が ON なら即反映する
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            changed |= DrawInlineThemeEditor(applier);

            if (changed && AutoApplyEnabled && applier.theme != null)
                ApplyAll(applier);
        }

        /// <summary>テーマの見た目反映と Udon Proxy への反映をまとめて実行する。</summary>
        private static void ApplyAll(PasocomMate.AunCast.AunCastThemeApplier applier)
        {
            applier.ApplyTheme(applier.transform);
            ApplyThemeToUdonProxies(applier.transform, applier.theme);
        }

        /// <summary>インライン表示したテーマエディタを描画し、値が変更されたら true を返す。</summary>
        private bool DrawInlineThemeEditor(PasocomMate.AunCast.AunCastThemeApplier applier)
        {
            if (applier.theme == null)
            {
                if (_themeEditor != null)
                {
                    DestroyImmediate(_themeEditor);
                    _themeEditor = null;
                }
                return false;
            }

            if (_themeEditor == null || _themeEditor.target != applier.theme)
            {
                if (_themeEditor != null)
                    DestroyImmediate(_themeEditor);
                _themeEditor = CreateEditor(applier.theme);
            }

            EditorGUILayout.Space(8);

            _themeEditorExpanded = EditorGUILayout.Foldout(_themeEditorExpanded,
                $"Theme: {applier.theme.name}", true, EditorStyles.foldoutHeader);

            bool changed = false;
            if (_themeEditorExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                _themeEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(applier.theme);
                    changed = true;
                }
                EditorGUI.indentLevel--;
            }
            return changed;
        }

        private static void SwitchContentView(Transform root, bool showStaff)
        {
            var userCg = root.Find(USER_CONTENT_PATH)?.GetComponent<CanvasGroup>();
            var staffCg = root.Find(STAFF_CONTENT_PATH)?.GetComponent<CanvasGroup>();
            if (userCg == null || staffCg == null) return;

            Undo.RecordObject(userCg, "Switch AunCast Content View");
            Undo.RecordObject(staffCg, "Switch AunCast Content View");

            SetCanvasGroupVisible(userCg, !showStaff);
            SetCanvasGroupVisible(staffCg, showStaff);

            var selectPath = showStaff ? STAFF_PADDED_PATH : USER_PADDED_PATH;
            var selectTarget = root.Find(selectPath);
            if (selectTarget != null)
                Selection.activeGameObject = selectTarget.gameObject;
        }

        private static void SetCanvasGroupVisible(CanvasGroup cg, bool visible)
        {
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }

        private static void RecordUndoTargets(Transform root)
        {
            var targets = new List<UnityEngine.Object>();
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                targets.Add(img);
            foreach (var btn in root.GetComponentsInChildren<Button>(true))
                targets.Add(btn);
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                targets.Add(tmp);
            foreach (var udon in root.GetComponentsInChildren<UdonBehaviour>(true))
                targets.Add(udon);
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                targets.Add(mr);
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
                targets.Add(raw);

            // ApplyPortableContentSize で書き換える RectTransform / BoxCollider も Undo 対象に含める
            var panel = root.Find("PortablePanel");
            if (panel != null)
            {
                targets.Add(panel);
                var box = panel.GetComponent<BoxCollider>();
                if (box != null)
                    targets.Add(box);
                var scaler = panel.Find("ContentScaler");
                if (scaler != null)
                    targets.Add(scaler);
                // FitVideoScreenToArea で書き換える VideoScreen の RectTransform も Undo 対象に含める
                var videoScreen = panel.Find(
                    "ContentScaler/PortableContentArea/UserContent/UserPadded/VideoScreenArea/VideoScreen");
                if (videoScreen != null)
                    targets.Add(videoScreen);
            }

            Undo.RecordObjects(targets.ToArray(), "Apply AunCast Theme");
        }

        public static void ApplyThemeToUdonProxies(Transform root, PasocomMate.AunCast.AunCastTheme theme)
        {
            if (theme == null) return;

            ApplyThemeToProxy<UserStatusPanel>(root, "PortablePanel", proxy =>
            {
                SetSerializedField(proxy, "userBackgroundColor", theme.userBackgroundColor);
                SetSerializedField(proxy, "staffBackgroundColor", theme.staffBackgroundColor);
                SetSerializedField(proxy, "disabledButtonLabelAlpha", theme.disabledButtonLabelAlpha);
            });

            ApplyThemeToAllProxies<WallControlPanel>(root, proxy =>
            {
                SetSerializedField(proxy, "disabledButtonLabelAlpha", theme.disabledButtonLabelAlpha);
            });

            ApplyThemeToProxy<HudProgressOverlay>(root, "HudProgressOverlay", proxy =>
            {
                SetSerializedField(proxy, "vrLocalOffset", theme.hudProgressLocalOffset);
            });
        }

        private static void ApplyThemeToProxy<T>(Transform root, string path, Action<T> apply)
            where T : UdonSharpBehaviour
        {
            var t = root.Find(path);
            if (t == null) return;
            var proxy = t.GetComponent<T>();
            if (proxy == null) return;
            apply(proxy);
            UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        }

        private static void ApplyThemeToAllProxies<T>(Transform root, Action<T> apply)
            where T : UdonSharpBehaviour
        {
            if (root == null) return;

            var proxies = root.GetComponentsInChildren<T>(true);
            foreach (var proxy in proxies)
            {
                if (proxy == null) continue;
                apply(proxy);
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
            }
        }

        private static void SetSerializedField(UnityEngine.Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[AunCast] Serialized field not found: {target.name}.{fieldName}");
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Float:
                    prop.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = value as UnityEngine.Object;
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value?.ToString() ?? string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = (Color)value;
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = (Vector2)value;
                    break;
                default:
                    Debug.LogWarning($"[AunCast] Unsupported SerializedPropertyType: {prop.propertyType} for {fieldName}");
                    break;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
