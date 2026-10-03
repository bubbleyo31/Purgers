using System;
using UnityEngine;
using Fusion;
using Purgers.GameFlow.Stage;
using Purgers.Progression;

public enum PlacementMode
{
    Marker = 0,
    FusionInitialSpawn = 1
}


[DisallowMultipleComponent]
public sealed class MapRunSelectionPrototype : MonoBehaviour
{
    private static readonly ConnectorSide[] AllSides =
    {
        ConnectorSide.North,
        ConnectorSide.East,
        ConnectorSide.South,
        ConnectorSide.West
    };

    [Header("場景參考")]

    [Tooltip(
        "本輪遊戲開始時使用的第一個 Map Chunk。")]
    [SerializeField]
    private MapChunk startingChunk;

    [Tooltip(
        "Boss 關專用 MapChunk Prefab。只由 StageRuntimePlan 的 DedicatedBossChunk route 使用；" +
        "Prefab 不可包含 NetworkObject，且必須具備四向 Connector、出生點、Cartography 與預烘焙 NavMesh。")]
    [SerializeField]
    private MapChunk bossChunkPrefab;

    [Tooltip(
        "用來驗證出生位置的非網路測試物件。" +
        "目前不要指定正式的 Fusion Player NetworkObject。")]
    [SerializeField]
    private Transform prototypePlayerMarker;

    [Tooltip(
        "Host 固定本輪出口後，用來生成並對齊唯一一個第二 Chunk 的元件。" +
        "只在 Fusion Initial Spawn 模式使用；留空時只抽選入口／出口，不生成下一個 Chunk。")]
    [SerializeField]
    private ConnectorAlignmentPrototype nextChunkAlignment;


    [Header("決定性抽選")]

    [Tooltip(
        "入口與出口抽選使用的固定 Seed。" +
        "Marker 模式，以及關閉 Host 每輪隨機 Seed 時使用。" +
        "相同 Seed 應得到相同的入口與出口，方便重現問題。")]
    [SerializeField]
    private int seed = 12345;

    [Tooltip(
        "啟用後，每個新的 Host Runner 會建立一次新的 Run Seed，" +
        "因此每次進入遊戲都會重新抽選四個入口之一與另一個出口。" +
        "同一輪死亡重生仍沿用已選入口。")]
    [SerializeField]
    private bool randomizeRunSeedOnHost = true;

    [Tooltip(
        "啟用後，在進入 Play Mode 時自動選擇入口、出口並放置測試角色。")]
    [SerializeField]
    private bool initializeOnStart = true;

    [Tooltip(
        "Play Mode 中使用下一個 Seed 重新抽選入口與出口的按鍵。")]
    [SerializeField]
    private KeyCode rerollKey = KeyCode.F9;


    [SerializeField]
    [Tooltip(
        "Marker：僅在無 NetworkRunner 的離線 Play Mode 移動測試標記。" +
        "FusionInitialSpawn：由 Host 的 GameLogic 取得首次出生座標；不移動現有玩家。")]
    private PlacementMode placementMode = PlacementMode.Marker;

    [Tooltip(
        "啟用後，Host 第一次固定本輪入口／出口時，依 Phase 4-A 拓撲生成" +
        "StageRules 要求的完整 Chunk 鏈。Level 1／5 不新增、Level 2 新增一塊、" +
        "Level 3 新增兩塊；Boss 關不走此流程。")]
    [SerializeField]
    private bool spawnSecondChunkOnHost = true;

    private NetworkRunner selectionRunner;
    private bool selectionAttempted;
    private bool selectionReady;
    private bool mapPreparationReady;
    private Pose initialSpawnPose;
    private int activeRunSeed;
    private MapChunk activeStartingChunk;

    public ConnectorSide CurrentEntrySide { get; private set; }

    public ConnectorSide CurrentExitSide { get; private set; }

    public int ActiveRunSeed =>
        activeRunSeed;

    public bool IsSelectionReady =>
        selectionReady;

    public bool IsMapPreparationReady =>
        selectionReady && mapPreparationReady;

    public MapChunk StartingChunk => activeStartingChunk != null
        ? activeStartingChunk
        : startingChunk;
    public ConnectorAlignmentPrototype Alignment => nextChunkAlignment;
    public StageRuntimePlan RuntimePlan { get; private set; }

    private void Start()
    {
        if (initializeOnStart)
        {
            InitializeSelection(seed);
        }
    }

    private void Update()
    {
        if (placementMode != PlacementMode.Marker ||
            !DevelopmentToolsPolicy.CanRunLocalMapPrototype ||
            Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked ||
            !Input.GetKeyDown(rerollKey))
            return;

        seed++;
        InitializeSelection(seed);
    }

