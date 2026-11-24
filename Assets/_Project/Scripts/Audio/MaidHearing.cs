using System;
using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;
using UnityEngine.AI;

namespace MaidCafe.Audio
{
    /// <summary>
    /// A maid's ear: an emitter at her head that casts occlusion and permeation rays towards the player's body, so
    /// <see cref="Evaluate"/> knows how much of a noise actually reaches her (straight line, around corners, through
    /// doorways or muffled through a wall) instead of a single wall check. Noises away from the player (alarms, the
    /// gramophone) are traced on demand with a short-lived emitter at the noise. Without the native library
    /// <see cref="Evaluate"/> returns false and the caller uses <see cref="LinecastAudibility"/>.
    /// </summary>
    public sealed class MaidHearing : MonoBehaviour, IAcousticClient
    {
        /// <summary>Noises from the player closer than this to the player's body use the continuously traced body filter.</summary>
        public const float NearPlayer = 2.5f;

        /// <summary>Longest wait for an on-demand trace before falling back to line of sight.</summary>
        private const float ProbeTimeout = 0.75f;

        /// <summary>Hearing radius at audibility 0 (fully sealed) relative to the open-air radius.</summary>
        public const float SealedRadiusFactor = 0.3f;

        /// <summary>Line-of-sight fallback audibility when a wall is in the way (gives the old 0.6 radius factor).</summary>
        public const float BlockedFallbackAudibility = 0.18f;

        /// <summary>How far off a heard position can be at audibility 0 (faint sounds are harder to place).</summary>
        public const float MaxLocalizationError = 3f;

        private Func<Vector3> _earPosition;
        private AcousticEmitter _ear;
        private readonly List<Probe> _probes = new List<Probe>();

        private class Probe
        {
            public AcousticEmitter Emitter;
            public Vector3 Position;
            public Action<float> Callback;
            public float Deadline;
        }

        public AcousticEmitter Ear => _ear;

        public void Init(Func<Vector3> earPosition) => _earPosition = earPosition;

        private Vector3 EarPosition => _earPosition != null ? _earPosition() : transform.position + Vector3.up * 1.55f;

        private void OnEnable() => AcousticWorld.Register(this);

        private void OnDisable()
        {
            AcousticWorld.Unregister(this);
            var world = AcousticWorld.Instance;
            if (world != null)
            {
                foreach (var p in _probes) world.Release(p.Emitter);
                world.Release(_ear);
            }
            _probes.Clear();
            _ear = null;
        }

        /// <summary>
        /// Starts evaluating how audible <paramref name="e"/> is at this maid (0 = nothing reaches her, 1 = open air).
        /// <paramref name="onHeard"/> runs now or within a few frames. Returns false if ray-traced hearing is
        /// unavailable for this noise (the caller should fall back to <see cref="LinecastAudibility"/>).
        /// </summary>
        public bool Evaluate(NoiseEvent e, Action<float> onHeard)
        {
            var world = AcousticWorld.Instance;
            if (!AcousticWorld.Available || _ear == null || !_ear.Valid) return false;

            var player = PlayerContext.Instance;
            if (e.FromPlayer && player != null && FlatDistance(e.Position, player.transform.position) < NearPlayer)
            {
                if (!world.TryGetFilter(_ear, world.PlayerBody, out float lf, out float hf)) return false;
                onHeard(Audibility(lf, hf));
                return true;
            }

            var probe = world.CreateEmitter("Noise", EmitterSettings.Target, e.Position + Vector3.up * 0.5f);
            if (probe == null) return false;
            world.AddTarget(_ear, probe);
            _probes.Add(new Probe { Emitter = probe, Position = e.Position, Callback = onHeard, Deadline = Time.time + ProbeTimeout });
            return true;
        }

        /// <summary>Audibility of the player's body right now (-1 if not traced yet). Used by tests and the dev overlay.</summary>
        public float PlayerAudibility()
        {
            var world = AcousticWorld.Instance;
            if (!AcousticWorld.Available || _ear == null) return -1f;
            return world.TryGetFilter(_ear, world.PlayerBody, out float lf, out float hf) ? Audibility(lf, hf) : -1f;
        }

        void IAcousticClient.SyncToWorld()
        {
            var world = AcousticWorld.Instance;
            if (_ear == null || !_ear.Valid)
            {
                _ear = world.CreateEmitter("MaidEar", EmitterSettings.MaidEar, EarPosition);
                if (_ear == null) return;
            }
            world.SetPosition(_ear, EarPosition);
            if (world.PlayerBody != null && !AcousticWorld.IsTargetOf(_ear, world.PlayerBody)) world.AddTarget(_ear, world.PlayerBody);
        }

        void IAcousticClient.ReadFromWorld()
        {
            if (_probes.Count == 0) return;
            var world = AcousticWorld.Instance;
            for (int i = _probes.Count - 1; i >= 0; i--)
            {
                var p = _probes[i];
                float a;
                if (world.TryGetFilter(_ear, p.Emitter, out float lf, out float hf)) a = Audibility(lf, hf);
                else if (Time.time > p.Deadline) a = LinecastAudibility(EarPosition, p.Position);
                else continue;

                _probes.RemoveAt(i);
                world.Release(p.Emitter);
                p.Callback?.Invoke(a);
            }
        }

        // ------------------------------------------------------------------ rules (static so tests and the AI share them)

        /// <summary>0–1 audibility from the traced gains. Highs carry footsteps and clacks, so they count for 40%.</summary>
        public static float Audibility(float gainLF, float gainHF) => Mathf.Clamp01(0.6f * gainLF + 0.4f * gainHF);

        /// <summary>Multiplier on a noise's open-air hearing radius.</summary>
        public static float RadiusFactor(float audibility) =>
            Mathf.Lerp(SealedRadiusFactor, 1f, Mathf.Sqrt(Mathf.Clamp01(audibility)));

        public static float LinecastAudibility(Vector3 ear, Vector3 source)
        {
            bool blocked = Physics.Linecast(ear, source + Vector3.up * 0.5f, Layers.SightBlockers, QueryTriggerInteraction.Ignore);
            return blocked ? BlockedFallbackAudibility : 1f;
        }

        /// <summary>Where the maid thinks a noise came from: exact in the open, up to a few metres off when faint.</summary>
        public static Vector3 EstimateSource(Vector3 source, float audibility)
        {
            float error = Mathf.Lerp(MaxLocalizationError, 0.3f, Mathf.Clamp01(audibility));
            Vector2 offset = UnityEngine.Random.insideUnitCircle * error;
            Vector3 guess = source + new Vector3(offset.x, 0f, offset.y);
            return NavMesh.SamplePosition(guess, out NavMeshHit hit, 2f, Generation.NavAreas.Ground) ? hit.position : source;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
