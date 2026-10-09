using System.Collections.Generic;
using UnityEngine;

namespace Eonward
{
    /// <summary>Furnace fire light flicker.</summary>
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
            lightSource.intensity = baseIntensity * (0.8f + 0.4f * n);
        }
    }

    /// <summary>
    /// Top-down strategy camera: one finger drags the map, two fingers pinch to zoom.
    /// In the editor: left mouse drags, scroll wheel zooms.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        Vector3 focus = new Vector3(0, 0, -2f);
        float distance = 34f;
        const float MinDistance = 14f;
        const float MaxDistance = 70f;
        const float Pitch = 52f;
        const float Yaw = 45f;
        const float MaxFocusRadius = 45f;

        Vector3 lastMouse;
        bool dragging;
        float lastPinch;

        void Awake()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = SceneEnvironment.FogColor;
            Cam.fieldOfView = 40f;
            Cam.nearClipPlane = 0.5f;
            Cam.farClipPlane = 220f;
            camGo.AddComponent<AudioListener>();
            Apply();
        }

        void LateUpdate()
        {
            HandleInput();
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
            // Convert a screen drag into ground-plane movement (scaled by zoom).
            float worldPerPixel = 2f * distance * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            var right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
            var forward = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            focus -= (right * screenDelta.x + forward * screenDelta.y / Mathf.Sin(Pitch * Mathf.Deg2Rad)) * worldPerPixel;
            if (focus.magnitude > MaxFocusRadius) focus = focus.normalized * MaxFocusRadius;
        }

        void Zoom(float factor)
        {
            distance = Mathf.Clamp(distance * factor, MinDistance, MaxDistance);
        }

        void HandleInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Moved) Pan(t.deltaPosition);
                lastPinch = 0f;
                return;
            }
            if (Input.touchCount >= 2)
            {
                var a = Input.GetTouch(0);
                var b = Input.GetTouch(1);
                float d = Vector2.Distance(a.position, b.position);
                if (lastPinch > 0f && d > 1f) Zoom(lastPinch / d);
                lastPinch = d;
                return;
            }
            lastPinch = 0f;

            if (Input.GetMouseButtonDown(0)) { dragging = true; lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(0)) dragging = false;
            if (dragging)
            {
                var m = Input.mousePosition;
                Pan(m - lastMouse);
                lastMouse = m;
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) Zoom(scroll > 0 ? 0.9f : 1.1f);
#endif
        }
    }

    /// <summary>A survivor walking between the furnace and work sites (ambient life).</summary>
    public class Survivor : MonoBehaviour
    {
        public List<Vector3> Spots;
        Vector3 target;
        float wait;
        float speed;
        Transform body;

        void Start()
        {
            speed = Random.Range(1.1f, 1.7f);
            body = transform.GetChild(0);
            PickTarget();
        }

        void PickTarget()
        {
            var spot = Spots[Random.Range(0, Spots.Count)];
            var jitter = Random.insideUnitCircle * 1.2f;
            target = spot + new Vector3(jitter.x, 0, jitter.y);
        }

        void Update()
        {
            if (wait > 0f) { wait -= Time.deltaTime; return; }
            var to = target - transform.position;
            to.y = 0;
            if (to.magnitude < 0.2f)
            {
                wait = Random.Range(1.5f, 5f);
                PickTarget();
                return;
            }
            var step = to.normalized * speed * Time.deltaTime;
            transform.position += Vector3.ClampMagnitude(step, to.magnitude);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 8f * Time.deltaTime);
            // Little walking bob.
            body.localPosition = new Vector3(0, 0.5f + Mathf.Abs(Mathf.Sin(Time.time * speed * 6f)) * 0.06f, 0);
        }

        public static void Spawn(Transform parent, List<Vector3> spots, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var start = spots[i % spots.Count] + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));
                var root = Parts.Group("Survivor", parent, start).transform;
                var body = Parts.Group("Body", root, new Vector3(0, 0.5f, 0)).transform;
                Parts.Prim(PrimitiveType.Capsule, body, Vector3.zero, new Vector3(0.42f, 0.5f, 0.42f), i % 3 == 0 ? "CoatBlue" : "Coat");
                Parts.Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.62f, 0), Vector3.one * 0.3f, "Skin");
                Parts.Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.7f, 0.02f), new Vector3(0.34f, 0.2f, 0.34f), "Canvas");
                root.gameObject.AddComponent<Survivor>().Spots = spots;
            }
        }
    }

    /// <summary>Keeps the snow volume in front of the camera, between it and the ground.</summary>
    public class FollowView : MonoBehaviour
    {
        public Camera Cam;

        void LateUpdate()
        {
            if (Cam == null) return;
            transform.position = Cam.transform.position + Cam.transform.forward * 20f;
            transform.rotation = Quaternion.identity;
        }
    }

    /// <summary>Snowfall in front of the camera and furnace smoke.</summary>
    public static class Effects
    {
        public static ParticleSystem Snowfall(Camera cam)
        {
            var go = new GameObject("Snowfall");
            go.AddComponent<FollowView>().Cam = cam;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 7f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.maxParticles = 1800;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 220f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 20f, 60f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.6f, 1.2f);
            velocity.y = new ParticleSystem.MinMaxCurve(-2.6f, -1.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Palette.Get("Snowflake");
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        public static ParticleSystem Smoke(Transform at)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(at, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
            main.startColor = new Color(0.5f, 0.5f, 0.53f, 0.55f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 9f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.3f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.35f, 0.33f, 0.33f), 0f), new GradientColorKey(new Color(0.75f, 0.77f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Palette.Get("Smoke");
            ps.Play();
            return ps;
        }
    }
}
