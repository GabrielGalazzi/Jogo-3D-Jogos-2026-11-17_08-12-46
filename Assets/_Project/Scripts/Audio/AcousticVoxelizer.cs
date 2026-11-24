using System.Collections.Generic;
using MaidCafe.Generation;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>An axis-aligned solid box for the acoustic world, with its acoustic material and overwrite priority.</summary>
    public struct AcousticBox
    {
        public Bounds Bounds;
        public int Material;
        public int Priority;

        public AcousticBox(Bounds bounds, int material, int priority = 0)
        {
            Bounds = bounds;
            Material = material;
            Priority = priority;
        }
    }

    /// <summary>
    /// A dense voxel grid of acoustic material ids (0 = air). Memory layout matches the SDK's VoxelPrimitive data:
    /// index = (x * Height + y) * Depth + z. Voxel (x, y, z) covers [Origin + (x, y, z) * VoxelSize, + VoxelSize].
    /// </summary>
    public sealed class VoxelGrid
    {
        public Vector3 Origin;
        public float VoxelSize;
        public int Width, Height, Depth;
        public int[] Data;

        public Vector3 Size => new Vector3(Width, Height, Depth) * VoxelSize;

        public int Index(int x, int y, int z) => (x * Height + y) * Depth + z;

        public int Get(int x, int y, int z) => Data[Index(x, y, z)];

        public bool InBounds(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Depth;

        public Vector3 CellCenter(int x, int y, int z) => Origin + (new Vector3(x, y, z) + Vector3.one * 0.5f) * VoxelSize;

        /// <summary>Material at a world position (0 outside the grid).</summary>
        public int Sample(Vector3 world)
        {
            Vector3 p = (world - Origin) / VoxelSize;
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            return InBounds(x, y, z) ? Get(x, y, z) : 0;
        }

        public int CountSolid()
        {
            int n = 0;
            foreach (int m in Data) if (m != 0) n++;
            return n;
        }
    }

    /// <summary>
    /// Turns the generated floor's architecture into the voxel grid the ray-traced audio runs against.
    /// Rules (per axis): a voxel is solid when its centre lies inside the box; a box thinner than one voxel along an
    /// axis fills the single voxel containing its centre, so thin walls and slabs never disappear.
    /// The grid is aligned so voxel centres sit on the wall lines (multiples of the voxel size in X/Z) and a voxel
    /// boundary sits at floor level (y = 0), which keeps the 0.2 m walls one voxel thick and door openings open.
    /// </summary>
    public static class AcousticVoxelizer
    {
        public const float DefaultVoxelSize = 0.25f;
        private const float Epsilon = 1e-4f;

        /// <summary>Rasterizes the boxes into a grid that covers all of them plus <paramref name="margin"/>.</summary>
        public static VoxelGrid Rasterize(IReadOnlyList<AcousticBox> boxes, float voxelSize = DefaultVoxelSize, float margin = 0.5f)
        {
            var region = new Bounds();
            for (int i = 0; i < boxes.Count; i++)
            {
                if (i == 0) region = boxes[i].Bounds;
                else region.Encapsulate(boxes[i].Bounds);
            }
            region.Expand(margin * 2f);
            return Rasterize(boxes, region, voxelSize);
        }

        public static VoxelGrid Rasterize(IReadOnlyList<AcousticBox> boxes, Bounds region, float voxelSize)
        {
            float s = voxelSize;
            var origin = new Vector3(
                (Mathf.Floor(region.min.x / s) - 0.5f) * s,
                Mathf.Floor(region.min.y / s) * s,
                (Mathf.Floor(region.min.z / s) - 0.5f) * s);

            var grid = new VoxelGrid
            {
                Origin = origin,
                VoxelSize = s,
                Width = Mathf.Max(1, Mathf.CeilToInt((region.max.x - origin.x) / s)),
                Height = Mathf.Max(1, Mathf.CeilToInt((region.max.y - origin.y) / s)),
                Depth = Mathf.Max(1, Mathf.CeilToInt((region.max.z - origin.z) / s))
            };
            grid.Data = new int[grid.Width * grid.Height * grid.Depth];
            var priority = new int[grid.Data.Length];

            foreach (var box in boxes)
            {
                if (box.Material == 0) continue;
                var b = box.Bounds;
                if (!AxisRange(b.min.x, b.max.x, origin.x, s, grid.Width, out int x0, out int x1)) continue;
                if (!AxisRange(b.min.y, b.max.y, origin.y, s, grid.Height, out int y0, out int y1)) continue;
                if (!AxisRange(b.min.z, b.max.z, origin.z, s, grid.Depth, out int z0, out int z1)) continue;

                for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    int row = (x * grid.Height + y) * grid.Depth;
                    for (int z = z0; z <= z1; z++)
                    {
                        int i = row + z;
                        if (grid.Data[i] != 0 && priority[i] > box.Priority) continue;
                        grid.Data[i] = box.Material;
                        priority[i] = box.Priority;
                    }
                }
            }
            return grid;
        }

        /// <summary>Voxel index range covered by [lo, hi] along one axis. False if it misses the grid.</summary>
        public static bool AxisRange(float lo, float hi, float origin, float s, int count, out int a, out int b)
        {
            if (hi - lo < s)
            {
                a = b = Mathf.FloorToInt(((lo + hi) * 0.5f - origin) / s);
            }
            else
            {
                a = Mathf.CeilToInt((lo - origin) / s - 0.5f - Epsilon);
                b = Mathf.FloorToInt((hi - origin) / s - 0.5f + Epsilon);
            }
            a = Mathf.Max(a, 0);
            b = Mathf.Min(b, count - 1);
            return a <= b;
        }

        // ------------------------------------------------------------------ floor geometry

        /// <summary>
        /// Static architecture of a built floor: room floors, ceilings, walls (exterior walls get the heavier material),
        /// pillars and the elevator car shells. Furniture, props, hiding spots and doors are not voxelized (doors and
        /// locker panels are thin moving boxes, see <see cref="CollectDynamic"/>).
        /// </summary>
        public static List<AcousticBox> CollectStatic(FloorInstance floor)
        {
            var boxes = new List<AcousticBox>();
            var exterior = FloorBuilder.Palette.Exterior;
            foreach (Transform child in floor.StaticRoot)
            {
                var col = child.GetComponent<BoxCollider>();
                if (col == null || col.isTrigger || !col.enabled) continue;

                string n = child.name;
                int material;
                int prio = 0;
                if (n.StartsWith("Floor_")) material = AcousticMaterials.Floor;
                else if (n.StartsWith("Ceiling_")) material = AcousticMaterials.Ceiling;
                else if (n == "Pillar") { material = AcousticMaterials.Pillar; prio = 2; }
                else if (n == "Wall")
                {
                    var r = child.GetComponent<MeshRenderer>();
                    bool outside = r != null && r.sharedMaterial == exterior;
                    material = outside ? AcousticMaterials.ExteriorWall : AcousticMaterials.InteriorWall;
                    prio = outside ? 3 : 1;
                }
                else continue;
                boxes.Add(new AcousticBox(WorldAabb(col), material, prio));
            }

            foreach (var car in new[] { floor.StartCar, floor.ExitCar })
            {
                if (car == null) continue;
                foreach (Transform child in car.transform)
                {
                    if (!child.name.StartsWith("Car")) continue;
                    var col = child.GetComponent<BoxCollider>();
                    if (col == null || col.isTrigger) continue;
                    boxes.Add(new AcousticBox(WorldAabb(col), AcousticMaterials.ElevatorShell, 4));
                }
            }
            return boxes;
        }

        /// <summary>Thin boxes that are added as individual prisms: elevator doors and locker panels (incl. the door).</summary>
        public static List<(BoxCollider collider, int material)> CollectDynamic(FloorInstance floor)
        {
            var list = new List<(BoxCollider, int)>();
            foreach (var car in new[] { floor.StartCar, floor.ExitCar })
            {
                if (car == null) continue;
                foreach (Transform child in car.transform)
                {
                    if (child.name != "DoorL" && child.name != "DoorR") continue;
                    var col = child.GetComponent<BoxCollider>();
                    if (col != null) list.Add((col, AcousticMaterials.ElevatorShell));
                }
            }
            foreach (var spot in floor.HidingSpots)
            {
                if (spot == null || spot.Kind != Interaction.HidingKind.Locker) continue;
                foreach (var col in spot.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (col.isTrigger) continue;
                    list.Add((col, AcousticMaterials.LockerMetal));
                }
            }
            return list;
        }

        /// <summary>World-space AABB of a box collider (exact for the axis-aligned and 90°-rotated boxes the floors use).</summary>
        public static Bounds WorldAabb(BoxCollider col)
        {
            Matrix4x4 m = col.transform.localToWorldMatrix;
            Vector3 center = m.MultiplyPoint3x4(col.center);
            Vector3 e = col.size * 0.5f;
            var extents = new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
            return new Bounds(center, extents * 2f);
        }
    }
}
