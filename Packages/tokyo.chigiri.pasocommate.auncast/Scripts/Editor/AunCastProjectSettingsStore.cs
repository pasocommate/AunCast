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
            public int agreedMajorVersion = -1;
            public string agreedVersion = string.Empty;
            public string agreedAtUtc = string.Empty;
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
        /// 指定メジャーバージョンに対して同意済みかを返す。
        /// バージョン不明（負値）のときは誤ブロックを避けるため同意済み扱いとする。
        /// </summary>
        internal static bool HasConsented(int currentMajorVersion)
        {
            if (currentMajorVersion < 0) return true;
            return Load().terms.agreedMajorVersion >= currentMajorVersion;
        }

        /// <summary>同意を記録して ProjectSettings へ永続化する。</summary>
        internal static void SetConsented(int currentMajorVersion, string fullVersion)
        {
            ProjectSettingsData data = Load();
            data.terms.agreedMajorVersion = currentMajorVersion;
            data.terms.agreedVersion = fullVersion ?? string.Empty;
            data.terms.agreedAtUtc = DateTime.UtcNow.ToString("o");

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
