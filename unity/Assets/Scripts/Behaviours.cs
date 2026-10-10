using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Founders
{
    /// <summary>Fire light flicker.</summary>
    public class Flicker : MonoBehaviour
    {
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
            lightSource.intensity = baseIntensity * (0.75f + 0.5f * n);
        }
    }

    /// <summary>
    /// Isometric-style strategy camera. One finger drags the map, two fingers pinch to zoom,
    /// a short tap selects a cell. In the editor: left mouse drags/taps, wheel zooms.
    /// Touches that start on the UI are ignored (see <see cref="Hud.BlocksPointer"/>).
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        /// <summary>Called with the screen position (pixels, bottom-left origin) of a tap on the world.</summary>
        public Action<Vector2> OnTap;
        public Func<Vector2, bool> IsBlocked = _ => false;
        public Rect Bounds = new Rect(-4, -36, 40, 40); // x/z extent the focus point may move in

        Vector3 focus;
        float distance = 26f;
        Vector3 moveTarget;
        float moveUntil;
        float zoomTarget = -1f;

        const float MinDistance = 9f;
        const float MaxDistance = 55f;
        const float Pitch = 47f;
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
            Apply();
        }

        void Apply()
        {
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Cam.transform.position = focus + rotation * new Vector3(0, 0, -distance);
            Cam.transform.rotation = rotation;
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

        /// <summary>Point on the ground plane (y = 0) under a screen position.</summary>
        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
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
            if (!dragging && (pos - pressStart).magnitude > TapSlop) dragging = true;
            if (dragging) Pan(delta);
        }

        void Release(Vector2 pos)
        {
            if (!pressBlocked && !dragging && !pinching) OnTap?.Invoke(pos);
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

    /// <summary>A villager walking between buildings (ambient life; not tied to the rules).</summary>
    public class Villager : MonoBehaviour
    {
        public Func<Vector3> PickSpot;
        public WorldTerrain Ground;
        Vector3 target;
        float wait;
        float speed;
        Transform body;

        void Start()
        {
            speed = Random.Range(0.9f, 1.4f);
            body = transform.GetChild(0);
            wait = Random.Range(0f, 3f);
            target = transform.position;
        }

        void Update()
        {
            if (wait > 0f) { wait -= Time.deltaTime; return; }
            var to = target - transform.position;
            to.y = 0;
            if (to.magnitude < 0.15f)
            {
                wait = Random.Range(1.5f, 6f);
                var spot = PickSpot != null ? PickSpot() : transform.position;
                var jitter = Random.insideUnitCircle * 0.9f;
                target = spot + new Vector3(jitter.x, 0, jitter.y);
                return;
            }
            var step = to.normalized * speed * Time.deltaTime;
            var next = transform.position + Vector3.ClampMagnitude(step, to.magnitude);
            if (Ground != null) next.y = Ground.HeightAt(next);
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 8f * Time.deltaTime);
            body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * speed * 7f)) * 0.05f, 0);
        }

        public static Villager Spawn(Transform parent, Vector3 at, int seed)
        {
            var root = Parts.Group("Villager", parent, at).transform;
            var body = Parts.Group("Body", root, Vector3.zero).transform;
            // Placeholder figure (real character models are a later step): hide clothing, skin, hair.
            Parts.Prim(PrimitiveType.Capsule, body, new Vector3(0, 0.55f, 0), new Vector3(0.34f, 0.5f, 0.28f), ModelLibrary.Surface("Hide"));
            Parts.Prim(PrimitiveType.Sphere, body, new Vector3(0, 1.18f, 0), Vector3.one * 0.22f, "Skin");
            Parts.Prim(PrimitiveType.Sphere, body, new Vector3(0, 1.24f, -0.02f), new Vector3(0.24f, 0.15f, 0.24f), "Hair");
            Parts.Prim(PrimitiveType.Capsule, body, new Vector3(-0.08f, 0.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), "Skin");
            Parts.Prim(PrimitiveType.Capsule, body, new Vector3(0.08f, 0.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), "Skin");
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
