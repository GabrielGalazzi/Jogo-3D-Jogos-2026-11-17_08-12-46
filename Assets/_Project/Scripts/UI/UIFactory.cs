using System;
using MaidCafe.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>Colour palette: dark café, pastel pink accents, blood-red danger.</summary>
    public static class UIStyle
    {
        public static readonly Color PanelBg = new Color(0.035f, 0.025f, 0.045f, 0.9f);
        public static readonly Color Button = new Color(0.13f, 0.09f, 0.13f, 0.95f);
        public static readonly Color ButtonHighlight = new Color(0.78f, 0.36f, 0.52f, 1f);
        public static readonly Color ButtonPressed = new Color(0.55f, 0.1f, 0.2f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.91f, 0.93f);
        public static readonly Color Accent = new Color(0.96f, 0.58f, 0.74f);
        public static readonly Color Danger = new Color(0.86f, 0.16f, 0.22f);
        public static readonly Color Dim = new Color(0.72f, 0.66f, 0.72f);
    }

    /// <summary>
    /// Builds uGUI + TextMeshPro elements in code (canvas scaled for 1920×1080), so screens need no
    /// hand-made prefabs. All visible strings go through the localization table.
    /// </summary>
    public static class UIFactory
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        public static Canvas Canvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float margin = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static Image Image(Transform parent, string name, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null)
        {
            var rt = Rect(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color ?? UIStyle.Text;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.text = text;
            return tmp;
        }

        public static TextMeshProUGUI LocText(Transform parent, string name, string key, float size,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null)
        {
            var tmp = Text(parent, name, string.Empty, size, align, color);
            var loc = tmp.gameObject.AddComponent<LocalizedText>();
            loc.Key = key;
            return tmp;
        }

        public static Button Button(Transform parent, string locKey, Action onClick, Vector2? size = null, float fontSize = 30f)
        {
            Vector2 s = size ?? new Vector2(460f, 66f);
            var rt = Rect("Button_" + locKey, parent);
            rt.sizeDelta = s;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Color.white;
            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = UIStyle.Button;
            colors.highlightedColor = UIStyle.ButtonHighlight;
            colors.selectedColor = UIStyle.ButtonHighlight;
            colors.pressedColor = UIStyle.ButtonPressed;
            colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = s.x;
            le.preferredHeight = s.y;

            var label = LocText(rt, "Label", locKey, fontSize);
            Stretch(label.rectTransform, 6f);
            btn.onClick.AddListener(() => onClick?.Invoke());
            return btn;
        }

        public static VerticalLayoutGroup Column(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var rt = Rect(name, parent);
            var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childAlignment = align;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            return vlg;
        }

        /// <summary>Label + slider + value text in one row.</summary>
        public static Slider Slider(Transform parent, string labelKey, float min, float max, float value,
            Action<float> onChanged, Func<float, string> format)
        {
            var row = Row(parent, "Row_" + labelKey);
            var label = LocText(row, "Label", labelKey, 26f, TextAlignmentOptions.MidlineLeft);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 380f;

            var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGo.transform.SetParent(row, false);
            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            var le = sliderGo.AddComponent<LayoutElement>();
            le.preferredWidth = 420f;
            le.preferredHeight = 24f;
            TintSlider(sliderGo.transform);

            var valueText = Text(row, "Value", format(value), 26f, TextAlignmentOptions.MidlineRight);
            valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = format(v);
                onChanged(v);
            });
            return slider;
        }

        /// <summary>Label + toggle in one row.</summary>
        public static Toggle Toggle(Transform parent, string labelKey, bool value, Action<bool> onChanged)
        {
            var row = Row(parent, "Row_" + labelKey);
            var label = LocText(row, "Label", labelKey, 26f, TextAlignmentOptions.MidlineLeft);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 380f;

            var toggleGo = DefaultControls.CreateToggle(new DefaultControls.Resources());
            toggleGo.transform.SetParent(row, false);
            var legacyLabel = toggleGo.GetComponentInChildren<Text>();
            if (legacyLabel != null) UnityEngine.Object.Destroy(legacyLabel.gameObject);
            var toggle = toggleGo.GetComponent<Toggle>();
            toggle.isOn = value;
            var le = toggleGo.AddComponent<LayoutElement>();
            le.preferredWidth = 540f;
            le.preferredHeight = 34f;
            foreach (var img in toggleGo.GetComponentsInChildren<Image>())
            {
                img.color = img.gameObject.name == "Checkmark" ? UIStyle.Accent : UIStyle.Button;
                if (img.gameObject.name == "Background") img.rectTransform.sizeDelta = new Vector2(34f, 34f);
                if (img.gameObject.name == "Checkmark") img.rectTransform.sizeDelta = new Vector2(22f, 22f);
            }
            toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }

        /// <summary>Label + a button showing the current option; pressing it (mouse, Enter or gamepad A) cycles to the next one.</summary>
        public static Button Choice(Transform parent, string labelKey, string[] optionKeys, int index, Action<int> onChanged)
        {
            var row = Row(parent, "Row_" + labelKey);
            var label = LocText(row, "Label", labelKey, 26f, TextAlignmentOptions.MidlineLeft);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 380f;

            int current = Mathf.Clamp(index, 0, optionKeys.Length - 1);
            LocalizedText value = null;
            var button = Button(row, optionKeys[current], () =>
            {
                current = (current + 1) % optionKeys.Length;
                if (value != null) value.Key = optionKeys[current];
                onChanged(current);
            }, new Vector2(420f, 44f), 26f);
            value = button.GetComponentInChildren<LocalizedText>();
            return button;
        }

        public static RectTransform Row(Transform parent, string name)
        {
            var row = Rect(name, parent);
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            le.preferredWidth = 960f;
            return row;
        }

        private static void TintSlider(Transform slider)
        {
            foreach (var img in slider.GetComponentsInChildren<Image>())
            {
                switch (img.gameObject.name)
                {
                    case "Background": img.color = UIStyle.Button; break;
                    case "Fill": img.color = UIStyle.Accent; break;
                    case "Handle": img.color = UIStyle.Text; break;
                }
            }
        }

        /// <summary>Creates an EventSystem using the Input System UI module (mouse, keyboard and gamepad).</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
