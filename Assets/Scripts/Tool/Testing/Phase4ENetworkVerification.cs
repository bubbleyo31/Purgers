#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Fusion;
using Purgers.GameFlow.SafeHouse;
using Purgers.GameFlow.Stage;
using Purgers.Map;
using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 4-E two-process probe. Static mode validates one ordinary stage.
/// Cycle mode additionally commits success, observes SafeHouse, and re-enters
/// the next stage through the production scene-transition methods.
/// </summary>
public sealed class Phase4ENetworkVerification : MonoBehaviour
{
    private enum VerificationMode
    {
        Static,
        Cycle
    }

    private NetworkRunner runner;
    private JsonGameSaveRepository repository;
    private string role;
    private string outputDirectory;
    private int initialStageLevel;
    private int cycleLength;
    private VerificationMode mode;
    private float startedAt;
    private float joinAt;
    private bool startRequested;
    private bool initialGameVerified;
    private bool successRequested;
    private bool safeHouseVerified;
    private bool reentryRequested;
    private bool reentryVerified;
    private bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--phase4e-role");
        if (index < 0 || index + 7 >= arguments.Length)
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
        int stageLevel = int.Parse(
            arguments[index + 5],
            CultureInfo.InvariantCulture);
        int configuredCycleLength = int.Parse(
            arguments[index + 6],
            CultureInfo.InvariantCulture);
        VerificationMode requestedMode =
            string.Equals(
                arguments[index + 7],
                "cycle",
                StringComparison.OrdinalIgnoreCase)
                ? VerificationMode.Cycle
                : VerificationMode.Static;

