using System.Collections.Generic;
using MaidCafe.Core;
using Unity.AI.Navigation;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Furnishes a generated floor with the Kenney Furniture Kit (CC0, Resources/KenneyFurniture).
    /// Every room gets a theme (dining room, kitchen, lounge or storage; hallways only get a few small pieces) and
    /// fills the wall and centre slots left free in the <see cref="SlotPool"/> after hiding spots, batteries,
    /// objective props and gifts, so furniture never overlaps them, a doorway or an elevator.
    /// Pieces are solid (one box collider each, on the Default layer): they block sight and give cover. Large pieces are
    /// NavMesh obstacles that no maid walks on. Small pieces (<see cref="ClimbablePieces"/>) are left out of the bake:
    /// their footprint plus <see cref="ZoneMargin"/> becomes the <see cref="NavAreas.Furniture"/> area, which only
    /// chasing maids cross, climbing over the piece. Runs before the bake; <see cref="FloorBuilder.Validate"/> still
    /// checks every path afterwards (without the furniture area, so the player can always get through).
    /// </summary>
    public static class FurnitureDecorator
    {
        /// <summary>Kenney models are authored at 1 unit = 20 cm; slightly larger fits the café scale.</summary>
        private const float ModelScale = 0.21f;

        /// <summary>Wall runs stay within this half-span of the wall's centre so they never reach the corners
        /// (where a deep piece on the perpendicular wall may stand).</summary>
        private const float WallHalfSpan = 0.95f;

        private const float WallGap = 0.03f;

        /// <summary>Small pieces a chasing maid may climb over; everything else is always walked around.</summary>
        private static readonly HashSet<string> ClimbablePieces = new HashSet<string>
        {
            "chair", "chairCushion", "chairRounded", "stoolBar", "cardboardBoxClosed", "cardboardBoxOpen",
            "tableCoffee", "sideTable", "trashcan", "bench"
        };

        /// <summary>A climbable piece taller than this above the floor (e.g. a box stacked on another) stays an obstacle.</summary>
        private const float MaxClimbHeight = 1.05f;

        /// <summary>
        /// Clearance around a climbable piece's footprint inside the furniture area, so maids that avoid the area keep
        /// their body (agent radius 0.35 m) out of the piece, like the 0.5 m erosion around baked obstacles.
        /// </summary>
        private const float ZoneMargin = 0.45f;

        private enum Theme { Dining, Kitchen, Lounge, Storage, Hallway }

        private struct ModelInfo
        {
            public GameObject Prefab;
            public Bounds LocalBounds; // in model units, relative to the prefab root
        }

        private static readonly Dictionary<string, ModelInfo> Models = new Dictionary<string, ModelInfo>();
        private static readonly Dictionary<Material, Material> Faded = new Dictionary<Material, Material>();

        private static readonly string[] Tables = { "table", "tableCloth", "tableRound", "tableCross", "tableCrossCloth" };
        private static readonly string[] Chairs = { "chair", "chairCushion", "chairRounded" };
        private static readonly string[] KitchenUnits = { "kitchenCabinet", "kitchenCabinetDrawer", "kitchenSink", "kitchenStove", "kitchenCabinet" };

        public static void Decorate(FloorInstance instance, Rng rng)
        {
            var layout = instance.Layout;
            var slots = instance.Slots;
            var root = new GameObject("Furniture").transform;
            root.SetParent(instance.StaticRoot, false);

            foreach (var room in layout.Rooms)
            {
                Theme theme = room.IsHallway ? Theme.Hallway : PickTheme(rng);
                float wallFill, centerFill;
                switch (theme)
                {
                    case Theme.Dining: wallFill = 0.45f; centerFill = 0.8f; break;
                    case Theme.Kitchen: wallFill = 0.75f; centerFill = 0.5f; break;
                    case Theme.Lounge: wallFill = 0.5f; centerFill = 0.6f; break;
                    case Theme.Storage: wallFill = 0.65f; centerFill = 0.5f; break;
                    default: wallFill = 0.2f; centerFill = 0f; break;
                }

                int centers = Mathf.RoundToInt(slots.CenterCount(room.Id) * centerFill);
                for (int i = 0; i < centers && slots.TryTakeCenter(room.Id, out var cell); i++)
                {
                    PlaceCenterSet(theme, FloorLayout.CellCenter(cell), rng, root);
                }

                int walls = Mathf.RoundToInt(slots.WallCount(room.Id) * wallFill);
                for (int i = 0; i < walls && slots.TryTakeWall(room.Id, out var slot); i++)
                {
                    PlaceWallSet(theme, slot, rng, root);
                }
            }
        }

        private static Theme PickTheme(Rng rng)
        {
            float r = rng.Range(0f, 1f);
            if (r < 0.5f) return Theme.Dining;
            if (r < 0.7f) return Theme.Kitchen;
            if (r < 0.85f) return Theme.Lounge;
            return Theme.Storage;
        }

        // ------------------------------------------------------------------ sets

        private static void PlaceCenterSet(Theme theme, Vector3 center, Rng rng, Transform root)
        {
            float yaw = rng.Range(0, 4) * 90f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 jitter = new Vector3(rng.Range(-0.3f, 0.3f), 0f, rng.Range(-0.3f, 0.3f));
            Vector3 c = center + jitter;
            switch (theme)
            {
                case Theme.Dining:
                {
                    string table = rng.Pick(Tables);
                    var t = Place(table, c, rot, root);
                    if (t == null) return;
                    Vector3 half = t.Value; // footprint half size (x along table width, z along depth)
                    string chair = rng.Pick(Chairs);
                    // Chairs on the long sides (and sometimes the ends), facing the table.
                    var seats = new List<(Vector3 offset, float face)>
                    {
                        (new Vector3(0f, 0f, -(half.z + 0.3f)), 0f),
                        (new Vector3(0f, 0f, half.z + 0.3f), 180f)
                    };
                    if (half.x > 0.6f)
                    {
                        seats.Add((new Vector3(-(half.x + 0.3f), 0f, 0f), 90f));
                        seats.Add((new Vector3(half.x + 0.3f, 0f, 0f), -90f));
                    }
                    foreach (var (offset, face) in seats)
                    {
                        if (!rng.Chance(0.75f)) continue;
                        // The chairs' backrests are at local +z, so a chair facing the table looks along -offset.
                        var chairRot = rot * Quaternion.Euler(0f, face + 180f + rng.Range(-12f, 12f), 0f);
                        Vector3 pos = c + rot * offset;
                        if (rng.Chance(0.15f))
                        {
                            // Knocked over: on its back, pushed away from the table.
                            Place(chair, pos + rot * offset.normalized * 0.35f, chairRot * Quaternion.Euler(-90f, 0f, 0f), root);
                        }
                        else
                        {
                            Place(chair, pos, chairRot, root);
                        }
                    }
                    break;
                }
                case Theme.Kitchen:
                {
                    // Bar island with stools on one side.
                    var bar = Place("kitchenBar", c, rot, root);
                    if (bar == null) return;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (!rng.Chance(0.8f)) continue;
                        Place("stoolBar", c + rot * new Vector3(s * 0.45f, 0f, -(bar.Value.z + 0.35f)), rot * Quaternion.Euler(0f, rng.Range(-20f, 20f), 0f), root);
                    }
                    break;
                }
                case Theme.Lounge:
                {
                    Place("rugRound", c, rot, root, solid: false);
                    var coffee = Place("tableCoffee", c, rot, root);
                    if (coffee == null) return;
                    // Two armchairs facing the coffee table from opposite sides.
                    Place("loungeChair", c + rot * new Vector3(0f, 0f, -(coffee.Value.z + 0.6f)), rot * Quaternion.Euler(0f, 180f, 0f), root);
                    if (rng.Chance(0.6f)) Place("loungeChair", c + rot * new Vector3(0f, 0f, coffee.Value.z + 0.6f), rot, root);
                    break;
                }
                case Theme.Storage:
                {
                    int boxes = rng.Range(3, 6);
                    for (int b = 0; b < boxes; b++)
                    {
                        Vector3 p = c + new Vector3(rng.Range(-0.8f, 0.8f), 0f, rng.Range(-0.8f, 0.8f));
                        var boxRot = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                        string model = rng.Chance(0.3f) ? "cardboardBoxOpen" : "cardboardBoxClosed";
                        var size = Place(model, p, boxRot, root);
                        if (size != null && rng.Chance(0.35f))
                        {
                            Place("cardboardBoxClosed", p + Vector3.up * (size.Value.y * 2f), Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), root);
                        }
                    }
                    break;
                }
            }
        }

        private static void PlaceWallSet(Theme theme, WallSlot slot, Rng rng, Transform root)
        {
            switch (theme)
            {
                case Theme.Kitchen:
                {
                    // A run of two counter units, or a fridge and a counter.
                    var run = new List<string>();
                    if (rng.Chance(0.3f)) run.Add(rng.Chance(0.5f) ? "kitchenFridge" : "kitchenFridgeLarge");
                    while (run.Count < 2) run.Add(rng.Pick(KitchenUnits));
                    PlaceRun(run, slot, rng, root);
                    break;
                }
                case Theme.Dining:
                    PlaceRun(new List<string> { rng.Pick(new[] { "bookcaseOpen", "sideTable", "pottedPlant", "coatRackStanding", "lampRoundFloor", "bench", "trashcan" }) }, slot, rng, root);
                    break;
                case Theme.Lounge:
                    PlaceRun(new List<string> { rng.Pick(new[] { "loungeSofa", "bookcaseClosedWide", "loungeChair", "pottedPlant", "lampRoundFloor", "sideTable" }) }, slot, rng, root);
                    break;
                case Theme.Storage:
                {
                    if (rng.Chance(0.6f))
                    {
                        PlaceRun(new List<string> { rng.Chance(0.5f) ? "bookcaseClosed" : "bookcaseOpen", rng.Chance(0.5f) ? "bookcaseClosed" : "cardboardBoxClosed" }, slot, rng, root);
                    }
                    else
                    {
                        PlaceRun(new List<string> { "cardboardBoxClosed", "cardboardBoxOpen" }, slot, rng, root);
                    }
                    break;
                }
                default:
                    PlaceRun(new List<string> { rng.Pick(new[] { "pottedPlant", "bench", "coatRackStanding", "trashcan" }) }, slot, rng, root);
                    break;
            }
        }

        /// <summary>Places pieces side by side with their backs against the wall, centred (with a little jitter) on the wall segment.</summary>
        private static void PlaceRun(List<string> pieces, WallSlot slot, Rng rng, Transform root)
        {
            var dir = new Vector3(slot.Dir.x, 0f, slot.Dir.y);
            var along = new Vector3(-dir.z, 0f, dir.x);
            var rot = Quaternion.LookRotation(dir); // local +z (the backs) towards the wall

            float total = 0f;
            var widths = new List<float>();
            foreach (var p in pieces)
            {
                Vector3 size = Size(p);
                widths.Add(size.x);
                total += size.x;
            }
            if (total > WallHalfSpan * 2f)
            {
                // Too wide for the segment: keep only the first piece if it fits.
                if (widths[0] > WallHalfSpan * 2f) return;
                pieces = new List<string> { pieces[0] };
                widths = new List<float> { widths[0] };
                total = widths[0];
            }

            float slack = WallHalfSpan * 2f - total;
            float start = -total * 0.5f + rng.Range(-slack * 0.5f, slack * 0.5f);
            Vector3 wallInner = FloorLayout.CellCenter(slot.Cell) + dir * (FloorLayout.CellSize * 0.5f - FloorBuilder.WallThickness * 0.5f);
            for (int i = 0; i < pieces.Count; i++)
            {
                float depth = Size(pieces[i]).z;
                Vector3 pos = wallInner - dir * (depth * 0.5f + WallGap) + along * (start + widths[i] * 0.5f);
                Place(pieces[i], pos, rot, root);
                start += widths[i];
            }
        }

        // ------------------------------------------------------------------ instancing

        /// <summary>World size of a model at the café scale (x = width, y = height, z = depth).</summary>
        private static Vector3 Size(string model)
        {
            var info = Load(model);
            return info.Prefab == null ? Vector3.zero : info.LocalBounds.size * ModelScale;
        }

        /// <summary>
        /// Instantiates <paramref name="model"/> so that its footprint is centred on <paramref name="position"/> and it rests
        /// on the floor (the kit's pivots are inconsistent). Returns the footprint half size, or null if the model is missing.
        /// </summary>
        private static Vector3? Place(string model, Vector3 position, Quaternion rotation, Transform root, bool solid = true)
        {
            var info = Load(model);
            if (info.Prefab == null) return null;

            var holder = new GameObject(model).transform;
            holder.SetParent(root, false);
            holder.SetPositionAndRotation(position, rotation);

            var go = Object.Instantiate(info.Prefab, holder, false);
            Bounds b = info.LocalBounds;
            // Keep the prefab root's own scale (some kit models have one) on top of the café scale.
            go.transform.localScale = info.Prefab.transform.localScale * ModelScale;
            go.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * ModelScale;

            float lowest = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Fade(mats[i]);
                r.sharedMaterials = mats;
                lowest = Mathf.Min(lowest, r.bounds.min.y);
            }
            // Rest on the requested height whatever the rotation (knocked-over pieces, stacked boxes).
            if (lowest < float.MaxValue) holder.position += Vector3.up * (position.y - lowest);

            if (solid)
            {
                // Box around the actual geometry in the holder's space (also correct for knocked-over pieces).
                var box = holder.gameObject.AddComponent<BoxCollider>();
                Bounds local = HolderBounds(holder);
                box.center = local.center;
                box.size = local.size * 0.97f;
                if (ClimbablePieces.Contains(model)) MakeClimbable(holder, box, root);
            }
            else
            {
                holder.gameObject.layer = Layers.NoNavMesh;
                foreach (var t in holder.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.NoNavMesh;
            }
            return b.extents * ModelScale;
        }

        /// <summary>
        /// Leaves a small piece out of the NavMesh bake and marks its footprint (plus <see cref="ZoneMargin"/>) as the
        /// furniture area. Pieces resting higher than <see cref="MaxClimbHeight"/> stay ordinary obstacles.
        /// </summary>
        private static void MakeClimbable(Transform holder, BoxCollider box, Transform root)
        {
            // World bounds of the collider (computed from the transform: physics may not have synced it yet).
            var m = holder.localToWorldMatrix;
            var world = new Bounds(m.MultiplyPoint3x4(box.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                world.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            float floorY = root.position.y;
            if (world.max.y - floorY > MaxClimbHeight) return;

            var modifier = holder.gameObject.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;
            holder.gameObject.AddComponent<ClimbableFurniture>();

            var zone = new GameObject(holder.name + "_ClimbZone");
            zone.transform.SetParent(root, false);
            zone.transform.SetPositionAndRotation(new Vector3(world.center.x, floorY, world.center.z), Quaternion.identity);
            var volume = zone.AddComponent<NavMeshModifierVolume>();
            volume.area = NavAreas.Furniture;
            volume.center = new Vector3(0f, 0.5f, 0f);
            volume.size = new Vector3(world.size.x + ZoneMargin * 2f, 1.4f, world.size.z + ZoneMargin * 2f);
        }

        private static Bounds HolderBounds(Transform holder)
        {
            var toHolder = holder.worldToLocalMatrix;
            bool any = false;
            var result = new Bounds();
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = toHolder * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        private static ModelInfo Load(string model)
        {
            if (Models.TryGetValue(model, out var info)) return info;
            info.Prefab = Resources.Load<GameObject>("KenneyFurniture/" + model);
            if (info.Prefab != null)
            {
                // Bounds in the space the instance is placed in: including the root's own rotation and scale.
                var toParent = Matrix4x4.Translate(-info.Prefab.transform.position);
                bool any = false;
                foreach (var mf in info.Prefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var m = toParent * mf.transform.localToWorldMatrix;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var p = m.MultiplyPoint3x4(corner);
                        if (!any) { info.LocalBounds = new Bounds(p, Vector3.zero); any = true; }
                        else info.LocalBounds.Encapsulate(p);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[FurnitureDecorator] Missing model Resources/KenneyFurniture/{model}");
            }
            Models[model] = info;
            return info;
        }

        /// <summary>The kit's bright colours, faded and darkened to fit the abandoned café (cached per source material).</summary>
        private static Material Fade(Material source)
        {
            if (source == null) return null;
            if (Faded.TryGetValue(source, out var mat) && mat != null) return mat;
            Color c = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            float grey = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            Color faded = Color.Lerp(new Color(grey, grey, grey), c, 0.7f) * 0.8f;
            faded.a = 1f;
            string n = source.name.ToLowerInvariant();
            float smoothness = n.Contains("glass") ? 0.8f : n.Contains("metal") ? 0.5f : n.Contains("carpet") ? 0.05f : 0.25f;
            mat = MaterialLibrary.Get("Kenney_" + source.name, faded, smoothness);
            Faded[source] = mat;
            return mat;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Models.Clear();
            Faded.Clear();
        }
    }
}
