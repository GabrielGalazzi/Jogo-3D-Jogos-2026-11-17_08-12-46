using System;
using System.Collections.Generic;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Interaction;
using MaidCafe.Player;
using UnityEngine;
using UnityEngine.AI;

namespace MaidCafe.AI
{
    public enum MaidKind
    {
        /// <summary>Patrols rooms; screams (alarm) when she starts a chase.</summary>
        Patrol,

        /// <summary>Director-driven stalker: drifts towards the player's area and answers every alarm.</summary>
        Head
    }

    public enum MaidState
    {
        Patrol,
        Investigate,
        Chase,
        Search,
        Catching
    }

    [Serializable]
    public class MaidStats
    {
        public float WalkSpeed = 1.5f;
        public float InvestigateSpeed = 2.2f;
        public float RunSpeed = 4.4f;
        public float ViewDistanceLit = 15f;
        public float ViewDistanceDark = 7f;
        public float FieldOfView = 110f;
        public float HearingMultiplier = 1f;
        public float AwarenessGain = 2.2f;
        public float CatchDistance = 1.15f;

        /// <summary>After losing sight, how long she keeps running for the last known position before searching anyway (e.g. it is unreachable).</summary>
        public float LoseSightTime = 3f;

        public float SearchDuration = 14f;
    }

