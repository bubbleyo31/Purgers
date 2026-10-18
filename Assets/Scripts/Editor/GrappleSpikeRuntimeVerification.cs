using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>隔離測試 Runner，直接驅動正式能力入口；不儲存或修改正式 Scene/Prefab。</summary>
public static class GrappleSpikeRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool network;
    private static string session;
    private static readonly List<string> report = new List<string>();
    private static readonly List<GameObject> temporary = new List<GameObject>();
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Vector3 Origin = new Vector3(3500, 1000, 3500);

    public static async void Begin(bool multiplayer = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需在沒有現存 Runner 的隔離 Play Mode 執行。");
        Status = "執行中"; report.Clear(); network = multiplayer; session = "spike-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            if (network) client = await Connect(false);
            PlayerRef inputOwner = network ? client.LocalPlayer : host.LocalPlayer;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj = host.Spawn(prefab.GetComponent<NetworkObject>(), Origin, Quaternion.identity, inputOwner);
            host.SetPlayerObject(inputOwner, obj);
            var player = obj.GetComponent<Player>();
            await Until(() => player.Health.IsAlive && player.AbilityRuntimeManager.LoadoutRevision > 0, "player ready");
            var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(GrappleSpikeAssetSetup.DefinitionPath);
            Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition, out string error), "formal Loadout equips: " + error);
            await Until(() => PlayerAbilityQualification.GetModule<PlayerGrappleSpikeAbility>(player) != null, "ability bound");
            var spike = PlayerAbilityQualification.GetModule<PlayerGrappleSpikeAbility>(player);
            // 凍結測試玩家自動移動，仍由正式 State Authority 模組送傷；不宣稱自然按鍵驗證。
            host.SetIsSimulated(obj, false);
            player.Movement.KCC.SetPosition(Origin); player.transform.position = Origin;
            Set(spike, "baseDamage", 10f); Set(spike, "knockbackDistance", 0f);
            Set(player.GrappleMomentumEnergy, "NormalizedEnergy", .5f);
            Set(player.Grapple, "SupportInteractionTetherActive", (NetworkBool)false);
            Set(player.Grapple, "ReleaseMomentumActive", (NetworkBool)false);
            Set(player.Grapple, "CurrentPhase", GrapplePhase.Idle);
            await Drive(spike);
            Check(spike.Phase == GrappleSpikePhase.Ready && !spike.IsSpikeVisible, "idle remains invisible and untimed");
            Set(player.Grapple, "CurrentPhase", GrapplePhase.Shooting); await Drive(spike);
            Check(!spike.ContactEligible && !spike.IsAbilityActive, "hook flight does not enable spikes");
            Set(player.Grapple, "CurrentPhase", GrapplePhase.Attached); await Drive(spike);
            Check(spike.ContactEligible && !spike.IsSpikeVisible && spike.EffectEndTick == 0, "eligible empty space stays armed without visuals or timer");
            var a = Enemy(Origin + Vector3.right * .8f);
            var b = Enemy(Origin - Vector3.right * .8f);
            float hpA = a.Health.CurrentHealth, hpB = b.Health.CurrentHealth;
            await Drive(spike);
            Check(Near(a.Health.CurrentHealth, hpA - 15f) && Near(b.Health.CurrentHealth, hpB - 15f), "multiple targets each receive one 1.5x hit despite multiple colliders");
            int first = host.Tick.Raw, end = spike.EffectEndTick, cooldownEnd = spike.CooldownEndTick;
            Check(spike.IsAbilityActive && spike.IsSpikeVisible, "first effective hit starts visible effect");
            typeof(PlayerGrappleSpikeAbility).GetMethod("LateUpdate", Flags).Invoke(spike, null);
            var visible = (GameObject)typeof(PlayerGrappleSpikeAbility).GetField("visual", Flags).GetValue(spike);
            Check(visible != null && visible.activeSelf && visible.scene == player.gameObject.scene,
                "local visual is active in its owner runner scene");
            Check(Vector3.Distance(visible.transform.position, spike.Center) < .001f &&
                Near(visible.transform.localScale.x, spike.Radius), "visual follows center and gameplay radius");
            Check(Near(player.GrappleMomentumEnergy.NormalizedEnergy, .5f), "damage does not consume boost");
            Scan(spike, spike.Center, spike.Center);
            Check(Near(a.Health.CurrentHealth, hpA - 15f), "same tick repeat cannot duplicate damage");
            Set(player.Grapple, "CurrentPhase", GrapplePhase.Idle); await Drive(spike);
            Check(!spike.IsSpikeVisible && spike.EffectEndTick == end, "detach hides spikes and retains absolute deadline");
            Set(player.Grapple, "ReleaseMomentumActive", (NetworkBool)true); await Drive(spike);
            Check(!spike.ContactEligible, "release momentum is disabled by default");
            Set(spike, "includeReleaseMomentum", true); await Drive(spike);
            Check(spike.ContactEligible && spike.IsSpikeVisible, "optional release momentum restores eligibility without refreshing");
            Set(spike, "includeReleaseMomentum", false);
            Set(player.Grapple, "ReleaseMomentumActive", (NetworkBool)false);
            Set(player.Grapple, "CurrentPhase", GrapplePhase.Attached);
            await Until(() => host.Tick.Raw >= first + GrappleSpikeRules.DurationTicks(.5f, host.DeltaTime), "per target interval");
            Set(player.GrappleMomentumEnergy, "NormalizedEnergy", 1f); await Drive(spike);
            Check(Near(a.Health.CurrentHealth, hpA - 35f), "reattach uses current 2x boost after interval");
            Check(spike.EffectEndTick == end && spike.CooldownEndTick == cooldownEnd, "repeat hits never refresh effect or cooldown deadline");
            var swept = Enemy(Origin + Vector3.right * 10f); float sweptHp = swept.Health.CurrentHealth;
            Scan(spike, spike.Center, spike.Center + Vector3.right * 20f);
            Check(Near(swept.Health.CurrentHealth, sweptHp - 20f), "high speed swept volume catches target between endpoints");
            var blocked = Enemy(Origin + Vector3.forward * 1.1f); float blockedHp = blocked.Health.CurrentHealth;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, a.gameObject.scene);
            wall.transform.position = Origin + Vector3.up + Vector3.forward * .55f;
            wall.transform.localScale = new Vector3(3f, 3f, .1f); Physics.SyncTransforms();
            Scan(spike, spike.Center, spike.Center);
            Check(Near(blocked.Health.CurrentHealth, blockedHp), "world wall blocks contact damage");
            UnityEngine.Object.Destroy(wall);
            Set(player.Grapple, "CurrentPhase", GrapplePhase.Idle); await Drive(spike);
            await Until(() => host.Tick.Raw >= end, "effect expiry without grapple");
            Check(spike.Phase == GrappleSpikePhase.Cooldown && !spike.IsSpikeVisible, "effect expires without grapple and enters cooldown");
            hpA = a.Health.CurrentHealth; Set(player.Grapple, "CurrentPhase", GrapplePhase.Attached); await Drive(spike);
            Check(Near(a.Health.CurrentHealth, hpA), "cooldown rejects collision damage");
            // 移開所有敵人；冷卻到期後仍在 Attached，也只能恢復待命。
            // 正式 NetworkTransform 可能覆寫直接搬動的測試敵人；用權威 Despawn 建立真正無目標的情境。
            host.Despawn(a.Object); host.Despawn(b.Object); host.Despawn(blocked.Object); host.Despawn(swept.Object);
            Physics.SyncTransforms();
            await Until(() => host.Tick.Raw >= cooldownEnd, "cooldown expiry"); await Drive(spike);
            Check(spike.Phase == GrappleSpikePhase.Ready && !spike.IsSpikeVisible, "cooldown completion while attached returns to invisible armed state");
            var pushed = Enemy(Origin + Vector3.right); float pushedHp = pushed.Health.CurrentHealth;
            Set(spike, "knockbackDistance", .6f); Set(spike, "knockbackSpeed", 6f); await Drive(spike);
            Check(Near(pushed.Health.CurrentHealth, pushedHp - 20f) && pushed.StateController.IsAbilityKnockbackActive, "fresh cycle damages and requests existing knockback");
            Vector3 pushStart = pushed.transform.position;
            await Until(() => !pushed.StateController.IsAbilityKnockbackActive, "knockback ends");
            Check(pushed.transform.position.x > pushStart.x && Near(pushed.transform.position.y, pushStart.y), "knockback travels outward horizontally");
            Check(!pushed.StateController.IsAbilityStunned && Near(pushed.Health.CurrentHealth, pushedHp - 20f), "micro knockback adds no stun or second damage");

            if (network)
            {
                await Until(() => client.TryFindObject(spike.Object.Id, out var found) && found.GetComponent<PlayerGrappleSpikeAbility>().OwnerPlayer != null, "input authority runtime replication");
                var remote = client.FindObject(spike.Object.Id).GetComponent<PlayerGrappleSpikeAbility>();
                await Until(() => remote.EffectEndTick == spike.EffectEndTick, "deadline replication");
                Check(remote.Object.HasInputAuthority && !remote.Object.HasStateAuthority, "remote input owner has presentation authority only");
                int seq = remote.HitSequence; remote.SimulateAbility(default, default);
                Check(remote.HitSequence == seq, "input authority cannot commit contact damage");
                await client.Shutdown(); client = null;
                client = await Connect(false);
                await Until(() => client.TryFindObject(spike.Object.Id, out var found) && found.GetComponent<PlayerGrappleSpikeAbility>().OwnerPlayer != null, "late observer binding");
                remote = client.FindObject(spike.Object.Id).GetComponent<PlayerGrappleSpikeAbility>();
                await Until(() => remote.EffectEndTick == spike.EffectEndTick && remote.CooldownEndTick == spike.CooldownEndTick, "late observer timers");
                Check(true, "late join/reconnected observer receives current deadlines without restarting effect");
            }
            player.Health.ReceiveDamage(new DamageRequest { RequestedDamage = 100000f, BaseDamage = 100000f, DamageType = DamageType.Environment });
            await Task.Delay(100);
            Check(!spike.IsSpikeVisible, "owner death removes local visual eligibility");
            host.Despawn(spike.Object); await Task.Delay(100);
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError("[GrappleSpike verification] " + ex); }
        finally
        {
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj);
            temporary.Clear();
            Directory.CreateDirectory("Temp/GrappleSpike");
            File.WriteAllLines("Temp/GrappleSpike/" + (network ? "host-client" : "single") + ".txt", report);
        }
    }
    private static async Task Drive(PlayerGrappleSpikeAbility spike)
    {
        int tick = host.Tick.Raw; await Until(() => host.Tick.Raw > tick, "next tick");
        Physics.SyncTransforms(); spike.SimulateAbility(default, default);
    }
    private static void Scan(PlayerGrappleSpikeAbility spike, Vector3 start, Vector3 end)
    {
        Physics.SyncTransforms();
        typeof(PlayerGrappleSpikeAbility).GetMethod("ScanContacts", Flags).Invoke(spike, new object[] {start, end, host.Tick.Raw});
    }
    private static EnemyActor Enemy(Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab");
        var obj = host.Spawn(prefab.GetComponent<NetworkObject>(), position, Quaternion.identity, onBeforeSpawned:(r,o)=>
        {
            foreach (var b in o.GetComponents<MonoBehaviour>())
                if (b is EnemyIdlePatrolBrain || b is EnemyAwarenessBrain || b is EnemyPerceptionController ||
                    b is EnemyCombatDecisionController || b is EnemyChaseBrain || b is EnemyPatrolNavigator) b.enabled = false;
        });
        foreach (var collider in obj.GetComponentsInChildren<Collider>()) collider.enabled = false;
        for (int i = 0; i < 2; i++)
        {
            var child = new GameObject("Spike verification hit collider"); child.transform.SetParent(obj.transform, false);
            child.transform.position = position + Vector3.up + Vector3.forward * (.03f * i);
            var scale = obj.transform.lossyScale;
            child.transform.localScale = new Vector3(1f/scale.x, 1f/scale.y, 1f/scale.z);
            child.AddComponent<SphereCollider>().radius = .2f;
        }
        Physics.SyncTransforms(); return obj.GetComponent<EnemyActor>();
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Spike Test Host" : "Spike Test Client").AddComponent<NetworkRunner>();
        temporary.Add(runner.gameObject); runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>();
        // 動態 AddComponent 的 Fusion UnityEvent 可能尚未初始化；晚加入會使用 AOI callbacks。
        foreach (var field in typeof(NetworkEvents).GetFields(Flags))
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType) && field.GetValue(events) == null)
                field.SetValue(events, Activator.CreateInstance(field.FieldType));
        events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r,input)=>input.Set(default(NetInput)));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs
        {
            GameMode = network ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app,
            PlayerCount = 3, IsVisible = false
        });
        Check(result.Ok, "runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner ticks"); return runner;
    }
    private static void Set(object obj, string name, object value)
    {
        var field = obj.GetType().GetField(name, Flags);
        if (field != null) field.SetValue(obj, value); else obj.GetType().GetProperty(name, Flags).SetValue(obj, value);
    }
    private static bool Near(float a, float b) => Mathf.Abs(a-b) < .02f;
    private static void Check(bool ok, string text)
    { if (!ok) throw new InvalidOperationException(text); report.Add("PASS " + text); }
    private static async Task Until(Func<bool> condition, string stage)
    {
        Stage = stage; float until = Time.realtimeSinceStartup + 25f;
        while (!condition()) { if (Time.realtimeSinceStartup > until) throw new TimeoutException(stage); await Task.Delay(10); }
    }
}

[InitializeOnLoad]
public static class GrappleSpikeValidationResults
{
    private static UnityEditor.TestTools.TestRunner.Api.TestRunnerApi api;
    static GrappleSpikeValidationResults()
    {
        if (SessionState.GetBool("GrappleSpikeRecordTests", false)) Register();
    }
    public static void Register()
    {
        if (api != null) return;
        SessionState.SetBool("GrappleSpikeRecordTests", true);
        api = ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>();
        api.RegisterCallbacks(new Recorder());
    }
    private sealed class Recorder : UnityEditor.TestTools.TestRunner.Api.ICallbacks
    {
        public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor tests) { }
        public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
        public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result) { }
        public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
        {
            Directory.CreateDirectory("Temp/GrappleSpike");
            File.WriteAllText("Temp/GrappleSpike/final-tests.xml", result.ToXml().OuterXml);
            File.WriteAllText("Temp/GrappleSpike/final-tests-summary.txt",
                "passed=" + result.PassCount + " failed=" + result.FailCount + " skipped=" + result.SkipCount +
                " inconclusive=" + result.InconclusiveCount + " result=" + result.ResultState);
            SessionState.SetBool("GrappleSpikeRecordTests", false);
        }
    }
}

