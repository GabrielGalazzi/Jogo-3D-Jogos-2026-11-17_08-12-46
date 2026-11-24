using System;
using UnityEngine;

namespace MaidCafe.Core
{
    public enum NoiseType
    {
        Footstep,
        Door,
        Interaction,
        Breakable,
        Alarm
    }

    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly NoiseType Type;
        public readonly bool FromPlayer;

        public NoiseEvent(Vector3 position, float radius, NoiseType type, bool fromPlayer)
        {
            Position = position;
            Radius = radius;
            Type = type;
            FromPlayer = fromPlayer;
        }
    }

    /// <summary>
    /// Global noise bus. Anything that makes sound the enemies can hear calls Emit;
    /// enemy perception subscribes to OnNoise.
    /// </summary>
    public static class NoiseSystem
    {
        public static event Action<NoiseEvent> OnNoise;

        public static void Emit(Vector3 position, float radius, NoiseType type, bool fromPlayer = true)
        {
            if (radius <= 0f) return;
            OnNoise?.Invoke(new NoiseEvent(position, radius, type, fromPlayer));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnNoise = null;
        }
    }
}