    /// <summary>
    /// Porcelain maid brain: perception (sight cone + line of sight + light/crouch modifiers, hearing via
    /// NoiseSystem) feeding an awareness meter, and a small state machine
    /// Patrol → Investigate → Chase → Search → Patrol. Catches the player on contact.
    /// Last known position: while chasing she only follows the player she can see. Once she loses sight she runs to
    /// where she last saw them (she does not know where they went), then searches around it, starting a few metres
    /// along the direction they were moving. Seeing the player again restarts the chase; otherwise she goes back
    /// to patrolling.
    /// Fairness rules: hiding is safe unless the maid saw the player enter the hiding spot (clear line of sight to it)
    /// or hears the player breathing (not holding breath) while searching right next to it; walls muffle the breathing
    /// like any other noise (<see cref="HearsBreathing"/>).
    /// Furniture: nothing but the floor is walkable, so she never stands on a table or the hiding table. Small pieces
    /// (chairs, stools, boxes, low tables, benches, bins; <see cref="ClimbableFurniture"/>) are the
    /// <see cref="NavAreas.Furniture"/> area: she only crosses it while chasing (per-agent area cost), climbing over
    /// the piece (body lifted to its top, slower).
    /// Spreading out: patrol destinations prefer points away from the other maids and from where they are heading
    /// (<see cref="PatrolSpacing"/>), and search stops avoid hiding spots another maid is already checking. When the
    /// player has stayed hidden for a while, <see cref="EnemyDirector"/> calls <see cref="Disperse"/>: she drops the
    /// search and patrols away from the hiding area (the Head Maid stops drifting towards the player meanwhile).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MaidAI : MonoBehaviour
    {
        public static readonly List<MaidAI> All = new List<MaidAI>();

        [SerializeField] private MaidKind kind;
        [SerializeField] private MaidStats stats = new MaidStats();
        [SerializeField] private Transform eyes;

        private EyeGlow[] _eyeGlows;

        private NavMeshAgent _agent;
        private FloorInstance _floor;
        private Rng _rng;
        private float _graceUntil;
        private float _awareness;
        private float _lastSeenTime = -999f;
        private float _lastHeardTime = -999f;
        private MaidHearing _hearing;
        private Vector3 _lastKnownPosition;
        private Vector3 _lastSeenVelocity;
        private Vector3? _searchLead;
        private float _stateTimer;
        private float _repathTimer;
        private float _waitTimer;
        private float _breathDetect;
        private int _searchStops;
        private HidingSpot _ejectTarget;
        private float _lift;
        private MaterialPropertyBlock _block;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public MaidKind Kind => kind;
        public MaidState State { get; private set; } = MaidState.Patrol;
        public float Awareness => _awareness;
        public bool SeesPlayer { get; private set; }

        /// <summary>
        /// True on frames where she is searching and can hear the hidden player breathing (the detection builds up
        /// while this holds; see UpdateSearch). MaidAudio says a line when it turns on.
        /// </summary>
        public bool HearsHiddenBreathing { get; private set; }

        public Vector3 HeadPosition => eyes != null ? eyes.position : transform.position + Vector3.up * 1.55f;

        /// <summary>
        /// True while she has a live lead on the player: chasing, catching, or picking up the hidden player's breathing
        /// (the detection has not faded yet). The maids only spread out from a hidden player when none of them is.
        /// </summary>
        public bool OnPlayersTrail => State == MaidState.Chase || State == MaidState.Catching || _breathDetect > 0f;

        public static bool AnyChasing
        {
            get
            {
                foreach (var m in All) if (m.State == MaidState.Chase) return true;
                return false;
            }
        }

        public void Init(FloorInstance floor, MaidKind maidKind, MaidStats maidStats, Transform eyesTransform, EyeGlow[] eyeGlow, int seed, float grace)
        {
            _floor = floor;
            kind = maidKind;
            stats = maidStats;
            eyes = eyesTransform;
            _eyeGlows = eyeGlow;
            _rng = new Rng(seed);
            _graceUntil = Time.time + grace;
            _agent = GetComponent<NavMeshAgent>();
            _agent.speed = stats.WalkSpeed;
            _agent.angularSpeed = 540f;
            _agent.acceleration = 14f;
            _agent.stoppingDistance = 0.3f;
            _agent.radius = 0.35f;
            _agent.height = 1.8f;
            _agent.autoBraking = true;
            EnterPatrol();
        }

        private void OnEnable()
        {
            All.Add(this);
            NoiseSystem.OnNoise += OnNoise;
        }

        private void OnDisable()
        {
            All.Remove(this);
            NoiseSystem.OnNoise -= OnNoise;
        }

        /// <summary>Teleports the maid (e.g. far from a respawning player) and calms her down.</summary>
        public void ResetTo(Vector3 position, float grace)
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            _agent.Warp(position);
            _lift = 0f;
            _agent.baseOffset = 0f;
            _awareness = 0f;
            _lastSeenTime = -999f;
            _lastHeardTime = -999f;
            _ejectTarget = null;
            _graceUntil = Time.time + grace;
            EnterPatrol();
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            var player = PlayerContext.Instance;
            if (player == null || _agent == null || !_agent.isOnNavMesh) return;

            float dt = Time.deltaTime;
            UpdateClimb(dt);
            HearsHiddenBreathing = false;
            if (State == MaidState.Catching) return;

            bool grace = Time.time < _graceUntil;
            float visibility = 0f;
            SeesPlayer = !grace && CanSee(player, out visibility);
            if (SeesPlayer)
            {
                _lastSeenTime = Time.time;
                _lastKnownPosition = player.transform.position;
                Vector3 v = player.Controller != null ? player.Controller.velocity : Vector3.zero;
                v.y = 0f;
                _lastSeenVelocity = v;
            }

            switch (State)
            {
                case MaidState.Patrol: UpdatePatrol(dt, visibility); break;
                case MaidState.Investigate: UpdateInvestigate(dt, visibility); break;
                case MaidState.Chase: UpdateChase(player, dt); break;
                case MaidState.Search: UpdateSearch(player, dt, visibility); break;
            }

            UpdateEyes();
        }

        private void UpdatePatrol(float dt, float visibility)
        {
            if (Accumulate(dt, visibility, 1f)) return;

            if (Arrived())
            {
                _waitTimer -= dt;
                if (_waitTimer <= 0f) SetDestination(PickPatrolPoint());
            }
        }

        private void UpdateInvestigate(float dt, float visibility)
        {
            if (Accumulate(dt, visibility, 2f)) return;

            if (Arrived())
            {
                _waitTimer -= dt;
                if (_waitTimer <= 0f) EnterSearch(transform.position, 2);
            }
        }

