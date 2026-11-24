using System.Collections.Generic;
using MaidCafe.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaidCafe.AI
{
    /// <summary>Which character model and colour treatment a maid uses.</summary>
    public enum MaidLook
    {
        /// <summary>Patrol maid: "maid pigtails" (VRoid).</summary>
        Patrol,
        Head,
        Watcher,

        /// <summary>Patrol maid variant: "Maid Style Anime" (rigged for this project).</summary>
        StyleAnime,

        /// <summary>Patrol maid variant: "Typical maid" (rigged and re-posed for this project).</summary>
        Typical
    }

    /// <summary>A material slot whose emission shows the maid's awareness (her eyes).</summary>
    public struct EyeGlow
    {
        public Renderer Renderer;

        /// <summary>Material index on the renderer, or -1 for the whole renderer.</summary>
        public int MaterialIndex;
    }

    /// <summary>References into an instantiated maid model.</summary>
    public class MaidRig
    {
        public Transform Model;
        public Animator Animator;
        public Transform Head;
        public Transform Eyes;
        public Transform RightHand;
        public EyeGlow[] Glows;

        /// <summary>Capsules fitted to the body (see <see cref="MaidBodyColliders"/>).</summary>
        public List<CapsuleCollider> BodyColliders;
    }

    /// <summary>
    /// Loads the imported maid characters (Resources/Maids) and gives them the porcelain treatment at runtime:
    /// skin becomes cracked bisque porcelain (built from the model's own skin texture, see <see cref="PorcelainSkin"/>),
    /// the eye whites become black hollows and the irises glow (driven by the AI). Clothes and hair keep their
    /// textures, saturated and without sky reflections (see <see cref="Fabric"/>), with a per-role tint.
    /// Source models and licences: see credits.md.
    /// </summary>
    public static class MaidVisuals
    {
        private const string ControllerPath = "Maids/MaidAnimator";

        /// <summary>Models a patrol maid can use (the Head Maid and the bell maid always keep their own).</summary>
        public static readonly MaidLook[] PatrolLooks = { MaidLook.Patrol, MaidLook.StyleAnime, MaidLook.Typical };

        private struct LookDef
        {
            public string Resource;
            public float Height;

            /// <summary>Glaze tint applied to the desaturated skin.</summary>
            public Color Glaze;

            /// <summary>How much of the original painted skin colour survives (0 = pure porcelain, 1 = untouched skin).</summary>
            public float SkinTone;

            /// <summary>How far the skin brightness is lifted toward white (softens painted shading).</summary>
            public float Lift;

            /// <summary>Darkness of the hairline cracks (0 = none, 1 = full).</summary>
            public float Cracks;

            /// <summary>Satin glaze; high values read as glossy plastic.</summary>
            public float GlazeSmoothness;

            /// <summary>Role tint multiplied over the saturated cloth colours; white keeps the model's own colours.</summary>
            public Color Cloth;

            /// <summary>Role tint multiplied over the saturated hair colours; white keeps the model's own colours.</summary>
            public Color Hair;
        }

        /// <summary>
        /// Saturation gain applied to hair and cloth colours (1 = unchanged). Compensates for the dim lighting and the
        /// global colour grade (<see cref="MaidCafe.Core.PostFX"/>), which otherwise leave the outfits looking faded.
        /// </summary>
        private const float FabricSaturation = 1.4f;

        private const int CrackSize = 512;

        private static readonly Dictionary<(Material, MaidLook), Material> Converted = new Dictionary<(Material, MaidLook), Material>();

        /// <summary>Saturated copies of hair/cloth textures, keyed by the imported texture.</summary>
        private static readonly Dictionary<Texture, Texture2D> Vivid = new Dictionary<Texture, Texture2D>();

        private static Texture2D _cracks;
        private static Color32[] _crackPixels;
        private static Material _cutoutTemplate;
        private static Material _cutoutEmissiveTemplate;
        private static Material _fabricTemplate;

        private static LookDef Def(MaidLook look)
        {
            switch (look)
            {
                case MaidLook.Head:
                    return new LookDef
                    {
                        Resource = "Maids/AnimeMaidVRChat/AnimeMaidVRChat",
                        Height = 1.86f,
                        Glaze = new Color(0.92f, 0.92f, 0.95f),
                        SkinTone = 0.35f,
                        Lift = 0.22f,
                        Cracks = 0.85f,
                        GlazeSmoothness = 0.5f,
                        Cloth = new Color(1f, 0.6f, 0.72f), // the Head Maid's pink uniform
                        Hair = Color.white
                    };
                case MaidLook.Watcher:
                    return new LookDef
                    {
                        Resource = "Maids/CuteAnimeMaid/CuteAnimeMaid",
                        Height = 1.55f,
                        Glaze = new Color(1f, 0.96f, 0.92f),
                        SkinTone = 0.55f,
                        Lift = 0.18f,
                        Cracks = 0.7f,
                        GlazeSmoothness = 0.4f,
                        Cloth = Color.white,
                        Hair = Color.white
                    };
                case MaidLook.StyleAnime:
                    return new LookDef
                    {
                        Resource = "Maids/MaidStyleAnime/MaidStyleAnime",
                        Height = 1.6f,
                        Glaze = new Color(0.97f, 0.95f, 0.92f),
                        SkinTone = 0.5f,
                        Lift = 0.2f,
                        Cracks = 0.8f,
                        GlazeSmoothness = 0.45f,
                        Cloth = Color.white,
                        Hair = Color.white
                    };
                case MaidLook.Typical:
                    return new LookDef
                    {
                        Resource = "Maids/TypicalMaid/TypicalMaid",
                        Height = 1.66f,
                        Glaze = new Color(0.96f, 0.94f, 0.93f),
                        SkinTone = 0.45f,
                        Lift = 0.2f,
                        Cracks = 0.8f,
                        GlazeSmoothness = 0.45f,
                        Cloth = Color.white,
                        Hair = Color.white
                    };
                default:
                    return new LookDef
                    {
                        Resource = "Maids/MaidPigtails/MaidPigtails",
                        Height = 1.62f,
                        Glaze = new Color(0.98f, 0.95f, 0.91f),
                        SkinTone = 0.5f,
                        Lift = 0.2f,
                        Cracks = 0.8f,
                        GlazeSmoothness = 0.45f,
                        Cloth = Color.white,
                        Hair = Color.white
                    };
            }
        }

        /// <summary>Instantiates the model for <paramref name="look"/> under <paramref name="parent"/>. Returns null if the asset is missing.</summary>
        public static MaidRig Build(MaidLook look, Transform parent)
        {
            var def = Def(look);
            var prefab = Resources.Load<GameObject>(def.Resource);
            if (prefab == null)
            {
                Debug.LogWarning($"[MaidVisuals] Missing model Resources/{def.Resource}; using the greybox maid.");
                return null;
            }

            var go = Object.Instantiate(prefab, parent, false);
            go.name = "Model";
            var model = go.transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;

            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = new Bounds(model.position, Vector3.zero);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float height = Mathf.Max(0.1f, bounds.max.y - model.position.y);
            model.localScale = Vector3.one * (def.Height / height);

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(ControllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // All three are measured from this model's mesh, so they must run now, while it is still in the bind pose:
            // upright torso + hands folded in front while standing/walking/searching, hands kept out of the skirt in
            // every animation, and a solid body for the player.
            go.AddComponent<MaidRestPose>().Init(animator, model, def.Resource);
            go.AddComponent<MaidSkirtGuard>().Init(animator, model, def.Resource);
            var bodyColliders = MaidBodyColliders.Build(animator, model, def.Resource);
            // Last: it swaps the skirt meshes for re-skinned copies that the legs can lift.
            var skirt = go.AddComponent<MaidSkirtFlare>();
            if (!skirt.Init(animator, model, def.Resource)) Object.Destroy(skirt);

            var glows = new List<EyeGlow>();
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.skinnedMotionVectors = false;
                r.updateWhenOffscreen = false;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = Convert(mats[i], look, def, out bool isIris);
                    if (isIris) glows.Add(new EyeGlow { Renderer = r, MaterialIndex = i });
                }
                r.sharedMaterials = mats;
            }

            var rig = new MaidRig
            {
                Model = model,
                Animator = animator,
                Head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null,
                RightHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null,
                Glows = glows.ToArray(),
                BodyColliders = bodyColliders
            };

            // Eye anchor (used for sight raycasts and the "caught" camera snap): between the eyes, slightly forward.
            var eyes = new GameObject("Eyes").transform;
            Transform eyeL = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            Transform eyeR = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;
            Vector3 eyePos = eyeL != null && eyeR != null
                ? (eyeL.position + eyeR.position) * 0.5f
                : (rig.Head != null ? rig.Head.position + model.up * 0.08f * model.localScale.y : model.position + Vector3.up * def.Height * 0.93f);
            eyes.SetParent(rig.Head != null ? rig.Head : model, true);
            eyes.position = eyePos + model.forward * 0.06f;
            eyes.rotation = model.rotation;
            rig.Eyes = eyes;
            return rig;
        }

        /// <summary>
        /// Puts a small brass hand bell in the right fist (the watcher maid rings it to call the others). Call right
        /// after <see cref="Build"/>, while the model is in its bind pose: the handle is laid across the palm where the
        /// curled fingers close (<see cref="MaidBellGrip"/>), and the cup comes out on the little-finger side, so it
        /// hangs mouth-down below the fist when the thumb points up.
        /// </summary>
        public static Transform AttachBell(MaidRig rig)
        {
            if (rig?.RightHand == null || rig.Animator == null || !rig.Animator.isHuman) return null;
            var arm = MaidArmIK.Bind(rig.Animator, rig.Model, 1f);
            if (arm == null || arm.Middle == null || arm.Index == null || arm.Little == null) return null;

            Transform hand = arm.Hand;
            Vector3 palm = MaidArmIK.PalmNormal(arm, rig.Model);
            float knuckles = (arm.Middle.position - hand.position).magnitude;
            Vector3 toLittle = (arm.Little.position - arm.Index.position).normalized;
            // Inside the fist: under the base of the fingers, a finger's thickness off the palm.
            Vector3 grip = Vector3.Lerp(hand.position, arm.Middle.position, 0.92f) + palm * (knuckles * 0.3f);

            const float handleGrip = 0.035f; // where the fist holds the handle, from its bottom end
            var brass = MaterialLibrary.Get("BellBrass", new Color(0.78f, 0.62f, 0.3f), 0.85f);
            var wood = MaterialLibrary.Get("BellHandle", new Color(0.25f, 0.13f, 0.08f), 0.4f);
            var bell = new GameObject("HandBell").transform;
            bell.SetParent(hand, false);
            bell.rotation = Quaternion.FromToRotation(Vector3.up, toLittle);
            bell.position = grip - toLittle * handleGrip;
            float s = 1f / Mathf.Max(0.01f, hand.lossyScale.x);
            Part(bell, PrimitiveType.Cylinder, "Handle", new Vector3(0f, 0.04f, 0f) * s, new Vector3(0.02f, 0.045f, 0.02f) * s, wood);
            Part(bell, PrimitiveType.Sphere, "Cup", new Vector3(0f, 0.115f, 0f) * s, new Vector3(0.075f, 0.07f, 0.075f) * s, brass);
            Part(bell, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.135f, 0f) * s, new Vector3(0.085f, 0.006f, 0.085f) * s, brass);

            var restPose = rig.Model.GetComponent<MaidRestPose>();
            if (restPose != null) restPose.HoldsBell = true;
            rig.Model.gameObject.AddComponent<MaidBellGrip>().Init(arm, rig.Model.GetComponentInChildren<SkinnedMeshRenderer>());
            return bell;
        }

        private static void Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ materials

        private enum Slot
        {
            Skin,
            FacePaint,
            EyeWhite,
            Iris,
            Hidden,
            Hair,
            Cloth
        }

        private static Slot Classify(string name)
        {
            if (name.Contains("EyeIris")) return Slot.Iris;
            if (name.Contains("EyeWhite")) return Slot.EyeWhite;
            if (name.Contains("EyeHighlight") || name.Contains("EyeExtra")) return Slot.Hidden;
            if (name.EndsWith("_SKIN")) return Slot.Skin;
            if (name.EndsWith("_FACE")) return Slot.FacePaint;
            if (name.Contains("_HAIR")) return Slot.Hair;
            return Slot.Cloth;
        }

        private static Material Convert(Material source, MaidLook look, LookDef def, out bool isIris)
        {
            isIris = false;
            if (source == null) return null;
            var slot = Classify(source.name);
            isIris = slot == Slot.Iris;

            var key = (source, look);
            if (Converted.TryGetValue(key, out var cached) && cached != null) return cached;

            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            Material mat;
            switch (slot)
            {
                case Slot.Skin:
                    mat = new Material(MaterialLibrary.Get("PorcelainBase_" + look, Color.white, def.GlazeSmoothness));
                    mat.SetTexture("_BaseMap", tex != null ? PorcelainSkin(tex, def) : CrackTexture());
                    if (tex == null) mat.SetColor("_BaseColor", def.Glaze);
                    break;
                case Slot.FacePaint:
                    mat = Cutout(tex, new Color(0.3f, 0.26f, 0.28f), 0.5f);
                    break;
                case Slot.EyeWhite:
                    mat = new Material(MaterialLibrary.Get("PorcelainEyeHollow", new Color(0.015f, 0.01f, 0.012f), 0.95f));
                    break;
                case Slot.Iris:
                    mat = CutoutEmissive(tex, new Color(0.25f, 0.05f, 0.05f), new Color(0.25f, 0.05f, 0.05f));
                    break;
                case Slot.Hidden:
                    mat = Cutout(null, new Color(0f, 0f, 0f, 0f), 0f);
                    break;
                case Slot.Hair:
                    mat = Fabric(source, tex, def.Hair, 0.35f);
                    break;
                default:
                    mat = Fabric(source, tex, def.Cloth, 0.25f);
                    break;
            }
            mat.name = $"{source.name}_{look}";
            Converted[key] = mat;
            return mat;
        }

        /// <summary>
        /// The imported material's own colour (white on textured VRoid materials; the only colour information on
        /// untextured parts such as the "Maid Style Anime" dress and apron).
        /// </summary>
        private static Color SourceColor(Material source)
        {
            Color c = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            c.a = 1f;
            return c;
        }

        private static Material Cutout(Texture tex, Color color, float smoothness)
        {
            if (_cutoutTemplate == null) _cutoutTemplate = Resources.Load<Material>("MaterialTemplates/LitCutout");
            var mat = _cutoutTemplate != null ? new Material(_cutoutTemplate) : new Material(MaterialLibrary.Get("CutoutFallback", Color.white));
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        /// <summary>
        /// Hair and cloth: the model's own colours with extra saturation (<see cref="FabricSaturation"/>) times the role
        /// tint, on a cutout material without environment reflections. The only reflection source in the game is
        /// Unity's default daylight sky; on fabric it laid a grey-blue sheen over every outfit and washed the colours
        /// out (red hair read as lavender, black dresses as grey-navy).
        /// </summary>
        private static Material Fabric(Material source, Texture tex, Color tint, float smoothness)
        {
            // The template carries the keyword so the matching Lit variant is kept in builds.
            if (_fabricTemplate == null) _fabricTemplate = Resources.Load<Material>("MaterialTemplates/LitCutoutMatte");
            var mat = _fabricTemplate != null ? new Material(_fabricTemplate) : Cutout(null, Color.white, smoothness);
            if (tex != null) mat.SetTexture("_BaseMap", VividTexture(tex));
            mat.SetColor("_BaseColor", Saturate(SourceColor(source), FabricSaturation) * tint);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return mat;
        }

        private static Color Saturate(Color c, float gain)
        {
            float luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(
                Mathf.Clamp01(luma + (c.r - luma) * gain),
                Mathf.Clamp01(luma + (c.g - luma) * gain),
                Mathf.Clamp01(luma + (c.b - luma) * gain),
                c.a);
        }

        /// <summary>A copy of a hair/cloth texture with <see cref="FabricSaturation"/> applied (built once per texture per run).</summary>
        private static Texture2D VividTexture(Texture source)
        {
            if (Vivid.TryGetValue(source, out var cached) && cached != null) return cached;
            var tex = ReadableCopy(source, "_Vivid");
            tex.wrapMode = source.wrapMode;
            tex.filterMode = source.filterMode;
            tex.anisoLevel = source.anisoLevel;
            var px = tex.GetPixels32();
            const float gain = FabricSaturation;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                px[i] = new Color32(
                    (byte)Mathf.Clamp(luma + (c.r - luma) * gain, 0f, 255f),
                    (byte)Mathf.Clamp(luma + (c.g - luma) * gain, 0f, 255f),
                    (byte)Mathf.Clamp(luma + (c.b - luma) * gain, 0f, 255f),
                    c.a);
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            tex.Compress(false);
            tex.Apply(false, true);
            Vivid[source] = tex;
            return tex;
        }

        /// <summary>
        /// CPU-readable sRGB copy of a texture (the imported textures are not readable, so it goes through a temporary
        /// RenderTexture). The caller edits the pixels, then compresses and freezes it.
        /// </summary>
        private static Texture2D ReadableCopy(Texture source, string suffix)
        {
            int w = source.width, h = source.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = source.name + suffix, wrapMode = TextureWrapMode.Repeat };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static Material CutoutEmissive(Texture tex, Color color, Color emission)
        {
            if (_cutoutEmissiveTemplate == null) _cutoutEmissiveTemplate = Resources.Load<Material>("MaterialTemplates/LitCutoutEmissive");
            var mat = _cutoutEmissiveTemplate != null ? new Material(_cutoutEmissiveTemplate) : new Material(MaterialLibrary.Get("CutoutEmissiveFallback", Color.white, 0.5f, emission));
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_EmissionColor", emission);
            mat.SetFloat("_Smoothness", 0.9f);
            return mat;
        }

        /// <summary>
        /// Builds a porcelain version of a skin texture: the painted skin is partly desaturated, lifted toward the
        /// glaze tint and overlaid with the crack pattern. Keeping some of the original colour (blush, shading at the
        /// joints) makes the maids read as painted bisque dolls rather than flat white plastic.
        /// The source textures are not CPU-readable, so they are copied through a temporary RenderTexture.
        /// </summary>
        private static Texture2D PorcelainSkin(Texture source, LookDef def)
        {
            int w = source.width, h = source.height;
            var tex = ReadableCopy(source, "_Porcelain");
            var px = tex.GetPixels32();
            var cracks = CrackPixels();
            float gr = def.Glaze.r, gg = def.Glaze.g, gb = def.Glaze.b;
            for (int y = 0; y < h; y++)
            {
                int crackRow = (y * CrackSize / h) % CrackSize * CrackSize;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var c = px[i];
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    float pale = Mathf.Lerp(r * 0.299f + g * 0.587f + b * 0.114f, 1f, def.Lift);
                    float crack = Mathf.Lerp(1f, cracks[crackRow + (x * CrackSize / w) % CrackSize].r / 255f, def.Cracks);
                    px[i] = new Color32(
                        (byte)(Mathf.Clamp01(Mathf.Lerp(pale * gr, r, def.SkinTone) * crack) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.Lerp(pale * gg, g, def.SkinTone) * crack) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.Lerp(pale * gb, b, def.SkinTone) * crack) * 255f),
                        c.a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            tex.Compress(false);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Procedural glaze texture: off-white with faint mottling and dark hairline cracks (generated once, deterministic).</summary>
        public static Texture2D CrackTexture()
        {
            if (_cracks != null) return _cracks;
            _cracks = new Texture2D(CrackSize, CrackSize, TextureFormat.RGBA32, true) { name = "PorcelainCracks", wrapMode = TextureWrapMode.Repeat };
            _cracks.SetPixels32(CrackPixels());
            _cracks.Apply(true, true);
            return _cracks;
        }

        private static Color32[] CrackPixels()
        {
            if (_crackPixels != null) return _crackPixels;
            const int size = CrackSize;
            var px = new Color32[size * size];
            var rnd = new System.Random(1337);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.012f, y * 0.012f) * 0.06f + Mathf.PerlinNoise(x * 0.05f + 31f, y * 0.05f + 7f) * 0.03f;
                    byte v = (byte)(255 * Mathf.Min(1f, 0.94f + n));
                    px[y * size + x] = new Color32(v, v, v, 255);
                }
            }

            void Darken(int x, int y, float amount)
            {
                x = ((x % size) + size) % size;
                y = ((y % size) + size) % size;
                int i = y * size + x;
                var c = px[i];
                byte v = (byte)Mathf.Min(c.r, 255 * (1f - amount));
                px[i] = new Color32(v, v, v, 255);
            }

            void Crack(float x, float y, float angle, int steps, float strength, int depth)
            {
                for (int s = 0; s < steps; s++)
                {
                    angle += (float)(rnd.NextDouble() - 0.5) * 0.9f;
                    x += Mathf.Cos(angle) * 1.5f;
                    y += Mathf.Sin(angle) * 1.5f;
                    float a = strength * (1f - s / (float)steps * 0.6f);
                    Darken((int)x, (int)y, a);
                    Darken((int)x + 1, (int)y, a * 0.55f);
                    Darken((int)x, (int)y + 1, a * 0.55f);
                    if (depth < 2 && rnd.NextDouble() < 0.025)
                    {
                        Crack(x, y, angle + (rnd.NextDouble() < 0.5 ? 0.9f : -0.9f), steps / 2, strength * 0.8f, depth + 1);
                    }
                }
            }

            for (int i = 0; i < 26; i++)
            {
                Crack(rnd.Next(size), rnd.Next(size), (float)(rnd.NextDouble() * Mathf.PI * 2), rnd.Next(60, 180), 0.55f + (float)rnd.NextDouble() * 0.3f, 0);
            }

            _crackPixels = px;
            return px;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Converted.Clear();
            Vivid.Clear();
            _cracks = null;
            _crackPixels = null;
            _cutoutTemplate = null;
            _cutoutEmissiveTemplate = null;
            _fabricTemplate = null;
        }
    }
}
