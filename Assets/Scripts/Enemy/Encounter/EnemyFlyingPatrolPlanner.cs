using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    /// <summary>有界立體取樣與連通節點篩選；碰撞判定由現有 Navigator 提供。</summary>
    public static class EnemyFlyingPatrolPlanner
    {
        // 每軸最多 6 格，加出生盒 27 點：最多 243 個候選，沒有逐 Tick 取樣。
        public static List<Vector3> Plan(Matrix4x4 localToWorld, Bounds patrolBounds,
            Bounds spawnBounds, int samplesPerAxis, int maximumPoints, float minimumSpacing,
            Func<Vector3, Vector3, bool> canTraverse)
        {
            var result = new List<Vector3>();
            if (canTraverse == null || maximumPoints < 2 ||
                patrolBounds.size.x <= 0f || patrolBounds.size.y <= 0f || patrolBounds.size.z <= 0f)
                return result;

            var candidates = new List<Vector3>();
            float spacingSquared = Mathf.Max(0.01f, minimumSpacing) * Mathf.Max(0.01f, minimumSpacing);
            AddGrid(localToWorld, spawnBounds, patrolBounds, 3, spacingSquared, canTraverse, candidates);
            int seedCount = candidates.Count;
            if (seedCount == 0) return result;
            AddGrid(localToWorld, patrolBounds, patrolBounds, Mathf.Clamp(samplesPerAxis, 3, 6),
                spacingSquared, canTraverse, candidates);

            var visited = new bool[candidates.Count];
            var best = new List<int>();
            // 只保留至少包含一個合法出生點的最大連通集合，避免選到牆另一側的空間。
            for (int seed = 0; seed < seedCount; seed++)
            {
                if (visited[seed]) continue;
                var component = new List<int> { seed };
                visited[seed] = true;
                for (int cursor = 0; cursor < component.Count; cursor++)
                {
                    int from = component[cursor];
                    for (int to = 0; to < candidates.Count; to++)
                    {
                        if (visited[to] ||
                            !canTraverse(candidates[from], candidates[to]) ||
                            !canTraverse(candidates[to], candidates[from])) continue;
                        visited[to] = true;
                        component.Add(to);
                    }
                }
                if (component.Count > best.Count) best = component;
            }

            if (best.Count < 2) return result;
            // BFS 前綴仍連通。不可先洗牌再截斷，否則可能刪掉必要的中繼點。
            int count = Mathf.Min(best.Count, Mathf.Clamp(maximumPoints, 2, 64));
            for (int i = 0; i < count; i++) result.Add(candidates[best[i]]);
            return result;
        }

        private static void AddGrid(Matrix4x4 localToWorld, Bounds sampleBounds, Bounds allowedBounds,
            int count, float spacingSquared, Func<Vector3, Vector3, bool> canTraverse, List<Vector3> points)
        {
            for (int y = 0; y < count; y++)
            for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
            {
                Vector3 local = sampleBounds.min + Vector3.Scale(sampleBounds.size,
                    new Vector3((x + 0.5f) / count, (y + 0.5f) / count, (z + 0.5f) / count));
                if (!allowedBounds.Contains(local)) continue;
                Vector3 world = localToWorld.MultiplyPoint3x4(local);
                bool close = false;
                foreach (Vector3 existing in points)
                    if ((existing - world).sqrMagnitude < spacingSquared) { close = true; break; }
                // 零長度通道查詢代表膠囊在該位置沒有重疊障礙。
                if (!close && canTraverse(world, world)) points.Add(world);
            }
        }
    }
}