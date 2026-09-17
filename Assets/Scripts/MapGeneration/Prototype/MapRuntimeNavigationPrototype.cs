using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class MapRuntimeNavigationPrototype : MonoBehaviour
{
    private static readonly ConnectorSide[] AllSides =
    {
        ConnectorSide.North,
        ConnectorSide.East,
        ConnectorSide.South,
        ConnectorSide.West
    };

    [Header("Chunk NavMesh")]

    [SerializeField, Tooltip(
        "Runtime NavMesh 使用的 Agent Type ID。Humanoid 通常為 0，" +
        "必須與 EnemyGroundPatrolNavigator 使用的 NavMesh Query 相容。")]
    private int agentTypeId;

    [SerializeField, Min(0.01f), Tooltip(
        "接合點往兩個 Chunk 內側搜尋 NavMesh 的距離。" +
        "用來建立跨越 Connector 接縫的雙向 NavMesh Link。")]
    private float connectionLinkInsideDistance = 6f;

    [SerializeField, Min(0.01f), Tooltip(
        "接合 Link 兩端使用 NavMesh.SamplePosition 的最大搜尋距離。")]
    private float connectionLinkSampleDistance = 10f;

    [SerializeField, Min(0f), Tooltip(
        "跨 Chunk NavMesh Link 的寬度。0 代表單一路徑點。")]
    private float connectionLinkWidth = 4f;

    [Header("Ground Patrol Planning")]

    [SerializeField, Min(3), Tooltip(
        "每個 Chunk 在 X 與 Z 軸建立多少個候選取樣格。" +
        "實際巡邏點仍會經過 NavMesh、邊界、重複距離與可連通性檢查。")]
    private int samplesPerAxis = 7;

    [SerializeField, Min(0f), Tooltip(
        "從四個 Connector 所形成的 Chunk 邊界往內縮多少公尺，" +
        "避免巡邏點落在接合線或阻擋屋外側。")]
    private float connectorBoundaryInset = 12f;

    [SerializeField, Tooltip(
        "NavMesh 取樣起點相對 Chunk Root 的局部 Y 高度。" +
        "目前地面位於 Root Y=0，因此預設從上方 2 公尺向附近 NavMesh 投影。")]
    private float sampleLocalHeight = 2f;

    [SerializeField, Min(0.01f), Tooltip(
        "每個格點呼叫 NavMesh.SamplePosition 時允許搜尋的最大距離。")]
    private float navMeshSampleDistance = 8f;

    [SerializeField, Min(0f), Tooltip(
        "兩個自動巡邏點之間至少保留的水平距離。")]
    private float minimumPointSpacing = 8f;

    [SerializeField, Min(2), Tooltip(
        "每個 Chunk 最多保留多少個自動巡邏點。")]
    private int maximumPointsPerChunk = 24;

    [SerializeField, Min(2), Tooltip(
        "少於此數量代表該 Chunk 的 NavMesh 或取樣設定無效，" +
        "不建立不完整的 EnemyPatrolArea。")]
    private int minimumPointsPerChunk = 4;

    [SerializeField, Min(0f), Tooltip(
        "自動 Patrol Area 的最大巡邏點距離；0 代表不限制，" +
        "由 Enemy Navigator 的完整路徑檢查決定是否可用。")]
    private float maximumPatrolPointDistance;

    [SerializeField, Tooltip(
        "自動建立的地面巡邏區 ID。多個 Chunk 可以共用此 ID；" +
        "Enemy 出生時會直接綁定實際使用的 Area。")]
    private string groundPatrolAreaId = "ground_runtime";

    [SerializeField, Tooltip(
        "輸出 Runtime NavMesh 三角形數與每個 Chunk 的巡邏點規劃結果。")]
    private bool debugRuntimeNavigation = true;

    private readonly List<EnemyPatrolArea> generatedGroundAreas =
        new List<EnemyPatrolArea>();

    private GameObject runtimeMapRoot;
    private NavMeshLinkInstance connectionLink;
    private bool buildAttempted;

    public bool IsReady { get; private set; }
    public IReadOnlyList<EnemyPatrolArea> GeneratedGroundAreas =>
        generatedGroundAreas;

    /// <summary>
    /// 只由已完成 Host 權威檢查的地圖生成流程呼叫。
    /// 順序必須是 Chunk 對齊、阻擋屋生成、NavMesh 建置、Patrol Area 規劃。
    /// </summary>
    public bool TryBuildAndPlan(
        MapChunk firstChunk,
        MapConnector firstConnectedConnector,
        MapChunk secondChunk,
        MapConnector secondConnectedConnector,
        int runSeed)
    {
        if (!DevelopmentToolsPolicy.IsEnabled ||
            !Application.isPlaying ||
            !isActiveAndEnabled)
        {
            return false;
        }

        if (buildAttempted)
        {
            Debug.LogWarning(
                "[MapRuntimeNavigation] 本輪已經嘗試建置，不會重複執行。",
                this);
            return IsReady;
        }

        buildAttempted = true;
        IsReady = false;

        if (firstChunk == null ||
            firstConnectedConnector == null ||
            secondChunk == null ||
            secondConnectedConnector == null)
        {
            Debug.LogError(
                "[MapRuntimeNavigation] First 或 Second Chunk 為 Null。",
                this);
            return false;
        }

        if (firstChunk.gameObject.scene != secondChunk.gameObject.scene)
        {
            Debug.LogError(
                "[MapRuntimeNavigation] 兩個 Chunk 必須位於同一個 Unity Scene。",
                this);
            return false;
        }

        EnemyPatrolArea pendingFirstArea = null;
        EnemyPatrolArea pendingSecondArea = null;

        try
        {
            PrepareRuntimeRoot(firstChunk, secondChunk);

            if (!TryGetBakedSurface(
                    firstChunk,
                    out NavMeshSurface firstSurface) ||
                !TryGetBakedSurface(
                    secondChunk,
                    out NavMeshSurface secondSurface))
            {
                return false;
            }

            firstSurface.RemoveData();
            secondSurface.RemoveData();

            firstSurface.AddData();
            secondSurface.AddData();

            if (!TryCreateConnectionLink(
                    firstConnectedConnector,
                    secondConnectedConnector))
            {
                return false;
            }

            NavMeshTriangulation triangulation =
                NavMesh.CalculateTriangulation();

            if (triangulation.vertices == null ||
                triangulation.vertices.Length == 0)
            {
                Debug.LogError(
                    "[MapRuntimeNavigation] 預先烘焙的 Chunk NavMesh 沒有載入任何頂點。" +
                    "請檢查 MapChunk Prefab 的 NavMeshSurface 與 NavMeshData。",
                    this);
                return false;
            }

            bool firstAreaCreated =
                TryCreateGroundPatrolArea(
                    firstChunk,
                    runSeed,
                    0,
                    out pendingFirstArea);

            bool secondAreaCreated =
                firstAreaCreated &&
                TryCreateGroundPatrolArea(
                    secondChunk,
                    runSeed,
                    1,
                    out pendingSecondArea);

            if (!firstAreaCreated || !secondAreaCreated)
            {
                DestroyRuntimeArea(pendingFirstArea);
                DestroyRuntimeArea(pendingSecondArea);

                if (connectionLink.valid)
                    connectionLink.Remove();

                return false;
            }

            generatedGroundAreas.Add(pendingFirstArea);
            generatedGroundAreas.Add(pendingSecondArea);
            IsReady = true;

            if (debugRuntimeNavigation)
            {
                Debug.Log(
                    "[MapRuntimeNavigation] Chunk NavMesh、接合 Link 與巡邏區已就緒。" +
                    $"\nNavMesh Vertices: {triangulation.vertices.Length}" +
                    $"\nNavMesh Triangles: {triangulation.indices.Length / 3}" +
                    $"\nFirst Points: {pendingFirstArea.PointCount}" +
                    $"\nSecond Points: {pendingSecondArea.PointCount}",
                    this);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is MissingReferenceException ||
            exception is InvalidOperationException ||
            exception is ArgumentException)
        {
            DestroyRuntimeArea(pendingFirstArea);
            DestroyRuntimeArea(pendingSecondArea);

            if (connectionLink.valid)
                connectionLink.Remove();

            Debug.LogError(
                $"[MapRuntimeNavigation] 建置失敗。\n{exception.Message}",
                this);
            return false;
        }
    }

    private void PrepareRuntimeRoot(
        MapChunk firstChunk,
        MapChunk secondChunk)
    {
        runtimeMapRoot = new GameObject("RuntimeGeneratedMapRoot");

        Scene targetScene = firstChunk.gameObject.scene;
        SceneManager.MoveGameObjectToScene(
            runtimeMapRoot,
            targetScene);

        runtimeMapRoot.transform.SetPositionAndRotation(
            Vector3.zero,
            Quaternion.identity);

        firstChunk.transform.SetParent(
            runtimeMapRoot.transform,
            true);

        secondChunk.transform.SetParent(
            runtimeMapRoot.transform,
            true);

    }

    private bool TryGetBakedSurface(
        MapChunk chunk,
        out NavMeshSurface surface)
    {
        surface = null;

        NavMeshSurface[] surfaces =
            chunk.GetComponentsInChildren<NavMeshSurface>(true);

        if (surfaces.Length != 1)
        {
            Debug.LogError(
                $"[MapRuntimeNavigation] {chunk.name} 必須剛好包含一個" +
                $"預先烘焙的 NavMeshSurface；目前為 {surfaces.Length}。",
                chunk);
            return false;
        }

        surface = surfaces[0];

        if (surface.navMeshData == null)
        {
            Debug.LogError(
                $"[MapRuntimeNavigation] {chunk.name} 的 NavMeshSurface" +
                "尚未在 Prefab 編輯模式完成 Bake。",
                surface);
            return false;
        }

        if (surface.agentTypeID != agentTypeId)
        {
            Debug.LogError(
                $"[MapRuntimeNavigation] {chunk.name} Agent Type 不一致。" +
                $"\nExpected: {agentTypeId}" +
                $"\nActual: {surface.agentTypeID}",
                surface);
            return false;
        }

        return true;
    }

    private bool TryCreateConnectionLink(
        MapConnector firstConnector,
        MapConnector secondConnector)
    {
        Vector3 firstCandidate =
            firstConnector.transform.position -
            firstConnector.transform.forward *
            connectionLinkInsideDistance;

        Vector3 secondCandidate =
            secondConnector.transform.position -
            secondConnector.transform.forward *
            connectionLinkInsideDistance;

        if (!NavMesh.SamplePosition(
                firstCandidate,
                out NavMeshHit firstHit,
                connectionLinkSampleDistance,
                NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(
                secondCandidate,
                out NavMeshHit secondHit,
                connectionLinkSampleDistance,
                NavMesh.AllAreas))
        {
            Debug.LogError(
                "[MapRuntimeNavigation] 無法在接合 Connector 兩側找到 NavMesh，" +
                "請調整 Link Inside Distance 或 Sample Distance。",
                this);
            return false;
        }

        var linkData = new NavMeshLinkData
        {
            startPosition = firstHit.position,
            endPosition = secondHit.position,
            width = connectionLinkWidth,
            costModifier = -1f,
            bidirectional = true,
            area = 0,
            agentTypeID = agentTypeId
        };

        connectionLink = NavMesh.AddLink(linkData);

        if (!connectionLink.valid)
        {
            Debug.LogError(
                "[MapRuntimeNavigation] 跨 Chunk NavMesh Link 建立失敗。",
                this);
            return false;
        }

        connectionLink.owner = this;
        return true;
    }

    private bool TryCreateGroundPatrolArea(
        MapChunk chunk,
        int runSeed,
        int chunkIndex,
        out EnemyPatrolArea patrolArea)
    {
        patrolArea = null;

        if (!TryGetPlanningBounds(
                chunk,
                out float minimumX,
                out float maximumX,
                out float minimumZ,
                out float maximumZ))
        {
            return false;
        }

        List<Vector3> sampledPoints =
            SampleNavMeshPoints(
                chunk,
                minimumX,
                maximumX,
                minimumZ,
                maximumZ);

        List<Vector3> connectedPoints =
            SelectLargestConnectedSet(sampledPoints);

        if (connectedPoints.Count < minimumPointsPerChunk)
        {
            Debug.LogError(
                $"[MapRuntimeNavigation] {chunk.name} 可連通巡邏點不足。" +
                $"\nSampled: {sampledPoints.Count}" +
                $"\nConnected: {connectedPoints.Count}" +
                $"\nRequired: {minimumPointsPerChunk}",
                chunk);
            return false;
        }

        ShuffleDeterministically(
            connectedPoints,
            unchecked(runSeed ^ (chunkIndex * 486187739)));

        if (connectedPoints.Count > maximumPointsPerChunk)
        {
            connectedPoints.RemoveRange(
                maximumPointsPerChunk,
                connectedPoints.Count - maximumPointsPerChunk);
        }

        GameObject areaObject =
            new GameObject(
                $"RuntimeGroundPatrolArea_{chunkIndex}");

        areaObject.transform.SetParent(
            chunk.transform,
            false);

        GameObject pointsObject =
            new GameObject("PatrolPoints");

        pointsObject.transform.SetParent(
            areaObject.transform,
            false);

        for (int index = 0;
             index < connectedPoints.Count;
             index++)
        {
            GameObject pointObject =
                new GameObject($"Point_{index:00}");

            pointObject.transform.SetParent(
                pointsObject.transform,
                false);

            pointObject.transform.position =
                connectedPoints[index];
        }

        patrolArea =
            areaObject.AddComponent<EnemyPatrolArea>();

        patrolArea.ConfigureRuntime(
            groundPatrolAreaId,
            pointsObject.transform,
            maximumPatrolPointDistance);

        if (debugRuntimeNavigation)
        {
            Debug.Log(
                $"[MapRuntimeNavigation] {chunk.name} 巡邏區已規劃。" +
                $"\nSampled: {sampledPoints.Count}" +
                $"\nConnected: {connectedPoints.Count}" +
                $"\nBounds X: {minimumX:F1}..{maximumX:F1}" +
                $"\nBounds Z: {minimumZ:F1}..{maximumZ:F1}",
                patrolArea);
        }

        return true;
    }

    private bool TryGetPlanningBounds(
        MapChunk chunk,
        out float minimumX,
        out float maximumX,
        out float minimumZ,
        out float maximumZ)
    {
        minimumX = float.PositiveInfinity;
        maximumX = float.NegativeInfinity;
        minimumZ = float.PositiveInfinity;
        maximumZ = float.NegativeInfinity;

        foreach (ConnectorSide side in AllSides)
        {
            MapConnector connector =
                chunk.GetConnector(side);

            Vector3 localPosition =
                chunk.transform.InverseTransformPoint(
                    connector.transform.position);

            minimumX = Mathf.Min(minimumX, localPosition.x);
            maximumX = Mathf.Max(maximumX, localPosition.x);
            minimumZ = Mathf.Min(minimumZ, localPosition.z);
            maximumZ = Mathf.Max(maximumZ, localPosition.z);
        }

        minimumX += connectorBoundaryInset;
        maximumX -= connectorBoundaryInset;
        minimumZ += connectorBoundaryInset;
        maximumZ -= connectorBoundaryInset;

        if (minimumX >= maximumX ||
            minimumZ >= maximumZ)
        {
            Debug.LogError(
                $"[MapRuntimeNavigation] {chunk.name} 的 Connector 邊界無效；" +
                "請降低 Boundary Inset 或檢查 Connector 位置。",
                chunk);
            return false;
        }

        return true;
    }

    private List<Vector3> SampleNavMeshPoints(
        MapChunk chunk,
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ)
    {
        var result = new List<Vector3>();
        int axisCount = Mathf.Max(3, samplesPerAxis);
        float minimumSpacingSquared =
            minimumPointSpacing * minimumPointSpacing;

        for (int zIndex = 0;
             zIndex < axisCount;
             zIndex++)
        {
            float zT = axisCount <= 1
                ? 0.5f
                : zIndex / (axisCount - 1f);

            for (int xIndex = 0;
                 xIndex < axisCount;
                 xIndex++)
            {
                float xT = axisCount <= 1
                    ? 0.5f
                    : xIndex / (axisCount - 1f);

                Vector3 localCandidate =
                    new Vector3(
                        Mathf.Lerp(minimumX, maximumX, xT),
                        sampleLocalHeight,
                        Mathf.Lerp(minimumZ, maximumZ, zT));

                Vector3 worldCandidate =
                    chunk.transform.TransformPoint(
                        localCandidate);

                if (!NavMesh.SamplePosition(
                        worldCandidate,
                        out NavMeshHit hit,
                        navMeshSampleDistance,
                        NavMesh.AllAreas))
                {
                    continue;
                }

                Vector3 sampledLocal =
                    chunk.transform.InverseTransformPoint(
                        hit.position);

                if (sampledLocal.x < minimumX ||
                    sampledLocal.x > maximumX ||
                    sampledLocal.z < minimumZ ||
                    sampledLocal.z > maximumZ)
                {
                    continue;
                }

                bool tooClose = false;

                foreach (Vector3 existing in result)
                {
                    Vector2 delta =
                        new Vector2(
                            existing.x - hit.position.x,
                            existing.z - hit.position.z);

                    if (delta.sqrMagnitude < minimumSpacingSquared)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                    result.Add(hit.position);
            }
        }

        return result;
    }

    private static List<Vector3> SelectLargestConnectedSet(
        List<Vector3> candidates)
    {
        if (candidates.Count <= 1)
            return new List<Vector3>(candidates);

        int bestRootIndex = -1;
        int bestReachableCount = 0;
        var path = new NavMeshPath();

        for (int rootIndex = 0;
             rootIndex < candidates.Count;
             rootIndex++)
        {
            int reachableCount = 0;

            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                if (IsPathComplete(
                        candidates[rootIndex],
                        candidates[candidateIndex],
                        path))
                {
                    reachableCount++;
                }
            }

            if (reachableCount > bestReachableCount)
            {
                bestReachableCount = reachableCount;
                bestRootIndex = rootIndex;
            }
        }

        var result = new List<Vector3>();

        if (bestRootIndex < 0)
            return result;

        Vector3 bestRoot = candidates[bestRootIndex];

        foreach (Vector3 candidate in candidates)
        {
            if (IsPathComplete(
                    bestRoot,
                    candidate,
                    path))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static bool IsPathComplete(
        Vector3 start,
        Vector3 destination,
        NavMeshPath path)
    {
        return NavMesh.CalculatePath(
                   start,
                   destination,
                   NavMesh.AllAreas,
                   path) &&
               path.status ==
               NavMeshPathStatus.PathComplete;
    }

    private static void ShuffleDeterministically(
        List<Vector3> points,
        int seed)
    {
        var random = new System.Random(seed);

        for (int index = points.Count - 1;
             index > 0;
             index--)
        {
            int swapIndex = random.Next(0, index + 1);
            Vector3 temporary = points[index];
            points[index] = points[swapIndex];
            points[swapIndex] = temporary;
        }
    }

    private static void DestroyRuntimeArea(
        EnemyPatrolArea patrolArea)
    {
        if (patrolArea != null)
            Destroy(patrolArea.gameObject);
    }

    private void OnValidate()
    {
        samplesPerAxis = Mathf.Max(3, samplesPerAxis);
        connectorBoundaryInset =
            Mathf.Max(0f, connectorBoundaryInset);
        navMeshSampleDistance =
            Mathf.Max(0.01f, navMeshSampleDistance);
        connectionLinkInsideDistance =
            Mathf.Max(0.01f, connectionLinkInsideDistance);
        connectionLinkSampleDistance =
            Mathf.Max(0.01f, connectionLinkSampleDistance);
        connectionLinkWidth =
            Mathf.Max(0f, connectionLinkWidth);
        minimumPointSpacing =
            Mathf.Max(0f, minimumPointSpacing);
        maximumPointsPerChunk =
            Mathf.Max(2, maximumPointsPerChunk);
        minimumPointsPerChunk =
            Mathf.Clamp(
                minimumPointsPerChunk,
                2,
                maximumPointsPerChunk);
        maximumPatrolPointDistance =
            Mathf.Max(0f, maximumPatrolPointDistance);
    }

    private void OnDisable()
    {
        if (connectionLink.valid)
            connectionLink.Remove();
    }
}
