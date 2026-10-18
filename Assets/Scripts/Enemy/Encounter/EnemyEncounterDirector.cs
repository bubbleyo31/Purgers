using System;
using System.Collections.Generic;
using Fusion;
using Purgers.GameFlow.Stage;
using Purgers.Map;
using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    /// <summary>
    /// 普通關卡的 Host Encounter owner。只讀地圖與玩家位置；不接管敵人 AI、
    /// 傷害、死亡獎勵或 Stage 結果。由 MapChunk Prefab 上的 Zone/Gate 決定內容。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed partial class EnemyEncounterDirector : NetworkBehaviour
    {
        // 小於 EnemyGroundPatrolNavigator 預設的 0.3m NavMesh 貼合距離。
        private const float GroundSpawnMaximumSupportOffset = 0.25f;

        [Header("關卡與地圖引用")]
        [SerializeField, Tooltip("同一 Runner 的 StageFlowController；建議與本元件放在同一個既有 Scene NetworkObject。")]
        private StageFlowController stageFlow;
        [SerializeField, Tooltip("同一 Runner 的 NetworkMapState；只讀 Host 已完成的 Chunk 清單。")]
        private NetworkMapState mapState;
        [SerializeField, Tooltip("本場景的 MapRunSelectionPrototype；由其 Alignment 取得 Host 預烘焙導航與逐 Chunk 巡邏區。")]
        private MapRunSelectionPrototype mapSelection;

        [Header("生成預算")]
        [SerializeField, Min(1), Tooltip("全場同一 Runner 同時存活的 Enemy 上限，包含其他正式來源；預設 12，可在 Inspector 依效能調整。")]
        private int maximumAliveEnemies = 12;
        [SerializeField, Min(1), Tooltip("同一生成區同時存活的敵人上限；預設 3。")]
        private int maximumAlivePerZone = 3;
        [SerializeField, Min(1), Tooltip("本 Director 尚未 Despawn 的 NetworkObject 上限，包含死亡動畫／屍體；預設 20。")]
        private int maximumOwnedNetworkObjects = 20;
        [SerializeField, Min(0.02f), Tooltip("兩次正式生成之間的最短秒數；預設 0.75。所有生成區共用。")]
        private float minimumSecondsBetweenSpawns = 0.75f;
        [SerializeField, Min(1), Tooltip("最近成功生成而暫時鎖定的區域數；預設 5。第 6 區成功時解鎖最早的一區。")]
        private int recentLockedZoneCount = 5;

        [Header("高速穿越與候選期限")]
        [SerializeField, Min(0.1f), Tooltip("單 Tick 允許做門檻穿越判定的最大位移，公尺；更大的跳躍視為傳送，避免沿線誤啟動。預設 40。")]
        private float maximumSweepDistance = 40f;
        [SerializeField, Min(0.1f), Tooltip("一次啟動後最多等待剩餘敵人生成的秒數；到期就取消，不累積欠怪。預設 3 秒。")]
        private float activationLifetimeSeconds = 3f;
        [SerializeField, Min(0.1f), Tooltip("出生點與觸發玩家的最遠距離，公尺；玩家跑太遠就取消剩餘生成。預設 55。")]
        private float maximumSpawnDistanceFromTrigger = 55f;
        [SerializeField, Min(0f), Tooltip("出生點沿本次門檻穿越方向至少領先觸發玩家的公尺數；雙向門檻反向進入時改用反方向，預設 2。")]
        private float minimumForwardDistance = 2f;

        [Header("出生安全檢查")]
        [SerializeField, Min(1), Tooltip("每次生成最多嘗試幾個出生盒內候選點；預設 16。失敗即放棄這次機會。")]
        private int candidateAttempts = 16;
        [SerializeField, Min(0.01f), Tooltip("地面候選點投影到既有 Agent Type NavMesh 的最大距離，公尺；預設 2。")]
        private float groundSampleDistance = 2f;
        [SerializeField, Min(0.1f), Tooltip("出生點距任何存活玩家至少幾公尺；預設 8。")]
        private float minimumPlayerDistance = 8f;
        [SerializeField, Tooltip("只勾會遮住玩家視線的實體世界 Layer。未設定則拒絕生成；不勾 Enemy／Player。")]
        private LayerMask worldOcclusionMask;
        [SerializeField, Tooltip("只勾會占用出生點的 Player／Enemy Layer。未設定則拒絕生成；不勾地板 World。")]
        private LayerMask spawnOccupancyMask;
        [SerializeField, Min(0.01f), Tooltip("出生點占用球半徑，公尺；預設 0.75。")]
        private float spawnOccupancyRadius = 0.75f;
        [SerializeField, Min(0f), Tooltip("出生點占用球中心相對 Root 往上高度，公尺；預設 0.9。")]
        private float spawnOccupancyCenterHeight = 0.9f;

        [Header("落後敵人清理")]
        [SerializeField, Min(1f), Tooltip("Enemy 與所有存活玩家都超過此距離、脫戰且不可見，才開始清理倒數；預設 70 公尺。")]
        private float cleanupDistance = 70f;
        [SerializeField, Min(0.1f), Tooltip("滿足遠離／脫戰／遮蔽條件要持續幾秒才由 Host Despawn；預設 8 秒。清理不發擊殺經驗。")]
        private float cleanupDelaySeconds = 8f;
        [SerializeField, Min(0.05f), Tooltip("檢查落後敵人的間隔秒數；預設 0.5。")]
        private float cleanupCheckInterval = 0.5f;
        [SerializeField, Tooltip("輸出區域啟動、拒絕與清理原因；大量敵人時建議關閉。")]
        private bool debugEncounter;

        private sealed class ZoneRuntime
        {
            public EnemySpawnZone Zone;
            public EnemySpawnApproachGate[] Gates;
            public readonly List<CandidateRuntime> Candidates = new List<CandidateRuntime>();
        }

        private sealed class CandidateRuntime
        {
            public NetworkPrefabRef Prefab;
            public string PrefabName;
            public EnemyLocomotionKind Kind;
            public EnemySpawnEntry Entry;
            public EnemyPatrolArea PatrolArea;
            public List<EnemyFlyingPatrolNavigator> FlyingNavigators;
            public float FlyingMinimumTravel;
        }

        private sealed class OwnedEnemy
        {
            public NetworkObject Object;
            public EnemySpawnZone Zone;
            public float FarSince = -1f;
        }

        private struct PlayerTrack
        {
            public NetworkObject Object;
            public Vector3 Position;
        }

        private struct PlayerSnapshot
        {
            public PlayerRef Ref;
            public Vector3 Position;
            public Vector3 PreviousPosition;
            public bool HasPrevious;
        }

        private readonly List<ZoneRuntime> zones = new List<ZoneRuntime>();
        private readonly List<EnemyPatrolArea> generatedFlyingAreas = new List<EnemyPatrolArea>();
        private readonly List<OwnedEnemy> ownedEnemies = new List<OwnedEnemy>();
        private readonly Dictionary<PlayerRef, PlayerTrack> previousPlayers = new Dictionary<PlayerRef, PlayerTrack>();
        private readonly List<PlayerRef> stalePlayers = new List<PlayerRef>();
        private readonly List<PlayerSnapshot> players = new List<PlayerSnapshot>();
        private readonly Collider[] occupancyBuffer = new Collider[32];
        private RecentZoneLock<EnemySpawnZone> recentZones;
        private MapRuntimeNavigationPrototype navigation;
        private ZoneRuntime pendingZone;
        private EnemySpawnApproachGate pendingGate;
        private Vector3 pendingForward;
        private PlayerRef pendingPlayer;
        private readonly List<SpawnBatch> pendingBatches = new List<SpawnBatch>();
        private int pendingBatchIndex;
        private bool pendingSucceeded;

        private sealed class SpawnBatch
        {
            public CandidateRuntime Candidate;
            public int Remaining;
        }
        private float pendingExpiresAt;
        private float nextSpawnTime;
        private float nextCleanupTime;
        private bool zonesBuilt;
        private bool configurationErrorLogged;
        private bool maskErrorLogged;

        public override void Spawned()
        {
            if (!Object.HasStateAuthority) return;
            if (stageFlow == null) stageFlow = GetComponent<StageFlowController>();
            if (mapState == null) mapState = GetComponent<NetworkMapState>();
            ClearGeneratedFlyingAreas();
            recentZones = new RecentZoneLock<EnemySpawnZone>(recentLockedZoneCount);
            zones.Clear();
            ownedEnemies.Clear();
            previousPlayers.Clear();
            CancelPendingActivation();
            zonesBuilt = false;
        }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority ||
                Runner == null || !Runner.IsServer)
                return;

            if (stageFlow == null || mapState == null || mapSelection == null ||
                stageFlow.Runner != Runner || mapState.Runner != Runner ||
                mapSelection.gameObject.scene != gameObject.scene)
            {
                if (!configurationErrorLogged)
                {
                    Debug.LogError("[Enemy Encounter] 同 Runner 的 StageFlow、NetworkMapState 與 MapRunSelection 引用未完成。", this);
                    configurationErrorLogged = true;
                }
                return;
            }

            if (stageFlow.Phase != StagePhase.Active || stageFlow.IsBossStage)
            {
                CancelPendingActivation();
                previousPlayers.Clear();
                return;
            }

            if (!zonesBuilt && !TryBuildZones()) return;
            if (worldOcclusionMask.value == 0 || spawnOccupancyMask.value == 0)
            {
                if (!maskErrorLogged)
                {
                    Debug.LogError("[Enemy Encounter] World Occlusion Mask 與 Spawn Occupancy Mask 都必須設定；目前拒絕生成。", this);
                    maskErrorLogged = true;
                }
                return;
            }
            maskErrorLogged = false;
            CollectPlayers();
            CleanupInvalidOwned();
            if (Time.time >= nextCleanupTime)
            {
                nextCleanupTime = Time.time + cleanupCheckInterval;
                CleanupLeftBehindEnemies();
            }

            if (players.Count == 0 || Time.time < nextSpawnTime) return;
            if (pendingZone != null)
            {
                ContinuePendingActivation();
                return;
            }

            TryActivateCrossedZone();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ClearGeneratedFlyingAreas();
            zones.Clear();
            ownedEnemies.Clear();
            previousPlayers.Clear();
            players.Clear();
            CancelPendingActivation();
            zonesBuilt = false;
        }

        private bool TryBuildZones()
        {
            if (!mapState.IsReady || mapSelection.Alignment == null) return false;
            navigation = mapSelection.Alignment.RuntimeNavigation;
            // 先等地面資料，避免飛行候選在每個等待 Tick 反覆規劃／銷毀。
            if ((navigation == null || !navigation.IsReady ||
                navigation.GeneratedGroundAreas.Count != mapState.Chunks.Count) && HasActiveGroundCandidates())
                return false;
            ClearGeneratedFlyingAreas();
            zones.Clear();
            for (int chunkIndex = 0; chunkIndex < mapState.Chunks.Count; chunkIndex++)
            {
                MapChunk chunk = mapState.Chunks[chunkIndex];
                if (chunk == null) continue;
                foreach (EnemySpawnZone zone in chunk.GetComponentsInChildren<EnemySpawnZone>(true))
                {
                    if (zone == null || !zone.isActiveAndEnabled ||
                        zone.GetComponentInParent<MapChunk>() != chunk)
                        continue;

                    EnemySpawnApproachGate[] gates = zone.GetGates();
                    if (gates.Length == 0)
                    {
                        Debug.LogError($"[Enemy Encounter] {zone.name} 缺少啟動 Gate，無法登記。", zone);
                        continue;
                    }
                    var runtime = new ZoneRuntime { Zone = zone, Gates = gates };
                    if (!TryBuildCandidates(runtime, chunk, chunkIndex)) return false;
                    if (runtime.Candidates.Count > 0 || zone.UsesEnemyEntries) zones.Add(runtime);
                }
            }
            zonesBuilt = true;
            if (zones.Count == 0)
                Debug.LogWarning("[Enemy Encounter] 地圖已就緒，但沒有任何有效的怪物生成區；請檢查 MapChunk Prefab 與 Gate／Patrol Area 設定。", this);
            if (debugEncounter) Debug.Log($"[Enemy Encounter] 登記 {zones.Count} 個怪物生成區。", this);
            return true;
        }

        private bool TryResolveFlyingArea(EnemySpawnZone zone, NetworkPrefabRef candidatePrefab, out EnemyPatrolArea area,
            out List<EnemyFlyingPatrolNavigator> navigators, out float minimumTravel)
        {
            area = zone.FlyingPatrolArea;
            navigators = new List<EnemyFlyingPatrolNavigator>();
            minimumTravel = 0f;
            // 每種 Prefab 使用自己的實際膠囊；大型敵人不會限制同區小型敵人的巡邏範圍。
            try
            {
                foreach (NetworkPrefabRef prefab in new[] { candidatePrefab })
                {
                    if (!prefab.IsValid) continue;
                    NetworkPrefabId id = Runner.Prefabs.GetId((NetworkObjectGuid)prefab);
                    NetworkObject asset = id.IsValid ? Runner.Prefabs.Load(id, true) : null;
                    EnemyActor actor = asset != null ? asset.GetComponent<EnemyActor>() : null;
                    EnemyFlyingPatrolNavigator navigator = asset != null
                        ? asset.GetComponent<EnemyFlyingPatrolNavigator>() : null;
                    EnemyIdlePatrolBrain brain = asset != null ? asset.GetComponent<EnemyIdlePatrolBrain>() : null;
                    if (actor == null || actor.Definition == null ||
                        actor.Definition.LocomotionKind != EnemyLocomotionKind.FreeFlying ||
                        navigator == null || brain == null)
                        throw new InvalidOperationException("候選必須是已登記且具備 Flying Navigator／Patrol Brain 的 Free Flying Prefab。");
                    if (!navigators.Contains(navigator)) navigators.Add(navigator);
                    minimumTravel = Mathf.Max(minimumTravel, brain.PatrolArrivalDistance);
                }
                if (navigators.Count == 0)
                    throw new InvalidOperationException("沒有有效的飛行 Enemy Prefab。");
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                Debug.LogError($"[Enemy Encounter] {zone.name} 飛行巡邏設定失敗：{exception.Message}", zone);
                return false;
            }

            if (area != null) return true; // 保留既有手動引用，不覆寫、不銷毀。
            PhysicsScene physics = Runner.GetPhysicsScene();
            List<EnemyFlyingPatrolNavigator> profiles = navigators;
            Bounds spawnBounds = zone.SpawnLocalBounds;
            spawnBounds.center += zone.transform.InverseTransformVector(FlyingSpawnRootOffset(profiles));
            List<Vector3> points = EnemyFlyingPatrolPlanner.Plan(
                zone.transform.localToWorldMatrix, zone.FlyingPatrolLocalBounds, spawnBounds,
                zone.FlyingPatrolSamplesPerAxis, zone.FlyingPatrolMaximumPoints,
                Mathf.Max(zone.FlyingPatrolMinimumSpacing, minimumTravel * 2f + 0.01f),
                (from, to) => IsFlyingPassageClear(profiles, physics, from, to));
            if (points.Count < 2)
            {
                Debug.LogWarning($"[Enemy Encounter] {zone.name} 無法自動建立飛行巡邏區：" +
                    "找不到至少 2 個可從出生盒連通的空中節點。請檢查出生盒與巡邏盒重疊、取樣密度、" +
                    "Enemy Prefab 的膠囊尺寸及 Obstacle Mask。此飛行候選停用，其他候選與區域照常。", zone);
                return false;
            }

            var root = new GameObject($"RuntimeFlyingPatrolArea_{generatedFlyingAreas.Count}");
            root.transform.SetParent(zone.transform, false);
            var pointRoot = new GameObject("PatrolPoints");
            pointRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < points.Count; i++)
            {
                var point = new GameObject($"Point_{i:00}");
                point.transform.SetParent(pointRoot.transform, false);
                point.transform.position = points[i];
            }
            area = root.AddComponent<EnemyPatrolArea>();
            area.ConfigureRuntime($"flying_runtime_{zone.GetInstanceID()}_{generatedFlyingAreas.Count}", pointRoot.transform, 0f);
            generatedFlyingAreas.Add(area);
            if (debugEncounter)
                Debug.Log($"[Enemy Encounter] {zone.name} 自動建立 {points.Count} 個飛行巡邏點。", zone);
            return true;
        }

        private static Vector3 FlyingSpawnRootOffset(List<EnemyFlyingPatrolNavigator> navigators)
        {
            float lift = 0f;
            if (navigators != null)
                foreach (var navigator in navigators)
                    if (navigator != null) lift = Mathf.Max(lift, navigator.SpawnRootOffset.y);
            return Vector3.up * lift;
        }

        private static bool IsFlyingPassageClear(List<EnemyFlyingPatrolNavigator> navigators,
            PhysicsScene physics, Vector3 from, Vector3 to)
        {
            if (navigators == null || navigators.Count == 0) return false;
            foreach (EnemyFlyingPatrolNavigator navigator in navigators)
                if (navigator == null || !navigator.IsPassageClear(physics, from, to)) return false;
            return true;
        }

        private bool HasFlyingPatrolPath(ZoneRuntime zone, CandidateRuntime candidate, Vector3 position)
        {
            if (zone.Zone.UsesAutomaticFlyingPatrol &&
                !zone.Zone.FlyingPatrolLocalBounds.Contains(zone.Zone.transform.InverseTransformPoint(position)))
                return false;
            PhysicsScene physics = Runner.GetPhysicsScene();
            for (int i = 0; i < candidate.PatrolArea.PointCount; i++)
                if (candidate.PatrolArea.TryGetPoint(i, position, out Vector3 point) &&
                    Vector3.Distance(position, point) > candidate.FlyingMinimumTravel &&
                    IsFlyingPassageClear(candidate.FlyingNavigators, physics, position, point))
                    return true;
            return false;
        }

        private void ClearGeneratedFlyingAreas()
        {
            foreach (EnemyPatrolArea area in generatedFlyingAreas)
            {
                if (area == null) continue;
                area.gameObject.SetActive(false);
                Destroy(area.gameObject);
            }
            generatedFlyingAreas.Clear();
        }
        private void CollectPlayers()
        {
            players.Clear();
            stalePlayers.Clear();
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetPlayerObject(player, out NetworkObject obj) ||
                    obj == null || !obj.IsValid)
                    continue;
                PlayerHealth health = obj.GetComponent<PlayerHealth>();
                if (health == null || !health.IsAlive) continue;

                Vector3 position = obj.transform.position;
                bool hasPrevious = previousPlayers.TryGetValue(player, out PlayerTrack old) &&
                                   old.Object == obj;
                players.Add(new PlayerSnapshot
                {
                    Ref = player, Position = position,
                    PreviousPosition = old.Position, HasPrevious = hasPrevious
                });
                previousPlayers[player] = new PlayerTrack { Object = obj, Position = position };
            }

            foreach (PlayerRef player in previousPlayers.Keys)
            {
                bool found = false;
                for (int i = 0; i < players.Count; i++)
                    if (players[i].Ref == player) { found = true; break; }
                if (!found) stalePlayers.Add(player);
            }
            for (int i = 0; i < stalePlayers.Count; i++) previousPlayers.Remove(stalePlayers[i]);
        }

        private void TryActivateCrossedZone()
        {
            ZoneRuntime chosen = null;
            EnemySpawnApproachGate chosenGate = null;
            Vector3 chosenForward = Vector3.zero;
            PlayerRef chosenPlayer = PlayerRef.None;
            float bestDistance = float.PositiveInfinity;

            foreach (ZoneRuntime zone in zones)
            {
                if (!zone.Zone.isActiveAndEnabled || recentZones.IsLocked(zone.Zone)) continue;
                foreach (EnemySpawnApproachGate gate in zone.Gates)
                {
                    if (gate == null || !gate.isActiveAndEnabled) continue;
                    foreach (PlayerSnapshot player in players)
                    {
                        if (!player.HasPrevious ||
                            Vector3.Distance(player.PreviousPosition, player.Position) > maximumSweepDistance ||
                            !gate.TryCross(player.PreviousPosition, player.Position, out _, out Vector3 approachForward))
                            continue;

                        float distance = Vector3.Distance(player.Position, zone.Zone.transform.position);
                        if (distance >= bestDistance) continue;
                        chosen = zone;
                        chosenGate = gate;
                        chosenForward = approachForward;
                        chosenPlayer = player.Ref;
                        bestDistance = distance;
                    }
                }
            }

            if (chosen == null) return;
            BuildActivationBatches(chosen);
            if (pendingBatches.Count == 0) return; // 全部未抽中，不鎖區、不留下補發。
            pendingZone = chosen;
            pendingGate = chosenGate;
            pendingForward = chosenForward;
            pendingPlayer = chosenPlayer;
            pendingBatchIndex = 0;
            pendingSucceeded = false;
            pendingExpiresAt = Time.time + activationLifetimeSeconds;
            ContinuePendingActivation();
        }

        private void ContinuePendingActivation()
        {
            if (pendingZone == null || Time.time > pendingExpiresAt ||
                !TryGetPlayer(pendingPlayer, out _))
            {
                CancelPendingActivation();
                return;
            }

            while (pendingBatchIndex < pendingBatches.Count)
            {
                SpawnBatch batch = pendingBatches[pendingBatchIndex];
                if (!TrySpawnOne(pendingZone, batch.Candidate, pendingGate, pendingPlayer, pendingForward))
                {
                    // 此種出生失敗便取消其剩餘數量，其他已抽中的種類仍可嘗試。
                    pendingBatchIndex++;
                    continue;
                }
                if (!pendingSucceeded)
                {
                    EnemySpawnZone released = recentZones.MarkSuccessful(pendingZone.Zone);
                    if (debugEncounter)
                        Debug.Log($"[Enemy Encounter] {pendingZone.Zone.name} 成功啟動；解鎖 {(released != null ? released.name : "無")}。", pendingZone.Zone);
                    pendingSucceeded = true;
                }
                if (--batch.Remaining <= 0) pendingBatchIndex++;
                if (pendingBatchIndex >= pendingBatches.Count) CancelPendingActivation();
                return; // 全場間隔仍限制每次只出生一隻。
            }
            CancelPendingActivation();
        }

        private void CancelPendingActivation()
        {
            pendingZone = null;
            pendingBatches.Clear();
            pendingBatchIndex = 0;
            pendingSucceeded = false;
        }

        private bool TrySpawnOne(ZoneRuntime zone, CandidateRuntime candidate, EnemySpawnApproachGate gate, PlayerRef triggerPlayer, Vector3 approachForward)
        {
            if (zone == null || gate == null || !zone.Zone.isActiveAndEnabled)
                return false;
            if (CountAllAliveEnemies() >= maximumAliveEnemies ||
                CountAliveInZone(zone.Zone) >= maximumAlivePerZone ||
                ownedEnemies.Count >= maximumOwnedNetworkObjects)
            {
                LogSkip(zone.Zone, $"{candidate.PrefabName} [{candidate.Kind}] 全場／單區存活數或 NetworkObject 預算已滿");
                return false;
            }
            if (!TryGetPlayer(triggerPlayer, out PlayerSnapshot player))
            {
                LogSkip(zone.Zone, "觸發玩家已離開或死亡");
                return false;
            }
            if (!TrySelectSpawnPosition(zone, candidate, approachForward, player, out Vector3 position))
                return false;

            bool setupValid = true;
            NetworkObject spawned = Runner.Spawn(
                candidate.Prefab, position, zone.Zone.transform.rotation, PlayerRef.None,
                (spawnRunner, spawnedObject) =>
                {
                    EnemyActor actor = spawnedObject.GetComponent<EnemyActor>();
                    if (actor == null || actor.Definition == null ||
                        actor.Definition.LocomotionKind != candidate.Kind ||
                        !actor.TryInitializePatrolBeforeSpawn(candidate.PatrolArea))
                    {
                        setupValid = false;
                        Debug.LogError("[Enemy Encounter] Prefab 移動類型或出生前巡邏契約無效；Stationary 必須移除巡邏／追逐 Brain 與 Navigator。", spawnedObject);
                    }
                });

            if (spawned == null) return false;
            if (!setupValid)
            {
                Runner.Despawn(spawned);
                return false;
            }

            ownedEnemies.Add(new OwnedEnemy { Object = spawned, Zone = zone.Zone });
            if (debugEncounter)
                Debug.Log($"[Enemy Encounter] {zone.Zone.name} 生成 {candidate.PrefabName} [{candidate.Kind}]，位置 {position}。", zone.Zone);
            nextSpawnTime = Time.time + minimumSecondsBetweenSpawns;
            return true;
        }

        private void LogSkip(EnemySpawnZone zone, string reason)
        {
            if (debugEncounter)
                Debug.Log($"[Enemy Encounter] {zone.name} 跳過：{reason}。", zone);
        }

        private bool TrySelectSpawnPosition(
            ZoneRuntime zone,
            CandidateRuntime profile,
            Vector3 approachForward,
            PlayerSnapshot player,
            out Vector3 position)
        {
            position = default;
            // 每個候選只記錄第一個拒絕原因；維持原有檢查順序及短路行為。
            int navRejected = 0, supportRejected = 0, boundsRejected = 0, patrolRejected = 0;
            int distanceRejected = 0, forwardRejected = 0, occupiedRejected = 0, visibilityRejected = 0;
            for (int attempt = 0; attempt < candidateAttempts; attempt++)
            {
                Vector3 candidate = zone.Zone.RandomPoint(profile.Kind);
                if (profile.Kind == EnemyLocomotionKind.FreeFlying)
                    candidate += FlyingSpawnRootOffset(profile.FlyingNavigators);
                if (profile.Kind == EnemyLocomotionKind.Ground)
                {
                    if (!navigation.TrySampleGroundPosition(candidate, groundSampleDistance, out candidate))
                    { navRejected++; continue; }
                    if (!EnemyEncounterRules.TryProjectGroundSpawn(
                        Runner.GetPhysicsScene(), candidate, worldOcclusionMask,
                        GroundSpawnMaximumSupportOffset, out candidate))
                    { supportRejected++; continue; }
                    if (!zone.Zone.Contains(candidate))
                    { boundsRejected++; continue; }
                    if (!HasGroundPatrolPath(candidate, profile.PatrolArea))
                    { patrolRejected++; continue; }
                }
                else if (profile.Kind == EnemyLocomotionKind.FreeFlying && !HasFlyingPatrolPath(zone, profile, candidate))
                { patrolRejected++; continue; }

                if (Vector3.Distance(player.Position, candidate) > maximumSpawnDistanceFromTrigger)
                { distanceRejected++; continue; }
                if (Vector3.Dot(candidate - player.Position, approachForward) < minimumForwardDistance)
                { forwardRejected++; continue; }
                if (IsOccupied(candidate))
                { occupiedRejected++; continue; }
                if (!PassesPlayerVisibilityChecks(Runner.GetPhysicsScene(), candidate, zone.Zone.RequireSpawnOcclusion))
                { visibilityRejected++; continue; }

                position = candidate;
                return true;
            }
            if (debugEncounter)
                LogSkip(zone.Zone, $"{profile.PrefabName} [{profile.Kind}] 出生取樣 {candidateAttempts} 次全部失敗；" +
                    $"NavMesh={navRejected}、貼地={supportRejected}、盒外={boundsRejected}、巡邏路徑={patrolRejected}、" +
                    $"超出最遠距離={distanceRejected}、不在前方={forwardRejected}、占用={occupiedRejected}、" +
                    $"玩家過近／遮擋={visibilityRejected}；遮擋要求={zone.Zone.RequireSpawnOcclusion}；" +
                    $"玩家={player.Position}、穿越方向={approachForward}");
            return false;
        }

        private bool HasGroundPatrolPath(Vector3 position, EnemyPatrolArea area)
        {
            int checkedPaths = 0;
            for (int i = 0; i < area.PointCount; i++)
            {
                if (area.QueryPoint(i, position, out Vector3 point, out _, out _, out _) !=
                    EnemyPatrolPointQueryResult.Valid)
                    continue;
                if (navigation.TryResolveCompleteGroundPath(position, point, groundSampleDistance, out _))
                    return true;
                if (++checkedPaths >= 3) break;
            }
            return false;
        }

        private bool IsOccupied(Vector3 position) =>
            Runner.GetPhysicsScene().OverlapSphere(
                position + Vector3.up * spawnOccupancyCenterHeight,
                spawnOccupancyRadius, occupancyBuffer, spawnOccupancyMask,
                QueryTriggerInteraction.Ignore) > 0;

        // 清理永遠要求遮擋，不受任何 Zone 的測試開關影響。
        private bool IsConcealedFromAllPlayers(Vector3 position) =>
            PassesPlayerVisibilityChecks(Runner.GetPhysicsScene(), position, true);

        private bool PassesPlayerVisibilityChecks(
            PhysicsScene physicsScene, Vector3 position, bool requireOcclusion)
        {
            if (players.Count == 0 || (requireOcclusion && worldOcclusionMask.value == 0)) return false;
            foreach (PlayerSnapshot player in players)
            {
                Vector3 eye = player.Position + Vector3.up * 1.5f;
                if (Vector3.Distance(player.Position, position) < minimumPlayerDistance)
                    return false;
                // 關閉視線限制也不能略過任一玩家的最小出生距離。
                if (!requireOcclusion) continue;
                for (int sample = 0; sample < 3; sample++)
                {
                    Vector3 target = position + Vector3.up * (0.3f + sample * 0.7f);
                    Vector3 ray = target - eye;
                    float distance = ray.magnitude;
                    if (distance < 0.01f ||
                        !physicsScene.Raycast(eye, ray / distance, out _,
                            distance - 0.01f, worldOcclusionMask, QueryTriggerInteraction.Ignore))
                        return false;
                }
            }
            return true;
        }

        private bool TryGetPlayer(PlayerRef player, out PlayerSnapshot result)
        {
            foreach (PlayerSnapshot snapshot in players)
            {
                if (snapshot.Ref != player) continue;
                result = snapshot;
                return true;
            }
            result = default;
            return false;
        }

        private int CountAllAliveEnemies()
        {
            int count = 0;
            foreach (EnemyActor actor in FindObjectsOfType<EnemyActor>())
                if (actor != null && actor.Runner == Runner && actor.IsAlive) count++;
            return count;
        }

        private int CountAliveInZone(EnemySpawnZone zone)
        {
            int count = 0;
            foreach (OwnedEnemy owned in ownedEnemies)
            {
                if (owned.Zone != zone || owned.Object == null || !owned.Object.IsValid) continue;
                EnemyActor actor = owned.Object.GetComponent<EnemyActor>();
                if (actor != null && actor.IsAlive) count++;
            }
            return count;
        }

        private void CleanupInvalidOwned()
        {
            for (int i = ownedEnemies.Count - 1; i >= 0; i--)
                if (ownedEnemies[i].Object == null || !ownedEnemies[i].Object.IsValid)
                    ownedEnemies.RemoveAt(i);
        }

        private void CleanupLeftBehindEnemies()
        {
            if (players.Count == 0) return;
            for (int i = ownedEnemies.Count - 1; i >= 0; i--)
            {
                OwnedEnemy owned = ownedEnemies[i];
                if (owned.Object == null || !owned.Object.IsValid) continue;
                EnemyActor actor = owned.Object.GetComponent<EnemyActor>();
                if (actor == null || !actor.IsAlive) continue;
                EnemyPerceptionController perception = owned.Object.GetComponent<EnemyPerceptionController>();
                EnemyCombatDecisionController combat = owned.Object.GetComponent<EnemyCombatDecisionController>();
                EnemyStateController state = actor.StateController;
                bool farFromAll = true;
                foreach (PlayerSnapshot player in players)
                    if (Vector3.Distance(player.Position, owned.Object.transform.position) <= cleanupDistance)
                    { farFromAll = false; break; }

                bool canClean = farFromAll &&
                    (perception == null || !perception.HasTarget) &&
                    (combat == null || combat.CurrentActionPhase == EnemyCombatActionPhase.None) &&
                    (state == null ||
                     (state.CurrentControlState == EnemyControlState.Normal &&
                      state.CurrentActionState == EnemyActionState.None)) &&
                    IsConcealedFromAllPlayers(owned.Object.transform.position);
                if (!canClean)
                {
                    owned.FarSince = -1f;
                    continue;
                }

                if (owned.FarSince < 0f)
                {
                    owned.FarSince = Time.time;
                    continue;
                }
                if (Time.time - owned.FarSince < cleanupDelaySeconds) continue;

                if (debugEncounter) Debug.Log($"[Enemy Encounter] 清理落後敵人 {owned.Object.name}；不經過死亡獎勵。", this);
                Runner.Despawn(owned.Object);
                ownedEnemies.RemoveAt(i);
            }
        }

        private void OnValidate()
        {
            maximumAliveEnemies = Mathf.Max(1, maximumAliveEnemies);
            maximumAlivePerZone = Mathf.Max(1, maximumAlivePerZone);
            maximumOwnedNetworkObjects = Mathf.Max(1, maximumOwnedNetworkObjects);
            minimumSecondsBetweenSpawns = Mathf.Max(0.02f, minimumSecondsBetweenSpawns);
            recentLockedZoneCount = Mathf.Max(1, recentLockedZoneCount);
            maximumSweepDistance = Mathf.Max(0.1f, maximumSweepDistance);
            activationLifetimeSeconds = Mathf.Max(0.1f, activationLifetimeSeconds);
            maximumSpawnDistanceFromTrigger = Mathf.Max(0.1f, maximumSpawnDistanceFromTrigger);
            minimumForwardDistance = Mathf.Max(0f, minimumForwardDistance);
            candidateAttempts = Mathf.Max(1, candidateAttempts);
            groundSampleDistance = Mathf.Max(0.01f, groundSampleDistance);
            minimumPlayerDistance = Mathf.Max(0.1f, minimumPlayerDistance);
            spawnOccupancyRadius = Mathf.Max(0.01f, spawnOccupancyRadius);
            spawnOccupancyCenterHeight = Mathf.Max(0f, spawnOccupancyCenterHeight);
            cleanupDistance = Mathf.Max(1f, cleanupDistance);
            cleanupDelaySeconds = Mathf.Max(0.1f, cleanupDelaySeconds);
            cleanupCheckInterval = Mathf.Max(0.05f, cleanupCheckInterval);
        }
    }
}
