using System;
using UnityEngine;

namespace MaidCafe.Core
{
    /// <summary>
    /// Player-facing options persisted in PlayerPrefs.
    /// </summary>
    public static class GameSettings
    {
        private const string KeySensitivity = "settings.mouseSensitivity";
        private const string KeyInvertY = "settings.invertY";
        private const string KeyFov = "settings.fov";
        private const string KeyVolume = "settings.masterVolume";
        private const string KeyBrightness = "settings.brightness";
        private const string KeyLanguage = "settings.language";
        private const string KeyCrouchToggle = "settings.crouchToggle";
        private const string KeyShowFps = "settings.showFps";
        private const string KeyTextureQuality = "settings.textureQuality";

        public static event Action Changed;

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(KeySensitivity, 1f);
            set => SetFloat(KeySensitivity, Mathf.Clamp(value, 0.1f, 5f));
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            set => SetInt(KeyInvertY, value ? 1 : 0);
        }

        public static float FieldOfView
        {
            get => PlayerPrefs.GetFloat(KeyFov, 72f);
            set => SetFloat(KeyFov, Mathf.Clamp(value, 60f, 95f));
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(KeyVolume, 0.8f);
            set => SetFloat(KeyVolume, Mathf.Clamp01(value));
        }

        /// <summary>Exposure offset in EV applied by the post-processing volume.</summary>
        public static float Brightness
        {
            get => PlayerPrefs.GetFloat(KeyBrightness, 0f);
            set => SetFloat(KeyBrightness, Mathf.Clamp(value, -1f, 1.5f));
        }

        /// <summary>"en" or "pt".</summary>
        public static string Language
        {
            get => PlayerPrefs.GetString(KeyLanguage, DefaultLanguage());
            set
            {
                PlayerPrefs.SetString(KeyLanguage, value == "pt" ? "pt" : "en");
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool CrouchToggle
        {
            get => PlayerPrefs.GetInt(KeyCrouchToggle, 1) == 1;
            set => SetInt(KeyCrouchToggle, value ? 1 : 0);
        }

        /// <summary>Frames-per-second readout in the top-right corner (on by default).</summary>
        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(KeyShowFps, 1) == 1;
            set => SetInt(KeyShowFps, value ? 1 : 0);
        }

        /// <summary>Number of <see cref="TextureQuality"/> steps: High (full), Medium (half), Low (quarter).</summary>
        public const int TextureQualityLevels = 3;

        /// <summary>
        /// Texture resolution: 0 = High (full, surfaces at most 2048 px), 1 = Medium (half), 2 = Low (quarter).
        /// It is the global mipmap limit: the top mip levels of every mipmapped texture are skipped, which also frees
        /// the matching video memory. Applied at once and at startup.
        /// </summary>
        public static int TextureQuality
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KeyTextureQuality, 0), 0, TextureQualityLevels - 1);
            set
            {
                PlayerPrefs.SetInt(KeyTextureQuality, Mathf.Clamp(value, 0, TextureQualityLevels - 1));
                PlayerPrefs.Save();
                ApplyTextureQuality();
                Changed?.Invoke();
            }
        }

        public static void ApplyTextureQuality()
        {
            if (_editorMipmapLimit < 0) _editorMipmapLimit = QualitySettings.globalTextureMipmapLimit;
            QualitySettings.globalTextureMipmapLimit = TextureQuality;
        }

        // The editor's own value, restored when play mode ends so a "Low" choice never sticks to the editor views.
        private static int _editorMipmapLimit = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyGraphicsAtStartup()
        {
            ApplyTextureQuality();
            Application.quitting += RestoreEditorGraphics;
        }

        private static void RestoreEditorGraphics()
        {
            Application.quitting -= RestoreEditorGraphics;
            if (Application.isEditor && _editorMipmapLimit >= 0) QualitySettings.globalTextureMipmapLimit = _editorMipmapLimit;
        }

        private static string DefaultLanguage()
        {
            return Application.systemLanguage == SystemLanguage.Portuguese ? "pt" : "en";
        }

        private static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            _editorMipmapLimit = -1;
        }
    }
}
