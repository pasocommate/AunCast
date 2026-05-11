using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class TimeoutCleanupTests
    {
        private ResyncCoordinator _coordinator;
        private int _maxPlayers;

        [SetUp]
        public void SetUp()
        {
            _coordinator = TestHelper.CreateComponent<ResyncCoordinator>();
            TestHelper.Invoke(_coordinator, "InitializeArrays");
            _maxPlayers = TestHelper.Get<ResyncCoordinator, int>(_coordinator, "maxPlayers");
            TestHelper.Set(_coordinator, "_ownerTimestamp", new float[_maxPlayers]);
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_coordinator);
        }

        [Test]
        public void GrantedTimeout_ClearsSlot()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var timestamps = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");
            float grantTimeout = TestHelper.Get<ResyncCoordinator, float>(
                _coordinator, "grantTimeoutSec");

            resyncState[0] = (byte)ResyncCoordinator.STATE_GRANTED;
            timestamps[0] = 100f;
            float serverTime = 100f + grantTimeout + 1f;

            TestHelper.Invoke(_coordinator, "CleanupExpiredStates", new object[] { serverTime });

            Assert.AreEqual(ResyncCoordinator.STATE_NONE, resyncState[0],
                "Grant タイムアウト後は STATE_NONE に戻るべき");
        }

        [Test]
        public void RunningTimeout_ClearsSlot()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var timestamps = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");
            float runningTimeout = TestHelper.Get<ResyncCoordinator, float>(
                _coordinator, "runningTimeoutSec");

            resyncState[1] = (byte)ResyncCoordinator.STATE_RUNNING;
            timestamps[1] = 200f;
            float serverTime = 200f + runningTimeout + 1f;

            TestHelper.Invoke(_coordinator, "CleanupExpiredStates", new object[] { serverTime });

            Assert.AreEqual(ResyncCoordinator.STATE_NONE, resyncState[1],
                "Running タイムアウト後は STATE_NONE に戻るべき");
        }

        [Test]
        public void NotExpired_NoChange()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var timestamps = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");
            float grantTimeout = TestHelper.Get<ResyncCoordinator, float>(
                _coordinator, "grantTimeoutSec");

            resyncState[2] = (byte)ResyncCoordinator.STATE_GRANTED;
            timestamps[2] = 300f;
            float serverTime = 300f + grantTimeout - 1f;

            TestHelper.Invoke(_coordinator, "CleanupExpiredStates", new object[] { serverTime });

            Assert.AreEqual(ResyncCoordinator.STATE_GRANTED, resyncState[2],
                "タイムアウト前は状態が維持されるべき");
        }

        [Test]
        public void NegativeElapsed_Skipped()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var timestamps = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");

            resyncState[3] = (byte)ResyncCoordinator.STATE_GRANTED;
            timestamps[3] = 500f;
            float serverTime = 400f; // elapsed < 0

            TestHelper.Invoke(_coordinator, "CleanupExpiredStates", new object[] { serverTime });

            Assert.AreEqual(ResyncCoordinator.STATE_GRANTED, resyncState[3],
                "elapsed < 0 の場合は変更しない（時刻巻き戻り保護）");
        }
    }
}
