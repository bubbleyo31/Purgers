#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Fusion;
using Purgers.GameFlow.Stage;
using Purgers.Map;
using Purgers.Progression;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Explicit two-process Phase 4-D probe. Host starts Level 3 first; Client joins
/// after the configured delay and must reconstruct the authoritative snapshot.
/// </summary>
public sealed class Phase4DNetworkVerification : MonoBehaviour
{
    private const int ExpectedStageLevel = 3;
    private const int ExpectedChunkCount = 3;

    private NetworkRunner runner;
    private string role;
    private string outputDirectory;
    private float startedAt;
    private float joinAt;
    private bool startRequested;
    private bool verified;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--phase4d-role");
        if (index < 0 || index + 4 >= arguments.Length)
            return;

        MultiClimb.Menu.MenuConnectionBehaviour menu =
            FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if (menu == null)
            return;

        NetworkRunner prefab =
            (NetworkRunner)typeof(MultiClimb.Menu.MenuConnectionBehaviour)
                .GetField(
                    "networkRunnerPrefab",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(menu);

        string requestedRole = arguments[index + 1];
        string session = arguments[index + 2];
        string output = arguments[index + 3];
        float delay = float.Parse(
            arguments[index + 4],
            CultureInfo.InvariantCulture);

        GameObject probeObject = new GameObject("Phase4DVerification");
        DontDestroyOnLoad(probeObject);
        probeObject.AddComponent<Phase4DNetworkVerification>()
            .Initialize(requestedRole, session, output, delay, prefab);
    }

    private void Initialize(
        string requestedRole,
        string session,
        string output,
        float delay,
        NetworkRunner prefab)
    {
        role = requestedRole;
        outputDirectory = Path.GetFullPath(output);
        Directory.CreateDirectory(outputDirectory);
        startedAt = Time.unscaledTime;
        joinAt = startedAt + Mathf.Max(0f, delay);
        Application.runInBackground = true;
        StartCoroutine(StartWhenDue(session, prefab));
    }

    private System.Collections.IEnumerator StartWhenDue(
        string session,
        NetworkRunner prefab)
    {
        while (Time.unscaledTime < joinAt)
            yield return null;

        StartRunner(session, prefab);
    }

    private async void StartRunner(string session, NetworkRunner prefab)
    {
        if (startRequested)
            return;

        startRequested = true;
        try
        {
            runner = Instantiate(prefab);
            runner.ProvideInput = true;
            GameSaveRuntimeContext context =
                runner.gameObject.AddComponent<GameSaveRuntimeContext>();

            if (role == "Host")
            {
                var repository = new JsonGameSaveRepository(
                    Path.Combine(outputDirectory, "Saves"));
                var created =
                    repository.CreateNew("Phase 4-D isolated verification");
                if (!created.Success)
                    throw new InvalidOperationException(created.Error);

                created.Value.RunProgression.StageLevel = ExpectedStageLevel;
                context.InitializeHost(repository, created.Value);
            }
            else
            {
                context.InitializeClientReadOnly();
            }

            var manager =
                runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            manager.IsSceneTakeOverEnabled = false;
            var scene = new NetworkSceneInfo();
            scene.AddSceneRef(SceneRef.FromIndex(2), LoadSceneMode.Additive);

            var settings = new Fusion.Photon.Realtime.FusionAppSettings();
            Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings
                .CopyTo(settings);
            settings.FixedRegion = "asia";

            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                CustomPhotonAppSettings = settings,
                GameMode = role == "Host" ? GameMode.Host : GameMode.Client,
                SessionName = session,
                Scene = role == "Host" ? scene : (NetworkSceneInfo?)null,
                SceneManager = manager,
                PlayerCount = 2
            });

            Log($"StartGame={result.Ok}, reason={result.ShutdownReason}, delay={joinAt - startedAt:F1}");
            if (!result.Ok)
            {
                FailAndQuit("Runner 啟動失敗。");
                return;
            }

