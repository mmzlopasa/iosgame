using System.Collections.Generic;
using Godot;

namespace Founders.UI
{
    /// <summary>
    /// The Unity Asset Store "Adventure UI Kit" (Smashy Tech), licensed by the player and kept in a private
    /// repository: the iOS build copies its prepared pictures to res://art/ui/Kit/ (Skin/&lt;name&gt;.png with the
    /// 9-slice borders in Skin/slices.txt, and Icons/), never into the public repository. Without it the game's
    /// own skins are used, with the same layout.
    /// </summary>
    public static class Kit
    {
        public const string Root = "res://art/ui/Kit/";

        /// <summary>9-slice borders of a kit skin in pixels, and whether its middle is light (dark text on it).</summary>
        public sealed class SkinSlice
        {
            public int Left, Top, Right, Bottom;
            public bool? Light;
        }

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static Dictionary<string, SkinSlice> slices;

        /// <summary>A kit picture by path under Kit/ without extension ("Skin/panel"), or null.</summary>
        public static Texture2D Texture(string path)
        {
            if (textures.TryGetValue(path, out var t)) return t;
            t = Theme.Load(Root + path);
            textures[path] = t;
            return t;
        }

        /// <summary>The kit's slices for a skin, or null when the kit has no picture for it.</summary>
        public static SkinSlice Slice(string name)
        {
            if (slices == null)
            {
                string text = null;
                string path = Root + "Skin/slices.txt";
                if (FileAccess.FileExists(path)) text = FileAccess.GetFileAsString(path);
                slices = Parse(text);
            }
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
