using System.Collections.Generic;
using UnityEngine;

namespace Purgers.Map
{
    /// <summary>Broad phase only; exact world Bounds.Contains remains the final blocker rule.</summary>
    public sealed class MinimapBlockerIndex
    {
        private const float BucketSize = 16f;
        private readonly Dictionary<Vector2Int, List<Bounds>> buckets = new();

        public void Clear() => buckets.Clear();

        public void Add(Bounds bounds)
        {
            Vector3 min = bounds.min, max = bounds.max;
            for (int z = Cell(min.z); z <= Cell(max.z); z++)
                for (int x = Cell(min.x); x <= Cell(max.x); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out var list)) buckets.Add(key, list = new List<Bounds>());
                    list.Add(bounds);
                }
        }

        public bool Contains(Vector3 position)
        {
            if (!buckets.TryGetValue(new Vector2Int(Cell(position.x), Cell(position.z)), out var list)) return false;
            for (int i = 0; i < list.Count; i++) if (list[i].Contains(position)) return true;
            return false;
        }

        private static int Cell(float coordinate) => Mathf.FloorToInt(coordinate / BucketSize);
    }
}
