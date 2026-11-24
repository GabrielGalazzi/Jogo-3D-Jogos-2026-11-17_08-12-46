using MaidCafe.AI;
using MaidCafe.Core;
using MaidCafe.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>
    /// HUD noise meter (bottom left): a sound-wave of bars showing the player's noise right now, and a threshold mark
    /// where the maids can hear it. Both come from the values the AI uses:
    /// <list type="bullet">
    /// <item>Level (metres of noise radius): the footstep radius of the current gait while moving
    /// (<see cref="PlayerMotor.MovementNoise"/>: crouch &lt; walk &lt; sprint), every other noise the player makes through
    /// <see cref="NoiseSystem"/> (doors, hiding, pickups, objective stations, gasping; held briefly, then falling), and
    /// breathing while hidden (<see cref="MaidAI.BreathHearingRadius"/>, 0 while holding breath).</item>
    /// <item>Threshold: the smallest noise radius any maid would hear right now (<see cref="MaidAI.NoiseNeededToHear"/>,
    /// her hearing multiplier, distance and walls; <see cref="MaidAI.BreathNeededToHear"/> for breathing, searching
    /// maids only). The source closest to being heard is shown.</item>
    /// </list>
    /// Colour: calm below 70% of the threshold, amber approaching it, red once a maid can hear. Bars past the threshold
    /// are tinted red even when unlit, so the audible zone is always visible; with nobody in earshot the mark sits
    /// dimmed at the end of the scale.
    /// </summary>
    public class NoiseMeter : MonoBehaviour
    {
        /// <summary>Metres of noise radius at the right end of the meter (sprinting is 13 m; loud tasks peg it).</summary>
        public const float FullScale = 16f;

        /// <summary>Share of the threshold at which the meter turns amber.</summary>
        public const float WarningRatio = 0.7f;

        private const int BarCount = 32;
        private const float Width = 320f;
        private const float Gap = 3f;
        private const float MaxBarHeight = 40f;
        private const float MinBarHeight = 4f;
        private const float SpikeHold = 0.35f;
        private const float SpikeFallPerSecond = 20f;

        private static readonly Color Calm = new Color(0.72f, 0.93f, 0.86f, 0.95f);
        private static readonly Color Warning = new Color(1f, 0.72f, 0.25f, 1f);
        private static readonly Color Heard = UIStyle.Danger;
        private static readonly Color Idle = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color IdleAudible = new Color(0.86f, 0.16f, 0.22f, 0.28f);

        private RectTransform[] _bars;
        private Image[] _barImages;
        private RectTransform _mark;
        private Image _markImage;
        private Image _caretImage;
        private TextMeshProUGUI _label;

        private float _spike;
        private float _spikeHold;
        private float _shownLevel;
        private float _shownThreshold = FullScale * 1.5f;
        private float _phase;

        /// <summary>Noise radius (m) shown, compared with <see cref="Threshold"/>.</summary>
        public float Level { get; private set; }

        /// <summary>Smallest noise radius (m) a maid would hear right now (infinity when none can).</summary>
        public float Threshold { get; private set; } = float.PositiveInfinity;

        /// <summary>True when the current noise reaches a maid.</summary>
        public bool IsHeard => Level > 0f && Level >= Threshold;

        public void Build(Transform canvas)
        {
            var root = UIFactory.Rect("NoiseMeter", canvas);
            UIFactory.Place(root, Vector2.zero, Vector2.zero, new Vector2(40f, 34f), new Vector2(Width, 76f));

            _label = UIFactory.LocText(root, "Label", "ui.hud.noise", 22f, TextAlignmentOptions.BottomLeft, UIStyle.Dim);
            UIFactory.Place(_label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Width, 26f));

            var wave = UIFactory.Rect("Wave", root);
            UIFactory.Place(wave, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Width, MaxBarHeight + 8f));

            float barWidth = (Width - (BarCount - 1) * Gap) / BarCount;
            _bars = new RectTransform[BarCount];
            _barImages = new Image[BarCount];
            for (int i = 0; i < BarCount; i++)
            {
                var img = UIFactory.Image(wave, "Bar" + i, Idle);
                _barImages[i] = img;
                _bars[i] = img.rectTransform;
                UIFactory.Place(_bars[i], new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(i * (barWidth + Gap) + barWidth * 0.5f, 0f), new Vector2(barWidth, MinBarHeight));
            }

            _markImage = UIFactory.Image(wave, "Threshold", UIStyle.Text);
            _mark = _markImage.rectTransform;
            UIFactory.Place(_mark, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(Width, 0f), new Vector2(3f, MaxBarHeight + 10f));
            _caretImage = UIFactory.Image(_mark, "Caret", UIStyle.Text);
            UIFactory.Place(_caretImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(8f, 8f));
            _caretImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private void OnEnable() => NoiseSystem.OnNoise += OnNoise;
        private void OnDisable() => NoiseSystem.OnNoise -= OnNoise;

        private void OnNoise(NoiseEvent e)
        {
            if (!e.FromPlayer || e.Radius < _spike) return;
            _spike = e.Radius;
            _spikeHold = SpikeHold;
        }

        private void Update()
        {
            var player = PlayerContext.Instance;
            if (player == null || _bars == null) return;
            float dt = Time.deltaTime;

            if (_spikeHold > 0f) _spikeHold -= dt;
            else _spike = Mathf.Max(0f, _spike - SpikeFallPerSecond * dt);

            Measure(player);
            Draw(dt);
        }

        private void Measure(PlayerContext player)
        {
            float general = Mathf.Max(player.Motor.MovementNoise, _spike);
            float breathing = player.IsHiding && !player.IsHoldingBreath ? MaidAI.BreathHearingRadius : 0f;

            float generalThreshold = float.PositiveInfinity;
            float breathThreshold = float.PositiveInfinity;
            Vector3 at = player.transform.position;
            foreach (var maid in MaidAI.All)
            {
                if (maid == null) continue;
                generalThreshold = Mathf.Min(generalThreshold, maid.NoiseNeededToHear(at));
                if (player.IsHiding) breathThreshold = Mathf.Min(breathThreshold, maid.BreathNeededToHear(player.CurrentHidingSpot));
            }

            // Show whichever source is closest to being heard (louder one when neither is).
            float generalRatio = general / generalThreshold;
            float breathRatio = breathing / breathThreshold;
            bool showBreathing = breathing > 0f && (breathRatio > generalRatio || (breathRatio == generalRatio && breathing > general));
            Level = showBreathing ? breathing : general;
            Threshold = showBreathing ? breathThreshold : generalThreshold;
        }

        private void Draw(float dt)
        {
            // Fast attack, slower release, so steps read as a steady level and spikes stay visible.
            float attack = Level > _shownLevel ? 30f : 5f;
            _shownLevel = Mathf.Lerp(_shownLevel, Level, 1f - Mathf.Exp(-attack * dt));
            float threshold = Mathf.Min(Threshold, FullScale * 1.5f);
            _shownThreshold = Mathf.Lerp(_shownThreshold, threshold, 1f - Mathf.Exp(-10f * dt));

            float levelFraction = Mathf.Clamp01(_shownLevel / FullScale);
            float thresholdFraction = _shownThreshold / FullScale;
            bool inRange = thresholdFraction <= 1f;

            float ratio = Level <= 0f ? 0f : Level / Threshold;
            Color state = ratio >= 1f ? Heard : ratio >= WarningRatio ? Warning : Calm;

            _phase += dt * (3f + 12f * levelFraction);
            for (int i = 0; i < BarCount; i++)
            {
                float f = (i + 0.5f) / BarCount;
                bool lit = f <= levelFraction && _shownLevel > 0.05f;
                bool beyond = inRange && f >= thresholdFraction;
                float height;
                Color color;
                if (lit)
                {
                    float wave = 0.72f + 0.28f * Mathf.Sin(_phase + i * 0.85f);
                    height = MinBarHeight + (MaxBarHeight - MinBarHeight) * (0.3f + 0.7f * f) * wave;
                    color = beyond ? Heard : state;
                }
                else
                {
                    height = MinBarHeight;
                    color = beyond ? IdleAudible : Idle;
                }
                _bars[i].sizeDelta = new Vector2(_bars[i].sizeDelta.x, height);
                _barImages[i].color = color;
            }

            Color markColor = inRange ? (ratio >= 1f ? Heard : ratio >= WarningRatio ? Warning : UIStyle.Text) : new Color(1f, 1f, 1f, 0.3f);
            _mark.anchoredPosition = new Vector2(Mathf.Min(thresholdFraction, 1f) * Width, 0f);
            _markImage.color = markColor;
            _caretImage.color = markColor;
            _label.color = ratio >= WarningRatio ? state : UIStyle.Dim;
        }
    }
}
