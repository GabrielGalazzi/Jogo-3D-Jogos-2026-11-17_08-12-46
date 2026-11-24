using MaidCafe.AI;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// The sounds a maid makes, all ray-traced towards the player: porcelain heel clacks paced by her stride, the
    /// patrol maid's shriek when she starts a chase, the watcher maid's hand bell while she rings, and voice lines.
    /// Voices: each maid model has its own voice (<see cref="VoiceFor"/>). Patrol maids and the Head Maid speak when
    /// they start hearing the hidden player breathing (a cue to hold the breath); the watcher maid speaks when she
    /// first notices the player and calls the others when she starts ringing. Lines follow the game language.
    /// </summary>
    public sealed class MaidAudio : MonoBehaviour
    {
        private const float WalkStride = 0.75f;
        private const float RunStride = 1.15f;
        private const float RunSpeed = 3f;
        private const float MaxStepDistance = 28f;
        private const float MaxVoiceDistance = 22f;

        /// <summary>Seconds before the same maid says another line.</summary>
        private const float VoiceCooldown = 7f;

        /// <summary>Seconds between lines of any two maids, so they never talk over each other.</summary>
        private const float SharedVoiceGap = 2.5f;

        private static float _nextSharedVoice;

        private MaidAI _ai;
        private WatcherMaid _watcher;
        private RaytracedSource _body;
        private RaytracedSource _mouth;
        private RaytracedSource _bell;
        private Vector3 _lastPosition;
        private float _stride;
        private MaidState _lastState;
        private MaidLook _look = MaidLook.Patrol;
        private float _nextVoice;
        private bool _heardBreathing;
        private bool _watcherAlert;
        private bool _watcherRinging;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _nextSharedVoice = 0f;

        /// <summary>Sets which model this maid is, which picks her voice. Called by MaidFactory right after AddComponent.</summary>
        public void Init(MaidLook look) => _look = look;

        /// <summary>The voice lines (heard-breathing lines for patrol maids and the Head Maid) of a maid model.</summary>
        public static Sfx VoiceFor(MaidLook look)
        {
            switch (look)
            {
                case MaidLook.Head: return Sfx.VoiceHead;
                case MaidLook.StyleAnime: return Sfx.VoiceStyleAnime;
                case MaidLook.Typical: return Sfx.VoiceTypical;
                case MaidLook.Watcher: return Sfx.VoiceWatcherNotice;
                default: return Sfx.VoicePatrol;
            }
        }

        private void Awake()
        {
            _ai = GetComponent<MaidAI>();
            _watcher = GetComponent<WatcherMaid>();
            if (_watcher != null) _look = MaidLook.Watcher;
            _body = RaytracedSource.Create(transform, "MaidBody", new Vector3(0f, 0.9f, 0f), MaxStepDistance);
            _mouth = RaytracedSource.Create(transform, "MaidVoice", new Vector3(0f, 1.5f, 0f), MaxVoiceDistance);
            if (_watcher != null) _bell = RaytracedSource.Create(transform, "HandBell", new Vector3(0.3f, 1.6f, 0f), 40f);
            _lastPosition = transform.position;
            if (_ai != null) _lastState = _ai.State;
        }

        private void Update()
        {
            UpdateSteps();
            UpdateShriek();
            UpdateVoice();
            UpdateBell();
        }

        private void UpdateSteps()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _lastPosition;
            d.y = 0f;
            _lastPosition = p;
            float moved = d.magnitude;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (moved > 2f || moved < 1e-4f) return; // teleported (respawn) or standing still

            float speed = moved / dt;
            _stride += moved;
            float stride = speed > RunSpeed ? RunStride : WalkStride;
            if (_stride < stride) return;
            _stride = 0f;
            float volume = speed > RunSpeed ? 1f : Mathf.Lerp(0.45f, 0.7f, speed / RunSpeed);
            _body.PlayOneShot(SoundBank.Get(Sfx.FootstepMaid), volume);
        }

        private void UpdateShriek()
        {
            if (_ai == null) return;
            var state = _ai.State;
            if (state == MaidState.Chase && _lastState != MaidState.Chase && _ai.Kind == MaidKind.Patrol)
            {
                _mouth.Stop(); // the shriek cuts off whatever she was saying
                _mouth.PlayOneShot(SoundBank.Get(Sfx.MaidShriek), 1f);
            }
            _lastState = state;
        }

        private void UpdateVoice()
        {
            if (_ai != null)
            {
                bool hears = _ai.HearsHiddenBreathing;
                if (hears && !_heardBreathing) Say(VoiceFor(_look));
                _heardBreathing = hears;
            }
            else if (_watcher != null)
            {
                bool alert = _watcher.IsNoticing || _watcher.IsRinging;
                if (_watcher.IsRinging && !_watcherRinging) Say(Sfx.VoiceWatcherCall, force: true);
                else if (alert && !_watcherAlert) Say(Sfx.VoiceWatcherNotice);
                _watcherAlert = alert;
                _watcherRinging = _watcher.IsRinging;
            }
        }

        /// <param name="force">Ignore this maid's own cooldown (still waits for the other maids' lines).</param>
        private void Say(Sfx lines, bool force = false)
        {
            float now = Time.time;
            if ((!force && now < _nextVoice) || now < _nextSharedVoice) return;
            var clip = SoundBank.GetLocalized(lines);
            if (clip == null) return;
            _mouth.Play(clip, 1f);
            _nextVoice = now + VoiceCooldown + clip.length;
            _nextSharedVoice = now + SharedVoiceGap + clip.length * 0.5f;
        }

        private void UpdateBell()
        {
            if (_bell == null) return;
            bool ringing = _watcher.IsRinging;
            if (ringing && !_bell.IsPlaying) _bell.Play(SoundBank.Get(Sfx.HandBellLoop), 0.9f, loop: true);
            else if (!ringing && _bell.IsPlaying) _bell.Stop();
        }
    }
}
