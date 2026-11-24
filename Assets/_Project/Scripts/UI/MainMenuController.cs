using MaidCafe.AI;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>
    /// Main menu scene: a live background (a generated café room with a motionless porcelain maid staring
    /// at the camera, the music box lullaby playing) and the menu: Play (difficulty + optional seed), Settings, How to play, Credits, Quit.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        public const string GameScene = "Game";

        private GameObject _main;
        private GameObject _difficulty;
        private GameObject _settings;
        private GameObject _howTo;
        private GameObject _credits;
        private Selectable _firstMain;
        private Selectable _firstDifficulty;
        private Selectable _firstSettings;
        private Selectable _firstHowTo;
        private Selectable _firstCredits;
        private TMP_InputField _seedField;
        private Transform _camera;
        private Vector3 _cameraBase;
        private Quaternion _cameraRot;

        private void Start()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            AudioListener.volume = GameSettings.MasterVolume;
            GameInput.EnsureInitialized();
            GameInput.SetGameplayEnabled(false);
            GameInput.SetCursorLocked(false);
            UIFactory.EnsureEventSystem();
            gameObject.AddComponent<PostFX>();

            BuildBackground();
            BuildMenu();
            Show(_main, _firstMain);
        }

        private void Update()
        {
            if (_camera != null)
            {
                float t = Time.time;
                _camera.position = _cameraBase + new Vector3(Mathf.Sin(t * 0.21f) * 0.12f, Mathf.Sin(t * 0.33f) * 0.04f, 0f);
                _camera.rotation = _cameraRot * Quaternion.Euler(Mathf.Sin(t * 0.17f) * 1.2f, Mathf.Sin(t * 0.13f) * 2.5f, 0f);
            }

            if (GameInput.Pause.WasPressedThisFrame() && !_main.activeSelf) Show(_main, _firstMain);
        }

        // ------------------------------------------------------------------ background

        private void BuildBackground()
        {
            FloorBuilder.ApplyAtmosphere();
            var layout = LayoutGenerator.Generate(20261125, 2, 5);
            var floor = FloorBuilder.Build(layout, transform, 0);

            var outward = new Vector3(layout.Start.Outward.x, 0f, layout.Start.Outward.y);
            Vector3 camPos = FloorInstance.PortalFront(layout.Start, 0.9f) + Vector3.up * 1.6f;
            Vector3 forward = -outward;

            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            _camera = camGo.transform;
            _cameraBase = camPos;
            _cameraRot = Quaternion.LookRotation(forward + Vector3.down * 0.05f);
            _camera.SetPositionAndRotation(_cameraBase, _cameraRot);

            // A maid standing a few metres away, facing the camera, under a dim light.
            Vector3 maidPos = camPos - Vector3.up * 1.6f + forward * 5.5f;
            var maid = MaidFactory.Create(MaidKind.Head, new MaidStats(), floor, maidPos, 1, 9999f);
            // Silent backdrop: no footsteps or ray-traced hearing in the menu (there is no acoustic world here).
            foreach (var c in maid.GetComponents<MaidCafe.Audio.MaidAudio>()) c.enabled = false;
            foreach (var c in maid.GetComponentsInChildren<AudioSource>()) c.enabled = false;
            maid.enabled = false;
            var agent = maid.GetComponent<UnityEngine.AI.NavMeshAgent>();
            agent.isStopped = true;
            maid.transform.rotation = Quaternion.LookRotation(-forward);

            var lightGo = new GameObject("MaidSpot");
            lightGo.transform.position = maid.transform.position + Vector3.up * 2.8f + forward * -0.6f;
            lightGo.transform.rotation = Quaternion.LookRotation(Vector3.down);
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 55f;
            spot.range = 6f;
            spot.intensity = 2.5f;
            spot.color = new Color(1f, 0.75f, 0.8f);
            lightGo.AddComponent<FlickerLight>();

            // The objective's music box lullaby plays in the room behind the maid (plain AudioSource: the menu has no
            // acoustic world). Created after the maid's sources were disabled above, so it is not muted with them.
            var musicGo = new GameObject("MenuMusicBox");
            musicGo.transform.position = maid.transform.position + Vector3.up * 1.0f + forward * 1.5f;
            var music = musicGo.AddComponent<AudioSource>();
            music.clip = SoundBank.Get(Sfx.MusicBoxLoop);
            music.loop = true;
            music.playOnAwake = false;
            music.volume = 0.55f;
            music.spatialBlend = 0.35f; // mostly in the head, a little from the room in front
            music.rolloffMode = AudioRolloffMode.Linear;
            music.minDistance = 2f;
            music.maxDistance = 30f;
            music.dopplerLevel = 0f;
            if (music.clip != null) music.Play();
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            var canvas = UIFactory.Canvas("MainMenu", 20, transform);
            var t = canvas.transform;

            var shade = UIFactory.Image(t, "LeftShade", new Color(0f, 0f, 0f, 0.6f));
            UIFactory.Place(shade.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(780f, 1080f));

            // Main.
            var mainRoot = UIFactory.Rect("Main", t);
            UIFactory.Stretch(mainRoot);
            _main = mainRoot.gameObject;
            var title = UIFactory.LocText(mainRoot, "Title", "game.title", 88f, TextAlignmentOptions.Left, UIStyle.Accent);
            // One line in both languages: shrink rather than wrap into the tagline.
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true;
            title.fontSizeMin = 56f;
            title.fontSizeMax = 88f;
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -110f), new Vector2(1100f, 120f));
            var tagline = UIFactory.LocText(mainRoot, "Tagline", "game.tagline", 30f, TextAlignmentOptions.Left, UIStyle.Dim);
            UIFactory.Place(tagline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, -230f), new Vector2(1100f, 50f));

            var col = UIFactory.Column(mainRoot, "Buttons", 16f, TextAnchor.UpperLeft);
            UIFactory.Place((RectTransform)col.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f, -60f), new Vector2(480f, 460f));
            _firstMain = UIFactory.Button(col.transform, "ui.play", () => Show(_difficulty, _firstDifficulty));
            UIFactory.Button(col.transform, "ui.settings", () => Show(_settings, _firstSettings));
            UIFactory.Button(col.transform, "ui.howToPlay", () => Show(_howTo, _firstHowTo));
            UIFactory.Button(col.transform, "ui.credits", () => Show(_credits, _firstCredits));
            if (Application.platform != RuntimePlatform.WebGLPlayer) UIFactory.Button(col.transform, "ui.quit", Quit); // a browser tab cannot quit

            var version = UIFactory.Text(mainRoot, "Version", "v" + Application.version, 20f, TextAlignmentOptions.BottomRight, UIStyle.Dim);
            UIFactory.Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(400f, 30f));

            // Difficulty.
            var diffCol = MenuPanels.Frame(t, "Difficulty", "ui.chooseDifficulty", new Vector2(1100f, 860f), out _difficulty);
            diffCol.spacing = 10f;
            _firstDifficulty = DifficultyEntry(diffCol.transform, Difficulty.Easy);
            DifficultyEntry(diffCol.transform, Difficulty.Normal);
            DifficultyEntry(diffCol.transform, Difficulty.Hardcore);

            var seedRow = UIFactory.Row(diffCol.transform, "SeedRow");
            var seedLabel = UIFactory.LocText(seedRow, "Label", "ui.seed", 24f, TextAlignmentOptions.MidlineLeft, UIStyle.Dim);
            seedLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 300f;
            var seedGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            seedGo.transform.SetParent(seedRow, false);
            _seedField = seedGo.GetComponent<TMP_InputField>();
            _seedField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _seedField.characterLimit = 9;
            foreach (var txt in seedGo.GetComponentsInChildren<TMP_Text>(true))
            {
                txt.font = TMP_Settings.defaultFontAsset;
                txt.fontSize = 24f;
                txt.color = txt.gameObject.name == "Placeholder" ? UIStyle.Dim : Color.black;
            }
            var placeholder = _seedField.placeholder as TMP_Text;
            if (placeholder != null) placeholder.gameObject.AddComponent<LocalizedText>().Key = "ui.seedPlaceholder";
            var seedLe = seedGo.AddComponent<LayoutElement>();
            seedLe.preferredWidth = 360f;
            seedLe.preferredHeight = 44f;

            UIFactory.Button(diffCol.transform, "ui.back", () => Show(_main, _firstMain));

            _settings = MenuPanels.Settings(t, () => Show(_main, _firstMain), out _firstSettings);
            _howTo = MenuPanels.TextPanel(t, "HowTo", "ui.howToPlay", "ui.controls.body", () => Show(_main, _firstMain), out _firstHowTo);
            _credits = MenuPanels.TextPanel(t, "Credits", "ui.credits", "ui.credits.body", () => Show(_main, _firstMain), out _firstCredits);
        }

        private Selectable DifficultyEntry(Transform parent, Difficulty difficulty)
        {
            string id = difficulty.ToString().ToLowerInvariant();
            var button = UIFactory.Button(parent, "ui.diff." + id, () => StartGame(difficulty), new Vector2(560f, 64f), 32f);
            var desc = UIFactory.LocText(parent, "Desc_" + id, "ui.diff." + id + ".desc", 22f, TextAlignmentOptions.Center, UIStyle.Dim);
            var le = desc.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 960f;
            le.preferredHeight = 64f;
            return button;
        }

        private void StartGame(Difficulty difficulty)
        {
            int? seed = null;
            if (_seedField != null && int.TryParse(_seedField.text, out int parsed) && parsed >= 0) seed = parsed;
            RunConfig.NewRun(difficulty, seed);
            SceneManager.LoadScene(GameScene);
        }

        private void Show(GameObject panel, Selectable first)
        {
            _main.SetActive(panel == _main);
            _difficulty.SetActive(panel == _difficulty);
            _settings.SetActive(panel == _settings);
            _howTo.SetActive(panel == _howTo);
            _credits.SetActive(panel == _credits);
            UIFactory.Select(first);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
