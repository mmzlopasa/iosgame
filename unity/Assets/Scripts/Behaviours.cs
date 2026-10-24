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
        /// <summary>True when a press at this point grabs something to drag (the building being placed).</summary>
        public Func<Vector2, bool> GrabAt = _ => false;
        public Action<Vector2> OnGrab;
        bool grabbing;
        public Rect Bounds = new Rect(-4, -36, 40, 40); // x/z extent the focus point may move in
        /// <summary>Ground height at a world position (hills and mountains); null means flat ground at y = 0.</summary>
        public Func<Vector3, float> HeightAt;

        Vector3 focus;
        float distance = 26f;
        Vector3 moveTarget;
        float moveUntil;
        float zoomTarget = -1f;

        /// <summary>
        /// Vertical field of view, picked by the screen's orientation every frame (the phone may turn at any time).
        /// Landscape screens are short: 26° keeps buildings large enough to tap while the valley spreads sideways.
        /// Portrait screens are tall and narrow: a wider vertical angle (34°, as the game had when it was portrait)
        /// keeps a useful width of the valley in view between the resource bar and the bottom sheets.
        /// </summary>
        public const float PortraitFov = 34f;
        public const float LandscapeFov = 26f;
        static float Fov => Screen.width >= Screen.height ? LandscapeFov : PortraitFov;

        /// <summary>
        /// Where the focus point appears on screen, as a shift of the picture in normalised device units (0 = the
        /// centre, +y = up). The UI lifts the focus above a portrait bottom sheet so the selected building or the
        /// ghost being placed stays in view; the shift eases in and out and taps still hit what they show.
        /// </summary>
        public Vector2 ViewShift;
        Vector2 shiftNow;

        const float MinDistance = 9f;
        const float MaxDistance = 80f;
        /// <summary>Extra tilt the player added (two fingers up/down): negative looks flatter, positive more from above.</summary>
        float tilt;
        float Pitch => Mathf.Clamp(Mathf.Lerp(52f, 40f, Mathf.InverseLerp(MinDistance, MaxDistance, distance)) + tilt, 22f, 82f);
        /// <summary>Compass heading of the camera; the player turns it freely with two fingers.</summary>
        public static float Yaw { get; private set; } = -45f;
        float lastTwist;
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
                Cam.fieldOfView = Fov;
                Cam.nearClipPlane = 0.5f;
            }
            // The fog hides everything beyond it (see Apply): drawing further only costs frame rate.
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
            if (!Mathf.Approximately(Cam.fieldOfView, Fov)) Cam.fieldOfView = Fov;
            ApplyShift();
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            var position = focus + rotation * new Vector3(0, 0, -distance);
            // Never inside a mountain between the camera and the focus.
            if (HeightAt != null) position.y = Mathf.Max(position.y, HeightAt(position) + 2.5f);
            Cam.transform.position = position;
            Cam.transform.rotation = Quaternion.LookRotation(focus - position);
            // The haze starts beyond what is in view, however far the camera is zoomed out: it only
            // hides the end of the world on the horizon, never the valley the player looks at.
            RenderSettings.fogStartDistance = distance + 70f;
            RenderSettings.fogEndDistance = distance + 230f;
            Cam.farClipPlane = distance + 250f;
        }

        /// <summary>An off-centre projection (a lens shift) that moves the picture so the focus sits at <see cref="ViewShift"/>.</summary>
        void ApplyShift()
        {
            shiftNow = Vector2.Lerp(shiftNow, ViewShift, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            if ((shiftNow - ViewShift).sqrMagnitude < 1e-6f) shiftNow = ViewShift;
            Cam.ResetProjectionMatrix();
            if (shiftNow.sqrMagnitude < 1e-8f) return;
            var p = Cam.projectionMatrix;
            // A point straight ahead lands at ndc = -m02 / -m12 (view space looks down -z).
            p.m02 = -shiftNow.x;
            p.m12 = -shiftNow.y;
            Cam.projectionMatrix = p;
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

        public void Rotate(float degrees)
        {
            moveUntil = Mathf.Min(moveUntil, Time.time);
            Yaw = Mathf.Repeat(Yaw + degrees + 180f, 360f) - 180f;
        }

        void Tilt(float degrees) => tilt = Mathf.Clamp(tilt + degrees, -24f, 30f);

        /// <summary>Turns the camera back to the default north-west view.</summary>
        public void ResetView()
        {
            Yaw = -45f;
            tilt = 0f;
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
            grabbing = !pressBlocked && GrabAt(pos);
        }

        void Move(Vector2 pos, Vector2 delta)
        {
            if (pressBlocked) return;
            if (grabbing)
            {
                // Dragging the building being placed moves it, not the camera.
                if (!dragging && (pos - pressStart).magnitude > TapSlop * 0.5f) dragging = true;
                if (dragging) OnGrab?.Invoke(pos);
                return;
            }
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
            if (dragging && PaintMode() && !grabbing) OnPaintEnd?.Invoke();
            pressBlocked = false;
            dragging = false;
            grabbing = false;
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
                var dir = b.position - a.position;
                float twist = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                if (lastPinch > 0f && d > 1f)
                {
                    Zoom(lastPinch / d);
                    // Twist two fingers to turn the camera all the way around.
                    Rotate(-Mathf.DeltaAngle(lastTwist, twist));
                    // Slide both fingers up or down together to tilt.
                    if (Vector2.Dot(a.deltaPosition, b.deltaPosition) > 0f)
                        Tilt((a.deltaPosition.y + b.deltaPosition.y) * 0.5f * 0.12f);
                }
                lastPinch = d;
                lastTwist = twist;
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
            // Right mouse button turns and tilts; Q and E turn too.
            if (Input.GetMouseButton(1))
            {
                var delta = (Vector2)(Input.mousePosition - lastMouse);
                Rotate(delta.x * 0.3f);
                Tilt(delta.y * 0.15f);
            }
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButton(1)) lastMouse = m;
            if (Input.GetKey(KeyCode.Q)) Rotate(-90f * Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.E)) Rotate(90f * Time.unscaledDeltaTime);
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !IsBlocked(m)) Zoom(scroll > 0 ? 0.9f : 1.1f);
#endif
        }
    }

    /// <summary>Where a villager goes next and what they do there.</summary>
    public struct VillagerTask
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
    /// A villager: walks (along the roads when there are any) to the place of their job and
    /// does the work there with the right tool: woodcutters chop, miners and quarriers strike,
    /// farmers sow, water and harvest, hunters and fishers throw spears, builders hammer.
    /// Uses the animated CC0 people (Models/Person_*, legacy clips) with their baked PBR texture
    /// set, a lighter mesh for the distant view, and per-person skin tone, dyed clothes and hair;
    /// falls back to a simple figure if missing.
    /// </summary>
    public class Villager : MonoBehaviour
    {
        /// <summary>Set by the world: the next task, given where the villager stands now.</summary>
        public Func<Vector3, VillagerTask> NextTask;
        /// <summary>The clip playing now (Walk, Chop, Plant…).</summary>
        public string Clip => current;
        public string Tool => shownTool;
        bool child, elder;
        public bool IsChild => child;
        NeedBubble need;

        /// <summary>Survival need shown over the head ("food", "winter") or null.</summary>
        public void SetNeed(string iconName)
        {
            if (need == null) { if (iconName == null) return; need = NeedBubble.Attach(transform); }
            need.Show(iconName);
        }
        public WorldTerrain Ground;
        VillagerTask task;
        bool hasTask;
        readonly Queue<Vector3> route = new Queue<Vector3>();
        Vector3 target;
        float wait;
        float speed;
        Transform body;
        /// <summary>Height of the body model before scaling (metres), to size the seat.</summary>
        float bodyHeight = 1.78f;
        Animation anim;
        /// <summary>Plays the clips of the player's own character (Assets/Custom People), else null.</summary>
        CustomPersonAnimator customAnim;
        string current;
        readonly Dictionary<string, GameObject> tools = new Dictionary<string, GameObject>();
        string shownTool;

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

        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        static GameObject Model(string name)
        {
            if (models.TryGetValue(name, out var m)) return m;
            m = Resources.Load<GameObject>("Models/" + name);
            if (m == null) Debug.LogWarning($"[FOUNDERS] {name} model missing.");
            return models[name] = m;
        }

        void Start()
        {
            speed = Random.Range(1.0f, 1.25f) * (elder ? 0.75f : 1f);
            body = transform.GetChild(0);
            wait = Random.Range(0f, 3f);
            target = transform.position;
            anim = GetComponentInChildren<Animation>();
            customAnim = GetComponentInChildren<CustomPersonAnimator>();
            if (customAnim != null)
            {
                string first = Random.value < 0.5f ? "Idle" : "Talk";
                customAnim.Play(first, 0f, randomStart: true); // desynchronised loops
                current = first;
            }
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
            if (customAnim != null)
            {
                if (current == clip) return;
                customAnim.Play(clip, fade);
                current = clip;
                return;
            }
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

        GameObject carried;
        string carriedId;

        /// <summary>Logs on the shoulder, a stone or a basket of berries on the way home.</summary>
        void ShowCarry(string id)
        {
            if (id == carriedId) return;
            if (carried != null) Destroy(carried);
            carried = null;
            carriedId = id;
            if (id == null || !ModelLibrary.HasModel(id)) return;
            float s = id switch
            {
                "k_wood" => 4.2f, "k_stone" => 2.2f, "k_planks" => 1.4f, "k_log" => 0.8f, "k_box" => 1.8f,
                "k_bedroll" => 1.2f, "ceramic_pot" => 0.6f, _ => 1.2f,
            };
            var at = id == "wicker_basket_01" ? new Vector3(0.22f, 0.78f, 0.12f) : new Vector3(0f, 1.25f, 0.06f);
            carried = ModelLibrary.Place(id, transform, at, s, 90f);
            foreach (var r in carried.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        GameObject seat;

        /// <summary>A log to sit on (the sitting clip is made for a seat at knee height).</summary>
        void ShowSeat(bool on)
        {
            if (!on)
            {
                if (seat != null) Destroy(seat);
                seat = null;
                return;
            }
            if (seat != null || !ModelLibrary.HasModel("k_log")) return;
            // The clip's seat is 0.45 m high and 0.34 m behind the feet for a 1.78 m person.
            float k = body != null ? body.localScale.y * bodyHeight / 1.78f : 1f;
            float logHeight = Mathf.Max(0.01f, ModelLibrary.Size("k_log").y);
            // A short log, across behind the feet.
            seat = ModelLibrary.Place("k_log", transform, new Vector3(0f, 0f, -0.3f * k), 0.43f * k / logHeight, 90f, new Vector3(1f, 1f, 0.55f));
        }

        void ShowTool(string tool)
        {
            if (tool == shownTool) return;
            if (shownTool != null && tools.TryGetValue(shownTool, out var old)) old.SetActive(false);
            shownTool = tool;
            if (tool != null && tools.TryGetValue(tool, out var go)) go.SetActive(true);
        }

        /// <summary>Drops the current task; the villager asks for a new one right away.</summary>
        public void Interrupt()
        {
            ShowSeat(false);
            wait = 0f;
            hasTask = false;
            route.Clear();
            target = transform.position;
        }

        void Update()
        {
            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                // Turn to the work (the tree being cut, the fire, the building being raised).
                if (task.Face is Vector3 face)
                {
                    var look = face - transform.position;
                    look.y = 0;
                    if (look.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 8f * Time.deltaTime);
                }
                return;
            }
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
                    ShowCarry(null);
                    ShowTool(task.Tool);
                    // Face the work before the first swing: the walk ends looking along the path.
                    if (task.Face is Vector3 f)
                    {
                        var look = f - transform.position;
                        look.y = 0;
                        if (look.sqrMagnitude > 0.01f)
                            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 120f);
                    }
                    ShowSeat(task.Action == "Sit");
                    Play(task.Action);
                    wait = task.Duration;
                    return;
                }
                if (NextTask == null) { wait = 2f; return; }
                task = NextTask(transform.position);
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
            bool onRoad = task.Route != null && route.Count > 0;
            Play(task.WalkClip ?? "Walk");
            var step = to.normalized * speed * (onRoad ? 1.3f : 1f) * Time.deltaTime;
            var next = transform.position + Vector3.ClampMagnitude(step, to.magnitude);
            if (Ground != null) next.y = Ground.HeightAt(next);
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 6f * Time.deltaTime);
            if (anim == null && customAnim == null)
                body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * speed * 7f)) * 0.05f, 0);
        }

        sealed class PersonModel
        {
            public string Model, Gender, Age;
            /// <summary>Baked texture set (P_&lt;model&gt; material): skin, dye and hair are multiplied onto it.</summary>
            public bool Textured;
            /// <summary>Average painted skin colour (linear) and the default dye and hair colours (sRGB).</summary>
            public Color Skin, Main, Hair;
            /// <summary>Linear grey level of the dyed zones' detail.</summary>
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
            var asset = Resources.Load<TextAsset>("people");
            if (asset == null) return personModels;
            var root = new Core.JObj(Core.Json.Parse(asset.text));
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

        /// <summary>The painted colour of a model material (the pack stores linear colours).</summary>
        static Color Original(Material m)
        {
            if (m == null) return Color.grey;
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.grey;
            return new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), 1f);
        }

        /// <summary>
        /// The _BaseColor that turns a texture of linear colour <paramref name="baseLinear"/> into the
        /// sRGB colour <paramref name="wanted"/> (Unity converts property colours from sRGB to linear).
        /// </summary>
        static Color Ratio(Color wanted, Color baseLinear)
        {
            var w = wanted.linear;
            return new Color(w.r / Mathf.Max(0.004f, baseLinear.r), w.g / Mathf.Max(0.004f, baseLinear.g),
                             w.b / Mathf.Max(0.004f, baseLinear.b), 1f).gamma;
        }

        static readonly Dictionary<string, Material> personMaterials = new Dictionary<string, Material>();

        static Material PersonMaterial(string model)
        {
            if (personMaterials.TryGetValue(model, out var m)) return m;
            return personMaterials[model] = Resources.Load<Material>(Palette.ResourceFolder + "/P_" + model);
        }

        /// <summary>
        /// A villager: one of the people (man, woman or elder), or a child (a smaller person). Skin
        /// tone, the dye of the main garment and the hair colour vary per person so no two look alike.
        /// </summary>
        public static Villager Spawn(Transform parent, Vector3 at, int seed, bool female, bool child = false, string job = null)
        {
            var root = Parts.Group(child ? "Child" : "Villager", parent, at).transform;
            root.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            var rng = new System.Random(seed * 7919 + 17);
            bool elder = !child && rng.Next(7) == 0;
            // The player's own characters (Assets/Custom/Resources/Custom/People) replace the game's people.
            var custom = CustomAssets.PickPerson(rng, female, child, elder, job);
            if (custom != null) return SpawnCustom(root, custom, rng, female, child, elder);
            var pool = PersonModels().FindAll(p => p.Gender == (female ? "female" : "male") && p.Age == (elder ? "elder" : "adult"));
            if (pool.Count == 0) pool = PersonModels();
            var person = pool.Count > 0 ? pool[rng.Next(pool.Count)] : null;
            string modelName = person != null ? person.Model.Replace("Models/", "") : female ? "VillagerF" : "Villager";
            var model = Model(modelName);

            if (model != null)
            {
                var body = Instantiate(model, root);
                body.name = "Body";
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;

                var skin = SkinTones[rng.Next(SkinTones.Length)];
                var cloth = HideTints[rng.Next(HideTints.Length)];
                var hair = elder ? new Color(0.70f, 0.69f, 0.66f) : HairColors[rng.Next(HairColors.Length)];
                bool recolourClothes = rng.Next(3) != 0;
                var textured = person != null && person.Textured ? PersonMaterial(modelName) : null;
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Materials are matched by name: Skin, Hair and the main garment vary per person.
                    var source = r.sharedMaterials;
                    var mats = new Material[source.Length];
                    var tints = new Color[source.Length];
                    for (int i = 0; i < source.Length; i++)
                    {
                        string n = source[i] != null ? source[i].name : "";
                        if (textured != null)
                        {
                            // One texture set; the zone's tint dyes it (grey detail × colour) or shifts the skin tone.
                            mats[i] = textured;
                            if (n.StartsWith("Skin")) tints[i] = Ratio(skin, person.Skin);
                            else if (n.StartsWith("Hair")) tints[i] = Ratio(elder ? person.Hair : hair, new Color(person.Neutral, person.Neutral, person.Neutral));
                            else if (n.StartsWith("Main") && person.Dye) tints[i] = Ratio(recolourClothes ? cloth : person.Main, new Color(person.Neutral, person.Neutral, person.Neutral));
                            else tints[i] = Color.white;
                            continue;
                        }
                        mats[i] = Palette.Get("Tunic");
                        if (n.StartsWith("Skin")) tints[i] = skin;
                        else if (n.StartsWith("Hair")) { mats[i] = Palette.Get("Hair"); tints[i] = hair; }
                        else if ((n.StartsWith("Main") || n.StartsWith("Shirt")) && recolourClothes) tints[i] = cloth;
                        else tints[i] = Original(source[i]);
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
                // Real height: every model is scaled to the height of its person.
                var bounds = new Bounds(root.position, Vector3.zero);
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>()) bounds.Encapsulate(r.bounds);
                float height = child ? 1.0f + (float)rng.NextDouble() * 0.15f : (female ? 1.6f : 1.7f) + (float)rng.NextDouble() * 0.12f;
                if (elder) height -= 0.06f;
                if (bounds.size.y > 0.01f) body.transform.localScale *= height / bounds.size.y;
                SetUpLods(root.gameObject, body);
                var v = root.gameObject.AddComponent<Villager>();
                v.child = child;
                v.elder = elder;
                if (bounds.size.y > 0.01f) v.bodyHeight = bounds.size.y;
                return v;
            }

            var figure = Parts.Group("Body", root, Vector3.zero).transform;
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(0, 0.55f, 0), new Vector3(0.34f, 0.5f, 0.28f), ModelLibrary.Surface("Hide"));
            Parts.Prim(PrimitiveType.Sphere, figure, new Vector3(0, 1.18f, 0), Vector3.one * 0.22f, "Skin");
            Parts.Prim(PrimitiveType.Sphere, figure, new Vector3(0, 1.24f, -0.02f), new Vector3(0.24f, 0.15f, 0.24f), "Hair");
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(-0.08f, 0.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), "Skin");
            Parts.Prim(PrimitiveType.Capsule, figure, new Vector3(0.08f, 0.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), "Skin");
            return root.gameObject.AddComponent<Villager>();
        }

        /// <summary>A villager drawn with the player's own rigged character and clips, at a real height.</summary>
        static Villager SpawnCustom(Transform root, CustomAssets.Person person, System.Random rng, bool female, bool child, bool elder)
        {
            var body = Instantiate(person.Prefab, root);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) Destroy(c);
            CustomAssets.FixMaterials(body);
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.updateWhenOffscreen = false;
            }
            var bounds = CustomAssets.LocalBounds(body, root);
            float scale0 = Mathf.Max(0.0001f, body.transform.localScale.y);
            float height = child ? 1.0f + (float)rng.NextDouble() * 0.15f : (female ? 1.6f : 1.7f) + (float)rng.NextDouble() * 0.12f;
            if (elder) height -= 0.06f;
            // A child drawn with a grown-up's model is that model made smaller; a Child_ model keeps its proportions.
            if (bounds.size.y > 0.01f) body.transform.localScale *= height / bounds.size.y;
            var animator = body.GetComponentInChildren<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            body.AddComponent<CustomPersonAnimator>().Init(animator, person.Clips);
            var v = root.gameObject.AddComponent<Villager>();
            v.child = child;
            v.elder = elder;
            if (bounds.size.y > 0.01f) v.bodyHeight = bounds.size.y / scale0;
            return v;
        }

        /// <summary>
        /// Distant villagers draw the lighter mesh (Body_LOD1, a third of the triangles, same skeleton
        /// and textures); people never disappear, they are small but always there.
        /// </summary>
        static void SetUpLods(GameObject root, GameObject body)
        {
            var near = new List<Renderer>();
            Renderer far = null;
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (r.name.StartsWith("Body_LOD1")) far = r;
                else near.Add(r);
            }
            if (far == null || near.Count == 0) return;
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.09f, near.ToArray()), new LOD(0.0005f, new[] { far }) });
            group.RecalculateBounds();
        }
    }

    /// <summary>
    /// A round survival bubble over a villager's head (hunger, cold, tiredness): a dark disc with
    /// a white icon that always faces the camera and bobs gently.
    /// </summary>
    public class NeedBubble : MonoBehaviour
    {
        static Texture2D disc;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static Mesh quad;
        Transform icon;
        string shown;
        float phase;

        static Texture2D Disc()
        {
            if (disc != null) return disc;
            const int n = 64;
            disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01((1f - d) * 18f);
                    var c = d > 0.88f ? new Color(1f, 1f, 1f, a) : new Color(0.12f, 0.13f, 0.15f, a * 0.92f);
                    disc.SetPixel(x, y, c);
                }
            disc.Apply();
            return disc;
        }

        static Material MaterialFor(string key, Texture tex)
        {
            if (materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Palette.Get("Mist")) { name = "Need_" + key };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = 3100;
            return materials[key] = m;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "NeedQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
            return quad;
        }

        static GameObject Card(Transform parent, Material m, float size, float z)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static NeedBubble Attach(Transform villager)
        {
            var go = new GameObject("Need");
            go.transform.SetParent(villager, false);
            go.transform.localPosition = new Vector3(0, 2.35f, 0);
            var b = go.AddComponent<NeedBubble>();
            Card(go.transform, MaterialFor("disc", Disc()), 0.62f, 0.01f);
            b.phase = Random.value * 6f;
            go.SetActive(false);
            return b;
        }

        /// <summary>Shows an icon (e.g. "food", "winter") or hides the bubble with null.</summary>
        public void Show(string iconName)
        {
            if (iconName == shown) return;
            shown = iconName;
            if (icon != null) Destroy(icon.gameObject);
            icon = null;
            gameObject.SetActive(iconName != null);
            if (iconName == null) return;
            var tex = UI.Theme.Icon(iconName);
            if (tex != null) icon = Card(transform, MaterialFor(iconName, tex), 0.38f, 0f).transform;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
            transform.localPosition = new Vector3(0, 2.35f + Mathf.Sin(Time.time * 2.2f + phase) * 0.06f, 0);
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
