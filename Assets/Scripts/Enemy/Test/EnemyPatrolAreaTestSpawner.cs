using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 測試用 Enemy 網路生成器。
///
/// 本地玩家按下測試熱鍵後，由指定 Scene NetworkObject 的 State Authority
/// 正式呼叫 Runner.Spawn；Client 不會自行 Instantiate Enemy。
///
/// 生成位置只取自指定 EnemyPatrolArea 的人工巡邏節點，
/// 不會在球形範圍內隨機猜座標，也不會在設定錯誤時退回世界原點。
///
/// 這是開發期測試工具。正式關卡波次、房間清除與生成預算
/// 應另外建立正式 Encounter／Wave 系統，不要讓它們依賴鍵盤熱鍵。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyPatrolAreaTestSpawner : NetworkBehaviour
{
    public enum PrefabSelectionMode : byte
    {
        Random = 0,
        RoundRobin = 1
    }

    public enum SpawnRotationMode : byte
    {
        PatrolAreaForward = 0,
        RandomYaw = 1
    }

    [Header("測試熱鍵")]

    [SerializeField]
    [Tooltip(
        "是否允許這個測試生成器讀取本機熱鍵。\n" +
        "正式 Build 若不需要測試生成，請關閉此欄或移除整個測試物件。")]
    private bool enableSpawnHotkey = true;

    [SerializeField]
    [Tooltip("本機按下後要求生成一隻 Enemy。預設為 F4，可在 Inspector 改成其他 KeyCode。")]
    private KeyCode spawnKey = KeyCode.F4;

    [SerializeField]
    [Tooltip(
        "開啟後，沒有 State Authority 的 Client 也能按熱鍵，透過 Reliable RPC 要求 State Authority 生成。\n" +
        "這只適合多人測試；正式發布前應關閉，避免客戶端任意要求生成敵人。")]
    private bool allowClientSpawnRequests = true;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "State Authority 接受兩次生成要求之間的最短秒數，使用 Unscaled Time。\n" +
        "這是全域防連點，不是每位玩家各自計時；0 代表不限制。")]
    private float minimumSecondsBetweenRequests = 0.15f;

    [Header("生成區域與 Prefab")]

    [SerializeField]
    [Tooltip(
        "Enemy 生成位置來源。只會使用此 EnemyPatrolArea 的人工節點。\n\n" +
        "注意：Enemy Prefab 內 EnemyIdlePatrolBrain 的 Patrol Area Id，" +
        "仍必須與此物件的 Area Id 相同，生成後才會在同一區域巡邏。")]
    private EnemyPatrolArea patrolArea;

    [SerializeField]
    [Tooltip(
        "可生成的 Enemy Network Prefab 清單。\n" +
        "每個 Prefab 必須已登記到 Fusion NetworkProjectConfig，並包含 NetworkObject 與完整 Enemy 元件。")]
    private NetworkPrefabRef[] enemyPrefabs =
        Array.Empty<NetworkPrefabRef>();

    [SerializeField]
    [Tooltip(
        "Random：每次從所有有效 Prefab 隨機選一隻。\n" +
        "Round Robin：依 Inspector 陣列順序循環生成，適合逐一測試所有敵人。")]
    private PrefabSelectionMode prefabSelection =
        PrefabSelectionMode.Random;

    [SerializeField]
    [Tooltip(
        "Patrol Area Forward：使用 Patrol Area 的水平朝向。\n" +
        "Random Yaw：每次只隨機世界 Y 軸角度，不傾斜 Enemy Root。")]
    private SpawnRotationMode spawnRotation =
        SpawnRotationMode.RandomYaw;

    [SerializeField]
    [Tooltip(
        "加入巡邏節點世界座標的生成偏移。\n" +
        "地面 Enemy 通常維持 (0,0,0)；如果 Prefab Root Pivot 不在腳底才調整 Y。")]
    private Vector3 spawnPositionOffset = Vector3.zero;

    [Header("生成點占用檢查")]

    [SerializeField]
    [Tooltip(
        "生成前用 OverlapSphere 檢查哪些 Layer 會占用節點。\n" +
        "建議勾 Enemy 與 Player，不要勾地板 World，否則球體可能永遠判定節點被占用。\n" +
        "Mask 為空時略過占用檢查。")]
    private LayerMask spawnOccupancyMask;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("生成點占用球半徑，單位為公尺。建議略大於 Enemy Body Radius，例如 0.75。")]
    private float spawnOccupancyRadius = 0.75f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "占用球中心相對生成 Root 向上的高度，單位為公尺。\n" +
        "Root 在腳底、Enemy 高約 1.8 公尺時建議 0.9。")]
    private float spawnOccupancyCenterHeight = 0.9f;

    [Header("測試數量保護")]

    [SerializeField]
    [Min(0)]
    [Tooltip(
        "這個生成器同時允許存活／尚未 Despawn 的最大生成數。\n" +
        "只統計由本元件生成的 NetworkObject；0 代表不限制。建議測試時保留 20，避免誤按產生過多敵人。")]
    private int maximumAliveFromThisSpawner = 20;

    [Header("除錯與 Gizmos")]

    [SerializeField]
    [Tooltip("輸出按鍵來源、Prefab、巡邏節點 Index、生成座標及拒絕原因。")]
    private bool debugTestSpawner = true;

    [SerializeField]
    [Tooltip("選取生成器時，在 Patrol Area 可用節點顯示生成占用球。")]
    private bool drawSpawnPointGizmos = true;

    [SerializeField]
    [Tooltip("可供生成查詢使用的巡邏節點 Gizmo 顏色。")]
    private Color spawnPointGizmoColor =
        new Color(0.25f, 1f, 0.35f, 0.65f);

    [SerializeField]
    [Tooltip("被 Patrol Area 最大距離規則排除的節點 Gizmo 顏色。")]
    private Color rejectedPointGizmoColor =
        new Color(1f, 0.2f, 0.1f, 0.7f);

    private readonly Collider[] occupancyBuffer =
        new Collider[32];

    private readonly List<NetworkObject> spawnedEnemies =
        new List<NetworkObject>();

    private int nextPrefabIndex;
    private float nextAllowedRequestTime;
    private bool fusionSpawned;

    private void Update()
    {
        if (!enableSpawnHotkey ||
            !fusionSpawned ||
            Object == null ||
            !Object.IsValid ||
            !Input.GetKeyDown(spawnKey))
        {
            return;
        }

        if (Object.HasStateAuthority)
        {
            TrySpawnOne(Runner.LocalPlayer);
            return;
        }

        if (!allowClientSpawnRequests)
        {
            if (debugTestSpawner)
            {
                Debug.LogWarning(
                    "[Enemy Test Spawner] 本機不是 State Authority，且 Client Spawn Request 已關閉。",
                    this
                );
            }

            return;
        }

        RPC_RequestSpawnEnemy();
    }

    public override void Spawned()
    {
        fusionSpawned = true;

        if (Object.HasStateAuthority)
        {
            nextPrefabIndex = 0;
            nextAllowedRequestTime = 0f;
            spawnedEnemies.Clear();
        }
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        fusionSpawned = false;
        spawnedEnemies.Clear();
    }

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable,
        TickAligned = false
    )]
    private void RPC_RequestSpawnEnemy(
        RpcInfo info = default
    )
    {
        if (!Object.HasStateAuthority ||
            !allowClientSpawnRequests)
        {
            return;
        }

        TrySpawnOne(info.Source);
    }

    /// <summary>
    /// 所有真正 Spawn 都只會進入此 State Authority 路徑。
    /// 任一設定或節點檢查失敗都會中止，不使用 Instantiate 或世界原點備援。
    /// </summary>
    private void TrySpawnOne(PlayerRef requestedBy)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        if (minimumSecondsBetweenRequests > 0f &&
            Time.unscaledTime < nextAllowedRequestTime)
        {
            return;
        }

        nextAllowedRequestTime =
            Time.unscaledTime +
            minimumSecondsBetweenRequests;

        RemoveInvalidSpawnedReferences();

        if (maximumAliveFromThisSpawner > 0 &&
            spawnedEnemies.Count >= maximumAliveFromThisSpawner)
        {
            Debug.LogWarning(
                "[Enemy Test Spawner] 已達此生成器的數量上限。" +
                $"\nCurrent={spawnedEnemies.Count}" +
                $"\nMaximum={maximumAliveFromThisSpawner}",
                this
            );
            return;
        }

        if (!TrySelectEnemyPrefab(
                out NetworkPrefabRef selectedPrefab,
                out int prefabIndex
            ))
        {
            Debug.LogError(
                "[Enemy Test Spawner] Enemy Prefabs 沒有任何有效的 NetworkPrefabRef。",
                this
            );
            return;
        }

        if (!TrySelectSpawnPoint(
                out Vector3 spawnPosition,
                out int pointIndex,
                out string failureDetail
            ))
        {
            Debug.LogWarning(
                "[Enemy Test Spawner] 找不到可使用的 Patrol Area 生成節點。" +
                $"\nArea={(patrolArea != null ? patrolArea.AreaId : "null")}" +
                $"\nDetail={failureDetail}",
                this
            );
            return;
        }

        Quaternion rotation = ResolveSpawnRotation();

        NetworkObject spawned = Runner.Spawn(
            selectedPrefab,
            spawnPosition,
            rotation,
            PlayerRef.None
        );

        if (spawned == null)
        {
            Debug.LogError(
                "[Enemy Test Spawner] Runner.Spawn 沒有回傳 NetworkObject。" +
                $"\nPrefab Index={prefabIndex}" +
                $"\nPosition={spawnPosition:F3}",
                this
            );
            return;
        }

        spawnedEnemies.Add(spawned);

        if (debugTestSpawner)
        {
            Debug.Log(
                "[Enemy Test Spawner] 生成成功。" +
                $"\nRequested By={requestedBy}" +
                $"\nPrefab Index={prefabIndex}" +
                $"\nArea={patrolArea.AreaId}" +
                $"\nPoint Index={pointIndex}" +
                $"\nPosition={spawnPosition:F3}" +
                $"\nAlive From This Spawner={spawnedEnemies.Count}",
                spawned
            );
        }
    }

    private bool TrySelectEnemyPrefab(
        out NetworkPrefabRef selectedPrefab,
        out int selectedIndex
    )
    {
        selectedPrefab = default;
        selectedIndex = -1;

        if (enemyPrefabs == null ||
            enemyPrefabs.Length == 0)
        {
            return false;
        }

        int startIndex =
            prefabSelection == PrefabSelectionMode.Random
                ? UnityEngine.Random.Range(0, enemyPrefabs.Length)
                : Mathf.Abs(nextPrefabIndex) % enemyPrefabs.Length;

        for (int offset = 0;
             offset < enemyPrefabs.Length;
             offset++)
        {
            int index =
                (startIndex + offset) %
                enemyPrefabs.Length;

            if (!enemyPrefabs[index].IsValid)
            {
                continue;
            }

            selectedPrefab = enemyPrefabs[index];
            selectedIndex = index;

            if (prefabSelection == PrefabSelectionMode.RoundRobin)
            {
                nextPrefabIndex =
                    (index + 1) % enemyPrefabs.Length;
            }

            return true;
        }

        return false;
    }

    private bool TrySelectSpawnPoint(
        out Vector3 spawnPosition,
        out int selectedPointIndex,
        out string failureDetail
    )
    {
        spawnPosition = default;
        selectedPointIndex = -1;
        failureDetail = string.Empty;

        if (patrolArea == null)
        {
            failureDetail = "Patrol Area 未指定。";
            return false;
        }

        int pointCount = patrolArea.PointCount;

        if (pointCount <= 0)
        {
            failureDetail =
                "Patrol Area 沒有任何人工節點；請檢查 Patrol Points Parent 的直接子物件。";
            return false;
        }

        int firstIndex = UnityEngine.Random.Range(0, pointCount);
        int rejectedByArea = 0;
        int occupied = 0;

        for (int offset = 0;
             offset < pointCount;
             offset++)
        {
            int pointIndex =
                (firstIndex + offset) % pointCount;

            EnemyPatrolPointQueryResult query =
                patrolArea.QueryPoint(
                    pointIndex,
                    patrolArea.transform.position,
                    out Vector3 point,
                    out _,
                    out _,
                    out _
                );

            if (query != EnemyPatrolPointQueryResult.Valid)
            {
                rejectedByArea++;
                continue;
            }

            Vector3 candidate =
                point + spawnPositionOffset;

            if (IsSpawnPointOccupied(candidate))
            {
                occupied++;
                continue;
            }

            spawnPosition = candidate;
            selectedPointIndex = pointIndex;
            return true;
        }

        failureDetail =
            $"Point Count={pointCount}, " +
            $"Rejected By Area Distance={rejectedByArea}, " +
            $"Occupied={occupied}. " +
            "生成查詢以 Patrol Area Root 作為距離參考；若節點被距離排除，" +
            "請調整 EnemyPatrolArea.Maximum Point Distance 或設為 0。";

        return false;
    }

    private bool IsSpawnPointOccupied(
        Vector3 rootPosition
    )
    {
        if (spawnOccupancyMask.value == 0)
        {
            return false;
        }

        Vector3 center =
            rootPosition +
            Vector3.up * spawnOccupancyCenterHeight;

        return Runner.GetPhysicsScene().OverlapSphere(
            center,
            spawnOccupancyRadius,
            occupancyBuffer,
            spawnOccupancyMask,
            QueryTriggerInteraction.Ignore
        ) > 0;
    }

    private Quaternion ResolveSpawnRotation()
    {
        if (spawnRotation == SpawnRotationMode.RandomYaw)
        {
            return Quaternion.Euler(
                0f,
                UnityEngine.Random.Range(0f, 360f),
                0f
            );
        }

        Vector3 forward =
            patrolArea != null
                ? Vector3.ProjectOnPlane(
                    patrolArea.transform.forward,
                    Vector3.up
                )
                : Vector3.forward;

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.forward;
        }

        return Quaternion.LookRotation(
            forward.normalized,
            Vector3.up
        );
    }

    private void RemoveInvalidSpawnedReferences()
    {
        for (int index = spawnedEnemies.Count - 1;
             index >= 0;
             index--)
        {
            NetworkObject candidate = spawnedEnemies[index];

            if (candidate == null ||
                !candidate.IsValid)
            {
                spawnedEnemies.RemoveAt(index);
            }
        }
    }

    private void OnValidate()
    {
        minimumSecondsBetweenRequests =
            Mathf.Max(0f, minimumSecondsBetweenRequests);
        spawnOccupancyRadius =
            Mathf.Max(0.01f, spawnOccupancyRadius);
        spawnOccupancyCenterHeight =
            Mathf.Max(0f, spawnOccupancyCenterHeight);
        maximumAliveFromThisSpawner =
            Mathf.Max(0, maximumAliveFromThisSpawner);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSpawnPointGizmos ||
            patrolArea == null)
        {
            return;
        }

        int pointCount = patrolArea.PointCount;

        for (int index = 0;
             index < pointCount;
             index++)
        {
            EnemyPatrolPointQueryResult query =
                patrolArea.QueryPoint(
                    index,
                    patrolArea.transform.position,
                    out Vector3 point,
                    out _,
                    out _,
                    out _
                );

            Gizmos.color =
                query == EnemyPatrolPointQueryResult.Valid
                    ? spawnPointGizmoColor
                    : rejectedPointGizmoColor;

            Vector3 center =
                point +
                spawnPositionOffset +
                Vector3.up * spawnOccupancyCenterHeight;

            Gizmos.DrawWireSphere(
                center,
                spawnOccupancyRadius
            );
        }
    }
}
