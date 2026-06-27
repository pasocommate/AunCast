#if UNITY_EDITOR
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// ビルド時に EventBus / publisher / subscriber と Resync 設定を最新化する。
    /// </summary>
    public class AunCastBuildCallback : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var settings = FindInScene<AunCastSettings>(scene);
            if (settings == null) return;

            // ビルド・アップロード前に EventBus / publisher / subscriber の参照を最新化する。
            // recordUndo: false なので新規 Hub の自動作成は走らず、既存配線の整合更新のみ。
            AunCastSettingsInspector.RewireEventHubAndConsumers(
                settings.transform, recordUndo: false, writeLog: false);
            AunCastSettingsInspector.ApplyResyncSettingsToScene(settings.transform, settings);
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
