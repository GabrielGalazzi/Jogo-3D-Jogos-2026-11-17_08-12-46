using System.Collections.Generic;
using UnityEngine;

namespace MaidCafe.Generation
{
    public enum RoomType
    {
        DiningHall,
        Kitchen,
        Storage,
        ManagerOffice,
        StaffRoom,
        Restroom,
        Stage,
        Hallway
    }

    public class Room
    {
        public int Id;
        public RectInt Rect;
        public RoomType Type;
        public readonly List<int> Neighbors = new List<int>();

        public int Area => Rect.width * Rect.height;
        public bool IsHallway => Type == RoomType.Hallway;
    }

    /// <summary>A doorway between two orthogonally adjacent cells that belong to different rooms.</summary>
    public struct Door
    {
        public int RoomA;
        public int RoomB;
        public Vector2Int CellA;
        public Vector2Int CellB;
    }

    /// <summary>An opening in the outer wall that leads to an elevator (or the final exit).</summary>
    public struct Portal
    {
        public int Room;
        public Vector2Int Cell;

        /// <summary>Unit grid direction from the cell towards the outside of the building.</summary>
        public Vector2Int Outward;
    }

    /// <summary>
    /// Pure data description of one generated floor. Cells are 4x4 m on the XZ plane;
    /// cell (x, y) maps to world (x * CellSize, 0, y * CellSize). <see cref="Width"/> x <see cref="Height"/> is the
    /// footprint's bounding grid: the building's outline can be irregular, so cells outside it have room -1.
    /// </summary>
    public class FloorLayout
    {
        public const float CellSize = 4f;

        public int Seed;
        public int FloorIndex;
        public int FloorCount;
        public int Width;
        public int Height;

        /// <summary>Room id of every grid cell; -1 for cells outside the building (notches, courtyards).</summary>
        public int[,] CellRoom;

        /// <summary>Number of cells inside the building (the floor's area in cells).</summary>
        public int Area
        {
            get
            {
                int n = 0;
                if (CellRoom == null) return 0;
                foreach (int r in CellRoom) if (r >= 0) n++;
                return n;
            }
        }

        /// <summary>True if the cell is part of the building (in the grid and in a room).</summary>
        public bool InFootprint(Vector2Int c) => RoomAt(c) >= 0;
        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<Door> Doors = new List<Door>();

        /// <summary>Hop distance (through doors) from the start room to every room.</summary>
        public int[] RoomDistance;

        public Portal Start;
        public Portal Exit;

        public bool IsFinalFloor => FloorIndex >= FloorCount - 1;

        private readonly HashSet<long> _doorEdges = new HashSet<long>();

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

        public int RoomAt(Vector2Int c) => InBounds(c) ? CellRoom[c.x, c.y] : -1;

        public void RegisterDoor(Door door)
        {
            Doors.Add(door);
            _doorEdges.Add(EdgeKey(door.CellA, door.CellB));
        }

        public bool HasDoor(Vector2Int a, Vector2Int b) => _doorEdges.Contains(EdgeKey(a, b));

        public bool IsPortal(Vector2Int cell, Vector2Int dir)
        {
            return (Start.Cell == cell && Start.Outward == dir) || (Exit.Cell == cell && Exit.Outward == dir);
        }

        public static Vector3 CellCenter(Vector2Int c)
        {
            return new Vector3((c.x + 0.5f) * CellSize, 0f, (c.y + 0.5f) * CellSize);
        }

        public Room RoomOf(Vector2Int c) => Rooms[RoomAt(c)];

        private long EdgeKey(Vector2Int a, Vector2Int b)
        {
            int ia = a.y * Width + a.x;
            int ib = b.y * Width + b.x;
            if (ia > ib) (ia, ib) = (ib, ia);
            return ((long)ia << 32) | (uint)ib;
        }

        public static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };
    }
}
