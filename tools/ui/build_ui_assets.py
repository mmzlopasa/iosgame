#!/usr/bin/env python3
"""Builds the UI assets for the Unity project: colour icons, 9-slice skins, images, fonts and the
generated C# tables (font advances and skin slices) that keep code and art in step.

Everything is drawn as SVG/HTML/CSS and rasterised with headless Chromium, so the art stays editable
as text. The look follows a casual mobile strategy kit (parchment popups, wooden title planks, chunky
outlined buttons, rarity cells, painted icons, Changa One type); nothing is copied, it is all drawn here.
The font (SIL OFL) is fetched from google/fonts.

The icons, the 9-slice skin textures and the app icon are now rendered by tools/art (Blender + numpy;
see tools/art/README.md). This script still owns the skin sizes and slice borders (SKINS below, which
tools/art/skins.py paints to), the images, fonts and UiGenerated.cs; pass --legacy-art to rebuild the
old CSS/SVG icons, skins and app icon instead.

Usage:  python3 tools/ui/build_ui_assets.py [--no-fonts] [--icons-only] [--legacy-art] [--generated-only]   (needs: pip install fonttools pillow)
Output: unity/Assets/Art/UI/Resources/UI/{Icons,Skin,Images,Fonts}, unity/Assets/Scripts/UI/UiGenerated.cs
"""
import glob
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import icons  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI")
LICENSES = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Licenses")
GENERATED_CS = os.path.join(ROOT, "unity", "Assets", "Scripts", "UI", "UiGenerated.cs")

# Skins are drawn in points and rendered at 3 pixels per point (phones draw ~3 pixels per panel unit).
SKIN_SCALE = 3
ICON_PX = 128
INK = "#3A2314"

# MARK: - Skins (9-slice textures)


def chunky(w, h, r, fill, lip, lip_h=3.5, hi="rgba(255,255,255,.55)", outline=INK, ow=2.5, drop=2.5, extra=""):
    """A rounded box with a thick dark outline, a bright top edge, a darker bottom lip and a drop."""
    return (f'<div style="position:relative;width:{w}px;height:{h + drop}px">'
            f'<div style="position:absolute;left:0;top:{drop}px;width:{w}px;height:{h}px;border-radius:{r}px;background:rgba(0,0,0,.35)"></div>'
            f'<div style="position:absolute;left:0;top:0;box-sizing:border-box;width:{w}px;height:{h}px;border-radius:{r}px;'
            f'border:{ow}px solid {outline};background:{fill};overflow:hidden;'
            f'box-shadow:inset 0 -{lip_h}px 0 {lip}, inset 0 1.6px 0 {hi}">{extra}</div></div>')


def gloss(light, base, dark):
    return f"linear-gradient(180deg,{light} 0%,{light} 44%,{base} 56%,{dark} 100%)"


def cell(top, bottom, rim):
    return chunky(48, 48, 10, f"linear-gradient(180deg,{top},{bottom})", rim, 4, "rgba(255,255,255,.45)",
                  extra=f'<div style="position:absolute;inset:0;border-radius:7px;box-shadow:inset 0 0 0 2px {rim}"></div>')


def noise(alpha=0.10, freq=0.85, tint="0.42 0 0 0 0  0 0.26 0 0 0  0 0 0.12 0 0"):
    """Paper / wood fibre: fractal noise as a translucent overlay (subtle, so it reads as material, not dirt)."""
    svg = ("<svg xmlns='http://www.w3.org/2000/svg' width='120' height='120'><filter id='n'>"
           f"<feTurbulence type='fractalNoise' baseFrequency='{freq}' numOctaves='3' stitchTiles='stitch'/>"
           f"<feColorMatrix values='{tint} 0 0 0 {alpha} 0'/></filter><rect width='120' height='120' filter='url(%23n)'/></svg>")
    return f'<div style="position:absolute;inset:0;background-image:url(&quot;data:image/svg+xml;utf8,{svg}&quot;);background-size:60px 60px"></div>'


