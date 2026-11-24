using UnityEngine;

namespace MaidCafe.AI
{
    /// <summary>
    /// Closes the right hand around the hand bell's handle (see <see cref="MaidVisuals.AttachBell"/>): the fingers curl
    /// into a fist every frame on top of the animation, and the rest pose keeps that hand apart from the left one.
    /// Runs after <see cref="MaidRestPose"/> and <see cref="MaidSkirtGuard"/> (finger rotations are local, so the
    /// order with the arm IK does not matter, but the rest pose's relaxed curl must not be added on top).
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class MaidBellGrip : MonoBehaviour
    {
        private const float FingerCurl = 82f; // degrees per joint (scaled per joint by MaidArmIK)
        private const float ThumbCurl = 38f;

        private MaidArmIK.Arm _hand;
        private Renderer _body;

        public void Init(MaidArmIK.Arm rightArm, Renderer body)
        {
            _hand = rightArm;
            _body = body;
        }

        private void LateUpdate()
        {
            if (_hand == null || (_body != null && !_body.isVisible)) return;
            MaidArmIK.CurlFingers(_hand, FingerCurl, ThumbCurl, 1f);
        }
    }
}
