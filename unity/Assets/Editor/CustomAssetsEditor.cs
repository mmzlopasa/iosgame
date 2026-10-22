using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Founders;
using Founders.Core;
using Founders.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Grid = Founders.Grid;

namespace FoundersEditor
{
    /// <summary>
    /// Import settings for the player's own art in Assets/Custom (see Assets/Custom/README.md):
    /// models keep their file units (buildings are fitted to their plot at runtime), characters in
    /// People get a Humanoid avatar and looping clips that stay on the spot, textures get UI or
    /// 3D settings by folder, normal maps and data maps are recognised by their suffix.
    /// </summary>
    public class CustomAssetPostprocessor : AssetPostprocessor
    {
        public const string Folder = "Assets/Custom/";
        public const string ResourcesFolder = "Assets/Custom/Resources/" + CustomAssets.Root + "/";
        /// <summary>A copy of one of the game's people, imported as Humanoid, whose clips any humanoid character can play.</summary>
        public const string BuiltinCopy = "Assets/Generated/Resources/" + CustomAssets.BuiltinClipsPath + ".fbx";
        public const string BuiltinSource = "Assets/Art/Resources/Models/Person_Farmer_Male.fbx";

        public override uint GetVersion() => 1;

        /// <summary>Path below Assets/Custom/Resources/Custom/ ("People/Bob.fbx"), or null outside it.</summary>
        public static string Rel(string path) => path.StartsWith(ResourcesFolder) ? path.Substring(ResourcesFolder.Length) : null;

        bool IsPeople => assetPath == BuiltinCopy || (Rel(assetPath)?.StartsWith("People/") ?? false);
        bool IsSharedAnimation => Rel(assetPath)?.StartsWith("People/Animations/") ?? false;

        void OnPreprocessModel()
        {
            bool builtin = assetPath == BuiltinCopy;
            if (!assetPath.StartsWith(Folder) && !builtin) return;
            var mi = (ModelImporter)assetImporter;
            bool people = IsPeople;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            if (!builtin) mi.bakeAxisConversion = true; // Blender exports stand upright with no rotation on the root
            mi.isReadable = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.addCollider = false;
            mi.importBlendShapes = people;
            mi.meshCompression = people ? ModelImporterMeshCompression.Off : ModelImporterMeshCompression.Low;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            // Materials (and textures embedded in the file) become URP/Lit materials inside the model.
            mi.materialImportMode = builtin || IsSharedAnimation ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            if (people)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.optimizeGameObjects = false;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsPeople) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            // One clip per file in People/Animations: the clip takes the file's name ("Walk.fbx" → Walk).
            bool rename = IsSharedAnimation && clips.Length == 1;
            foreach (var c in clips)
            {
                if (rename) c.name = file;
                c.loopTime = true;
                // The villager moves itself: travel is dropped, turning and height stay in the pose.
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            string rel = Rel(assetPath) ?? "";
            string file = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            ti.isReadable = false;
            if (rel.StartsWith("UI/") || rel.StartsWith("Icons/"))
            {
                // Pictures for the interface: exact pixels, no power-of-two rescale, real transparency.
                bool skin = rel.StartsWith("UI/Skin/") || rel.StartsWith("UI/Icons/");
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = rel.StartsWith("Icons/"); // building pictures are shown small too
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = rel.StartsWith("UI/Images/") ? 4096 : skin ? 2048 : 1024;
                ti.textureCompression = skin ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
                return;
            }
            bool terrain = rel.StartsWith("Textures/Terrain/");
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = terrain ? 1024 : 2048;
            ti.textureCompression = TextureImporterCompression.Compressed; // ASTC on iPhone
            if (IsNormalMap(file)) ti.textureType = TextureImporterType.NormalMap;
            else if (IsDataMap(file)) ti.sRGBTexture = false;
            else ti.alphaSource = TextureImporterAlphaSource.FromInput;
        }

