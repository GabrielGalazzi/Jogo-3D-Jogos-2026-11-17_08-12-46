using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.AI
{
    /// <summary>Maids placed on one floor (see <see cref="EnemyDirector.RosterFor"/>).</summary>
    public readonly struct FloorRoster
    {
        public readonly int Patrols;
        public readonly bool Head;
        public readonly int Watchers;

        public FloorRoster(int patrols, bool head, int watchers)
        {
            Patrols = patrols;
            Head = head;
            Watchers = watchers;
        }

        /// <summary>Maids that walk around and can catch the player: patrol maids plus the Head Maid.</summary>
        public int Hunters => Patrols + (Head ? 1 : 0);

        public int Total => Hunters + Watchers;

        public override string ToString() => $"{Patrols} patrol{(Head ? " + Head" : "")} + {Watchers} bell";
    }

    /// <summary>
    /// Spawns and manages the maids of the current floor: counts follow a gradual threat ladder
    /// (<see cref="RosterFor"/>), stats scale with depth and difficulty, spawn points are far from the player, and
    /// respawns relocate enemies with a grace period.
    /// Anti-camping: once the player has stayed hidden for <see cref="DisperseAfterHiding"/> seconds with no maid on
    /// their trail, the maids give up on that area and spread out (<see cref="MaidAI.Disperse"/>); their patrols avoid
    /// the hiding area until <see cref="QuietZoneLinger"/> seconds after the player comes out (<see cref="TryGetQuietZone"/>).
    /// Also computes whether the player stands in light (used by enemy sight).
    /// </summary>
    public class EnemyDirector : MonoBehaviour
    {
        public const float SpawnGrace = 6f;
        public const float RespawnGrace = 5f;

        /// <summary>Seconds the player stays hidden, with no maid chasing or hearing them, before the maids spread out.</summary>
        public const float DisperseAfterHiding = 12f;

        /// <summary>Radius (m) around the hiding spot that patrol destinations avoid once the maids have spread out.</summary>
        public const float QuietZoneRadius = 8f;

        /// <summary>Seconds the patrols keep avoiding the hiding area after the player leaves the hiding spot.</summary>
        public const float QuietZoneLinger = 15f;

        /// <summary>The third hunter on a floor is the Head Maid (at most one); every hunter after her is a patrol maid.</summary>
        public const int HeadMaidFromHunters = 3;

        public static EnemyDirector Instance { get; private set; }

        private readonly List<MaidAI> _maids = new List<MaidAI>();
        private readonly List<WatcherMaid> _watchers = new List<WatcherMaid>();
        private FloorInstance _floor;
        private float _lightTimer;
        private float _hiddenFor;
        private bool _dispersed;
        private Vector3 _quietCenter;
        private float _quietUntil = -1f;

        public bool PlayerLit { get; private set; }
        public IReadOnlyList<MaidAI> Maids => _maids;
        public IReadOnlyList<WatcherMaid> Watchers => _watchers;

        /// <summary>Seconds the player has been hiding without interruption (0 when not hiding).</summary>
        public float HiddenTime => _hiddenFor;

        /// <summary>True once the maids have spread out from the player's current hiding spot.</summary>
        public bool Dispersed => _dispersed;

        /// <summary>
        /// True while patrol destinations must avoid the area around <paramref name="center"/> (radius
        /// <see cref="QuietZoneRadius"/>): from the moment the maids spread out until <see cref="QuietZoneLinger"/>
        /// seconds after the player leaves the hiding spot.
        /// </summary>
        public bool TryGetQuietZone(out Vector3 center)
        {
            center = _quietCenter;
            return _dispersed || Time.time < _quietUntil;
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SpawnFor(FloorInstance floor)
        {
            _maids.Clear();
            _floor = floor;
            ResetDispersal();
            int depth = floor.Layout.FloorIndex;
            var rng = new Rng(Rng.Combine(floor.Layout.Seed, 99));
            var roster = RosterFor(RunConfig.Difficulty, depth);

            var used = new List<Vector3>();
            // Patrol maids step through the model pool from a random start, so maids on one floor never share a model
            // (until there are more maids than models). Own random stream: spawn positions stay the same per seed.
            var looks = MaidVisuals.PatrolLooks;
            int lookOffset = new Rng(Rng.Combine(floor.Layout.Seed, 77)).Range(0, looks.Length);
            for (int i = 0; i < roster.Patrols; i++)
            {
                var stats = StatsFor(MaidKind.Patrol, depth);
                var look = looks[(lookOffset + i) % looks.Length];
                _maids.Add(MaidFactory.Create(MaidKind.Patrol, stats, floor, PickSpawn(floor, rng, floor.SpawnPosition, used), rng.Range(0, int.MaxValue), SpawnGrace, look));
            }
            if (roster.Head)
            {
                var stats = StatsFor(MaidKind.Head, depth);
                _maids.Add(MaidFactory.Create(MaidKind.Head, stats, floor, PickSpawn(floor, rng, floor.SpawnPosition, used), rng.Range(0, int.MaxValue), SpawnGrace + 4f));
            }

            _watchers.Clear();
            int watchers = roster.Watchers;
            var usedRooms = new HashSet<int>();
            // Posts reserved by FloorBuilder before the furniture went in.
            foreach (var slot in floor.WatcherPosts)
            {
                if (_watchers.Count >= watchers) break;
                if (!TryPostAt(slot, out var post, out var facing)) continue;
                usedRooms.Add(slot.Room);
                _watchers.Add(MaidFactory.CreateWatcher(WatcherStats(depth), floor, post, facing, SpawnGrace));
            }

            // Fallback (a reserved post with no NavMesh in front of it): any wall slot still free, as before.
            var postRng = new Rng(Rng.Combine(floor.Layout.Seed, 515));
            while (_watchers.Count < watchers && TryPickWatcherPost(floor, postRng, usedRooms, out var post, out var facing))
            {
                _watchers.Add(MaidFactory.CreateWatcher(WatcherStats(depth), floor, post, facing, SpawnGrace));
            }
        }

        /// <summary>Standing point 0.65 m out from a wall slot, on the ground NavMesh, facing into the room.</summary>
        private static bool TryPostAt(WallSlot slot, out Vector3 position, out Quaternion facing)
        {
            var d3 = new Vector3(slot.Dir.x, 0f, slot.Dir.y);
            Vector3 p = FloorLayout.CellCenter(slot.Cell) + d3 * (FloorLayout.CellSize * 0.5f - 0.65f);
            facing = Quaternion.LookRotation(-d3);
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 1f, NavAreas.Ground))
            {
                position = hit.position;
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>
        /// Threat step of floor <paramref name="depth"/> (0-based) on the ladder of <see cref="RosterForStep"/>: one step
        /// per floor on Normal (floor n = step n), one step ahead on Hardcore, two steps every three floors on Easy.
        /// </summary>
        public static int ThreatStep(Difficulty difficulty, int depth)
        {
            depth = Mathf.Max(0, depth);
            switch (difficulty)
            {
                case Difficulty.Easy: return 1 + depth * 2 / 3;
                case Difficulty.Hardcore: return depth + 2;
                default: return depth + 1;
            }
        }

        /// <summary>
        /// Maids on a threat step. Odd steps add a hunter, even steps put a bell maid next to the previous step's
        /// hunters, so the pressure grows by at most one maid per step and one new thing at a time:
        /// 1 = 1 patrol · 2 = 1 patrol + bell · 3 = 2 patrols · 4 = 2 patrols + bell · 5 = 2 patrols + Head ·
        /// 6 = 2 patrols + Head + bell · 7 = 3 patrols + Head · … The third hunter is the Head Maid
        /// (<see cref="HeadMaidFromHunters"/>).
        /// </summary>
        public static FloorRoster RosterForStep(int step)
        {
            step = Mathf.Max(1, step);
            int hunters = (step + 1) / 2;
            bool head = hunters >= HeadMaidFromHunters;
            int watchers = step % 2 == 0 ? 1 : 0;
            return new FloorRoster(hunters - (head ? 1 : 0), head, watchers);
        }

        /// <summary>Maids on floor <paramref name="depth"/> (0-based) for a difficulty.</summary>
        public static FloorRoster RosterFor(Difficulty difficulty, int depth) => RosterForStep(ThreatStep(difficulty, depth));

        /// <summary>Number of watcher (bell) maids on a floor of the current run (<see cref="RosterFor"/>).</summary>
        public static int WatcherCount(int depth) => RosterFor(RunConfig.Difficulty, depth).Watchers;

        /// <summary>Boxes of sweets placed on a floor: one per watcher plus a spare (two on Easy).</summary>
        public static int GiftCount(int depth)
        {
            int watchers = WatcherCount(depth);
            if (watchers == 0) return 0;
            return watchers + (RunConfig.Difficulty == Difficulty.Easy ? 2 : RunConfig.Difficulty == Difficulty.Hardcore ? 0 : 1);
        }

        /// <summary>Sends the nearest patrol maid(s) that are not already chasing to <paramref name="position"/>.</summary>
        public void Summon(Vector3 position, int count)
        {
            var candidates = new List<MaidAI>();
            foreach (var m in _maids)
            {
                if (m == null || m.Kind != MaidKind.Patrol) continue;
                if (m.State == MaidState.Chase || m.State == MaidState.Catching) continue;
                candidates.Add(m);
            }
            candidates.Sort((a, b) => (a.transform.position - position).sqrMagnitude.CompareTo((b.transform.position - position).sqrMagnitude));
            for (int i = 0; i < count && i < candidates.Count; i++) candidates[i].Summon(position);
        }

        private static MaidStats WatcherStats(int depth)
        {
            float d = Mathf.Clamp01(depth / 9f);
            float diff = RunConfig.Difficulty == Difficulty.Easy ? -0.25f : RunConfig.Difficulty == Difficulty.Hardcore ? 0.3f : 0f;
            var s = StatsFor(MaidKind.Patrol, depth);
            s.ViewDistanceLit = 12f + 2f * d;
            s.ViewDistanceDark = 6f + 1.5f * d;
            s.FieldOfView = 100f;
            s.AwarenessGain = Mathf.Max(0.5f, 0.9f + 0.6f * d + diff * 0.8f);
            return s;
        }

        /// <summary>
        /// Fallback post against a wall in a room (not a hallway, not the arrival room), facing into the room, from the
        /// wall slots left after the furniture (the reserved <see cref="FloorInstance.WatcherPosts"/> come first).
        /// </summary>
        private static bool TryPickWatcherPost(FloorInstance floor, Rng rng, HashSet<int> usedRooms, out Vector3 position, out Quaternion facing)
        {
            position = default;
            facing = Quaternion.identity;
            var layout = floor.Layout;
            var rooms = new List<int>();
            foreach (int room in floor.Slots.RoomsWithWallSlots())
            {
                if (room == layout.Start.Room || usedRooms.Contains(room) || layout.Rooms[room].IsHallway) continue;
                if (Vector3.Distance(floor.RoomCenter(room), floor.SpawnPosition) < FloorBuilder.WatcherMinSpawnDistance) continue;
                rooms.Add(room);
            }
            rng.Shuffle(rooms);
            foreach (int room in rooms)
            {
                if (!floor.Slots.TryTakeWall(room, out var slot)) continue;
                if (!TryPostAt(slot, out position, out facing)) continue;
                usedRooms.Add(room);
                return true;
            }
            return false;
        }

        /// <summary>Moves every maid far away from the player's respawn point and calms them down.</summary>
        public void RelocateAwayFrom(Vector3 playerPosition)
        {
            if (_floor == null) return;
            var rng = new Rng(Random.Range(0, int.MaxValue));
            var used = new List<Vector3>();
            foreach (var maid in _maids)
            {
                if (maid == null) continue;
                maid.ResetTo(PickSpawn(_floor, rng, playerPosition, used), RespawnGrace);
            }
            foreach (var watcher in _watchers)
            {
                if (watcher != null) watcher.ResetAfterRespawn(RespawnGrace);
            }
            ResetDispersal();
            GameEvents.RaiseChaseChanged(false);
        }

        public void Clear()
        {
            _maids.Clear();
            _watchers.Clear();
            _floor = null;
            ResetDispersal();
        }

        private void ResetDispersal()
        {
            _hiddenFor = 0f;
            _dispersed = false;
            _quietUntil = -1f;
        }

        /// <summary>
        /// Anti-camping. Counts how long the player has been hiding; once it passes <see cref="DisperseAfterHiding"/>
        /// and no maid is on the player's trail (<see cref="MaidAI.OnPlayersTrail"/>), every maid is told once to spread
        /// out. Leaving the hiding spot starts the <see cref="QuietZoneLinger"/> countdown.
        /// </summary>
        private void UpdateDispersal(float dt)
        {
            var player = PlayerContext.Instance;
            if (player == null || _floor == null || !player.IsHiding)
            {
                if (_dispersed) _quietUntil = Time.time + QuietZoneLinger;
                _dispersed = false;
                _hiddenFor = 0f;
                return;
            }

            _hiddenFor += dt;
            if (_dispersed || _hiddenFor < DisperseAfterHiding) return;
            foreach (var maid in _maids)
            {
                if (maid != null && maid.OnPlayersTrail) return;
            }

            _dispersed = true;
            _quietCenter = player.CurrentHidingSpot.transform.position;
            foreach (var maid in _maids)
            {
                if (maid != null) maid.Disperse(_quietCenter);
            }
        }

        private static MaidStats StatsFor(MaidKind kind, int depth)
        {
            float d = Mathf.Clamp01(depth / 9f);
            float diff = RunConfig.Difficulty == Difficulty.Easy ? -0.25f : RunConfig.Difficulty == Difficulty.Hardcore ? 0.3f : 0f;
            var s = new MaidStats
            {
                WalkSpeed = 1.4f + 0.3f * d,
                InvestigateSpeed = 2.1f + 0.4f * d,
                RunSpeed = 4.1f + 0.6f * d + diff,
                ViewDistanceLit = 14f + 3f * d,
                ViewDistanceDark = 6f + 2f * d,
                FieldOfView = 110f,
                HearingMultiplier = 1f + 0.3f * d + diff * 0.5f,
                AwarenessGain = 2f + 1.2f * d + diff * 2f,
                CatchDistance = 1.15f,
                LoseSightTime = 3f + diff * 2f,
                SearchDuration = 14f + 6f * d
            };
            if (kind == MaidKind.Head)
            {
                s.WalkSpeed += 0.3f;
                s.RunSpeed += 0.5f;
                s.ViewDistanceLit += 4f;
                s.ViewDistanceDark += 3f;
                s.AwarenessGain *= 1.4f;
                s.LoseSightTime += 2f;
                s.SearchDuration += 8f;
            }
            return s;
        }

        private static Vector3 PickSpawn(FloorInstance floor, Rng rng, Vector3 awayFrom, List<Vector3> used)
        {
            var points = floor.PatrolPoints;
            Vector3 best = points.Count > 0 ? points[0] : floor.SpawnPosition;
            float bestScore = float.MinValue;
            for (int i = 0; i < 24 && points.Count > 0; i++)
            {
                var p = points[rng.Range(0, points.Count)];
                float score = Vector3.Distance(p, awayFrom);
                foreach (var u in used) score = Mathf.Min(score, Vector3.Distance(p, u) * 1.5f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            used.Add(best);
            return best;
        }

        private void Update()
        {
            UpdateDispersal(Time.deltaTime);
            _lightTimer -= Time.deltaTime;
            if (_lightTimer > 0f) return;
            _lightTimer = 0.2f;
            PlayerLit = ComputePlayerLit();
        }

        private bool ComputePlayerLit()
        {
            var player = PlayerContext.Instance;
            if (player == null || _floor == null) return false;
            Vector3 p = player.transform.position + Vector3.up;
            foreach (var light in _floor.RoomLights)
            {
                if (light == null || !light.enabled || light.intensity <= 0.05f) continue;
                if ((light.transform.position - p).sqrMagnitude < light.range * light.range * 0.3f) return true;
            }
            return false;
        }
    }
}
