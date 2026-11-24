using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaidCafe.UI
{
    /// <summary>Victory and game-over screens.</summary>
    public class EndScreen : MonoBehaviour
    {
        private GameObject _canvasGo;
        private Transform _canvas;

        private void Awake()
        {
            // Above the ScreenFader (200): end screens appear right after a fade to black.
            var canvas = UIFactory.Canvas("EndScreen", 250, transform);
            _canvasGo = canvas.gameObject;
            _canvas = canvas.transform;
            _canvasGo.SetActive(false);
        }

        public void ShowVictory(string stats, Action onPlayAgain, Action onMainMenu)
        {
            Build("ui.victory.title", "ui.victory.subtitle", stats, "ui.playAgain", onPlayAgain, onMainMenu, UIStyle.Accent);
        }

        public void ShowGameOver(string stats, Action onRetry, Action onMainMenu)
        {
            Build("ui.gameover.title", "ui.gameover.subtitle", stats, "ui.retry", onRetry, onMainMenu, UIStyle.Danger);
        }

        public void Hide() => _canvasGo.SetActive(false);

        private void Build(string titleKey, string subtitleKey, string stats, string primaryKey, Action primary, Action menu, Color titleColor)
        {
            foreach (Transform child in _canvas) Destroy(child.gameObject);
            var bg = UIFactory.Image(_canvas, "Bg", new Color(0f, 0f, 0f, 0.92f), raycast: true);
            UIFactory.Stretch(bg.rectTransform);

            var col = UIFactory.Column(bg.transform, "Column", 22f);
            UIFactory.Place((RectTransform)col.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 820f));

            var title = UIFactory.LocText(col.transform, "Title", titleKey, 84f, TextAlignmentOptions.Center, titleColor);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;
            var sub = UIFactory.LocText(col.transform, "Subtitle", subtitleKey, 32f, TextAlignmentOptions.Center, UIStyle.Text);
            sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            var statsText = UIFactory.Text(col.transform, "Stats", stats, 28f, TextAlignmentOptions.Center, UIStyle.Dim);
            statsText.gameObject.AddComponent<LayoutElement>().preferredHeight = 200f;

            var first = UIFactory.Button(col.transform, primaryKey, primary);
            UIFactory.Button(col.transform, "ui.mainMenu", menu);
            _canvasGo.SetActive(true);
            UIFactory.Select(first);
        }
    }
}