    public bool TryGetInitialSpawnPose(NetworkRunner runner, out Pose pose)
    {
        pose = default;

        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            placementMode != PlacementMode.FusionInitialSpawn ||
            runner == null ||
            !runner.IsRunning ||
            !runner.IsServer)
            return false;

        if (selectionRunner != runner)
        {
            selectionRunner = runner;
            selectionAttempted = false;
            selectionReady = false;
            mapPreparationReady = false;
            activeStartingChunk = startingChunk;
            activeRunSeed = randomizeRunSeedOnHost
                ? CreateRunSeed()
                : seed;
        }

        // 同一 Runner 只決定一次，避免晚加入者使用不同入口。
        // 設定失敗也固定回退；修正後重新啟動測試。
        if (!selectionAttempted)
        {
            selectionAttempted = true;

            RuntimePlan = StageRules.ResolveRuntimePlan(
                ResolveStageLevel(runner),
                ResolveCycleLength(runner));

            if (!TryPrepareStartingChunkForPlan(RuntimePlan))
                return false;

            selectionReady = TrySelectSpawnPose(
                activeRunSeed,
                out initialSpawnPose);

            if (selectionReady)
            {
                Debug.Log(
                    $"[MapRunSelection] Host 入口已固定。" +
                    $"\nRun Seed: {activeRunSeed}" +
                    $"\nEntry: {CurrentEntrySide}, Exit: {CurrentExitSide}" +
                    $"\nPosition: {initialSpawnPose.position}",
                    this);

                if (RuntimePlan.MapRoute == StageMapRoute.Unsupported)
                {
                    Debug.LogError(
                        "[MapRunSelection] 普通關最多支援兩個新增 Chunk；" +
                        $"StageLevel {RuntimePlan.StageLevel}、CycleLength " +
                        $"{RuntimePlan.CycleLength} 要求 {RuntimePlan.AdditionalChunkCount}。",
                        this);
                }
                else if (nextChunkAlignment == null)
                {
                    Debug.LogWarning(
                        "[MapRunSelection] 尚未指定 Next Chunk Alignment，" +
                        "無法建立 Phase 4-B 地圖。",
                        this);
                }
                else if (RuntimePlan.MapRoute == StageMapRoute.OrdinaryTopology &&
                         RuntimePlan.AdditionalChunkCount > 0 &&
                         !spawnSecondChunkOnHost)
                {
                    Debug.LogWarning(
                        "[MapRunSelection] Host 多 Chunk 生成已停用，" +
                        "Level 2～3 地圖不會進入 Ready。",
                        this);
                }
                else
                {
                    mapPreparationReady =
                        nextChunkAlignment.TryBuildTopologyForHost(
                            runner,
                            StartingChunk,
                            CurrentEntrySide,
                            CurrentExitSide,
                            activeRunSeed,
                            RuntimePlan.AdditionalChunkCount);
                }

                if (!mapPreparationReady)
                {
                    Debug.LogError(
                        "[MapRunSelection] 入口抽選成功，但本輪地圖準備失敗；" +
                        "StageFlow 不會開始計時。",
                        this);
                }
            }
        }

