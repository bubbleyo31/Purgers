#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Reflection;
using Fusion;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.SafeHouse;
using Purgers.GameFlow.Stage;
using Purgers.GameFlow.Transition;
using Purgers.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit opt-in, two-process development probe. Uses an isolated save repository.</summary>
[DefaultExecutionOrder(-200)]
public sealed class Phase41NetworkVerification : MonoBehaviour
{
    private NetworkRunner runner;
    private string role;
    private string output;
    private float started;
    private float stageSeenAt;
    private StageFlowController stage;
    private int stageVisit;
    private bool delayed;
    private bool activeLogged;
    private bool completed;
    private float nextSnapshot;
    private float safeSince;
    private bool controlChecked;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--phase41-client");
        if (index < 0 || index + 2 >= args.Length) return;
        var menu = FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if (menu == null) return;
        var prefab = (NetworkRunner)typeof(MultiClimb.Menu.MenuConnectionBehaviour)
            .GetField("networkRunnerPrefab", PrivateInstance).GetValue(menu);
        StartVerification("Client", args[index + 1], args[index + 2], prefab);
    }

    public static void StartVerification(string role, string session, string output, NetworkRunner prefab)
    {
        if (!Application.isPlaying || FindObjectOfType<Phase41NetworkVerification>() != null) return;
        var probe = new GameObject("Phase41Verification").AddComponent<Phase41NetworkVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.Begin(role, session, output, prefab);
    }

    private async void Begin(string testRole, string session, string directory, NetworkRunner prefab)
    {
        role = testRole;
        output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        started = Time.unscaledTime;
        Application.runInBackground = true;
        try
        {
            runner = Instantiate(prefab);
            runner.ProvideInput = true;
            var context = runner.gameObject.AddComponent<GameSaveRuntimeContext>();
            if (role == "Host")
            {
                var repository = new JsonGameSaveRepository(Path.Combine(output, "Saves"));
                var save = repository.CreateNew("Phase 4 前置-1 isolated verification");
                if (!save.Success) throw new Exception(save.Error);
                context.InitializeHost(repository, save.Value);
            }
            else context.InitializeClientReadOnly();
            var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            manager.IsSceneTakeOverEnabled = false;
            var scene = new NetworkSceneInfo();
            scene.AddSceneRef(SceneRef.FromIndex(1), LoadSceneMode.Additive);
            // Editor and Player can select different cached "best" regions.
            // Clone settings so this opt-in probe uses one region without changing project settings.
            var settings = new Fusion.Photon.Realtime.FusionAppSettings();
            Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);
            settings.FixedRegion = "asia";
            var result = await runner.StartGame(new StartGameArgs
            {
                CustomPhotonAppSettings = settings,
                GameMode = role == "Host" ? GameMode.Host : GameMode.Client,
                SessionName = session,
                Scene = role == "Host" ? scene : (NetworkSceneInfo?)null,
                SceneManager = manager,
                PlayerCount = 2
            });
            Log("StartGame=" + result.Ok + ", reason=" + result.ShutdownReason);
            if (!result.Ok) completed = true;
        }
        catch (Exception ex) { Log("FAIL " + ex); completed = true; }
    }

    private void Update()
    {
        if (completed || runner == null || !runner.IsRunning) return;
        if (Time.unscaledTime - started > 240f)
        {
            Log("FAIL timeout");
            completed = true;
            return;
        }
        var transition = runner.GetComponent<LocalSceneTransition>();
        if (Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + 1f;
            Log($"snapshot peers={PlayerCount()} locked={LocalPlayerControl.AllInputBlocked} " +
                $"loaded={transition?.IsLocalLoadComplete} alpha={transition?.Fade.Alpha:F2} " +
                $"stage={stageVisit}:{(stage != null && stage.IsNetworkReady ? stage.Phase.ToString() : "none")} " +
                $"acks={(stage != null && stage.IsNetworkReady ? stage.LoadedPlayerCount : 0)} " +
                $"time={(stage != null && stage.IsNetworkReady ? stage.RemainingStageSeconds : 0):F2}");
        }
        if (!controlChecked && transition != null && !transition.IsTransitioning && transition.IsLocalLoadComplete)
        {
            controlChecked = true;
            var move = LocalPlayerControl.Acquire(PlayerControlMask.Movement, "probe movement");
            var look = LocalPlayerControl.Acquire(PlayerControlMask.Look, "probe look");
            var load = LocalPlayerControl.Acquire(PlayerControlMask.AllInput, "probe transition");
            load.Dispose();
            bool stacked = LocalPlayerControl.Locks.Mask == (PlayerControlMask.Movement | PlayerControlMask.Look);
            move.Dispose(); look.Dispose();
            Log(stacked ? "PASS independent lock owners" : "FAIL independent lock owners");
        }

        var found = FindObjectOfType<StageFlowController>();
        if (found != null && found.IsNetworkReady && found != stage)
        {
            stage = found;
            stageVisit++;
            stageSeenAt = Time.unscaledTime;
            activeLogged = delayed = false;
            safeSince = 0f;
            if (role == "Client")
            {
                typeof(StageFlowController).GetField("nextLoadReportTime", PrivateInstance)
                    .SetValue(stage, Time.unscaledTime + 10f);
                delayed = true;
                Log("Delay local load report for 10 seconds, visit=" + stageVisit);
            }
        }
        if (stage != null && stage.IsNetworkReady)
        {
            if (role == "Client" && stageVisit == 2 && delayed && Time.unscaledTime - stageSeenAt > 3f)
            {
                Log("PASS disconnect while Host waits for second load");
                completed = true;
                runner.Shutdown();
                return;
            }
            if (stage.Phase == StagePhase.Active && !activeLogged)
            {
                activeLogged = true;
                bool waited = stageVisit == 1 ? Time.unscaledTime - stageSeenAt >= 8f : PlayerCount() == 1;
                Log((waited ? "PASS " : "FAIL ") + "stage started after load barrier, visit=" + stageVisit);
                if (role == "Host" && stageVisit == 2)
                {
                    Log("PASS repeat scene load and disconnected participant removed; verification complete");
                    completed = true;
                    return;
                }
            }
            if (role == "Host" && stageVisit == 1 && stage.Phase == StagePhase.Active &&
                Time.unscaledTime - stageSeenAt > 14f && stage.TryGetSelectedExtraction(out var point))
            {
                foreach (PlayerRef player in runner.ActivePlayers)
                    if (runner.TryGetPlayerObject(player, out var obj) && obj != null)
                        TeleportForVerification(obj, point.transform.position + Vector3.up);
            }
            return;
        }

        var safe = FindObjectOfType<SafeHouseFlowController>();
        if (safe == null || !safe.IsNetworkReady || LocalPlayerControl.AllInputBlocked) return;
        if (role == "Host" && PlayerCount() == 2)
        {
            if (safeSince == 0f) safeSince = Time.unscaledTime;
            if (Time.unscaledTime - safeSince < 3f) return;
            // Repeat Level 1: this verifies the barrier, not the later map topology phase.
            runner.GetComponent<GameSaveRuntimeContext>().ActiveSave.RunProgression.StageLevel = 1;
            var anchor = (Transform)typeof(SafeHouseFlowController).GetField("startTerminalAnchor", PrivateInstance).GetValue(safe);
            if (anchor != null && runner.TryGetPlayerObject(runner.LocalPlayer, out var local))
                TeleportForVerification(local, anchor.position);
            safe.RequestOpenReadyCheck();
        }
        if (safe.Phase == SafeHousePhase.ReadyCheck && !safe.IsLocalPlayerReady)
            safe.RequestToggleLocalReady();
    }

    private int PlayerCount()
    {
        int count = 0;
        foreach (PlayerRef player in runner.ActivePlayers) count++;
        return count;
    }

    private static void TeleportForVerification(NetworkObject player, Vector3 position)
    {
        // KCC.SetPosition in Update only changes RenderData and vanishes next tick.
        // This explicit development probe updates the authoritative fixed snapshot too.
        var kcc = player.GetComponent<PlayerMovement>().KCC;
        kcc.FixedData.BasePosition = position;
        kcc.FixedData.DesiredPosition = position;
        kcc.FixedData.TargetPosition = position;
        kcc.SetPosition(position);
    }

    private void Log(string message)
    {
        string line = $"{DateTime.UtcNow:O} [{role}] {message}";
        File.AppendAllText(Path.Combine(output, role + ".log"), line + Environment.NewLine);
        Debug.Log("[Phase41Verification] " + message);
    }
}
#endif
