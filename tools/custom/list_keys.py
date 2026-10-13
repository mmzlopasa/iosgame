#!/usr/bin/env python3
"""Writes the tables of valid custom-asset file names into unity/Assets/Custom/README.md.

The names come from the game itself, so the README never drifts from the code:
  - buildings and decorations (sprite keys, names, plot sizes): unity/Assets/Resources/content.json
  - UI icons: the files in unity/Assets/Art/UI/Resources/UI/Icons (+ law_<id> for every law)
  - 9-slice skins and their borders: unity/Assets/Scripts/UI/UiGenerated.cs
  - images: unity/Assets/Art/UI/Resources/UI/Images

Run after changing content or UI art:  python3 tools/custom/list_keys.py
(--check exits with 1 when the README is out of date, without writing it.)
"""
import json
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "unity" / "Assets"
README = ASSETS / "Custom" / "README.md"
BEGIN, END = "<!-- KEYS:BEGIN (tools/custom/list_keys.py) -->", "<!-- KEYS:END -->"
CELL = 2  # metres per grid cell (Grid.CellSize)
TERRAIN = ["Grass", "Mud", "Sand", "RockGround"]
TILED = {"panel", "plank", "section"}


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24])


def plot(fp):
    w, h = fp["width"], fp["height"]
    return f"{w}×{h} ({w * CELL}×{h * CELL} m)"


def tables():
    content = json.loads((ASSETS / "Resources" / "content.json").read_text(encoding="utf-8"))
    out = []

    # Buildings: every sprite key, the buildings (and levels) that show it, and their plots.
    uses = {}
    for b in content["buildings"]:
        looks = [(b["sprite"], 1, b["name"])] + [(l["sprite"], l["level"], l.get("name") or b["name"]) for l in b.get("looks", [])]
        for sprite, level, shown in looks:
            label = f"{b['name']} (`{b['id']}`)" + (f" od razine / from level {level} – „{shown}”" if level > 1 else "")
            uses.setdefault(sprite, []).append((label, plot(b["footprint"])))
    out.append("### Zgrade / Buildings – `Buildings/<ključ>.fbx`\n")
    out.append("| Ključ (ime datoteke) / Key (file name) | Koristi se za / Used for | Parcela / Plot (cells, metres) |")
    out.append("|---|---|---|")
    for sprite in sorted(uses):
        labels = "<br>".join(u for u, _ in uses[sprite])
        plots = "<br>".join(p for _, p in uses[sprite])
        out.append(f"| `{sprite}` | {labels} | {plots} |")
    out.append("")

    # Decorations.
    out.append("### Ukrasi / Decorations – `Decorations/<ključ>.fbx`\n")
    out.append("| Ključ / Key | Ime / Name | Vrsta / Category | Napomena / Note |")
    out.append("|---|---|---|---|")
    for d in content["decorations"]:
        key = d.get("sprite") or d["id"]
        if d.get("road") and not d.get("bridge"):
            note = "cesta se crta po tlu – **ne može se zamijeniti** / painted road, not replaceable"
            key_cell = f"~~`{key}`~~"
        elif d.get("bridge"):
            note = "most: ishodište modela = površina vode, dug 2 m po osi Z / bridge: origin = water surface, 2 m along Z"
            key_cell = f"`{key}`"
        elif d.get("category") == "Fences":
            note = "s modelom se dijelovi ne spajaju; okreće se / pieces no longer join, they turn"
            key_cell = f"`{key}`"
        else:
            note = ""
            key_cell = f"`{key}`"
        out.append(f"| {key_cell} | {d['name']} | {d.get('category', '')} | {note} |")
    out.append("")

    # Building pictures.
    deco_icons = [f"`deco_{d.get('sprite') or d['id']}`" for d in content["decorations"]]
    out.append("### Slike zgrada / Building pictures – `Icons/<ključ>.png`\n")
    out.append("Za zgradu: isti ključ kao u tablici zgrada (npr. `Icons/longhouse.png`). "
               "For a building: the same key as in the buildings table.\n")
    out.append("Za ukras / For a decoration: " + ", ".join(deco_icons) + "\n")

    # UI icons.
    icons = sorted(p.stem for p in (ASSETS / "Art" / "UI" / "Resources" / "UI" / "Icons").glob("*.png"))
    icons += sorted(f"law_{l['id']}" for l in content.get("laws", []) if f"law_{l['id']}" not in icons)
    out.append("### Ikone sučelja / UI icons – `UI/Icons/<ime>.png` (kvadratne, 128×128 px)\n")
    out.append(", ".join(f"`{i}`" for i in icons) + "\n")

    # Skins.
    src = (ASSETS / "Scripts" / "UI" / "UiGenerated.cs").read_text(encoding="utf-8")
    scale = int(re.search(r"SkinScale = (\d+)", src).group(1))
    skin_dir = ASSETS / "Art" / "UI" / "Resources" / "UI" / "Skin"
    out.append("### Okviri i gumbi / UI skins (9-slice) – `UI/Skin/<ime>.png`\n")
    out.append("Rubovi (lijevo, gore, desno, dolje) se ne rastežu; sredina se rasteže"
               " (kod `panel`, `plank`, `section` se ponavlja). Druga veličina slike je u redu ako su proporcije iste – rubovi se skaliraju s njom. "
               "Borders (left, top, right, bottom) never stretch; the middle does (tiles for panel/plank/section). "
               "Another resolution is fine with the same proportions – the borders scale with it.\n")
    out.append("| Ime / Name | Slika igre / Game picture (px) | Rubovi / Borders L,T,R,B (px) | Sredina / Middle |")
    out.append("|---|---|---|---|")
    for name, l, t, r, b in re.findall(r'\["(\w+)"\] = new SkinSlice\((\d+), (\d+), (\d+), (\d+)\)', src):
        png = skin_dir / f"{name}.png"
        size = "×".join(map(str, png_size(png))) if png.exists() else "?"
        border = ", ".join(str(int(v) * scale) for v in (l, t, r, b))
        out.append(f"| `{name}` | {size} | {border} | {'ponavlja / tiled' if name in TILED else 'rasteže / stretched'} |")
    out.append("")

    # Images.
    image_dir = ASSETS / "Art" / "UI" / "Resources" / "UI" / "Images"
    images = [f"`{p.stem}` ({'×'.join(map(str, png_size(p)))} px)" for p in sorted(image_dir.glob("*.png"))]
    out.append("### Velike slike / Images – `UI/Images/<ime>.png`\n")
    out.append(", ".join(images) + "\n")

    # Terrain.
    out.append("### Tlo / Ground – `Textures/Terrain/<ime>.png` (+ `<ime>_normal.png`)\n")
    out.append(", ".join(f"`{t}`" for t in TERRAIN) + " – tekstura se ponavlja svakih 5 m / tiles every 5 m.\n")
    return "\n".join(out)


def main():
    text = README.read_text(encoding="utf-8")
    if BEGIN not in text or END not in text:
        sys.exit(f"{README}: markers {BEGIN} … {END} missing")
    head, rest = text.split(BEGIN, 1)
    _, tail = rest.split(END, 1)
    new = head + BEGIN + "\n" + tables() + "\n" + END + tail
    if "--check" in sys.argv:
        if new != text:
            sys.exit("Assets/Custom/README.md is out of date: run python3 tools/custom/list_keys.py")
        return
    README.write_text(new, encoding="utf-8")
    print(f"wrote {README.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
