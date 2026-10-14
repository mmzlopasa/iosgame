using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Founders.UI;
using UnityEditor;
using UnityEngine;

namespace FoundersEditor
{
    /// <summary>
    /// Puts the Unity Asset Store "Adventure UI Kit" (Smashy Tech) on the game's interface.
    ///
    /// The game's UI is built in code from named 9-slice skins (panel, plank, section, btn_green, tab_on,
    /// slot, bar_track, fill_gold…, see UiGenerated) and named icons. When the kit is imported anywhere
    /// under Assets (a folder whose path contains "Adventure"), this picks a kit picture for every skin
    /// and interface icon by its file and folder names (colour words, "button", "panel", "tab"…), copies
    /// them to Assets/Generated/Resources/Kit/UI/{Skin,Icons} and writes the kit's own 9-slice borders to
    /// Kit/UI/Skin/slices.txt (runtime: Founders.UI.Kit). Skins the kit has no picture for keep the game's
    /// own, re-coloured nothing: the game's look fills the gaps.
    ///
    /// Choices can be corrected in Assets/Custom/AdventureUiKit.txt, one per line:
    ///     skin:panel = Assets/AdventureUIKit/Sprites/Popup_Big.png
    ///     icon:close = Assets/AdventureUIKit/Icons/X.png
    ///     skin:plank = none            (keep the game's own)
    ///     folder = Assets/SomeOtherName (where the kit is, if its folder is not called "Adventure…")
    /// The menu command writes the current choices there for editing; builds read it.
    ///
    /// Runs before every build (BuildScript.Generate) and from FOUNDERS → Adventure UI Kit → Apply.
    /// Assets/Generated is not in git: the copies are made again on every machine and in CI.
    /// </summary>
    public static class AdventureUiKit
    {
        const string OutRoot = "Assets/Generated/Resources/" + Kit.Root + "/UI/";
        const string MapFile = "Assets/Custom/AdventureUiKit.txt";
        static readonly string[] ImageExt = { ".png", ".psd", ".tga", ".jpg", ".jpeg" };

        // MARK: - What each of the game's skins looks like in a UI kit's file names

        sealed class Role
        {
            public string Name;
            public string[][] All;     // every group must match (any word of the group)
            public string[] Bonus = Array.Empty<string>();
            public string[] Not = Array.Empty<string>();
            public bool Square;        // a square-ish picture (slots, round buttons)
        }

        static readonly string[] ButtonW = { "button", "btn", "buttons" };
        static readonly string[] PanelW = { "panel", "popup", "window", "frame", "board", "dialog", "background", "bg", "plate" };
        static readonly string[] SlotW = { "slot", "cell", "item", "inventory", "card" };
        static readonly string[] BarW = { "bar", "progress", "slider", "hp", "health", "exp", "xp" };
        static readonly string[] Green = { "green" }, Blue = { "blue", "cyan", "azure" }, Red = { "red" };
        static readonly string[] Orange = { "orange", "yellow", "gold", "golden" }, Grey = { "grey", "gray", "disabled", "inactive", "silver" };
        static readonly string[] Brown = { "brown", "wood", "wooden" }, Purple = { "purple", "violet", "magenta", "pink" };
        static readonly string[] NotIcon = { "icon", "icons", "ico" };

        static Role R(string name, string[][] all, string[] bonus = null, string[] not = null, bool square = false) =>
            new Role { Name = name, All = all, Bonus = bonus ?? Array.Empty<string>(), Not = (not ?? Array.Empty<string>()).Concat(NotIcon).ToArray(), Square = square };

