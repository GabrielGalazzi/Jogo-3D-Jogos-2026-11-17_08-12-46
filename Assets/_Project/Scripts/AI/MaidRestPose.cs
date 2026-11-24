using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaidCafe.AI
{
    /// <summary>
    /// Arm posture for the imported maids, applied on top of the Animator:
    /// - Rest (standing, walking, searching): hands folded in front at the waist, right hand over the left, palms
    ///   toward the body, elbows relaxed at the sides (the classic waiting pose of a maid). The torso is kept upright.
    /// - Ringing: the right arm is raised and shakes the bell; the left arm keeps the rest pose.
    /// Blends out for running, catching and curtsying, where the clip's arms are used.
    /// The clips put the hands inside the bulkier bodies and skirts, so the hand positions come from each model's own
    /// mesh: a front-depth profile (clothes and apron included, hair excluded) is sampled once per model in the bind
    /// pose and the hands are laid on it with a small clearance. <see cref="MaidSkirtGuard"/> runs afterwards and keeps
    /// the hands and forearms out of the skirt in every animation.
    /// </summary>
    public class MaidRestPose : MonoBehaviour
    {
        private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
        private static readonly int SearchHash = Animator.StringToHash("Search");
        private static readonly int RingHash = Animator.StringToHash("Ring");
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        // Posture (degrees). Lean is measured hips bone → neck bone, positive = forward.
        private const float TargetTorsoLean = 0f;
        private const float MaxTorsoCorrection = 12f;
        private const float HeadCounterRotation = 0.7f; // share of the torso correction the neck gives back

        // Folded hands, for a reference maid whose hips bone is 0.91 m high; scaled by the actual hips height.
        private const float ReferenceHipsHeight = 0.913f;
        private const float WristHeight = 0.075f;    // above the hips bone (just under the waistband)
        private const float MaxWristRaise = 0.1f;    // raised when the arms cannot reach lower
        private const float WristSpread = 0.055f;    // each wrist sits this far to its side of the centre line
        private const float Clearance = 0.03f;       // wrist centre to the front surface
        private const float HandStack = 0.022f;      // the right hand lies on top of (in front of) the left
        private const float FingerDrop = 0.5f;       // how much the fingers point down (direction weight)
        private const float RestCurl = 18f;          // relaxed finger curl (degrees per joint)
        private const float SampleHalfWidth = 0.1f;  // lateral band sampled for the front profile
        private const float ProfileBottom = -0.25f, ProfileTop = 0.3f, ProfileStep = 0.01f;

        // Ringing (right arm): wrist relative to the right shoulder joint, reference scale.
        private const float RingUp = 0.2f, RingForward = 0.24f, RingOut = 0.24f;
        private const float RingShakeHz = 6.5f, RingShakeDegrees = 28f;

        // Locomotion blend-tree speeds (see MaidAssetPipeline): full pose up to a brisk walk, gone by the run.
        private const float FadeStartSpeed = 2.4f, FadeEndSpeed = 3.2f;
        private const float BlendRate = 5f;

        private static readonly Dictionary<string, float[]> Profiles = new Dictionary<string, float[]>();

        private Animator _animator;
        private Transform _model;
        private Transform _hips, _spine, _chest, _upperChest, _neck;
        private Renderer _body;
        private MaidArmIK.Arm _left, _right;
        private float[] _profile;
        private float _k;
        private float _restLeft, _restRight, _ring, _torso;

        /// <summary>Set by <see cref="MaidBellGrip"/>: the right hand holds a bell, so it is not folded into the left.</summary>
        public bool HoldsBell { get; set; }

        public MaidArmIK.Arm Left => _left;
        public MaidArmIK.Arm Right => _right;

        /// <summary>
        /// Binds the rig. Call right after instantiation, while the model is still in its bind pose.
        /// <paramref name="profileKey"/> identifies the model + scale so the profile is measured once.
        /// </summary>
        public bool Init(Animator animator, Transform model, string profileKey)
        {
            _animator = animator;
            _model = model;
            if (animator == null || !animator.isHuman) return false;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            _left = MaidArmIK.Bind(animator, model, -1f);
            _right = MaidArmIK.Bind(animator, model, 1f);
            if (_hips == null || _left == null || _right == null) return false;

            _k = Mathf.Max(0.1f, Vector3.Dot(_hips.position - model.position, model.up) / ReferenceHipsHeight);
            if (!Profiles.TryGetValue(profileKey, out _profile))
            {
                _profile = MeasureFrontProfile(model, _hips.position, _k);
                Profiles[profileKey] = _profile;
            }
            _body = model.GetComponentInChildren<SkinnedMeshRenderer>();
            return true;
        }

        /// <summary>
        /// Furthest point of the front (body and clothes, hair excluded) ahead of the hips bone, per height slice,
        /// within a narrow band around the centre line.
        /// </summary>
        private static float[] MeasureFrontProfile(Transform model, Vector3 hips, float k)
        {
            int bins = Mathf.RoundToInt((ProfileTop - ProfileBottom) / ProfileStep) + 1;
            var profile = new float[bins];
            for (int i = 0; i < bins; i++) profile[i] = float.NegativeInfinity;

            Vector3 f = model.forward, r = model.right, u = model.up;
            float halfWidth = SampleHalfWidth * k;
            var mesh = new Mesh();
            var vertices = new List<Vector3>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                smr.BakeMesh(mesh, true);
                mesh.GetVertices(vertices);
                var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in vertices)
                {
                    Vector3 d = toWorld.MultiplyPoint3x4(v) - hips;
                    if (Mathf.Abs(Vector3.Dot(d, r)) > halfWidth) continue;
                    int bin = Mathf.RoundToInt((Vector3.Dot(d, u) / k - ProfileBottom) / ProfileStep);
                    if (bin < 0 || bin >= bins) continue;
                    profile[bin] = Mathf.Max(profile[bin], Vector3.Dot(d, f) / k);
                }
            }
            Destroy(mesh);

            // Fill empty slices from their neighbours (thin meshes can leave gaps).
            for (int pass = 0; pass < bins; pass++)
            {
                bool changed = false;
                for (int i = 0; i < bins; i++)
                {
                    if (!float.IsNegativeInfinity(profile[i])) continue;
                    float n = Mathf.Max(i > 0 ? profile[i - 1] : float.NegativeInfinity, i < bins - 1 ? profile[i + 1] : float.NegativeInfinity);
                    if (float.IsNegativeInfinity(n)) continue;
                    profile[i] = n;
                    changed = true;
                }
                if (!changed) break;
            }
            for (int i = 0; i < bins; i++) if (float.IsNegativeInfinity(profile[i])) profile[i] = 0.1f;
            return profile;
        }

        /// <summary>Front depth (reference-scale metres ahead of the hips bone) at a height relative to the hips bone.</summary>
        private float FrontDepth(float height)
        {
            float t = (height - ProfileBottom) / ProfileStep;
            int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, _profile.Length - 1);
            int j = Mathf.Min(i + 1, _profile.Length - 1);
            // The hand spans a few centimetres vertically, so take the larger neighbour rather than interpolating.
            return Mathf.Max(_profile[i], _profile[j]);
        }

        private void LateUpdate()
        {
            if (_animator == null || _profile == null) return;

            var state = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0) : _animator.GetCurrentAnimatorStateInfo(0);
            float rest = 0f, ring = 0f;
            if (state.shortNameHash == LocomotionHash)
            {
                rest = 1f - Mathf.InverseLerp(FadeStartSpeed, FadeEndSpeed, _animator.GetFloat(SpeedId));
            }
            else if (state.shortNameHash == SearchHash)
            {
                rest = 1f;
            }
            else if (state.shortNameHash == RingHash)
            {
                ring = 1f;
            }

            float step = BlendRate * Time.deltaTime;
            _restLeft = Mathf.MoveTowards(_restLeft, Mathf.Max(rest, ring), step);
            _restRight = Mathf.MoveTowards(_restRight, rest, step);
            _ring = Mathf.MoveTowards(_ring, ring, step);
            _torso = Mathf.MoveTowards(_torso, Mathf.Max(rest, ring), step);
            if (_body != null && !_body.isVisible) return;

            if (_torso > 0.001f) Straighten(_torso);
            if (_restLeft > 0.001f) Fold(_left, _restLeft);
            if (_restRight > 0.001f) Fold(_right, _restRight);
            if (_ring > 0.001f) Ring(_right, _ring);
        }

        // ------------------------------------------------------------------ posture

        private float TorsoLean()
        {
            Vector3 v = _neck.position - _hips.position;
            return Mathf.Atan2(Vector3.Dot(v, _model.forward), Vector3.Dot(v, _model.up)) * Mathf.Rad2Deg;
        }

        private static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// <summary>Brings the torso upright (small corrections only), bending through the spine and chest.</summary>
        private void Straighten(float weight)
        {
            if (_neck == null || _spine == null) return;
            Vector3 r = _model.right;
            float current = TorsoLean();
            float delta = Mathf.Clamp(TargetTorsoLean - current, -MaxTorsoCorrection, MaxTorsoCorrection) * weight;
            float chestShare = _upperChest != null ? 0.35f : 0.6f;
            Rotate(_spine, r, delta * 0.4f);
            Rotate(_chest, r, delta * chestShare);
            Rotate(_upperChest, r, delta * 0.25f);

            // Pivots differ, so the shares do not add up exactly: finish at the spine.
            float residual = current + delta - TorsoLean();
            Rotate(_spine, r, residual);

            // Rotations about one axis add up for the head's orientation; give most of it back so she still looks ahead.
            float applied = delta * (0.4f + chestShare + (_upperChest != null ? 0.25f : 0f)) + residual;
            Rotate(_neck, r, -applied * HeadCounterRotation);
        }

        // ------------------------------------------------------------------ folded hands

        private Vector3 WristTarget(MaidArmIK.Arm arm, float height, bool alone, out float depth)
        {
            depth = FrontDepth(height);
            float stack = arm.Side > 0f ? HandStack : 0f;
            return _hips.position
                   + _model.up * (height * _k)
                   + _model.right * (arm.Side * WristSpread * (alone ? 1.4f : 1f) * _k)
                   + _model.forward * ((depth + Clearance + stack) * _k);
        }

        /// <summary>Hand resting on the front of the waist, fingers across toward the other side and drooping.</summary>
        private void Fold(MaidArmIK.Arm arm, float weight)
        {
            Vector3 f = _model.forward, r = _model.right, u = _model.up;
            float s = arm.Side;
            // The right hand holding a bell (or a left hand whose partner is ringing) rests alone, a little wider.
            bool alone = (s > 0f && HoldsBell) || (s < 0f && _ring > 0.5f);

            float height = WristHeight;
            Vector3 wrist = WristTarget(arm, height, alone, out float depth);
            while ((wrist - arm.Upper.position).magnitude > arm.Reach * 0.97f && height < WristHeight + MaxWristRaise)
            {
                height += 0.01f;
                wrist = WristTarget(arm, height, alone, out depth);
            }

            // Fingers follow the slope of the front: the apron and skirt bulge forward below the waist.
            float drop = arm.HandLength * 0.5f * FingerDrop / _k;
            float slope = Mathf.Clamp((FrontDepth(height - drop) - depth) * _k / (arm.HandLength * 0.5f), 0f, 0.6f);
            float across = Mathf.Sqrt(Mathf.Max(0.05f, 1f - FingerDrop * FingerDrop - slope * slope));
            Vector3 fingers = (-s * across * r - FingerDrop * u + slope * f).normalized;

            // Elbows relaxed at the sides, slightly back. Blended from the animated elbow so fading never pops.
            Vector3 elbowHint = arm.Upper.position + (s * 0.2f * r - 0.12f * f - 0.32f * u) * _k;

            MaidArmIK.SolveTwoBone(arm, Vector3.Lerp(arm.Hand.position, wrist, weight), Vector3.Lerp(arm.Lower.position, elbowHint, weight));
            // Palm toward the body with the thumb side up.
            MaidArmIK.OrientHand(arm, fingers, u, weight);
            if (!(s > 0f && HoldsBell)) MaidArmIK.CurlFingers(arm, RestCurl, RestCurl * 0.5f, weight);
        }

        // ------------------------------------------------------------------ ringing

        /// <summary>Right arm raised in front, the fist shaking the bell; the left arm stays folded.</summary>
        private void Ring(MaidArmIK.Arm arm, float weight)
        {
            Vector3 f = _model.forward, r = _model.right, u = _model.up;
            float s = arm.Side;
            Vector3 wrist = arm.Upper.position + (s * RingOut * r + RingForward * f + RingUp * u) * _k;
            Vector3 elbowHint = arm.Upper.position + (s * 0.4f * r - 0.05f * f - 0.15f * u) * _k;
            MaidArmIK.SolveTwoBone(arm, Vector3.Lerp(arm.Hand.position, wrist, weight), Vector3.Lerp(arm.Lower.position, elbowHint, weight));

            // Fist with the knuckles forward and the thumb up, so the bell hangs mouth-down below it; the wrist
            // flicks back and forth to ring it.
            float shake = Mathf.Sin(Time.time * RingShakeHz * 2f * Mathf.PI) * RingShakeDegrees;
            Vector3 fingers = Quaternion.AngleAxis(shake, s * r) * (0.95f * f - 0.3f * s * r).normalized;
            Vector3 thumbSide = Quaternion.AngleAxis(shake, s * r) * u;
            MaidArmIK.OrientHand(arm, fingers, thumbSide, weight);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Profiles.Clear();
        }
    }
}
