using System;
using System.Collections.Generic;
using Fusion;
using Purgers.MapGeneration.Topology;
using UnityEngine;

public sealed class ConnectorAlignmentPrototype : MonoBehaviour
{
    private static readonly ConnectorSide[] AllSides =
    {
        ConnectorSide.North,
        ConnectorSide.East,
        ConnectorSide.South,
        ConnectorSide.West
    };

    [Header("區塊參考")]

    [Tooltip(
        "目前已經放置在場景中的固定地圖區塊。" +
        "測試時不會移動或旋轉這個 Chunk。")]
    [SerializeField]
    private MapChunk fixedChunk;

    [Tooltip(
        "準備生成並接到 Fixed Chunk 上的地圖 Prefab。" +
        "Prefab Root 建議維持 Position 零值、Rotation 零值與 Scale 1。")]
    [SerializeField]
    private MapChunk movingChunkPrefab;

    [Tooltip(
        "Host 自動生成第二 Chunk 時可抽選的 Prefab。" +
        "會忽略 Null；若沒有有效元素，則回退使用 Moving Chunk Prefab。" +
        "目前只有一個候選時，仍會隨機決定生成方向與入口旋轉。")]
    [SerializeField]
    private MapChunk[] hostMovingChunkPrefabs;

    [Tooltip(
        "兩個 Chunk 對齊成功後，為所有未接合 Connector 生成阻擋屋。" +
        "留空時只生成並對齊 Chunk。")]
    [SerializeField]
    private MapConnectorBlockerPrototype connectorBlockers;

    [Tooltip(
        "Host 完成第二 Chunk 對齊與阻擋屋生成後，" +
        "載入各 Chunk 預烘焙 NavMeshData、建立接縫 Link，" +
        "並自動規劃每個 Chunk 的地面巡邏區。")]
    [SerializeField]
    private MapRuntimeNavigationPrototype runtimeNavigation;

    [Header("Phase 4-B 多 Chunk 拓撲")]

    [SerializeField, Min(1), Tooltip(
        "把 Prefab Connector 的局部公尺座標量化成 Phase 4-A 整數拓撲單位。" +
        "Host 與測試必須使用相同值；預設 1000 代表 1 mm 精度。")]
    private int topologyUnitsPerMeter = 1000;

    [SerializeField, Min(1), Tooltip(
        "Phase 4-A Planner 為每個新增 Chunk 允許的最大重抽次數。" +
        "超過後整份地圖準備失敗，不提交部分拓撲。")]
    private int maxTopologyAttemptsPerChunk = 32;


    [Header("區塊連接測試")]

    [Tooltip(
        "Fixed Chunk 上作為本次出口的 Connector 方向。" +
        "例如選擇 West，代表下一張地圖會接到它的西側開口。")]
    [SerializeField]
    private ConnectorSide fixedExitSide = ConnectorSide.West;

    [Tooltip(
        "新生成 Chunk 上用來銜接 Fixed Exit 的入口方向。" +
        "例如選擇 North，系統會旋轉新 Chunk，使 North Connector 面向 Fixed Exit。")]
    [SerializeField]
    private ConnectorSide movingEntrySide = ConnectorSide.North;

    [Tooltip(
        "啟用後，Host 會依本輪 Run Seed 從第二 Chunk 的四個 Connector 中" +
        "隨機選擇一個作為入口。離線 N 鍵測試仍使用 Moving Entry Side。")]
    [SerializeField]
    private bool randomizeMovingEntrySideOnHost = true;

    [Tooltip(
        "Play Mode 中執行一次生成及 Connector 對齊測試的按鍵。")]
    [SerializeField]
    private KeyCode spawnKey = KeyCode.N;


    [Header("設定驗證")]

    [Tooltip(
        "兩個 Connector 位置可接受的最大誤差，單位為 Unity 世界單位。" +
        "這只用於驗證結果，不會改變地圖之間的實際距離。")]
    [SerializeField, Min(0f)]
    private float positionTolerance = 0.001f;

    [Tooltip(
        "Connector Forward 與 Up 方向可接受的最大角度誤差，單位為度。" +
        "數值越小，驗證要求越嚴格。")]
    [SerializeField, Min(0f)]
    private float angleTolerance = 0.1f;

