using System;
using System.Collections.Generic;
using Founders.Art;
using Founders.Core;
using Founders.World;
using Godot;
using Json = Founders.Core.Json;

namespace Founders.People
{
    /// <summary>What a villager does next: walk (maybe along a route) to a spot and play a clip there.</summary>
    public sealed class VillagerTask
    {
        public Vector3 Spot;
        /// <summary>Clip played on arrival: Idle, Talk, Work, Gather, Sit, Lie, Chop, Plant, Harvest, Water, Throw, Lantern…</summary>
        public string Action;
        /// <summary>Tool shown in the right hand while working (Prop_Axe, Prop_Pickaxe, Prop_Hammer, Prop_Spear) or null.</summary>
        public string Tool;
        /// <summary>Clip used on the way there (Walk, or Carry when bringing goods home).</summary>
        public string WalkClip;
        public float Duration;
        /// <summary>Optional waypoints (around obstacles, along roads) before the spot.</summary>
        public List<Vector3> Route;
        /// <summary>What to turn towards while working (the tree, the rock, the fire).</summary>
        public Vector3? Face;
        /// <summary>Model carried home on the way there (k_wood, k_stone, wicker_basket_01…) or null.</summary>
        public string Carry;
        /// <summary>The open cell the path ends in before the last steps to the spot (beside a tree or rock).</summary>
        public (int x, int y)? Approach;
    }

    /// <summary>
    /// A villager: walks (along the roads when there are any) to the place of their job and does the work
    /// there with the right tool: woodcutters chop, miners and quarriers strike, farmers sow, water and
    /// harvest, hunters and fishers throw spears, builders hammer. Uses the animated CC0 people
    /// (art/models/Person_*) with their painted texture set, a lighter mesh for the distant view, and
    /// per-person skin tone, dyed clothes and hair. The node faces along its −Z (Godot's forward); inside it
    /// things are placed in the Unity version's local space (see <see cref="U"/>).
    /// </summary>
    public sealed partial class Villager : Node3D
    {
        /// <summary>Set by the world: the next task, given where the villager stands now.</summary>
        public Func<Vector3, VillagerTask> NextTask;
        /// <summary>The clip playing now (Walk, Chop, Plant…).</summary>
        public string Clip => current;
        public string Tool => shownTool;
        bool child, elder;
        public bool IsChild => child;
        NeedBubble need;
        public Terrain Ground;
        /// <summary>Moved by someone else (a trader with the caravan): no tasks of their own.</summary>
        public bool Puppet;

        VillagerTask task;
        bool hasTask;
        readonly Queue<Vector3> route = new Queue<Vector3>();
        Vector3 target;
        float wait;
        float speed;
        Node3D body;
        /// <summary>Height of the body model before scaling (metres), to size the seat.</summary>
        float bodyHeight = 1.78f;
        AnimationPlayer anim;
        string current;
        readonly Dictionary<string, List<Node3D>> tools = new Dictionary<string, List<Node3D>>();
        string shownTool;
        float clock;

        /// <summary>Survival need shown over the head ("food", "winter", "sick") or null.</summary>
        public void SetNeed(string iconName)
        {
            if (need == null) { if (iconName == null) return; need = NeedBubble.Attach(this); }
            need.Show(iconName);
        }

        // Skin tones of the valley's people (sRGB), from fair to dark.
        static readonly Color[] SkinTones =
        {
            new Color(0.87f, 0.68f, 0.56f), new Color(0.80f, 0.60f, 0.47f), new Color(0.70f, 0.50f, 0.37f),
            new Color(0.56f, 0.39f, 0.28f), new Color(0.42f, 0.29f, 0.21f),
        };
        // Clothing: plant and earth dyes on wool and linen (oat, weld green, woad, madder, ochre, walnut, lichen, moss),
        // a little brighter than real dyes, as on the painted icons.
        static readonly Color[] HideTints =
        {
            new Color(0.66f, 0.57f, 0.42f), new Color(0.46f, 0.55f, 0.30f), new Color(0.32f, 0.45f, 0.62f),
            new Color(0.66f, 0.31f, 0.22f), new Color(0.76f, 0.60f, 0.30f), new Color(0.45f, 0.33f, 0.24f),
            new Color(0.52f, 0.42f, 0.58f), new Color(0.30f, 0.48f, 0.44f),
        };
        static readonly Color[] HairColors =
        {
            new Color(0.09f, 0.065f, 0.05f), new Color(0.18f, 0.12f, 0.07f), new Color(0.30f, 0.20f, 0.11f),
            new Color(0.05f, 0.045f, 0.04f), new Color(0.42f, 0.30f, 0.18f), new Color(0.55f, 0.36f, 0.20f),
        };

