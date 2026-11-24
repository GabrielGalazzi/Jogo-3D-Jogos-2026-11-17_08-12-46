using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>Pause menu: resume, settings, controls, return to last safe point, main menu, quit.</summary>
    public class PauseMenu : MonoBehaviour
    {
        public event Action ResumeRequested;
        public event Action SafePointRequested;
        public event Action MainMenuRequested;
        public event Action QuitRequested;

        private GameObject _canvasGo;
        private GameObject _main;
        private GameObject _settings;
        private GameObject _controls;
        private Selectable _firstMain;
        private Selectable _firstSettings;
        private Selectable _firstControls;

        public bool IsOpen => _canvasGo != null && _canvasGo.activeSelf;

        /// <summary>True when a sub-panel (settings/controls) is open; Esc should go back instead of resuming.</summary>
        public bool InSubPanel => (_settings != null && _settings.activeSelf) || (_controls != null && _controls.activeSelf);

        private void Awake()
        {
            var canvas = UIFactory.Canvas("PauseMenu", 50, transform);
            _canvasGo = canvas.gameObject;

            var col = MenuPanels.Frame(canvas.transform, "Main", "ui.paused", new Vector2(640f, 700f), out _main);
            col.spacing = 16f;
            _firstMain = UIFactory.Button(col.transform, "ui.resume", () => ResumeRequested?.Invoke());
            UIFactory.Button(col.transform, "ui.settings", () => Show(_settings, _firstSettings));
            UIFactory.Button(col.transform, "ui.howToPlay", () => Show(_controls, _firstControls));
            UIFactory.Button(col.transform, "ui.safePoint", () => SafePointRequested?.Invoke());
            UIFactory.Button(col.transform, "ui.mainMenu", () => MainMenuRequested?.Invoke());
            if (Application.platform != RuntimePlatform.WebGLPlayer) // a browser tab cannot quit
                UIFactory.Button(col.transform, "ui.quit", () => QuitRequested?.Invoke());

            _settings = MenuPanels.Settings(canvas.transform, Back, out _firstSettings);
            _controls = MenuPanels.TextPanel(canvas.transform, "Controls", "ui.howToPlay", "ui.controls.body", Back, out _firstControls);

            _canvasGo.SetActive(false);
        }

        public void Open()
        {
            _canvasGo.SetActive(true);
            Show(_main, _firstMain);
        }

        public void Close() => _canvasGo.SetActive(false);

        public void Back() => Show(_main, _firstMain);

        private void Show(GameObject panel, Selectable first)
        {
            _main.SetActive(panel == _main);
            _settings.SetActive(panel == _settings);
            _controls.SetActive(panel == _controls);
            UIFactory.Select(first);
        }
    }
}