PANEL_EXTRA = (noise(0.13) +
               # the kit's cream inner rim just inside the dark outline
               '<div style="position:absolute;inset:2px 2px 5px 2px;border-radius:9px;box-shadow:inset 0 0 0 1.5px rgba(255,246,226,.75)"></div>'
               # little chipped notches on the edges
               '<div style="position:absolute;left:-1px;top:7px;width:5px;height:6px;background:#3A2314;clip-path:polygon(0 0,100% 50%,0 100%)"></div>'
               '<div style="position:absolute;right:-1px;bottom:10px;width:5px;height:6px;background:#3A2314;clip-path:polygon(100% 0,0 50%,100% 100%)"></div>')

GRAIN = (noise(0.16, 0.55, "0.5 0 0 0 0  0 0.28 0 0 0  0 0 0.1 0 0") +
         '<div style="position:absolute;left:6px;right:10px;top:8px;height:1.3px;background:rgba(120,62,24,.35);border-radius:1px"></div>'
         '<div style="position:absolute;left:20px;right:4px;top:15px;height:1.1px;background:rgba(120,62,24,.25);border-radius:1px"></div>'
         '<div style="position:absolute;left:4px;right:24px;top:22px;height:1.2px;background:rgba(120,62,24,.3);border-radius:1px"></div>'
         '<div style="position:absolute;left:46px;top:12px;width:6px;height:3.5px;border-radius:50%;border:1px solid rgba(120,62,24,.35)"></div>')

