using System.Collections.Generic;
using System.IO;
using System.Linq;
using Founders;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace FoundersEditor
{
    /// <summary>
    /// Lists the nature packs ("Ultimate Nature PRO" in Assets/Innerverse Games and "Ultimate Nature –
    /// Starter" in Assets/InnerverseInteractive) for the game: every prefab by name, a URP/Lit copy of
    /// every material they use (PRO's vegetation and Standard materials do not render in URP), their
    /// ground layers and the water. Writes Assets/Generated/Resources/NatureKit.asset (not committed:
    /// the packs are licensed and stay out of the public repository).
    /// </summary>
    public static class NatureKitSetup
    {
        static readonly string[] Roots = { "Assets/Innerverse Games", "Assets/InnerverseInteractive" };
        const string Gen = "Assets/Generated";
        const string KitPath = Gen + "/Resources/NatureKit.asset";
        const string MaterialFolder = Gen + "/NatureKit";
        /// <summary>Demo and helper objects of the packs that are not nature.</summary>
        static readonly string[] Skip = { "Human_Reference", "Ground", "Grid", "Bridge_Arch", "Bridge_Straight" };

        [MenuItem("FOUNDERS/Prepare nature packs")]
        public static void Run()
        {
            var roots = Roots.Where(AssetDatabase.IsValidFolder).ToArray();
            if (roots.Length == 0)
            {
                if (File.Exists(KitPath)) AssetDatabase.DeleteAsset(KitPath);
                Debug.Log("[FOUNDERS] nature packs not in the project: the game's own nature is used.");
                return;
            }
            Directory.CreateDirectory(Gen + "/Resources");
            if (AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.DeleteAsset(MaterialFolder);
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            var kit = ScriptableObject.CreateInstance<NatureKit>();
            var converted = new Dictionary<Material, Material>();
            var names = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", roots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Demo/") || path.Contains("/Scenes/")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || Skip.Contains(prefab.name) || !names.Add(prefab.name)) continue;
                if (prefab.GetComponentsInChildren<MeshFilter>(true).Length == 0) continue;
                kit.Prefabs.Add(new NatureKit.PrefabEntry { Name = prefab.name, Prefab = prefab });
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !converted.ContainsKey(m)) converted[m] = Convert(m);
            }
            foreach (var kv in converted) kit.Materials.Add(new NatureKit.MaterialEntry { Source = kv.Key, Converted = kv.Value });

            foreach (var guid in AssetDatabase.FindAssets("t:TerrainLayer", roots))
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guid));
                if (layer != null) kit.TerrainLayers.Add(layer);
            }

            // Water: the Starter pack's URP water first, PRO's own water shader otherwise.
            var water = FindMaterial(roots, "UNS_Water") ?? FindMaterial(roots, "Water");
            if (water != null)
            {
                var copy = new Material(water) { name = "KitWater" };
                copy.renderQueue = water.renderQueue;
                // The pack's water is see-through and mirror-smooth: over the dark riverbed it read as
                // glossy brown mud, and the low sun drew a glaring streak along every bank. Calmer,
                // more opaque painted water with no sun highlight and only a faint sheen.
                if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", new Color(0.22f, 0.50f, 0.74f, 0.88f));
                if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", 0.12f);
                if (copy.HasProperty("_SpecularHighlights")) { copy.SetFloat("_SpecularHighlights", 0f); copy.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
                if (copy.HasProperty("_EnvironmentReflections")) { copy.SetFloat("_EnvironmentReflections", 0f); copy.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
                kit.Water = SaveMaterial(copy, $"{MaterialFolder}/KitWater.mat");
            }

            if (File.Exists(KitPath)) AssetDatabase.DeleteAsset(KitPath);
            AssetDatabase.CreateAsset(kit, KitPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FOUNDERS] nature packs: {kit.Prefabs.Count} prefabs, {kit.Materials.Count} materials, {kit.TerrainLayers.Count} ground layers, water {(kit.Water != null ? kit.Water.shader.name : "none")}");
            foreach (var group in NatureKit.Groups)
            {
                var missing = group.Value.Where(n => !names.Contains(n)).ToList();
                if (missing.Count == group.Value.Length) Debug.LogWarning($"[FOUNDERS] nature packs: nothing for '{group.Key}' ({string.Join(", ", group.Value)})");
            }
        }

        /// <summary>The sky of the packs (procedural, not the 250 MB HDRI), or null.</summary>
        public static Material FindSky()
        {
            var roots = Roots.Where(AssetDatabase.IsValidFolder).ToArray();
            if (roots.Length == 0) return null;
            return FindMaterial(roots, "Procedural_Skybox") ?? FindMaterial(roots, "UNS_Skybox_Procedural");
        }

        static Material FindMaterial(string[] roots, string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Material", roots))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name) return m;
            }
            return null;
        }

        static Material SaveMaterial(Material m, string path)
        {
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        static Texture Tex(Material m, params string[] props)
        {
            foreach (var p in props)
                if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return null;
        }

        static Color Col(Material m, Color fallback, params string[] props)
        {
            foreach (var p in props)
                if (m.HasProperty(p)) return m.GetColor(p);
            return fallback;
        }

        static float Num(Material m, float fallback, params string[] props)
        {
            foreach (var p in props)
                if (m.HasProperty(p)) return m.GetFloat(p);
            return fallback;
        }

        /// <summary>
        /// A URP material for one of the packs' materials, drawn with GPU instancing. URP materials are
        /// copied as they are; PRO's vegetation shader (a built-in pipeline surface shader) and Standard
        /// materials become URP/Lit with the same texture, colour, smoothness and leaf cut-out.
        /// </summary>
        static Material Convert(Material src)
        {
            string shader = src.shader != null ? src.shader.name : "";
            string file = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{Safe(src.name)}.mat");
            Material m;
            if (shader.StartsWith("Universal Render Pipeline/") || shader.StartsWith("Shader Graphs/"))
            {
                m = new Material(src) { name = src.name };
            }
            else
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = src.name };
                var tex = Tex(src, "_Texture", "_MainTex", "_BaseMap", "_Albedo", "_AlbedoMap");
                if (tex != null)
                {
                    m.SetTexture("_BaseMap", tex);
                    if (src.HasProperty("_MainTex")) m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
                }
                m.SetColor("_BaseColor", Col(src, Color.white, "_MainColor", "_Color", "_BaseColor"));
                var normal = Tex(src, "_BumpMap", "_NormalMap", "_Normal");
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", Num(src, 1f, "_BumpScale", "_NormalStrength")); }
                m.SetFloat("_Metallic", 0f);
                bool vegetation = shader.Contains("Vegetation");
                bool cutout = vegetation || src.IsKeywordEnabled("_ALPHATEST_ON") || src.GetTag("RenderType", false) == "TransparentCutout";
                if (cutout)
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", Num(src, 0.5f, "_AlphaCutoff", "_Cutoff"));
                }
                // Leaves and grass cards are seen from both sides.
                if (vegetation) m.SetFloat("_Cull", 0f);
                if (src.HasProperty("_EmissionColor") && src.IsKeywordEnabled("_EMISSION"))
                {
                    m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                    m.EnableKeyword("_EMISSION");
                }
                BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            }
            Matte(m);
            // The packs' stone is pale grey (its moss and snow cover needs their own shader): under the
            // blue winter and night light it looked like ice or glass. A darker, warmer stone instead.
            string n = src.name.ToLowerInvariant();
            if ((n.Contains("rock") || n.Contains("cliff") || n.Contains("mountain") || n.Contains("stone")) && m.HasProperty("_BaseColor"))
            {
                var c = m.GetColor("_BaseColor");
                m.SetColor("_BaseColor", new Color(c.r * 0.66f, c.g * 0.62f, c.b * 0.56f, 1f));
            }
            m.enableInstancing = true;
            return SaveMaterial(m, file);
        }

        /// <summary>
        /// No shine on nature: leaves, bark and stone are matte in the painted look, and skipping the
        /// highlights and reflections is also cheaper for the iPhone.
        /// </summary>
        static void Matte(Material m)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.08f));
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            if (m.HasProperty("_EnvironmentReflections")) { m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
    }
}
