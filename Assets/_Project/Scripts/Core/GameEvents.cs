using System;
using UnityEngine;

namespace MaidCafe.Core
{
    /// <summary>
    /// Small global event bus for cross-system notifications (HUD hints, items, objectives).
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Show a short localized hint on the HUD (localization key).</summary>
        public static event Action<string> Hint;

        /// <summary>An item was picked up (item id).</summary>
        public static event Action<string> ItemPickedUp;

        /// <summary>An enemy caught the player (attacker transform).</summary>
        public static event Action<Transform> PlayerCaught;

        /// <summary>Number of enemies currently chasing changed from 0 to 1+ (true) or back to 0 (false).</summary>
        public static event Action<bool> ChaseChanged;

        public static void ShowHint(string key) => Hint?.Invoke(key);

        public static void RaiseItemPickedUp(string itemId) => ItemPickedUp?.Invoke(itemId);

        public static void RaisePlayerCaught(Transform attacker) => PlayerCaught?.Invoke(attacker);

        public static void RaiseChaseChanged(bool chasing) => ChaseChanged?.Invoke(chasing);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Hint = null;
            ItemPickedUp = null;
            PlayerCaught = null;
            ChaseChanged = null;
        }
    }

    /// <summary>Physics layer helpers. Layer 2 ("Ignore Raycast") holds triggers and NavMesh-excluded geometry.</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int NoNavMesh = 2;

        /// <summary>The maids' body colliders (see AI/MaidBodyColliders). Must match "MaidBody" in the TagManager.</summary>
        public const int MaidBody = 8;

        /// <summary>
        /// Geometry that blocks enemy sight and the line-of-sight hearing fallback: everything solid except maid bodies.
        /// The no-navmesh layer is included (its only solid colliders are the elevator cars' walls and doors); its
        /// triggers are skipped by the queries' <c>QueryTriggerInteraction.Ignore</c>.
        /// </summary>
        public static int SightBlockers => ~(1 << MaidBody);

        /// <summary>Everything solid except maid bodies (a maid's own body must not block her reach).</summary>
        public static int SolidExceptMaids => ~(1 << MaidBody);
    }
}
