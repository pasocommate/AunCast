using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class BitpackTests
    {
        private PlaybackMonitor _monitor;

        [SetUp]
        public void SetUp()
        {
            _monitor = TestHelper.CreateComponent<PlaybackMonitor>();
            // Start() を明示呼び出しして popcount テーブルと配列を初期化
            TestHelper.Invoke(_monitor, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            TestHelper.Destroy(_monitor);
        }

        [Test]
        public void SetAndGet_CorrectBit()
        {
            // スロット 0, 7, 8, 15, 81 でテスト（バイト境界のエッジケース）
            int[] testSlots = { 0, 7, 8, 15, 81 };

            foreach (int slot in testSlots)
            {
                bool changed = (bool)TestHelper.Invoke(_monitor, "SetSlotActive",
                    new object[] { slot, true });
                Assert.IsTrue(changed, $"スロット {slot}: 変化があるべき");

                bool value = (bool)TestHelper.Invoke(_monitor, "GetSlotActive",
                    new object[] { slot });
                Assert.IsTrue(value, $"スロット {slot}: true であるべき");
            }

            // 設定していないスロットは false
            bool unset = (bool)TestHelper.Invoke(_monitor, "GetSlotActive",
                new object[] { 3 });
            Assert.IsFalse(unset, "未設定スロットは false であるべき");
        }

        [Test]
        public void SetSlotActive_NoChange_ReturnsFalse()
        {
            TestHelper.Invoke(_monitor, "SetSlotActive", new object[] { 5, true });
            bool changed = (bool)TestHelper.Invoke(_monitor, "SetSlotActive",
                new object[] { 5, true });
            Assert.IsFalse(changed, "同じ値への設定は変化なしを返すべき");
        }

        [Test]
        public void CountBits_Empty_ReturnsZero()
        {
            int count = _monitor.GetPlayingEstimateCount();
            Assert.AreEqual(0, count, "初期状態は 0");
        }

        [Test]
        public void CountBits_SparsePattern_CorrectCount()
        {
            int[] slots = { 0, 10, 20, 40, 80 };
            foreach (int s in slots)
                TestHelper.Invoke(_monitor, "SetSlotActive", new object[] { s, true });

            int count = _monitor.GetPlayingEstimateCount();
            Assert.AreEqual(5, count, "5 スロット設定 → カウント 5");
        }

        [Test]
        public void ClearSlot_ClearsAllArrays()
        {
            int slot = 10;
            TestHelper.Invoke(_monitor, "SetSlotActive", new object[] { slot, true });
            TestHelper.Invoke(_monitor, "SetSlotConnecting", new object[] { slot, true });
            TestHelper.Invoke(_monitor, "SetSlotError", new object[] { slot, true });

            _monitor.ClearSlot(slot);

            Assert.AreEqual(0, _monitor.GetPlaybackActive(slot), "playback クリア");
            Assert.AreEqual(0, _monitor.GetConnectingActive(slot), "connecting クリア");
            Assert.AreEqual(0, _monitor.GetErrorActive(slot), "error クリア");
        }
    }
}
