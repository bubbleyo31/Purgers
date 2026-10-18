using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>隔離 Runner 的受控血條驗證，不保存正式場景、敵人 Prefab 或存檔。</summary>
public static class EnemyHealthBarRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly List<string> report = new List<string>();
    private static readonly Vector3 Origin = new Vector3(1500, 1500, 1500);
    private static string session;
    private static bool multiplayer;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); report.Add("PASS " + label); }
    private static async Task Until(Func<bool> condition, string label)
    {
        Stage = label; float end = Time.realtimeSinceStartup + 35;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
        report.Add("PASS " + label);
    }

    public static async void Begin(bool network = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需在沒有 Runner 的隔離 Play Mode 執行。");
        Status = "執行中"; report.Clear(); multiplayer = network; session = "enemy-health-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var target = CreateTarget(host); var hp = target.GetComponent<TestDamageReceiver>();
            var hud = CreateHud(host, out var camera);
            await Until(() => target.HasSnapshot && hud.VisibleBarCount == 0, "initial full health stays hidden");
            await driver.AtTick(() => hp.ReceiveDamage(Damage(24)));
            await Until(() => hud.TryGetPresentation(target, out _, out _, out float r) && Mathf.Abs(r - .76f) < .0001f,
                "authoritative damage immediately presents 76 percent");
            await Capture(camera, "normal-76");
            EnemyHealthBarTarget replica = null; LocalEnemyHealthBarHUD clientHud = null; Camera clientCamera = null;
            if (network)
            {
                client = await Connect(false); replica = CreateTarget(client);
                clientHud = CreateHud(client, out clientCamera);
                await Until(() => clientHud.TryGetPresentation(replica, out _, out _, out float r) && Mathf.Abs(r - .76f) < .0001f,
                    "late join reads existing 76 percent snapshot without historical RPC");
                Check(!replica.Object.HasStateAuthority, "client is not health authority");
                replica.GetComponent<TestDamageReceiver>().ReceiveDamage(Damage(20)); await Task.Delay(100);
                Check(Mathf.Abs(hp.CurrentHealth - 76) < .001f && Mathf.Abs(replica.GetComponent<TestDamageReceiver>().CurrentHealth - 76) < .001f,
                    "client damage request cannot mutate health");
                Check(!hud.TryGetPresentation(replica, out _, out _, out _) && !clientHud.TryGetPresentation(target, out _, out _, out _),
                    "HUDs never mix Runner targets");
                clientCamera.transform.rotation = Quaternion.Euler(0, 180, 0);
                await Until(() => clientHud.VisibleBarCount == 0 && hud.VisibleBarCount == 1, "each peer independently hides offscreen enemy");
                clientCamera.transform.rotation = Quaternion.identity;
                await Until(() => clientHud.VisibleBarCount == 1, "returning camera restores client bar");
            }
            Set(target, "displayMode", EnemyHealthBarTarget.DisplayMode.Large);
            Set(target, "bodyCenter", new Vector3(0, 5, 0)); Set(target, "bodySize", new Vector3(4, 10, 4));
            var visual = target.transform.Find("Body"); visual.localPosition = new Vector3(0, 5, 0); visual.localScale = new Vector3(4, 10, 4);
            camera.transform.position = Origin + new Vector3(0, 1, -4);
            await Until(() => hud.TryGetPresentation(target, out _, out bool fallback, out _) && fallback, "large body remains visible when head leaves frame");
            await Capture(camera, "large-head-outside");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall); host.MoveToRunnerScene(wall);
            wall.name = "Temporary occluding wall"; wall.transform.position = Origin + new Vector3(0, 3, -2.5f);
            wall.transform.localScale = new Vector3(20, 20, .3f); Physics.SyncTransforms();
            await Until(() => hud.VisibleBarCount == 0, "opaque wall immediately hides large enemy bar");
            if (network) Check(clientHud.VisibleBarCount == 1, "host physics wall does not occlude client physics scene");
            await Capture(camera, "wall-hidden");
            wall.SetActive(false); Physics.SyncTransforms();
            await Until(() => hud.VisibleBarCount == 1, "bar returns after wall clears");
            Set(target, "displayMode", EnemyHealthBarTarget.DisplayMode.Hidden);
            await Until(() => hud.VisibleBarCount == 0, "explicit hidden mode excludes inherited Boss presentation");
            Set(target, "displayMode", EnemyHealthBarTarget.DisplayMode.Large);
            await Until(() => hud.VisibleBarCount == 1, "normal presentation can resume");
            if (network)
            {
                UnityEngine.Object.Destroy(clientHud.transform.root.gameObject); UnityEngine.Object.Destroy(clientCamera.gameObject);
                await client.Shutdown(); client = null;
                await Until(() => hud.VisibleBarCount == 1, "client disconnect leaves host presentation intact");
                client = await Connect(false); replica = CreateTarget(client); clientHud = CreateHud(client, out clientCamera);
                await Until(() => clientHud.TryGetPresentation(replica, out _, out _, out float r) && Mathf.Abs(r - .76f) < .0001f,
                    "reconnect initializes current health instead of stale UI state");
            }
            await driver.AtTick(() => hp.ReceiveDamage(Damage(1000)));
            await Until(() => hud.VisibleBarCount == 0 && (!network || clientHud.VisibleBarCount == 0), "death hides bars on every peer");
            await driver.AtTick(() => host.Despawn(target.Object));
            await Until(() => !EnemyHealthBarTarget.Active.Any(t => t != null && t.Runner == host), "despawn removes registered target");
            Check(hud.VisibleBarCount == 0, "despawn leaves no orphan bar");
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogException(ex); }
        finally
        {
            try { if (client != null) await client.Shutdown(); if (host != null) await host.Shutdown(); }
            finally
            {
                client = host = null;
                foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item);
                temporary.Clear(); Directory.CreateDirectory("Temp/EnemyHealthBars");
                File.WriteAllLines("Temp/EnemyHealthBars/" + (network ? "host-client" : "single") + ".txt", report);
            }
        }
    }

    /// <summary>10 秒生命變化窗：真實牆鐘等待，包含 Host/Client、晚加入及 UI 停用後恢復。</summary>
    public static async void BeginActivity(bool network = true)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需在沒有 Runner 的隔離 Play Mode 執行。");
        Status = "執行中"; report.Clear(); multiplayer = network; session = "health-activity-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var target = CreateTarget(host); var hp = target.GetComponent<TestDamageReceiver>();
            var hud = CreateHud(host, out var camera);
            await Until(() => target.HasSnapshot && hud.VisibleBarCount == 0, "initial full enemy stays hidden");
            await driver.AtTick(() => hp.ReceiveDamage(Damage(24)));
            await Until(() => hud.TryGetPresentation(target, out _, out _, out float r) && Mathf.Abs(r - .76f) < .001f,
                "authoritative damage opens percentage bar");
            LocalEnemyHealthBarHUD remoteHud = null;
            if (network)
            {
                // 晚加入發生在 Host 的顯示窗過期後，驗證本機期限不必同步歷史傷害時間。
                Stage = "host ten second quiet period"; await Task.Delay(10500);
                Check(hud.VisibleBarCount == 0, "host hides damaged enemy after ten seconds");
                client = await Connect(false); var replica = CreateTarget(client);
                remoteHud = CreateHud(client, out _);
                await Until(() => remoteHud.TryGetPresentation(replica, out _, out _, out float r) && Mathf.Abs(r - .76f) < .001f,
                    "late join opens local window for damaged snapshot");
                Check(hud.VisibleBarCount == 0, "late join does not refresh host deadline");
                Check(!replica.Object.HasStateAuthority, "client has no health authority");
                replica.GetComponent<TestDamageReceiver>().ReceiveDamage(Damage(10)); await Task.Delay(150);
                Check(Mathf.Abs(hp.CurrentHealth - 76) < .001f, "client cannot change health or wake host bar");
            }
            Stage = "all peers ten second quiet period"; await Task.Delay(10500);
            Check(hud.VisibleBarCount == 0 && (!network || remoteHud.VisibleBarCount == 0), "unchanged injured snapshots expire on all peers");
            hud.enabled = false; await Task.Delay(100); hud.enabled = true; await Task.Delay(150);
            Check(hud.VisibleBarCount == 0, "HUD reenable cannot restart expired deadline");
            await driver.AtTick(() => hp.ReceiveDamage(Damage(1)));
            await Until(() => hud.VisibleBarCount == 1 && (!network || remoteHud.VisibleBarCount == 1), "new damage reopens bars on all peers");
            Stage = "damage refresh boundary"; await Task.Delay(5500);
            await driver.AtTick(() => hp.ReceiveDamage(Damage(1))); await Task.Delay(5500);
            Check(hud.VisibleBarCount == 1 && (!network || remoteHud.VisibleBarCount == 1), "second damage extends window beyond first hit deadline");
            await driver.AtTick(() => hp.ReceiveHeal(new HealRequest { RequestedHeal = 100 }));
            await Until(() => hud.TryGetPresentation(target, out _, out _, out float r) && r == 1,
                "healing to full stays visible as a fresh change");
            Stage = "healed full health quiet period"; await Task.Delay(10500);
            Check(hud.VisibleBarCount == 0 && (!network || remoteHud.VisibleBarCount == 0), "healed full health hides after ten unchanged seconds");
            await driver.AtTick(() => hp.ReceiveDamage(Damage(1)));
            await Until(() => hud.VisibleBarCount == 1 && (!network || remoteHud.VisibleBarCount == 1), "hidden full enemy reopens after new damage");
            await driver.AtTick(() => hp.ReceiveDamage(Damage(1000)));
            await Until(() => hud.VisibleBarCount == 0 && (!network || remoteHud.VisibleBarCount == 0), "death hides immediately before timeout");
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogException(ex); }
        finally
        {
            try { if (client != null) await client.Shutdown(); if (host != null) await host.Shutdown(); }
            finally
            {
                client = host = null;
                foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item);
                temporary.Clear(); Directory.CreateDirectory("Temp/EnemyHealthBarActivity");
                File.WriteAllLines("Temp/EnemyHealthBarActivity/" + (network ? "host-client" : "single") + ".txt", report);
            }
        }
    }
    private static DamageRequest Damage(float amount) => new DamageRequest
    { RequestedDamage = amount, BaseDamage = amount, DamageType = DamageType.Environment, HeadshotDamageMultiplier = 1 };

    private static EnemyHealthBarTarget CreateTarget(NetworkRunner runner)
    {
        var root = new GameObject("Temporary health target", typeof(NetworkObject), typeof(TestDamageReceiver), typeof(EnemyHealthBarTarget));
        temporary.Add(root); runner.MoveToRunnerScene(root); root.transform.position = Origin;
        var health = root.GetComponent<TestDamageReceiver>(); Set(health, "debugHealth", false); Set(health, "debugAuthorityWarning", false);
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up; body.transform.localScale = new Vector3(1, 2, 1);
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        return root.GetComponent<EnemyHealthBarTarget>();
    }

    private static LocalEnemyHealthBarHUD CreateHud(NetworkRunner runner, out Camera camera)
    {
        var camRoot = new GameObject("Temporary enemy health camera", typeof(Camera)); temporary.Add(camRoot); runner.MoveToRunnerScene(camRoot);
        camera = camRoot.GetComponent<Camera>(); camera.transform.position = Origin + new Vector3(0, 1, -8);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .12f);
        var rt = new RenderTexture(960, 540, 24); temporary.Add(rt); camera.targetTexture = rt;
        var canvasRoot = new GameObject("Temporary enemy health canvas", typeof(Canvas)); temporary.Add(canvasRoot); runner.MoveToRunnerScene(canvasRoot);
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyHealthBarSetup.PrefabPath);
        var obj = UnityEngine.Object.Instantiate(prefab, canvasRoot.transform);
        var hud = obj.GetComponent<LocalEnemyHealthBarHUD>();
        hud.Configure(AssetDatabase.LoadAssetAtPath<EnemyHealthBarSettings>(EnemyHealthBarSetup.SettingsPath), runner, camera);
        return hud;
    }

    private static async Task Capture(Camera camera, string name)
    {
        await Task.Delay(150); Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        try { RenderTexture.active = camera.targetTexture; image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
            Directory.CreateDirectory("Temp/EnemyHealthBars"); File.WriteAllBytes("Temp/EnemyHealthBars/" + name + ".png", image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(image); }
    }

    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Health test Host" : "Health test Client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true; var events = runner.gameObject.AddComponent<NetworkEvents>();
        events.OnInput = new NetworkEvents.InputEvent(); events.OnInput.AddListener((r, input) => input.Set(default(NetInput)));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = multiplayer ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 2, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "Runner first tick"); return runner;
    }
}
