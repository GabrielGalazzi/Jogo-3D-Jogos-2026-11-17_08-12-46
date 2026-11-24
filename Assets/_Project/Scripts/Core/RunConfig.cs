using UnityEngine;

namespace MaidCafe.Core
{
    public enum Difficulty
    {
        /// <summary>Caught → respawn at the last completed objective step.</summary>
        Easy,

        /// <summary>Caught → current floor restarts (same seed).</summary>
        Normal,

        /// <summary>Caught → the whole run restarts with a new seed.</summary>
        Hardcore
    }

    /// <summary>
    /// State of the current run. Static so it survives the MainMenu → Game scene load.
    /// </summary>
    public static class RunConfig
    {
        public const int MinFloors = 3;
        public const int MaxFloors = 10;

        public static Difficulty Difficulty { get; private set; } = Difficulty.Normal;
        public static int RunSeed { get; private set; }
        public static int FloorCount { get; private set; } = MinFloors;
        public static int CurrentFloor { get; set; }
        public static bool HasActiveRun { get; private set; }

        // Statistics shown on the victory screen.
        public static int Deaths { get; set; }
        public static float RunTime { get; set; }

        public static void NewRun(Difficulty difficulty, int? seed = null)
        {
            Difficulty = difficulty;
            RunSeed = seed ?? NewRandomSeed();
            FloorCount = FloorCountForSeed(RunSeed);
            CurrentFloor = 0;
            Deaths = 0;
            RunTime = 0f;
            HasActiveRun = true;
        }

        /// <summary>Hardcore restart: same difficulty, new seed (and possibly a new floor count).</summary>
        public static void RestartWithNewSeed()
        {
            int deaths = Deaths;
            float time = RunTime;
            NewRun(Difficulty);
            Deaths = deaths;
            RunTime = time;
        }

        public static void EndRun()
        {
            HasActiveRun = false;
        }

        public static int FloorCountForSeed(int seed)
        {
            return new Rng(Rng.Combine(seed, 9999)).Range(MinFloors, MaxFloors + 1);
        }

        public static int FloorSeed(int floorIndex) => Rng.Combine(RunSeed, floorIndex);

        public static bool IsFinalFloor => CurrentFloor >= FloorCount - 1;

        private static int NewRandomSeed()
        {
            unchecked
            {
                return (int)((uint)System.DateTime.Now.Ticks ^ (uint)Random.Range(int.MinValue, int.MaxValue)) & 0x7FFFFFFF;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            HasActiveRun = false;
            Difficulty = Difficulty.Normal;
        }
    }
}
