using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PasocomMate.AunCast.Internal;

namespace PasocomMate.AunCast.Tests
{
    public class SetupSceneValidationTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        private bool RunSetup()
        {
            _root = new GameObject("AunCast");
            _root.AddComponent<AunCastSettings>();
            _root.AddComponent<AunCastTheme>();
            AunCastSetup.SetupSceneOnRoot(_root);
            return _root.transform.childCount > 0;
        }

        [Test]
        public void SetupScene_CompletesWithoutError()
        {
            bool result = RunSetup();
            Assert.IsTrue(result, "AunCast ルートの子が生成されなかった");
        }

        [Test]
        public void SetupScene_AllCoreComponentsExist()
        {
            if (!RunSetup()) { Assert.Inconclusive("Setup 失敗"); return; }

            Assert.IsNotNull(_root.GetComponentInChildren<ResyncCoordinator>(),
                "ResyncCoordinator が存在しない");
            Assert.IsNotNull(_root.GetComponentInChildren<PlaybackMonitor>(),
                "PlaybackMonitor が存在しない");
            Assert.IsNotNull(_root.GetComponentInChildren<LocalDualPlayerController>(),
                "LocalDualPlayerController が存在しない");
            Assert.IsNotNull(_root.GetComponentInChildren<PlaybackSwitcher>(),
                "PlaybackSwitcher が存在しない");
            Assert.IsNotNull(_root.GetComponentInChildren<ActivePlayerMonitor>(),
                "ActivePlayerMonitor が存在しない");
        }

        [Test]
        public void SetupScene_AllImagesHavePositionAsUV1()
        {
            if (!RunSetup()) { Assert.Inconclusive("Setup 失敗"); return; }

            var images = _root.GetComponentsInChildren<Image>(true);
            var missing = new System.Collections.Generic.List<string>();

            foreach (var img in images)
            {
                if (img.GetComponent<PositionAsUV1>() == null)
                    missing.Add(GetHierarchyPath(img.transform));
            }

            Assert.IsEmpty(missing,
                $"PositionAsUV1 が欠けている Image:\n  {string.Join("\n  ", missing)}");
        }

        [Test]
        public void SetupScene_SilenceMeterMarkersAreWired()
        {
            if (!RunSetup()) { Assert.Inconclusive("Setup 失敗"); return; }

            var panel = _root.GetComponentInChildren<UserStatusPanel>(true);
            Assert.IsNotNull(panel, "UserStatusPanel が存在しない");

            var thresholdMarker = TestHelper.Get<UserStatusPanel, Image>(panel, "silenceThresholdMarker");
            var peakMarker = TestHelper.Get<UserStatusPanel, Image>(panel, "silencePeakMarker");
            Assert.IsNotNull(thresholdMarker, "silenceThresholdMarker が配線されていない");
            Assert.IsNotNull(peakMarker, "silencePeakMarker が配線されていない");
        }

        [Test]
        public void SetupScene_SilenceMeterDefaultsApplied()
        {
            if (!RunSetup()) { Assert.Inconclusive("Setup 失敗"); return; }

            var panel = _root.GetComponentInChildren<UserStatusPanel>(true);
            Assert.IsNotNull(panel, "UserStatusPanel が存在しない");

            float hold = TestHelper.Get<UserStatusPanel, float>(panel, "silenceMeterPeakHoldSec");
            float decay = TestHelper.Get<UserStatusPanel, float>(panel, "silenceMeterPeakDecayDbPerSec");
            Assert.AreEqual(0.5f, hold, 0.0001f, "ピーク保持時間のデフォルト反映が不正");
            Assert.AreEqual(12f, decay, 0.0001f, "ピーク減衰速度のデフォルト反映が不正");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
