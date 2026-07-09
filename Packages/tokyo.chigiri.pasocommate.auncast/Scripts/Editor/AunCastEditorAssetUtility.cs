using UnityEditor;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// GUID 経由のアセットロードなど、エディタ共通のアセットユーティリティ。
    /// </summary>
    internal static class AunCastEditorAssetUtility
    {
        // アセットはパス文字列リテラルではなく GUID で特定する（プロジェクト方針）。
        public static T LoadAssetByGuid<T>(string guid) where T : UnityEngine.Object
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }
    }
}
