using System.Collections.Generic;
using System.Linq;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>Tunables for one floor; scaled by depth through <see cref="ForDepth"/>.</summary>
    public struct FloorParams
    {
        /// <summary>Footprint of the first floor, in 4x4 m cells (480 m²).</summary>
        public const int BaseArea = 30;

        /// <summary>Each floor is this much bigger than the one before (+15% area)...</summary>
        public const float GrowthPerFloor = 0.15f;

        /// <summary>...up to this footprint (1,120 m², reached on the 8th floor).</summary>
        public const int MaxArea = 70;

        /// <summary>Footprint cells the floor should have (the generated floor is within ±5% of it).</summary>
        public int TargetArea;

        public int MinRoom;
        public int MaxRoom;
        public float LoopChance;
        public float HallwayChance;
        public float StopChance;
        public int MinLoops;

        /// <summary>Share of floors whose outline stays a plain rectangle; the rest are irregular.</summary>
        public float RectangleChance;

        /// <summary>Target footprint (cells) of floor <paramref name="floorIndex"/> (0-based): +15% per floor, capped.</summary>
        public static int AreaForFloor(int floorIndex)
        {
            float area = BaseArea * Mathf.Pow(1f + GrowthPerFloor, Mathf.Max(0, floorIndex));
            return Mathf.Min(MaxArea, Mathf.FloorToInt(area + 0.5f));
        }

        /// <param name="floorCount">Unused since floors grow per floor reached (kept for callers and tests).</param>
        public static FloorParams ForDepth(int floorIndex, int floorCount)
        {
            int area = AreaForFloor(floorIndex);
            return new FloorParams
            {
                TargetArea = area,
                MinRoom = 2,
                MaxRoom = 4,
                MinLoops = area >= 45 ? 2 : 1,
                LoopChance = 0.3f,
                HallwayChance = 0.3f,
                StopChance = 0.3f,
                RectangleChance = 0.1f
            };
        }
    }

    /// <summary>
    /// Pure, deterministic floor layout generator:
    /// <list type="number">
    /// <item>Footprint: the bounding grid with notches cut from corners and sides and, on bigger floors, an enclosed
    /// courtyard; cells outside the building have room -1. Strict L, T and U outlines are rejected
    /// (<see cref="LayoutValidator.ClassifyOutline(bool[,])"/>), as are parts thinner than two cells.</item>
    /// <item>Rooms: the footprint is cut into large rectangular blocks, each split into rooms by BSP (occasionally with
    /// a one-cell hallway).</item>
    /// <item>Doors: random spanning tree plus loop doors, then extra doors until no single room, corridor or door is the
    /// only way between two major areas (<see cref="LayoutValidator.FindSeparation"/>).</item>
    /// <item>Start and exit portals on the outer wall, as far apart as possible.</item>
    /// </list>
    /// Attempts that cannot satisfy <see cref="LayoutValidator.Check"/> are discarded and retried with a derived seed.
    /// </summary>
    public static class LayoutGenerator
    {
        private const int MaxAttempts = 80;
        private const int FootprintAttempts = 300;
        private const int MaxGridSide = 18;

        public static FloorLayout Generate(int seed, int floorIndex, int floorCount)
        {
            return Generate(seed, floorIndex, floorCount, FloorParams.ForDepth(floorIndex, floorCount));
        }

        public static FloorLayout Generate(int seed, int floorIndex, int floorCount, FloorParams p)
        {
            // Outline kind is decided once per floor: irregular footprints fail more often further down (a wing joined by
            // a narrow neck cannot have two routes), so re-rolling it per attempt would favour rectangles.
            bool rectangle = new Rng(Rng.Combine(seed, 31337)).Chance(p.RectangleChance);

            FloorLayout last = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var rng = new Rng(attempt == 0 ? seed : Rng.Combine(seed, 7919 + attempt));
                var layout = TryGenerate(seed, floorIndex, floorCount, p, rectangle, rng);
                if (layout == null) continue;
                last = layout;
                if (LayoutValidator.Check(layout).Count == 0) return layout;
            }

            // Should not happen (tests run thousands of seeds); a plain rectangle is the most forgiving fallback.
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var layout = TryGenerate(seed, floorIndex, floorCount, p, true, new Rng(Rng.Combine(seed, 104729 + attempt)));
                if (layout == null) continue;
                last = layout;
                if (LayoutValidator.Check(layout).Count == 0) return layout;
            }
            return last;
        }

        private static FloorLayout TryGenerate(int seed, int floorIndex, int floorCount, FloorParams p, bool rectangle, Rng rng)
        {
            var mask = Footprint(p, rectangle, rng);
            if (mask == null) return null;
            int w = mask.GetLength(0), h = mask.GetLength(1);

            var layout = new FloorLayout
            {
                Seed = seed,
                FloorIndex = floorIndex,
                FloorCount = floorCount,
                Width = w,
                Height = h,
                CellRoom = new int[w, h]
            };
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                layout.CellRoom[x, y] = -1;
            }

            // 1. Rooms.
            var rects = Partition(mask, p, rng);
            if (rects == null) return null;
            for (int i = 0; i < rects.Count; i++)
            {
                var room = new Room
                {
                    Id = i,
                    Rect = rects[i].rect,
                    Type = rects[i].hallway ? RoomType.Hallway : RoomType.DiningHall
                };
                layout.Rooms.Add(room);
                foreach (var c in room.Rect.allPositionsWithin) layout.CellRoom[c.x, c.y] = i;
            }

            // Enough rooms for the objectives (dining hall, kitchen, office) and for the maids to be dodged around.
            int minRooms = Mathf.Max(4, mask.Length > 0 ? Count(mask) / 8 : 4);
            if (layout.Rooms.Count < minRooms || layout.Rooms.Count(r => !r.IsHallway) < 3) return null;

            // 2. Shared edges between adjacent rooms (in deterministic order).
            var sharedKeys = new List<(int a, int b)>();
            var shared = new Dictionary<(int a, int b), List<(Vector2Int ca, Vector2Int cb)>>();
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                var c = new Vector2Int(x, y);
                AddShared(layout, c, c + Vector2Int.right, sharedKeys, shared);
                AddShared(layout, c, c + Vector2Int.up, sharedKeys, shared);
            }

            // 3. Random spanning tree + loop doors. No preference for hallway edges: a corridor must not become the hub.
            var order = new List<(int a, int b)>(sharedKeys);
            rng.Shuffle(order);
            var parent = Enumerable.Range(0, layout.Rooms.Count).ToArray();
            foreach (var edge in order)
            {
                bool isTreeEdge = Union(parent, edge.a, edge.b);
                if (isTreeEdge || rng.Chance(p.LoopChance)) AddDoor(layout, edge, shared[edge], rng);
            }
            if (layout.Rooms.Any(r => Find(parent, r.Id) != Find(parent, 0))) return null;

            // 4. Two independent routes between the big rooms; corridors lead somewhere.
            if (!EnsureRoutes(layout, LayoutValidator.MajorRooms(layout), sharedKeys, shared, rng)) return null;
            foreach (var room in layout.Rooms)
            {
                if (!room.IsHallway || room.Neighbors.Count >= 2) continue;
                var options = sharedKeys.Where(k => (k.a == room.Id || k.b == room.Id) && !HasDoor(layout, k)).ToList();
                if (options.Count > 0)
                {
                    var k = rng.Pick(options);
                    AddDoor(layout, k, shared[k], rng);
                }
            }

            // Chase escapes: at least MinLoops cycles in the door graph.
            var free = sharedKeys.Where(k => !HasDoor(layout, k)).ToList();
            rng.Shuffle(free);
            for (int i = 0; Loops(layout) < p.MinLoops && i < free.Count; i++)
            {
                AddDoor(layout, free[i], shared[free[i]], rng);
            }

            // 5. Start and exit portals on the outer wall (facing the outside, never into a courtyard).
            var outside = OutsideCells(layout);
            var boundaryRooms = layout.Rooms.Where(r => HasOutsideEdge(layout, r, outside)).ToList();
            if (boundaryRooms.Count < 2) return null;
            var startCandidates = boundaryRooms.Where(r => !r.IsHallway).ToList();
            if (startCandidates.Count == 0) startCandidates = boundaryRooms;
            Room startRoom = rng.Pick(startCandidates);

            layout.RoomDistance = Distances(layout, startRoom.Id);
            Vector2 startCenter = startRoom.Rect.center;
            Room exitRoom = boundaryRooms
                .Where(r => r.Id != startRoom.Id)
                .OrderByDescending(r => layout.RoomDistance[r.Id])
                .ThenByDescending(r => Vector2.Distance(r.Rect.center, startCenter))
                .ThenBy(r => r.Id)
                .First();

            // The arrival and exit rooms are major areas too, even when small.
            if (!EnsureRoutes(layout, LayoutValidator.MajorRooms(layout, startRoom.Id, exitRoom.Id), sharedKeys, shared, rng)) return null;
            layout.RoomDistance = Distances(layout, startRoom.Id);

            layout.Start = PickPortal(layout, startRoom, outside, rng, null);
            var exit = PickPortal(layout, exitRoom, outside, rng, layout.Start.Cell + layout.Start.Outward);
            if (exit.Room < 0) return null;
            layout.Exit = exit;

            // 6. Room types.
            AssignRoomTypes(layout, startRoom.Id, exitRoom.Id);
            return layout;
        }

        // ------------------------------------------------------------------ footprint

        /// <summary>
        /// A footprint of about <see cref="FloorParams.TargetArea"/> cells (a plain rectangle when
        /// <paramref name="rectangle"/>, otherwise irregular), or null. Never a strict L/T/U outline, always connected,
        /// no part thinner than two cells.
        /// </summary>
        private static bool[,] Footprint(FloorParams p, bool rectangle, Rng rng)
        {
            int target = p.TargetArea;
            int tolerance = Mathf.Max(2, Mathf.RoundToInt(target * 0.05f));
            for (int attempt = 0; attempt < FootprintAttempts; attempt++)
            {
                float fill = rectangle ? 1f : rng.Range(0.62f, 0.82f);
                float aspect = rng.Range(0.75f, 1.4f);
                float box = target / fill;
                int w = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(box * aspect)), 4, MaxGridSide);
                int h = Mathf.Clamp(Mathf.RoundToInt(box / w), 4, MaxGridSide);

                var mask = new bool[w, h];
                for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    mask[x, y] = true;
                }

                int area = w * h;
                if (!rectangle)
                {
                    // At least two notches in the outline (one alone is exactly an L or a U); the cells to remove are
                    // shared between the planned notches. A courtyard does not count: it leaves the outline as it is.
                    int wantedNotches = rng.Range(2, 4);
                    int notches = 0;
                    bool hole = false;
                    for (int op = 0; op < 14; op++)
                    {
                        if (notches >= wantedNotches && area <= target + tolerance) break;
                        if (area <= target) break;
                        int excess = area - target + tolerance;
                        int budget = notches < wantedNotches ? Mathf.Max(4, excess / (wantedNotches - notches)) : excess;
                        int roll = rng.Range(0, 10);
                        if (roll < 5 || (roll >= 8 && (hole || w < 7 || h < 7)))
                        {
                            int removed = CarveCorner(mask, rng, budget);
                            area -= removed;
                            if (removed > 0) notches++;
                        }
                        else if (roll < 8)
                        {
                            int removed = CarveSide(mask, rng, budget);
                            area -= removed;
                            if (removed > 0) notches++;
                        }
                        else
                        {
                            int removed = CarveHole(mask, rng, budget);
                            area -= removed;
                            hole = removed > 0;
                        }
                    }
                }

                mask = Trim(mask);
                area = Count(mask);
                if (Mathf.Abs(area - target) > tolerance) continue;
                if (!Connected(mask) || !NoThinParts(mask)) continue;
                var shape = LayoutValidator.ClassifyOutline(mask);
                if (shape == OutlineShape.L || shape == OutlineShape.T || shape == OutlineShape.U) continue;
                return mask;
            }
            return null;
        }

        /// <summary>Cuts a rectangular notch (≥ 2x2) out of a random corner; returns the cells removed.</summary>
        private static int CarveCorner(bool[,] mask, Rng rng, int budget)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (w < 4 || h < 4) return 0;
            for (int tries = 0; tries < 6; tries++)
            {
                int nw = rng.Range(2, Mathf.Max(3, w / 2 + 1));
                int nh = rng.Range(2, Mathf.Max(3, h / 2 + 1));
                if (nw * nh > budget || nw > w - 2 || nh > h - 2) continue;
                int corner = rng.Range(0, 4);
                int x0 = (corner & 1) == 0 ? 0 : w - nw;
                int y0 = (corner & 2) == 0 ? 0 : h - nh;
                return Clear(mask, new RectInt(x0, y0, nw, nh));
            }
            return 0;
        }

        /// <summary>Cuts a notch into the middle of a random side (≥ 2 cells from both corners).</summary>
        private static int CarveSide(bool[,] mask, Rng rng, int budget)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int tries = 0; tries < 6; tries++)
            {
                int side = rng.Range(0, 4);
                bool horizontal = side < 2; // notch in the bottom (0) or top (1) side
                int along = horizontal ? w : h;
                int across = horizontal ? h : w;
                if (along < 6 || across < 4) continue;
                int length = rng.Range(2, along - 3);
                int depth = rng.Range(1, Mathf.Max(2, across / 2));
                if (length * depth > budget) continue;
                int start = rng.Range(2, along - 2 - length + 1);
                RectInt r;
                if (horizontal) r = new RectInt(start, side == 0 ? 0 : h - depth, length, depth);
                else r = new RectInt(side == 2 ? 0 : w - depth, start, depth, length);
                return Clear(mask, r);
            }
            return 0;
        }

        /// <summary>Cuts an enclosed courtyard (≥ 2x2) with at least two cells of building all round it.</summary>
        private static int CarveHole(bool[,] mask, Rng rng, int budget)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int tries = 0; tries < 6; tries++)
            {
                int hw = rng.Range(2, Mathf.Max(3, w - 4 + 1));
                int hh = rng.Range(2, Mathf.Max(3, h - 4 + 1));
                if (hw * hh > budget || hw > w - 4 || hh > h - 4) continue;
                int x0 = rng.Range(2, w - 2 - hw + 1);
                int y0 = rng.Range(2, h - 2 - hh + 1);
                return Clear(mask, new RectInt(x0, y0, hw, hh));
            }
            return 0;
        }

        private static int Clear(bool[,] mask, RectInt r)
        {
            int removed = 0;
            foreach (var c in r.allPositionsWithin)
            {
                if (!mask[c.x, c.y]) continue;
                mask[c.x, c.y] = false;
                removed++;
            }
            return removed;
        }

        private static int Count(bool[,] mask)
        {
            int n = 0;
            foreach (bool b in mask) if (b) n++;
            return n;
        }

        /// <summary>Drops empty border rows and columns so the grid is the footprint's bounding box.</summary>
        private static bool[,] Trim(bool[,] mask)
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
            if (maxX < 0) return mask;
            var trimmed = new bool[maxX - minX + 1, maxY - minY + 1];
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                trimmed[x - minX, y - minY] = mask[x, y];
            }
            return trimmed;
        }

        private static bool Connected(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            var start = new Vector2Int(-1, -1);
            for (int x = 0; x < w && start.x < 0; x++)
            for (int y = 0; y < h; y++)
            {
                if (mask[x, y]) { start = new Vector2Int(x, y); break; }
            }
            if (start.x < 0) return false;
            var seen = new bool[w, h];
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            seen[start.x, start.y] = true;
            int reached = 0;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                reached++;
                foreach (var d in FloorLayout.Directions)
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h || seen[n.x, n.y] || !mask[n.x, n.y]) continue;
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            return reached == Count(mask);
        }

        /// <summary>True if every footprint cell lies in some 2x2 block of footprint cells (no one-cell-wide spurs).</summary>
        private static bool NoThinParts(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            var covered = new bool[w, h];
            for (int x = 0; x < w - 1; x++)
            for (int y = 0; y < h - 1; y++)
            {
                if (!mask[x, y] || !mask[x + 1, y] || !mask[x, y + 1] || !mask[x + 1, y + 1]) continue;
                covered[x, y] = covered[x + 1, y] = covered[x, y + 1] = covered[x + 1, y + 1] = true;
            }
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (mask[x, y] && !covered[x, y]) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ rooms

        /// <summary>
        /// Cuts the footprint into large rectangular blocks (at least 2x2, picked among the largest still free) and
        /// splits each block into rooms. Leftover one-cell-wide strips of 2+ cells become hallways; anything else
        /// returns null.
        /// </summary>
        private static List<(RectInt rect, bool hallway)> Partition(bool[,] mask, FloorParams p, Rng rng)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            var free = (bool[,])mask.Clone();
            var blocks = new List<RectInt>();

            for (int guard = 0; guard < 64; guard++)
            {
                var sums = PrefixSums(free);
                var candidates = new List<RectInt>();
                int best = 0;
                for (int x0 = 0; x0 < w; x0++)
                for (int y0 = 0; y0 < h; y0++)
                {
                    if (!free[x0, y0]) continue;
                    for (int x1 = x0 + 1; x1 < w; x1++)
                    for (int y1 = y0 + 1; y1 < h; y1++)
                    {
                        int area = (x1 - x0 + 1) * (y1 - y0 + 1);
                        if (RectSum(sums, x0, y0, x1, y1) != area) break; // taller rectangles from here are not full either
                        if (area < best * 0.8f) continue;
                        if (area > best) best = area;
                        candidates.Add(new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
                    }
                }
                if (best == 0) break;
                candidates.RemoveAll(r => r.width * r.height < best * 0.8f);
                var block = rng.Pick(candidates);
                Clear(free, block);
                blocks.Add(block);
            }

            var result = new List<(RectInt rect, bool hallway)>();
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!free[x, y]) continue;
                int right = x;
                while (right + 1 < w && free[right + 1, y]) right++;
                int up = y;
                while (up + 1 < h && free[x, up + 1]) up++;
                var strip = right - x >= up - y
                    ? new RectInt(x, y, right - x + 1, 1)
                    : new RectInt(x, y, 1, up - y + 1);
                if (strip.width * strip.height < 2) return null;
                Clear(free, strip);
                result.Add((strip, true));
            }

            foreach (var block in blocks) Split(block, p, rng, 0, result);
            return result;
        }

        private static int[,] PrefixSums(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            var s = new int[w + 1, h + 1];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                s[x + 1, y + 1] = (mask[x, y] ? 1 : 0) + s[x, y + 1] + s[x + 1, y] - s[x, y];
            }
            return s;
        }

        private static int RectSum(int[,] s, int x0, int y0, int x1, int y1)
        {
            return s[x1 + 1, y1 + 1] - s[x0, y1 + 1] - s[x1 + 1, y0] + s[x0, y0];
        }

        private static void Split(RectInt r, FloorParams p, Rng rng, int depth, List<(RectInt, bool)> output)
        {
            int w = r.width;
            int h = r.height;

            // Occasionally carve a one-cell-wide hallway through the long axis of a big block.
            if (depth == 0 && rng.Chance(p.HallwayChance))
            {
                bool alongX = w >= h;
                int across = alongX ? h : w;
                int along = alongX ? w : h;
                if (across >= 2 * p.MinRoom + 1 && along >= 4)
                {
                    int pos = rng.Range(p.MinRoom, across - p.MinRoom);
                    RectInt a, hall, b;
                    if (alongX)
                    {
                        a = new RectInt(r.x, r.y, w, pos);
                        hall = new RectInt(r.x, r.y + pos, w, 1);
                        b = new RectInt(r.x, r.y + pos + 1, w, h - pos - 1);
                    }
                    else
                    {
                        a = new RectInt(r.x, r.y, pos, h);
                        hall = new RectInt(r.x + pos, r.y, 1, h);
                        b = new RectInt(r.x + pos + 1, r.y, w - pos - 1, h);
                    }
                    Split(a, p, rng, depth + 1, output);
                    output.Add((hall, true));
                    Split(b, p, rng, depth + 1, output);
                    return;
                }
            }

            bool canSplitW = w >= 2 * p.MinRoom;
            bool canSplitH = h >= 2 * p.MinRoom;
            bool tooBig = w > p.MaxRoom || h > p.MaxRoom;

            if (!canSplitW && !canSplitH)
            {
                output.Add((r, false));
                return;
            }
            if (!tooBig && rng.Chance(p.StopChance))
            {
                output.Add((r, false));
                return;
            }

            bool splitW;
            if (canSplitW && canSplitH)
            {
                if (w > p.MaxRoom && h <= p.MaxRoom) splitW = true;
                else if (h > p.MaxRoom && w <= p.MaxRoom) splitW = false;
                else if (w > h) splitW = rng.Chance(0.8f);
                else if (w < h) splitW = rng.Chance(0.2f);
                else splitW = rng.Chance(0.5f);
            }
            else
            {
                splitW = canSplitW;
            }

            int size = splitW ? w : h;
            int cut = rng.Range(p.MinRoom, size - p.MinRoom + 1);
            if (splitW)
            {
                Split(new RectInt(r.x, r.y, cut, h), p, rng, depth + 1, output);
                Split(new RectInt(r.x + cut, r.y, w - cut, h), p, rng, depth + 1, output);
            }
            else
            {
                Split(new RectInt(r.x, r.y, w, cut), p, rng, depth + 1, output);
                Split(new RectInt(r.x, r.y + cut, w, h - cut), p, rng, depth + 1, output);
            }
        }

        // ------------------------------------------------------------------ doors

        /// <summary>
        /// Adds doors until no single room or door is the only way between two of <paramref name="majors"/>: each
        /// round finds such a bottleneck and opens a wall between rooms on different sides of it (preferring rooms
        /// whose sides hold major areas). False if the walls left cannot fix it.
        /// </summary>
        private static bool EnsureRoutes(FloorLayout layout, HashSet<int> majors, List<(int a, int b)> sharedKeys,
            Dictionary<(int a, int b), List<(Vector2Int ca, Vector2Int cb)>> shared, Rng rng)
        {
            for (int guard = 0; guard < 120; guard++)
            {
                var separation = LayoutValidator.FindSeparation(layout, majors);
                if (separation == null) return true;

                var labels = separation.Labels;
                var groupsWithMajors = new HashSet<int>();
                foreach (int m in majors)
                {
                    if (labels[m] >= 0) groupsWithMajors.Add(labels[m]);
                }

                var candidates = new List<(int a, int b)>();
                int bestPriority = -1;
                foreach (var key in sharedKeys)
                {
                    if (HasDoor(layout, key)) continue;
                    int la = labels[key.a], lb = labels[key.b];
                    if (la < 0 || lb < 0 || la == lb) continue;
                    int priority = (groupsWithMajors.Contains(la) ? 1 : 0) + (groupsWithMajors.Contains(lb) ? 1 : 0);
                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        candidates.Clear();
                    }
                    if (priority == bestPriority) candidates.Add(key);
                }
                if (candidates.Count == 0) return false;
                var pick = rng.Pick(candidates);
                AddDoor(layout, pick, shared[pick], rng);
            }
            return false;
        }

        private static bool HasDoor(FloorLayout layout, (int a, int b) key) => layout.Rooms[key.a].Neighbors.Contains(key.b);

        private static int Loops(FloorLayout layout) => layout.Doors.Count - (layout.Rooms.Count - 1);

        private static void AddDoor(FloorLayout layout, (int a, int b) edge, List<(Vector2Int ca, Vector2Int cb)> candidates, Rng rng)
        {
            int index = candidates.Count >= 3 ? rng.Range(1, candidates.Count - 1) : rng.Range(0, candidates.Count);
            var (ca, cb) = candidates[index];
            layout.RegisterDoor(new Door
            {
                RoomA = layout.CellRoom[ca.x, ca.y],
                RoomB = layout.CellRoom[cb.x, cb.y],
                CellA = ca,
                CellB = cb
            });
            layout.Rooms[edge.a].Neighbors.Add(edge.b);
            layout.Rooms[edge.b].Neighbors.Add(edge.a);
        }

        private static void AddShared(FloorLayout layout, Vector2Int c, Vector2Int n,
            List<(int, int)> keys, Dictionary<(int, int), List<(Vector2Int, Vector2Int)>> shared)
        {
            int ra = layout.RoomAt(c);
            int rb = layout.RoomAt(n);
            if (ra < 0 || rb < 0 || ra == rb) return;
            var key = ra < rb ? (ra, rb) : (rb, ra);
            if (!shared.TryGetValue(key, out var list))
            {
                list = new List<(Vector2Int, Vector2Int)>();
                shared[key] = list;
                keys.Add(key);
            }
            list.Add((c, n));
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static bool Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra == rb) return false;
            parent[rb] = ra;
            return true;
        }

        public static int[] Distances(FloorLayout layout, int from)
        {
            var dist = Enumerable.Repeat(-1, layout.Rooms.Count).ToArray();
            var queue = new Queue<int>();
            dist[from] = 0;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int r = queue.Dequeue();
                foreach (int n in layout.Rooms[r].Neighbors)
                {
                    if (dist[n] >= 0) continue;
                    dist[n] = dist[r] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        // ------------------------------------------------------------------ portals

        /// <summary>Cells outside the building that connect to the open outside (not enclosed courtyards).</summary>
        private static bool[,] OutsideCells(FloorLayout layout)
        {
            int w = layout.Width, h = layout.Height;
            var outside = new bool[w, h];
            var queue = new Queue<Vector2Int>();
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                if (!border || layout.CellRoom[x, y] >= 0) continue;
                outside[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in FloorLayout.Directions)
                {
                    var n = c + d;
                    if (!layout.InBounds(n) || outside[n.x, n.y] || layout.CellRoom[n.x, n.y] >= 0) continue;
                    outside[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            return outside;
        }

        private static bool IsOutside(FloorLayout layout, bool[,] outside, Vector2Int c) =>
            !layout.InBounds(c) || outside[c.x, c.y];

        private static bool HasOutsideEdge(FloorLayout layout, Room room, bool[,] outside)
        {
            foreach (var c in room.Rect.allPositionsWithin)
            {
                foreach (var dir in FloorLayout.Directions)
                {
                    if (IsOutside(layout, outside, c + dir)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A portal in the room's outer wall (preferably away from the room's corners). The elevator car sits in the
        /// outside cell beyond it, so <paramref name="taken"/> (the other car's cell) is skipped. Room -1 if none.
        /// </summary>
        private static Portal PickPortal(FloorLayout layout, Room room, bool[,] outside, Rng rng, Vector2Int? taken)
        {
            var preferred = new List<Portal>();
            var fallback = new List<Portal>();
            foreach (var c in room.Rect.allPositionsWithin)
            {
                foreach (var dir in FloorLayout.Directions)
                {
                    var beyond = c + dir;
                    if (!IsOutside(layout, outside, beyond)) continue;
                    if (taken.HasValue && beyond == taken.Value) continue;
                    var portal = new Portal { Room = room.Id, Cell = c, Outward = dir };
                    // Avoid the room's corner cells along that wall so the opening is centred.
                    bool alongX = dir.y != 0;
                    bool corner = alongX
                        ? (c.x == room.Rect.xMin || c.x == room.Rect.xMax - 1) && room.Rect.width > 2
                        : (c.y == room.Rect.yMin || c.y == room.Rect.yMax - 1) && room.Rect.height > 2;
                    (corner ? fallback : preferred).Add(portal);
                }
            }
            if (preferred.Count > 0) return rng.Pick(preferred);
            if (fallback.Count > 0) return rng.Pick(fallback);
            return new Portal { Room = -1 };
        }

        // ------------------------------------------------------------------ room types

        private static void AssignRoomTypes(FloorLayout layout, int startRoom, int exitRoom)
        {
            var rooms = layout.Rooms.Where(r => !r.IsHallway)
                .OrderByDescending(r => r.Area)
                .ThenBy(r => r.Id)
                .ToList();
            if (rooms.Count == 0) return;

            var assigned = new HashSet<int>();
            void Assign(Room r, RoomType t)
            {
                r.Type = t;
                assigned.Add(r.Id);
            }

            Assign(rooms[0], RoomType.DiningHall);
            if (rooms.Count > 1) Assign(rooms[1], RoomType.Kitchen);

            // Small rooms away from the start become the office and storage.
            var small = rooms.Where(r => !assigned.Contains(r.Id))
                .OrderBy(r => r.Area)
                .ThenBy(r => r.Id == startRoom ? 1 : 0)
                .ThenBy(r => r.Id)
                .ToList();
            if (small.Count > 0) Assign(small[0], RoomType.ManagerOffice);
            if (small.Count > 1) Assign(small[1], RoomType.Storage);

            RoomType[] cycle = { RoomType.StaffRoom, RoomType.Stage, RoomType.Restroom, RoomType.DiningHall, RoomType.Storage };
            int i = 0;
            foreach (var r in rooms)
            {
                if (assigned.Contains(r.Id)) continue;
                Assign(r, cycle[i % cycle.Length]);
                i++;
            }
        }
    }
}
