# ArtSource

Source files for art made or processed outside Unity. Not part of the build.

- `Downloads/` — raw third-party downloads, **git-ignored** (large). Re-download from the URLs in `credits.md`
  (Sketchfab and VRoid Hub need a logged-in account):
  - `SF_AnimeMaidVRChat/` — Sketchfab "Anime Maid Character Model - VR Chat" (VR Avatars, CC BY 4.0), original FBX.
  - `SF_TypicalMaid/` — Sketchfab "Typical maid | Original work" (Partaevil, CC BY 4.0), original GLB.
  - `SF_MaidStyleAnime/` — Sketchfab "Maid Style Anime" (neutralize, CC BY 4.0), original FBX.
  - `VR_MaidPigtails/` — VRoid Hub "maid pigtails" (skates99), VRM 0.0.
  - `VR_CuteAnimeMaid/` — VRoid Hub "cute anime maid" (Higg2y), VRM 0.0. Conditions forbid violent use: this model is only
    used for the non-violent bell maid.
  - `KenneyFurnitureKit/` — Kenney Furniture Kit 2.0 zip (CC0).
  - `PolyHaven/<asset>/` — Poly Haven texture sets (CC0), 2K JPG: `<asset>_diff_2k.jpg`, `<asset>_nor_gl_2k.jpg`,
    `<asset>_arm_2k.jpg` (download URLs from `https://api.polyhaven.com/files/<asset>`; the asset list is in
    `credits.md`).
  - `SF_Props/` — Sketchfab prop models (CC BY 4.0, GLB as downloaded; `credits_info.json` lists title, author and URL
    of each), `PP_Fuse/` — Poly Pizza "Fuse" (moriarty, CC BY 3.0), `PolyHavenModels/<asset>/` — Poly Haven models
    (CC0, glTF 2K, with `info.json`: authors and URL). The full list is in `credits.md`.
- `Work/export_props.py` + `Work/Props_work.blend` — build the objective prop models: open `Props_work.blend` in
  Blender 5.x and run the script (`blender -b ArtSource/Work/Props_work.blend --python ArtSource/Work/export_props.py`,
  or exec it and call `main()`; `main(only=["Desk"])` re-exports one prop). The `PROPS` list at the top describes each
  prop (source, meshes kept, real size, decimation, edits such as the fuse-box door swing and the desk narrowing,
  texture cap, glow). Output: `Assets/_Project/Resources/Props/<Prop>.fbx`, `Assets/_Project/Textures/Props/<Set>/`
  (`_Albedo`, `_Normal` (OpenGL), `_Mask` = R metallic / A smoothness) and `props.json`; Unity then builds the materials
  (`MaidCafe → Art → Rebuild Prop Materials`, automatic when `props.json` changes).
- `Work/build_audio.py` — builds every recorded sound and voice line in `Assets/_Project/Resources/Audio/<Sfx>/`
  (mono 16-bit WAV) from `Downloads/Freesound/` (CC0 originals, file names start with the Freesound id) and
  `Downloads/ElevenLabs/` (AI voice takes, one take per maid and language), using `Work/voice_words.json` (word timings
  of the chosen voice takes) to cut one clip per line. Needs numpy, scipy and ffmpeg. Run from the project root:
  `python ArtSource/Work/build_audio.py` (rebuilds all folders it owns), or only some steps, e.g.
  `python ArtSource/Work/build_audio.py violin`. The violin stingers (stereo) are layered from the VSCO 2 CE
  spiccato samples in `Downloads/Freesound/VSCO2_Violin/`. Sources and licences: `credits.md`.
- `Work/build_surfaces.py` — builds the 22 surface sets in `Assets/_Project/Textures/Surfaces/` (albedo 2K, normal 2K,
  metallic/smoothness mask 1K, plus `surfaces.json` with each set's real-world tile size) from the Poly Haven maps:
  recolours, stains, the three aged wallpapers (original patterns over `worn_plaster_wall`). Run from the project root:
  `python ArtSource/Work/build_surfaces.py ArtSource/Downloads/PolyHaven Assets/_Project/Textures/Surfaces [Name,...]`
  (needs numpy + Pillow; pass names to rebuild only some sets). Unity then reimports the textures and rebuilds the
  materials (`MaidCafe → Art → Rebuild Surface Materials`).
- `Work/Maids_work.blend` — Blender 5.3 working file: one scene per model (re-oriented to face -Y, hair decimated,
  materials rebuilt) and the seven maid actions on the `MaidPigtails_Armature`.
  - Export models: FBX, scale `FBX_SCALE_ALL`, forward `-Z`, up `Y`, no leaf bones, textures saved as PNG in
    `Assets/_Project/Resources/Maids/<Model>/Textures/`.
  - Export animations: select `MaidPigtails_Armature` only, FBX with "All Actions" baked →
    `Assets/_Project/Animations/MaidAnimations.fbx`. Unity rebuilds the Animator Controller on import.
  - Scenes `MaidStyleAnime` and `TypicalMaid` hold the two models rigged for this project. Their armatures copy the
    `J_Bip_*` hierarchy of `MaidPigtails_Armature` (same names, so Unity maps them the same way), fitted to each body.
    TypicalMaid was weighted in its statue pose and then re-posed to a T-pose that was applied as rest; its finger
    bones carry a 0.1% weight on one hand vertex only so Unity counts them as skeleton bones (the hands are rigid).
    Export them like the other models (armature + its three meshes selected).
  - The animation FBX's avatar does NOT use the pose stored in that file (it is a frame of an action): the import
    pipeline copies the MaidPigtails rest pose into it (`MaidAssetPipeline.UseModelReferencePose`). Keep the clips
    authored on `MaidPigtails_Armature`.
  - 2026-11-18 fixes: MaidStyleAnime limb weights redistributed along the real knee/ankle/elbow/wrist (the original
    automatic weights followed the vertical bone stubs). TypicalMaid: the hands are separate rigid pieces; both were moved
    onto their forearm stubs and the hand bones placed at the wrists. The right hand (a closed fist from the statue) was
    replaced by a mirror of the left hand (R finger bones mirrored too), the hand shells welded and capped. `fix_apron_shadow.py` painted the statue's baked
    arm shadow out of `TypicalMaid_ClothB.png` (original in `Work/Originals/`; run:
    `python ArtSource/Work/fix_apron_shadow.py <original> <output> <preview>`).
  - `Work/Maids_work_back_up_model.blend` = `Maids_work.blend` plus the final, hand-made TypicalMaid hand alignment
    (hands and finger bones moved onto the forearms). The current `TypicalMaid.fbx` is exported from this file, with
    "Triangulate Faces" on (the hand/forearm n-gon caps are otherwise discarded by Unity as self-intersecting).
  - The clips were generated with a small joint-position solver (two-bone IK on joint positions; the glTF import makes all
    bones vertical, so Blender IK constraints do not work on this rig). Design speeds: Walk 1.1 m/s, Run 3.94 m/s
    (used by `MaidAnimator` to scale playback).
