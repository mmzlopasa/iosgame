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
        float distance = 30f;
        Vector3 moveTarget;
        float moveUntil;
        float zoomTarget = -1f;

        const float MinDistance = 12f;
        const float MaxDistance = 60f;
        const float Pitch = 50f;
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
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = SceneEnvironment.SkyColor;
            Cam.fieldOfView = 38f;
            Cam.nearClipPlane = 0.5f;
            Cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
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
            transform.position += Vector3.ClampMagnitude(step, to.magnitude);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 8f * Time.deltaTime);
            body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * speed * 7f)) * 0.05f, 0);
        }

        public static Villager Spawn(Transform parent, Vector3 at, int seed)
        {
            var root = Parts.Group("Villager", parent, at).transform;
            var body = Parts.Group("Body", root, Vector3.zero).transform;
            Parts.Prim(PrimitiveType.Capsule, body, new Vector3(0, 0.42f, 0), new Vector3(0.32f, 0.36f, 0.32f), seed % 3 == 0 ? "Tunic2" : "Tunic");
            Parts.Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.86f, 0), Vector3.one * 0.24f, "Skin");
            Parts.Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.93f, -0.02f), new Vector3(0.26f, 0.16f, 0.26f), "Hair");
            return root.gameObject.AddComponent<Villager>();
        }
    }

    /// <summary>Fire smoke and sparks.</summary>
    public static class Effects
    {
        public static ParticleSystem Smoke(Transform at, float scale = 1f)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(at, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * scale, 1.0f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 0.8f * scale);
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.15f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.6f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.35f, 0.33f, 0.31f), 0f), new GradientColorKey(new Color(0.72f, 0.72f, 0.74f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Palette.Get("Smoke");
            ps.Play();
            return ps;
        }

        public static ParticleSystem Sparks(Transform at)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(at, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new Color(1f, 0.7f, 0.3f, 1f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 10f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.1f;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Palette.Get("Spark");
            ps.Play();
            return ps;
        }
    }
}
