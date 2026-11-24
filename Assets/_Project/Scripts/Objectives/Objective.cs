using System;
using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Localization;
using UnityEngine;

namespace MaidCafe.Objectives
{
    public enum ObjectiveType
    {
        RestorePower,
        FindKeycard,
        ServeCustomer,
        SilenceMusicBoxes
    }

    /// <summary>
    /// One floor's main objective, made of sequential steps. Subclasses spawn their own items and
    /// stations into the floor (before the NavMesh bake) and advance steps from interactions.
    /// Completing the last step unlocks the exit elevator.
    /// </summary>
    public abstract class Objective
    {
        protected FloorInstance Floor;
        protected Rng Rng;

        public abstract ObjectiveType Type { get; }
        public abstract string TitleKey { get; }
        public abstract int StepCount { get; }

        public int Step { get; private set; }
        public bool IsComplete => Step >= StepCount;

        /// <summary>Positions of items/stations that must be reachable (fed to floor validation).</summary>
        public readonly List<Vector3> Targets = new List<Vector3>();

        /// <summary>Any visible change (counter or step).</summary>
        public event Action<Objective> Progressed;

        /// <summary>A step finished (used for Easy-mode checkpoints).</summary>
        public event Action<Objective> StepCompleted;

        public event Action<Objective> Completed;

        protected bool IsDeepFloor => Floor.Layout.FloorIndex >= Floor.Layout.FloorCount / 2;

        public void Setup(FloorInstance floor, Rng rng)
        {
            Floor = floor;
            Rng = rng;
            Spawn();
        }

        protected abstract void Spawn();

        /// <summary>Localized text for the current step (includes counters).</summary>
        public abstract string StepText();

        public string Title => Loc.Get(TitleKey);

        public virtual void OnItemPicked(string itemId) { }

        protected void NotifyProgress() => Progressed?.Invoke(this);

        protected void AdvanceStep()
        {
            if (IsComplete) return;
            Step++;
            StepCompleted?.Invoke(this);
            if (IsComplete)
            {
                OnCompleted();
                Completed?.Invoke(this);
            }
            Progressed?.Invoke(this);
        }

        protected virtual void OnCompleted()
        {
            Floor.ExitCar.Unlock();
            GameEvents.ShowHint(Floor.Layout.IsFinalFloor ? "hint.exitOpen" : "hint.elevatorOpen");
        }

        public static Objective Create(ObjectiveType type)
        {
            switch (type)
            {
                case ObjectiveType.RestorePower: return new RestorePowerObjective();
                case ObjectiveType.FindKeycard: return new FindKeycardObjective();
                case ObjectiveType.ServeCustomer: return new ServeCustomerObjective();
                default: return new MusicBoxObjective();
            }
        }

        /// <summary>
        /// Objective for a floor: the four types are shuffled per run and cycled, so two consecutive
        /// floors never repeat the same objective.
        /// </summary>
        public static ObjectiveType TypeForFloor(int runSeed, int floorIndex)
        {
            var types = new List<ObjectiveType>
            {
                ObjectiveType.RestorePower, ObjectiveType.FindKeycard, ObjectiveType.ServeCustomer, ObjectiveType.SilenceMusicBoxes
            };
            new Rng(Rng.Combine(runSeed, 4242)).Shuffle(types);
            return types[floorIndex % types.Count];
        }
    }
}
