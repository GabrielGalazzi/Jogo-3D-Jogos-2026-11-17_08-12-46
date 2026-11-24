using System;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// Every diegetic sound effect in the game. Recorded clips in Resources/Audio/&lt;name&gt;/ (built by
    /// ArtSource/Work/build_audio.py) override the synthesized placeholder; see <see cref="ProceduralSfx.FallbackOf"/>
    /// for what plays when a folder is missing. New values go at the end.
    /// </summary>
    public enum Sfx
    {
        FootstepPlayer,
        FootstepMaid,
        MaidShriek,
        HandBellLoop,
        LockerDoor,
        ClothRustle,
        Pickup,
        ElevatorChime,
        ElevatorDoors,
        MusicBoxLoop,
        GramophoneLoop,
        Lever,
        ServiceBell,
        Interact,
        Gasp,
        MusicBoxBreak,
        NeedleScratch,

        // Player footsteps per gait and floor surface (see PlayerAudio).
        FootstepWalkWood,
        FootstepWalkTile,
        FootstepRunWood,
        FootstepRunTile,
        FootstepCrouch,
        CrouchCloth,

        // Player breathing.
        BreathHideLoop,
        BreathHoldStart,
        BreathRelease,
        BreathRunLoop,
        BreathExhaustedLoop,

        // Doors.
        ElevatorDoorOpen,
        ElevatorDoorClose,
        LockerOpen,
        LockerClose,

        // Maid voice lines (clips named <lang>_<n>, see SoundBank.GetLocalized), one voice per maid model.
        VoicePatrol,
        VoiceStyleAnime,
        VoiceTypical,
        VoiceHead,
        VoiceWatcherNotice,
        VoiceWatcherCall,

        // Non-diegetic violin stingers (TensionStingers, stereo): calm stabs, chase stabs (sorted low → high), the catch.
        ViolinStab,
        ViolinChase,
        ViolinCatch
    }

    /// <summary>
    /// Synthesizes placeholder sound effects in code (no audio assets needed): additive partials with exponential
    /// decays, filtered noise and simple envelopes. Melodies are original. Deterministic per (sound, variant).
    /// </summary>
    public static class ProceduralSfx
    {
        public const int Rate = 32000;

        /// <summary>
        /// The synthesized sound used for <paramref name="id"/> when its recorded clips are missing, or null when it
        /// has no placeholder (breathing and voices stay silent rather than sounding like a synth).
        /// </summary>
        public static Sfx? FallbackOf(Sfx id)
        {
            switch (id)
            {
                case Sfx.FootstepWalkWood:
                case Sfx.FootstepWalkTile:
                case Sfx.FootstepRunWood:
                case Sfx.FootstepRunTile:
                case Sfx.FootstepCrouch:
                    return Sfx.FootstepPlayer;
                case Sfx.CrouchCloth:
                    return Sfx.ClothRustle;
                case Sfx.ElevatorDoorOpen:
                case Sfx.ElevatorDoorClose:
                    return Sfx.ElevatorDoors;
                case Sfx.LockerOpen:
                case Sfx.LockerClose:
                    return Sfx.LockerDoor;
                case Sfx.BreathHideLoop:
                case Sfx.BreathHoldStart:
                case Sfx.BreathRelease:
                case Sfx.BreathRunLoop:
                case Sfx.BreathExhaustedLoop:
                case Sfx.VoicePatrol:
                case Sfx.VoiceStyleAnime:
                case Sfx.VoiceTypical:
                case Sfx.VoiceHead:
                case Sfx.VoiceWatcherNotice:
                case Sfx.VoiceWatcherCall:
                case Sfx.ViolinStab:
                case Sfx.ViolinChase:
                case Sfx.ViolinCatch:
                    return null;
                default:
                    return id;
            }
        }

        public static int VariantCount(Sfx id)
        {
            var source = FallbackOf(id);
            if (source == null) return 0;
            return source == Sfx.FootstepPlayer || source == Sfx.FootstepMaid ? 4 : 1;
        }

        public static AudioClip Create(Sfx id, int variant)
        {
            float[] data = Synthesize(id, variant);
            Normalize(data, 0.9f);
            var clip = AudioClip.Create($"{id}_{variant}", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static float[] Synthesize(Sfx id, int variant)
        {
            id = FallbackOf(id) ?? id;
            var rng = new Noise((uint)(1 + (int)id * 7919 + variant * 104729));
            switch (id)
            {
                case Sfx.FootstepPlayer: return FootstepPlayer(rng);
                case Sfx.FootstepMaid: return FootstepMaid(rng);
                case Sfx.MaidShriek: return Shriek(rng);
                case Sfx.HandBellLoop: return HandBellLoop(rng);
                case Sfx.LockerDoor: return LockerDoor(rng);
                case Sfx.ClothRustle: return ClothRustle(rng);
                case Sfx.Pickup: return Pickup(rng);
                case Sfx.ElevatorChime: return ElevatorChime();
                case Sfx.ElevatorDoors: return ElevatorDoors(rng);
                case Sfx.MusicBoxLoop: return MusicBoxLoop(rng);
                case Sfx.GramophoneLoop: return GramophoneLoop(rng);
                case Sfx.Lever: return Lever(rng);
                case Sfx.ServiceBell: return ServiceBell(rng);
                case Sfx.Interact: return Interact(rng);
                case Sfx.Gasp: return Gasp(rng);
                case Sfx.MusicBoxBreak: return MusicBoxBreak(rng);
                case Sfx.NeedleScratch: return NeedleScratch(rng);
                default: return new float[Rate / 10];
            }
        }

        // ------------------------------------------------------------------ sounds

        private static float[] FootstepPlayer(Noise rng)
        {
            var b = Buffer(0.32f);
            float thump = 72f * rng.Range(0.88f, 1.12f);
            Partial(b, 0f, thump, 0.9f, 0.045f, pitchDrop: 0.35f);
            NoiseBurst(b, 0f, rng, Biquad.Bandpass(620f * rng.Range(0.85f, 1.15f), 1.0f), 0.6f, 0.03f);
            NoiseBurst(b, 0f, rng, Biquad.Highpass(2500f, 0.7f), 0.15f, 0.005f);
            NoiseBurst(b, 0.045f * rng.Range(0.8f, 1.2f), rng, Biquad.Bandpass(1000f, 1.2f), 0.3f, 0.02f);
            return b;
        }

        private static float[] FootstepMaid(Noise rng)
        {
            // A porcelain heel on a wooden floor: a hard clack, a short ceramic ring and a soft thump.
            var b = Buffer(0.45f);
            NoiseBurst(b, 0f, rng, Biquad.Bandpass(2800f * rng.Range(0.9f, 1.1f), 2f), 1f, 0.006f);
            float f0 = 1650f * rng.Range(0.92f, 1.08f);
            float[] ratio = { 1f, 2.31f, 3.94f, 5.82f };
            float[] amp = { 0.35f, 0.25f, 0.15f, 0.08f };
            float[] decay = { 0.09f, 0.06f, 0.04f, 0.025f };
            for (int i = 0; i < ratio.Length; i++) Partial(b, 0f, f0 * ratio[i], amp[i], decay[i]);
            Partial(b, 0f, 110f, 0.35f, 0.02f);
            float tip = 0.028f * rng.Range(0.8f, 1.25f);
            NoiseBurst(b, tip, rng, Biquad.Bandpass(3400f, 2.5f), 0.4f, 0.004f);
            for (int i = 0; i < ratio.Length; i++) Partial(b, tip, f0 * 1.06f * ratio[i], amp[i] * 0.4f, decay[i] * 0.7f);
            return b;
        }

        private static float[] Shriek(Noise rng)
        {
            const float len = 1.7f;
            var b = Buffer(len);
            int n = b.Length;
            float phase = 0f;
            var f1 = Biquad.Bandpass(950f, 2f);
            var f2 = Biquad.Bandpass(2450f, 3f);
            var f3 = Biquad.Bandpass(3350f, 4f);
            var breath = Biquad.Bandpass(3000f, 1f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float glide = t < 0.35f ? Mathf.Lerp(620f, 980f, Mathf.SmoothStep(0f, 1f, t / 0.35f)) : Mathf.Lerp(980f, 1080f, (t - 0.35f) / (len - 0.35f));
                float f = glide * (1f + 0.03f * Mathf.Sin(2f * Mathf.PI * 6.5f * t) + 0.01f * rng.Next());
                phase += 2f * Mathf.PI * f / Rate;
                float saw = 0f;
                for (int h = 1; h <= 12 && f * h < 12000f; h++) saw += Mathf.Sin(phase * h) / h;
                float voiced = f1.Process(saw) * 1.0f + f2.Process(saw) * 0.7f + f3.Process(saw) * 0.45f;
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((len - t) / 0.5f);
                float x = (voiced * 1.6f + breath.Process(rng.Next()) * 0.25f) * env;
                b[i] = (float)Math.Tanh(1.8f * x);
            }
            return b;
        }

        private static float[] HandBellLoop(Noise rng)
        {
            const float loop = 2.4f;
            var b = Buffer(loop + 1.5f);
            float[] ratio = { 0.5f, 1f, 1.19f, 1.5f, 2f, 2.5f, 3f, 4f };
            float[] amp = { 0.3f, 1f, 0.5f, 0.35f, 0.4f, 0.2f, 0.15f, 0.08f };
            float[] decay = { 1.2f, 0.9f, 0.7f, 0.6f, 0.5f, 0.35f, 0.25f, 0.2f };
            const float f0 = 1150f;
            float t = 0f;
            int k = 0;
            while (t < loop)
            {
                float a = (k % 2 == 0 ? 1f : 0.72f) * rng.Range(0.85f, 1f);
                NoiseBurst(b, t, rng, Biquad.Highpass(3000f, 0.7f), 0.25f * a, 0.003f);
                for (int i = 0; i < ratio.Length; i++) Partial(b, t, f0 * ratio[i], amp[i] * a, decay[i], rng.Range(0f, 6.28f));
                t += 0.155f * rng.Range(0.82f, 1.18f);
                k++;
            }
            return WrapLoop(b, loop);
        }

        private static float[] LockerDoor(Noise rng)
        {
            var b = Buffer(0.8f);
            NoiseBurst(b, 0f, rng, Biquad.Highpass(3000f, 0.7f), 0.4f, 0.004f);
            // Hinge squeak.
            int squeak = (int)(0.26f * Rate);
            float ph = 0f;
            for (int i = 0; i < squeak; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(900f, 1400f, t / 0.26f) * (1f + 0.02f * Mathf.Sin(2f * Mathf.PI * 23f * t));
                ph += 2f * Mathf.PI * f / Rate;
                b[i] += Mathf.Sin(ph) * 0.13f * Mathf.Sin(Mathf.PI * t / 0.26f);
            }
            float[] freq = { 310f, 740f, 1290f, 2010f, 2870f };
            float[] amp = { 1f, 0.6f, 0.45f, 0.3f, 0.2f };
            float[] decay = { 0.25f, 0.18f, 0.12f, 0.08f, 0.05f };
            for (int i = 0; i < freq.Length; i++) Partial(b, 0.3f, freq[i] * rng.Range(0.97f, 1.03f), amp[i] * 0.5f, decay[i]);
            NoiseBurst(b, 0.3f, rng, Biquad.Bandpass(150f, 2f), 0.9f, 0.06f);
            return b;
        }

        private static float[] ClothRustle(Noise rng)
        {
            var b = Buffer(0.65f);
            var f = Biquad.Bandpass(2400f, 0.8f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Bump(t, 0.08f, 0.07f) + 0.8f * Bump(t, 0.25f, 0.09f) + 0.5f * Bump(t, 0.45f, 0.08f);
                b[i] = f.Process(rng.Next()) * env;
            }
            return b;
        }

        private static float[] Pickup(Noise rng)
        {
            var b = Buffer(0.28f);
            NoiseBurst(b, 0f, rng, Biquad.Highpass(2000f, 0.7f), 0.5f, 0.003f);
            Partial(b, 0.05f, 620f, 0.4f, 0.03f);
            NoiseBurst(b, 0.05f, rng, Biquad.Bandpass(1800f, 1.5f), 0.4f, 0.02f);
            NoiseBurst(b, 0.11f, rng, Biquad.Highpass(2500f, 0.7f), 0.35f, 0.003f);
            return b;
        }

        private static float[] ElevatorChime()
        {
            var b = Buffer(2.1f);
            foreach (var (start, f) in new[] { (0f, 1318.5f), (0.45f, 1046.5f) })
            {
                Partial(b, start, f, 1f, 1.1f, attack: 0.005f);
                Partial(b, start, f * 2f, 0.3f, 0.6f, attack: 0.005f);
                Partial(b, start, f * 3f, 0.12f, 0.3f, attack: 0.005f);
            }
            return b;
        }

        private static float[] ElevatorDoors(Noise rng)
        {
            var b = Buffer(1.9f);
            var lp1 = Biquad.Lowpass(220f, 0.7f);
            var lp2 = Biquad.Lowpass(220f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((1.45f - t) / 0.3f);
                float rumble = lp2.Process(lp1.Process(rng.Next())) * 2.5f;
                float whine = 0.08f * (Mathf.Sin(2f * Mathf.PI * 180f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 360f * t));
                b[i] = (rumble + whine) * env;
            }
            Partial(b, 1.5f, 90f, 0.7f, 0.08f);
            NoiseBurst(b, 1.5f, rng, Biquad.Bandpass(300f, 1.5f), 0.5f, 0.05f);
            return b;
        }

        private static float[] Lever(Noise rng)
        {
            var b = Buffer(2.5f);
            Partial(b, 0f, 70f, 1f, 0.12f);
            NoiseBurst(b, 0f, rng, Biquad.Bandpass(450f, 1f), 0.7f, 0.08f);
            Partial(b, 0f, 260f, 0.4f, 0.3f);
            Partial(b, 0f, 690f, 0.3f, 0.2f);
            Partial(b, 0f, 1180f, 0.2f, 0.12f);
            for (int i = 0; i < 14; i++)
            {
                NoiseBurst(b, rng.Range(0.25f, 0.7f), rng, Biquad.Highpass(3500f, 0.7f), rng.Range(0.1f, 0.35f), 0.002f);
            }
            int start = (int)(0.25f * Rate);
            for (int i = start; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01((t - 0.25f) / 0.4f) * Mathf.Clamp01((2.45f - t) / 0.4f);
                float hum = 0.5f * Mathf.Sin(2f * Mathf.PI * 50f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 100f * t)
                            + 0.25f * Mathf.Sin(2f * Mathf.PI * 150f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 200f * t);
                b[i] += hum * 0.35f * env;
            }
            return b;
        }

        private static float[] ServiceBell(Noise rng)
        {
            var b = Buffer(2.6f);
            NoiseBurst(b, 0f, rng, Biquad.Highpass(4000f, 0.7f), 0.3f, 0.003f);
            const float f0 = 2350f;
            Partial(b, 0f, f0, 1f, 1.6f);
            Partial(b, 0f, f0 * 2.73f, 0.45f, 0.6f);
            Partial(b, 0f, f0 * 5.2f, 0.2f, 0.3f);
            Partial(b, 0f, f0 * 0.5f * 1.003f, 0.12f, 1.2f);
            return b;
        }

        private static float[] Interact(Noise rng)
        {
            var b = Buffer(0.35f);
            Partial(b, 0f, 180f, 0.8f, 0.05f);
            NoiseBurst(b, 0f, rng, Biquad.Bandpass(700f, 1.5f), 0.6f, 0.04f);
            NoiseBurst(b, 0f, rng, Biquad.Highpass(2500f, 0.7f), 0.25f, 0.004f);
            return b;
        }

        private static float[] Gasp(Noise rng)
        {
            var b = Buffer(0.85f);
            var a = Biquad.Bandpass(1100f, 0.7f);
            var c = Biquad.Bandpass(2600f, 1f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.12f) * Mathf.Exp(-Mathf.Max(0f, t - 0.12f) / 0.25f);
                float x = rng.Next();
                float voiced = 0.05f * (Mathf.Sin(2f * Mathf.PI * 180f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 360f * t));
                b[i] = (a.Process(x) * 0.9f + c.Process(x) * 0.5f + voiced) * env;
            }
            return b;
        }

        private static float[] MusicBoxBreak(Noise rng)
        {
            var b = Buffer(1.4f);
            for (int i = 0; i < 6; i++)
            {
                NoiseBurst(b, rng.Range(0f, 0.12f), rng, Biquad.Bandpass(rng.Range(2000f, 4000f), 2f), rng.Range(0.4f, 1f), 0.01f);
            }
            // The spring lets go.
            float ph = 0f;
            int n = (int)(0.6f * Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(300f, 80f, t / 0.6f) * (1f + 0.08f * Mathf.Sin(2f * Mathf.PI * 30f * t));
                ph += 2f * Mathf.PI * f / Rate;
                b[(int)(0.05f * Rate) + i] += Mathf.Sin(ph) * 0.3f * Mathf.Exp(-t / 0.4f);
            }
            // A last few notes, slowing down and going flat.
            float[] notes = { 88, 84, 81 };
            float[] times = { 0.15f, 0.42f, 0.85f };
            for (int i = 0; i < notes.Length; i++) Tine(b, times[i], Midi(notes[i]) * Mathf.Pow(2f, -(0.2f + i * 0.25f) / 12f), 0.6f);
            return b;
        }

        private static float[] NeedleScratch(Noise rng)
        {
            var b = Buffer(0.9f);
            var f = Biquad.Bandpass(3000f, 3f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                if (i % 64 == 0) f = Biquad.Bandpass(Mathf.Lerp(3000f, 600f, t / 0.9f), 3f, f);
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t / 0.35f);
                b[i] = f.Process(rng.Next()) * env * 2f;
            }
            NoiseBurst(b, 0f, rng, Biquad.Highpass(1500f, 0.7f), 0.6f, 0.004f);
            return b;
        }

        // ------------------------------------------------------------------ music loops (original melodies)

        private static float[] MusicBoxLoop(Noise rng)
        {
            // A minor lullaby in 3/4, 96 BPM, 8 bars. (midi note, beats)
            const float beat = 0.625f;
            int[,] melody =
            {
                { 76, 2 }, { 81, 2 }, { 84, 2 },
                { 83, 3 }, { 81, 1 }, { 76, 2 },
                { 77, 2 }, { 81, 2 }, { 86, 2 },
                { 84, 4 }, { 83, 2 },
                { 81, 2 }, { 84, 2 }, { 88, 2 },
                { 86, 3 }, { 84, 1 }, { 83, 2 },
                { 80, 2 }, { 83, 2 }, { 76, 2 },
                { 81, 6 },
            }; // durations in half-beats
            int[] bass = { 57, 52, 50, 57, 57, 53, 52, 57 };
            float loop = 24 * beat;
            var b = Buffer(loop + 2.5f);

            float t = 0f;
            for (int i = 0; i < melody.GetLength(0); i++)
            {
                Tine(b, t + rng.Range(0f, 0.012f), Midi(melody[i, 0]) * Detune(rng), 0.8f);
                t += melody[i, 1] * beat * 0.5f;
            }
            for (int bar = 0; bar < 8; bar++)
            {
                float bt = bar * 3 * beat;
                Tine(b, bt, Midi(bass[bar]) * Detune(rng), 0.55f);
                Tine(b, bt + beat, Midi(bass[bar] + 12 + 7) * Detune(rng), 0.25f);
                int third = bass[bar] == 52 || bass[bar] == 53 ? 4 : 3; // E and F chords are major
                Tine(b, bt + 2 * beat, Midi(bass[bar] + 12 + third) * Detune(rng), 0.22f);
            }
            ApplyWow(b, 0.6f, 0.003f);
            return WrapLoop(b, loop);
        }

        private static float[] GramophoneLoop(Noise rng)
        {
            // D minor waltz in 3/4, 84 BPM, 8 bars: melody over an oom-pah-pah accompaniment, then an old-record filter.
            const float beat = 60f / 84f;
            int[,] melody =
            {
                { 74, 2 }, { 77, 1 },
                { 81, 2 }, { 79, 1 },
                { 77, 1 }, { 76, 1 }, { 74, 1 },
                { 73, 3 },
                { 74, 2 }, { 77, 1 },
                { 82, 2 }, { 81, 1 },
                { 79, 1 }, { 76, 1 }, { 73, 1 },
                { 74, 3 },
            }; // durations in beats
            int[] root = { 50, 45, 43, 45, 50, 46, 45, 50 };
            int[][] chord =
            {
                new[] { 53, 57 }, new[] { 53, 57 }, new[] { 58, 62 }, new[] { 61, 64 },
                new[] { 53, 57 }, new[] { 62, 65 }, new[] { 61, 64 }, new[] { 53, 57 }
            };
            float loop = 24 * beat;
            var b = Buffer(loop + 1.5f);

            float t = 0f;
            for (int i = 0; i < melody.GetLength(0); i++)
            {
                float d = melody[i, 1] * beat;
                Voice(b, t, d * 0.95f, Midi(melody[i, 0]), 6, 0.55f, vibrato: 0.006f);
                t += d;
            }
            for (int bar = 0; bar < 8; bar++)
            {
                float bt = bar * 3 * beat;
                Voice(b, bt, beat * 0.8f, Midi(root[bar]), 4, 0.5f, vibrato: 0f);
                foreach (int beatIdx in new[] { 1, 2 })
                foreach (int note in chord[bar])
                {
                    Voice(b, bt + beatIdx * beat, beat * 0.35f, Midi(note), 3, 0.18f, vibrato: 0f);
                }
            }
            ApplyWow(b, 0.55f, 0.004f);

            // Old record: band-limited, slightly saturated, crackle and hiss.
            var hp = Biquad.Highpass(260f, 0.7f);
            var lp = Biquad.Lowpass(3400f, 0.7f);
            var hiss = Biquad.Highpass(2000f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float x = lp.Process(hp.Process(b[i]));
                x = (float)Math.Tanh(x * 1.4f);
                x += hiss.Process(rng.Next()) * 0.015f;
                if (rng.Next01() < 9f / Rate) x += rng.Next() * 0.5f;
                b[i] = x;
            }
            return WrapLoop(b, loop);
        }

        // ------------------------------------------------------------------ building blocks

        private static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        private static float Midi(float note) => 440f * Mathf.Pow(2f, (note - 69f) / 12f);

        private static float Detune(Noise rng) => Mathf.Pow(2f, rng.Range(-0.08f, 0.08f) / 12f);

        private static float Bump(float t, float center, float width)
        {
            float x = (t - center) / width;
            return Mathf.Exp(-x * x);
        }

        /// <summary>A decaying sine starting at <paramref name="start"/> (optional pitch drop for thumps).</summary>
        private static void Partial(float[] b, float start, float freq, float amp, float decay, float phase = 0f,
            float pitchDrop = 0f, float attack = 0.0005f)
        {
            int s0 = Mathf.Max(0, (int)(start * Rate));
            int n = Mathf.Min(b.Length - s0, (int)(decay * 7f * Rate));
            float ph = phase;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = freq * (1f + pitchDrop * Mathf.Exp(-t / 0.02f));
                ph += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Exp(-t / decay) * Mathf.Clamp01(t / attack);
                b[s0 + i] += Mathf.Sin(ph) * amp * env;
            }
        }

        private static void NoiseBurst(float[] b, float start, Noise rng, Biquad filter, float amp, float decay)
        {
            int s0 = Mathf.Max(0, (int)(start * Rate));
            int n = Mathf.Min(b.Length - s0, (int)(decay * 8f * Rate));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                b[s0 + i] += filter.Process(rng.Next()) * amp * Mathf.Exp(-t / decay) * 2f;
            }
        }

        /// <summary>A music box tine: fundamental, a warm octave and the bright cantilever overtone (6.27x) plus a pluck click.</summary>
        private static void Tine(float[] b, float start, float f, float amp)
        {
            float d = 1.6f * Mathf.Pow(440f / f, 0.3f);
            Partial(b, start, f, amp, d, attack: 0.002f);
            Partial(b, start, f * 2f, amp * 0.12f, d * 0.6f, attack: 0.002f);
            if (f * 6.27f < Rate * 0.45f) Partial(b, start, f * 6.27f, amp * 0.25f, 0.08f, attack: 0.001f);
        }

        /// <summary>A sustained harmonic voice (1/n partials) with a soft attack/release.</summary>
        private static void Voice(float[] b, float start, float duration, float f, int harmonics, float amp, float vibrato)
        {
            int s0 = Mathf.Max(0, (int)(start * Rate));
            int n = Mathf.Min(b.Length - s0, (int)((duration + 0.12f) * Rate));
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.04f) * Mathf.Clamp01((duration + 0.12f - t) / 0.12f) * (1f - 0.25f * Mathf.Clamp01(t / duration));
                ph += 2f * Mathf.PI * f * (1f + vibrato * Mathf.Sin(2f * Mathf.PI * 5.5f * t)) / Rate;
                float x = 0f;
                for (int h = 1; h <= harmonics; h++) x += Mathf.Sin(ph * h) / h;
                b[s0 + i] += x * amp * env;
            }
        }

        /// <summary>Slow pitch wobble (worn mechanism) by resampling with a sinusoidal read offset.</summary>
        private static void ApplyWow(float[] b, float rateHz, float depth)
        {
            var src = (float[])b.Clone();
            float maxShift = depth * Rate / (2f * Mathf.PI * rateHz);
            for (int i = 0; i < b.Length; i++)
            {
                float pos = i + maxShift * Mathf.Sin(2f * Mathf.PI * rateHz * i / Rate);
                int i0 = Mathf.Clamp((int)pos, 0, src.Length - 1);
                int i1 = Mathf.Min(i0 + 1, src.Length - 1);
                float frac = pos - Mathf.Floor(pos);
                b[i] = Mathf.Lerp(src[i0], src[i1], frac);
            }
        }

        /// <summary>Folds everything after <paramref name="loopSeconds"/> back onto the start so the clip loops seamlessly.</summary>
        private static float[] WrapLoop(float[] b, float loopSeconds)
        {
            int n = Mathf.Min(b.Length, Mathf.RoundToInt(loopSeconds * Rate));
            var outBuf = new float[n];
            for (int i = 0; i < b.Length; i++) outBuf[i % n] += b[i];
            return outBuf;
        }

        private static void Normalize(float[] b, float peak)
        {
            float max = 0f;
            foreach (float x in b) max = Mathf.Max(max, Mathf.Abs(x));
            if (max < 1e-6f) return;
            float g = peak / max;
            for (int i = 0; i < b.Length; i++) b[i] *= g;
        }

        // ------------------------------------------------------------------ DSP helpers

        private sealed class Noise
        {
            private uint _s;

            public Noise(uint seed) => _s = seed == 0 ? 1u : seed;

            /// <summary>Uniform in [0, 1).</summary>
            public float Next01()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return (_s & 0xFFFFFF) / 16777216f;
            }

            /// <summary>Uniform in [-1, 1).</summary>
            public float Next() => Next01() * 2f - 1f;

            public float Range(float a, float b) => a + (b - a) * Next01();
        }

        /// <summary>RBJ cookbook biquad.</summary>
        private struct Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public static Biquad Bandpass(float f, float q) => Make(f, q, 0);
            public static Biquad Lowpass(float f, float q) => Make(f, q, 1);
            public static Biquad Highpass(float f, float q) => Make(f, q, 2);

            /// <summary>Retunes a bandpass while keeping the filter state (for sweeps).</summary>
            public static Biquad Bandpass(float f, float q, Biquad state)
            {
                var b = Make(f, q, 0);
                b._x1 = state._x1; b._x2 = state._x2; b._y1 = state._y1; b._y2 = state._y2;
                return b;
            }

            private static Biquad Make(float f, float q, int kind)
            {
                f = Mathf.Clamp(f, 10f, Rate * 0.45f);
                float w = 2f * Mathf.PI * f / Rate;
                float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * q);
                float b0, b1, b2;
                switch (kind)
                {
                    case 0: b0 = alpha; b1 = 0f; b2 = -alpha; break;
                    case 1: b0 = (1f - cos) * 0.5f; b1 = 1f - cos; b2 = (1f - cos) * 0.5f; break;
                    default: b0 = (1f + cos) * 0.5f; b1 = -(1f + cos); b2 = (1f + cos) * 0.5f; break;
                }
                float a0 = 1f + alpha;
                return new Biquad
                {
                    _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0,
                    _a1 = -2f * cos / a0, _a2 = (1f - alpha) / a0
                };
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }
    }
}
