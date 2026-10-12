"""Colour cartoon icons for the Founders UI (drawn here as SVG, rasterised by build_ui_assets.py).

One family: flat fills with a darker shade and a soft highlight, thin dark inner lines and a thick
dark outline plus a small drop, added to every icon by the same SVG filter (OUTLINE below), so the
outline is identical everywhere. Canvas: 64 x 64 units, art kept inside 7..57 to leave room for it.
"""

INK = "#2B1A0E"
HI = 'fill="#fff" fill-opacity=".5"'
LINE = f'fill="none" stroke="{INK}" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"'
THIN = f'fill="none" stroke="{INK}" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"'

# Uniform outline (dilated silhouette) and a short drop under it.
OUTLINE = f"""<filter id="ol" x="-25%" y="-25%" width="150%" height="150%" color-interpolation-filters="sRGB">
  <feMorphology in="SourceAlpha" operator="dilate" radius="2.7" result="d"/>
  <feFlood flood-color="{INK}"/><feComposite in2="d" operator="in" result="o"/>
  <feOffset in="o" dy="1.8" result="s0"/><feFlood flood-color="#000" flood-opacity=".3"/><feComposite in2="s0" operator="in" result="s"/>
  <feMerge><feMergeNode in="s"/><feMergeNode in="o"/><feMergeNode in="SourceGraphic"/></feMerge>
</filter>"""


def star(cx, cy, r1, r2, n=5, rot=-90):
    import math
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r2
        a = math.radians(rot + i * 180 / n)
        pts.append(f"{cx + r * math.cos(a):.2f},{cy + r * math.sin(a):.2f}")
    return " ".join(pts)


def face(skin, shade, hair, hair2, female, shirt):
    """A villager portrait: head and shoulders."""
    long_hair = f'<path d="M14 30c0-14 8-22 18-22s18 8 18 22v16c-3 2-7 2-9 0V30H23v16c-2 2-6 2-9 0z" fill="{hair2}"/>' if female else ""
    return (long_hair +
            f'<path d="M8 60c1-10 10-15 24-15s23 5 24 15z" fill="{shirt}"/>'
            f'<path d="M8 60c1-10 10-15 24-15 3 0 6 .3 8 .8C30 47 20 52 18 60z" fill="#000" fill-opacity=".12"/>'
            f'<rect x="27" y="38" width="10" height="9" fill="{shade}"/>'
            f'<ellipse cx="32" cy="29" rx="13" ry="14" fill="{skin}"/>'
            f'<path d="M45 29a13 14 0 0 1-13 14c6-4 9-9 9-14z" fill="{shade}"/>'
            f'<ellipse cx="19" cy="31" rx="2.6" ry="3.4" fill="{shade}"/><ellipse cx="45" cy="31" rx="2.6" ry="3.4" fill="{shade}"/>'
            + (f'<path d="M18 26c0-10 6-15 14-15s14 5 14 15c-4-1-8-4-10-8-3 5-10 8-18 8z" fill="{hair}"/>'
               if female else
               f'<path d="M18 27c-1-11 6-17 14-17s15 6 14 17c-2-3-3-6-4-8-6 3-15 3-20 0-1 2-2 5-4 8z" fill="{hair}"/>')
            + f'<circle cx="27" cy="30" r="1.9" fill="{INK}"/><circle cx="37" cy="30" r="1.9" fill="{INK}"/>'
            f'<path d="M28 36.5c2.5 2 5.5 2 8 0" {THIN}/>'
            f'<ellipse cx="24" cy="34" rx="2.4" ry="1.4" fill="#E8826E" fill-opacity=".45"/><ellipse cx="40" cy="34" rx="2.4" ry="1.4" fill="#E8826E" fill-opacity=".45"/>')


