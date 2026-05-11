using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class ParameterRangeValidationTests
    {
        private ActivePlayerMonitor _monitor;
        private ResyncCoordinator _coordinator;
        private ResyncCoordinatorClient _client;

        [SetUp]
        public void SetUp()
        {
            _monitor = TestHelper.CreateComponent<ActivePlayerMonitor>();
            _coordinator = TestHelper.CreateComponent<ResyncCoordinator>();
            _client = TestHelper.CreateComponent<ResyncCoordinatorClient>();
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_monitor);
            TestHelper.Destroy(_coordinator);
            TestHelper.Destroy(_client);
        }

        [Test]
        public void MonitorInterval_NotExceedRecommended()
        {
            float value = TestHelper.Get<ActivePlayerMonitor, float>(_monitor, "monitorIntervalSec");
            Assert.LessOrEqual(value, 0.1f,
                $"monitorIntervalSec ({value}) は 0.1 秒以下であるべき");
        }

        [Test]
        public void StalledTimeout_InRange()
        {
            float value = TestHelper.Get<ActivePlayerMonitor, float>(_monitor, "stalledTimeoutSec");
            Assert.GreaterOrEqual(value, 1.5f,
                $"stalledTimeoutSec ({value}) は 1.5 秒以上であるべき");
            Assert.LessOrEqual(value, 3.0f,
                $"stalledTimeoutSec ({value}) は 3.0 秒以下であるべき");
        }

        [Test]
        public void MaxConcurrent_SafeForCDN()
        {
            byte value = TestHelper.Get<ResyncCoordinator, byte>(_coordinator, "maxConcurrentResyncUsers");
            Assert.LessOrEqual(value, 15,
                $"maxConcurrentResyncUsers ({value}) は CDN 上限 (100) の 15% = 15 以下であるべき");
        }

        [Test]
        public void CycleTimeout_LessThanRunningTimeout()
        {
            float cycleTimeout = TestHelper.Get<ResyncCoordinatorClient, float>(
                _client, "resyncCycleTimeoutSec");
            float runningTimeout = TestHelper.Get<ResyncCoordinator, float>(
                _coordinator, "runningTimeoutSec");

            Assert.Less(cycleTimeout, runningTimeout,
                $"resyncCycleTimeoutSec ({cycleTimeout}) < runningTimeoutSec ({runningTimeout}) であるべき");
        }

        [Test]
        public void DriftThreshold_IsConfigured()
        {
            float value = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "driftResyncThresholdSec");
            Assert.Greater(value, 0f,
                "driftResyncThresholdSec は 0 より大きくなければならない");
        }
    }
}
