using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Founders
{
    /// <summary>Fire light flicker. Lamps (<see cref="NightOnly"/>) only shine after dusk.</summary>
    public class Flicker : MonoBehaviour
    {
        public bool NightOnly;
        Light lightSource;
        float baseIntensity;
        float seed;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            baseIntensity = lightSource.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * 3.2f);
            float night = NightOnly ? Mathf.Lerp(0.15f, 1f, Atmosphere.Nightness) : 1f;
            lightSource.intensity = baseIntensity * (0.75f + 0.5f * n) * night;
            lightSource.enabled = night > 0.2f;
        }
    }

    /// <summary>
    /// Isometric-style strategy camera. One finger drags the map, two fingers pinch to zoom,
    /// a short tap selects a cell. In the editor: left mouse drags/taps, wheel zooms.
    /// Touches that start on the UI are ignored (see <see cref="UI.GameUi.BlocksPointer"/>).
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        /// <summary>Called with the screen position (pixels, bottom-left origin) of a tap on the world.</summary>
        public Action<Vector2> OnTap;
        public Func<Vector2, bool> IsBlocked = _ => false;
        /// <summary>While true, one-finger drags paint (roads, fences) instead of moving the camera.</summary>
        public Func<bool> PaintMode = () => false;
        public Action<Vector2> OnPaint;
        public Action OnPaintEnd;
        public Rect Bounds = new Rect(-4, -36, 40, 40); // x/z extent the focus point may move in
        /// <summary>Ground height at a world position (hills and mountains); null means flat ground at y = 0.</summary>
        public Func<Vector3, float> HeightAt;

        Vector3 focus;
        float distance = 26f;
        Vector3 moveTarget;
        float moveUntil;
        float zoomTarget = -1f;

        const float MinDistance = 9f;
        const float MaxDistance = 80f;
        float Pitch => Mathf.Lerp(52f, 40f, Mathf.InverseLerp(MinDistance, MaxDistance, distance));
        public const float Yaw = -45f;
        const float TapSlop = 12f; // pixels

        Vector2 pressStart;
        bool pressBlocked;
        bool dragging;
        bool pinching;
        float lastPinch;
        Vector3 lastMouse;

        void Awake()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                Cam.fieldOfView = 34f;
                Cam.nearClipPlane = 0.5f;
                Cam.farClipPlane = 400f;
            }
            Cam.transform.SetParent(transform, false);
            Apply();
        }

        public void SetFocus(Vector3 point, bool instant = false, float zoom = -1f)
        {
            if (instant)
            {
                focus = point;
                if (zoom > 0) distance = zoom;
                Apply();
                return;
            }
            moveTarget = point;
            moveUntil = Time.time + 0.6f;
            zoomTarget = zoom;
        }

        void LateUpdate()
        {
            HandleInput();
            if (Time.time < moveUntil)
            {
                float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
                focus = Vector3.Lerp(focus, moveTarget, k);
                if (zoomTarget > 0) distance = Mathf.Lerp(distance, zoomTarget, k);
            }
            // The focus rides over hills and mountains.
            if (HeightAt != null)
            {
                float ground = Mathf.Max(HeightAt(focus), WorldTerrain.WaterLevel);
                focus.y = Mathf.Lerp(focus.y, ground, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
            Apply();
        }

        void Apply()
        {
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            var position = focus + rotation * new Vector3(0, 0, -distance);
            // Never inside a mountain between the camera and the focus.
            if (HeightAt != null) position.y = Mathf.Max(position.y, HeightAt(position) + 2.5f);
            Cam.transform.position = position;
            Cam.transform.rotation = Quaternion.LookRotation(focus - position);
        }

        void Pan(Vector2 screenDelta)
        {
            moveUntil = 0f;
            float worldPerPixel = 2f * distance * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            var right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
            var forward = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            focus -= (right * screenDelta.x + forward * screenDelta.y / Mathf.Sin(Pitch * Mathf.Deg2Rad)) * worldPerPixel;
            focus.x = Mathf.Clamp(focus.x, Bounds.xMin, Bounds.xMax);
            focus.z = Mathf.Clamp(focus.z, Bounds.yMin, Bounds.yMax);
        }

        void Zoom(float factor)
        {
            moveUntil = 0f;
            distance = Mathf.Clamp(distance * factor, MinDistance, MaxDistance);
        }

        /// <summary>Point on the ground (the terrain, or the plane y = 0) under a screen position.</summary>
        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
            if (HeightAt != null)
            {
                // March along the ray until it dips below the terrain, then refine.
                float prev = 0f;
                for (float t = 0.5f; t < 420f; t += 0.5f)
                {
                    var p = ray.GetPoint(t);
                    if (p.y > Mathf.Max(HeightAt(p), WorldTerrain.WaterLevel)) { prev = t; continue; }
                    float lo = prev, hi = t;
                    for (int i = 0; i < 12; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        var m = ray.GetPoint(mid);
                        if (m.y > Mathf.Max(HeightAt(m), WorldTerrain.WaterLevel)) lo = mid; else hi = mid;
                    }
                    point = ray.GetPoint(hi);
                    return true;
                }
            }
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        void Press(Vector2 pos)
        {
            pressStart = pos;
            pressBlocked = IsBlocked(pos);
            dragging = false;
        }

        void Move(Vector2 pos, Vector2 delta)
        {
            if (pressBlocked) return;
            bool paint = PaintMode();
            if (!dragging && (pos - pressStart).magnitude > TapSlop)
            {
                dragging = true;
                if (paint) OnPaint?.Invoke(pressStart);
            }
            if (!dragging) return;
            if (paint) OnPaint?.Invoke(pos);
            else Pan(delta);
        }

        void Release(Vector2 pos)
        {
            if (!pressBlocked && !dragging && !pinching) OnTap?.Invoke(pos);
            if (dragging && PaintMode()) OnPaintEnd?.Invoke();
            pressBlocked = false;
            dragging = false;
        }

        void HandleInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount >= 2)
            {
                var a = Input.GetTouch(0);
                var b = Input.GetTouch(1);
                if (!pinching)
                {
                    pinching = true;
                    pressBlocked = pressBlocked || IsBlocked(a.position) || IsBlocked(b.position);
                    lastPinch = 0f;
                }
                if (pressBlocked) return;
                float d = Vector2.Distance(a.position, b.position);
                if (lastPinch > 0f && d > 1f) Zoom(lastPinch / d);
                lastPinch = d;
                return;
            }
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (!pinching) Press(t.position);
                        break;
                    case TouchPhase.Moved:
                        if (!pinching) Move(t.position, t.deltaPosition);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (t.phase == TouchPhase.Ended) Release(t.position);
                        pinching = false;
                        pressBlocked = false;
                        break;
                }
                return;
            }
            pinching = false;
            if (Input.touchSupported && Application.isMobilePlatform) return;

            Vector2 m = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) { Press(m); lastMouse = m; }
            else if (Input.GetMouseButton(0)) { Move(m, (Vector2)(Input.mousePosition - lastMouse)); lastMouse = m; }
            if (Input.GetMouseButtonUp(0)) Release(m);
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !IsBlocked(m)) Zoom(scroll > 0 ? 0.9f : 1.1f);
#endif
        }
    }

    /// <summary>Where a villager goes next and what they do there.</summary>
    public struct VillagerTask
    {
        public Vector3 Spot;
        /// <summary>Clip played on arrival: Idle, Talk, Work, Gather, Sit, Chop, Plant, Harvest, Water, Throw, Lantern…</summary>
        public string Action;
        /// <summary>Tool shown in the right hand while working (Prop_Axe, Prop_Pickaxe, Prop_Hammer, Prop_Spear) or null.</summary>
        public string Tool;
        /// <summary>Clip used on the way there (Walk, or Carry when bringing goods home).</summary>
        public string WalkClip;
        public float Duration;
        /// <summary>Optional waypoints along the roads before the spot.</summary>
        public List<Vector3> Route;
    }

    /// <summary>
    /// A villager: walks (along the roads when there are any) to the place of their job and
    /// does the work there with the right tool: woodcutters chop, miners and quarriers strike,
    /// farmers sow, water and harvest, hunters and fishers throw spears, builders hammer.
    /// Uses the animated CC0 characters (Models/Villager and VillagerF, legacy clips) with
    /// per-person skin, clothing and hair tints; falls back to a simple figure if missing.
    /// </summary>
    public class Villager : MonoBehaviour
    {
        /// <summary>Set by the world: the next task, given where the villager stands now.</summary>
        public Func<Vector3, VillagerTask> NextTask;
        public WorldTerrain Ground;
        VillagerTask task;
        bool hasTask;
        readonly Queue<Vector3> route = new Queue<Vector3>();
        Vector3 target;
        float wait;
        float speed;
        Transform body;
        Animation anim;
        string current;
        readonly Dictionary<string, GameObject> tools = new Dictionary<string, GameObject>();
        string shownTool;

        static readonly Color[] SkinTones =
        {
            new Color(0.92f, 0.72f, 0.58f), new Color(0.84f, 0.62f, 0.48f), new Color(0.74f, 0.52f, 0.38f),
            new Color(0.62f, 0.43f, 0.31f), new Color(0.52f, 0.36f, 0.26f),
        };
        // Multiplies the leather texture: warm browns that read as hide, apart from the skin.
        static readonly Color[] HideTints =
        {
            new Color(0.95f, 0.80f, 0.64f), new Color(0.80f, 0.66f, 0.52f), new Color(1.00f, 0.86f, 0.70f),
            new Color(0.72f, 0.60f, 0.50f), new Color(0.88f, 0.75f, 0.60f),
        };
        static readonly Color[] HairColors =
        {
            new Color(0.08f, 0.055f, 0.04f), new Color(0.16f, 0.10f, 0.06f), new Color(0.28f, 0.18f, 0.10f),
            new Color(0.05f, 0.045f, 0.04f), new Color(0.38f, 0.27f, 0.16f),
        };

        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        static Material cloth;

        /// <summary>Matte leather for clothing (the shared hide surface is too glossy on a body).</summary>
        static Material ClothMaterial()
        {
            if (cloth != null) return cloth;
            var src = ModelLibrary.Surface("Hide");
            if (src == null) return null;
            cloth = new Material(src) { name = "VillagerHide" };
            if (cloth.HasProperty("_Smoothness")) cloth.SetFloat("_Smoothness", 0.12f);
            if (cloth.HasProperty("_EnvironmentReflections")) cloth.SetFloat("_EnvironmentReflections", 0f);
            return cloth;
        }

        static GameObject Model(string name)
        {
            if (models.TryGetValue(name, out var m)) return m;
            m = Resources.Load<GameObject>("Models/" + name);
            if (m == null) Debug.LogWarning($"[FOUNDERS] {name} model missing.");
            return models[name] = m;
        }

        void Start()
        {
            speed = Random.Range(1.0f, 1.25f);
            body = transform.GetChild(0);
            wait = Random.Range(0f, 3f);
            target = transform.position;
            anim = GetComponentInChildren<Animation>();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Prop_")) continue;
                tools[r.name] = r.gameObject;
                r.gameObject.SetActive(false);
            }
            if (anim != null)
            {
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
                foreach (AnimationState st in anim) st.wrapMode = WrapMode.Loop;
                Play(Random.value < 0.5f ? "Idle" : "Talk", 0f);
                // Desynchronise identical loops.
                foreach (AnimationState st in anim) st.normalizedTime = Random.value;
            }
        }

        void Play(string clip, float fade = 0.25f)
        {
            if (anim == null || current == clip) return;
            foreach (AnimationState st in anim)
            {
                if (!st.name.EndsWith(clip, StringComparison.Ordinal)) continue;
                if (fade > 0) anim.CrossFade(st.name, fade); else anim.Play(st.name);
                current = clip;
                return;
            }
            if (clip != "Idle") Play("Idle", fade);
        }

        void ShowTool(string tool)
        {
            if (tool == shownTool) return;
            if (shownTool != null && tools.TryGetValue(shownTool, out var old)) old.SetActive(false);
            shownTool = tool;
            if (tool != null && tools.TryGetValue(tool, out var go)) go.SetActive(true);
        }

        void Update()
        {
            if (wait > 0f) { wait -= Time.deltaTime; return; }
            var to = target - transform.position;
            to.y = 0;
            if (to.magnitude < 0.15f)
            {
                if (route.Count > 0)
                {
                    target = route.Dequeue();
                    return;
                }
                if (hasTask)
                {
                    hasTask = false;
                    ShowTool(task.Tool);
                    Play(task.Action);
                    wait = task.Duration;
                    return;
                }
                if (NextTask == null) { wait = 2f; return; }
                task = NextTask(transform.position);
                hasTask = true;
                route.Clear();
                if (task.Route != null) foreach (var p in task.Route) route.Enqueue(p);
                target = route.Count > 0 ? route.Dequeue() : task.Spot;
                route.Enqueue(task.Spot);
                // Tools stay in hand on the way to work; carried goods replace them.
                ShowTool(task.WalkClip == "Carry" ? null : task.Tool);
                return;
            }
            bool onRoad = task.Route != null && route.Count > 0;
            Play(task.WalkClip ?? "Walk");
            var step = to.normalized * speed * (onRoad ? 1.3f : 1f) * Time.deltaTime;
            var next = transform.position + Vector3.ClampMagnitude(step, to.magnitude);
            if (Ground != null) next.y = Ground.HeightAt(next);
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 6f * Time.deltaTime);
            if (anim == null)
                body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * speed * 7f)) * 0.05f, 0);
        }

        public static Villager Spawn(Transform parent, Vector3 at, int seed, bool female)
        {
            var root = Parts.Group("Villager", parent, at).transform;
            root.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            var model = Model(female ? "VillagerF" : "Villager") ?? Model("Villager");

            if (model != null)
            {
                var rng = new System.Random(seed * 7919 + 17);
                var body = Instantiate(model, root);
                body.name = "Body";
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                float s = (female ? 0.9f : 0.95f) + (float)rng.NextDouble() * 0.07f;
                body.transform.localScale = Vector3.one * s;

                var skin = SkinTones[rng.Next(SkinTones.Length)];
                var hide = HideTints[rng.Next(HideTints.Length)];
                var hair = HairColors[rng.Next(HairColors.Length)];
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Submesh order is up to the FBX importer, so materials are matched by name.
                    var source = r.sharedMaterials;
                    var mats = new Material[source.Length];
                    var tints = new Color[source.Length];
                    for (int i = 0; i < source.Length; i++)
                    {
                        string n = source[i] != null ? source[i].name : "";
                        if (n.StartsWith("Hide")) { mats[i] = ClothMaterial(); tints[i] = hide; }
                        else if (n.StartsWith("Hair")) { mats[i] = Palette.Get("Hair"); tints[i] = hair; }
                        else { mats[i] = Palette.Get("Skin"); tints[i] = skin; }
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.updateWhenOffscreen = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_BaseColor", tints[i]);
                        r.SetPropertyBlock(block, i);
                    }
                }
                // Hand tools: wooden handles and knapped stone heads.
                foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var source = r.sharedMaterials;
                    var mats = new Material[source.Length];
                    for (int i = 0; i < source.Length; i++)
                        mats[i] = source[i] != null && source[i].name.StartsWith("Stone") ? ModelLibrary.Surface("DarkRock") : ModelLibrary.Surface("RoughWood");
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                return root.gameObject.AddComponent<Villager>();
            }

            var figure = Parts.Group("Body", root, Vector3.zero).transform;
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(0, 0.55f, 0), new Vector3(0.34f, 0.5f, 0.28f), ModelLibrary.Surface("Hide"));
            Parts.Prim(PrimitiveType.Sphere, figure, new Vector3(0, 1.18f, 0), Vector3.one * 0.22f, "Skin");
            Parts.Prim(PrimitiveType.Sphere, figure, new Vector3(0, 1.24f, -0.02f), new Vector3(0.24f, 0.15f, 0.24f), "Hair");
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(-0.08f, 0.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), "Skin");
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(0.08f, 0.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), "Skin");
            return root.gameObject.AddComponent<Villager>();
        }
    }

    /// <summary>Fire: additive flames, glowing embers, sparks, rising smoke and a flickering light.</summary>
    public static class Effects
    {
        static ParticleSystem NewSystem(Transform parent, string name, Vector3 at, string material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Palette.Get(material);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>A small flame on a torch or in a lantern, with a warm light that comes on at night.</summary>
        public static void Torch(Transform parent, Vector3 at, float scale, bool lantern)
        {
            if (!lantern)
            {
                var flames = NewSystem(parent, "TorchFlame", at, "Flame");
                var main = flames.main;
                main.loop = true;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f * scale, 0.9f * scale);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.8f, 0.35f, 0.9f));
                main.maxParticles = 30;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = flames.emission;
                em.rateOverTime = 22f;
                var sh = flames.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 6f;
                sh.radius = 0.04f;
                var sz = flames.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
                flames.Play();
            }
            var lightGo = Parts.Group("LampLight", parent, at + new Vector3(0, 0.15f, 0));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = lantern ? new Color(1f, 0.7f, 0.38f) : new Color(1f, 0.58f, 0.25f);
            light.range = 7f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<Flicker>().NightOnly = true;
        }

        public static void Fire(Transform parent, Vector3 at, float scale)
        {
            // Flames
            var flames = NewSystem(parent, "Flames", at, "Flame");
            var main = flames.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.1f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.6f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.8f, 0.35f, 0.9f));
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = flames.emission;
            em.rateOverTime = 34f;
            var sh = flames.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 8f;
            sh.radius = 0.14f * scale;
            var sz = flames.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
            var col = flames.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            flames.Play();

            // Sparks
            var sparks = NewSystem(parent, "Sparks", at + new Vector3(0, 0.2f * scale, 0), "Spark");
            var sm = sparks.main;
            sm.loop = true;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            sm.maxParticles = 40;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var se = sparks.emission;
            se.rateOverTime = 6f * scale;
            var ss = sparks.shape;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 25f;
            ss.radius = 0.1f;
            var noise = sparks.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 1.2f;
            sparks.Play();

            // Smoke
            var smoke = NewSystem(parent, "Smoke", at + new Vector3(0, 0.6f * scale, 0), "Smoke");
            var mm = smoke.main;
            mm.loop = true;
            mm.prewarm = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 0.8f * scale);
            mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            mm.maxParticles = 60;
            mm.simulationSpace = ParticleSystemSimulationSpace.World;
            var me = smoke.emission;
            me.rateOverTime = 4f;
            var msh = smoke.shape;
            msh.shapeType = ParticleSystemShapeType.Cone;
            msh.angle = 6f;
            msh.radius = 0.1f;
            var msz = smoke.sizeOverLifetime;
            msz.enabled = true;
            msz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.5f));
            var mcol = smoke.colorOverLifetime;
            mcol.enabled = true;
            var mg = new Gradient();
            mg.SetKeys(new[] { new GradientColorKey(new Color(0.3f, 0.28f, 0.27f), 0f), new GradientColorKey(new Color(0.7f, 0.7f, 0.72f), 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.2f), new GradientAlphaKey(0f, 1f) });
            mcol.color = mg;
            var mv = smoke.velocityOverLifetime;
            mv.enabled = true;
            mv.space = ParticleSystemSimulationSpace.World;
            mv.x = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            mv.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            mv.z = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            smoke.Play();

            // Light
            var lightGo = Parts.Group("FireLight", parent, at + new Vector3(0, 0.6f * scale, 0));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.22f);
            light.range = 7f * scale;
            light.intensity = 2.4f * scale;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<Flicker>();
        }
    }
}
