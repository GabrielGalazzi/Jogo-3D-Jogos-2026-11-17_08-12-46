# Always at Your Service

*Sempre ao Seu Serviço* in Portuguese. Project/code name: `MaidCafeHorror`.

First-person procedural survival horror set in a maid café. Escape through 3–10 randomly generated
floors while porcelain maids hunt you. Built with Unity 6 (URP) for Windows.

- **Game Design Document (PT-BR):** [GDD.md](GDD.md)
- **Changelog:** [changelog.md](changelog.md)
- **itch.io:** _link pending_

## How to play
| Action | Key |
|---|---|
| Move | WASD |
| Look | Mouse |
| Sprint | Shift |
| Crouch | Ctrl / C |
| Interact | E |
| Phone night vision | Right mouse / F |
| Hold breath (while hiding) | Space |
| Pause | Esc |

Gamepad is supported too (left/right stick, A interact, B crouch, LT phone, RB hold breath, Start pause).

Complete each floor's objective to unlock the elevator. Avoid being seen, break line of sight before
hiding, hold your breath when a maid searches next to you, and keep an eye on your phone battery.
The noise meter (bottom left) shows how much noise you are making and marks the level at which a maid would hear
you: stay below the mark (it turns amber when you get close and red when you are heard).

**Difficulties:** Easy (caught → back to the last objective step), Normal (caught → floor restarts),
Hardcore (caught → a brand-new café).

## Building
Open the project in Unity 6000.6.6f1 and use `MaidCafe → Build Windows (Release)` (output: `Builds/Windows/`).
Zip the `Builds/Windows` folder and upload it to itch.io.

Browser version: `MaidCafe → Build WebGL (itch.io)` (output: `Builds/WebGL/`, page template
`Assets/WebGLTemplates/MaidCafe`). Zip the *contents* of `Builds/WebGL` (`index.html` at the zip root), upload it to
itch.io as an HTML game ("This file will be played in the browser"), set the viewport to 1280×720, enable the
fullscreen button and tick **SharedArrayBuffer support**: the ray-traced audio runs on worker threads, which browsers
only allow on cross-origin isolated pages. Without it the browser version falls back to line-of-sight audio.

## Ray-traced audio SDK (Vercidium Audio)
Maid hearing and all world sounds are ray traced with the Vercidium Audio SDK. Its licence only allows it to be
distributed inside a compiled game, so it is **not in this repository**. To build with it:
1. Download the SDK (v1.11.1) from https://vercidium.com and unzip it.
2. Copy `3d/native/dev/windows/vaudionative.dll`, `vaudio-debug-window.exe` and `glfw3.dll` into
   `Assets/ThirdParty/VercidiumAudio/Plugins/Dev/`, and `3d/native/production/windows/vaudionative.dll` into
   `Assets/ThirdParty/VercidiumAudio/Plugins/Production/`.
3. Unity applies the import settings automatically (menu `MaidCafe → Audio → Reapply Vercidium Audio Import
   Settings` if needed): the dev library runs in the Editor, the production library ships in the Windows build.
4. For the browser build, copy `3d/wasm/production/vaudio-wrapper.js` and `3d/wasm/production/_framework/` (the
   `.br`/`.gz` copies are not needed) into `Assets/ThirdParty/VercidiumAudio/Web~/`. The folder name ends in `~`, so
   Unity does not import it; `Build WebGL` copies it to `Builds/WebGL/vaudio/`, where the page loads it.

Without the SDK the game still runs: maids use line-of-sight hearing and sounds use a simple occlusion check.

## Maid showcase (development scene)
`Assets/_Project/Scenes/Showcase.unity` (editor only, not in the build) shows the three maid models side by side,
without AI, all playing the same animation in place. Controls: WASD + mouse, Shift faster, **E / Right Arrow** next
animation, **Q / Left Arrow** previous animation, **F** walk / fly (flying has no collision and can go above and
below the models; the floor is see-through from below), Space jump / fly up, Ctrl or C fly down,
**H** show the maids' body colliders, Esc frees the mouse.

## Project layout
See [CLAUDE.md](CLAUDE.md) for the architecture and [jobs.md](jobs.md) for the task board.

## Credits
See [credits.md](credits.md) for the full list with licenses.

