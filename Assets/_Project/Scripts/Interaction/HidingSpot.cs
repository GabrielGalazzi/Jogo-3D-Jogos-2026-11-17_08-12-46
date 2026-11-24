using System.Collections;
using System.Collections.Generic;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Interaction
{
    public enum HidingKind
    {
        Locker,
        UnderTable
    }

    /// <summary>
    /// A place the player can hide in (locker, under a table). Enemies only search a hiding spot
    /// if they saw the player enter it (<see cref="SeenEntering"/>), which keeps hiding fair.
    /// A spot can have extra exits (<see cref="AddExit"/>, one per side of a table): the player leaves on the side the
    /// camera faces if there is room to stand there, otherwise on the clear side closest to that direction.
    /// </summary>
    public class HidingSpot : MonoBehaviour, IInteractable
    {
        public static readonly List<HidingSpot> All = new List<HidingSpot>();

        private const float StandRadius = 0.35f;
        private const float StandHeight = 1.75f;

        [SerializeField] private HidingKind kind = HidingKind.Locker;
        [Tooltip("Where the player's feet go while hiding; forward = view direction.")]
        [SerializeField] private Transform hidePoint;
        [Tooltip("Where the player's feet go when leaving (default exit; maids drag the player out here).")]
        [SerializeField] private Transform exitPoint;
        [Tooltip("Other places the player can leave through (e.g. the other sides of a table).")]
        [SerializeField] private List<Transform> extraExits = new List<Transform>();
        [SerializeField] private Transform door;
        [SerializeField] private float yawRange = 40f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 20f;
        [SerializeField] private float moveDuration = 0.45f;
        [Tooltip("Camera height while hiding. 0 = use the normal standing eye height.")]
        [SerializeField] private float eyeHeight;

        private PlayerContext _occupant;
        private bool _moving;

        public HidingKind Kind => kind;
        public bool Occupied => _occupant != null;
        public bool SeenEntering { get; set; }
        public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.position;

        public string PromptKey => Occupied ? "interact.leave" : "interact.hide";
        public string NameKey => kind == HidingKind.Locker ? "object.locker" : "object.table";
        public float HoldDuration => 0f;

        public void Configure(HidingKind hidingKind, Transform hide, Transform exit, Transform doorTransform,
            float hiddenEyeHeight, float viewYawRange, float viewMinPitch, float viewMaxPitch)
        {
            kind = hidingKind;
            hidePoint = hide;
            exitPoint = exit;
            door = doorTransform;
            eyeHeight = hiddenEyeHeight;
            yawRange = viewYawRange;
            minPitch = viewMinPitch;
            maxPitch = viewMaxPitch;
        }

        /// <summary>Adds another place the player can leave through (feet position, outside the spot).</summary>
        public void AddExit(Transform exit)
        {
            if (exit != null) extraExits.Add(exit);
        }

        /// <summary>
        /// Where the player leaves. First choice: the exit on the side of the spot's footprint that the view ray
        /// (from the hiding position, along <paramref name="viewYaw"/>) crosses. If there is no room to stand there
        /// (<see cref="HasRoomToStand"/>), the other exits in order of how close their direction is to the view.
        /// Falls back to the default exit (validated reachable at floor generation) when none is clear.
        /// </summary>
        public Vector3 ChooseExit(float viewYaw)
        {
            if (extraExits.Count == 0) return ExitPosition;

            Vector3 viewLocal = Quaternion.Inverse(transform.rotation) * (Quaternion.Euler(0f, viewYaw, 0f) * Vector3.forward);
            Vector3 facedNormal = FacedSideNormal(viewLocal);

            var candidates = new List<(float key, Vector3 position)>();
            Transform faced = null;
            float bestDot = float.MinValue;
            void Consider(Transform t)
            {
                if (t == null) return;
                Vector3 to = transform.InverseTransformPoint(t.position);
                to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) return;
                float angle = Vector3.Angle(new Vector3(viewLocal.x, 0f, viewLocal.z), to);
                candidates.Add((angle, t.position));
                float dot = Vector3.Dot(to.normalized, facedNormal);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    faced = t;
                }
            }
            Consider(exitPoint);
            foreach (var t in extraExits) Consider(t);

            if (faced != null && facedNormal != Vector3.zero && HasRoomToStand(faced.position)) return faced.position;
            candidates.Sort((a, b) => a.key.CompareTo(b.key));
            foreach (var c in candidates)
            {
                if (HasRoomToStand(c.position)) return c.position;
            }
            return ExitPosition;
        }

        private Bounds? _footprint;

        /// <summary>
        /// Outward normal (local space, ±X or ±Z) of the footprint side the view ray crosses, from the hiding position.
        /// Zero if the spot has no colliders to measure.
        /// </summary>
        private Vector3 FacedSideNormal(Vector3 viewLocal)
        {
            if (!_footprint.HasValue) _footprint = LocalFootprint();
            Bounds b = _footprint.Value;
            if (b.size == Vector3.zero) return Vector3.zero;

            Vector3 origin = hidePoint != null ? transform.InverseTransformPoint(hidePoint.position) : Vector3.zero;
            float tx = Mathf.Abs(viewLocal.x) > 1e-4f
                ? ((viewLocal.x > 0f ? b.max.x : b.min.x) - origin.x) / viewLocal.x
                : float.PositiveInfinity;
            float tz = Mathf.Abs(viewLocal.z) > 1e-4f
                ? ((viewLocal.z > 0f ? b.max.z : b.min.z) - origin.z) / viewLocal.z
                : float.PositiveInfinity;
            return tz <= tx
                ? new Vector3(0f, 0f, Mathf.Sign(viewLocal.z))
                : new Vector3(Mathf.Sign(viewLocal.x), 0f, 0f);
        }

        /// <summary>Local-space bounds of the spot's box colliders (table top, legs, cloth).</summary>
        private Bounds LocalFootprint()
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var box in GetComponentsInChildren<BoxCollider>())
            {
                if (box.isTrigger) continue;
                Vector3 e = box.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = box.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    Vector3 local = transform.InverseTransformPoint(box.transform.TransformPoint(corner));
                    if (!any)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        any = true;
                    }
                    else bounds.Encapsulate(local);
                }
            }
            return any ? bounds : new Bounds();
        }

        private static readonly Collider[] StandHits = new Collider[16];
        private static readonly RaycastHit[] PathHits = new RaycastHit[16];

        /// <summary>
        /// True if a standing player fits at <paramref name="feet"/>: floor under it at the same height, no solid
        /// geometry (walls, furniture) in the standing capsule, and nothing solid between the hiding place and it.
        /// The spot's own geometry and the player do not count; maids do not either (leaving into one is the
        /// player's risk).
        /// </summary>
        public bool HasRoomToStand(Vector3 feet)
        {
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            int solid = Layers.SolidExceptMaids;

            if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 0.8f, solid, ignore)) return false;
            if (Mathf.Abs(ground.point.y - feet.y) > 0.15f) return false;

            Vector3 bottom = feet + Vector3.up * (StandRadius + 0.05f);
            Vector3 top = feet + Vector3.up * (StandHeight - StandRadius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, StandRadius, StandHits, solid, ignore);
            for (int i = 0; i < count; i++)
            {
                if (!IsOwnOrOccupant(StandHits[i].transform)) return false;
            }

            Vector3 from = (hidePoint != null ? hidePoint.position : transform.position) + Vector3.up * 0.4f;
            Vector3 to = feet + Vector3.up * 0.4f;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance > 0.01f)
            {
                count = Physics.RaycastNonAlloc(from, delta / distance, PathHits, distance, solid, ignore);
                for (int i = 0; i < count; i++)
                {
                    if (!IsOwnOrOccupant(PathHits[i].collider.transform)) return false;
                }
            }
            return true;
        }

        private bool IsOwnOrOccupant(Transform t)
        {
            if (t.IsChildOf(transform)) return true;
            var player = _occupant != null ? _occupant : PlayerContext.Instance;
            return player != null && t.IsChildOf(player.transform);
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private static readonly RaycastHit[] ExposureHits = new RaycastHit[16];

        /// <summary>
        /// True when a straight line from <paramref name="point"/> reaches the hidden player's position or the spot's
        /// opening without crossing any geometry other than the spot itself (its door, panels or cloth). A wall,
        /// a closed elevator door or a large piece of furniture in between returns false. Used by the maids for the
        /// two senses that can expose a hidden player: watching them get in, and hearing them breathe.
        /// </summary>
        public bool IsExposedTo(Vector3 point)
        {
            Vector3 inside = (hidePoint != null ? hidePoint.position : transform.position)
                             + Vector3.up * (eyeHeight > 0f ? eyeHeight + 0.1f : 1.2f);
            Vector3 opening = (transform.position + ExitPosition) * 0.5f + Vector3.up * 1.0f;
            return ClearLine(point, inside) || ClearLine(point, opening);
        }

        private bool ClearLine(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, ExposureHits, distance, Layers.SightBlockers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = ExposureHits[i].collider.transform;
                if (hit.IsChildOf(transform)) continue;
                if (_occupant != null && hit.IsChildOf(_occupant.transform)) continue;
                return false;
            }
            return true;
        }

        public bool CanInteract(PlayerContext player)
        {
            if (_moving) return false;
            return _occupant == null ? !player.IsHiding : _occupant == player;
        }

        public void Interact(PlayerContext player)
        {
            if (_occupant == player) StartCoroutine(Exit(player));
            else if (_occupant == null) StartCoroutine(Enter(player));
        }

        /// <summary>Used by enemies that saw the player enter: instantly drags the player out.</summary>
        public void ForceEject()
        {
            if (_occupant == null) return;
            StopAllCoroutines();
            var player = _occupant;
            ReleaseOccupant();
            player.Motor.EyeHeightOverride = null;
            player.Motor.KeepStanding = false;
            player.Controller.enabled = true;
            player.Teleport(ExitPosition, player.Look.Yaw);
            player.SetHidingSpot(null);
            player.Look.ClearLimits();
        }

        /// <summary>Clears the occupant without moving the player (respawn/reset).</summary>
        public void ReleaseOccupant()
        {
            _occupant = null;
            _moving = false;
            SeenEntering = false;
            SetDoor(false);
        }

        private IEnumerator Enter(PlayerContext player)
        {
            _moving = true;
            _occupant = player;
            player.SetHidingSpot(this);
            player.Controller.enabled = false;
            // A locker is entered standing: a crouched player stands up as they step in (capsule now, camera
            // during the move) and stays standing until they have left it.
            if (kind == HidingKind.Locker) player.Motor.StandUpNow();
            if (eyeHeight > 0f) player.Motor.EyeHeightOverride = eyeHeight;
            NoiseSystem.Emit(transform.position, 3f, NoiseType.Door);
            PlayDoorSound(opening: true);

            // With a full turn allowed (under a table) the view keeps its direction; otherwise it turns to face out.
            bool freeYaw = yawRange >= 180f;
            float hiddenYaw = freeYaw ? player.Look.Yaw : hidePoint.eulerAngles.y;

            SetDoor(true);
            yield return MovePlayer(player, hidePoint.position, hiddenYaw);
            SetDoor(false);
            PlayDoorSound(opening: false);

            player.Look.SetLimits(hiddenYaw, yawRange, minPitch, maxPitch);
            _moving = false;
        }

        private IEnumerator Exit(PlayerContext player)
        {
            _moving = true;
            player.Look.ClearLimits();
            NoiseSystem.Emit(transform.position, 3f, NoiseType.Door);
            PlayDoorSound(opening: true);

            // Leave on the side the camera faces (or the closest clear side); a single-exit spot uses its exit.
            Vector3 exit = ChooseExit(player.Look.Yaw);

            SetDoor(true);
            yield return MovePlayer(player, exit, player.Look.Yaw);
            SetDoor(false);
            PlayDoorSound(opening: false);

            player.Motor.EyeHeightOverride = null;
            player.Motor.KeepStanding = false;
            player.Controller.enabled = true;
            player.SetHidingSpot(null);
            _occupant = null;
            SeenEntering = false;
            _moving = false;
        }

        private IEnumerator MovePlayer(PlayerContext player, Vector3 target, float targetYaw)
        {
            Vector3 start = player.transform.position;
            float startYaw = player.Look.Yaw;
            for (float t = 0f; t < 1f; t += Time.deltaTime / moveDuration)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                player.transform.position = Vector3.Lerp(start, target, k);
                player.Look.SetYaw(Mathf.LerpAngle(startYaw, targetYaw, k));
                yield return null;
            }
            player.transform.position = target;
            player.Look.SetYaw(targetYaw);
        }

        /// <summary>
        /// Locker: the squeaky door as it swings open, the slam when it shuts behind the player. Table: one rustle of
        /// the tablecloth as the player slips under or out (nothing when it falls back).
        /// </summary>
        private void PlayDoorSound(bool opening)
        {
            Vector3 at = transform.position + Vector3.up;
            if (kind == HidingKind.Locker) GameAudio.PlayAt(opening ? Sfx.LockerOpen : Sfx.LockerClose, at, opening ? 0.7f : 0.8f, 18f);
            else if (opening) GameAudio.PlayAt(Sfx.ClothRustle, at, 0.6f, 12f);
        }

        private void SetDoor(bool open)
        {
            if (door == null) return;
            door.localRotation = Quaternion.Euler(0f, open ? -100f : 0f, 0f);
        }
    }
}
