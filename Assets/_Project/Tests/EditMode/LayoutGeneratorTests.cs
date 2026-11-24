using System.Collections.Generic;
using System.Linq;
using MaidCafe.Core;
using MaidCafe.Generation;
using NUnit.Framework;
using UnityEngine;

namespace MaidCafe.Tests
{
    public class LayoutGeneratorTests
    {
        private const int SeedCount = 500;

        private static IEnumerable<(int seed, int floor, int count)> Cases()
        {
            for (int s = 0; s < SeedCount; s++)
            {
                int count = 3 + s % 8;      // 3..10 floors
                int floor = s % count;      // spread across depths
                yield return (Rng.Combine(s, floor), floor, count);
            }
        }

        [Test]
        public void FootprintCellsBelongToTheirRoomAndRoomsTileTheFootprint()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                int roomCells = 0;
                for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    int r = layout.CellRoom[x, y];
                    Assert.That(r, Is.InRange(-1, layout.Rooms.Count - 1), $"seed {seed}");
                    if (r < 0) continue;
                    Assert.IsTrue(layout.Rooms[r].Rect.Contains(new Vector2Int(x, y)), $"seed {seed}");
                }
                foreach (var room in layout.Rooms)
                {
                    foreach (var c in room.Rect.allPositionsWithin) Assert.AreEqual(room.Id, layout.RoomAt(c), $"seed {seed}");
                    roomCells += room.Area;
                }
                Assert.AreEqual(layout.Area, roomCells, $"seed {seed}: rooms overlap or leave gaps");
            }
        }

        [Test]
        public void FloorAreaGrowsFifteenPercentPerFloorUpToTheCap()
        {
            Assert.AreEqual(FloorParams.BaseArea, FloorParams.AreaForFloor(0));
            for (int floor = 1; floor < 10; floor++)
            {
                int previous = FloorParams.AreaForFloor(floor - 1);
                int expected = Mathf.Min(FloorParams.MaxArea, Mathf.RoundToInt(FloorParams.BaseArea * Mathf.Pow(1.15f, floor)));
                Assert.That(FloorParams.AreaForFloor(floor), Is.InRange(expected - 1, expected + 1), $"floor {floor}");
                Assert.GreaterOrEqual(FloorParams.AreaForFloor(floor), previous, $"floor {floor}");
                Assert.LessOrEqual(FloorParams.AreaForFloor(floor), FloorParams.MaxArea);
            }

            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                int target = FloorParams.AreaForFloor(floor);
                int tolerance = Mathf.Max(2, Mathf.RoundToInt(target * 0.05f));
                Assert.That(layout.Area, Is.InRange(target - tolerance, target + tolerance), $"seed {seed} floor {floor}");
            }
        }

        [Test]
        public void OutlinesAreMostlyIrregularAndNeverAStrictLTOrU()
        {
            int irregular = 0;
            foreach (var (seed, floor, count) in Cases())
            {
                var shape = LayoutValidator.ClassifyOutline(LayoutGenerator.Generate(seed, floor, count));
                Assert.That(shape, Is.Not.EqualTo(OutlineShape.L).And.Not.EqualTo(OutlineShape.T).And.Not.EqualTo(OutlineShape.U), $"seed {seed}");
                if (shape == OutlineShape.Irregular) irregular++;
            }
            Assert.Greater(irregular, SeedCount * 0.75f, "most floors should not be plain rectangles");
        }

        [Test]
        public void OutlineClassifierRecognisesShapes()
        {
            Assert.AreEqual(OutlineShape.Rectangle, LayoutValidator.ClassifyOutline(Mask("####", "####", "####")));
            Assert.AreEqual(OutlineShape.Rectangle, LayoutValidator.ClassifyOutline(Mask("######", "#..###", "#..###", "######")), "courtyard ring");
            Assert.AreEqual(OutlineShape.L, LayoutValidator.ClassifyOutline(Mask("##..", "##..", "####", "####")));
            Assert.AreEqual(OutlineShape.T, LayoutValidator.ClassifyOutline(Mask("######", "######", "..##..", "..##..")));
            Assert.AreEqual(OutlineShape.U, LayoutValidator.ClassifyOutline(Mask("##..##", "##..##", "######", "######")));
            Assert.AreEqual(OutlineShape.Irregular, LayoutValidator.ClassifyOutline(Mask("####..", "####..", "..####", "..####")), "Z");
            Assert.AreEqual(OutlineShape.Irregular, LayoutValidator.ClassifyOutline(Mask(".##.", "####", "####", ".##.")), "plus");
            Assert.AreEqual(OutlineShape.Irregular, LayoutValidator.ClassifyOutline(Mask("##..##", "######", "######", "##..##")), "H");
            Assert.AreEqual(OutlineShape.Irregular, LayoutValidator.ClassifyOutline(Mask("##....", "####..", "######", "######")), "stepped corner");
        }

        [Test]
        public void MajorAreasHaveTwoIndependentRoutes()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                var errors = LayoutValidator.Check(layout);
                Assert.IsEmpty(errors, $"seed {seed} floor {floor}: {string.Join("; ", errors)}");
            }
        }

        [Test]
        public void ValidatorRejectsASingleCorridorSpine()
        {
            // Rooms above and below a corridor, every room opening only onto it (plus side loops between neighbours):
            // the corridor is the only way between the top and the bottom rooms.
            var spine = Build(6, 5,
                new[] { (new RectInt(0, 2, 6, 1), true), (new RectInt(0, 3, 3, 2), false), (new RectInt(3, 3, 3, 2), false),
                        (new RectInt(0, 0, 3, 2), false), (new RectInt(3, 0, 3, 2), false) },
                new[] { (0, 1), (0, 2), (0, 3), (0, 4), (1, 2), (3, 4) });
            var errors = LayoutValidator.Check(spine);
            Assert.IsTrue(errors.Any(e => e.Contains("Corridor 0")), string.Join("; ", errors));

            // The same rooms in a ring (no corridor): two routes everywhere.
            var ring = Build(6, 4,
                new[] { (new RectInt(0, 0, 3, 2), false), (new RectInt(3, 0, 3, 2), false), (new RectInt(0, 2, 3, 2), false),
                        (new RectInt(3, 2, 3, 2), false) },
                new[] { (0, 1), (1, 3), (3, 2), (2, 0) });
            Assert.IsEmpty(LayoutValidator.Check(ring));

            // A single door as the only link is rejected too.
            var bridge = Build(6, 4,
                new[] { (new RectInt(0, 0, 3, 2), false), (new RectInt(3, 0, 3, 2), false), (new RectInt(0, 2, 3, 2), false),
                        (new RectInt(3, 2, 3, 2), false) },
                new[] { (0, 1), (1, 3), (2, 0) });
            Assert.IsNotEmpty(LayoutValidator.Check(bridge));
        }

        /// <summary>Rows top to bottom, '#' = building.</summary>
        private static bool[,] Mask(params string[] rows)
        {
            int w = rows[0].Length, h = rows.Length;
            var mask = new bool[w, h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                mask[x, h - 1 - y] = rows[y][x] == '#';
            }
            return mask;
        }

        /// <summary>A hand-made layout: rooms (rect, hallway) and doors between room indices (first shared cell pair).</summary>
        private static FloorLayout Build(int w, int h, (RectInt rect, bool hallway)[] rooms, (int a, int b)[] doors)
        {
            var layout = new FloorLayout { Width = w, Height = h, CellRoom = new int[w, h] };
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                layout.CellRoom[x, y] = -1;
            }
            for (int i = 0; i < rooms.Length; i++)
            {
                layout.Rooms.Add(new Room { Id = i, Rect = rooms[i].rect, Type = rooms[i].hallway ? RoomType.Hallway : RoomType.DiningHall });
                foreach (var c in rooms[i].rect.allPositionsWithin) layout.CellRoom[c.x, c.y] = i;
            }
            foreach (var (a, b) in doors)
            {
                bool placed = false;
                foreach (var c in rooms[a].rect.allPositionsWithin)
                {
                    foreach (var d in FloorLayout.Directions)
                    {
                        if (placed || layout.RoomAt(c + d) != b) continue;
                        layout.RegisterDoor(new Door { RoomA = a, RoomB = b, CellA = c, CellB = c + d });
                        placed = true;
                    }
                }
                Assert.IsTrue(placed, $"rooms {a} and {b} are not adjacent");
                layout.Rooms[a].Neighbors.Add(b);
                layout.Rooms[b].Neighbors.Add(a);
            }
            layout.Start = new Portal { Room = rooms.Length - 1 };
            layout.Exit = new Portal { Room = 1 };
            return layout;
        }

        [Test]
        public void AllRoomsReachableFromStart()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                Assert.IsTrue(layout.RoomDistance.All(d => d >= 0), $"seed {seed}: unreachable room");
            }
        }

        [Test]
        public void DoorsJoinAdjacentCellsOfDifferentRooms()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                foreach (var door in layout.Doors)
                {
                    Assert.AreEqual(1, Mathf.Abs(door.CellA.x - door.CellB.x) + Mathf.Abs(door.CellA.y - door.CellB.y), $"seed {seed}");
                    Assert.AreNotEqual(layout.RoomAt(door.CellA), layout.RoomAt(door.CellB), $"seed {seed}");
                    Assert.AreEqual(door.RoomA, layout.RoomAt(door.CellA));
                    Assert.AreEqual(door.RoomB, layout.RoomAt(door.CellB));
                }
            }
        }

        [Test]
        public void StartAndExitArePortalsOnTheOuterWallInDifferentRooms()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                Assert.AreNotEqual(layout.Start.Room, layout.Exit.Room, $"seed {seed}");
                foreach (var portal in new[] { layout.Start, layout.Exit })
                {
                    Assert.IsTrue(layout.InFootprint(portal.Cell), $"seed {seed}");
                    Assert.IsFalse(layout.InFootprint(portal.Cell + portal.Outward), $"seed {seed}");
                    Assert.AreEqual(portal.Room, layout.RoomAt(portal.Cell), $"seed {seed}");
                }
                // The two elevator cars stand in different outside cells.
                Assert.AreNotEqual(layout.Start.Cell + layout.Start.Outward, layout.Exit.Cell + layout.Exit.Outward, $"seed {seed}");
                Assert.GreaterOrEqual(layout.RoomDistance[layout.Exit.Room], 1, $"seed {seed}");
            }
        }

        [Test]
        public void EveryRoomGetsAHidingSpot()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                var slots = new SlotPool(layout, new Rng(seed));
                var spots = PlacementPlanner.PlanHidingSpots(layout, slots, new Rng(seed + 1));
                foreach (var room in layout.Rooms.Where(r => !r.IsHallway))
                {
                    Assert.IsTrue(spots.Any(s => s.Room == room.Id), $"seed {seed}: room {room.Id} ({room.Type}) has no hiding spot");
                }
            }
        }

        [Test]
        public void FloorHasRequiredObjectiveRooms()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                Assert.IsTrue(layout.Rooms.Any(r => r.Type == RoomType.DiningHall), $"seed {seed}");
                Assert.IsTrue(layout.Rooms.Any(r => r.Type == RoomType.Kitchen), $"seed {seed}");
                Assert.IsTrue(layout.Rooms.Any(r => r.Type == RoomType.ManagerOffice), $"seed {seed}");
                Assert.GreaterOrEqual(layout.Rooms.Count, 4, $"seed {seed}");
            }
        }

        [Test]
        public void DoorGraphHasAtLeastOneLoopForChaseEscapes()
        {
            foreach (var (seed, floor, count) in Cases())
            {
                var layout = LayoutGenerator.Generate(seed, floor, count);
                // A spanning tree has rooms-1 doors; anything above that is a loop.
                Assert.GreaterOrEqual(layout.Doors.Count, layout.Rooms.Count, $"seed {seed}: no loop in the door graph");
            }
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            foreach (var (seed, floor, count) in Cases().Take(100))
            {
                var a = LayoutGenerator.Generate(seed, floor, count);
                var b = LayoutGenerator.Generate(seed, floor, count);
                Assert.AreEqual(a.Rooms.Count, b.Rooms.Count);
                Assert.AreEqual(a.Doors.Count, b.Doors.Count);
                for (int i = 0; i < a.Doors.Count; i++)
                {
                    Assert.AreEqual(a.Doors[i].CellA, b.Doors[i].CellA);
                    Assert.AreEqual(a.Doors[i].CellB, b.Doors[i].CellB);
                }
                Assert.AreEqual(a.Start.Cell, b.Start.Cell);
                Assert.AreEqual(a.Exit.Cell, b.Exit.Cell);
            }
        }

        [Test]
        public void RunFloorCountIsBetweenThreeAndTen()
        {
            for (int s = 0; s < 1000; s++)
            {
                int floors = RunConfig.FloorCountForSeed(s);
                Assert.That(floors, Is.InRange(3, 10));
            }
        }
    }
}
