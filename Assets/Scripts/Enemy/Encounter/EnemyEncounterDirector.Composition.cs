using System;
using System.Collections.Generic;
using Fusion;
using Purgers.Map;
using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    public sealed partial class EnemyEncounterDirector
    {
        private bool HasActiveGroundCandidates()
        {
            foreach (MapChunk chunk in mapState.Chunks)
            {
                if (chunk == null) continue;
                foreach (EnemySpawnZone zone in chunk.GetComponentsInChildren<EnemySpawnZone>(true))
                {
                    if (!zone.isActiveAndEnabled || zone.GetComponentInParent<MapChunk>() != chunk || zone.GetGates().Length == 0) continue;
                    if (!zone.UsesEnemyEntries)
                    {
                        if (zone.LocomotionKind == EnemyLocomotionKind.Ground) return true;
                        continue;
                    }
                    foreach (EnemySpawnEntry entry in zone.EnemyEntries)
                    {
                        if (entry == null || !entry.IsEnabled) continue;
                        try
                        {
                            NetworkPrefabId id = Runner.Prefabs.GetId((NetworkObjectGuid)entry.Prefab);
                            NetworkObject asset = id.IsValid ? Runner.Prefabs.Load(id, true) : null;
                            EnemyActor actor = asset != null ? asset.GetComponent<EnemyActor>() : null;
                            if (actor != null && actor.Definition != null &&
                                actor.Definition.LocomotionKind == EnemyLocomotionKind.Ground) return true;
                        }
                        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                        {
                            // 正式候選建置統一報錯；無效 Prefab 不應阻擋其他種類。
                        }
                    }
                }
            }
            return false;
        }
        // false 僅表示地面導航尚未就緒，需要下一 Tick 重試；設定錯誤只排除此候選。
        private bool TryBuildCandidates(ZoneRuntime runtime, MapChunk chunk, int chunkIndex)
        {
            EnemySpawnZone zone = runtime.Zone;
            if (zone.UsesEnemyEntries)
            {
                foreach (EnemySpawnEntry entry in zone.EnemyEntries)
                {
                    if (entry == null || !entry.IsEnabled) continue;
                    if (!TryAddCandidate(runtime, chunk, chunkIndex, entry.Prefab, entry)) return false;
                }
            }
            else
            {
                foreach (NetworkPrefabRef prefab in zone.EnemyPrefabs)
                    if (prefab.IsValid && !TryAddCandidate(runtime, chunk, chunkIndex, prefab, null)) return false;
            }
            return true;
        }

        private bool TryAddCandidate(ZoneRuntime zone, MapChunk chunk, int chunkIndex,
            NetworkPrefabRef prefab, EnemySpawnEntry entry)
        {
            try
            {
                NetworkPrefabId id = Runner.Prefabs.GetId((NetworkObjectGuid)prefab);
                NetworkObject asset = id.IsValid ? Runner.Prefabs.Load(id, true) : null;
                EnemyActor actor = asset != null ? asset.GetComponent<EnemyActor>() : null;
                if (actor == null || actor.Definition == null)
                    throw new InvalidOperationException("Prefab 未登記，或缺少 EnemyActor／EnemyDefinition。");
                EnemyLocomotionKind kind = actor.Definition.LocomotionKind;
                if (!zone.Zone.UsesEnemyEntries && kind != zone.Zone.LocomotionKind)
                    throw new InvalidOperationException("舊版清單的移動類型與 Zone 不符；請改用混合敵人清單。");
                var candidate = new CandidateRuntime { Prefab = prefab, PrefabName = asset.name, Kind = kind, Entry = entry };
                if (kind == EnemyLocomotionKind.Ground)
                {
                    if (navigation == null || !navigation.IsReady ||
                        navigation.GeneratedGroundAreas.Count != mapState.Chunks.Count) return false;
                    candidate.PatrolArea = navigation.GeneratedGroundAreas[chunkIndex];
                }
                else if (kind == EnemyLocomotionKind.FreeFlying)
                {
                    // 相同 Prefab 重複條目共用規劃結果，但機率仍各自判定。
                    CandidateRuntime existing = zone.Candidates.Find(item => item.Prefab.Equals(prefab));
                    if (existing != null)
                    {
                        candidate.PatrolArea = existing.PatrolArea;
                        candidate.FlyingNavigators = existing.FlyingNavigators;
                        candidate.FlyingMinimumTravel = existing.FlyingMinimumTravel;
                    }
                    else if (!TryResolveFlyingArea(zone.Zone, prefab, out candidate.PatrolArea,
                        out candidate.FlyingNavigators, out candidate.FlyingMinimumTravel)) return true;
                }
                else if (kind != EnemyLocomotionKind.Stationary)
                    throw new InvalidOperationException("不支援的敵人移動類型。");

                if (kind != EnemyLocomotionKind.Stationary &&
                    (candidate.PatrolArea == null || candidate.PatrolArea.PointCount == 0 ||
                     candidate.PatrolArea.GetComponentInParent<MapChunk>() != chunk))
                    throw new InvalidOperationException("找不到同一 Chunk 內有效的自動／人工巡邏區。");
                zone.Candidates.Add(candidate);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                Debug.LogError($"[Enemy Encounter] {zone.Zone.name} 排除敵人 {prefab}：{exception.Message}", zone.Zone);
            }
            return true;
        }

        private void BuildActivationBatches(ZoneRuntime zone)
        {
            pendingBatches.Clear();
            if (zone.Zone.UsesEnemyEntries)
            {
                foreach (CandidateRuntime candidate in zone.Candidates)
                {
                    int count = candidate.Entry.RollCount(UnityEngine.Random.value);
                    if (count > 0) pendingBatches.Add(new SpawnBatch { Candidate = candidate, Remaining = count });
                }
                return;
            }
            // 舊資產維持原本每個名額等機率抽選的行為，不自動改寫序列化資料。
            for (int i = 0; i < zone.Zone.EnemiesPerActivation; i++)
            {
                if (!zone.Zone.TryChoosePrefab(out NetworkPrefabRef prefab)) break;
                CandidateRuntime candidate = zone.Candidates.Find(item => item.Prefab.Equals(prefab));
                if (candidate != null) pendingBatches.Add(new SpawnBatch { Candidate = candidate, Remaining = 1 });
            }
        }
    }
}
