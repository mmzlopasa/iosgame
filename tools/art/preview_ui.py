#!/usr/bin/env python3
"""Review sheets for the UI art: composes a mock popup from the real skins (9-sliced with the slice
table in build_ui_assets.SKINS, exactly like the game) plus icons and Changa One text, and a sheet of
every skin at its native size and stretched.

Usage: python3 tools/art/preview_ui.py <out dir> [--skins <dir>] [--icons <dir>]
Writes <out>/ui_mock.png and <out>/skins_sheet.png.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "ui"))
import build_ui_assets as B  # noqa: E402

UI = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI")
FONT = os.path.join(UI, "Fonts", "ChangaOne-Regular.ttf")
K = 3  # px per pt


def nine(skin_dir, name, w, h):
    """9-slice a skin to w x h points (returns an RGBA image of w*K x h*K px)."""
    im = Image.open(os.path.join(skin_dir, name + ".png")).convert("RGBA")
    _html, _w, _h, l, t, r, b = B.SKINS[name]
    l, t, r, b = l * K, t * K, r * K, b * K
    W, H = int(w * K), int(h * K)
    iw, ih = im.size
    out = Image.new("RGBA", (W, H))
    xs_src = [0, l, iw - r, iw]
    ys_src = [0, t, ih - b, ih]
    xs_dst = [0, l, W - r, W]
    ys_dst = [0, t, H - b, H]
    for i in range(3):
        for j in range(3):
            sx0, sx1, sy0, sy1 = xs_src[i], xs_src[i + 1], ys_src[j], ys_src[j + 1]
            dx0, dx1, dy0, dy1 = xs_dst[i], xs_dst[i + 1], ys_dst[j], ys_dst[j + 1]
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            piece = im.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.BILINEAR)
            out.paste(piece, (dx0, dy0))
    return out


def text(d, xy, s, size, fill="#FFFFFF", outline="#2B1A0E", anchor="la", stroke=None):
    f = ImageFont.truetype(FONT, int(size * K))
    sw = stroke if stroke is not None else max(2, int(size * K * 0.12))
    x, y = xy
    if outline:
        d.text((x * K, y * K + sw * 0.6), s, font=f, fill=outline, anchor=anchor, stroke_width=sw, stroke_fill=outline)
    d.text((x * K, y * K), s, font=f, fill=fill, anchor=anchor, stroke_width=sw if outline else 0,
           stroke_fill=outline)


def icon(canvas, icons, name, x, y, size):
    p = os.path.join(icons, name + ".png")
    if not os.path.exists(p):
        return
    im = Image.open(p).convert("RGBA").resize((int(size * K), int(size * K)), Image.LANCZOS)
    canvas.alpha_composite(im, (int(x * K), int(y * K)))


def mock(skins, icons, out):
    W, H = 390, 760
    bg = Image.new("RGBA", (W * K, H * K), (96, 78, 140, 255))
    d = ImageDraw.Draw(bg)
    # backdrop gradient
    for yy in range(H * K):
        t = yy / (H * K)
        d.line([(0, yy), (W * K, yy)], fill=(int(110 - 30 * t), int(86 - 20 * t), int(150 - 30 * t), 255))
    # HUD row
    cap = nine(skins, "capsule", 120, 30)
    bg.alpha_composite(cap, (int(66 * K), int(18 * K)))
    icon(bg, icons, "moon", 70, 20, 26)
    text(d, (98, 24), "20:51", 15)
    cap2 = nine(skins, "capsule", 120, 30)
    bg.alpha_composite(cap2, (int(196 * K), int(18 * K)))
    icon(bg, icons, "wood", 199, 20, 26)
    text(d, (228, 24), "1.8M", 15)
    tile = nine(skins, "tile", 50, 52)
    bg.alpha_composite(tile, (int(10 * K), int(10 * K)))
    icon(bg, icons, "face_m1", 13, 12, 44)
    # popup
    px, py, pw, ph = 22, 80, 346, 520
    bg.alpha_composite(nine(skins, "panel", pw, ph), (px * K, py * K))
    bg.alpha_composite(nine(skins, "plank", pw + 8, 38), ((px - 4) * K, (py - 6) * K))
    text(d, (px + pw / 2, py + 12), "Watchtower Lv. 3", 17, anchor="ma")
    # section with rows
    sx, sy, sw_, sh = px + 12, py + 46, pw - 24, 150
    bg.alpha_composite(nine(skins, "section", sw_, sh), (sx * K, sy * K))
    text(d, (sx + 10, sy + 8), "Upgrade Resources", 13)
    rows = [("wood", "Wood", "1.8M / 1.7M", True), ("stone", "Stone", "1.2M / 1.7M", False),
            ("food", "Food", "900 / 500", True)]
    for i, (ic, label, val, ok) in enumerate(rows):
        yy = sy + 34 + i * 36
        icon(bg, icons, ic, sx + 10, yy, 26)
        text(d, (sx + 42, yy + 5), label, 12)
        text(d, (sx + 150, yy + 5), val, 12, fill="#9ACC41" if ok else "#FF6A5A")
        if ok:
            icon(bg, icons, "check", sx + sw_ - 40, yy, 26)
        else:
            b = nine(skins, "btn_blue", 52, 28)
            bg.alpha_composite(b, (int((sx + sw_ - 62) * K), int(yy * K)))
            text(d, (sx + sw_ - 36, yy + 5), "Go", 12, anchor="ma")
    # tabs
    ty = sy + sh + 12
    for i, (lab, on) in enumerate((("Furniture", False), ("Recruit 3/4", True))):
        tw = (sw_ - 8) / 2
        t_ = nine(skins, "tab_on" if on else "tab_off", tw, 34.5 if on else 33.5)
        bg.alpha_composite(t_, (int((sx + i * (tw + 8)) * K), int(ty * K)))
        text(d, (sx + i * (tw + 8) + tw / 2, ty + 8), lab, 13, anchor="ma")
    # portraits in slots / rarity cells
    cy = ty + 50
    faces = ["face_m1", "face_m2", "face_f1", "user", "lock"]
    for i, f in enumerate(faces):
        cx = sx + 6 + i * 62
        bg.alpha_composite(nine(skins, "slot", 52, 54.5), (int(cx * K), int(cy * K)))
        icon(bg, icons, f, cx + 4, cy + 3, 44 if f.startswith("face") else 34)
    cy2 = cy + 66
    rar = ["cell_common", "cell_uncommon", "cell_rare", "cell_epic", "cell_legendary"]
    ics = ["stone", "wood", "winter", "knowledge", "trophy"]
    for i, (c, ic) in enumerate(zip(rar, ics)):
        cx = sx + 6 + i * 62
        bg.alpha_composite(nine(skins, c, 52, 54.5), (int(cx * K), int(cy2 * K)))
        icon(bg, icons, ic, cx + 7, cy2 + 5, 38)
        text(d, (cx + 26, cy2 + 38), "100", 11, anchor="ma")
    # progress bars
    by = cy2 + 70
    for i, f in enumerate(("fill_green", "fill_gold", "fill_blue", "fill_red")):
        xx = sx + i * 80
        bg.alpha_composite(nine(skins, "bar_track", 72, 14), (int(xx * K), int(by * K)))
        bg.alpha_composite(nine(skins, f, 18 + i * 14, 14), (int(xx * K), int(by * K)))
    # buttons row
    yb = py + ph - 62
    bg.alpha_composite(nine(skins, "btn_orange", 150, 46), (int((px + 14) * K), int(yb * K)))
    text(d, (px + 14 + 75, yb + 6), "Finish Now", 15, anchor="ma")
    text(d, (px + 14 + 75, yb + 24), "1,698", 12, anchor="ma")
    bg.alpha_composite(nine(skins, "btn_green", 150, 46), (int((px + pw - 164) * K), int(yb * K)))
    text(d, (px + pw - 164 + 75, yb + 6), "Upgrade", 15, anchor="ma")
    icon(bg, icons, "clock", px + pw - 164 + 42, yb + 23, 16)
    text(d, (px + pw - 164 + 85, yb + 24), "08:59:45", 12, anchor="ma")
    # bottom controls
    yy = 625
    for i, (sk, ic) in enumerate((("btn_red", "minus"), ("btn_green", "plus"), ("btn_grey", "close"),
                                  ("btn_brown", "settings"), ("btn_blue", "info"))):
        bg.alpha_composite(nine(skins, sk, 46, 44), (int((24 + i * 58) * K), int(yy * K)))
        icon(bg, icons, ic, 24 + i * 58 + 10, yy + 7, 26)
    bg.alpha_composite(nine(skins, "badge_green", 26, 24), (int(320 * K), int(yy * K)))
    text(d, (333, yy + 4), "7", 12, anchor="ma")
    bg.alpha_composite(nine(skins, "badge_red", 22, 22), (int(352 * K), int(yy * K)))
    icon(bg, icons, "back", 18, 690, 56)
    bg.convert("RGB").save(out)


def blue_hud(skins, icons, out):
    """The combination: blue-kit HUD and buttons over the game, warm wood/parchment popups."""
    W, H = 390, 720
    bg = Image.new("RGBA", (W * K, H * K))
    d = ImageDraw.Draw(bg)
    for yy in range(H * K):
        t = yy / (H * K)
        d.line([(0, yy), (W * K, yy)], fill=(int(30 + 30 * t), int(110 - 40 * t), int(220 - 60 * t), 255))
    navy = "#1B1F4A"

    def put(name, x, y, w, h):
        bg.alpha_composite(nine(skins, name, w, h), (int(x * K), int(y * K)))

    # top HUD: avatar frame + name/rank, counters with +
    put("hud_hex_frame", 10, 10, 64, 72)
    icon(bg, icons, "face_m1", 18, 22, 48)
    text(d, (80, 18), "FOUNDER", 16, outline=navy)
    put("hud_rank", 80, 42, 70, 18)
    text(d, (115, 43), "Lv. 7", 11, anchor="ma", fill="#FAC602", outline=navy)
    for i, (ic, val) in enumerate((("wood", "1,250"), ("stone", "860"), ("food", "2.4K"))):
        x = 172 + (i % 2) * 108
        y = 14 + (i // 2) * 34
        put("hud_capsule", x + 10, y, 92, 28)
        icon(bg, icons, ic, x - 2, y - 4, 34)
        text(d, (x + 56, y + 5), val, 14, anchor="ma", outline=navy)
        put("hud_plus", x + 82, y + 2.5, 22, 23.5)
    # side hex buttons and round buttons
    for i, ic in enumerate(("storage", "flame", "trophy")):
        put("hud_hex", 330, 100 + i * 58, 48, 48.5)
        icon(bg, icons, ic, 338, 106 + i * 58, 32)
    put("hud_round_blue", 290, 100, 26, 27.5)
    icon(bg, icons, "close", 294, 103, 18)
    put("hud_round_yellow", 290, 134, 26, 27.5)
    icon(bg, icons, "info", 293, 137, 20)
    # energy bar
    put("hud_bar_track", 12, 96, 250, 26)
    put("hud_bar_fill_blue", 34, 102, 160, 14)
    icon(bg, icons, "sick", 13, 98, 22)
    text(d, (140, 101), "68 / 100", 11, anchor="ma", outline=navy)
    # warm popup (unchanged style)
    px, py, pw, ph = 18, 290, 300, 250
    put("panel", px, py, pw, ph)
    put("plank", px - 4, py - 6, pw + 8, 38)
    text(d, (px + pw / 2, py + 2), "Storehouse Lv. 2", 16, anchor="ma")
    put("section", px + 12, py + 44, pw - 24, 110)
    for i, (ic, label) in enumerate((("wood", "Wood"), ("stone", "Stone"))):
        icon(bg, icons, ic, px + 22, py + 56 + i * 44, 30)
        text(d, (px + 58, py + 62 + i * 44), label, 13)
        icon(bg, icons, "check", px + pw - 60, py + 58 + i * 44, 26)
    # buttons in the blue kit's style
    y = py + 170
    put("btn_brown", px + 14, y, 128, 46)
    text(d, (px + 78, y + 12), "REMOVE", 14, anchor="ma", outline=navy)
    put("btn_orange", px + 154, y, 132, 46)
    text(d, (px + 220, y + 12), "LEVEL UP", 15, anchor="ma", outline="#8A4A00")
    rows = [("btn_green", "VIEW ALL"), ("btn_blue", "CLOSE"), ("btn_red", "CANCEL"), ("btn_grey", "LOCKED")]
    for i, (sk, lab) in enumerate(rows):
        x = 14 + (i % 2) * 186
        yy = 560 + (i // 2) * 52
        put(sk, x, yy, 176, 44)
        text(d, (x + 88, yy + 11), lab, 15, anchor="ma", outline=navy)
    # bottom nav: square buttons
    for i, ic in enumerate(("next", "back", "rotate", "close", "settings")):
        put("hud_square_blue", 22 + i * 72, 668, 44, 46)
        icon(bg, icons, ic, 30 + i * 72, 674, 28)
    bg.convert("RGB").save(out)


def sheet(skins, out):
    names = list(B.SKINS)
    W = 1500
    canvas = Image.new("RGBA", (W, 4000), (96, 78, 140, 255))
    x, y, row = 12, 12, 0
    f = ImageFont.truetype(FONT, 18)
    d = ImageDraw.Draw(canvas)
    for n in names:
        im = Image.open(os.path.join(skins, n + ".png")).convert("RGBA")
        big = nine(skins, n, B.SKINS[n][1] * 2.2, B.SKINS[n][2] * 1.2)
        w = im.size[0] + big.size[0] + 30
        if x + w > W:
            x, y, row = 12, y + row + 34, 0
        canvas.alpha_composite(im, (x, y))
        canvas.alpha_composite(big, (x + im.size[0] + 10, y))
        d.text((x, y + max(im.size[1], big.size[1]) + 4), n, font=f, fill="white")
        x += w
        row = max(row, max(im.size[1], big.size[1]))
    canvas.crop((0, 0, W, y + row + 40)).convert("RGB").save(out)


if __name__ == "__main__":
    a = sys.argv[1:]
    out = a[0]
    skins = a[a.index("--skins") + 1] if "--skins" in a else os.path.join(UI, "Skin")
    icons = a[a.index("--icons") + 1] if "--icons" in a else os.path.join(UI, "Icons")
    os.makedirs(out, exist_ok=True)
    mock(skins, icons, os.path.join(out, "ui_mock.png"))
    blue_hud(skins, icons, os.path.join(out, "blue_hud.png"))
    sheet(skins, os.path.join(out, "skins_sheet.png"))
    print("wrote", os.path.join(out, "ui_mock.png"), os.path.join(out, "skins_sheet.png"))
