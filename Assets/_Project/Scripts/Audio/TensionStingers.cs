using System.Collections.Generic;
using MaidCafe.AI;
using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// Non-diegetic violin stingers (score, not world sound: 2D, not ray-traced, never heard by the maids). Added by the
    /// GameManager; plays through its own <see cref="StingerMixer"/>.
    /// - Calm: a short dissonant stab (<see cref="Sfx.ViolinStab"/>) at random, every 35-80 s of play.
    /// - Chase: a stab the moment a maid starts chasing, then stabs (<see cref="Sfx.ViolinChase"/>) at a rate inversely
    ///   proportional to the distance of the nearest chasing maid (<see cref="StabRate"/>, re-evaluated every frame, so a
    ///   maid closing in speeds them up at once). A heat value (<see cref="TargetHeat"/>: closeness + time spent in the
    ///   chase) only rises while the chase lasts: it moves the stabs up in register and pitch, raises their volume and
    ///   drives the mixer's harmonic saturation, so the chase sounds louder and harsher the longer it lasts and the
    ///   closer she gets. After the chase the heat falls back over a few seconds.
    /// - Caught: one last stab (<see cref="Sfx.ViolinCatch"/>) at full heat.
    /// Clips: ArtSource/Work/build_audio.py, step "violin" (chase clips sorted from low and warm to high and shrill).
    /// </summary>
    public sealed class TensionStingers : MonoBehaviour
    {
        /// <summary>Chase rhythm: stabs per second = RateDistance / distance in metres (clamped).</summary>
        public const float RateDistance = 7f;

        /// <summary>Slowest chase rhythm: farther than 28 m, one stab every 4 s.</summary>
        public const float MinRate = 0.25f;

        /// <summary>Fastest chase rhythm: closer than 1.75 m, one stab every 0.25 s.</summary>
        public const float MaxRate = 4f;

        /// <summary>Heat from closeness: 0 at this distance or farther, full at <see cref="NearDistance"/>.</summary>
        public const float FarDistance = 18f;

        public const float NearDistance = 2f;

        /// <summary>Seconds of chase that add the full time share of the heat.</summary>
        public const float HeatRampSeconds = 25f;

        private const float HeatFloor = 0.15f;
        private const float HeatFromCloseness = 0.55f;
        private const float HeatFromTime = 0.45f;
        private const float HeatFallPerSecond = 0.25f;

        private const float CalmMinInterval = 35f;
        private const float CalmMaxInterval = 80f;
        private const float CalmFirstMinDelay = 20f;
        private const float CalmFirstMaxDelay = 45f;
        private const float CalmAfterChase = 25f;
        private const float CalmVolume = 0.42f;

        private const float ChaseMinVolume = 0.5f;
        private const float ChaseMaxVolume = 0.85f;
        private const float OpeningBoost = 0.12f;
        private const float CatchVolume = 0.95f;

        /// <summary>Semitones the chase stabs rise at full heat.</summary>
        private const float MaxPitchRise = 2f;

        private StingerMixer _mixer;
        private StingerMixer.Clip[] _calm;
        private StingerMixer.Clip[] _chase;
        private StingerMixer.Clip[] _catch;

        private bool _chasing;
        private bool _wasPlaying;
        private float _chaseTime;
        private float _phase;
        private float _calmTimer;
        private int _lastChase = -1;
        private int _lastCalm = -1;

        /// <summary>Current heat 0..1 (diagnostics).</summary>
        public float Heat { get; private set; }

        /// <summary>Current chase rhythm in stabs per second, 0 when nobody is chasing (diagnostics).</summary>
        public float Rate { get; private set; }

        /// <summary>Distance to the nearest chasing maid, infinity when nobody is chasing (diagnostics).</summary>
        public float ChaserDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Stabs played since the scene started (diagnostics).</summary>
        public int StabCount { get; private set; }

        public StingerMixer Mixer => _mixer;

        /// <summary>Stabs per second during a chase: inversely proportional to the distance, clamped.</summary>
        public static float StabRate(float distance)
        {
            return Mathf.Clamp(RateDistance / Mathf.Max(distance, 0.01f), MinRate, MaxRate);
        }

        /// <summary>
        /// The heat a chase calls for at <paramref name="distance"/> after <paramref name="chaseSeconds"/>; the stingers
        /// keep the highest value reached during the chase.
        /// </summary>
        public static float TargetHeat(float distance, float chaseSeconds)
        {
            float closeness = Mathf.InverseLerp(FarDistance, NearDistance, distance);
            float time = Mathf.Clamp01(chaseSeconds / HeatRampSeconds);
            return Mathf.Clamp01(HeatFloor + HeatFromCloseness * closeness + HeatFromTime * time);
        }

        private void Awake()
        {
            _mixer = StingerMixer.Create(transform);
            _calm = Decode(Sfx.ViolinStab);
            _chase = Decode(Sfx.ViolinChase);
            _catch = Decode(Sfx.ViolinCatch);
            _calmTimer = Random.Range(CalmFirstMinDelay, CalmFirstMaxDelay);
        }

        private void OnEnable() => GameEvents.PlayerCaught += OnPlayerCaught;

        private void OnDisable() => GameEvents.PlayerCaught -= OnPlayerCaught;

        private static StingerMixer.Clip[] Decode(Sfx id)
        {
            var clips = new List<StingerMixer.Clip>();
            int n = SoundBank.Count(id);
            for (int i = 0; i < n; i++)
            {
                var clip = StingerMixer.Clip.From(SoundBank.Get(id, i));
                if (clip != null) clips.Add(clip);
            }
            if (clips.Count == 0) Debug.LogWarning($"[TensionStingers] no clips for {id} (Resources/Audio/{id}/)");
            return clips.ToArray();
        }

        private void Update()
        {
            var game = GameManager.Instance;
            bool playing = game != null && game.State == GameState.Playing;
            float dt = Time.deltaTime;

            // Back in play after loading a floor, a respawn or a reload: no calm stab right away.
            if (playing && !_wasPlaying)
            {
                _calmTimer = Mathf.Max(_calmTimer, Random.Range(CalmFirstMinDelay, CalmFirstMaxDelay));
            }
            _wasPlaying = playing || (game != null && game.State == GameState.Paused);

            var player = PlayerContext.Instance;
            float distance = float.PositiveInfinity;
            bool chased = playing && player != null && NearestChaser(player.transform.position, out distance) != null;
            ChaserDistance = chased ? distance : float.PositiveInfinity;

            if (chased)
            {
                if (!_chasing) BeginChase(distance);
                _chaseTime += dt;
                Heat = Mathf.Max(Heat, TargetHeat(distance, _chaseTime));
                Rate = StabRate(distance);
                _phase += dt * Rate;
                if (_phase >= 1f)
                {
                    _phase -= Mathf.Floor(_phase);
                    PlayChaseStab(0f);
                }
            }
            else
            {
                if (_chasing) EndChase();
                Rate = 0f;
                if (!(game != null && game.State == GameState.Caught)) Heat = Mathf.MoveTowards(Heat, 0f, HeatFallPerSecond * dt);
                if (playing)
                {
                    _calmTimer -= dt;
                    if (_calmTimer <= 0f)
                    {
                        PlayCalmStab();
                        _calmTimer = Random.Range(CalmMinInterval, CalmMaxInterval);
                    }
                }
            }
            _mixer.Heat = Heat;
        }

        private void BeginChase(float distance)
        {
            _chasing = true;
            _chaseTime = 0f;
            _phase = 0f;
            Heat = Mathf.Max(Heat, TargetHeat(distance, 0f));
            _mixer.Heat = Heat;
            PlayChaseStab(OpeningBoost);
        }

        private void EndChase()
        {
            _chasing = false;
            _phase = 0f;
            _calmTimer = Mathf.Max(_calmTimer, CalmAfterChase);
        }

        private void OnPlayerCaught(Transform attacker)
        {
            var game = GameManager.Instance;
            if (game != null && game.State != GameState.Playing && game.State != GameState.Caught) return;
            Heat = 1f;
            _mixer.Heat = 1f;
            _chasing = false;
            _phase = 0f;
            Play(_catch, Random.Range(0, _catch.Length), CatchVolume, Mathf.Pow(2f, Random.Range(-0.3f, 0.3f) / 12f));
        }

        private void PlayChaseStab(float boost)
        {
            if (_chase.Length == 0) return;
            int n = _chase.Length;
            int i = Mathf.Clamp(Mathf.RoundToInt(Heat * (n - 1)) + Random.Range(-1, 2), 0, n - 1);
            if (i == _lastChase && n > 1) i = i == 0 ? 1 : i == n - 1 ? n - 2 : i + (Random.value < 0.5f ? -1 : 1);
            _lastChase = i;
            float volume = Mathf.Lerp(ChaseMinVolume, ChaseMaxVolume, Heat) * Random.Range(0.92f, 1f) + boost;
            float pitch = Mathf.Pow(2f, (Heat * MaxPitchRise + Random.Range(-0.2f, 0.2f)) / 12f);
            Play(_chase, i, volume, pitch);
        }

        private void PlayCalmStab()
        {
            if (_calm.Length == 0) return;
            int i = Random.Range(0, _calm.Length);
            if (i == _lastCalm && _calm.Length > 1) i = (i + Random.Range(1, _calm.Length)) % _calm.Length;
            _lastCalm = i;
            Play(_calm, i, CalmVolume * Random.Range(0.85f, 1f), Mathf.Pow(2f, Random.Range(-1f, 1f) / 12f));
        }

        private void Play(StingerMixer.Clip[] clips, int index, float volume, float pitch)
        {
            if (clips.Length == 0) return;
            if (_mixer.Play(clips[Mathf.Clamp(index, 0, clips.Length - 1)], volume, pitch)) StabCount++;
        }

        private static MaidAI NearestChaser(Vector3 from, out float distance)
        {
            MaidAI best = null;
            distance = float.PositiveInfinity;
            foreach (var maid in MaidAI.All)
            {
                if (maid == null || maid.State != MaidState.Chase) continue;
                float d = Vector3.Distance(from, maid.transform.position);
                if (d < distance)
                {
                    distance = d;
                    best = maid;
                }
            }
            return best;
        }
    }
}
