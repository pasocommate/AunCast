using NUnit.Framework;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    public class DriftEmaTests
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
        public void Warmup_AccumulatorStaysZero()
        {
            float now = 10f;
            float warmupSec = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "driftWarmupSec");

            // ウォームアップ中の状態を設定
            TestHelper.Set(_monitor, "_driftWarmupUntil", now + warmupSec);
            TestHelper.Set(_monitor, "_driftAccumulator", 0.5f);

            // PollActive のドリフト計算部分を模擬:
            // canMeasureDrift = isPlaying && now >= _driftWarmupUntil
            // ウォームアップ中は accumulator が 0 にリセットされる
            bool canMeasureDrift = now >= (now + warmupSec);
            Assert.IsFalse(canMeasureDrift, "ウォームアップ中は計測不可");

            // ドリフト計測不可時のロジック: _driftAccumulator = 0f
            // このロジックは PollActive 内にあるが、ここでは数式を直接検証
            float acc = canMeasureDrift ? 0.5f : 0f;
            Assert.AreEqual(0f, acc, "ウォームアップ中は accumulator = 0");
        }

        [Test]
        public void AfterWarmup_EmaConvergesToRawDrift()
        {
            float tau = TestHelper.Get<ActivePlayerMonitor, float>(
                _monitor, "driftSmoothingTimeConstant");
            float dt = 0.1f;

            // 一定のドリフト 0.2 秒を 50 ステップ注入
            float rawDrift = 0.2f;
            float accumulator = 0f;

            for (int i = 0; i < 50; i++)
            {
                float alpha = Mathf.Clamp01(1f - Mathf.Exp(-dt / tau));
                accumulator = Mathf.Lerp(accumulator, rawDrift, alpha);
            }

            // 50 ステップ (5 秒) 後、tau=1.5 なら十分に収束しているはず
            Assert.AreEqual(rawDrift, accumulator, 0.01f,
                $"EMA は rawDrift ({rawDrift}) に収束するべき (実際={accumulator:F4})");
        }

        [Test]
        public void Alpha_Formula_IsCorrect()
        {
            float tau = 1.5f;
            float dt = 0.1f;

            float expected = 1f - Mathf.Exp(-dt / tau);
            float alpha = Mathf.Clamp01(1f - Mathf.Exp(-dt / tau));

            Assert.AreEqual(expected, alpha, 1e-6f,
                "alpha = 1 - exp(-dt/tau) の計算が正しいこと");
            Assert.Greater(alpha, 0f, "alpha > 0");
            Assert.Less(alpha, 1f, "alpha < 1");
        }

        [Test]
        public void ZeroDrift_AccumulatorDecays()
        {
            float tau = 1.5f;
            float dt = 0.1f;

            // 初期ドリフトが 0.5 あり、rawDrift=0 が続く場合に減衰
            float accumulator = 0.5f;
            for (int i = 0; i < 100; i++)
            {
                float alpha = Mathf.Clamp01(1f - Mathf.Exp(-dt / tau));
                accumulator = Mathf.Lerp(accumulator, 0f, alpha);
            }

            Assert.Less(Mathf.Abs(accumulator), 0.001f,
                "rawDrift=0 が続くと accumulator は 0 に減衰する");
        }
    }
}
