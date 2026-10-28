using Founders.Core;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// Map cells on the ground. Cell (x, y) is centred at (x·S, 0, y·S): x runs east, y runs south
    /// (row 0 of the map is the far north). The Unity version used (x·S, 0, −y·S) in its left-handed
    /// space, which looks the same.
    /// </summary>
    public static class Grid
    {
        public const float CellSize = 2f;

        public static Vector3 CellCenter(float x, float y) => new Vector3(x * CellSize, 0f, y * CellSize);

        public static Vector3 FootprintCenter(int x, int y, Footprint fp) =>
            CellCenter(x + (fp.Width - 1) * 0.5f, y + (fp.Height - 1) * 0.5f);

        public static (int x, int y) CellAt(Vector3 world) =>
            (Mathf.RoundToInt(world.X / CellSize), Mathf.RoundToInt(world.Z / CellSize));
    }

    /// <summary>Perlin noise in 0..1 like Unity's Mathf.PerlinNoise, so the ported shapes keep their look.</summary>
    public static class Perlin
    {
        static readonly FastNoiseLite noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 1f,
            FractalType = FastNoiseLite.FractalTypeEnum.None,
            Seed = 1337,
        };

        public static float At(float x, float y) => Mathf.Clamp(noise.GetNoise2D(x, y) * 0.75f + 0.5f, 0f, 1f);
    }
}
