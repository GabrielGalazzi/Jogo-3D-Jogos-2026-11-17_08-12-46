using System.Collections.Generic;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// Fire-and-forget ray-traced sounds at a world position (doors, pickups, interactions). Uses a small pool of
    /// <see cref="RaytracedSource"/> objects per scene; each play gets a freshly traced emitter at its position.
    /// </summary>
    public static class GameAudio
    {
        private const int MaxPool = 24;
        private static readonly List<RaytracedSource> Pool = new List<RaytracedSource>();
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            _root = null;
        }

        /// <summary>Plays <paramref name="id"/> at <paramref name="position"/>. Returns the source (null if the pool is exhausted).</summary>
        public static RaytracedSource PlayAt(Sfx id, Vector3 position, float volume = 1f, float maxDistance = 25f, float pitchJitter = 0.04f)
        {
            var clip = SoundBank.Get(id);
            if (clip == null) return null;
            var src = Acquire();
            if (src == null) return null;
            float pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.gameObject.SetActive(true);
            src.BeginPooled(position, clip, volume, maxDistance, pitch);
            return src;
        }

        private static RaytracedSource Acquire()
        {
            if (_root == null)
            {
                Pool.Clear();
                _root = new GameObject("GameAudio").transform;
            }
            for (int i = Pool.Count - 1; i >= 0; i--)
            {
                if (Pool[i] == null) Pool.RemoveAt(i);
                else if (!Pool[i].gameObject.activeSelf) return Pool[i];
            }
            if (Pool.Count >= MaxPool) return null;
            var src = RaytracedSource.Create(_root, $"OneShot_{Pool.Count}", Vector3.zero, 25f);
            Pool.Add(src);
            return src;
        }
    }
}
