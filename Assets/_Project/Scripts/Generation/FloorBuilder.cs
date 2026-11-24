using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Interaction;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Turns a <see cref="FloorLayout"/> into scene geometry: rooms, walls with doorways, pillars,
    /// elevators, hiding spots, batteries and lights, then bakes the NavMesh.
    /// All solid geometry uses BoxColliders for clean, snag-free collisions.
    /// </summary>
    public static class FloorBuilder
    {
        public const float WallHeight = 3f;
        public const float WallThickness = 0.2f;
        public const float PillarSize = 0.3f;
        public const float DoorWidth = 1.6f;
        public const float DoorHeight = 2.3f;

        private const float CarDepth = 2.2f;
        private const float CarWidth = 2.2f;
        private const float CarHeight = 2.6f;

        /// <param name="populate">
        /// Optional callback that spawns gameplay content (objectives) before the NavMesh bake.
        /// It returns world positions that must be reachable; they are added to validation.
        /// </param>
        /// <param name="brokenLightChance">Chance that a room's ceiling light is dead (hallways +0.1; the arrival and exit rooms are always lit).</param>
        /// <param name="watcherCount">Bell-maid posts to reserve (<see cref="FloorInstance.WatcherPosts"/>).</param>
        public static FloorInstance Build(FloorLayout layout, Transform parent, int batteryCount,
            System.Func<FloorInstance, IEnumerable<Vector3>> populate = null, int giftCount = 0, float brokenLightChance = 0.25f,
            int watcherCount = 0)
        {
            var rng = new Rng(Rng.Combine(layout.Seed, 77));
            var root = new GameObject($"Floor_{layout.FloorIndex + 1}_seed{layout.Seed}");
            root.transform.SetParent(parent, false);

            var instance = new FloorInstance
            {
                Layout = layout,
                Root = root,
                Rng = rng,
                StaticRoot = new GameObject("Static").transform,
                DynamicRoot = new GameObject("Dynamic").transform
            };
            instance.StaticRoot.SetParent(root.transform, false);
            instance.DynamicRoot.SetParent(root.transform, false);

            BuildFloorsAndCeilings(layout, instance.StaticRoot);
            BuildWalls(layout, instance.StaticRoot);

            instance.StartCar = BuildElevator(layout.Start, ElevatorMode.Arrival, instance.DynamicRoot);
            instance.ExitCar = BuildElevator(layout.Exit, layout.IsFinalFloor ? ElevatorMode.FinalExit : ElevatorMode.Exit, instance.DynamicRoot);

            instance.Slots = new SlotPool(layout, rng);
            foreach (var p in PlacementPlanner.PlanHidingSpots(layout, instance.Slots, rng))
            {
                var spot = p.Kind == PlacementKind.Locker
                    ? BuildLocker(new WallSlot { Room = p.Room, Cell = p.Cell, Dir = p.WallDir }, instance.DynamicRoot)
                    : BuildHideTable(p.Cell, rng.Chance(0.5f) ? 0f : 90f, instance.DynamicRoot);
                instance.HidingSpots.Add(spot);
            }
            foreach (var p in PlacementPlanner.PlanBatteries(layout, instance.Slots, rng, batteryCount))
            {
                instance.Batteries.Add(BuildBattery(new WallSlot { Room = p.Room, Cell = p.Cell, Dir = p.WallDir }, instance.DynamicRoot));
            }

            BuildLights(layout, rng, instance, brokenLightChance);

            if (populate != null)
            {
                var targets = populate(instance);
                if (targets != null) instance.RequiredTargets.AddRange(targets);
            }

            // Gifts (sweets for the watcher maid) use their own RNG so lights/objectives stay identical per seed.
            if (giftCount > 0)
            {
                var giftRng = new Rng(Rng.Combine(layout.Seed, 4242));
                foreach (var p in PlacementPlanner.PlanBatteries(layout, instance.Slots, giftRng, giftCount))
                {
                    instance.Gifts.Add(BuildGift(new WallSlot { Room = p.Room, Cell = p.Cell, Dir = p.WallDir }, instance.DynamicRoot));
                }
            }

            // Bell-maid posts, reserved before the furniture: the decorator fills cell centres, and a wall slot in a cell
            // with a centre piece can no longer be taken (the posts used to be picked after it and were often missing).
            if (watcherCount > 0)
            {
                instance.WatcherPosts.AddRange(PlanWatcherPosts(instance, watcherCount, new Rng(Rng.Combine(layout.Seed, 515))));
            }

            // Furniture takes whatever slots are left, so it never overlaps gameplay objects. Own RNG: the rest of the
            // floor stays identical per seed. Placed before the NavMesh bake so maids path around it.
            FurnitureDecorator.Decorate(instance, new Rng(Rng.Combine(layout.Seed, 9001)));

            StaticBatchingUtility.Combine(instance.StaticRoot.gameObject);

            instance.Surface = BakeNavMesh(root);
            CollectPatrolPoints(instance);
            return instance;
        }

        // ------------------------------------------------------------------ geometry

        private static void BuildFloorsAndCeilings(FloorLayout layout, Transform parent)
        {
            float s = FloorLayout.CellSize;
            foreach (var room in layout.Rooms)
            {
                var r = room.Rect;
                var center = new Vector3(r.center.x * s, 0f, r.center.y * s);
                var size = new Vector3(r.width * s, 0.2f, r.height * s);
                var floor = Box(parent, $"Floor_{room.Id}_{room.Type}", center + Vector3.down * 0.1f, size, Palette.FloorFor(room.Type));
                // The only walkable geometry: everything else is baked as not walkable (see BakeNavMesh).
                var walkable = floor.AddComponent<NavMeshModifier>();
                walkable.overrideArea = true;
                walkable.area = NavAreas.Walkable;
                Box(parent, $"Ceiling_{room.Id}", center + Vector3.up * (WallHeight + 0.1f), size, Palette.Ceiling);
            }
        }

        private static void BuildWalls(FloorLayout layout, Transform parent)
        {
            var vertices = new HashSet<Vector2Int>();
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    int roomC = layout.CellRoom[x, y];
                    if (roomC < 0) continue; // outside the building's outline
                    foreach (var dir in FloorLayout.Directions)
                    {
                        var n = c + dir;
                        int roomN = layout.RoomAt(n);
                        if (roomN >= 0)
                        {
                            if (dir != Vector2Int.right && dir != Vector2Int.up) continue; // build shared edges once
                            if (roomN == roomC) continue;
                            BuildEdge(parent, c, dir,
                                Palette.WallFor(layout.Rooms[roomC].Type),
                                Palette.WallFor(layout.Rooms[roomN].Type),
                                layout.HasDoor(c, n));
                        }
                        else
                        {
                            BuildEdge(parent, c, dir, Palette.WallFor(layout.Rooms[roomC].Type), Palette.Exterior, layout.IsPortal(c, dir));
                        }
                        AddEdgeVertices(c, dir, vertices);
                    }
                }
            }

            foreach (var v in vertices)
            {
                var pos = new Vector3(v.x * FloorLayout.CellSize, WallHeight * 0.5f, v.y * FloorLayout.CellSize);
                Box(parent, "Pillar", pos, new Vector3(PillarSize, WallHeight, PillarSize), Palette.Trim);
            }
        }

        private static void AddEdgeVertices(Vector2Int c, Vector2Int dir, HashSet<Vector2Int> vertices)
        {
            if (dir == Vector2Int.right) { vertices.Add(new Vector2Int(c.x + 1, c.y)); vertices.Add(new Vector2Int(c.x + 1, c.y + 1)); }
            else if (dir == Vector2Int.left) { vertices.Add(new Vector2Int(c.x, c.y)); vertices.Add(new Vector2Int(c.x, c.y + 1)); }
            else if (dir == Vector2Int.up) { vertices.Add(new Vector2Int(c.x, c.y + 1)); vertices.Add(new Vector2Int(c.x + 1, c.y + 1)); }
            else { vertices.Add(new Vector2Int(c.x, c.y)); vertices.Add(new Vector2Int(c.x + 1, c.y)); }
        }

        /// <summary>
        /// Builds one wall edge as two half-thickness slabs (one per side, each with its room's material)
        /// with an optional centred doorway. Side A faces cell c.
        /// </summary>
        private static void BuildEdge(Transform parent, Vector2Int c, Vector2Int dir, Material matA, Material matB, bool opening)
        {
            var d3 = new Vector3(dir.x, 0f, dir.y);
            Vector3 center = FloorLayout.CellCenter(c) + d3 * (FloorLayout.CellSize * 0.5f);
            bool alongX = dir.y != 0;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            float length = FloorLayout.CellSize - PillarSize;
            float half = WallThickness * 0.5f;

            for (int side = 0; side < 2; side++)
            {
                Material mat = side == 0 ? matA : matB;
                Vector3 sc = center + (side == 0 ? -d3 : d3) * (WallThickness * 0.25f);
                if (!opening)
                {
                    WallBox(parent, sc + Vector3.up * (WallHeight * 0.5f), length, WallHeight, half, alongX, mat);
                    continue;
                }

                float seg = (length - DoorWidth) * 0.5f;
                float segOffset = DoorWidth * 0.5f + seg * 0.5f;
                WallBox(parent, sc - along * segOffset + Vector3.up * (WallHeight * 0.5f), seg, WallHeight, half, alongX, mat);
                WallBox(parent, sc + along * segOffset + Vector3.up * (WallHeight * 0.5f), seg, WallHeight, half, alongX, mat);
                float lintel = WallHeight - DoorHeight;
                WallBox(parent, sc + Vector3.up * (DoorHeight + lintel * 0.5f), DoorWidth, lintel, half, alongX, mat);
            }

            if (opening)
            {
                // Decorative frame (no collider so it never narrows the walkable opening).
                float depth = WallThickness + 0.04f;
                Vector3 frameL = center - along * (DoorWidth * 0.5f) + Vector3.up * (DoorHeight * 0.5f);
                Vector3 frameR = center + along * (DoorWidth * 0.5f) + Vector3.up * (DoorHeight * 0.5f);
                WallBox(parent, frameL, 0.08f, DoorHeight, depth, alongX, Palette.Trim, collider: false);
                WallBox(parent, frameR, 0.08f, DoorHeight, depth, alongX, Palette.Trim, collider: false);
                WallBox(parent, center + Vector3.up * (DoorHeight + 0.04f), DoorWidth + 0.16f, 0.08f, depth, alongX, Palette.Trim, collider: false);
            }
        }

        private static void WallBox(Transform parent, Vector3 center, float length, float height, float thickness, bool alongX,
            Material mat, bool collider = true)
        {
            var size = alongX ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length);
            Box(parent, "Wall", center, size, mat, collider: collider);
        }

        // ------------------------------------------------------------------ elevators

        private static ElevatorCar BuildElevator(Portal portal, ElevatorMode mode, Transform parent)
        {
            var outward = new Vector3(portal.Outward.x, 0f, portal.Outward.y);
            Vector3 edge = FloorLayout.CellCenter(portal.Cell) + outward * (FloorLayout.CellSize * 0.5f);

            var root = new GameObject($"Elevator_{mode}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(edge, Quaternion.LookRotation(-outward));
            var t = root.transform;

            // Local space: +Z points into the building; the car sits behind the wall (negative Z).
            float zc = -WallThickness * 0.5f - CarDepth * 0.5f;
            int noNav = Layers.NoNavMesh;
            LocalBox(t, "CarFloor", new Vector3(0f, -0.1f, zc), new Vector3(CarWidth + 0.2f, 0.2f, CarDepth), Palette.CarFloor, noNav);
            LocalBox(t, "CarCeiling", new Vector3(0f, CarHeight + 0.05f, zc), new Vector3(CarWidth + 0.2f, 0.1f, CarDepth), Palette.Steel, noNav);
            LocalBox(t, "CarWallL", new Vector3(-(CarWidth * 0.5f + 0.05f), CarHeight * 0.5f, zc), new Vector3(0.1f, CarHeight, CarDepth), Palette.Steel, noNav);
            LocalBox(t, "CarWallR", new Vector3(CarWidth * 0.5f + 0.05f, CarHeight * 0.5f, zc), new Vector3(0.1f, CarHeight, CarDepth), Palette.Steel, noNav);
            LocalBox(t, "CarBack", new Vector3(0f, CarHeight * 0.5f, -WallThickness * 0.5f - CarDepth - 0.05f), new Vector3(CarWidth + 0.2f, CarHeight, 0.1f), Palette.Steel, noNav);

            // Sliding doors on the room-side face of the wall.
            float panelWidth = DoorWidth * 0.5f + 0.05f;
            var left = LocalBox(t, "DoorL", new Vector3(-DoorWidth * 0.25f, DoorHeight * 0.5f, WallThickness * 0.5f + 0.03f),
                new Vector3(panelWidth, DoorHeight, 0.05f), Palette.Steel, noNav);
            var right = LocalBox(t, "DoorR", new Vector3(DoorWidth * 0.25f, DoorHeight * 0.5f, WallThickness * 0.5f + 0.036f),
                new Vector3(panelWidth, DoorHeight, 0.05f), Palette.Steel, noNav);

            // Sign above the doors.
            var signMat = mode == ElevatorMode.Arrival ? Palette.SignOff : mode == ElevatorMode.FinalExit ? Palette.SignExit : Palette.SignElevator;
            LocalBox(t, "Sign", new Vector3(0f, DoorHeight + 0.3f, WallThickness * 0.5f + 0.03f), new Vector3(0.7f, 0.2f, 0.03f), signMat, noNav, collider: false);

            // Interior light.
            var lightGo = new GameObject("CarLight");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = new Vector3(0f, CarHeight - 0.2f, zc);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 1.4f;
            light.color = new Color(0.85f, 0.92f, 1f);
            light.shadows = LightShadows.None;

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(t, false);
            spawn.localPosition = new Vector3(0f, 0.05f, zc + 0.2f);
            spawn.localRotation = Quaternion.identity;

            var car = root.AddComponent<ElevatorCar>();
            car.Configure(mode, left.transform, right.transform, DoorWidth * 0.25f, DoorWidth * 0.5f + panelWidth * 0.5f,
                new Vector3(0f, 1.2f, zc), new Vector3(CarWidth - 0.3f, 2.6f, CarDepth - 0.3f), spawn, light);
            return car;
        }

        // ------------------------------------------------------------------ bell-maid posts

        /// <summary>Minimum distance (m) from the arrival elevator to a bell maid's room centre.</summary>
        public const float WatcherMinSpawnDistance = 9f;

        /// <summary>
        /// Takes up to <paramref name="count"/> wall slots for bell-maid posts, one per room: not the arrival room, not a
        /// hallway, room centre at least <see cref="WatcherMinSpawnDistance"/> from the arrival elevator.
        /// </summary>
        private static List<WallSlot> PlanWatcherPosts(FloorInstance instance, int count, Rng rng)
        {
            var layout = instance.Layout;
            Vector3 spawn = instance.SpawnPosition;
            var rooms = new List<int>();
            foreach (int room in instance.Slots.RoomsWithWallSlots())
            {
                if (room == layout.Start.Room || layout.Rooms[room].IsHallway) continue;
                if (Vector3.Distance(instance.RoomCenter(room), spawn) < WatcherMinSpawnDistance) continue;
                rooms.Add(room);
            }
            rng.Shuffle(rooms);

            var posts = new List<WallSlot>();
            foreach (int room in rooms)
            {
                if (posts.Count >= count) break;
                if (instance.Slots.TryTakeWall(room, out var slot)) posts.Add(slot);
            }
            return posts;
        }

        // ------------------------------------------------------------------ hiding spots

        private static HidingSpot BuildLocker(WallSlot slot, Transform parent)
        {
            const float w = 0.9f, h = 2.1f, depth = 0.65f, th = 0.04f;
            const float slitBottom = 1.55f, slitTop = 1.68f;

            var d3 = new Vector3(slot.Dir.x, 0f, slot.Dir.y);
            Vector3 pos = FloorLayout.CellCenter(slot.Cell) + d3 * (FloorLayout.CellSize * 0.5f - WallThickness * 0.5f - depth * 0.5f - 0.02f);
            var root = new GameObject("Locker");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-d3));
            var t = root.transform;
            var mat = Palette.Locker;

            LocalBox(t, "Back", new Vector3(0f, h * 0.5f, -depth * 0.5f + th * 0.5f), new Vector3(w, h, th), mat);
            LocalBox(t, "SideL", new Vector3(-(w * 0.5f - th * 0.5f), h * 0.5f, 0f), new Vector3(th, h, depth), mat);
            LocalBox(t, "SideR", new Vector3(w * 0.5f - th * 0.5f, h * 0.5f, 0f), new Vector3(th, h, depth), mat);
            LocalBox(t, "Top", new Vector3(0f, h - th * 0.5f, 0f), new Vector3(w, th, depth), mat);
            LocalBox(t, "Bottom", new Vector3(0f, th * 0.5f, 0f), new Vector3(w, th, depth), mat);

            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(t, false);
            hinge.localPosition = new Vector3(-w * 0.5f, 0f, depth * 0.5f - 0.015f);
            LocalBox(hinge, "DoorLower", new Vector3(w * 0.5f, slitBottom * 0.5f, 0f), new Vector3(w, slitBottom, 0.03f), Palette.LockerDoor);
            LocalBox(hinge, "DoorUpper", new Vector3(w * 0.5f, (slitTop + h) * 0.5f, 0f), new Vector3(w, h - slitTop, 0.03f), Palette.LockerDoor);

            var hide = new GameObject("HidePoint").transform;
            hide.SetParent(t, false);
            hide.localPosition = new Vector3(0f, 0f, -0.03f);
            var exit = new GameObject("ExitPoint").transform;
            exit.SetParent(t, false);
            exit.localPosition = new Vector3(0f, 0f, depth * 0.5f + 0.65f);

            var spot = root.AddComponent<HidingSpot>();
            spot.Configure(HidingKind.Locker, hide, exit, hinge, 0f, 35f, -20f, 20f);
            return spot;
        }

        private static HidingSpot BuildHideTable(Vector2Int cell, float yaw, Transform parent)
        {
            var root = new GameObject("ClothTable");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(FloorLayout.CellCenter(cell), Quaternion.Euler(0f, yaw, 0f));
            var t = root.transform;

            LocalBox(t, "Top", new Vector3(0f, 0.765f, 0f), new Vector3(1.9f, 0.05f, 1.1f), Palette.Wood);
            foreach (var sx in new[] { -0.85f, 0.85f })
            foreach (var sz in new[] { -0.45f, 0.45f })
            {
                LocalBox(t, "Leg", new Vector3(sx, 0.37f, sz), new Vector3(0.06f, 0.74f, 0.06f), Palette.Wood);
            }
            LocalBox(t, "ClothFront", new Vector3(0f, 0.52f, 0.56f), new Vector3(1.92f, 0.44f, 0.02f), Palette.Cloth);
            LocalBox(t, "ClothBack", new Vector3(0f, 0.52f, -0.56f), new Vector3(1.92f, 0.44f, 0.02f), Palette.Cloth);
            LocalBox(t, "ClothL", new Vector3(-0.96f, 0.52f, 0f), new Vector3(0.02f, 0.44f, 1.12f), Palette.Cloth);
            LocalBox(t, "ClothR", new Vector3(0.96f, 0.52f, 0f), new Vector3(0.02f, 0.44f, 1.12f), Palette.Cloth);

            var hide = new GameObject("HidePoint").transform;
            hide.SetParent(t, false);
            // One exit per side, ~0.6 m clear of the cloth; the front one is the default (validated, used by the maids).
            Transform Exit(string name, Vector3 local)
            {
                var e = new GameObject(name).transform;
                e.SetParent(t, false);
                e.localPosition = local;
                return e;
            }
            var exit = Exit("ExitPoint", new Vector3(0f, 0f, 1.15f));

            var spot = root.AddComponent<HidingSpot>();
            // Full turn under the table (yaw range 180), pitch limited by the table top and the floor.
            spot.Configure(HidingKind.UnderTable, hide, exit, null, 0.22f, 180f, -15f, 10f);
            spot.AddExit(Exit("ExitPointBack", new Vector3(0f, 0f, -1.15f)));
            spot.AddExit(Exit("ExitPointLeft", new Vector3(-1.55f, 0f, 0f)));
            spot.AddExit(Exit("ExitPointRight", new Vector3(1.55f, 0f, 0f)));
            return spot;
        }

        // ------------------------------------------------------------------ pickups

        /// <summary>Nightstand models used as item stands (picked per slot, so a floor looks the same for a seed).</summary>
        private static readonly string[] StandModels = { "NightstandA", "NightstandB" };

        /// <summary>
        /// A small stand against the slot's wall (a nightstand model, or a greybox cabinet when the models are missing);
        /// returns the centre of its top. Used for batteries, gifts and the objective items.
        /// </summary>
        public static Vector3 BuildItemStand(WallSlot slot, Transform parent)
        {
            var d3 = new Vector3(slot.Dir.x, 0f, slot.Dir.y);
            var rotation = Quaternion.LookRotation(-d3);
            int pick = ((slot.Cell.x * 31 + slot.Cell.y) * 31 + slot.Dir.x * 3 + slot.Dir.y) & 0x7fffffff;
            string model = StandModels[pick % StandModels.Length];
            Vector3 size = PropLibrary.Size(model);
            if (size != Vector3.zero)
            {
                Vector3 bottom = FloorLayout.CellCenter(slot.Cell) + d3 * (FloorLayout.CellSize * 0.5f - WallThickness * 0.5f - size.z * 0.5f - 0.02f);
                PropLibrary.Solid(model, parent, "Stand", parent.InverseTransformPoint(bottom), Quaternion.Inverse(parent.rotation) * rotation);
                return bottom + Vector3.up * size.y;
            }

            const float depth = 0.45f, height = 0.9f;
            Vector3 basePos = FloorLayout.CellCenter(slot.Cell) + d3 * (FloorLayout.CellSize * 0.5f - WallThickness * 0.5f - depth * 0.5f - 0.02f);
            Box(parent, "Cabinet", basePos + Vector3.up * (height * 0.5f), new Vector3(0.8f, height, depth), Palette.Wood,
                rotation: rotation);
            return basePos + Vector3.up * height;
        }

        /// <summary>
        /// Trigger root of a pickup resting on <paramref name="top"/>, with the model as its visual (or the greybox
        /// visual built by <paramref name="fallback"/> when the model is missing). The root sits at the visual's centre
        /// (pickups spin about it).
        /// </summary>
        public static GameObject PickupRoot(string name, Transform parent, Vector3 top, string model, float scale,
            Vector3 fallbackSize, System.Action<Transform> fallback)
        {
            var root = new GameObject(name);
            root.layer = Layers.NoNavMesh;
            root.transform.SetParent(parent, false);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;

            Vector3 size = PropLibrary.Size(model) * scale;
            if (size != Vector3.zero)
            {
                root.transform.position = top + Vector3.up * (size.y * 0.5f + 0.01f);
                PropLibrary.Visual(model, root.transform, new Vector3(0f, -size.y * 0.5f, 0f), Quaternion.identity, scale);
            }
            else
            {
                size = fallbackSize;
                root.transform.position = top + Vector3.up * (size.y * 0.5f + 0.02f);
                fallback(root.transform);
            }
            trigger.size = Vector3.Max(size + Vector3.one * 0.12f, Vector3.one * 0.3f);
            return root;
        }

        private static Pickup BuildBattery(WallSlot slot, Transform parent)
        {
            Vector3 top = BuildItemStand(slot, parent);

            // AA cells, drawn a little larger than life so they can be spotted on the stands.
            var root = PickupRoot("Battery", parent, top, "Batteries", 1.5f, new Vector3(0.07f, 0.12f, 0.07f), t =>
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Visual";
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.layer = Layers.NoNavMesh;
                visual.transform.SetParent(t, false);
                visual.transform.localScale = new Vector3(0.07f, 0.06f, 0.07f);
                visual.GetComponent<MeshRenderer>().sharedMaterial = Palette.Battery;
            });

            var pickup = root.AddComponent<Pickup>();
            pickup.Configure(PickupKind.Battery, "battery", "object.battery");
            return pickup;
        }

        /// <summary>A wrapped box of sweets on a cabinet: the gift that pacifies the watcher maid.</summary>
        private static Pickup BuildGift(WallSlot slot, Transform parent)
        {
            Vector3 top = BuildItemStand(slot, parent);

            // An open heart-shaped box of chocolates.
            var root = PickupRoot("Sweets", parent, top, "Chocolates", 1f, new Vector3(0.17f, 0.095f, 0.17f), t =>
            {
                GiftPart(t, "Box", new Vector3(0.16f, 0.09f, 0.16f), Palette.GiftBox);
                GiftPart(t, "RibbonA", new Vector3(0.17f, 0.095f, 0.025f), Palette.GiftRibbon);
                GiftPart(t, "RibbonB", new Vector3(0.025f, 0.095f, 0.17f), Palette.GiftRibbon);
            });

            var pickup = root.AddComponent<Pickup>();
            pickup.Configure(PickupKind.Gift, "gift", "object.sweets");
            return pickup;
        }

        private static void GiftPart(Transform parent, string name, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.layer = Layers.NoNavMesh;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ lights

        private static void BuildLights(FloorLayout layout, Rng rng, FloorInstance instance, float brokenChance)
        {
            foreach (var room in layout.Rooms)
            {
                bool important = room.Id == layout.Start.Room || room.Id == layout.Exit.Room;
                float chance = Mathf.Clamp(brokenChance + (room.IsHallway ? 0.1f : 0f), 0f, 0.85f);
                bool broken = !important && rng.Chance(chance);
                bool flicker = !broken && rng.Chance(0.35f);

                var r = room.Rect;
                var pos = new Vector3(r.center.x * FloorLayout.CellSize, WallHeight - 0.05f, r.center.y * FloorLayout.CellSize);
                var lamp = Box(instance.DynamicRoot, "Lamp", pos, new Vector3(0.6f, 0.06f, 0.6f), broken ? Palette.LampOff : Palette.LampOn, collider: false);
                lamp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (broken) continue;

                var go = new GameObject("RoomLight");
                go.transform.SetParent(instance.DynamicRoot, false);
                go.transform.position = pos + Vector3.down * 0.3f;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = Mathf.Max(r.width, r.height) * FloorLayout.CellSize * 0.75f + 2f;
                light.intensity = room.IsHallway ? 1.1f : 1.5f;
                light.color = new Color(1f, 0.82f, 0.68f);
                light.shadows = LightShadows.None;
                instance.RoomLights.Add(light);

                if (flicker)
                {
                    var f = go.AddComponent<FlickerLight>();
                    f.SetLamp(lamp.GetComponent<Renderer>(), Palette.LampEmission);
                }
            }
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>
        /// Bakes the floor's NavMesh. Only the floor slabs are walkable (their NavMeshModifier); every other collider
        /// (furniture, hiding spots, stands, objective props, ceilings) is an obstacle whose top is never walkable, so
        /// maids cannot climb onto tables or the hiding table. Small furniture is left out of the bake and becomes the
        /// <see cref="NavAreas.Furniture"/> area (<see cref="FurnitureDecorator"/>). The agent's step height
        /// (ProjectSettings/NavMeshAreas, 0.25 m) is low for the same reason: Recast would otherwise turn low
        /// not-walkable spans (the hiding table's cloth) back into walkable ledges.
        /// </summary>
        private static NavMeshSurface BakeNavMesh(GameObject root)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.defaultArea = NavAreas.NotWalkable;
            surface.layerMask = ~((1 << Layers.NoNavMesh) | (1 << Layers.MaidBody));
            surface.BuildNavMesh();
            return surface;
        }

        private static void CollectPatrolPoints(FloorInstance instance)
        {
            var layout = instance.Layout;
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (layout.CellRoom[x, y] < 0) continue; // outside the building's outline
                    if (NavMesh.SamplePosition(FloorLayout.CellCenter(c), out var hit, 1.8f, NavAreas.Ground))
                    {
                        instance.PatrolPoints.Add(hit.position);
                        instance.PatrolPointRooms.Add(layout.CellRoom[x, y]);
                    }
                }
            }
        }

        /// <summary>
        /// Checks that every important point is reachable on the NavMesh from the arrival elevator, on the ground the
        /// player can walk (without the furniture area, which is solid for them).
        /// </summary>
        public static List<string> Validate(FloorInstance instance)
        {
            var errors = new List<string>();
            if (!NavMesh.SamplePosition(FloorInstance.PortalFront(instance.Layout.Start), out var startHit, 1.5f, NavAreas.Ground))
            {
                errors.Add("Start position is not on the NavMesh");
                return errors;
            }

            var path = new NavMeshPath();
            void Check(string label, Vector3 target, float radius)
            {
                if (!NavMesh.SamplePosition(target, out var hit, radius, NavAreas.Ground))
                {
                    errors.Add($"{label}: no NavMesh near {target}");
                    return;
                }
                if (!NavMesh.CalculatePath(startHit.position, hit.position, NavAreas.Ground, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    errors.Add($"{label}: unreachable ({path.status})");
                }
            }

            Check("Exit", FloorInstance.PortalFront(instance.Layout.Exit), 1.5f);
            foreach (var spot in instance.HidingSpots) Check($"Hiding spot {spot.name}", spot.ExitPosition, 1.2f);
            foreach (var battery in instance.Batteries) Check("Battery", battery.transform.position, 1.6f);
            foreach (var gift in instance.Gifts) Check("Gift", gift.transform.position, 1.6f);
            for (int i = 0; i < instance.RequiredTargets.Count; i++) Check($"Objective target {i}", instance.RequiredTargets[i], 1.8f);
            if (instance.PatrolPoints.Count < instance.Layout.Area / 2)
            {
                errors.Add($"Too few patrol points ({instance.PatrolPoints.Count})");
            }
            return errors;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A box of <paramref name="size"/> metres; textured materials get metre UVs (<see cref="SurfaceMapping"/>).</summary>
        public static GameObject Box(Transform parent, string name, Vector3 worldCenter, Vector3 size, Material mat,
            int layer = 0, bool collider = true, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;
            if (rotation.HasValue) go.transform.rotation = rotation.Value;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            SurfaceMapping.Apply(go, mat);
            return go;
        }

        public static GameObject LocalBox(Transform parent, string name, Vector3 localCenter, Vector3 size, Material mat,
            int layer = 0, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            SurfaceMapping.Apply(go, mat);
            return go;
        }

        /// <summary>
        /// Surface palette: a maid café that used to be cute and has been rotting for years. Textured sets come from
        /// <c>Resources/Surfaces</c> (Poly Haven CC0 maps and the project's own aged wallpapers, see
        /// <c>ArtSource/Work/build_surfaces.py</c>); each entry keeps a flat greybox colour as fallback.
        /// </summary>
        public static class Palette
        {
            public static Material WallFor(RoomType type)
            {
                switch (type)
                {
                    // Rose striped wallpaper with hearts, peeling.
                    case RoomType.DiningHall: return MaterialLibrary.Surface("Wall_Dining", "WallpaperRose", Color.white, new Color(0.72f, 0.5f, 0.56f));
                    // Institutional green paint flaking off concrete.
                    case RoomType.Kitchen: return MaterialLibrary.Surface("Wall_Kitchen", "WallPaintedConcrete", Color.white, new Color(0.78f, 0.8f, 0.76f), 0.5f);
                    case RoomType.Storage: return MaterialLibrary.Surface("Wall_Storage", "WallBrickPainted", new Color(0.86f, 0.86f, 0.9f), new Color(0.55f, 0.5f, 0.44f));
                    case RoomType.ManagerOffice: return MaterialLibrary.Surface("Wall_Office", "WallPanels", new Color(0.8f, 0.74f, 0.74f), new Color(0.32f, 0.42f, 0.36f));
                    // Lilac wallpaper with stars.
                    case RoomType.StaffRoom: return MaterialLibrary.Surface("Wall_Staff", "WallpaperLilac", Color.white, new Color(0.6f, 0.55f, 0.7f));
                    case RoomType.Restroom: return MaterialLibrary.Surface("Wall_Restroom", "WallPlasterTeal", Color.white, new Color(0.6f, 0.78f, 0.72f), 0.55f);
                    case RoomType.Stage: return MaterialLibrary.Surface("Wall_Stage", "WallVelvet", new Color(0.72f, 0.56f, 0.6f), new Color(0.45f, 0.12f, 0.16f));
                    // Hallways: cream and mint wallpaper with pink dots.
                    default: return MaterialLibrary.Surface("Wall_Hallway", "WallpaperCream", Color.white, new Color(0.82f, 0.74f, 0.66f));
                }
            }

            public static Material FloorFor(RoomType type)
            {
                switch (type)
                {
                    case RoomType.DiningHall:
                        return MaterialLibrary.Surface("Floor_Dining", "FloorCheckerRose", Color.white, new Color(0.62f, 0.5f, 0.52f), 0.45f);
                    case RoomType.Kitchen:
                        return MaterialLibrary.Surface("Floor_Kitchen", "FloorCheckerDiner", Color.white, new Color(0.62f, 0.62f, 0.6f), 0.55f);
                    case RoomType.Restroom:
                        return MaterialLibrary.Surface("Floor_Restroom", "FloorMosaicMint", Color.white, new Color(0.62f, 0.62f, 0.6f), 0.55f);
                    case RoomType.Storage:
                        return MaterialLibrary.Surface("Floor_Storage", "FloorConcrete", Color.white, new Color(0.38f, 0.37f, 0.35f), 0.1f);
                    case RoomType.Stage:
                        return MaterialLibrary.Surface("Floor_Stage", "FloorStage", Color.white, new Color(0.28f, 0.16f, 0.1f), 0.4f);
                    case RoomType.ManagerOffice:
                        return MaterialLibrary.Surface("Floor_Office", "FloorParquet", new Color(0.72f, 0.64f, 0.58f), new Color(0.4f, 0.27f, 0.18f), 0.35f);
                    default:
                        return MaterialLibrary.Surface("Floor_Wood", "FloorWoodWorn", new Color(0.86f, 0.84f, 0.82f), new Color(0.4f, 0.27f, 0.18f), 0.35f);
                }
            }

            public static Material Ceiling => MaterialLibrary.Surface("Ceiling", "CeilingStained", new Color(0.82f, 0.8f, 0.78f), new Color(0.7f, 0.68f, 0.65f), 0.1f);
            public static Material Exterior => MaterialLibrary.Get("Exterior", new Color(0.2f, 0.2f, 0.22f));
            public static Material Trim => MaterialLibrary.Surface("Trim", "TrimPainted", Color.white, new Color(0.92f, 0.88f, 0.84f), 0.4f);
            public static Material Steel => MaterialLibrary.Surface("Steel", "MetalSheetRusty", new Color(0.86f, 0.88f, 0.9f), new Color(0.55f, 0.57f, 0.6f), 0.75f);
            public static Material CarFloor => MaterialLibrary.Surface("CarFloor", "MetalTread", new Color(0.75f, 0.75f, 0.75f), new Color(0.25f, 0.25f, 0.27f), 0.3f);
            // Pastel lockers rusting through: baby-blue bodies, pink doors.
            public static Material Locker => MaterialLibrary.Surface("Locker", "MetalRustyPaint", new Color(0.7f, 0.82f, 0.92f), new Color(0.55f, 0.62f, 0.66f), 0.55f);
            public static Material LockerDoor => MaterialLibrary.Surface("LockerDoor", "MetalRustyPaint", new Color(0.96f, 0.74f, 0.82f), new Color(0.62f, 0.68f, 0.72f), 0.55f);
            public static Material Wood => MaterialLibrary.Surface("Wood", "WoodWorn", Color.white, new Color(0.36f, 0.22f, 0.14f), 0.35f);
            // Pink gingham table cloths with old tea stains.
            public static Material Cloth => MaterialLibrary.Surface("Cloth", "GinghamRose", Color.white, new Color(0.88f, 0.8f, 0.84f), 0.05f);
            public static Material Battery => MaterialLibrary.Get("Battery", new Color(0.2f, 0.5f, 0.25f), 0.6f, new Color(0.05f, 0.35f, 0.08f));
            public static Material GiftBox => MaterialLibrary.Get("GiftBox", new Color(0.95f, 0.45f, 0.65f), 0.4f, new Color(0.35f, 0.08f, 0.18f));
            public static Material GiftRibbon => MaterialLibrary.Get("GiftRibbon", new Color(0.97f, 0.95f, 0.9f), 0.5f);
            public static Color LampEmission => new Color(1.6f, 1.3f, 1.0f);
            public static Material LampOn => MaterialLibrary.Get("LampOn", Color.white, 0.2f, LampEmission);
            public static Material LampOff => MaterialLibrary.Get("LampOff", new Color(0.3f, 0.3f, 0.3f), 0.2f);
            public static Material SignOff => MaterialLibrary.Get("SignOff", new Color(0.1f, 0.1f, 0.1f));
            public static Material SignElevator => MaterialLibrary.Get("SignElevator", new Color(0.4f, 0.1f, 0.1f), 0.3f, new Color(1.2f, 0.2f, 0.15f));
            public static Material SignExit => MaterialLibrary.Get("SignExit", new Color(0.1f, 0.4f, 0.15f), 0.3f, new Color(0.2f, 1.4f, 0.3f));
        }

        public static void ApplyAtmosphere()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.03f, 0.04f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.01f, 0.008f, 0.012f);
            RenderSettings.fogDensity = 0.055f;
        }
    }
}
