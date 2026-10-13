using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Founders
{
    /// <summary>
    /// The player's own art, dropped into <c>Assets/Custom/Resources/Custom/</c> (see Assets/Custom/README.md):
    /// building and decoration models, villagers with their animations, building pictures, UI icons,
    /// skins and images, and ground textures. Everything is looked up by file name; with the folder
    /// empty every lookup misses and the game draws its own art unchanged.
    /// </summary>
    public static class CustomAssets
    {
        /// <summary>Resources path of the custom folder (Assets/Custom/Resources/Custom).</summary>
        public const string Root = "Custom";
        /// <summary>The game's own villager clips, retargeted for humanoid characters (made by the editor only when custom people exist).</summary>
        public const string BuiltinClipsPath = "CustomBuiltinClips/Person_Clips";

        // MARK: - Textures (icons, skins, images, ground)

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        /// <summary>A custom picture by path under Custom/ without extension ("UI/Icons/wood"), or null.</summary>
        public static Texture2D Texture(string path)
        {
            if (textures.TryGetValue(path, out var t)) return t;
            t = Resources.Load<Texture2D>(Root + "/" + path);
            textures[path] = t;
            return t;
        }

        // MARK: - Models (buildings, decorations)

        sealed class Variant
        {
            public int Level;
            public GameObject Prefab;
        }

        static Dictionary<string, List<Variant>> buildings, decorations;

        /// <summary>"medieval_house@L5" → ("medieval_house", 5); "medieval_house" → (…, 1). Keys are lower case.</summary>
        public static (string key, int level) ParseName(string name)
        {
            int at = name.LastIndexOf('@');
            if (at > 0)
            {
                var tail = name.Substring(at + 1);
                if (tail.Length >= 2 && (tail[0] == 'L' || tail[0] == 'l') && int.TryParse(tail.Substring(1), out int level))
                    return (name.Substring(0, at).Trim().ToLowerInvariant(), Mathf.Max(1, level));
            }
            return (name.Trim().ToLowerInvariant(), 1);
        }

        static Dictionary<string, List<Variant>> LoadModels(string folder)
        {
            var map = new Dictionary<string, List<Variant>>();
            foreach (var go in Resources.LoadAll<GameObject>(Root + "/" + folder))
            {
                if (go == null || go.transform.parent != null) continue; // only whole models, not their parts
                var (key, level) = ParseName(go.name);
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Variant>();
                list.Add(new Variant { Level = level, Prefab = go });
            }
            foreach (var list in map.Values) list.Sort((a, b) => a.Level.CompareTo(b.Level));
            return map;
        }

        static Dictionary<string, List<Variant>> Buildings => buildings ??= LoadModels("Buildings");
        static Dictionary<string, List<Variant>> Decorations => decorations ??= LoadModels("Decorations");

        /// <summary>The variant with the highest level not above <paramref name="level"/>, or null.</summary>
        static GameObject Pick(Dictionary<string, List<Variant>> map, string key, int level)
        {
            if (string.IsNullOrEmpty(key) || map.Count == 0) return null;
            if (!map.TryGetValue(key.ToLowerInvariant(), out var list)) return null;
            GameObject best = null;
            foreach (var v in list) if (v.Level <= level) best = v.Prefab;
            return best;
        }

        public static bool HasBuilding(string sprite) => sprite != null && Buildings.ContainsKey(sprite.ToLowerInvariant());
        public static bool HasDecoration(string sprite) => sprite != null && Decorations.ContainsKey(sprite.ToLowerInvariant());

        /// <summary>A thumbnail key ("medieval_house", "deco:well") has a custom model.</summary>
        public static bool HasModelFor(string thumbKey) =>
            thumbKey.StartsWith("deco:") ? HasDecoration(thumbKey.Substring(5)) : HasBuilding(thumbKey);

        /// <summary>Changes whenever the set of custom models changes (keeps cached thumbnails honest).</summary>
        public static string Signature
        {
            get
            {
                if (Buildings.Count == 0 && Decorations.Count == 0) return "";
                int h = 17;
                foreach (var map in new[] { Buildings, Decorations })
                    foreach (var kv in map)
                        foreach (var v in kv.Value)
                            h = h * 31 + (kv.Key + "@" + v.Level).GetHashCode();
                return "_c" + ((uint)h).ToString("x8");
            }
        }

        /// <summary>
        /// The custom building model for a sprite at a level, fitted to its footprint (width along x,
        /// depth along z, in metres), standing on the ground, centred; null when there is none.
        /// </summary>
        public static GameObject Building(string sprite, int level, Transform parent, Material tint, Vector2 footprint)
        {
            var prefab = Pick(Buildings, sprite, level);
            if (prefab == null) return null;
            var root = new GameObject("Model_" + sprite);
            root.transform.SetParent(parent, false);
            root.AddComponent<CustomModel>();
            // The model's front (+Z, Blender's front view) faces the street and the camera (−Z).
            var turn = Parts.Group("Turn", root.transform, Vector3.zero, 180f).transform;
            Place(prefab, turn, tint, footprint * 0.94f, grow: true, ground: true);
            return root;
        }

        /// <summary>
        /// A custom decoration in the middle of its cell, turned by quarter turns; shrunk to the cell if
        /// larger, otherwise kept at its real size. Bridges span their cell and keep their own height
        /// (origin = water surface).
        /// </summary>
        public static GameObject Decoration(string sprite, Transform parent, Material tint, float yaw, bool bridge)
        {
            var prefab = Pick(Decorations, sprite, 1);
            if (prefab == null) return null;
            var root = new GameObject("Decoration_" + sprite);
            root.transform.SetParent(parent, false);
            root.AddComponent<CustomModel>();
            var turn = Parts.Group("Turn", root.transform, Vector3.zero, yaw + 180f).transform;
            float cell = Grid.CellSize * (bridge ? 1.02f : 0.95f);
            Place(prefab, turn, tint, new Vector2(cell, cell), grow: bridge, ground: !bridge);
            return root;
        }

        static void Place(GameObject prefab, Transform holder, Material tint, Vector2 fit, bool grow, bool ground)
        {
            var go = Object.Instantiate(prefab, holder, false);
            go.name = prefab.name;
            go.transform.localPosition = Vector3.zero;
            Clean(go);
            if (tint != null)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = tint;
                    r.sharedMaterials = mats;
                }
            }
            else FixMaterials(go);

            var b = LocalBounds(go, holder);
            if (b.size == Vector3.zero) return;
            float sx = Mathf.Max(0.001f, b.size.x), sz = Mathf.Max(0.001f, b.size.z);
            float s = Mathf.Min(fit.x / sx, fit.y / sz);
            if (!grow) s = Mathf.Min(1f, s);
            go.transform.localScale *= s;
            // Scaling about the holder's origin scales the box about it too: recentre it there.
            go.transform.localPosition = new Vector3(-b.center.x * s, ground ? -b.min.y * s : 0f, -b.center.z * s);
        }

        /// <summary>Bounds of the meshes in the holder's space (exact for rotated parents, no read/write needed).</summary>
        public static Bounds LocalBounds(GameObject go, Transform holder)
        {
            var toHolder = holder.worldToLocalMatrix;
            bool any = false;
            var result = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                Bounds mb;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) mb = smr.sharedMesh.bounds;
                else if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) mb = mf.sharedMesh.bounds;
                else continue;
                var m = toHolder * r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var p = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var q = m.MultiplyPoint3x4(p);
                    if (!any) { result = new Bounds(q, Vector3.zero); any = true; }
                    else result.Encapsulate(q);
                }
            }
            return result;
        }

        /// <summary>Custom models only draw: no colliders (taps), cameras or listeners of their own.</summary>
        static void Clean(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) Kill(c.gameObject);
            foreach (var c in go.GetComponentsInChildren<AudioListener>(true)) Kill(c);
        }

        /// <summary>Destroy, also from editor tools (thumbnail export) outside play mode.</summary>
        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // MARK: - Materials

        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        static Shader lit;

        /// <summary>Whether a material renders in this URP game (anything else shows pink).</summary>
        public static bool RendersInUrp(Material m)
        {
            if (m == null || m.shader == null) return false;
            var n = m.shader.name;
            if (n == "Hidden/InternalErrorShader" || !m.shader.isSupported) return false;
            return n.StartsWith("Universal Render Pipeline/") || n.StartsWith("Shader Graphs/") || n.StartsWith("Founders/");
        }

        /// <summary>Swaps materials of other pipelines (Standard, Legacy, Autodesk…) for URP/Lit copies.</summary>
        public static void FixMaterials(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (RendersInUrp(mats[i])) continue;
                    mats[i] = ToUrp(mats[i]);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>A URP/Lit copy of a material, keeping colour, albedo, normal, metallic, occlusion, emission and transparency.</summary>
        public static Material ToUrp(Material src)
        {
            if (src != null && converted.TryGetValue(src, out var done)) return done;
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(lit) { name = (src != null ? src.name : "Missing") + " (URP)" };
            if (src != null)
            {
                var albedo = Tex(src, "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_Diffuse");
                if (albedo != null) { m.SetTexture("_BaseMap", albedo); m.SetTextureScale("_BaseMap", src.mainTextureScale); m.SetTextureOffset("_BaseMap", src.mainTextureOffset); }
                var color = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
                m.SetColor("_BaseColor", color);
                var normal = Tex(src, "_BumpMap", "_NormalMap", "_Normal");
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                var metal = Tex(src, "_MetallicGlossMap", "_MetallicMap");
                if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                m.SetFloat("_Metallic", src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f);
                float smooth = src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness") : 0.3f;
                if (metal != null && src.HasProperty("_GlossMapScale")) smooth = src.GetFloat("_GlossMapScale");
                m.SetFloat("_Smoothness", smooth);
                var ao = Tex(src, "_OcclusionMap");
                if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.EnableKeyword("_OCCLUSIONMAP"); }
                var emission = Tex(src, "_EmissionMap");
                var emissionColor = src.HasProperty("_EmissionColor") ? src.GetColor("_EmissionColor") : Color.black;
                if (emission != null || emissionColor.maxColorComponent > 0.01f)
                {
                    if (emission != null) m.SetTexture("_EmissionMap", emission);
                    m.SetColor("_EmissionColor", emission != null && emissionColor.maxColorComponent <= 0.01f ? Color.white : emissionColor);
                    m.EnableKeyword("_EMISSION");
                }
                // Standard's rendering mode: 1 cutout, 2 fade, 3 transparent.
                float mode = src.HasProperty("_Mode") ? src.GetFloat("_Mode") : 0f;
                if (mode == 1f || src.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }
                else if (mode >= 2f || src.renderQueue >= 2900)
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                converted[src] = m;
            }
            return m;
        }

        static Texture Tex(Material m, params string[] names)
        {
            foreach (var n in names)
                if (m.HasProperty(n) && m.GetTexture(n) is Texture t && t != null) return t;
            return null;
        }

        // MARK: - People

        /// <summary>Clip names the villagers play, with what to fall back on when a character lacks one.</summary>
        public static readonly string[] ClipNames =
            { "Idle", "Walk", "Jog", "Carry", "Talk", "Watch", "Gather", "Work", "Chop", "Plant", "Harvest", "Water", "Throw", "Sit", "Lie", "Torch", "Lantern", "Eat", "Push" };

        static readonly Dictionary<string, string> ClipFallback = new Dictionary<string, string>
        {
            ["Jog"] = "Walk", ["Carry"] = "Walk", ["Talk"] = "Idle", ["Watch"] = "Idle", ["Gather"] = "Work", ["Work"] = "Idle",
            ["Chop"] = "Work", ["Plant"] = "Gather", ["Harvest"] = "Gather", ["Water"] = "Gather", ["Throw"] = "Work",
            ["Sit"] = "Idle", ["Lie"] = "Sit", ["Torch"] = "Idle", ["Lantern"] = "Torch", ["Eat"] = "Idle", ["Push"] = "Work",
        };

        /// <summary>Other words for the clip names (as Mixamo and other packs call them), compared without case, spaces or _ -.</summary>
        static readonly Dictionary<string, string> ClipAliases = new Dictionary<string, string>
        {
            ["idle"] = "Idle", ["breathingidle"] = "Idle", ["standing"] = "Idle", ["standingidle"] = "Idle",
            ["walk"] = "Walk", ["walking"] = "Walk",
            ["jog"] = "Jog", ["jogging"] = "Jog", ["run"] = "Jog", ["running"] = "Jog",
            ["carry"] = "Carry", ["carrying"] = "Carry", ["walkcarry"] = "Carry",
            ["talk"] = "Talk", ["talking"] = "Talk",
            ["watch"] = "Watch", ["armscrossed"] = "Watch", ["lookaround"] = "Watch",
            ["gather"] = "Gather", ["gathering"] = "Gather", ["pickup"] = "Gather", ["pickingup"] = "Gather",
            ["work"] = "Work", ["working"] = "Work", ["hammer"] = "Work", ["hammering"] = "Work",
            ["chop"] = "Chop", ["chopping"] = "Chop",
            ["plant"] = "Plant", ["planting"] = "Plant",
            ["harvest"] = "Harvest", ["harvesting"] = "Harvest",
            ["water"] = "Water", ["watering"] = "Water",
            ["throw"] = "Throw", ["throwing"] = "Throw",
            ["sit"] = "Sit", ["sitting"] = "Sit", ["sittingidle"] = "Sit",
            ["lie"] = "Lie", ["lying"] = "Lie", ["laying"] = "Lie", ["sleeping"] = "Lie",
            ["torch"] = "Torch", ["lantern"] = "Lantern",
            ["eat"] = "Eat", ["eating"] = "Eat",
            ["push"] = "Push", ["pushing"] = "Push",
        };

        /// <summary>The game's clip name for a clip ("Armature|Walking" → "Walk"), or null.</summary>
        public static string ClipNameOf(string clipName)
        {
            if (string.IsNullOrEmpty(clipName) || clipName.StartsWith("__preview__")) return null;
            var n = clipName;
            int bar = n.LastIndexOf('|');
            if (bar >= 0) n = n.Substring(bar + 1);
            int at = n.LastIndexOf('@');
            if (at >= 0) n = n.Substring(at + 1);
            var key = new System.Text.StringBuilder();
            foreach (char c in n) if (char.IsLetter(c)) key.Append(char.ToLowerInvariant(c));
            if (ClipAliases.TryGetValue(key.ToString(), out var exact)) return exact;
            // "Walk_Loop", "Idle01", "CharacterArmature|Walk_Forward": the longest known word it starts with.
            string best = null;
            foreach (var kv in ClipAliases)
                if (key.Length > kv.Key.Length && key.ToString().StartsWith(kv.Key) && (best == null || kv.Key.Length > best.Length)) best = kv.Key;
            return best != null ? ClipAliases[best] : null;
        }

        /// <summary>Clips by game name; earlier sources win.</summary>
        static void AddClips(Dictionary<string, AnimationClip> into, IEnumerable<AnimationClip> clips)
        {
            foreach (var c in clips)
            {
                var name = c != null ? ClipNameOf(c.name) : null;
                if (name != null && !into.ContainsKey(name)) into[name] = c;
            }
        }

        static Dictionary<string, AnimationClip> sharedClips, builtinClips;

        /// <summary>Clips in People/Animations (one per file, named after the file).</summary>
        public static Dictionary<string, AnimationClip> SharedClips
        {
            get
            {
                if (sharedClips != null) return sharedClips;
                sharedClips = new Dictionary<string, AnimationClip>();
                AddClips(sharedClips, Resources.LoadAll<AnimationClip>(Root + "/People/Animations"));
                return sharedClips;
            }
        }

        /// <summary>The game's own clips, retargeted (only present when the editor found custom people).</summary>
        public static Dictionary<string, AnimationClip> BuiltinClips
        {
            get
            {
                if (builtinClips != null) return builtinClips;
                builtinClips = new Dictionary<string, AnimationClip>();
                // Only when the copy really became Humanoid (its rig was recognised).
                AddClips(builtinClips, Array.FindAll(Resources.LoadAll<AnimationClip>(BuiltinClipsPath), c => c != null && c.humanMotion));
                return builtinClips;
            }
        }

        public sealed class Person
        {
            public string Name;
            /// <summary>Lower-case role from the name prefix ("woodcutter", "child", "elder") or "".</summary>
            public string Role;
            /// <summary>"female", "male" or "" (any).</summary>
            public string Gender;
            public GameObject Prefab;
            public Dictionary<string, AnimationClip> Clips;
        }

        /// <summary>Name prefixes that pick people for a job (JobType order), plus Child_ and Elder_.</summary>
        public static readonly Dictionary<string, string> JobRoles = new Dictionary<string, string>
        {
            ["foraging"] = "forager", ["woodcutting"] = "woodcutter", ["stonecutting"] = "stonecutter", ["crafting"] = "crafter",
            ["construction"] = "builder", ["research"] = "scholar", ["farming"] = "farmer", ["fishing"] = "fisher",
            ["hunting"] = "hunter", ["mining"] = "miner", ["herding"] = "herder", ["healing"] = "healer",
        };

        static List<Person> people;

        /// <summary>The custom characters that can walk (they have Idle and Walk from somewhere).</summary>
        public static List<Person> People
        {
            get
            {
                if (people != null) return people;
                people = new List<Person>();
                var animationFiles = new HashSet<string>();
                foreach (var go in Resources.LoadAll<GameObject>(Root + "/People/Animations")) if (go != null) animationFiles.Add(go.name);
                foreach (var go in Resources.LoadAll<GameObject>(Root + "/People"))
                {
                    if (go == null || go.transform.parent != null || animationFiles.Contains(go.name)) continue;
                    if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
                    var p = Describe(go);
                    if (!p.Clips.ContainsKey("Idle") || !p.Clips.ContainsKey("Walk"))
                    {
                        Debug.LogWarning($"[FOUNDERS] custom person {go.name}: no Idle/Walk animation found, not used (see Assets/Custom/README.md).");
                        continue;
                    }
                    people.Add(p);
                }
                if (people.Count > 0) Debug.Log($"[FOUNDERS] custom people: {string.Join(", ", people.ConvertAll(p => p.Name))}");
                return people;
            }
        }

        /// <summary>Name, role, gender and clips of a character (its own clips first, then shared, then the game's).</summary>
        public static Person Describe(GameObject go)
        {
            string name = go.name;
            string lower = name.ToLowerInvariant();
            int us = lower.IndexOf('_');
            string prefix = us > 0 ? lower.Substring(0, us) : "";
            string role = prefix == "child" || prefix == "elder" || JobRoles.ContainsValue(prefix) ? prefix : "";
            var words = lower.Split('_', '-', ' ', '.');
            string gender = Array.Exists(words, w => w == "female" || w == "woman" || w == "girl") ? "female"
                          : Array.Exists(words, w => w == "male" || w == "man" || w == "boy") ? "male" : "";
            var clips = new Dictionary<string, AnimationClip>();
            AddClips(clips, Resources.LoadAll<AnimationClip>(Root + "/People/" + name));
            // Humanoid clips play on any humanoid character; others only on the rig they were made for.
            var animator = go.GetComponentInChildren<Animator>(true);
            bool human = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
            foreach (var kv in SharedClips) if (!clips.ContainsKey(kv.Key) && kv.Value.humanMotion == human) clips[kv.Key] = kv.Value;
            if (human) foreach (var kv in BuiltinClips) if (!clips.ContainsKey(kv.Key)) clips[kv.Key] = kv.Value;
            return new Person { Name = name, Role = role, Gender = gender, Prefab = go, Clips = clips };
        }

        /// <summary>
        /// A custom person for a villager, or null when there are none: children prefer Child_*, elders
        /// Elder_*, workers their job's prefix (Woodcutter_*…); then the wanted gender; else anyone.
        /// </summary>
        public static Person PickPerson(System.Random rng, bool female, bool child, bool elder, string jobId)
        {
            var all = People;
            if (all.Count == 0) return null;
            string wantRole = child ? "child" : elder ? "elder" : jobId != null && JobRoles.TryGetValue(jobId, out var r) ? r : null;
            var pool = wantRole != null ? all.FindAll(p => p.Role == wantRole) : new List<Person>();
            if (pool.Count == 0) pool = all.FindAll(p => p.Role != "child" && p.Role != "elder" && !JobRoles.ContainsValue(p.Role));
            if (pool.Count == 0) pool = all.FindAll(p => p.Role != "child");
            if (pool.Count == 0) pool = all;
            var gendered = pool.FindAll(p => p.Gender == (female ? "female" : "male"));
            if (gendered.Count == 0) gendered = pool.FindAll(p => p.Gender == "");
            if (gendered.Count > 0) pool = gendered;
            return pool[rng.Next(pool.Count)];
        }

        /// <summary>The clip to play for a game clip name, following the fallbacks (Chop → Work → Idle), or null.</summary>
        public static AnimationClip Resolve(Dictionary<string, AnimationClip> clips, string name)
        {
            for (int guard = 0; name != null && guard < 6; guard++)
            {
                if (clips.TryGetValue(name, out var c)) return c;
                name = ClipFallback.TryGetValue(name, out var next) ? next : name == "Idle" ? null : "Idle";
            }
            return null;
        }
    }

    /// <summary>Marks a model built from the player's own file (no extra crates and banners on it).</summary>
    public class CustomModel : MonoBehaviour { }

    /// <summary>
    /// Plays a custom character's clips through the Animator with the Playables API (no Animator
    /// Controller asset needed): one clip at a time, cross-faded, looping, staying on the spot
    /// (root motion off; the villager moves itself).
    /// </summary>
    public class CustomPersonAnimator : MonoBehaviour
    {
        Dictionary<string, AnimationClip> clips;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] slots = new AnimationClipPlayable[2];
        readonly AnimationClip[] slotClips = new AnimationClip[2];
        int active = -1;
        float fadeLeft, fadeTime;

        public void Init(Animator animator, Dictionary<string, AnimationClip> clips)
        {
            this.clips = clips;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            graph = PlayableGraph.Create("Villager_" + name);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        public bool Has(string clip) => clips != null && CustomAssets.Resolve(clips, clip) != null;

        /// <summary>Cross-fades to the clip (or its fallback); random start time when <paramref name="randomStart"/>.</summary>
        public void Play(string clip, float fade, bool randomStart = false)
        {
            if (!graph.IsValid()) return;
            var c = CustomAssets.Resolve(clips, clip);
            if (c == null || (active >= 0 && slotClips[active] == c)) return;
            int slot = active < 0 ? 0 : 1 - active;
            if (slots[slot].IsValid())
            {
                graph.Disconnect(mixer, slot);
                slots[slot].Destroy();
            }
            var p = AnimationClipPlayable.Create(graph, c);
            p.SetApplyFootIK(true);
            if (randomStart) p.SetTime(UnityEngine.Random.value * c.length);
            graph.Connect(p, 0, mixer, slot);
            slots[slot] = p;
            slotClips[slot] = c;
            active = slot;
            fadeTime = fadeLeft = Mathf.Max(0f, fade);
            if (fadeTime <= 0f) Weights(1f);
        }

        void Weights(float t)
        {
            mixer.SetInputWeight(active, t);
            mixer.SetInputWeight(1 - active, slots[1 - active].IsValid() ? 1f - t : 0f);
        }

        void Update()
        {
            if (!graph.IsValid() || active < 0) return;
            if (fadeLeft > 0f)
            {
                fadeLeft -= Time.deltaTime;
                Weights(1f - Mathf.Clamp01(fadeLeft / Mathf.Max(0.001f, fadeTime)));
            }
            // Clips not imported as loops are wrapped by hand: villagers loop every clip.
            for (int i = 0; i < 2; i++)
            {
                if (!slots[i].IsValid() || slotClips[i] == null || slotClips[i].isLooping) continue;
                float len = Mathf.Max(0.05f, slotClips[i].length);
                double t = slots[i].GetTime();
                if (t > len) slots[i].SetTime(t % len);
            }
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
