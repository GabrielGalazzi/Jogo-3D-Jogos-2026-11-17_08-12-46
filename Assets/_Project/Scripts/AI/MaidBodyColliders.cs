using System;
using System.Collections.Generic;
using MaidCafe.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MaidCafe.AI
{
    /// <summary>
    /// Solid body for an imported maid, so the player cannot walk through her: one capsule per body part
    /// (head, torso, hips + skirt, upper and lower arms, upper and lower legs, feet), fitted to the model's own mesh
    /// in the bind pose and attached to the bones, so the shape follows every animation.
    /// Each vertex belongs to the part of its most influential bone. A part's capsule runs along the part's bone
    /// axis (vertical for the hips/skirt and the head), is centred on its vertices, spans 2–98% of them along the
    /// axis and has the radius that contains 80% of them. Hair is left out: it is soft and would inflate the head.
    /// The capsules are on the MaidBody layer: they block the player but not the maids' sight, hearing or reach.
    /// </summary>
    public static class MaidBodyColliders
    {
        private const float RadiusPercentile = 0.8f;
        private const float EndPercentile = 0.02f;
        private const int MinVertices = 24;

        private enum Part
        {
            Hips, Torso, Head,
            UpperArmL, LowerArmL, UpperArmR, LowerArmR,
            UpperLegL, LowerLegL, FootL, UpperLegR, LowerLegR, FootR
        }

        /// <summary>A fitted capsule, stored relative to the bone it follows.</summary>
        private struct Shape
        {
            public string Name;
            public HumanBodyBones Bone;
            public Vector3 LocalCenter;
            public Quaternion LocalRotation; // the capsule's axis is this rotation's Y
            public float Radius;             // world metres
            public float Height;             // world metres, caps included
        }

        private static readonly Dictionary<string, Shape[]> Cache = new Dictionary<string, Shape[]>();

        /// <summary>
        /// Adds the body capsules to an instantiated maid. Call right after instantiation, while the model is still in
        /// its bind pose. <paramref name="key"/> identifies the model + scale so the fit is computed once.
        /// </summary>
        public static List<CapsuleCollider> Build(Animator animator, Transform model, string key)
        {
            var colliders = new List<CapsuleCollider>();
            if (animator == null || !animator.isHuman) return colliders;
            if (!Cache.TryGetValue(key, out var shapes))
            {
                shapes = Fit(animator, model);
                Cache[key] = shapes;
            }

            foreach (var shape in shapes)
            {
                Transform bone = animator.GetBoneTransform(shape.Bone);
                if (bone == null) continue;
                var go = new GameObject("Body_" + shape.Name) { layer = Layers.MaidBody };
                go.transform.SetParent(bone, false);
                go.transform.localPosition = shape.LocalCenter;
                go.transform.localRotation = shape.LocalRotation;
                float scale = Mathf.Max(1e-5f, go.transform.lossyScale.x);

                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = shape.Radius / scale;
                capsule.height = shape.Height / scale;

                // Moving colliders belong on kinematic bodies (cheap to move every frame).
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                colliders.Add(capsule);
            }
            return colliders;
        }

        // ------------------------------------------------------------------ fitting

        private static Shape[] Fit(Animator animator, Transform model)
        {
            var partOf = new Dictionary<Transform, Part>();
            void Map(HumanBodyBones bone, Part part)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null) partOf[t] = part;
            }

            Map(HumanBodyBones.Hips, Part.Hips);
            Map(HumanBodyBones.Spine, Part.Torso);
            Map(HumanBodyBones.Chest, Part.Torso);
            Map(HumanBodyBones.UpperChest, Part.Torso);
            Map(HumanBodyBones.Neck, Part.Torso);
            Map(HumanBodyBones.LeftShoulder, Part.Torso);
            Map(HumanBodyBones.RightShoulder, Part.Torso);
            Map(HumanBodyBones.Head, Part.Head);
            Map(HumanBodyBones.LeftUpperArm, Part.UpperArmL);
            Map(HumanBodyBones.LeftLowerArm, Part.LowerArmL);
            Map(HumanBodyBones.LeftHand, Part.LowerArmL);
            Map(HumanBodyBones.RightUpperArm, Part.UpperArmR);
            Map(HumanBodyBones.RightLowerArm, Part.LowerArmR);
            Map(HumanBodyBones.RightHand, Part.LowerArmR);
            Map(HumanBodyBones.LeftUpperLeg, Part.UpperLegL);
            Map(HumanBodyBones.LeftLowerLeg, Part.LowerLegL);
            Map(HumanBodyBones.LeftFoot, Part.FootL);
            Map(HumanBodyBones.LeftToes, Part.FootL);
            Map(HumanBodyBones.RightUpperLeg, Part.UpperLegR);
            Map(HumanBodyBones.RightLowerLeg, Part.LowerLegR);
            Map(HumanBodyBones.RightFoot, Part.FootR);
            Map(HumanBodyBones.RightToes, Part.FootR);

            // Gather bind-pose vertices per part (skirt, bust, finger and eye bones resolve to their humanoid parent).
            var points = new Dictionary<Part, List<Vector3>>();
            var resolved = new Dictionary<Transform, int>(); // -1 = no part
            var mesh = new Mesh();
            var vertices = new List<Vector3>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                BoneWeight[] weights = smr.sharedMesh.boneWeights;
                Transform[] bones = smr.bones;
                smr.BakeMesh(mesh, true);
                mesh.GetVertices(vertices);
                var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                int count = Mathf.Min(vertices.Count, weights.Length);
                for (int i = 0; i < count; i++)
                {
                    int boneIndex = Dominant(weights[i]);
                    if (boneIndex < 0 || boneIndex >= bones.Length || bones[boneIndex] == null) continue;
                    int part = Resolve(bones[boneIndex], partOf, model, resolved);
                    if (part < 0) continue;
                    if (!points.TryGetValue((Part)part, out var list)) points[(Part)part] = list = new List<Vector3>();
                    list.Add(toWorld.MultiplyPoint3x4(vertices[i]));
                }
            }
            Object.Destroy(mesh);

            Vector3 up = model.up;
            var shapes = new List<Shape>();
            Vector3 Pos(HumanBodyBones b) => animator.GetBoneTransform(b) != null ? animator.GetBoneTransform(b).position : model.position;
            bool Has(HumanBodyBones b) => animator.GetBoneTransform(b) != null;

            void Add(Part part, HumanBodyBones attach, Vector3 origin, Vector3 axis)
            {
                if (!points.TryGetValue(part, out var list) || list.Count < MinVertices || !Has(attach)) return;
                if (axis.sqrMagnitude < 1e-8f) axis = up;
                if (TryFit(list, origin, axis.normalized, out Vector3 center, out Quaternion rotation, out float radius, out float height))
                {
                    Transform bone = animator.GetBoneTransform(attach);
                    shapes.Add(new Shape
                    {
                        Name = part.ToString(),
                        Bone = attach,
                        LocalCenter = bone.InverseTransformPoint(center),
                        LocalRotation = Quaternion.Inverse(bone.rotation) * rotation,
                        Radius = radius,
                        Height = height
                    });
                }
            }

            void AddLimb(Part part, HumanBodyBones start, HumanBodyBones end)
            {
                if (Has(start) && Has(end)) Add(part, start, Pos(start), Pos(end) - Pos(start));
            }

            void AddFoot(Part part, HumanBodyBones foot, HumanBodyBones toes)
            {
                if (!Has(foot)) return;
                // Shoes lie along the ground, pointing forward.
                Vector3 along = Has(toes) ? Pos(toes) - Pos(foot) : model.forward;
                along = Vector3.ProjectOnPlane(along, up);
                Add(part, foot, Pos(foot), along.sqrMagnitude > 1e-6f ? along : model.forward);
            }

            Add(Part.Hips, HumanBodyBones.Hips, Pos(HumanBodyBones.Hips), up);
            HumanBodyBones torsoBone = Has(HumanBodyBones.Chest) ? HumanBodyBones.Chest : HumanBodyBones.Spine;
            Vector3 neck = Has(HumanBodyBones.Neck) ? Pos(HumanBodyBones.Neck) : Pos(HumanBodyBones.Head);
            Add(Part.Torso, torsoBone, Pos(HumanBodyBones.Spine), neck - Pos(HumanBodyBones.Spine));
            Add(Part.Head, HumanBodyBones.Head, Pos(HumanBodyBones.Head), up);
            AddLimb(Part.UpperArmL, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
            AddLimb(Part.LowerArmL, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AddLimb(Part.UpperArmR, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
            AddLimb(Part.LowerArmR, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            AddLimb(Part.UpperLegL, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
            AddLimb(Part.LowerLegL, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            AddLimb(Part.UpperLegR, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
            AddLimb(Part.LowerLegR, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            AddFoot(Part.FootL, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            AddFoot(Part.FootR, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            return shapes.ToArray();
        }

        private static bool TryFit(List<Vector3> points, Vector3 origin, Vector3 axis,
            out Vector3 center, out Quaternion rotation, out float radius, out float height)
        {
            // Centre the axis on the vertices (skirts and busts are not centred on their bone).
            Vector3 offset = Vector3.zero;
            foreach (var p in points)
            {
                Vector3 v = p - origin;
                offset += v - Vector3.Dot(v, axis) * axis;
            }
            origin += offset / points.Count;

            var along = new float[points.Count];
            var across = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 v = points[i] - origin;
                float t = Vector3.Dot(v, axis);
                along[i] = t;
                across[i] = (v - t * axis).magnitude;
            }
            Array.Sort(along);
            Array.Sort(across);
            float tMin = along[Mathf.FloorToInt((points.Count - 1) * EndPercentile)];
            float tMax = along[Mathf.CeilToInt((points.Count - 1) * (1f - EndPercentile))];
            radius = Mathf.Clamp(across[Mathf.FloorToInt((points.Count - 1) * RadiusPercentile)], 0.02f, 0.6f);
            height = Mathf.Max(radius * 2f, tMax - tMin);
            center = origin + axis * ((tMin + tMax) * 0.5f);
            rotation = Quaternion.FromToRotation(Vector3.up, axis);
            return tMax > tMin;
        }

        /// <summary>Index of the bone with the largest skinning weight, or -1 if the vertex is unweighted.</summary>
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

        /// <summary>Walks up from a skinning bone to the first humanoid bone with a part (cached).</summary>
        private static int Resolve(Transform bone, Dictionary<Transform, Part> partOf, Transform model, Dictionary<Transform, int> cache)
        {
            if (cache.TryGetValue(bone, out int known)) return known;
            int result = -1;
            for (Transform t = bone; t != null && t != model; t = t.parent)
            {
                if (partOf.TryGetValue(t, out Part part))
                {
                    result = (int)part;
                    break;
                }
            }
            cache[bone] = result;
            return result;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
        }
    }
}
