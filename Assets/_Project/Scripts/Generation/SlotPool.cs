using System.Collections.Generic;
using System.Linq;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>A free wall segment inside a cell, facing into the room. Dir points from the cell centre towards the wall.</summary>
    public struct WallSlot
    {
        public int Room;
        public Vector2Int Cell;
        public Vector2Int Dir;
    }

    /// <summary>
    /// Tracks where props and items may still be placed on a floor so nothing overlaps,
    /// blocks a doorway, or blocks a portal (elevator). Pure data and deterministic for a given Rng.
    /// </summary>
    public class SlotPool
    {
        private readonly Dictionary<int, List<WallSlot>> _wall = new Dictionary<int, List<WallSlot>>();
        private readonly Dictionary<int, List<Vector2Int>> _center = new Dictionary<int, List<Vector2Int>>();
        private readonly HashSet<Vector2Int> _usedCenters = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _usedWallCells = new HashSet<Vector2Int>();

        public SlotPool(FloorLayout layout, Rng rng)
        {
            foreach (var room in layout.Rooms)
            {
                _wall[room.Id] = new List<WallSlot>();
                _center[room.Id] = new List<Vector2Int>();
            }

            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    int roomId = layout.CellRoom[x, y];
                    if (roomId < 0) continue; // outside the building's outline
                    bool isPortalCell = c == layout.Start.Cell || c == layout.Exit.Cell;
                    bool hasDoor = false;

                    foreach (var dir in FloorLayout.Directions)
                    {
                        var n = c + dir;
                        bool isWall = layout.RoomAt(n) != roomId;
                        if (!isWall) continue;
                        if (layout.InFootprint(n) && layout.HasDoor(c, n))
                        {
                            hasDoor = true;
                            continue;
                        }
                        if (isPortalCell) continue; // keep the whole elevator cell clear
                        _wall[roomId].Add(new WallSlot { Room = roomId, Cell = c, Dir = dir });
                    }

                    bool hallway = layout.Rooms[roomId].IsHallway;
                    if (!hallway && !hasDoor && !isPortalCell)
                    {
                        _center[roomId].Add(c);
                    }
                }
            }

            foreach (var list in _wall.Values) rng.Shuffle(list);
            foreach (var list in _center.Values) rng.Shuffle(list);
        }

        public int WallCount(int room) => _wall[room].Count;
        public int CenterCount(int room) => _center[room].Count;

        public bool TryTakeWall(int room, out WallSlot slot)
        {
            var list = _wall[room];
            // Do not put a wall prop in a cell whose centre is already used by a large prop.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (_usedCenters.Contains(list[i].Cell)) continue;
                slot = list[i];
                list.RemoveAt(i);
                _usedWallCells.Add(slot.Cell);
                return true;
            }
            slot = default;
            return false;
        }

        public bool TryTakeCenter(int room, out Vector2Int cell)
        {
            var list = _center[room];
            // A large centre prop needs the whole cell, so skip cells that already have wall props.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (_usedWallCells.Contains(list[i])) continue;
                cell = list[i];
                list.RemoveAt(i);
                _usedCenters.Add(cell);
                return true;
            }
            cell = default;
            return false;
        }

        /// <summary>Takes a wall slot in the first room (in the given order) that still has one.</summary>
        public bool TryTakeWallInAny(IEnumerable<int> rooms, out WallSlot slot)
        {
            foreach (int r in rooms)
            {
                if (TryTakeWall(r, out slot)) return true;
            }
            slot = default;
            return false;
        }

        public IEnumerable<int> RoomsWithWallSlots() => _wall.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key);
    }
}