        static readonly Dictionary<string, PackedScene> models = new Dictionary<string, PackedScene>();

        static PackedScene Model(string name)
        {
            if (models.TryGetValue(name, out var m)) return m;
            string path = $"res://art/models/{name}.fbx";
            m = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            if (m == null) GD.Print($"[FOUNDERS] {name} model missing.");
            return models[name] = m;
        }

        public override void _Ready()
        {
            speed = (float)GD.RandRange(1.0, 1.25) * (elder ? 0.75f : 1f);
            wait = (float)GD.RandRange(0.0, 3.0);
            target = Position;
            anim = FindPlayer(this);
            if (anim != null)
            {
                Loop(anim);
                Play(GD.Randf() < 0.5f ? "Idle" : "Talk", 0f);
                // Desynchronise identical loops.
                if (anim.CurrentAnimation != "") anim.Seek(GD.Randf() * anim.CurrentAnimationLength, true);
            }
        }

        public static AnimationPlayer FindPlayer(Node n)
        {
            if (n is AnimationPlayer p) return p;
            foreach (var c in n.GetChildren())
            {
                var found = FindPlayer(c);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Every clip of the model loops (the files store them as single takes).</summary>
        public static void Loop(AnimationPlayer player)
        {
            foreach (var name in player.GetAnimationList())
            {
                var a = player.GetAnimation(name);
                if (a != null && a.LoopMode == Animation.LoopModeEnum.None) a.LoopMode = Animation.LoopModeEnum.Linear;
            }
        }

        /// <summary>The clip whose name ends with <paramref name="clip"/> ("Armature|Walk" for "Walk").</summary>
        public static string FindClip(AnimationPlayer player, string clip)
        {
            foreach (var name in player.GetAnimationList())
                if (name.EndsWith("|" + clip, StringComparison.Ordinal) || name == clip || name.EndsWith("/" + clip, StringComparison.Ordinal))
                    return name;
            return null;
        }

        void Play(string clip, float fade = 0.25f)
        {
            if (anim == null || current == clip) return;
            var name = FindClip(anim, clip);
            if (name == null)
            {
                if (clip != "Idle") Play("Idle", fade);
                return;
            }
            anim.Play(name, fade);
            current = clip;
        }

        Node3D carried;
        string carriedId;

        /// <summary>Logs on the shoulder, a stone or a basket of berries on the way home.</summary>
        void ShowCarry(string id)
        {
            if (id == carriedId) return;
            carried?.QueueFree();
            carried = null;
            carriedId = id;
            if (id == null || !ModelLibrary.HasModel(id)) return;
            float s = id switch
            {
                "k_wood" => 4.2f, "k_stone" => 2.2f, "k_planks" => 1.4f, "k_log" => 0.8f, "k_box" => 1.8f,
                "k_bedroll" => 1.2f, "ceramic_pot" => 0.6f, _ => 1.2f,
            };
            var at = id == "wicker_basket_01" ? new Vector3(0.22f, 0.78f, 0.12f) : new Vector3(0f, 1.25f, 0.06f);
            carried = ModelLibrary.Place(id, this, at, s, 90f);
            NoShadows(carried);
        }

        static void NoShadows(Node n)
        {
            if (n is GeometryInstance3D g) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            foreach (var c in n.GetChildren()) NoShadows(c);
        }

        Node3D seat;

        /// <summary>A log to sit on (the sitting clip is made for a seat at knee height).</summary>
        void ShowSeat(bool on)
        {
            if (!on)
            {
                seat?.QueueFree();
                seat = null;
                return;
            }
            if (seat != null || !ModelLibrary.HasModel("k_log")) return;
            // The clip's seat is 0.45 m high and 0.34 m behind the feet for a 1.78 m person.
            float k = body != null ? body.Scale.Y * bodyHeight / 1.78f : 1f;
            float logHeight = Mathf.Max(0.01f, ModelLibrary.Size("k_log").Y);
            seat = ModelLibrary.Place("k_log", this, new Vector3(0f, 0f, -0.3f * k), 0.43f * k / logHeight, 90f, new Vector3(1f, 1f, 0.55f));
        }

        void ShowTool(string tool)
        {
            if (tool == shownTool) return;
            if (shownTool != null && tools.TryGetValue(shownTool, out var old)) foreach (var n in old) n.Visible = false;
            shownTool = tool;
            if (tool != null && tools.TryGetValue(tool, out var parts)) foreach (var n in parts) n.Visible = true;
        }

        /// <summary>Drops the current task; the villager asks for a new one right away.</summary>
        public void Interrupt()
        {
            ShowSeat(false);
            wait = 0f;
            hasTask = false;
            route.Clear();
            target = Position;
        }

        void Face(Vector3 dir, float rate, float delta)
        {
            dir.Y = 0;
            if (dir.LengthSquared() < 1e-4f) return;
            var want = Basis.LookingAt(dir.Normalized(), Vector3.Up);
            var q = Basis.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), Mathf.Clamp(rate * delta, 0f, 1f));
            Basis = new Basis(q);
        }

