using System.Collections.Generic;
using System.IO;
using System.Linq;
using Founders;
using Founders.Core;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FoundersEditor
{
    /// <summary>Import settings for the processed CC0 art (see tools/assets/build_assets.py).</summary>
    public class ArtPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Art/") && !assetPath.StartsWith("Assets/Generated/")) return;
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            if (assetPath.StartsWith("Assets/Art/UI/"))
            {
                // UI art: pixel-exact, no mips, no power-of-two rescale; only the big backdrop is compressed.
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 4096;
                ti.textureCompression = file == "loading_bg" ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
                return;
            }
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 1024;
            if (assetPath.EndsWith(".hdr"))
            {
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                ti.maxTextureSize = 1024;
                ti.sRGBTexture = false;
                return;
            }
            if (file.EndsWith("_normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (file.EndsWith("_mask"))
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.maxTextureSize = 512;
            }
            else if (file.Contains("_impostor") || assetPath.EndsWith(".png"))
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.5f;
                ti.wrapMode = file.Contains("_impostor") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Art/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; // materials are assigned from the art manifest
            bool character = Path.GetFileNameWithoutExtension(assetPath) == "Villager";
            mi.importAnimation = character;
            if (character)
            {
                // Legacy clips: played by name from code, no Animator Controller assets needed.
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.generateAnimations = ModelImporterGenerateAnimations.GenerateAnimations;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReductionAndCompression;
                mi.optimizeGameObjects = false;
                // Keep the material names (Skin / Hide / Hair): the game maps its own materials by name.
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            }
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.addCollider = false;
        }
    }

    /// <summary>
    /// Builds the realistic rendering setup from code: URP pipeline + renderer (SSAO), lit
    /// materials from the photo-scanned textures, terrain layers, HDRI sky and the global
    /// post-processing volume. Everything lands in Assets/Generated (not committed).
    /// </summary>
    public static class ArtSetup
    {
        const string Gen = "Assets/Generated";
        const string Rendering = Gen + "/Rendering";
        const string Materials = Gen + "/Resources/" + Palette.ResourceFolder;
        const string Layers = Gen + "/Resources/TerrainLayers";
        const string GenTextures = Gen + "/Textures";

        public static UniversalRenderPipelineAsset PipelineAsset { get; private set; }
        public static VolumeProfile PostProfile { get; private set; }
        public static Material SkyMaterial { get; private set; }
        public static Cubemap SkyCube { get; private set; }

        public static void Run()
        {
            Folder(Rendering);
            Folder(Materials);
            Folder(Layers);
            Folder(GenTextures);
            ConfigureUrp();
            var manifest = LoadManifest();
            GenerateTextures();
            GenerateColorMaterials();
            GenerateTexturedMaterials(manifest);
            GenerateTerrainLayers(manifest);
            GenerateSky(manifest);
            GenerateVolume();
            AssetDatabase.SaveAssets();
        }

        public static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T Save<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static JObj LoadManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Resources/art_manifest.json");
            if (asset == null)
            {
                Debug.LogWarning("[FOUNDERS] art_manifest.json missing; run tools/assets/build_assets.py");
                return null;
            }
            return new JObj(Json.Parse(asset.text));
        }

        // MARK: - URP

        static void ConfigureUrp()
        {
            string path = Rendering + "/Founders_URP.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create();
                AssetDatabase.CreateAsset(asset, path);
                // Creates Assets/UniversalRenderer.asset with the default post-processing resources.
                var renderer = (UniversalRendererData)asset.LoadBuiltinRendererData(RendererType.UniversalRenderer);
                var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
                var so = new SerializedObject(renderer);
                var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId))
                {
                    map.arraySize += 1;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(renderer);
            }

            asset.supportsHDR = true;
            asset.msaaSampleCount = 1; // MSAA on Metal tile memory triggered RenderPass sample-count errors; SMAA runs in post instead
            asset.renderScale = 1f;
            asset.supportsCameraDepthTexture = true;
            asset.mainLightShadowmapResolution = 2048;
            asset.shadowDistance = 70f;
            asset.shadowCascadeCount = 2;
            asset.cascade2Split = 0.3f;
            asset.maxAdditionalLightsCount = 8;
            asset.colorGradingMode = ColorGradingMode.HighDynamicRange;
            asset.useSRPBatcher = true;
            // These have internal setters; the editor sets them through serialization too.
            var aso = new SerializedObject(asset);
            aso.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            aso.FindProperty("m_SoftShadowsSupported").boolValue = true;
            aso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            PipelineAsset = asset;
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        // MARK: - Materials

        static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");

        static Material LitMaterial(string name, Color color, float smoothness = 0.3f, float metallic = 0f, Color emission = default)
        {
            var m = new Material(LitShader) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.maxColorComponent > 0f)
            {
                m.SetColor("_EmissionColor", emission);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            return m;
        }

        static void Finish(Material m)
        {
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        }

        /// <summary>Plain-colour materials (UI feedback, people) and particle materials.</summary>
        static void GenerateColorMaterials()
        {
            foreach (var spec in Palette.All)
            {
                Material m;
                if (spec.Particle)
                {
                    m = ParticleMaterial(spec.Name, spec.Color, spec.Additive, spec.Texture);
                }
                else
                {
                    m = LitMaterial(spec.Name, spec.Color, spec.Smoothness, spec.Metallic, spec.Emission);
                    Finish(m);
                }
                Save(m, $"{Materials}/{spec.Name}.mat");
            }
        }

        static Material ParticleMaterial(string name, Color color, bool additive, string textureName)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GenTextures}/{textureName ?? "SoftDot"}.png");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);           // transparent
            m.SetFloat("_Blend", additive ? 2f : 0f); // additive or alpha
            m.SetFloat("_Cull", 0f);
            BaseShaderGUI.SetMaterialKeywords(m, null, ParticleGUI.SetMaterialKeywords);
            return m;
        }

        static Texture2D Tex(string assetsRelative) =>
            string.IsNullOrEmpty(assetsRelative) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/" + assetsRelative);

        static Material TexturedLit(string name, string albedo, string normal, string mask, Vector2 tiling,
                                    bool alphaClip = false, bool doubleSided = false, Color? tint = null, float smoothness = 1f)
        {
            var m = LitMaterial(name, tint ?? Color.white, smoothness);
            var a = Tex(albedo);
            if (a != null) { m.SetTexture("_BaseMap", a); m.SetTextureScale("_BaseMap", tiling); }
            var n = Tex(normal);
            if (n != null) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); }
            var k = Tex(mask);
            if (k != null)
            {
                m.SetTexture("_MetallicGlossMap", k);
                m.SetTexture("_OcclusionMap", k);
                m.SetFloat("_OcclusionStrength", 1f);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            else
            {
                m.SetFloat("_Smoothness", 0.2f);
            }
            if (alphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
            }
            if (doubleSided) m.SetFloat("_Cull", 0f);
            Finish(m);
            return m;
        }

        /// <summary>T_&lt;Texture&gt;, M_&lt;model&gt;__&lt;material&gt; and I_&lt;tree&gt;_side/_top materials from the manifest.</summary>
        static void GenerateTexturedMaterials(JObj manifest)
        {
            if (manifest == null) return;
            var textures = manifest.Obj("textures");
            foreach (var name in textures.Keys)
            {
                var t = textures.Obj(name);
                var m = TexturedLit("T_" + name, t.Str("albedo"), t.Str("normal"), t.Str("mask"), Vector2.one);
                Save(m, $"{Materials}/T_{name}.mat");
            }

            var models = manifest.Obj("models");
            foreach (var id in models.Keys)
            {
                var mats = models.Obj(id).Obj("materials");
                foreach (var matName in mats.Keys)
                {
                    var t = mats.Obj(matName);
                    string key = ModelLibrary.MaterialKey(id, matName);
                    var m = TexturedLit(key, t.OptStr("albedo"), t.OptStr("normal"), t.OptStr("mask"), Vector2.one,
                                        t.OptBool("alphaClip", false), t.OptBool("doubleSided", false) || t.OptBool("alphaClip", false));
                    Save(m, $"{Materials}/{key}.mat");
                }
            }

            var impostors = manifest.Obj("impostors");
            foreach (var name in impostors.Keys)
            {
                var t = impostors.Obj(name);
                foreach (var view in new[] { "side", "side2", "top" })
                {
                    var m = TexturedLit($"I_{name}_{view}", t.Str(view), null, null, Vector2.one, alphaClip: true, doubleSided: true);
                    m.SetFloat("_Smoothness", 0.05f);
                    m.SetFloat("_EnvironmentReflections", 0f);
                    Finish(m);
                    Save(m, $"{Materials}/I_{name}_{view}.mat");
                }
            }
        }

        static void GenerateTerrainLayers(JObj manifest)
        {
            if (manifest == null) return;
            var textures = manifest.Obj("textures");
            foreach (var name in new[] { "Grass", "ForestFloor", "Mud", "Sand", "Riverbed", "RockGround" })
            {
                if (!textures.Has(name)) continue;
                var t = textures.Obj(name);
                var layer = new TerrainLayer
                {
                    diffuseTexture = Tex(t.Str("albedo")),
                    normalMapTexture = Tex(t.Str("normal")),
                    tileSize = new Vector2(5f, 5f),
                    normalScale = 1f,
                    smoothness = name == "Riverbed" || name == "Sand" ? 0.35f : 0.12f,
                    metallic = 0f,
                };
                Save(layer, $"{Layers}/{name}.terrainlayer");
            }
            var terrainMat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "TerrainLit" };
            terrainMat.EnableKeyword("_NORMALMAP");
            // Keep the instancing variants in the build (they are stripped unless a material asks for them).
            terrainMat.enableInstancing = true;
            Save(terrainMat, $"{Materials}/TerrainLit.mat");
        }

        static void GenerateSky(JObj manifest)
        {
            SkyCube = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Art/Sky/Sky.hdr");
            var shader = Shader.Find("Skybox/Cubemap");
            var sky = new Material(shader) { name = "Sky" };
            if (SkyCube != null) sky.SetTexture("_Tex", SkyCube);
            sky.SetFloat("_Exposure", 1.0f);
            sky.SetFloat("_Rotation", 40f);
            SkyMaterial = Save(sky, $"{Materials}/Sky.mat");
        }

        // MARK: - Generated textures (particles, mist)

        static void GenerateTextures()
        {
            WriteTexture("SoftDot", 64, (x, y) =>
            {
                float d = Mathf.Clamp01(1f - new Vector2(x - 0.5f, y - 0.5f).magnitude * 2f);
                return new Color(1, 1, 1, d * d);
            });
            WriteTexture("Cloud", 128, (x, y) =>
            {
                float d = Mathf.Clamp01(1f - new Vector2(x - 0.5f, y - 0.5f).magnitude * 2f);
                float n = 0f, amp = 0.5f, f = 3f;
                for (int o = 0; o < 4; o++) { n += Mathf.PerlinNoise(x * f + 11.3f, y * f + 7.1f) * amp; amp *= 0.5f; f *= 2f; }
                float a = Mathf.Clamp01(d * 1.4f - 0.15f) * Mathf.Clamp01(n * 1.6f - 0.15f);
                return new Color(1, 1, 1, a);
            });
            AssetDatabase.Refresh();
        }

        static void WriteTexture(string name, int size, System.Func<float, float, Color> f)
        {
            string path = $"{GenTextures}/{name}.png";
            if (File.Exists(path)) return;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }

        // MARK: - Post-processing

        static void GenerateVolume()
        {
            string path = Rendering + "/Post.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (var c in profile.components.ToList()) Object.DestroyImmediate(c, true);
            profile.components.Clear();

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            var color = Add<ColorAdjustments>();
            color.postExposure.Override(0.25f);
            color.contrast.Override(14f);
            color.saturation.Override(6f);
            var white = Add<WhiteBalance>();
            white.temperature.Override(6f);
            var vignette = Add<Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            PostProfile = profile;
        }

        /// <summary>Scene objects that need URP types: camera with post-processing, global volume, sky.</summary>
        public static void PopulateScene()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderShadows = true;

            var volumeGo = new GameObject("PostProcessing");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = PostProfile;

            if (SkyMaterial != null) RenderSettings.skybox = SkyMaterial;
            if (SkyCube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = SkyCube;
                RenderSettings.reflectionIntensity = 1f;
            }
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
        }
    }
}