        static readonly Role[] Roles =
        {
            R("panel", new[] { PanelW }, new[] { "big", "main", "large", "popup", "window", "base" }, new[] { "title", "header", "small", "inner", "button", "btn", "tab", "bar", "slot", "shadow" }),
            R("plank", new[] { new[] { "title", "header", "ribbon", "banner", "name", "label", "heading" } }, new[] { "panel", "big", "wide" }, new[] { "button", "btn", "icon" }),
            R("section", new[] { new[] { "inner", "content", "inset", "box", "field", "section", "list", "frame", "panel" } }, new[] { "inner", "inset", "content", "small" }, new[] { "button", "btn", "title", "header", "tab", "bar", "big", "main" }),
            R("btn_green", new[] { ButtonW, Green }, new[] { "big", "normal" }, new[] { "round", "circle", "small", "pressed" }),
            R("btn_blue", new[] { ButtonW, Blue }, new[] { "big", "normal" }, new[] { "round", "circle", "small", "pressed" }),
            R("btn_orange", new[] { ButtonW, Orange }, new[] { "big", "normal" }, new[] { "round", "circle", "small", "pressed" }),
            R("btn_red", new[] { ButtonW, Red }, new[] { "big", "normal" }, new[] { "round", "circle", "small", "pressed" }),
            R("btn_grey", new[] { ButtonW, Grey }, new[] { "big", "normal" }, new[] { "round", "circle", "small", "pressed" }),
            R("btn_brown", new[] { ButtonW, Brown }, new[] { "big", "normal" }, new[] { "round", "circle", "small", "pressed" }),
            R("tab_on", new[] { new[] { "tab", "tabs" } }, new[] { "on", "active", "selected", "select", "pressed", "current" }, new[] { "off", "inactive", "unselected", "normal" }),
            R("tab_off", new[] { new[] { "tab", "tabs" } }, new[] { "off", "inactive", "unselected", "normal", "idle" }, new[] { "on", "active", "selected" }),
            R("slot", new[] { SlotW }, new[] { "empty", "bg", "background", "frame" }, new[] { "button", "btn" }, true),
            R("tile", new[] { SlotW }, new[] { "frame", "bg" }, new[] { "button", "btn", "empty" }, true),
            R("cell_common", new[] { SlotW, Grey.Concat(new[] { "common", "white" }).ToArray() }, square: true),
            R("cell_uncommon", new[] { SlotW, Green.Concat(new[] { "uncommon" }).ToArray() }, square: true),
            R("cell_rare", new[] { SlotW, Blue.Concat(new[] { "rare" }).ToArray() }, square: true),
            R("cell_epic", new[] { SlotW, Purple.Concat(new[] { "epic" }).ToArray() }, square: true),
            R("cell_legendary", new[] { SlotW, Orange.Concat(new[] { "legendary", "legend" }).ToArray() }, square: true),
            R("capsule", new[] { new[] { "capsule", "pill", "counter", "resource", "currency", "field", "input", "amount" } }, new[] { "bg", "frame" }, new[] { "button", "btn" }),
            R("bar_track", new[] { BarW, new[] { "bg", "back", "background", "track", "empty", "frame", "base" } }),
            R("fill_green", new[] { BarW, Green }, new[] { "fill", "filler" }, new[] { "bg", "back", "frame" }),
            R("fill_gold", new[] { BarW, Orange }, new[] { "fill", "filler" }, new[] { "bg", "back", "frame" }),
            R("fill_blue", new[] { BarW, Blue }, new[] { "fill", "filler" }, new[] { "bg", "back", "frame" }),
            R("fill_red", new[] { BarW, Red }, new[] { "fill", "filler" }, new[] { "bg", "back", "frame" }),
            R("badge_green", new[] { new[] { "badge", "notification", "notif", "dot", "counter", "count", "marker" }, Green }, square: true),
            R("badge_red", new[] { new[] { "badge", "notification", "notif", "dot", "counter", "count", "marker" }, Red }, square: true),
            // HUD pieces on the world: the same kinds of picture
            R("hud_capsule", new[] { new[] { "capsule", "pill", "counter", "resource", "currency", "amount", "top" } }, new[] { "hud", "top", "bar" }, new[] { "button", "btn" }),
            R("hud_plus", new[] { new[] { "plus", "add" } }, new[] { "button", "btn", "small" }, square: true),
            R("hud_round_blue", new[] { ButtonW, new[] { "round", "circle", "circular" } }, new[] { "blue" }, square: true),
            R("hud_round_yellow", new[] { ButtonW, new[] { "round", "circle", "circular" } }, new[] { "yellow", "orange", "gold" }, square: true),
            R("hud_square_blue", new[] { ButtonW, new[] { "square", "small", "icon" } }, new[] { "blue" }, square: true),
            R("hud_bar_track", new[] { BarW, new[] { "bg", "back", "background", "track", "frame" } }, new[] { "hud", "level", "exp", "xp" }),
            R("hud_bar_fill_blue", new[] { BarW, Blue }, new[] { "fill", "exp", "xp" }, new[] { "bg", "back" }),
            R("hud_rank", new[] { new[] { "rank", "level", "lvl", "label", "tag" } }, new[] { "small", "bg" }, new[] { "button", "btn", "bar" }),
            R("hud_hex", new[] { new[] { "avatar", "portrait", "profile", "hex", "hexagon" } }, new[] { "frame", "bg" }, square: true),
            R("hud_hex_frame", new[] { new[] { "avatar", "portrait", "profile", "hex", "hexagon" }, new[] { "frame", "border", "ring" } }, square: true),
            // Whole buttons drawn as they are, symbol included (see E.Whole): the close, back and settings buttons.
            R("close_btn", new[] { ButtonW, new[] { "close", "cross", "x" } }, square: true),
            R("back_btn", new[] { new[] { "back", "previous", "prev", "left", "return" } }, new[] { "button", "btn" }, square: true),
            R("settings_btn", new[] { ButtonW, new[] { "setting", "settings", "gear", "options" } }, square: true),
            R("toggle_on", new[] { new[] { "toggle", "switch" }, new[] { "on" } }),
            R("toggle_off", new[] { new[] { "toggle", "switch" }, new[] { "off" } }),
        };

