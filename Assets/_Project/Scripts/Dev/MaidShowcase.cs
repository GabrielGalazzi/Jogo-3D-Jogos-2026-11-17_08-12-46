using System.Collections.Generic;
using MaidCafe.AI;
using MaidCafe.Generation;
using MaidCafe.Localization;
using MaidCafe.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MaidCafe.Dev
{
    /// <summary>
    /// Model viewer (Scenes/Showcase.unity, editor/dev tool, not in the build list): the three maids stand in a row
    /// on a platform, without AI, and all play the same animation in place through the game's Animator Controller
    /// (so the rest pose, porcelain materials and body colliders are exactly as in the game). Nothing here can catch
    /// or hurt the viewer.
    /// Keys: E or Right Arrow = next animation, Q or Left Arrow = previous, H = show the body colliders.
    /// Movement is handled by <see cref="ShowcaseViewer"/>.
    /// </summary>
    public class MaidShowcase : MonoBehaviour
    {
        private const float Spacing = 2.6f;
        private const float OneShotPause = 0.8f;
        private static readonly Color Background = new Color(0.085f, 0.08f, 0.095f);

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ChasingId = Animator.StringToHash("Chasing");
        private static readonly int SearchingId = Animator.StringToHash("Searching");
        private static readonly int RingingId = Animator.StringToHash("Ringing");

        /// <summary>One entry of the animation list: which controller state to show and the parameters that keep it there.</summary>
        private struct Clip
        {
            public string NameKey;
            public string State;
            public float Speed;
            public bool Chasing, Searching, Ringing;
            public bool OneShot; // non-looping clip: replayed after a short pause
        }

        // Speeds match the authored clips (MaidAnimator: walk 1.1 m/s, run 3.94 m/s at 1x).
        private static readonly Clip[] Clips =
        {
            new Clip { NameKey = "showcase.anim.idle", State = "Locomotion" },
            new Clip { NameKey = "showcase.anim.walk", State = "Locomotion", Speed = 1.1f },
            new Clip { NameKey = "showcase.anim.run", State = "Locomotion", Speed = 3.94f, Chasing = true },
            new Clip { NameKey = "showcase.anim.search", State = "Search", Searching = true },
            new Clip { NameKey = "showcase.anim.ring", State = "Ring", Ringing = true },
            new Clip { NameKey = "showcase.anim.catch", State = "Catch", OneShot = true },
            new Clip { NameKey = "showcase.anim.curtsy", State = "Curtsy", OneShot = true }
        };

        private static readonly (MaidLook Look, string NameKey)[] Maids =
        {
            (MaidLook.StyleAnime, "showcase.maid.patrolStyle"),
            (MaidLook.Patrol, "showcase.maid.patrol"),
            (MaidLook.Head, "showcase.maid.head"),
            (MaidLook.Watcher, "showcase.maid.watcher"),
            (MaidLook.Typical, "showcase.maid.patrolTypical")
        };

        private readonly List<Animator> _animators = new List<Animator>();
        private readonly List<float> _replayAt = new List<float>();
        private readonly List<Transform> _labels = new List<Transform>();
        private readonly List<CapsuleCollider> _colliders = new List<CapsuleCollider>();
        private readonly List<GameObject> _colliderViews = new List<GameObject>();
        private ShowcaseViewer _viewer;
        private TextMeshProUGUI _status;
        private Material _colliderMaterial;
        private int _index;
        private bool _showColliders;
        private bool _shownFlying;

        private void Start()
        {
            BuildEnvironment();
            for (int i = 0; i < Maids.Length; i++) BuildMaid(i);
            _viewer = ShowcaseViewer.Create(new Vector3(0f, 0.05f, -5f), 0f, Background);
            BuildHud();
            Loc.LanguageChanged += RefreshStatus;
            Play(0);
        }

        private void OnDestroy() => Loc.LanguageChanged -= RefreshStatus;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.eKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Play(_index + 1);
                if (kb.qKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) Play(_index - 1);
                if (kb.hKey.wasPressedThisFrame) ToggleColliders();
            }
            if (_viewer != null && _viewer.Flying != _shownFlying) RefreshStatus();
            ReplayOneShots();
        }

        private void LateUpdate()
        {
            // Name tags face the viewer.
            if (_viewer == null) return;
            Vector3 eye = _viewer.Camera.transform.position;
            foreach (var label in _labels) label.rotation = Quaternion.LookRotation(label.position - eye);
        }

        // ------------------------------------------------------------------ animations

        private void Play(int index)
        {
            _index = (index % Clips.Length + Clips.Length) % Clips.Length;
            var clip = Clips[_index];
            for (int i = 0; i < _animators.Count; i++)
            {
                var a = _animators[i];
                a.SetFloat(SpeedId, clip.Speed);
                a.SetBool(ChasingId, clip.Chasing);
                a.SetBool(SearchingId, clip.Searching);
                a.SetBool(RingingId, clip.Ringing);
                a.CrossFadeInFixedTime(clip.State, 0.2f, 0, 0f);
                _replayAt[i] = -1f;
            }
            RefreshStatus();
        }

        /// <summary>Catch and Curtsy do not loop: once one ends (or the controller leaves it), play it again after a pause.</summary>
        private void ReplayOneShots()
        {
            var clip = Clips[_index];
            if (!clip.OneShot) return;
            for (int i = 0; i < _animators.Count; i++)
            {
                var a = _animators[i];
                if (a.IsInTransition(0)) continue;
                var state = a.GetCurrentAnimatorStateInfo(0);
                bool finished = !state.IsName(clip.State) || state.normalizedTime >= 1f;
                if (!finished)
                {
                    _replayAt[i] = -1f;
                    continue;
                }
                if (_replayAt[i] < 0f) _replayAt[i] = Time.time + OneShotPause;
                else if (Time.time >= _replayAt[i])
                {
                    _replayAt[i] = -1f;
                    if (state.IsName(clip.State)) a.Play(clip.State, 0, 0f);
                    else a.CrossFadeInFixedTime(clip.State, 0.2f, 0, 0f);
                }
            }
        }

        // ------------------------------------------------------------------ scene

        private void BuildEnvironment()
        {
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.37f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.23f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.09f, 0.1f);

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.Soft;
            key.transform.SetParent(transform, false);
            key.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.75f, 0.8f, 1f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fill.transform.SetParent(transform, false);
            fill.transform.rotation = Quaternion.Euler(25f, 150f, 0f);

            // A Plane is one-sided: solid and visible from above, see-through from below.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(4f, 1f, 4f); // 40 x 40 m
            var floorMat = new Material(MaterialLibrary.Get("ShowcaseFloor", new Color(0.36f, 0.34f, 0.37f), 0.15f));
            floorMat.SetTexture("_BaseMap", GridTexture(new Color32(255, 255, 255, 255), new Color32(150, 150, 160, 255)));
            floorMat.SetTextureScale("_BaseMap", new Vector2(40f, 40f)); // 1 m cells
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        }

        private void BuildMaid(int i)
        {
            var holder = new GameObject(Maids[i].Look.ToString()).transform;
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3((i - (Maids.Length - 1) * 0.5f) * Spacing, 0f, 0f);
            holder.localRotation = Quaternion.Euler(0f, 180f, 0f); // facing the viewer's start

            var rig = MaidVisuals.Build(Maids[i].Look, holder);
            if (rig == null) return;
            if (Maids[i].Look == MaidLook.Watcher) MaidVisuals.AttachBell(rig);
            _animators.Add(rig.Animator);
            _replayAt.Add(-1f);
            if (rig.BodyColliders != null) _colliders.AddRange(rig.BodyColliders);

            float top = 0f;
            foreach (var r in rig.Model.GetComponentsInChildren<SkinnedMeshRenderer>()) top = Mathf.Max(top, r.bounds.max.y);
            // Created with a RectTransform: adding TextMeshPro would otherwise replace (and invalidate) the Transform.
            var label = new GameObject("Label", typeof(RectTransform)).transform;
            label.SetParent(holder, false);
            label.position = new Vector3(holder.position.x, top + 0.3f, holder.position.z);
            var tmp = label.gameObject.AddComponent<TextMeshPro>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = 2.2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UIStyle.Accent;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
            label.gameObject.AddComponent<LocalizedText>().Key = Maids[i].NameKey;
            _labels.Add(label);
        }

        private void BuildHud()
        {
            var canvas = UIFactory.Canvas("ShowcaseHUD", 10, transform);
            var title = UIFactory.LocText(canvas.transform, "Title", "showcase.title", 40f, TextAlignmentOptions.TopLeft, UIStyle.Accent);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(1200f, 60f));

            _status = UIFactory.Text(canvas.transform, "Status", string.Empty, 30f, TextAlignmentOptions.TopLeft);
            UIFactory.Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -90f), new Vector2(1200f, 100f));

            var help = UIFactory.LocText(canvas.transform, "Controls", "showcase.controls", 24f, TextAlignmentOptions.BottomLeft, UIStyle.Dim);
            UIFactory.Place(help.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(1400f, 160f));
        }

        private void RefreshStatus()
        {
            if (_status == null) return;
            string anim = Loc.Format("showcase.animation", Loc.Get(Clips[_index].NameKey), _index + 1, Clips.Length);
            _shownFlying = _viewer != null && _viewer.Flying;
            string mode = Loc.Get(_shownFlying ? "showcase.mode.fly" : "showcase.mode.walk");
            _status.text = anim + "\n" + mode;
        }

        // ------------------------------------------------------------------ body colliders

        private void ToggleColliders()
        {
            _showColliders = !_showColliders;
            if (_showColliders && _colliderViews.Count == 0)
            {
                foreach (var capsule in _colliders) _colliderViews.Add(ColliderView(capsule));
            }
            foreach (var view in _colliderViews) view.SetActive(_showColliders);
        }

        /// <summary>A see-through grid capsule matching a collider.</summary>
        private GameObject ColliderView(CapsuleCollider capsule)
        {
            if (_colliderMaterial == null)
            {
                var template = Resources.Load<Material>("MaterialTemplates/LitCutoutEmissive");
                _colliderMaterial = new Material(template);
                _colliderMaterial.SetTexture("_BaseMap", GridTexture(new Color32(0, 0, 0, 0), new Color32(255, 255, 255, 255)));
                _colliderMaterial.SetTextureScale("_BaseMap", new Vector2(6f, 4f));
                _colliderMaterial.SetColor("_BaseColor", new Color(0.3f, 1f, 0.5f));
                _colliderMaterial.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 0.4f));
            }
            var view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(view.GetComponent<Collider>());
            view.name = "View";
            view.transform.SetParent(capsule.transform, false);
            view.transform.localPosition = capsule.center;
            view.transform.localScale = new Vector3(capsule.radius * 2f, capsule.height * 0.5f, capsule.radius * 2f);
            var renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _colliderMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return view;
        }

        /// <summary>Square grid texture: <paramref name="fill"/> cells with <paramref name="line"/> borders.</summary>
        private static Texture2D GridTexture(Color32 fill, Color32 line)
        {
            const int size = 64;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool border = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    px[y * size + x] = border ? line : fill;
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