# name: (html, css width, css height, slices left, top, right, bottom in points)
SKINS = {
    # The popup body: warm parchment, dark outline, lighter rim and a bottom lip.
    "panel": (chunky(72, 72, 12, "linear-gradient(180deg,#EDD6B5,#E6C9A2)", "#C9A27A", 3.5, "#F8EBD4", extra=PANEL_EXTRA), 192, 195, 16, 16, 16, 19),
    # The wooden title strip of a popup (and of sections that need weight).
    "plank": (chunky(80, 34, 7, "linear-gradient(180deg,#EDB677,#D69553 60%,#CB8A55)", "#A8693A", 3.5, "#F7CF98", extra=GRAIN), 188, 38, 14, 11, 14, 14),
    # Inset section inside a panel (no dark outline).
    "section": ('<div style="position:relative;overflow:hidden;box-sizing:border-box;width:48px;height:48px;border-radius:9px;background:#E4C298;'
                'border:1.5px solid #C69E6E;box-shadow:inset 0 2px 2px rgba(90,50,20,.18), 0 1px 0 #F6E6CC">' + noise(0.09) + '</div>', 120, 120, 12, 12, 12, 12),
    # Buttons: green primary, blue secondary, orange premium, grey disabled, red cancel, brown neutral.
    "btn_green": (chunky(48, 40, 8, gloss("#B8E06A", "#9ACC41", "#86B932"), "#5E8E22"), 48, 42.5, 11, 10, 11, 13),
    "btn_blue": (chunky(48, 40, 8, gloss("#B9D3F7", "#94B9EF", "#7FA6E2"), "#5A7FBF"), 48, 42.5, 11, 10, 11, 13),
    "btn_orange": (chunky(48, 40, 8, gloss("#FFD57A", "#FDB939", "#F2A425"), "#C9781A"), 48, 42.5, 11, 10, 11, 13),
    "btn_grey": (chunky(48, 40, 8, gloss("#CFCFCF", "#B3B3B3", "#A3A3A3"), "#7E7E7E"), 48, 42.5, 11, 10, 11, 13),
    "btn_red": (chunky(48, 40, 8, gloss("#EE7F70", "#DD5444", "#CC4434"), "#97281C"), 48, 42.5, 11, 10, 11, 13),
    "btn_brown": (chunky(48, 40, 8, gloss("#E9B47A", "#D69553", "#C9844A"), "#8E5328"), 48, 42.5, 11, 10, 11, 13),
    # Tabs: the selected one raised gold, the others flat brown.
    "tab_on": (chunky(48, 32, 8, gloss("#FFE09A", "#F7C35A", "#EFAF3E"), "#C98420", 3, "#FFF2C8"), 48, 34.5, 11, 10, 11, 12),
    "tab_off": (chunky(48, 32, 8, "linear-gradient(180deg,#B9784A,#A4683A)", "#7E4A24", 2.5, "rgba(255,255,255,.25)", drop=1.5), 48, 33.5, 11, 10, 11, 11),
    # Item slot on parchment, the wooden HUD tile and the rarity cells behind rewards and resources.
    "slot": (chunky(48, 48, 9, "linear-gradient(180deg,#F4E4C8,#EBD3AE)", "#CDA878", 3, "#FFF8EA"), 48, 50.5, 12, 12, 12, 14),
    "tile": (chunky(48, 48, 10, gloss("#F0BC80", "#E0A265", "#CB8A55"), "#9A5E30", 4, "#FBD9A8"), 48, 50.5, 13, 13, 13, 16),
    "cell_common": (cell("#9C9C9C", "#D8D8D8", "#6E6E6E"), 48, 50.5, 13, 13, 13, 16),
    "cell_uncommon": (cell("#4F9A2C", "#A6DE66", "#3A7A1E"), 48, 50.5, 13, 13, 13, 16),
    "cell_rare": (cell("#2A8DBA", "#86D8F2", "#1E6C92"), 48, 50.5, 13, 13, 13, 16),
    "cell_epic": (cell("#9450C8", "#E6A0F4", "#7438A6"), 48, 50.5, 13, 13, 13, 16),
    "cell_legendary": (cell("#D9762A", "#F8BC6C", "#B65C1A"), 48, 50.5, 13, 13, 13, 16),
    # The HUD capsule (dark, translucent) and small chips on it.
    "capsule": ('<div style="box-sizing:border-box;width:40px;height:26px;border-radius:11px;background:rgba(30,19,11,.66);'
                'border:1.5px solid rgba(20,12,6,.75);box-shadow:inset 0 1.2px 0 rgba(255,255,255,.14)"></div>', 40, 26, 12, 12, 12, 12),
    # Progress bars: dark track, coloured fills.
    "bar_track": ('<div style="box-sizing:border-box;width:30px;height:14px;border-radius:7px;background:#4A2E1A;'
                  f'border:1.5px solid {INK};box-shadow:inset 0 2px 0 rgba(0,0,0,.35)"></div>', 30, 14, 7, 7, 7, 7),
    "fill_green": ('<div style="box-sizing:border-box;width:30px;height:14px;border-radius:7px;background:linear-gradient(180deg,#C6F07A,#8BD040 55%,#6CB02C);'
                   'border:1.5px solid #2F5A14;box-shadow:inset 0 1.5px 0 rgba(255,255,255,.55)"></div>', 30, 14, 7, 7, 7, 7),
    "fill_gold": ('<div style="box-sizing:border-box;width:30px;height:14px;border-radius:7px;background:linear-gradient(180deg,#FFE48A,#FDB939 55%,#E89A20);'
                  'border:1.5px solid #7A4A10;box-shadow:inset 0 1.5px 0 rgba(255,255,255,.6)"></div>', 30, 14, 7, 7, 7, 7),
    "fill_blue": ('<div style="box-sizing:border-box;width:30px;height:14px;border-radius:7px;background:linear-gradient(180deg,#C2DCFA,#94B9EF 55%,#6E98DC);'
                  'border:1.5px solid #2C4A80;box-shadow:inset 0 1.5px 0 rgba(255,255,255,.6)"></div>', 30, 14, 7, 7, 7, 7),
    "fill_red": ('<div style="box-sizing:border-box;width:30px;height:14px;border-radius:7px;background:linear-gradient(180deg,#F49284,#DD5444 55%,#C23C2C);'
                 'border:1.5px solid #6E1A10;box-shadow:inset 0 1.5px 0 rgba(255,255,255,.5)"></div>', 30, 14, 7, 7, 7, 7),
    # Badges: the green level square and the red notification dot.
    "badge_green": (chunky(20, 20, 5, "linear-gradient(180deg,#A9DC55,#7DBE34)", "#5E8E22", 2, "rgba(255,255,255,.5)", ow=2, drop=1.5), 20, 21.5, 7, 7, 7, 8),
    "badge_red": (chunky(20, 20, 10, "linear-gradient(180deg,#F07060,#D84434)", "#97281C", 2, "rgba(255,255,255,.5)", ow=2, drop=1.5), 20, 21.5, 9, 9, 9, 10),
    # HUD pieces in the blue casual kit's style: painted by tools/art/skins.py only (no CSS version).
    "hud_capsule": ("", 60, 26, 13, 12, 13, 12),          # dark navy resource counter bar
    "hud_plus": ("", 22, 23.5, 10, 10, 10, 11),            # green + next to a counter (fixed size)
    "hud_hex": ("", 48, 48.5, 22, 21, 22, 24),             # hexagon button (shop, energy, gift)
    "hud_hex_frame": ("", 64, 72, 30, 33, 30, 35),         # hexagon avatar frame (name / rank block)
    "hud_round_blue": ("", 24, 25.5, 11, 11, 11, 12),      # small round button (close)
    "hud_round_yellow": ("", 24, 25.5, 11, 11, 11, 12),    # small round button (info)
    "hud_square_blue": ("", 40, 42.5, 12, 11, 12, 14),     # square nav button (down, back, next, close, settings)
    "hud_bar_track": ("", 90, 24, 26, 8, 8, 8),            # heart / energy bar track with a hexagon cap
    "hud_bar_fill_blue": ("", 30, 14, 7, 7, 7, 7),         # its cyan-blue fill
    "hud_rank": ("", 40, 16, 8, 8, 8, 8),                  # small navy pill with a yellow rim (rank)
}


