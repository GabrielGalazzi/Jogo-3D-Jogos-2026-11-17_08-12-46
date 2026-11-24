using MaidCafe.AI;
using MaidCafe.Core;
using MaidCafe.Localization;
using MaidCafe.Objectives;
using MaidCafe.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>
    /// In-game HUD: crosshair, interaction prompt with hold progress, objective, hints, phone battery,
    /// stamina, breath (while hiding) and the floor banner. Minimal and diegetic-feeling on purpose.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        private CanvasGroup _root;
        private Image _crosshair;
        private TextMeshProUGUI _prompt;
        private Image _holdBg;
        private Image _holdFill;
        private TextMeshProUGUI _objTitle;
        private TextMeshProUGUI _objStep;
        private CanvasGroup _hintGroup;
        private TextMeshProUGUI _hint;
        private CanvasGroup _bannerGroup;
        private TextMeshProUGUI _bannerTitle;
        private TextMeshProUGUI _bannerSub;
        private Image[] _battery;
        private TextMeshProUGUI _batteryLabel;
        private TextMeshProUGUI _gifts;
        private int _shownGifts = -1;
        private CanvasGroup _staminaGroup;
        private Image _staminaFill;
        private CanvasGroup _breathGroup;
        private Image _breathFill;
        private TextMeshProUGUI _breathText;

        private float _hintTimer;
        private float _bannerTimer;
        private string _lastStep;
        private float _stepFlash;

        public void SetVisible(bool visible) => _root.alpha = visible ? 1f : 0f;

        private void Awake()
        {
            var canvas = UIFactory.Canvas("HUD", 10, transform);
            _root = canvas.gameObject.AddComponent<CanvasGroup>();
            _root.interactable = false;
            _root.blocksRaycasts = false;
            var t = canvas.transform;

            _crosshair = UIFactory.Image(t, "Crosshair", new Color(1f, 1f, 1f, 0.55f));
            UIFactory.Place(_crosshair.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));

            _prompt = UIFactory.Text(t, "Prompt", "", 30f);
            UIFactory.Place(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1000f, 50f));
            _holdBg = UIFactory.Image(t, "HoldBg", new Color(0f, 0f, 0f, 0.6f));
            UIFactory.Place(_holdBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -94f), new Vector2(240f, 8f));
            _holdFill = UIFactory.Image(_holdBg.transform, "Fill", UIStyle.Accent);
            UIFactory.Place(_holdFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 8f));

            _objTitle = UIFactory.Text(t, "ObjectiveTitle", "", 30f, TextAlignmentOptions.TopLeft, UIStyle.Accent);
            UIFactory.Place(_objTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -32f), new Vector2(900f, 44f));
            _objStep = UIFactory.Text(t, "ObjectiveStep", "", 26f, TextAlignmentOptions.TopLeft);
            UIFactory.Place(_objStep.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -74f), new Vector2(900f, 80f));

            _hint = UIFactory.Text(t, "Hint", "", 28f, TextAlignmentOptions.Center, UIStyle.Text);
            _hintGroup = _hint.gameObject.AddComponent<CanvasGroup>();
            UIFactory.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1300f, 80f));

            var banner = UIFactory.Rect("Banner", t);
            _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            UIFactory.Place(banner, new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 160f));
            _bannerTitle = UIFactory.Text(banner, "Title", "", 64f, TextAlignmentOptions.Center, UIStyle.Text);
            UIFactory.Place(_bannerTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1400f, 80f));
            _bannerSub = UIFactory.Text(banner, "Sub", "", 32f, TextAlignmentOptions.Center, UIStyle.Accent);
            UIFactory.Place(_bannerSub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1400f, 60f));
            _bannerGroup.alpha = 0f;

            // Phone battery: label + 5 segments (bottom right).
            _batteryLabel = UIFactory.LocText(t, "BatteryLabel", "ui.hud.battery", 22f, TextAlignmentOptions.MidlineRight, UIStyle.Dim);
            UIFactory.Place(_batteryLabel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-196f, 40f), new Vector2(300f, 30f));
            _battery = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                _battery[i] = UIFactory.Image(t, "Battery" + i, UIStyle.Accent);
                UIFactory.Place(_battery[i].rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f - (4 - i) * 30f, 42f), new Vector2(24f, 26f));
            }

            // Sweets carried for the watcher maid (only shown when carrying some).
            _gifts = UIFactory.Text(t, "Gifts", "", 22f, TextAlignmentOptions.MidlineRight, UIStyle.Accent);
            UIFactory.Place(_gifts.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 78f), new Vector2(400f, 30f));

            // Stamina (bottom centre, fades out when full).
            var stamBg = UIFactory.Image(t, "StaminaBg", new Color(0f, 0f, 0f, 0.5f));
            _staminaGroup = stamBg.gameObject.AddComponent<CanvasGroup>();
            UIFactory.Place(stamBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(360f, 6f));
            _staminaFill = UIFactory.Image(stamBg.transform, "Fill", UIStyle.Text);
            UIFactory.Place(_staminaFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(360f, 6f));

            // Breath (only while hiding).
            var breath = UIFactory.Rect("Breath", t);
            _breathGroup = breath.gameObject.AddComponent<CanvasGroup>();
            UIFactory.Place(breath, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(800f, 80f));
            _breathText = UIFactory.LocText(breath, "Text", "ui.hud.holdBreath", 26f, TextAlignmentOptions.Center, UIStyle.Text);
            UIFactory.Place(_breathText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(800f, 40f));
            var breathBg = UIFactory.Image(breath, "Bg", new Color(0f, 0f, 0f, 0.6f));
            UIFactory.Place(breathBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(300f, 8f));
            _breathFill = UIFactory.Image(breathBg.transform, "Fill", new Color(0.55f, 0.75f, 1f));
            UIFactory.Place(_breathFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(300f, 8f));
            _breathGroup.alpha = 0f;

            // Noise meter (bottom left): live player noise against the maids' hearing threshold.
            gameObject.AddComponent<NoiseMeter>().Build(t);

            _hintGroup.alpha = 0f;
        }

        private void OnEnable() => GameEvents.Hint += ShowHint;
        private void OnDisable() => GameEvents.Hint -= ShowHint;

        public void ShowHint(string key)
        {
            _hint.text = Loc.Get(key);
            _hintTimer = 4f;
        }

        public void ShowBanner(string title, string subtitle, float duration = 4f)
        {
            _bannerTitle.text = title;
            _bannerSub.text = subtitle;
            _bannerTimer = duration;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var player = PlayerContext.Instance;
            if (player == null) return;

            UpdatePrompt(player);
            UpdateObjective(dt);

            _hintTimer -= dt;
            _hintGroup.alpha = Mathf.Clamp01(_hintTimer / 0.5f);
            _bannerTimer -= dt;
            _bannerGroup.alpha = Mathf.Clamp01(_bannerTimer / 0.8f);

            float battery = player.Phone.Battery01;
            int lit = Mathf.CeilToInt(battery * 5f - 0.001f);
            bool blink = player.Phone.IsLowBattery && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
            for (int i = 0; i < 5; i++)
            {
                Color c = i < lit ? (battery <= 0.2f ? UIStyle.Danger : UIStyle.Accent) : new Color(1f, 1f, 1f, 0.12f);
                if (blink && i == 0) c.a = 0.3f;
                _battery[i].color = c;
            }
            _batteryLabel.color = player.Phone.NightVisionOn ? new Color(0.5f, 1f, 0.55f) : UIStyle.Dim;
            if (player.Gifts != _shownGifts)
            {
                _shownGifts = player.Gifts;
                _gifts.text = _shownGifts > 0 ? Loc.Format("ui.hud.gifts", _shownGifts) : string.Empty;
            }

            float stamina = player.Motor.Stamina01;
            _staminaFill.rectTransform.sizeDelta = new Vector2(360f * stamina, 6f);
            _staminaFill.color = stamina < 0.25f ? UIStyle.Danger : UIStyle.Text;
            _staminaGroup.alpha = Mathf.MoveTowards(_staminaGroup.alpha, stamina < 0.99f ? 0.9f : 0f, dt * 2f);

            bool hiding = player.IsHiding;
            _breathGroup.alpha = Mathf.MoveTowards(_breathGroup.alpha, hiding ? 1f : 0f, dt * 4f);
            if (hiding)
            {
                float b = player.Breath.Breath01;
                _breathFill.rectTransform.sizeDelta = new Vector2(300f * b, 8f);
                _breathFill.color = b < 0.25f ? UIStyle.Danger : new Color(0.55f, 0.75f, 1f);
                _breathText.color = MaidNearby(player, 7f) ? UIStyle.Danger : UIStyle.Dim;
            }
        }

        private void UpdatePrompt(PlayerContext player)
        {
            var target = player.Interactor.Current;
            if (target == null)
            {
                _prompt.text = string.Empty;
                _holdBg.enabled = false;
                _holdFill.enabled = false;
                _crosshair.color = new Color(1f, 1f, 1f, 0.45f);
                return;
            }

            string action = Loc.Get(target.PromptKey);
            if (!string.IsNullOrEmpty(target.NameKey)) action += " " + Loc.Get(target.NameKey);
            _prompt.text = Loc.Format(target.HoldDuration > 0f ? "ui.prompt.hold" : "ui.prompt.press", action);
            _crosshair.color = UIStyle.Accent;

            bool holding = target.HoldDuration > 0f && player.Interactor.HoldProgress > 0f;
            _holdBg.enabled = holding;
            _holdFill.enabled = holding;
            if (holding) _holdFill.rectTransform.sizeDelta = new Vector2(240f * player.Interactor.HoldProgress, 8f);
        }

        private void UpdateObjective(float dt)
        {
            var obj = ObjectiveRunner.Instance != null ? ObjectiveRunner.Instance.Current : null;
            if (obj == null)
            {
                _objTitle.text = string.Empty;
                _objStep.text = string.Empty;
                return;
            }
            _objTitle.text = obj.Title;
            string step = "› " + obj.StepText();
            if (step != _lastStep)
            {
                _lastStep = step;
                _stepFlash = 1f;
            }
            _objStep.text = step;
            _stepFlash = Mathf.Max(0f, _stepFlash - dt);
            _objStep.color = Color.Lerp(UIStyle.Text, UIStyle.Accent, _stepFlash);
        }

        private static bool MaidNearby(PlayerContext player, float radius)
        {
            foreach (var m in MaidAI.All)
            {
                if ((m.transform.position - player.transform.position).sqrMagnitude < radius * radius) return true;
            }
            return false;
        }
    }
}
