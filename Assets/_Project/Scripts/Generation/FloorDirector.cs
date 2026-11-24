using System;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Owns the currently built floor. Generates, builds and validates floors, regenerating with
    /// seed+1 when validation fails so the player never gets an unwinnable layout.
    /// </summary>
    public class FloorDirector : MonoBehaviour
    {
        private const int MaxAttempts = 6;

        public static FloorDirector Instance { get; private set; }

        public FloorInstance Current { get; private set; }

        public event Action<FloorInstance> FloorBuilt;

        /// <summary>Spawns gameplay content (objective) into a floor before its NavMesh bake. Returns required targets.</summary>
        public Func<FloorInstance, System.Collections.Generic.IEnumerable<Vector3>> Populate { get; set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public FloorInstance BuildFloor(int floorIndex, int floorCount, int seed)
        {
            ClearFloor();

            FloorInstance built = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int attemptSeed = seed + attempt;
                var layout = LayoutGenerator.Generate(attemptSeed, floorIndex, floorCount);

                // Layout rules first (cheap, no geometry): escape routes, no single-corridor bottleneck, outline.
                var topology = LayoutValidator.Check(layout);
                if (topology.Count > 0 && attempt < MaxAttempts - 1)
                {
                    Debug.LogWarning($"[FloorDirector] Floor {floorIndex + 1} seed {attemptSeed} layout rejected:\n - {string.Join("\n - ", topology)}");
                    continue;
                }

                var instance = FloorBuilder.Build(layout, transform, BatteryCount(floorIndex), Populate, AI.EnemyDirector.GiftCount(floorIndex),
                    BrokenLightChance(floorIndex), AI.EnemyDirector.WatcherCount(floorIndex));
                var errors = FloorBuilder.Validate(instance);
                if (errors.Count == 0)
                {
                    built = instance;
                    break;
                }

                Debug.LogWarning($"[FloorDirector] Floor {floorIndex + 1} seed {attemptSeed} failed validation:\n - {string.Join("\n - ", errors)}");
                if (attempt == MaxAttempts - 1)
                {
                    Debug.LogError("[FloorDirector] Using the last floor despite validation errors.");
                    built = instance;
                }
                else
                {
                    instance.Dispose();
                }
            }

            Current = built;
            FloorBuilt?.Invoke(Current);
            return Current;
        }

        public void ClearFloor()
        {
            Current?.Dispose();
            Current = null;
        }

        /// <summary>
        /// Share of dead ceiling lights: ~40% on the first floor rising to ~65% on the deepest, so the phone's night
        /// vision matters more the deeper the player goes (-0.1 on Easy, +0.1 on Hardcore).
        /// </summary>
        public static float BrokenLightChance(int floorIndex)
        {
            float chance = Mathf.Lerp(0.4f, 0.65f, Mathf.Clamp01(floorIndex / 9f));
            if (RunConfig.Difficulty == Difficulty.Easy) chance -= 0.1f;
            if (RunConfig.Difficulty == Difficulty.Hardcore) chance += 0.1f;
            return chance;
        }

        private static int BatteryCount(int floorIndex)
        {
            switch (RunConfig.Difficulty)
            {
                case Difficulty.Easy: return 4;
                case Difficulty.Hardcore: return 2;
                default: return 3;
            }
        }
    }
}