# Skins whose middle and edges repeat instead of stretching (-unity-slice-type: tiled): the real-material
# skins painted by tools/art/skins.py (wood grain and parchment repeat seamlessly; stretched they would smear).
TILED = ("panel", "plank", "section")


def build_skins():
    out = os.path.join(OUT, "Skin")
    os.makedirs(out, exist_ok=True)
    for name, (html, w, h, *_s) in SKINS.items():
        if html:  # the HUD skins exist only as tools/art paintings
            render(html, w, h, os.path.join(out, f"{name}.png"), scale=SKIN_SCALE)
    print(f"skins: {len(SKINS)}")


# MARK: - Images

LOGO = """
<svg xmlns="http://www.w3.org/2000/svg" width="{s}" height="{s}" viewBox="0 0 120 120">
  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#FFF0B8"/><stop offset=".55" stop-color="#FFD76A"/><stop offset="1" stop-color="#E9B84A"/>
  </linearGradient></defs>
  <path d="M22 84 A38 38 0 0 1 98 84" fill="none" stroke="url(#g)" stroke-width="3.2" stroke-linecap="round"/>
  <path d="M60 30 V22 M33 41 L28 35 M87 41 L92 35 M22 62 L14 60 M98 62 L106 60" stroke="#FFD76A" stroke-width="3" stroke-linecap="round" opacity=".85"/>
  <ellipse cx="60" cy="88" rx="24" ry="9.5" fill="url(#g)"/>
  <ellipse cx="59" cy="72" rx="16.5" ry="7.8" fill="url(#g)"/>
  <ellipse cx="61" cy="58.5" rx="10" ry="6.2" fill="url(#g)"/>
  <path d="M18 98 H102" stroke="#FFD76A" stroke-width="2.4" stroke-linecap="round" opacity=".55"/>
</svg>"""

