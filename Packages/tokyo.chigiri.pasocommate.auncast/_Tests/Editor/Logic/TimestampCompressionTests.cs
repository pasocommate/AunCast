using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class TimestampCompressionTests
    {
        private ResyncCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _coordinator = TestHelper.CreateComponent<ResyncCoordinator>();
            // 内部配列を初期化
            TestHelper.Invoke(_coordinator, "InitializeArrays");
            int maxPlayers = TestHelper.Get<ResyncCoordinator, int>(_coordinator, "maxPlayers");
            TestHelper.Set(_coordinator, "_ownerTimestamp", new float[maxPlayers]);
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_coordinator);
        }

        [Test]
        public void RoundTrip_PreservesWithin100ms()
        {
            int maxPlayers = TestHelper.Get<ResyncCoordinator, int>(_coordinator, "maxPlayers");
            var resyncState = TestHelper.Get<ResyncCoordinator, byte[]>(_coordinator, "resyncState");
            var ownerTimestamp = TestHelper.Get<ResyncCoordinator, float[]>(_coordinator, "_ownerTimestamp");

            // テストデータ: スロット 0, 3, 7 に異なるタイムスタンプを設定
            float[] testTimes = { 100.5f, 105.3f, 102.7f };
            int[] testSlots = { 0, 3, 7 };

            for (int i = 0; i < testSlots.Length; i++)
            {
                resyncState[testSlots[i]] = (byte)ResyncCoordinator.STATE_QUEUED;
                ownerTimestamp[testSlots[i]] = testTimes[i];
            }

            TestHelper.Invoke(_coordinator, "CompressTimestamps");

            // GetUserTimestamp で復元し、0.1 秒精度を確認
            for (int i = 0; i < testSlots.Length; i++)
            {
                float restored = _coordinator.GetUserTimestamp(testSlots[i]);
                Assert.AreEqual(testTimes[i], restored, 0.1f,
                    $"スロット {testSlots[i]}: 元={testTimes[i]:F3}, 復元={restored:F3}");
            }
        }

        [Test]
        public void AllNone_OffsetIsZero()
        {
            // 全スロット STATE_NONE のまま圧縮
            TestHelper.Invoke(_coordinator, "CompressTimestamps");

            float offset = TestHelper.Get<ResyncCoordinator, float>(
                _coordinator, "userTimestampOffset");
            Assert.AreEqual(0f, offset, "全 NONE の場合 offset は 0 であるべき");
        }
    }
}
