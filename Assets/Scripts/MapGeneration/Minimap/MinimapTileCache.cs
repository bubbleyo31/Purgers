using System;
using UnityEngine;

namespace Purgers.Map
{
    /// <summary>One native-grid texture per generated chunk, independent of viewport pixel resolution.</summary>
    public sealed class MinimapTileCache : IDisposable
    {
        public readonly MapCartographyData Data;
        public readonly Texture2D Terrain, Exploration;
        private readonly Color32[] terrain, exploration;
        private int cursor;
        private float baseHeight = float.MaxValue;
        private bool explorationDirty;
        public bool Ready => cursor >= Data.CellCount;
        public int ExploredCells { get; private set; }
        public int UploadCount { get; private set; }

        public MinimapTileCache(MapCartographyData data)
        {
            Data = data;
            terrain = new Color32[data.CellCount];
            exploration = new Color32[data.CellCount];
            Terrain = NewTexture("Minimap terrain", data);
            Exploration = NewTexture("Minimap exploration", data);
            for (int i = 0; i < data.CellCount; i++)
                if (data.Kind(i) != 0) baseHeight = Mathf.Min(baseHeight, data.Elevation(i));
            Exploration.SetPixels32(exploration);
            Exploration.Apply(false, false);
        }

        private static Texture2D NewTexture(string name, MapCartographyData data)
            => new Texture2D(data.Width, data.Height, TextureFormat.RGBA32, false)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        /// <summary>Bounded startup work. No scene or dictionary queries after the tile is complete.</summary>
        public int Build(int budget, Matrix4x4 localToWorld, Func<Vector3, bool> blocked)
        {
            int begin = cursor;
            int end = Mathf.Min(cursor + budget, Data.CellCount);
            for (; cursor < end; cursor++)
            {
                byte kind = Data.Kind(cursor);
                bool closed = kind != 0 && blocked != null && blocked(localToWorld.MultiplyPoint3x4(Data.CellCenter(cursor)));
                terrain[cursor] = MinimapRules.HeightColor(closed ? (byte)0 : kind, Data.Elevation(cursor) - baseHeight);
                // Empty cells must not cover a neighboring rotated chunk.
                if (kind == 0) terrain[cursor].a = 0;
            }
            if (begin < Data.CellCount && Ready)
            {
                Terrain.SetPixels32(terrain);
                Terrain.Apply(false, true);
                UploadCount++;
            }
            return cursor - begin;
        }

        public void RevealWord(int word, uint bits)
        {
            for (int bit = 0; bit < 32; bit++)
            {
                int cell = word * 32 + bit;
                if (cell < 0 || cell >= exploration.Length || (bits & (1u << bit)) == 0 ||
                    Data.Kind(cell) == 0 || exploration[cell].a != 0) continue;
                // Subtle exploration tint preserves the height hierarchy.
                exploration[cell] = new Color32(85, 175, 155, 28);
                ExploredCells++;
                explorationDirty = true;
            }
        }

        public void ClearExploration()
        {
            Array.Clear(exploration, 0, exploration.Length);
            ExploredCells = 0; explorationDirty = true;
        }

        public void FlushExploration()
        {
            if (!explorationDirty) return;
            Exploration.SetPixels32(exploration);
            Exploration.Apply(false, false);
            UploadCount++;
            explorationDirty = false;
        }

        public void Dispose()
        {
            if (Application.isPlaying) { UnityEngine.Object.Destroy(Terrain); UnityEngine.Object.Destroy(Exploration); }
            else { UnityEngine.Object.DestroyImmediate(Terrain); UnityEngine.Object.DestroyImmediate(Exploration); }
        }
    }
}
