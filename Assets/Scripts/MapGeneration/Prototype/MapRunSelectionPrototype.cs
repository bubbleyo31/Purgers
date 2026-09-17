using System;
using UnityEngine;
using Fusion;

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

    [Header("Scene References")]

    [Tooltip(
        "本輪遊戲開始時使用的第一個 Map Chunk。")]
    [SerializeField]
    private MapChunk startingChunk;

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


    [Header("Deterministic Selection")]

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
        "啟用後，Host 第一次固定本輪入口／出口時自動生成一個第二 Chunk。" +
        "本選項不會生成第三個 Chunk，也不負責 Client 同步。")]
    [SerializeField]
    private bool spawnSecondChunkOnHost = true;

    private NetworkRunner selectionRunner;
    private bool selectionAttempted;
    private bool selectionReady;
    private Pose initialSpawnPose;
    private int activeRunSeed;

    public ConnectorSide CurrentEntrySide { get; private set; }

    public ConnectorSide CurrentExitSide { get; private set; }

    public int ActiveRunSeed =>
        activeRunSeed;

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
            !Input.GetKeyDown(rerollKey))
            return;

        seed++;
        InitializeSelection(seed);
    }

    public bool TryGetInitialSpawnPose(NetworkRunner runner, out Pose pose)
    {
        pose = default;

        if (!DevelopmentToolsPolicy.IsEnabled ||
            !Application.isPlaying ||
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
            activeRunSeed = randomizeRunSeedOnHost
                ? CreateRunSeed()
                : seed;
        }

        // 同一 Runner 只決定一次，避免晚加入者使用不同入口。
        // 設定失敗也固定回退；修正後重新啟動測試。
        if (!selectionAttempted)
        {
            selectionAttempted = true;
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

                if (spawnSecondChunkOnHost)
                {
                    if (nextChunkAlignment == null)
                    {
                        Debug.LogWarning(
                            "[MapRunSelection] 已啟用 Host 第二 Chunk 自動生成，" +
                            "但尚未指定 Next Chunk Alignment。",
                            this);
                    }
                    else
                    {
                        nextChunkAlignment.TrySpawnAndAlignForHost(
                            runner,
                            startingChunk,
                            CurrentExitSide,
                            activeRunSeed);
                    }
                }
            }
        }

        pose = initialSpawnPose;
        return selectionReady;
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

        startingChunk.GetConnector(CurrentEntrySide)
            .SetPrototypeRole(ConnectorPrototypeRole.Entrance);

        startingChunk.GetConnector(CurrentExitSide)
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

        if (startingChunk == null ||
            !startingChunk.gameObject.activeInHierarchy ||
            startingChunk.gameObject.scene != gameObject.scene)
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
                startingChunk.GetConnector(side);

            var random = new System.Random(selectedSeed);
            int entryIndex = random.Next(0, AllSides.Length);
            int exitIndex = random.Next(0, AllSides.Length - 1);

            if (exitIndex >= entryIndex)
                exitIndex++;

            var entrance = startingChunk.GetConnector(AllSides[entryIndex]);
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
        foreach (ConnectorSide side in AllSides)
        {
            MapConnector connector =
                startingChunk.GetConnector(side);

            connector.SetPrototypeRole(
                ConnectorPrototypeRole.None);
        }
    }
}