    private MapChunk spawnedChunk;
    private MapConnector connectedFixedConnector;
    private MapConnector connectedMovingConnector;
    private readonly List<MapChunk> generatedChunks = new List<MapChunk>();
    private readonly List<int> openSideMasks = new List<int>();
    private bool topologyBuildAttempted;

    public MapChunk SpawnedChunk =>
        spawnedChunk;

    public MapChunk SpawnedPrefab { get; private set; }
    public MapConnectorBlockerPrototype Blockers => connectorBlockers;
    public MapRuntimeNavigationPrototype RuntimeNavigation => runtimeNavigation;
    public IReadOnlyList<MapChunk> GeneratedChunks => generatedChunks;
    public IReadOnlyList<int> OpenSideMasks => openSideMasks;
    public MapTopologyPlan TopologyPlan { get; private set; }
    public MapChunk FinalChunk =>
        TopologyPlan != null && TopologyPlan.IsComplete && generatedChunks.Count > 0
            ? generatedChunks[TopologyPlan.FinalChunkInstanceIndex]
            : null;
    public int StartingOpenSides => connectedFixedConnector == null ? 0 : 1 << (int)connectedFixedConnector.Side;
    public int SpawnedOpenSides => connectedMovingConnector == null ? 0 : 1 << (int)connectedMovingConnector.Side;

