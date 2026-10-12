#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>明確啟動的跳字權威／本機呈現驗證；僅建立臨時 Runner 與 HUD，不保存正式場景。</summary>
public static class DamageNumberRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static string session;
    private static bool multiplayer;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        report.Add("PASS " + message);
    }
    private static async Task Until(Func<bool> condition, string stage)
    {
        Stage = stage; float end = Time.realtimeSinceStartup + 30f;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(stage); await Task.Delay(20); }
    }
    public static async void Begin(bool network = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("只可在沒有現存 Runner 的 Play Mode 執行。");
        multiplayer = network; Status = "執行中"; report.Clear(); session = "damage-number-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TestEnemy.prefab").GetComponent<NetworkObject>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); host.MoveToRunnerScene(floor); temporary.Add(floor);
            floor.transform.position = new Vector3(1000f, 99f, 1000f); floor.transform.localScale = new Vector3(200f, 1f, 200f);
            var local = host.Spawn(playerPrefab, new Vector3(1000f, 100f, 1000f), Quaternion.identity, host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer, local);
            var enemy = host.Spawn(targetPrefab, new Vector3(1000f, 100f, 1020f), Quaternion.identity);
            var receiver = enemy.GetComponent<TestDamageReceiver>();
            var hostRelay = local.GetComponent<PlayerCombatFeedbackRelay>();
            var camObject = new GameObject("Damage Number Verification Camera"); temporary.Add(camObject);
            var camera = camObject.AddComponent<Camera>(); camera.transform.position = new Vector3(1000f, 101f, 1010f);
            camera.transform.rotation = Quaternion.identity; camera.depth = 100f;
            var settings = ScriptableObject.CreateInstance<DamageNumberSettings>(); temporary.Add(settings);
            settings.lifetime = 3f;
            var hostHud = CreateHud(settings, host, camera);
            await Until(() => hostHud.BoundRelay == hostRelay, "bind host HUD");
            CombatHitFeedbackData received = default; int hostEvents = 0;
            hostRelay.LocalHitConfirmed += hit => { received = hit; hostEvents++; };
            Vector3 hitPoint = new Vector3(1000f, 101f, 1020f);
            var request = Request(local, enemy, hitPoint, 1);
            float before = receiver.CurrentHealth;
            DamageReceiverUtility.TryApplyDamage(enemy.gameObject, request, out var result);
            hostRelay.ReportAbilityDamage(result);
            await Until(() => hostEvents == 1 && hostHud.ActiveCount == 1, "host hit and popup");
            Check(receiver.CurrentHealth == before - result.AppliedDamage, "authority applies damage exactly once");
            Check(received.IsEnemyTarget && received.TargetKey != 0 && received.HitPoint == hitPoint, "authority hit position and enemy grouping identity arrive intact");
            Check(received.HitZone == DamageHitZoneType.Head && DamageNumberRules.IsHeadHit(received), "physical head zone reaches head style");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall); host.MoveToRunnerScene(wall);
            wall.transform.position = camera.transform.position + Vector3.forward * 5f; wall.transform.localScale = new Vector3(20f, 20f, 1f);
            await Task.Delay(100); Check(hostHud.ActiveCount == 1, "opaque wall does not suppress confirmed damage number");
            var queued = result; queued.Deferred = true;
            hostRelay.ReportAbilityDamage(queued); await Task.Delay(100);
            Check(hostEvents == 1, "deferred result cannot emit a premature popup");
            if (network)
            {
                hostHud.Clear();
                client = await Connect(false);
                await Until(() => client.FindObject(local.Id) != null, "late join host player");
                int leaked = 0;
                client.FindObject(local.Id).GetComponent<PlayerCombatFeedbackRelay>().LocalHitConfirmed += hit => leaked++;
                var remote = host.Spawn(playerPrefab, new Vector3(1003f, 100f, 1000f), Quaternion.identity, client.LocalPlayer);
                host.SetPlayerObject(client.LocalPlayer, remote);
                await Until(() => client.FindObject(remote.Id) != null, "client player snapshot");
                var own = client.FindObject(remote.Id).GetComponent<PlayerCombatFeedbackRelay>();
                var authority = remote.GetComponent<PlayerCombatFeedbackRelay>();
                var clientHud = CreateHud(settings, client, camera);
                await Until(() => clientHud.BoundRelay == own, "client HUD binds Input Authority");
                await Task.Delay(200);
                Check(clientHud.ReceivedEventCount == 0 && clientHud.ActiveCount == 0 && leaked == 0, "late join never replays old popup");
                int clientEvents = 0; CombatHitFeedbackData clientHit = default;
                own.LocalHitConfirmed += hit => { clientEvents++; clientHit = hit; };
                own.ReportAbilityDamage(result); await Task.Delay(100);
                Check(clientEvents == 0, "Input Authority cannot forge confirmed feedback");
                request = Request(remote, enemy, hitPoint, 2);
                request.DamageType = DamageType.DamageOverTime;
                request.HitZone = DamageHitZoneType.Body;
                before = receiver.CurrentHealth;
                DamageReceiverUtility.TryApplyDamage(enemy.gameObject, request, out result); authority.ReportAbilityDamage(result);
                request.Sequence++;
                DamageReceiverUtility.TryApplyDamage(enemy.gameObject, request, out var second); authority.ReportAbilityDamage(second);
                await Until(() => clientEvents == 2 && clientHud.ActiveCount == 1, "client dot ticks merge");
                Check(receiver.CurrentHealth == before - result.AppliedDamage - second.AppliedDamage, "two confirmed ticks reduce HP exactly twice");
                Check(clientHit.AppliedDamage == second.AppliedDamage && clientHit.HitPoint == hitPoint, "client receives actual HP loss and world hit point");
                Check(hostEvents == 1 && hostHud.ReceivedEventCount == 1 && leaked == 0, "damage feedback stays on attacking player's Input Authority");
                var snapshotKey = clientHit.TargetKey;
                var enemyId = enemy.Id;
                host.Despawn(enemy);
                await Until(() => client.FindObject(enemyId) == null, "target despawn");
                Check(clientHud.ActiveCount == 1 && snapshotKey != 0, "target despawn keeps last hit snapshot alive");
                await client.Shutdown(); client = null;
                await Until(() => clientHud.ActiveCount == 0 && clientHud.BoundRelay == null, "disconnect clears owned HUD");
                client = await Connect(false);
                await Until(() => client.FindObject(local.Id) != null, "reconnect host snapshot");
                var reconnectHud = CreateHud(settings, client, camera); await Task.Delay(300);
                Check(reconnectHud.ReceivedEventCount == 0 && reconnectHud.ActiveCount == 0, "reconnect does not replay transient damage");
            }
            else
            {
                host.Despawn(enemy); await Task.Delay(100);
                Check(hostHud.ActiveCount == 1 && hostEvents == 1 && received.HitPoint == hitPoint,
                    "despawn keeps original popup without another hit event");
            }
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError("[DamageNumberRuntimeVerification] " + ex); }
        finally
        {
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item);
            temporary.Clear(); Directory.CreateDirectory("Temp/DamageNumbers");
            File.WriteAllLines("Temp/DamageNumbers/" + (network ? "host-client" : "single") + ".txt", report);
        }
    }
    private static DamageRequest Request(NetworkObject player, NetworkObject target, Vector3 point, int sequence) => new DamageRequest
    {
        RequestedDamage = 2f, BaseDamage = 2f, DamageType = DamageType.Bullet, HitZone = DamageHitZoneType.Head,
        HeadshotDamageMultiplier = 1f, Attacker = player.InputAuthority, SourceObject = player.gameObject, SourceNetworkObject = player,
        HitObject = target.gameObject, HitPoint = point, HitDirection = Vector3.forward, Sequence = sequence
    };
    private static LocalPlayerDamageNumbers CreateHud(DamageNumberSettings settings, NetworkRunner runner, Camera camera)
    {
        var canvas = new GameObject("Damage Number Verification HUD", typeof(RectTransform), typeof(Canvas)); temporary.Add(canvas);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var hud = canvas.AddComponent<LocalPlayerDamageNumbers>(); hud.Configure(settings, runner, camera); return hud;
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Damage Number Host" : "Damage Number Client").AddComponent<NetworkRunner>();
        temporary.Add(runner.gameObject); runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => input.Set(default(NetInput)));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var started = await runner.StartGame(new StartGameArgs { GameMode = multiplayer ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 2, IsVisible = false });
        Check(started.Ok, "Runner starts " + started.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "first simulation tick"); return runner;
    }
}
#endif
