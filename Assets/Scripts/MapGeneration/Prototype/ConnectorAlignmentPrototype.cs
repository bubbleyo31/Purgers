using System;
using Fusion;
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

    [Header("Chunk References")]

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


    [Header("Connection Test")]

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


    [Header("Validation")]

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

    public MapChunk SpawnedChunk =>
        spawnedChunk;

    private void Update()
    {
        if (DevelopmentToolsPolicy.CanRunLocalMapPrototype &&
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
        if (!DevelopmentToolsPolicy.IsEnabled ||
            !Application.isPlaying ||
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

            // Fusion 以 Additive Scene 載入 Game 時，Active Scene 可能仍是 Menu。
            // Instantiate 預設會落在 Active Scene，因此必須明確跟隨固定 Chunk，
            // 避免 Menu 卸載時把執行期地圖一起移除。
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                spawnedChunk.gameObject,
                sourceChunk.gameObject.scene);

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
            spawnedChunk);

        return passed;
    }
}
