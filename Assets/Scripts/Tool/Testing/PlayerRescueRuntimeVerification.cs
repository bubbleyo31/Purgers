#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>手動啟動的隔離救援多人驗證；不保存場景或正式玩家進度。</summary>
public static partial class PlayerRescueRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client, lateClient;
    private static string session;
    private static bool holdE;
    private static bool holdHostE;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.Add("PASS " + message); }
    private static async Task Until(Func<bool> predicate, string label, float seconds = 40)
    {
        Stage = label; float end = Time.realtimeSinceStartup + seconds;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
    }
    private static void Fatal(Player player) => player.Health.ReceiveDamage(new DamageRequest
        { RequestedDamage = 10000, BaseDamage = 10000, DamageType = DamageType.Environment });
    private static Vector3 origin = new Vector3(1200, 1200, 1200);

    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無其他 Runner 的 Play Mode。");
        Status = "執行中"; report.Clear(); holdE = false; holdHostE = false;
        session = "rescue-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var logic = CreateLogic(host);
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null, "host player");
            client = await Connect(false); CreateLogic(client);
            await Until(() => client.TryGetPlayerObject(host.LocalPlayer, out _) &&
                host.TryGetPlayerObject(client.LocalPlayer, out _) && client.TryGetPlayerObject(client.LocalPlayer, out _), "replicas");
            var victim = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            var helper = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            var replica = PlayerAbilityQualification.FindPlayer(client, host.LocalPlayer);
            Field(victim.Health, "debugHealth", false); Field(helper.Health, "debugHealth", false);
            await driver.AtTick(() =>
            {
                victim.Movement.KCC.SetPosition(origin);
                helper.Movement.KCC.SetPosition(origin + Vector3.right * 1.5f);
            });
            await Task.Delay(300);
            NetworkObject enemy = null;
            await driver.AtTick(() =>
            {
                var director = new GameObject("Rescue test alert director", typeof(EnemyAlertDirector)); temporary.Add(director); host.MoveToRunnerScene(director);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_C_Beam.prefab").GetComponent<NetworkObject>();
                enemy = host.Spawn(prefab, origin + Vector3.forward * 40, Quaternion.identity, onBeforeSpawned: (r, obj) =>
                {
                    obj.GetComponent<EnemyCombatDecisionController>().enabled = false;
                    Field(obj.GetComponent<EnemyPerceptionController>(), "sightDistance", .01f);
                    foreach (var option in obj.GetComponents<EnemyCombatOption>())
                        typeof(EnemyCombatOption).GetField("maximumStartDistance", Private).SetValue(option, .01f);
                });
                enemy.GetComponent<EnemyPerceptionController>().TryAcceptSharedTarget(victim.Object, victim.transform.position);
            });
            var perception = enemy.GetComponent<EnemyPerceptionController>();
            int deaths = 0; victim.Health.Died += () => deaths++;
            await driver.AtTick(() => Fatal(victim));
            await Until(() => replica.Health.IsDowned, "downed replicated");
            Check(!perception.HasTarget, "downed immediately clears enemy chase target");
            lateClient = await Connect(false); CreateLogic(lateClient);
            await Until(() => lateClient.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null && p.GetComponent<PlayerHealth>().IsDowned,
                "late join sees existing downed snapshot");
            Check(true, "late join receives downed lifetime without replaying death");
            await lateClient.Shutdown(); lateClient = null;
            await Until(() => host.ActivePlayers.Count() == 2, "late join departure acknowledged by host");
            Check(!victim.Health.IsAlive && !victim.Health.IsDead && deaths == 0, "lethal hit downs without Died or despawn");
            Check(victim.Health.DownedRemainingSeconds > 0 && victim.Health.DownedRemainingSeconds <= 30,
                "default 30 second downed lifetime");
            Check(PlayerRescueRules.RequiresExtraction(victim.Health.IsDead), "downed remains extraction required");
            var countArgs = new object[] { null, 0, 0, 0 };
            typeof(Purgers.GameFlow.Stage.StageFlowController).GetMethod("CollectLivingPlayerState", Private).Invoke(
                logic.GetComponent<Purgers.GameFlow.Stage.StageFlowController>(), countArgs);
            Check((int)countArgs[2] == 2, "actual stage extraction counts downed and healthy members: " + countArgs[2]);
            Check(!replica.Health.KillUnrescuable(), "client cannot authoritatively kill or revive");
            await driver.AtTick(() => Fatal(victim));
            Check(victim.Health.IsDowned && deaths == 0, "additional damage cannot kill downed player");
            holdE = true;
            await Until(() => victim.Health.RescueElapsedSeconds > .5f, "client E starts rescue");
            float paused = victim.Health.DownedRemainingSeconds;
            await Task.Delay(350);
            Check(Mathf.Abs(victim.Health.DownedRemainingSeconds - paused) < .02f, "bleedout clock paused while rescuing; before=" + paused + " after=" + victim.Health.DownedRemainingSeconds + " rescuer=" + victim.Health.Rescuer + " progress=" + victim.Health.RescueElapsedSeconds + " reason=" + victim.Health.RescueDebugLastInterruption);
            Check(replica.Health.Rescuer != null && replica.Health.RescueProgress > 0, "rescuer and progress replicate to client");
            await driver.AtTick(() => helper.Health.ReceiveDamage(new DamageRequest { RequestedDamage = 10, BaseDamage = 10, DamageType = DamageType.Environment }));
            Check(victim.Health.Rescuer == helper.Object && victim.Health.RescueElapsedSeconds > .5f, "rescuer damage does not cancel");
            holdE = false;
            await Until(() => victim.Health.Rescuer == null, "release cancels");
            Check(victim.Health.RescueElapsedSeconds == 0, "release resets progress");
            holdE = true;
            await Until(() => victim.Health.RescueElapsedSeconds > .2f, "second rescue");
            await driver.AtTick(() => helper.Movement.KCC.SetPosition(origin + Vector3.right * 10));
            await Until(() => victim.Health.Rescuer == null, "distance cancels");
            Check(victim.Health.RescueElapsedSeconds == 0, "distance resets progress");
            holdE = false;
            await driver.AtTick(() => helper.Movement.KCC.SetPosition(origin + Vector3.right * 1.5f));
            await Task.Delay(150); holdE = true;
            await Until(() => victim.Health.RescueElapsedSeconds > .2f, "third rescue");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall); host.MoveToRunnerScene(wall);
            wall.transform.position = origin + new Vector3(.75f, 0, 0); wall.transform.localScale = new Vector3(.1f, 5, 5);
            await Until(() => victim.Health.Rescuer == null, "wall cancels");
            Check(victim.Health.RescueElapsedSeconds == 0, "occlusion resets progress");
            UnityEngine.Object.Destroy(wall); holdE = false; await Task.Delay(150);
            holdE = true;
            await Until(() => victim.Health.RescueElapsedSeconds > .1f, "final rescue");
            float started = Time.realtimeSinceStartup;
            await Until(() => victim.Health.IsAlive, "five seconds revive");
            Check(Time.realtimeSinceStartup - started > 4.5f, "full uninterrupted five second rescue required");
            Check(Mathf.Abs(victim.Health.CurrentHealth - 35) < .01f && !victim.Health.IsDowned && deaths == 0,
                "revive retains same object with 35 percent maximum HP");
            Check(victim.Health.PreviousAggroRecoveryTimer.RemainingTime(host) > 4.5f, "revive starts five second previous aggro delay");
            await driver.AtTick(() =>
            {
                var input = default(NetInput); input.Buttons.Set(InputButton.Ability1, true);
                helper.Health.FilterRescueInput(ref input);
                Check(!input.Buttons.IsSet(InputButton.Ability1), "held rescue E cannot leak into ability after revive");
            });
            holdE = false;
            await Task.Delay(3500);
            Check(perception.CurrentTarget != victim.Object, "old aggro does not relock before five seconds without detection");
            await Until(() => perception.CurrentTarget == victim.Object, "old aggro relocks after five seconds", 5);
            Check(true, "previous enemy relocks at five seconds");
            await driver.AtTick(() =>
            {
                Field(victim.Health, "rescueDurationSeconds", .3f);
                Fatal(victim);
            });
            holdE = true;
            await Until(() => victim.Health.IsAlive, "secondary sight override setup");
            await driver.AtTick(() =>
            {
                Field(perception, "sightDistance", 100f); Field(perception, "horizontalFieldOfView", 360f); Field(perception, "verticalFieldOfView", 180f);
            });
            await Until(() => perception.CurrentTarget == victim.Object, "sight overrides delay", 2);
            Check(victim.Health.PreviousAggroRecoveryTimer.RemainingTime(host) > 2f, "normal sight overrides five second delay early");
            holdE = false;
            await Task.Delay(100);
            await driver.AtTick(() => { Field(perception, "sightDistance", .01f); Fatal(victim); });
            holdE = true;
            await Until(() => victim.Health.IsAlive, "secondary attack distance setup");
            await driver.AtTick(() =>
            {
                foreach (var option in enemy.GetComponents<EnemyCombatOption>())
                    typeof(EnemyCombatOption).GetField("maximumStartDistance", Private).SetValue(option, 100f);
            });
            await Until(() => perception.CurrentTarget == victim.Object, "attack distance overrides delay", 2);
            Check(victim.Health.PreviousAggroRecoveryTimer.RemainingTime(host) > 2f, "unobstructed attack distance overrides delay outside sight");
            holdE = false;
            await Task.Delay(100);
            await driver.AtTick(() => { Field(victim.Health, "rescueDurationSeconds", 20f); Fatal(victim); });
            var canvas = new GameObject("Rescue HUD verification", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler)); temporary.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; canvas.GetComponent<Canvas>().sortingOrder = 1000;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerRescueHUD.prefab");
            var hud = UnityEngine.Object.Instantiate(hudPrefab, canvas.transform); Field(hud.GetComponent<LocalPlayerRescueHUD>(), "runner", client);
            await Task.Delay(300);
            holdE = true;
            await Until(() => victim.Health.RescueElapsedSeconds > .1f, "UI rescue presentation");
            Directory.CreateDirectory("Temp/PlayerRescueVerification"); ScreenCapture.CaptureScreenshot("Temp/PlayerRescueVerification/rescue-hud.png");
            await Task.Delay(500);
            UnityEngine.Object.Destroy(canvas);
            await driver.AtTick(() => host.Despawn(enemy));
            holdE = false;
            await driver.AtTick(() => victim.Movement.KCC.SetPosition(new Vector3(origin.x, logic.FallDeathWorldY - 1, origin.z)));
            await Until(() => !host.TryGetPlayerObject(host.LocalPlayer, out _), "below map causes immediate true death");
            Check(deaths == 1, "out of map death emits Died once");
            await Task.Delay(3500);
            Check(!host.TryGetPlayerObject(host.LocalPlayer, out _), "combat death does not use original automatic respawn timer");
            Check(!PlayerRescueRules.RequiresExtraction(true), "dead excluded from extraction");
            typeof(Purgers.GameFlow.Stage.StageFlowController).GetMethod("CollectLivingPlayerState", Private).Invoke(
                logic.GetComponent<Purgers.GameFlow.Stage.StageFlowController>(), countArgs);
            Check((int)countArgs[2] == 1, "actual stage extraction excludes despawned dead member");
            await driver.AtTick(() => Fatal(helper));
            await Until(() => helper.Health.IsDowned, "last healthy member downed");
            float lastDown = Time.realtimeSinceStartup;
            await Until(() => !host.TryGetPlayerObject(client.LocalPlayer, out _), "no healthy members die after two seconds");
            Check(Time.realtimeSinceStartup - lastDown >= 1.8f, "team wipe waits two seconds rather than immediate death");
            // 驗證保留的原自動復活分支；正式安全屋啟用判定另外由 SafeHouseFlowController 負責。
            Field(logic, "useTeammateRescueOutsideSafeHouse", false);
            await driver.AtTick(() => { logic.PrepareForAuthoritativeSceneTransition(); });
            // 透過既有切場回呼重建連線玩家，不改存檔、不載入正式場景。
            await driver.AtTick(() => { var args = default(SceneLoadDoneArgs); logic.SceneLoadDone(in args); });
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null, "scene reconciliation revives dead players");
            var restored = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            Check(restored.Health.IsAlive && restored.Health.CurrentHealth == restored.Health.MaximumHealth,
                "scene return creates healthy player");
            var previous = restored.Object.Id;
            await driver.AtTick(() => Fatal(restored));
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null && p.Id != previous, "retained automatic respawn mode");
            Check(true, "original automatic respawn path still works");
            Field(logic, "useTeammateRescueOutsideSafeHouse", true);
            var timedVictim = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            await driver.AtTick(() => Fatal(timedVictim));
            float downAt = Time.realtimeSinceStartup;
            Check(timedVictim.Health.DownedRemainingSeconds > 29.9f, "natural timeout starts at full thirty seconds");
            await Until(() => !host.TryGetPlayerObject(host.LocalPlayer, out _), "natural thirty second timeout", 36);
            Check(Time.realtimeSinceStartup - downAt >= 29.5f, "healthy teammate present does not prevent thirty second death");
            await driver.AtTick(() => { CreateSafeHouseFlow(host); });
            CreateSafeHouseFlow(client);
            await Until(() => Purgers.GameFlow.SafeHouse.SafeHouseFlowController.IsActiveFor(host) &&
                Purgers.GameFlow.SafeHouse.SafeHouseFlowController.IsActiveFor(client), "safehouse flow replicated");
            Check(!logic.UsesTeammateRescue, "real SafeHouseFlow overrides enabled rescue mode");
            await driver.AtTick(() => { logic.PrepareForAuthoritativeSceneTransition(); var args = default(SceneLoadDoneArgs); logic.SceneLoadDone(in args); });
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null, "safehouse restores dead member");
            var safePlayer = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            previous = safePlayer.Object.Id;
            await driver.AtTick(() => Fatal(safePlayer));
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null && p.Id != previous, "safehouse automatic resurrection");
            Check(true, "safehouse automatically respawns without downed rescue");
            Status = "通過 " + report.Count;
        }
        catch (Exception ex) { Status = "失敗 " + ex.Message; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            holdE = false; holdHostE = false;
            if (lateClient != null && lateClient.IsRunning) await lateClient.Shutdown();
            if (client != null && client.IsRunning) await client.Shutdown();
            if (host != null && host.IsRunning) await host.Shutdown();
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj);
            temporary.Clear();
            Directory.CreateDirectory("Temp/PlayerRescueVerification");
            File.WriteAllLines("Temp/PlayerRescueVerification/host-client.txt", report);
        }
    }

    private static GameLogic CreateLogic(NetworkRunner runner)
    {
        var root = new GameObject("Temporary rescue GameLogic", typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        var logic = root.AddComponent<GameLogic>();
        root.AddComponent<Purgers.GameFlow.Stage.StageFlowController>();
        Field(logic, "playerPrefab", NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        Field(logic, "debugPlayerLifecycle", false); Field(logic, "fallbackSpawnPosition", origin);
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(floor); runner.MoveToRunnerScene(floor);
        floor.transform.position = origin + Vector3.down * 2; floor.transform.localScale = new Vector3(100, 1, 100);
        floor.layer = LayerMask.NameToLayer("Ground") < 0 ? 0 : LayerMask.NameToLayer("Ground");
        return logic;
    }
    private static void CreateSafeHouseFlow(NetworkRunner runner)
    {
        var root = new GameObject("Temporary rescue SafeHouseFlow", typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        root.AddComponent<Purgers.GameFlow.SafeHouse.SafeHouseFlowController>();
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(2), new[] { root.GetComponent<NetworkObject>() }, default);
    }

    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Rescue host" : "Rescue client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => { var data = default(NetInput); data.Buttons.Set(InputButton.Ability1, server ? holdHostE : r == client && holdE); input.Set(data); });
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var scene = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); scene.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = server ? GameMode.Host : GameMode.Client, SessionName = session,
            Config = config, SceneManager = scene, CustomPhotonAppSettings = app, PlayerCount = 4, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner tick");
        return runner;
    }
}
#endif
