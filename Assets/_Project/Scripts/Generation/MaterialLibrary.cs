using System.Collections.Generic;
using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Runtime material cache for procedurally built geometry.
    /// Materials are cloned from two template assets in Resources/MaterialTemplates (LitBase, LitEmissive)
    /// so the URP Lit shader and its emission variant are guaranteed to be included in player builds
    /// (Shader.Find + runtime keywords would otherwise be stripped).
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _baseTemplate;
        private static Material _emissiveTemplate;

        public static Material Get(string name, Color color, float smoothness = 0.25f, Color? emission = null)
        {
            if (Cache.TryGetValue(name, out var mat) && mat != null) return mat;

            LoadTemplates();
            var template = emission.HasValue ? _emissiveTemplate : _baseTemplate;
            mat = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            Cache[name] = mat;
            return mat;
        }

        /// <summary>
        /// Textured surface material: a tinted clone of <c>Resources/Surfaces/&lt;surface&gt;</c> (built by the editor's
        /// SurfaceAssetPipeline, tiled in metres; geometry gets metre UVs from <see cref="SurfaceMapping"/>).
        /// Falls back to a flat <see cref="Get"/> material when the surface asset is missing.
        /// </summary>
        public static Material Surface(string name, string surface, Color tint, Color fallback, float fallbackSmoothness = 0.25f)
        {
            if (Cache.TryGetValue(name, out var mat) && mat != null) return mat;

            var source = Resources.Load<Material>("Surfaces/" + surface);
            if (source == null) return Get(name, fallback, fallbackSmoothness);
            mat = new Material(source) { name = name };
            mat.SetColor("_BaseColor", tint);
            Cache[name] = mat;
            return mat;
        }

        private static void LoadTemplates()
        {
            if (_baseTemplate == null) _baseTemplate = Resources.Load<Material>("MaterialTemplates/LitBase");
            if (_emissiveTemplate == null) _emissiveTemplate = Resources.Load<Material>("MaterialTemplates/LitEmissive");
        }

        public static void Clear() => Cache.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
        }
    }
}