        /// <summary>Places a puppet: walking (with goods on the shoulder) or standing still, facing <paramref name="facing"/>.</summary>
        public void Steer(Vector3 position, Vector3 facing, bool walking, string carry = null)
        {
            Position = position;
            Face(facing, 8f, U.Dt);
            ShowCarry(walking ? carry : null);
            Play(walking ? (carry != null ? "Carry" : "Walk") : "Idle");
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            clock += dt;
            if (Puppet) return;
            if (wait > 0f)
            {
                wait -= dt;
                // Turn to the work (the tree being cut, the fire, the building being raised).
                if (task?.Face is Vector3 face) Face(face - Position, 8f, dt);
                return;
            }
            var to = target - Position;
            to.Y = 0;
            if (to.Length() < 0.15f)
            {
                if (route.Count > 0)
                {
                    target = route.Dequeue();
                    return;
                }
                if (hasTask)
                {
                    hasTask = false;
                    ShowCarry(null);
                    ShowTool(task.Tool);
                    // Face the work before the first swing: the walk ends looking along the path.
                    if (task.Face is Vector3 f) Face(f - Position, 1000f, 1f);
                    ShowSeat(task.Action == "Sit");
                    Play(task.Action);
                    wait = task.Duration;
                    return;
                }
                if (NextTask == null) { wait = 2f; return; }
                task = NextTask(Position);
                hasTask = true;
                ShowSeat(false);
                route.Clear();
                if (task.Route != null) foreach (var p in task.Route) route.Enqueue(p);
                target = route.Count > 0 ? route.Dequeue() : task.Spot;
                route.Enqueue(task.Spot);
                // Tools stay in hand on the way to work; carried goods replace them.
                ShowTool(task.Carry != null || task.WalkClip == "Carry" ? null : task.Tool);
                ShowCarry(task.Carry);
                return;
            }
            bool onRoad = task?.Route != null && route.Count > 0;
            Play(task?.WalkClip ?? "Walk");
            float len = to.Length();
            var step = to / len * Mathf.Min(speed * (onRoad ? 1.3f : 1f) * dt, len);
            var next = Position + step;
            if (Ground != null) next.Y = Ground.HeightAt(next);
            Position = next;
            Face(to, 6f, dt);
        }

        // ------------------------------------------------------------------ looks

        sealed class PersonModel
        {
            public string Model, Gender, Age;
            public bool Textured;
            /// <summary>Average painted skin colour (linear) and the default dye and hair colours (sRGB).</summary>
            public Color Skin, Main, Hair;
            public float Neutral = 0.5f;
            /// <summary>False: the clothes keep the pack's own colours (only skin and hair vary).</summary>
            public bool Dye = true;
        }
        static List<PersonModel> personModels;

