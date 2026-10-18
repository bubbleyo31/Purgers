using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>隔離 Runner、正式玩家 Prefab 與原狀態入口的受控檢查；不保存 Scene、Prefab 或存檔。</summary>
public static class StatusEffectRuntimeVerification
{
    public static string Status { get; private set; } = "未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static StageTimerPauseVerificationDriver driver;
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly List<string> report = new List<string>();
    private static readonly Vector3 Origin = new Vector3(6000,6000,6000);
    private static string session;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r=>r.IsRunning)) throw new InvalidOperationException("需無 Runner 的隔離 Play Mode。");
        Status="執行中"; report.Clear(); session="status-hud-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);driver=host.gameObject.AddComponent<StageTimerPauseVerificationDriver>();host.AddGlobal(driver);
            var enemy=Enemy(host);var state=enemy.GetComponent<EnemyStateController>();var mark=enemy.GetComponent<AttackGrappleMarkState>();
            await Until(()=>enemy.HasSnapshot,"enemy first valid snapshot");
            Check(!enemy.IsReadyFor(host),"full health initially hidden");
            await driver.AtTick(()=>state.TryBeginTimedStun(60));
            await Until(()=>state.StunStatus.IsVisible,"authoritative stun starts without damage");
            Check(enemy.IsReadyFor(host,true) && Near(enemy.HealthRatio,1),"full health status forces health visibility");
            await Task.Delay(350);float before=state.StunStatus.Ratio;
            await driver.AtTick(()=>state.TryBeginTimedStun(1));
            Check(state.StunStatus.Ratio<=before+.01f,"shorter stun overlap does not refill");
            await driver.AtTick(()=>state.TryBeginTimedStun(90));
            Check(state.StunStatus.Ratio>.98f,"longer stun resets denominator");
            client=await Connect(false);var replica=Enemy(client);
            await Until(()=>replica.HasSnapshot && replica.GetComponent<EnemyStateController>().StunStatus.IsVisible,"late join receives current stun");
            var remoteState=replica.GetComponent<EnemyStateController>();
            Check(Near(remoteState.StunStatus.Ratio,state.StunStatus.Ratio,.06f),"late join uses shared denominator rather than full bar");
            Check(!remoteState.TryBeginTimedStun(100),"client cannot change authoritative stun");
            await driver.AtTick(()=>{mark.ApplyOrRefreshMark(host.LocalPlayer,8);mark.ApplyOrRefreshMark(client.LocalPlayer,3);});
            await Until(()=>replica.GetComponent<AttackGrappleMarkState>().GetMarkStatus(client.LocalPlayer).IsVisible,"mark dictionaries and spans replicate");
            await Task.Delay(500);
            Check(mark.GetMarkStatus(host.LocalPlayer).Ratio>mark.GetMarkStatus(client.LocalPlayer).Ratio,"each marker reads independent duration");
            Check(!mark.GetMarkStatus(PlayerRef.None).IsVisible,"no observer identity never exposes marks");
            await driver.AtTick(()=>mark.ApplyOrRefreshMark(host.LocalPlayer,2));
            Check(mark.GetMarkStatus(host.LocalPlayer).Ratio>.98f,"explicit mark refresh refills even when shorter");
            var buffer=new StatusEffectSnapshot[StatusEffectSources.Capacity];
            Check(enemy.ReadStatusEffects(client,buffer)==0 && replica.ReadStatusEffects(host,buffer)==0,"enemy source rejects wrong Runner");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var own=host.Spawn(prefab.GetComponent<NetworkObject>(),Origin+Vector3.right*2,Quaternion.identity,host.LocalPlayer);
            var other=host.Spawn(prefab.GetComponent<NetworkObject>(),Origin+Vector3.left*2,Quaternion.identity,client.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer,own);host.SetPlayerObject(client.LocalPlayer,other);
            var player=own.GetComponent<Player>();var gate=own.GetComponent<PlayerActionGate>();
            await Until(()=>player.Health.IsAlive && player.AbilityRuntimeManager.LoadoutRevision>0 && client.TryFindObject(own.Id,out _),"formal players ready on both peers");
            await driver.AtTick(()=>gate.ApplyExplosionStatus(8,.25f,10));
            await Task.Delay(400);
            await driver.AtTick(()=>gate.ApplyExplosionStatus(1,.8f,1));
            Check(gate.GrappleBlockedStatus.Ratio<.99f && gate.GrappleBlockedStatus.Ratio>.8f,"shorter grapple block preserves progress");
            Check(gate.SlowStatus.IsVisible && Near(gate.ExplosionGroundSpeedMultiplier,.2f),"strong short slow controls gameplay independently of HUD union");
            await Task.Delay(1200);
            Check(gate.SlowStatus.IsVisible && Near(gate.ExplosionGroundSpeedMultiplier,.75f),"weak long slow remains visible after strongest expires");
            var remoteGate=client.FindObject(own.Id).GetComponent<PlayerActionGate>();
            await Until(()=>remoteGate.SlowStatus.IsVisible && Near(remoteGate.SlowStatus.Ratio,gate.SlowStatus.Ratio,.05f),"teammate snapshot matches authoritative slow duration");
            Check(!remoteGate.Object.HasInputAuthority && !remoteGate.Object.HasStateAuthority,"observer is neither target input nor state authority");
            var inputGate=client.FindObject(other.Id).GetComponent<PlayerActionGate>();
            Check(inputGate.Object.HasInputAuthority && !inputGate.Object.HasStateAuthority,"remote player retains input authority separation");
            await driver.AtTick(()=>gate.ClearNegativeControlEffects());
            await Until(()=>!remoteGate.SlowStatus.IsVisible && !remoteGate.GrappleBlockedStatus.IsVisible,"cleanse removes negative status bars on both peers");

            var definition=AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/BulletTime.asset");
            await driver.AtTick(()=>Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition,out var error),"equip production bullet time: "+error));
            await Until(()=>PlayerAbilityQualification.GetModule<PlayerBulletTimeAbility>(player)!=null,"bullet time module ready");
            var bullet=PlayerAbilityQualification.GetModule<PlayerBulletTimeAbility>(player);
            Set(bullet,"durationSeconds",8f);Set(bullet,"effectRadius",10000f);Set(bullet,"playerMovementMultiplier",1f);
            await driver.AtTick(()=>{var input=new NetInput();input.Buttons.Set(InputButton.Ability1,true);bullet.SimulateAbility(input,default);});
            await Until(()=>bullet.IsSessionActive && gate.BulletTimeStatus.IsVisible && state.BulletTimeStatus.IsVisible,"bullet time appears on player and enemy");
            Check(Near(gate.ExplosionGroundSpeedMultiplier,1) && Near(bullet.PlayerMovementMultiplier,1),"bullet status does not depend on slow multiplier");
            float paused=state.StunStatus.Ratio;await Task.Delay(500);
            Check(Near(paused,state.StunStatus.Ratio,.025f),"bullet time pauses underlying stun bar");
            await Until(()=>remoteGate.BulletTimeStatus.IsVisible && remoteState.BulletTimeStatus.IsVisible,"bullet status replicated to non-authority observer");
            var sources=new StatusEffectSources(own.gameObject);int count=sources.Read(host.LocalPlayer,buffer);
            Check(count==1 && buffer[0].Kind==StatusEffectKind.BulletTime,"self reads bullet-only snapshot after cleanse");
            await driver.AtTick(()=>{var input=new NetInput();input.Buttons.Set(InputButton.Ability1,true);bullet.SimulateAbility(input,default);});
            await Until(()=>!gate.BulletTimeStatus.IsVisible && !remoteGate.BulletTimeStatus.IsVisible,"manual cancellation removes bullet status on both peers");
            await driver.AtTick(()=>state.TryBeginTimedStun(90));await Task.Delay(400);
            float beforeReconnect=state.StunStatus.Ratio;
            await client.Shutdown();client=null;client=await Connect(false);replica=Enemy(client);
            await Until(()=>replica.HasSnapshot && replica.GetComponent<EnemyStateController>().StunStatus.IsVisible,"reconnect restores active target");
            Check(replica.GetComponent<EnemyStateController>().StunStatus.Ratio<=beforeReconnect+.05f,"reconnect cannot refill old stun");
            await driver.AtTick(()=>enemy.GetComponent<TestDamageReceiver>().ReceiveDamage(new DamageRequest { RequestedDamage=10000,BaseDamage=10000,DamageType=DamageType.Environment }));
            await Until(()=>!enemy.HasSnapshot && !replica.HasSnapshot,"death removes all enemy status presentation");
            await driver.AtTick(()=>host.Despawn(enemy.Object));
            Check(enemy==null || !enemy.HasSnapshot,"despawn clears status target");
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);}
        finally
        {
            if(client!=null)await client.Shutdown();if(host!=null)await host.Shutdown();client=host=null;
            foreach(var item in temporary)if(item!=null)UnityEngine.Object.Destroy(item);temporary.Clear();
            Directory.CreateDirectory("Temp/StatusEffects");File.WriteAllLines("Temp/StatusEffects/host-client.txt",report);
        }
    }
    private static EnemyHealthBarTarget Enemy(NetworkRunner runner)
    {
        var root=new GameObject("Status verification target",typeof(NetworkObject),typeof(EnemyActor),typeof(EnemyHealthBarTarget),typeof(AttackGrappleMarkState),typeof(FocusTarget),typeof(GrappleInteractionTarget));
        temporary.Add(root);runner.MoveToRunnerScene(root);root.transform.position=Origin;
        var definition=ScriptableObject.CreateInstance<EnemyDefinition>();temporary.Add(definition);
        Set(root.GetComponent<EnemyActor>(),"definition",definition);
        Set(root.GetComponent<TestDamageReceiver>(),"debugHealth",false);Set(root.GetComponent<AttackGrappleMarkState>(),"debugMark",false);
        new NetworkObjectBaker().Bake(root);runner.RegisterSceneObjects(SceneRef.FromIndex(0),new[]{root.GetComponent<NetworkObject>()},default);
        return root.GetComponent<EnemyHealthBarTarget>();
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Status Host":"Status Client").AddComponent<NetworkRunner>();temporary.Add(runner.gameObject);
        runner.ProvideInput=true;var events=runner.gameObject.AddComponent<NetworkEvents>();
        foreach(var field in typeof(NetworkEvents).GetFields(Flags))if(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType)&&field.GetValue(events)==null)field.SetValue(events,Activator.CreateInstance(field.FieldType));
        events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>input.Set(default(NetInput)));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs{GameMode=server?GameMode.Host:GameMode.Client,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=3,IsVisible=false});
        Check(result.Ok,"connect "+(server?"Host":"Client")+": "+result.ShutdownReason);
        await Until(()=>runner.IsRunning && runner.Tick.Raw>0 && !runner.SceneManager.IsBusy,"Runner first tick");return runner;
    }
    private static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Flags).SetValue(obj,value);
    private static bool Near(float a,float b,float tolerance=.01f)=>Mathf.Abs(a-b)<tolerance;
    private static void Check(bool result,string label){if(!result)throw new Exception(label);report.Add("PASS "+label);}
    private static async Task Until(Func<bool> condition,string label)
    {
        Stage=label;float end=Time.realtimeSinceStartup+40;
        while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(label);await Task.Delay(25);}
        report.Add("PASS "+label);
    }
}
