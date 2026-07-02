#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// Play 停止直後に、各 ThemeApplier へ「表示中ビュー（viewer/staff）に合った Background 色」を
    /// 再適用させる。
    ///
    /// 実行時のクロスフェードで Background 色が書き換わったまま Stop すると、シーン復元では
    /// 表示状態（viewer/staff）と色が食い違って残ることがある（prefab override 由来）。
    /// そこで停止直後に、いま表示されているビューをシーンから読み取り、それにふさわしい色へ
    /// 当て直すことで整合を取る。override の revert等はせず、色の整合だけを担う軽量な仕組み。
    /// </summary>
    [InitializeOnLoad]
    internal static class AunCastThemePreviewNormalizer
    {
        static AunCastThemePreviewNormalizer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                    foreach (var applier in root.GetComponentsInChildren<AunCastThemeApplier>(true))
                        applier.SyncBackgroundColorToCurrentView();
            }
        }
    }
}
#endif
