using System.Collections.Generic;
using UnityEngine;

namespace Founders.UI
{
    /// <summary>
    /// The Unity Asset Store "Adventure UI Kit" (Smashy Tech), when it is imported into the project.
    /// The editor (FoundersEditor.AdventureUiKit, run before every build and from the menu
    /// FOUNDERS → Adventure UI Kit) picks the kit's pictures for the game's skins (panels, buttons,
    /// tabs, bars…) and interface icons and copies them to Assets/Generated/Resources/Kit/UI/…, with the
    /// kit's own 9-slice borders in Kit/UI/Skin/slices.txt. Lookup order everywhere:
    /// the player's own Custom picture → the kit → the game's own. Without the kit nothing changes.
    /// </summary>
    public static class Kit
    {
        public const string Root = "Kit";

        /// <summary>9-slice borders of a kit skin in pixels, and whether its middle is light (dark text on it).</summary>
        public sealed class SkinSlice
        {
            public int Left, Top, Right, Bottom;
            public bool? Light;
        }

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static Dictionary<string, SkinSlice> slices;

        /// <summary>A kit picture by path under Kit/ without extension ("UI/Skin/panel"), or null.</summary>
        public static Texture2D Texture(string path)
        {
            if (textures.TryGetValue(path, out var t)) return t;
            t = Resources.Load<Texture2D>(Root + "/" + path);
            textures[path] = t;
            return t;
        }

        /// <summary>The kit's slices for a skin, or null when the kit has no picture for it.</summary>
        public static SkinSlice Slice(string name)
        {
            if (slices == null) slices = Parse(Resources.Load<TextAsset>(Root + "/UI/Skin/slices")?.text);
            return slices.TryGetValue(name, out var s) ? s : null;
        }

        /// <summary>Lines "name left top right bottom [light|dark]" (pixels); '#' starts a comment.</summary>
        public static Dictionary<string, SkinSlice> Parse(string text)
        {
            var d = new Dictionary<string, SkinSlice>();
            if (string.IsNullOrEmpty(text)) return d;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 5) continue;
                if (!int.TryParse(p[1], out var l) || !int.TryParse(p[2], out var t) ||
                    !int.TryParse(p[3], out var r) || !int.TryParse(p[4], out var b)) continue;
                d[p[0]] = new SkinSlice
                {
                    Left = l, Top = t, Right = r, Bottom = b,
                    Light = p.Length > 5 ? p[5] == "light" : (bool?)null,
                };
            }
            return d;
        }
    }
}
