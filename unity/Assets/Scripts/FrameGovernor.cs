using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Founders.UI;

namespace Founders
{
    /// <summary>
    /// Holds the frame rate at the screen's refresh (120 on ProMotion iPhones): when frames come too
    /// slowly, the 3D view is drawn at a slightly lower resolution (FSR sharpens it back up); when
    /// there is room again, it climbs back to the quality the player chose. The interface is always
    /// drawn at full resolution, so text stays crisp.
    /// </summary>
    public class FrameGovernor : MonoBehaviour
    {
        /// <summary>The lowest the view may go (below this the picture gets soft).</summary>
        const float MinScale = 0.6f;
        const float Step = 0.05f;

        float window, frames, calm;
        float scale = -1f;

        void Update()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            float cap = Prefs.RenderScaleCap;
            if (scale < 0f || scale > cap) scale = cap;
            window += Time.unscaledDeltaTime;
            frames++;
            if (window < 0.75f) return;
            float fps = frames / window;
            window = 0f;
            frames = 0f;
            // Never chase more frames than the screen shows (a 60 Hz phone stays at 60).
            int screenHz = Mathf.Max(30, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            // Some phones report their full refresh rate only once the first frames are on screen.
            if (!Prefs.BatterySaver && Application.targetFrameRate < screenHz) Application.targetFrameRate = screenHz;
            int target = Mathf.Min(Application.targetFrameRate > 0 ? Application.targetFrameRate : 60, screenHz);
            if (fps < target * 0.88f && scale > MinScale)
            {
                scale = Mathf.Max(MinScale, scale - Step);
                calm = 0f;
            }
            else if (fps > target * 0.97f && scale < cap)
            {
                // Climb back only after a few calm seconds, so it does not flicker between two sizes.
                calm += 0.75f;
                if (calm >= 3f) { scale = Mathf.Min(cap, scale + Step * 0.6f); calm = 0f; }
            }
            else calm = 0f;
            if (Mathf.Abs(urp.renderScale - scale) > 0.001f) urp.renderScale = scale;
        }
    }
}
