#if UNITY_EDITOR
using System;
using UnityEditorInternal;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCast 利用規約への同意状態をプロジェクト単位で保存する。
    /// 保存先は ProjectSettings/AunCastConsent.asset。アセットDB には現れず、
    /// VCS にコミットすればプロジェクト（チーム）単位で一度同意すれば共有できる。
    /// 同意したメジャーバージョンを記録し、メジャー更新時に再同意を促す。
    /// </summary>
    internal static class AunCastConsentStore
    {
        // プロジェクトルートからの相対パス。SaveToSerializedFileAndForget はこの形式を取る。
        private const string CONSENT_FILE_PATH = "ProjectSettings/AunCastConsent.asset";

        // ProjectSettings へ直接シリアライズするための器。アセットとしては生成しない。
        private sealed class ConsentData : ScriptableObject
        {
            public int agreedMajorVersion = -1;
            public string agreedVersion = string.Empty;
            public string agreedAtUtc = string.Empty;
        }

        private static ConsentData _cache;

        private static ConsentData Load()
        {
            if (_cache != null) return _cache;

            UnityEngine.Object[] loaded = InternalEditorUtility.LoadSerializedFileAndForget(CONSENT_FILE_PATH);
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (loaded[i] is ConsentData data)
                    {
                        _cache = data;
                        return _cache;
                    }
                }
            }

            _cache = ScriptableObject.CreateInstance<ConsentData>();
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
            InternalEditorUtility.SaveToSerializedFileAndForget(
                new UnityEngine.Object[] { data }, CONSENT_FILE_PATH, true);
        }

        /// <summary>記録済みの同意バージョン（未同意なら空文字）。</summary>
        internal static string GetAgreedVersion()
        {
            return Load().agreedVersion;
        }
    }
}
#endif
