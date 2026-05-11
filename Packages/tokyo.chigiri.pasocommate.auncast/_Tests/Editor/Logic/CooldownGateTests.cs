using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class CooldownGateTests
    {
        private ResyncCoordinatorClient _client;

        [SetUp]
        public void SetUp()
        {
            _client = TestHelper.CreateComponent<ResyncCoordinatorClient>();
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_client);
        }

        [Test]
        public void WithinSuppressSec_NotEligible()
        {
            float suppressSec = TestHelper.Get<ResyncCoordinatorClient, float>(
                _client, "silenceSuppressSec");

            float lastCompleted = 100f;
            TestHelper.Set(_client, "_lastResyncCompletedAt", lastCompleted);

            float now = lastCompleted + suppressSec - 1f;
            bool eligible = _client.IsSilenceAutoResyncEligible(now);

            Assert.IsFalse(eligible,
                $"silenceSuppressSec ({suppressSec}) 以内は不適格であるべき");
        }

        [Test]
        public void AfterSuppressSec_Eligible()
        {
            float suppressSec = TestHelper.Get<ResyncCoordinatorClient, float>(
                _client, "silenceSuppressSec");

            float lastCompleted = 100f;
            TestHelper.Set(_client, "_lastResyncCompletedAt", lastCompleted);

            float now = lastCompleted + suppressSec + 1f;
            bool eligible = _client.IsSilenceAutoResyncEligible(now);

            Assert.IsTrue(eligible,
                $"silenceSuppressSec ({suppressSec}) 超過後は適格であるべき");
        }

        [Test]
        public void NeverCompleted_AlwaysEligible()
        {
            // _lastResyncCompletedAt のデフォルト値は 0
            TestHelper.Set(_client, "_lastResyncCompletedAt", 0f);

            float suppressSec = TestHelper.Get<ResyncCoordinatorClient, float>(
                _client, "silenceSuppressSec");

            float now = suppressSec + 1f;
            bool eligible = _client.IsSilenceAutoResyncEligible(now);

            Assert.IsTrue(eligible, "一度も完了していない場合は適格であるべき");
        }
    }
}
