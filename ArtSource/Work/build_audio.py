"""Builds the recorded sound effects and maid voice lines in Assets/_Project/Resources/Audio/<Sfx>/.

Every folder name is a value of the `Sfx` enum (Scripts/Audio/ProceduralSfx.cs); `SoundBank` loads all clips of a
folder as variants and falls back to the synthesized placeholder when a folder is missing.

Inputs (git-ignored, see credits.md for every source and licence):
  ArtSource/Downloads/Freesound/   CC0 originals from freesound.org, file names "<id>__<user>__<title>.<ext>"
  ArtSource/Downloads/ElevenLabs/  AI voice takes (ElevenLabs): one take per maid and language holding all her lines
  ArtSource/Work/voice_words.json  word timings of the chosen voice takes (speech-to-text, made once), used to find
                                   where each line starts and ends

Processing: decode with ffmpeg to mono 44.1 kHz, cut (single footsteps are found by onset detection, other cuts are
listed below in seconds), fade, normalize, write 16-bit PCM WAV. Loops get an equal-power crossfade at the seam.
Voice clips are named <lang>_<n>.wav (lang = en | pt); `SoundBank.GetLocalized` picks the game language's clips.
The violin stingers are different: single spiccato notes (VSCO 2 CE, Freesound/VSCO2_Violin/) re-pitched and
layered into chords, then given EQ, light saturation and a synthetic reverb tail; written as stereo WAVs.

Usage (from the project root; needs numpy, scipy and ffmpeg on PATH). Rebuilds every folder listed in FOLDERS, or
only the folders of the steps named on the command line (footsteps, one-shots, loops, voices, violin):
  python ArtSource/Work/build_audio.py
  python ArtSource/Work/build_audio.py violin
"""
import difflib
import json
import os
import re
import subprocess
import sys
import unicodedata
import wave
import zlib
from fractions import Fraction

import numpy as np
from scipy import signal

SR = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FREESOUND = os.path.join(ROOT, "ArtSource", "Downloads", "Freesound")
ELEVEN = os.path.join(ROOT, "ArtSource", "Downloads", "ElevenLabs")
WORDS = os.path.join(ROOT, "ArtSource", "Work", "voice_words.json")
OUT = os.path.join(ROOT, "Assets", "_Project", "Resources", "Audio")


# ---------------------------------------------------------------------------------------------------------- basics

def load(path):
    """Mono float32 samples at SR (any format ffmpeg reads; stereo is averaged)."""
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)


def freesound(sound_id):
    """A Freesound download by id (also searched in subfolders, e.g. VSCO2_Violin/)."""
    for folder, _, names in os.walk(FREESOUND):
        for name in names:
            if name.startswith(f"{sound_id}__"):
                return load(os.path.join(folder, name))
    raise FileNotFoundError(f"Freesound {sound_id} not found in {FREESOUND}")


def cut(x, start, end):
    return x[max(0, int(start * SR)):min(len(x), int(end * SR))].copy()


def fade(x, fade_in=0.005, fade_out=0.03):
    x = x.copy()
    n_in = min(len(x), int(fade_in * SR))
    n_out = min(len(x), int(fade_out * SR))
    if n_in > 0:
        x[:n_in] *= np.sin(np.linspace(0, np.pi / 2, n_in)) ** 2
    if n_out > 0:
        x[-n_out:] *= np.cos(np.linspace(0, np.pi / 2, n_out)) ** 2
    return x


def highpass(x, hz):
    """2nd-order Butterworth high-pass, zero phase: removes rumble and DC."""
    sos = signal.butter(2, hz, btype="highpass", fs=SR, output="sos")
    return signal.sosfiltfilt(sos, x)


def peak_normalize(x, db=-3.0):
    p = np.max(np.abs(x))
    return x if p < 1e-9 else x * (10 ** (db / 20) / p)


