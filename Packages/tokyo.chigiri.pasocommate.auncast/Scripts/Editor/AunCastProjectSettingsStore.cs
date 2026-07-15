#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCast のプロジェクト単位設定を保存する。
    /// 保存先は ProjectSettings/AunCastProjectSettings.json。
    ///
    /// Unity のシリアライズドファイル API（ScriptableSingleton /
    /// SaveToSerializedFileAndForget）は型解決がドメインリロード直後に失敗し、
    /// 小さな状態がたびたび既定値へ戻る不具合があったため、素のテキストを File IO で
    /// 直接読み書きする。型解決に依存しないため確実で、VCS にコミットすればチーム
    /// 単位で共有できる。
    /// </summary>
    internal static class AunCastProjectSettingsStore
    {
        private const string SETTINGS_FILE_NAME = "AunCastProjectSettings.json";
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        // JsonUtility 用のデータ器（素の Serializable クラス。ScriptableObject ではない）。
        [Serializable]
        private sealed class ProjectSettingsData
        {
            public TermsData terms = new TermsData();
        }

        [Serializable]
        private sealed class TermsData
        {
            // 旧設定形式との読み取り互換性のため残す。判定には使用しない。
            public int agreedMajorVersion = -1;

            // 同意時のパッケージ版を監査用に保持する。判定には使用しない。
            public string agreedVersion = string.Empty;
            public string agreedAtUtc = string.Empty;
            public string agreedTermsHash = string.Empty;
        }

        // ドメインリロードで null になり、次回 Load() でファイルから再構築される。
        private static ProjectSettingsData _cache;

        private static string GetFilePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "ProjectSettings", SETTINGS_FILE_NAME);
        }

        private static ProjectSettingsData Load()
        {
            if (_cache != null) return _cache;

            try
            {
                string path = GetFilePath();
                if (File.Exists(path))
                {
                    ProjectSettingsData data =
                        JsonUtility.FromJson<ProjectSettingsData>(File.ReadAllText(path, Utf8WithoutBom));
                    if (data != null)
                    {
                        EnsureDefaults(data);
                        _cache = data;
                        return _cache;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AunCast] 同意状態の読み込みに失敗しました: {e.Message}");
            }

            _cache = new ProjectSettingsData();
            return _cache;
        }

        private static void EnsureDefaults(ProjectSettingsData data)
        {
            if (data.terms == null)
                data.terms = new TermsData();
        }

        /// <summary>
        /// ハッシュ判定へ移行した時点（v5.1.2）の規約ファイルのハッシュ。
        /// 旧形式の同意記録の引き継ぎは、現在の規約がこのハッシュと一致する場合だけに限る。
        /// 規約本文を改訂したらこの定数は更新せず、改訂後の規約には必ず再同意を求める。
        /// </summary>
        private const string MIGRATABLE_TERMS_HASH =
            "151689DBB71E602051EC63CB00042E2F196EE2331930A5703E5655B677AE4AE1";

        /// <summary>指定した規約ファイルのハッシュに対して同意済みかを返す。</summary>
        internal static bool HasConsented(string currentTermsHash)
        {
            if (string.IsNullOrEmpty(currentTermsHash)) return false;

            return string.Equals(
                Load().terms.agreedTermsHash, currentTermsHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// 旧形式（ハッシュ未記録）の同意記録を現在の形式へ引き継ぐ。移行した場合だけ true を返す。
        /// 引き継ぎ先は移行時点の規約に固定するため、規約本文が改訂されていれば移行せず再同意を求める。
        /// </summary>
        internal static bool TryMigrateLegacyConsent(string currentTermsHash)
        {
            if (string.IsNullOrEmpty(currentTermsHash)) return false;
            if (!string.Equals(currentTermsHash, MIGRATABLE_TERMS_HASH, StringComparison.Ordinal))
                return false;

            ProjectSettingsData data = Load();
            TermsData terms = data.terms;
            if (!string.IsNullOrEmpty(terms.agreedTermsHash)) return false;
            if (string.IsNullOrEmpty(terms.agreedVersion)) return false;

            terms.agreedTermsHash = currentTermsHash;
            Save(data);
            return true;
        }

        /// <summary>同意を記録して ProjectSettings へ永続化する。</summary>
        internal static void SetConsented(string currentTermsHash, string fullVersion)
        {
            if (string.IsNullOrEmpty(currentTermsHash))
            {
                Debug.LogError("[AunCast] 規約ハッシュが空のため、同意状態を保存できません。");
                return;
            }

            ProjectSettingsData data = Load();
            data.terms.agreedVersion = fullVersion ?? string.Empty;
            data.terms.agreedAtUtc = DateTime.UtcNow.ToString("o");
            data.terms.agreedTermsHash = currentTermsHash;

            Save(data);
        }

        private static void Save(ProjectSettingsData data)
        {
            try
            {
                string path = GetFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data, true), Utf8WithoutBom);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AunCast] 同意状態の保存に失敗しました: {e.Message}");
            }
        }

        /// <summary>記録済みの同意バージョン（未同意なら空文字）。</summary>
        internal static string GetAgreedVersion()
        {
            return Load().terms.agreedVersion;
        }
    }
}
#endif
