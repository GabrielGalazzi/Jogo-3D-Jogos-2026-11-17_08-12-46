# Changelog

All notable changes, by day. Used as the source for the GDD (`GDD.md`).
Format: `## YYYY-MM-DD` followed by bullet points grouped by area.

## 2026-11-24 (final title, menu music, rebuilds)
### Game
- Final title (user decision): **Always at Your Service** (EN) / **Sempre ao Seu Serviço** (PT). `strings.json`
  `game.title`; `PlayerSettings.productName` = "Always at Your Service" (window title, WebGL page title and footer);
  `BuildTools.ExeName` = `AlwaysAtYourService.exe` (data folder `AlwaysAtYourService_Data`). The product name change
  also moves the PlayerPrefs registry key, so saved settings start from the defaults once.
- Main menu title: auto-sized on one line (88 → min 56 pt, no wrapping), so the longer titles never wrap into the
  tagline.
### Audio
- User request: the music box lullaby (`Sfx.MusicBoxLoop`, the objective's tune) loops in the main menu
  (`MainMenuController.BuildBackground`: plain AudioSource behind the maid, volume 0.55, spatial blend 0.35; the menu
  has no acoustic world).
### Build
- Rebuilt Windows (429 MB, 160 s) and WebGL (117 MB, 113 s, SDK web files copied; page title "Always at Your
  Service"); editor switched back to Windows. Zips (files at the root): `Builds/AlwaysAtYourService_Windows_2026-11-24.zip`
  (235 MB, 196 files, Burst backup folder excluded) and `Builds/AlwaysAtYourService_WebGL_2026-11-24.zip` (121 MB,
  32 files, `index.html` at the root). Not play-tested (user: everything else already verified).
### Docs
- `GDD.pdf`: formatted A4 export of `GDD.md` (cover, table of contents with page numbers and links, PDF
  bookmarks; 11 pages). Content unchanged; re-export it whenever `GDD.md` changes.
- `GDD.md` §12 Audio: removed the "Ainda faltam" line (maid footsteps, heartbeat, ambience); `GDD.pdf` re-exported.
- `GDD.md` §12 Music: removed "Ambiente: ainda por fazer". User decision: no ambient music ships (`jobs.md` updated);
  `GDD.pdf` re-exported.

## 2026-11-24 (WebGL build for itch.io, with the ray-traced audio)
### Build
- User request: a WebGL build to publish on itch.io, keeping the ray-traced audio ("a core part of the game").
  `BuildTools.BuildWebGL` (menu `MaidCafe → Build WebGL (itch.io)`, batch `-executeMethod ...BuildWebGL`) →
  `Builds/WebGL/`: gzip with decompression fallback (itch.io sends no Content-Encoding header), data caching, 1280×720
  canvas, page template `Assets/WebGLTemplates/MaidCafe` (Unity's Default template + SDK loader), copies the SDK's web
  files to `vaudio/`; switches to WebGL for the build and back to Windows afterwards.
- WebGL uses the "PC" quality level (same look as Windows). The PC level excluded WebGL, and URP strips shader variants
  by the levels *included* for the target: the first builds stripped every URP Lit / UberPost variant (3D world not drawn,
  only the UI). `BuildTools` now also includes the level for WebGL; URP's "strip unused post-processing variants" is
  turned off for the WebGL build only (the volumes are created in code, so no profile asset tells URP what to keep).
- Download size: the 2K surface/prop texture sets made the data file 220 MB (itch.io limit: 200 MB per file).
  `SurfaceAssetPipeline.LimitForWebGL` gives every surface and prop texture a WebGL-only 1024 px override (Windows keeps
  2048); menu `MaidCafe → Art → Reimport Surface and Prop Textures`. Build: ~118 MB.
### Audio
- Ray-traced audio in the browser: the SDK's 3D WebAssembly build (.NET WASM, worker threads) is loaded by the page into
  `window.MaidCafeVAudio`; `Plugins/WebGL/VAudioWeb.jslib` exposes it to C# with integer handles; `VAudioNativeWeb.cs`
  (WebGL player only) implements every `VAudioNative` function on top of it, so `AcousticWorld`, `RaytracedSource`,
  `RaytracedListener` and `MaidHearing` are unchanged. Voxel data is reordered from the native layout
  ((x·h + y)·d + z) to the WebAssembly one (x + y·w + z·w·h), verified in the browser. The native `DllImport`s are now
  desktop/editor only (a WebGL build calling them would abort). `AcousticWorld.TryLateInit` creates the world when the
  SDK finishes loading after the game scene started. Requires a cross-origin isolated page (itch.io: "SharedArrayBuffer
  support"); without it the browser build uses line-of-sight audio.
- Browsers ignore AudioLowPassFilter, AudioReverbZone and OnAudioFilterRead: `WebAudioBridge` sends each playing traced
  source's position + low-pass cutoff and the listener's reverb to the page once per frame; the jslib routes every Unity
  channel through its own Web Audio low-pass (matched to the source by position) and a shared convolution reverb (decay,
  HF damping, pre-delay and level from the traced EAX, impulse regenerated and cross-faded when the room changes).
- `StingerMixer` on WebGL: the violin stabs play on 8 plain AudioSources (same clips, gain and pitch; no saturation);
  it never reads clip metadata there (the browser decodes clips asynchronously).
- The browser world uses the worker threads the page reserved (4) instead of `SystemInfo.processorCount`, which a
  browser under-reports (the first in-game test ran on 1 thread).
### UI
- WebGL: Quit buttons hidden (main menu and pause menu); the fullscreen toggle asks the browser for fullscreen instead
  of resizing the canvas. The page ignores browser refusals of mouse lock instead of showing Unity's error alert.
- Page template: the game fills the page (the itch.io frame) above the 38 px footer instead of a fixed 1280×720 canvas
  plus footer, which scrolled and hid the bottom of the game and the fullscreen button in a 1280×720 frame.
### Verification
- EditMode tests 40/40. SDK checks in a cross-origin isolated page (headless Chrome): voxel layout x + y·w + z·w·h,
  custom material ids, a wall cuts LF to 0.26 / HF to 0.01, EAX values, `World_Update` returns nothing (handled).
- Browser build served with COOP/COEP, headless Chrome on the GPU (D3D11): SDK ready in ~1.3 s, Unity loaded in ~10 s,
  menu and floor render (236-240 FPS on the test machine), "Vercidium Audio 1.11.1 (WebAssembly) running (4 threads)",
  floor voxelized in 8-18 ms. Walking a Normal floor: every playing 3D sound matched its traced source; low-pass cutoff
  between 22 kHz (line of sight) and ~900 Hz (behind walls); room reverb decay 0.64-0.77 s and level changing per room.
  Not checked by ear (headless): listen in a real browser.

## 2026-11-19 (Windows rebuild)
### Build
- User request: recompile the Windows build and update the zip. Clean compile (90/90 scripts), release build via
  `BuildTools.BuildWindows()` succeeded in 18 s, 429 MB. Includes the gradual maid count, anti-camping dispersal and
  crouch-sprint changes below (only scripts and ProjectSettings changed since the morning build; the asset files were
  reused by the incremental build).
- Smoke run: main menu, windowed, 20 s, no errors in the player log.
- Repackaged `Builds/MaidCafeHorror_Windows_2026-11-19.zip` in place (235 MB, 196 files, Burst debug folder excluded,
  files at the zip root).

## 2026-11-19 (gradual maid count, anti-camping, sprint from a crouch)
### Enemies
- User request: too many maids on the first floors; the count should grow gradually (floor 1: one maid, floor 2: a
  bell maid and a maid, floor 3: two patrol maids, floor 4: a bell maid and two patrol maids, and so on).
  `EnemyDirector.RosterForStep(step)`: odd steps add a hunter, even steps add a bell maid next to the previous step's
  hunters; the third hunter is the Head Maid (`HeadMaidFromHunters`), later hunters are patrol maids; at most one bell
  maid. `ThreatStep(difficulty, depth)`: Normal floor n = step n, Hardcore one step ahead, Easy two steps every three
  floors. `WatcherCount` (bell-maid posts reserved by `FloorBuilder`) and `GiftCount` follow the same table.
  - Normal, floors 1-10: 1P · 1P+B · 2P · 2P+B · 2P+H · 2P+H+B · 3P+H · 3P+H+B · 4P+H · 4P+H+B (totals 1 to 6; before:
    1, 3, 4, 4, 5, 6, 7, 7, 7, 7 with the Head Maid from floor 2 and two bell maids from floor 6).
  - Easy: 1P · 1P · 1P+B · 2P · 2P · 2P+B · 2P+H · 2P+H · 2P+H+B · 3P+H (Head Maid from floor 7; before floor 3).
  - Hardcore: 1P+B · 2P · 2P+B · 2P+H · 2P+H+B · 3P+H · 3P+H+B · 4P+H · 4P+H+B · 5P+H (before: 2P+H+B already on floor 1).
  - Floors without a bell maid get no boxes of sweets (unchanged rule: gifts follow the bell maids).
- User request: after the player has been hiding for a while the maids gathered around the area and waited for them to
  come out; they should spread out to other areas and keep apart from each other.
  - `EnemyDirector.UpdateDispersal`: after `DisperseAfterHiding` (12 s) hidden, once no maid is on the player's trail
    (`MaidAI.OnPlayersTrail`: chasing, catching, or breath detection not yet faded), every maid gets `MaidAI.Disperse`
    once: searching/investigating maids switch to patrol (awareness capped at 0.2); a patrolling maid already heading out
    of the area keeps her route.
  - Quiet zone (`TryGetQuietZone`): from the dispersal until `QuietZoneLinger` (15 s) after the player leaves the hiding
    spot, patrol destinations avoid `QuietZoneRadius` (8 m) around it (inside it, the farther out the better) and the
    Head Maid does not drift towards the player. Noises still draw maids in as before.
  - `MaidAI.PickPatrolPoint` rewritten as the best of 16 random patrol points under ordered rules (quiet zone > Head
    Maid's 5-14 m drift ring > more than 6 m away > spacing): spacing = distance to the nearest other maid or her
    destination, up to `PatrolSpacing` (12 m), plus 0-4 m of jitter. Applies always, not only after hiding.
  - `PickSearchPoint`: a hiding spot or point another maid is at or heading to (< 3 m) is ranked lower, so two maids
    searching the same area check different spots.
  - Unchanged: a maid that saw the player enter still drags them out; a searching maid next to the spot still hears
    breathing (the dispersal waits until that detection has faded).
### Player
- User request: sprinting while crouched now stands the player up and runs at once. `PlayerMotor` reads the sprint
  request before the crouch update; with room above (`CanStandUp`) the crouch is released in the same frame, so the
  sprint speed applies immediately. Toggle mode: the toggle is cleared. Hold mode: the held crouch is ignored until the
  key is released and pressed again. Under a low ceiling the player stays crouched and cannot sprint, as before.
### Tests and verification
- `Tests/EditMode/EnemyRosterTests` (5 tests: requested Normal curve, at most +1 maid per floor and never fewer, one
  hunter and no Head Maid on floor 1, harder difficulty never has fewer maids, Head Maid is the third hunter). EditMode
  suite 40/40.
- Play mode (Normal, seed with 7 floors): floors 1-5 spawned 1P, 1P+B, 2P, 2P+B, 2P+H. Dispersal on floor 3: two maids
  searching 4-7 m from the player's locker switched to patrol when the hide timer reached 12 s; 6 destinations picked
  while hidden (nearest 8.2 m from the locker), 3 during the linger (nearest 16.1 m), normal patrols after 15 s; new
  destinations 9-31 m from the other maid. In a first attempt, maids told to search at the locker itself heard the
  player breathing and caught them (existing rule, working as designed). Crouch-sprint with a virtual gamepad: toggle
  and hold modes stand and sprint on the first frame (5.3 m/s after 10 frames).

## 2026-11-19 (Windows build for itch.io)
### Build
- User request: a new Windows build to upload to itch.io for testing. Release build via `BuildTools.BuildWindows()`:
  succeeded in 72 s, 430 MB output (310 MB of textures in `resources.assets.resS`), `vaudionative.dll` included.
- Smoke run: main menu, windowed, 20 s, no errors in the player log (URP reports its depth-of-field and Panini passes
  as stripped; the game uses neither effect).
- Packaged `Builds/MaidCafeHorror_Windows_2026-11-19.zip` (235 MB, 196 files, Burst debug folder excluded).

## 2026-11-19 (violin tension stingers)
### Audio
- User request: an occasional short, sharp violin sound for tension; when a maid starts chasing, it keeps getting more
  intense and plays more often, at a rate proportional to the inverse of the distance between her and the player; creepy
  and in keeping with the game; harmonic saturation / distortion so the audio seems to keep getting louder during a chase.
- Sources: 28 CC0 spiccato notes of the VSCO 2 Community Edition (sgossner / Versilian Studios; violin section and solo
  violin), original WAVs (44.1 kHz, 16-bit stereo, 5.7 MB) downloaded through the user's Freesound account into the
  git-ignored `ArtSource/Downloads/Freesound/VSCO2_Violin/`. A first build used Freesound's 192 kbps HQ previews (no
  login); the originals replaced them and the clips were rebuilt (pitches identical within 0.6 cents; same chords,
  levels and attacks). Pitches measured (YIN): each sounds one octave above its VSCO file name (the Freesound titles
  use MIDI note names, e.g. B5 = MIDI 83).
- `build_audio.py` step `violin` (run alone with `python ArtSource/Work/build_audio.py violin`; the script now takes step
  names): every note re-pitched from the nearest sample (≤ 3 semitones, band-limited resampling) to original dissonant
  chords (minor seconds, tritones, minor ninths), detuned ±12 cents, attacks tightened (the section samples swell in over
  70-180 ms; each note now starts 20 ms before its peak, so the chord hits at once: 10-90% attack 4-34 ms, was up to
  160 ms), stereo spread, a 3 kHz edge, light baked saturation on the chase chords, a synthetic dark reverb tail. Output:
  `Resources/Audio/ViolinStab/` (4 calm stabs, ~2 s with the tail), `ViolinChase/` (8 chase stabs ~1 s, sorted from low and
  warm to high and shrill), `ViolinCatch/` (2 full-register hits ~2.5 s). 14 stereo WAVs, 3.7 MB. `write()` supports stereo.
- New `Sfx` values `ViolinStab`, `ViolinChase`, `ViolinCatch` (no synthesized fallback: silent if the folders are missing);
  `SoundBank.Count`.
- `Audio/TensionStingers` (added by `GameManager`): calm stab every 35-80 s at random (not in the first 20-45 s of a floor
  or within 25 s of a chase); chase: a stab when it starts, then stabs at `StabRate` = 7 / distance per second (clamped
  0.25-4/s), distance to the nearest chasing maid re-read every frame (phase accumulator, so a maid closing in speeds them up
  at once). Heat (`TargetHeat`: 0.15 + 0.55 × closeness (18 m → 2 m) + 0.45 × chase time (25 s), held at its maximum during
  the chase, falls 0.25/s after) picks the chord (register climbs), pitch (+2 semitones at full heat), volume (0.5 → 0.85)
  and the saturation. Catch: a full-register hit at full heat.
- `Audio/StingerMixer`: a clipless 2D AudioSource whose `OnAudioFilterRead` mixes up to 8 decoded clips on the audio
  thread (lock-free request ring), saturates the sum (`Audio/HarmonicSaturation`: asymmetric tanh, drive 1 → 6 with heat²,
  level of a full-scale input held, curve faded in over the first half of the heat), then a heat-following low-pass
  (18 → 7 kHz), DC blocker and soft limiter (output ≤ 0.8). Bypasses the traced reverb zones; not heard by the maids.
- Verified: compiles (runtime source count 84 = files), EditMode tests 35/35 (5 new in `TensionStingerTests`: rate ∝ 1 /
  distance and its clamps, heat, saturation bypass, bound ≤ full scale with the reference level held, RMS rising with
  heat). Play mode (Game scene, Normal): 14 clips decoded (stereo, 44.1 kHz) and mixed at 48 kHz; forced calm stab (output
  peak 0.26); a natural chase and catch; a forced chase: at a constant 20.4 m stabs every 2.91-2.92 s (rate 0.343 = 7 /
  20.4) with the heat climbing from 0.16 over time, then as she closed in from 17.5 m: rate × distance = 7.0 at every
  sample, intervals 1.82 → 1.02 → 0.70 → 0.14 s, catch hit; output peak ≤ 0.70 with 3 voices overlapping; no console
  errors or warnings. Offline simulation of the same bus: about +9 dB RMS from chase start to contact at the same peak
  (-3 dBFS), stabs still 16 dB above the gaps at 4/s. Not verified by ear (no audio output in the agent session).
- Credits: `credits.md`, README, in-game credits (EN/PT). GDD: §7 (chase violin) and §12 (music, stingers).

## 2026-11-19 (recorded sounds and maid voices)
### Audio
- User request: refined sounds for breathing (hidden, holding, running, fatigue), walking, running, crouching, the
  elevator and locker doors, the bell maid's bell and the customer's service bell, all from CC0 sites; and voice lines
  for the maids when they start hearing the hidden player, "in a cute maid voice", AI-generated, a unique voice per
  maid, all adults (20-24 at least, user request).
- 19 CC0 recordings from Freesound (originals, downloaded through the user's account; full list in `credits.md`) and
  AI voice takes from ElevenLabs (user's Free-plan account) are kept raw in the git-ignored `ArtSource/Downloads/`
  (`Freesound/`, `ElevenLabs/`). New `ArtSource/Work/build_audio.py` (numpy + scipy + ffmpeg) rebuilds every clip in
  `Assets/_Project/Resources/Audio/<Sfx>/` (102 mono 16-bit WAVs, 16 MB): single footsteps found by onset detection,
  other cuts listed in the script, fades, normalization, crossfaded loops. Voice takes are cut into one clip per line
  using word timings from speech-to-text (faster-whisper, run once; saved in `ArtSource/Work/voice_words.json`).
- Voices: three designed with ElevenLabs Voice Design (Patrol "pigtails" ~22 bright and sing-song, Head Maid late
  twenties composed and cold, Bell maid ~21 shy and breathy; picked by measured pitch so they stay adult and
  distinct: median F0 333 / 262 / 286 Hz), plus the ElevenLabs default voices Jessica (blonde patrol maid) and
  Laura (long-dress patrol maid): five maid models, five voices. English lines from the voice-design previews,
  Portuguese (PT-BR) lines generated with Eleven v4. About 2,460 of the account's 10,000 monthly credits used.
  All 50 clips checked by speech-to-text: each holds exactly its line.
  - Patrol maids and the Head Maid (5 lines each, e.g. "Did I hear something?", "I swear I heard something!",
    "Where's this noise coming from?"): said when a searching maid starts hearing the hidden player breathing
    (`MaidAI.HearsHiddenBreathing`), i.e. the cue to hold the breath before the 1.4 s detection completes.
  - Bell maid: a notice line when she first notices the player ("Um... is someone there?"), a call when she starts
    ringing ("Everyone! A guest is here!").
  - Lines follow the game language (`SoundBank.GetLocalized`, clips `en_<n>` / `pt_<n>`); 7 s cooldown per maid and a
    2.5 s gap between any two maids; the patrol shriek cuts a line off.
- Player (`PlayerAudio`, rewritten): footsteps per gait (crouch / walk / sprint) and floor surface, wood or tile
  (by the floor's material: dining hall, kitchen, restroom, storage and the elevator floor sound hard); cloth rustle
  when crouching or standing; a scared breathing loop while hidden, a breath in when holding it, a breath out when
  letting go, the gasp when it runs out; fast breathing that fades in and grows louder as stamina drains while
  sprinting (from 30% fatigue), and panting while exhausted (`PlayerMotor.IsExhausted`, new) until stamina returns.
- Doors: lockers squeak open and slam shut (two sounds per use, `HidingSpot.PlayDoorSound`), tables rustle the
  tablecloth; elevator doors have separate open / close sounds (`ElevatorCar`), new chime.
- Replaced: hand bell loop, service bell, gasp, cloth rustle, elevator chime. Still synthesized: maid heels, patrol
  shriek, pickups, levers, music boxes, gramophone, needle scratch (and every recorded sound if its folder is missing:
  `ProceduralSfx.FallbackOf`; breathing and voices have no placeholder and stay silent).
- `SoundBank`: random variants never repeat the previous one (3+ variants), clips sorted by name, cache reset on
  play-mode start. 21 new `Sfx` values (appended at the end of the enum).
- Credits: `credits.md`, README and the in-game credits (EN/PT) list the 16 Freesound authors and ElevenLabs.
- GDD (Portuguese): §6 breathing and fatigue sounds, §7 maid voices (the breathing cue), §12 corrected to five
  maid models, recorded sounds, voices and what is still synthesized.
- Verified: compiles (runtime source count 81 = files), EditMode tests 30/30, every `Sfx` resolves to its recorded
  clips; play mode on Normal: arrival elevator door-open sound, locker open + close, hidden breathing loop at 0.32,
  a searching long-dress maid heard the breathing 0.3 s in and said "Hello? Is someone hiding in here?" before the
  catch, exhausted panting fades in to 0.62, bell maid notice line, call line + new bell loop while ringing, bell stops
  with the ringing. Not verified by ear (no audio output in the agent session): volumes and mix need the user's listen.

## 2026-11-18 (bug fixes and features round: lockers, detection, tables, night vision, generation, bell, noise meter)
### Art (maids)
- Maid hair and clothes looked washed out (user report). Cause, measured in play mode: their materials reflected
  Unity's default daylight sky cubemap (the only reflection source in the game), which laid a grey-blue sheen over
  every outfit (the Typical maid's red hair read as lavender, black dresses as grey-navy); the per-look tints
  (0.55–0.92 grey) then darkened them further, and the global grade (saturation −12) dulled what was left.
  `MaidVisuals.Fabric` now builds hair/cloth materials from the new template `MaterialTemplates/LitCutoutMatte`
  (cutout + `_ENVIRONMENTREFLECTIONS_OFF`, so the variant ships in builds), with saturated copies of the model's
  textures (`FabricSaturation` 1.4, `VividTexture`, cached per texture) and a white tint, except the Head Maid's pink
  uniform (now 1/0.6/0.72, brighter). Porcelain skin, face and eyes unchanged. Cost: about 1 s per session in the
  Editor (24 textures ≤ 1024²), paid once per model when it first spawns, during floor loading (behind the fade).
  Verified with all five models side by side in a lit room. Side effect: in unlit rooms the dresses now fade into the
  dark (the sky sheen used to make whole silhouettes glow faintly); porcelain skin and eyes stay visible.
### Player
- Fix: entering a locker while crouched kept the player crouched inside (camera below the door slit) and they came
  out crouched. Cause: with crouch-toggle on, the toggled state survived while input was locked in the locker.
  Entering a locker now stands the player up at once (`PlayerMotor.StandUpNow`: standing capsule immediately, camera
  rises during the step-in) and `PlayerMotor.KeepStanding` blocks crouching until they have left; a crouch key still
  held on the way out is ignored until released. Verified in play mode (capsule 1.00 → 1.75 m, eye 0.88 → 1.63 m).
### AI
- Fix (user report): a maid could find the player in a locker from behind a wall, then walk round to the front.
  Cause: the breathing check of a searching maid (`MaidAI.UpdateSearch`) was a flat 2.6 m distance test with no wall
  or sound check; lockers stand against walls, so the circle reached into the next room (measured on one floor: 2 of 6
  lockers, 24–43% of the circle behind a wall, as close as 1.2 m). After 1.4 s she marked the spot "seen entering" and
  chased round to its front. Reproduced in play mode (caught from 1.6 m behind the wall).
  - Breathing (`MaidAI.HearsBreathing`): heard within 2.6 m only when nothing but the hiding spot itself is between her
    ear and the player or the spot's opening (`HidingSpot.IsExposedTo`); through a wall it is muffled by the sealed
    factor like every other noise (0.3 → 0.78 m, i.e. never in practice). Verified: behind the wall 1.3 m away, no
    detection in 5 s; in front at 2 m, caught as before.
  - "Saw the player hide" (`MaidAI.WatchesSpot`): seeing the player less than 0.6 s before they hid is no longer
    enough; she must also have the spot in her view cone and range with a clear line to it. Verified: a chasing maid
    behind the locker's wall switched to searching without marking the spot.
  - Sight and the line-of-sight hearing fallback ignored the elevator cars' walls and doors (layer 2 was excluded from
    `Layers.SightBlockers`); they now block both. Triggers on that layer stay ignored.
### Hiding
- Under a table the view turns a full 360° (pitch still limited); entering keeps the current view direction. A yaw
  range of 180 or more in `PlayerLook.SetLimits` now means "yaw free".
- The hiding table has an exit on each of its four sides (`HidingSpot.AddExit`). Leaving uses the side the view ray
  crosses (from the table's collider footprint, so the long and short sides split at the real corners), if there is
  room to stand there (`HidingSpot.HasRoomToStand`: floor at the same height, standing capsule free of walls and
  furniture, nothing solid on the way out); otherwise the clear side closest to the view direction; otherwise the
  front exit (the validated one, still used when a maid drags the player out). Verified in play mode on three tables
  (side per view angle, a side against a wall refused, exit on the back when facing it).
### Night vision
- Night vision no longer makes the player easier to spot: the +2 m view distance for the "phone glow" was removed from
  the shared sight test (`MaidAI.CanSeePlayer`, used by patrol, head and bell maids). It was the only coupling (the
  IR fill light is not a room light, so it never counted for "player lit"). Verified: same sight result and
  visibility at 3–7.5 m with night vision on and off. How-to-play text (EN/PT) and GDD §6 updated.
### Level generation
- Floors grow with progress: 30 cells (480 m²) on the first floor, +15% area per floor, capped at 70 cells
  (1,120 m², from the 8th floor) (`FloorParams.AreaForFloor`). Generated floors are within ±5% of the target
  (measured over 1,000 layouts: averages 30 / 35 / 40 / 46 / 52 / 61 / 70 / 71 / 71 / 71). Before: the grid grew
  from 6×5 to 9×7 relative to the run length, so floor 3 of a 3-floor run was as big as floor 10 of a 10-floor run.
- Irregular outlines: the footprint is the bounding grid with 2–3 notches cut from corners and sides and sometimes an
  enclosed courtyard; cells outside the building have room -1 (`FloorLayout.CellRoom`). ~89% of floors are irregular,
  ~11% plain rectangles; strict L, T and U outlines are rejected (`LayoutValidator.ClassifyOutline`), as are parts
  thinner than two cells. Rooms stay rectangles: the footprint is cut into large blocks, each split by the old BSP.
  Walls, slots, patrol points and the patrol-point check skip outside cells; elevators face the open outside (never a
  courtyard) and the two cars never share an outside cell.
- Loops and redundant routes: the spanning tree no longer prefers corridor edges (it made corridors the hub), and
  doors are added until no single room, corridor or door is the only way between two major areas (start room, exit
  room, every non-hallway room larger than 2×2 cells). Small 2×2 rooms may still be dead ends; every corridor joins at
  least two rooms. Minimum loops kept (1, or 2 from 45 cells).
- New validation step `LayoutValidator.Check` (reachability, single-corridor / single-room / single-door bottlenecks,
  dead-end corridors, outline), run by the generator (retries with derived seeds) and again by `FloorDirector` before
  any geometry is built (a rejected layout moves on to the next seed).
- Tests: EditMode 29/29 (5 new: area growth, outline classes incl. hand-made L/T/U/Z/plus/H/ring masks, mostly
  irregular and never L/T/U over 500 seeds, two independent routes over 500 seeds, the validator rejecting a corridor
  spine with side loops and a single-door bridge while accepting a ring). 1,000 layouts: 0 invalid, avg 0.5 ms, worst
  24 ms. Play-mode stress pass: 40 floors (depths 1–10), 0 validation failures or warnings, build avg 45 ms (was
  33 ms; deeper floors are bigger), worst 73 ms. Top-down screenshot of an irregular floor checked.
- GDD §4 rewritten (size, shape, alternative routes, validation).
### Bell maid
- Sweets delay her bell: once her awareness is full she keeps watching for `WatcherMaid.ExtraRingDelay(sweets)` more
  seconds before ringing: 1.5 s per box of sweets carried, capped at 4 s; the player must stay in view for it
  (breaking line of sight unwinds it with her awareness). With no sweets she rings the moment she is fully aware, as
  before. The summon interval while she is already ringing (6 s) is unchanged. Verified in play mode (3.5 m in front
  of her): 0 sweets → rang 0.97 s after first sight (= full awareness); 2 sweets → full awareness at 0.97 s, rang
  3.00 s later. New EditMode test `WatcherMaidTests` (30/30). GDD §7: bell maid entry and the hiding rules updated.
- Fix (found while testing, approved by the user): the bell maid often did not spawn (42 of 56 expected over 36
  floors). Her post was picked in `EnemyDirector.SpawnFor`, after `FurnitureDecorator` had used the room cells, and
  `SlotPool.TryTakeWall` refuses wall slots in cells whose centre holds furniture, so rooms "had" slots but none could
  be taken. The posts are now reserved in `FloorBuilder.Build` before the furniture (`FloorInstance.WatcherPosts`,
  `FloorBuilder.PlanWatcherPosts`, same room rules; `FloorDirector` passes `EnemyDirector.WatcherCount`), and
  `SpawnFor` places the watchers there (NavMesh point 0.65 m from the wall; the old after-furniture pick is kept only
  as a fallback). Verified on the same 36 floors: 56/56 watchers, all at reserved posts, none overlapping furniture,
  no warnings. EditMode 30/30.
### HUD noise meter (code complete and compiling; not yet checked in play mode)
- New `UI/NoiseMeter` at the bottom left of the HUD (built by `HUD.Awake`): a "NOISE" / "RUÍDO" label
  (`ui.hud.noise`), 32 animated bars drawn as a sound wave, and a threshold mark with a caret. Scale 0–16 m of noise
  radius (sprinting fills ~80%; the loudest objective tasks peg it).
- Level shown, in metres of noise radius, from the values the maids react to:
  - movement: the footstep radius of the current gait while moving on the ground (`PlayerMotor.MovementNoise`):
    crouch 1.5 < walk 5 < sprint 13, 0 when still;
  - every other noise the player makes through `NoiseSystem` (locker/table 3, pickups 1.5, objective stations 4–30,
    out-of-breath gasp 7), held 0.35 s and then falling at 20 m/s;
  - breathing while hidden: 2.6 m (`MaidAI.BreathHearingRadius`), 0 while holding the breath.
  Noises the player does not make (screams, the bell, the gramophone, elevator chimes) are not shown.
- Threshold: the smallest noise radius any maid would hear right now, `MaidAI.NoiseNeededToHear` (distance divided by
  her hearing multiplier and the wall factor of the ray-traced player-body audibility; the line-of-sight check only
  without the SDK), infinite during her spawn grace or while she is catching the player. For breathing,
  `MaidAI.BreathNeededToHear` (searching maids only). The meter shows whichever source is closest to being heard.
- Colours: mint below 70% of the threshold, amber from 70%, red once a maid can hear; bars past the threshold are
  tinted red even when unlit, so the audible zone is always visible; with no maid in earshot the mark sits dimmed at
  the end of the scale. Fast-attack / slow-release smoothing on the level, smoothing on the threshold.
- One hearing rule: `MaidAI.HearingRange` is now used by both `MaidAI.Hear` and the meter;
  `PlayerMotor.StepNoiseRadius` is the radius footsteps emit; `MaidAI.HearsBreathing` uses `BreathNeededToHear`.
- Known limitation: a noise made away from the player (e.g. at an objective station) is compared with the threshold
  at the player's position; the maids trace such noises from where they happen.
- Docs: GDD §11 (HUD), README "How to play", CLAUDE.md §6 (UI).

## 2026-11-18 (maids and furniture, in progress)
### AI / navigation
- Fix (user report): maids walked on top of furniture (dining tables, the hiding table, nightstands) and found the
  player hiding under the table while standing on it. Cause: the NavMesh bake treated the top of every collider as
  walkable and the agent step height was 0.75 m (tables are 0.69–0.77 m, the hiding table's cloth ends at 0.74 m).
- The NavMesh bake now makes only the floor slabs walkable (`NavMeshModifier` on each floor, every other collider
  baked as Not Walkable via `NavMeshSurface.defaultArea`). Agent step height lowered to 0.25 m
  (`ProjectSettings/NavMeshAreas.asset`). Side effect: the roof slabs no longer carry a NavMesh either.
- Small furniture (chairs, bar stools, cardboard boxes, coffee table, side table, bin, bench; `FurnitureDecorator
  .ClimbablePieces`, at most 1.05 m tall) is left out of the bake; its footprint plus a 0.45 m margin is the new
  NavMesh area 3 "Furniture". Maids walk round it, except while chasing (per-agent area cost 60 vs 2), when they climb
  over it: the body is lifted onto the piece's top (agent base offset, probed with downward rays) and runs at 60%
  speed while lifted. Large furniture, hiding spots, stands and objective props are never climbed.
- Floor validation, patrol points, spawn points, checkpoints, search destinations and hearing estimates use the ground
  areas only (`NavAreas.Ground`), so the player can always walk every validated route.

## 2026-11-18 (objective prop models)
### Art
- Every objective prop and pickup is now a textured 3D model at real-world scale instead of a greybox box (22 models,
  all free licences, credited in `credits.md`, the README and the in-game credits):
  - Restore power: cartridge fuses (Poly Pizza, CC BY 3.0), a vintage fuse box with its door swung flat against the
    wall, and an early-1900s knife switch as the main lever, open until pulled, then closed (Sketchfab, CC BY 4.0).
  - Find the keycard: an old gold key, a worn metal office desk (narrowed to 1.6 m, greyed) with the manager's
    notebook, an antique iron safe.
  - Serve the customer: teapot and sugar bowl from a floral porcelain tea set, a strawberry chocolate cake; at the
    reserved table a plate and a cup on its saucer, a copper reception bell, and the "customer": an antique porcelain
    doll standing on a painted wooden chair.
  - Silence the music boxes: wooden music boxes with the lid open (mirror, glass over the comb), a horn gramophone on
    a painted nightstand.
  - Pickups everywhere: AA batteries (1.5x life size so they can be spotted) and an open heart-shaped box of
    chocolates (the watcher maid's gift). Batteries, gifts and objective items now stand on two antique nightstand
    models (picked per slot) instead of greybox cabinets.
- Sources: Sketchfab (pixelgrapher, Maxim Mavrichev, Weirdo.ar, 3Dee, Alex Filip, Mad_Lobster_Workshop, Rylae Shylna,
  eyes360vr, HusarL, alicean; CC BY 4.0), Poly Pizza (moriarty; CC BY 3.0), Poly Haven (CC0). Raw downloads in the
  git-ignored `ArtSource/Downloads/` (`SF_Props/`, `PP_Fuse/`, `PolyHavenModels/`).
- Pickup items keep a faint glow (emission map = their albedo) so they still read in the dark; environment
  reflections are off on the prop materials, as on the surfaces.
### Tools
- `ArtSource/Work/export_props.py` (Blender): imports the downloads, bakes transforms, scales to real size, puts the
  origin at the bottom centre with the front facing the room, applies the per-model edits (door swing, desk narrowing,
  tea-set split, decimation of the key/cake/tea set/notebook), exports `Resources/Props/<Prop>.fbx` and writes the
  texture sets (albedo, OpenGL normal, URP metallic/smoothness mask; 2K for the desk and doll, 1K or less otherwise)
  plus `Textures/Props/props.json`.
- `Scripts/Editor/PropAssetPipeline.cs`: import rules for the prop models and textures; builds one URP Lit material per
  manifest entry in `Materials/Props` and remaps the FBX materials to them (menu
  `MaidCafe → Art → Rebuild Prop Materials`, also runs when `props.json` is imported).
### Code
- `Generation/PropLibrary`: loads the prop models, places them by their bounds (footprint centred, bottom on the given
  point), `Solid` adds one fitted box collider (stations and the NavMesh bake use it). Every caller keeps its greybox
  box as the fallback when a model is missing.
- `FloorBuilder.BuildItemStand` (nightstand models, greybox cabinet fallback) and `FloorBuilder.PickupRoot` (pickup
  trigger + model visual) are shared by batteries, gifts and objective items. `ObjectiveProps.ItemOnCabinet` takes a
  model name; new helpers `SolidOrBox`, `Depth`, `AgainstWall`. The four objectives use the models; the breaker lever
  swaps the open knife switch for the closed one when pulled (the greybox lever still rotates).
### Verified
- Play mode on all four objective types (screenshots of every prop), knife switch swap, EditMode tests 24/24,
  40-floor stress pass (depths 0–9): 0 validation failures, no greybox fallbacks, build avg 33 ms / worst 53 ms
  (was 20 / 36 ms). Prop textures ≈ 45 MB of GPU memory at High quality (computed from sizes and formats; the
  editor's profiler reports ~91 MB, twice the GPU size for every texture).

## 2026-11-18 (surface textures)
### Art
- Walls, floors, ceilings, trim, elevators, lockers, cabinets, tables and objective props now use PBR texture sets
  (albedo, normal, metallic/smoothness) instead of flat greybox colours. Art direction: a cute, playful maid café that
  has been rotting for years (pastels kept but faded, stained, peeling and grimy).
- 22 sets in `Assets/_Project/Textures/Surfaces/`: 19 from Poly Haven (CC0, 2K downloads), some recoloured for the
  café palette (rose/cream checker floor, mint mosaic, black/cream diner checker, pink gingham with tea stains, cream
  repaint on chipped trim, leak stains on the ceiling), and 3 original wallpapers (rose with hearts, cream/mint with
  dots, lilac with stars) drawn and aged by script, peeling over Poly Haven's worn plaster.
  Made reproducibly by `ArtSource/Work/build_surfaces.py` (raw maps in the git-ignored `ArtSource/Downloads/PolyHaven/`).
- Texture limits: albedo and normal maps at most 2048 px, metallic/smoothness masks 1024 px. Ambient occlusion is partly
  baked into the albedo (the game's ambient light is almost black, so an occlusion map alone would show nothing).
- Room look: dining hall = rose heart wallpaper + rose checker; hallways = cream/mint wallpaper + worn planks; staff
  room = lilac wallpaper; kitchen = peeling green paint + diner checker; restroom = teal plaster + mint mosaic;
  storage = painted brick + worn concrete; office = dark wood panels + herringbone parquet; stage = red velvet + dark
  planks. Lockers: pastel blue bodies with pink doors, rusting. Elevators: rusty sheet metal, tread-plate floor.
### Generation
- `SurfaceMapping`: textured boxes get their own mesh with UVs in world metres (box mapping by face direction; V = world
  height, so the 3 m wallpapers keep their lower panel and border at the right height and textures line up across
  neighbouring wall pieces). Tangents rebuilt for normal maps. Flat-colour boxes keep the shared cube.
  `GeneratedMesh` destroys those meshes with the floor. `FloorBuilder.Box` takes an optional rotation (the battery/gift
  cabinets were scaled after creation, which would have stretched their UVs).
- `MaterialLibrary.Surface(name, surface, tint, fallback)`: tinted clone of `Resources/Surfaces/<surface>`, with the old
  flat colour as fallback. `FloorBuilder.Palette` and the breaker, safe, music boxes and reserved table use it.
### Editor
- `SurfaceAssetPipeline`: import rules for the surface textures (size caps, normal maps, linear masks, mipmaps that follow
  the global mipmap limit, trilinear + 8x anisotropic) and menu `MaidCafe → Art → Rebuild Surface Materials` (also runs
  when `surfaces.json` is imported): one URP Lit material per set, tiled by its real-world size, normal-map and
  metallic/smoothness keywords on.
- The surface materials have environment reflections off: the only reflection source is Unity's default sky cubemap,
  which made glossy spots on the new floors glow pale blue inside the dark building. Other materials (maids, props) are
  unchanged.
### Settings / UI
- New setting "Texture quality": High (2K) / Medium (1K) / Low (512), saved in PlayerPrefs (`GameSettings.TextureQuality`)
  and applied at once and at startup through `QualitySettings.globalTextureMipmapLimit` (the editor's own value is
  restored when play mode ends). New `UIFactory.Choice` control (button that cycles localized options). EN + PT strings.
  Measured in the editor (all texture memory): High 218 MB, Medium 71 MB, Low 35 MB.
- In-game credits (EN + PT): "Textures: Poly Haven (CC0)".
### Tests / build
- New EditMode tests `SurfaceMappingTests` (6): metre UVs on walls/floors, continuity across neighbouring boxes, rotated
  boxes, flat boxes untouched, every surface material within the size caps and with its keywords. EditMode 24/24.
- Play mode: every room type checked on a Normal floor, no console errors from game code. Windows release build
  succeeded (29 s, 376 MB, was 170 MB: the 22 texture sets take about 9 MB each in GPU format); the player ran with no
  errors in its log.

## 2026-11-18 (ray-traced audio, follow-up)
### Audio
- The SDK no longer logs a notice for every memory allocation (one line per new emitter, i.e. per one-shot sound):
  `AcousticWorld.Init` turns them off with `vaWorldSetLogMemoryAllocationWarnings` (new binding in `VAudioNative`).
  Checked in play mode: 8 one-shots plus maids and the player body → only the two AcousticWorld status lines.
- Listening test and hearing balance done by the user: the placeholder sounds and the maid hearing work as intended.
### Build
- New Windows release build (198 MB). Checked: the production audio DLL ships in `Plugins/x86_64` (debug-window files
  do not), the player log reports the SDK running and the floor voxelized, and with the DLL removed the game falls back
  to line-of-sight audio without errors. Maid models' runtime mesh reads also confirmed working in a player build.
- Removed the unused `com.unity.ai.inference` package (Unity Inference Engine; no game code or other package used it).
  The build no longer ships `DirectML.dll`, the Sentis shaders or the App UI native plugins: 198 MB → 170 MB (the
  rebuild took 20 s instead of 65 s, partly thanks to build caches). EditMode tests 18/18, release build runs without errors.
### UI
- In-game credits (EN + PT) now read "Audio: Powered by Vercidium Audio" (SDK licence, clause 6.6).
### Docs
- GDD: new "Áudio" subsection in section 12 (ray-traced sound, maid hearing, sound list, fallback without the SDK);
  section 7 (hearing) and the scenery bullet brought up to date.

## 2026-11-18 (ray-traced audio)
### Audio
- New ray-traced audio system built on the Vercidium Audio SDK 1.11.1 (native C library, CPU background threads).
  Every generated floor is copied into the audio world as a 0.25 m voxel grid (walls, floors, ceilings, pillars,
  elevator shells; built in ~6 ms during the loading screen), plus moving boxes for the elevator doors and locker
  panels, so closed doors and lockers muffle sound.
- Maids now hear by ray tracing: each maid has an "ear" that measures how much of a noise actually reaches her
  (straight, around corners, through doorways, or muffled through a wall). Quieter, muffled noises are heard from
  shorter distances and placed less precisely (up to 3 m off). During a chase, once she loses sight of the player,
  the player's own noises (sprinting footsteps) keep telling her where they went, so moving quietly is how you escape.
  Without the SDK the game falls back to the previous line-of-sight rule.
- Every sound the player hears in the world is ray traced too: volume and muffling follow the path from the sound to
  the player's ear, and the room reverb (decay, reflections) is measured by rays and changes from room to room.
- First sounds in the game (synthesized placeholders, original melodies): player footsteps and gasp, porcelain heel
  clacks and the patrol maid's shriek, the watcher maid's hand bell, locker doors and table cloth, pickups, the
  breaker lever, the service bell, music boxes that play until silenced (find them by ear), the gramophone and its
  needle scratch, the elevator chime and doors. A recorded clip in `Resources/Audio/<name>` replaces any of them.
- Dev key F9 opens the SDK's debug window (editor only), which shows the voxelized floor and the rays.
### Tests
- 9 new EditMode tests (voxelizer rules, hearing and filter mappings, every synthesized sound, and a native trace
  through a doorway vs. a closed wall). 18/18 pass.

## 2026-11-18 (maid model and animation refinement)
### Characters
- Legs no longer pass through the long dress (user request): the long-dress patrol maid's dress and apron now lift
  over her legs instead of following only her hips. At runtime the skirt is re-skinned onto a grid of "panel" bones
  (16 directions × 8 rings down the skirt, all pivoting at hip height); every frame each panel swings out and up just
  enough to clear the thighs, shins and feet (spheres measured from the leg mesh), the cloth below a raised knee hangs
  back down, and the panels settle back when the leg moves away (`AI/MaidSkirtFlare`). Measured by counting leg-mesh
  edges that cross the skirt over 40 poses of each animation: before, 7,500–11,000 in walk, run, catch and curtsy;
  now 0 in every animation. Standing, searching and bell-ringing poses do not move the skirt. Cost ≈ 0.1 ms per maid
  per frame. Models whose skirts already follow the legs (the VRoid maids, the blonde maid) are left unchanged.
- Long-dress patrol maid ("Typical maid"): imported the hand alignment done by hand in
  `ArtSource/Work/Maids_work_back_up_model.blend` (hands and finger bones moved onto the forearms). The model is now
  exported with triangulated faces so the wrist and forearm caps are not dropped by Unity. All seven animations were
  checked on the new model (humanoid avatar valid, same skeleton and skin weights).
- Fixed knees bending backwards ("ostrich legs") in the idle, search and bell-ringing poses on every maid. The
  animation file's humanoid reference pose was a mid-action frame (knees bent 41°), so all clips were converted with
  a 25° error at the knees and a backwards lean of the torso. The import pipeline now uses the rest pose of the model
  the clips were authored on (`MaidAssetPipeline.UseModelReferencePose`). Knees now bend slightly forward, as authored.
- Blonde patrol maid ("Maid Style Anime"): re-weighted the legs and arms so they bend at the real knee, ankle, elbow
  and wrist (the calves had a second bend half-way down and the elbows bent 10 cm too early). Model re-exported.
- New default posture (user request, reference images): standing, walking and searching maids hold their hands
  folded in front at the waist, right hand over the left, palms toward the body, fingers relaxed, torso upright
  (was: hands behind the back, which made them lean and looked unnatural). `AI/MaidRestPose`, shared IK in `AI/MaidArmIK`.
- Hands no longer pass through skirts and aprons: each model gets a "skirt cone" measured from its own mesh (widening
  downward from the waist); in every animation, hand and forearm points inside it are pushed out and the arm is
  re-solved (`AI/MaidSkirtGuard`). Checked on all five maids in idle, walk, run, search, ring, catch and curtsy.
- Long-dress patrol maid ("Typical maid"): the hands floated off the wrists (the statue's hands were separate pieces
  left 2.6 cm below / 4.5 cm beyond the forearms); moved onto the wrists with the wrist bones at the joints. Removed the
  permanent arm shadow that was painted into the apron texture.
- Long-dress patrol maid, follow-up (user report): her right hand was always a closed fist (the original statue's
  grip) and both hands still looked detached when bent (each was an unwelded, open shell). The right hand is now a
  mirror of her open left hand with matching finger bones; the hand shells were welded and closed, and the forearm
  ends capped. Both hands now sit in the cuffs and can open, close and fold.
- Bell maid: the bell handle is now inside her closed right fist (fingers curled around it) with the bell hanging
  mouth-down, instead of sticking out of the back of the hand. Ringing raises only the right arm, out to the side at
  head height, and shakes the bell from the wrist; the left hand stays folded at the waist.
### Player
- Fixed (user report): entering a hiding spot while walking or sprinting kept the camera head-bob playing for the
  whole time the player stayed hidden. The motor kept its last velocity while the CharacterController was disabled,
  so `HorizontalSpeed` never dropped; it now clears its velocity (and footstep stride) whenever the controller is off
  (`PlayerMotor`), and the camera settles back to rest as the player slides in. Verified in play mode: entering a
  locker at full sprint speed → speed 0, camera at its rest position, bob phase frozen; leaving works as before.
- Stamina now recovers while hiding (user request). It used to freeze for as long as the player stayed in a locker or
  under a table, so hiding while exhausted gave no breather. Same rate and delay as when standing still. Verified in
  play mode: hidden with empty stamina → 56% after 4.7 s (1.2 s delay, then 0.8 s/s), exhausted state cleared.
### UI
- FPS counter in the top-right corner of the screen (user request), above all other UI in every scene, averaged over
  half a second and shown in red below 30 FPS. New option in Settings: "Show FPS counter" (on by default; EN/PT).
### Tools
- Import pipeline: maid models are now imported with Read/Write enabled. The maid components (rest pose, skirt
  cone, body colliders, skirt flare) read each model's mesh at runtime, which a Windows player only allows for
  readable meshes (the editor allows it regardless, so this had not shown up yet).
- Import pipeline: animation reference pose taken from the MaidPigtails model (see above).
- Verified: EditMode tests 9/9; Game scene runs without errors; showcase checked from front and side.

## 2026-11-17 (art pass, part 3)
### Enemy AI
- Last known position: a chasing maid now only follows the player while she can see them. When she loses sight
  she runs to where she last saw them (she no longer "knows" where they went), looks around, checks a point a few
  metres along the direction they were moving, then up to 3 nearby hiding spots/rooms. Seeing the player again
  restarts the chase; otherwise she returns to patrolling. Verified in play mode (scripted, all three branches).
### Furniture
- 41 café models from the Kenney Furniture Kit (CC0) in `Assets/ThirdParty/KenneyFurnitureKit/` (import rule
  `Editor/FurnitureAssetPipeline`). New `Generation/FurnitureDecorator` furnishes every floor: each room gets a theme
  (dining room, kitchen, lounge, storage; hallways only a few small pieces) and uses the wall/centre slots left after
  hiding spots, batteries, objective props and sweets, so it never blocks them, doors or elevators. Dining tables
  with chairs facing them (some knocked over), kitchen counter runs and bar islands, armchairs around coffee tables,
  stacked boxes. Colours faded to fit the café; every piece is solid (blocks sight, gives cover, maids path around).
- Verified: realistic sizes (tables 0.69 m, counters 0.95 m), fronts face the room; 40-floor stress pass:
  0 validation failures, ~68 pieces per floor, build 26 ms on average (38 ms worst).
### Lighting
- More dead lights (user request: night vision felt useless): 40% chance on floor 1 rising to 65% on the deepest
  floor, +10% in hallways, -10% on Easy, +10% on Hardcore; the arrival and exit rooms stay lit. Measured on Normal:
  27% of rooms dark on floor 1, 41% on floor 5, 55% on floor 10 (was ~16%).

## 2026-11-17 (art pass, part 2)
### Characters
- Re-imported the three maid models after the Blender material fix: every material now has its texture
  (clothes, hair and eyes were white before). Verified in play mode.
- Porcelain skin reworked (user feedback: "too glossy and monotonic"). The skin is now built from each model's own
  painted skin texture: partly desaturated, lifted toward a bisque glaze tint, with hairline cracks on top, and a
  satin finish (smoothness 0.4–0.5 instead of 0.82–0.9). Per role: the Head Maid stays the most ceramic and cool,
  the bell maid is the warmest and most human. Generated once per model while the floor loads (behind the fade).
- Fixed a bug in the procedural crack texture: bright mottled areas overflowed and wrapped to near-black, scattering
  dark blotches over ~5% of the skin. Cracks are now denser and slightly wider so they still read at a distance.
- Rest posture (user feedback: hands clipping through bodies and skirts, backs leaning too far back). New
  `AI/MaidRestPose`, applied on top of the animation while a maid stands or walks:
  - hands folded on the lower back (top of the hips), palms out, right hand in the left, placed with a two-bone IK;
    the position comes from each model's own mesh (back-depth profile incl. hair, bows and skirt), so all three
    bodies are clear of clipping;
  - torso straightened from 14–17° backward lean to upright, head mostly kept level, shoulders drawn back slightly;
  - blends out for running, searching, ringing, catching and curtsying (the bell maid keeps her bell behind her back
    until she rings it).
- Solid maids (user request): the player can no longer walk through them. `AI/MaidBodyColliders` fits 13 capsules
  per model (head, torso, hips + skirt, arms, legs, feet) to the model's own mesh and attaches them to the bones, so
  the body shape follows every animation. They use a new physics layer, `MaidBody` (8), which the maids' sight,
  hearing and lunge checks and the NavMesh bake ignore. The bell maid's single capsule was replaced by the fitted body
  (the interact prompt still works through it). Verified: walking into any maid stops 0.58 m from her centre;
  sight and reach work even with her own body on the line.
### Two more maid models (patrol variants)
- "Maid Style Anime" and "Typical maid" (both CC BY 4.0, previously unusable: no skeleton) were rigged in Blender
  with the same humanoid skeleton as the other maids, so every animation, the rest pose, the porcelain treatment and
  the body colliders work on them unchanged.
  - Maid Style Anime: rescaled (it was imported at 1/100), 78 pieces merged into body/hair/cloth, heavy ruffles
    decimated (114k → 61k vertices), colours chosen for the untextured parts (the download had none: navy dress,
    white apron, red bows, black tights, blonde hair), skin weights by bone heat, clothes/hair transferred from it.
  - Typical maid: a statue (hands clasped, torso turned 30°, toon outline shells). Outline shells removed, skeleton
    fitted from the sleeves and hands, weights by distance with region rules (the apron never follows the hands,
    the long dress mostly follows the hips), then re-posed to a T-pose (torso untwisted, head straightened) and that
    pose applied as the rest pose; hands decimated (55k → 35k vertices total).
- Patrol maids now use one of three models (pigtails, blonde, long dress); maids on the same floor get different
  models. The Head Maid and the bell maid keep theirs. The showcase scene shows all five.
- The import pipeline now forces the correct Hips bone (Unity's auto-mapper picked the "Root" helper on one model).
- Untextured imported materials keep their own colour under the porcelain tint.
### Tools
- New development scene `Scenes/Showcase.unity` (`Scripts/Dev/MaidShowcase`, `ShowcaseViewer`): the three maids side
  by side without AI, all playing the same animation in place; E / Q (or the arrow keys) step forwards / backwards
  through Idle, Walk, Run, Search, Ring, Catch and Curtsy (one-shot clips replay). Walk mode collides like the
  player; fly mode (F) has no collision and can go above and below the models (one-sided floor). H shows the body
  colliders. HUD text is localised (EN/PT). Not part of the build.
### Testing
- Bell maid play-tested (scripted, Normal floor 2): notices → rings → the nearest maid is summoned to the player;
  giving sweets pacifies her (curtsy, no more ringing). Notice speed still to be judged in a human play-test.
- EditMode tests 9/9 pass. Stress pass of 40 generated floors with the new sweets pickups: 0 validation failures,
  all sweets placed, floor build 20 ms on average (36 ms worst).

## 2026-11-17 (art pass, part 1)
### Characters
- Licences checked for the 8 maid models suggested by the user. Downloaded the 5 usable ones (credit required, non-commercial allowed); rejected 2 not downloadable and 1 copyrighted character (Re:Zero Ram).
- Three rigged models processed in Blender (re-oriented, hair decimated, re-exported) and imported as Humanoid:
  patrol maid ("maid pigtails"), Head Maid ("Anime Maid - VR Chat", taller), bell maid ("cute anime maid").
- Porcelain look applied at runtime: cracked glazed skin (procedural crack texture), black hollow eyes with glowing irises that show the maid's awareness, tinted outfits per role.
- Seven animations made by hand in Blender and shared by all maids through Humanoid retargeting: Idle (hands clasped, doll head tilt), Walk (quick stiff steps), Run (arms reaching forward), Search (jerky look-around), Catch (lunge and grab), Ring and Curtsy.
- Import rules and the Animator Controller are generated by `MaidAssetPipeline` (menu MaidCafe → Art).
### New enemy: the bell maid (watcher)
- Never attacks or touches the player. She stands at a post and scans the room; if she sees you long enough she rings a hand bell, calling the nearest maid (and the Head Maid) to your position.
- Boxes of sweets (new pickup, shown on the HUD) can be given to her: she curtsies and stops calling for the rest of the floor.
- Appears from floor 2 (Normal), floor 3 (Easy) or floor 1 (Hardcore). New EN/PT strings and a How-to-play line.
- **Status:** code compiles; models verified in play mode; bell maid and gifts not yet play-tested (see jobs.md).


## 2026-11-17
### Planning
- Defined the concept: first-person procedural survival horror in a maid café, porcelain maids as enemies.
- Decisions: 3–10 floors (seeded random per run); one main objective per floor with 2–4 steps;
  objective pool = Restore power, Find the keycard, Serve the "customer", Silence the music boxes.
- Difficulties: Easy (checkpoint per objective step), Normal (restart floor), Hardcore (restart run, new seed).
- Player tool: phone with night vision and battery.
- Enemies: patrol maids + Head Maid stalker that answers alarms.
- Languages: English and Brazilian Portuguese with an in-game toggle.
- Target: Windows build, 1920×1080, itch.io jam (deadline 2026-11-25).

### Setup
- Added packages: MCP for Unity v10.3.0 (editor automation), AI Navigation 2.0.16 (runtime NavMesh),
  Unity Test Framework 1.4.6.
- Created project docs: `CLAUDE.md`, `jobs.md`, `changelog.md`, `credits.md`, `GDD.md`, `README.md`.
- Added `Builds/` to `.gitignore`.
- Created folder layout `Assets/_Project/...` and assembly definitions (Runtime, Editor, EditMode tests).

### Code (first pass, not yet compiled in Unity)
- Core: seeded RNG, noise event bus, game events, settings (PlayerPrefs), run configuration
  (difficulty, seed, 3–10 floors), input actions built in code (keyboard/mouse + gamepad).
- Localization: EN/PT string table loader and `LocalizedText` component.
- Player: first-person movement (walk/sprint with stamina/crouch), camera look with head-bob and
  sprint FOV, interaction raycast with hold-to-use, hiding spots, phone with night vision and battery.
- Procedural generation: BSP room layout with hallways, spanning-tree doors plus loops, start/exit
  elevators placed as far apart as possible, slot pool and hiding spot/battery planner.
- Tests: EditMode tests over 500 seeds (connectivity, doors, portals, hiding spots, determinism).

### Integration (MCP for Unity connected)
- Connected the editor through MCP for Unity. Fixed three scripts that Unity had not imported
  (recreated them through the editor).
- Floor builder: rooms, two-sided walls with doorways and frames, pillars, ceilings, sliding-door
  elevators (arrival and exit), lockers with a peek slit, tables with tablecloths for hiding,
  batteries on cabinets, room lights (some broken, some flickering), static batching.
- Runtime NavMesh bake with a validation pass (exit, hiding spots and batteries must be reachable);
  if validation fails, the floor is regenerated with the next seed.
- Door graph now guarantees at least one loop (two on deeper floors) for escapes during chases.
- Night vision tuned: green monochrome image (channel mixer), grain, vignette, soft fill light.
- `Game.unity` scene with a temporary test harness (`DevBootstrap`) and a debug overlay.
- Tests: 9 EditMode tests pass. Play-mode stress test: 40 floors, 0 failures, worst build 25 ms.

### Objectives
- Four floor objectives, each with 2–4 steps; the order is shuffled per run so consecutive floors never repeat:
  Restore power (fuses → breaker box → main lever), Manager's keycard (key → desk with the safe code →
  safe → swipe at the elevator), Special customer (tea, cake and sugar → reserved table with a porcelain
  doll → service bell), Silence the melody (music boxes → gramophone).
- Loud actions (lever, bell, breaking music boxes) make noise that attracts the maids.
- Objective items glow faintly so they can be found in the dark. Hints explain what is missing.
- Objective props are placed before the NavMesh bake and are validated as reachable.

### Enemies (porcelain maids)
- Maid AI: sight cone with line of sight (affected by light, crouching, sprinting and the phone glow),
  awareness meter, hearing (muffled through walls), patrol → investigate → chase → search.
  Patrol maids scream when they start a chase, calling the others.
- Head Maid: stronger maid that drifts toward the player's area and answers every alarm.
- Fair hiding: a maid only drags you out of a hiding spot if she saw you enter it, or if she is searching
  right next to it and you are not holding your breath (Space). Holding your breath is limited, and
  running out makes you gasp loudly.
- Enemy count and stats scale with floor depth and difficulty. Grace period after spawning and respawning.
- Being caught: the view snaps to the maid's face and the camera shakes. Then Easy returns you to the last
  objective step, Normal restarts the floor and Hardcore restarts the run with a new seed.

### Game flow, menus and HUD
- `GameManager` controls the whole flow: loading floor → playing ⇄ pause → caught → (checkpoint, floor
  reload or game over) → elevator transition → victory. It replaces the temporary test harness.
- Main menu with a live background (a generated room where a porcelain maid stands still under a
  flickering light). Options: Play (difficulty + optional seed), Settings, How to play, Credits, Quit.
- Settings: language EN/PT, mouse sensitivity, invert Y, field of view, volume, brightness, crouch
  mode, fullscreen. All saved.
- Pause menu: resume, settings, how to play, return to last safe point (anti-softlock; not allowed during
  a chase), main menu, quit.
- HUD: crosshair, interaction prompt with hold bar, objective and current step, hints, phone battery,
  stamina, breath bar while hiding, floor banner.
- Fades between floors showing the floor number and objective; "Caught" screen per difficulty;
  victory and game-over screens with stats (floor, time, times caught, difficulty, seed).
- Post-processing: base grade (adjustable brightness, vignette, grain, bloom) and a red pulse with
  chromatic aberration during chases.
- Safety net: if the player falls out of the world, they are returned to the last safe position.
- Scenes: `MainMenu` and `Game` in Build Settings. Template files removed. Player settings: 1920×1080,
  windowed fullscreen, product name "Maid Cafe Horror".
- Runtime materials now clone template materials (in `Resources`) so the shaders are included in the build.
- One-click Windows build menu (`MaidCafe → Build Windows`).
- First Windows build succeeded (`Builds/Windows/MaidCafeHorror.exe`).

### Fixes
- **Softlock after being caught (Easy), reported by the user:** the first checkpoint was the spawn point
  inside the arrival elevator, whose doors close for good once the player leaves, so respawning there
  trapped the player.
  - Checkpoints now start in the room in front of the elevator and are always snapped to walkable floor.
  - The arrival elevator now reopens whenever the player is inside it (covers respawns and "return to last safe point").
  - Verified in play mode for Easy (respawn outside the car) and Normal (floor reloads, doors open).
- Maids can now "lunge" at a player standing just beyond their reach (e.g. inside an open elevator car)
  when nothing solid is in between, so elevators cannot be used as safe camping spots.
