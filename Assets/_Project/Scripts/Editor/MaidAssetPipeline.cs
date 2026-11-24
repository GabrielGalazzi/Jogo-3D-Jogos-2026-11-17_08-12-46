using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MaidCafe.EditorTools
{
    /// <summary>
    /// Import rules for the maid characters and their animations, plus the generated Animator Controller.
    /// - Models in Resources/Maids/*: Humanoid rig, blend shapes, no animation; textures capped at 1024 px.
    /// - Animations/MaidAnimations.fbx: Humanoid clips authored once in Blender (ArtSource/Work/Maids_work.blend)
    ///   and retargeted to every maid. Clip names come from the Blender action names.
    /// - Resources/Maids/MaidAnimator.controller is rebuilt automatically when the animation file is imported
    ///   (or from the menu MaidCafe → Art → Rebuild Maid Animator).
    /// </summary>
    public class MaidAssetPipeline : AssetPostprocessor
    {
        public const string ModelsFolder = "Assets/_Project/Resources/Maids/";
        public const string AnimationsPath = "Assets/_Project/Animations/MaidAnimations.fbx";
        public const string ControllerPath = "Assets/_Project/Resources/Maids/MaidAnimator.controller";

        /// <summary>Model whose armature the clips were authored on (its rest pose is the clips' reference pose).</summary>
        private const string AnimationRigSource = ModelsFolder + "MaidPigtails/MaidPigtails.fbx";

        private const string HipsBone = "J_Bip_C_Hips";

        private static readonly HashSet<string> LoopingClips = new HashSet<string> { "Idle", "Walk", "Run", "Search", "Ring" };

        private static bool IsMaidModel(string path) => path.StartsWith(ModelsFolder) && path.EndsWith(".fbx");

        private void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            bool isModel = IsMaidModel(assetPath);
            bool isAnim = assetPath == AnimationsPath;
            if (!isModel && !isAnim) return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            // The auto-mapper picks a hair bone as "Jaw" on these rigs (the jaw is never animated), so drop it.
            var description = importer.humanDescription;
            if (description.human != null && description.human.Any(h => h.humanName == "Jaw"))
            {
                description.human = description.human.Where(h => h.humanName != "Jaw").ToArray();
                importer.humanDescription = description;
            }

            // It can also pick the "Root" helper bone (at the feet) as Hips (seen on MaidStyleAnime), which breaks
            // every hips-relative pose. All maid rigs have the VRoid hips bone, so enforce it.
            if (description.human != null && description.skeleton != null && description.skeleton.Any(b => b.name == HipsBone))
            {
                var human = description.human;
                for (int i = 0; i < human.Length; i++)
                {
                    if (human[i].humanName == "Hips" && human[i].boneName != HipsBone)
                    {
                        human[i].boneName = HipsBone;
                        description.human = human;
                        importer.humanDescription = description;
                        break;
                    }
                }
            }
            if (isAnim) UseModelReferencePose(importer);

            importer.optimizeGameObjects = false;
            importer.importBlendShapes = isModel;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            // The maid components measure each model's mesh at runtime (rest pose, skirt cone, body colliders) and
            // the skirt flare re-skins a copy of it. A player can only read meshes imported with Read/Write.
            importer.isReadable = isModel;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importAnimation = isAnim;
            importer.materialImportMode = isModel ? ModelImporterMaterialImportMode.ImportViaMaterialDescription : ModelImporterMaterialImportMode.None;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        /// <summary>
        /// The animation file holds only the armature, so Unity takes its avatar reference pose from the node
        /// transforms stored in the file, which are a frame of an action (knees bent ~40°, torso tilted ~7°), not
        /// the rest pose. Every clip is converted to muscle values relative to that pose, which made straight legs
        /// retarget as knees bent backwards on every maid. The clips were authored on the MaidPigtails armature, so its
        /// model file (skinned, saved in the rest pose) provides the correct reference pose for the shared bones.
        /// </summary>
        private static void UseModelReferencePose(ModelImporter importer)
        {
            var source = AssetImporter.GetAtPath(AnimationRigSource) as ModelImporter;
            if (source == null || source.humanDescription.skeleton == null) return;
            var restPose = new Dictionary<string, SkeletonBone>();
            foreach (var bone in source.humanDescription.skeleton)
            {
                if (bone.name.StartsWith("J_Bip_") || bone.name.StartsWith("J_Sec_") || bone.name.StartsWith("J_Adj_")) restPose[bone.name] = bone;
            }

            var description = importer.humanDescription;
            if (description.skeleton == null) return;
            var skeleton = description.skeleton;
            int replaced = 0;
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (!restPose.TryGetValue(skeleton[i].name, out var rest)) continue;
                skeleton[i].position = rest.position;
                skeleton[i].rotation = rest.rotation;
                skeleton[i].scale = rest.scale;
                replaced++;
            }
            if (replaced == 0) return;
            description.skeleton = skeleton;
            importer.humanDescription = description;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != AnimationsPath) return;
            var importer = (ModelImporter)assetImporter;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var clip in importer.defaultClipAnimations)
            {
                string take = clip.takeName;
                int bar = take.LastIndexOf('|');
                string name = bar >= 0 ? take.Substring(bar + 1) : take;
                clip.name = name;
                clip.loopTime = LoopingClips.Contains(name);
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clips.Add(clip);
            }
            importer.clipAnimations = clips.ToArray();
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ModelsFolder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Contains(AnimationsPath))
            {
                EditorApplication.delayCall += RebuildAnimator;
            }
        }

        [MenuItem("MaidCafe/Art/Reimport Maid Assets")]
        public static void ReimportAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Resources/Maids", "Assets/_Project/Animations" }))
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
            RebuildAnimator();
        }

        [MenuItem("MaidCafe/Art/Rebuild Maid Animator")]
        public static void RebuildAnimator()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimationsPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => c.name)
                .ToDictionary(g => g.Key, g => g.First());
            string[] required = { "Idle", "Walk", "Run", "Search", "Catch", "Ring", "Curtsy" };
            var missing = required.Where(r => !clips.ContainsKey(r)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError($"[MaidAssetPipeline] Missing clips in {AnimationsPath}: {string.Join(", ", missing)}");
                return;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter("Chasing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Searching", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Ringing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Catch", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Curtsy", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walk"], 0.9f);
            tree.AddChild(clips["Walk"], 2.4f);
            tree.AddChild(clips["Run"], 3.4f);
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = "AnimSpeed";
            sm.defaultState = locomotion;

            var search = sm.AddState("Search");
            search.motion = clips["Search"];
            var toSearch = locomotion.AddTransition(search);
            toSearch.hasExitTime = false;
            toSearch.duration = 0.25f;
            toSearch.AddCondition(AnimatorConditionMode.If, 0f, "Searching");
            toSearch.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
            var searchToMove = search.AddTransition(locomotion);
            searchToMove.hasExitTime = false;
            searchToMove.duration = 0.2f;
            searchToMove.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
            var searchEnd = search.AddTransition(locomotion);
            searchEnd.hasExitTime = false;
            searchEnd.duration = 0.3f;
            searchEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, "Searching");

            var ring = sm.AddState("Ring");
            ring.motion = clips["Ring"];
            var toRing = locomotion.AddTransition(ring);
            toRing.hasExitTime = false;
            toRing.duration = 0.2f;
            toRing.AddCondition(AnimatorConditionMode.If, 0f, "Ringing");
            var ringEnd = ring.AddTransition(locomotion);
            ringEnd.hasExitTime = false;
            ringEnd.duration = 0.35f;
            ringEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, "Ringing");

            var curtsy = sm.AddState("Curtsy");
            curtsy.motion = clips["Curtsy"];
            var toCurtsy = sm.AddAnyStateTransition(curtsy);
            toCurtsy.hasExitTime = false;
            toCurtsy.duration = 0.2f;
            toCurtsy.canTransitionToSelf = false;
            toCurtsy.AddCondition(AnimatorConditionMode.If, 0f, "Curtsy");
            var curtsyEnd = curtsy.AddTransition(locomotion);
            curtsyEnd.hasExitTime = true;
            curtsyEnd.exitTime = 0.95f;
            curtsyEnd.duration = 0.2f;

            var caught = sm.AddState("Catch");
            caught.motion = clips["Catch"];
            var toCatch = sm.AddAnyStateTransition(caught);
            toCatch.hasExitTime = false;
            toCatch.duration = 0.08f;
            toCatch.canTransitionToSelf = false;
            toCatch.AddCondition(AnimatorConditionMode.If, 0f, "Catch");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MaidAssetPipeline] Rebuilt {ControllerPath} with {clips.Count} clips.");
        }
    }
}
