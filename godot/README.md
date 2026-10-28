# Founders - The Long Rise (Godot)

Since 2026-10-27 the game is rebuilt in **Godot 4.7 (.NET / C#)**. The Unity version is frozen on the branch
`unity-archive` (IPA in the `unity-final` release) and stays in `unity/` untouched.

- `Core/` — the game rules, copied from `unity/Assets/Scripts/Core` (no engine code). Saves use the same format.
- `data/content.json` — all buildings, research, eras and balance.
- `scenes/`, `scripts/` — the Godot side (world, camera, UI).
- `tests/` — the rules tests (`dotnet test godot/tests`), Godot ignores this folder.
- Landscape only, iOS target, bundle id `com.scyde.founders.godot` (installs next to the Unity app).

Builds run only in GitHub Actions (`.github/workflows/godot-build.yml`): rules tests and a C# compile on Linux,
then Godot exports an Xcode project on macOS, it is built unsigned and published as `Founders-Godot.ipa` in the
`godot-latest` pre-release.

Licensed packs (Adventure UI Kit, Ultimate Nature) stay in their private repositories. The Asset Store EULA allows
them outside Unity, but they must never be committed here (this repository is public).

## Rebuild plan

1. **Foundation** — project, CI to IPA, rules + save running, the valley drawn, camera, top bar. *(this step)*
2. **World** — terrain with real heights and painted materials, water, nature (trees, rocks, flowers), day/night, seasons.
3. **Buildings** — every building as a model (from the Blender pipeline in `tools/`), construction stages, placing and roads.
4. **People** — villagers walking, working, carrying; portraits.
5. **UI** — new interface: HUD, build menu, people, research, goals, world, trade, profile, settings, loading screen.
6. **Services** — cloud save (server/cloud API), profile, admin page keeps working.
7. **Polish** — sound, effects, performance on iPhone, then the first TestFlight-ready build.
