using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// The map generator's pictures: the valley seen from above as a painted relief map (meadows,
    /// forests, stony hills, peaks, water with lighter shallows, contour lines and hill shading), and the
    /// little round swatches beside each slider. Drawn on the CPU in a few milliseconds.
    /// </summary>
    public static class MapPreview
    {
        static readonly Color Meadow = new Color(0.47f, 0.60f, 0.25f);
        static readonly Color Woods = new Color(0.22f, 0.40f, 0.17f);
        static readonly Color Sand = new Color(0.83f, 0.76f, 0.52f);
        static readonly Color Stone = new Color(0.62f, 0.58f, 0.48f);
        static readonly Color Peak = new Color(0.50f, 0.46f, 0.43f);
        static readonly Color Snow = new Color(0.93f, 0.94f, 0.96f);
        static readonly Color Rust = new Color(0.66f, 0.38f, 0.22f);
        static readonly Color Shallow = new Color(0.46f, 0.70f, 0.86f);
        static readonly Color Deep = new Color(0.20f, 0.42f, 0.66f);

        static Color CellColour(char c) => c switch
        {
            'T' => Woods, ',' => Sand, 'R' => Stone, 'M' => Peak, 'O' => Rust, '~' => Deep, _ => Meadow,
        };

        static float CellHeight(char c) => c switch
        {
            '~' => -1.2f, ',' => 0.02f, 'T' => 0.12f, 'R' => 0.45f, 'O' => 0.5f, 'M' => 1.6f, _ => 0.08f,
        };

        /// <summary>Renders <paramref name="map"/> into <paramref name="tex"/> (made or resized as needed, <paramref name="size"/> pixels square).</summary>
        public static Texture2D Render(MapDefinition map, Texture2D tex, int size = 256)
        {
            int w = map.Width, h = map.Height;
            if (tex == null || tex.width != size)
            {
                if (tex != null) Object.Destroy(tex);
                tex = new Texture2D(size, size, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "MapPreview" };
            }
            // Heights per cell: water deepens away from the shore, mountains rise towards their heart.
            var height = new float[h, w];
            var colour = new Color[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = Cell(map, x, y);
                    height[y, x] = CellHeight(c);
                    colour[y, x] = CellColour(c);
                }
            // Water deepens and peaks rise with the distance from their edge (12 cells at most).
            for (int pass = 0; pass < 12; pass++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        char c = Cell(map, x, y);
                        if (c == 'M')
                        {
                            float low = Mathf.Min(Mathf.Min(At(height, x - 1, y), At(height, x + 1, y)), Mathf.Min(At(height, x, y - 1), At(height, x, y + 1)));
                            height[y, x] = Mathf.Min(height[y, x], low + 0.12f);
                        }
                        else if (c == '~')
                        {
                            float high = Mathf.Max(Mathf.Max(At(height, x - 1, y), At(height, x + 1, y)), Mathf.Max(At(height, x, y - 1), At(height, x, y + 1)));
                            height[y, x] = Mathf.Max(height[y, x], high - 0.12f);
                        }
                    }
            var smooth = Blur(Blur(height));
            var px = new Color32[size * size];
            float k = w / (float)size;
            for (int j = 0; j < size; j++)
            {
                // Texture rows run bottom-up; map row 0 is the north edge (top of the picture).
                float my = (size - 1 - j + 0.5f) * k - 0.5f;
                for (int i = 0; i < size; i++)
                {
                    float mx = (i + 0.5f) * k - 0.5f;
                    float e = Sample(smooth, mx, my);
                    var col = SampleColour(colour, mx, my);
                    char c = Cell(map, Mathf.RoundToInt(mx), Mathf.RoundToInt(my));
                    if (c == '~')
                    {
                        col = Color.Lerp(Shallow, Deep, Mathf.Clamp01(-e * 1.6f));
                    }
                    else
                    {
                        if (c == 'M') col = Color.Lerp(col, Snow, Mathf.Clamp01((e - 1.05f) * 2.5f));
                        // Little tree crowns in the woods.
                        if (c == 'T') col *= 0.9f + 0.2f * Hash(i / 3, j / 3);
                    }
                    // Light from the north-west.
                    float gx = Sample(smooth, mx + 0.7f, my) - Sample(smooth, mx - 0.7f, my);
                    float gy = Sample(smooth, mx, my + 0.7f) - Sample(smooth, mx, my - 0.7f);
                    float shade = Mathf.Clamp(1f - (gx + gy) * 1.4f, 0.7f, 1.25f);
                    col *= shade;
                    // Contour lines on land.
                    if (c != '~')
                    {
                        float band = e * 10f;
                        float f = Mathf.Abs(band - Mathf.Round(band));
                        if (f < 0.06f && e > 0.15f) col *= 0.82f;
                    }
                    else if (e > -0.12f) col = Color.Lerp(col, Color.white, 0.15f);
                    col.a = 1f;
                    px[j * size + i] = col;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        static char Cell(MapDefinition m, int x, int y)
        {
            x = Mathf.Clamp(x, 0, m.Width - 1);
            y = Mathf.Clamp(y, 0, m.Height - 1);
            var row = m.Rows[y];
            return x < row.Length ? row[x] : '.';
        }

        static float At(float[,] a, int x, int y) =>
            a[Mathf.Clamp(y, 0, a.GetLength(0) - 1), Mathf.Clamp(x, 0, a.GetLength(1) - 1)];

        static float[,] Blur(float[,] a)
        {
            int h = a.GetLength(0), w = a.GetLength(1);
            var b = new float[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++) s += At(a, x + dx, y + dy);
                    b[y, x] = s / 9f;
                }
            return b;
        }

        static float Sample(float[,] a, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            return Mathf.Lerp(Mathf.Lerp(At(a, x0, y0), At(a, x0 + 1, y0), fx), Mathf.Lerp(At(a, x0, y0 + 1), At(a, x0 + 1, y0 + 1), fx), fy);
        }

        static Color SampleColour(Color[,] a, float x, float y)
        {
            int h = a.GetLength(0), w = a.GetLength(1);
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color C(int xx, int yy) => a[Mathf.Clamp(yy, 0, h - 1), Mathf.Clamp(xx, 0, w - 1)];
            return Color.Lerp(Color.Lerp(C(x0, y0), C(x0 + 1, y0), fx), Color.Lerp(C(x0, y0 + 1), C(x0 + 1, y0 + 1), fx), fy);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // MARK: - Swatches

        static readonly System.Collections.Generic.Dictionary<MapSettings.Feature, Texture2D> swatches =
            new System.Collections.Generic.Dictionary<MapSettings.Feature, Texture2D>();

        /// <summary>A round painted badge for a slider: a tiny scene of the feature (lake, river, island, peaks…).</summary>
        public static Texture2D Swatch(MapSettings.Feature f)
        {
            if (swatches.TryGetValue(f, out var t) && t != null) return t;
            const int S = 64;
            t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, name = "Swatch_" + f };
            var px = new Color32[S * S];
            var rim = new Color(0.16f, 0.10f, 0.22f);
            for (int j = 0; j < S; j++)
                for (int i = 0; i < S; i++)
                {
                    // u, v in [-1, 1], v up.
                    float u = (i + 0.5f) / S * 2 - 1, v = (j + 0.5f) / S * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    Color c = Scene(f, u, v);
                    // Soft light from the top-left and a dark rim, like the kit's round buttons.
                    c *= 1.08f - 0.18f * (u - v) * 0.5f;
                    if (r > 0.86f) c = Color.Lerp(c, rim, Mathf.Clamp01((r - 0.86f) / 0.05f));
                    c.a = Mathf.Clamp01((1f - r) * S * 0.5f);
                    px[j * S + i] = c;
                }
            t.SetPixels32(px);
            t.Apply(false);
            swatches[f] = t;
            return t;
        }

        static Color Scene(MapSettings.Feature f, float u, float v)
        {
            switch (f)
            {
                case MapSettings.Feature.Lake:
                {
                    float d = (u * u) / 0.36f + (v + 0.05f) * (v + 0.05f) / 0.22f;
                    return d < 1 ? Color.Lerp(Shallow, Deep, 1 - d) : Meadow;
                }
                case MapSettings.Feature.River:
                {
                    float c = 0.35f * Mathf.Sin(v * 3.2f);
                    return Mathf.Abs(u - c) < 0.17f ? Color.Lerp(Shallow, Deep, 0.5f) : Mathf.Abs(u - c) < 0.24f ? Sand : Meadow;
                }
                case MapSettings.Feature.Island:
                {
                    float d = Mathf.Sqrt(u * u + v * v);
                    return d < 0.32f ? (d < 0.18f ? Woods : Meadow) : d < 0.4f ? Sand : Color.Lerp(Shallow, Deep, Mathf.Clamp01((d - 0.4f) * 2.5f));
                }
                case MapSettings.Feature.Mountain:
                {
                    float top = 0.55f - Mathf.Abs(u) * 1.4f;
                    if (v < top && v > -0.5f) return v > top - 0.25f ? Snow : (u < 0 ? Peak * 1.12f : Peak * 0.85f);
                    return Meadow;
                }
                case MapSettings.Feature.Hill:
                {
                    float a = 0.35f - ((u + 0.25f) * (u + 0.25f)) * 1.6f, b = 0.05f - ((u - 0.4f) * (u - 0.4f)) * 2.2f;
                    if (v < a && v > -0.45f) return u < -0.25f ? Stone * 1.1f : Stone * 0.9f;
                    if (v < b && v > -0.45f) return u < 0.4f ? Stone : Stone * 0.82f;
                    return Meadow;
                }
                case MapSettings.Feature.Forest:
                {
                    foreach (var (cx, cy, r) in new[] { (-0.3f, 0.15f, 0.3f), (0.28f, 0.2f, 0.28f), (0f, -0.25f, 0.32f) })
                    {
                        float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
                        if (d < r) return Woods * (1.15f - 0.4f * d / r);
                    }
                    return Meadow;
                }
                default: // ore
                {
                    foreach (var (cx, cy) in new[] { (-0.25f, 0.2f), (0.2f, 0.3f), (0.1f, -0.2f), (-0.3f, -0.3f) })
                        if ((u - cx) * (u - cx) + (v - cy) * (v - cy) < 0.03f) return Rust * 1.1f;
                    return Stone * 0.85f;
                }
            }
        }
    }
}