        static Color ColorOf(List<object> a, Color fallback) =>
            a != null && a.Count >= 3 ? new Color(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2]), 1f) : fallback;

        static List<PersonModel> PersonModels()
        {
            if (personModels != null) return personModels;
            personModels = new List<PersonModel>();
            string text = Godot.FileAccess.GetFileAsString("res://data/people.json");
            if (string.IsNullOrEmpty(text)) return personModels;
            var root = new JObj(Json.Parse(text));
            foreach (var p in root.Objs("people"))
                personModels.Add(new PersonModel
                {
                    Model = p.Str("model"), Gender = p.Str("gender"), Age = p.Str("age"),
                    Textured = p.Has("textures"),
                    Skin = ColorOf(p.OptArr("skin"), new Color(0.5f, 0.32f, 0.24f)),
                    Main = ColorOf(p.OptArr("main"), new Color(0.55f, 0.47f, 0.36f)),
                    Hair = ColorOf(p.OptArr("hair"), new Color(0.18f, 0.12f, 0.07f)),
                    Neutral = (float)(p.OptNum("neutral") ?? 0.5),
                    Dye = p.OptBool("dye", true),
                });
            return personModels;
        }

        /// <summary>
        /// The albedo colour that turns a texture of linear colour <paramref name="baseLinear"/> into the
        /// sRGB colour <paramref name="wanted"/> (the material converts its colour from sRGB to linear).
        /// </summary>
        static Color Ratio(Color wanted, Color baseLinear)
        {
            var w = wanted.SrgbToLinear();
            return new Color(w.R / Mathf.Max(0.004f, baseLinear.R), w.G / Mathf.Max(0.004f, baseLinear.G),
                             w.B / Mathf.Max(0.004f, baseLinear.B), 1f).LinearToSrgb();
        }

        static readonly Dictionary<string, Material> personMaterials = new Dictionary<string, Material>();

        /// <summary>The painted texture set of a model, tinted (one material per model and colour).</summary>
        static Material PersonMaterial(string model, Color tint)
        {
            string key = model + "|" + tint.ToHtml(false);
            if (personMaterials.TryGetValue(key, out var m)) return m;
            var mat = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Roughness = 0.88f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            };
            string painted = $"res://art/textures/People/{model}_painted.jpg";
            string albedo = $"res://art/textures/People/{model}_albedo.jpg";
            string normal = $"res://art/textures/People/{model}_normal.jpg";
            if (ResourceLoader.Exists(painted)) mat.AlbedoTexture = GD.Load<Texture2D>(painted);
            else if (ResourceLoader.Exists(albedo)) mat.AlbedoTexture = GD.Load<Texture2D>(albedo);
            if (ResourceLoader.Exists(normal))
            {
                mat.NormalEnabled = true;
                mat.NormalTexture = GD.Load<Texture2D>(normal);
                mat.NormalScale = 0.6f;
            }
            return personMaterials[key] = mat;
        }

        static Material Plain(Color c)
        {
            string key = "plain|" + c.ToHtml(false);
            if (personMaterials.TryGetValue(key, out var m)) return m;
            return personMaterials[key] = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.9f };
        }

        /// <summary>
        /// A villager: one of the people (man, woman or elder), or a child (a smaller person). Skin tone, the
        /// dye of the main garment and the hair colour vary per person so no two look alike.
        /// </summary>
        public static Villager Spawn(Node3D parent, Vector3 at, int seed, bool female, bool child = false, string job = null)
        {
            var v = new Villager { Name = child ? "Child" : "Villager" };
            v.Position = at;
            v.Rotation = new Vector3(0, (float)GD.RandRange(0, Mathf.Tau), 0);
            var rng = new Random(seed * 7919 + 17);
            bool elder = !child && rng.Next(7) == 0;
            v.child = child;
            v.elder = elder;
            var pool = PersonModels().FindAll(p => p.Gender == (female ? "female" : "male") && p.Age == (elder ? "elder" : "adult"));
            if (pool.Count == 0) pool = PersonModels();
            var person = pool.Count > 0 ? pool[rng.Next(pool.Count)] : null;
            string modelName = person != null ? person.Model.Replace("Models/", "") : female ? "Person_Farmer_Female" : "Person_Farmer_Male";
            var scene = Model(modelName);

            if (scene != null)
            {
                var holder = new Node3D { Name = "Body" };
                v.AddChild(holder);
                var inst = scene.Instantiate<Node3D>();
                // The files face +Z; the villager faces −Z like every Godot node (and the Unity-space parts inside it).
                inst.RotateY(Mathf.Pi);
                holder.AddChild(inst);
                v.body = holder;
                // Chunky, soft painted figures (bigger heads and hands, rounded shapes).
                PersonStyle.Apply(inst);

                var skin = SkinTones[rng.Next(SkinTones.Length)];
                var cloth = HideTints[rng.Next(HideTints.Length)];
                var hair = elder ? new Color(0.70f, 0.69f, 0.66f) : HairColors[rng.Next(HairColors.Length)];
                bool recolourClothes = rng.Next(3) != 0;
                bool textured = person != null && person.Textured;
                var meshes = new List<MeshInstance3D>();
                ModelLibrary.Collect(inst, meshes);
                var neutral = new Color(person?.Neutral ?? 0.5f, person?.Neutral ?? 0.5f, person?.Neutral ?? 0.5f);
                foreach (var mi in meshes)
                {
                    if (mi.Mesh == null) continue;
                    if (mi.Name.ToString().StartsWith("Prop_", StringComparison.Ordinal))
                    {
                        // Hand tools: wooden handles and knapped stone heads.
                        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                        {
                            string sn = mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                            mi.SetSurfaceOverrideMaterial(i, ModelLibrary.Surface(sn.StartsWith("Stone", StringComparison.Ordinal) ? "DarkRock" : "RoughWood"));
                        }
                        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                        continue;
                    }
                    for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    {
                        string n = mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                        Material mat;
                        if (textured)
                        {
                            // One texture set; the zone's tint dyes it (grey detail × colour) or shifts the skin tone.
                            Color tint = Colors.White;
                            if (n.StartsWith("Skin", StringComparison.Ordinal)) tint = Ratio(skin, person.Skin);
                            else if (n.StartsWith("Hair", StringComparison.Ordinal)) tint = Ratio(elder ? person.Hair : hair, neutral);
                            else if (n.StartsWith("Main", StringComparison.Ordinal) && person.Dye) tint = Ratio(recolourClothes ? cloth : person.Main, neutral);
                            mat = PersonMaterial(modelName, tint);
                        }
                        else
                        {
                            var own = mi.Mesh.SurfaceGetMaterial(i) as BaseMaterial3D;
                            var c = own?.AlbedoColor ?? new Color(0.5f, 0.45f, 0.4f);
                            if (n.StartsWith("Skin", StringComparison.Ordinal)) c = skin;
                            else if (n.StartsWith("Hair", StringComparison.Ordinal)) c = hair;
                            else if ((n.StartsWith("Main", StringComparison.Ordinal) || n.StartsWith("Shirt", StringComparison.Ordinal)) && recolourClothes) c = cloth;
                            mat = Plain(c);
                        }
                        mi.SetSurfaceOverrideMaterial(i, mat);
                    }
                    mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                }
                // Distant villagers draw the lighter mesh (Body_LOD1); people never disappear.
                bool hasLod = meshes.Exists(m => m.Name.ToString().StartsWith("Body_LOD1", StringComparison.Ordinal));
                if (hasLod)
                    foreach (var mi in meshes)
                    {
                        string n = mi.Name.ToString();
                        if (n.StartsWith("Prop_", StringComparison.Ordinal)) continue;
                        if (n.StartsWith("Body_LOD1", StringComparison.Ordinal)) mi.VisibilityRangeBegin = 35f;
                        else mi.VisibilityRangeEnd = 35f;
                    }
                // Tools are hidden until a job needs them.
                foreach (var mi in meshes)
                {
                    string n = mi.Name.ToString();
                    if (!n.StartsWith("Prop_", StringComparison.Ordinal)) continue;
                    string tool = n;
                    int cut = tool.IndexOfAny(new[] { '.', '_' }, 5);
                    if (cut > 0) tool = tool.Substring(0, cut);
                    mi.Visible = false;
                    // The handle and the head may be separate meshes: both answer to the tool's name.
                    if (!v.tools.TryGetValue(tool, out var list)) v.tools[tool] = list = new List<Node3D>();
                    list.Add(mi);
                }
                // Real height: every model is scaled to the height of its person.
                var bounds = ModelLibrary.LocalBounds(holder, mi => !mi.Name.ToString().StartsWith("Prop_", StringComparison.Ordinal)
                                                                    && !mi.Name.ToString().StartsWith("Body_LOD1", StringComparison.Ordinal));
                float height = child ? 1.0f + (float)rng.NextDouble() * 0.15f : (female ? 1.6f : 1.7f) + (float)rng.NextDouble() * 0.12f;
                if (elder) height -= 0.06f;
                if (bounds.Size.Y > 0.01f)
                {
                    holder.Scale = Vector3.One * (height / bounds.Size.Y);
                    v.bodyHeight = bounds.Size.Y;
                }
                parent.AddChild(v);
                return v;
            }

            // A simple figure when the model is missing.
            var figure = new Node3D { Name = "Body" };
            v.AddChild(figure);
            v.body = figure;
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(0, 0.55f, 0), new Vector3(0.34f, 0.5f, 0.28f), ModelLibrary.Surface("Hide"));
            Parts.Prim(PrimitiveType.Sphere, figure, new Vector3(0, 1.18f, 0), Vector3.One * 0.22f, "Skin");
            Parts.Prim(PrimitiveType.Sphere, figure, new Vector3(0, 1.24f, -0.02f), new Vector3(0.24f, 0.15f, 0.24f), "Hair");
            parent.AddChild(v);
            return v;
        }
    }

    /// <summary>
    /// A round survival bubble over a villager's head (hunger, cold, sickness): a dark disc with a white
    /// icon that always faces the camera and bobs gently.
    /// </summary>
    public sealed partial class NeedBubble : Node3D
    {
        static Texture2D disc;
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        Sprite3D icon;
        string shown;
        float phase, t;

        static Texture2D Disc()
        {
            if (disc != null) return disc;
            const int n = 64;
            var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp((1f - d) * 18f, 0f, 1f);
                    img.SetPixel(x, y, d > 0.88f ? new Color(1f, 1f, 1f, a) : new Color(0.12f, 0.13f, 0.15f, a * 0.92f));
                }
            return disc = ImageTexture.CreateFromImage(img);
        }

        static Texture2D Icon(string name)
        {
            if (icons.TryGetValue(name, out var t)) return t;
            string path = $"res://art/ui/Icons/{name}.png";
            return icons[name] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }

        static Sprite3D Card(Node3D parent, Texture2D tex, float size, float z)
        {
            var s = new Sprite3D
            {
                Texture = tex,
                PixelSize = size / Mathf.Max(1, tex.GetWidth()),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                NoDepthTest = false,
                AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, 0, z),
                RenderPriority = 2,
            };
            parent.AddChild(s);
            return s;
        }

        public static NeedBubble Attach(Node3D villager)
        {
            var b = new NeedBubble { Name = "Need", Position = new Vector3(0, 2.35f, 0) };
            villager.AddChild(b);
            Card(b, Disc(), 0.62f, 0f).RenderPriority = 1;
            b.phase = GD.Randf() * 6f;
            b.Visible = false;
            return b;
        }

        /// <summary>Shows an icon (e.g. "food", "winter") or hides the bubble with null.</summary>
        public void Show(string iconName)
        {
            if (iconName == shown) return;
            shown = iconName;
            icon?.QueueFree();
            icon = null;
            Visible = iconName != null;
            if (iconName == null) return;
            var tex = Icon(iconName);
            if (tex != null) icon = Card(this, tex, 0.38f, 0.01f);
        }

        public override void _Process(double delta)
        {
            if (!Visible) return;
            t += (float)delta;
            Position = new Vector3(0, 2.35f + Mathf.Sin(t * 2.2f + phase) * 0.06f, 0);
        }
    }
}
