using System;
using System.Collections.Generic;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Localization
{
    /// <summary>
    /// Minimal two-language string table (English / Brazilian Portuguese).
    /// Strings live in Resources/Localization/strings.json.
    /// </summary>
    public static class Loc
    {
        [Serializable]
        private class Entry
        {
            public string key;
            public string en;
            public string pt;
        }

        [Serializable]
        private class Table
        {
            public Entry[] entries;
        }

        private static Dictionary<string, Entry> _table;

        public static event Action LanguageChanged;

        public static string Language => GameSettings.Language;

        public static void SetLanguage(string language)
        {
            if (language == GameSettings.Language) return;
            GameSettings.Language = language;
            LanguageChanged?.Invoke();
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            EnsureLoaded();
            if (!_table.TryGetValue(key, out Entry entry))
            {
                Debug.LogWarning($"[Loc] Missing key '{key}'");
                return key;
            }
            string value = Language == "pt" ? entry.pt : entry.en;
            return string.IsNullOrEmpty(value) ? entry.en : value;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        public static bool Has(string key)
        {
            EnsureLoaded();
            return _table.ContainsKey(key);
        }

        private static void EnsureLoaded()
        {
            if (_table != null) return;
            _table = new Dictionary<string, Entry>();
            var asset = Resources.Load<TextAsset>("Localization/strings");
            if (asset == null)
            {
                Debug.LogError("[Loc] Resources/Localization/strings.json not found");
                return;
            }
            var table = JsonUtility.FromJson<Table>(asset.text);
            if (table?.entries == null) return;
            foreach (var entry in table.entries)
            {
                if (!string.IsNullOrEmpty(entry.key)) _table[entry.key] = entry;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _table = null;
            LanguageChanged = null;
        }
    }
}
