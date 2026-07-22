#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
// UnityEditor.PackageInfo と衝突するため、明示的にエイリアスで PackageManager 側を指す。
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PasocomMate.AunCast.Tests
{
    /// <summary>
    /// リリース手順（.claude/commands/release.md）でバージョンを手で書き換える 3 箇所が
    /// 食い違っていないかを検証する。更新漏れは起動ログ・著作権表示・マニュアル見出しに出る。
    /// </summary>
    [TestFixture]
    public class PackageVersionConsistencyTests
    {
        private const string CONTROLLER_SCRIPT_GUID = "6c1e3a5d9f4b0276ca8d2e1f7b3c5094";
        private const string MANUAL_CSS_RELATIVE_PATH = "Manual/src/assets/stylesheets/extra.css";

        private PackageInfo _packageInfo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(CONTROLLER_SCRIPT_GUID);
            Assert.IsNotEmpty(scriptPath, "AunCastDualPlayerController.cs の GUID が解決できません");

            _packageInfo = PackageInfo.FindForAssetPath(scriptPath);
            Assert.IsNotNull(_packageInfo, $"パッケージ情報を取得できません: {scriptPath}");
        }

        [Test]
        public void PackageVersionConstant_MatchesPackageJson()
        {
            Assert.AreEqual(
                _packageInfo.version,
                AunCastDualPlayerController.PACKAGE_VERSION,
                "package.json の version と AunCastDualPlayerController.PACKAGE_VERSION が一致していません。"
                + "リリース時は両方を更新してください。");
        }

        [Test]
        public void ManualHeaderVersion_MatchesPackageJson()
        {
            string cssPath = Path.Combine(GetRepositoryRoot(), MANUAL_CSS_RELATIVE_PATH);
            if (!File.Exists(cssPath))
                Assert.Ignore($"マニュアルのソースがないプロジェクトのためスキップします: {cssPath}");

            Match match = Regex.Match(
                File.ReadAllText(cssPath),
                @"header\s+h1::after\s*\{[^}]*?content:\s*""v(?<version>[^""]+)""",
                RegexOptions.Singleline);
            Assert.IsTrue(match.Success,
                $"header h1::after の content からバージョンを読み取れません: {MANUAL_CSS_RELATIVE_PATH}");

            Assert.AreEqual(
                _packageInfo.version,
                match.Groups["version"].Value,
                "package.json の version と extra.css の header h1::after が一致していません。"
                + "リリース時は両方を更新してください。");
        }

        /// <summary>
        /// マニュアルは Unity のアセットではないため、リポジトリルートからの相対で辿る。
        /// 開発プロジェクトではパッケージがリポジトリ直下に埋め込まれている前提。
        /// </summary>
        private string GetRepositoryRoot()
        {
            // <root>/Packages/<package> の 2 階層上がリポジトリルート。
            return Directory.GetParent(Directory.GetParent(_packageInfo.resolvedPath).FullName).FullName;
        }
    }
}
#endif
