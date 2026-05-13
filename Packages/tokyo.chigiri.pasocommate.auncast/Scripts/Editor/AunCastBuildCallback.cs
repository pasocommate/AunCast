#if UNITY_EDITOR
using UdonSharp;
using UdonSharpEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// ビルド時に VRC_SceneDescriptor の capacity を StaffControlPanel.instanceCapacity へ自動注入する。
    /// AunCastSettings.instanceCapacity が 0 の場合のみ発動し、明示指定されている場合はスキップする。
    /// </summary>
    public class AunCastBuildCallback : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var settings = FindInScene<AunCastSettings>(scene);
            if (settings == null) return;

            int capacity = settings.instanceCapacity;
            if (capacity <= 0)
            {
                capacity = GetSceneDescriptorCapacity(scene);
                if (capacity <= 0)
                {
                    Debug.LogWarning("[AunCast] VRC_SceneDescriptor の capacity が 0 です。StaffControlPanel の instanceCapacity を解決できません。");
                    return;
                }
            }

            var root = settings.transform;
            var staffPanels = root.GetComponentsInChildren<StaffControlPanel>(true);
            foreach (var panel in staffPanels)
            {
                var so = new UnityEditor.SerializedObject(panel);
                var prop = so.FindProperty("instanceCapacity");
                if (prop == null) continue;
                prop.intValue = capacity;
                so.ApplyModifiedPropertiesWithoutUndo();
                UdonSharpEditorUtility.CopyProxyToUdon(panel);
            }

            Debug.Log($"[AunCast] ビルド時に instanceCapacity = {capacity} を StaffControlPanel へ注入しました。");
        }

        private static int GetSceneDescriptorCapacity(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                var descriptor = go.GetComponentInChildren<VRC.SDKBase.VRC_SceneDescriptor>(true);
                if (descriptor == null) continue;

                var so = new UnityEditor.SerializedObject(descriptor);
                var prop = so.FindProperty("capacity");
                if (prop != null)
                    return prop.intValue;
            }
            return 0;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                var found = go.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
