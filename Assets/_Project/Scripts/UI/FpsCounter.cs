using MaidCafe.Core;
using MaidCafe.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>
    /// Frames-per-second readout in the top-right corner of the screen, above all other UI, in every scene.
    /// Created once at startup and kept across scene loads; shown or hidden by <see cref="GameSettings.ShowFps"/>.
    /// The value is averaged over half a second so it stays readable, and turns red below 30 FPS.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        private const float Interval = 0.5f;
        private const float LowFps = 30f;

        private Canvas _canvas;
        private TextMeshProUGUI _text;
        private float _elapsed;
        private int _frames;
        private int _shown = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("FpsCounter");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsCounter>();
        }

        private void Awake()
        {
            _canvas = UIFactory.Canvas("FpsCanvas", 300, transform);
            // A readout only: it must never take clicks from the menus below it.
            var raycaster = _canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null) Destroy(raycaster);
            _text = UIFactory.Text(_canvas.transform, "Fps", string.Empty, 22f, TextAlignmentOptions.TopRight, UIStyle.Dim);
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            UIFactory.Place(_text.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(240f, 32f));
            Apply();
        }

        private void OnEnable() => GameSettings.Changed += Apply;
        private void OnDisable() => GameSettings.Changed -= Apply;

        /// <summary>Visibility and language follow the settings.</summary>
        private void Apply()
        {
            if (_canvas == null) return;
            _canvas.enabled = GameSettings.ShowFps;
            if (_shown >= 0) _text.text = Loc.Format("ui.hud.fps", _shown);
        }

        private void Update()
        {
            if (!_canvas.enabled) return;
            // Unscaled: keeps counting while the game is paused (time scale 0).
            _elapsed += Time.unscaledDeltaTime;
            _frames++;
            if (_elapsed < Interval) return;

            float fps = _frames / _elapsed;
            _shown = Mathf.RoundToInt(fps);
            _text.text = Loc.Format("ui.hud.fps", _shown);
            _text.color = fps < LowFps ? UIStyle.Danger : UIStyle.Dim;
            _elapsed = 0f;
            _frames = 0;
        }
    }
}
