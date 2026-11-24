using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Interaction;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.AI
{
    /// <summary>
    /// The bell maid: a non-violent watcher. She stands at her post and scans the room. If she sees the player
    /// long enough she rings her hand bell, which calls the nearest maid (and the Head Maid) to the player's
    /// position. She never chases, touches or catches the player.
    /// Giving her a box of sweets (a pickup) pacifies her for the rest of the floor: she curtsies and stops ringing.
    /// While the player carries sweets she hesitates before ringing (<see cref="ExtraRingDelay"/>): once fully aware
    /// she keeps watching for that many more seconds first. Without sweets she rings as soon as she is fully aware.
    /// </summary>
    public class WatcherMaid : MonoBehaviour, IInteractable
    {
        public static readonly List<WatcherMaid> All = new List<WatcherMaid>();

        private enum WatchState
        {
            Watching,
            Noticing,
            Ringing,
            Pacified
        }

        private static readonly int RingingId = Animator.StringToHash("Ringing");
        private static readonly int CurtsyId = Animator.StringToHash("Curtsy");
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        private const float SummonInterval = 6f;
        private const float StopRingingAfter = 4f;
        private const float ScanAngle = 40f;

        /// <summary>Extra seconds she waits before ringing for each box of sweets the player carries...</summary>
        public const float RingDelayPerSweet = 1.5f;

        /// <summary>...up to this many seconds in total.</summary>
        public const float MaxExtraRingDelay = 4f;

        /// <summary>Extra bell delay for <paramref name="sweets"/> boxes of sweets (0 with none).</summary>
        public static float ExtraRingDelay(int sweets) => Mathf.Min(MaxExtraRingDelay, Mathf.Max(0, sweets) * RingDelayPerSweet);

        private MaidStats _stats;
        private Transform _eyes;
        private EyeGlow[] _glows;
        private Animator _animator;
        private Transform _head;
        private MaterialPropertyBlock _block;

        private WatchState _state = WatchState.Watching;
        private float _awareness;
        private float _graceUntil;
        private float _lastSeen = -999f;
        private float _nextSummon;
        private float _scanPhase;
        private float _lookYaw;
        private float _lookPitch;
        private bool _announcedNotice;

        /// <summary>Seconds she has kept the player in view while fully aware (counts towards the sweets delay).</summary>
        private float _ringHold;

        public bool IsPacified => _state == WatchState.Pacified;
        public bool IsRinging => _state == WatchState.Ringing;

        /// <summary>True while her awareness is building (she has spotted something but not rung yet).</summary>
        public bool IsNoticing => _state == WatchState.Noticing;
        public float Awareness => _awareness;

        public void Init(MaidStats stats, Transform eyes, EyeGlow[] glows, Animator animator, Transform head, float grace)
        {
            _stats = stats;
            _eyes = eyes;
            _glows = glows;
            _animator = animator;
            _head = head;
            _graceUntil = Time.time + grace;
            _scanPhase = Random.value * 10f;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>After a respawn: calm down (a pacified watcher stays pacified).</summary>
        public void ResetAfterRespawn(float grace)
        {
            _graceUntil = Time.time + grace;
            if (_state == WatchState.Pacified) return;
            _awareness = 0f;
            SetState(WatchState.Watching);
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (_animator != null) _animator.SetFloat(SpeedId, 0f);
            var player = PlayerContext.Instance;
            if (player == null || _stats == null) return;

            float dt = Time.deltaTime;
            if (_state == WatchState.Pacified)
            {
                UpdateEyes(new Color(0.35f, 0.7f, 0.9f) * 0.8f);
                return;
            }

            bool sees = Time.time >= _graceUntil
                        && MaidAI.CanSeePlayer(EyePosition, LookDirection(), _stats, _state == WatchState.Ringing, player, out float visibility)
                        && Gain(dt, visibility);
            if (sees) _lastSeen = Time.time;
            else _awareness = Mathf.Max(0f, _awareness - dt * 0.2f);

            switch (_state)
            {
                case WatchState.Watching:
                    if (_awareness > 0.15f) SetState(WatchState.Noticing);
                    break;
                case WatchState.Noticing:
                    if (!_announcedNotice && _awareness > 0.35f)
                    {
                        _announcedNotice = true;
                        GameEvents.ShowHint("hint.watcherNotice");
                    }
                    if (_awareness >= 1f)
                    {
                        // Sweets make her hesitate: the player has to stay in view this much longer before she rings.
                        if (sees) _ringHold += dt;
                        if (_ringHold >= ExtraRingDelay(player.Gifts)) StartRinging();
                    }
                    else
                    {
                        _ringHold = Mathf.Max(0f, _ringHold - dt);
                        if (_awareness <= 0f) SetState(WatchState.Watching);
                    }
                    break;
                case WatchState.Ringing:
                    if (sees && Time.time >= _nextSummon) Summon(player.transform.position);
                    if (Time.time - _lastSeen > StopRingingAfter)
                    {
                        _awareness = 0.5f;
                        SetState(WatchState.Noticing);
                    }
                    break;
            }

            Color eye = _state == WatchState.Ringing
                ? new Color(3f, 0.25f, 0.15f)
                : Color.Lerp(new Color(0.2f, 0.12f, 0.05f), new Color(2.2f, 1.1f, 0.15f), _awareness);
            UpdateEyes(eye);
        }

        private bool Gain(float dt, float visibility)
        {
            _awareness = Mathf.Min(1f, _awareness + dt * _stats.AwarenessGain * (0.35f + visibility));
            return true;
        }

        private void StartRinging()
        {
            SetState(WatchState.Ringing);
            var player = PlayerContext.Instance;
            Summon(player != null ? player.transform.position : transform.position);
            GameEvents.ShowHint("hint.watcherRang");
        }

        private void Summon(Vector3 playerPosition)
        {
            _nextSummon = Time.time + SummonInterval;
            // The bell is loud: the Head Maid answers alarms from anywhere; the nearest maid is sent to the player.
            NoiseSystem.Emit(transform.position + Vector3.up, 28f, NoiseType.Alarm, fromPlayer: false);
            EnemyDirector.Instance?.Summon(playerPosition, 1);
        }

        private void SetState(WatchState state)
        {
            _state = state;
            if (state != WatchState.Noticing) _ringHold = 0f;
            if (_animator != null) _animator.SetBool(RingingId, state == WatchState.Ringing);
        }

        private void UpdateEyes(Color emission) => MaidAI.SetEyeGlow(_glows, emission, ref _block);

        // ------------------------------------------------------------------ head / sight direction

        private Vector3 EyePosition => _eyes != null ? _eyes.position : transform.position + Vector3.up * 1.45f;

        /// <summary>Where she is looking: a slow scan around her post, or straight at the player once she noticed them.</summary>
        private Vector3 LookDirection() => Quaternion.AngleAxis(_lookYaw, Vector3.up) * transform.forward;

        private void LateUpdate()
        {
            var player = PlayerContext.Instance;
            float dt = Time.deltaTime;
            float targetYaw;
            float targetPitch = 0f;
            bool tracking = player != null && (_state != WatchState.Watching) && Time.time - _lastSeen < 2.5f;
            if (_state == WatchState.Pacified && player != null)
            {
                tracking = Vector3.Distance(player.transform.position, transform.position) < 6f;
            }

            if (tracking)
            {
                Vector3 to = player.EyePosition - EyePosition;
                targetYaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, new Vector3(to.x, 0f, to.z), Vector3.up), -75f, 75f);
                targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -25f, 25f);
            }
            else
            {
                _scanPhase += dt * 0.35f;
                // Scan with pauses: hold at each side, then turn (doll-like).
                float s = Mathf.Sin(_scanPhase);
                targetYaw = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.35f) * ScanAngle;
            }

            float k = 1f - Mathf.Exp(-dt * (tracking ? 6f : 2.5f));
            _lookYaw = Mathf.Lerp(_lookYaw, targetYaw, k);
            _lookPitch = Mathf.Lerp(_lookPitch, targetPitch, k);

            if (_head != null)
            {
                _head.rotation = Quaternion.AngleAxis(_lookYaw, transform.up)
                               * Quaternion.AngleAxis(_lookPitch, transform.right)
                               * _head.rotation;
            }
        }

        // ------------------------------------------------------------------ interaction (gift)

        public string PromptKey => PlayerContext.Instance != null && PlayerContext.Instance.Gifts > 0 ? "interact.giveGift" : "interact.inspect";
        public string NameKey => "object.watcher";
        public float HoldDuration => PlayerContext.Instance != null && PlayerContext.Instance.Gifts > 0 ? 0.8f : 0f;

        public bool CanInteract(PlayerContext player) => _state != WatchState.Pacified;

        public void Interact(PlayerContext player)
        {
            if (_state == WatchState.Pacified) return;
            if (player.Gifts <= 0)
            {
                GameEvents.ShowHint("hint.watcherWantsGift");
                return;
            }
            player.Gifts--;
            _awareness = 0f;
            SetState(WatchState.Pacified);
            if (_animator != null) _animator.SetTrigger(CurtsyId);
            GameEvents.ShowHint("hint.watcherPacified");
        }
    }
}