        static readonly string[] NormalSuffixes = { "_n", "_nrm", "_norm", "_normal", "_normals", "_normalmap", "_nor", "_normal_gl", "_nor_gl", "_normal_dx", "_nor_dx", "-normal" };
        static readonly string[] DataSuffixes = { "_metallic", "_metalness", "_metal", "_roughness", "_rough", "_ao", "_occlusion", "_mask", "_orm", "_arm", "_height", "_disp", "_displacement", "_smoothness", "_gloss" };

        public static bool IsNormalMap(string file) => NormalSuffixes.Any(s => file.EndsWith(s));
        static bool IsDataMap(string file) => DataSuffixes.Any(s => file.EndsWith(s));
    }

    /// <summary>
    /// "FOUNDERS/Check custom assets": what the game will use from Assets/Custom, names that match
    /// nothing (typos), sizes, missing animations. Also prepares the folder for a build
    /// (<see cref="Prepare"/>): materials of other pipelines are turned into URP/Lit, and the game's
    /// villager clips are made available to humanoid characters.
    /// </summary>
    public static class CustomAssetsEditor
    {
        static readonly string[] ModelExtensions = { ".fbx", ".prefab", ".obj", ".dae", ".3ds", ".dxf" };
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".hdr" };
        static readonly string[] Ignored = { ".meta", ".md", ".txt", ".gitkeep", ".mat" };
        static readonly string[] Unsupported = { ".blend", ".gltf", ".glb", ".max", ".ma", ".mb", ".c4d", ".skp", ".usdz", ".zip", ".rar", ".7z" };

        /// <summary>Batch mode: Unity -batchmode -projectPath unity -executeMethod FoundersEditor.CustomAssetsEditor.CheckBatch -quit</summary>
        public static void CheckBatch()
        {
            Prepare();
            Debug.Log(Check());
        }

        [MenuItem("FOUNDERS/Check custom assets")]
        public static void CheckMenu()
        {
            Prepare();
            var report = Check();
            Debug.Log(report);
            int warnings = report.Split('\n').Count(l => l.Contains("WARNING") || l.Contains("UNKNOWN"));
            EditorUtility.DisplayDialog("Custom assets", warnings == 0
                ? "No problems found. The full list is in the Console."
                : $"{warnings} problem(s) found. See the Console for the full report.", "OK");
        }

        // MARK: - Prepare (called by the build)

