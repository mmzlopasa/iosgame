using System;
using System.Collections.Generic;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// The licensed nature packs "Ultimate Nature PRO" and "Ultimate Nature – Starter" (Innerverse):
    /// trees, bushes, plants, rocks, cliffs, ground layers, water and sky. The packs are not in the
    /// public repository; when they are in the project, the editor (Editor/NatureKitSetup.cs) lists
    /// their prefabs here, with URP copies of their materials, and the valley is drawn with them
    /// alone. Without the packs the asset does not exist and the game keeps its own nature.
    /// </summary>
    public class NatureKit : ScriptableObject
    {
        [Serializable]
        public class PrefabEntry
        {
            public string Name;
            public GameObject Prefab;
        }

        [Serializable]
        public class MaterialEntry
        {
            public Material Source;
            public Material Converted;
        }

        public List<PrefabEntry> Prefabs = new List<PrefabEntry>();
        public List<MaterialEntry> Materials = new List<MaterialEntry>();
        public List<TerrainLayer> TerrainLayers = new List<TerrainLayer>();
        public Material Water;

        static NatureKit loaded;
        static bool tried;

        /// <summary>The kit in this build, or null when the packs were not in the project.</summary>
        public static NatureKit Current
        {
            get
            {
                if (tried) return loaded;
                tried = true;
                loaded = Resources.Load<NatureKit>("NatureKit");
                if (loaded != null && loaded.Prefabs.Count == 0) loaded = null;
                if (loaded != null) Debug.Log($"[FOUNDERS] nature kit: {loaded.Prefabs.Count} prefabs, {loaded.Materials.Count} materials, {loaded.TerrainLayers.Count} ground layers");
                return loaded;
            }
        }

        Dictionary<string, GameObject> byName;
        Dictionary<Material, Material> remap;
        readonly Dictionary<string, List<GameObject>> groups = new Dictionary<string, List<GameObject>>();

        public GameObject Prefab(string name)
        {
            if (byName == null)
            {
                byName = new Dictionary<string, GameObject>();
                foreach (var e in Prefabs)
                    if (e.Prefab != null && !byName.ContainsKey(e.Name)) byName[e.Name] = e.Prefab;
            }
            return byName.TryGetValue(name, out var p) ? p : null;
        }

        /// <summary>The prefabs of a group that are in the project (see <see cref="Groups"/>), in a fixed order.</summary>
        public List<GameObject> Group(string group)
        {
            if (groups.TryGetValue(group, out var list)) return list;
            list = new List<GameObject>();
            if (Groups.TryGetValue(group, out var names))
                foreach (var n in names)
                {
                    var p = Prefab(n);
                    if (p != null) list.Add(p);
                }
            return groups[group] = list;
        }

        public bool Has(string group) => Group(group).Count > 0;

        /// <summary>
        /// One prefab of a group, picked by the random generator. Only the first few of a group are
        /// used: every different model is drawn separately, so fewer kinds keep the iPhone fast.
        /// </summary>
        public GameObject Pick(string group, System.Random rng)
        {
            var list = Group(group);
            return list.Count == 0 ? null : list[rng.Next(Math.Min(list.Count, MaxVariants))];
        }

        const int MaxVariants = 3;

        /// <summary>The URP material that stands in for one of the packs' materials.</summary>
        public Material Convert(Material source)
        {
            if (source == null) return null;
            if (remap == null)
            {
                remap = new Dictionary<Material, Material>();
                foreach (var e in Materials)
                    if (e.Source != null && e.Converted != null) remap[e.Source] = e.Converted;
            }
            return remap.TryGetValue(source, out var m) ? m : source;
        }

        /// <summary>The first ground layer of the packs named one of these (exactly, else containing the word).</summary>
        public TerrainLayer Layer(params string[] names)
        {
            foreach (var n in names)
                foreach (var l in TerrainLayers)
                    if (l != null && l.diffuseTexture != null && l.name == n) return l;
            foreach (var n in names)
                foreach (var l in TerrainLayers)
                    if (l != null && l.diffuseTexture != null && l.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return l;
            return null;
        }

        readonly Dictionary<string, TerrainLayer> matte = new Dictionary<string, TerrainLayer>();

        /// <summary>
        /// The ground layer for one of the game's grounds (Grass, Mud, Sand, RockGround), or null. A
        /// matte copy: the packs' layers are partly smooth (and the dirt's mask map makes it shine),
        /// which made the valley floor look wet and glossy.
        /// </summary>
        public TerrainLayer GroundFor(string ground)
        {
            if (matte.TryGetValue(ground, out var cached)) return cached;
            var l = PackGroundFor(ground);
            if (l == null) return matte[ground] = null;
            return matte[ground] = new TerrainLayer
            {
                name = l.name + "_Matte",
                diffuseTexture = l.diffuseTexture,
                normalMapTexture = l.normalMapTexture,
                normalScale = Mathf.Min(l.normalScale, 0.6f),
                tileSize = l.tileSize,
                tileOffset = l.tileOffset,
                smoothness = 0f,
                metallic = 0f,
                specular = Color.black,
            };
        }

        TerrainLayer PackGroundFor(string ground)
        {
            switch (ground)
            {
                case "Grass": return Layer("Grass", "UNS_Grass", "Meadow");
                case "Mud": return Layer("Dirt", "UNS_Dirt", "Mud", "Soil");
                case "Sand": return Layer("Sand", "Gravel", "Pebbles");
                case "RockGround": return Layer("Rock", "Stone", "Cliff", "Gravel");
                default: return null;
            }
        }

        /// <summary>
        /// What the valley is made of: prefab names of both packs per kind of object. PRO comes
        /// first; the Starter pack adds its spruces, rocks and plants to the same kinds.
        /// </summary>
        public static readonly Dictionary<string, string[]> Groups = new Dictionary<string, string[]>
        {
            ["fir"] = new[] { "Tree_Fir_Tall_01", "Tree_Fir_Tall_02", "Tree_Fir_Short", "UNS_Spruce_01", "UNS_Spruce_02" },
            ["fir_young"] = new[] { "Tree_Fir_Sapling", "Tree_Fir_Short" },
            ["broadleaf"] = new[] { "Tree_Broadleaf_01", "Tree_Broadleaf_02", "Tree_Broadleaf_03", "Tree_Broadleaf_04", "Tree_Broadleaf_05" },
            ["birch"] = new[] { "Tree_Birch_01", "Tree_Birch_02", "Tree_Birch_03", "Tree_Birch_04", "Tree_Birch_05" },
            ["dead_tree"] = new[] { "Tree_Fir_Dead", "Tree_Birch_01_Dead", "Tree_Birch_03_Dead", "Tree_Broadleaf_02_Dead", "Tree_Broadleaf_04_Dead" },
            ["bush"] = new[] { "Bush_Short", "Bush_Tall", "UNS_Bush" },
            ["bush_flower"] = new[] { "Bush_Flower_Short", "Bush_Flower_Tall" },
            ["bush_dead"] = new[] { "Bush_Dead_Short", "Bush_Dead_Tall" },
            ["grass"] = new[] { "Grass_01_Low", "Grass_01_Medium", "Grass_02", "UNS_Grass" },
            ["grass_tall"] = new[] { "Grass_01_High", "Grass_01_Medium" },
            ["reeds"] = new[] { "Grass_Reeds", "Plant_Reeds" },
            ["flowers"] = new[] { "Flower_Poppy", "Flower_Lavender", "Flower_Cherry_Blossom", "UNS_Flower" },
            ["fern"] = new[] { "Plant_Fern_Low", "Plant_Fern_High" },
            ["clover"] = new[] { "Plant_Clovers" },
            ["lily"] = new[] { "Plant_Water_Lily" },
            ["wheat"] = new[] { "Plant_Wheat" },
            ["mushroom"] = new[] { "Mushroom_Amanita", "Mushroom_Liberty_Cap", "UNS_Mushroom_Patch" },
            ["rock"] = new[] { "Rock_Large_01", "Rock_Large_02", "Rock_Large_03", "UNS_Standard_Rock_01", "UNS_Standard_Rock_02",
                               "UNS_Standard_Rock_03", "UNS_Standard_Rock_04", "UNS_Standard_Rock_05" },
            ["pebble"] = new[] { "Rock_Small_01", "Rock_Small_02", "Rock_Small_03", "UNS_Tiny_Rock_01", "UNS_Tiny_Rock_02",
                                 "UNS_Tiny_Rock_03", "UNS_Tiny_Rock_04", "UNS_Tiny_Rock_05" },
            ["cliff"] = new[] { "Rock_Cliff_01", "Rock_Cliff_02", "Rock_Cliff_03", "UNS_Rock_Cliff_01", "UNS_Rock_Cliff_02",
                                "UNS_Rock_Cliff_03", "UNS_Rock_Cliff_04", "UNS_Rock_Cliff_05" },
            ["log"] = new[] { "Tree_Fir_Log", "Tree_Birch_Log", "Tree_Broadleaf_Log", "UNS_Log" },
            ["stump"] = new[] { "Tree_Fir_Stump", "Tree_Birch_Stump", "Tree_Broadleaf_Stump", "UNS_Stump" },
            ["branch"] = new[] { "Branch_01", "Branch_02", "Branch_03", "Stick_01", "Stick_02", "UNS_Branch" },
        };
    }
}