        GameObject probeObject = new GameObject("Phase4EVerification");
        DontDestroyOnLoad(probeObject);
        probeObject.AddComponent<Phase4ENetworkVerification>().Initialize(
            requestedRole,
            session,
            output,
            delay,
            stageLevel,
            configuredCycleLength,
            requestedMode,
            prefab);
    }

    private void Initialize(
        string requestedRole,
        string session,
        string output,
        float delay,
        int stageLevel,
        int configuredCycleLength,
        VerificationMode requestedMode,
        NetworkRunner prefab)
    {
        role = requestedRole;
        outputDirectory = Path.GetFullPath(output);
        Directory.CreateDirectory(outputDirectory);
        initialStageLevel = Mathf.Max(1, stageLevel);
        cycleLength = Mathf.Max(1, configuredCycleLength);
        mode = requestedMode;
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
                repository = new JsonGameSaveRepository(
                    Path.Combine(outputDirectory, "Saves"));
                GameSaveRepositoryResult<GameSaveData> created =
                    repository.CreateNew(
                        "Phase 4-E isolated verification",
                        cycleLength);
                if (!created.Success)
                    throw new InvalidOperationException(created.Error);

                created.Value.RunProgression.StageLevel = initialStageLevel;
                GameSaveRepositoryResult<GameSaveData> staged =
                    repository.Write(created.Value);
                if (!staged.Success)
                    throw new InvalidOperationException(staged.Error);

                context.InitializeHost(repository, staged.Value);
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

            Log(
                $"StartGame={result.Ok}, reason={result.ShutdownReason}, " +
                $"stage={initialStageLevel}, cycle={cycleLength}, mode={mode}");
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
        if (finished)
            return;

        if (Time.unscaledTime - startedAt > 420f)
        {
            FailAndQuit("逾時，Phase 4-E Runtime 流程未完成。");
            return;
        }

        if (runner == null || !runner.IsRunning)
            return;

        if (!initialGameVerified)
        {
            TryVerifyGame(initialStageLevel, "Initial", false);
            return;
        }

        if (mode == VerificationMode.Static)
        {
            FinishWhenPeerReady("Initial");
            return;
        }

        if (!successRequested)
        {
            if (role != "Host")
            {
                successRequested = true;
                return;
            }

            if (!File.Exists(Path.Combine(
                    outputDirectory,
                    "Client.Initial.complete")))
            {
                return;
            }

            StageFlowController stage = FindStageFlow();
            if (stage == null || stage.Phase != StagePhase.Active)
                return;

            typeof(StageFlowController).GetMethod(
                    "CompleteStage",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(
                    stage,
                    new object[] { true, StageFailureReason.None });
            successRequested = true;
            Log("Requested authoritative successful stage completion.");
            return;
        }

        if (!safeHouseVerified)
        {
            TryVerifySafeHouse(initialStageLevel + 1);
            return;
        }

        if (!reentryRequested)
        {
            if (role != "Host")
            {
                reentryRequested = true;
                return;
            }

            if (!File.Exists(Path.Combine(
                    outputDirectory,
                    "Client.SafeHouse.complete")))
            {
                return;
            }

            SafeHouseFlowController safeHouse = FindSafeHouseFlow();
            if (safeHouse == null)
                return;

            typeof(SafeHouseFlowController).GetMethod(
                    "BeginStageLoad",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(safeHouse, null);
            reentryRequested = true;
            Log("Requested authoritative re-entry from SafeHouse.");
            return;
        }

        if (!reentryVerified)
        {
            TryVerifyGame(initialStageLevel + 1, "Reentry", true);
            return;
        }

        FinishWhenPeerReady("Reentry");
    }

    private void TryVerifyGame(
        int expectedStageLevel,
        string label,
        bool isReentry)
    {
        NetworkMapState map = FindMapState();
        if (map == null || !map.IsReady || !map.HasExplorationSnapshot)
            return;

        StageFlowController stage = map.GetComponent<StageFlowController>();
        if (stage == null || stage.Phase != StagePhase.Active ||
            stage.StageLevel != expectedStageLevel ||
            !stage.TryGetSelectedExtraction(out StageExtractionPoint extraction))
        {
            return;
        }

        if (!IsStageHudReady(stage))
            return;

        StageRuntimePlan plan = StageRules.ResolveRuntimePlan(
            expectedStageLevel,
            cycleLength);
        try
        {
            Require(plan.Kind == StageRuntimeKind.Ordinary,
                "驗證案例不是目前支援的普通關。");
            Require(stage.CycleLength == cycleLength,
                "StageFlow CycleLength 與存檔 Snapshot 不一致。");
            Require(stage.CycleStage == plan.CycleStage && !stage.IsBossStage,
                "StageFlow 的循環階段或 Boss 分流不一致。");
            Require(map.ChunkCount == plan.AdditionalChunkCount + 1,
                "ChunkCount 不符合 Runtime plan。");
            Require(map.FinalChunkIndex == map.ChunkCount - 1,
                "FinalChunkIndex 不是唯一末端 Chunk。");
            Require(extraction.OwnerChunk == map.Chunks[map.FinalChunkIndex],
                "撤離點不屬於最後 Chunk。");
            RequireStageHud(stage);
            VerifyAuthoritySeparation(map);

            string snapshot = BuildGameSnapshot(map, stage, extraction.PointId);
            if (!TryExchangeSnapshot(label, snapshot))
                return;

            if (isReentry)
                reentryVerified = true;
            else
                initialGameVerified = true;

            Log(
                $"PASS {label} Game Level {expectedStageLevel}: " +
                $"Cycle {plan.CycleStage}/{plan.CycleLength}, " +
                $"Chunks={map.ChunkCount}, HUD 與 Host/Client 結果一致。");
        }
        catch (Exception exception)
        {
            FailAndQuit(exception.Message);
        }
    }

    private void TryVerifySafeHouse(int expectedStageLevel)
    {
        SafeHouseFlowController safeHouse = FindSafeHouseFlow();
        if (safeHouse == null ||
            safeHouse.Phase != SafeHousePhase.WaitingForHost ||
            safeHouse.StageLevel != expectedStageLevel)
        {
            return;
        }

        if (!IsSafeHouseHudReady(safeHouse))
            return;

        try
        {
            StageRuntimePlan plan = StageRules.ResolveRuntimePlan(
                expectedStageLevel,
                cycleLength);
            Require(safeHouse.CycleLength == cycleLength,
                "SafeHouse CycleLength 與存檔 Snapshot 不一致。");
            Require(safeHouse.CycleStage == plan.CycleStage,
                "SafeHouse CycleStage 不一致。");
            Require((bool)safeHouse.IsBossStage == plan.IsBossStage,
                "SafeHouse Boss 分流不一致。");
            RequireSafeHouseHud(safeHouse);
            Require(FindMapState() == null,
                "返回 SafeHouse 後仍殘留 NetworkMapState。");

            if (role == "Host")
            {
                GameSaveRuntimeContext context =
                    runner.GetComponent<GameSaveRuntimeContext>();
                Require(context != null && context.HasWritableHostSave,
                    "Host 返回 SafeHouse 後遺失可寫存檔 Context。");
                Require(
                    context.ActiveSave.RunProgression.StageLevel ==
                    expectedStageLevel,
                    "記憶中的 StageLevel 未成功提交。");
                GameSaveRepositoryResult<GameSaveData> loaded =
                    repository.Load(context.ActiveSave.SaveId);
                Require(loaded.Success, loaded.Error);
                Require(
                    loaded.Value.RunProgression.StageLevel ==
                    expectedStageLevel,
                    "磁碟存檔的 StageLevel 未成功提交。");
                Require(loaded.Value.CycleLengthSnapshot == cycleLength,
                    "成功提交改寫了 CycleLengthSnapshot。");
            }
            else
            {
                GameSaveRuntimeContext context =
                    runner.GetComponent<GameSaveRuntimeContext>();
                Require(
                    context != null &&
                    context.AccessMode == GameSaveAccessMode.ClientReadOnly &&
                    context.ActiveSave == null,
                    "Client 不應取得 Host 存檔寫入權限。");
            }

            string snapshot =
                $"{safeHouse.StageLevel}|{safeHouse.CycleLength}|" +
                $"{safeHouse.CycleStage}|{(bool)safeHouse.IsBossStage}";
            if (!TryExchangeSnapshot("SafeHouse", snapshot))
                return;

            safeHouseVerified = true;
            Log(
                $"PASS SafeHouse Level {expectedStageLevel}: " +
                "成功提交、HUD、存檔權限與 Host/Client 結果一致。");
        }
        catch (Exception exception)
        {
            FailAndQuit(exception.Message);
        }
    }

    private bool TryExchangeSnapshot(string label, string snapshot)
    {
        string hostPath = Path.Combine(
            outputDirectory,
            "Host." + label + ".snapshot");
        if (role == "Host")
        {
            File.WriteAllText(hostPath, snapshot);
            return true;
        }

        if (!File.Exists(hostPath))
            return false;

        string hostSnapshot = File.ReadAllText(hostPath);
        Require(
            string.Equals(hostSnapshot, snapshot, StringComparison.Ordinal),
            $"{label} 的 Host/Client Snapshot 不一致。\n" +
            "Host=" + hostSnapshot + "\nClient=" + snapshot);
        File.WriteAllText(
            Path.Combine(outputDirectory, "Client." + label + ".complete"),
            "PASS");
        return true;
    }

    private void FinishWhenPeerReady(string label)
    {
        if (role == "Host")
        {
            if (!File.Exists(Path.Combine(
                    outputDirectory,
                    "Client." + label + ".complete")))
            {
                return;
            }
        }

        Log("PASS Phase 4-E verification complete.");
        finished = true;
        Application.Quit(0);
    }

    private void VerifyAuthoritySeparation(NetworkMapState map)
    {
        ConnectorAlignmentPrototype alignment =
            FindObjectOfType<MapRunSelectionPrototype>().Alignment;
        Require(alignment != null && alignment.RuntimeNavigation != null,
            "缺少 Runtime Navigation owner。");

        if (role == "Host")
        {
            Require(alignment.RuntimeNavigation.IsReady,
                "Host Runtime Navigation 未就緒。");
            Require(
                alignment.RuntimeNavigation.GeneratedGroundAreas.Count ==
                map.ChunkCount,
                "Host 每 Chunk 巡邏區數量不一致。");
        }
        else
        {
            Require(
                !alignment.RuntimeNavigation.IsReady &&
                alignment.RuntimeNavigation.GeneratedGroundAreas.Count == 0,
                "Client 不應建立 AI 導航或巡邏區。");
        }
    }

    private static void RequireStageHud(StageFlowController stage)
    {
        StageHudController[] activeHuds = FindObjectsOfType<StageHudController>(true)
            .Where(candidate => candidate.gameObject.activeInHierarchy)
            .ToArray();
        Require(activeHuds.Length == 1,
            $"Stage HUD 必須只有一份，實際 {activeHuds.Length} 份。");
        Require(FindObjectsOfType<SafeHouseHudController>(true)
                .All(candidate => !candidate.gameObject.activeInHierarchy),
            "進入 Game 後仍有 SafeHouse HUD 顯示。");
        Require(FindObjectsOfType<LocalMinimapController>(true)
                .Count(candidate => candidate.gameObject.activeInHierarchy) == 1,
            "Game 必須只有一份隨 Stage HUD 清理的小地圖。");

        StageHudController hud = activeHuds[0];
        StageFlowController boundFlow = (StageFlowController)typeof(StageHudController)
            .GetField(
                "flowController",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(hud);
        Require(boundFlow == stage, "Stage HUD 綁定到錯誤的關卡擁有者。");
        TMP_Text label = (TMP_Text)typeof(StageHudController)
            .GetField(
                "stageLevelLabel",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(hud);
        string expected = StageHudText.BuildStageHeader(
            stage.StageLevel,
            stage.CycleStage,
            stage.CycleLength,
            stage.IsBossStage);
        Require(label != null && label.text == expected,
            $"Stage HUD 不一致：'{label?.text}' / '{expected}'。");
    }

    private static bool IsStageHudReady(StageFlowController stage)
    {
        StageHudController[] activeHuds = FindObjectsOfType<StageHudController>(true)
            .Where(candidate => candidate.gameObject.activeInHierarchy)
            .ToArray();
        if (activeHuds.Length != 1)
            return false;

        StageHudController hud = activeHuds[0];
        StageFlowController boundFlow = (StageFlowController)typeof(StageHudController)
            .GetField(
                "flowController",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(hud);
        TMP_Text label = (TMP_Text)typeof(StageHudController)
            .GetField(
                "stageLevelLabel",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(hud);
        return boundFlow == stage &&
               label != null && label.text == StageHudText.BuildStageHeader(
            stage.StageLevel,
            stage.CycleStage,
            stage.CycleLength,
            stage.IsBossStage);
    }

    private static void RequireSafeHouseHud(SafeHouseFlowController flow)
    {
        SafeHouseHudController[] activeHuds =
            FindObjectsOfType<SafeHouseHudController>(true)
                .Where(candidate => candidate.gameObject.activeInHierarchy)
                .ToArray();
        Require(activeHuds.Length == 1,
            $"SafeHouse HUD 必須只有一份，實際 {activeHuds.Length} 份。");
        Require(FindObjectsOfType<StageHudController>(true)
                .All(candidate => !candidate.gameObject.activeInHierarchy),
            "進入 SafeHouse 後仍有 Stage HUD 顯示。");
        var minimaps = FindObjectsOfType<LocalMinimapController>(true)
            .Where(candidate => candidate.gameObject.activeInHierarchy).ToArray();
        Require(minimaps.Length == 1 &&
            (SafeHouseFlowController)typeof(LocalMinimapController).GetField("safeHouse",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(minimaps[0]) == flow,
            "SafeHouse 必須只有一份綁定當前 Flow 的安全屋平面圖。");

        SafeHouseHudController hud = activeHuds[0];
        SafeHouseFlowController boundFlow =
            (SafeHouseFlowController)typeof(SafeHouseHudController)
                .GetField(
                    "flowController",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(hud);
        Require(boundFlow == flow, "SafeHouse HUD 綁定到錯誤的關卡擁有者。");
        TMP_Text label = (TMP_Text)typeof(SafeHouseHudController)
            .GetField(
                "stageLevelLabel",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(hud);
        string expected = StageHudText.BuildStageHeader(
            flow.StageLevel,
            flow.CycleStage,
            flow.CycleLength,
            flow.IsBossStage);
        Require(label != null && label.text == expected,
            $"SafeHouse HUD 不一致：'{label?.text}' / '{expected}'。");
    }

    private static bool IsSafeHouseHudReady(SafeHouseFlowController flow)
    {
        SafeHouseHudController[] activeHuds =
            FindObjectsOfType<SafeHouseHudController>(true)
                .Where(candidate => candidate.gameObject.activeInHierarchy)
                .ToArray();
        if (activeHuds.Length != 1)
            return false;

        SafeHouseHudController hud = activeHuds[0];
        SafeHouseFlowController boundFlow =
            (SafeHouseFlowController)typeof(SafeHouseHudController)
                .GetField(
                    "flowController",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(hud);
        TMP_Text label = (TMP_Text)typeof(SafeHouseHudController)
            .GetField(
                "stageLevelLabel",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(hud);
        return boundFlow == flow &&
               label != null && label.text == StageHudText.BuildStageHeader(
            flow.StageLevel,
            flow.CycleStage,
            flow.CycleLength,
            flow.IsBossStage);
    }

    private NetworkMapState FindMapState()
    {
        return FindObjectsOfType<NetworkMapState>()
            .FirstOrDefault(candidate => candidate.Runner == runner);
    }

    private StageFlowController FindStageFlow()
    {
        return FindObjectsOfType<StageFlowController>()
            .FirstOrDefault(candidate => candidate.Runner == runner);
    }

    private SafeHouseFlowController FindSafeHouseFlow()
    {
        return FindObjectsOfType<SafeHouseFlowController>()
            .FirstOrDefault(candidate => candidate.Runner == runner);
    }

    private static string BuildGameSnapshot(
        NetworkMapState map,
        StageFlowController stage,
        string extractionPointId)
    {
        var builder = new StringBuilder();
        builder.Append(stage.StageLevel)
            .Append('|').Append(stage.CycleLength)
            .Append('|').Append(stage.CycleStage)
            .Append('|').Append((bool)stage.IsBossStage)
            .Append('|').Append(map.LayoutRevision)
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
                .Append(',').Append(placement.Position.z.ToString("F3", CultureInfo.InvariantCulture));
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
        if (finished)
            return;

        Log("FAIL " + message);
        finished = true;
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
        Debug.Log("[Phase4EVerification] " + message);
    }
}
#endif
