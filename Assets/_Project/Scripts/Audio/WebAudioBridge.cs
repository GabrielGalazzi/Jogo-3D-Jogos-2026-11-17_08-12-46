using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace MaidCafe.Audio
{
    /// <summary>
    /// WebGL player only: browsers ignore Unity's AudioLowPassFilter and AudioReverbZone, so the traced muffling and room
    /// reverb are rebuilt with the Web Audio API (Plugins/WebGL/VAudioWeb.jslib). Each frame the playing
    /// <see cref="RaytracedSource"/>s report their position and low-pass cutoff, <see cref="RaytracedListener"/> reports the
    /// reverb it applies to its zone, and <see cref="AcousticWorld"/> sends everything to the page in one call
    /// (<see cref="Flush"/>). Every method is a no-op on other platforms.
    /// </summary>
    public static class WebAudioBridge
    {
        private const int MaxSources = 256;

        private static readonly float[] Sources = new float[MaxSources * 4];
        private static readonly float[] Reverb = new float[9];
        private static int _count;
        private static bool _reverbSet;

#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool Active => true;

        [DllImport("__Internal")]
        private static extern void VAW_AudioSync(float[] sources, int count, float[] reverb);
#else
        public static bool Active => false;
#endif

        /// <summary>A playing 3D source at <paramref name="position"/> filtered at <paramref name="cutoff"/> Hz this frame.</summary>
        public static void ReportSource(Vector3 position, float cutoff)
        {
            if (!Active || _count >= MaxSources) return;
            int b = _count * 4;
            Sources[b] = position.x;
            Sources[b + 1] = position.y;
            Sources[b + 2] = position.z;
            Sources[b + 3] = cutoff;
            _count++;
        }

        /// <summary>The room reverb in AudioReverbZone units (millibels, seconds, Hz).</summary>
        public static void ReportReverb(float room, float roomHF, float decayTime, float decayHFRatio, float reflections,
            float reflectionsDelay, float lateReverb, float reverbDelay, float hfReference)
        {
            if (!Active) return;
            Reverb[0] = room;
            Reverb[1] = roomHF;
            Reverb[2] = decayTime;
            Reverb[3] = decayHFRatio;
            Reverb[4] = reflections;
            Reverb[5] = reflectionsDelay;
            Reverb[6] = lateReverb;
            Reverb[7] = reverbDelay;
            Reverb[8] = hfReference;
            _reverbSet = true;
        }

        /// <summary>Sends this frame's reports to the page (once per frame, after every Update).</summary>
        public static void Flush()
        {
            float[] reverb = _reverbSet ? Reverb : null;
#if UNITY_WEBGL && !UNITY_EDITOR
            VAW_AudioSync(Sources, _count, reverb);
#endif
            _count = 0;
            _reverbSet = false;
        }
    }
}
