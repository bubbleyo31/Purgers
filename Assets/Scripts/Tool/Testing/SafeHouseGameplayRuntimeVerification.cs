#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.GameFlow.SafeHouse;
using UnityEditor;
using UnityEngine;

/// <summary>隔離 Runner 驗證；不載入／保存正式場景、存檔或 Prefab，不代替自然操作驗收。</summary>
public static class SafeHouseGameplayRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool multiplayer;
    private static string session;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static NetInput clientInput;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.Add("PASS " + message); }
    private static async Task Until(Func<bool> condition, string label)
    {
        Stage = label; float end = Time.realtimeSinceStartup + 35;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
    }
    public static async void Begin(bool network = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無現存 Runner 的 Play Mode。");
        Status = "執行中"; report.Clear(); multiplayer = network; clientInput = default;
        session = "safehouse-rules-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var playerObject = host.Spawn(prefab, new Vector3(1500, 2000, 1500), Quaternion.identity, host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer, playerObject);
            var player = playerObject.GetComponent<Player>(); var gate = playerObject.GetComponent<PlayerActionGate>();
            await Until(() => gate.IsFusionSpawned && player.Health.IsAlive, "player spawned");
            var probe = playerObject.gameObject.AddComponent<SafeHouseDamageProbe>();
            Check(!SafeHouseFlowController.IsActiveFor(host), "outside SafeHouse has no scene restrictions");
            var safe = CreateFlow(host);
            await Until(() => safe.IsNetworkReady, "safe flow spawned");
            Check(SafeHouseFlowController.IsActiveFor(host), "registered SafeHouse belongs to its Runner");
            Check(gate.IsBlocked(PlayerActionBlockMask.Grapple) && gate.IsBlocked(PlayerActionBlockMask.ActiveAbility), "Q and E are blocked");
            Check(!gate.IsBlocked(PlayerActionBlockMask.Fire | PlayerActionBlockMask.Aim | PlayerActionBlockMask.Reload | PlayerActionBlockMask.QuickAction | PlayerActionBlockMask.Movement | PlayerActionBlockMask.Look), "weapons F movement and look stay available");
            var manager = playerObject.GetComponent<PlayerAbilityRuntimeManager>();
            await Until(() => (NetworkBool)typeof(PlayerAbilityRuntimeManager).GetProperty("StartingLoadoutApplied", Private).GetValue(manager), "initial loadout tick");
            var shieldDefinition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Shield.asset");
            await driver.AtTick(() => {
                string error;
                var loadout = AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Loadouts/Shield.asset");
                typeof(PlayerAbilityRuntimeManager).GetField("startingLoadout", Private).SetValue(manager,loadout);
                Check(manager.TryApplyLoadoutStateAuthority(loadout, out error), "initial loadout: " + error);
                Check(manager.TryEquipRewardAbilityStateAuthority(shieldDefinition, out error), "loadout equip remains available: " + error);
            });
            PlayerShieldAbility shield = null;
            await Until(() => { for (int i=0;i<8;i++) { var runtime=manager.GetEquippedRuntimeAtSlot(i); if(runtime!=null && runtime.TryGetModule<PlayerShieldAbility>(out shield))return true; } return false; }, "shield equipped");
            await driver.AtTick(() =>
            {
                float energy = player.GrappleCharges.CurrentEnergy;
                var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
                manager.SimulateActiveAbilities(input, default);
                shield.SimulateAbility(input, default);
                Check(shield.Phase == PlayerActiveAbilityPhase.Ready && shield.CooldownRemainingSeconds == 0 && !shield.IsUsable, "E rejected without cooldown through manager and direct entry");
                player.Grapple.Simulate(true, true, false, false, Vector2.zero, 0f);
                Check(player.GrappleCharges.CurrentEnergy == energy, "Q rejection preserves grapple energy");
                float hp = player.Health.CurrentHealth;
                foreach (DamageType kind in Enum.GetValues(typeof(DamageType)))
                {
                    var request = new DamageRequest { RequestedDamage=5, BaseDamage=5, DamageType=kind, Attacker=host.LocalPlayer, SourceNetworkObject=playerObject, SourceObject=playerObject.gameObject };
                    DamageResult result;
                    DamageReceiverUtility.TryApplyDamage(playerObject.gameObject, request, out result);
                    Check(result.RejectReason == DamageRejectReason.FriendlyFireBlocked && result.AppliedDamage == 0, "self damage rejected: " + kind);
                    Check(player.Health.ReceiveDamage(request).RejectReason == DamageRejectReason.FriendlyFireBlocked, "direct health entry rejected: " + kind);
                }
                Check(player.Health.CurrentHealth == hp && probe.Calls == 0, "no HP loss or incoming modifier consumption");
                var sourceLess = new DamageRequest { RequestedDamage=2, BaseDamage=2, Attacker=PlayerRef.None, DamageType=DamageType.Environment };
                DamageResult environment; DamageReceiverUtility.TryApplyDamage(playerObject.gameObject, sourceLess, out environment);
                Check(environment.AppliedDamage > 0 && probe.Calls == 1, "environment damage remains governed by existing rules");
            });
            if (network)
            {
                client = await Connect(false);
                var clientSafe = CreateFlow(client);
                await Until(() => clientSafe.IsNetworkReady && SafeHouseFlowController.IsActiveFor(client), "late client receives SafeHouse scene object");
                var remote = host.Spawn(prefab, new Vector3(1510,2000,1500), Quaternion.identity, client.LocalPlayer);
                host.SetPlayerObject(client.LocalPlayer, remote);
                await Until(() => client.FindObject(remote.Id) != null, "client-owned player replicated");
                var replica = client.FindObject(remote.Id);
                await Until(() => replica.GetComponent<PlayerActionGate>().IsFusionSpawned, "client gate spawned");
                Check(replica.HasInputAuthority && !replica.HasStateAuthority && replica.GetComponent<PlayerActionGate>().IsBlocked(PlayerActionBlockMask.ActiveAbility | PlayerActionBlockMask.Grapple), "late Input Authority receives QE restrictions");
                await driver.AtTick(() =>
                {
                    var request = new DamageRequest { RequestedDamage=500, BaseDamage=500, Attacker=client.LocalPlayer, SourceNetworkObject=remote, DamageType=DamageType.Explosion };
                    DamageResult result; DamageReceiverUtility.TryApplyDamage(playerObject.gameObject, request, out result);
                    Check(result.RejectReason == DamageRejectReason.FriendlyFireBlocked && player.Health.IsAlive, "client to host lethal friendly damage rejected");
                    request.Attacker=host.LocalPlayer; request.SourceNetworkObject=playerObject;
                    DamageReceiverUtility.TryApplyDamage(remote.gameObject, request, out result);
                    Check(result.RejectReason == DamageRejectReason.FriendlyFireBlocked && remote.GetComponent<PlayerHealth>().IsAlive, "host to client lethal friendly damage rejected");
                });
                clientInput.Buttons.Set(InputButton.Grapple,true); clientInput.Buttons.Set(InputButton.Ability1,true);
                float before = remote.GetComponent<PlayerGrappleCharges>().CurrentEnergy;
                await Task.Delay(400); clientInput=default;
                Check(remote.GetComponent<PlayerGrappleCharges>().CurrentEnergy >= before, "submitted client Q/E cannot consume grapple charge");
                await client.Shutdown(); client=null;
                Check(SafeHouseFlowController.IsActiveFor(host), "client shutdown does not clear host restrictions");
                client = await Connect(false); clientSafe=CreateFlow(client);
                await Until(() => clientSafe.IsNetworkReady, "reconnected client flow spawned");
                Check(SafeHouseFlowController.IsActiveFor(client), "reconnect restores SafeHouse context");
            }
            await driver.AtTick(() =>
            {
                gate.SetBlocks(PlayerActionBlockSource.StatusEffect, PlayerActionBlockMask.Reload);
                host.Despawn(safe.Object);
                Check(!SafeHouseFlowController.IsActiveFor(host), "despawn removes SafeHouse context");
                Check(!gate.IsBlocked(PlayerActionBlockMask.Grapple | PlayerActionBlockMask.ActiveAbility) && gate.IsBlocked(PlayerActionBlockMask.Reload), "leaving unlocks QE without clearing other restrictions");
                var request = new DamageRequest { RequestedDamage=3, BaseDamage=3, Attacker=host.LocalPlayer, SourceNetworkObject=playerObject, DamageType=DamageType.Bullet };
                DamageResult result; DamageReceiverUtility.TryApplyDamage(playerObject.gameObject, request, out result);
                Check(result.AppliedDamage > 0 && probe.Calls == 2, "outside SafeHouse self damage and modifiers resume");
                var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
                manager.SimulateActiveAbilities(input, default);
                Check(shield.Phase != PlayerActiveAbilityPhase.Ready, "E resumes outside SafeHouse");
            });
            Status = "通過";
        }
        catch (Exception ex) { Status="失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError(ex); }
        finally
        {
            if(client!=null) { await client.Shutdown(); client=null; }
            if(host!=null) { await host.Shutdown(); host=null; }
            foreach(var item in temporary) if(item!=null) UnityEngine.Object.Destroy(item); temporary.Clear();
            Directory.CreateDirectory("Temp/SafeHouseGameplay");
            File.WriteAllLines("Temp/SafeHouseGameplay/"+(network?"host-client":"single")+".txt", report);
        }
    }
    private static SafeHouseFlowController CreateFlow(NetworkRunner runner)
    {
        var root = new GameObject("Temporary SafeHouse flow"); temporary.Add(root); runner.MoveToRunnerScene(root);
        var flow = root.AddComponent<SafeHouseFlowController>();
        typeof(SafeHouseFlowController).GetField("debugFlow", Private).SetValue(flow,false);
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[]{flow.GetComponent<NetworkObject>()}, default);
        return flow;
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server?"SafeHouse test host":"SafeHouse test client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput=new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r,input)=>input.Set(server?default(NetInput):clientInput));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var sceneManager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); sceneManager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs{GameMode=multiplayer?(server?GameMode.Host:GameMode.Client):GameMode.Single,SessionName=session,Config=config,SceneManager=sceneManager,CustomPhotonAppSettings=app,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason); await Until(()=>runner.IsRunning && runner.Tick.Raw>0,"runner first tick"); return runner;
    }
}
public sealed class SafeHouseDamageProbe : MonoBehaviour, IDamageRequestModifier
{
    public int Calls;
    public void ModifyDamageRequest(ref DamageRequest request) { Calls++; }
}
#endif
