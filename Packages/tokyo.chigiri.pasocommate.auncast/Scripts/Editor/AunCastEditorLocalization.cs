using UnityEngine;

namespace PasocomMate.AunCast.Internal
{
    internal static class AunCastEditorLocalization
    {
        internal static string Localize(string ja, string en)
        {
            return Application.systemLanguage == SystemLanguage.Japanese ? ja : en;
        }
    }
}
