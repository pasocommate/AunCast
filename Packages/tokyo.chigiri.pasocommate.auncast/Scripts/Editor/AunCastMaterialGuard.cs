#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// エディタ（ClientSim）での Play 検証時に、Udon スクリプト（映像スクリーンや Background の
    /// Dissolve 等）が共有マテリアルアセットへ直接書き込むことでアセットが汚れ、Edit モード復帰後も
    /// 変更が残る問題への対策。
    ///
    /// Play 直前（アセットがまだクリーンなうち）に対象マテリアルを一時フォルダへ複製退避し、
    /// Edit モード復帰時に <see cref="EditorUtility.CopySerialized"/> で汚れたマテリアルへ
    /// クリーンなシリアライズ状態を丸ごと上書きして実行時変更を破棄する。
    ///
    /// ネイティブ .mat は <c>ImportAsset(ForceUpdate)</c> では実行時変更を破棄できないため、
    /// 再インポートではなく複製→CopySerialized 方式を採る。書き込み側（U# スクリプト）には
    /// 一切触れず、<c>#if UNITY_EDITOR</c> でビルド成果物にも含まれないエディタ限定の保険。
    /// </summary>
    [InitializeOnLoad]
    internal static class AunCastMaterialGuard
    {
        /// <summary>Play をまたいで GUID リストを保持するための SessionState キー（ドメインリロードで消えないため）。</summary>
        private const string SESSION_KEY = "AunCast.MaterialGuard.Guids";

        /// <summary>クリーンなマテリアルの複製を一時退避するフォルダ。</summary>
        private const string TEMP_FOLDER = "Assets/AunCastMaterialGuardTemp";

        static AunCastMaterialGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                // Play 押下直後・Play 開始前。アセットがまだ汚れていないこの時点で複製退避する。
                case PlayModeStateChange.ExitingEditMode:
                    SnapshotTargetMaterials();
                    break;
                // Edit モードへ復帰した直後。退避した複製で実行時変更を打ち消す。
                case PlayModeStateChange.EnteredEditMode:
                    RestoreTargetMaterials();
                    break;
            }
        }

        /// <summary>対象マテリアルを収集し、クリーンなうちに一時フォルダへ複製する。</summary>
        private static void SnapshotTargetMaterials()
        {
            // 前回 Play が異常終了して取り残された複製があれば掃除しておく。
            DeleteTempFolder();

            var guids = CollectTargetGuids();
            if (guids.Count == 0)
            {
                SessionState.EraseString(SESSION_KEY);
                return;
            }

            EnsureTempFolder();
            foreach (var guid in guids)
            {
                string src = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(src)) continue;
                AssetDatabase.CopyAsset(src, TempPathFor(guid));
            }

            // GUID をカンマ区切りで保存。SessionState はドメインリロードをまたいで保持される。
            SessionState.SetString(SESSION_KEY, string.Join(",", guids));
        }

        /// <summary>退避した複製で各マテリアルの実行時変更を打ち消し、一時フォルダを片付ける。</summary>
        private static void RestoreTargetMaterials()
        {
            string stored = SessionState.GetString(SESSION_KEY, string.Empty);
            SessionState.EraseString(SESSION_KEY);
            if (string.IsNullOrEmpty(stored)) return;

            try
            {
                foreach (var guid in stored.Split(','))
                {
                    if (string.IsNullOrEmpty(guid)) continue;

                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    var dirty = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var clean = LoadTempSnapshot(guid);
                    if (dirty == null || clean == null) continue;

                    // Play 中の実行時変更を、退避したクリーンなシリアライズ状態で上書きして破棄する。
                    // CopySerialized は m_Name も上書きするため、退避先ファイル名（GUID）に
                    // 引きずられてマテリアル名が壊れる。復元後にファイル名と一致する本来の名前へ戻す。
                    string assetName = Path.GetFileNameWithoutExtension(path);
                    EditorUtility.CopySerialized(clean, dirty);
                    dirty.name = assetName;
                    EditorUtility.SetDirty(dirty);
                    AssetDatabase.SaveAssetIfDirty(dirty);
                }
            }
            finally
            {
                DeleteTempFolder();
            }
        }

        /// <summary>
        /// ロード済み全シーンの AunCast リグ配下から、実行時に書き換わりうるマテリアルアセットの
        /// GUID を収集する。映像スクリーンだけでなく、Background の Dissolve マテリアルや HUD・
        /// ボタン等、Udon スクリプトが共有マテリアルへ書き込む全経路を取りこぼさないよう、
        /// Renderer / Image / RawImage の参照マテリアルを一括で対象にする。
        /// 対象は AunCastSettings を含むルート配下に限定し、無関係シーンの全マテリアル複製を避ける。
        /// </summary>
        private static HashSet<string> CollectTargetGuids()
        {
            var guids = new HashSet<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    // AunCast リグ（AunCastSettings を持つ階層）のみを走査対象にする。
                    if (root.GetComponentInChildren<AunCastSettings>(true) == null) continue;

                    // MeshRenderer 等は sharedMaterials 全枠が書き換え対象になりうる。
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        foreach (var mat in renderer.sharedMaterials)
                            AddGuidIfAsset(mat, guids);

                    // UI Graphic は material に色・_Gamma・_FlipY・_DissolveThreshold 等を書き込む。
                    foreach (var img in root.GetComponentsInChildren<Image>(true))
                        AddGuidIfAsset(img.material, guids);
                    foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
                        AddGuidIfAsset(raw.material, guids);
                }
            }

            return guids;
        }

        /// <summary>永続アセットのマテリアルのみ GUID を登録する。ビルトインや実行時インスタンスは除外。</summary>
        private static void AddGuidIfAsset(Material mat, HashSet<string> guids)
        {
            if (mat == null) return;
            if (!EditorUtility.IsPersistent(mat)) return;

            string path = AssetDatabase.GetAssetPath(mat);
            // ビルトイン（例: Resources/unity_builtin_extra）や非 .mat は復元不可・不要なので除外する。
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".mat")) return;

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
                guids.Add(guid);
        }

        /// <summary>一時フォルダに退避したクリーンな複製を GUID 経由でロードする。</summary>
        private static Material LoadTempSnapshot(string guid)
        {
            string tempGuid = AssetDatabase.AssetPathToGUID(TempPathFor(guid));
            if (string.IsNullOrEmpty(tempGuid)) return null;
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(tempGuid));
        }

        private static string TempPathFor(string guid)
        {
            return $"{TEMP_FOLDER}/{guid}.mat";
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER))
                AssetDatabase.CreateFolder("Assets", "AunCastMaterialGuardTemp");
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TEMP_FOLDER))
                AssetDatabase.DeleteAsset(TEMP_FOLDER);
        }
    }
}
#endif
