using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class StallDetectionTests
    {
        private ActivePlayerMonitor _monitor;

        [SetUp]
        public void SetUp()
        {
            _monitor = TestHelper.CreateComponent<ActivePlayerMonitor>();
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_monitor);
        }

        [Test]
        public void DetectActiveFailure_StallTimeout_ReturnsTrue()
        {
            float stalledTimeout = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "stalledTimeoutSec");

            // 停滞開始から stalledTimeoutSec 以上経過
            float stallStart = 100f;
            float now = stallStart + stalledTimeout + 0.1f;

            TestHelper.Set(_monitor, "_stallStartedAt", stallStart);
            TestHelper.Set(_monitor, "_driftAccumulator", 0f);
            TestHelper.Set(_monitor, "_driftWarmupUntil", 0f);

            bool failure = _monitor.DetectActiveFailure(now);
            Assert.IsTrue(failure, "停滞タイムアウト超過で障害検出されるべき");
        }

        [Test]
        public void DetectActiveFailure_DriftOverThreshold_ReturnsTrue()
        {
            float threshold = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "driftResyncThresholdSec");

            // ウォームアップ済み、ドリフト閾値超過
            TestHelper.Set(_monitor, "_stallStartedAt", 0f);
            TestHelper.Set(_monitor, "_driftWarmupUntil", 50f);
            TestHelper.Set(_monitor, "_driftAccumulator", threshold + 0.05f);

            float now = 60f; // > _driftWarmupUntil

            bool failure = _monitor.DetectActiveFailure(now);
            Assert.IsTrue(failure, "ドリフト閾値超過で障害検出されるべき");
        }

        [Test]
        public void DetectActiveFailure_Normal_ReturnsFalse()
        {
            float threshold = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "driftResyncThresholdSec");

            // 停滞なし、ドリフト正常
            TestHelper.Set(_monitor, "_stallStartedAt", 0f);
            TestHelper.Set(_monitor, "_driftWarmupUntil", 50f);
            TestHelper.Set(_monitor, "_driftAccumulator", threshold * 0.5f);

            float now = 60f;

            bool failure = _monitor.DetectActiveFailure(now);
            Assert.IsFalse(failure, "正常時は障害検出されないべき");
        }

        [Test]
        public void DetectActiveFailure_DuringWarmup_IgnoresDrift()
        {
            float threshold = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "driftResyncThresholdSec");

            // ウォームアップ中は大きなドリフ��でも無視
            TestHelper.Set(_monitor, "_stallStartedAt", 0f);
            TestHelper.Set(_monitor, "_driftWarmupUntil", 100f);
            TestHelper.Set(_monitor, "_driftAccumulator", threshold + 1f);

            float now = 50f; // < _driftWarmupUntil

            bool failure = _monitor.DetectActiveFailure(now);
            Assert.IsFalse(failure, "ウォームアップ中はドリフトで障害検出しない");
        }

        [Test]
        public void IsVerifySatisfied_EnoughAdvances_ReturnsTrue()
        {
            int minAdvances = TestHelper.Get<ActivePlayerMonitor, int>(
                _monitor, "minConsecutiveAdvances");
            float minDuration = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "verifyMinDurationSec");

            TestHelper.Set(_monitor, "_standbyAdvanceCount", minAdvances);
            TestHelper.Set(_monitor, "_verifyStartedAt", 10f);

            float now = 10f + minDuration + 0.1f;

            bool satisfied = _monitor.IsVerifySatisfied(now);
            Assert.IsTrue(satisfied, "条件を満たせば true");
        }

        [Test]
        public void IsVerifySatisfied_NotEnoughAdvances_ReturnsFalse()
        {
            int minAdvances = TestHelper.Get<ActivePlayerMonitor, int>(
                _monitor, "minConsecutiveAdvances");
            float minDuration = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "verifyMinDurationSec");

            TestHelper.Set(_monitor, "_standbyAdvanceCount", minAdvances - 1);
            TestHelper.Set(_monitor, "_verifyStartedAt", 10f);

            float now = 10f + minDuration + 0.1f;

            bool satisfied = _monitor.IsVerifySatisfied(now);
            Assert.IsFalse(satisfied, "カウント不足で false");
        }

        [Test]
        public void IsVerifySatisfied_NotEnoughTime_ReturnsFalse()
        {
            int minAdvances = TestHelper.Get<ActivePlayerMonitor, int>(
                _monitor, "minConsecutiveAdvances");
            float minDuration = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "verifyMinDurationSec");

            TestHelper.Set(_monitor, "_standbyAdvanceCount", minAdvances + 5);
            TestHelper.Set(_monitor, "_verifyStartedAt", 10f);

            float now = 10f + minDuration * 0.5f;

            bool satisfied = _monitor.IsVerifySatisfied(now);
            Assert.IsFalse(satisfied, "時間不足で false");
        }
    }
}