        /// <summary>Interface glyphs the kit may draw (resource, building and villager pictures stay the game's painted ones).</summary>
        static readonly Dictionary<string, string[]> IconWords = new Dictionary<string, string[]>
        {
            ["close"] = new[] { "close", "cross", "x", "cancel", "exit" },
            ["back"] = new[] { "back", "return", "left", "previous", "prev" },
            ["next"] = new[] { "next", "right", "forward" },
            ["check"] = new[] { "check", "ok", "confirm", "tick", "accept", "yes", "done" },
            ["plus"] = new[] { "plus", "add" },
            ["minus"] = new[] { "minus", "subtract", "remove" },
            ["settings"] = new[] { "settings", "setting", "gear", "cog", "options", "option" },
            ["info"] = new[] { "info", "information", "help", "question" },
            ["lock"] = new[] { "lock", "locked", "padlock" },
            ["home"] = new[] { "home", "house" },
            ["bell"] = new[] { "bell", "notification", "notifications" },
            ["alert"] = new[] { "alert", "warning", "exclamation", "attention" },
            ["user"] = new[] { "user", "profile", "avatar", "account", "player" },
            ["trophy"] = new[] { "trophy", "cup", "achievement", "achievements" },
            ["clock"] = new[] { "clock", "time", "timer", "hourglass" },
            ["trash"] = new[] { "trash", "delete", "bin" },
            ["world"] = new[] { "world", "map", "globe" },
            ["goals"] = new[] { "quest", "quests", "goal", "goals", "mission", "missions", "task", "tasks" },
        };

        // MARK: - Entry points

        [MenuItem("FOUNDERS/Adventure UI Kit/Apply (pick pictures, write AdventureUiKit.txt)")]
        public static void ApplyMenu()
        {
            var report = Apply(writeMap: true);
            Debug.Log(report);
            EditorUtility.DisplayDialog("Adventure UI Kit", report.Length > 1800 ? report.Substring(0, 1800) + "\n…(see Console)" : report, "OK");
        }

        [MenuItem("FOUNDERS/Adventure UI Kit/Remove from the game (use the game's own look)")]
        public static void RemoveMenu()
        {
            if (AssetDatabase.IsValidFolder(OutRoot.TrimEnd('/'))) AssetDatabase.DeleteAsset(OutRoot.TrimEnd('/'));
            Debug.Log("[FOUNDERS] Adventure UI Kit copies removed; the game draws its own skins.");
        }

        /// <summary>Batch: Unity -batchmode -executeMethod FoundersEditor.AdventureUiKit.ApplyBatch -quit</summary>
        public static void ApplyBatch() => Debug.Log(Apply(writeMap: false));

