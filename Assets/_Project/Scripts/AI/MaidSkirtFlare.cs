using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MaidCafe.AI
{
    /// <summary>
    /// Lets the legs lift the skirt instead of passing through it (there is no cloth simulation).
    /// Skirts that follow only the hips (the long dress and apron of the "Typical maid") are re-skinned at runtime onto
    /// a grid of "panel" bones parented to the hips: 16 directions around the body × a few rings down the skirt, all
    /// pivoting at the centre of the hips at hip-joint height. Each frame every panel swings outward and upward just
    /// enough for the cloth it carries to clear the legs: the thighs, shins and feet are spheres sampled along the bones,
    /// with radii measured from the leg mesh. Rings below a push fall back toward their rest angle, so the cloth drapes
    /// over a raised knee and hangs down in front of the shin instead of standing out as a cone.
    /// Clearance is tested on the whole ray from the pivot (a cloth point at a large enough angle from straight down is
    /// outside the sphere at any distance), so blending between neighbouring panels, which pulls cloth slightly toward
    /// the pivot, cannot carry it into a leg.
    /// Measured once per model in the bind pose:
    /// - the skirt's inner surface, as the smallest angle from straight down (seen from the pivot) of its vertices per
    ///   direction around the hips and per distance from the pivot (where the bind-pose legs already touch it, the
    ///   surface is raised to them, so a standing maid never flares);
    /// - the leg radii, per bone and along the bone, from the vertices that bone dominates.
    /// Skirt parts that already follow the legs through their own weights (the VRoid skirts) are left as they are.
    /// The panels rise at once (no frame of a leg showing through) and fall back with a short half-life.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public class MaidSkirtFlare : MonoBehaviour
    {
        private const int Panels = 16;
        private const int MaxRings = 8;
        private const int Sectors = 36;
        private const float BinStep = 0.02f;          // distance bins of the measured surface (reference metres)
        private const float MaxDistance = 1.2f;       // from the pivot (reference metres; long dresses reach the floor)
        private const float RampStart = 0.03f;        // below the pivot, where the skirt starts to follow the panels
        private const float RampEnd = 0.22f;          // ... and where it follows them fully (reference metres)
        private const float FirstRing = 0.1f;         // distance of the first ring from the pivot (reference metres)
        private const float RingSpacing = 0.1f;       // between the rings down the skirt (reference metres)
        private const float Clearance = 0.02f;        // leg surface to the skirt's inner surface
        private const float LegMargin = 0.02f;        // cloth this close to a leg belongs to the leg (stockings, shoes)
        private const float MaxFlare = 75f;           // degrees
        private const float KeepBelow = 0.35f;        // share of a ring's swing the ring below keeps at least (drape)
        private const float KeepAbove = 0.15f;        // ... and the ring above (the cloth above a push is drawn along)
        private const float BlendShrink = 0.93f;      // blended panels sit up to this much closer to the pivot
        private const float FallHalfLife = 0.12f;     // seconds
        private const int MinSkirtVertices = 150;
        private const float ReferenceHipsHeight = 0.913f;
        private const int RadiusBins = 4;
        private const float RadiusPercentile = 0.9f;
        private static readonly float[] NeighbourShare = { 0.35f, 0.1f }; // swing passed to the 1st and 2nd neighbours

        /// <summary>One leg part: from a bone to the next one (the foot is extended past the toe bone to the tip).</summary>
        private enum Part { Thigh, Shin, Foot }

        private static readonly float[][] Samples =
        {
            new[] { 0.25f, 0.45f, 0.65f, 0.85f, 1f }, // thigh: the hip joint itself sits inside the pelvis
            new[] { 0.2f, 0.4f, 0.6f, 0.8f, 1f },     // shin
            new[] { 0f, 0.5f, 1f }                    // foot
        };

        private class RendererData
        {
            public Mesh Mesh;
            public bool AppendHips;
        }

        /// <summary>Everything measured for one model (hips-local units, relative to the pivot unless noted).</summary>
        private class ModelData
        {
            public Vector3 Up, Forward, Right;
            public Vector3 Pivot;                 // hips-local position of the panels' common pivot
            public float Step;                    // distance bin size of the measured surface
            public int Bins;
            public float[,] Phi;                  // [sector, bin] inner-surface angle from straight down (radians), NaN = no skirt
            public int[] TopBin, HemBin;          // per sector, first and last bin with skirt (-1 = none)
            public float RampStart, RampEnd, Clearance;
            public float[,] LegRadius;            // [part, bin]
            public Vector3[] PanelAxis;           // swing axis of each direction
            public int Rings;
            public float RingStart, RingStep;     // distance of ring 0 and between rings
            public readonly Dictionary<string, RendererData> Renderers = new Dictionary<string, RendererData>();
        }

        private static readonly Dictionary<string, ModelData> Models = new Dictionary<string, ModelData>();

        private ModelData _data;
        private Transform _hips;
        private readonly Transform[] _legL = new Transform[4], _legR = new Transform[4]; // upper leg, lower leg, foot, toes
        private Transform[] _panels;                         // [ring * Panels + direction]
        private float[] _angle, _need, _column, _spread;
        private Renderer _visibility;

        /// <summary>Strongest swing this frame, in degrees (for tests and tuning).</summary>
        public float MaxFlareNow { get; private set; }

        /// <summary>
        /// Re-skins the skirt onto the panels. Call right after instantiation, while the model is still in its bind
        /// pose, and after every other component that reads the original meshes. Returns false (and does nothing)
        /// when the model has no hips-driven skirt.
        /// </summary>
        public bool Init(Animator animator, Transform model, string key)
        {
            if (animator == null || !animator.isHuman) return false;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!BindLeg(animator, _legL, true) || !BindLeg(animator, _legR, false) || _hips == null) return false;

            if (!Models.TryGetValue(key, out _data))
            {
                _data = Measure(animator, model);
                Models[key] = _data;
            }
            if (_data == null || _data.Renderers.Count == 0) return false;

            int count = _data.Rings * Panels;
            _panels = new Transform[count];
            for (int ring = 0; ring < _data.Rings; ring++)
            {
                for (int p = 0; p < Panels; p++)
                {
                    var panel = new GameObject($"SkirtPanel{ring}_{p}").transform;
                    panel.SetParent(_hips, false);
                    panel.localPosition = _data.Pivot;
                    panel.localRotation = Quaternion.identity;
                    panel.localScale = Vector3.one;
                    _panels[ring * Panels + p] = panel;
                }
            }
            _angle = new float[count];
            _need = new float[count];
            _spread = new float[count];
            _column = new float[_data.Rings];

            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!_data.Renderers.TryGetValue(smr.name, out var rd)) continue;
                var bones = new List<Transform>(smr.bones);
                if (rd.AppendHips) bones.Add(_hips);
                bones.AddRange(_panels);
                smr.sharedMesh = rd.Mesh;
                smr.bones = bones.ToArray();
                // The swing can carry the hem well outside the imported bounds.
                Transform root = smr.rootBone != null ? smr.rootBone : smr.transform;
                var bounds = smr.localBounds;
                bounds.Expand(1.2f / Mathf.Max(1e-5f, root.lossyScale.x));
                smr.localBounds = bounds;
                if (_visibility == null) _visibility = smr;
            }
            return _visibility != null;
        }

        private static bool BindLeg(Animator a, Transform[] leg, bool left)
        {
            leg[0] = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            leg[1] = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            leg[2] = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            leg[3] = a.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            return leg[0] != null && leg[1] != null && leg[2] != null;
        }

        // ------------------------------------------------------------------ geometry helpers

        /// <summary>Start and end of a leg part, hips-local, relative to the pivot.</summary>
        private static void PartEnds(Transform hips, Vector3 pivot, Transform[] leg, Part part, out Vector3 a, out Vector3 b)
        {
            Vector3 P(Transform t) => hips.InverseTransformPoint(t.position) - pivot;
            switch (part)
            {
                case Part.Thigh:
                    a = P(leg[0]);
                    b = P(leg[1]);
                    break;
                case Part.Shin:
                    a = P(leg[1]);
                    b = P(leg[2]);
                    break;
                default:
                    a = P(leg[2]);
                    // The toe bone sits at the ball of the foot; the tip is about half that length further on.
                    b = leg[3] != null ? P(leg[3]) + (P(leg[3]) - a) * 0.5f : a + (a - P(leg[1])) * 0.35f;
                    break;
            }
        }

        private static float Azimuth(ModelData d, Vector3 planar) =>
            Mathf.Atan2(Vector3.Dot(planar, d.Right), Vector3.Dot(planar, d.Forward));

        private static int SectorOf(float azimuth) =>
            Mathf.Clamp(Mathf.FloorToInt((azimuth + Mathf.PI) / (2f * Mathf.PI) * Sectors), 0, Sectors - 1);

        private static float Ramp(ModelData d, float depth) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(d.RampStart, d.RampEnd, depth));

        private static float WrapAngle(float a)
        {
            while (a > Mathf.PI) a -= 2f * Mathf.PI;
            while (a < -Mathf.PI) a += 2f * Mathf.PI;
            return a;
        }

        /// <summary>
        /// A leg sphere (centre <paramref name="q"/>, radius <paramref name="r"/>) seen from the pivot: its distance,
        /// the angle from straight down of its centre, its direction around the hips and its angular half-width there.
        /// </summary>
        private static void SphereView(ModelData d, Vector3 q, float r, out float distance, out float centre, out float azimuth, out float halfWidth)
        {
            distance = q.magnitude;
            float depth = -Vector3.Dot(q, d.Up);
            Vector3 planar = q + depth * d.Up;
            float rho = planar.magnitude;
            centre = Mathf.Atan2(rho, depth);
            azimuth = Azimuth(d, planar);
            halfWidth = rho > r ? Mathf.Asin(r / rho) : Mathf.PI;
        }

        /// <summary>
        /// Half the angle (seen from the pivot) the sphere covers at distance <paramref name="x"/> from the pivot;
        /// negative where the sphere does not reach that distance.
        /// </summary>
        private static float CrossHalfAngle(float x, float distance, float r)
        {
            if (x <= 1e-5f || Mathf.Abs(x - distance) >= r) return -1f;
            return Mathf.Acos(Mathf.Clamp((x * x + distance * distance - r * r) / (2f * x * distance), -1f, 1f));
        }

        /// <summary>
        /// Largest half-angle the sphere covers over the distances that cloth from rest distance <paramref name="x"/>
        /// can take (blending pulls it up to <see cref="BlendShrink"/> closer to the pivot).
        /// </summary>
        private static float CoverAt(float x, float distance, float r)
        {
            float lo = x * BlendShrink;
            float peak = distance > r ? Mathf.Sqrt(distance * distance - r * r) : 0f; // where the sphere looks widest
            float best = Mathf.Max(CrossHalfAngle(x, distance, r), CrossHalfAngle(lo, distance, r));
            if (peak > lo && peak < x) best = Mathf.Max(best, CrossHalfAngle(peak, distance, r));
            return best;
        }

        /// <summary>Inner-surface angle at a direction and distance; NaN where there is no skirt.</summary>
        private static float SkirtPhi(ModelData d, float azimuth, float distance)
        {
            // Two nearest sectors; the smaller angle (the tighter side) is the one the leg meets first.
            float sf = (azimuth + Mathf.PI) / (2f * Mathf.PI) * Sectors - 0.5f;
            int s0 = ((Mathf.FloorToInt(sf) % Sectors) + Sectors) % Sectors;
            int s1 = (s0 + 1) % Sectors;
            float a = SectorPhi(d, s0, distance), b = SectorPhi(d, s1, distance);
            if (float.IsNaN(a)) return b;
            if (float.IsNaN(b)) return a;
            return Mathf.Min(a, b);
        }

        private static float SectorPhi(ModelData d, int sector, float distance)
        {
            int top = d.TopBin[sector], hem = d.HemBin[sector];
            if (hem < 0) return float.NaN;
            float t = distance / d.Step;
            if (t > hem + 1f || t < top) return float.NaN;
            int i = Mathf.Clamp(Mathf.FloorToInt(t), top, hem);
            int j = Mathf.Min(i + 1, hem);
            return Mathf.Lerp(d.Phi[sector, i], d.Phi[sector, j], Mathf.Clamp01(t - i));
        }

        // ------------------------------------------------------------------ measuring

        private static ModelData Measure(Animator animator, Transform model)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var legL = new Transform[4];
            var legR = new Transform[4];
            BindLeg(animator, legL, true);
            BindLeg(animator, legR, false);

            float k = Mathf.Max(0.1f, Vector3.Dot(hips.position - model.position, model.up) / ReferenceHipsHeight);
            float unit = k / Mathf.Max(1e-5f, hips.lossyScale.x); // reference metres → hips-local units
            var d = new ModelData
            {
                Up = hips.InverseTransformDirection(model.up).normalized,
                Step = BinStep * unit,
                RampStart = RampStart * unit,
                RampEnd = RampEnd * unit,
                Clearance = Clearance * unit,
                // Ring 0 carries the cloth around the hips on its own: that cloth follows the panels only partly, so
                // it can need a large swing that must not spill onto the cloth below it.
                RingStart = FirstRing * unit,
                RingStep = RingSpacing * unit
            };
            d.Forward = Vector3.ProjectOnPlane(hips.InverseTransformDirection(model.forward), d.Up).normalized;
            d.Right = Vector3.Cross(d.Up, d.Forward);
            Vector3 joints = (hips.InverseTransformPoint(legL[0].position) + hips.InverseTransformPoint(legR[0].position)) * 0.5f;
            d.Pivot = Vector3.Dot(joints, d.Up) * d.Up;
            d.Bins = Mathf.CeilToInt(MaxDistance * unit / d.Step) + 1;
            d.PanelAxis = new Vector3[Panels];
            for (int p = 0; p < Panels; p++)
            {
                float alpha = p * 2f * Mathf.PI / Panels;
                Vector3 dir = Mathf.Cos(alpha) * d.Forward + Mathf.Sin(alpha) * d.Right;
                // Rotating "straight down" toward the panel's direction: the skirt swings out and up.
                d.PanelAxis[p] = Vector3.Cross(-d.Up, dir).normalized;
            }

            // Which humanoid bone each skinning bone follows (skirt bones resolve to their humanoid parent).
            var humanOf = new Dictionary<Transform, HumanBodyBones>();
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                Transform t = animator.GetBoneTransform(b);
                if (t != null) humanOf[t] = b;
            }
            var partBone = new Dictionary<Transform, Part>
            {
                [legL[0]] = Part.Thigh, [legR[0]] = Part.Thigh,
                [legL[1]] = Part.Shin, [legR[1]] = Part.Shin,
                [legL[2]] = Part.Foot, [legR[2]] = Part.Foot
            };
            if (legL[3] != null) partBone[legL[3]] = Part.Foot;
            if (legR[3] != null) partBone[legR[3]] = Part.Foot;

            // Gather bind-pose vertices of every non-hair renderer.
            var renderers = new List<SkinnedMeshRenderer>();
            var data = new List<VertexInfo[]>();
            var owned = new List<bool[]>();
            var baked = new Mesh();
            var positions = new List<Vector3>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null || smr.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[MaidSkirtFlare] Mesh '{mesh.name}' is not readable (Read/Write off); its skirt cannot follow the legs.");
                    continue;
                }
                Transform[] bones = smr.bones;
                var hipsOwned = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) hipsOwned[i] = Owner(bones[i], humanOf, model) == HumanBodyBones.Hips;

                // Cloth vertices (materials tagged _CLOTH; skin, face, eyes and hair never flare).
                var isCloth = new bool[mesh.vertexCount];
                Material[] mats = smr.sharedMaterials;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    if (sm >= mats.Length || mats[sm] == null || !mats[sm].name.Contains("_CLOTH")) continue;
                    foreach (int index in mesh.GetIndices(sm)) isCloth[index] = true;
                }

                smr.BakeMesh(baked, true);
                baked.GetVertices(positions);
                var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                NativeArray<byte> perVertex = mesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
                var infos = new VertexInfo[mesh.vertexCount];
                int w = 0;
                for (int i = 0; i < infos.Length; i++)
                {
                    int n = perVertex[i];
                    float hipsWeight = 0f, best = 0f;
                    int dominant = -1;
                    for (int j = 0; j < n; j++)
                    {
                        BoneWeight1 bw = weights[w + j];
                        if (bw.boneIndex < hipsOwned.Length && hipsOwned[bw.boneIndex]) hipsWeight += bw.weight;
                        if (bw.weight > best)
                        {
                            best = bw.weight;
                            dominant = bw.boneIndex;
                        }
                    }
                    w += n;
                    infos[i] = new VertexInfo
                    {
                        Q = hips.InverseTransformPoint(toWorld.MultiplyPoint3x4(positions[i])) - d.Pivot,
                        Hips = hipsWeight,
                        Cloth = isCloth[i],
                        Part = dominant >= 0 && dominant < bones.Length && bones[dominant] != null && partBone.TryGetValue(bones[dominant], out Part part)
                            ? (int)part : -1
                    };
                }
                renderers.Add(smr);
                data.Add(infos);
                owned.Add(hipsOwned);
            }
            Object.Destroy(baked);

            MeasureLegs(d, hips, legL, legR, data, unit);
            if (!MeasureSkirt(d, hips, legL, legR, data)) return d;

            // Rings from where the skirt fully follows the panels down to the longest hem.
            int hemBin = 0;
            foreach (int h in d.HemBin) hemBin = Mathf.Max(hemBin, h);
            float hem = (hemBin + 1) * d.Step;
            d.Rings = Mathf.Clamp(Mathf.CeilToInt((hem - d.RingStart) / d.RingStep) + 1, 1, MaxRings);

            for (int i = 0; i < renderers.Count; i++)
            {
                var rd = BuildMesh(d, renderers[i], hips, data[i], owned[i]);
                if (rd != null) d.Renderers[renderers[i].name] = rd;
            }
            return d;
        }

        private struct VertexInfo
        {
            public Vector3 Q;       // hips-local, relative to the pivot (bind pose)
            public float Hips;      // weight on bones that follow the hips
            public bool Cloth;
            public int Part;        // leg part of the dominant bone, -1 if none
            public bool Skirt;      // set by MeasureSkirt: follows the panels
        }

        private static HumanBodyBones Owner(Transform bone, Dictionary<Transform, HumanBodyBones> humanOf, Transform root)
        {
            for (Transform t = bone; t != null && t != root; t = t.parent)
            {
                if (humanOf.TryGetValue(t, out var b)) return b;
            }
            return HumanBodyBones.LastBone;
        }

        /// <summary>Leg radius per part and along the part, from the vertices each leg bone dominates.</summary>
        private static void MeasureLegs(ModelData d, Transform hips, Transform[] legL, Transform[] legR, List<VertexInfo[]> data, float unit)
        {
            d.LegRadius = new float[3, RadiusBins];
            float[] fallback = { 0.07f * unit, 0.05f * unit, 0.05f * unit };
            for (int part = 0; part < 3; part++)
            {
                PartEnds(hips, d.Pivot, legL, (Part)part, out Vector3 aL, out Vector3 bL);
                PartEnds(hips, d.Pivot, legR, (Part)part, out Vector3 aR, out Vector3 bR);
                var bins = new List<float>[RadiusBins];
                var all = new List<float>();
                for (int i = 0; i < RadiusBins; i++) bins[i] = new List<float>();
                foreach (var infos in data)
                {
                    foreach (var v in infos)
                    {
                        if (v.Part != part || v.Hips >= 0.5f) continue;
                        // Each vertex belongs to the nearer of the two legs.
                        float tL = SegmentT(v.Q, aL, bL), tR = SegmentT(v.Q, aR, bR);
                        float dL = (v.Q - Vector3.Lerp(aL, bL, tL)).magnitude, dR = (v.Q - Vector3.Lerp(aR, bR, tR)).magnitude;
                        float t = dL <= dR ? tL : tR;
                        float dist = Mathf.Min(dL, dR);
                        int bin = Mathf.Clamp(Mathf.FloorToInt(t * RadiusBins), 0, RadiusBins - 1);
                        bins[bin].Add(dist);
                        all.Add(dist);
                    }
                }
                float overall = all.Count >= 8 ? Percentile(all, RadiusPercentile) : fallback[part];
                for (int i = 0; i < RadiusBins; i++) d.LegRadius[part, i] = bins[i].Count >= 8 ? Percentile(bins[i], RadiusPercentile) : overall;
            }
        }

        private static float SegmentT(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            return Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
        }

        private static float Percentile(List<float> values, float p)
        {
            values.Sort();
            return values[Mathf.Clamp(Mathf.FloorToInt((values.Count - 1) * p), 0, values.Count - 1)];
        }

        private static float LegRadiusAt(ModelData d, Part part, float t) =>
            d.LegRadius[(int)part, Mathf.Clamp(Mathf.FloorToInt(t * RadiusBins), 0, RadiusBins - 1)];

        /// <summary>True when a bind-pose point lies on a leg (inside its radius plus the margin).</summary>
        private static bool OnLeg(ModelData d, Transform hips, Transform[] legL, Transform[] legR, Vector3 q, float margin)
        {
            for (int side = 0; side < 2; side++)
            {
                Transform[] leg = side == 0 ? legL : legR;
                for (int part = 0; part < 3; part++)
                {
                    PartEnds(hips, d.Pivot, leg, (Part)part, out Vector3 a, out Vector3 b);
                    float t = SegmentT(q, a, b);
                    if ((q - Vector3.Lerp(a, b, t)).magnitude < LegRadiusAt(d, (Part)part, t) + margin) return true;
                }
            }
            return false;
        }

        /// <summary>Selects the skirt vertices and measures the skirt's inner surface. False if there is no hips-driven skirt.</summary>
        private static bool MeasureSkirt(ModelData d, Transform hips, Transform[] legL, Transform[] legR, List<VertexInfo[]> data)
        {
            float margin = LegMargin * (d.Step / BinStep);
            var phi = new float[Sectors, d.Bins];
            for (int s = 0; s < Sectors; s++)
                for (int b = 0; b < d.Bins; b++) phi[s, b] = float.NaN;
            var hipsCount = new int[Sectors];
            var legCount = new int[Sectors];
            int total = 0;

            foreach (var infos in data)
            {
                for (int i = 0; i < infos.Length; i++)
                {
                    var v = infos[i];
                    if (!v.Cloth) continue;
                    float depth = -Vector3.Dot(v.Q, d.Up);
                    if (depth <= d.RampStart) continue;
                    if (OnLeg(d, hips, legL, legR, v.Q, margin)) continue;
                    Vector3 planar = v.Q + depth * d.Up;
                    int sector = SectorOf(Azimuth(d, planar));
                    if (v.Hips < 0.5f)
                    {
                        legCount[sector]++;
                        if (v.Hips > 0.01f) infos[i].Skirt = true; // partly hips-driven: its hips share follows the panels
                        continue;
                    }
                    hipsCount[sector]++;
                    infos[i].Skirt = true;
                    total++;
                    int bin = Mathf.FloorToInt(v.Q.magnitude / d.Step);
                    if (bin >= d.Bins) continue;
                    float angle = Mathf.Atan2(planar.magnitude, depth);
                    if (float.IsNaN(phi[sector, bin]) || angle < phi[sector, bin]) phi[sector, bin] = angle;
                }
            }
            if (total < MinSkirtVertices) return false;

            // Sectors where the skirt mostly follows the legs need nothing; true gaps (no cloth at all) borrow from
            // their neighbours.
            var active = new bool[Sectors];
            var empty = new bool[Sectors];
            for (int s = 0; s < Sectors; s++)
            {
                active[s] = hipsCount[s] > 0 && hipsCount[s] >= legCount[s];
                empty[s] = hipsCount[s] == 0 && legCount[s] == 0;
                if (!active[s])
                    for (int b = 0; b < d.Bins; b++) phi[s, b] = float.NaN;
            }
            for (int pass = 0; pass < Sectors; pass++)
            {
                bool changed = false;
                for (int s = 0; s < Sectors; s++)
                {
                    if (active[s] || !empty[s]) continue;
                    int from = active[(s + 1) % Sectors] ? (s + 1) % Sectors : active[(s + Sectors - 1) % Sectors] ? (s + Sectors - 1) % Sectors : -1;
                    if (from < 0) continue;
                    for (int b = 0; b < d.Bins; b++) phi[s, b] = phi[from, b];
                    active[s] = true;
                    changed = true;
                }
                if (!changed) break;
            }

            // Skirt extent per sector, and gaps along the distance filled by interpolation.
            d.TopBin = new int[Sectors];
            d.HemBin = new int[Sectors];
            for (int s = 0; s < Sectors; s++)
            {
                int top = -1, hem = -1;
                for (int b = 0; b < d.Bins; b++)
                {
                    if (float.IsNaN(phi[s, b])) continue;
                    if (top < 0) top = b;
                    hem = b;
                }
                d.TopBin[s] = top;
                d.HemBin[s] = hem;
                if (top < 0) continue;
                int last = top;
                for (int b = top + 1; b <= hem; b++)
                {
                    if (float.IsNaN(phi[s, b])) continue;
                    for (int g = last + 1; g < b; g++) phi[s, g] = Mathf.Lerp(phi[s, last], phi[s, b], (g - last) / (float)(b - last));
                    last = b;
                }
            }
            d.Phi = phi;

            // Where the bind-pose legs already reach the skirt, raise the surface to them: standing never flares.
            for (int side = 0; side < 2; side++)
            {
                Transform[] leg = side == 0 ? legL : legR;
                for (int part = 0; part < 3; part++)
                {
                    PartEnds(hips, d.Pivot, leg, (Part)part, out Vector3 a, out Vector3 b);
                    foreach (float t in Samples[part])
                    {
                        float r = LegRadiusAt(d, (Part)part, t) + d.Clearance;
                        SphereView(d, Vector3.Lerp(a, b, t), r, out float distance, out float centre, out float azimuth, out float halfWidth);
                        int b0 = Mathf.Max(0, Mathf.FloorToInt((distance - r) / d.Step));
                        int b1 = Mathf.Min(d.Bins - 1, Mathf.CeilToInt((distance + r) / BlendShrink / d.Step));
                        for (int s = 0; s < Sectors; s++)
                        {
                            if (d.TopBin[s] < 0) continue;
                            float sectorAngle = (s + 0.5f) / Sectors * 2f * Mathf.PI - Mathf.PI;
                            if (Mathf.Abs(WrapAngle(sectorAngle - azimuth)) > halfWidth + Mathf.PI / Sectors) continue;
                            for (int bin = Mathf.Max(b0, d.TopBin[s]); bin <= Mathf.Min(b1, d.HemBin[s]); bin++)
                            {
                                float cover = CoverAt((bin + 0.5f) * d.Step, distance, r);
                                if (cover >= 0f && centre + cover > phi[s, bin]) phi[s, bin] = centre + cover;
                            }
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>Copy of the renderer's mesh with the skirt's hips weight moved onto the panels (null if no skirt).</summary>
        private static RendererData BuildMesh(ModelData d, SkinnedMeshRenderer smr, Transform hips, VertexInfo[] infos, bool[] hipsOwned)
        {
            bool any = false;
            foreach (var v in infos) any |= v.Skirt;
            if (!any) return null;

            Mesh source = smr.sharedMesh;
            Transform[] bones = smr.bones;
            int hipsIndex = Array.IndexOf(bones, hips);
            bool appendHips = hipsIndex < 0;
            var bindposes = new List<Matrix4x4>(source.bindposes);
            if (appendHips)
            {
                hipsIndex = bones.Length;
                bindposes.Add(hips.worldToLocalMatrix * smr.transform.localToWorldMatrix);
            }
            // The panels sit at the pivot without rotation in the bind pose: their bind pose is the hips' one, shifted.
            int panelBase = bindposes.Count;
            Matrix4x4 panelBind = Matrix4x4.Translate(-d.Pivot) * bindposes[hipsIndex];
            for (int p = 0; p < d.Rings * Panels; p++) bindposes.Add(panelBind);

            NativeArray<byte> perVertex = source.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = source.GetAllBoneWeights();
            var newPerVertex = new NativeArray<byte>(perVertex.Length, Allocator.Temp);
            var newWeights = new List<BoneWeight1>(weights.Length + infos.Length * 2);
            var scratch = new List<BoneWeight1>(8);
            int w = 0;
            for (int i = 0; i < perVertex.Length; i++)
            {
                int n = perVertex[i];
                var v = infos[i];
                float depth = -Vector3.Dot(v.Q, d.Up);
                float share = v.Skirt ? Ramp(d, depth) : 0f;
                if (share * v.Hips < 0.002f)
                {
                    for (int j = 0; j < n; j++) newWeights.Add(weights[w + j]);
                    newPerVertex[i] = (byte)n;
                    w += n;
                    continue;
                }

                scratch.Clear();
                float hipsWeight = 0f;
                // Mostly hips-driven cloth goes entirely onto the hips and panels: small leftover leg weights (often
                // from the other leg) would drag it back into a raised knee against the panels.
                bool wholly = v.Hips >= 0.5f;
                for (int j = 0; j < n; j++)
                {
                    BoneWeight1 bw = weights[w + j];
                    if (wholly || (bw.boneIndex < hipsOwned.Length && hipsOwned[bw.boneIndex])) hipsWeight += bw.weight; // rigid with the hips
                    else scratch.Add(bw);
                }
                w += n;

                // Two nearest directions around the hips × two nearest rings down the skirt.
                Vector3 planar = v.Q + depth * d.Up;
                float pf = Azimuth(d, planar) / (2f * Mathf.PI) * Panels;
                if (pf < 0f) pf += Panels;
                int p0 = Mathf.FloorToInt(pf) % Panels;
                int p1 = (p0 + 1) % Panels;
                float f = pf - Mathf.Floor(pf);
                float rf = Mathf.Clamp((v.Q.magnitude - d.RingStart) / d.RingStep, 0f, d.Rings - 1);
                int r0 = Mathf.Min(Mathf.FloorToInt(rf), d.Rings - 1);
                int r1 = Mathf.Min(r0 + 1, d.Rings - 1);
                float g = rf - r0;
                float panel = hipsWeight * share;
                scratch.Add(new BoneWeight1 { boneIndex = hipsIndex, weight = hipsWeight * (1f - share) });
                scratch.Add(new BoneWeight1 { boneIndex = panelBase + r0 * Panels + p0, weight = panel * (1f - f) * (1f - g) });
                scratch.Add(new BoneWeight1 { boneIndex = panelBase + r0 * Panels + p1, weight = panel * f * (1f - g) });
                scratch.Add(new BoneWeight1 { boneIndex = panelBase + r1 * Panels + p0, weight = panel * (1f - f) * g });
                scratch.Add(new BoneWeight1 { boneIndex = panelBase + r1 * Panels + p1, weight = panel * f * g });
                scratch.Sort((x, y) => y.weight.CompareTo(x.weight));
                int count = 0;
                float sum = 0f;
                for (int j = 0; j < scratch.Count && count < 4; j++)
                {
                    if (scratch[j].weight < 1e-4f) break;
                    sum += scratch[j].weight;
                    count++;
                }
                for (int j = 0; j < count; j++)
                {
                    BoneWeight1 bw = scratch[j];
                    bw.weight /= sum;
                    newWeights.Add(bw);
                }
                newPerVertex[i] = (byte)count;
            }

            var mesh = Object.Instantiate(source);
            mesh.name = source.name + "_SkirtFlare";
            var weightArray = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(newPerVertex, weightArray);
            mesh.bindposes = bindposes.ToArray();
            weightArray.Dispose();
            newPerVertex.Dispose();
            return new RendererData { Mesh = mesh, AppendHips = appendHips };
        }

        // ------------------------------------------------------------------ runtime

        private void LateUpdate()
        {
            if (_data == null || _panels == null) return;
            if (_visibility != null && !_visibility.isVisible) return;

            int rings = _data.Rings;
            Array.Clear(_need, 0, _need.Length);
            // First what each ring needs at its own distance, then top-ups where cloth between two rings (whose swing is
            // interpolated) would still fall short.
            Accumulate(_legL, false);
            Accumulate(_legR, false);
            Accumulate(_legL, true);
            Accumulate(_legR, true);

            // Down each direction: the ring below a push keeps part of its swing (the cloth drapes rather than
            // dropping straight back) and the ring above is drawn along part of the way.
            for (int p = 0; p < Panels; p++)
            {
                for (int r = 0; r < rings; r++) _column[r] = _need[r * Panels + p];
                for (int r = 1; r < rings; r++) _column[r] = Mathf.Max(_column[r], _column[r - 1] * KeepBelow);
                for (int r = rings - 2; r >= 0; r--) _column[r] = Mathf.Max(_column[r], _column[r + 1] * KeepAbove);
                for (int r = 0; r < rings; r++) _need[r * Panels + p] = _column[r];
            }

            // Around the body: cloth is one piece, a push drags the neighbouring directions part of the way.
            for (int r = 0; r < rings; r++)
            {
                int row = r * Panels;
                for (int p = 0; p < Panels; p++)
                {
                    float v = _need[row + p];
                    for (int n = 0; n < NeighbourShare.Length; n++)
                    {
                        v = Mathf.Max(v, _need[row + (p + n + 1) % Panels] * NeighbourShare[n]);
                        v = Mathf.Max(v, _need[row + (p + Panels - n - 1) % Panels] * NeighbourShare[n]);
                    }
                    _spread[row + p] = v;
                }
            }

            float fall = Mathf.Pow(0.5f, Time.deltaTime / FallHalfLife);
            float limit = MaxFlare * Mathf.Deg2Rad;
            float max = 0f;
            for (int r = 0; r < rings; r++)
            {
                for (int p = 0; p < Panels; p++)
                {
                    int i = r * Panels + p;
                    float target = Mathf.Min(_spread[i], limit);
                    // Rises at once (no frame of a leg showing through), falls back with the half-life.
                    _angle[i] = target >= _angle[i] ? target : target + (_angle[i] - target) * fall;
                    _panels[i].localRotation = Quaternion.AngleAxis(_angle[i] * Mathf.Rad2Deg, _data.PanelAxis[p]);
                    max = Mathf.Max(max, _angle[i]);
                }
            }
            MaxFlareNow = max * Mathf.Rad2Deg;
        }

        private float RingDistance(int ring) => _data.RingStart + ring * _data.RingStep;

        /// <summary>
        /// Swing the cloth from rest distance <paramref name="x"/> needs (in the sphere's direction) to pass outside the
        /// sphere's cross-section at that distance; 0 if it already does or there is no skirt there.
        /// </summary>
        private float Requirement(float x, float distance, float r, float centre, float azimuth)
        {
            float cover = CoverAt(x, distance, r);
            if (cover < 0f) return 0f;
            float skirt = SkirtPhi(_data, azimuth, x);
            if (float.IsNaN(skirt)) return 0f;
            float swing = centre + cover - skirt;
            if (swing <= 0f) return 0f;
            // Near the waist the cloth follows the panels only partly, so it needs a larger swing.
            return swing / Mathf.Max(0.3f, Ramp(_data, x * Mathf.Cos(skirt)));
        }

        /// <summary>Largest requirement over the rest distances [lo, hi] (a few samples and the sphere's widest point).</summary>
        private float MaxRequirement(float lo, float hi, float distance, float r, float centre, float azimuth)
        {
            if (hi < lo) return 0f;
            float best = 0f;
            for (int s = 0; s <= 4; s++) best = Mathf.Max(best, Requirement(Mathf.Lerp(lo, hi, s * 0.25f), distance, r, centre, azimuth));
            float peak = distance > r ? Mathf.Sqrt(distance * distance - r * r) : 0f;
            if (peak >= lo && peak <= hi) best = Mathf.Max(best, Requirement(peak, distance, r, centre, azimuth));
            if (peak / BlendShrink >= lo && peak / BlendShrink <= hi) best = Mathf.Max(best, Requirement(peak / BlendShrink, distance, r, centre, azimuth));
            return best;
        }

        private bool InRange(int panel, float azimuth, float halfWidth)
        {
            float wedge = 2f * Mathf.PI / Panels;
            return halfWidth >= Mathf.PI || Mathf.Abs(WrapAngle(panel * wedge - azimuth)) <= halfWidth + wedge;
        }

        /// <summary>
        /// Swing each panel needs so that the cloth it carries clears the leg's spheres (into _need). Every panel in a
        /// sphere's angular range is held as if the leg stood right in front of it (neighbouring panels blend into the
        /// cloth in front of the leg). Pass 1 (<paramref name="between"/> false): each ring at its own distance; ring 0
        /// also carries all the cloth above it and the last ring all the cloth below it. Pass 2: cloth between two rings
        /// takes an interpolated swing, so where that falls short both rings are raised by the shortfall.
        /// </summary>
        private void Accumulate(Transform[] leg, bool between)
        {
            int rings = _data.Rings;
            for (int part = 0; part < 3; part++)
            {
                PartEnds(_hips, _data.Pivot, leg, (Part)part, out Vector3 a, out Vector3 b);
                foreach (float t in Samples[part])
                {
                    float r = LegRadiusAt(_data, (Part)part, t) + _data.Clearance;
                    SphereView(_data, Vector3.Lerp(a, b, t), r, out float distance, out float centre, out float azimuth, out float halfWidth);
                    // Cloth from rest distances in this span can end up within the sphere's reach.
                    float near = Mathf.Max(0f, distance - r), far = (distance + r) / BlendShrink;

                    if (!between)
                    {
                        for (int ring = 0; ring < rings; ring++)
                        {
                            float x = RingDistance(ring);
                            float need = x >= near && x <= far ? Requirement(x, distance, r, centre, azimuth) : 0f;
                            if (ring == 0 && near < x) need = Mathf.Max(need, MaxRequirement(near, Mathf.Min(far, x), distance, r, centre, azimuth));
                            if (ring == rings - 1 && far > x) need = Mathf.Max(need, MaxRequirement(Mathf.Max(near, x), far, distance, r, centre, azimuth));
                            if (need <= 0f) continue;
                            for (int p = 0; p < Panels; p++)
                            {
                                int i = ring * Panels + p;
                                if (need > _need[i] && InRange(p, azimuth, halfWidth)) _need[i] = need;
                            }
                        }
                        continue;
                    }

                    for (int ring = 0; ring < rings - 1; ring++)
                    {
                        float x0 = RingDistance(ring), x1 = RingDistance(ring + 1);
                        if (x1 <= near || x0 >= far) continue;
                        for (int s = 1; s <= 4; s++)
                        {
                            float g = s * 0.2f;
                            float x = Mathf.Lerp(x0, x1, g);
                            if (x < near || x > far) continue;
                            float need = Requirement(x, distance, r, centre, azimuth);
                            if (need <= 0f) continue;
                            for (int p = 0; p < Panels; p++)
                            {
                                if (!InRange(p, azimuth, halfWidth)) continue;
                                int i0 = ring * Panels + p, i1 = i0 + Panels;
                                float shortfall = need - Mathf.Lerp(_need[i0], _need[i1], g);
                                if (shortfall <= 0f) continue;
                                _need[i0] += shortfall;
                                _need[i1] += shortfall;
                            }
                        }
                    }
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Models.Clear();
        }
    }
}