def rms_normalize(x, rms_db=-20.0, peak_cap_db=-1.0):
    """Matches loudness (RMS of the louder half of the frames) while keeping the peak below the cap."""
    h = int(0.02 * SR)
    n = max(1, len(x) // h)
    frames = np.sqrt(np.mean(x[:n * h].reshape(n, h) ** 2, axis=1) + 1e-12)
    loud = np.sort(frames)[n // 2:]
    gain = 10 ** (rms_db / 20) / max(np.mean(loud), 1e-9)
    gain = min(gain, 10 ** (peak_cap_db / 20) / max(np.max(np.abs(x)), 1e-9))
    return x * gain


def loop_crossfade(x, seconds):
    """Makes x loop seamlessly: its last `seconds` are crossfaded (equal power) into its start."""
    n = int(seconds * SR)
    head, body, tail = x[:n], x[n:-n], x[-n:]
    t = np.linspace(0, np.pi / 2, n)
    return np.concatenate([tail * np.cos(t) + head * np.sin(t), body])


def write(sfx, name, x):
    """16-bit WAV: mono for a 1-D array, stereo for an (n, 2) array."""
    folder = os.path.join(OUT, sfx)
    os.makedirs(folder, exist_ok=True)
    pcm = np.clip(x, -1.0, 1.0)
    pcm = (pcm * 32767).astype("<i2")
    with wave.open(os.path.join(folder, name + ".wav"), "wb") as w:
        w.setnchannels(1 if pcm.ndim == 1 else pcm.shape[1])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def frame_db(x, hop=0.01):
    h = int(hop * SR)
    n = len(x) // h
    rms = np.sqrt(np.mean(x[:n * h].reshape(n, h) ** 2, axis=1) + 1e-12)
    return 20 * np.log10(rms / rms.max())


def onsets(x, thr_db=-30.0, min_gap=0.12, hop=0.01):
    """Sound events [(start s, end s, peak dB rel. max)] separated by at least `min_gap` of quieter frames."""
    db = frame_db(x, hop)
    segs, start, last = [], None, 0
    for i, loud in enumerate(db > thr_db):
        if loud:
            start = i if start is None else start
            last = i
        elif start is not None and (i - last) * hop >= min_gap:
            segs.append((start, last))
            start = None
    if start is not None:
        segs.append((start, last))
    return [(a * hop, (b + 1) * hop, float(db[a:b + 1].max())) for a, b in segs]


# ------------------------------------------------------------------------------------------------------- footsteps

def footsteps(sfx, sound_id, length, count, window=(0.0, 1e9), thr_db=-30.0, peak_range=(-16.0, -1.0),
              max_event=0.25, hp=70.0, level_db=-3.0):
    """Single steps cut at their onsets: skips scuffs (events longer than `max_event`) and outliers in loudness,
    then keeps `count` steps spread over the recording."""
    x = highpass(freesound(sound_id), hp)
    events = [e for e in onsets(x, thr_db) if window[0] <= e[0] <= window[1]]
    good = [e for e in events if e[1] - e[0] <= max_event and peak_range[0] <= e[2] <= peak_range[1]]
    if len(good) < count:
        raise RuntimeError(f"{sfx}: only {len(good)} usable steps in {sound_id}")
    pick = [good[int(round(i * (len(good) - 1) / (count - 1)))] for i in range(count)]
    starts = [e[0] for e in events]
    for n, (s, _, _) in enumerate(pick, 1):
        later = [t for t in starts if t > s + 0.05]
        end = min(s + length, (later[0] - 0.02) if later else s + length)
        clip = fade(cut(x, s - 0.015, end), 0.003, min(0.08, (end - s) * 0.5))
        write(sfx, f"step_{n:02d}", peak_normalize(clip, level_db))


def build_footsteps():
    # Walking: sneakers on a wooden floor (the slow walking part of the take) and on tiles.
    footsteps("FootstepWalkWood", 235718, 0.38, 6, window=(7.5, 22.5))
    footsteps("FootstepWalkTile", 170506, 0.38, 6, peak_range=(-19.0, -4.0))
    # Running: the same kind of shoes, fast pace.
    footsteps("FootstepRunWood", 505297, 0.30, 6, window=(0.2, 11.0), peak_range=(-13.0, -3.0))
    footsteps("FootstepRunTile", 170505, 0.30, 6, peak_range=(-12.0, -2.0))
    # Crouching: quiet rubber-soled steps (a quiet take with a high noise floor, hence the higher threshold).
    footsteps("FootstepCrouch", 613723, 0.30, 6, window=(7.3, 13.3), thr_db=-14.0, peak_range=(-10.0, 0.5),
              hp=120.0)


# ------------------------------------------------------------------------------------------------------ one-shots

def one_shots():
    # Crouching down / getting up: cloth movement, seven takes in one recording.
    x = highpass(freesound(464995), 80)
    for n, (s, e, _) in enumerate([ev for ev in onsets(x) if ev[1] - ev[0] > 0.5][:6], 1):
        write("CrouchCloth", f"cloth_{n:02d}", peak_normalize(fade(cut(x, s - 0.05, min(e + 0.1, s + 1.2)), 0.01, 0.15), -6))

    # Hiding under the table: tablecloth rustle.
    x = highpass(freesound(19291), 80)
    write("ClothRustle", "rustle_01", peak_normalize(fade(cut(x, 0.42, 0.98), 0.005, 0.12), -4))
    write("ClothRustle", "rustle_02", peak_normalize(fade(cut(x, 0.95, 2.0), 0.01, 0.2), -4))

    # Locker: squeaky door opening, then the slam of it closing.
    x = highpass(freesound(442370), 60)
    write("LockerOpen", "open_01", peak_normalize(fade(cut(x, 0.12, 1.75), 0.005, 0.12), -2))
    write("LockerClose", "close_01", peak_normalize(fade(cut(x, 3.45, 6.2), 0.01, 0.2), -2))

    # Elevator: the recordings are a large goods lift (4-5 s); the car doors slide in ~0.9 s (ElevatorCar.doorSpeed),
    # so the opening keeps the release clunk and the start of the slide, the closing keeps the end of the slide + slam.
    x = highpass(freesound(161228), 40)
    write("ElevatorDoorOpen", "open_01", peak_normalize(fade(cut(x, 0.25, 2.1), 0.005, 0.6), -2))
    x = highpass(freesound(161230), 40)
    write("ElevatorDoorClose", "close_01", peak_normalize(fade(cut(x, 2.0, 3.95), 0.15, 0.1), -2))
    x = freesound(556700)
    write("ElevatorChime", "chime_01", peak_normalize(fade(cut(x, 0.15, 2.2), 0.002, 0.3), -3))

    # The reserved customer's "ring for service" desk bell.
    x = freesound(685111)
    write("ServiceBell", "bell_01", peak_normalize(fade(cut(x, 0.75, 2.65), 0.002, 0.3), -2))

    # Player breathing.
    x = highpass(freesound(547906), 80)  # quick deep breath in, before holding it
    write("BreathHoldStart", "inhale_01", peak_normalize(fade(cut(x, 1.35, 2.35), 0.02, 0.15), -3))
    x = highpass(freesound(419779), 80)  # letting the held breath out
    write("BreathRelease", "exhale_01", peak_normalize(fade(cut(x, 0.65, 1.6), 0.02, 0.2), -3))
    write("BreathRelease", "exhale_02", peak_normalize(fade(cut(x, 3.45, 4.8), 0.02, 0.25), -3))
    x = highpass(freesound(401333), 80)  # ran out of breath
    write("Gasp", "gasp_01", peak_normalize(fade(cut(x, 0.2, 1.1), 0.005, 0.15), -1))


# ---------------------------------------------------------------------------------------------------------- loops

def loops():
    # Scared, frozen breathing while hidden (made as a loop by its author).
    x = highpass(freesound(554906), 60)
    write("BreathHideLoop", "loop_01", peak_normalize(fade(x, 0.003, 0.003), -3))
    # Fast breathing while sprinting (loop).
    x = highpass(freesound(554907), 60)
    write("BreathRunLoop", "loop_01", peak_normalize(fade(x, 0.003, 0.003), -3))
    # Panting once out of stamina: 1.0-7.4 s lies between two quiet troughs of the breathing cycle.
    x = highpass(freesound(566400), 60)
    write("BreathExhaustedLoop", "loop_01", peak_normalize(loop_crossfade(cut(x, 1.0, 7.5), 0.1), -3))
    # The bell maid's hand bell, rung continuously (3.85-8.45 s of the take).
    x = highpass(freesound(503134), 100)
    write("HandBellLoop", "loop_01", peak_normalize(loop_crossfade(cut(x, 3.85, 8.75), 0.3), -2))


# ---------------------------------------------------------------------------------------------------------- voices

PATROL_EN = ["did i hear something", "i swear i heard something", "where's this noise coming from",
             "hello is someone hiding in here", "hehe i can hear you breathing"]
PATROL_PT = ["eu ouvi alguma coisa", "eu juro que ouvi alguma coisa", "de onde vem esse barulho",
             "oie tem alguem escondido aqui", "hihi eu to ouvindo voce respirar"]

# Sfx folder -> {language: (take, lines)}; the watcher's five lines are split into two folders below.
VOICES = {
    "VoicePatrol": {"en": ("patrol_design_v1.mp3", PATROL_EN), "pt": ("patrol_pt_t1.mp3", PATROL_PT)},
    "VoiceStyleAnime": {"en": ("styleanime_en_t1.mp3", PATROL_EN), "pt": ("styleanime_pt_t2.mp3", PATROL_PT)},
    "VoiceTypical": {"en": ("typical_en_t1.mp3", PATROL_EN), "pt": ("typical_pt_t2.mp3", PATROL_PT)},
    "VoiceHead": {
        "en": ("head_design_v1.mp3", ["hm did i hear something", "i'm certain i heard something",
                                       "now where is this noise coming from", "come out dear guest don't be shy",
                                       "such ragged breathing you're close aren't you"]),
        "pt": ("head_pt_t2.mp3", ["hm eu ouvi alguma coisa", "tenho certeza de que ouvi alguma coisa",
                                  "ora de onde vem esse barulho", "pode sair querido cliente nao seja timido",
                                  "que respiracao ofegante voce esta perto nao esta"]),
    },
    "Watcher": {
        "en": ("watcher_design_v1.mp3", ["um is someone there", "did i just see something",
                                          "a guest please wait right there", "everyone a guest is here",
                                          "over here please hurry"]),
        "pt": ("watcher_pt_t2.mp3", ["hum tem alguem ai", "eu vi alguma coisa", "um cliente por favor espere ai",
                                     "pessoal chegou um cliente", "aqui por favor venham rapido"]),
    },
}


def norm_token(word):
    word = unicodedata.normalize("NFKD", word.lower())
    word = "".join(c for c in word if not unicodedata.combining(c))
    return re.sub(r"[^a-z0-9']", "", word)


def line_spans(words, lines):
    """(start, end) of each scripted line in the take, from the speech-to-text word timings. The script and the
    recognised words are aligned with difflib, so a missed or misheard word (giggles, "Hm" heard as "Hã") only
    loses that word."""
    heard = [norm_token(w) for w, _, _ in words]
    script, owner = [], []
    for i, line in enumerate(lines):
        for token in line.split():
            script.append(norm_token(token))
            owner.append(i)
    spans = [[None, None] for _ in lines]
    matcher = difflib.SequenceMatcher(a=script, b=heard, autojunk=False)
    for op, a0, a1, b0, b1 in matcher.get_opcodes():
        if op == "insert":
            continue
        for k in range(a1 - a0):
            b = b0 + min(k, b1 - b0 - 1) if b1 > b0 else None
            if b is None:
                continue
            line = owner[a0 + k]
            s, e = words[b][1], words[b][2]
            spans[line][0] = s if spans[line][0] is None else min(spans[line][0], s)
            spans[line][1] = e if spans[line][1] is None else max(spans[line][1], e)
    for i, (s, e) in enumerate(spans):
        if s is None:
            raise RuntimeError(f"line {i + 1} '{lines[i]}' not found in the take")
    return spans


def silences(x, thr_db=-38.0, min_len=0.12, hop=0.01):
    db = frame_db(x, hop)
    out, start = [], None
    for i, quiet in enumerate(db < thr_db):
        if quiet and start is None:
            start = i
        elif not quiet and start is not None:
            if (i - start) * hop >= min_len:
                out.append((start * hop, i * hop))
            start = None
    if start is not None and (len(db) - start) * hop >= min_len:
        out.append((start * hop, len(db) * hop))
    return out


def slice_take(take, lines, words):
    """Cuts a take into one clip per line. Each boundary is the first silence after the previous line's last word,
    so sounds without words before a line (a giggle, a breath) stay with the line they introduce."""
    x = highpass(load(os.path.join(ELEVEN, take)), 70)
    spans = line_spans(words[take], lines)
    gaps = silences(x)
    cuts = []
    for i in range(len(lines) - 1):
        end_i, start_next = spans[i][1], spans[i + 1][0]
        candidates = [g for g in gaps if g[1] > end_i - 0.05 and g[0] < start_next + 0.1]
        if candidates:
            g = candidates[0]
            cuts.append((min(g[0] + 0.08, (g[0] + g[1]) / 2), max(g[1] - 0.05, (g[0] + g[1]) / 2)))
        else:
            mid = (end_i + start_next) / 2
            cuts.append((mid, mid))
    first = next((g[1] for g in gaps if g[0] <= spans[0][0] - 0.02 and g[1] <= spans[0][0] + 0.05), 0.0)
    last = next((g[0] for g in gaps if g[0] >= spans[-1][1] - 0.05), len(x) / SR)
    bounds = [(max(0.0, first - 0.04), cuts[0][0])] + \
             [(cuts[i][1], cuts[i + 1][0]) for i in range(len(cuts) - 1)] + \
             [(cuts[-1][1], min(len(x) / SR, last + 0.1))]
    clips = []
    for (s, e) in bounds:
        clips.append(rms_normalize(fade(cut(x, s, e), 0.005, 0.04), -20.0, -1.0))
    return clips, bounds


def build_voices():
    words = json.load(open(WORDS, encoding="utf8"))
    for sfx, langs in VOICES.items():
        for lang, (take, lines) in langs.items():
            clips, bounds = slice_take(take, lines, words)
            print(f"  {sfx}/{lang}: " + "  ".join(f"[{s:.2f}-{e:.2f}]" for s, e in bounds))
            if sfx == "Watcher":
                for n, clip in enumerate(clips[:3], 1):
                    write("VoiceWatcherNotice", f"{lang}_{n}", clip)
                for n, clip in enumerate(clips[3:], 1):
                    write("VoiceWatcherCall", f"{lang}_{n}", clip)
            else:
                for n, clip in enumerate(clips, 1):
                    write(sfx, f"{lang}_{n}", clip)


# ------------------------------------------------------------------------------------------------- violin stingers

# Non-diegetic violin stabs (Scripts/Audio/TensionStingers.cs): original dissonant clusters layered from single
# spiccato notes of the VSCO 2 Community Edition (sgossner, CC0; original WAVs in Freesound/VSCO2_Violin/).
# Each note is re-pitched from the nearest sample (at most 3 semitones away) to its target, a few cents out of tune
# on purpose. Clips are stereo (layers spread left to right, decorrelated reverb), unlike the diegetic mono sounds.

# Freesound id -> (kind, measured f0 in Hz). "ens" = violin section, "solo" = solo violin. f0 measured (YIN, first
# 250 ms after the attack); every sample sounds one octave above the note in its VSCO file name.
VIOLIN_SAMPLES = {
    374542: ("ens", 196.3), 374553: ("ens", 195.8), 374535: ("ens", 246.3), 374546: ("ens", 246.1),
    374538: ("ens", 293.4), 374549: ("ens", 293.2), 374541: ("ens", 372.1), 374534: ("ens", 440.0),
    374556: ("ens", 440.4), 374537: ("ens", 524.5), 374540: ("ens", 657.9), 374551: ("ens", 657.6),
    374543: ("ens", 787.0), 374554: ("ens", 785.0), 374536: ("ens", 993.6), 374547: ("ens", 987.1),
    374539: ("ens", 1169.9), 374550: ("ens", 1176.0),
    373846: ("solo", 195.7), 373847: ("solo", 391.7), 373840: ("solo", 524.0), 373844: ("solo", 657.3),
    373848: ("solo", 783.9), 373852: ("solo", 881.8), 373841: ("solo", 1047.6), 373845: ("solo", 1317.6),
    373849: ("solo", 1570.8), 373842: ("solo", 2093.6),
}

# (clip name, MIDI notes): minor seconds, tritones and minor ninths. Calm stabs are thin (one sample per note) with
# a long dark tail. Chase stabs go from low and warm to high and shrill; TensionStingers climbs through them as the
# chase heats up (variant index ~ heat), so keep them sorted. The catch stab stacks every register.
VIOLIN_CALM = [("calm_1", [62, 63, 69]), ("calm_2", [59, 72, 77]), ("calm_3", [76, 88, 89]),
               ("calm_4", [55, 61, 66, 67])]
VIOLIN_CHASE = [("chase_1", [62, 63, 68]), ("chase_2", [66, 67, 72]), ("chase_3", [69, 70, 75]),
                ("chase_4", [72, 73, 78]), ("chase_5", [76, 77, 82]), ("chase_6", [79, 80, 85]),
                ("chase_7", [83, 84, 89]), ("chase_8", [86, 87, 92, 93])]
VIOLIN_CATCH = [("catch_1", [55, 61, 66, 72, 77, 83, 88, 89]), ("catch_2", [59, 65, 70, 76, 81, 86, 87, 92])]

_violin_cache = {}


def violin_sample(sound_id):
    """The sample tightened for a stab: it starts 20 ms before its loudest point (the section samples swell in over
    70-180 ms because the players are not exactly together), with a 4 ms fade-in, rumble removed, scaled to unit RMS
    over its first 250 ms. Every layer of a chord therefore peaks at the same time."""
    if sound_id not in _violin_cache:
        x = highpass(freesound(sound_id), 120)
        env = np.convolve(np.abs(x), np.ones(88) / 88, "same")
        peak = int(np.argmax(env[:int(0.3 * SR)]))
        x = fade(x[max(0, peak - int(0.02 * SR)):], 0.004, 0.0)
        _violin_cache[sound_id] = x / max(np.sqrt(np.mean(x[:int(0.25 * SR)] ** 2)), 1e-9)
    return _violin_cache[sound_id]


def violin_pick(midi, used, kind=None):
    """The sample closest in pitch to `midi` (a sample already in the chord counts as 1.5 semitones further)."""
    target = 440.0 * 2 ** ((midi - 69) / 12)
    best = None
    for sound_id, (k, f0) in VIOLIN_SAMPLES.items():
        shift = 12 * np.log2(target / f0)
        if abs(shift) > 3.0 or (kind is not None and k != kind):
            continue
        score = abs(shift) + (1.5 if sound_id in used else 0.0)
        if best is None or score < best[0]:
            best = (score, sound_id)
    return None if best is None else best[1]


def repitch(x, ratio):
    """Band-limited resampling that plays x `ratio` times faster (pitch up by `ratio`)."""
    f = Fraction(ratio).limit_denominator(400)
    return signal.resample_poly(x, f.denominator, f.numerator)


def peaking(x, hz, gain_db, q=0.8):
    """RBJ peaking EQ (along the first axis)."""
    a_ = 10 ** (gain_db / 40)
    w = 2 * np.pi * hz / SR
    alpha = np.sin(w) / (2 * q)
    b = [1 + alpha * a_, -2 * np.cos(w), 1 - alpha * a_]
    a = [1 + alpha / a_, -2 * np.cos(w), 1 - alpha / a_]
    return signal.lfilter(b, a, x, axis=0)


def reverb_ir(rt60, rng, predelay=0.018):
    """Stereo room tail: decorrelated noise per channel, highs dying 2.5x faster than lows, a few early taps."""
    n = int((predelay + rt60 * 1.1) * SR)
    t = np.arange(n) / SR
    lowpass = signal.butter(2, 2200, btype="lowpass", fs=SR, output="sos")
    ir = np.zeros((n, 2))
    for ch in range(2):
        noise = rng.standard_normal(n)
        low = signal.sosfilt(lowpass, noise)
        tail = low * np.exp(-6.91 * t / rt60) + 0.35 * (noise - low) * np.exp(-6.91 * t / (rt60 * 0.4))
        tail[:int(predelay * SR)] = 0.0
        for _ in range(6):
            tail[int(rng.uniform(0.006, 0.045) * SR)] += rng.uniform(-3.0, 3.0)
        ir[:, ch] = tail / np.sqrt(np.sum(tail ** 2))
    return ir


def violin_stab(name, notes, dry_len, doubled, rt60, wet_db, bite_db, drive, rms_db):
    """One stab: every note of `notes` re-pitched, slightly detuned and spread over the stereo field, an edge around
    3 kHz (towards a sul ponticello rasp), optional baked saturation (`drive` > 1), then a reverb tail."""
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    lo, hi = min(notes), max(notes)
    dry = np.zeros((int((dry_len + 0.02) * SR), 2))
    used = set()
    for midi in notes:
        first = violin_pick(midi, used)
        layers = [(first, 1.0)]
        if doubled:
            other = "solo" if VIOLIN_SAMPLES[first][0] == "ens" else "ens"
            second = violin_pick(midi, used | {first}, other)
            if second is not None:
                layers.append((second, 10 ** (-5 / 20)))
        rel = (midi - lo) / max(1, hi - lo)
        for sound_id, gain in layers:
            used.add(sound_id)
            cents = rng.uniform(-12.0, 12.0)
            target = 440.0 * 2 ** ((midi - 69 + cents / 100) / 12)
            x = repitch(violin_sample(sound_id), target / VIOLIN_SAMPLES[sound_id][1])[:int(dry_len * SR)]
            x = fade(x, 0.0, min(0.15, dry_len * 0.4)) * gain * (1.0 + 0.2 * rel)
            pan = float(np.clip(-0.35 + 0.7 * rel + rng.uniform(-0.1, 0.1), -1, 1))
            angle = (pan + 1) * np.pi / 4
            d = int(rng.uniform(0.0, 0.006) * SR)
            dry[d:d + len(x), 0] += x * np.cos(angle)
            dry[d:d + len(x), 1] += x * np.sin(angle)
    dry = peaking(dry, 3000, bite_db)
    if drive > 1.0:
        p = np.max(np.abs(dry))
        dry = np.tanh(drive * dry / p) / np.tanh(drive) * p
    ir = reverb_ir(rt60, rng)
    mid = dry.mean(axis=1)
    out = np.zeros((len(dry) + len(ir) - 1, 2))
    out[:len(dry)] += dry
    for ch in range(2):
        out[:, ch] += signal.fftconvolve(mid, ir[:, ch]) * 10 ** (wet_db / 20)
    # Trim where the tail has fallen 60 dB below the peak, fade the last 0.25 s.
    env = np.abs(out).max(axis=1)
    above = np.nonzero(env > np.max(env) * 1e-3)[0]
    out = out[:above[-1] + 1] if len(above) else out
    n_out = min(len(out), int(0.25 * SR))
    out[-n_out:] *= np.cos(np.linspace(0, np.pi / 2, n_out))[:, None] ** 2
    # Loudness as rms_normalize (louder half of the 20 ms frames of the mid signal), peak capped at -1 dBFS.
    h = int(0.02 * SR)
    m = out.mean(axis=1)
    frames = np.sqrt(np.mean(m[:len(m) // h * h].reshape(-1, h) ** 2, axis=1) + 1e-12)
    gain = 10 ** (rms_db / 20) / max(np.mean(np.sort(frames)[len(frames) // 2:]), 1e-9)
    gain = min(gain, 10 ** (-1 / 20) / max(np.max(np.abs(out)), 1e-9))
    return out * gain


def build_violin():
    for name, notes in VIOLIN_CALM:
        write("ViolinStab", name, violin_stab(name, notes, dry_len=0.7, doubled=False, rt60=2.0, wet_db=-8.0,
                                              bite_db=2.0, drive=1.0, rms_db=-20.0))
    for i, (name, notes) in enumerate(VIOLIN_CHASE):
        write("ViolinChase", name, violin_stab(name, notes, dry_len=0.45, doubled=True, rt60=1.0, wet_db=-14.0,
                                               bite_db=4.0 + 0.5 * i, drive=1.2 + 0.15 * i, rms_db=-18.0))
    for name, notes in VIOLIN_CATCH:
        write("ViolinCatch", name, violin_stab(name, notes, dry_len=0.9, doubled=True, rt60=2.6, wet_db=-9.0,
                                               bite_db=5.0, drive=2.0, rms_db=-16.0))


# ------------------------------------------------------------------------------------------------------------ main

# Output folders per step (each step empties its folders before writing them).
FOLDERS = {
    "footsteps": ["FootstepWalkWood", "FootstepWalkTile", "FootstepRunWood", "FootstepRunTile", "FootstepCrouch"],
    "one-shots": ["CrouchCloth", "ClothRustle", "LockerOpen", "LockerClose", "ElevatorDoorOpen", "ElevatorDoorClose",
                  "ElevatorChime", "ServiceBell", "BreathHoldStart", "BreathRelease", "Gasp"],
    "loops": ["BreathHideLoop", "BreathRunLoop", "BreathExhaustedLoop", "HandBellLoop"],
    "voices": ["VoicePatrol", "VoiceStyleAnime", "VoiceTypical", "VoiceHead", "VoiceWatcherNotice",
               "VoiceWatcherCall"],
    "violin": ["ViolinStab", "ViolinChase", "ViolinCatch"],
}
BUILT = [folder for folders in FOLDERS.values() for folder in folders]


def main(only=None):
    steps = [("footsteps", build_footsteps), ("one-shots", one_shots), ("loops", loops), ("voices", build_voices),
             ("violin", build_violin)]
    if only:
        unknown = set(only) - set(FOLDERS)
        if unknown:
            raise SystemExit(f"unknown step(s) {sorted(unknown)}; steps: {list(FOLDERS)}")
        steps = [s for s in steps if s[0] in only]
    for label, fn in steps:
        # Start from empty folders so a removed variant does not linger (Unity keeps the .meta of a rewritten file).
        for sfx in FOLDERS[label]:
            folder = os.path.join(OUT, sfx)
            if os.path.isdir(folder):
                for name in os.listdir(folder):
                    if name.endswith(".wav"):
                        os.remove(os.path.join(folder, name))
        print(label)
        fn()
    total = sum(len(f) for _, _, f in os.walk(OUT))
    print(f"done: {OUT} ({total} files)")


if __name__ == "__main__":
    main(sys.argv[1:])
