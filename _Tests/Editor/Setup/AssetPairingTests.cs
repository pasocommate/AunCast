using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace PasocomMate.AunCast.Tests
{
    public class AssetPairingTests
    {
        private const string UDON_SCRIPTS_DIR = "Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon";

        [Test]
        public void AllUdonSharpScripts_HaveMatchingAsset()
        {
            var csFiles = Directory.GetFiles(UDON_SCRIPTS_DIR, "*.cs", SearchOption.AllDirectories);
            var missing = new System.Collections.Generic.List<string>();

            foreach (var csFile in csFiles)
            {
                string normalized = csFile.Replace('\\', '/');
                string content = File.ReadAllText(normalized);

                if (!content.Contains("UdonBehaviourSyncMode"))
                    continue;

                string assetPath = Path.ChangeExtension(normalized, ".asset");
                if (!File.Exists(assetPath))
                    missing.Add(normalized);
            }

            Assert.IsEmpty(missing,
                $".asset が存在しない UdonSharp スクリプト:\n  {string.Join("\n  ", missing)}");
        }

        [Test]
        public void AllAssets_HaveMatchingScript()
        {
            var assetFiles = Directory.GetFiles(UDON_SCRIPTS_DIR, "*.asset", SearchOption.AllDirectories);
            var orphans = new System.Collections.Generic.List<string>();

            foreach (var assetFile in assetFiles)
            {
                string normalized = assetFile.Replace('\\', '/');

                // UdonSharp メタアセットはスキップ
                if (normalized.EndsWith("AunCast.UdonSharp.asset"))
                    continue;

                string csPath = Path.ChangeExtension(normalized, ".cs");
                if (!File.Exists(csPath))
                    orphans.Add(normalized);
            }

            Assert.IsEmpty(orphans,
                $"対応する .cs がない孤立 .asset:\n  {string.Join("\n  ", orphans)}");
        }
    }
}
