# Rendered UI art

The game's icons, 9-slice skins and app icon, made in the style of a casual survival-strategy UI kit
(glossy, chunky, painted 3D icons with a thick dark outline; bevelled buttons with a highlight band and
a bottom lip; wooden planks; parchment panels). Nothing is copied from the kit; everything is modelled,
painted or rendered here from CC0 sources (Quaternius characters and Medieval Village MegaKit
textures) and the OFL Changa One font.

| File | What it makes |
|---|---|
| `iconlib.py` | Blender helpers: the house camera and light rig, the toon "painted" material, chunky modelling primitives |
| `blender_icons.py` | Every icon, modelled procedurally (villager portraits from the `Person_*` models), including one `law_<id>` picture per custom in the Book of Customs; raw 512 px renders + object-index passes |
| `post_icons.py` | Grade, inner ink lines between parts, the thick `#2B1A0E` outline, drop shadow, downsample to 128 px; contact sheets |
| `skins.py` | The 9-slice skins (numpy signed-distance painting at 4x): procedural real materials (periodic wood grain, aged parchment, wrought iron with rivets) for the popups: parchment in a mitred walnut frame with iron corner brackets, an oak title plank with an engraved border and iron straps, inset sections; enamelled blue-kit buttons and `hud_*` pieces. `panel`, `plank` and `section` are tiled (`TILED` in `build_ui_assets.py`, `-unity-slice-type: tiled`): their middles repeat seamlessly instead of stretching. Sizes and slice borders come from `tools/ui/build_ui_assets.py` `SKINS` (run it with `--generated-only` to rewrite `UiGenerated.cs`) |
| `blender_appicon.py` | The 1024 px app icon (opaque): the carved stone and gold "FOUNDERS / THE LONG RISE" emblem, written to Unity and the Xcode asset catalogue |
| `blender_appicon_scene.py` | The earlier scene icon (the founder with a torch at dusk), kept as an alternative |
| `preview_ui.py` | Review sheets: a mock popup built from the real skins and icons, and every skin native and stretched |
| `blender_buildings.py` | Path-traced build-menu pictures of every building (Cycles, game sky HDRI + warm sun, shadow catcher, AgX); one job per model from the Unity OBJ export or a kit FBX |
| `render_buildings.py` | Driver for the above: `--models <building-models.zip>` (CI artifact of `export-models.yml`) or `--fbx` (local fallback, medieval_* only); adds the outline and contact shadow, writes 256 px `UI/Buildings/<key>.png` |
| `build_art.py` | Runs the steps above |

## Re-running

    python3 tools/art/build_art.py                        # everything (~20 min on 4 CPU cores)
    python3 tools/art/build_art.py icons --only wood,stone   # just some icons
    python3 tools/art/build_art.py skins sheets            # skins (seconds) and review sheets
    python3 tools/art/build_art.py appicon                 # app icon (~5 min)

Needs Blender 4.2 (`blender` on PATH or `/usr/local/bin/blender`), Python 3 with pillow and numpy.
Raw renders and review sheets go to `tools/art/.cache/` (ignored by git).

To add an icon, write an `@icon("name")` builder in `blender_icons.py` (build around the origin; the
camera frames whatever is visible) and run `build_art.py icons --only name`.

If a skin's size or slice borders must change, edit `SKINS` in `tools/ui/build_ui_assets.py`, then run
`python3 tools/ui/build_ui_assets.py --generated-only` (regenerates `UiGenerated.cs`) and `build_art.py skins`.
`build_ui_assets.py` no longer draws icons, skins or the app icon unless given `--legacy-art`.
