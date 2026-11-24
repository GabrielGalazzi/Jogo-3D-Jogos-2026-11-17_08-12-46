using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// The player's ear, on the camera next to the AudioListener. Owns the listener emitter that casts occlusion and
    /// permeation rays towards every <see cref="RaytracedSource"/> and reverb rays into the room. The traced EAX reverb
    /// (decay, reflections, late reverb, HF/LF damping) drives a Unity AudioReverbZone that follows the camera, so a
    /// tiled kitchen rings and a cluttered dining hall stays dry, and the reverb changes as the player walks between them.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class RaytracedListener : MonoBehaviour, IAcousticClient
    {
        public static RaytracedListener Instance { get; private set; }

        /// <summary>Reverb parameters ease towards the traced values over roughly 1 / rate seconds.</summary>
        private const float ReverbSmoothRate = 2.5f;

        private AcousticEmitter _ear;
        private AudioReverbZone _zone;
        private Reverb _current = Reverb.Default;
        private Reverb _target = Reverb.Default;

        /// <summary>True once the room reverb has been traced at least once.</summary>
        public bool HasTracedReverb { get; private set; }

        public AcousticEmitter Ear => _ear;

        private void Awake()
        {
            var zoneGo = new GameObject("ReverbZone");
            zoneGo.transform.SetParent(transform, false);
            _zone = zoneGo.AddComponent<AudioReverbZone>();
            _zone.reverbPreset = AudioReverbPreset.User;
            _zone.minDistance = 500f;
            _zone.maxDistance = 501f;
            _current.ApplyTo(_zone);
        }

        private void OnEnable()
        {
            Instance = this;
            AcousticWorld.Register(this);
        }

        private void OnDisable()
        {
            AcousticWorld.Unregister(this);
            if (_ear != null && AcousticWorld.Instance != null) AcousticWorld.Instance.Release(_ear);
            _ear = null;
            if (Instance == this) Instance = null;
        }

        void IAcousticClient.SyncToWorld()
        {
            var world = AcousticWorld.Instance;
            if (_ear == null || !_ear.Valid)
            {
                _ear = world.CreateEmitter("PlayerEar", EmitterSettings.PlayerEar, transform.position);
                if (_ear == null) return;
            }
            world.Listener = _ear;
            world.SetPosition(_ear, transform.position);
        }

        void IAcousticClient.ReadFromWorld()
        {
            if (_ear == null) return;
            if (AcousticWorld.Instance.TryGetEAX(_ear, out var eax))
            {
                _target = Reverb.FromEAX(eax);
                if (!HasTracedReverb) _current = _target; // first result: no slow fade from the default room
                HasTracedReverb = true;
            }
        }

        private void Update()
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * ReverbSmoothRate);
            _current = Reverb.Lerp(_current, _target, k);
            _current.ApplyTo(_zone);
            // WebGL ignores AudioReverbZone: the page runs its own reverb with the same parameters.
            if (WebAudioBridge.Active)
            {
                WebAudioBridge.ReportReverb(_current.Room, _current.RoomHF, _current.DecayTime, _current.DecayHFRatio,
                    _current.Reflections, _current.ReflectionsDelay, _current.LateReverb, _current.ReverbDelay, _current.HFReference);
            }
        }

        /// <summary>Reverb parameters in Unity's (I3DL2) units.</summary>
        private struct Reverb
        {
            public float Room, RoomHF, RoomLF, DecayTime, DecayHFRatio, Reflections, ReflectionsDelay, LateReverb, ReverbDelay;
            public float HFReference, LFReference, Diffusion, Density;

            /// <summary>A small, fairly dry room, used until the first trace (and without the native library).</summary>
            public static Reverb Default => new Reverb
            {
                Room = -1100f, RoomHF = -900f, RoomLF = 0f, DecayTime = 0.9f, DecayHFRatio = 0.7f,
                Reflections = -900f, ReflectionsDelay = 0.008f, LateReverb = -500f, ReverbDelay = 0.012f,
                HFReference = 5000f, LFReference = 250f, Diffusion = 100f, Density = 100f
            };

            public static Reverb FromEAX(VAudioNative.VAEAXReverb e) => new Reverb
            {
                Room = Millibels(e.gain, -10000f, 0f),
                RoomHF = Millibels(e.gainHF, -10000f, 0f),
                RoomLF = Millibels(e.gainLF, -10000f, 0f),
                DecayTime = Mathf.Clamp(e.decayTime, 0.1f, 20f),
                DecayHFRatio = Mathf.Clamp(e.decayHFRatio, 0.1f, 2f),
                Reflections = Millibels(e.reflectionsGain, -10000f, 1000f),
                ReflectionsDelay = Mathf.Clamp(e.reflectionsDelay, 0f, 0.3f),
                LateReverb = Millibels(e.lateReverbGain, -10000f, 2000f),
                ReverbDelay = Mathf.Clamp(e.lateReverbDelay, 0f, 0.1f),
                HFReference = Mathf.Clamp(e.hfReference > 0f ? e.hfReference : 5000f, 1000f, 20000f),
                LFReference = Mathf.Clamp(e.lfReference > 0f ? e.lfReference : 250f, 20f, 1000f),
                Diffusion = Mathf.Clamp01(e.diffusion) * 100f,
                Density = Mathf.Clamp01(e.density) * 100f
            };

            private static float Millibels(float linear, float min, float max)
            {
                if (float.IsNaN(linear) || linear <= 1e-5f) return min;
                return Mathf.Clamp(2000f * Mathf.Log10(linear), min, max);
            }

            public static Reverb Lerp(Reverb a, Reverb b, float t) => new Reverb
            {
                Room = Mathf.Lerp(a.Room, b.Room, t),
                RoomHF = Mathf.Lerp(a.RoomHF, b.RoomHF, t),
                RoomLF = Mathf.Lerp(a.RoomLF, b.RoomLF, t),
                DecayTime = Mathf.Lerp(a.DecayTime, b.DecayTime, t),
                DecayHFRatio = Mathf.Lerp(a.DecayHFRatio, b.DecayHFRatio, t),
                Reflections = Mathf.Lerp(a.Reflections, b.Reflections, t),
                ReflectionsDelay = Mathf.Lerp(a.ReflectionsDelay, b.ReflectionsDelay, t),
                LateReverb = Mathf.Lerp(a.LateReverb, b.LateReverb, t),
                ReverbDelay = Mathf.Lerp(a.ReverbDelay, b.ReverbDelay, t),
                HFReference = Mathf.Lerp(a.HFReference, b.HFReference, t),
                LFReference = Mathf.Lerp(a.LFReference, b.LFReference, t),
                Diffusion = Mathf.Lerp(a.Diffusion, b.Diffusion, t),
                Density = Mathf.Lerp(a.Density, b.Density, t)
            };

            public void ApplyTo(AudioReverbZone z)
            {
                if (z == null) return;
                z.room = Mathf.RoundToInt(Room);
                z.roomHF = Mathf.RoundToInt(RoomHF);
                z.roomLF = Mathf.RoundToInt(RoomLF);
                z.decayTime = DecayTime;
                z.decayHFRatio = DecayHFRatio;
                z.reflections = Mathf.RoundToInt(Reflections);
                z.reflectionsDelay = ReflectionsDelay;
                z.reverb = Mathf.RoundToInt(LateReverb);
                z.reverbDelay = ReverbDelay;
                z.HFReference = HFReference;
                z.LFReference = LFReference;
                z.diffusion = Diffusion;
                z.density = Density;
            }
        }
    }
}
