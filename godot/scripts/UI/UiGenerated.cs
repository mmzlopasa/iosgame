// From tools/ui/build_ui_assets.py (the Unity version's generated slices).
using System.Collections.Generic;

namespace Founders.UI
{
    /// <summary>9-slice borders of a skin texture, in points (the textures are drawn at 3 pixels per point).</summary>
    public readonly struct SkinSlice
    {
        public readonly int Left, Top, Right, Bottom;
        public SkinSlice(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    }

    public static class UiGenerated
    {
        public const int SkinScale = 3;

        public static readonly Dictionary<string, SkinSlice> Skins = new Dictionary<string, SkinSlice>
        {
            ["panel"] = new SkinSlice(16, 16, 16, 19),
            ["plank"] = new SkinSlice(14, 11, 14, 14),
            ["section"] = new SkinSlice(12, 12, 12, 12),
            ["btn_green"] = new SkinSlice(11, 10, 11, 13),
            ["btn_blue"] = new SkinSlice(11, 10, 11, 13),
            ["btn_orange"] = new SkinSlice(11, 10, 11, 13),
            ["btn_grey"] = new SkinSlice(11, 10, 11, 13),
            ["btn_red"] = new SkinSlice(11, 10, 11, 13),
            ["btn_brown"] = new SkinSlice(11, 10, 11, 13),
            ["tab_on"] = new SkinSlice(11, 10, 11, 12),
            ["tab_off"] = new SkinSlice(11, 10, 11, 11),
            ["slot"] = new SkinSlice(12, 12, 12, 14),
            ["tile"] = new SkinSlice(13, 13, 13, 16),
            ["cell_common"] = new SkinSlice(13, 13, 13, 16),
            ["cell_uncommon"] = new SkinSlice(13, 13, 13, 16),
            ["cell_rare"] = new SkinSlice(13, 13, 13, 16),
            ["cell_epic"] = new SkinSlice(13, 13, 13, 16),
            ["cell_legendary"] = new SkinSlice(13, 13, 13, 16),
            ["capsule"] = new SkinSlice(12, 12, 12, 12),
            ["bar_track"] = new SkinSlice(7, 7, 7, 7),
            ["fill_green"] = new SkinSlice(7, 7, 7, 7),
            ["fill_gold"] = new SkinSlice(7, 7, 7, 7),
            ["fill_blue"] = new SkinSlice(7, 7, 7, 7),
            ["fill_red"] = new SkinSlice(7, 7, 7, 7),
            ["badge_green"] = new SkinSlice(7, 7, 7, 8),
            ["badge_red"] = new SkinSlice(9, 9, 9, 10),
            ["hud_capsule"] = new SkinSlice(13, 12, 13, 12),
            ["hud_plus"] = new SkinSlice(10, 10, 10, 11),
            ["hud_hex"] = new SkinSlice(22, 21, 22, 24),
            ["hud_hex_frame"] = new SkinSlice(30, 33, 30, 35),
            ["hud_round_blue"] = new SkinSlice(11, 11, 11, 12),
            ["hud_round_yellow"] = new SkinSlice(11, 11, 11, 12),
            ["hud_square_blue"] = new SkinSlice(12, 11, 12, 14),
            ["hud_bar_track"] = new SkinSlice(26, 8, 8, 8),
            ["hud_bar_fill_blue"] = new SkinSlice(7, 7, 7, 7),
            ["hud_rank"] = new SkinSlice(8, 8, 8, 8),
        };

        /// <summary>Skins whose middle and edges repeat (tiled) instead of stretching: real wood and parchment.</summary>
        public static readonly HashSet<string> Tiled = new HashSet<string> { "panel", "plank", "section" };
    }
}
