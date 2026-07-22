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
            // recordUndo: false なので新規 EventBus の自動作成は走らず、既存配線の整合更新のみ。
            AunCastSettingsInspector.RewireEventBusAndConsumers(
                settings.transform, recordUndo: false, writeLog: false);
            // 各コンポーネントへ焼き込む管理設定を最新化する。AunCastSettings は IEditorOnly で
            // ビルドから除外されるため、ここで転写しないとコントローラ側の defaultUrl や
            // 内蔵 AVPro の再生設定等がインスペクタ最終編集時の古い値のまま残る。
            AunCastSettingsInspector.ApplyVideoPlayerSettingsToScene(settings.transform, settings);
            AunCastSettingsInspector.ApplyResyncSettingsToScene(settings.transform, settings);
            AunCastSettingsInspector.ApplyUiSettingsToScene(settings.transform, settings);
            AunCastSettingsInspector.ApplyPlaybackMonitorSettingsToScene(settings.transform, settings);
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
