using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Founders.UI;

namespace Founders
{
    /// <summary>
    /// Keeps the motion even. Frames that arrive at uneven times (some at 120 Hz, some at 80) feel
    /// like stutter even when the average is high, so the game runs at a steady pace the phone can
    /// hold: the screen's full refresh (120 on ProMotion iPhones) while it keeps up, otherwise half of
    /// it (60), which every iPhone holds smoothly. The 3D view's resolution is adjusted rarely and in
    /// whole steps, because every change of it rebuilds the screen buffers (a hitch of its own).
    /// The interface is always drawn at full resolution, so text stays crisp.
    /// </summary>
    public class FrameGovernor : MonoBehaviour
    {
        /// <summary>The lowest the view may go (below this the picture gets soft).</summary>
        const float MinScale = 0.65f;
        const float Step = 0.1f;
        /// <summary>Seconds of frames measured before deciding anything.</summary>
        const float Window = 2f;
        /// <summary>No two changes closer than this (each one costs a hitch).</summary>
        const float Cooldown = 6f;

        float window, frames, slowFrames, since = 0f;
        float scale = -1f;
        int pace;
        int failures;

        void Update()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            float cap = Prefs.RenderScaleCap;
            if (scale < 0f || scale > cap) { scale = cap; urp.renderScale = scale; }
            int screenHz = Mathf.Max(30, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            if (Prefs.BatterySaver) { pace = 30; Apply(30); return; }
            if (pace == 0 || pace > screenHz) pace = screenHz;
            Apply(pace);

            float dt = Time.unscaledDeltaTime;
            since += dt;
            window += dt;
            frames++;
            // A frame that took clearly longer than the pace allows is a visible stutter.
            if (dt > 1.5f / pace) slowFrames++;
            if (window < Window) return;
            float fps = frames / window;
            float slowShare = slowFrames / Mathf.Max(1f, frames);
            window = frames = slowFrames = 0f;
            if (since < Cooldown) return;

            bool struggling = fps < pace * 0.9f || slowShare > 0.08f;
            bool easy = fps > pace * 0.98f && slowShare < 0.01f;
            if (struggling)
            {
                if (scale > MinScale + 0.001f)
                {
                    scale = Mathf.Max(MinScale, scale - Step);
                    urp.renderScale = scale;
                }
                else if (pace > 60 && pace >= screenHz)
                {
                    // Even the lowest resolution cannot hold the full refresh: half of it, steadily.
                    pace = Mathf.Max(60, screenHz / 2);
                    failures++;
                    scale = cap;
                    urp.renderScale = scale;
                }
                since = 0f;
            }
            else if (easy && scale < cap - 0.001f)
            {
                scale = Mathf.Min(cap, scale + Step);
                urp.renderScale = scale;
                since = -Cooldown; // climbing back waits longer than stepping down
            }
            else if (easy && pace < screenHz && failures < 2 && since > 60f)
            {
                // Plenty of room for a minute: try the full refresh again (twice at most).
                pace = screenHz;
                since = 0f;
            }
        }

        static void Apply(int fps)
        {
            if (Application.targetFrameRate != fps) Application.targetFrameRate = fps;
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
        }
    }
}
