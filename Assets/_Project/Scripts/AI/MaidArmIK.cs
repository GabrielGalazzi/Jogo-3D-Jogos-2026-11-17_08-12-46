using System.Collections.Generic;
using UnityEngine;

namespace MaidCafe.AI
{
    /// <summary>
    /// Arm helpers shared by the maid posture components (<see cref="MaidRestPose"/>, <see cref="MaidSkirtGuard"/>,
    /// <see cref="MaidBellGrip"/>): bone binding, a two-bone IK solve, hand orientation and finger curling.
    /// All of it works on the bones after the Animator has written them (call from LateUpdate).
    /// </summary>
    public static class MaidArmIK
    {
        /// <summary>One finger joint and the local axis that curls it toward the palm.</summary>
        public struct FingerJoint
        {
            public Transform Bone;
            public Vector3 Axis;  // in the bone's local space
            public float Share;   // fraction of the requested curl angle
            public bool Thumb;
        }

        public class Arm
        {
            public float Side; // -1 left, +1 right
            public float Reach;
            public Transform Shoulder, Upper, Lower, Hand, Index, Middle, Little;
            public readonly List<FingerJoint> Fingers = new List<FingerJoint>();

            /// <summary>Hand length (wrist to fingertips), world metres in the bind pose.</summary>
            public float HandLength;
        }

        private static readonly HumanBodyBones[] LeftFingers =
        {
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal
        };

        private static readonly HumanBodyBones[] RightFingers =
        {
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
        };

        private static readonly HumanBodyBones[] LeftThumb =
            { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal };

        private static readonly HumanBodyBones[] RightThumb =
            { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal };

        // Proximal, intermediate and distal joints do not bend equally in a relaxed or closed hand.
        private static readonly float[] JointShare = { 0.9f, 1.1f, 0.75f };

