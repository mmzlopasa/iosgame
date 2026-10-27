using System.Collections.Generic;
using Founders.Art;
using Founders.World;
using Godot;

namespace Founders.People
{
    /// <summary>
    /// A farm animal (CC0 Quaternius Farm Animals: cow, sheep, pig, horse, llama) that wanders and grazes
    /// inside its pen or pasture. Size and colours come from the model; it walks with its own walk cycle,
    /// or hops gently if the model has none.
    /// </summary>
    public sealed partial class Animal : Node3D
    {
        static readonly Dictionary<string, PackedScene> models = new Dictionary<string, PackedScene>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        /// <summary>Body length in metres per kind.</summary>
        static readonly Dictionary<string, float> Lengths = new Dictionary<string, float>
        {
            ["cow"] = 2.0f, ["sheep"] = 1.1f, ["pig"] = 1.05f, ["horse"] = 2.1f, ["llama"] = 1.5f,
        };

        public Terrain Ground;
        /// <summary>Led by someone else (a pack horse with a caravan): no wandering of its own.</summary>
        public bool Puppet;
        Vector3 home;
        float radius;
        Vector3 target;
        float wait;
        float speed;
        AnimationPlayer anim;
        Node3D body;
        bool hasWalk;
        string current;
        float clock;

        static PackedScene Model(string kind)
        {
            if (models.TryGetValue(kind, out var m)) return m;
            string name = "Animal_" + char.ToUpperInvariant(kind[0]) + kind.Substring(1);
            string path = $"res://art/models/{name}.fbx";
            m = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            if (m == null) GD.Print($"[FOUNDERS] {name} model missing.");
            return models[kind] = m;
        }

        /// <summary>A matte material with the colour the model was painted with.</summary>
        static Material Painted(Material source)
        {
            var c = source is BaseMaterial3D b ? b.AlbedoColor : new Color(0.8f, 0.8f, 0.8f);
            string key = c.ToHtml(false);
            if (materials.TryGetValue(key, out var m)) return m;
            return materials[key] = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.85f };
        }

        public static Animal Spawn(string kind, Node3D parent, Vector3 home, float radius, int seed, Terrain ground)
        {
            var scene = Model(kind);
            if (scene == null) return null;
            var rng = new System.Random(seed);
            var a = new Animal { Name = "Animal_" + kind };
            var start = home + new Vector3(((float)rng.NextDouble() - 0.5f) * radius, 0, ((float)rng.NextDouble() - 0.5f) * radius);
            a.Position = ground != null ? ground.OnGround(start) : start;
            a.Rotation = new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0);
            var holder = new Node3D { Name = "Body" };
            a.AddChild(holder);
            var inst = scene.Instantiate<Node3D>();
            inst.RotateY(Mathf.Pi); // the files face +Z, the animal walks along its −Z
            holder.AddChild(inst);
            var meshes = new List<MeshInstance3D>();
            ModelLibrary.Collect(inst, meshes);
            foreach (var mi in meshes)
            {
                if (mi.Mesh == null) continue;
                for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) mi.SetSurfaceOverrideMaterial(i, Painted(mi.Mesh.SurfaceGetMaterial(i)));
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            }
            // Real size: scale the model so its body is as long as the animal should be.
            var bounds = ModelLibrary.LocalBounds(holder);
            float size = Mathf.Max(bounds.Size.X, bounds.Size.Z);
            float length = Lengths.TryGetValue(kind, out var l) ? l : 1.5f;
            if (size > 0.01f) holder.Scale = Vector3.One * (length / size * (0.92f + (float)rng.NextDouble() * 0.16f));
            a.body = holder;
            a.home = home;
            a.radius = radius;
            a.Ground = ground;
            a.speed = kind == "horse" ? 0.9f : kind == "cow" ? 0.55f : 0.6f;
            a.wait = (float)rng.NextDouble() * 4f;
            parent.AddChild(a);
            return a;
        }

        public override void _Ready()
        {
            anim = Villager.FindPlayer(this);
            target = Position;
            if (anim != null)
            {
                Villager.Loop(anim);
                hasWalk = Villager.FindClip(anim, "WalkSlow") != null || Villager.FindClip(anim, "Walk") != null;
                Play("Idle");
                if (anim.CurrentAnimation != "") anim.Seek(GD.Randf() * anim.CurrentAnimationLength, true);
            }
        }

        void Play(string clip)
        {
            if (anim == null || current == clip) return;
            var name = Villager.FindClip(anim, clip) ?? (clip == "WalkSlow" ? Villager.FindClip(anim, "Walk") : null);
            if (name == null) return;
            anim.Play(name, 0.3f);
            current = clip;
        }

        void Face(Vector3 dir, float rate, float dt)
        {
            dir.Y = 0;
            if (dir.LengthSquared() < 1e-4f) return;
            var want = Basis.LookingAt(dir.Normalized(), Vector3.Up);
            Basis = new Basis(Basis.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), Mathf.Clamp(rate * dt, 0f, 1f)));
        }

        /// <summary>Places a led animal: walking or standing, facing <paramref name="facing"/>.</summary>
        public void Steer(Vector3 position, Vector3 facing, bool walking)
        {
            Position = position;
            Face(facing, 6f, U.Dt);
            if (body == null) return;
            Play(walking && hasWalk ? "WalkSlow" : "Idle");
            body.Position = walking && !hasWalk ? new Vector3(0, Mathf.Abs(Mathf.Sin(clock * 9f)) * 0.06f, 0) : Vector3.Zero;
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            clock += dt;
            if (Puppet) return;
            if (wait > 0f)
            {
                wait -= dt;
                return;
            }
            var to = target - Position;
            to.Y = 0;
            if (to.Length() < 0.1f)
            {
                // Graze for a while, then amble to another spot of the pen.
                Play("Idle");
                body.Position = Vector3.Zero;
                wait = (float)GD.RandRange(3.0, 9.0);
                var p = home + new Vector3((float)GD.RandRange(-radius, radius), 0, (float)GD.RandRange(-radius, radius));
                target = Ground != null ? Ground.OnGround(p) : p;
                return;
            }
            Play(hasWalk ? "WalkSlow" : "Idle");
            float len = to.Length();
            var next = Position + to / len * Mathf.Min(speed * dt, len);
            if (Ground != null) next.Y = Ground.HeightAt(next);
            Position = next;
            Face(to, 3f, dt);
            // Animals without a walk cycle trot with a small bounce.
            if (!hasWalk) body.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(clock * 9f)) * 0.06f, 0);
        }
    }
}
