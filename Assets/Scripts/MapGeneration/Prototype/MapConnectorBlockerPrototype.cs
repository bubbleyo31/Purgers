using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class MapConnectorBlockerPrototype : MonoBehaviour
{
    private static readonly ConnectorSide[] AllSides =
    {
        ConnectorSide.North,
        ConnectorSide.East,
        ConnectorSide.South,
        ConnectorSide.West
    };

    [Header("Blocker Prefab")]

    [Tooltip(
        "生成在未接合 Connector 上的阻擋屋 Prefab。" +
        "Prefab Root 會對齊 Connector 的 Blocker Spawn Point；" +
        "此 Prototype 使用一般 Instantiate，因此 Prefab 不可包含 NetworkObject。")]
    [SerializeField]
    private GameObject blockerPrefab;

    [Tooltip(
        "啟用後輸出每個阻擋屋使用的 Chunk、Connector 方向與生成位置。")]
    [SerializeField]
    private bool debugBlockerPlacement;

    [Header("NavMesh Blocking")]

    [Tooltip(
        "啟用後，阻擋屋生成時確保存在 Box 型 NavMeshObstacle 並開啟 Carving。" +
        "Chunk 使用預先烘焙 NavMeshData，因此 Runtime 阻擋屋必須用 Carving 封閉通道。")]
    [SerializeField]
    private bool ensureCarvingNavMeshObstacle = true;

    private readonly List<GameObject> spawnedBlockers =
        new List<GameObject>();

    public int SpawnedBlockerCount => spawnedBlockers.Count;

    /// <summary>
    /// 兩個 Chunk 接合完成後，封閉除了已接合 Connector 以外的所有開口。
    /// 玩家出生入口不是 Chunk 接合點，因此也會生成阻擋屋。
    /// </summary>
    public bool TryGenerateForConnection(
        MapChunk firstChunk,
        MapConnector firstConnectedConnector,
        MapChunk secondChunk,
        MapConnector secondConnectedConnector)
    {
        if (!DevelopmentToolsPolicy.IsEnabled ||
            !Application.isPlaying ||
            !isActiveAndEnabled)
        {
            return false;
        }

        if (spawnedBlockers.Count > 0)
        {
            Debug.LogWarning(
                "[MapConnectorBlocker] 本輪阻擋屋已經生成，不會重複生成。",
                this);
            return false;
        }

        if (blockerPrefab == null)
        {
            Debug.LogWarning(
                "[MapConnectorBlocker] 尚未指定 Blocker Prefab。",
                this);
            return false;
        }

        if (blockerPrefab.GetComponentInChildren<NetworkObject>(true) != null)
        {
            Debug.LogError(
                "[MapConnectorBlocker] Blocker Prefab 不可包含 NetworkObject；" +
                "目前流程使用一般 Instantiate。",
                blockerPrefab);
            return false;
        }

        if (!ValidateConnectionOwnership(
                firstChunk,
                firstConnectedConnector,
                secondChunk,
                secondConnectedConnector))
        {
            return false;
        }

        try
        {
            GenerateForChunk(
                firstChunk,
                firstConnectedConnector);

            GenerateForChunk(
                secondChunk,
                secondConnectedConnector);

            Debug.Log(
                $"[MapConnectorBlocker] 阻擋屋生成完成。" +
                $"\nCount: {spawnedBlockers.Count}" +
                $"\nOpen Connection: " +
                $"{firstChunk.name}/{firstConnectedConnector.Side} <-> " +
                $"{secondChunk.name}/{secondConnectedConnector.Side}",
                this);

            return true;
        }
        catch (Exception exception) when (
            exception is MissingReferenceException ||
            exception is InvalidOperationException)
        {
            ClearGeneratedBlockers();

            Debug.LogError(
                $"[MapConnectorBlocker] 阻擋屋生成失敗。\n{exception.Message}",
                this);

            return false;
        }
    }

    private void GenerateForChunk(
        MapChunk chunk,
        MapConnector connectedConnector)
    {
        foreach (ConnectorSide side in AllSides)
        {
            MapConnector connector = chunk.GetConnector(side);

            if (connector == connectedConnector)
                continue;

            Transform anchor = connector.BlockerSpawnPoint;
            Pose pose = connector.BlockerSpawnPose;

            GameObject blocker = Instantiate(
                blockerPrefab,
                pose.position,
                pose.rotation,
                anchor);

            blocker.name =
                $"{blockerPrefab.name}_{chunk.name}_{side}_Blocker";

            ConfigureNavMeshBlocking(blocker);

            spawnedBlockers.Add(blocker);

            if (debugBlockerPlacement)
            {
                Debug.Log(
                    $"[MapConnectorBlocker] 已封閉 Connector。" +
                    $"\nChunk: {chunk.name}" +
                    $"\nSide: {side}" +
                    $"\nPosition: {pose.position:F3}",
                    blocker);
            }
        }
    }

    private void ConfigureNavMeshBlocking(
        GameObject blocker)
    {
        if (!ensureCarvingNavMeshObstacle)
            return;

        NavMeshObstacle[] existingObstacles =
            blocker.GetComponentsInChildren<NavMeshObstacle>(true);

        if (existingObstacles.Length > 0)
        {
            foreach (NavMeshObstacle obstacle in existingObstacles)
            {
                obstacle.enabled = true;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }

            return;
        }

        Collider[] colliders =
            blocker.GetComponentsInChildren<Collider>(true);

        bool hasBounds = false;
        Bounds localBounds = default;

        foreach (Collider collider in colliders)
        {
            if (collider == null ||
                !collider.enabled ||
                collider.isTrigger)
            {
                continue;
            }

            Bounds worldBounds = collider.bounds;

            for (int cornerIndex = 0;
                 cornerIndex < 8;
                 cornerIndex++)
            {
                Vector3 worldCorner =
                    worldBounds.center +
                    Vector3.Scale(
                        worldBounds.extents,
                        new Vector3(
                            (cornerIndex & 1) == 0 ? -1f : 1f,
                            (cornerIndex & 2) == 0 ? -1f : 1f,
                            (cornerIndex & 4) == 0 ? -1f : 1f));

                Vector3 localCorner =
                    blocker.transform.InverseTransformPoint(
                        worldCorner);

                if (!hasBounds)
                {
                    localBounds =
                        new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        if (!hasBounds)
        {
            Debug.LogWarning(
                "[MapConnectorBlocker] 阻擋屋沒有可用 Collider，" +
                "無法自動建立 NavMeshObstacle。",
                blocker);
            return;
        }

        NavMeshObstacle generatedObstacle =
            blocker.AddComponent<NavMeshObstacle>();

        generatedObstacle.shape =
            NavMeshObstacleShape.Box;
        generatedObstacle.center =
            localBounds.center;
        generatedObstacle.size =
            localBounds.size;
        generatedObstacle.carving = true;
        generatedObstacle.carveOnlyStationary = true;
        generatedObstacle.carvingMoveThreshold = 0.1f;
        generatedObstacle.carvingTimeToStationary = 0.1f;
    }

    private bool ValidateConnectionOwnership(
        MapChunk firstChunk,
        MapConnector firstConnectedConnector,
        MapChunk secondChunk,
        MapConnector secondConnectedConnector)
    {
        if (firstChunk == null ||
            firstConnectedConnector == null ||
            secondChunk == null ||
            secondConnectedConnector == null)
        {
            Debug.LogError(
                "[MapConnectorBlocker] Chunk 或已接合 Connector 為 Null。",
                this);
            return false;
        }

        if (firstChunk == secondChunk)
        {
            Debug.LogError(
                "[MapConnectorBlocker] 接合的兩端不可屬於同一個 Chunk。",
                this);
            return false;
        }

        if (!firstConnectedConnector.transform.IsChildOf(firstChunk.transform) ||
            !secondConnectedConnector.transform.IsChildOf(secondChunk.transform))
        {
            Debug.LogError(
                "[MapConnectorBlocker] 已接合 Connector 不屬於指定的 Chunk。",
                this);
            return false;
        }

        return true;
    }

    private void ClearGeneratedBlockers()
    {
        foreach (GameObject blocker in spawnedBlockers)
        {
            if (blocker != null)
                Destroy(blocker);
        }

        spawnedBlockers.Clear();
    }
}