ICONS = {
    # MARK: resources
    "food": (  # red apple with a leaf
        '<path d="M32 18c-6-4-19-3-21 10-2 12 6 26 14 27 3 .4 5-1 7-1s4 1.4 7 1c8-1 16-15 14-27-2-13-15-14-21-10z" fill="#E8483C"/>'
        '<path d="M46 20c6 4 8 13 5 22-3 8-8 13-12 13 6-6 10-20 7-35z" fill="#A82A22"/>'
        '<path d="M17 26c1-5 5-8 9-8-4 3-6 7-6 12-2 1-3-1-3-4z" ' + HI + '/>'
        '<path d="M32 19c0-5 1-8 3-10" ' + LINE + '/>'
        '<path d="M35 14c3-6 9-7 13-5-2 5-8 7-13 5z" fill="#6FBF3A"/><path d="M35 14c4-1 8-3 13-5" ' + THIN + '/>'),
    "wood": (  # tree stump with rings (after the kit)
        '<path d="M12 22v26c0 5 9 9 20 9s20-4 20-9V22z" fill="#B8642E"/>'
        '<path d="M40 25v31c7-1 12-4 12-8V22z" fill="#8A4520"/>'
        '<path d="M18 29v22M25 32v24M46 30v20" ' + THIN + ' stroke-opacity=".55"/>'
        '<ellipse cx="32" cy="22" rx="20" ry="9" fill="#F0B56E"/>'
        '<ellipse cx="32" cy="22" rx="13" ry="5.6" fill="none" stroke="#C67A3A" stroke-width="1.8"/>'
        '<ellipse cx="32" cy="22" rx="6.5" ry="2.8" fill="none" stroke="#C67A3A" stroke-width="1.8"/>'
        '<path d="M31 22l7 20 3-18" fill="#F0B56E" stroke="' + INK + '" stroke-width="1.6" stroke-linejoin="round"/>'
        '<path d="M16 19c3-3 8-5 14-5" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="2" stroke-linecap="round"/>'),
    "stone": (  # grey boulder
        '<path d="M10 40l4-17 13-10 17 2 10 13 1 15-11 10-22 1z" fill="#C3CDD1"/>'
        '<path d="M44 15l10 13 1 15-11 10-11 .5 10-12z" fill="#8F9DA4"/>'
        '<path d="M14 23l13-10 6 9-9 8zM33 22l11-7 0 14z" fill="#E9EFF1"/>'
        '<path d="M33 22l-9 8 6 10 13 3M24 30l-10 10" ' + THIN + '/>'),
    "ore": (  # dark rock with copper nuggets
        '<path d="M9 42l5-16 14-11 15 2 11 12 1 14-12 9H21z" fill="#6E6A78"/>'
        '<path d="M43 17l11 12 1 14-12 9-10 1 11-15z" fill="#4C4855"/>'
        '<path d="M14 26l14-11 4 6-12 9z" fill="#8C8898"/>'
        '<path d="M20 33l7-4 6 4-2 8-8 1z" fill="#F29A4A"/><path d="M27 29l6 4-2 8 6-5-1-6z" fill="#C2621F"/>'
        '<path d="M37 21l7 1 2 6-6 3-4-4z" fill="#F7B868"/><path d="M14 44l5-3 5 3-3 5h-5z" fill="#F29A4A"/>'
        '<path d="M22 32l3-1M38 23l3 0" stroke="#fff" stroke-width="1.8" stroke-linecap="round"/>'),
    "hides": (  # stretched hide
        '<path d="M19 9c4 3 9 4 13 4s9-1 13-4c2 4 7 6 10 6-2 6-2 10 0 14-3 4-3 9 0 14-4 1-8 4-10 8-4-2-9-3-13-3s-9 1-13 3c-2-4-6-7-10-8 3-5 3-10 0-14 2-4 2-8 0-14 3 0 8-2 10-6z" fill="#C98A52"/>'
        '<path d="M32 13c4 0 9-1 13-4 2 4 7 6 10 6-2 6-2 10 0 14-3 4-3 9 0 14-4 1-8 4-10 8-4-2-9-3-13-3z" fill="#A86A36"/>'
        '<path d="M24 22c3 3 13 3 16 0M22 35c4 3 16 3 20 0" ' + THIN + ' stroke-opacity=".5"/>'
        '<path d="M15 18c3 0 6-2 7-4" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2" stroke-linecap="round"/>'),
    "knowledge": (  # glowing bulb
        '<circle cx="32" cy="26" r="17" fill="#FFE27A"/>'
        '<path d="M44 14a17 17 0 0 1-6 28l-1 4h-6c9-5 15-17 13-32z" fill="#F2B530"/>'
        '<path d="M24 41h16v8H24z" fill="#B7C3CB"/><path d="M24 44h16" ' + THIN + '/><path d="M27 49h10l-2 5h-6z" fill="#7C8890"/>'
        '<path d="M27 32l5-7 5 7" ' + THIN + '/>'
        '<path d="M21 23c0-5 4-9 8-10" fill="none" stroke="#fff" stroke-opacity=".75" stroke-width="3" stroke-linecap="round"/>'),
    "people": (  # two villagers
        '<path d="M4 56c1-9 7-13 15-13s14 4 15 13z" fill="#5E9BD8"/><circle cx="19" cy="31" r="9" fill="#F2C29A"/>'
        '<path d="M10 30c-1-7 4-11 9-11s10 4 9 11c-2-2-3-4-3-5-4 2-8 2-12 0 0 1-1 3-3 5z" fill="#7A4A24"/>'
        '<path d="M30 57c1-10 8-15 17-15s16 5 17 15z" fill="#7CC04A"/><circle cx="46" cy="28" r="10" fill="#F5CBA6"/>'
        '<path d="M36 27c-1-8 4-13 10-13s11 5 10 13c-2-2-4-5-4-6-4 2-9 2-13 0-1 1-2 4-3 6z" fill="#E0A23A"/>'
        '<path d="M30 57c1-10 8-15 17-15 2 0 4 .2 5 .6C44 44 38 50 37 57z" fill="#000" fill-opacity=".15"/>'),
    "user": (  # vacant slot silhouette
        '<path d="M10 57c1-11 10-17 22-17s21 6 22 17z" fill="#D9BC94"/>'
        '<circle cx="32" cy="25" r="12" fill="#D9BC94"/>'
        '<path d="M40 16a12 12 0 0 1-8 21c5-4 8-12 8-21zM54 57c-1-11-10-17-22-17 9 3 14 9 15 17z" fill="#B8976C"/>'),
    "moon": (
        '<path d="M40 8a24 24 0 1 0 16 36A20 20 0 0 1 40 8z" fill="#8FA8F0"/>'
        '<path d="M56 44A24 24 0 0 1 12 38c10 9 30 11 44 6z" fill="#5C74C8"/>'
        '<circle cx="24" cy="24" r="4" fill="#6E86D8"/><circle cx="30" cy="40" r="3" fill="#6E86D8"/><circle cx="18" cy="36" r="2.4" fill="#6E86D8"/>'
        '<path d="M22 14c-5 3-8 8-9 13" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="2.4" stroke-linecap="round"/>'),
    "summer": (  # sun
        '<g fill="#FFB52E">' + "".join(f'<path d="M32 3l4 9h-8z" transform="rotate({a} 32 32)"/>' for a in range(0, 360, 45)) + '</g>'
        '<circle cx="32" cy="32" r="15" fill="#FFD84A"/><path d="M43 22a15 15 0 0 1-17 24c10 0 18-11 17-24z" fill="#F7A823"/>'
        '<path d="M23 28c1-4 4-7 8-8" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.6" stroke-linecap="round"/>'),
    "spring": (  # sprout
        '<path d="M14 52c5-5 31-5 36 0z" fill="#8C5A32"/>'
        '<path d="M32 52V32" stroke="#4E9A22" stroke-width="4" stroke-linecap="round"/>'
        '<path d="M32 34c-2-9-10-15-21-14 0 10 9 16 21 14z" fill="#7CC74A"/><path d="M32 34c-6-2-12-7-16-12" ' + THIN + '/>'
        '<path d="M32 30c1-11 9-19 22-19 1 12-8 20-22 19z" fill="#9BDB5A"/><path d="M32 30c5-6 11-11 18-15" ' + THIN + '/>'
        '<path d="M15 23c1 3 3 5 6 7" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="2" stroke-linecap="round"/>'),
    "autumn": (  # maple-ish leaf
        '<path d="M32 6l5 10 8-3-2 10 10 2-7 8 6 6-11 1 1 10-10-6-10 6 1-10-11-1 6-6-7-8 10-2-2-10 8 3z" fill="#F08A2E"/>'
        '<path d="M32 6l5 10 8-3-2 10 10 2-7 8 6 6-11 1 1 10-10-6z" fill="#D2601C"/>'
        '<path d="M32 18v40M32 34l-9-7M32 34l9-7" ' + THIN + '/>'),
    "winter": (  # snowflake
        '<g stroke="' + INK + '" stroke-width="8" stroke-linecap="round">'
        + "".join(f'<path d="M32 8v48M32 14l-6-5M32 14l6-5" transform="rotate({a} 32 32)" fill="none"/>' for a in (0, 60, 120)) + '</g>'
        '<g stroke="#9AD6FF" stroke-width="4.4" stroke-linecap="round" fill="none">'
        + "".join(f'<path d="M32 8v48M32 14l-6-5M32 14l6-5" transform="rotate({a} 32 32)"/>' for a in (0, 60, 120)) + '</g>'
        '<circle cx="32" cy="32" r="5" fill="#E6F6FF"/>'),
    "thermometer": (
        '<rect x="25" y="6" width="14" height="38" rx="7" fill="#EAF2F6"/>'
        '<circle cx="32" cy="47" r="10" fill="#E8483C"/><rect x="29" y="22" width="6" height="22" fill="#E8483C"/>'
        '<path d="M39 13v29l-3-2V12z" fill="#B9C9D2"/><circle cx="35" cy="49" r="5" fill="#A82A22" fill-opacity=".6"/>'
        '<path d="M39 16h4M39 24h4M39 32h4" ' + THIN + '/>'
        '<path d="M28 12v8M27 44a5 5 0 0 0 1 5" fill="none" stroke="#fff" stroke-opacity=".8" stroke-width="2" stroke-linecap="round"/>'),
    "flame": (
        '<path d="M32 4c4 10 18 16 18 32a18 18 0 0 1-36 0c0-8 4-13 8-17 0 6 2 9 5 10-1-11 2-19 5-25z" fill="#FF8A2A"/>'
        '<path d="M42 22c5 5 8 9 8 14a18 18 0 0 1-18 18c10-3 14-14 10-32z" fill="#E2531C"/>'
        '<path d="M32 26c3 6 10 9 10 17a10 10 0 0 1-20 0c0-5 3-7 5-9 0 3 1 4 3 5 0-5 1-9 2-13z" fill="#FFD447"/>'
        '<path d="M28 44c0-3 1-5 3-7" fill="none" stroke="#fff" stroke-opacity=".8" stroke-width="2.4" stroke-linecap="round"/>'),
    "campfire": (
        '<path d="M8 50l46-8 2 6-46 8z" fill="#A8602E"/><path d="M10 42l46 8-2 6-46-8z" fill="#C8763A"/>'
        '<path d="M32 6c3 8 14 13 14 25a14 14 0 0 1-28 0c0-6 3-10 6-13 0 5 2 7 4 8-1-8 1-15 4-20z" fill="#FF8A2A"/>'
        '<path d="M32 24c2 5 8 7 8 13a8 8 0 0 1-16 0c0-4 2-5 4-7 0 2 1 3 2 4 0-4 1-7 2-10z" fill="#FFD447"/>'),
    # MARK: people and mood
    "approval": (  # happy face (hope)
        '<circle cx="32" cy="32" r="24" fill="#FFD447"/><path d="M50 16a24 24 0 0 1-30 37c17 2 31-14 30-37z" fill="#F2A71E"/>'
        '<ellipse cx="24" cy="27" rx="3" ry="4" fill="' + INK + '"/><ellipse cx="40" cy="27" rx="3" ry="4" fill="' + INK + '"/>'
        '<path d="M20 36c6 9 18 9 24 0z" fill="#8A2A1C" stroke="' + INK + '" stroke-width="2" stroke-linejoin="round"/>'
        '<path d="M15 26c1-6 5-11 10-13" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="3" stroke-linecap="round"/>'),
    "discontent": (  # angry face
        '<circle cx="32" cy="32" r="24" fill="#F27A4A"/><path d="M50 16a24 24 0 0 1-30 37c17 2 31-14 30-37z" fill="#C9472A"/>'
        '<path d="M18 22l11 5M46 22l-11 5" stroke="' + INK + '" stroke-width="3.2" stroke-linecap="round"/>'
        '<ellipse cx="25" cy="31" rx="2.6" ry="3.2" fill="' + INK + '"/><ellipse cx="39" cy="31" rx="2.6" ry="3.2" fill="' + INK + '"/>'
        '<path d="M22 45c6-6 14-6 20 0" fill="none" stroke="' + INK + '" stroke-width="3" stroke-linecap="round"/>'
        '<path d="M15 26c1-6 5-11 10-13" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="3" stroke-linecap="round"/>'),
    "sick": (  # heart with a plaster
        '<path d="M32 56C14 44 6 34 6 23 6 14 13 8 21 8c5 0 9 3 11 7 2-4 6-7 11-7 8 0 15 6 15 15 0 11-8 21-26 33z" fill="#E8483C"/>'
        '<path d="M50 11c5 3 8 7 8 12 0 11-8 21-26 33 12-12 20-26 18-45z" fill="#A82A22"/>'
        '<rect x="18" y="25" width="28" height="11" rx="3" fill="#F5E3C8" transform="rotate(-35 32 30)"/>'
        '<rect x="28" y="27" width="8" height="7" fill="#E2C9A2" transform="rotate(-35 32 30)"/>'
        '<path d="M12 20c1-5 5-8 9-8" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.6" stroke-linecap="round"/>'),
    "customs": (  # book of customs with a seal
        '<path d="M12 10h34a6 6 0 0 1 6 6v36a6 6 0 0 1-6 6H12z" fill="#A8432E"/>'
        '<path d="M12 50h36v8H16a4 4 0 0 1-4-4z" fill="#F3E2C2"/><path d="M14 52h32" ' + THIN + ' stroke-opacity=".4"/>'
        '<rect x="12" y="10" width="7" height="40" fill="#7E2A1C"/>'
        '<path d="M25 16h20v14H25z" fill="#C9583F"/><path d="M28 21h14M28 25h10" stroke="#F3D7A0" stroke-width="2" stroke-linecap="round"/>'
        '<circle cx="40" cy="40" r="7" fill="#FFC93A"/><circle cx="40" cy="40" r="3.4" fill="#E09A1E"/>'
        '<path d="M36 46l-2 8 6-3 6 3-2-8" fill="#E8483C"/>'),
    "home": (
        '<path d="M14 30h36v26H14z" fill="#E9C48E"/><path d="M40 30h10v26H40z" fill="#C99A62"/>'
        '<path d="M26 40h12v16H26z" fill="#8A4E22"/><path d="M34 40h4v16h-4z" fill="#6A3A18"/>'
        '<path d="M6 33L32 9l26 24-5 4L32 18 11 37z" fill="#E8483C"/><path d="M32 9l26 24-5 4L32 18z" fill="#A82A22"/>'
        '<rect x="41" y="12" width="6" height="10" fill="#9AA6AE"/>'
        '<path d="M18 36h5v5h-5z" fill="#7FC8F0"/>'),
    "storage": (  # wooden crate
        '<path d="M8 22l24-10 24 10v24L32 58 8 46z" fill="#D8964E"/><path d="M32 34l24-12v24L32 58z" fill="#A8642E"/>'
        '<path d="M8 22l24 12 24-12" ' + LINE + '/><path d="M32 34v24" ' + LINE + '/>'
        '<path d="M13 28l14 7M13 38l14 7M37 35l14-7M37 45l14-7" stroke="#8A4E22" stroke-width="2" stroke-linecap="round"/>'
        '<path d="M12 21l20-8" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2" stroke-linecap="round"/>'),
    "build": (  # hammer
        '<path d="M28 26l7 7-21 21a5 5 0 0 1-7-7z" fill="#C8763A"/><path d="M35 33l-21 21a5 5 0 0 1-4 1l21-22z" fill="#8E4E22"/>'
        '<path d="M22 14l10-6c6-1 12 1 16 5l9 9-6 6-5-5-6 6-11-11 2-2c-2-1-6-1-9-2z" fill="#B9C3CA"/>'
        '<path d="M48 13l9 9-6 6-5-5z" fill="#7C8890"/><path d="M26 13c4-3 9-4 14-3" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2" stroke-linecap="round"/>'),
    "goals": (  # target with a dart
        '<circle cx="30" cy="34" r="22" fill="#F5EFE3"/><circle cx="30" cy="34" r="16" fill="#E8483C"/>'
        '<circle cx="30" cy="34" r="10" fill="#F5EFE3"/><circle cx="30" cy="34" r="4.5" fill="#E8483C"/>'
        '<path d="M30 34L52 12" stroke="' + INK + '" stroke-width="3.2" stroke-linecap="round"/>'
        '<path d="M48 8l2 6 6 2-5 4-6-1-1-6z" fill="#FFC93A"/>'),
    "world": (  # folded map
        '<path d="M6 14l16-6 20 6 16-6v42l-16 6-20-6-16 6z" fill="#F3E2C2"/>'
        '<path d="M22 8l20 6v42l-20-6z" fill="#E2C89C"/>'
        '<path d="M10 22c4-2 8 1 10 4-3 3-8 3-10 0zM26 30c5-3 10 0 12 5-4 4-10 4-12 0z" fill="#8CCB55"/>'
        '<path d="M44 20c4 0 8 4 10 8-5 2-9 0-10-3z" fill="#7FC8F0"/>'
        '<path d="M14 44l8-6 6 4 6-8 8 6 8-4" fill="none" stroke="#E8483C" stroke-width="2.2" stroke-dasharray="3 3" stroke-linecap="round"/>'),
    "bell": (
        '<path d="M32 8c-11 0-16 8-16 18 0 10-4 16-8 20h48c-4-4-8-10-8-20 0-10-5-18-16-18z" fill="#FFC93A"/>'
        '<path d="M42 12c4 3 6 8 6 14 0 10 4 16 8 20H40c4-10 5-22 2-34z" fill="#E09A1E"/>'
        '<rect x="6" y="44" width="52" height="6" rx="3" fill="#E09A1E"/><circle cx="32" cy="53" r="5" fill="#C27A10"/>'
        '<path d="M22 22c1-5 4-8 8-9" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.6" stroke-linecap="round"/>'),
    "legacy": (  # scroll
        '<path d="M14 12h32a6 6 0 0 1 6 6v32H20a6 6 0 0 1-6-6z" fill="#F3E2C2"/>'
        '<path d="M46 12a6 6 0 0 1 6 6v32H44V18a6 6 0 0 0-6-6z" fill="#D9B98A"/>'
        '<path d="M14 44a6 6 0 0 0 6 6h28a6 6 0 0 1-6-6z" fill="#D9B98A"/>'
        '<circle cx="14" cy="16" r="6" fill="#C8763A"/><circle cx="48" cy="50" r="6" fill="#C8763A"/>'
        '<path d="M24 22h18M24 29h18M24 36h12" stroke="#A8794A" stroke-width="2.4" stroke-linecap="round"/>'),
    "settings": (  # gear
        '<path d="' + "M32 6" + '" fill="none"/>'
        '<g fill="#B9C3CA">' + "".join(f'<rect x="27" y="4" width="10" height="12" rx="2" transform="rotate({a} 32 32)"/>' for a in range(0, 360, 45)) + '</g>'
        '<circle cx="32" cy="32" r="19" fill="#B9C3CA"/><path d="M45 18a19 19 0 0 1-23 30c14 1 25-12 23-30z" fill="#7C8890"/>'
        '<circle cx="32" cy="32" r="7.5" fill="#5A646C"/>'
        '<path d="M19 28c1-4 4-8 8-9" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.6" stroke-linecap="round"/>'),
    "landmark": (  # stone temple / church
        '<path d="M6 24L32 8l26 16z" fill="#E9D7B4"/><path d="M32 8l26 16H32z" fill="#C9B48C"/>'
        '<rect x="8" y="24" width="48" height="5" fill="#D6C29C"/>'
        + "".join(f'<rect x="{x}" y="29" width="6" height="20" fill="#EFE2C6"/><rect x="{x + 4}" y="29" width="2" height="20" fill="#C9B48C"/>' for x in (12, 23, 35, 46)) +
        '<rect x="6" y="49" width="52" height="7" fill="#C9B48C"/>'
        '<circle cx="32" cy="18" r="3" fill="#FFC93A"/>'),
    "trophy": (
        '<path d="M18 8h28v14c0 10-6 17-14 17s-14-7-14-17z" fill="#FFC93A"/>'
        '<path d="M38 8h8v14c0 10-6 17-14 17 4-4 6-13 6-31z" fill="#E09A1E"/>'
        '<path d="M18 12h-8c0 10 4 14 10 15M46 12h8c0 10-4 14-10 15" ' + LINE + ' stroke-width="3"/>'
        '<path d="M28 38h8v8h-8z" fill="#E09A1E"/><path d="M18 46h28v10H18z" fill="#A8642E"/>'
        '<path d="M24 50h16" stroke="#FFC93A" stroke-width="2.4"/>'
        '<path d="M23 12v8c0 4 1 7 3 9" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.6" stroke-linecap="round"/>'),
    # MARK: actions and glyphs
    "back": (  # big golden arrow (kit back button)
        '<path d="M6 32L30 10v12h16c6 0 12 4 12 10s-6 10-12 10H30v12z" fill="#F2C47A"/>'
        '<path d="M6 32L30 54V42h16c6 0 12-4 12-10H6z" fill="#D9984A"/>'
        '<path d="M13 30l15-13" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.6" stroke-linecap="round"/>'),
    "next": (
        '<path d="M22 10l22 22-22 22-6-6 16-16-16-16z" fill="#F2C47A"/><path d="M44 32L22 54l-6-6 16-16z" fill="#D9984A"/>'),
    "arrow": (  # green delta arrow
        '<path d="M6 24h24V12l26 20-26 20V40H6z" fill="#8FD13F"/><path d="M6 32h50L30 52V40H6z" fill="#4E9A22"/>'
        '<path d="M10 28h22" stroke="#fff" stroke-opacity=".6" stroke-width="2.6" stroke-linecap="round"/>'),
    "close": (
        '<path d="M14 20l6-6 12 12 12-12 6 6-12 12 12 12-6 6-12-12-12 12-6-6 12-12z" fill="#fff"/>'
        '<path d="M44 38l6 6-6 6-12-12z" fill="#000" fill-opacity=".14"/>'),
    "check": (
        '<path d="M8 32l8-8 10 10 22-22 8 8-30 30z" fill="#8FD13F"/><path d="M56 20L26 50 8 32l4-4 14 14 26-26z" fill="#4E9A22"/>'
        '<path d="M13 31l3-3" stroke="#fff" stroke-opacity=".7" stroke-width="2.4" stroke-linecap="round"/>'),
    "plus": ('<path d="M26 10h12v16h16v12H38v16H26V38H10V26h16z" fill="#fff"/>'),
    "minus": ('<rect x="10" y="26" width="44" height="12" rx="2" fill="#fff"/>'),
    "clock": (
        '<circle cx="32" cy="32" r="24" fill="#FFC93A"/><circle cx="32" cy="32" r="18" fill="#FBF4E4"/>'
        '<path d="M44 18a18 18 0 0 1-22 27c12 1 22-11 22-27z" fill="#E6D9BE"/>'
        '<path d="M32 20v12l8 6" ' + LINE + ' stroke-width="3.4"/>'),
    "lock": (
        '<path d="M20 28v-7a12 12 0 0 1 24 0v7" fill="none" stroke="' + INK + '" stroke-width="9"/>'
        '<path d="M20 28v-7a12 12 0 0 1 24 0v7" fill="none" stroke="#B9C3CA" stroke-width="5"/>'
        '<rect x="12" y="27" width="40" height="29" rx="6" fill="#FFC93A"/><path d="M40 27h6a6 6 0 0 1 6 6v17a6 6 0 0 1-6 6h-6z" fill="#E09A1E"/>'
        '<circle cx="32" cy="39" r="4" fill="' + INK + '"/><path d="M32 41v7" stroke="' + INK + '" stroke-width="3.4" stroke-linecap="round"/>'),
    "alert": (
        '<path d="M32 6l27 48H5z" fill="#FFC93A" stroke-linejoin="round"/><path d="M32 6l27 48H38z" fill="#E09A1E"/>'
        '<path d="M32 22v16" stroke="' + INK + '" stroke-width="6" stroke-linecap="round"/><circle cx="32" cy="46" r="3.6" fill="' + INK + '"/>'),
    "info": (
        '<rect x="10" y="10" width="44" height="44" rx="10" fill="#F2A23A"/>'
        '<path d="M32 18v18" stroke="#fff" stroke-width="7" stroke-linecap="round"/><circle cx="32" cy="45" r="4.2" fill="#fff"/>'),
    "sparkle": (
        '<polygon points="' + star(32, 33, 25, 10, 4, -90) + '" fill="#FFD447"/>'
        '<polygon points="32,8 42,23 57,33 42,43 32,58" fill="#F2A71E"/>'
        '<polygon points="' + star(51, 12, 7, 2.6, 4, -90) + '" fill="#FFF1A8"/>'),
    "copy": (
        '<rect x="20" y="18" width="34" height="38" rx="5" fill="#F3E2C2"/><rect x="10" y="8" width="34" height="38" rx="5" fill="#E2C89C"/>'),
    "compass": (
        '<circle cx="32" cy="32" r="25" fill="#FFC93A"/><circle cx="32" cy="32" r="19" fill="#FBF4E4"/>'
        '<path d="M32 13l6 19H26z" fill="#E8483C"/><path d="M32 51l-6-19h12z" fill="#B9C3CA"/>'
        '<circle cx="32" cy="32" r="3" fill="' + INK + '"/>'),
    "eye": (
        '<path d="M4 32c7-12 17-18 28-18s21 6 28 18c-7 12-17 18-28 18S11 44 4 32z" fill="#FBF4E4"/>'
        '<circle cx="32" cy="32" r="11" fill="#5CB8F2"/><circle cx="32" cy="32" r="5" fill="' + INK + '"/><circle cx="29" cy="29" r="2.4" fill="#fff"/>'),
    "eye_off": (
        '<path d="M4 32c7-12 17-18 28-18s21 6 28 18c-7 12-17 18-28 18S11 44 4 32z" fill="#FBF4E4"/>'
        '<circle cx="32" cy="32" r="11" fill="#5CB8F2"/><circle cx="32" cy="32" r="5" fill="' + INK + '"/>'
        '<path d="M10 54L54 10" stroke="' + INK + '" stroke-width="9" stroke-linecap="round"/><path d="M10 54L54 10" stroke="#E8483C" stroke-width="5" stroke-linecap="round"/>'),
    "rotate": (
        '<path d="M50 30a18 18 0 1 1-6-13" fill="none" stroke="' + INK + '" stroke-width="12" stroke-linecap="round"/>'
        '<path d="M50 30a18 18 0 1 1-6-13" fill="none" stroke="#5CB8F2" stroke-width="7" stroke-linecap="round"/>'
        '<path d="M36 8h20v20z" fill="#5CB8F2"/>'),
    "trash": (
        '<path d="M14 18h36l-4 38H18z" fill="#B9C3CA"/><path d="M40 18h10l-4 38h-9z" fill="#7C8890"/>'
        '<rect x="9" y="12" width="46" height="8" rx="3" fill="#D6DDE2"/><path d="M26 12v-4h12v4" fill="#7C8890"/>'
        '<path d="M25 26v22M32 26v22M39 26v22" stroke="#5A646C" stroke-width="2.6" stroke-linecap="round"/>'),
    "level_up": (
        '<path d="M32 6l22 22H42v12H22V28H10z" fill="#8FD13F"/><path d="M32 6l22 22H42v12h-8z" fill="#4E9A22"/>'
        '<path d="M22 44h20v10H22z" fill="#8FD13F"/><path d="M34 44h8v10h-8z" fill="#4E9A22"/>'),
    # MARK: tools and nature
    "pickaxe": (
        '<path d="M30 26l6 6-22 22a4 4 0 0 1-6-6z" fill="#C8763A"/>'
        '<path d="M10 20C18 8 36 4 52 10l2 4-6 2C38 12 24 14 14 24z" fill="#B9C3CA"/>'
        '<path d="M52 10c6 16 2 34-10 42l-4-2 2-6c6-10 8-24 4-34z" fill="#7C8890"/><rect x="27" y="23" width="12" height="12" rx="2" fill="#8E4E22" transform="rotate(45 33 29)"/>'),
    "axe": (
        '<path d="M34 22l6 6-24 26a4 4 0 0 1-6-6z" fill="#C8763A"/>'
        '<path d="M30 14l10-8c8 2 16 10 18 18l-8 10c-2-8-12-18-20-20z" fill="#B9C3CA"/>'
        '<path d="M40 6c8 2 16 10 18 18l-4 5c-2-9-8-17-16-21z" fill="#E6ECEF"/><path d="M30 14l20 20-4 4-20-20z" fill="#7C8890"/>'),
    "spear": (
        '<path d="M12 56l30-30" stroke="' + INK + '" stroke-width="8" stroke-linecap="round"/><path d="M12 56l30-30" stroke="#C8763A" stroke-width="4.4" stroke-linecap="round"/>'
        '<path d="M38 26l18-20-6 24-6 2z" fill="#B9C3CA"/><path d="M56 6l-6 24-6 2z" fill="#7C8890"/>'),
    "fish": (
        '<path d="M8 32c8-12 22-16 34-10l12-8-2 18 2 18-12-8C30 48 16 44 8 32z" fill="#5CB8F2"/>'
        '<path d="M8 32c8 12 22 16 34 10l12 8-2-18H8z" fill="#2F7DC0"/>'
        '<circle cx="20" cy="29" r="3" fill="' + INK + '"/><path d="M28 24c3 5 3 11 0 16" ' + THIN + '/>'
        '<path d="M14 26c4-4 9-6 14-6" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="2.4" stroke-linecap="round"/>'),
    "wheat": (
        '<path d="M32 58V20" stroke="#C99A3A" stroke-width="4" stroke-linecap="round"/>'
        + "".join(f'<ellipse cx="{32 + dx}" cy="{y}" rx="5" ry="8" fill="#FFD25A" transform="rotate({r} {32 + dx} {y})"/>'
                  for dx, y, r in ((-6, 22, -30), (6, 22, 30), (-6, 32, -30), (6, 32, 30), (-6, 42, -30), (6, 42, 30)))
        + '<ellipse cx="32" cy="12" rx="5" ry="8" fill="#FFD25A"/>'
        + "".join(f'<ellipse cx="{32 + dx}" cy="{y}" rx="2" ry="5" fill="#E6A82A" transform="rotate({r} {32 + dx} {y})"/>'
                  for dx, y, r in ((7, 23, 30), (7, 33, 30), (7, 43, 30)))),
    "tree": (
        '<rect x="28" y="44" width="8" height="13" fill="#8E4E22"/>'
        '<path d="M32 4l14 16h-6l12 14h-7l11 13H10l11-13h-7l12-14h-6z" fill="#5DB043"/>'
        '<path d="M32 4l14 16h-6l12 14h-7l11 13H32z" fill="#3E8A30"/>'
        '<path d="M28 14l-5 6M24 30l-6 6" stroke="#fff" stroke-opacity=".5" stroke-width="2.4" stroke-linecap="round"/>'),
    "road": (
        '<path d="M22 6h20l14 52H8z" fill="#C99A62"/><path d="M42 6l14 52H32V6z" fill="#A87A44"/>'
        '<path d="M32 10v8M32 26v9M32 44v10" stroke="#F3E2C2" stroke-width="3.4" stroke-linecap="round"/>'
        '<circle cx="18" cy="48" r="3" fill="#B9C3CA"/><circle cx="46" cy="20" r="2.4" fill="#B9C3CA"/>'),
    "fence": (
        "".join(f'<path d="M{x} 14l5-6 5 6v42H{x}z" fill="#D8964E"/><path d="M{x + 7} 12l3 2v42h-3z" fill="#A8642E"/>' for x in (8, 27, 46))
        + '<rect x="6" y="22" width="52" height="7" fill="#C8763A"/><rect x="6" y="40" width="52" height="7" fill="#C8763A"/>'),
    "lantern": (
        '<path d="M26 6h12v6H26z" fill="#5A646C"/><path d="M18 14h28l-4 6H22z" fill="#5A646C"/>'
        '<rect x="20" y="20" width="24" height="28" rx="3" fill="#FFE27A"/><path d="M24 24h16v20H24z" fill="#FFB52E"/>'
        '<path d="M32 28c3 4 5 6 5 9a5 5 0 0 1-10 0c0-3 2-5 5-9z" fill="#fff" fill-opacity=".85"/>'
        '<path d="M18 48h28l-3 8H21z" fill="#5A646C"/>'),
    # MARK: faces for villager portraits
    "face_m1": face("#F2C29A", "#D6976A", "#6A3A18", "#6A3A18", False, "#5E9BD8"),
    "face_m2": face("#C98E62", "#A86E46", "#2B1A0E", "#2B1A0E", False, "#7CC04A"),
    "face_m3": face("#F5CBA6", "#D8A27A", "#C9883A", "#C9883A", False, "#C8583C"),
    "face_f1": face("#F5CBA6", "#D8A27A", "#A8432E", "#8A321F", True, "#A777E0"),
    "face_f2": face("#E2AE80", "#C08758", "#3A2314", "#2B1A0E", True, "#E8A23A"),
    "face_f3": face("#F2C29A", "#D6976A", "#E9C060", "#C99A3A", True, "#4FB6A8"),
}


def svg(name, size):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 64 64">'
            f'<defs>{OUTLINE}</defs><g filter="url(#ol)"><g transform="translate(32 32) scale(.86) translate(-32 -32)">{ICONS[name]}</g></g></svg>')
