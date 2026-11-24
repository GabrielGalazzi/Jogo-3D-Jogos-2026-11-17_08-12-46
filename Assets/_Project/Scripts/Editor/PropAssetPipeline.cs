using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaidCafe.EditorTools
{
    /// <summary>
    /// Objective prop models (safe, gramophone, music box, fuse box, tea set, ...), exported from Blender by
    /// <c>ArtSource/Work/export_props.py</c> into <c>Resources/Props/&lt;Prop&gt;.fbx</c>, with their texture sets in
    /// <c>Textures/Props/&lt;Set&gt;/</c> and a manifest (<c>Textures/Props/props.json</c>).
    ///
    /// Import rules: static meshes (no rig, animation, cameras, lights or colliders: the game adds its own box colliders),
    /// textures at most 2048 px with mipmaps that follow the global mipmap limit (the "Texture quality" setting).
    ///
    /// <see cref="RebuildMaterials"/> makes one URP Lit material per manifest entry in <c>Materials/Props</c> and remaps
    /// the FBX materials of the same name to them, so the prefabs loaded from Resources already carry the final
    /// materials (and their shader variants are kept in player builds). Pickup items get an emission map (their albedo
    /// times a "glow" factor) so they read in the dark, like the greybox items did. Environment reflections are off, as
    /// on the surface materials: the only reflection source is the default sky cubemap, which made metal glow pale blue
    /// inside the dark building. Runs automatically whenever <c>props.json</c> is imported.
    /// </summary>
    public class PropAssetPipeline : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/_Project/Resources/Props/";
        public const string TextureFolder = "Assets/_Project/Textures/Props/";
        public const string MaterialFolder = "Assets/_Project/Materials/Props";
        private const string ManifestPath = TextureFolder + "props.json";
        private const string BaseTemplatePath = "Assets/_Project/Resources/MaterialTemplates/LitBase.mat";
        private const string EmissiveTemplatePath = "Assets/_Project/Resources/MaterialTemplates/LitEmissive.mat";

        public const int MaxSize = 2048;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
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
            importer.anisoLevel = 4;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.maxTextureSize = MaxSize;

            if (file.EndsWith("_Normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            else if (file.EndsWith("_Mask"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.Compressed;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                // Only the albedo of see-through materials (the music box glass) has an alpha channel.
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Compressed;
            }
            SurfaceAssetPipeline.LimitForWebGL(importer);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (path != ManifestPath) continue;
                // Textures and models imported in the same refresh are ready once the refresh is over.
                EditorApplication.delayCall += RebuildMaterials;
                return;
            }
        }

        [MenuItem("MaidCafe/Art/Rebuild Prop Materials")]
        public static void RebuildMaterials()
        {
            if (!File.Exists(ManifestPath))
            {
                Debug.LogWarning($"[PropAssetPipeline] {ManifestPath} not found.");
                return;
            }
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var baseTemplate = AssetDatabase.LoadAssetAtPath<Material>(BaseTemplatePath);
            var emissiveTemplate = AssetDatabase.LoadAssetAtPath<Material>(EmissiveTemplatePath);
            if (manifest?.materials == null || baseTemplate == null || emissiveTemplate == null)
            {
                Debug.LogError("[PropAssetPipeline] Manifest or material templates could not be loaded.");
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials")) AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "Props");

            var built = new Dictionary<string, Material>();
            foreach (var e in manifest.materials)
            {
                var template = e.glow > 0f ? emissiveTemplate : baseTemplate;
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
                Configure(mat, e);
                if (create) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
                built[e.name] = mat;
            }
            AssetDatabase.SaveAssets();

            int remapped = 0;
            foreach (var prop in manifest.props ?? Array.Empty<Prop>())
            {
                string modelPath = ModelFolder + prop.name + ".fbx";
                if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
                {
                    Debug.LogWarning($"[PropAssetPipeline] {modelPath} not found.");
                    continue;
                }
                bool changed = false;
                var existing = importer.GetExternalObjectMap();
                foreach (var matName in prop.materials)
                {
                    if (!built.TryGetValue(matName, out var mat)) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), matName);
                    if (existing.TryGetValue(id, out var current) && current == mat) continue;
                    importer.RemoveRemap(id);
                    importer.AddRemap(id, mat);
                    changed = true;
                }
                if (changed)
                {
                    importer.SaveAndReimport();
                    remapped++;
                }
            }
            Debug.Log($"[PropAssetPipeline] {built.Count} prop materials written to {MaterialFolder}, {remapped} models remapped.");
        }

        private static void Configure(Material mat, MaterialEntry e)
        {
            string dir = TextureFolder + e.set + "/" + e.set;
            var albedo = e.albedo ? AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Albedo.png") : null;
            var normal = e.normal ? AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Normal.png") : null;
            var mask = e.mask ? AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Mask.png") : null;

            mat.name = e.name;
            // Blender factors are linear; material colours are authored in gamma space.
            var baseColor = e.baseColor != null && e.baseColor.Length >= 3
                ? new Color(e.baseColor[0], e.baseColor[1], e.baseColor[2]).gamma
                : Color.white;
            baseColor.a = e.baseColor != null && e.baseColor.Length >= 4 ? e.baseColor[3] : 1f;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);

            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
            }

            if (mask != null)
            {
                // R metallic, A smoothness; URP multiplies the map's smoothness by _Smoothness.
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", Mathf.Clamp01(e.smoothness));
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.SetFloat("_Metallic", Mathf.Clamp01(e.metallic));
                mat.SetFloat("_Smoothness", Mathf.Clamp01(e.smoothness));
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            }

            if (e.glow > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", albedo);
                Color glow = albedo != null ? Color.white * e.glow : baseColor * e.glow;
                glow.a = 1f;
                mat.SetColor("_EmissionColor", glow);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (e.alpha == "blend")
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_ZWrite", 1f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = -1;
            }
        }

        [Serializable]
        private class Manifest
        {
            public MaterialEntry[] materials;
            public Prop[] props;
        }

        [Serializable]
        private class MaterialEntry
        {
            public string name;
            public string set;
            public string alpha;
            public float[] baseColor;
            public float metallic;
            public float smoothness;
            public bool albedo;
            public bool normal;
            public bool mask;
            public float glow;
        }

        [Serializable]
        private class Prop
        {
            public string name;
            public string[] materials;
        }
    }
}
