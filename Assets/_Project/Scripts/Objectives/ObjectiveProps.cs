using System;
using System.Collections.Generic;
using System.Linq;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Interaction;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>Placement helpers and greybox visuals for objective items and stations.</summary>
    public static class ObjectiveProps
    {
        private const float WallT = FloorBuilder.WallThickness;

        // ------------------------------------------------------------------ room selection

        /// <summary>
        /// Rooms ordered for placement: preferred room types first (in the given order), then the
        /// farthest rooms from the start. The start room is excluded unless nothing else exists.
        /// </summary>
        public static List<int> Rooms(FloorInstance f, Rng rng, IList<RoomType> prefer = null, ICollection<int> avoid = null)
        {
            var rooms = f.Layout.Rooms
                .Where(r => r.Id != f.Layout.Start.Room && (avoid == null || !avoid.Contains(r.Id)))
                .ToList();
            if (rooms.Count == 0) rooms = f.Layout.Rooms.Where(r => r.Id != f.Layout.Start.Room).ToList();
            if (rooms.Count == 0) rooms = f.Layout.Rooms.ToList();
            rng.Shuffle(rooms);
            return rooms
                .OrderBy(r => prefer == null ? 0 : (prefer.IndexOf(r.Type) < 0 ? 99 : prefer.IndexOf(r.Type)))
                .ThenByDescending(r => f.Layout.RoomDistance[r.Id])
                .Select(r => r.Id)
                .ToList();
        }

        /// <summary>Takes a wall slot in the first room of the list that has one; falls back to any room.</summary>
        public static bool TakeWall(FloorInstance f, IEnumerable<int> rooms, out WallSlot slot)
        {
            if (f.Slots.TryTakeWallInAny(rooms, out slot)) return true;
            return f.Slots.TryTakeWallInAny(f.Layout.Rooms.Select(r => r.Id).Where(id => id != f.Layout.Start.Room), out slot)
                   || f.Slots.TryTakeWallInAny(f.Layout.Rooms.Select(r => r.Id), out slot);
        }

        /// <summary>Spreads <paramref name="count"/> wall slots over different rooms, farthest rooms first.</summary>
        public static List<WallSlot> Spread(FloorInstance f, Rng rng, int count, ICollection<int> avoid = null)
        {
            var rooms = Rooms(f, rng, null, avoid);
            var result = new List<WallSlot>();
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < rooms.Count; k++)
                {
                    int room = rooms[(i + k) % rooms.Count];
                    if (f.Slots.TryTakeWall(room, out var slot))
                    {
                        result.Add(slot);
                        break;
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ transforms

        public static Vector3 Dir3(WallSlot s) => new Vector3(s.Dir.x, 0f, s.Dir.y);

        /// <summary>Centre (at floor height) of an object of the given depth standing against the slot's wall.</summary>
        public static Vector3 WallAnchor(WallSlot s, float depth)
        {
            return FloorLayout.CellCenter(s.Cell) + Dir3(s) * (FloorLayout.CellSize * 0.5f - WallT * 0.5f - depth * 0.5f - 0.02f);
        }

        public static Transform Root(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go.transform;
        }

        public static Transform WallRoot(FloorInstance f, string name, WallSlot s, float depth)
        {
            return Root(name, f.DynamicRoot, WallAnchor(s, depth), Quaternion.LookRotation(-Dir3(s)));
        }

        // ------------------------------------------------------------------ builders

        /// <summary>
        /// A stand against the wall with a pickup on top. The pickup shows <paramref name="model"/> (Resources/Props);
        /// <paramref name="mat"/> and <paramref name="size"/> describe the greybox box used when the model is missing.
        /// </summary>
        public static Pickup ItemOnCabinet(FloorInstance f, WallSlot s, string itemId, string nameKey, string model,
            Material mat, Vector3 size, float modelScale = 1f)
        {
            Vector3 top = FloorBuilder.BuildItemStand(s, f.DynamicRoot);
            return MakePickup(f.DynamicRoot, top, itemId, nameKey, model, mat, size, modelScale);
        }

        /// <summary>An objective item resting on <paramref name="top"/>.</summary>
        public static Pickup MakePickup(Transform parent, Vector3 top, string itemId, string nameKey, string model,
            Material mat, Vector3 size, float modelScale = 1f)
        {
            var go = FloorBuilder.PickupRoot("Item_" + itemId, parent, top, model, modelScale, size,
                t => FloorBuilder.LocalBox(t, "Visual", Vector3.zero, size, mat, Layers.NoNavMesh, collider: false));
            var pickup = go.AddComponent<Pickup>();
            pickup.Configure(PickupKind.Item, itemId, nameKey);
            return pickup;
        }

        /// <summary>
        /// A solid prop model with its bottom centre at <paramref name="localBottom"/>, or a greybox box of
        /// <paramref name="boxSize"/> when the model is missing. Stations go on the returned object.
        /// </summary>
        public static GameObject SolidOrBox(Transform parent, string name, string model, Vector3 localBottom,
            Vector3 boxSize, Material boxMat, float scale = 1f)
        {
            var go = PropLibrary.Solid(model, parent, name, localBottom, Quaternion.identity, scale);
            return go != null ? go : FloorBuilder.LocalBox(parent, name, localBottom + Vector3.up * (boxSize.y * 0.5f), boxSize, boxMat);
        }

        /// <summary>Depth of a model (for <see cref="WallRoot"/>), or <paramref name="fallback"/> when it is missing.</summary>
        public static float Depth(string model, float fallback)
        {
            Vector3 size = PropLibrary.Size(model);
            return size == Vector3.zero ? fallback : size.z;
        }

        /// <summary>
        /// Local z that puts the back of an object of depth <paramref name="depth"/> against the wall, inside a
        /// <see cref="WallRoot"/> made for depth <paramref name="rootDepth"/>.
        /// </summary>
        public static float AgainstWall(float rootDepth, float depth) => -(rootDepth - depth) * 0.5f;

        public static ObjectiveStation AddStation(GameObject go, string readyPrompt, string nameKey, float hold, float noise,
            string lockedHint, Func<bool> ready, Action used)
        {
            var st = go.AddComponent<ObjectiveStation>();
            st.ReadyPromptKey = readyPrompt;
            st.DisplayNameKey = nameKey;
            st.Hold = hold;
            st.NoiseRadius = noise;
            st.LockedHintKey = lockedHint;
            st.IsReady = ready;
            st.OnUsed = used;
            return st;
        }

        /// <summary>Material with a faint glow so key items read in the dark.</summary>
        public static Material ItemMat(string name, Color color, float glow = 0.25f)
        {
            return MaterialLibrary.Get("Item_" + name, color, 0.5f, color * glow);
        }

        /// <summary>Seat height of the "Chair" model (measured on the mesh).</summary>
        private const float ChairSeatHeight = 0.45f;

        /// <summary>
        /// The porcelain "customer": an antique doll standing on a chair at the reserved table, facing it (local +Z).
        /// Unsettling set dressing.
        /// </summary>
        public static void PorcelainDoll(Transform parent, Vector3 localSeat)
        {
            var chair = PropLibrary.Solid("Chair", parent, "Chair", localSeat, Quaternion.identity);
            if (chair != null && PropLibrary.Visual("Doll", parent, localSeat + new Vector3(0f, ChairSeatHeight, 0.04f),
                    Quaternion.identity, 1f, Layers.Default) != null) return;
            PorcelainDollGreybox(parent, localSeat, chair: chair == null);
        }

        private static void PorcelainDollGreybox(Transform parent, Vector3 localSeat, bool chair)
        {
            var porcelain = MaterialLibrary.Get("Porcelain", new Color(0.95f, 0.93f, 0.9f), 0.9f);
            var dress = MaterialLibrary.Get("DollDress", new Color(0.12f, 0.1f, 0.14f), 0.3f);
            if (chair)
            {
                FloorBuilder.LocalBox(parent, "ChairSeat", localSeat + new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.05f, 0.45f), FloorBuilder.Palette.Wood);
                FloorBuilder.LocalBox(parent, "ChairBack", localSeat + new Vector3(0f, 0.75f, -0.2f), new Vector3(0.45f, 0.6f, 0.05f), FloorBuilder.Palette.Wood);
            }
            FloorBuilder.LocalBox(parent, "DollBody", localSeat + new Vector3(0f, 0.68f, 0f), new Vector3(0.28f, 0.42f, 0.2f), dress, collider: false);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "DollHead";
            head.transform.SetParent(parent, false);
            head.transform.localPosition = localSeat + new Vector3(0f, 1.02f, 0f);
            head.transform.localScale = Vector3.one * 0.24f;
            head.GetComponent<MeshRenderer>().sharedMaterial = porcelain;
        }
    }
}
