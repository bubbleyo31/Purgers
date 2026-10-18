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

/// <summary>手動觸發的隔離多人觀戰驗證；只改 Play Mode 實例，不保存場景、資產或玩家進度。</summary>
public static class SpectatorRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static string session;
    private static bool aim;
    private static GameObject hiddenMenu;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.Add("PASS " + message); }
    private static async Task Until(Func<bool> predicate, string label)
    {
        Stage = label; float end = Time.realtimeSinceStartup + 40;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
    }
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);

    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要沒有其他 Runner 的 Play Mode。");
        Status = "執行中"; report.Clear(); aim = false;
        session = "spectator-" + Guid.NewGuid().ToString("N");
        try
        {
            hiddenMenu = GameObject.Find("GameplayHUD Canvas/Menu");
            if (hiddenMenu != null) hiddenMenu.SetActive(false);
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var server = CreateLogic(host);
            Field(server, "respawnDelay", 18f);
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null, "host player");
            client = await Connect(false); var replica = CreateLogic(client);
            await Until(() => host.TryGetPlayerObject(client.LocalPlayer, out _) && client.TryGetPlayerObject(client.LocalPlayer, out _) &&
                client.TryGetPlayerObject(host.LocalPlayer, out _), "replicated players");
            // 同 Editor 共用一組相機，驗證固定以 Client 為觀看端；Host 呈現元件停用，權威模擬照常。
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Player>())
                if (p.Runner == host)
                {
                    p.GetComponent<PlayerLocalView>().enabled = false;
                    foreach (var a in p.GetComponentsInChildren<PlayerAimController>()) a.enabled = false;
                }
            var spectator = UnityEngine.Object.FindObjectOfType<LocalPlayerSpectatorController>();
            Check(spectator != null && CameraFollow.Singleton != null, "existing scene camera and spectator reused");
            Field(spectator, "runner", client);
            foreach (var hud in UnityEngine.Object.FindObjectsOfType<LocalPlayerHealthSlider>(true)) Field(hud, "runner", client);
            foreach (var hud in UnityEngine.Object.FindObjectsOfType<LocalPlayerWeaponHUD>(true)) Field(hud, "runner", client);
            var victim = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            var target = PlayerAbilityQualification.FindPlayer(client, host.LocalPlayer);
            var source = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            await Until(() => target.ProfessionRuntimeManager != null && target.ProfessionRuntimeManager.CurrentRuntimeObject != null,
                "target weapon runtime replicated");
            var targetProfession = target.GetComponent<PlayerProfession>();
            var eye = target.Movement.CamTarget;
            var renderer = target.GetComponent<TeammateVisualAnchor>().bodyRenderers[0];
            var originalMode = renderer.shadowCastingMode;
            var oldVictimId = victim.Object.Id;
            await Task.Delay(500);
            await driver.AtTick(() =>
            {
                source.Health.ReceiveDamage(new DamageRequest { RequestedDamage = 25, BaseDamage = 25, DamageType = DamageType.Environment });
                source.Movement.AddLookRotationImpulse(-25, 40);
                victim.Health.ReceiveDamage(new DamageRequest { RequestedDamage = 10000, BaseDamage = 10000, DamageType = DamageType.Environment });
            });
            await Until(() => spectator.IsSpectating && spectator.SpectatedPlayer == host.LocalPlayer, "client enters first person spectator");
            await Task.Delay(250);
            Check(LocalPlayerSpectatorController.TryGetViewedPlayerObject(client, out var viewed) && viewed == target.Object,
                "client HUD source resolves host replica");
            Check(!LocalPlayerSpectatorController.IsViewedObject(null), "missing object cannot be presented");
            Check(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, "observed body hidden with shadows retained");
            var manager = ProfessionViewModelManager.Singleton;
            Check(manager != null && manager.HasViewModel, "observed player hand and weapon instantiated");
            Check((PlayerProfession)typeof(ProfessionViewModelManager).GetField("targetProfession", Private).GetValue(manager) == targetProfession,
                "ViewModel reads observed profession");
            Check((bool)typeof(ProfessionViewModelManager).GetField("observedPresentationOnly", Private).GetValue(manager),
                "observer uses read-only weapon binding");
            var healthHud = UnityEngine.Object.FindObjectOfType<LocalPlayerHealthSlider>();
            Check(healthHud != null && (PlayerHealth)typeof(LocalPlayerHealthSlider).GetField("boundHealth", Private).GetValue(healthHud) == target.Health,
                "health HUD binds target health");
            var weaponHud = UnityEngine.Object.FindObjectOfType<LocalPlayerWeaponHUD>();
            var args = new object[] { null };
            Check(weaponHud != null && (bool)typeof(LocalPlayerWeaponHUD).GetMethod("TryResolveLocalRuntime", Private).Invoke(weaponHud, args) &&
                (NetworkObject)args[0] == target.ProfessionRuntimeManager.CurrentRuntimeObject, "ammo HUD resolves target runtime");
            Check(target.Health.CurrentHealth == 75f, "target damage snapshot replicated without local HP writes");
            var rig = CameraFollow.Singleton;
            float maxPositionError = 0, maxAngleError = 0;
            for (int i = 0; i < 20; i++)
            {
                typeof(LocalPlayerSpectatorController).GetMethod("LateUpdate", Private).Invoke(spectator, null);
                typeof(CameraFollow).GetMethod("LateUpdate", Private).Invoke(rig, null);
                maxPositionError = Mathf.Max(maxPositionError, Vector3.Distance(rig.transform.position, eye.position));
                maxAngleError = Mathf.Max(maxAngleError, Quaternion.Angle(rig.transform.rotation, eye.rotation));
                await Task.Delay(25);
            }
            Check(maxPositionError < .001f && maxAngleError < .01f, "20 first-person poses preserve pitch/yaw with no parent-child drift");
            aim = true;
            await Until(() => target.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponentInChildren<PlayerAimController>().IsAiming, "observed ADS replicated");
            await Task.Delay(350);
            Check(Camera.main != null && Camera.main.fieldOfView < 70f, "observed ADS drives existing camera FOV");
            aim = false;
            Stage = "畫面擷取窗口";
            Directory.CreateDirectory("Temp/SpectatorVerification");
            ScreenCapture.CaptureScreenshot("Temp/SpectatorVerification/observed-client.png");
            await Task.Delay(4000);
            await Until(() => client.TryGetPlayerObject(client.LocalPlayer, out var p) && p != null && p.Id != oldVictimId, "existing automatic respawn remains available");
            await Until(() => !spectator.IsSpectating, "respawn exits spectator");
            Check(renderer.shadowCastingMode == originalMode, "exit restores observed body's previous render mode");
            Check(LocalPlayerSpectatorController.TryGetViewedPlayerObject(client, out var own) && own.InputAuthority == client.LocalPlayer,
                "respawn restores own HUD source");
            Check(!(bool)typeof(ProfessionViewModelManager).GetField("observedPresentationOnly", Private).GetValue(manager),
                "respawn restores normal local weapon binding");
            // 再次死亡後，讓被觀戰者死亡，檢查沒有目標時不殘留模型、HUD 或回授來源。
            victim = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            await driver.AtTick(() => victim.Health.ReceiveDamage(new DamageRequest { RequestedDamage = 10000, BaseDamage = 10000, DamageType = DamageType.Environment }));
            await Until(() => spectator.IsSpectating && spectator.SpectatedPlayer == host.LocalPlayer, "second spectator entry");
            await driver.AtTick(() => source.Health.ReceiveDamage(new DamageRequest { RequestedDamage = 10000, BaseDamage = 10000, DamageType = DamageType.Environment }));
            await Until(() => spectator.SpectatedPlayer == PlayerRef.None, "target death releases camera owner");
            await Task.Delay(150);
            Check(!LocalPlayerSpectatorController.TryGetViewedPlayerObject(client, out _), "no target returns no HUD source");
            Check(!manager.HasViewModel, "despawned target leaves no first person weapon behind");
            Check((PlayerHealth)typeof(LocalPlayerHealthSlider).GetField("boundHealth", Private).GetValue(healthHud) == null,
                "no target clears old health HUD binding");
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogException(ex); }
        finally
        {
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item);
            temporary.Clear();
            if (hiddenMenu != null) hiddenMenu.SetActive(true);
            hiddenMenu = null;
            Directory.CreateDirectory("Temp/SpectatorVerification");
            File.WriteAllLines("Temp/SpectatorVerification/host-client.txt", report);
        }
    }

    private static GameLogic CreateLogic(NetworkRunner runner)
    {
        var root = new GameObject("Temporary spectator GameLogic", typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        var logic = root.AddComponent<GameLogic>();
        Field(logic, "playerPrefab", NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        Field(logic, "debugPlayerLifecycle", false);
        Field(logic, "useTeammateRescueOutsideSafeHouse", false);
        Field(logic, "fallbackSpawnPosition", new Vector3(1200, 1200, 1200));
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(floor); runner.MoveToRunnerScene(floor);
        floor.transform.position = new Vector3(1200, 1198, 1200); floor.transform.localScale = new Vector3(100, 1, 100);
        floor.layer = LayerMask.NameToLayer("Ground") < 0 ? 0 : LayerMask.NameToLayer("Ground");
        return logic;
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Spectator host" : "Spectator client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => { var data = default(NetInput); data.Buttons.Set(InputButton.Aim, server && aim); input.Set(data); });
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
