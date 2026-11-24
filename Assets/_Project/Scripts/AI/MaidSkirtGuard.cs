using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaidCafe.AI
{
    /// <summary>
    /// Keeps the maid's hands and forearms outside her skirt in every animation.
    /// The animation clips are shared by all maids and authored on one body, so on fuller skirts and aprons the hands
    /// sink into the cloth. Each model gets a "skirt cone": around the hips' vertical axis, the outer radius of the
    /// body and clothes (hair and arms excluded) is measured in the bind pose per height slice and per direction, and
    /// made to widen monotonically downward from the waist (a flared cone, so the hem's inside is closed too).
    /// After the Animator and <see cref="MaidRestPose"/>, any hand or forearm point inside the cone is pushed out
    /// radially and the arm is re-solved with a two-bone IK (the hand keeps its orientation).
    /// The cone follows the hips bone, like the skirt.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class MaidSkirtGuard : MonoBehaviour
    {
        private const int Sectors = 24;
        private const float SliceStep = 0.03f;      // metres (reference scale)
        private const float ConeBottom = -1.0f;     // below the hips bone (long dresses reach the floor)
        private const float ConeTop = 0.16f;        // above the hips bone (waist)
        private const float Clearance = 0.03f;      // hand/forearm surface to the cloth
        private const float ReferenceHipsHeight = 0.913f;
        private const int Iterations = 3;

        private class Cone
        {
            public float[,] Radius; // [slice, sector], hips-local units
            public Vector3 Up, Forward, Right; // hips-local axes
            public float Bottom, Top, Step;    // hips-local units
        }

        private static readonly Dictionary<string, Cone> Cones = new Dictionary<string, Cone>();

        private Transform _hips;
        private Renderer _body;
        private MaidArmIK.Arm _left, _right;
        private Cone _cone;
        private float _clearanceLocal;
        private readonly Vector3[] _points = new Vector3[8];

        /// <summary>Binds the rig and measures the cone. Call right after instantiation, in the bind pose.</summary>
        public bool Init(Animator animator, Transform model, string key)
        {
            if (animator == null || !animator.isHuman) return false;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _left = MaidArmIK.Bind(animator, model, -1f);
            _right = MaidArmIK.Bind(animator, model, 1f);
            if (_hips == null || _left == null || _right == null) return false;
            if (!Cones.TryGetValue(key, out _cone))
            {
                _cone = Measure(animator, model, _hips);
                Cones[key] = _cone;
            }
            _clearanceLocal = Clearance * Mathf.Max(0.1f, Vector3.Dot(_hips.position - model.position, model.up) / ReferenceHipsHeight)
                              / Mathf.Max(1e-5f, _hips.lossyScale.x);
            _body = model.GetComponentInChildren<SkinnedMeshRenderer>();
            return true;
        }

        // ------------------------------------------------------------------ measuring

        private static Cone Measure(Animator animator, Transform model, Transform hips)
        {
            float k = Mathf.Max(0.1f, Vector3.Dot(hips.position - model.position, model.up) / ReferenceHipsHeight);
            float toLocal = 1f / Mathf.Max(1e-5f, hips.lossyScale.x);
            var cone = new Cone
            {
                Up = hips.InverseTransformDirection(model.up).normalized,
                Bottom = ConeBottom * k * toLocal,
                Top = ConeTop * k * toLocal,
                Step = SliceStep * k * toLocal
            };
            cone.Forward = Vector3.ProjectOnPlane(hips.InverseTransformDirection(model.forward), cone.Up).normalized;
            cone.Right = Vector3.Cross(cone.Up, cone.Forward);
            int slices = Mathf.CeilToInt((cone.Top - cone.Bottom) / cone.Step) + 1;
            var radius = new float[slices, Sectors];

            // Arm, hand and finger vertices never count: the cone is what the arms must stay out of.
            var armBones = new HashSet<Transform>();
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                string n = b.ToString();
                if (n.Contains("Arm") || n.Contains("Hand") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle")
                    || n.Contains("Ring") || n.Contains("Little"))
                {
                    Transform t = animator.GetBoneTransform(b);
                    if (t != null) armBones.Add(t);
                }
            }

            var mesh = new Mesh();
            var vertices = new List<Vector3>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                BoneWeight[] weights = smr.sharedMesh.boneWeights;
                Transform[] bones = smr.bones;
                var isArm = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) isArm[i] = bones[i] != null && IsUnder(bones[i], armBones, model);
                smr.BakeMesh(mesh, true);
                mesh.GetVertices(vertices);
                var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                int count = Mathf.Min(vertices.Count, weights.Length);
                for (int i = 0; i < count; i++)
                {
                    int dominant = Dominant(weights[i]);
                    if (dominant >= 0 && dominant < isArm.Length && isArm[dominant]) continue;
                    Vector3 p = hips.InverseTransformPoint(toWorld.MultiplyPoint3x4(vertices[i]));
                    float h = Vector3.Dot(p, cone.Up);
                    int slice = Mathf.RoundToInt((h - cone.Bottom) / cone.Step);
                    if (slice < 0 || slice >= slices) continue;
                    Vector3 planar = p - h * cone.Up;
                    float r = planar.magnitude;
                    int sector = Sector(cone, planar);
                    if (r > radius[slice, sector]) radius[slice, sector] = r;
                }
            }
            UnityEngine.Object.Destroy(mesh);

            // Fill sectors with no vertices from their angular neighbours, then widen downward (a cone).
            for (int s = 0; s < slices; s++)
            {
                for (int pass = 0; pass < Sectors; pass++)
                {
                    bool changed = false;
                    for (int a = 0; a < Sectors; a++)
                    {
                        if (radius[s, a] > 0f) continue;
                        float n = Mathf.Max(radius[s, (a + 1) % Sectors], radius[s, (a + Sectors - 1) % Sectors]);
                        if (n <= 0f) continue;
                        radius[s, a] = n;
                        changed = true;
                    }
                    if (!changed) break;
                }
            }
            for (int s = slices - 2; s >= 0; s--)
            {
                for (int a = 0; a < Sectors; a++) radius[s, a] = Mathf.Max(radius[s, a], radius[s + 1, a]);
            }
            cone.Radius = radius;
            return cone;
        }

        private static bool IsUnder(Transform bone, HashSet<Transform> set, Transform root)
        {
            for (Transform t = bone; t != null && t != root; t = t.parent)
            {
                if (set.Contains(t)) return true;
            }
            return false;
        }

        private static int Dominant(BoneWeight w)
        {
            int index = -1;
            float best = 0f;
            if (w.weight0 > best) { best = w.weight0; index = w.boneIndex0; }
            if (w.weight1 > best) { best = w.weight1; index = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; index = w.boneIndex2; }
            if (w.weight3 > best) index = w.boneIndex3;
            return index;
        }

        private static int Sector(Cone cone, Vector3 planar)
        {
            float angle = Mathf.Atan2(Vector3.Dot(planar, cone.Right), Vector3.Dot(planar, cone.Forward));
            int sector = Mathf.FloorToInt((angle + Mathf.PI) / (2f * Mathf.PI) * Sectors);
            return Mathf.Clamp(sector, 0, Sectors - 1);
        }

        /// <summary>Cone radius (hips-local) at a hips-local point's height and direction; 0 outside the cone's height.</summary>
        private float RadiusAt(float h, Vector3 planar)
        {
            float t = (h - _cone.Bottom) / _cone.Step;
            if (t < 0f || t > _cone.Radius.GetLength(0) - 1) return 0f;
            int i = Mathf.FloorToInt(t);
            int j = Mathf.Min(i + 1, _cone.Radius.GetLength(0) - 1);
            // Interpolate between the two nearest sectors to avoid steps; take the larger of the two slices.
            float angle = Mathf.Atan2(Vector3.Dot(planar, _cone.Right), Vector3.Dot(planar, _cone.Forward));
            float sf = (angle + Mathf.PI) / (2f * Mathf.PI) * Sectors - 0.5f;
            int a0 = ((Mathf.FloorToInt(sf) % Sectors) + Sectors) % Sectors;
            int a1 = (a0 + 1) % Sectors;
            float w = sf - Mathf.Floor(sf);
            float ri = Mathf.Lerp(_cone.Radius[i, a0], _cone.Radius[i, a1], w);
            float rj = Mathf.Lerp(_cone.Radius[j, a0], _cone.Radius[j, a1], w);
            return Mathf.Max(ri, rj);
        }

        // ------------------------------------------------------------------ runtime

        private void LateUpdate()
        {
            if (_cone == null || (_body != null && !_body.isVisible)) return;
            Guard(_left);
            Guard(_right);
        }

        /// <summary>World push that moves <paramref name="p"/> out of the cone (zero if already outside).</summary>
        private Vector3 Push(Vector3 p)
        {
            Vector3 local = _hips.InverseTransformPoint(p);
            float h = Vector3.Dot(local, _cone.Up);
            Vector3 planar = local - h * _cone.Up;
            float r = planar.magnitude;
            if (r < 1e-5f) return Vector3.zero;
            float limit = RadiusAt(h, planar);
            if (limit <= 0f) return Vector3.zero;
            limit += _clearanceLocal;
            if (r >= limit) return Vector3.zero;
            return _hips.TransformVector(planar / r * (limit - r));
        }

        private static Vector3 Deepest(Vector3 a, Vector3 b) => b.sqrMagnitude > a.sqrMagnitude ? b : a;

        private void Guard(MaidArmIK.Arm arm)
        {
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                Vector3 elbow = arm.Lower.position, wrist = arm.Hand.position;
                Vector3 knuckles = arm.Middle != null ? arm.Middle.position : wrist;
                Vector3 tips = wrist + (knuckles - wrist) * 2f;

                // Hand: wrist, knuckles, finger tips, both sides of the palm.
                Vector3 hand = Push(wrist);
                hand = Deepest(hand, Push(knuckles));
                hand = Deepest(hand, Push(tips));
                if (arm.Index != null) hand = Deepest(hand, Push(arm.Index.position));
                if (arm.Little != null) hand = Deepest(hand, Push(arm.Little.position));

                // Forearm: elbow and two points along it.
                Vector3 forearm = Push(elbow);
                forearm = Deepest(forearm, Push(Vector3.Lerp(elbow, wrist, 0.35f)));
                forearm = Deepest(forearm, Push(Vector3.Lerp(elbow, wrist, 0.7f)));

                if (hand.sqrMagnitude < 1e-8f && forearm.sqrMagnitude < 1e-8f) return;

                Vector3 target = wrist + hand;
                // Moving the elbow out (and a little forward of where it was) lifts the forearm out of the cloth.
                Vector3 hint = elbow + forearm * 1.5f;
                MaidArmIK.SolveTwoBone(arm, target, hint);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cones.Clear();
        }
    }
}
