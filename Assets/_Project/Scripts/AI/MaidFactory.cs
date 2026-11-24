using MaidCafe.Audio;
using MaidCafe.Generation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace MaidCafe.AI
{
    /// <summary>
    /// Builds maid characters: the imported porcelain models (see <see cref="MaidVisuals"/>) with the AI on top.
    /// Falls back to the greybox body if a model asset is missing, so the AI never depends on the visuals.
    /// </summary>
    public static class MaidFactory
    {
        /// <summary>A patrol maid or the Head Maid, with AI.</summary>
        /// <param name="patrolLook">Model for a patrol maid (one of <see cref="MaidVisuals.PatrolLooks"/>); ignored for the Head Maid.</param>
        public static MaidAI Create(MaidKind kind, MaidStats stats, FloorInstance floor, Vector3 position, int seed, float grace,
            MaidLook patrolLook = MaidLook.Patrol)
        {
            var root = new GameObject(kind == MaidKind.Head ? "HeadMaid" : "PatrolMaid");
            root.transform.SetParent(floor.DynamicRoot, false);
            if (NavMesh.SamplePosition(position, out var hit, 2f, NavAreas.Ground)) position = hit.position;
            root.transform.position = position;

            var rig = MaidVisuals.Build(kind == MaidKind.Head ? MaidLook.Head : patrolLook, root.transform);
            Transform eyes;
            EyeGlow[] glows;
            if (rig != null)
            {
                eyes = rig.Eyes;
                glows = rig.Glows;
            }
            else
            {
                BuildGreybox(kind, root.transform, out eyes, out glows);
            }

            var agent = root.AddComponent<NavMeshAgent>();
            agent.baseOffset = 0f;
            var ai = root.AddComponent<MaidAI>();
            ai.Init(floor, kind, stats, eyes, glows, seed, grace);
            var animator = root.AddComponent<MaidAnimator>();
            if (rig != null) animator.Bind(rig.Animator, rig.Head);

            // Ray-traced hearing (her ear at her eyes) and her own sounds (heel clacks, shriek, her model's voice).
            root.AddComponent<MaidHearing>().Init(() => ai.HeadPosition);
            root.AddComponent<MaidAudio>().Init(kind == MaidKind.Head ? MaidLook.Head : patrolLook);
            return ai;
        }

        /// <summary>
        /// The watcher maid: stands at a post, rings a hand bell to call the others when she sees the player,
        /// and is pacified by a gift. She never chases or touches the player.
        /// </summary>
        public static WatcherMaid CreateWatcher(MaidStats stats, FloorInstance floor, Vector3 position, Quaternion facing, float grace)
        {
            var root = new GameObject("WatcherMaid");
            root.transform.SetParent(floor.DynamicRoot, false);
            if (NavMesh.SamplePosition(position, out var hit, 1.5f, NavAreas.Ground)) position = hit.position;
            root.transform.SetPositionAndRotation(position, facing);

            var rig = MaidVisuals.Build(MaidLook.Watcher, root.transform);
            Transform eyes;
            EyeGlow[] glows;
            Animator animator = null;
            Transform head = null;
            if (rig != null)
            {
                eyes = rig.Eyes;
                glows = rig.Glows;
                animator = rig.Animator;
                head = rig.Head;
                MaidVisuals.AttachBell(rig);
            }
            else
            {
                BuildGreybox(MaidKind.Patrol, root.transform, out eyes, out glows);
                head = root.transform.Find("Model/Head");
            }

            // Solid body: the imported model brings capsules fitted to its body (MaidBodyColliders), which the
            // interactor also hits; the greybox fallback gets one capsule.
            if (rig == null)
            {
                var body = root.AddComponent<CapsuleCollider>();
                body.center = new Vector3(0f, 0.8f, 0f);
                body.height = 1.6f;
                body.radius = 0.28f;
            }
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = new Vector3(0f, 0.8f, 0f);
            obstacle.height = 1.6f;
            obstacle.radius = 0.3f;
            obstacle.carving = false;

            var watcher = root.AddComponent<WatcherMaid>();
            watcher.Init(stats, eyes, glows, animator, head, grace);
            root.AddComponent<MaidAudio>(); // her hand bell and voice
            return watcher;
        }

        // ------------------------------------------------------------------ greybox fallback

        private static void BuildGreybox(MaidKind kind, Transform root, out Transform eyes, out EyeGlow[] glows)
        {
            float scale = kind == MaidKind.Head ? 1.12f : 1f;
            var model = new GameObject("Model").transform;
            model.SetParent(root, false);
            model.localScale = Vector3.one * scale;

            var porcelain = MaterialLibrary.Get("MaidPorcelain", new Color(0.93f, 0.91f, 0.88f), 0.85f);
            var dress = kind == MaidKind.Head
                ? MaterialLibrary.Get("MaidDressHead", new Color(0.08f, 0.02f, 0.04f), 0.35f)
                : MaterialLibrary.Get("MaidDress", new Color(0.06f, 0.06f, 0.08f), 0.3f);
            var apron = MaterialLibrary.Get("MaidApron", new Color(0.88f, 0.86f, 0.84f), 0.2f);
            var eyeMat = MaterialLibrary.Get("MaidEye", Color.black, 0.9f, new Color(0.25f, 0.05f, 0.05f));

            Part(model, PrimitiveType.Cylinder, "Skirt", new Vector3(0f, 0.45f, 0f), new Vector3(0.62f, 0.45f, 0.62f), dress);
            Part(model, PrimitiveType.Cube, "Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.36f, 0.5f, 0.22f), dress);
            Part(model, PrimitiveType.Cube, "Apron", new Vector3(0f, 0.85f, 0.12f), new Vector3(0.34f, 0.75f, 0.02f), apron);
            Part(model, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 1.45f, 0f), new Vector3(0.08f, 0.06f, 0.08f), porcelain);
            Part(model, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.62f, 0f), new Vector3(0.26f, 0.3f, 0.26f), porcelain);
            Part(model, PrimitiveType.Cube, "Headdress", new Vector3(0f, 1.78f, -0.02f), new Vector3(0.24f, 0.05f, 0.1f), apron);
            Part(model, PrimitiveType.Cube, "ArmL", new Vector3(-0.23f, 1.05f, 0.05f), new Vector3(0.08f, 0.55f, 0.08f), porcelain);
            Part(model, PrimitiveType.Cube, "ArmR", new Vector3(0.23f, 1.05f, 0.05f), new Vector3(0.08f, 0.55f, 0.08f), porcelain);
            var eyeL = Part(model, PrimitiveType.Sphere, "EyeL", new Vector3(-0.055f, 1.65f, 0.115f), new Vector3(0.045f, 0.03f, 0.02f), eyeMat);
            var eyeR = Part(model, PrimitiveType.Sphere, "EyeR", new Vector3(0.055f, 1.65f, 0.115f), new Vector3(0.045f, 0.03f, 0.02f), eyeMat);

            eyes = new GameObject("Eyes").transform;
            eyes.SetParent(model, false);
            eyes.localPosition = new Vector3(0f, 1.62f, 0.12f);
            glows = new[]
            {
                new EyeGlow { Renderer = eyeL.GetComponent<Renderer>(), MaterialIndex = -1 },
                new EyeGlow { Renderer = eyeR.GetComponent<Renderer>(), MaterialIndex = -1 }
            };
        }

        private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }
    }
}
