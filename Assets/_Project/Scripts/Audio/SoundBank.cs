using System.Collections.Generic;
using MaidCafe.Localization;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// Clip lookup for <see cref="Sfx"/>. Recorded clips placed in Resources/Audio/&lt;SfxName&gt; (a single clip, or a
    /// folder of variants) replace the synthesized placeholders from <see cref="ProceduralSfx"/>. Clips are created once
    /// per session and cached. Random picks never repeat the previous variant when there are three or more.
    /// </summary>
    public static class SoundBank
    {
        private static readonly Dictionary<Sfx, AudioClip[]> Cache = new Dictionary<Sfx, AudioClip[]>();
        private static readonly Dictionary<(Sfx, string), AudioClip[]> LocalizedCache = new Dictionary<(Sfx, string), AudioClip[]>();
        private static readonly Dictionary<(Sfx, string), int> LastPick = new Dictionary<(Sfx, string), int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            LocalizedCache.Clear();
            LastPick.Clear();
        }

        /// <summary>A clip for <paramref name="id"/>; a random variant when <paramref name="variant"/> is negative.</summary>
        public static AudioClip Get(Sfx id, int variant = -1)
        {
            var clips = Load(id);
            if (clips.Length == 0) return null;
            return variant < 0 ? Pick(clips, (id, "")) : clips[variant % clips.Length];
        }

        /// <summary>
        /// A random clip of <paramref name="id"/> in the current game language: clips are named "&lt;lang&gt;_&lt;n&gt;"
        /// (voice lines, see ArtSource/Work/build_audio.py). Falls back to English, then to any clip.
        /// </summary>
        public static AudioClip GetLocalized(Sfx id)
        {
            string language = Loc.Language == "pt" ? "pt" : "en";
            var key = (id, language);
            if (!LocalizedCache.TryGetValue(key, out var clips) || clips.Length == 0 || clips[0] == null)
            {
                clips = Filter(Load(id), language + "_");
                if (clips.Length == 0) clips = Filter(Load(id), "en_");
                if (clips.Length == 0) clips = Load(id);
                LocalizedCache[key] = clips;
            }
            return clips.Length == 0 ? null : Pick(clips, key);
        }

        /// <summary>Number of variants of <paramref name="id"/> (recorded clips, or synthesized placeholders); 0 = silent.</summary>
        public static int Count(Sfx id) => Load(id).Length;

        /// <summary>Builds every clip up front (call during a loading screen to avoid a hitch on first use).</summary>
        public static void Preload()
        {
            foreach (Sfx id in System.Enum.GetValues(typeof(Sfx))) Load(id);
        }

        private static AudioClip Pick(AudioClip[] clips, (Sfx, string) key)
        {
            int i = Random.Range(0, clips.Length);
            if (clips.Length >= 3 && LastPick.TryGetValue(key, out int last) && i == last)
            {
                i = (i + Random.Range(1, clips.Length)) % clips.Length;
            }
            LastPick[key] = i;
            return clips[i];
        }

        private static AudioClip[] Filter(AudioClip[] clips, string prefix)
        {
            var list = new List<AudioClip>();
            foreach (var clip in clips)
            {
                if (clip != null && clip.name.StartsWith(prefix)) list.Add(clip);
            }
            return list.ToArray();
        }

        private static AudioClip[] Load(Sfx id)
        {
            if (Cache.TryGetValue(id, out var clips) && (clips.Length == 0 || clips[0] != null)) return clips;

            clips = Resources.LoadAll<AudioClip>("Audio/" + id);
            if (clips == null || clips.Length == 0)
            {
                int n = ProceduralSfx.VariantCount(id);
                clips = new AudioClip[n];
                for (int i = 0; i < n; i++) clips[i] = ProceduralSfx.Create(id, i);
            }
            else
            {
                // Resources.LoadAll gives no guaranteed order; sort by name so fixed variants stay stable.
                System.Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            Cache[id] = clips;
            return clips;
        }
    }
}