| Asset | Author | License |
|---|---|---|
| Unity Engine and packages | Unity Technologies | Unity Companion License |
| MCP for Unity (development tool only) | CoplayDev | MIT |
| TextMesh Pro Essential Resources | Unity Technologies | Unity Companion License |
| Liberation Sans font | Liberation Fonts project / Red Hat | SIL Open Font License 1.1 |
| "maid pigtails" VRoid model (modified) | skates99 (VRoid Hub) | VRoid Hub conditions, credit required, non-commercial |
| "Anime Maid Character Model - VR Chat" (modified) | VR Avatars (Sketchfab) | CC BY 4.0 |
| "cute anime maid" VRoid model (modified) | Higg2y (VRoid Hub) | VRoid Hub conditions, credit required, non-commercial, no violence |
| "Typical maid / Original work" (modified, rigged by us) | Partaevil (Sketchfab) | CC BY 4.0 |
| "Maid Style Anime" (modified, rigged by us) | neutralize (Sketchfab) | CC BY 4.0 |
| Furniture Kit | Kenney | CC0 1.0 |
| PBR textures (20 sets, modified: recoloured, repacked, stained) | Poly Haven | CC0 1.0 |
| "Antique Iron Safe" (modified) | pixelgrapher (Sketchfab) | CC BY 4.0 |
| "Vintage Gramophone" (modified) | Maxim Mavrichev / mvrc.art (Sketchfab) | CC BY 4.0 |
| "Music Box - Mid Poly" (modified) | Weirdo.ar (Sketchfab) | CC BY 4.0 |
| "Old Gold Key" (modified) | 3Dee / mellydeeis (Sketchfab) | CC BY 4.0 |
| "Freebie - Game Art: Vintage Fuse Box" (modified) | Alex Filip / filip.alecsandru (Sketchfab) | CC BY 4.0 |
| "Early 1900s Knife Switches Type D" (modified) | Mad_Lobster_Workshop (Sketchfab) | CC BY 4.0 |
| "Reception Bell. Wood, shiny copper" (modified) | Rylae Shylna / risteralline (Sketchfab) | CC BY 4.0 |
| "Chocolate+Box" (modified) | eyes360vr (Sketchfab) | CC BY 4.0 |
| "AA Battery" (modified) | HusarL (Sketchfab) | CC BY 4.0 |
| "A porcelain doll" (modified) | alicean (Sketchfab) | CC BY 4.0 |
| "Fuse" (modified) | moriarty (Poly Pizza) | CC BY 3.0 |
| Models: Tea Set 01, Strawberry Chocolate Cake, Metal Office Desk, Binder Notebook, Painted Wooden Chair 02, Classic Nightstand 01, Painted Wooden Nightstand (modified) | Poly Haven (James Ray Cock, Rico Cilliers, Jurita Burger, Kuutti Siitonen, Ulan Cabanilla, DaDrood, Kirill Sannikov) | CC0 1.0 |
| Wallpaper patterns (drawn and aged by script) | This project | Project's own |
| Ray-traced audio: **Powered by Vercidium Audio** (SDK v1.11.1, native + WebAssembly builds) | Vercidium Pty Ltd | Vercidium Audio EULA (non-commercial licence: coursework / game jam) |
| WebGL page template (Unity's "Default" template, modified) | Unity Technologies | Unity Editor terms |
| Sound effects (19 recordings, modified): Nox_Sound, abrmy, 14GPanskaMuzatko_Matej, ckvoiceover, njjjjjjjjjjjjjjjjjjjjjjjj, speedygonzo, dhallcomposer, SpliceSound, dynamique, usamah, martian, toddcircle, lolamadeus, NachtmahrTV, se2001, tonmayster | Freesound authors (listed) | CC0 1.0 |
| Violin samples for the tension stingers (VSCO 2 Community Edition, 28 spiccato notes, modified) | sgossner / Versilian Studios (Freesound) | CC0 1.0 |
| Maid voice lines (EN + PT-BR) | Generated with ElevenLabs (designed voices + default voices Jessica and Laura) | ElevenLabs Terms of Service |
| faster-whisper (development tool only: timing of the voice lines) | SYSTRAN | MIT |
| Other sound effects and melodies (synthesized in code) | This project | Project's own |
