// Compile-check only: minimal stand-ins for the URP / Core RP package API used by the project
// (the real assemblies live in the Unity package, not in the editor's Managed folder).
// Signatures copied from com.unity.render-pipelines.universal / core 17.3.0.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering
{
    public class VolumeComponent : ScriptableObject { }
    public class VolumeParameter<T> { public virtual void Override(T x) { } public T value; }
    public class FloatParameter : VolumeParameter<float> { public FloatParameter(float v, bool o = false) { } }
    public class MinFloatParameter : VolumeParameter<float> { public MinFloatParameter(float v, float min, bool o = false) { } }
    public class ClampedFloatParameter : VolumeParameter<float> { public ClampedFloatParameter(float v, float min, float max, bool o = false) { } }
    public class VolumeProfile : ScriptableObject
    {
        public List<VolumeComponent> components = new List<VolumeComponent>();
        public T Add<T>(bool overrides = false) where T : VolumeComponent => default;
        public bool TryGet<T>(out T component) where T : VolumeComponent { component = default; return false; }
    }
    public class Volume : MonoBehaviour { public bool isGlobal { get; set; } public VolumeProfile sharedProfile; }
}

namespace UnityEngine.Rendering.Universal
{
    public enum RendererType { Custom, UniversalRenderer, _2DRenderer }
    public enum ColorGradingMode { LowDynamicRange, HighDynamicRange }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public enum TonemappingMode { None, Neutral, ACES }
    public enum UpscalingFilterSelection { Auto, Linear, Point, FSR, STP }
    public abstract class ScriptableRendererFeature : ScriptableObject { public bool isActive => true; public void SetActive(bool active) { } }
    public class ScreenSpaceAmbientOcclusion : ScriptableRendererFeature { }
    public abstract class ScriptableRendererData : ScriptableObject { public List<ScriptableRendererFeature> rendererFeatures { get; } = new List<ScriptableRendererFeature>(); }
    public class UniversalRendererData : ScriptableRendererData { }
    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => null;
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null) => null;
        public ScriptableRendererData LoadBuiltinRendererData(RendererType type = RendererType.UniversalRenderer) => null;
        public bool supportsHDR { get; set; }
        public UpscalingFilterSelection upscalingFilter { get; set; }
        public int msaaSampleCount { get; set; }
        public float renderScale { get; set; }
        public bool supportsCameraDepthTexture { get; set; }
        public int mainLightShadowmapResolution { get; set; }
        public float shadowDistance { get; set; }
        public int shadowCascadeCount { get; set; }
        public float cascade2Split { get; set; }
        public int maxAdditionalLightsCount { get; set; }
        public ColorGradingMode colorGradingMode { get; set; }
        public bool useSRPBatcher { get; set; }
    }
    public class UniversalAdditionalCameraData : MonoBehaviour
    {
        public bool renderPostProcessing { get; set; }
        public AntialiasingMode antialiasing { get; set; }
        public bool renderShadows { get; set; }
    }
    public class TonemappingModeParameter : VolumeParameter<TonemappingMode> { }
    public sealed class Tonemapping : VolumeComponent { public TonemappingModeParameter mode; }
    public sealed class Bloom : VolumeComponent { public MinFloatParameter threshold, intensity; public ClampedFloatParameter scatter; }
    public sealed class ColorAdjustments : VolumeComponent { public FloatParameter postExposure; public ClampedFloatParameter contrast, saturation; }
    public sealed class WhiteBalance : VolumeComponent { public ClampedFloatParameter temperature, tint; }
    public sealed class Vignette : VolumeComponent { public ClampedFloatParameter intensity, smoothness; }
}

namespace UnityEditor
{
    public abstract class BaseShaderGUI : ShaderGUI
    {
        public static void SetMaterialKeywords(Material material, Action<Material> shadingModelFunc = null, Action<Material> shaderFunc = null) { }
    }
}

namespace UnityEditor.Rendering.Universal.ShaderGUI
{
    public static class LitGUI { public static void SetMaterialKeywords(Material material) { } }
    public static class ParticleGUI { public static void SetMaterialKeywords(Material material) { } }
}
