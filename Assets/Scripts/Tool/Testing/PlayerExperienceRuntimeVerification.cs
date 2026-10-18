#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit isolated multi-peer smoke test; never saves scenes or player progress.</summary>
public static class PlayerExperienceRuntimeVerification
{
    public static string Status { get; private set; } = "Not started";
    private static readonly List<string> Report = new List<string>();
    private static readonly List<NetworkRunner> Runners = new List<NetworkRunner>();
    private static readonly List<GameObject> Temporary = new List<GameObject>();
    private static string session;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(1500, 1500, 1500);

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        Report.Add("PASS " + label);
    }

    private static async Task Until(Func<bool> predicate, string label)
    {
        float end = Time.realtimeSinceStartup + 35;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup > end) throw new TimeoutException(label);
            await Task.Delay(50);
        }
    }

    private static void Field(object target, string name, object value)
        => target.GetType().GetField(name, Private).SetValue(target, value);

    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("Requires Play Mode with no running Runner.");
        Status = "Running"; Report.Clear(); Runners.Clear(); Temporary.Clear();
        session = "xp-table-" + Guid.NewGuid().ToString("N");
        try
        {
            var host = await Connect(true);
            var logic = CreateLogic(host, new[] { 5, 15, 30 });
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>();
            host.AddGlobal(driver);
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null, "host player");
            Check(logic.TryGetPlayerExperience(host.LocalPlayer, out var xp, out int required) &&
                xp.Level == 1 && required == 5, "initial threshold");
            var client = await Connect(false);
            var clientLogic = CreateLogic(client, new[] { 900 });
            await Until(() => clientLogic.TryGetPlayerExperience(host.LocalPlayer, out _, out _), "client initial XP");
            Check(!clientLogic.TryAwardPlayerExperience(host.LocalPlayer, 100), "client cannot grant XP");
            await driver.AtTick(() => Check(logic.TryAwardPlayerExperience(host.LocalPlayer, 83), "authoritative grant"));
            Check(logic.TryGetPlayerExperience(host.LocalPlayer, out xp, out required) &&
                xp.Level == 5 && xp.Experience == 3 && xp.PendingRewards == 5 && required == 30,
                "multi-level overflow and fixed tail with pending rewards");
            await Until(() => clientLogic.TryGetPlayerExperience(host.LocalPlayer, out var value, out int threshold) &&
                value.Level == 5 && value.Experience == 3 && threshold == 30, "client snapshot");
            Check(true, "client receives Host threshold despite different local table");
            CheckHud(clientLogic, host.LocalPlayer);
            await driver.AtTick(() => Check((bool)typeof(GameLogic).GetMethod("TryDebugLevelUpStateAuthority", Private)
                .Invoke(logic, new object[] { host.LocalPlayer }), "F8 authority path"));
            Check(logic.TryGetPlayerExperience(host.LocalPlayer, out xp, out required) &&
                xp.Level == 6 && xp.Experience == 0 && required == 30, "F8 fills exact custom requirement");
            var late = await Connect(false);
            var lateLogic = CreateLogic(late, new[] { 700 });
            await Until(() => lateLogic.TryGetPlayerExperience(host.LocalPlayer, out var value, out int threshold) &&
                value.Level == 6 && threshold == 30, "late join");
            Check(true, "late join receives current level and threshold");

            // Scene adoption must not replace the Session table.
            var sceneRoot = new GameObject("Temporary scene configuration");
            Temporary.Add(sceneRoot);
            var sceneLogic = sceneRoot.AddComponent<GameLogic>();
            Field(sceneLogic, "experienceRequirements", new[] { 999 });
            await driver.AtTick(() => typeof(GameLogic).GetMethod("AdoptSceneConfigurationFrom", Private)
                .Invoke(logic, new object[] { sceneLogic }));
            Check(((int[])typeof(GameLogic).GetField("experienceRequirements", Private).GetValue(logic))[2] == 30,
                "scene configuration adoption retains Session table");
            await driver.AtTick(() => logic.ResetPlayerExperiencesForFailedRun());
            await Until(() => lateLogic.TryGetPlayerExperience(host.LocalPlayer, out var value, out int threshold) &&
                value.Level == 1 && value.Experience == 0 && threshold == 5, "failed run reset");
            Check(true, "failed run resets synchronized threshold");
            Status = "Passed " + Report.Count;
        }
        catch (Exception e) { Status = "Failed: " + e; Report.Add(Status); Debug.LogError(Status); }
        finally
        {
            foreach (var runner in Runners)
                if (runner != null && runner.IsRunning) await runner.Shutdown();
            foreach (var go in Temporary) if (go != null) UnityEngine.Object.Destroy(go);
            Directory.CreateDirectory("Temp/ExperienceTable");
            File.WriteAllLines("Temp/ExperienceTable/runtime.txt", Report);
        }
    }

    private static void CheckHud(GameLogic logic, PlayerRef player)
    {
        Check(logic.TryGetPlayerExperience(player, out var xp, out int required), "HUD snapshot available");
        var root = new GameObject("Temporary XP HUD"); Temporary.Add(root); root.SetActive(false);
        var hud = root.AddComponent<LocalPlayerRewardHUD>();
        var labelObject = new GameObject("label", typeof(RectTransform)); Temporary.Add(labelObject);
        var label = labelObject.AddComponent<TMPro.TextMeshProUGUI>();
        Field(hud, "experienceLabel", label);
        typeof(LocalPlayerRewardHUD).GetMethod("RefreshExperience", Private)
            .Invoke(hud, new object[] { xp, 0, required });
        Check(label.text.StartsWith("3 / 30"), "HUD presents Host denominator");
    }

    private static GameLogic CreateLogic(NetworkRunner runner, int[] requirements)
    {
        var root = new GameObject("Temporary XP GameLogic", typeof(NetworkObject)); Temporary.Add(root);
        runner.MoveToRunnerScene(root);
        var logic = root.AddComponent<GameLogic>();
        root.AddComponent<Purgers.GameFlow.Stage.StageFlowController>();
        Field(logic, "experienceRequirements", requirements);
        Field(logic, "playerPrefab", NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        Field(logic, "debugPlayerLifecycle", false);
        Field(logic, "fallbackSpawnPosition", Origin);
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); Temporary.Add(floor); runner.MoveToRunnerScene(floor);
        floor.transform.position = Origin + Vector3.down * 2;
        floor.transform.localScale = new Vector3(100, 1, 100);
        floor.layer = Mathf.Max(0, LayerMask.NameToLayer("Ground"));
        return logic;
    }

    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "XP host" : "XP client").AddComponent<NetworkRunner>();
        Runners.Add(runner); Temporary.Add(runner.gameObject); runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>();
        events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => input.Set(default(NetInput)));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var scene = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();
        scene.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs {
            GameMode = server ? GameMode.Host : GameMode.Client, SessionName = session,
            Config = config, SceneManager = scene, CustomPhotonAppSettings = app, PlayerCount = 4, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner tick");
        return runner;
    }
}
#endif
