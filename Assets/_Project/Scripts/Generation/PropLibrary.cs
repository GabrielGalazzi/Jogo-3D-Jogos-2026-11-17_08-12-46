using System.Collections.Generic;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Textured models for objective props, pickups and item stands (<c>Resources/Props</c>). They are exported by
    /// <c>ArtSource/Work/export_props.py</c> at real-world size with the origin at the bottom centre and the front facing
    /// +Z; their URP materials come from the editor's PropAssetPipeline (see <c>credits.md</c> for the sources).
    ///
    /// Models are visual only: <see cref="Solid"/> adds one box collider fitted to the model, which is what stations
    /// raycast against and what the NavMesh bake sees. Every caller keeps its greybox box as the fallback, so a missing
    /// model never breaks a floor.
    /// </summary>
    public static class PropLibrary
    {
        private struct Model
        {
            public GameObject Prefab;
            public Bounds Bounds; // in the prefab root's space
        }

        private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>();

        public static bool Has(string model) => Load(model).Prefab != null;

        /// <summary>Size of the model at scale 1 (x = width, y = height, z = depth), or zero when it is missing.</summary>
        public static Vector3 Size(string model)
        {
            var info = Load(model);
            return info.Prefab == null ? Vector3.zero : info.Bounds.size;
        }

        /// <summary>
        /// Instantiates a model without colliders. Its footprint is centred on <paramref name="localBottomCentre"/>
        /// (in <paramref name="parent"/>'s space) and its bottom rests on that point's height; front = local +Z of
        /// <paramref name="localRotation"/>. Returns the holder transform, or null when the model is missing.
        /// </summary>
        public static Transform Visual(string model, Transform parent, Vector3 localBottomCentre, Quaternion localRotation,
            float scale = 1f, int layer = Layers.NoNavMesh)
        {
            var info = Load(model);
            if (info.Prefab == null) return null;

            var holder = new GameObject(model).transform;
            holder.gameObject.layer = layer;
            holder.SetParent(parent, false);
            holder.localPosition = localBottomCentre;
            holder.localRotation = localRotation;

            var go = Object.Instantiate(info.Prefab, holder, false);
            go.name = "Model";
            Bounds b = info.Bounds;
            go.transform.localRotation = info.Prefab.transform.localRotation;
            go.transform.localScale = info.Prefab.transform.localScale * scale;
            go.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z) * scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            return holder;
        }

        /// <summary>
        /// Like <see cref="Visual"/>, plus a box collider on the holder fitted to the model (stations go on the returned
        /// object). Returns null when the model is missing.
        /// </summary>
        public static GameObject Solid(string model, Transform parent, string name, Vector3 localBottomCentre,
            Quaternion localRotation, float scale = 1f, int layer = Layers.Default)
        {
            var holder = Visual(model, parent, localBottomCentre, localRotation, scale, layer);
            if (holder == null) return null;
            holder.name = name;
            Vector3 size = Load(model).Bounds.size * scale;
            var box = holder.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = size;
            return holder.gameObject;
        }

        /// <summary>Shows or hides a holder's model (used to swap the breaker's knife switch from open to closed).</summary>
        public static void SetModelVisible(GameObject holder, bool visible)
        {
            var model = holder != null ? holder.transform.Find("Model") : null;
            if (model != null) model.gameObject.SetActive(visible);
        }

        private static Model Load(string model)
        {
            if (string.IsNullOrEmpty(model)) return default;
            if (Models.TryGetValue(model, out var info)) return info;
            info.Prefab = Resources.Load<GameObject>("Props/" + model);
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
                        var corner = mb.center + Vector3.Scale(mb.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var p = m.MultiplyPoint3x4(corner);
                        if (!any)
                        {
                            info.Bounds = new Bounds(p, Vector3.zero);
                            any = true;
                        }
                        else info.Bounds.Encapsulate(p);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[PropLibrary] Missing model Resources/Props/{model}; using the greybox fallback.");
            }
            Models[model] = info;
            return info;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Models.Clear();
        }
    }
}
