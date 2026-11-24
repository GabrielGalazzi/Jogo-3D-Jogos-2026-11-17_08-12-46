using System.Collections;
using MaidCafe.AI;
using MaidCafe.Audio;
using MaidCafe.Generation;
using MaidCafe.Localization;
using MaidCafe.Objectives;
using MaidCafe.Player;
using MaidCafe.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MaidCafe.Core
{
    public enum GameState
    {
        Loading,
        Playing,
        Paused,
        Caught,
        Transition,
        Ended
    }

    /// <summary>
    /// Game-flow state machine for the Game scene:
    /// Loading → Playing ⇄ Paused → Caught → (checkpoint | floor reload | game over) → Transition → … → Victory.
    /// Owns the floor/objective/enemy directors and the HUD, fader, pause and end screens.
    /// Includes anti-softlock safety nets (fall-through watchdog, "return to last safe point").
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenu";

        [SerializeField] private Difficulty editorDifficulty = Difficulty.Normal;

        public static GameManager Instance { get; private set; }
        public GameState State { get; private set; } = GameState.Loading;

        private FloorDirector _floors;
        private AcousticWorld _acoustics;
        private ObjectiveRunner _objectives;
        private EnemyDirector _enemies;
        private PlayerContext _player;
        private HUD _hud;
        private ScreenFader _fader;
        private PauseMenu _pause;
        private EndScreen _end;
        private float _watchdogTimer;
        private float _airTime;

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            GameInput.EnsureInitialized();
            UIFactory.EnsureEventSystem();
            FloorBuilder.ApplyAtmosphere();

            _floors = GetOrAdd<FloorDirector>();
            // After the FloorDirector: it voxelizes every floor the director builds (ray-traced audio).
            _acoustics = GetOrAdd<AcousticWorld>();
            SoundBank.Preload(); // synthesize the sound effects while the screen is still black
            _objectives = GetOrAdd<ObjectiveRunner>();
            _enemies = GetOrAdd<EnemyDirector>();
            GetOrAdd<TensionStingers>(); // after SoundBank.Preload: decodes the violin stingers
            GetOrAdd<PostFX>();
            _hud = GetOrAdd<HUD>();
            _fader = GetOrAdd<ScreenFader>();
            _pause = GetOrAdd<PauseMenu>();
            _end = GetOrAdd<EndScreen>();
            _floors.Populate = _objectives.Populate;

            _pause.ResumeRequested += Resume;
            _pause.SafePointRequested += ReturnToSafePoint;
            _pause.MainMenuRequested += GoToMainMenu;
            _pause.QuitRequested += Quit;
            GameEvents.PlayerCaught += OnPlayerCaught;
            GameSettings.Changed += ApplyAudioSettings;
            ApplyAudioSettings();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameEvents.PlayerCaught -= OnPlayerCaught;
            GameSettings.Changed -= ApplyAudioSettings;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private T GetOrAdd<T>() where T : Component
        {
            var c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }

        private void Start()
        {
            // Launched straight into the Game scene from the editor: start a default run.
            if (!RunConfig.HasActiveRun) RunConfig.NewRun(editorDifficulty);
            StartCoroutine(EnterFloor(1.2f));
        }

        // ------------------------------------------------------------------ floor flow

        private IEnumerator EnterFloor(float holdBlack)
        {
            State = GameState.Loading;
            GameInput.SetGameplayEnabled(false);
            _fader.SetAlpha(1f);
            BuildCurrentFloor();

            var obj = _objectives.Current;
            string floorTitle = FloorTitle();
            _fader.SetText(floorTitle, obj != null ? obj.Title : string.Empty);
            yield return new WaitForSecondsRealtime(holdBlack);

            _hud.ShowBanner(floorTitle, obj != null ? obj.Title : string.Empty, 4f);
            GameInput.SetCursorLocked(true);
            GameInput.SetGameplayEnabled(true);
            State = GameState.Playing;
            yield return _fader.Fade(0f, 0.9f);
            _fader.SetText(string.Empty);
        }

        private string FloorTitle()
        {
            return RunConfig.IsFinalFloor
                ? Loc.Get("ui.hud.finalFloor")
                : Loc.Format("ui.hud.floor", RunConfig.CurrentFloor + 1, RunConfig.FloorCount);
        }

        private void BuildCurrentFloor()
        {
            _objectives.Detach();
            _enemies.Clear();
            var floor = _floors.BuildFloor(RunConfig.CurrentFloor, RunConfig.FloorCount, RunConfig.FloorSeed(RunConfig.CurrentFloor));

            if (_player == null) _player = PlayerFactory.Create(floor.SpawnPosition, floor.SpawnYaw);
            else
            {
                _player.ResetState();
                _player.Teleport(floor.SpawnPosition, floor.SpawnYaw);
            }
            _player.Phone.ResetBattery(Mathf.Max(_player.Phone.Battery01, 0.5f));
            _player.Gifts = 0;

            floor.StartCar.OpenForArrival();
            floor.ExitCar.PlayerBoarded += OnExitBoarded;
            _objectives.Activate(floor);
            _enemies.SpawnFor(floor);
            _airTime = 0f;
        }

        private void OnExitBoarded()
        {
            if (State != GameState.Playing) return;
            StartCoroutine(LeaveFloor());
        }

        private IEnumerator LeaveFloor()
        {
            State = GameState.Transition;
            GameInput.SetGameplayEnabled(false);
            _fader.SetText(string.Empty);
            yield return _fader.Fade(1f, 1.0f);

            if (RunConfig.IsFinalFloor)
            {
                ShowVictory();
                yield break;
            }
            RunConfig.CurrentFloor++;
            yield return EnterFloor(1.6f);
        }

        // ------------------------------------------------------------------ death

        private void OnPlayerCaught(Transform attacker)
        {
            if (State != GameState.Playing || _player == null) return;
            StartCoroutine(HandleCaught(attacker));
        }

        private IEnumerator HandleCaught(Transform attacker)
        {
            State = GameState.Caught;
            RunConfig.Deaths++;
            var maid = attacker.GetComponent<MaidAI>();
            yield return CaughtSequence.Play(_player, maid != null ? maid.HeadPosition : attacker.position + Vector3.up * 1.6f);

            _fader.SetText(Loc.Get("ui.caught.title"), Loc.Get(CaughtSubtitleKey()));
            yield return _fader.Fade(1f, 0.25f);
            yield return new WaitForSecondsRealtime(1.6f);

            switch (RunConfig.Difficulty)
            {
                case Difficulty.Easy:
                    _player.ResetState();
                    _player.Teleport(_objectives.CheckpointPosition, _objectives.CheckpointYaw);
                    _enemies.RelocateAwayFrom(_objectives.CheckpointPosition);
                    GameInput.SetGameplayEnabled(true);
                    State = GameState.Playing;
                    yield return _fader.Fade(0f, 0.8f);
                    _fader.SetText(string.Empty);
                    break;

                case Difficulty.Normal:
                    yield return EnterFloor(0.4f);
                    break;

                default:
                    ShowGameOver();
                    break;
            }
        }

        private static string CaughtSubtitleKey()
        {
            switch (RunConfig.Difficulty)
            {
                case Difficulty.Easy: return "ui.caught.easy";
                case Difficulty.Normal: return "ui.caught.normal";
                default: return "ui.caught.hardcore";
            }
        }

        // ------------------------------------------------------------------ end screens

        private string Stats()
        {
            int minutes = Mathf.FloorToInt(RunConfig.RunTime / 60f);
            int seconds = Mathf.FloorToInt(RunConfig.RunTime % 60f);
            return Loc.Format("ui.stats", RunConfig.CurrentFloor + 1, RunConfig.FloorCount, $"{minutes:00}:{seconds:00}",
                RunConfig.Deaths, Loc.Get("ui.diff." + RunConfig.Difficulty.ToString().ToLowerInvariant()), RunConfig.RunSeed);
        }

        private void ShowVictory()
        {
            State = GameState.Ended;
            _enemies.Clear();
            _floors.ClearFloor();
            GameInput.SetGameplayEnabled(false);
            GameInput.SetCursorLocked(false);
            _hud.SetVisible(false);
            FreezePlayer();
            _end.ShowVictory(Stats(), () => RestartRun(keepStats: false), GoToMainMenu);
            RunConfig.EndRun();
        }

        private void ShowGameOver()
        {
            State = GameState.Ended;
            GameInput.SetGameplayEnabled(false);
            GameInput.SetCursorLocked(false);
            _hud.SetVisible(false);
            FreezePlayer();
            _end.ShowGameOver(Stats(), () => RestartRun(keepStats: true), GoToMainMenu);
        }

        private void FreezePlayer()
        {
            if (_player == null) return;
            _player.MovementLocked = true;
            _player.Controller.enabled = false;
        }

        /// <summary>New run with a new seed. Hardcore retries keep the death/time statistics.</summary>
        private void RestartRun(bool keepStats)
        {
            _end.Hide();
            _hud.SetVisible(true);
            if (keepStats) RunConfig.RestartWithNewSeed();
            else RunConfig.NewRun(RunConfig.Difficulty);
            StartCoroutine(EnterFloor(1.2f));
        }

        // ------------------------------------------------------------------ pause

        private void Update()
        {
            if (State == GameState.Playing) RunConfig.RunTime += Time.deltaTime;

            if (GameInput.Pause.WasPressedThisFrame())
            {
                if (State == GameState.Playing) Pause();
                else if (State == GameState.Paused)
                {
                    if (_pause.InSubPanel) _pause.Back();
                    else Resume();
                }
            }

            if (State == GameState.Playing) Watchdog();
            DevKeys();
        }

        private void Pause()
        {
            State = GameState.Paused;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            GameInput.SetGameplayEnabled(false);
            GameInput.SetCursorLocked(false);
            _pause.Open();
        }

        private void Resume()
        {
            _pause.Close();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GameInput.SetCursorLocked(true);
            GameInput.SetGameplayEnabled(true);
            State = GameState.Playing;
        }

        /// <summary>Anti-softlock: teleports the player back to the last checkpoint (not allowed mid-chase).</summary>
        private void ReturnToSafePoint()
        {
            if (MaidAI.AnyChasing)
            {
                Resume();
                GameEvents.ShowHint("hint.noSafePointInChase");
                return;
            }
            Resume();
            _player.ResetState();
            _player.Teleport(_objectives.CheckpointPosition, _objectives.CheckpointYaw);
            GameEvents.ShowHint("hint.safePoint");
        }

        private void GoToMainMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            RunConfig.EndRun();
            SceneManager.LoadScene(MainMenuScene);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void ApplyAudioSettings() => AudioListener.volume = GameSettings.MasterVolume;

        // ------------------------------------------------------------------ safety nets

        private void Watchdog()
        {
            if (_player == null || _player.IsHiding) return;
            if (!_player.Controller.enabled) return;

            _airTime = _player.Controller.isGrounded ? 0f : _airTime + Time.deltaTime;
            _watchdogTimer -= Time.deltaTime;
            if (_watchdogTimer > 0f) return;
            _watchdogTimer = 0.5f;

            // Fell through the world or stuck falling: put the player back on solid ground.
            if (_player.transform.position.y < -3f || _airTime > 4f)
            {
                Debug.LogWarning("[GameManager] Watchdog: player out of bounds, restoring last safe position.");
                _player.Teleport(_player.LastSafePosition + Vector3.up * 0.1f, _player.Look.Yaw);
                _airTime = 0f;
            }
        }

        private void DevKeys()
        {
            // Editor and development builds only.
            if (!Debug.isDebugBuild) return;
            var kb = Keyboard.current;
            if (kb == null || State != GameState.Playing) return;
            if (kb.f6Key.wasPressedThisFrame && _floors.Current != null) _floors.Current.ExitCar.Unlock();
            if (kb.f7Key.wasPressedThisFrame)
            {
                foreach (var m in _enemies.Maids) if (m != null) m.enabled = !m.enabled;
            }
            if (kb.f8Key.wasPressedThisFrame) StartCoroutine(LeaveFloor());
            if (kb.f9Key.wasPressedThisFrame && _acoustics != null)
            {
                // The SDK's debug window (dev library, Editor only) shows the voxelized floor and the rays.
                _acoustics.SetDebugWindow(!_acoustics.DebugWindowOpen);
                Debug.Log(_acoustics.Stats());
            }
        }
    }
}
