using MaidCafe.Audio;
using NUnit.Framework;
using UnityEngine;

namespace MaidCafe.Tests
{
    public class TensionStingerTests
    {
        [Test]
        public void ChaseRateIsInverselyProportionalToDistance()
        {
            foreach (float d in new[] { 2f, 3.5f, 7f, 10f, 14f, 20f, 27f })
            {
                Assert.AreEqual(TensionStingers.RateDistance, TensionStingers.StabRate(d) * d, 1e-3f, $"rate x distance at {d} m");
            }
            Assert.AreEqual(2f * TensionStingers.StabRate(14f), TensionStingers.StabRate(7f), 1e-4f, "half the distance, twice the rate");
            Assert.AreEqual(TensionStingers.MaxRate, TensionStingers.StabRate(0.5f), 1e-5f, "clamped when she is on top of the player");
            Assert.AreEqual(TensionStingers.MaxRate, TensionStingers.StabRate(0f), 1e-5f);
            Assert.AreEqual(TensionStingers.MinRate, TensionStingers.StabRate(100f), 1e-5f, "clamped far away");
        }

        [Test]
        public void HeatGrowsWithClosenessAndChaseTime()
        {
            float far = TensionStingers.TargetHeat(30f, 0f);
            float mid = TensionStingers.TargetHeat(10f, 0f);
            float near = TensionStingers.TargetHeat(2f, 0f);
            Assert.Less(far, mid);
            Assert.Less(mid, near);
            Assert.Greater(far, 0f, "a chase always starts with some heat");
            Assert.Less(TensionStingers.TargetHeat(10f, 0f), TensionStingers.TargetHeat(10f, 10f), "time adds heat");
            Assert.AreEqual(1f, TensionStingers.TargetHeat(2f, TensionStingers.HeatRampSeconds), 1e-5f);
            for (float d = 0f; d < 40f; d += 0.5f)
            {
                float h = TensionStingers.TargetHeat(d, d * 2f);
                Assert.That(h, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void SaturationIsBypassedWithoutHeat()
        {
            Assert.IsTrue(new HarmonicSaturation(0f).IsBypass);
            Assert.IsTrue(default(HarmonicSaturation).IsBypass);
            for (float x = -1f; x <= 1f; x += 0.05f)
            {
                Assert.AreEqual(x, HarmonicSaturation.Shape(x, 0f), 1e-6f);
            }
        }

        [Test]
        public void SaturationNeverExceedsFullScaleAndHoldsTheReferenceLevel()
        {
            foreach (float heat in new[] { 0.1f, 0.3f, 0.5f, 0.75f, 1f })
            {
                for (float x = -1f; x <= 1f; x += 0.01f)
                {
                    Assert.LessOrEqual(Mathf.Abs(HarmonicSaturation.Shape(x, heat)), 1.0001f, $"heat {heat}, x {x}");
                }
                Assert.AreEqual(0f, HarmonicSaturation.Shape(0f, heat), 1e-6f, "silence stays silent");
                float r = HarmonicSaturation.ReferenceLevel;
                float up = HarmonicSaturation.Shape(r, heat);
                float down = -HarmonicSaturation.Shape(-r, heat);
                Assert.LessOrEqual(Mathf.Max(up, down), r + 1e-4f, $"reference level held at heat {heat}");
                Assert.Greater(Mathf.Min(up, down), 0.6f * r, $"no collapse at heat {heat}");
            }
        }

        [Test]
        public void MoreHeatSoundsLouderAtTheSamePeak()
        {
            // A decaying 660 Hz tone peaking at -1 dBFS, like a violin stab.
            const int rate = 48000;
            var stab = new float[rate / 2];
            for (int i = 0; i < stab.Length; i++)
            {
                float t = i / (float)rate;
                stab[i] = 0.89f * Mathf.Exp(-t / 0.08f) * Mathf.Sin(2f * Mathf.PI * 660f * t);
            }
            float previous = Rms(stab, 0f);
            foreach (float heat in new[] { 0.25f, 0.5f, 0.75f, 1f })
            {
                float rms = Rms(stab, heat);
                Assert.Greater(rms, previous, $"heat {heat} is louder than the step before");
                previous = rms;
            }
            Assert.Greater(20f * Mathf.Log10(Rms(stab, 1f) / Rms(stab, 0f)), 5f, "full heat adds at least 5 dB RMS (5.8 measured)");
        }

        private static float Rms(float[] x, float heat)
        {
            var curve = new HarmonicSaturation(heat);
            double sum = 0;
            foreach (float s in x)
            {
                float y = curve.Apply(s);
                sum += y * y;
            }
            return (float)System.Math.Sqrt(sum / x.Length);
        }
    }
}