# Full-bleed square: iOS applies its own corner mask, so no rounding and no transparency.
APP_ICON = """
<div style="width:1024px;height:1024px;position:relative;overflow:hidden;
  background: radial-gradient(75% 60% at 50% 62%, rgba(255,200,90,.42), transparent 70%), linear-gradient(180deg,#1E2130 0%,#14161D 60%,#0D0F14 100%)">
<svg width="1024" height="1024" viewBox="0 0 120 120" style="position:absolute;left:0;top:0">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FFF0B8"/><stop offset=".55" stop-color="#FFD76A"/><stop offset="1" stop-color="#E9B84A"/></linearGradient>
    <radialGradient id="sun" cx=".5" cy="1" r=".9"><stop offset="0" stop-color="#FFE7A0" stop-opacity=".55"/><stop offset="1" stop-color="#FFD76A" stop-opacity="0"/></radialGradient>
  </defs>
  <path d="M24 84 A36 36 0 0 1 96 84 Z" fill="url(#sun)"/>
  <path d="M0 92 L22 80 L38 88 L58 74 L78 86 L98 76 L120 88 V120 H0 Z" fill="#1A1D25"/>
  <path d="M0 100 L30 92 L60 98 L92 90 L120 96 V120 H0 Z" fill="#0F1117"/>
  <path d="M24 84 A36 36 0 0 1 96 84" fill="none" stroke="url(#g)" stroke-width="3" stroke-linecap="round"/>
  <path d="M60 36 V29 M35 46 L30 41 M85 46 L90 41 M25 65 L18 63 M95 65 L102 63" stroke="#FFD76A" stroke-width="2.6" stroke-linecap="round" opacity=".85"/>
  <ellipse cx="60" cy="88" rx="22" ry="8.6" fill="url(#g)"/>
  <ellipse cx="59" cy="73.5" rx="15" ry="7" fill="url(#g)"/>
  <ellipse cx="61" cy="61" rx="9.2" ry="5.6" fill="url(#g)"/>
</svg></div>"""

# Loading / launch backdrop (no text: the text is live UI so it stays sharp).
BACKDROP = """
<div style="width:1170px;height:2532px;position:relative;overflow:hidden;
  background: radial-gradient(70% 45% at 50% 34%, rgba(255,215,106,.22), transparent 70%),
              radial-gradient(60% 40% at 0% 100%, rgba(160,175,200,.14), transparent 60%),
              linear-gradient(180deg,#11131a 0%,#0D0F14 55%,#0a0b10 100%)">
<svg width="1170" height="900" viewBox="0 0 390 300" style="position:absolute;left:0;bottom:450px">
  <path d="M0 210 L40 170 L75 192 L120 128 L160 176 L205 120 L250 168 L292 132 L340 182 L390 150 L390 300 L0 300 Z" fill="#1A1D25"/>
  <path d="M0 238 L52 206 L98 230 L150 190 L196 226 L246 196 L300 232 L348 204 L390 222 L390 300 L0 300 Z" fill="#14161D"/>
  <path d="M0 266 L70 246 L132 262 L196 240 L262 260 L330 244 L390 258 L390 300 L0 300 Z" fill="#0F1117"/>
</svg>
<div style="position:absolute;left:0;right:0;bottom:0;height:452px;background:#0F1117"></div>
</div>"""


def chromium():
    hits = sorted(glob.glob("/opt/pw-browsers/chromium_headless_shell-*/chrome-linux/headless_shell"))
    if hits:
        return hits[-1]
    for name in ("chromium", "chromium-browser", "google-chrome"):
        path = shutil.which(name)
        if path:
            return path
    sys.exit("headless Chromium not found")