        private void UpdateChase(PlayerContext player, float dt)
        {
            _agent.speed = stats.RunSpeed * (_lift > ClimbSlowLift ? ClimbSpeedFactor : 1f);

            // Dragging the player out of a hiding spot she saw them enter.
            if (player.IsHiding)
            {
                var spot = player.CurrentHidingSpot;
                // Hiding while she is still watching does not work: break line of sight first. She must have seen the
                // player a moment ago and still be looking at the spot, with nothing in between.
                if (!spot.SeenEntering && Time.time - _lastSeenTime < 0.6f && WatchesSpot(spot)) spot.SeenEntering = true;
                if (spot.SeenEntering)
                {
                    _ejectTarget = spot;
                    SetDestinationThrottled(spot.ExitPosition, dt);
                    if (FlatDistance(transform.position, spot.ExitPosition) < 1.0f)
                    {
                        spot.ForceEject();
                        Catch();
                    }
                    return;
                }
                EnterSearch(_lastKnownPosition, SearchStopsAfterChase, _lastSeenVelocity);
                return;
            }

            // Contact: caught even if she is not looking (the player ran into her).
            float distance = FlatDistance(transform.position, player.transform.position);
            if (distance <= stats.CatchDistance && (SeesPlayer || distance < 0.8f))
            {
                Catch();
                return;
            }

            if (SeesPlayer)
            {
                SetDestinationThrottled(player.transform.position, dt);

                // Lunge: she reached the edge of the walkable area (doorway of an elevator car, etc.) and cannot get
                // closer, but the player is within arm's reach with nothing solid in between.
                bool cannotGetCloser = !_agent.pathPending && _agent.remainingDistance <= 0.35f;
                if (cannotGetCloser && distance <= LungeDistance && ClearReach(player))
                {
                    Catch();
                }
                return;
            }

            // Lost sight: she only knows where she last saw (or heard, see Hear) the player. Run there, then search around it.
            SetDestinationThrottled(_lastKnownPosition, dt);
            bool reached = FlatDistance(transform.position, _lastKnownPosition) < 1.0f
                           || (!_agent.pathPending && _agent.hasPath && _agent.remainingDistance <= 0.6f);
            float lastContact = Mathf.Max(_lastSeenTime, _lastHeardTime);
            if (reached || Time.time - lastContact > stats.LoseSightTime + 2f)
            {
                EnterSearch(_lastKnownPosition, SearchStopsAfterChase, _lastSeenVelocity);
            }
        }

        private void UpdateSearch(PlayerContext player, float dt, float visibility)
        {
            if (Accumulate(dt, visibility, 2.5f)) return;

            _stateTimer -= dt;

            // Listening for breathing next to the hiding spot the player is in.
            if (player.IsHiding && !player.IsHoldingBreath && HearsBreathing(player.CurrentHidingSpot))
            {
                HearsHiddenBreathing = true;
                _breathDetect += dt;
                if (_breathDetect > 1.4f)
                {
                    player.CurrentHidingSpot.SeenEntering = true;
                    EnterChase();
                    return;
                }
            }
            else
            {
                _breathDetect = Mathf.Max(0f, _breathDetect - dt * 0.5f);
            }

            if (Arrived())
            {
                _waitTimer -= dt;
                if (_waitTimer > 0f) return;
                if (_searchLead.HasValue)
                {
                    // First look where the player seemed to be heading.
                    SetDestination(_searchLead.Value, 1.2f);
                    _searchLead = null;
                }
                else if (_searchStops > 0 && _stateTimer > 0f)
                {
                    _searchStops--;
                    SetDestination(PickSearchPoint(), 1.2f);
                }
                else
                {
                    EnterPatrol();
                }
            }
        }

        // ------------------------------------------------------------------ perception

        private bool CanSee(PlayerContext player, out float visibility)
        {
            return CanSeePlayer(HeadPosition, transform.forward, stats, State == MaidState.Chase, player, out visibility);
        }

        /// <summary>
        /// Shared sight test (also used by the watcher maid): view distance depends on light, crouching and sprinting
        /// (the phone's night vision has no effect); outside the view cone only very close presence is noticed;
        /// walls block sight.
        /// </summary>
        public static bool CanSeePlayer(Vector3 eye, Vector3 forward, MaidStats stats, bool alert, PlayerContext player, out float visibility)
        {
            visibility = 0f;
            if (player.IsHiding) return false;

            Vector3 target = player.transform.position + Vector3.up * (player.Motor.IsCrouching ? 0.7f : 1.25f);
            Vector3 toTarget = target - eye;
            float distance = toTarget.magnitude;

            bool lit = EnemyDirector.Instance != null && EnemyDirector.Instance.PlayerLit;
            float maxDistance = lit ? stats.ViewDistanceLit : stats.ViewDistanceDark;
            if (player.Motor.IsCrouching) maxDistance *= 0.65f;
            if (player.Motor.IsSprinting) maxDistance *= 1.25f;
            if (alert) maxDistance = Mathf.Max(maxDistance, stats.ViewDistanceLit);
            if (distance > maxDistance) return false;

            // Outside the view cone only very close presence is noticed.
            float angle = Vector3.Angle(new Vector3(forward.x, 0f, forward.z), new Vector3(toTarget.x, 0f, toTarget.z));
            if (angle > stats.FieldOfView * 0.5f && distance > 1.6f) return false;

            if (Physics.Raycast(eye, toTarget / distance, out RaycastHit hit, distance, Layers.SightBlockers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform != player.transform) return false;
            }

            visibility = 1f - distance / maxDistance;
            return true;
        }

