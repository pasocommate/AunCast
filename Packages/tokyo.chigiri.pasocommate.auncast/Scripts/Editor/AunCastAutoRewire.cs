#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// Play モード遷移時に AunCastEventBus の subscriber 配線を最新化する。
    /// ビルド・アップロード時の自動配線は AunCastBuildCallback 側で実行される。
    /// </summary>
    [InitializeOnLoad]
    internal static class AunCastAutoRewire
    {
        static AunCastAutoRewire()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Play 押下直後・Play 開始前に同期する。
            // ここで配線しておけば、複製や追加直後でも Play 実行時には最新の参照が保証される。
            if (state != PlayModeStateChange.ExitingEditMode) return;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                RewireScene(scene);
            }
        }

        private static void RewireScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var settings = root.GetComponentInChildren<AunCastSettings>(true);
                if (settings == null) continue;
                AunCastSettingsInspector.RewireEventBusAndConsumers(
                    settings.transform, recordUndo: false, writeLog: false);
                AunCastSettingsInspector.ApplyResyncSettingsToScene(settings.transform, settings);
            }
        }
    }
}
#endif
