using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCast プレハブをシーンへ配置するためのメニュー。
    /// Tools > PasocomMate > AunCast 階層に登録する。
    /// </summary>
    internal static class AunCastMenu
    {
        // AunCast.prefab の GUID（AunCast.prefab.meta より）。パス文字列は使わず GUID で特定する。
        private const string AUNCAST_PREFAB_GUID = "0d617877712537147a584eb2a1ef735c";

        [MenuItem("Tools/PasocomMate/AunCast/Create AunCast", false, 0)]
        [MenuItem("GameObject/PasocomMate/AunCast/Create AunCast", false, 10)]
        private static void CreateAunCast()
        {
            var prefab = AunCastEditorAssetUtility.LoadAssetByGuid<GameObject>(AUNCAST_PREFAB_GUID);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "AunCast",
                    AunCastEditorLocalization.Localize(
                        "AunCast.prefab が見つかりませんでした。パッケージが正しくインポートされているか確認してください。",
                        "AunCast.prefab was not found. Make sure the package is imported correctly."),
                    "OK");
                return;
            }

            // AunCast を 1 シーンに 2 つ以上置くと動作が破綻するため、既に存在する場合は追加せず通知する。
            if (SceneAlreadyHasAunCast(prefab))
            {
                EditorUtility.DisplayDialog(
                    "AunCast",
                    AunCastEditorLocalization.Localize(
                        "このシーンにはすでに AunCast が配置されています。1 つのシーンに 2 つ以上配置すると正しく動作しません。",
                        "An AunCast instance already exists in this scene. Placing two or more in a single scene will break its behavior."),
                    "OK");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return;

            // シーン原点に配置する。
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Undo.RegisterCreatedObjectUndo(
                instance,
                AunCastEditorLocalization.Localize("AunCast を作成", "Create AunCast"));

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }

        // 同じプレハブから生成されたインスタンスがシーン内に存在するか判定する。
        private static bool SceneAlreadyHasAunCast(GameObject prefab)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (source == prefab) return true;
            }
            return false;
        }
    }
}