        /// <summary>Copies the kit's pictures for the game (nothing when the kit is not in the project). Returns a report.</summary>
        public static string Apply(bool writeMap)
        {
            var sb = new StringBuilder("[FOUNDERS] Adventure UI Kit\n");
            var map = ReadMap(out var folderOverride);
            var folders = FindKitFolders(folderOverride);
            if (folders.Count == 0)
            {
                if (AssetDatabase.IsValidFolder(OutRoot.TrimEnd('/'))) AssetDatabase.DeleteAsset(OutRoot.TrimEnd('/'));
                sb.Append("  not in the project (import it from the Package Manager → My Assets; the folder name must contain \"Adventure\" or be set with folder = … in ")
                  .Append(MapFile).Append("). The game uses its own look.");
                return sb.ToString();
            }
            sb.Append("  found in: ").Append(string.Join(", ", folders)).Append('\n');
            var pics = FindPictures(folders);
            sb.Append("  ").Append(pics.Count).Append(" pictures\n");

            var chosen = new List<(string key, Pic pic)>();
            foreach (var role in Roles)
            {
                string key = "skin:" + role.Name;
                var p = map.TryGetValue(key, out var forced) ? Lookup(forced, pics, sb) ?? (forced == "none" ? null : Best(role, pics)) : Best(role, pics);
                if (p != null) chosen.Add((key, p));
            }
            foreach (var kv in IconWords)
            {
                string key = "icon:" + kv.Key;
                var p = map.TryGetValue(key, out var forced) ? Lookup(forced, pics, sb) ?? (forced == "none" ? null : BestIcon(kv.Value, pics)) : BestIcon(kv.Value, pics);
                if (p != null) chosen.Add((key, p));
            }

            // Fresh copies every time, so a corrected choice never leaves an old picture behind.
            if (AssetDatabase.IsValidFolder(OutRoot.TrimEnd('/'))) AssetDatabase.DeleteAsset(OutRoot.TrimEnd('/'));
            Directory.CreateDirectory(OutRoot + "Skin");
            Directory.CreateDirectory(OutRoot + "Icons");
            var slices = new StringBuilder("# name left top right bottom light|dark   (pixels; written by FoundersEditor.AdventureUiKit)\n");
            var copied = new List<string>();
            foreach (var (key, pic) in chosen)
            {
                bool skin = key.StartsWith("skin:");
                string name = key.Substring(5);
                string dst = Copy(pic, OutRoot + (skin ? "Skin/" : "Icons/") + name);
                if (dst == null) { sb.Append("  ").Append(key).Append(": could not read ").Append(pic.Id).Append('\n'); continue; }
                copied.Add(dst);
                if (skin)
                {
                    var (l, t, r, b) = Borders(pic);
                    bool? light = MiddleIsLight(dst);
                    slices.Append(name).Append(' ').Append(l).Append(' ').Append(t).Append(' ').Append(r).Append(' ').Append(b);
                    if (light != null) slices.Append(light.Value ? " light" : " dark");
                    slices.Append('\n');
                }
                sb.Append("  ").Append(key.PadRight(22)).Append(" ← ").Append(pic.Id).Append('\n');
            }
            File.WriteAllText(OutRoot + "Skin/slices.txt", slices.ToString());
            AssetDatabase.Refresh();
            foreach (var dst in copied) ConfigureImport(dst);

            var missing = Roles.Select(r => "skin:" + r.Name).Concat(IconWords.Keys.Select(k => "icon:" + k))
                .Where(k => chosen.All(c => c.key != k)).ToList();
            if (missing.Count > 0) sb.Append("  game's own look kept for: ").Append(string.Join(", ", missing)).Append('\n');
            if (writeMap) WriteMap(chosen, missing, folders);
            sb.Append("  ").Append(chosen.Count).Append(" pictures used. Fix any choice in ").Append(MapFile).Append(" and run Apply again.");
            return sb.ToString();
        }

        // MARK: - Finding and scoring pictures