        /// <summary>Converts non-URP materials under Assets/Custom and adds or removes the humanoid copy of the game's clips.</summary>
        public static void Prepare()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Custom")) return;
            ConvertMaterials();
            bool anyPeople = Files("People").Any(f => !f.StartsWith("People/Animations/") && IsModel(f));
            bool exists = File.Exists(CustomAssetPostprocessor.BuiltinCopy);
            if (anyPeople && !exists && File.Exists(CustomAssetPostprocessor.BuiltinSource))
            {
                EnsureFolder(Path.GetDirectoryName(CustomAssetPostprocessor.BuiltinCopy).Replace('\\', '/'));
                AssetDatabase.CopyAsset(CustomAssetPostprocessor.BuiltinSource, CustomAssetPostprocessor.BuiltinCopy);
                AssetDatabase.ImportAsset(CustomAssetPostprocessor.BuiltinCopy, ImportAssetOptions.ForceUpdate);
            }
            else if (!anyPeople && exists) AssetDatabase.DeleteAsset(CustomAssetPostprocessor.BuiltinCopy);
        }

        static void ConvertMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Custom" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || RendersInUrp(m)) continue;
                var urp = CustomAssets.ToUrp(m);
                string was = m.shader != null ? m.shader.name : "none";
                m.shader = urp.shader;
                m.CopyPropertiesFromMaterial(urp);
                m.shaderKeywords = urp.shaderKeywords;
                m.renderQueue = urp.renderQueue;
                EditorUtility.SetDirty(m);
                Debug.Log($"[FOUNDERS] custom: material {path} converted from {was} to URP/Lit");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>URP draws the shader: a URP or Shader Graph shader, or one with a pass URP runs (UniversalForward, or untagged).</summary>
        public static bool RendersInUrp(Material m)
        {
            if (CustomAssets.RendersInUrp(m)) return true;
            if (m == null || m.shader == null || !m.shader.isSupported) return false;
            var tag = new ShaderTagId("LightMode");
            for (int i = 0; i < m.shader.passCount; i++)
            {
                var mode = m.shader.FindPassTagValue(i, tag).name;
                if (string.IsNullOrEmpty(mode) || mode == "SRPDefaultUnlit" || mode.StartsWith("Universal")) return true;
            }
            return false;
        }

        // MARK: - Check

        /// <summary>The report: one line per file, then a summary.</summary>
        public static string Check()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[FOUNDERS] Custom assets check (Assets/Custom/Resources/Custom)");
            if (!AssetDatabase.IsValidFolder("Assets/Custom/Resources/Custom"))
            {
                sb.AppendLine("  Folder missing: nothing custom, the game uses its own art.");
                return sb.ToString();
            }
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/content.json");
            var content = json != null ? GameContent.Load(json.text) : null;
            int found = 0, problems = 0;
            void Ok(string s) { found++; sb.AppendLine("  OK       " + s); }
            void Warn(string s) { problems++; sb.AppendLine("  WARNING  " + s); }
            void Note(string s) => sb.AppendLine("  INFO     " + s);
            void Unknown(string file, string name, IEnumerable<string> valid)
            {
                problems++;
                var near = Nearest(name, valid);
                sb.AppendLine($"  UNKNOWN  {file}: \"{name}\" matches nothing" + (near != null ? $" (did you mean \"{near}\"?)" : "") + " - ignored by the game");
            }

            // Valid names.
            var buildingKeys = new Dictionary<string, (Vector2 metres, int maxLevel, string names)>();
            var decorationKeys = new Dictionary<string, string>();
            if (content != null)
            {
                foreach (var b in content.Buildings)
                {
                    foreach (var (sprite, from) in new[] { (b.Sprite, 1) }.Concat(b.Looks.Select(l => (l.Sprite, l.Level))))
                    {
                        var m = new Vector2(b.Footprint.Width, b.Footprint.Height) * Grid.CellSize;
                        string use = $"{b.Name} L{from}+";
                        if (buildingKeys.TryGetValue(sprite, out var e))
                            buildingKeys[sprite] = (e.metres.x * e.metres.y <= m.x * m.y ? e.metres : m, Math.Max(e.maxLevel, b.MaxLevel), e.names + ", " + use);
                        else buildingKeys[sprite] = (m, b.MaxLevel, use);
                    }
                }
                foreach (var d in content.Decorations)
                    if (!d.IsRoad || d.IsBridge) decorationKeys[d.Sprite] = d.Name;
            }
            var iconKeys = new HashSet<string>(buildingKeys.Keys);
            if (content != null) foreach (var d in content.Decorations) iconKeys.Add("deco_" + d.Sprite);
            var uiIcons = new HashSet<string>(Directory.Exists("Assets/Art/UI/Resources/UI/Icons")
                ? Directory.GetFiles("Assets/Art/UI/Resources/UI/Icons", "*.png").Select(Path.GetFileNameWithoutExtension) : Enumerable.Empty<string>());
            // Optional icons the game only uses when the player brings them (it falls back to a built-in one).
            foreach (var kind in new[] { "general", "construction", "research", "exploration" }) uiIcons.Add("speedup_" + kind);
            if (content != null) foreach (var law in content.Laws) uiIcons.Add("law_" + law.Id);
            var images = new HashSet<string> { "loading_bg", "logo_mark", "glow" };
            var terrain = new HashSet<string> { "Grass", "Mud", "Sand", "RockGround" };

            foreach (var file in Files(""))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string name = Path.GetFileNameWithoutExtension(file);
                string dir = Path.GetDirectoryName(file).Replace('\\', '/');
                string asset = CustomAssetPostprocessor.ResourcesFolder + file;
                if (Ignored.Contains(ext) || Path.GetFileName(file).StartsWith(".")) continue;
                if (Unsupported.Contains(ext)) { Warn($"{file}: {ext} files are not read by Unity here - export as .fbx (models) or .png (pictures)"); continue; }
                bool model = ModelExtensions.Contains(ext), image = ImageExtensions.Contains(ext);

                if ((dir == "Buildings" || dir.StartsWith("Buildings/")) && model)
                {
                    var (key, level) = CustomAssets.ParseName(name);
                    if (!buildingKeys.TryGetValue(key, out var info)) { Unknown(file, key, buildingKeys.Keys); continue; }
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    if (go == null) { Warn($"{file}: Unity could not import it as a model"); continue; }
                    var size = MeasureModel(go, out int tris, out var pink);
                    if (size == Vector3.zero) { Warn($"{file}: has no visible mesh"); continue; }
                    float fit = Mathf.Min(info.metres.x * 0.94f / Mathf.Max(0.001f, size.x), info.metres.y * 0.94f / Mathf.Max(0.001f, size.z));
                    Ok($"{file}: building \"{key}\"{(level > 1 ? $" from level {level}" : "")} ({info.names}); file size {Fmt(size)} m, plot {info.metres.x:0.#}x{info.metres.y:0.#} m -> scaled x{fit:0.###}, {tris} triangles");
                    if (level > info.maxLevel) Warn($"{file}: level {level} is never reached (max level {info.maxLevel})");
                    if (fit < 0.02f || fit > 50f) Warn($"{file}: scale x{fit:0.####} - the model's units are probably wrong (export in metres, 1 unit = 1 m)");
                    if (size.y * fit > 30f) Warn($"{file}: will stand {size.y * fit:0.#} m tall on its plot - very tall and thin?");
                    if (tris > 60000) Warn($"{file}: {tris} triangles - heavy for a phone (keep buildings under ~20000)");
                    foreach (var p in pink) Warn($"{file}: material \"{p}\" is not a URP material - converted to URP/Lit at runtime (check colours)");
                }
                else if ((dir == "Decorations" || dir.StartsWith("Decorations/")) && model)
                {
                    var (key, _) = CustomAssets.ParseName(name);
                    if (!decorationKeys.TryGetValue(key, out var dname)) { Unknown(file, key, decorationKeys.Keys); continue; }
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    if (go == null) { Warn($"{file}: Unity could not import it as a model"); continue; }
                    var size = MeasureModel(go, out int tris, out var pink);
                    if (size == Vector3.zero) { Warn($"{file}: has no visible mesh"); continue; }
                    Ok($"{file}: decoration \"{key}\" ({dname}); size {Fmt(size)} m, {tris} triangles");
                    float cell = Grid.CellSize * 0.95f;
                    if (Mathf.Max(size.x, size.z) > cell && key != "wooden_bridge") Warn($"{file}: wider than its {Grid.CellSize} m cell - shrunk to fit");
                    if (Mathf.Max(size.x, size.y, size.z) < 0.1f) Warn($"{file}: only {Fmt(size)} m - units probably wrong (1 unit = 1 m)");
                    if (tris > 15000) Warn($"{file}: {tris} triangles - heavy for a decoration (keep under ~5000)");
                    foreach (var p in pink) Warn($"{file}: material \"{p}\" is not a URP material - converted to URP/Lit at runtime");
                }
                else if (dir == "People" && model) CheckPerson(file, asset, Ok, Warn, Note);
                else if (dir == "People/Animations" && model)
                {
                    var clips = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
                    var names = clips.Select(c => CustomAssets.ClipNameOf(c.name)).Where(n => n != null).Distinct().ToList();
                    if (names.Count == 0) Unknown(file, name, CustomAssets.ClipNames);
                    else Ok($"{file}: animation(s) {string.Join(", ", names)} for every custom person");
                    var importer = AssetImporter.GetAtPath(asset) as ModelImporter;
                    if (importer != null && importer.animationType != ModelImporterAnimationType.Human) Warn($"{file}: not imported as Humanoid");
                }
                else if (dir == "Icons" && image) CheckImage(file, asset, name, iconKeys, Ok, Warn, Unknown, square: true);
                else if (dir == "UI/Icons" && image) CheckImage(file, asset, name, uiIcons, Ok, Warn, Unknown, square: true);
                else if (dir == "UI/Skin" && image) CheckSkin(file, asset, name, Ok, Warn, Unknown);
                else if (dir == "UI/Images" && image) CheckImage(file, asset, name, images, Ok, Warn, Unknown, square: false);
                else if (dir == "Textures/Terrain" && image)
                {
                    string layer = name.EndsWith("_normal") ? name.Substring(0, name.Length - 7) : name;
                    if (!terrain.Contains(layer)) Unknown(file, layer, terrain);
                    else Ok($"{file}: ground \"{layer}\"{(name != layer ? " normal map" : "")}");
                }
                else if (dir.StartsWith("Buildings") || dir.StartsWith("Decorations") || dir.StartsWith("People"))
                {
                    if (image) continue; // textures next to their models
                    Warn($"{file}: not used - people go directly in People/ and their clips directly in People/Animations/ (no other sub-folders)");
                }
                else Warn($"{file}: not used - unknown folder or file type (see Assets/Custom/README.md)");
            }
            if (Files("People").Any(f => IsModel(f) && !f.StartsWith("People/Animations/")))
            {
                var builtin = AssetDatabase.LoadAllAssetsAtPath(CustomAssetPostprocessor.BuiltinCopy).OfType<AnimationClip>()
                    .Select(c => CustomAssets.ClipNameOf(c.name)).Where(n => n != null).Distinct().ToList();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(CustomAssetPostprocessor.BuiltinCopy).OfType<Avatar>().FirstOrDefault();
                if (builtin.Count > 0 && (avatar == null || !avatar.isValid || !avatar.isHuman))
                {
                    Warn("the game's own clips could not be made Humanoid - custom people need their own Idle and Walk (People/Animations)");
                    builtin.Clear();
                }
                sb.AppendLine(builtin.Count > 0
                    ? $"  INFO     the game's own clips are available to humanoid characters: {string.Join(", ", builtin)}"
                    : "  INFO     the game's own clips are not available (run FOUNDERS/Generate scene and materials) - characters need their own Idle and Walk");
            }
            sb.AppendLine($"[FOUNDERS] Custom assets: {found} used, {problems} warning(s).");
            return sb.ToString();
        }

        static void CheckPerson(string file, string asset, Action<string> ok, Action<string> warn, Action<string> note)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if (go == null) { warn($"{file}: Unity could not import it as a model"); return; }
            if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) { warn($"{file}: no skinned (rigged) mesh - not used as a person"); return; }
            var importer = AssetImporter.GetAtPath(asset) as ModelImporter;
            var animator = go.GetComponentInChildren<Animator>(true);
            bool human = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
            if (!human) warn($"{file}: no valid Humanoid avatar (rig not recognised) - only clips inside this file can play; check the rig in the Inspector (Rig > Humanoid > Configure)");
            var own = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"));
            var ownNames = own.Select(c => CustomAssets.ClipNameOf(c.name)).Where(n => n != null).Distinct().ToList();
            var shared = Files("People/Animations").Where(IsModel)
                .SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(CustomAssetPostprocessor.ResourcesFolder + f).OfType<AnimationClip>())
                .Select(c => CustomAssets.ClipNameOf(c.name)).Where(n => n != null);
            var builtin = human ? AssetDatabase.LoadAllAssetsAtPath(CustomAssetPostprocessor.BuiltinCopy).OfType<AnimationClip>()
                .Select(c => CustomAssets.ClipNameOf(c.name)).Where(n => n != null) : Enumerable.Empty<string>();
            var all = new HashSet<string>(ownNames.Concat(human ? shared : Enumerable.Empty<string>()).Concat(builtin));
            var described = CustomAssets.Describe(go);
            string role = described.Role == "" ? "any job" : described.Role;
            string gender = described.Gender == "" ? "any gender" : described.Gender;
            var missing = CustomAssets.ClipNames.Where(n => !all.Contains(n)).ToList();
            ok($"{file}: person ({role}, {gender}, {(human ? "Humanoid" : importer != null ? importer.animationType.ToString() : "prefab")}); own clips: {(ownNames.Count > 0 ? string.Join(", ", ownNames) : "none")}");
            if (!all.Contains("Idle") || !all.Contains("Walk"))
                warn($"{file}: no Idle and/or Walk animation - this person is NOT used (add People/Animations/Idle.fbx and Walk.fbx, or clips named Idle and Walk in the file)");
            else if (missing.Count > 0)
                note($"{file}: no clip for {string.Join(", ", missing)} - plays the nearest one instead (Chop -> Work -> Idle, Jog -> Walk ...)");
        }

        static void CheckImage(string file, string asset, string name, HashSet<string> valid, Action<string> ok, Action<string> warn,
                               Action<string, string, IEnumerable<string>> unknown, bool square)
        {
            if (!valid.Contains(name)) { unknown(file, name, valid); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            if (tex == null) { warn($"{file}: Unity could not import it as a picture"); return; }
            ok($"{file}: {tex.width}x{tex.height}");
            if (square && Mathf.Abs(tex.width - tex.height) > Mathf.Max(tex.width, tex.height) * 0.1f) warn($"{file}: not square - it will look squashed");
            if (square && Mathf.Max(tex.width, tex.height) < 96) warn($"{file}: only {tex.width}px - blurry on an iPhone (use 128 or 256)");
        }

        static void CheckSkin(string file, string asset, string name, Action<string> ok, Action<string> warn, Action<string, string, IEnumerable<string>> unknown)
        {
            if (!UiGenerated.Skins.TryGetValue(name, out var s)) { unknown(file, name, UiGenerated.Skins.Keys); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/UI/Resources/UI/Skin/" + name + ".png");
            if (tex == null) { warn($"{file}: Unity could not import it as a picture"); return; }
            ok($"{file}: skin {tex.width}x{tex.height}" + (original != null ? $" (game's {original.width}x{original.height})" : ""));
            if (original != null)
            {
                float rx = tex.width / (float)original.width, ry = tex.height / (float)original.height;
                if (Mathf.Abs(rx - ry) > 0.05f * Mathf.Max(rx, ry))
                    warn($"{file}: proportions differ from the game's {original.width}x{original.height} - the corners will be cut in the wrong place");
            }
        }

        // MARK: - Helpers

        /// <summary>Files below Assets/Custom/Resources/Custom/&lt;sub&gt; as paths relative to it.</summary>
        static IEnumerable<string> Files(string sub)
        {
            string root = CustomAssetPostprocessor.ResourcesFolder.TrimEnd('/');
            string dir = sub == "" ? root : root + "/" + sub;
            if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/').Substring(root.Length + 1))
                .Where(f => !f.EndsWith(".meta"))
                .OrderBy(f => f, StringComparer.Ordinal);
        }

        static bool IsModel(string file) => ModelExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());

        /// <summary>Size in metres (as imported), triangle count and the names of materials URP cannot draw.</summary>
        static Vector3 MeasureModel(GameObject go, out int tris, out List<string> pink)
        {
            tris = 0;
            pink = new List<string>();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            if (inst == null) inst = UnityEngine.Object.Instantiate(go);
            try
            {
                inst.transform.position = Vector3.zero;
                var holder = new GameObject("Measure").transform;
                inst.transform.SetParent(holder, false);
                var b = CustomAssets.LocalBounds(inst, holder);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                    if (mesh != null) for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)(mesh.GetIndexCount(i) / 3);
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !RendersInUrp(m) && !pink.Contains(m.name)) pink.Add(m.name);
                }
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
                return b.size;
            }
            finally
            {
                if (inst != null) UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        static string Fmt(Vector3 v) => $"{v.x:0.##}x{v.y:0.##}x{v.z:0.##}";

        /// <summary>The closest valid name (edit distance), if it is close enough to be a typo.</summary>
        static string Nearest(string name, IEnumerable<string> valid)
        {
            string best = null;
            int bestD = int.MaxValue;
            foreach (var v in valid)
            {
                int d = Distance(name.ToLowerInvariant(), v.ToLowerInvariant());
                if (d < bestD) { bestD = d; best = v; }
            }
            return best != null && bestD <= Math.Max(3, name.Length / 3) ? best : null;
        }

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