        pose = initialSpawnPose;
        return selectionReady;
    }

    public bool TryAdoptReplicaStartingChunk(MapChunk replacement)
    {
        if (replacement == null || startingChunk == null)
            return false;

        if (activeStartingChunk != null &&
            activeStartingChunk != startingChunk &&
            activeStartingChunk != replacement)
        {
            Destroy(activeStartingChunk.gameObject);
        }

        startingChunk.gameObject.SetActive(false);
        activeStartingChunk = replacement;
        return true;
    }

    private bool TryPrepareStartingChunkForPlan(StageRuntimePlan plan)
    {
        activeStartingChunk = startingChunk;

        if (plan.MapRoute != StageMapRoute.DedicatedBossChunk)
            return true;

        if (startingChunk == null || bossChunkPrefab == null)
        {
            Debug.LogError(
                "[MapRunSelection] Boss route 缺少 Starting Chunk 或 Boss Chunk Prefab。",
                this);
            return false;
        }

        if (bossChunkPrefab.GetComponentInChildren<NetworkObject>(true) != null)
        {
            Debug.LogError(
                "[MapRunSelection] Boss Chunk Prefab 不可包含 NetworkObject；" +
                "Boss 敵人必須由 State Authority 另行 Spawn。",
                bossChunkPrefab);
            return false;
        }

        MapChunk created = Instantiate(
            bossChunkPrefab,
            startingChunk.transform.position,
            startingChunk.transform.rotation,
            startingChunk.transform.parent);

        if (created.gameObject.scene != startingChunk.gameObject.scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                created.gameObject,
                startingChunk.gameObject.scene);
        }

        created.name = bossChunkPrefab.name + "_Host_Boss";
        startingChunk.gameObject.SetActive(false);
        activeStartingChunk = created;
        return true;
    }

    private static int ResolveStageLevel(NetworkRunner runner)
    {
        GameSaveRuntimeContext context =
            runner.GetComponent<GameSaveRuntimeContext>();

        return context?.ActiveSave?.RunProgression != null
            ? Mathf.Max(1, context.ActiveSave.RunProgression.StageLevel)
            : 1;
    }

    private static int ResolveCycleLength(NetworkRunner runner)
    {
        GameSaveRuntimeContext context =
            runner.GetComponent<GameSaveRuntimeContext>();

        return context?.ActiveSave != null
            ? Mathf.Max(1, context.ActiveSave.CycleLengthSnapshot)
            : GameSaveSchema.DefaultCycleLength;
    }

    private static int CreateRunSeed()
    {
        unchecked
        {
            return Guid.NewGuid().GetHashCode() ^
                   Environment.TickCount;
        }
    }

    [ContextMenu("Initialize Selection")]
    private void InitializeSelectionFromContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "請在 Play Mode 中執行入口與出口抽選。",
                this);

            return;
        }

        InitializeSelection(seed);
    }

    public void InitializeSelection(int selectedSeed)
    {
        if (placementMode != PlacementMode.Marker ||
            !DevelopmentToolsPolicy.CanRunLocalMapPrototype)
            return;

        if (prototypePlayerMarker == null)
        {
            Debug.LogError("Prototype Player Marker 尚未指定。", this);
            return;
        }

        // 避免誤把網路玩家或其父子物件指定為 Marker。
        if (prototypePlayerMarker.GetComponentInParent<NetworkObject>() != null ||
            prototypePlayerMarker.GetComponentInChildren<NetworkObject>(true) != null)
        {
            Debug.LogError("Marker 不可包含或隸屬 NetworkObject。", this);
            return;
        }

        if (!TrySelectSpawnPose(selectedSeed, out Pose pose))
            return;

        ResetConnectorRoles();

        StartingChunk.GetConnector(CurrentEntrySide)
            .SetPrototypeRole(ConnectorPrototypeRole.Entrance);

        StartingChunk.GetConnector(CurrentExitSide)
            .SetPrototypeRole(ConnectorPrototypeRole.Exit);

        prototypePlayerMarker.SetPositionAndRotation(
            pose.position, pose.rotation);

        Debug.Log(
            $"[MapRunSelection] Marker 已放置。" +
            $"\nSeed: {selectedSeed}, Entry: {CurrentEntrySide}, Exit: {CurrentExitSide}",
            this);
    }

    private bool TrySelectSpawnPose(int selectedSeed, out Pose pose)
    {
        pose = default;

        MapChunk runtimeStartingChunk = StartingChunk;
        if (runtimeStartingChunk == null ||
            !runtimeStartingChunk.gameObject.activeInHierarchy ||
            runtimeStartingChunk.gameObject.scene != gameObject.scene)
        {
            Debug.LogWarning(
                "[MapRunSelection] Starting Chunk 無效或不在同一場景；使用既有出生流程。",
                this);
            return false;
        }

        try
        {
            // GetConnector 會對缺少或重複方向拋出例外。
            // 完整驗證後才提交本次結果。
            foreach (ConnectorSide side in AllSides)
                runtimeStartingChunk.GetConnector(side);

            var random = new System.Random(selectedSeed);
            int entryIndex = random.Next(0, AllSides.Length);
            int exitIndex = random.Next(0, AllSides.Length - 1);

            if (exitIndex >= entryIndex)
                exitIndex++;

            var entrance = runtimeStartingChunk.GetConnector(AllSides[entryIndex]);
            Transform spawnPoint = entrance.PlayerSpawnPoint;

            if (spawnPoint == null || !spawnPoint.gameObject.activeInHierarchy)
            {
                Debug.LogWarning(
                    "[MapRunSelection] 抽中入口缺少有效 PlayerSpawnPoint；使用既有出生流程。",
                    entrance);
                return false;
            }

            pose = new Pose(spawnPoint.position, spawnPoint.rotation);
            CurrentEntrySide = AllSides[entryIndex];
            CurrentExitSide = AllSides[exitIndex];
            return true;
        }
        catch (Exception exception) when (
            exception is MissingReferenceException ||
            exception is InvalidOperationException)
        {
            Debug.LogWarning(
                $"[MapRunSelection] Connector 設定無效；使用既有出生流程。\n{exception.Message}",
                this);
            return false;
        }
    }

    private void ResetConnectorRoles()
    {
        MapChunk runtimeStartingChunk = StartingChunk;
        foreach (ConnectorSide side in AllSides)
        {
            MapConnector connector =
                runtimeStartingChunk.GetConnector(side);

            connector.SetPrototypeRole(
                ConnectorPrototypeRole.None);
        }
    }
}
