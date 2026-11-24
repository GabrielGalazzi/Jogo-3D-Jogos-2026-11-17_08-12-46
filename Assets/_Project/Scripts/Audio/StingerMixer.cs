using System;
using System.Threading;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// The stingers' own small mixer on the audio thread: an AudioSource without a clip whose custom filter
    /// (OnAudioFilterRead, which makes it a generator) plays up to <see cref="MaxVoices"/> overlapping clips from decoded
    /// PCM at any pitch, sums them and runs the sum through <see cref="HarmonicSaturation"/> (driven by
    /// <see cref="Heat"/>), a low-pass that follows the heat (tames the fizz of the extra harmonics), a DC blocker and a
    /// soft limiter. The sum is saturated, not each clip, like a bus saturator: overlapping stabs fuse into one harsher
    /// sound and the limiter keeps the output below full scale.
    /// Non-diegetic: 2D, bypasses the traced reverb zones, never heard by the maids. Paused with the AudioListener.
    /// <see cref="Play"/> (main thread) hands requests to the audio thread through a single-producer/single-consumer ring.
    /// WebGL player: browsers support neither custom audio filters nor AudioClip.GetData, so each clip is played by one of
    /// <see cref="MaxVoices"/> plain 2D AudioSources at the requested gain and pitch (no saturation, low-pass or limiter).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class StingerMixer : MonoBehaviour
    {
        public const int MaxVoices = 8;

        /// <summary>Output level after the limiter (the stabs' loudness ceiling, about -3 dBFS).</summary>
        public const float OutputGain = 0.8f;

        private const int RingSize = 16;
        private const int MaxChannels = 16;
        private const float LimiterKnee = 0.8f;
        private const float LowPassAtNoHeat = 18000f;
        private const float LowPassAtFullHeat = 7000f;
        private const float DcBlockHz = 10f;

        /// <summary>A clip decoded once on the main thread (interleaved samples).</summary>
        public sealed class Clip
        {
            public readonly string Name;
            public readonly AudioClip Source;
            public readonly float[] Samples;
            public readonly int Channels;
            public readonly int Frames;
            public readonly int Frequency;

            private Clip(AudioClip clip, float[] samples)
            {
                Name = clip.name;
                Source = clip;
                Samples = samples;
                if (samples == null) return; // WebGL: the browser decodes the clip later; its metadata is not needed
                Channels = clip.channels;
                Frames = clip.samples;
                Frequency = clip.frequency;
            }

            /// <summary>Decodes <paramref name="clip"/> (must not be a streamed clip); null if its data cannot be read.</summary>
            public static Clip From(AudioClip clip)
            {
                if (clip == null) return null;
                if (UsesSourceVoices) return new Clip(clip, null); // played by an AudioSource voice, never decoded
                if (clip.samples < 2) return null;
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                var samples = new float[clip.samples * clip.channels];
                return clip.GetData(samples, 0) ? new Clip(clip, samples) : null;
            }
        }

        private struct Voice
        {
            public Clip Clip;
            public double Position;
            public double Step;
            public float Gain;
        }

        private struct Request
        {
            public Clip Clip;
            public float Gain;
            public float Pitch;
        }

        /// <summary>Saturation amount 0..1 (main thread writes, the audio thread reads it once per block).</summary>
        public volatile float Heat;

        private readonly Request[] _ring = new Request[RingSize];
        private int _ringWrite;
        private int _ringRead;
        private volatile bool _stopRequested;

        private readonly Voice[] _voices = new Voice[MaxVoices];
        private readonly float[] _lowPass = new float[MaxChannels];
        private readonly float[] _dcIn = new float[MaxChannels];
        private readonly float[] _dcOut = new float[MaxChannels];
        private bool _shaping;
        private int _outputRate = 48000;
        private AudioSource _source;
        private AudioSource[] _sourceVoices;
        private volatile int _activeVoices;
        private volatile float _peak;

        /// <summary>Voices sounding in the last audio block.</summary>
        public int ActiveVoices => _activeVoices;

        /// <summary>Highest output sample since the last call (diagnostics).</summary>
        public float TakePeak()
        {
            float p = _peak;
            _peak = 0f;
            return p;
        }

        /// <summary>True in the WebGL player: clips play on plain AudioSources instead of the audio-thread mixer.</summary>
        public static bool UsesSourceVoices => Application.platform == RuntimePlatform.WebGLPlayer;

        public static StingerMixer Create(Transform parent)
        {
            var go = new GameObject("Stingers");
            go.transform.SetParent(parent, false);
            var source = AddSource(go);
            var mixer = go.AddComponent<StingerMixer>();
            mixer._source = source;
            if (UsesSourceVoices)
            {
                mixer._sourceVoices = new AudioSource[MaxVoices];
                mixer._sourceVoices[0] = source;
                for (int v = 1; v < MaxVoices; v++) mixer._sourceVoices[v] = AddSource(go);
            }
            else
            {
                source.Play(); // no clip: the custom filter generates the sound
            }
            return mixer;
        }

        private static AudioSource AddSource(GameObject go)
        {
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            source.priority = 0;
            return source;
        }

        private void Awake()
        {
            _outputRate = AudioSettings.outputSampleRate;
        }

        /// <summary>Starts <paramref name="clip"/> at <paramref name="gain"/> and <paramref name="pitch"/> (1 = original).</summary>
        public bool Play(Clip clip, float gain, float pitch)
        {
            if (clip == null || pitch <= 0f) return false;
            if (_sourceVoices != null) return PlayOnSource(clip, gain, pitch);
            if (clip.Frames < 2) return false;
            if (_source != null && !_source.isPlaying) _source.Play();
            int write = _ringWrite;
            int next = (write + 1) % RingSize;
            if (next == Volatile.Read(ref _ringRead)) return false;
            _ring[write] = new Request { Clip = clip, Gain = gain, Pitch = pitch };
            Volatile.Write(ref _ringWrite, next);
            return true;
        }

        /// <summary>Silences every voice at the next audio block (pending requests are dropped too).</summary>
        public void StopAll()
        {
            _stopRequested = true;
            if (_sourceVoices == null) return;
            foreach (var s in _sourceVoices) s.Stop();
        }

        /// <summary>WebGL path: free voice first, otherwise the one closest to its end.</summary>
        private bool PlayOnSource(Clip clip, float gain, float pitch)
        {
            if (clip.Source == null) return false;
            AudioSource voice = null;
            float oldest = -1f;
            foreach (var s in _sourceVoices)
            {
                if (!s.isPlaying)
                {
                    voice = s;
                    break;
                }
                float done = s.clip != null && s.clip.length > 0f ? s.time / s.clip.length : 1f;
                if (done > oldest)
                {
                    oldest = done;
                    voice = s;
                }
            }
            if (voice == null) return false;
            voice.Stop();
            voice.clip = clip.Source;
            voice.volume = Mathf.Clamp01(gain * OutputGain);
            voice.pitch = pitch;
            voice.Play();
            return true;
        }

        // ------------------------------------------------------------------ audio thread

#if !UNITY_WEBGL || UNITY_EDITOR
        private void OnAudioFilterRead(float[] data, int channels)
        {
            int read = _ringRead;
            int write = Volatile.Read(ref _ringWrite);
            if (_stopRequested)
            {
                _stopRequested = false;
                for (int v = 0; v < MaxVoices; v++) _voices[v].Clip = null;
                read = write;
            }
            while (read != write)
            {
                StartVoice(_ring[read]);
                _ring[read].Clip = null;
                read = (read + 1) % RingSize;
            }
            Volatile.Write(ref _ringRead, read);

            Array.Clear(data, 0, data.Length);
            int frames = data.Length / channels;
            int active = 0;
            for (int v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Clip == null) continue;
                active++;
                MixVoice(ref _voices[v], data, channels, frames);
            }
            _activeVoices = active;
            if (active == 0)
            {
                ResetFilters();
                return;
            }

            float heat = Heat;
            heat = heat < 0f ? 0f : heat > 1f ? 1f : heat;
            var curve = new HarmonicSaturation(heat);
            if (curve.IsBypass) ResetFilters();
            _shaping = !curve.IsBypass;
            float cutoff = LowPassAtNoHeat + (LowPassAtFullHeat - LowPassAtNoHeat) * heat;
            float lp = 1f - (float)Math.Exp(-2.0 * Math.PI * cutoff / _outputRate);
            float dc = 1f - (float)(2.0 * Math.PI * DcBlockHz / _outputRate);
            float peak = _peak;
            for (int i = 0; i < data.Length; i++)
            {
                float x = data[i];
                int c = i % channels;
                if (_shaping && c < MaxChannels)
                {
                    x = curve.Apply(x);
                    _lowPass[c] += lp * (x - _lowPass[c]);
                    x = _lowPass[c];
                    float y = x - _dcIn[c] + dc * _dcOut[c];
                    _dcIn[c] = x;
                    _dcOut[c] = y;
                    x = y;
                }
                x = SoftLimit(x) * OutputGain;
                data[i] = x;
                float a = x < 0f ? -x : x;
                if (a > peak) peak = a;
            }
            _peak = peak;
        }
