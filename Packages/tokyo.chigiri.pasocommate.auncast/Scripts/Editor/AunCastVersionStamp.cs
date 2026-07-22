#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
// UnityEditor.PackageInfo と衝突するため、明示的にエイリアスで PackageManager 側を指す。
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// シーン保存時に、そのシーン内の各 AunCastSettings へ現在のパッケージバージョンを
    /// _lastOpenedVersion として刻む。将来のバージョンアップ移行判定の基準にするための記録で、
    /// ここでは検知や警告は行わない。
    /// Play 遷移で配線更新する AunCastAutoRewire と同じ [InitializeOnLoad] + シーン走査の形に揃える。
    /// </summary>
    [InitializeOnLoad]
    internal static class AunCastVersionStamp
    {
        private const string LAST_OPENED_VERSION_PROPERTY = "_lastOpenedVersion";

        static AunCastVersionStamp()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        // sceneSaving はディスク書き込み前に呼ばれるため、ここでの変更は保存内容へ反映される。
        private static void OnSceneSaving(Scene scene, string path)
        {
            string current = GetCurrentPackageVersion();
            if (string.IsNullOrEmpty(current)) return;

            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (AunCastSettings settings in root.GetComponentsInChildren<AunCastSettings>(true))
                    StampLastOpenedVersion(settings, current);
        }

        // 値が既に一致する場合は書き込まない。保存のたびにシーンを Dirty 化しないため。
        private static void StampLastOpenedVersion(AunCastSettings settings, string current)
        {
            var so = new SerializedObject(settings);
            SerializedProperty prop = so.FindProperty(LAST_OPENED_VERSION_PROPERTY);
            if (prop == null) return;
            if (prop.stringValue == current) return;
            prop.stringValue = current;
            // 保存処理中の書き込みのため Undo には積まない。
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // パッケージバージョンをアセンブリ経由で静的に解決する。Editor インスタンスは不要。
        private static string GetCurrentPackageVersion()
        {
            PackageInfo info = PackageInfo.FindForAssembly(typeof(AunCastSettings).Assembly);
            return info != null ? info.version : string.Empty;
        }
    }
}
#endif
