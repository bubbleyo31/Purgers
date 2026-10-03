using System;
using System.Collections.Generic;
using Fusion;
using Purgers.GameFlow.Stage;
using Purgers.MapGeneration.Topology;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Purgers.Map
{
    public struct MapChunkPlacement : INetworkStruct
    {
        public int CatalogIndex;
        public int ContentVersion;
        public Vector3 Position;
        public Quaternion Rotation;
        public int OpenSides;
        public int ConnectedFromIndex;
        public int EntrySide;
        public int ExitSide;
    }

    /// <summary>Stage-scoped authoritative layout and exploration. Does not own AI or player movement.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkMapState : NetworkBehaviour
    {
        [SerializeField, Tooltip("既有地圖生成 owner。只讀它已完成的配置，不再抽一次 Seed。")]
        private MapRunSelectionPrototype selection;
        [SerializeField, Tooltip("Host／Client 必須使用相同順序的 Chunk Prefab；每份需有預烘焙 Cartography。")]
        private MapChunk[] catalog;
        [SerializeField, Tooltip("僅 Host 的設定生效；Play Mode 可即時切換。個人探索紀錄一直保留。")]
        private bool shareTeamExploration;
        [SerializeField, Min(1), Tooltip("玩家周圍可探索的水平半徑（公尺），與地圖視野及標記距離無關。")]
        private float explorationRadius = 22f;
        [SerializeField, Min(0.5f), Tooltip("允許探索玩家上方表面的高度差；飛行時下方不設高度上限，但仍需水平接近與地形視線。單層底圖不支援重疊樓層。")]
        private float verticalTolerance = 6f;
        [SerializeField, Tooltip("遮蔽查詢使用的物理層。只把屬於本輪 MapChunk 的 Collider 當地形遮蔽。")]
        private LayerMask occlusionMask = ~0;

        [Networked] public int LayoutRevision { get; private set; }
        [Networked] public int ChunkCount { get; private set; }
        [Networked] public int FinalChunkIndex { get; private set; }
        [Networked] public int SelectedExtractionIndex { get; private set; }
        [Networked] public NetworkBool SharedExploration { get; private set; }
        [Networked, Capacity(16)] public NetworkArray<MapChunkPlacement> Placements => default;

        private readonly List<MapChunk> chunks = new();
        private readonly List<int> openSides = new();
        private readonly MinimapBlockerIndex blockers = new();
        private readonly Queue<SnapshotPacket> snapshots = new();
        private readonly HashSet<PlayerRef> snapshotRequested = new();
        private readonly RaycastHit[] sightHits = new RaycastHit[32];
        private ExplorationStore exploration = new();
        private bool spawned, applying, failed, requestedSnapshot;
        private float nextExplorationTime;
        public bool IsReady { get; private set; }
        public bool HasExplorationSnapshot { get; private set; }
        public string Failure { get; private set; }
        public IReadOnlyList<MapChunk> Chunks => chunks;
        public ExplorationStore Exploration => exploration;
        public float ExplorationRadius => explorationRadius;
        public float VerticalTolerance => verticalTolerance;
        public bool IsNetworkReady => spawned && Object != null && Object.IsValid && Runner != null && Runner.IsRunning;

        public bool TryGetFinalChunk(out MapChunk chunk)
        {
            if (IsReady &&
                FinalChunkIndex >= 0 &&
                FinalChunkIndex < chunks.Count)
            {
                chunk = chunks[FinalChunkIndex];
                return chunk != null;
            }

            chunk = null;
            return false;
        }

        private readonly struct SnapshotPacket
        {
            public readonly PlayerRef Target;
            public readonly int[] Words;
            public readonly bool Complete;
            public SnapshotPacket(PlayerRef target, int[] words, bool complete)
            { Target = target; Words = words; Complete = complete; }
        }

        public override void Spawned()
        {
            spawned = true;
            if (Object.HasStateAuthority)
            {
                SharedExploration = shareTeamExploration;
                SelectedExtractionIndex = -1;
            }
        }

        public bool TryPublishExtractionSelection(
            int selectedIndex,
            int extractionCandidateCount)
        {
            if (!IsNetworkReady ||
                !Object.HasStateAuthority ||
                !IsReady ||
                !MapLayoutRules.IsValidExtractionSelection(
                    selectedIndex,
                    extractionCandidateCount))
            {
                return false;
            }

            SelectedExtractionIndex = selectedIndex;
            return true;
        }

        public bool SetSharedExploration(bool shared)
        {
            if (!IsNetworkReady || !Object.HasStateAuthority) return false;
            shareTeamExploration = shared;
            SharedExploration = shared;
            return true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !Runner.IsServer || failed) return;
            SharedExploration = shareTeamExploration;
            if (!IsReady)
            {
                if (selection != null && selection.IsMapPreparationReady) PublishGeneratedLayout();
                return;
            }
            var stage = GetComponent<StageFlowController>();
            if (stage == null || stage.Phase != StagePhase.Active || Runner.SimulationTime < nextExplorationTime)
                return;
            nextExplorationTime = (float)Runner.SimulationTime + 0.1f;
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetPlayerObject(player, out NetworkObject obj) || obj == null || !obj.IsValid)
                    continue;
                var health = obj.GetComponent<PlayerHealth>();
                if (health == null || !health.IsAlive) continue;
                RevealAround(player.RawEncoded, obj.transform.position);
            }
        }

        public override void Render()
        {
            if (!IsNetworkReady || failed) return;
            if (!Object.HasStateAuthority && !IsReady && LayoutRevision > 0 && !applying)
                ApplyReplicaLayout();
            if (!IsReady) return;
            if (!Object.HasStateAuthority && !requestedSnapshot)
            {
                requestedSnapshot = true;
                RPC_RequestExploration();
            }
            if (Object.HasStateAuthority)
            {
                List<ExplorationStore.Word> changes = exploration.DrainChanges();
                for (int offset = 0; offset < changes.Count; offset += 24)
                    RPC_ExplorationDelta(Pack(changes, offset));
                for (int i = 0; i < 4 && snapshots.Count > 0; i++)
                {
                    SnapshotPacket packet = snapshots.Dequeue();
                    RPC_ExplorationSnapshot(packet.Target, packet.Words, packet.Complete);
                }
            }
        }

        private void PublishGeneratedLayout()
        {
            if (selection.Alignment == null ||
                selection.Alignment.GeneratedChunks.Count == 0 ||
                selection.Alignment.GeneratedChunks.Count !=
                selection.Alignment.OpenSideMasks.Count ||
                selection.Alignment.TopologyPlan == null ||
                !selection.Alignment.TopologyPlan.IsComplete)
            {
                Fail("Host Phase 4-B 地圖結果不存在、不完整，或 Chunk／開口數量不一致。");
                return;
            }

            chunks.AddRange(selection.Alignment.GeneratedChunks);
            openSides.AddRange(selection.Alignment.OpenSideMasks);
            FinalChunkIndex =
                selection.Alignment.TopologyPlan.FinalChunkInstanceIndex;

            if (FinalChunkIndex < 0 || FinalChunkIndex >= chunks.Count)
            {
                Fail("Host 發布的最後 Chunk 索引無效。");
                return;
            }

            var publishedPlacements =
                new MapChunkPlacement[chunks.Count];

            for (int i = 0; i < chunks.Count; i++)
            {
                MapChunk chunk = chunks[i];
                MapTopologyPlacement topologyPlacement =
                    selection.Alignment.TopologyPlan.Placements[i];
                int index = Array.FindIndex(catalog ?? Array.Empty<MapChunk>(), prefab =>
                    prefab != null && prefab.Cartography != null && prefab.Cartography == chunk.Cartography);
                if (index < 0 || chunk.Cartography == null || !chunk.Cartography.IsValid)
                { Fail("Chunk 缺少有效 Cartography 或不在相同 Catalog 中：" + chunk.name); return; }
                if ((chunk.transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
                { Fail("小地圖區塊目前要求世界 Scale 為 1：" + chunk.name); return; }
                publishedPlacements[i] = new MapChunkPlacement {
                    CatalogIndex = index, ContentVersion = chunk.Cartography.ContentVersion,
                    Position = chunk.transform.position, Rotation = chunk.transform.rotation,
                    OpenSides = openSides[i],
                    ConnectedFromIndex = topologyPlacement.ConnectedFromInstanceIndex,
                    EntrySide = (int)topologyPlacement.LocalEntryDirection,
                    ExitSide = topologyPlacement.LocalExitDirection.HasValue
                        ? (int)topologyPlacement.LocalExitDirection.Value
                        : -1 };
            }

            if (!MapLayoutRules.TryValidateLinearTopology(
                    publishedPlacements,
                    publishedPlacements.Length,
                    FinalChunkIndex,
                    out string topologyFailure))
            {
                Fail("Host 拒絕發布不完整拓撲：" + topologyFailure);
                return;
            }

            for (int i = 0; i < publishedPlacements.Length; i++)
                Placements.Set(i, publishedPlacements[i]);

            ChunkCount = chunks.Count;
            LayoutRevision = 1;
            FinishLayout();
            HasExplorationSnapshot = true;
        }

        private void ApplyReplicaLayout()
        {
            applying = true;
            try
            {
                if (selection == null || selection.StartingChunk == null || selection.Alignment == null ||
                    selection.Alignment.Blockers == null ||
                    FinalChunkIndex < 0 || FinalChunkIndex >= ChunkCount)
                    throw new InvalidOperationException("Client 地圖接線或 ChunkCount 無效。");

                var publishedPlacements =
                    new MapChunkPlacement[ChunkCount];
                for (int i = 0; i < ChunkCount; i++)
                    publishedPlacements[i] = Placements[i];

                if (!MapLayoutRules.TryValidateLinearTopology(
                        publishedPlacements,
                        ChunkCount,
                        FinalChunkIndex,
                        out string topologyFailure))
                {
                    throw new InvalidOperationException(
                        "Client 拒絕不完整拓撲：" + topologyFailure);
                }

                // Validate the whole catalog before creating any geometry.
                for (int i = 0; i < ChunkCount; i++)
                {
                    MapChunkPlacement p = publishedPlacements[i];
                    if (catalog == null || p.CatalogIndex < 0 || p.CatalogIndex >= catalog.Length ||
                        catalog[p.CatalogIndex] == null || catalog[p.CatalogIndex].Cartography == null ||
                        !catalog[p.CatalogIndex].Cartography.IsValid ||
                        catalog[p.CatalogIndex].Cartography.ContentVersion != p.ContentVersion)
                        throw new InvalidOperationException("Host／Client Cartography 版本或 Catalog 不一致。");
                }
                MapChunk sceneFirst = selection.StartingChunk;
                MapChunk expectedFirst =
                    catalog[publishedPlacements[0].CatalogIndex];
                MapChunk first = sceneFirst;

                if (sceneFirst.Cartography != expectedFirst.Cartography)
                {
                    first = Instantiate(
                        expectedFirst,
                        sceneFirst.transform.parent);
                    if (first.gameObject.scene != sceneFirst.gameObject.scene)
                    {
                        SceneManager.MoveGameObjectToScene(
                            first.gameObject,
                            sceneFirst.gameObject.scene);
                    }

                    if (!selection.TryAdoptReplicaStartingChunk(first))
                    {
                        Destroy(first.gameObject);
                        throw new InvalidOperationException(
                            "Client 無法採用 Host 發布的專用起始 Chunk。");
                    }
                }

                for (int i = 0; i < ChunkCount; i++)
                {
                    MapChunkPlacement p = publishedPlacements[i];
                    MapChunk chunk = i == 0 ? first : Instantiate(catalog[p.CatalogIndex], first.transform.parent);
                    if (i != 0 && chunk.gameObject.scene != first.gameObject.scene)
                        SceneManager.MoveGameObjectToScene(chunk.gameObject, first.gameObject.scene);
                    chunk.transform.SetPositionAndRotation(p.Position, p.Rotation);
                    // AI queries belong to Host. Client cartography is prebaked and needs no global NavMesh registration.
                    // Disabling only this replica's surfaces also avoids duplicate meshes in Multi-Peer Editor mode.
                    foreach (var surface in chunk.GetComponentsInChildren<NavMeshSurface>(true))
                    { surface.RemoveData(); surface.enabled = false; }
                    chunks.Add(chunk);
                    openSides.Add(p.OpenSides);
                }
                if (!selection.Alignment.Blockers.TryGenerateForLayout(chunks, openSides))
                    throw new InvalidOperationException("Client 封口建立失敗。");
                Physics.SyncTransforms();
                FinishLayout();
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private void FinishLayout()
        {
            blockers.Clear();
            if (selection.Alignment != null && selection.Alignment.Blockers != null)
                foreach (GameObject blocker in selection.Alignment.Blockers.SpawnedBlockers)
                    foreach (Collider collider in blocker.GetComponentsInChildren<Collider>())
                        if (collider.enabled && !collider.isTrigger) blockers.Add(collider.bounds);
            IsReady = true;
        }

        private void Fail(string message)
        {
            failed = true; Failure = message;
            Debug.LogError("[NetworkMapState] " + message, this);
        }

        public bool IsBlocked(Vector3 floor)
        {
            return blockers.Contains(floor + Vector3.up);
        }

        public bool HasLineOfSight(Vector3 from, Vector3 target)
        {
            Vector3 start = from + Vector3.up * 1.5f;
            Vector3 delta = target + Vector3.up * 1.5f - start;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            int count = Runner.GetPhysicsScene().Raycast(start, delta / distance, sightHits,
                distance, occlusionMask, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false; // Full query buffer: fail closed.
            for (int i = 0; i < count; i++)
            {
                var chunk = sightHits[i].collider.GetComponentInParent<MapChunk>();
                if (chunk != null && chunks.Contains(chunk)) return false;
            }
            return true;
        }

        private void RevealAround(int player, Vector3 position)
        {
            float radiusSquared = explorationRadius * explorationRadius;
            for (int c = 0; c < chunks.Count; c++)
            {
                MapChunk chunk = chunks[c];
                var data = chunk.Cartography;
                Vector3 local = chunk.transform.InverseTransformPoint(position);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((local.x - explorationRadius - data.Minimum.x) / data.CellSize));
                int x1 = Mathf.Min(data.Width - 1, Mathf.FloorToInt((local.x + explorationRadius - data.Minimum.x) / data.CellSize));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((local.z - explorationRadius - data.Minimum.y) / data.CellSize));
                int y1 = Mathf.Min(data.Height - 1, Mathf.FloorToInt((local.z + explorationRadius - data.Minimum.y) / data.CellSize));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    int cell = y * data.Width + x;
                    if (data.Kind(cell) == 0 || exploration.IsExplored(player, c, cell, false)) continue;
                    Vector3 floor = chunk.transform.TransformPoint(data.CellCenter(cell));
                    float dx = floor.x - position.x, dz = floor.z - position.z;
                    if (dx * dx + dz * dz > radiusSquared ||
                        !MinimapRules.CanExploreHeight(position.y, floor.y, verticalTolerance) || IsBlocked(floor)) continue;
                    if (HasLineOfSight(position, floor)) exploration.Reveal(player, c, cell);
                }
            }
        }

        public bool TryGetCell(Vector3 world, out int chunkIndex, out int cell)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                int candidate = chunk.Cartography.CellAt(chunk.transform.InverseTransformPoint(world));
                if (candidate >= 0 && chunk.Cartography.Kind(candidate) != 0)
                { chunkIndex = i; cell = candidate; return true; }
            }
            chunkIndex = cell = -1; return false;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable, TickAligned = false)]
        private void RPC_RequestExploration(RpcInfo info = default)
        {
            if (!IsReady || !info.Source.IsRealPlayer || snapshotRequested.Contains(info.Source)) return;
            bool connected = false;
            foreach (PlayerRef player in Runner.ActivePlayers) if (player == info.Source) connected = true;
            if (!connected) return;
            snapshotRequested.Add(info.Source);
            List<ExplorationStore.Word> words = exploration.Snapshot();
            for (int offset = 0; offset < words.Count; offset += 24)
                snapshots.Enqueue(new SnapshotPacket(info.Source, Pack(words, offset), false));
            snapshots.Enqueue(new SnapshotPacket(info.Source, Array.Empty<int>(), true));
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable, TickAligned = false)]
        private void RPC_ExplorationDelta(int[] words) => ApplyWords(words);

        [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable, TickAligned = false)]
        private void RPC_ExplorationSnapshot([RpcTarget] PlayerRef target, int[] words, bool complete)
        {
            ApplyWords(words);
            if (complete) HasExplorationSnapshot = true;
        }

        private void ApplyWords(int[] words)
        {
            if (!IsReady || Object.HasStateAuthority || words == null || words.Length % 4 != 0) return;
            for (int i = 0; i < words.Length; i += 4)
            {
                int chunk = words[i + 1], word = words[i + 2];
                if (chunk < 0 || chunk >= chunks.Count || word < 0 || word >= (chunks[chunk].Cartography.CellCount + 31) / 32)
                    continue;
                exploration.Merge(words[i], chunk, word, unchecked((uint)words[i + 3]));
            }
        }

        private static int[] Pack(List<ExplorationStore.Word> words, int offset)
        {
            int count = Math.Min(24, words.Count - offset);
            var result = new int[count * 4];
            for (int i = 0; i < count; i++)
            {
                var w = words[offset + i];
                result[i * 4] = w.Player; result[i * 4 + 1] = w.Chunk;
                result[i * 4 + 2] = w.Index; result[i * 4 + 3] = unchecked((int)w.Bits);
            }
            return result;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            spawned = false; IsReady = false;
            exploration = new ExplorationStore(); snapshots.Clear(); snapshotRequested.Clear();
        }
    }
}
