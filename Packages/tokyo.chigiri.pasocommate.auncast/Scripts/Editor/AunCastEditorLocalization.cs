using UnityEditor;
using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    internal static class AunCastEditorLocalization
    {
        /// <summary>インスペクタの表示言語モード。</summary>
        internal enum EditorLanguage
        {
            Auto = 0,     // OS 言語（Application.systemLanguage）に追従
            Japanese = 1, // 日本語固定
            English = 2,  // 英語固定
        }

        // 表示言語は個人のエディタ設定なので EditorPrefs（マシン単位・全プロジェクト共有）に保存する。
        private const string PREF_KEY = "AunCast.EditorLanguage";

        // EditorPrefs の読み出しを毎回行わないようキャッシュする。ドメインリロードで再読込される。
        private static int _languageCache = -1;

        internal static EditorLanguage Language
        {
            get
            {
                if (_languageCache < 0)
                    _languageCache = EditorPrefs.GetInt(PREF_KEY, (int)EditorLanguage.Auto);
                return (EditorLanguage)_languageCache;
            }
            set
            {
                _languageCache = (int)value;
                EditorPrefs.SetInt(PREF_KEY, _languageCache);
            }
        }

        /// <summary>現在の実効言語が日本語かどうか。Auto のときは OS 言語で判定する。</summary>
        internal static bool IsJapanese
        {
            get
            {
                switch (Language)
                {
                    case EditorLanguage.Japanese: return true;
                    case EditorLanguage.English: return false;
                    default: return Application.systemLanguage == SystemLanguage.Japanese;
                }
            }
        }

        internal static string Localize(string ja, string en)
        {
            return IsJapanese ? ja : en;
        }
    }
}
