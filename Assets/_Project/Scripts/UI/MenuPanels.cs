using System;
using MaidCafe.Core;
using MaidCafe.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>Panels shared by the main menu and the pause menu: settings, controls (how to play), credits.</summary>
    public static class MenuPanels
    {
        /// <summary>Centred dark panel with a title; returns the content column.</summary>
        public static VerticalLayoutGroup Frame(Transform parent, string name, string titleKey, Vector2 size, out GameObject root)
        {
            var overlay = UIFactory.Image(parent, name, new Color(0f, 0f, 0f, 0.55f), raycast: true);
            UIFactory.Stretch(overlay.rectTransform);
            root = overlay.gameObject;

            var panel = UIFactory.Image(overlay.transform, "Panel", UIStyle.PanelBg, raycast: true);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);

            var title = UIFactory.LocText(panel.transform, "Title", titleKey, 46f, TextAlignmentOptions.Center, UIStyle.Accent);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(size.x - 80f, 64f));

            var column = UIFactory.Column(panel.transform, "Content", 14f, TextAnchor.UpperCenter);
            var crt = (RectTransform)column.transform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.offsetMin = new Vector2(60f, 40f);
            crt.offsetMax = new Vector2(-60f, -110f);
            return column;
        }

        public static GameObject Settings(Transform parent, Action onBack, out Selectable first)
        {
            var col = Frame(parent, "SettingsPanel", "ui.settings", new Vector2(1160f, 860f), out var root);

            var langRow = UIFactory.Row(col.transform, "Language");
            var langLabel = UIFactory.LocText(langRow, "Label", "ui.language", 26f, TextAlignmentOptions.MidlineLeft);
            langLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 380f;
            var en = UIFactory.Button(langRow, "ui.lang.en", () => Loc.SetLanguage("en"), new Vector2(250f, 48f), 26f);
            UIFactory.Button(langRow, "ui.lang.pt", () => Loc.SetLanguage("pt"), new Vector2(250f, 48f), 26f);
            first = en;

            UIFactory.Slider(col.transform, "ui.sensitivity", 0.1f, 5f, GameSettings.MouseSensitivity,
                v => GameSettings.MouseSensitivity = v, v => v.ToString("0.00"));
            UIFactory.Toggle(col.transform, "ui.invertY", GameSettings.InvertY, v => GameSettings.InvertY = v);
            UIFactory.Slider(col.transform, "ui.fov", 60f, 95f, GameSettings.FieldOfView,
                v => GameSettings.FieldOfView = Mathf.Round(v), v => Mathf.Round(v).ToString("0") + "°");
            UIFactory.Slider(col.transform, "ui.volume", 0f, 1f, GameSettings.MasterVolume,
                v => GameSettings.MasterVolume = v, v => Mathf.RoundToInt(v * 100f) + "%");
            UIFactory.Slider(col.transform, "ui.brightness", -1f, 1.5f, GameSettings.Brightness,
                v => GameSettings.Brightness = v, v => (v >= 0 ? "+" : "") + v.ToString("0.0"));
            UIFactory.Toggle(col.transform, "ui.crouchToggle", GameSettings.CrouchToggle, v => GameSettings.CrouchToggle = v);
            UIFactory.Toggle(col.transform, "ui.showFps", GameSettings.ShowFps, v => GameSettings.ShowFps = v);
            UIFactory.Choice(col.transform, "ui.textureQuality", new[] { "ui.texture.high", "ui.texture.medium", "ui.texture.low" },
                GameSettings.TextureQuality, v => GameSettings.TextureQuality = v);
            UIFactory.Toggle(col.transform, "ui.fullscreen", Screen.fullScreenMode != FullScreenMode.Windowed, v =>
            {
                // WebGL: the canvas size belongs to the page; only ask the browser for fullscreen.
                if (Application.platform == RuntimePlatform.WebGLPlayer) Screen.fullScreen = v;
                else Screen.SetResolution(1920, 1080, v ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            });

            var spacer = UIFactory.Rect("Spacer", col.transform);
            spacer.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            UIFactory.Button(col.transform, "ui.back", onBack);
            return root;
        }

        public static GameObject TextPanel(Transform parent, string name, string titleKey, string bodyKey, Action onBack, out Selectable first)
        {
            var col = Frame(parent, name, titleKey, new Vector2(1240f, 880f), out var root);
            var body = UIFactory.LocText(col.transform, "Body", bodyKey, 25f, TextAlignmentOptions.TopLeft);
            var le = body.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 1100f;
            le.preferredHeight = 620f;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Ellipsis;
            first = UIFactory.Button(col.transform, "ui.back", onBack);
            return root;
        }
    }
}