        /// <summary>
        /// Binds one arm. Call while the model is in its bind pose (the palm direction and the finger curl axes are
        /// measured from it). <paramref name="side"/>: -1 left, +1 right.
        /// </summary>
        public static Arm Bind(Animator a, Transform model, float side)
        {
            bool left = side < 0f;
            var arm = new Arm
            {
                Side = side,
                Shoulder = a.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder),
                Upper = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
                Lower = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
                Hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
                Index = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal),
                Middle = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal),
                Little = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal)
            };
            if (arm.Upper == null || arm.Lower == null || arm.Hand == null) return null;
            arm.Reach = (arm.Lower.position - arm.Upper.position).magnitude + (arm.Hand.position - arm.Lower.position).magnitude;
            arm.HandLength = arm.Middle != null ? (arm.Middle.position - arm.Hand.position).magnitude * 2.1f : 0.17f;

            Vector3 palm = PalmNormal(arm, model);
            if (palm.sqrMagnitude > 0f)
            {
                AddFingers(a, arm, left ? LeftFingers : RightFingers, palm, false);
                AddFingers(a, arm, left ? LeftThumb : RightThumb, palm, true);
            }
            return arm;
        }

        /// <summary>World direction the palm faces (bind pose: the side of the hand facing down in a T-pose).</summary>
        public static Vector3 PalmNormal(Arm arm, Transform model)
        {
            if (arm.Middle == null || arm.Index == null || arm.Little == null) return Vector3.zero;
            Vector3 fingers = (arm.Middle.position - arm.Hand.position).normalized;
            Vector3 across = arm.Index.position - arm.Little.position;
            Vector3 normal = Vector3.Cross(fingers, across).normalized;
            return Vector3.Dot(normal, -model.up) >= 0f ? normal : -normal;
        }

        private static void AddFingers(Animator a, Arm arm, HumanBodyBones[] bones, Vector3 palm, bool thumb)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = a.GetBoneTransform(bones[i]);
                if (bone == null) continue;
                Transform next = i % 3 < 2 && i + 1 < bones.Length ? a.GetBoneTransform(bones[i + 1]) : null;
                Vector3 dir = next != null ? next.position - bone.position : bone.position - bone.parent.position;
                if (dir.sqrMagnitude < 1e-10f) continue;
                // Rotating the bone about dir × palm moves its tip toward the palm.
                Vector3 axis = Vector3.Cross(dir.normalized, palm);
                if (axis.sqrMagnitude < 1e-6f) continue;
                arm.Fingers.Add(new FingerJoint
                {
                    Bone = bone,
                    Axis = bone.InverseTransformDirection(axis.normalized),
                    Share = JointShare[i % 3],
                    Thumb = thumb
                });
            }
        }

        /// <summary>
        /// Curls the fingers toward the palm by <paramref name="fingers"/> degrees (thumb: <paramref name="thumb"/>),
        /// on top of what the Animator wrote this frame. <paramref name="weight"/> blends it in.
        /// </summary>
        public static void CurlFingers(Arm arm, float fingers, float thumb, float weight)
        {
            if (weight <= 0.001f) return;
            foreach (var joint in arm.Fingers)
            {
                float angle = (joint.Thumb ? thumb : fingers) * joint.Share * weight;
                joint.Bone.localRotation *= Quaternion.AngleAxis(angle, joint.Axis);
            }
        }

        /// <summary>
        /// Rotates the upper and lower arm so the wrist reaches <paramref name="target"/>, bending the elbow toward
        /// <paramref name="hint"/>. The hand keeps the world rotation it had before the solve.
        /// </summary>
        public static void SolveTwoBone(Arm arm, Vector3 target, Vector3 hint)
        {
            Quaternion handRotation = arm.Hand.rotation;
            Vector3 a = arm.Upper.position, b = arm.Lower.position, c = arm.Hand.position;
            float ab = (b - a).magnitude, bc = (c - b).magnitude;
            Vector3 toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, 0.01f, (ab + bc) * 0.999f);
            Vector3 dir = toTarget.normalized;

            Vector3 pole = hint - a;
            Vector3 bend = pole - Vector3.Dot(pole, dir) * dir;
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(dir, Vector3.up);
            bend.Normalize();

            float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
            Vector3 elbow = a + dir * (cosA * ab) + bend * (Mathf.Sqrt(1f - cosA * cosA) * ab);
            arm.Upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * arm.Upper.rotation;

            b = arm.Lower.position;
            c = arm.Hand.position;
            arm.Lower.rotation = Quaternion.FromToRotation(c - b, a + dir * d - b) * arm.Lower.rotation;
            arm.Hand.rotation = handRotation;
        }

        /// <summary>
        /// Turns the hand so its fingers point along <paramref name="fingers"/> with the index side toward
        /// <paramref name="indexSide"/>. Half of the roll goes to the forearm so the wrist does not twist like a candy
        /// wrapper (these rigs have no twist bones).
        /// </summary>
        public static void OrientHand(Arm arm, Vector3 fingers, Vector3 indexSide, float weight)
        {
            if (arm.Middle == null || arm.Index == null || arm.Little == null || weight <= 0.001f) return;
            Vector3 currentFingers = arm.Middle.position - arm.Hand.position;
            Vector3 currentSide = arm.Index.position - arm.Little.position;
            if (currentFingers.sqrMagnitude < 1e-8f || currentSide.sqrMagnitude < 1e-8f) return;

            Quaternion delta = Quaternion.LookRotation(fingers, indexSide) * Quaternion.Inverse(Quaternion.LookRotation(currentFingers, currentSide));
            delta = Quaternion.Slerp(Quaternion.identity, delta, weight);
            Quaternion handRotation = delta * arm.Hand.rotation;

            Vector3 forearmAxis = (arm.Hand.position - arm.Lower.position).normalized;
            Quaternion twist = TwistAround(delta, forearmAxis);
            arm.Lower.rotation = Quaternion.Slerp(Quaternion.identity, twist, 0.5f) * arm.Lower.rotation;
            arm.Hand.rotation = handRotation;
        }

        /// <summary>Twist part of a swing-twist decomposition of <paramref name="q"/> around <paramref name="axis"/>.</summary>
        public static Quaternion TwistAround(Quaternion q, Vector3 axis)
        {
            Vector3 p = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
            var t = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(t.x / m, t.y / m, t.z / m, t.w / m);
        }
    }
}
