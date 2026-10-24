using System.Collections.Generic;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// A farm animal (CC0 Quaternius Farm Animals: cow, sheep, pig, horse, llama) that wanders and
    /// grazes inside its pen or pasture. Size and colours come from the model; it walks with its
    /// own walk cycle, or hops gently if the model has none.
    /// </summary>
    public class Animal : MonoBehaviour
    {
        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        /// <summary>Body length in metres per kind.</summary>
        static readonly Dictionary<string, float> Lengths = new Dictionary<string, float>
        {
            ["cow"] = 2.0f, ["sheep"] = 1.1f, ["pig"] = 1.05f, ["horse"] = 2.1f, ["llama"] = 1.5f,
        };

        public WorldTerrain Ground;
        /// <summary>Led by someone else (a pack horse with a caravan): no wandering of its own.</summary>
        public bool Puppet;
        Vector3 home;
        float radius;
        Vector3 target;
        float wait;
        float speed;
        Animation anim;
        Transform body;
        bool hasWalk;
        string current;

        static GameObject Model(string kind)
        {
            if (models.TryGetValue(kind, out var m)) return m;
            string name = "Animal_" + char.ToUpperInvariant(kind[0]) + kind.Substring(1);
            m = Resources.Load<GameObject>("Models/" + name);
            if (m == null) Debug.LogWarning($"[FOUNDERS] {name} model missing.");
            return models[kind] = m;
        }

        /// <summary>A URP material with the colour the model was painted with.</summary>
        static Material Painted(Material source)
        {
            Color c = source != null && source.HasProperty("_Color") ? source.color : new Color(0.8f, 0.8f, 0.8f);
            if (source != null && source.HasProperty("_BaseColor")) c = source.GetColor("_BaseColor");
            string key = ColorUtility.ToHtmlStringRGB(c);
            if (materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Palette.Get("Tunic")) { name = "Animal_" + key };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return materials[key] = m;
        }

        public static Animal Spawn(string kind, Transform parent, Vector3 home, float radius, int seed, WorldTerrain ground)
        {
            var prefab = Model(kind);
            if (prefab == null) return null;
            var rng = new System.Random(seed);
            var root = new GameObject("Animal_" + kind).transform;
            root.SetParent(parent, false);
            var start = home + new Vector3(((float)rng.NextDouble() - 0.5f) * radius, 0, ((float)rng.NextDouble() - 0.5f) * radius);
            root.position = ground != null ? ground.OnGround(start) : start;
            root.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            var body = Instantiate(prefab, root);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var mats = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) mats[i] = Painted(src[i]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = false;
            }
            // Real size: scale the model so its body is as long as the animal should be.
            var bounds = new Bounds(root.position, Vector3.zero);
            foreach (var r in body.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float size = Mathf.Max(bounds.size.x, bounds.size.z);
            float length = Lengths.TryGetValue(kind, out var l) ? l : 1.5f;
            if (size > 0.01f) body.transform.localScale *= length / size * (0.92f + (float)rng.NextDouble() * 0.16f);
            var a = root.gameObject.AddComponent<Animal>();
            a.home = home;
            a.radius = radius;
            a.Ground = ground;
            a.speed = kind == "horse" ? 0.9f : kind == "cow" ? 0.55f : 0.6f;
            a.wait = (float)rng.NextDouble() * 4f;
            return a;
        }

        void Start()
        {
            body = transform.GetChild(0);
            anim = GetComponentInChildren<Animation>();
            target = transform.position;
            if (anim != null)
            {
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
                foreach (AnimationState st in anim)
                {
                    st.wrapMode = WrapMode.Loop;
                    if (st.name.EndsWith("WalkSlow") || st.name.EndsWith("Walk")) hasWalk = true;
                }
                Play("Idle");
                foreach (AnimationState st in anim) st.normalizedTime = Random.value;
            }
        }

        void Play(string clip)
        {
            if (anim == null || current == clip) return;
            foreach (AnimationState st in anim)
            {
                if (!st.name.EndsWith(clip)) continue;
                anim.CrossFade(st.name, 0.3f);
                current = clip;
                return;
            }
        }

        /// <summary>Places a led animal: walking or standing, facing <paramref name="facing"/>.</summary>
        public void Steer(Vector3 position, Vector3 facing, bool walking)
        {
            transform.position = position;
            facing.y = 0;
            if (facing.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 6f * Time.deltaTime);
            if (body == null) return; // not started yet
            Play(walking && hasWalk ? "WalkSlow" : "Idle");
            body.localPosition = walking && !hasWalk ? new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.06f, 0) : Vector3.zero;
        }

        void Update()
        {
            if (Puppet) return;
            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                return;
            }
            var to = target - transform.position;
            to.y = 0;
            if (to.magnitude < 0.1f)
            {
                // Graze for a while, then amble to another spot of the pen.
                Play("Idle");
                body.localPosition = Vector3.zero;
                wait = Random.Range(3f, 9f);
                var p = home + new Vector3(Random.Range(-radius, radius), 0, Random.Range(-radius, radius));
                target = Ground != null ? Ground.OnGround(p) : p;
                return;
            }
            Play(hasWalk ? "WalkSlow" : "Idle");
            var next = transform.position + Vector3.ClampMagnitude(to.normalized * speed * Time.deltaTime, to.magnitude);
            if (Ground != null) next.y = Ground.HeightAt(next);
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 3f * Time.deltaTime);
            // Animals without a walk cycle trot with a small bounce.
            if (!hasWalk) body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.06f, 0);
        }
    }
}
