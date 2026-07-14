using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class ParameterRangeValidationTests
    {
        private AunCastActivePlayerMonitor _monitor;
        private AunCastResyncCoordinator _coordinator;
        private AunCastResyncCoordinatorClient _client;

        [SetUp]
        public void SetUp()
        {
            _monitor = TestHelper.CreateComponent<AunCastActivePlayerMonitor>();
            _coordinator = TestHelper.CreateComponent<AunCastResyncCoordinator>();
            _client = TestHelper.CreateComponent<AunCastResyncCoordinatorClient>();
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
            float value = TestHelper.Get<AunCastActivePlayerMonitor, float>(_monitor, "monitorIntervalSec");
            Assert.LessOrEqual(value, 0.1f,
                $"monitorIntervalSec ({value}) は 0.1 秒以下であるべき");
        }

        [Test]
        public void StalledTimeout_InRange()
        {
            float value = TestHelper.Get<AunCastActivePlayerMonitor, float>(_monitor, "stalledTimeoutSec");
            Assert.GreaterOrEqual(value, 1.5f,
                $"stalledTimeoutSec ({value}) は 1.5 秒以上であるべき");
            Assert.LessOrEqual(value, 3.0f,
                $"stalledTimeoutSec ({value}) は 3.0 秒以下であるべき");
        }

        [Test]
        public void MaxConcurrent_SafeForCDN()
        {
            byte value = TestHelper.Get<AunCastResyncCoordinator, byte>(_coordinator, "maxConcurrentResyncUsers");
            Assert.LessOrEqual(value, 15,
                $"maxConcurrentResyncUsers ({value}) は CDN 上限 (100) の 15% = 15 以下であるべき");
        }

        [Test]
        public void CycleTimeout_LessThanRunningTimeout()
        {
            float cycleTimeout = TestHelper.Get<AunCastResyncCoordinatorClient, float>(
                _client, "resyncCycleTimeoutSec");
            float runningTimeout = TestHelper.Get<AunCastResyncCoordinator, float>(
                _coordinator, "runningTimeoutSec");

            Assert.Less(cycleTimeout, runningTimeout,
                $"resyncCycleTimeoutSec ({cycleTimeout}) < runningTimeoutSec ({runningTimeout}) であるべき");
        }

        [Test]
        public void DriftThreshold_IsConfigured()
        {
            // 閾値は Coordinator の同期変数 driftResyncThresholdIndex で一元管理される。
            // 既定値が OFF でない有効な段階であることを確認する。
            Assert.IsTrue(_coordinator.IsDriftResyncEnabled(),
                "driftResyncThresholdIndex の既定値は OFF 以外であるべき");
            Assert.Greater(_coordinator.GetDriftResyncThresholdSec(), 0f,
                "既定のドリフト Resync 閾値は 0 より大きくなければならない");
        }
    }
}