        static List<string> FindKitFolders(string forced)
        {
            if (!string.IsNullOrEmpty(forced)) return AssetDatabase.IsValidFolder(forced) ? new List<string> { forced } : new List<string>();
            var skip = new[] { "Assets/Custom", "Assets/Generated", "Assets/Art", "Assets/Scripts", "Assets/Editor" };
            return Directory.GetDirectories("Assets", "*", SearchOption.AllDirectories)
                .Select(d => d.Replace('\\', '/'))
                .Where(d => !skip.Any(s => d.StartsWith(s)))
                .Where(d => Path.GetFileName(d).IndexOf("adventure", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(d => !Path.GetDirectoryName(d).Replace('\\', '/').Split('/').Any(p => p.IndexOf("adventure", StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }

        sealed class Pic
        {
            public string Path;
            public string Sprite;              // one sprite of a sprite sheet, or null for the whole picture
            public Rect Rect;                  // its pixels in the sheet
            public Vector4 Border;             // its 9-slice border (x left, y bottom, z right, w top)
            public HashSet<string> Words;      // file (or sprite) name words
            public HashSet<string> FolderWords;
            public int W, H;
            /// <summary>"Assets/Kit/Ui.png" or "Assets/Kit/Sheet.png#Button_Green".</summary>
            public string Id => Sprite == null ? Path : Path + "#" + Sprite;
        }

        /// <summary>The picture a line of AdventureUiKit.txt names (null for "none", or when it is not in the kit: then the automatic pick is used).</summary>
        static Pic Lookup(string id, List<Pic> pics, StringBuilder report)
        {
            if (id == "none") return null;
            var p = pics.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (p == null) report.Append("  not in the kit, picked automatically instead: ").Append(id).Append('\n');
            return p;
        }

        static List<Pic> FindPictures(List<string> folders)
        {
            var list = new List<Pic>();
            foreach (var f in folders)
                foreach (var file in Directory.GetFiles(f, "*", SearchOption.AllDirectories))
                {
                    string p = file.Replace('\\', '/');
                    if (!ImageExt.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())) continue;
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    string rel = p.Substring(f.Length);
                    var folderWords = Words((System.IO.Path.GetDirectoryName(rel) ?? "") + " " + System.IO.Path.GetFileNameWithoutExtension(p));
                    if (ti != null && ti.spriteImportMode == SpriteImportMode.Multiple && ti.spritesheet != null && ti.spritesheet.Length > 0)
                    {
                        // A sprite sheet: every sprite is its own candidate, named by the sprite.
                        foreach (var m in ti.spritesheet)
                            list.Add(new Pic
                            {
                                Path = p, Sprite = m.name, Rect = m.rect, Border = m.border,
                                Words = Words(m.name), FolderWords = folderWords,
                                W = Mathf.RoundToInt(m.rect.width), H = Mathf.RoundToInt(m.rect.height),
                            });
                        continue;
                    }
                    list.Add(new Pic
                    {
                        Path = p,
                        Border = ti != null ? ti.spriteBorder : Vector4.zero,
                        Words = Words(System.IO.Path.GetFileNameWithoutExtension(p)),
                        FolderWords = Words(System.IO.Path.GetDirectoryName(rel) ?? ""),
                        W = tex != null ? tex.width : 0,
                        H = tex != null ? tex.height : 0,
                    });
                }
            return list;
        }

        /// <summary>Lower-case words of a name: "BtnGreen_Big-01" → btn, green, big, 01 (and digits dropped words).</summary>
        static HashSet<string> Words(string s)
        {
            s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
            s = Regex.Replace(s, "([A-Za-z])([0-9])", "$1 $2");
            return new HashSet<string>(Regex.Split(s.ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length > 0));
        }

        static bool Has(Pic p, string[] words, bool folders = true) =>
            words.Any(w => p.Words.Contains(w) || (folders && p.FolderWords.Contains(w)));

        static Pic Best(Role role, List<Pic> pics)
        {
            Pic best = null;
            float bestScore = float.MinValue;
            foreach (var p in pics)
            {
                if (!role.All.All(g => Has(p, g))) continue;
                if (role.Not.Any(w => p.Words.Contains(w))) continue;
                if (role.Square && p.W > 0 && (p.W > p.H * 1.6f || p.H > p.W * 1.6f)) continue;
                float score = role.Bonus.Count(w => p.Words.Contains(w)) * 2 + role.Bonus.Count(w => p.FolderWords.Contains(w));
                score += role.All.Sum(g => g.Count(w => p.Words.Contains(w))) * 1.5f; // in the name beats in the folder
                score -= p.Words.Count * 0.15f;                                       // simpler names are the base piece
                if (p.Words.Any(w => w == "1" || w == "01" || w == "001")) score += 0.5f;
                if (score > bestScore || (score == bestScore && string.CompareOrdinal(p.Id, best.Id) < 0)) { best = p; bestScore = score; }
            }
            return best;
        }

        static Pic BestIcon(string[] words, List<Pic> pics)
        {
            Pic best = null;
            float bestScore = float.MinValue;
            foreach (var p in pics)
            {
                if (!words.Any(w => p.Words.Contains(w))) continue;
                if (p.W > 256 || p.H > 256) continue; // a glyph, not a panel that happens to share a word
                if (p.W > 0 && (p.W > p.H * 1.4f || p.H > p.W * 1.4f)) continue;
                float score = (p.Words.Contains("icon") || p.FolderWords.Contains("icon") || p.FolderWords.Contains("icons")) ? 3 : 0;
                if (Has(p, ButtonW)) score -= 2; // a bare glyph beats a whole button
                score -= p.Words.Count * 0.2f;
                if (score > bestScore || (score == bestScore && string.CompareOrdinal(p.Id, best.Id) < 0)) { best = p; bestScore = score; }
            }
            return best;
        }

        // MARK: - Slices, brightness, import settings

        /// <summary>The kit's own 9-slice border (Sprite Editor), or about a third of the picture's smaller side.</summary>
        static (int l, int t, int r, int b) Borders(Pic p)
        {
            var v = p.Border; // x left, y bottom, z right, w top
            if (v != Vector4.zero)
                return (Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.w), Mathf.RoundToInt(v.z), Mathf.RoundToInt(v.y));
            int s = p.W > 0 ? Mathf.RoundToInt(Mathf.Min(p.W, p.H) * 0.32f) : 16;
            return (s, s, s, s);
        }

        /// <summary>Copies a picture (or cuts one sprite out of its sheet) to dst + extension. Returns the path or null.</summary>
        static string Copy(Pic p, string dstNoExt)
        {
            if (p.Sprite == null)
            {
                string dst = dstNoExt + Path.GetExtension(p.Path).ToLowerInvariant();
                File.Copy(p.Path, dst, true);
                return dst;
            }
            // Only PNG/JPG sheets can be cut here (LoadImage); others are skipped.
            var ext = Path.GetExtension(p.Path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return null;
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!sheet.LoadImage(File.ReadAllBytes(p.Path))) return null;
                var r = p.Rect;
                int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, sheet.width - 1), y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, sheet.height - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, sheet.width - x), h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, sheet.height - y);
                var cut = new Texture2D(w, h, TextureFormat.RGBA32, false);
                cut.SetPixels(sheet.GetPixels(x, y, w, h));
                cut.Apply();
                string dst = dstNoExt + ".png";
                File.WriteAllBytes(dst, cut.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(cut);
                return dst;
            }
            finally { UnityEngine.Object.DestroyImmediate(sheet); }
        }