        /// <summary>Open-air distance at which a searching maid hears a hidden player breathing.</summary>
        public const float BreathHearingRadius = 2.6f;

        /// <summary>
        /// Breathing is a quiet noise: heard within <see cref="BreathHearingRadius"/> when nothing but the hiding spot
        /// itself is between her ear and the player; through a wall it is muffled like every other noise
        /// (<see cref="MaidHearing.SealedRadiusFactor"/>), which leaves well under a metre, i.e. inside the wall.
        /// </summary>
        private bool HearsBreathing(HidingSpot spot)
        {
            float distance = FlatDistance(transform.position, spot.transform.position);
            if (distance >= BreathHearingRadius) return false; // cheap early out, same result as below
            return BreathNeededToHear(spot) < BreathHearingRadius;
        }

        /// <summary>True if the hiding spot is within her (alert) view distance and cone with a clear line to it.</summary>
        private bool WatchesSpot(HidingSpot spot)
        {
            Vector3 eye = HeadPosition;
            Vector3 to = spot.transform.position - eye;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > Mathf.Max(stats.ViewDistanceLit, stats.ViewDistanceDark)) return false;
            float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z), to);
            if (angle > stats.FieldOfView * 0.5f && distance > 1.6f) return false;
            return spot.IsExposedTo(eye);
        }

        private const float LungeDistance = 3f;
        private const int SearchStopsAfterChase = 3;
        private const float SearchLeadDistance = 3.5f;

        /// <summary>True if nothing solid (including elevator doors and walls on the no-navmesh layer) is between her and the player.</summary>
        private bool ClearReach(PlayerContext player)
        {
            Vector3 from = HeadPosition;
            Vector3 to = player.transform.position + Vector3.up * 1.1f;
            if (Physics.Linecast(from, to, out RaycastHit hit, Layers.SolidExceptMaids, QueryTriggerInteraction.Ignore))
            {
                return hit.collider.transform == player.transform;
            }
            return true;
        }

        /// <summary>Raises awareness while the player is visible. Returns true if a chase started.</summary>
        private bool Accumulate(float dt, float visibility, float multiplier)
        {
            if (SeesPlayer)
            {
                _awareness += dt * stats.AwarenessGain * multiplier * (0.35f + visibility);
                if (_awareness >= 1f)
                {
                    EnterChase();
                    return true;
                }
                if (_awareness > 0.35f && State == MaidState.Patrol)
                {
                    EnterInvestigate(_lastKnownPosition);
                }
            }
            else
            {
                _awareness = Mathf.Max(0f, _awareness - dt * 0.15f);
            }
            return false;
        }

        /// <summary>Called by the watcher maid's bell: hurry to <paramref name="position"/> and look around.</summary>
        public void Summon(Vector3 position)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            if (State == MaidState.Chase || State == MaidState.Catching) return;
            _awareness = Mathf.Max(_awareness, 0.6f);
            _lastKnownPosition = position;
            EnterInvestigate(position);
            _agent.speed = Mathf.Lerp(stats.InvestigateSpeed, stats.RunSpeed, 0.5f);
        }

        /// <summary>
        /// Called by <see cref="EnemyDirector"/> once the player has stayed hidden long enough with no maid on their
        /// trail: she gives up searching or investigating and patrols elsewhere (<see cref="PickPatrolPoint"/> keeps
        /// her out of the hiding area while it is quiet). A patrolling maid already heading out of the area keeps her
        /// route. Never interrupts a chase.
        /// </summary>
        public void Disperse(Vector3 hidingArea)
        {
            if (_agent == null || !_agent.isOnNavMesh || OnPlayersTrail) return;
            _awareness = Mathf.Min(_awareness, 0.2f);
            if (State == MaidState.Patrol && _agent.hasPath
                && FlatDistance(_agent.destination, hidingArea) >= EnemyDirector.QuietZoneRadius)
            {
                return;
            }
            EnterPatrol();
        }

        /// <summary>
        /// Hearing. How much of a noise reaches her comes from the ray-traced audio (<see cref="MaidHearing"/>: paths
        /// around corners, through doorways, muffled through walls), or a single line-of-sight check without the
        /// native library. Audibility scales the noise's hearing radius and how precisely she can place it.
        /// While chasing a player she can no longer see, the player's own noises keep telling her where they went.
        /// </summary>
        private void OnNoise(NoiseEvent e)
        {
            if (_agent == null || State == MaidState.Catching) return;
            if (State == MaidState.Chase && (SeesPlayer || !e.FromPlayer)) return; // only tracking by sound matters mid-chase
            if (Time.time < _graceUntil) return;
            if (!e.FromPlayer && FlatDistance(e.Position, transform.position) < 0.5f) return; // own scream

            if (_hearing == null) _hearing = GetComponent<MaidHearing>();
            if (_hearing != null && _hearing.Evaluate(e, a => Hear(e, a))) return;
            Hear(e, MaidHearing.LinecastAudibility(HeadPosition, e.Position));
        }

        private void Hear(NoiseEvent e, float audibility)
        {
            // Ray-traced results can arrive a few frames later: re-check that she can still react.
            if (this == null || !isActiveAndEnabled || _agent == null || !_agent.isOnNavMesh || State == MaidState.Catching) return;

            float radius = HearingRange(e.Radius, audibility);
            if (kind == MaidKind.Head && e.Type == NoiseType.Alarm) radius *= 10f; // she always answers alarms
            if (FlatDistance(transform.position, e.Position) > radius) return;

            Vector3 heardAt = MaidHearing.EstimateSource(e.Position, audibility);
            if (State == MaidState.Chase)
            {
                if (SeesPlayer || !e.FromPlayer) return;
                _lastKnownPosition = heardAt;
                _lastHeardTime = Time.time;
                return;
            }

            if (e.FromPlayer) _awareness = Mathf.Min(0.9f, _awareness + 0.25f);
            EnterInvestigate(heardAt);
        }

        /// <summary>
        /// Distance (m) at which she hears a noise of <paramref name="noiseRadius"/> reaching her with
        /// <paramref name="audibility"/> (0 sealed .. 1 open air). The single hearing rule: <see cref="Hear"/> and the HUD
        /// noise meter both use it.
        /// </summary>
        public float HearingRange(float noiseRadius, float audibility) =>
            noiseRadius * stats.HearingMultiplier * MaidHearing.RadiusFactor(audibility);

        /// <summary>
        /// Smallest noise radius (m) a player noise at <paramref name="noisePosition"/> needs for her to hear it right
        /// now (<see cref="HearingRange"/> with the same audibility <see cref="OnNoise"/> would use: the traced player
        /// body near the player, otherwise the line-of-sight check). Infinity while she ignores noises.
        /// </summary>
        public float NoiseNeededToHear(Vector3 noisePosition)
        {
            if (_agent == null || State == MaidState.Catching || Time.time < _graceUntil) return float.PositiveInfinity;
            float perMetre = HearingRange(1f, PlayerNoiseAudibility(noisePosition));
            return FlatDistance(transform.position, noisePosition) / Mathf.Max(1e-4f, perMetre);
        }

        /// <summary>
        /// Breathing radius (m) she would need to hear the player hidden in <paramref name="spot"/>; compare with
        /// <see cref="BreathHearingRadius"/>. Same rule as <see cref="HearsBreathing"/>; infinity unless she is searching
        /// (only searching maids listen for breathing).
        /// </summary>
        public float BreathNeededToHear(HidingSpot spot)
        {
            if (spot == null || State != MaidState.Search || Time.time < _graceUntil) return float.PositiveInfinity;
            float distance = FlatDistance(transform.position, spot.transform.position);
            return spot.IsExposedTo(HeadPosition) ? distance : distance / MaidHearing.SealedRadiusFactor;
        }

        private float PlayerNoiseAudibility(Vector3 noisePosition)
        {
            if (_hearing == null) _hearing = GetComponent<MaidHearing>();
            var player = PlayerContext.Instance;
            bool nearBody = player != null && FlatDistance(noisePosition, player.transform.position) < MaidHearing.NearPlayer;
            if (_hearing != null && nearBody)
            {
                float traced = _hearing.PlayerAudibility();
                if (traced >= 0f) return traced;
            }
            return MaidHearing.LinecastAudibility(HeadPosition, noisePosition);
        }

        // ------------------------------------------------------------------ states

        private void EnterPatrol()
        {
            State = MaidState.Patrol;
            SetFurniturePassable(false);
            _agent.speed = stats.WalkSpeed;
            _breathDetect = 0f;
            _searchLead = null;
            SetDestination(PickPatrolPoint());
        }

        private void EnterInvestigate(Vector3 position)
        {
            State = MaidState.Investigate;
            SetFurniturePassable(false);
            _agent.speed = stats.InvestigateSpeed;
            SetDestination(position, 2.5f);
        }

        private void EnterChase()
        {
            bool wasChasing = AnyChasing;
            State = MaidState.Chase;
            SetFurniturePassable(true);
            _awareness = 1f;
            // She knows where the player is when the chase starts (seen, or heard breathing in a hiding spot).
            var player = PlayerContext.Instance;
            if (player != null)
            {
                _lastKnownPosition = player.transform.position;
                _lastSeenTime = Time.time;
            }
            _agent.speed = stats.RunSpeed;
            if (kind == MaidKind.Patrol)
            {
                NoiseSystem.Emit(transform.position, 25f, NoiseType.Alarm, fromPlayer: false); // scream calls the others
            }
            if (!wasChasing) GameEvents.RaiseChaseChanged(true);
        }

        /// <summary>
        /// Searches around <paramref name="around"/>: looks around there, then (if the player was seen moving) a point a
        /// few metres along <paramref name="heading"/>, then up to <paramref name="stops"/> nearby hiding spots or rooms.
        /// </summary>
        private void EnterSearch(Vector3 around, int stops, Vector3 heading = default)
        {
            bool wasChasing = State == MaidState.Chase;
            State = MaidState.Search;
            SetFurniturePassable(false);
            _agent.speed = stats.InvestigateSpeed;
            _stateTimer = stats.SearchDuration;
            _searchStops = stops;
            _awareness = 0.5f;
            _lastKnownPosition = around;
            _searchLead = LeadPoint(around, heading);
            SetDestination(around, 1.5f);
            if (wasChasing && !AnyChasing) GameEvents.RaiseChaseChanged(false);
        }

        /// <summary>A reachable point ~3.5 m from <paramref name="from"/> along the player's last movement, if any.</summary>
        private Vector3? LeadPoint(Vector3 from, Vector3 heading)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.5f * 0.5f) return null;
            Vector3 guess = from + heading.normalized * SearchLeadDistance;
            if (!NavMesh.SamplePosition(guess, out var hit, 1.5f, NavAreas.Ground)) return null;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, hit.position, NavAreas.Ground, path) || path.status != NavMeshPathStatus.PathComplete) return null;
            return hit.position;
        }

        private void Catch()
        {
            State = MaidState.Catching;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            var player = PlayerContext.Instance;
            if (player != null)
            {
                Vector3 look = player.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
            }
            GameEvents.RaiseChaseChanged(false);
            GameEvents.RaisePlayerCaught(transform);
        }

        // ------------------------------------------------------------------ navigation helpers

        /// <summary>Patrol / investigate / search destination, snapped to the ground (never into a furniture footprint).</summary>
        private void SetDestination(Vector3 target, float waitOnArrival = -1f)
        {
            _agent.isStopped = false;
            if (NavMesh.SamplePosition(target, out var hit, 2f, NavAreas.Ground)) target = hit.position;
            _agent.SetDestination(target);
            _waitTimer = waitOnArrival >= 0f ? waitOnArrival : _rng.Range(0.5f, 3f);
        }

        private void SetDestinationThrottled(Vector3 target, float dt)
        {
            _repathTimer -= dt;
            if (_repathTimer > 0f) return;
            _repathTimer = 0.15f;
            _agent.isStopped = false;
            if (NavMesh.SamplePosition(target, out var hit, 1.5f, NavMesh.AllAreas)) _agent.SetDestination(hit.position);
        }

        private bool Arrived()
        {
            if (_agent.pathPending) return false;
            return _agent.remainingDistance <= _agent.stoppingDistance + 0.2f || !_agent.hasPath;
        }

        /// <summary>Patrol destinations prefer points at least this far (m) from every other maid and from where each one is heading.</summary>
        public const float PatrolSpacing = 12f;

        private const int PatrolCandidates = 16;
        private const float PatrolMinTravel = 6f;
        private const float PatrolJitter = 4f;
        private const float StalkChance = 0.65f;
        private const float StalkMinDistance = 5f;
        private const float StalkMaxDistance = 14f;

        // Patrol rule penalties, most important rule last: each one outweighs everything the rules before it can add up
        // to (spacing + jitter is at most 16), so the rules apply in order and spacing only decides among equals.
        private const float MinTravelPenalty = 50f;
        private const float StalkPenalty = 100f;
        private const float QuietZonePenalty = 300f;
        private const float QuietZonePenaltyPerMetre = 20f;

        /// <summary>A search stop this close (m) to another maid, or to where she is heading, is one she is already checking.</summary>
        private const float SearchSpacing = 3f;
        private const float SearchTakenPenalty = 6f;

        /// <summary>
        /// Next patrol destination, the best of <see cref="PatrolCandidates"/> random patrol points. Rules, from the most
        /// important: outside the quiet zone around a hidden player (<see cref="EnemyDirector.TryGetQuietZone"/>; inside
        /// it, the farther out the better); for the Head Maid, usually 5-14 m from the player (she drifts towards the
        /// player's area, but not while the quiet zone is active); more than 6 m from where she is; then as far as
        /// possible, up to <see cref="PatrolSpacing"/>, from the other maids and their destinations.
        /// </summary>
        private Vector3 PickPatrolPoint()
        {
            var points = _floor.PatrolPoints;
            if (points.Count == 0) return transform.position;
            var player = PlayerContext.Instance;
            var director = EnemyDirector.Instance;
            Vector3 quietCenter = default;
            bool quiet = director != null && director.TryGetQuietZone(out quietCenter);
            bool stalk = kind == MaidKind.Head && player != null && !quiet && _rng.Chance(StalkChance);

            Vector3 best = points[0];
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < PatrolCandidates; i++)
            {
                var p = points[_rng.Range(0, points.Count)];
                float score = Mathf.Min(DistanceToOtherMaids(p), PatrolSpacing) + _rng.Range(0f, PatrolJitter);
                if (FlatDistance(p, transform.position) <= PatrolMinTravel) score -= MinTravelPenalty;
                if (stalk)
                {
                    float d = FlatDistance(p, player.transform.position);
                    if (d <= StalkMinDistance || d >= StalkMaxDistance) score -= StalkPenalty;
                }
                if (quiet)
                {
                    float d = FlatDistance(p, quietCenter);
                    if (d < EnemyDirector.QuietZoneRadius)
                    {
                        score -= QuietZonePenalty + (EnemyDirector.QuietZoneRadius - d) * QuietZonePenaltyPerMetre;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        private Vector3 PickSearchPoint()
        {
            // Prefer hiding spots near the last known position: she checks them (but only opens one if she has a reason).
            // A spot another maid is already at or heading to counts as farther away, so two maids rarely check the same one.
            HidingSpot best = null;
            float bestScore = float.MaxValue;
            foreach (var spot in HidingSpot.All)
            {
                float d = FlatDistance(spot.transform.position, _lastKnownPosition);
                if (d > 9f) continue;
                float score = d + _rng.Range(0f, 4f);
                if (DistanceToOtherMaids(spot.ExitPosition) < SearchSpacing) score += SearchTakenPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = spot;
                }
            }
            if (best != null && _rng.Chance(0.6f)) return best.ExitPosition;

            var points = _floor.PatrolPoints;
            for (int i = 0; i < 10; i++)
            {
                var p = points[_rng.Range(0, points.Count)];
                if (FlatDistance(p, _lastKnownPosition) >= 9f) continue;
                if (i < 6 && DistanceToOtherMaids(p) < SearchSpacing) continue; // first tries: a point nobody else is checking
                return p;
            }
            return _lastKnownPosition;
        }

        /// <summary>Distance (m) from <paramref name="point"/> to the nearest other maid or to where she is heading.</summary>
        private float DistanceToOtherMaids(Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            foreach (var other in All)
            {
                if (other == this) continue;
                nearest = Mathf.Min(nearest, FlatDistance(point, other.transform.position));
                var agent = other._agent;
                if (agent != null && agent.isOnNavMesh) nearest = Mathf.Min(nearest, FlatDistance(point, agent.destination));
            }
            return nearest;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ------------------------------------------------------------------ small furniture

        /// <summary>
        /// Cost of the furniture area while chasing: she climbs over a chair or a box when going round it is about two
        /// metres longer.
        /// </summary>
        private const float ChaseFurnitureCost = 2f;

        /// <summary>
        /// Cost in every other state: she always walks round. Finite on purpose, so a maid that is standing in a
        /// footprint when the chase ends can still path out of it.
        /// </summary>
        private const float CalmFurnitureCost = 60f;

        /// <summary>Running speed factor while she is climbing over a piece.</summary>
        private const float ClimbSpeedFactor = 0.6f;

        private const float ClimbSlowLift = 0.15f;
        private const float ClimbLookAhead = 0.8f;
        private const float ClimbBodyHalfWidth = 0.18f;
        private const float ClimbRiseSpeed = 5f;
        private const float ClimbDropSpeed = 3.2f;
        private const float ClimbProbeHeight = 1.6f;

        private void SetFurniturePassable(bool passable)
        {
            _agent.SetAreaCost(NavAreas.Furniture, passable ? ChaseFurnitureCost : CalmFurnitureCost);
        }

        /// <summary>
        /// Lifts her body (agent base offset) onto a climbable piece under or just ahead of her, so she goes over chairs
        /// and boxes instead of through them. Keeps the current height while she is catching the player.
        /// </summary>
        private void UpdateClimb(float dt)
        {
            float target = State == MaidState.Catching ? _lift : FurnitureHeight();
            _lift = Mathf.MoveTowards(_lift, target, (target > _lift ? ClimbRiseSpeed : ClimbDropSpeed) * dt);
            if (!Mathf.Approximately(_agent.baseOffset, _lift)) _agent.baseOffset = _lift;
        }

        /// <summary>Highest climbable furniture top under her body or along her heading, above her feet (0 if none).</summary>
        private float FurnitureHeight()
        {
            Vector3 feet = transform.position - Vector3.up * _agent.baseOffset;
            Vector3 heading = _agent.velocity;
            heading.y = 0f;
            heading = heading.sqrMagnitude > 0.04f ? heading.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, heading) * ClimbBodyHalfWidth;

            float top = ProbeFurniture(feet, feet.y);
            top = Mathf.Max(top, ProbeFurniture(feet + side, feet.y));
            top = Mathf.Max(top, ProbeFurniture(feet - side, feet.y));
            top = Mathf.Max(top, ProbeFurniture(feet + heading * (ClimbLookAhead * 0.5f), feet.y));
            top = Mathf.Max(top, ProbeFurniture(feet + heading * ClimbLookAhead, feet.y));
            return top;
        }

        private static float ProbeFurniture(Vector3 point, float floorY)
        {
            var origin = new Vector3(point.x, floorY + ClimbProbeHeight, point.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ClimbProbeHeight, Layers.SolidExceptMaids, QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }
            if (!hit.collider.TryGetComponent<ClimbableFurniture>(out _)) return 0f;
            return Mathf.Max(0f, hit.point.y - floorY);
        }

        // ------------------------------------------------------------------ visuals

        private void UpdateEyes()
        {
            Color c = State == MaidState.Chase || State == MaidState.Catching
                ? new Color(3f, 0.2f, 0.15f)
                : Color.Lerp(new Color(0.25f, 0.05f, 0.05f), new Color(2f, 0.6f, 0.1f), _awareness);
            SetEyeGlow(_eyeGlows, c, ref _block);
        }

        /// <summary>Applies an emission colour to the eye material slots (per material index on skinned models).</summary>
        public static void SetEyeGlow(EyeGlow[] glows, Color emission, ref MaterialPropertyBlock block)
        {
            if (glows == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var g in glows)
            {
                if (g.Renderer == null) continue;
                if (g.MaterialIndex >= 0)
                {
                    g.Renderer.GetPropertyBlock(block, g.MaterialIndex);
                    block.SetColor(EmissionId, emission);
                    g.Renderer.SetPropertyBlock(block, g.MaterialIndex);
                }
                else
                {
                    g.Renderer.GetPropertyBlock(block);
                    block.SetColor(EmissionId, emission);
                    g.Renderer.SetPropertyBlock(block);
                }
            }
        }
    }
}
