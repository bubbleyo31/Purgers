#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Reflection;
using Fusion;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.SafeHouse;
using Purgers.GameFlow.Stage;
using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit opt-in two-process probe for SafeHouse Ready Check Phase 4 前置-2.</summary>
[DefaultExecutionOrder(-190)]
public sealed class Phase42NetworkVerification : MonoBehaviour
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private NetworkRunner runner;
    private string role;
    private string output;
    private float startedAt;
    private float stepStartedAt;
    private float nextSnapshotAt;
    private int step;
    private bool completed;
    private bool capturedReadyHud;
    private float stageHudReadyAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--phase42-client");
        if (index < 0 || index + 2 >= args.Length)
            return;

        var menu = FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if (menu == null)
            return;

        var prefab = (NetworkRunner)typeof(MultiClimb.Menu.MenuConnectionBehaviour)
            .GetField("networkRunnerPrefab", PrivateInstance)
            .GetValue(menu);
        StartVerification("Client", args[index + 1], args[index + 2], prefab);
    }

    public static void StartVerification(
        string testRole,
        string session,
        string directory,
        NetworkRunner prefab)
    {
        if (!Application.isPlaying ||
            FindObjectOfType<Phase42NetworkVerification>() != null)
        {
            return;
        }

        var probe = new GameObject("Phase42Verification")
            .AddComponent<Phase42NetworkVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.Begin(testRole, session, directory, prefab);
    }

    private async void Begin(
        string testRole,
        string session,
        string directory,
        NetworkRunner prefab)
    {
        role = testRole;
        output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        startedAt = Time.unscaledTime;
        stepStartedAt = startedAt;
        Application.runInBackground = true;

        try
        {
            runner = Instantiate(prefab);
            runner.ProvideInput = true;
            var context = runner.gameObject.AddComponent<GameSaveRuntimeContext>();

            if (role == "Host")
            {
                var repository = new JsonGameSaveRepository(
                    Path.Combine(output, "Saves"));
                GameSaveRepositoryResult<GameSaveData> save =
                    repository.CreateNew("Phase 4 前置-2 isolated verification");
                if (!save.Success)
                    throw new Exception(save.Error);
                context.InitializeHost(repository, save.Value);
            }
            else
            {
                context.InitializeClientReadOnly();
            }

            var manager = runner.gameObject
                .AddComponent<NetworkSceneManagerDefault>();
            manager.IsSceneTakeOverEnabled = false;
            var scene = new NetworkSceneInfo();
            scene.AddSceneRef(
                SceneRef.FromIndex(1),
                LoadSceneMode.Additive);

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

            Log("StartGame=" + result.Ok + ", reason=" + result.ShutdownReason);
            // This opt-in probe starts Runner directly, outside MenuConnection.
            // Hide the menu without invoking gameplay menu methods that require Connection.
            if (result.Ok)
            {
                var main = FindObjectOfType<Fusion.Menu.FusionMenuUIMain>(true);
                if (main != null)
                    main.gameObject.SetActive(false);
            }
            if (!result.Ok)
            {
                Log("FAIL StartGame=" + result.ShutdownReason);
                completed = true;
            }
        }
        catch (Exception exception)
        {
            Log("FAIL " + exception);
            completed = true;
        }
    }

    private void Update()
    {
        if (completed || runner == null || !runner.IsRunning)
            return;

        if (Time.unscaledTime - startedAt > 150f)
        {
            Log("FAIL timeout at step=" + step);
            completed = true;
            return;
        }

        if (Time.unscaledTime >= nextSnapshotAt)
        {
            nextSnapshotAt = Time.unscaledTime + 1f;
            SafeHouseFlowController snapshot =
                FindObjectOfType<SafeHouseFlowController>();
            Log(
                "snapshot step=" + step +
                " peers=" + PlayerCount() +
                " phase=" +
                (snapshot != null && snapshot.IsNetworkReady
                    ? snapshot.Phase.ToString()
                    : "none") +
                " ready=" +
                (snapshot != null && snapshot.IsNetworkReady
                    ? snapshot.ReadyPlayerCount.ToString()
                    : "0") +
                " remaining=" +
                (snapshot != null && snapshot.IsNetworkReady
                    ? snapshot.RemainingCountdownSeconds.ToString("F2")
                    : "0") +
                " vote=" +
                (snapshot != null && snapshot.IsNetworkReady
                    ? snapshot.RemainingReadyCheckSeconds.ToString("F2")
                    : "0") +
                " locks=" + LocalPlayerControl.Locks.Mask);
        }

        StageFlowController stage = FindObjectOfType<StageFlowController>();
        if (stage != null && stage.IsNetworkReady)
        {
            if (stage.Phase != StagePhase.Active ||
                Purgers.GameFlow.Transition.ScreenFadeLayer.IsCoveringScreen)
                return;
            // This probe runs before HUD Update; allow presentation to consume the new phase.
            if (stageHudReadyAt < 0f)
                stageHudReadyAt = Time.unscaledTime + 0.5f;
            if (Time.unscaledTime < stageHudReadyAt)
                return;
            int expectedStep = role == "Host" ? 8 : 11;
            Log(
                (step == expectedStep ? "PASS " : "FAIL ") +
                "transition reached Game; step=" + step +
                ", expected=" + expectedStep +
                ", stage phase=" + stage.Phase);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, role + "-StageHUD.png"));
            var hud = FindObjectOfType<StageHudController>();
            var timer = hud == null ? null : (TMP_Text)typeof(StageHudController)
                .GetField("timerLabel", PrivateInstance).GetValue(hud);
            bool clockOnly = timer != null && System.Text.RegularExpressions.Regex.IsMatch(timer.text, @"^\d{2,}:\d{2}$");
            Log((clockOnly ? "PASS " : "FAIL ") + "numeric stage timer=" + (timer != null ? timer.text : "missing"));
            ValidateSpeedHud();
            completed = true;
            return;
        }

        SafeHouseFlowController safe = FindObjectOfType<SafeHouseFlowController>();
        if (safe == null || !safe.IsNetworkReady ||
            LocalPlayerControl.AllInputBlocked)
        {
            return;
        }

        if (role == "Host")
            UpdateHost(safe);
        else
            UpdateClient(safe);
    }

    private void UpdateHost(SafeHouseFlowController safe)
    {
        switch (step)
        {
            case 0:
                if (PlayerCount() != 2 ||
                    !runner.TryGetPlayerObject(
                        runner.LocalPlayer,
                        out NetworkObject local) ||
                    local == null || !local.IsValid)
                {
                    return;
                }

                Transform anchor = (Transform)typeof(SafeHouseFlowController)
                    .GetField("startTerminalAnchor", PrivateInstance)
                    .GetValue(safe);
                if (anchor == null)
                    return;

                TeleportForVerification(local, anchor.position);
                safe.RequestOpenReadyCheck();
                if (safe.Phase == SafeHousePhase.ReadyCheck)
                {
                    Log("PASS Host opened Ready Check");
                    Advance();
                }
                break;

            case 1:
                if (safe.Phase != SafeHousePhase.WaitingForHost ||
                    Time.unscaledTime - stepStartedAt < 6f)
                    return;
                float timeoutElapsed = Time.unscaledTime - stepStartedAt;
                if (timeoutElapsed < 6.5f || timeoutElapsed > 8.5f)
                {
                    Log(
                        "FAIL " +
                        "Host ready vote timeout outside expected bounds: " +
                        timeoutElapsed.ToString("F2") + "s");
                    completed = true;
                    return;
                }
                Log(
                    "PASS Host observed seven-second ready vote timeout at " +
                    timeoutElapsed.ToString("F2") + "s");
                safe.RequestOpenReadyCheck();
                if (safe.Phase == SafeHousePhase.ReadyCheck)
                    Advance();
                break;

            case 2:
                if (safe.Phase == SafeHousePhase.WaitingForHost)
                {
                    Log("PASS Host observed Client cancel whole ready vote");
                    safe.RequestOpenReadyCheck();
                    if (safe.Phase == SafeHousePhase.ReadyCheck)
                        Advance();
                }
                break;

            case 3:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    safe.ReadyPlayerCount == 1 &&
                    !safe.IsLocalPlayerReady &&
                    ValidateHud(safe, 1, 1))
                {
                    Log("PASS Host observed Client ready through RPC");
                    Advance();
                }
                break;

            case 4:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    safe.ReadyPlayerCount == 0 &&
                    Time.unscaledTime - stepStartedAt >= 0.75f)
                {
                    Log("PASS Host observed Client cancel through RPC");
                    safe.RequestToggleLocalReady();
                    Advance();
                }
                break;

            case 5:
                if (safe.Phase == SafeHousePhase.Countdown &&
                    safe.ReadyPlayerCount == 2 &&
                    ValidateHud(safe, 2, 0))
                {
                    Log("PASS Host started three-second countdown");
                    Advance();
                }
                break;

            case 6:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    safe.ReadyPlayerCount == 1 &&
                    safe.IsLocalPlayerReady &&
                    ValidateHud(safe, 1, 1))
                {
                    Log("PASS Host cancelled countdown after Client unready");
                    Advance();
                }
                break;

            case 7:
                if (safe.Phase == SafeHousePhase.Countdown &&
                    safe.ReadyPlayerCount == 2)
                {
                    Log("PASS Host restarted countdown after all ready");
                    Advance();
                }
                break;
        }
    }

    private void UpdateClient(SafeHouseFlowController safe)
    {
        switch (step)
        {
            case 0:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    safe.ReadyPlayerCount == 0 &&
                    ValidateHud(safe, 0, 2))
                {
                    Log("PASS Client ready vote leaves gameplay input unrestricted");
                    Advance();
                }
                break;

            case 1:
                if (safe.Phase == SafeHousePhase.WaitingForHost)
                {
                    Log("PASS Client observed seven-second ready vote timeout");
                    Advance();
                }
                break;

            case 2:
                if (safe.Phase == SafeHousePhase.ReadyCheck)
                {
                    safe.RequestCancelLocalReadyCheck();
                    Log("Client requested whole ready vote cancel");
                    Advance();
                }
                break;

            case 3:
                if (safe.Phase == SafeHousePhase.WaitingForHost)
                {
                    Log("PASS Client ESC-equivalent cancel synchronized");
                    Advance();
                }
                break;

            case 4:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    safe.ReadyPlayerCount == 0)
                {
                    safe.RequestToggleLocalReady();
                    Log("Client requested ready toggle");
                    Advance();
                }
                break;

            case 5:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    safe.IsLocalPlayerReady &&
                    safe.ReadyPlayerCount == 1 &&
                    Time.unscaledTime - stepStartedAt >= 0.5f &&
                    ValidateHud(safe, 1, 1))
                {
                    Log("PASS Client ready synchronized");
                    safe.RequestToggleLocalReady();
                    Advance();
                }
                break;

            case 6:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    !safe.IsLocalPlayerReady &&
                    safe.ReadyPlayerCount == 0 &&
                    ValidateHud(safe, 0, 2))
                {
                    Log("PASS Client cancel synchronized");
                    Advance();
                }
                break;

            case 7:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    !safe.IsLocalPlayerReady &&
                    safe.ReadyPlayerCount == 1)
                {
                    Log("PASS Client observed Host ready");
                    safe.RequestToggleLocalReady();
                    Advance();
                }
                break;

            case 8:
                if (safe.Phase == SafeHousePhase.Countdown &&
                    safe.IsLocalPlayerReady &&
                    safe.RemainingCountdownSeconds <= 1.5f &&
                    ValidateHud(safe, 2, 0))
                {
                    Log("PASS Client observed countdown and requested cancel");
                    safe.RequestToggleLocalReady();
                    Advance();
                }
                break;

            case 9:
                if (safe.Phase == SafeHousePhase.ReadyCheck &&
                    !safe.IsLocalPlayerReady &&
                    safe.ReadyPlayerCount == 1 &&
                    Time.unscaledTime - stepStartedAt >= 0.5f &&
                    ValidateHud(safe, 1, 1))
                {
                    Log("PASS Client observed countdown cancellation");
                    safe.RequestToggleLocalReady();
                    Advance();
                }
                break;

            case 10:
                if (safe.Phase == SafeHousePhase.Countdown &&
                    safe.ReadyPlayerCount == 2)
                {
                    Log("PASS Client observed restarted countdown");
                    Advance();
                }
                break;
        }
    }

    private bool ValidateHud(
        SafeHouseFlowController safe,
        int expectedReady,
        int expectedUnready)
    {
        SafeHouseHudController hud = FindObjectOfType<SafeHouseHudController>();
        if (hud == null)
        {
            return false;
        }

        Type hudType = typeof(SafeHouseHudController);
        TMP_Text led = (TMP_Text)hudType
            .GetField("readyLedLabel", PrivateInstance)
            .GetValue(hud);
        TMP_Text status = (TMP_Text)hudType
            .GetField("readyStatusLabel", PrivateInstance)
            .GetValue(hud);
        GameObject panel = (GameObject)hudType
            .GetField("readyPanelRoot", PrivateInstance)
            .GetValue(hud);
        int ledCount = led == null ? 0 : CountCharacter(led.text, '●');
        RectTransform rect = panel == null
            ? null
            : panel.GetComponent<RectTransform>();
        bool countsMatch = status != null &&
            status.text.Contains("已準備 " + expectedReady) &&
            status.text.Contains("未準備 " + expectedUnready);
        Transform slot = rect != null ? rect.parent : null;
        Transform column = slot != null ? slot.parent : null;
        bool topLeftThirdRow = rect != null && slot != null && column != null &&
            slot.name == "ReadyCheckSlot" && column.name == "TopLeftColumn" &&
            column.Find("MinimapSlot") != null && slot.GetSiblingIndex() == 2 &&
            rect.anchorMin == new Vector2(0f, 1f) &&
            rect.anchorMax == new Vector2(0f, 1f) &&
            rect.anchoredPosition == Vector2.zero;
        bool inputUnrestricted =
            LocalPlayerControl.Locks.Mask == PlayerControlMask.None;
        bool passed = panel != null && panel.activeInHierarchy &&
            ledCount == safe.ConnectedPlayerCount &&
            countsMatch && topLeftThirdRow && inputUnrestricted;

        if (passed)
        {
            if (!capturedReadyHud)
            {
                capturedReadyHud = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(output, role + "-ReadyHUD.png"));
            }
            Log(
                "PASS HUD LEDs/counts/top-left-third-row/no-input-lock" +
                " leds=" + ledCount +
                " connected=" + safe.ConnectedPlayerCount +
                " counts=" + countsMatch +
                " row=" + topLeftThirdRow +
                " lock=" + LocalPlayerControl.Locks.Mask);
        }

        return passed;
    }

    private void ValidateSpeedHud()
    {
        var hud = FindObjectOfType<LocalPlayerSpeedSlider>();
        if (hud == null)
        {
            Log("FAIL speed HUD missing");
            return;
        }
        var type = typeof(LocalPlayerSpeedSlider);
        var player = (Player)type.GetField("boundPlayer", PrivateInstance).GetValue(hud);
        var visual = (GameObject)type.GetField("speedVisualRoot", PrivateInstance).GetValue(hud);
        var label = (TMP_Text)type.GetField("damageBonusLabel", PrivateInstance).GetValue(hud);
        var rect = visual != null ? visual.GetComponent<RectTransform>() : null;
        bool valid = player != null && player.Object != null && player.Object.IsValid &&
            player.Object.HasInputAuthority && visual.activeInHierarchy &&
            rect.parent.GetComponent<Canvas>() != null &&
            rect.anchorMin == new Vector2(0.5f, 0f) && label != null && label.text.StartsWith("傷害增加");
        Log((valid ? "PASS " : "FAIL ") + "local speed/bonus HUD bottom-center, label=" +
            (label != null ? label.text : "missing"));
    }

    private static int CountCharacter(string value, char character)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == character)
                count++;
        }
        return count;
    }

    private void Advance()
    {
        step++;
        stepStartedAt = Time.unscaledTime;
    }

    private int PlayerCount()
    {
        int count = 0;
        foreach (PlayerRef player in runner.ActivePlayers)
            count++;
        return count;
    }

    private static void TeleportForVerification(
        NetworkObject player,
        Vector3 position)
    {
        var kcc = player.GetComponent<PlayerMovement>().KCC;
        kcc.FixedData.BasePosition = position;
        kcc.FixedData.DesiredPosition = position;
        kcc.FixedData.TargetPosition = position;
        kcc.SetPosition(position);
    }

    private void Log(string message)
    {
        string line = DateTime.UtcNow.ToString("O") +
            " [" + role + "] " + message;
        File.AppendAllText(
            Path.Combine(output, role + ".log"),
            line + Environment.NewLine);
        Debug.Log("[Phase42Verification] " + message);
    }
}
#endif
