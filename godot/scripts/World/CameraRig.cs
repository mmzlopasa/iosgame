using System;
using System.Collections.Generic;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// The city-builder camera (as in the Unity version): one finger pans, two fingers pinch to zoom,
    /// twist to turn and slide up or down to tilt; it looks flatter as it pulls back so the valley walls
    /// show on the horizon. A short press without moving is a tap; a press on the building being placed
    /// drags it; in paint mode a drag paints roads and fences.
    /// UI controls take their own touches first (Godot only passes unhandled input to the world).
    /// </summary>
    public sealed partial class CameraRig : Node3D
    {
        public Camera3D Cam { get; private set; }
        public Action<Vector2> OnTap;
        public Func<bool> PaintMode = () => false;
        public Action<Vector2> OnPaint;
        public Action OnPaintEnd;
        public Func<Vector2, bool> GrabAt = _ => false;
        public Action<Vector2> OnGrab;
        /// <summary>x/z extent the focus point may move in (world metres).</summary>
        public Rect2 Bounds = new Rect2(-4, -4, 260, 260);
        public Func<Vector3, float> HeightAt;
        /// <summary>True when a point of the viewport is over the interface (a touch there is not for the world).</summary>
        public Func<Vector2, bool> Blocks = _ => false;
        /// <summary>Where the focus sits on screen (kept for the interface; landscape keeps it centred).</summary>
        public Vector2 ViewShift;
        readonly HashSet<int> blocked = new HashSet<int>();

        Vector3 focus;
        float distance = 30f;
        Vector3 moveTarget;
        double moveUntil;
        float zoomTarget = -1f;
        double now;

        const float Fov = 30f;
        const float MinDistance = 9f, MaxDistance = 150f;
        float tilt;
        float Pitch => Mathf.Clamp((distance <= 80f ? Mathf.Lerp(52f, 40f, Mathf.InverseLerp(MinDistance, 80f, distance))
                                                    : Mathf.Lerp(40f, 34f, Mathf.InverseLerp(80f, MaxDistance, distance))) + tilt, 22f, 82f);
        /// <summary>Compass heading of the camera in degrees (45 = looking north-west).</summary>
        public static float Yaw { get; private set; } = 45f;
        const float TapSlop = 14f;

        readonly Dictionary<int, Vector2> touches = new Dictionary<int, Vector2>();
        Vector2 pressStart;
        bool pressing, dragging, pinching, grabbing;
        float lastPinch, lastTwist;
        Vector2 lastMid;

        public override void _Ready()
        {
            Name = "CameraRig";
            Cam = new Camera3D { Fov = Fov, Near = 0.5f, Far = 400f, Current = true };
            AddChild(Cam);
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
            moveUntil = now + 0.6;
            zoomTarget = zoom;
        }

        public Vector3 Focus => focus;
        public float Distance => distance;

        public override void _Process(double delta)
        {
            now += delta;
            float dt = (float)delta;
            if (now < moveUntil)
            {
                float k = 1f - Mathf.Exp(-8f * dt);
                focus = focus.Lerp(moveTarget, k);
                if (zoomTarget > 0) distance = Mathf.Lerp(distance, zoomTarget, k);
            }
            if (HeightAt != null)
            {
                float ground = Mathf.Max(HeightAt(focus), Terrain.WaterLevel);
                focus.Y = Mathf.Lerp(focus.Y, ground, 1f - Mathf.Exp(-6f * dt));
            }
            Apply();
        }

        void Apply()
        {
            if (Cam == null) return;
            var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(-Pitch), Mathf.DegToRad(Yaw), 0f));
            var position = focus + basis * new Vector3(0, 0, distance);
            if (HeightAt != null) position.Y = Mathf.Max(position.Y, HeightAt(position) + 2.5f);
            Cam.GlobalPosition = position;
            Cam.LookAt(focus, Vector3.Up);
            Cam.Far = distance + 260f;
        }

        void Pan(Vector2 screenDelta)
        {
            moveUntil = 0;
            float worldPerPixel = 2f * distance * Mathf.Tan(Mathf.DegToRad(Fov * 0.5f)) / GetViewport().GetVisibleRect().Size.Y;
            var yaw = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(Yaw), 0));
            var right = yaw * Vector3.Right;
            var forward = yaw * Vector3.Forward;
            // Screen y grows downwards: dragging down moves the view north.
            focus -= (right * screenDelta.X - forward * screenDelta.Y / Mathf.Sin(Mathf.DegToRad(Pitch))) * worldPerPixel;
            focus.X = Mathf.Clamp(focus.X, Bounds.Position.X, Bounds.End.X);
            focus.Z = Mathf.Clamp(focus.Z, Bounds.Position.Y, Bounds.End.Y);
        }

        public void Rotate(float degrees)
        {
            moveUntil = Math.Min(moveUntil, now);
            Yaw = Mathf.PosMod(Yaw + degrees + 180f, 360f) - 180f;
        }

        void Tilt(float degrees) => tilt = Mathf.Clamp(tilt + degrees, -24f, 30f);

        public void ResetView()
        {
            Yaw = 45f;
            tilt = 0f;
        }

        public void Zoom(float factor)
        {
            moveUntil = 0;
            distance = Mathf.Clamp(distance * factor, MinDistance, MaxDistance);
        }

        public Func<Vector3, Vector3, (bool, Vector3)> Raycast;

        /// <summary>Point on the ground (or water) under a screen position.</summary>
        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var from = Cam.ProjectRayOrigin(screen);
            var dir = Cam.ProjectRayNormal(screen);
            if (Raycast != null)
            {
                var (ok, hit) = Raycast(from, dir);
                point = hit;
                if (ok) return true;
            }
            var plane = new Plane(Vector3.Up, 0f);
            var p = plane.IntersectsRay(from, dir);
            point = p ?? Vector3.Zero;
            return p.HasValue;
        }

        // ------------------------------------------------------------------ input

        void Press(Vector2 pos)
        {
            pressStart = pos;
            pressing = true;
            dragging = false;
            grabbing = GrabAt(pos);
        }

        void Move(Vector2 pos, Vector2 delta)
        {
            if (!pressing) return;
            if (grabbing)
            {
                if (!dragging && (pos - pressStart).Length() > TapSlop * 0.5f) dragging = true;
                if (dragging) OnGrab?.Invoke(pos);
                return;
            }
            bool paint = PaintMode();
            if (!dragging && (pos - pressStart).Length() > TapSlop)
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
            if (pressing && !dragging && !pinching) OnTap?.Invoke(pos);
            if (dragging && PaintMode() && !grabbing) OnPaintEnd?.Invoke();
            pressing = false;
            dragging = false;
            grabbing = false;
        }

        public override void _UnhandledInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventScreenTouch t:
                    // A finger that went down on the interface belongs to it until it lifts.
                    if (t.Pressed && touches.Count == 0 && Blocks(t.Position)) { blocked.Add(t.Index); break; }
                    if (blocked.Contains(t.Index)) { if (!t.Pressed) blocked.Remove(t.Index); break; }
                    if (t.Pressed)
                    {
                        touches[t.Index] = t.Position;
                        if (touches.Count == 1) { pinching = false; Press(t.Position); }
                        else { pinching = true; lastPinch = 0f; pressing = false; }
                    }
                    else
                    {
                        touches.Remove(t.Index);
                        if (touches.Count == 0) { if (!pinching) Release(t.Position); pinching = false; pressing = false; }
                    }
                    break;
                case InputEventScreenDrag d:
                    {
                        if (blocked.Contains(d.Index)) break;
                        var prev = touches.TryGetValue(d.Index, out var p) ? p : d.Position;
                        touches[d.Index] = d.Position;
                        if (touches.Count >= 2)
                        {
                            Vector2 a = default, b = default; int n = 0;
                            foreach (var v in touches.Values) { if (n == 0) a = v; else if (n == 1) b = v; n++; }
                            float dist = a.DistanceTo(b);
                            var dir = b - a;
                            float twist = Mathf.RadToDeg(Mathf.Atan2(dir.Y, dir.X));
                            var mid = (a + b) * 0.5f;
                            if (lastPinch > 0f && dist > 1f)
                            {
                                Zoom(lastPinch / dist);
                                Rotate(Mathf.Wrap(twist - lastTwist, -180f, 180f));
                                var slide = mid - lastMid;
                                if (Mathf.Abs(slide.Y) > Mathf.Abs(slide.X) * 1.5f) Tilt(-slide.Y * 0.12f);
                            }
                            lastPinch = dist;
                            lastTwist = twist;
                            lastMid = mid;
                        }
                        else if (!pinching) Move(d.Position, d.Position - prev);
                        break;
                    }
                case InputEventMagnifyGesture g:
                    if (g.Factor > 0.01f) Zoom(1f / g.Factor);
                    break;
                case InputEventMouseButton mb:
                    // Mouse events made from touches (Device −1) were handled as touches already.
                    if (mb.Device == InputEvent.DeviceIdEmulation) break;
                    if (mb.Pressed && mb.ButtonIndex == MouseButton.Left && Blocks(mb.Position)) break;
                    if (mb.ButtonIndex == MouseButton.Left)
                    {
                        if (mb.Pressed) Press(mb.Position); else Release(mb.Position);
                    }
                    else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) Zoom(0.9f);
                    else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) Zoom(1.1f);
                    break;
                case InputEventMouseMotion mm:
                    if (mm.Device == InputEvent.DeviceIdEmulation) break;
                    if ((mm.ButtonMask & MouseButtonMask.Left) != 0) Move(mm.Position, mm.Relative);
                    else if ((mm.ButtonMask & MouseButtonMask.Right) != 0) { Rotate(-mm.Relative.X * 0.3f); Tilt(-mm.Relative.Y * 0.15f); }
                    break;
            }
        }
    }
}