            Fusion.Menu.FusionMenuUIMain main =
                FindObjectOfType<Fusion.Menu.FusionMenuUIMain>(true);
            if (main != null)
                main.gameObject.SetActive(false);
        }
        catch (Exception exception)
        {
            FailAndQuit(exception.ToString());
        }
    }

    private void Update()
    {
        if (verified)
        {
            if (role == "Host" &&
                File.Exists(Path.Combine(outputDirectory, "Client.complete")))
            {
                Log("PASS 獨立 Client 完成晚加入驗證。");
                Application.Quit(0);
            }
            return;
        }

        if (Time.unscaledTime - startedAt > 300f)
        {
            FailAndQuit("逾時，未取得可驗證的完整 NetworkMapState。");
            return;
        }

        if (runner == null || !runner.IsRunning)
            return;

        NetworkMapState map = FindObjectsOfType<NetworkMapState>()
            .FirstOrDefault(candidate => candidate.Runner == runner);
        if (map == null || !map.IsReady || !map.HasExplorationSnapshot)
            return;

        StageFlowController stage = map.GetComponent<StageFlowController>();
        if (stage == null || stage.Phase != StagePhase.Active ||
            !stage.TryGetSelectedExtraction(out StageExtractionPoint extraction))
        {
            return;
        }

        Verify(map, stage, extraction);
    }

    private void Verify(
        NetworkMapState map,
        StageFlowController stage,
        StageExtractionPoint extraction)
    {
        try
        {
            Require(map.ChunkCount == ExpectedChunkCount,
                $"ChunkCount 預期 {ExpectedChunkCount}，實際 {map.ChunkCount}。");
            Require(map.Chunks.Count == map.ChunkCount,
                "本機 Chunk 清單與權威 ChunkCount 不一致。");
            Require(map.FinalChunkIndex == map.ChunkCount - 1,
                "FinalChunkIndex 不是唯一末端 Chunk。");
            Require(stage.SelectedExtractionIndex == map.SelectedExtractionIndex,
                "StageFlow 與 NetworkMapState 的撤離選擇不一致。");
            Require(extraction.OwnerChunk == map.Chunks[map.FinalChunkIndex],
                "撤離點不屬於最後 Chunk。");

            MapChunkPlacement[] placements = new MapChunkPlacement[map.ChunkCount];
            for (int index = 0; index < placements.Length; index++)
                placements[index] = map.Placements[index];
            Require(
                MapLayoutRules.TryValidateLinearTopology(
                    placements,
                    map.ChunkCount,
                    map.FinalChunkIndex,
                    out string topologyFailure),
                topologyFailure);

            MapConnectorBlockerPrototype blockers =
                FindObjectsOfType<MapConnectorBlockerPrototype>()
                    .First(candidate => candidate.gameObject.scene == map.gameObject.scene);
            Require(blockers.SpawnedBlockerCount == map.ChunkCount * 2 + 2,
                "封口數量不符合線性 N Chunk 拓撲。");

            foreach (MapChunk chunk in map.Chunks)
            {
                Require(chunk != null && chunk.Cartography != null && chunk.Cartography.IsValid,
                    "本機 Chunk 缺少有效 Cartography。");
                Require(chunk.GetComponentsInChildren<Collider>(true)
                        .Any(collider => collider.enabled && !collider.isTrigger),
                    "本機 Chunk 沒有啟用的實體 Collider。");
            }

            string snapshot = BuildSnapshot(map, stage, extraction.PointId);
            if (role == "Host")
                VerifyHost(map, snapshot);
            else
                VerifyLateClient(map, snapshot);
        }
        catch (Exception exception)
        {
            FailAndQuit(exception.Message);
        }
    }

    private void VerifyHost(NetworkMapState map, string snapshot)
    {
        ConnectorAlignmentPrototype alignment = FindObjectOfType<MapRunSelectionPrototype>()
            .Alignment;
        Require(alignment != null && alignment.RuntimeNavigation != null,
            "Host 缺少 Runtime Navigation owner。");
        Require(alignment.RuntimeNavigation.IsReady,
            "Host Runtime Navigation 未就緒。");
        Require(alignment.RuntimeNavigation.GeneratedGroundAreas.Count == map.ChunkCount,
            "Host 每 Chunk 巡邏區數量不一致。");
        Require(map.Chunks.Sum(chunk =>
                    chunk.GetComponentsInChildren<NavMeshSurface>(true)
                        .Count(surface => surface.enabled)) >= map.ChunkCount,
            "Host 未保留每 Chunk 的 NavMeshSurface。");

        File.WriteAllText(
            Path.Combine(outputDirectory, "Host.snapshot"),
            snapshot);
        Log("PASS Host 完整三 Chunk 拓撲、封口、最後 Chunk、撤離與導航權威。");
        verified = true;
    }

    private void VerifyLateClient(NetworkMapState map, string snapshot)
    {
        string hostSnapshotPath = Path.Combine(outputDirectory, "Host.snapshot");
        if (!File.Exists(hostSnapshotPath))
            throw new InvalidOperationException("找不到 Host 拓撲快照。");

        string hostSnapshot = File.ReadAllText(hostSnapshotPath);
        Require(string.Equals(hostSnapshot, snapshot, StringComparison.Ordinal),
            "晚加入 Client 的拓撲／最後 Chunk／撤離選擇與 Host 不一致。\n" +
            "Host=" + hostSnapshot + "\nClient=" + snapshot);

        ConnectorAlignmentPrototype alignment = FindObjectOfType<MapRunSelectionPrototype>()
            .Alignment;
        Require(alignment != null && alignment.RuntimeNavigation != null,
            "Client 缺少 Runtime Navigation 元件以供權威隔離檢查。");
        Require(!alignment.RuntimeNavigation.IsReady &&
                alignment.RuntimeNavigation.GeneratedGroundAreas.Count == 0,
            "Client 不應建立 AI 導航或巡邏區。");
        Require(map.Chunks.All(chunk =>
                    chunk.GetComponentsInChildren<NavMeshSurface>(true)
                        .All(surface => !surface.enabled)),
            "Client Replica 仍有啟用的 NavMeshSurface。");

        Log("PASS 晚加入獨立 Client 重建相同拓撲、Collider、Cartography、封口、最後 Chunk 與撤離選擇；未取得 AI 導航權威。");
        File.WriteAllText(
            Path.Combine(outputDirectory, "Client.complete"),
            "PASS");
        verified = true;
        Application.Quit(0);
    }

    private static string BuildSnapshot(
        NetworkMapState map,
        StageFlowController stage,
        string extractionPointId)
    {
        var builder = new StringBuilder();
        builder.Append(map.LayoutRevision)
            .Append('|').Append(map.ChunkCount)
            .Append('|').Append(map.FinalChunkIndex)
            .Append('|').Append(stage.SelectedExtractionIndex)
            .Append('|').Append(extractionPointId);

        for (int index = 0; index < map.ChunkCount; index++)
        {
            MapChunkPlacement placement = map.Placements[index];
            builder.Append('|').Append(index)
                .Append(':').Append(placement.CatalogIndex)
                .Append(',').Append(placement.ContentVersion)
                .Append(',').Append(placement.ConnectedFromIndex)
                .Append(',').Append(placement.EntrySide)
                .Append(',').Append(placement.ExitSide)
                .Append(',').Append(placement.OpenSides)
                .Append('@').Append(placement.Position.x.ToString("F3", CultureInfo.InvariantCulture))
                .Append(',').Append(placement.Position.y.ToString("F3", CultureInfo.InvariantCulture))
                .Append(',').Append(placement.Position.z.ToString("F3", CultureInfo.InvariantCulture))
                .Append(',').Append(placement.Rotation.x.ToString("F4", CultureInfo.InvariantCulture))
                .Append(',').Append(placement.Rotation.y.ToString("F4", CultureInfo.InvariantCulture))
                .Append(',').Append(placement.Rotation.z.ToString("F4", CultureInfo.InvariantCulture))
                .Append(',').Append(placement.Rotation.w.ToString("F4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private void FailAndQuit(string message)
    {
        Log("FAIL " + message);
        verified = true;
        File.WriteAllText(
            Path.Combine(outputDirectory, role + ".failed"),
            message);
        Application.Quit(1);
    }

    private void Log(string message)
    {
        string line = DateTime.UtcNow.ToString("O") + " " + message;
        File.AppendAllText(
            Path.Combine(outputDirectory, role + ".log"),
            line + Environment.NewLine);
        Debug.Log("[Phase4DVerification] " + message);
    }
}
#endif
