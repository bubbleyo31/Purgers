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

/// <summary>隔離 Runner 的真實 Q 輸入與音效驗證。測試期間固定玩家位置避免連線等待改變物理情境；不保存資產。</summary>
public static class GrappleFeedbackRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool network;
    private static string session;
    private static NetInput hostInput, clientInput;
    private static float peakCameraOffset;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly List<Player> pinned = new List<Player>();
    private static readonly Vector3 Origin = new Vector3(2400, 1000, 2400);
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); report.Add("PASS " + label); }
    private static void Set(object obj, string name, object value)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = obj.GetType().GetField(name, flags);
        if (field != null) field.SetValue(obj, value); else obj.GetType().GetProperty(name, flags).SetValue(obj, value);
    }
    private static async Task Until(Func<bool> test, string stage, float seconds = 20)
    {
        Stage = stage; float end = Time.realtimeSinceStartup + seconds;
        while (!test()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(stage); await Task.Delay(10); }
    }
    private static void Pin(Player p)
    {
        if (p == null || p.Object == null || !p.Object.IsValid) return;
        var k = p.Movement.KCC; k.SetPosition(Origin); k.SetLookRotation(0, 0);
        k.FixedData.BasePosition = k.FixedData.TargetPosition = k.FixedData.DesiredPosition = Origin;
        k.SetDynamicVelocity(Vector3.zero); k.SetKinematicVelocity(Vector3.zero); k.FixedData.IsGrounded = false;
    }
    private static void Energy(Player p, float energy)
    {
        var c = p.GetComponent<PlayerGrappleCharges>(); Set(c, "IsInitialized", (NetworkBool)true);
        Set(c, "MaximumEnergy", 50f); Set(c, "CurrentEnergy", energy);
    }
    private static void Configure(Player p)
    {
        pinned.Add(p); Pin(p); Energy(p, 50f);
        Set(p.Grapple, "grappleMask", (LayerMask)1);
        Set(p.Grapple, "grapplePreFireDelay", .25f);
        Set(p.Grapple, "ropeShootTimeAtMaxDistance", 1f);
        Set(p.Grapple, "grappleOcclusionObstacleMask", (LayerMask)0);
        Set(p.Grapple, "useHoldToMaintainGrappleInput", false);
        Set(p.Grapple, "debugGrapple", false);
    }
    private static T Read<T>(object obj, string name) =>
        (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);

    private static async Task VerifyFadeInterruption(PlayerGrapple authority, PlayerGrapple replica = null)
    {
        Check(Mathf.Approximately(Read<float>(authority, "grapplePullFadeOutDuration"), .15f),
            "production pull fade defaults to 0.15 seconds");
        // 延長測試實例尾音，讓正常收繩與再次出鈎能穩定落在淡出窗口內；不保存 Prefab。
        Set(authority, "grapplePullFadeOutDuration", 2f);
        if (replica != null) Set(replica, "grapplePullFadeOutDuration", 2f);
        int oldHandle = Read<int>(authority, "pullAudioHandle");
        int remoteHandle = replica != null ? Read<int>(replica, "pullAudioHandle") : -1;
        float releaseTime = Time.realtimeSinceStartup;
        var service = GameplayAudioService.Instance;
        await Press(false);
        await Until(() => Read<bool>(authority, "pullAudioFading") &&
            (replica == null || Read<bool>(replica, "pullAudioFading")), "normal release begins fade on each peer");
        Check(service.HasWorldLoop(oldHandle) && (replica == null || service.HasWorldLoop(remoteHandle)),
            "release retains owned loop during fade on each peer");
        await Until(() => authority.CurrentPhase == GrapplePhase.Idle, "retract before interrupt");
        int shots = authority.FeedbackShotCount;
        await Press(false);
        await Until(() => authority.FeedbackShotCount == shots + 1, "next shot interrupts fade");
        await Until(() => !service.HasWorldLoop(oldHandle) && (replica == null || !service.HasWorldLoop(remoteHandle)),
            "next shot clears old handles on each peer", 1f);
        Check(Time.realtimeSinceStartup - releaseTime < 2f,
            "next successful shot interrupts a longer unfinished tail");
        Set(authority, "grapplePullFadeOutDuration", .15f);
        if (replica != null) Set(replica, "grapplePullFadeOutDuration", .15f);
        await Until(() => authority.FeedbackPullPlaying && (replica == null || replica.FeedbackPullPlaying),
            "new grapple creates its own pull loop");
    }
    private static async Task Press(bool remote, int milliseconds = 100)
    {
        if (remote) clientInput.Buttons.Set(InputButton.Grapple, true); else hostInput.Buttons.Set(InputButton.Grapple, true);
        peakCameraOffset = 0f;
        float end = Time.realtimeSinceStartup + milliseconds / 1000f;
        while (Time.realtimeSinceStartup < end)
        {
            await Task.Delay(10);
            var shake = FirstPersonHitCameraShake.Singleton;
            if (shake == null) continue;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var effectRoot = (Transform)typeof(FirstPersonHitCameraShake).GetField("cameraEffectsRoot", flags).GetValue(shake);
            var baseline = (Vector3)typeof(FirstPersonHitCameraShake).GetField("baseLocalPosition", flags).GetValue(shake);
            if (effectRoot != null) peakCameraOffset = Mathf.Max(peakCameraOffset, Vector3.Distance(effectRoot.localPosition, baseline));
        }
        if (remote) clientInput.Buttons.Set(InputButton.Grapple, false); else hostInput.Buttons.Set(InputButton.Grapple, false);
        await Task.Delay(50);
    }
    public static async void Begin(bool multiplayer = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無現存 Runner 的 Play Mode。");
        Status = "執行中"; network = multiplayer; report.Clear(); pinned.Clear(); hostInput = clientInput = default;
        session = "grapple-audio-" + Guid.NewGuid().ToString("N");
        try
        {
            Check(GameplayAudioService.Instance != null, "existing audio service is present");
            Check(FirstPersonHitCameraShake.Singleton != null, "existing camera shake is present");
            host = await Connect(true);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var obj = host.Spawn(prefab, Origin, Quaternion.identity, host.LocalPlayer); host.SetPlayerObject(host.LocalPlayer, obj);
            var p = obj.GetComponent<Player>(); var g = p.Grapple;
            await Until(() => p.Health.IsRescueReady && p.Health.IsAlive, "spawn health"); Configure(p);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall); host.MoveToRunnerScene(wall);
            wall.layer = 0; wall.transform.position = Origin + Vector3.forward * 30;
            wall.transform.localScale = new Vector3(100, 100, 1); Physics.SyncTransforms(); await Task.Delay(100);
            Energy(p, 0); await Press(false); Check(g.FeedbackAcceptedCount == 0 && g.FeedbackShakeCount == 0, "zero energy has no launch or shake");
            Energy(p, 50); wall.SetActive(false); Physics.SyncTransforms(); await Press(false);
            Check(g.FeedbackAcceptedCount == 0 && g.FeedbackShotCount == 0, "invalid target has no shot");
            wall.SetActive(true); Physics.SyncTransforms();
            var gate = p.GetComponent<PlayerActionGate>(); gate.SetBlocks(PlayerActionBlockSource.System, PlayerActionBlockMask.Grapple);
            await Press(false); Check(g.FeedbackAcceptedCount == 0, "action gate rejects without feedback"); gate.ClearBlocks(PlayerActionBlockSource.System);
            await Press(false); await Until(() => g.FeedbackAcceptedCount == 1, "accepted Q");
            Check(g.FeedbackShakeCount == 1 && g.FeedbackShotCount == 0, "successful Q shakes before prefire completes");
            Check(peakCameraOffset > .001f, "successful launch moves the existing camera effects root");
            await Until(() => g.FeedbackShotCount == 1, "shot event");
            Check(g.FeedbackImpactCount == 0, "shot does not play impact early");
            await Until(() => g.FeedbackPullPlaying, "actual pull loop");
            Check(g.FeedbackImpactCount == 1, "arrival plays one impact");
            await Task.Delay(300); Check(g.FeedbackShotCount == 1 && g.FeedbackImpactCount == 1 && g.FeedbackShakeCount == 1, "held state never repeats one-shot events");
            var service = GameplayAudioService.Instance;
            var impact = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/AudioClips/Grapple/HookOnThing.mp3");
            var impactVoice = service.GetComponentsInChildren<AudioSource>().FirstOrDefault(s => s.clip == impact);
            Check(impactVoice != null && impactVoice.spatialBlend == 1f && impactVoice.transform.position.z > Origin.z + 29f,
                "impact source is at world hit point and uses 3D spatialization");
            var loop = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/AudioClips/Grapple/Hooking.mp3");
            Check(service.GetComponentsInChildren<AudioSource>().Any(s => s.clip == loop && s.loop && s.spatialBlend == 1f), "pull uses a 3D looping source");
            await Press(false); await Until(() => !g.FeedbackPullPlaying && g.CurrentPhase == GrapplePhase.Idle, "Q release stops loop");
            Check(g.FeedbackAcceptedCount == 1 && g.FeedbackShakeCount == 1, "Q release does not shake or launch");
            await Press(false); await Until(() => g.FeedbackShotCount == 2, "second shot"); await Press(false);
            await Until(() => g.CurrentPhase == GrapplePhase.Idle, "early retract");
            Check(g.FeedbackImpactCount == 1 && !g.FeedbackPullPlaying, "cancel before arrival has no impact or pull loop");
            Set(g, "useHoldToMaintainGrappleInput", true);
            await Press(false, 1400); await Until(() => g.CurrentPhase == GrapplePhase.Idle && !g.FeedbackPullPlaying, "hold mode release");
            Check(g.FeedbackAcceptedCount == 3 && g.FeedbackShakeCount == 3, "hold Q launches and shakes exactly once");
            Set(g, "useHoldToMaintainGrappleInput", false);
            await Press(false); await Until(() => g.FeedbackPullPlaying, "persistent pull");
            g.enabled = false; g.Render(); Check(!g.FeedbackPullPlaying, "disabled grapple cannot restart its loop");
            g.enabled = true; await Until(() => g.FeedbackPullPlaying, "enabled pull resumes");

            if (network)
            {
                client = await Connect(false);
                await Until(() => client.FindObject(obj.Id) != null, "late join object");
                var remote = client.FindObject(obj.Id).GetComponent<PlayerGrapple>();
                await Until(() => remote.FeedbackPullPlaying, "late join starts ongoing loop");
                Check(remote.FeedbackShakeCount == 0, "late join never shakes for another player");
                await VerifyFadeInterruption(g, remote);
                await Press(false); await Until(() => !g.FeedbackPullPlaying && !remote.FeedbackPullPlaying, "release stops both peer loops");
                await Until(() => g.CurrentPhase == GrapplePhase.Idle, "host retract done");
                // Move the host collider out of the remote shooter's ray while retaining its valid object.
                pinned.Remove(p); p.Movement.KCC.SetPosition(Origin + Vector3.right * 200);
                var owned = host.Spawn(prefab, Origin, Quaternion.identity, client.LocalPlayer); host.SetPlayerObject(client.LocalPlayer, owned);
                var cp = owned.GetComponent<Player>(); await Until(() => cp.Health.IsRescueReady && cp.Health.IsAlive, "client-owned health"); Configure(cp);
                await Until(() => client.FindObject(owned.Id) != null, "client-owned replica");
                var cg = cp.Grapple; var local = client.FindObject(owned.Id).GetComponent<PlayerGrapple>();
                // Mirror test-only timings on the predicted replica; production prefab values are already identical.
                Set(local, "grapplePreFireDelay", .25f); Set(local, "ropeShootTimeAtMaxDistance", 1f);
                Set(local, "grappleMask", (LayerMask)1); Set(local, "useHoldToMaintainGrappleInput", false);
                int oldHostShake = g.FeedbackShakeCount;
                await Task.Delay(300); await Press(true);
                await Until(() => cg.FeedbackPullPlaying && local.FeedbackPullPlaying, "client Q authoritative pull");
                Check(cg.FeedbackAcceptedCount == 1 && cg.FeedbackShotCount == 1 && cg.FeedbackImpactCount == 1, "host sends client-owned events once");
                Check(local.FeedbackShakeCount == 1 && cg.FeedbackShakeCount == 0 && g.FeedbackShakeCount == oldHostShake, "shake only reaches the input authority");
                await Press(true); await Until(() => !cg.FeedbackPullPlaying && !local.FeedbackPullPlaying, "client release converges");
                await Until(() => cg.CurrentPhase == GrapplePhase.Idle, "client retract done");
                pinned.Remove(cp); cp.Movement.KCC.SetPosition(Origin + Vector3.left * 200); pinned.Add(p);
                await Task.Delay(100); await Press(false); await Until(() => g.FeedbackPullPlaying && remote.FeedbackPullPlaying, "host pull before reconnect");
                await client.Shutdown(); client = null;
                Check(!remote.FeedbackPullPlaying, "disconnect disposes replica loop");
                client = await Connect(false); await Until(() => client.FindObject(obj.Id) != null, "reconnect object");
                remote = client.FindObject(obj.Id).GetComponent<PlayerGrapple>();
                await Until(() => remote.FeedbackPullPlaying, "reconnect restores current loop");
                Check(remote.FeedbackShakeCount == 0, "reconnect never replays launch shake");
                host.Despawn(obj); await Until(() => !g.FeedbackPullPlaying && !remote.FeedbackPullPlaying, "despawn cleans both loops");
            }
            else
            {
                await VerifyFadeInterruption(g);
                Set(p.Health, "CurrentHealth", 0f); await Until(() => !g.FeedbackPullPlaying, "health zero stops loop");
                host.Despawn(obj); Check(!g.FeedbackPullPlaying, "despawn keeps loop disposed");
            }
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError("[Grapple Feedback Verification] " + ex); }
        finally
        {
            pinned.Clear();
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item); temporary.Clear();
            Directory.CreateDirectory("Temp/GrappleFeedback");
            File.WriteAllLines("Temp/GrappleFeedback/" + (network ? "host-client" : "single") + ".txt", report);
        }
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Grapple Audio Host" : "Grapple Audio Client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => { if (server) foreach (var p in pinned) Pin(p); input.Set(server ? hostInput : clientInput); });
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = network ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 2, IsVisible = false });
        Check(result.Ok, "Runner started: " + result.ShutdownReason); await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "first tick"); return runner;
    }
}
#endif
