using System.Collections.Generic;

namespace MaidCafe.Core
{
    /// <summary>
    /// Deterministic random source used by all procedural systems.
    /// Wraps System.Random so the same seed always produces the same run.
    /// </summary>
    public sealed class Rng
    {
        private readonly System.Random _random;

        public Rng(int seed)
        {
            _random = new System.Random(seed);
        }

        /// <summary>Integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

        /// <summary>Float in [min, max).</summary>
        public float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);

        public float Value => (float)_random.NextDouble();

        public bool Chance(float probability) => _random.NextDouble() < probability;

        public T Pick<T>(IReadOnlyList<T> list) => list[_random.Next(list.Count)];

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Stable hash that combines a run seed with a sub-index (floor number, etc.).</summary>
        public static int Combine(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)(index + 1) * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0xC2B2AE3Du;
                h ^= h >> 13;
                return (int)(h & 0x7FFFFFFF);
            }
        }
    }
}
