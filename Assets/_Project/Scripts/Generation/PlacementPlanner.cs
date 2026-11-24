using System.Collections.Generic;
using System.Linq;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Generation
{
    public enum PlacementKind
    {
        Locker,
        UnderTable,
        Battery
    }

    public struct Placement
    {
        public PlacementKind Kind;
        public int Room;
        public Vector2Int Cell;

        /// <summary>For wall placements: direction from cell centre to the wall. Zero for centre placements.</summary>
        public Vector2Int WallDir;

        public bool OnWall => WallDir != Vector2Int.zero;
    }

    /// <summary>Decides where hiding spots and batteries go. Pure and deterministic.</summary>
    public static class PlacementPlanner
    {
        public static List<Placement> PlanHidingSpots(FloorLayout layout, SlotPool slots, Rng rng)
        {
            var result = new List<Placement>();
            foreach (var room in layout.Rooms)
            {
                int wanted = room.IsHallway ? (room.Area >= 4 ? 1 : 0) : (room.Area >= 9 ? 2 : 1);
                for (int i = 0; i < wanted; i++)
                {
                    bool tableRoom = room.Type == RoomType.DiningHall || room.Type == RoomType.StaffRoom || room.Type == RoomType.Stage;
                    if (tableRoom && rng.Chance(0.5f) && slots.TryTakeCenter(room.Id, out var center))
                    {
                        result.Add(new Placement { Kind = PlacementKind.UnderTable, Room = room.Id, Cell = center });
                        continue;
                    }
                    if (slots.TryTakeWall(room.Id, out var wall))
                    {
                        result.Add(new Placement { Kind = PlacementKind.Locker, Room = room.Id, Cell = wall.Cell, WallDir = wall.Dir });
                    }
                }
            }
            return result;
        }

        public static List<Placement> PlanBatteries(FloorLayout layout, SlotPool slots, Rng rng, int count)
        {
            var result = new List<Placement>();
            var rooms = layout.Rooms.Where(r => r.Id != layout.Start.Room).Select(r => r.Id).ToList();
            rng.Shuffle(rooms);
            for (int i = 0; i < count && rooms.Count > 0; i++)
            {
                int room = rooms[i % rooms.Count];
                if (slots.TryTakeWall(room, out var wall))
                {
                    result.Add(new Placement { Kind = PlacementKind.Battery, Room = room, Cell = wall.Cell, WallDir = wall.Dir });
                }
            }
            return result;
        }
    }
}
