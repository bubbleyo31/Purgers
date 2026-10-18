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

/// <summary>受控真實 Fusion Host/Client。延長暫時實例時窗以量測；不保存資產或正式 Scene。</summary>
public sealed class BeamRuntimeVerification : SimulationBehaviour
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static BeamRuntimeVerification driver;
    private static Action pending;
    private static TaskCompletionSource<bool> completion;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static string session;
    private static Player player;
    private static float yaw;
    private static readonly Vector3 origin = new Vector3(4200, 800, 4200);
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    public override void FixedUpdateNetwork()
    {
        if (player != null && player.Object != null && player.Object.IsValid)
        {
            var kcc = player.Movement.KCC;
            kcc.SetPosition(origin); kcc.SetLookRotation(0, yaw);
            kcc.FixedData.BasePosition = kcc.FixedData.TargetPosition = kcc.FixedData.DesiredPosition = origin;
            kcc.SetDynamicVelocity(Vector3.zero); kcc.SetKinematicVelocity(Vector3.zero);
        }
        if (pending == null) return;
        var action = pending; var done = completion; pending = null; completion = null;
        try { action(); done.SetResult(true); } catch (Exception ex) { done.SetException(ex); }
    }
    private static object Read(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(target);
            var property = type.GetProperty(name, Flags);
            if (property != null) return property.GetValue(target);
        }
        throw new MissingMemberException(name);
    }
    private static void Set(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null) { field.SetValue(target, value); return; }
            var property = type.GetProperty(name, Flags);
            if (property != null) { property.SetValue(target, value); return; }
        }
        throw new MissingMemberException(name);
    }
    private static async Task Tick(Action action)
    {
        completion = new TaskCompletionSource<bool>(); pending = action;
        var done = completion;
        if (await Task.WhenAny(done.Task, Task.Delay(10000)) != done.Task) throw new TimeoutException("host tick");
        await done.Task;
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        report.Add("PASS " + label);
    }
    private static async Task Until(Func<bool> condition, string stage, float seconds = 20)
    {
        Stage = stage; float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(stage);
            await Task.Delay(10);
        }
    }
    private static BeamPresentationRuntime Visual(object target) => (BeamPresentationRuntime)Read(target, "visual");
    private static int Loop(object target) => Visual(target) == null ? -1 : (int)Read(Visual(target), "loopHandle");
    private static BeamPresentationStage VisualStage(object target) => Visual(target) == null
        ? BeamPresentationStage.Hidden : (BeamPresentationStage)Read(Visual(target), "previous");
    private static LineRenderer Line(object target) => (LineRenderer)Read(Visual(target), "line");
    private static void Audio(object target, GameplayAudioCue cue)
    {
        var settings = (BeamPresentationSettings)Read(target, "presentation");
        foreach (var slot in new[] { settings.audio.prepare, settings.audio.aim, settings.audio.charge, settings.audio.fire })
        { slot.cue = cue; slot.volume = 0f; }
    }
    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("Needs idle Play Mode");
        Status = "執行中"; report.Clear(); session = "beam-" + Guid.NewGuid().ToString("N"); yaw = 0;
        try
        {
            var cue = ScriptableObject.CreateInstance<GameplayAudioCue>(); temporary.Add(cue);
            var clip = AudioClip.Create("Silent beam verification", 4410, 1, 44100, false); temporary.Add(clip);
            Set(cue, "clips", new[] { clip });
            host = await Connect(true);
            driver = host.gameObject.AddComponent<BeamRuntimeVerification>(); host.AddGlobal(driver);
            client = await Connect(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            // Client owns input, Host owns results.
            var obj = host.Spawn(prefab.GetComponent<NetworkObject>(), origin, Quaternion.identity, client.LocalPlayer);
            host.SetPlayerObject(client.LocalPlayer, obj); player = obj.GetComponent<Player>();
            await Until(() => PlayerAbilityQualification.HasRangedWeapon(player) && client.FindObject(obj.Id) != null, "player and client replica");
            var cannonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AbilityRuntime/Active/PiercingCannon.prefab");
            var abilityObject = host.Spawn(cannonPrefab.GetComponent<NetworkObject>(), origin, Quaternion.identity, client.LocalPlayer,
                (r, o) => o.GetComponent<PlayerAbilityRuntime>().InitializeBeforeSpawn(obj, 0));
            var cannon = abilityObject.GetComponent<PlayerPiercingCannonAbility>();
            await Until(() => client.FindObject(abilityObject.Id) != null, "cannon replica");
            var remote = client.FindObject(abilityObject.Id).GetComponent<PlayerPiercingCannonAbility>();
            await Until(() => cannon.IsUsable && Visual(cannon) != null && Visual(remote) != null, "cannon ready");
            Audio(cannon, cue); Audio(remote, cue);
            Set(cannon, "castDelaySeconds", 2f); Set(cannon, "beamVisibleSeconds", 1f);
            Check(cannon.Object.HasStateAuthority && !remote.Object.HasStateAuthority && remote.Object.HasInputAuthority,
                "Host State Authority / Client Input Authority separated");
            var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
            await Tick(() => cannon.SimulateAbility(input, default));
            await Until(() => VisualStage(cannon) == BeamPresentationStage.Aim && VisualStage(remote) == BeamPresentationStage.Aim, "both aim");
            Check(Loop(cannon) >= 0 && Loop(remote) >= 0, "aim loops restored on both peers");
            int aimLoop = Loop(cannon);
            Check((int)Read(cannon, "BeamVisualSequence") == 0, "prefire cannot commit shot");
            yaw = 70;
            await Until(() => Vector3.Angle(Line(cannon).GetPosition(1) - Line(cannon).GetPosition(0), Quaternion.Euler(0,70,0)*Vector3.forward) < 2, "aim follows current look");
            await Until(() => VisualStage(cannon) == BeamPresentationStage.Charge && VisualStage(remote) == BeamPresentationStage.Charge, "both charge");
            Check(Loop(cannon) != aimLoop && !GameplayAudioService.Instance.HasWorldLoop(aimLoop), "charge replaces owned aim loop");
            yaw = 110;
            await Until(() => VisualStage(cannon) == BeamPresentationStage.Fired && VisualStage(remote) == BeamPresentationStage.Fired, "both fired");
            Vector3 start = (Vector3)Read(cannon,"BeamStart"), end = (Vector3)Read(cannon,"BeamEnd");
            Check(Vector3.Angle(end-start, Quaternion.Euler(0,110,0)*Vector3.forward) < 2, "actual shot uses release aim, not locked preview");
            Check(Loop(cannon) < 0 && Loop(remote) < 0, "fire stops charge loops");
            Check(Visual(cannon).BurstCount == 1 && Visual(remote).BurstCount == 1, "one particle burst on each peer");
            Check(Visual(cannon).OneShotCount == 2 && Visual(remote).OneShotCount == 2, "prepare and fire one-shots play once on each peer");
            float initialWidth = Line(cannon).widthMultiplier;
            yaw = 200;
            await Task.Delay(180);
            Check(Line(cannon).widthMultiplier < initialWidth, "shot narrows over time");
            Check(Line(cannon).GetPosition(0) == start && Line(cannon).GetPosition(1) == end, "shot endpoints stay frozen while owner turns");
            await Until(() => !Line(cannon).enabled && !Line(remote).enabled, "shot fully disappears");
            await Tick(() => { Set(cannon, "castDelaySeconds", 90f); cannon.SimulateAbility(input, default); });
            await Until(() => VisualStage(cannon) == BeamPresentationStage.Aim, "second windup");
            // Late join/reconnect restores current telegraph and loop, without replaying old preparation.
            await client.Shutdown(); client = null;
            Check(Visual(remote) == null, "disconnect disposes replica presentation");
            client = await Connect(false);
            await Until(() => client.FindObject(abilityObject.Id) != null, "late reconnect cannon");
            remote = client.FindObject(abilityObject.Id).GetComponent<PlayerPiercingCannonAbility>();
            Audio(remote, cue);
            report.Add("INFO reconnect Phase=" + remote.Phase + " remaining=" + remote.ActiveRemainingSeconds + " usable=" + remote.IsUsable);
            await Until(() => VisualStage(remote) == BeamPresentationStage.Aim && Loop(remote) >= 0, "late join restores active aim loop");
            Check(Visual(remote).BurstCount == 0 && Visual(remote).StageEventCount == 0, "late join does not replay old transient events");
            int cancelLoop = Loop(cannon);
            await Tick(() => Set(cannon, "Phase", PlayerActiveAbilityPhase.Ready));
            await Until(() => !Line(cannon).enabled && !Line(remote).enabled, "cancellation hides both peers");
            Check(!GameplayAudioService.Instance.HasWorldLoop(cancelLoop), "cancellation releases loop");
            host.Despawn(abilityObject);
            await Until(() => Visual(remote) == null, "despawn disposes remote renderer");
            foreach (string variant in new[] { "B", "C_Beam" }) await Enemy(variant, cue, obj);
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); }
        finally
        {
            player = null;
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item);
            temporary.Clear();
            Directory.CreateDirectory("Temp/BeamPresentation");
            File.WriteAllLines("Temp/BeamPresentation/host-client.txt", report);
        }
    }

    private static async Task Enemy(string variant, GameplayAudioCue cue, NetworkObject target)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_" + variant + ".prefab");
        var obj = host.Spawn(prefab.GetComponent<NetworkObject>(), origin + Vector3.back * 20, Quaternion.identity);
        var attack = obj.GetComponent<EnemyRangedBeamAttack>();
        var controller = obj.GetComponent<EnemyCombatDecisionController>();
        controller.enabled = false; // Explicit phase probe, not natural AI acceptance.
        Set(attack, "damage", 0f); Set(attack, "shotBeamVisibleSeconds", 1f);
        await Until(() => client.FindObject(obj.Id) != null, variant + " replica");
        var presenter = obj.GetComponentInChildren<EnemyBeamPresenter>();
        var other = client.FindObject(obj.Id).GetComponentInChildren<EnemyBeamPresenter>();
        Audio(presenter, cue); Audio(other, cue);
        await Until(() => Visual(presenter) != null && Visual(other) != null, variant + " renderers");
        var context = new EnemyCombatContext(controller, obj.GetComponent<EnemyActor>(), obj.GetComponent<EnemyPerceptionController>(),
            target, origin + Vector3.up, 20f, host.DeltaTime);
        await Tick(() => typeof(EnemyCombatDecisionController).GetMethod("StartOption", Flags).Invoke(controller, new object[] {attack, context}));
        await Until(() => VisualStage(presenter) == BeamPresentationStage.Aim && VisualStage(other) == BeamPresentationStage.Aim, variant + " aim");
        await Tick(() => { Set(controller, "CurrentActionPhase", EnemyCombatActionPhase.LockedDelay); Set(attack,"PhaseTimer",TickTimer.CreateFromSeconds(host, 1)); });
        await Until(() => VisualStage(presenter) == BeamPresentationStage.Charge && VisualStage(other) == BeamPresentationStage.Charge, variant + " locked");
        await Tick(() => { Set(attack,"PhaseTimer",TickTimer.None); attack.TickOption(context); });
        await Until(() => VisualStage(presenter) == BeamPresentationStage.Fired && VisualStage(other) == BeamPresentationStage.Fired, variant + " fired");
        Check(Visual(presenter).BurstCount == 1 && Visual(other).BurstCount == 1, variant + " single burst on both peers");
        var start = attack.ShotStartPoint; var end = attack.ShotEndPoint;
        obj.transform.position += Vector3.right * 3;
        await Task.Delay(100);
        Check(Line(presenter).GetPosition(0) == start && Line(presenter).GetPosition(1) == end, variant + " frozen shot geometry");
        await Until(() => !Line(presenter).enabled && !Line(other).enabled, variant + " disappeared");
        Check(Loop(presenter) < 0 && Loop(other) < 0, variant + " no lingering loops");
        host.Despawn(obj);
        await Until(() => Visual(other) == null, variant + " despawn cleanup");
    }

    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Beam verification Host" : "Beam verification Client").AddComponent<NetworkRunner>();
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = server ? GameMode.Host : GameMode.Client,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app,
            PlayerCount = 2, IsVisible = false });
        Check(result.Ok, "Runner started " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner first tick");
        return runner;
    }
}
#endif

