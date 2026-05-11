using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class FifoQueueTests
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
        public void SelectNextQueuedUser_ReturnsOldestTimestamp()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var timestamps = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");

            // スロット 2 (t=10), スロット 5 (t=5 = 最古), スロット 8 (t=15)
            resyncState[2] = (byte)ResyncCoordinator.STATE_QUEUED;
            timestamps[2] = 10f;
            resyncState[5] = (byte)ResyncCoordinator.STATE_QUEUED;
            timestamps[5] = 5f;
            resyncState[8] = (byte)ResyncCoordinator.STATE_QUEUED;
            timestamps[8] = 15f;

            int result = (int)TestHelper.Invoke(_coordinator, "SelectNextQueuedUser");
            Assert.AreEqual(5, result, "最古のタイムスタンプを持つスロット 5 が選ばれるべき");
        }

        [Test]
        public void SelectNextQueuedUser_NoQueued_ReturnsNegative()
        {
            // 全スロット STATE_NONE のまま
            int result = (int)TestHelper.Invoke(_coordinator, "SelectNextQueuedUser");
            Assert.Less(result, 0, "QUEUED がなければ負の値を返すべき");
        }

        [Test]
        public void SelectNextQueuedUser_IgnoresGrantedAndRunning()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var timestamps = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");

            // スロット 0: GRANTED (t=1 = 最古だが対象外)
            resyncState[0] = (byte)ResyncCoordinator.STATE_GRANTED;
            timestamps[0] = 1f;
            // スロット 1: RUNNING (t=2 = 対象外)
            resyncState[1] = (byte)ResyncCoordinator.STATE_RUNNING;
            timestamps[1] = 2f;
            // スロット 2: QUEUED (t=10 = 唯一の候補)
            resyncState[2] = (byte)ResyncCoordinator.STATE_QUEUED;
            timestamps[2] = 10f;

            int result = (int)TestHelper.Invoke(_coordinator, "SelectNextQueuedUser");
            Assert.AreEqual(2, result, "QUEUED のスロット 2 のみが選ばれるべき");
        }

        [Test]
        public void CountGrantedOrRunning_MixedStates()
        {
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");

            resyncState[0] = (byte)ResyncCoordinator.STATE_GRANTED;
            resyncState[1] = (byte)ResyncCoordinator.STATE_RUNNING;
            resyncState[2] = (byte)ResyncCoordinator.STATE_QUEUED;
            resyncState[3] = (byte)ResyncCoordinator.STATE_RUNNING;
            resyncState[4] = (byte)ResyncCoordinator.STATE_NONE;

            int count = (int)TestHelper.Invoke(_coordinator, "CountGrantedOrRunning");
            Assert.AreEqual(3, count, "GRANTED(1) + RUNNING(2) = 3");
        }
    }
}
