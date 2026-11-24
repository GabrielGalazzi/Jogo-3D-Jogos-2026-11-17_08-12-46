using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MaidCafe.EditorTools
{
    /// <summary>
    /// Surface texture sets of the procedural floors (walls, floors, ceilings, trim, props), made by
    /// <c>ArtSource/Work/build_surfaces.py</c> from Poly Haven (CC0) maps and the project's own wallpaper patterns.
    ///
    /// Import rules: albedo and normal maps at most 2048 px, metallic/smoothness masks at most 1024 px, mipmaps on and
    /// following the global mipmap limit (the in-game "Texture quality" setting drops the top mip levels), trilinear and
    /// anisotropic filtering, repeat wrap. WebGL builds cap every set at <see cref="WebGLMaxSize"/> (download size: itch.io
    /// refuses files over 200 MB, and the 2K sets alone made the data file 220 MB).
    ///
    /// <see cref="RebuildMaterials"/> makes one URP Lit material per set in <c>Resources/Surfaces</c> from
    /// <c>surfaces.json</c>. Floor geometry has UVs in metres (<c>SurfaceMapping</c>), so each material is tiled by its
    /// set's real-world size. The materials are clones of the LitBase template with the normal-map and
    /// metallic/smoothness-map keywords on; being assets in Resources, their shader variants are kept in player builds.
    /// Runs automatically whenever <c>surfaces.json</c> is imported.
    /// </summary>
    public class SurfaceAssetPipeline : AssetPostprocessor
    {
        public const string TextureFolder = "Assets/_Project/Textures/Surfaces/";
        public const string MaterialFolder = "Assets/_Project/Resources/Surfaces";
        private const string ManifestPath = TextureFolder + "surfaces.json";
        private const string TemplatePath = "Assets/_Project/Resources/MaterialTemplates/LitBase.mat";

        public const int MaxSize = 2048;
        public const int MaskMaxSize = 1024;

        /// <summary>Largest texture size in the browser build (the in-game "Medium" texture quality).</summary>
        public const int WebGLMaxSize = 1024;

        /// <summary>WebGL-only override: same compression, at most <see cref="WebGLMaxSize"/> px. Other platforms unchanged.</summary>
        public static void LimitForWebGL(TextureImporter importer)
        {
            var web = importer.GetPlatformTextureSettings("WebGL");
            web.overridden = true;
            web.maxTextureSize = Math.Min(WebGLMaxSize, importer.maxTextureSize);
            web.format = TextureImporterFormat.Automatic;
            web.textureCompression = importer.textureCompression;
            importer.SetPlatformTextureSettings(web);
        }

        /// <summary>Reimports every surface and prop texture (after a change to the import rules).</summary>
        [MenuItem("MaidCafe/Art/Reimport Surface and Prop Textures")]
        public static void ReimportTextures()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder.TrimEnd('/'), PropAssetPipeline.TextureFolder.TrimEnd('/') });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in guids) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log($"[SurfaceAssetPipeline] Reimported {guids.Length} textures.");
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureFolder)) return;
            var importer = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);

            importer.mipmapEnabled = true;
            importer.ignoreMipmapLimit = false;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;

            if (file.EndsWith("_Normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.maxTextureSize = MaxSize;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC5 on desktop
            }
            else if (file.EndsWith("_Mask"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.maxTextureSize = MaskMaxSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.maxTextureSize = MaxSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
            }
            LimitForWebGL(importer);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (path != ManifestPath) continue;
                // Textures imported in the same refresh are ready once the refresh is over.
                EditorApplication.delayCall += RebuildMaterials;
                return;
            }
        }

        [MenuItem("MaidCafe/Art/Rebuild Surface Materials")]
        public static void RebuildMaterials()
        {
            if (!File.Exists(ManifestPath))
            {
                Debug.LogWarning($"[SurfaceAssetPipeline] {ManifestPath} not found.");
                return;
            }
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var template = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);
            if (manifest?.surfaces == null || template == null)
            {
                Debug.LogError("[SurfaceAssetPipeline] Manifest or LitBase template could not be loaded.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Resources", "Surfaces");

            int built = 0;
            foreach (var e in manifest.surfaces)
            {
                string dir = TextureFolder + e.name + "/" + e.name;
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Albedo.jpg");
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Normal.jpg");
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Mask.png");
                if (albedo == null)
                {
                    Debug.LogWarning($"[SurfaceAssetPipeline] {e.name}: albedo missing, skipped.");
                    continue;
                }

                string path = $"{MaterialFolder}/{e.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = mat == null;
                if (create) mat = new Material(template);
                else
                {
                    mat.shader = template.shader;
                    mat.CopyPropertiesFromMaterial(template);
                    mat.shaderKeywords = template.shaderKeywords;
                }
                mat.name = e.name;
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", new Vector2(1f / Mathf.Max(0.01f, e.tileU), 1f / Mathf.Max(0.01f, e.tileV)));
                mat.SetTextureOffset("_BaseMap", Vector2.zero);
                mat.SetFloat("_Metallic", 0f);
                // No environment reflections: the only reflection source is Unity's default sky cubemap, which made
                // glossy spots on floors and metal glow pale blue inside the dark building. Lights still give highlights.
                mat.SetFloat("_EnvironmentReflections", 0f);
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.SetFloat("_BumpScale", e.normalScale > 0f ? e.normalScale : 1f);
                    mat.EnableKeyword("_NORMALMAP");
                }
                else mat.DisableKeyword("_NORMALMAP");

                if (mask != null)
                {
                    // R metallic, A smoothness; URP multiplies the map's smoothness by _Smoothness.
                    mat.SetTexture("_MetallicGlossMap", mask);
                    mat.SetFloat("_SmoothnessTextureChannel", 0f);
                    mat.SetFloat("_Smoothness", e.smoothness > 0f ? e.smoothness : 1f);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                else
                {
                    mat.SetFloat("_Smoothness", 0.25f);
                    mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (create) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
                built++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SurfaceAssetPipeline] {built} surface materials written to {MaterialFolder}.");
        }

        [Serializable]
        private class Manifest
        {
            public Entry[] surfaces;
        }

        [Serializable]
        private class Entry
        {
            public string name;
            public string source;
            public float tileU;
            public float tileV;
            public float smoothness;
            public float normalScale;
        }
    }
}