    /// <summary>
    /// Phase 4-B Host 入口。Phase 4-A Planner 決定完整線性鏈，這個既有 owner
    /// 再依 plan 生成、對齊、封口，最後才交給既有 Runtime Navigation。
    /// </summary>
    public bool TryBuildTopologyForHost(
        NetworkRunner runner,
        MapChunk startingChunk,
        ConnectorSide startingEntrySide,
        ConnectorSide startingExitSide,
        int runSeed,
        int additionalChunkCount)
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            runner == null ||
            !runner.IsRunning ||
            !runner.IsServer ||
            startingChunk == null ||
            additionalChunkCount < 0)
        {
            return false;
        }

        if (topologyBuildAttempted)
            return TopologyPlan != null && TopologyPlan.IsComplete &&
                   runtimeNavigation != null && runtimeNavigation.IsReady;

        topologyBuildAttempted = true;

        if (connectorBlockers == null || runtimeNavigation == null)
        {
            Debug.LogError(
                "[MapTopologyRuntime] Connector Blockers 與 Runtime Navigation 都必須指定；" +
                "Phase 4-B 不接受只有幾何、沒有封口或導航的部分成功。",
                this);
            return false;
        }

        var definitionToPrefab =
            new Dictionary<MapChunkTopologyDefinition, MapChunk>();

        try
        {
            MapChunkTopologyDefinition startingDefinition =
                CreateTopologyDefinition(startingChunk, "Starting");
            List<MapChunk> candidatePrefabs = additionalChunkCount > 0
                ? CollectCandidatePrefabs()
                : new List<MapChunk>();
            var candidateDefinitions =
                new List<MapChunkTopologyDefinition>(candidatePrefabs.Count);

            foreach (MapChunk prefab in candidatePrefabs)
            {
                MapChunkTopologyDefinition definition =
                    CreateTopologyDefinition(prefab, "Prefab");
                candidateDefinitions.Add(definition);
                definitionToPrefab.Add(definition, prefab);
            }

            TopologyPlan = MapTopologyPlanner.Build(
                new MapTopologyBuildRequest(
                    runSeed,
                    additionalChunkCount,
                    maxTopologyAttemptsPerChunk,
                    startingDefinition,
                    candidateDefinitions,
                    ToTopologyDirection(startingEntrySide),
                    additionalChunkCount > 0
                        ? ToTopologyDirection(startingExitSide)
                        : (MapTopologyDirection?)null));

            if (!TopologyPlan.IsComplete)
            {
                Debug.LogError(
                    $"[MapTopologyRuntime] Planner 無法完成地圖。" +
                    $"\nReason: {TopologyPlan.FailureReason}" +
                    $"\nAttempts: {TopologyPlan.AttemptsUsed}" +
                    $"\nOverlap Rejections: {TopologyPlan.RejectedOverlapCount}",
                    this);
                return false;
            }

            if (!TryInstantiateTopology(
                    startingChunk,
                    definitionToPrefab,
                    out List<MapConnector> sourceConnectors,
                    out List<MapConnector> entryConnectors))
            {
                CleanupGeneratedTopology(startingChunk);
                return false;
            }

            BuildOpenSideMasks();

            if (!connectorBlockers.TryGenerateForLayout(
                    generatedChunks,
                    openSideMasks))
            {
                CleanupGeneratedTopology(startingChunk);
                return false;
            }

            if (!runtimeNavigation.TryBuildAndPlanLayout(
                    generatedChunks,
                    sourceConnectors,
                    entryConnectors,
                    runSeed))
            {
                CleanupGeneratedTopology(startingChunk);
                return false;
            }

            spawnedChunk = generatedChunks.Count > 1
                ? generatedChunks[1]
                : null;
            SpawnedPrefab = spawnedChunk != null
                ? definitionToPrefab[TopologyPlan.Placements[1].Definition]
                : null;
            connectedFixedConnector = sourceConnectors.Count > 0
                ? sourceConnectors[0]
                : null;
            connectedMovingConnector = entryConnectors.Count > 0
                ? entryConnectors[0]
                : null;

            Debug.Log(
                $"[MapTopologyRuntime] Host 多 Chunk 地圖已提交。" +
                $"\nChunk Count: {generatedChunks.Count}" +
                $"\nFinal Chunk: {TopologyPlan.FinalChunkInstanceIndex}" +
                $"\nSignature: {TopologyPlan.Signature}",
                this);
            return true;
        }
        catch (Exception exception) when (
            exception is MissingReferenceException ||
            exception is InvalidOperationException ||
            exception is ArgumentException ||
            exception is OverflowException)
        {
            CleanupGeneratedTopology(startingChunk);
            Debug.LogError(
                $"[MapTopologyRuntime] Host 地圖建立失敗。\n{exception.Message}",
                this);
            return false;
        }
    }

    /// <summary>
    /// Level 1 不生成下一個 Chunk。Host 封閉起始 Chunk 的四個 Connector，
    /// 載入既有預烘焙 NavMesh，並建立單一 Chunk 的巡邏區。
    /// </summary>
    public bool TryPrepareClosedStartingChunkForHost(
        NetworkRunner runner,
        MapChunk startingChunk,
        int runSeed)
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            runner == null ||
            !runner.IsRunning ||
            !runner.IsServer ||
            startingChunk == null)
        {
            return false;
        }

        if (connectorBlockers == null)
        {
            Debug.LogError(
                "[MapChunkConnection] Level 1 尚未指定 Connector Blockers。",
                this);
            return false;
        }

        if (!connectorBlockers.TryGenerateForClosedChunk(startingChunk))
            return false;

        if (runtimeNavigation == null)
        {
            Debug.LogWarning(
                "[MapChunkConnection] Level 1 尚未指定 Runtime Navigation；" +
                "阻擋屋已生成，但不會載入 NavMesh 或建立巡邏區。",
                this);
            return true;
        }

        return runtimeNavigation.TryLoadAndPlanSingleChunk(
            startingChunk,
            runSeed);
    }

    private void Update()
    {
        if (DevelopmentToolsPolicy.CanRunLocalMapPrototype &&
            !Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked &&
            Input.GetKeyDown(spawnKey))
        {
            SpawnAndAlign();
        }
    }

    [ContextMenu("Spawn And Align")]
    public void SpawnAndAlign()
    {
        if (!DevelopmentToolsPolicy.CanRunLocalMapPrototype)
            return;

        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "請在 Play Mode 中執行 Connector 拼接測試。",
                this);

            return;
        }

        TrySpawnAndAlign(
            fixedChunk,
            fixedExitSide,
            movingChunkPrefab,
            movingEntrySide,
            "Offline");
    }

    /// <summary>
    /// 由 Host 在本輪入口／出口固定後，生成唯一一個第二 Chunk。
    /// 目前只驗證 Host；生成物件不是 NetworkObject，不負責 Client 同步。
    /// </summary>
    public bool TrySpawnAndAlignForHost(
        NetworkRunner runner,
        MapChunk hostFixedChunk,
        ConnectorSide hostExitSide,
        int runSeed)
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            runner == null ||
            !runner.IsRunning ||
            !runner.IsServer)
        {
            return false;
        }

        System.Random random =
            new System.Random(
                unchecked(runSeed ^ 0x51ED270B));

        MapChunk selectedPrefab =
            SelectHostMovingChunkPrefab(random);

        ConnectorSide selectedEntrySide =
            randomizeMovingEntrySideOnHost
                ? AllSides[random.Next(0, AllSides.Length)]
                : movingEntrySide;

        bool alignmentPassed = TrySpawnAndAlign(
            hostFixedChunk,
            hostExitSide,
            selectedPrefab,
            selectedEntrySide,
            "Host");

        if (!alignmentPassed)
            return false;

        if (runtimeNavigation == null)
        {
            Debug.LogWarning(
                "[MapChunkConnection] 尚未指定 Runtime Navigation；" +
                "第二 Chunk 已生成，但不會建立 NavMesh 或巡邏區。",
                this);
            return true;
        }

        return runtimeNavigation.TryBuildAndPlan(
            hostFixedChunk,
            connectedFixedConnector,
            spawnedChunk,
            connectedMovingConnector,
            runSeed);
    }

    private bool TrySpawnAndAlign(
        MapChunk sourceChunk,
        ConnectorSide sourceExitSide,
        MapChunk selectedMovingChunkPrefab,
        ConnectorSide selectedMovingEntrySide,
        string modeLabel)
    {
        if (spawnedChunk != null)
        {
            Debug.LogWarning(
                "第二個測試區塊已經生成；本輪不會再次生成。",
                spawnedChunk);

            return false;
        }

        if (sourceChunk == null || selectedMovingChunkPrefab == null)
        {
            Debug.LogError(
                "Fixed Chunk 或 Moving Chunk Prefab 尚未指定。",
                this);

            return false;
        }

        try
        {
            MapConnector fixedExit =
                sourceChunk.GetConnector(sourceExitSide);

            if (!sourceChunk.gameObject.scene.IsValid() ||
                !sourceChunk.gameObject.scene.isLoaded)
            {
                throw new InvalidOperationException(
                    $"{sourceChunk.name} 不是已載入 Scene 中的 Chunk 實例。" +
                    "Fixed Chunk 不可指定 Prefab 資產。");
            }

            spawnedChunk = Instantiate(
                selectedMovingChunkPrefab,
                Vector3.zero,
                Quaternion.identity);
            SpawnedPrefab = selectedMovingChunkPrefab;

            // Fusion 以 Additive Scene 載入 Game 時，Active Scene 可能仍是 Menu。
            // Instantiate 預設會落在 Active Scene，因此必須明確跟隨固定 Chunk，
            // 避免 Menu 卸載時把執行期地圖一起移除。
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                spawnedChunk.gameObject,
                sourceChunk.gameObject.scene);

            // Multiple Peer 模式中，Fusion 只會隨 [Game] 根卸載子物件。
            // 生成 Chunk 必須立刻沿用固定 Chunk 的父層；不能只停留在同一 Unity Scene。
            if (sourceChunk.transform.parent != null)
            {
                spawnedChunk.transform.SetParent(
                    sourceChunk.transform.parent,
                    true);
            }

            spawnedChunk.name =
                $"{selectedMovingChunkPrefab.name}_{modeLabel}_Aligned";

            MapConnector movingEntry =
                spawnedChunk.GetConnector(selectedMovingEntrySide);

            AlignChunk(
                spawnedChunk,
                movingEntry,
                fixedExit);

            bool passed = ValidateAlignment(
                movingEntry,
                fixedExit);

            if (passed)
            {
                connectedFixedConnector = fixedExit;
                connectedMovingConnector = movingEntry;
            }

            if (passed)
            {
                if (connectorBlockers != null)
                {
                    connectorBlockers.TryGenerateForConnection(
                        sourceChunk,
                        fixedExit,
                        spawnedChunk,
                        movingEntry);
                }

                Debug.Log(
                    $"[MapChunkConnection] {modeLabel} 第二個 Chunk 已生成。" +
                    $"\nPrefab: {selectedMovingChunkPrefab.name}" +
                    $"\nFixed Exit: {sourceExitSide}" +
                    $"\nMoving Entry: {selectedMovingEntrySide}",
                    spawnedChunk);
            }

            return passed;
        }
        catch (Exception exception) when (
            exception is MissingReferenceException ||
            exception is InvalidOperationException ||
            exception is ArgumentException)
        {
            if (spawnedChunk != null)
            {
                Destroy(spawnedChunk.gameObject);
                spawnedChunk = null;
            }

            Debug.LogError(
                $"[MapChunkConnection] 第二個 Chunk 生成失敗。\n" +
                exception.Message,
                this);

            return false;
        }
    }

    private List<MapChunk> CollectCandidatePrefabs()
    {
        var result = new List<MapChunk>();

        if (hostMovingChunkPrefabs != null)
        {
            foreach (MapChunk candidate in hostMovingChunkPrefabs)
            {
                if (candidate != null && !result.Contains(candidate))
                    result.Add(candidate);
            }
        }

        if (result.Count == 0 && movingChunkPrefab != null)
            result.Add(movingChunkPrefab);

        if (result.Count == 0)
        {
            throw new MissingReferenceException(
                "Phase 4-B 沒有任何有效的 Host Moving Chunk Prefab。");
        }

        return result;
    }

    private MapChunkTopologyDefinition CreateTopologyDefinition(
        MapChunk chunk,
        string idPrefix)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        if (topologyUnitsPerMeter <= 0)
            throw new InvalidOperationException("Topology Units Per Meter 必須大於零。");

        var connectors = new List<MapTopologyConnector>(AllSides.Length);
        int minimumX = int.MaxValue;
        int minimumZ = int.MaxValue;
        int maximumX = int.MinValue;
        int maximumZ = int.MinValue;

        foreach (ConnectorSide side in AllSides)
        {
            MapConnector connector = chunk.GetConnector(side);
            Vector3 local = chunk.transform.InverseTransformPoint(
                connector.transform.position);
            int x = Mathf.RoundToInt(local.x * topologyUnitsPerMeter);
            int z = Mathf.RoundToInt(local.z * topologyUnitsPerMeter);

            connectors.Add(
                new MapTopologyConnector(
                    ToTopologyDirection(side),
                    new MapTopologyPoint(x, z)));
            minimumX = Mathf.Min(minimumX, x);
            minimumZ = Mathf.Min(minimumZ, z);
            maximumX = Mathf.Max(maximumX, x);
            maximumZ = Mathf.Max(maximumZ, z);
        }

        if (minimumX >= maximumX || minimumZ >= maximumZ)
        {
            throw new InvalidOperationException(
                $"{chunk.name} 的 Connector 無法形成有效矩形拓撲邊界。");
        }

        return new MapChunkTopologyDefinition(
            $"{idPrefix}:{chunk.name}",
            new MapTopologyRect(minimumX, minimumZ, maximumX, maximumZ),
            connectors);
    }

    private bool TryInstantiateTopology(
        MapChunk startingChunk,
        IReadOnlyDictionary<MapChunkTopologyDefinition, MapChunk> definitionToPrefab,
        out List<MapConnector> sourceConnectors,
        out List<MapConnector> entryConnectors)
    {
        sourceConnectors = new List<MapConnector>();
        entryConnectors = new List<MapConnector>();
        generatedChunks.Clear();
        generatedChunks.Add(startingChunk);

        for (int index = 1; index < TopologyPlan.Placements.Count; index++)
        {
            MapTopologyPlacement previous = TopologyPlan.Placements[index - 1];
            MapTopologyPlacement placement = TopologyPlan.Placements[index];

            if (!previous.LocalExitDirection.HasValue ||
                !definitionToPrefab.TryGetValue(placement.Definition, out MapChunk prefab))
            {
                return false;
            }

            MapChunk sourceChunk = generatedChunks[index - 1];
            MapConnector sourceExit = sourceChunk.GetConnector(
                ToConnectorSide(previous.LocalExitDirection.Value));
            MapChunk created = Instantiate(prefab, Vector3.zero, Quaternion.identity);

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                created.gameObject,
                startingChunk.gameObject.scene);

            if (startingChunk.transform.parent != null)
                created.transform.SetParent(startingChunk.transform.parent, true);

            created.name = $"{prefab.name}_Host_Topology_{index}";
            MapConnector createdEntry = created.GetConnector(
                ToConnectorSide(placement.LocalEntryDirection));

            AlignChunk(created, createdEntry, sourceExit);

            if (!ValidateAlignment(createdEntry, sourceExit))
            {
                Destroy(created.gameObject);
                return false;
            }

            generatedChunks.Add(created);
            sourceConnectors.Add(sourceExit);
            entryConnectors.Add(createdEntry);
        }

        return true;
    }

    private void BuildOpenSideMasks()
    {
        openSideMasks.Clear();

        for (int index = 0; index < TopologyPlan.Placements.Count; index++)
        {
            MapTopologyPlacement placement = TopologyPlan.Placements[index];
            int mask = index > 0
                ? 1 << (int)ToConnectorSide(placement.LocalEntryDirection)
                : 0;

            if (placement.LocalExitDirection.HasValue)
                mask |= 1 << (int)ToConnectorSide(placement.LocalExitDirection.Value);

            openSideMasks.Add(mask);
        }
    }

    private void CleanupGeneratedTopology(MapChunk startingChunk)
    {
        for (int index = generatedChunks.Count - 1; index >= 0; index--)
        {
            MapChunk chunk = generatedChunks[index];
            if (chunk != null && chunk != startingChunk)
                Destroy(chunk.gameObject);
        }

        generatedChunks.Clear();
        openSideMasks.Clear();
    }

    private static MapTopologyDirection ToTopologyDirection(ConnectorSide side)
    {
        return (MapTopologyDirection)(int)side;
    }

    private static ConnectorSide ToConnectorSide(MapTopologyDirection direction)
    {
        return (ConnectorSide)(int)direction;
    }

    private MapChunk SelectHostMovingChunkPrefab(
        System.Random random)
    {
        int validCount = 0;

        if (hostMovingChunkPrefabs != null)
        {
            foreach (MapChunk candidate in hostMovingChunkPrefabs)
            {
                if (candidate != null)
                    validCount++;
            }
        }

        if (validCount <= 0)
            return movingChunkPrefab;

        int selectedValidIndex =
            random.Next(0, validCount);

        foreach (MapChunk candidate in hostMovingChunkPrefabs)
        {
            if (candidate == null)
                continue;

            if (selectedValidIndex == 0)
                return candidate;

            selectedValidIndex--;
        }

        return movingChunkPrefab;
    }

    private static void AlignChunk(
        MapChunk movingChunk,
        MapConnector movingEntry,
        MapConnector fixedExit)
    {
        // 入口必須面向出口：
        // Entry.forward = -Exit.forward
        // Entry.up      =  Exit.up
        Quaternion desiredEntryRotation =
            Quaternion.LookRotation(
                -fixedExit.transform.forward,
                fixedExit.transform.up);

        Quaternion rotationDelta =
            desiredEntryRotation *
            Quaternion.Inverse(movingEntry.transform.rotation);

        movingChunk.transform.rotation =
            rotationDelta *
            movingChunk.transform.rotation;

        // 旋轉完成後，Entry 的世界座標已經改變，
        // 因此必須在旋轉後重新計算位移。
        Vector3 positionDelta =
            fixedExit.transform.position -
            movingEntry.transform.position;

        movingChunk.transform.position += positionDelta;
    }

    private bool ValidateAlignment(
        MapConnector movingEntry,
        MapConnector fixedExit)
    {
        float positionError = Vector3.Distance(
            fixedExit.transform.position,
            movingEntry.transform.position);

        float forwardAngleError = Vector3.Angle(
            fixedExit.transform.forward,
            -movingEntry.transform.forward);

        float upAngleError = Vector3.Angle(
            fixedExit.transform.up,
            movingEntry.transform.up);

        bool passed =
            positionError <= positionTolerance &&
            forwardAngleError <= angleTolerance &&
            upAngleError <= angleTolerance;

        string result = passed ? "PASS" : "FAIL";

        Debug.Log(
            $"Connector Alignment {result}\n" +
            $"Position Error: {positionError:F6}\n" +
            $"Forward Angle Error: {forwardAngleError:F4}°\n" +
            $"Up Angle Error: {upAngleError:F4}°",
            movingEntry);

        return passed;
    }

    private void OnValidate()
    {
        topologyUnitsPerMeter = Mathf.Max(1, topologyUnitsPerMeter);
        maxTopologyAttemptsPerChunk = Mathf.Max(1, maxTopologyAttemptsPerChunk);
        positionTolerance = Mathf.Max(0f, positionTolerance);
        angleTolerance = Mathf.Max(0f, angleTolerance);
    }
}
