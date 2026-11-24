using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Interaction;
using Unity.AI.Navigation;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>A built floor in the scene: its layout plus references to the live objects.</summary>
    public class FloorInstance
    {
        public FloorLayout Layout;
        public GameObject Root;
        public Transform StaticRoot;
        public Transform DynamicRoot;
        public NavMeshSurface Surface;
        public SlotPool Slots;
        public Rng Rng;

        public ElevatorCar StartCar;
        public ElevatorCar ExitCar;

        public readonly List<HidingSpot> HidingSpots = new List<HidingSpot>();
        public readonly List<Pickup> Batteries = new List<Pickup>();
        public readonly List<Pickup> Gifts = new List<Pickup>();
        public readonly List<Light> RoomLights = new List<Light>();

        /// <summary>
        /// Wall slots reserved for the bell (watcher) maids' posts, taken before the furniture so the decorator never
        /// fills the cells they need. <see cref="AI.EnemyDirector"/> spawns the watchers there.
        /// </summary>
        public readonly List<WallSlot> WatcherPosts = new List<WallSlot>();

        /// <summary>Extra positions that must be reachable (objective items and stations).</summary>
        public readonly List<Vector3> RequiredTargets = new List<Vector3>();

        /// <summary>Gameplay payload attached by the populate callback (the floor's objective).</summary>
        public object Payload;

        /// <summary>NavMesh-sampled points (one per cell) for enemy patrols.</summary>
        public readonly List<Vector3> PatrolPoints = new List<Vector3>();

        /// <summary>Room id of every patrol point (same index as <see cref="PatrolPoints"/>).</summary>
        public readonly List<int> PatrolPointRooms = new List<int>();

        public Vector3 SpawnPosition => StartCar.SpawnPoint.position;
        public float SpawnYaw => StartCar.SpawnPoint.eulerAngles.y;

        public Vector3 RoomCenter(int roomId)
        {
            var rect = Layout.Rooms[roomId].Rect;
            return new Vector3(rect.center.x * FloorLayout.CellSize, 0f, rect.center.y * FloorLayout.CellSize);
        }

        /// <summary>Point on the floor just inside the building in front of a portal (elevator).</summary>
        public static Vector3 PortalFront(Portal portal, float inset = 1.2f)
        {
            var outward = new Vector3(portal.Outward.x, 0f, portal.Outward.y);
            return FloorLayout.CellCenter(portal.Cell) + outward * (FloorLayout.CellSize * 0.5f - inset);
        }

        public int RoomAtWorld(Vector3 world)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(world.x / FloorLayout.CellSize), Mathf.FloorToInt(world.z / FloorLayout.CellSize));
            return Layout.RoomAt(cell);
        }

        public void Dispose()
        {
            if (Surface != null) Surface.RemoveData();
            if (Root != null)
            {
                Root.SetActive(false);
                Object.Destroy(Root);
            }
        }
    }
}
