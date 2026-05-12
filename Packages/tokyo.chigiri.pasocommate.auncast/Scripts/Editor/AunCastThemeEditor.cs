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
    /// AunCastTheme の Inspector カスタムエディタ。
    /// テーマカラー変更時にシーン内 UI へ即時プレビュー反映するためのボタン等を提供する。
    /// </summary>
    [CustomEditor(typeof(PasocomMate.AunCast.AunCastTheme))]
    public class AunCastThemeEditor : Editor
    {
        private const string USER_CONTENT_PATH = "PortablePanel/ContentScaler/PortableContentArea/UserContent";
        private const string STAFF_CONTENT_PATH = "PortablePanel/ContentScaler/PortableContentArea/StaffContent";
        private const string USER_PADDED_PATH = USER_CONTENT_PATH + "/UserPadded";
        private const string STAFF_PADDED_PATH = STAFF_CONTENT_PATH + "/StaffPadded";

        public override void OnInspectorGUI()
        {
            var theme = (PasocomMate.AunCast.AunCastTheme)target;
            var root = theme != null ? theme.transform : null;

            // --- ボタン類 ---
            using (new EditorGUI.DisabledScope(theme == null))
            {
                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool hasPortable = root != null && root.Find(USER_CONTENT_PATH) != null;
                    using (new EditorGUI.DisabledScope(!hasPortable))
                    {
                        if (GUILayout.Button("Show Viewer", GUILayout.Height(28)))
                            SwitchContentView(root, showStaff: false);
                        if (GUILayout.Button("Show Staff", GUILayout.Height(28)))
                            SwitchContentView(root, showStaff: true);
                    }
                }

                EditorGUILayout.Space(4);

                if (GUILayout.Button("Apply Theme", GUILayout.Height(32)))
                {
                    RecordUndoTargets(theme.transform);
                    theme.ApplyTheme(theme.transform);
                    ApplyThemeToUdonProxies(theme.transform, theme);
                    Debug.Log("[AunCast] テーマを適用しました");
                }
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
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
            // ビデオスクリーン: sharedMaterial / RawImage.material / RawImage.texture の差し替えを Undo 可能にする
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                targets.Add(mr);
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
                targets.Add(raw);
            Undo.RecordObjects(targets.ToArray(), "Apply AunCast Theme");
        }

        public static void ApplyThemeToUdonProxies(Transform root, PasocomMate.AunCast.AunCastTheme theme)
        {
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
                SetSerializedField(proxy, "localOffset", theme.hudProgressLocalOffset);
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
