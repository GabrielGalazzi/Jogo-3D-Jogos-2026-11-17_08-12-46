using System.Collections;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Player
{
    /// <summary>
    /// The short "caught" moment: input locks, the view snaps to the maid's face and the camera shakes.
    /// What happens afterwards (checkpoint / floor restart / run restart) is decided by the game flow.
    /// </summary>
    public static class CaughtSequence
    {
        public static IEnumerator Play(PlayerContext player, Vector3 attackerHead)
        {
            GameInput.SetGameplayEnabled(false);
            if (player.IsHiding) player.CurrentHidingSpot.ForceEject();
            player.MovementLocked = true;
            player.Phone.SetNightVision(false);

            float startYaw = player.Look.Yaw;
            float startPitch = 0f;
            Vector3 dir = attackerHead - player.EyePosition;
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float targetPitch = -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                player.Look.SetYaw(Mathf.LerpAngle(startYaw, targetYaw, k));
                player.Look.SetPitch(Mathf.Lerp(startPitch, targetPitch, k));
                yield return null;
            }

            Transform cam = player.Camera.transform;
            Vector3 basePos = cam.localPosition;
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                player.Look.SetYaw(targetYaw);
                player.Look.SetPitch(targetPitch);
                cam.localPosition = basePos + Random.insideUnitSphere * Mathf.Lerp(0.05f, 0.01f, t / 0.9f);
                yield return null;
            }
            cam.localPosition = basePos;
        }
    }
}
