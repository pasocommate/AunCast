using NUnit.Framework;

namespace PasocomMate.AunCast.Tests
{
    public class AudioSilenceDetectorDbfsTests
    {
        private AudioSilenceDetector _detector;

        [SetUp]
        public void SetUp()
        {
            _detector = TestHelper.CreateComponent<AudioSilenceDetector>();
            TestHelper.Invoke(_detector, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_detector);
        }

        [Test]
        public void GetSilenceRmsThresholdDbfs_ClampsToRange()
        {
            TestHelper.Set(_detector, "silenceRmsThresholdDbfs", -120f);
            Assert.AreEqual(-96f, _detector.GetSilenceRmsThresholdDbfs(), 0.0001f);

            TestHelper.Set(_detector, "silenceRmsThresholdDbfs", 6f);
            Assert.AreEqual(0f, _detector.GetSilenceRmsThresholdDbfs(), 0.0001f);

            TestHelper.Set(_detector, "silenceRmsThresholdDbfs", -60f);
            Assert.AreEqual(-60f, _detector.GetSilenceRmsThresholdDbfs(), 0.0001f);
        }

        [Test]
        public void GetLastRmsDbfs_UsesMinimumWhenSignalIsMissing()
        {
            float value = _detector.GetLastRmsDbfs();
            Assert.AreEqual(-96f, value, 0.0001f);
        }
    }
}
