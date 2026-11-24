using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// A 3D sound source whose volume and low-pass filter follow the ray-traced path from the source to the player's
    /// ear (<see cref="RaytracedListener"/>): occlusion rays find routes around corners and through doorways,
    /// permeation rays carry muffled energy through walls. The source owns a target emitter in the
    /// <see cref="AcousticWorld"/>; a fresh one is traced before its first sound plays, so nothing starts unmuffled.
    /// Without the native library it falls back to a line-of-sight check.
    /// Set <see cref="Occlude"/> to false for sounds made by the player's own body (always direct).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class RaytracedSource : MonoBehaviour, IAcousticClient
    {
        /// <summary>Longest wait for the first raytrace before a sound plays anyway.</summary>
        private const float FirstTraceTimeout = 0.25f;

        /// <summary>Filter changes are eased over roughly 1 / rate seconds.</summary>
        private const float SmoothRate = 3.5f;

        private const float FallbackInterval = 0.12f;
        private const float FallbackBlockedLF = 0.4f;
        private const float FallbackBlockedHF = 0.12f;

        public bool Occlude = true;

        /// <summary>Volume before occlusion (0–1).</summary>
        public float BaseVolume = 1f;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter LowPass { get; private set; }

        /// <summary>Current traced gains (smoothed), for debugging and tests.</summary>
        public float GainLF => _lf;
        public float GainHF => _hf;
        public bool Traced => _traced;

        private AcousticEmitter _emitter;
        private float _lf = 1f, _hf = 1f, _targetLF = 1f, _targetHF = 1f;
        private bool _traced;
        private float _fallbackTimer;

        private AudioClip _pendingClip;
        private float _pendingVolume;
        private bool _pendingLoop;
        private bool _pendingOneShot;
        private float _pendingDeadline;
        private float _pendingStartTime;

        private bool _pooled;
        private bool _started;
        private static AnimationCurve _rolloff;

        // ------------------------------------------------------------------ setup

        /// <summary>Adds a configured source on a new child object.</summary>
        public static RaytracedSource Create(Transform parent, string name, Vector3 localPosition, float maxDistance, bool occlude = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var src = go.AddComponent<RaytracedSource>();
            src.Occlude = occlude;
            src.Source.maxDistance = maxDistance;
            return src;
        }

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.spatialBlend = 1f;
            Source.dopplerLevel = 0f;
            Source.spread = 0f;
            Source.rolloffMode = AudioRolloffMode.Custom;
            Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            Source.minDistance = 1f;
            if (Source.maxDistance > 499f) Source.maxDistance = 30f;
            LowPass = gameObject.AddComponent<AudioLowPassFilter>();
            LowPass.cutoffFrequency = 22000f;
            LowPass.lowpassResonanceQ = 1f;
        }

        /// <summary>Roughly 1/distance near the source, reaching silence at maxDistance (x = distance / maxDistance).</summary>
        private static AnimationCurve Rolloff
        {
            get
            {
                if (_rolloff == null)
                {
                    _rolloff = new AnimationCurve(
                        new Keyframe(0f, 1f), new Keyframe(0.04f, 0.8f), new Keyframe(0.12f, 0.45f), new Keyframe(0.3f, 0.18f),
                        new Keyframe(0.55f, 0.07f), new Keyframe(0.8f, 0.02f), new Keyframe(1f, 0f));
                    for (int i = 0; i < _rolloff.length; i++) _rolloff.SmoothTangents(i, 0f);
                }
                return _rolloff;
            }
        }

        private void OnEnable() => AcousticWorld.Register(this);

        private void OnDisable()
        {
            AcousticWorld.Unregister(this);
            ReleaseEmitter();
            _pendingClip = null;
        }

        private void ReleaseEmitter()
        {
            if (_emitter != null && AcousticWorld.Instance != null) AcousticWorld.Instance.Release(_emitter);
            _emitter = null;
            _traced = false;
        }

        // ------------------------------------------------------------------ playback

        /// <summary>Plays <paramref name="clip"/> (looping or not), waiting for the first raytrace of a new emitter.</summary>
        public void Play(AudioClip clip, float volume = 1f, bool loop = false, float startTime = 0f)
        {
            if (clip == null) return;
            BaseVolume = volume;
            _pendingStartTime = startTime;
            Queue(clip, volume, loop, oneShot: false);
        }

        /// <summary>Plays an overlapping one-shot on this source (footsteps, screams).</summary>
        public void PlayOneShot(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;
            if (ReadyToPlay) Source.PlayOneShot(clip, volumeScale);
            else Queue(clip, volumeScale, loop: false, oneShot: true);
        }

        public void Stop()
        {
            _pendingClip = null;
            Source.Stop();
        }

        public bool IsPlaying => Source.isPlaying || _pendingClip != null;

        private bool ReadyToPlay => !Occlude || _traced || !AcousticWorld.Available;

        private void Queue(AudioClip clip, float volume, bool loop, bool oneShot)
        {
            _pendingClip = clip;
            _pendingVolume = volume;
            _pendingLoop = loop;
            _pendingOneShot = oneShot;
            _pendingDeadline = Time.unscaledTime + FirstTraceTimeout;
            if (ReadyToPlay) StartPending();
        }

        private void StartPending()
        {
            var clip = _pendingClip;
            _pendingClip = null;
            if (!_started || !Source.isPlaying)
            {
                // First sound from this emitter: start from the traced filter instead of easing in from open air.
                _lf = _targetLF;
                _hf = _targetHF;
                Apply();
            }
            _started = true;
            if (_pendingOneShot)
            {
                Source.PlayOneShot(clip, _pendingVolume);
                return;
            }
            Source.clip = clip;
            Source.loop = _pendingLoop;
            Source.time = Mathf.Clamp(_pendingStartTime, 0f, Mathf.Max(0f, clip.length - 0.05f));
            _pendingStartTime = 0f;
            Source.Play();
        }

        // ------------------------------------------------------------------ pooled one-shots (see GameAudio)

        internal void BeginPooled(Vector3 position, AudioClip clip, float volume, float maxDistance, float pitch)
        {
            _pooled = true;
            _started = false;
            transform.position = position;
            Source.maxDistance = maxDistance;
            Source.pitch = pitch;
            Source.loop = false;
            ReleaseEmitter(); // a new position needs a fresh trace
            _lf = _hf = _targetLF = _targetHF = 1f;
            Play(clip, volume, loop: false);
        }

        internal bool PooledFinished => _pooled && _pendingClip == null && _started && !Source.isPlaying && !AudioListener.pause;

        // ------------------------------------------------------------------ acoustics

        void IAcousticClient.SyncToWorld()
        {
            if (!Occlude) return;
            var world = AcousticWorld.Instance;
            if (_emitter == null || !_emitter.Valid)
            {
                _emitter = world.CreateEmitter(name, EmitterSettings.Target, transform.position);
                _traced = false;
                if (_emitter == null) return;
            }
            world.SetPosition(_emitter, transform.position);
            var ear = world.Listener;
            if (ear != null && ear.Valid && !AcousticWorld.IsTargetOf(ear, _emitter)) world.AddTarget(ear, _emitter);
        }

        void IAcousticClient.ReadFromWorld()
        {
            if (!Occlude || _emitter == null) return;
            var world = AcousticWorld.Instance;
            if (world.TryGetFilter(world.Listener, _emitter, out float lf, out float hf))
            {
                _targetLF = lf;
                _targetHF = hf;
                _traced = true;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!Occlude)
            {
                _targetLF = _targetHF = 1f;
            }
            else if (!AcousticWorld.Available)
            {
                _fallbackTimer -= dt;
                if (_fallbackTimer <= 0f)
                {
                    _fallbackTimer = FallbackInterval;
                    UpdateFallback();
                }
            }

            if (_pendingClip != null && (ReadyToPlay || Time.unscaledTime > _pendingDeadline)) StartPending();

            _lf = Mathf.MoveTowards(_lf, _targetLF, dt * SmoothRate);
            _hf = Mathf.MoveTowards(_hf, _targetHF, dt * SmoothRate);
            Apply();

            if (PooledFinished)
            {
                ReleaseEmitter();
                gameObject.SetActive(false);
            }
        }

        private void UpdateFallback()
        {
            var listener = RaytracedListener.Instance;
            if (listener == null)
            {
                _targetLF = _targetHF = 1f;
                return;
            }
            bool blocked = Physics.Linecast(listener.transform.position, transform.position, Layers.SightBlockers, QueryTriggerInteraction.Ignore);
            _targetLF = blocked ? FallbackBlockedLF : 1f;
            _targetHF = blocked ? FallbackBlockedHF : 1f;
        }

        private void Apply()
        {
            Source.volume = BaseVolume * VolumeFromGain(_lf);
            LowPass.cutoffFrequency = CutoffFromGains(_lf, _hf);
            // WebGL ignores AudioLowPassFilter: the page filters this source's channel instead.
            if (WebAudioBridge.Active && Source.isPlaying) WebAudioBridge.ReportSource(transform.position, LowPass.cutoffFrequency);
        }

        /// <summary>Loudness from the low-frequency gain (slightly compressed so faint paths stay audible).</summary>
        public static float VolumeFromGain(float gainLF) => Mathf.Pow(Mathf.Clamp01(gainLF), 0.6f);

        /// <summary>Low-pass cutoff from the high/low gain ratio: 22 kHz when nothing is in the way, ~300 Hz fully muffled.</summary>
        public static float CutoffFromGains(float gainLF, float gainHF)
        {
            float r = gainLF > 0.001f ? Mathf.Clamp01(gainHF / gainLF) : 0f;
            return 300f * Mathf.Pow(22000f / 300f, Mathf.Pow(r, 0.8f));
        }
    }
}