#endif

        private void StartVoice(Request request)
        {
            if (request.Clip == null) return;
            int slot = -1;
            double oldest = -1.0;
            for (int v = 0; v < MaxVoices; v++)
            {
                var voice = _voices[v];
                if (voice.Clip == null)
                {
                    slot = v;
                    break;
                }
                double done = voice.Position / voice.Clip.Frames;
                if (done > oldest)
                {
                    oldest = done;
                    slot = v;
                }
            }
            _voices[slot] = new Voice
            {
                Clip = request.Clip,
                Position = 0.0,
                Step = request.Clip.Frequency / (double)_outputRate * request.Pitch,
                Gain = request.Gain
            };
        }

        private static void MixVoice(ref Voice voice, float[] data, int channels, int frames)
        {
            var clip = voice.Clip;
            float[] s = clip.Samples;
            int cc = clip.Channels;
            for (int f = 0; f < frames; f++)
            {
                int i0 = (int)voice.Position;
                if (i0 >= clip.Frames - 1)
                {
                    voice.Clip = null;
                    return;
                }
                float t = (float)(voice.Position - i0);
                int b0 = i0 * cc;
                int b1 = b0 + cc;
                float left = (s[b0] + (s[b1] - s[b0]) * t) * voice.Gain;
                float right = cc > 1 ? (s[b0 + 1] + (s[b1 + 1] - s[b0 + 1]) * t) * voice.Gain : left;
                int o = f * channels;
                if (channels == 1)
                {
                    data[o] += 0.5f * (left + right);
                }
                else
                {
                    data[o] += left;
                    data[o + 1] += right;
                }
                voice.Position += voice.Step;
            }
        }

        /// <summary>Linear up to the knee, then a tanh shoulder that never exceeds 1.</summary>
        private static float SoftLimit(float x)
        {
            float a = x < 0f ? -x : x;
            if (a <= LimiterKnee) return x;
            float over = LimiterKnee + (1f - LimiterKnee) * (float)Math.Tanh((a - LimiterKnee) / (1f - LimiterKnee));
            return x < 0f ? -over : over;
        }

        private void ResetFilters()
        {
            if (!_shaping) return;
            _shaping = false;
            Array.Clear(_lowPass, 0, MaxChannels);
            Array.Clear(_dcIn, 0, MaxChannels);
            Array.Clear(_dcOut, 0, MaxChannels);
        }
    }
}
