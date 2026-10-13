using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Founders;
using Founders.Core;
using UnityEditor;
using UnityEngine;

namespace FoundersEditor
{
    /// <summary>
    /// Exports every building and decoration model, assembled exactly as the game does
    /// (<see cref="BuildingModels.Create"/> / <see cref="BuildingModels.Decoration"/>, no tint), as
    /// OBJ + MTL with its textures and a manifest, for the offline Blender renders of the build menu
    /// pictures (tools/art/blender_buildings.py).
    ///
    /// Coordinates: metres, Y up, right-handed: Unity's x is negated (z unchanged), so the street
    /// side of a building (Unity −z) is OBJ −z. Texture tiling/offset is baked into the UVs.
    ///
    /// Headless: Unity -batchmode -quit -projectPath unity
    ///   -executeMethod FoundersEditor.ExportBuildingModels.Export [-exportPath &lt;dir&gt;]
    /// (default dir: build/BuildingModels under the current directory).
    /// </summary>
    public static class ExportBuildingModels
    {
        const string DefaultOut = "build/BuildingModels";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [MenuItem("FOUNDERS/Export building models (OBJ)")]
        public static void Export()
        {
            try
            {
                string outDir = Path.GetFullPath(Arg("-exportPath") ?? DefaultOut);
                Run(outDir);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[FOUNDERS] Export failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>The same list as Thumbnails.Wanted: buildings at each look, then decorations.</summary>
        static List<(string key, string sprite, bool decoration, float metres)> Wanted(GameContent content)
        {
            var list = new List<(string, string, bool, float)>();
            var seen = new HashSet<string>();
            foreach (var b in content.Buildings)
            {
                float metres = Mathf.Max(b.Footprint.Width, b.Footprint.Height) * Founders.Grid.CellSize;
                if (seen.Add(b.Sprite)) list.Add((b.Sprite, b.Sprite, false, metres));
                foreach (var look in b.Looks)
                    if (seen.Add(look.Sprite)) list.Add((look.Sprite, look.Sprite, false, metres));
            }
            foreach (var d in content.Decorations)
                if (seen.Add("deco:" + d.Sprite)) list.Add(("deco:" + d.Sprite, d.Sprite, true, Founders.Grid.CellSize));
            return list;
        }

        static void Run(string outDir)
        {
            // Materials (Assets/Generated/Resources/Materials) are generated, not committed.
            ArtSetup.Run();
            AssetDatabase.Refresh();

            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/content.json");
            if (json == null) throw new System.Exception("Assets/Resources/content.json not found");
            var content = GameContent.Parse(json.text);

            // Only ever clear a previous export (never an arbitrary folder given by mistake).
            if (Directory.Exists(outDir) && File.Exists(Path.Combine(outDir, "manifest.json"))) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(Path.Combine(outDir, "textures"));

            var exporter = new Exporter(outDir);
            var entries = new List<string>();
            foreach (var w in Wanted(content))
            {
                var holder = new GameObject("Export_" + w.key);
                try
                {
                    var model = w.decoration
                        ? BuildingModels.Decoration(w.sprite, holder.transform, null, 0, 0)
                        : BuildingModels.Create(w.sprite, holder.transform, null, w.metres);
                    string entry = exporter.Write(w.key, w.sprite, w.decoration, w.metres, model);
                    if (entry != null) entries.Add(entry);
                    else Debug.Log($"[FOUNDERS] export {w.key}: nothing to draw (road), skipped");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[FOUNDERS] export {w.key} failed: {e}");
                }
                finally
                {
                    Object.DestroyImmediate(holder);
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\n  \"format\": \"founders-building-models/1\",\n");
            sb.Append("  \"units\": \"metres\",\n  \"axes\": \"Y up, right-handed (Unity x negated); street side is -z\",\n");
            sb.Append("  \"unity\": \"").Append(Application.unityVersion).Append("\",\n");
            sb.Append("  \"models\": [\n");
            sb.Append(string.Join(",\n", entries));
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(Path.Combine(outDir, "manifest.json"), sb.ToString());
            Debug.Log($"[FOUNDERS] Exported {entries.Count} models and {exporter.TextureCount} textures to {outDir}");
        }

        /// <summary>Writes OBJ/MTL files and shared textures.</summary>
        sealed class Exporter
        {
            readonly string dir;
            readonly Dictionary<Texture, string> textures = new Dictionary<Texture, string>();
            readonly HashSet<string> textureNames = new HashSet<string>();

            public Exporter(string dir) { this.dir = dir; }
            public int TextureCount => textures.Count;

            static string F(float v) => v.ToString("0.######", Inv);
            static string V3(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";
            static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            static Vector3 ToObj(Vector3 v) => new Vector3(-v.x, v.y, v.z);

            static string Safe(string s)
            {
                var sb = new StringBuilder();
                foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
                return sb.ToString();
            }

            /// <summary>Exports one assembled model; returns its manifest entry, or null if it draws nothing.</summary>
            public string Write(string key, string sprite, bool decoration, float metres, GameObject model)
            {
                string file = key.Replace(':', '_');
                var obj = new StringBuilder();
                var mtl = new StringBuilder();
                obj.Append("# Founders building model '").Append(key).Append("' (exported by FoundersEditor.ExportBuildingModels)\n");
                obj.Append("mtllib ").Append(file).Append(".mtl\n");
                mtl.Append("# Founders materials for '").Append(key).Append("'\n");

                var materials = new Dictionary<Material, string>();
                var materialJson = new List<string>();
                var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                int vBase = 1, vtBase = 1, vnBase = 1, parts = 0, triangles = 0;

                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!r.enabled || r.GetComponentInParent<ParticleSystem>() != null) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null || mesh.vertexCount == 0) continue;

                    var m = r.transform.localToWorldMatrix; // the holder sits at the origin
                    var nm = m.inverse.transpose;
                    bool mirrored = m.determinant < 0f;
                    var verts = mesh.vertices;
                    var normals = mesh.normals;
                    var uvs = mesh.uv;
                    if (verts.Length == 0) { Debug.LogWarning($"[FOUNDERS] export {key}: mesh {mesh.name} is not readable"); continue; }

                    obj.Append("o ").Append(Safe(r.gameObject.name)).Append('_').Append(parts++).Append('\n');
                    foreach (var v in verts)
                    {
                        var p = ToObj(m.MultiplyPoint3x4(v));
                        bmin = Vector3.Min(bmin, p);
                        bmax = Vector3.Max(bmax, p);
                        obj.Append("v ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n');
                    }
                    bool hasNormals = normals != null && normals.Length == verts.Length;
                    if (hasNormals)
                        foreach (var n in normals)
                        {
                            var q = ToObj(nm.MultiplyVector(n).normalized);
                            obj.Append("vn ").Append(F(q.x)).Append(' ').Append(F(q.y)).Append(' ').Append(F(q.z)).Append('\n');
                        }
                    bool hasUv = uvs != null && uvs.Length == verts.Length;

                    var shared = r.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        var mat = shared.Length > 0 ? shared[Mathf.Min(s, shared.Length - 1)] : null;
                        string mname = MaterialName(mat, materials, mtl, materialJson);
                        Vector2 scale = Vector2.one, offset = Vector2.zero;
                        if (mat != null && mat.HasProperty("_BaseMap"))
                        {
                            scale = mat.GetTextureScale("_BaseMap");
                            offset = mat.GetTextureOffset("_BaseMap");
                        }
                        // UVs per sub-mesh, with this material's tiling baked in.
                        if (hasUv)
                            foreach (var uv in uvs)
                                obj.Append("vt ").Append(F(uv.x * scale.x + offset.x)).Append(' ').Append(F(uv.y * scale.y + offset.y)).Append('\n');
                        obj.Append("usemtl ").Append(mname).Append('\n');
                        var tris = mesh.GetTriangles(s);
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                        {
                            // Negating x flips handedness, so the winding flips too (unless the transform mirrors as well).
                            int a = tris[t], b = mirrored ? tris[t + 1] : tris[t + 2], c = mirrored ? tris[t + 2] : tris[t + 1];
                            obj.Append('f');
                            foreach (int i in new[] { a, b, c })
                            {
                                obj.Append(' ').Append(vBase + i);
                                if (hasUv || hasNormals) obj.Append('/');
                                if (hasUv) obj.Append(vtBase + i);
                                if (hasNormals) obj.Append('/').Append(vnBase + i);
                            }
                            obj.Append('\n');
                            triangles++;
                        }
                        if (hasUv) vtBase += verts.Length;
                    }
                    vBase += verts.Length;
                    if (hasNormals) vnBase += verts.Length;
                }
                if (parts == 0) return null;

                File.WriteAllText(Path.Combine(dir, file + ".obj"), obj.ToString());
                File.WriteAllText(Path.Combine(dir, file + ".mtl"), mtl.ToString());

                // Lights and flames (particles are not exported; the renderer adds a glow there).
                var lights = new List<string>();
                foreach (var ps in model.GetComponentsInChildren<ParticleSystem>(true))
                {
                    string kind = ps.gameObject.name;
                    if (kind != "Flames" && kind != "TorchFlame") continue;
                    float size = ps.main.startSize.constantMax;
                    lights.Add($"      {{\"kind\": \"{Esc(kind)}\", \"position\": {V3(ToObj(ps.transform.position))}, \"size\": {F(size)}}}");
                }
                foreach (var l in model.GetComponentsInChildren<Light>(true))
                {
                    var c = l.color;
                    lights.Add($"      {{\"kind\": \"PointLight\", \"position\": {V3(ToObj(l.transform.position))}, \"color\": [{F(c.r)}, {F(c.g)}, {F(c.b)}], \"range\": {F(l.range)}, \"intensity\": {F(l.intensity)}}}");
                }

                var e = new StringBuilder();
                e.Append("    {\n");
                e.Append($"      \"key\": \"{Esc(key)}\",\n      \"sprite\": \"{Esc(sprite)}\",\n      \"decoration\": {(decoration ? "true" : "false")},\n");
                e.Append($"      \"footprintMetres\": {F(metres)},\n");
                e.Append($"      \"obj\": \"{file}.obj\",\n      \"mtl\": \"{file}.mtl\",\n");
                e.Append($"      \"picture\": \"{file}.png\",\n");
                e.Append($"      \"bounds\": {{\"min\": {V3(bmin)}, \"max\": {V3(bmax)}, \"center\": {V3((bmin + bmax) * 0.5f)}, \"size\": {V3(bmax - bmin)}}},\n");
                e.Append($"      \"parts\": {parts},\n      \"triangles\": {triangles},\n");
                e.Append("      \"materials\": [\n").Append(string.Join(",\n", materialJson)).Append("\n      ],\n");
                e.Append("      \"effects\": [\n").Append(string.Join(",\n", lights)).Append(lights.Count > 0 ? "\n" : "").Append("      ]\n");
                e.Append("    }");
                return e.ToString();
            }

            string MaterialName(Material mat, Dictionary<Material, string> names, StringBuilder mtl, List<string> json)
            {
                if (mat == null) mat = Palette.Get("Earth");
                if (names.TryGetValue(mat, out var existing)) return existing;
                string name = Safe(mat.name);
                if (names.ContainsValue(name)) name += "_" + names.Count;
                names[mat] = name;

                Color color = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.3f;
                float metal = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                bool alphaClip = mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f;
                float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
                bool doubleSided = mat.HasProperty("_Cull") && mat.GetFloat("_Cull") < 0.5f;
                bool emissive = mat.IsKeywordEnabled("_EMISSION") && mat.HasProperty("_EmissionColor");
                Color emission = emissive ? mat.GetColor("_EmissionColor") : Color.black;
                float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
                string albedo = mat.HasProperty("_BaseMap") ? CopyTexture(mat.GetTexture("_BaseMap"), false) : null;
                string normal = mat.HasProperty("_BumpMap") ? CopyTexture(mat.GetTexture("_BumpMap"), true) : null;
                string mask = mat.HasProperty("_MetallicGlossMap") ? CopyTexture(mat.GetTexture("_MetallicGlossMap"), false) : null;

                mtl.Append("\nnewmtl ").Append(name).Append('\n');
                mtl.Append($"Kd {F(color.r)} {F(color.g)} {F(color.b)}\n");
                mtl.Append($"Ka 0 0 0\nKs {F(0.04f + 0.96f * metal)} {F(0.04f + 0.96f * metal)} {F(0.04f + 0.96f * metal)}\n");
                mtl.Append($"Ns {F(Mathf.Lerp(2f, 900f, smooth * smooth))}\n");
                mtl.Append($"Pr {F(1f - smooth)}\nPm {F(metal)}\n");
                mtl.Append($"d {F(color.a)}\n");
                if (emissive) mtl.Append($"Ke {F(emission.r)} {F(emission.g)} {F(emission.b)}\n");
                mtl.Append("illum 2\n");
                if (albedo != null) mtl.Append("map_Kd ").Append(albedo).Append('\n');
                if (albedo != null && alphaClip) mtl.Append("map_d ").Append(albedo).Append('\n');
                if (normal != null) mtl.Append($"map_Bump -bm {F(bumpScale)} ").Append(normal).Append('\n');

                string Opt(string s) => s == null ? "null" : "\"" + Esc(s) + "\"";
                json.Add($"        {{\"name\": \"{Esc(name)}\", \"unity\": \"{Esc(mat.name)}\", \"color\": [{F(color.r)}, {F(color.g)}, {F(color.b)}, {F(color.a)}], " +
                         $"\"smoothness\": {F(smooth)}, \"metallic\": {F(metal)}, \"albedo\": {Opt(albedo)}, \"normal\": {Opt(normal)}, \"bumpScale\": {F(bumpScale)}, " +
                         $"\"mask\": {Opt(mask)}, \"maskChannels\": \"R metallic, G occlusion, A smoothness\", " +
                         $"\"alphaClip\": {(alphaClip ? "true" : "false")}, \"cutoff\": {F(cutoff)}, \"doubleSided\": {(doubleSided ? "true" : "false")}, " +
                         $"\"emission\": [{F(emission.r)}, {F(emission.g)}, {F(emission.b)}]}}");
                return name;
            }

            /// <summary>Copies a texture's source file into textures/ (or encodes it as PNG); returns the relative path.</summary>
            string CopyTexture(Texture tex, bool normalMap)
            {
                if (tex == null) return null;
                if (textures.TryGetValue(tex, out var rel)) return rel;
                string src = AssetDatabase.GetAssetPath(tex);
                string ext = Path.GetExtension(src).ToLowerInvariant();
                string baseName = Safe(string.IsNullOrEmpty(src) ? tex.name : Path.GetFileNameWithoutExtension(src));
                string name = baseName;
                for (int i = 2; textureNames.Contains(name); i++) name = baseName + "_" + i;
                textureNames.Add(name);

                if (!string.IsNullOrEmpty(src) && (ext == ".jpg" || ext == ".jpeg" || ext == ".png") && File.Exists(src))
                {
                    rel = "textures/" + name + ext;
                    File.Copy(src, Path.Combine(dir, rel), true);
                }
                else
                {
                    // Not a plain image file (generated or another format): read it back through the GPU.
                    rel = "textures/" + name + ".png";
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32,
                                                        normalMap ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active;
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, normalMap);
                    read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    read.Apply(false);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(Path.Combine(dir, rel), read.EncodeToPNG());
                    Object.DestroyImmediate(read);
                }
                textures[tex] = rel;
                return rel;
            }
        }
    }
}
