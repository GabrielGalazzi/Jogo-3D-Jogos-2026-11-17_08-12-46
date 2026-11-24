using System;
using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>
    /// Creates each floor's objective (as the FloorDirector populate step), forwards item pickups,
    /// and records checkpoints when steps complete (used by the Easy difficulty on death).
    /// </summary>
    public class ObjectiveRunner : MonoBehaviour
    {
        public static ObjectiveRunner Instance { get; private set; }

        public Objective Current { get; private set; }

        public bool HasCheckpoint { get; private set; }
        public Vector3 CheckpointPosition { get; private set; }
        public float CheckpointYaw { get; private set; }

        /// <summary>Objective text or step changed (HUD refresh).</summary>
        public event Action<Objective> Changed;

        public event Action<Objective> StepCompleted;
        public event Action<Objective> ObjectiveCompleted;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.ItemPickedUp += OnItemPicked;
        }

        private void OnDisable()
        {
            GameEvents.ItemPickedUp -= OnItemPicked;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>FloorDirector populate callback: spawns this floor's objective before the NavMesh bake.</summary>
        public IEnumerable<Vector3> Populate(FloorInstance floor)
        {
            var type = Objective.TypeForFloor(RunConfig.RunSeed, floor.Layout.FloorIndex);
            var objective = Objective.Create(type);
            objective.Setup(floor, new Rng(Rng.Combine(floor.Layout.Seed, 31)));
            floor.Payload = objective;
            return objective.Targets;
        }

        /// <summary>Makes the floor's objective current; the arrival point is the first checkpoint.</summary>
        public void Activate(FloorInstance floor)
        {
            Detach();
            Current = floor.Payload as Objective;
            if (Current != null)
            {
                Current.Progressed += OnProgressed;
                Current.StepCompleted += OnStepCompleted;
                Current.Completed += OnCompleted;
            }
            // First checkpoint: in the room just outside the arrival elevator (never inside the car, whose doors
            // close once the player has left it).
            Vector3 front = FloorInstance.PortalFront(floor.Layout.Start, 1.4f);
            if (UnityEngine.AI.NavMesh.SamplePosition(front, out var hit, 1.5f, NavAreas.Ground)) front = hit.position;
            SetCheckpoint(front, floor.SpawnYaw);
            Changed?.Invoke(Current);
        }

        public void Detach()
        {
            if (Current == null) return;
            Current.Progressed -= OnProgressed;
            Current.StepCompleted -= OnStepCompleted;
            Current.Completed -= OnCompleted;
            Current = null;
        }

        private void SetCheckpoint(Vector3 position, float yaw)
        {
            HasCheckpoint = true;
            CheckpointPosition = position;
            CheckpointYaw = yaw;
        }

        private void OnItemPicked(string itemId) => Current?.OnItemPicked(itemId);

        private void OnProgressed(Objective o) => Changed?.Invoke(o);

        private void OnStepCompleted(Objective o)
        {
            var player = PlayerContext.Instance;
            if (player != null)
            {
                Vector3 pos = player.IsHiding ? player.CurrentHidingSpot.ExitPosition : player.LastSafePosition;
                // Snap to walkable floor so a checkpoint can never be inside a prop or an elevator car.
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, NavAreas.Ground)) pos = hit.position;
                SetCheckpoint(pos, player.Look.Yaw);
            }
            StepCompleted?.Invoke(o);
        }

        private void OnCompleted(Objective o) => ObjectiveCompleted?.Invoke(o);
    }
}
