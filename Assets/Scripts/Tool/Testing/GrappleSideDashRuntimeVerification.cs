#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Fusion.Addons.KCC;
using UnityEditor;
using UnityEngine;

/// <summary>隔離 Runner 的正式輸入／KCC 驗證；只修改測試 instance，不保存 Scene 或 Prefab。</summary>
public static class GrappleSideDashRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool network;
    private static string session;
    private static NetInput hostInput, clientInput;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly Vector3 Origin = new Vector3(2400, 1000, 2400);
    private static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); report.Add("PASS " + text); }
    private static void Set(object obj, string name, object value)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = obj.GetType().GetField(name, flags);
        if (field != null) field.SetValue(obj, value); else obj.GetType().GetProperty(name, flags).SetValue(obj, value);
    }
    private static async Task Until(Func<bool> condition, string stage, float seconds = 20f)
    {
        Stage = stage; float end = Time.realtimeSinceStartup + seconds;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(stage); await Task.Delay(10); }
    }
    private static void Flight(Player player, Vector3 position, float energy = 10f)
    {
        var k = player.Movement.KCC;
        k.SetPosition(position); k.SetLookRotation(0f, 0f); k.SetKinematicVelocity(Vector3.zero);
        k.SetDynamicVelocity(Vector3.forward * 60f);
        k.FixedData.IsGrounded = false;
        k.FixedData.BasePosition = k.FixedData.TargetPosition = k.FixedData.DesiredPosition = position;
        var g = player.Grapple;
        Set(g, "CurrentPhase", GrapplePhase.Idle);
        Set(g, "ReleaseMomentumActive", (NetworkBool)true);
        Set(g, "ReleaseMomentumDirection", Vector3.forward);
        Set(g, "ReleaseMomentumSpeed", 60f);
        Set(g, "ReleaseMomentumBaseSpeed", 10f);
        Set(g, "SideDashActive", (NetworkBool)false);
        Set(g, "SideDashCooldown", TickTimer.None);
        player.GetComponent<PlayerStateMachine>().NotifyGrappleEnded(false);
        var charges = player.GetComponent<PlayerGrappleCharges>();
        Set(charges, "IsInitialized", (NetworkBool)true); Set(charges, "CurrentEnergy", energy); Set(charges, "MaximumEnergy", 50f);
        Physics.SyncTransforms();
    }
    private static void Tap(bool remote, int side, float interval = .08f)
    {
        var input = remote ? clientInput : hostInput;
        input.PreviousSideTapDirection = input.SideTapDirection;
        input.SideTapDirection = side; input.SideTapInterval = interval; input.SideTapSequence++;
        if (remote) clientInput = input; else hostInput = input;
    }
    private static async Task Pair(bool remote, int side)
    { Tap(remote, side); await Task.Delay(70); Tap(remote, side); }
    public static async void Begin(bool multiplayer = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要沒有現存 Runner 的 Play Mode。");
        Status = "執行中"; report.Clear(); hostInput = clientInput = default;
        network = multiplayer; session = "side-dash-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var obj = host.Spawn(prefab, Origin, Quaternion.identity, host.LocalPlayer); host.SetPlayerObject(host.LocalPlayer, obj);
            var p = obj.GetComponent<Player>(); var g = p.Grapple; var k = p.Movement.KCC; var charges = p.GetComponent<PlayerGrappleCharges>();
            await Until(() => p.Health.IsAlive, "player spawn"); await Task.Delay(200);
            Flight(p, Origin, .5f); await Task.Delay(70);
            int seq = g.SideDashSequence; Vector3 start = k.FixedData.TargetPosition;
            await Pair(false, -1); await Until(() => g.SideDashSequence > seq, "AA starts");
            Check(charges.CurrentEnergy == 0f, "positive insufficient energy drains to zero");
            Check(g.SideDashCooldownRemaining > 0f, "shared cooldown starts immediately");
            await Until(() => !g.IsSideDashing, "AA ends");
            Vector3 travel = k.FixedData.TargetPosition - start;
            report.Add("MEASURE free flight " + travel.ToString("F3"));
            Check(Mathf.Abs(travel.x + 20f) < .5f, "20 m finite left increment");
            Check(travel.z > 1f && travel.y < 0f, "forward inertia and gravity continue");
            Check(Mathf.Abs(k.FixedData.DynamicVelocity.x) < .1f, "no persistent dash side velocity");
            await Pair(false, 1); await Task.Delay(180);
            Check(g.SideDashSequence == seq + 1, "opposite double tap cannot bypass cooldown");
            await Task.Delay(1100); await Pair(false, -1); await Task.Delay(180);
            Check(g.SideDashSequence == seq + 1, "zero energy rejects new dash");

            Flight(p, Origin); await Task.Delay(80); seq = g.SideDashSequence;
            await Pair(false, 1); await Until(() => g.SideDashSequence > seq, "DD starts");
            k.SetLookRotation(0f, 180f);
            start = k.FixedData.TargetPosition;
            await Until(() => !g.IsSideDashing, "fixed heading ends");
            Check(k.FixedData.TargetPosition.x > start.x, "turning camera during dash does not reverse heading");

            Flight(p, Origin); await Task.Delay(80); seq = g.SideDashSequence;
            Tap(false, -1); await Task.Delay(80);
            Set(g, "CurrentPhase", GrapplePhase.Attached); Set(g, "SupportInteractionTetherActive", (NetworkBool)false);
            Set(g, "GrappleWorldPoint", k.FixedData.TargetPosition + Vector3.forward * 100f);
            Tap(false, -1);
            await Until(() => g.SideDashSequence > seq, "attached AA");
            Check(!g.IsGrappleControlActive, "successful attached dash releases rope");
            Check(k.FixedData.DynamicVelocity.z > 0f, "rope release preserves forward velocity");
            await Until(() => !g.IsSideDashing, "attached dash ends");

            Flight(p, Origin); await Task.Delay(80);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall); host.MoveToRunnerScene(wall);
            wall.layer = LayerMask.NameToLayer("Default"); wall.transform.position = Origin + new Vector3(-5f, 0f, 20f);
            wall.transform.localScale = new Vector3(1f, 100f, 200f); Physics.SyncTransforms();
            start = k.FixedData.TargetPosition; seq = g.SideDashSequence;
            await Pair(false, -1); await Until(() => g.SideDashSequence > seq, "wall dash starts");
            await Until(() => !g.IsSideDashing, "wall dash ends");
            travel = k.FixedData.TargetPosition - start;
            report.Add("MEASURE wall flight " + travel.ToString("F3"));
            Check(travel.x > -5f && travel.x < -.1f, "whole-body diagonal sweep stops at wall");
            Check(charges.CurrentEnergy == 8f && g.SideDashCooldownRemaining > 0f, "collision retains cost and cooldown");
            UnityEngine.Object.Destroy(wall); await Task.Delay(80);

            Flight(p, Origin); await Task.Delay(80); seq = g.SideDashSequence;
            hostInput.BlockedControls = Purgers.GameFlow.Control.PlayerControlMask.Movement;
            await Pair(false, 1); await Task.Delay(100);
            Check(g.SideDashSequence == seq && charges.CurrentEnergy == 10f, "movement lock rejects without spending");
            hostInput.BlockedControls = 0; await Task.Delay(100);
            Check(g.SideDashSequence == seq, "unlock does not replay buffered double tap");

            Flight(p, Origin); await Task.Delay(80); seq = g.SideDashSequence;
            Tap(false, -1); await Task.Delay(300); Tap(false, -1, .3f); await Task.Delay(100);
            Check(g.SideDashSequence == seq, "late second tap is rejected");
            Tap(false, 1); await Task.Delay(80); Tap(false, -1); await Task.Delay(100);
            Check(g.SideDashSequence == seq, "alternating keys are not a double tap");

            Flight(p, Origin); await Task.Delay(80);
            var blinkDefinition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Blink.asset");
            Check(p.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(blinkDefinition, out string equipError), "equip blink: " + equipError);
            await Until(() => p.AbilityRuntimeManager.TryGetActiveModule<PlayerBlinkAbility>(out var b) && b.IsUsable, "blink ready");
            p.AbilityRuntimeManager.TryGetActiveModule<PlayerBlinkAbility>(out var blink);
            var e = new NetInput(); e.Buttons.Set(InputButton.Ability1, true);
            blink.SimulateAbility(e, default);
            Check(blink.Phase == PlayerActiveAbilityPhase.Armed, "blink prepares before side dash");
            Set(g, "sideDashSpeed", 20f); seq = g.SideDashSequence;
            await Pair(false, 1); await Until(() => g.SideDashSequence > seq, "mutual exclusion dash starts");
            blink.SimulateAbility(e, default);
            Check(blink.Phase == PlayerActiveAbilityPhase.Armed, "side dash rejects blink execution without consuming armed state");
            var gate = p.GetComponent<PlayerActionGate>();
            gate.SetBlocks(PlayerActionBlockSource.StatusEffect, PlayerActionBlockMask.Movement);
            await Until(() => !g.IsSideDashing, "gate interrupts running dash");
            gate.ClearBlocks(PlayerActionBlockSource.StatusEffect); await Task.Delay(100);
            Check(!g.IsSideDashing, "unblocking does not resume interrupted dash");
            Set(g, "sideDashSpeed", 150f);
            Set(blink, "dashSpeed", 10f); blink.SimulateAbility(e, default);
            Check(blink.IsDashing, "blink executes after side dash interruption");
            Set(g, "SideDashCooldown", TickTimer.None); seq = g.SideDashSequence;
            await Pair(false, -1); await Task.Delay(100);
            Check(g.SideDashSequence == seq, "active blink rejects side dash");
            await Until(() => !blink.IsDashing, "blink ends");

            if (network)
            {
                client = await Connect(false);
                await Until(() => client.FindObject(obj.Id) != null, "late join player");
                Check(client.FindObject(obj.Id).GetComponent<PlayerGrapple>().SideDashSequence == g.SideDashSequence, "late join receives dash sequence");
                var owned = host.Spawn(prefab, Origin + Vector3.right * 100f, Quaternion.identity, client.LocalPlayer); host.SetPlayerObject(client.LocalPlayer, owned);
                var ownedPlayer = owned.GetComponent<Player>();
                await Until(() => client.FindObject(owned.Id) != null && ownedPlayer.Health.IsAlive, "client owned player"); await Task.Delay(250);
                Flight(ownedPlayer, Origin + Vector3.right * 100f, .5f); await Task.Delay(200);
                var ownedG = ownedPlayer.Grapple; int remoteSeq = ownedG.SideDashSequence;
                var remoteG = client.FindObject(owned.Id).GetComponent<PlayerGrapple>();
                await Pair(true, 1); await Until(() => ownedG.SideDashSequence > remoteSeq, "client input accepted by host");
                await Until(() => remoteG.SideDashSequence == ownedG.SideDashSequence, "client sequence converges");
                await Until(() => !ownedG.IsSideDashing && !remoteG.IsSideDashing, "client dash ends");
                var remoteCharges = client.FindObject(owned.Id).GetComponent<PlayerGrappleCharges>();
                await Until(() => remoteCharges.CurrentEnergy == 0f, "client energy converges");
                Check(ownedPlayer.GetComponent<PlayerGrappleCharges>().CurrentEnergy == 0f, "host owns exact final energy");
                await client.Shutdown(); client = null; client = await Connect(false);
                await Until(() => client.FindObject(owned.Id) != null, "reconnect objects");
                await Until(() => client.FindObject(owned.Id).GetComponent<PlayerGrapple>().SideDashSequence == ownedG.SideDashSequence, "reconnect sequence");
                Check(!client.FindObject(owned.Id).GetComponent<PlayerGrapple>().IsSideDashing, "reconnect does not replay completed dash");
            }
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError("[SideDash Verification] " + ex); }
        finally
        {
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj); temporary.Clear();
            Directory.CreateDirectory("Temp/GrappleSideDash"); File.WriteAllLines("Temp/GrappleSideDash/" + (network ? "host-client" : "single") + ".txt", report);
        }
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "SideDash Test Host" : "SideDash Test Client").AddComponent<NetworkRunner>();
        temporary.Add(runner.gameObject); runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => input.Set(server ? hostInput : clientInput));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = network ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 2, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason); await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "first tick"); return runner;
    }
}
#endif