def render(html, width, height, out_png, transparent=True, scale=1, head=""):
    """Renders an HTML fragment (CSS size width x height) to a PNG of (width*scale) x (height*scale) pixels."""
    pw, ph = int(round(width * scale)), int(round(height * scale))
    with tempfile.TemporaryDirectory() as tmp:
        page = os.path.join(tmp, "page.html")
        with open(page, "w", encoding="utf-8") as f:
            f.write('<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;padding:0;background:transparent}</style>' + head + '</head><body>')
            f.write(html)
            f.write("</body></html>")
        shot = os.path.join(tmp, "shot.png")
        # The window is sized in CSS pixels; a little taller than needed avoids clipped fractional edges.
        args = [chromium(), "--headless", "--no-sandbox", "--disable-gpu", "--hide-scrollbars",
                f"--window-size={int(width + 0.999)},{int(height + 0.999) + 2}", f"--screenshot={shot}",
                f"--force-device-scale-factor={scale}", "--allow-file-access-from-files"]
        if transparent:
            args.append("--default-background-color=00000000")
        args.append("file://" + page)
        subprocess.run(args, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        img = Image.open(shot)
        img = img.convert("RGBA") if transparent else img.convert("RGB")
        img.crop((0, 0, pw, ph)).save(out_png, optimize=True)


def build_icons():
    names = list(icons.ICONS)
    cell_px, cols = ICON_PX + 16, 8
    rows = (len(names) + cols - 1) // cols
    parts = []
    for i, name in enumerate(names):
        x, y = (i % cols) * cell_px, (i // cols) * cell_px
        parts.append(f'<div style="position:absolute;left:{x}px;top:{y}px">{icons.svg(name, ICON_PX)}</div>')
    out = os.path.join(OUT, "Icons")
    if os.path.isdir(out):
        for old in glob.glob(os.path.join(out, "*.png")):
            os.remove(old)
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        sheet = os.path.join(tmp, "sheet.png")
        render('<div style="position:relative">' + "".join(parts) + "</div>", cols * cell_px, rows * cell_px, sheet)
        img = Image.open(sheet)
        for i, name in enumerate(names):
            x, y = (i % cols) * cell_px, (i // cols) * cell_px
            img.crop((x, y, x + ICON_PX, y + ICON_PX)).save(os.path.join(out, f"{name}.png"), optimize=True)
    print(f"icons: {len(names)}")


# Soft warm glow under the heart in its card (the kit's radius illustration): rings at reach now and next.
GLOW = """<div style="width:360px;height:120px;border-radius:50%;
  background:radial-gradient(closest-side, rgba(255,214,90,.95), rgba(255,170,50,.55) 45%, rgba(255,150,40,.18) 75%, rgba(255,150,40,0))"></div>"""


def build_images():
    out = os.path.join(OUT, "Images")
    os.makedirs(out, exist_ok=True)
    render(GLOW, 360, 120, os.path.join(out, "glow.png"))
    render(LOGO.format(s=512), 512, 512, os.path.join(out, "logo_mark.png"))
    render(BACKDROP, 1170, 2532, os.path.join(out, "loading_bg.png"), transparent=False)
    old_ring = os.path.join(out, "avatar_ring.png")
    if os.path.exists(old_ring):
        os.remove(old_ring)
    # The app icon lives outside Resources (it is only referenced by the build script); it is rendered by
    # tools/art/blender_appicon.py unless the legacy art is asked for.
    if LEGACY:
        icon_dir = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "AppIcon")
        os.makedirs(icon_dir, exist_ok=True)
        render(APP_ICON, 1024, 1024, os.path.join(icon_dir, "AppIcon.png"), transparent=False)
    print("images: glow, logo_mark, loading_bg" + (", AppIcon" if LEGACY else ""))


# MARK: - Fonts

FONT_BASE = "https://raw.githubusercontent.com/google/fonts/main/ofl/"
FONTS = {"ChangaOne-Regular.ttf": "changaone/ChangaOne-Regular.ttf"}
FONT_LICENSE = ("changaone/OFL.txt", "OFL-ChangaOne.txt")
# The Adventure UI Kit's display face (Apache 2.0, Google Fonts), checked in with its vertical metrics
# re-centred on the capitals (so a one-line label sits in the middle of its box) and its tiny "%" removed (the
# fallback face draws it): titles, buttons, labels.
DISPLAY_FONT = "LuckiestGuy-Regular.ttf"


def build_fonts():
    out = os.path.join(OUT, "Fonts")
    os.makedirs(out, exist_ok=True)
    os.makedirs(LICENSES, exist_ok=True)
    # Only the kit's font ships now: earlier faces are removed with their licences.
    for old in glob.glob(os.path.join(out, "*")):
        if os.path.basename(old) not in FONTS and os.path.basename(old) != DISPLAY_FONT and not old.endswith(".txt"):
            os.remove(old)
    for old in glob.glob(os.path.join(LICENSES, "OFL-*.txt")):
        if os.path.basename(old) not in (FONT_LICENSE[1], "Apache-LuckiestGuy.txt"):
            os.remove(old)
    for name, path in FONTS.items():
        urllib.request.urlretrieve(FONT_BASE + path, os.path.join(out, name))
    urllib.request.urlretrieve(FONT_BASE + FONT_LICENSE[0], os.path.join(LICENSES, FONT_LICENSE[1]))
    # The licence also travels next to the font files.
    shutil.copy(os.path.join(LICENSES, FONT_LICENSE[1]), os.path.join(out, "OFL-ChangaOne.txt"))
    print("fonts: " + ", ".join(sorted(os.listdir(out))))


# MARK: - Generated C# (font advances for text fitting, skin slices)

EXTRA_CHARS = "·−×°–—’…%"


def write_generated():
    from fontTools.ttLib import TTFont
    font = TTFont(os.path.join(OUT, "Fonts", DISPLAY_FONT))
    cmap, hmtx, upm = font.getBestCmap(), font["hmtx"], font["head"].unitsPerEm
    # Characters the display face leaves out (its "%" is tiny, so it was dropped) are drawn by Changa One.
    back = TTFont(os.path.join(OUT, "Fonts", "ChangaOne-Regular.ttf"))
    bcmap, bhmtx, bupm = back.getBestCmap(), back["hmtx"], back["head"].unitsPerEm

    def adv(ch):
        g = cmap.get(ord(ch))
        if g:
            return hmtx[g][0] / upm
        g = bcmap.get(ord(ch))
        return bhmtx[g][0] / bupm if g else 0.6

    ascii_adv = ", ".join(f"{adv(chr(c)):.3f}f" for c in range(32, 127))
    extra = "\n".join(f'                case \'{c}\': return {adv(c):.3f}f;' for c in EXTRA_CHARS)
    skins = "\n".join(f'            ["{n}"] = new SkinSlice({l}, {t}, {r}, {b}),' for n, (_h, _w, _hh, l, t, r, b) in SKINS.items())
    tiled = ", ".join(f'"{n}"' for n in TILED)
    cs = f"""// <auto-generated> by tools/ui/build_ui_assets.py: do not edit. </auto-generated>
using System.Collections.Generic;

namespace Founders.UI
{{
    /// <summary>9-slice borders of a skin texture, in points (the textures are drawn at {SKIN_SCALE} pixels per point).</summary>
    public readonly struct SkinSlice
    {{
        public readonly int Left, Top, Right, Bottom;
        public SkinSlice(int l, int t, int r, int b) {{ Left = l; Top = t; Right = r; Bottom = b; }}
    }}

    public static class UiGenerated
    {{
        public const int SkinScale = {SKIN_SCALE};

        public static readonly Dictionary<string, SkinSlice> Skins = new Dictionary<string, SkinSlice>
        {{
{skins}
        }};

        /// <summary>Skins whose middle and edges repeat (tiled) instead of stretching: real wood and parchment.</summary>
        public static readonly HashSet<string> Tiled = new HashSet<string> {{ {tiled} }};

        /// <summary>Advance widths (em) of the display face (Luckiest Guy) for ' '..'~'.</summary>
        static readonly float[] Ascii = {{ {ascii_adv} }};

        /// <summary>Advance of a character in em (0.6 for anything not measured).</summary>
        public static float Advance(char c)
        {{
            if (c >= 32 && c < 127) return Ascii[c - 32];
            switch (c)
            {{
{extra}
                default: return 0.6f;
            }}
        }}
    }}
}}
"""
    with open(GENERATED_CS, "w", encoding="utf-8") as f:
        f.write(cs)
    print("generated: " + os.path.relpath(GENERATED_CS, ROOT))


LEGACY = "--legacy-art" in sys.argv

if __name__ == "__main__":
    if LEGACY:
        build_icons()
    if "--icons-only" in sys.argv:
        sys.exit(0)
    if LEGACY:
        build_skins()
    if "--generated-only" not in sys.argv:
        build_images()
    if "--no-fonts" not in sys.argv and "--generated-only" not in sys.argv:
        build_fonts()
    write_generated()
