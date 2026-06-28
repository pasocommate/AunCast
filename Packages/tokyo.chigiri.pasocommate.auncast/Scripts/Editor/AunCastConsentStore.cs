#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCast 利用規約への同意状態をプロジェクト単位で保存する。
    /// 保存先は ProjectSettings/AunCastConsent.json。
    ///
    /// Unity のシリアライズドファイル API（ScriptableSingleton /
    /// SaveToSerializedFileAndForget）は型解決がドメインリロード直後に失敗し、
    /// 同意状態がたびたび既定値へ戻る不具合があったため、素のテキストを File IO で
    /// 直接読み書きする。型解決に依存しないため確実で、VCS にコミットすればチーム
    /// 単位で共有できる。
    /// </summary>
    internal static class AunCastConsentStore
    {
        private const string CONSENT_FILE_NAME = "AunCastConsent.json";

        // JsonUtility 用のデータ器（素の Serializable クラス。ScriptableObject ではない）。
        [Serializable]
        private sealed class ConsentData
        {
            public int agreedMajorVersion = -1;
            public string agreedVersion = string.Empty;
            public string agreedAtUtc = string.Empty;
        }

        // ドメインリロードで null になり、次回 Load() でファイルから再構築される。
        private static ConsentData _cache;

        private static string GetFilePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "ProjectSettings", CONSENT_FILE_NAME);
        }

        private static ConsentData Load()
        {
            if (_cache != null) return _cache;

            try
            {
                string path = GetFilePath();
                if (File.Exists(path))
                {
                    ConsentData data = JsonUtility.FromJson<ConsentData>(File.ReadAllText(path));
                    if (data != null)
                    {
                        _cache = data;
                        return _cache;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AunCast] 同意状態の読み込みに失敗しました: {e.Message}");
            }

            _cache = new ConsentData();
            return _cache;
        }

        /// <summary>
        /// 指定メジャーバージョンに対して同意済みかを返す。
        /// バージョン不明（負値）のときは誤ブロックを避けるため同意済み扱いとする。
        /// </summary>
        internal static bool HasConsented(int currentMajorVersion)
        {
            if (currentMajorVersion < 0) return true;
            return Load().agreedMajorVersion >= currentMajorVersion;
        }

        /// <summary>同意を記録して ProjectSettings へ永続化する。</summary>
        internal static void SetConsented(int currentMajorVersion, string fullVersion)
        {
            ConsentData data = Load();
            data.agreedMajorVersion = currentMajorVersion;
            data.agreedVersion = fullVersion ?? string.Empty;
            data.agreedAtUtc = DateTime.UtcNow.ToString("o");

            try
            {
                string path = GetFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[AunCast] 同意状態の保存に失敗しました: {e.Message}");
            }
        }

        /// <summary>記録済みの同意バージョン（未同意なら空文字）。</summary>
        internal static string GetAgreedVersion()
        {
            return Load().agreedVersion;
        }
    }
}
#endif
