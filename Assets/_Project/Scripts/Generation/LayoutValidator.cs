using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>Shape of a floor's outer outline (holes/courtyards do not change it).</summary>
    public enum OutlineShape
    {
        Rectangle,
        L,
        T,
        U,
        Irregular
    }

    /// <summary>
    /// A single point of failure in a floor's door graph: removing <see cref="Room"/> (or the door between
    /// <see cref="DoorA"/> and <see cref="DoorB"/>) splits the major rooms into separate groups.
    /// </summary>
    public sealed class Separation
    {
        public int Room = -1;
        public int DoorA = -1;
        public int DoorB = -1;

        /// <summary>Connected group of every room once the room/door is removed (-1 = the removed room).</summary>
        public int[] Labels;

        public string Describe(FloorLayout layout)
        {
            if (Room >= 0)
            {
                var r = layout.Rooms[Room];
                return $"{(r.IsHallway ? "Corridor" : "Room")} {Room} ({r.Type}) is the only way between major areas";
            }
            return $"The door between rooms {DoorA} and {DoorB} is the only way between major areas";
        }
    }

    /// <summary>
    /// Pure rules every generated floor must satisfy (used by <see cref="LayoutGenerator"/> while generating and by
    /// <see cref="FloorDirector"/> before building):
    /// <list type="bullet">
    /// <item>Every room is reachable.</item>
    /// <item>Major areas (start room, exit room, every non-hallway room bigger than 2x2 cells) are linked by at least
    /// two routes that share no room and no door: no single room, corridor or door is the only way between them, so
    /// the player can slip past a maid by going round. Small 2x2 side rooms may be dead ends.</item>
    /// <item>Corridors lead somewhere (at least two doors).</item>
    /// <item>The outer outline is not a strict L, T or U shape.</item>
    /// </list>
    /// </summary>
    public static class LayoutValidator
    {
        /// <summary>Non-hallway rooms with at least this many cells are major areas.</summary>
        public const int MajorRoomMinArea = 5;

        public static List<string> Check(FloorLayout layout)
        {
            var errors = new List<string>();
            if (layout.Rooms.Count == 0)
            {
                errors.Add("No rooms");
                return errors;
            }

            var all = Components(layout, -1, -1, -1);
            if (layout.Rooms.Any(r => all[r.Id] != all[0])) errors.Add("Some rooms are unreachable");

            var majors = MajorRooms(layout, layout.Start.Room, layout.Exit.Room);
            var separation = FindSeparation(layout, majors);
            if (separation != null) errors.Add(separation.Describe(layout));

            foreach (var room in layout.Rooms)
            {
                if (room.IsHallway && room.Neighbors.Count < 2) errors.Add($"Corridor {room.Id} is a dead end");
            }

            var shape = ClassifyOutline(layout);
            if (shape == OutlineShape.L || shape == OutlineShape.T || shape == OutlineShape.U)
            {
                errors.Add($"Outline is a strict {shape} shape");
            }
            return errors;
        }

        /// <summary>Major areas: non-hallway rooms of <see cref="MajorRoomMinArea"/>+ cells, plus <paramref name="extra"/> rooms.</summary>
        public static HashSet<int> MajorRooms(FloorLayout layout, params int[] extra)
        {
            var set = new HashSet<int>();
            foreach (var r in layout.Rooms)
            {
                if (!r.IsHallway && r.Area >= MajorRoomMinArea) set.Add(r.Id);
            }
            foreach (int id in extra)
            {
                if (id >= 0 && id < layout.Rooms.Count) set.Add(id);
            }
            return set;
        }

        /// <summary>
        /// The first room or door whose removal splits <paramref name="majors"/> into more than one group, or null if
        /// every pair of major rooms has two independent routes.
        /// </summary>
        public static Separation FindSeparation(FloorLayout layout, HashSet<int> majors)
        {
            if (majors.Count < 2) return null;

            foreach (var room in layout.Rooms)
            {
                var labels = Components(layout, room.Id, -1, -1);
                if (MajorsSplit(labels, majors, room.Id)) return new Separation { Room = room.Id, Labels = labels };
            }

            foreach (var room in layout.Rooms)
            {
                foreach (int n in room.Neighbors)
                {
                    if (n < room.Id) continue;
                    var labels = Components(layout, -1, room.Id, n);
                    if (MajorsSplit(labels, majors, -1)) return new Separation { DoorA = room.Id, DoorB = n, Labels = labels };
                }
            }
            return null;
        }

        private static bool MajorsSplit(int[] labels, HashSet<int> majors, int removed)
        {
            int group = int.MinValue;
            foreach (int m in majors)
            {
                if (m == removed) continue;
                if (group == int.MinValue) group = labels[m];
                else if (labels[m] != group) return true;
            }
            return false;
        }

        /// <summary>
        /// Connected groups of the door graph without room <paramref name="skipRoom"/> and without the door between
        /// <paramref name="skipA"/> and <paramref name="skipB"/> (pass -1 to skip nothing).
        /// </summary>
        public static int[] Components(FloorLayout layout, int skipRoom, int skipA, int skipB)
        {
            int count = layout.Rooms.Count;
            var labels = Enumerable.Repeat(-2, count).ToArray();
            if (skipRoom >= 0) labels[skipRoom] = -1;
            var queue = new Queue<int>();
            int next = 0;
            for (int start = 0; start < count; start++)
            {
                if (labels[start] != -2) continue;
                labels[start] = next;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int r = queue.Dequeue();
                    foreach (int n in layout.Rooms[r].Neighbors)
                    {
                        if (labels[n] != -2) continue;
                        if ((r == skipA && n == skipB) || (r == skipB && n == skipA)) continue;
                        labels[n] = next;
                        queue.Enqueue(n);
                    }
                }
                next++;
            }
            return labels;
        }

        // ------------------------------------------------------------------ outline

        public static OutlineShape ClassifyOutline(FloorLayout layout)
        {
            var mask = new bool[layout.Width, layout.Height];
            for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
            {
                mask[x, y] = layout.CellRoom[x, y] >= 0;
            }
            return ClassifyOutline(mask);
        }

        /// <summary>
        /// Classifies the outline of the cells set in <paramref name="mask"/> from the notches cut into its bounding
        /// box (empty cells connected to the outside; enclosed holes are ignored):
        /// none = rectangle; one rectangular notch at a corner = L; one rectangular notch in the middle of a side = U;
        /// two rectangular corner notches on the same side = T; anything else = irregular.
        /// </summary>
        public static OutlineShape ClassifyOutline(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!mask[x, y]) continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
            if (maxX < 0) return OutlineShape.Irregular;

            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            var seen = new bool[bw, bh];
            var notches = new List<(bool rectangular, bool left, bool right, bool bottom, bool top)>();
            var queue = new Queue<Vector2Int>();
            for (int x = 0; x < bw; x++)
            for (int y = 0; y < bh; y++)
            {
                bool border = x == 0 || y == 0 || x == bw - 1 || y == bh - 1;
                if (!border || seen[x, y] || mask[minX + x, minY + y]) continue;

                int cells = 0, nx0 = x, ny0 = y, nx1 = x, ny1 = y;
                seen[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    cells++;
                    nx0 = Mathf.Min(nx0, c.x);
                    ny0 = Mathf.Min(ny0, c.y);
                    nx1 = Mathf.Max(nx1, c.x);
                    ny1 = Mathf.Max(ny1, c.y);
                    foreach (var d in FloorLayout.Directions)
                    {
                        var n = c + d;
                        if (n.x < 0 || n.y < 0 || n.x >= bw || n.y >= bh) continue;
                        if (seen[n.x, n.y] || mask[minX + n.x, minY + n.y]) continue;
                        seen[n.x, n.y] = true;
                        queue.Enqueue(n);
                    }
                }
                bool rectangular = cells == (nx1 - nx0 + 1) * (ny1 - ny0 + 1);
                notches.Add((rectangular, nx0 == 0, nx1 == bw - 1, ny0 == 0, ny1 == bh - 1));
            }

            if (notches.Count == 0) return OutlineShape.Rectangle;

            int Sides((bool rectangular, bool left, bool right, bool bottom, bool top) n) =>
                (n.left ? 1 : 0) + (n.right ? 1 : 0) + (n.bottom ? 1 : 0) + (n.top ? 1 : 0);

            if (notches.Count == 1 && notches[0].rectangular)
            {
                return Sides(notches[0]) >= 2 ? OutlineShape.L : OutlineShape.U;
            }

            if (notches.Count == 2 && notches.All(n => n.rectangular && Sides(n) == 2))
            {
                var a = notches[0];
                var b = notches[1];
                bool shareSide = (a.left && b.left) || (a.right && b.right) || (a.bottom && b.bottom) || (a.top && b.top);
                if (shareSide) return OutlineShape.T;
            }
            return OutlineShape.Irregular;
        }
    }
}
