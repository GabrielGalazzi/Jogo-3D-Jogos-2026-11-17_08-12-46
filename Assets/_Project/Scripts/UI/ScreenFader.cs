using System.Collections;
using TMPro;
using UnityEngine;

namespace MaidCafe.UI
{
    /// <summary>Full-screen black fade with a title/subtitle (floor transitions, caught screens).</summary>
    public class ScreenFader : MonoBehaviour
    {
        private CanvasGroup _group;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _subtitle;

        public float Alpha => _group != null ? _group.alpha : 0f;

        private void Awake()
        {
            var canvas = UIFactory.Canvas("Fader", 200, transform);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            var bg = UIFactory.Image(canvas.transform, "Black", Color.black);
            UIFactory.Stretch(bg.rectTransform);
            _title = UIFactory.Text(canvas.transform, "Title", "", 72f, TextAlignmentOptions.Center, UIStyle.Text);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1600f, 100f));
            _subtitle = UIFactory.Text(canvas.transform, "Subtitle", "", 32f, TextAlignmentOptions.Center, UIStyle.Accent);
            UIFactory.Place(_subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1600f, 120f));
            _group.alpha = 1f;
        }

        public void SetText(string title, string subtitle = "")
        {
            _title.text = title;
            _subtitle.text = subtitle;
        }

        public void SetAlpha(float alpha) => _group.alpha = alpha;

        public IEnumerator Fade(float target, float duration)
        {
            float start = _group.alpha;
            if (duration <= 0f)
            {
                _group.alpha = target;
                yield break;
            }
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
            {
                _group.alpha = Mathf.Lerp(start, target, t);
                yield return null;
            }
            _group.alpha = target;
        }
    }
}
