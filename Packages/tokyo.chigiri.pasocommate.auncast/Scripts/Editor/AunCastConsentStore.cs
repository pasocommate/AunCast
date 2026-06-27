#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCast 利用規約への同意状態を保持する ScriptableSingleton。
    /// 保存先は ProjectSettings/AunCastConsent.asset（アセットDB には現れず、
    /// VCS にコミットすればプロジェクト（チーム）単位で共有できる）。
    ///
    /// ScriptableSingleton は型 T 経由でインスタンスを解決するため、ネスト型を
    /// SaveToSerializedFileAndForget で保存したときに起きる m_Script 未解決
    /// （ドメインリロード直後に同意状態が既定値へ戻る不具合）を回避できる。
    /// </summary>
    [FilePath("ProjectSettings/AunCastConsent.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AunCastConsentData : ScriptableSingleton<AunCastConsentData>
    {
        public int agreedMajorVersion = -1;
        public string agreedVersion = string.Empty;
        public string agreedAtUtc = string.Empty;

        public void Persist()
        {
            Save(true);
        }
    }

    /// <summary>
    /// 利用規約への同意状態の参照・更新口。実体は <see cref="AunCastConsentData"/>。
    /// 同意したメジャーバージョンを記録し、メジャー更新時に再同意を促す。
    /// </summary>
    internal static class AunCastConsentStore
    {
        /// <summary>
        /// 指定メジャーバージョンに対して同意済みかを返す。
        /// バージョン不明（負値）のときは誤ブロックを避けるため同意済み扱いとする。
        /// </summary>
        internal static bool HasConsented(int currentMajorVersion)
        {
            if (currentMajorVersion < 0) return true;
            return AunCastConsentData.instance.agreedMajorVersion >= currentMajorVersion;
        }

        /// <summary>同意を記録して ProjectSettings へ永続化する。</summary>
        internal static void SetConsented(int currentMajorVersion, string fullVersion)
        {
            AunCastConsentData data = AunCastConsentData.instance;
            data.agreedMajorVersion = currentMajorVersion;
            data.agreedVersion = fullVersion ?? string.Empty;
            data.agreedAtUtc = DateTime.UtcNow.ToString("o");
            data.Persist();
        }

        /// <summary>記録済みの同意バージョン（未同意なら空文字）。</summary>
        internal static string GetAgreedVersion()
        {
            return AunCastConsentData.instance.agreedVersion;
        }
    }
}
#endif
