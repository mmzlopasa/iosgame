using System.Collections;
using System.Collections.Generic;
using System.IO;
using Founders.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Founders
{
    /// <summary>
    /// Pictures of the real buildings and decorations for the UI (build menu, cards). The game ships
    /// path-traced pictures in Resources/UI/Buildings (see tools/art/blender_buildings.py); any
    /// building without one falls back to this runtime studio: the model is
    /// built as in the world, photographed from the usual three-quarter view under a fixed daylight
    /// and given the kit's dark outline. Rendered once while the game loads and kept on disk, so the
    /// next launch only reads the files.
    /// </summary>
    public static class Thumbnails
    {
        public const int Size = 256;
        const int OutlinePx = 9;
        static readonly Color32 OutlineColor = new Color32(0x3A, 0x23, 0x14, 255);
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Offline Blender renders shipped with the game (tools/art/blender_buildings.py), under Resources.</summary>
        public const string BakedFolder = "UI/Buildings";
        static readonly Dictionary<string, Texture2D> baked = new Dictionary<string, Texture2D>();

        /// <summary>
        /// The picture of a building sprite ("medieval_house") or decoration ("deco:well"), or null:
        /// the pre-rendered picture when the game ships one, else the one rendered at load time.
        /// </summary>
        public static Texture2D Get(string key)
        {
            if (key == null) return null;
            var b = Baked(key);
            if (b != null) return b;
            return cache.TryGetValue(key, out var t) ? t : null;
        }

        /// <summary>The pre-rendered picture (Resources/UI/Buildings/&lt;key with ':' as '_'&gt;.png), or null.</summary>
        static Texture2D Baked(string key)
        {
            if (baked.TryGetValue(key, out var t)) return t;
            t = Resources.Load<Texture2D>(BakedFolder + "/" + key.Replace(':', '_'));
            baked[key] = t;
            return t;
        }

        static string Folder => Path.Combine(Application.persistentDataPath, "thumbs", Application.version + "_" + Application.buildGUID);

        /// <summary>Every picture the UI can ask for: buildings at each look, and decorations.</summary>
        static List<(string key, string sprite, bool decoration, float metres)> Wanted(GameContent content)
        {
            var list = new List<(string, string, bool, float)>();
            var seen = new HashSet<string>();
            foreach (var b in content.Buildings)
            {
                float metres = Mathf.Max(b.Footprint.Width, b.Footprint.Height) * Grid.CellSize;
                if (seen.Add(b.Sprite)) list.Add((b.Sprite, b.Sprite, false, metres));
                foreach (var look in b.Looks)
                    if (seen.Add(look.Sprite)) list.Add((look.Sprite, look.Sprite, false, metres));
            }
            foreach (var d in content.Decorations)
                if (seen.Add("deco:" + d.Sprite)) list.Add(("deco:" + d.Sprite, d.Sprite, true, Grid.CellSize));
            return list;
        }

        /// <summary>Loads the pictures from disk, or renders the missing ones (a few per frame).</summary>
        public static IEnumerator Prepare(GameContent content, System.Action<float> progress)
        {
            var wanted = Wanted(content);
            var missing = new List<(string key, string sprite, bool decoration, float metres)>();
            try { Directory.CreateDirectory(Folder); } catch { }
            foreach (var w in wanted)
            {
                if (cache.ContainsKey(w.key) || Baked(w.key) != null) continue; // shipped pictures need no studio
                string file = Path.Combine(Folder, FileName(w.key));
                Texture2D tex = null;
                try
                {
                    if (File.Exists(file))
                    {
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Thumb_" + w.key };
                        if (!tex.LoadImage(File.ReadAllBytes(file))) { Object.Destroy(tex); tex = null; }
                    }
                }
                catch { tex = null; }
                if (tex != null) { Finish(tex); cache[w.key] = tex; }
                else missing.Add(w);
            }
            if (missing.Count == 0) { progress?.Invoke(1f); yield break; }

            var studio = new Studio();
            try
            {
                for (int i = 0; i < missing.Count; i++)
                {
                    var w = missing[i];
                    var tex = studio.Shoot(w.sprite, w.decoration, w.metres);
                    if (tex != null)
                    {
                        cache[w.key] = tex;
                        try { File.WriteAllBytes(Path.Combine(Folder, FileName(w.key)), tex.EncodeToPNG()); } catch { }
                        Finish(tex);
                    }
                    progress?.Invoke((i + 1f) / missing.Count);
                    if (i % 3 == 2) yield return null;
                }
            }
            finally { studio.Dispose(); }
        }

        static string FileName(string key) => key.Replace(':', '_') + ".png";

        static void Finish(Texture2D tex)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.Apply(true, true); // mipmaps for small slots; no CPU copy kept
        }

        /// <summary>A small photo studio far from the world: a camera, a key and a fill light.</summary>
        sealed class Studio : System.IDisposable
        {
            static readonly Vector3 Origin = new Vector3(-4000f, 0f, -4000f);
            readonly GameObject root;
            readonly Camera cam;
            readonly RenderTexture rt;
            readonly List<(Light light, bool enabled)> worldLights = new List<(Light, bool)>();
            readonly bool fog;

            public Studio()
            {
                root = new GameObject("ThumbnailStudio");
                root.transform.position = Origin;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    worldLights.Add((l, l.enabled));
                    l.enabled = false;
                }
                fog = RenderSettings.fog;
                RenderSettings.fog = false;

                var key = new GameObject("Key").AddComponent<Light>();
                key.transform.SetParent(root.transform, false);
                key.type = LightType.Directional;
                key.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                key.color = new Color(1f, 0.96f, 0.88f);
                key.intensity = 2.1f;
                key.shadows = LightShadows.Soft;
                var fill = new GameObject("Fill").AddComponent<Light>();
                fill.transform.SetParent(root.transform, false);
                fill.type = LightType.Directional;
                fill.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
                fill.color = new Color(0.75f, 0.82f, 1f);
                fill.intensity = 0.7f;
                fill.shadows = LightShadows.None;

                rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "ThumbRT" };
                cam = new GameObject("ThumbCamera").AddComponent<Camera>();
                cam.transform.SetParent(root.transform, false);
                cam.enabled = false;
                cam.fieldOfView = 24f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.targetTexture = rt;
                cam.allowHDR = false;
                cam.allowMSAA = true;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = true;
            }

            public Texture2D Shoot(string sprite, bool decoration, float metres)
            {
                RenderSettings.fog = false; // the sky may have turned it back on between frames
                var holder = new GameObject("Subject");
                holder.transform.SetParent(root.transform, false);
                GameObject model;
                try
                {
                    model = decoration
                        ? BuildingModels.Decoration(sprite, holder.transform, null, 0, 0)
                        : BuildingModels.Create(sprite, holder.transform, null, metres);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[FOUNDERS] thumbnail {sprite}: {e.Message}");
                    Object.Destroy(holder);
                    return null;
                }
                // Fire and smoke particles would only blur the picture.
                foreach (var ps in holder.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);

                var renderers = holder.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) { Object.Destroy(holder); return null; }
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                // Three-quarter view from the front-left and above, as the camera usually looks at the town.
                var dir = Quaternion.Euler(32f, 45f, 0f) * Vector3.back;
                float radius = Mathf.Max(0.3f, bounds.extents.magnitude);
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                cam.transform.position = bounds.center + dir * dist;
                cam.transform.LookAt(bounds.center);
                // Fill the picture: measure the box on screen and move in until it spans ~84% (room for the outline).
                for (int pass = 0; pass < 2; pass++)
                {
                    float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        var v = cam.WorldToViewportPoint(corner);
                        minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                        minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
                    }
                    float span = Mathf.Max(maxX - minX, maxY - minY);
                    // Centre the box on screen, then scale the distance to the wanted span.
                    var mid = cam.ViewportToWorldPoint(new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, dist));
                    var shift = mid - cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, dist));
                    var target = bounds.center + shift;
                    dist *= Mathf.Clamp(span / 0.84f, 0.3f, 2f);
                    cam.transform.position = target + dir * dist;
                    cam.transform.LookAt(target);
                }

                // Frame by what is really drawn: find the drawn pixels, then move the camera so they span ~86%.
                // (Bounds can include flat ground patches or empty parts that leave the subject tiny.)
                for (int pass = 0; pass < 2; pass++)
                {
                    var probeDark = Grab(Color.black);
                    var probeLight = Grab(Color.white);
                    int x0 = Size, y0 = Size, x1 = -1, y1 = -1;
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                        {
                            var a = probeDark[y * Size + x];
                            var b = probeLight[y * Size + x];
                            int diff = (b.r - a.r) + (b.g - a.g) + (b.b - a.b);
                            if (diff > 3 * 255 - 40) continue; // background
                            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                    if (x1 < 0) break;
                    float spanPx = Mathf.Max(x1 - x0 + 1, y1 - y0 + 1);
                    float k = spanPx / (Size * 0.86f);
                    if (pass > 0 && Mathf.Abs(k - 1f) < 0.04f) break;
                    // Re-aim at the centre of what was drawn, then move in (or out) by the span ratio.
                    var mid = new Vector3((x0 + x1 + 1) * 0.5f / Size, (y0 + y1 + 1) * 0.5f / Size, dist);
                    var aim = cam.ViewportToWorldPoint(mid);
                    dist *= Mathf.Clamp(k, 0.2f, 2f);
                    var forward = cam.transform.forward;
                    cam.transform.position = aim - forward * dist;
                }

                // Black and white backgrounds give the true transparency (works whatever the pipeline does with alpha).
                var dark = Grab(Color.black);
                var light = Grab(Color.white);
                Object.DestroyImmediate(holder);

                var px = new Color32[Size * Size];
                for (int i = 0; i < px.Length; i++)
                {
                    var a = dark[i];
                    var b = light[i];
                    float alpha = 1f - ((b.r - a.r) + (b.g - a.g) + (b.b - a.b)) / (3f * 255f);
                    alpha = Mathf.Clamp01(alpha);
                    if (alpha < 0.01f) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    // Un-premultiply from the black render, with a touch more saturation and light (the world is graded).
                    float r = a.r / 255f / alpha, g = a.g / 255f / alpha, bl = a.b / 255f / alpha;
                    float lum = 0.3f * r + 0.59f * g + 0.11f * bl;
                    r = Mathf.Clamp01((lum + (r - lum) * 1.22f) * 1.08f);
                    g = Mathf.Clamp01((lum + (g - lum) * 1.22f) * 1.08f);
                    bl = Mathf.Clamp01((lum + (bl - lum) * 1.22f) * 1.08f);
                    px[i] = new Color32((byte)(r * 255), (byte)(g * 255), (byte)(bl * 255), (byte)(alpha * 255));
                }
                AddOutline(px);
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Thumb_" + sprite };
                tex.SetPixels32(px);
                return tex;
            }

            Color32[] Grab(Color background)
            {
                cam.backgroundColor = background;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                read.Apply(false);
                RenderTexture.active = prev;
                var px = read.GetPixels32();
                Object.DestroyImmediate(read);
                return px;
            }

            public void Dispose()
            {
                foreach (var (l, on) in worldLights) if (l != null) l.enabled = on;
                RenderSettings.fog = fog;
                if (cam != null) cam.targetTexture = null;
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
                if (root != null) Object.Destroy(root);
            }
        }

        /// <summary>The kit's thick dark outline: the shape grown by a few pixels (octagonal, so corners stay round) behind it.</summary>
        static void AddOutline(Color32[] px)
        {
            int n = Size;
            var a = new byte[n * n];
            for (int i = 0; i < a.Length; i++) a[i] = px[i].a;
            var tmp = new byte[n * n];
            for (int pass = 0; pass < OutlinePx; pass++)
            {
                bool cross = (pass & 1) == 1;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        byte m = a[y * n + x];
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (cross && dx != 0 && dy != 0) continue;
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                                byte v = a[yy * n + xx];
                                if (v > m) m = v;
                            }
                        tmp[y * n + x] = m;
                    }
                var t = a; a = tmp; tmp = t;
            }
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float fa = c.a / 255f, oa = a[i] / 255f;
                if (oa <= fa) continue;
                // The picture over its outline.
                float outA = fa + oa * (1f - fa);
                float k = outA > 0 ? fa / outA : 0;
                px[i] = new Color32(
                    (byte)(c.r * k + OutlineColor.r * (1 - k)),
                    (byte)(c.g * k + OutlineColor.g * (1 - k)),
                    (byte)(c.b * k + OutlineColor.b * (1 - k)),
                    (byte)(outA * 255));
            }
        }
    }
}