        /// <summary>Is the middle of the picture light (so text on it is dark)? null when it cannot be read.</summary>
        static bool? MiddleIsLight(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return null;
            var t = new Texture2D(2, 2);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(path))) return null;
                float sum = 0; int n = 0;
                for (int y = t.height / 3; y < t.height * 2 / 3; y += Mathf.Max(1, t.height / 24))
                    for (int x = t.width / 3; x < t.width * 2 / 3; x += Mathf.Max(1, t.width / 24))
                    {
                        var c = t.GetPixel(x, y);
                        if (c.a < 0.5f) continue;
                        sum += c.r * 0.299f + c.g * 0.587f + c.b * 0.114f; n++;
                    }
                return n == 0 ? (bool?)null : sum / n > 0.55f;
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }

        static void ConfigureImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.isReadable = false;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        // MARK: - AdventureUiKit.txt

        static Dictionary<string, string> ReadMap(out string folder)
        {
            folder = null;
            var d = new Dictionary<string, string>();
            if (!File.Exists(MapFile)) return d;
            foreach (var raw in File.ReadAllLines(MapFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k == "folder") folder = v.TrimEnd('/');
                else d[k] = v;
            }
            return d;
        }

        static void WriteMap(List<(string key, Pic pic)> chosen, List<string> missing, List<string> folders)
        {
            var sb = new StringBuilder();
            sb.Append("# Adventure UI Kit → game skins and icons (FOUNDERS → Adventure UI Kit → Apply writes this).\n");
            sb.Append("# Change a path to use another kit picture, or write none to keep the game's own. Then Apply again.\n");
            sb.Append("# The kit is in: ").Append(string.Join(", ", folders)).Append('\n');
            foreach (var (k, p) in chosen) sb.Append(k).Append(" = ").Append(p.Id).Append('\n');
            foreach (var k in missing) sb.Append("# ").Append(k).Append(" = (nothing found: the game's own)\n");
            File.WriteAllText(MapFile, sb.ToString());
            AssetDatabase.ImportAsset(MapFile);
        }
    }
}
