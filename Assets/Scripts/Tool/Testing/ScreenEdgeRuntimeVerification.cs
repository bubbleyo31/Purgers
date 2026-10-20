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

/// <summary>明確啟動的同 Editor Host/Client 呈現驗證。只改暫時測試實例，不存場景或資產。</summary>
public sealed class ScreenEdgeRuntimeVerification : SimulationBehaviour
{
    public static string Status { get; private set; }="尚未執行";
    public static string Stage { get; private set; }="";
    private static NetworkRunner host,client;
    private static Player hostPlayer,clientOwnedPlayer;
    private static float heldEnergy=-1;
    private static readonly List<string> report=new List<string>();
    private static readonly List<GameObject> temporary=new List<GameObject>();
    private static readonly PropertyInfo energyProperty=typeof(PlayerGrappleMomentumEnergy).GetProperty("NormalizedEnergy");
    private static readonly PropertyInfo boostTimerProperty=typeof(PlayerGrappleMomentumEnergy).GetProperty("MaximumBoostTimer",BindingFlags.Instance|BindingFlags.NonPublic);
    private static string session;
    private static LocalPlayerScreenEdgeEffects authored;
    public override void FixedUpdateNetwork()
    {
        foreach(var p in new[]{hostPlayer,clientOwnedPlayer})
            if(p!=null && p.Object!=null && p.Object.IsValid)
            {
                var pos=new Vector3(7000+(p==hostPlayer?0:4),700,7000);
                p.Movement.KCC.SetPosition(pos);p.Movement.KCC.SetDynamicVelocity(Vector3.zero);
            }
        if(heldEnergy>=0 && clientOwnedPlayer!=null && clientOwnedPlayer.Object.IsValid)
        {
            var energy=clientOwnedPlayer.GetComponent<PlayerGrappleMomentumEnergy>();
            energyProperty.SetValue(energy,heldEnergy);
            // Preserve the real full-state invariant so TickSimulation does not decay a fake full value.
            boostTimerProperty.SetValue(energy,heldEnergy>=1 ? TickTimer.CreateFromSeconds(Runner,.25f) : TickTimer.None);
        }
    }
    private static void Check(bool value,string label)
    {if(!value)throw new InvalidOperationException(label);report.Add("PASS "+label);}
    private static async Task Until(Func<bool> predicate,string stage)
    {Stage=stage;float end=Time.realtimeSinceStartup+25;while(!predicate()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(stage);await Task.Delay(10);}}
    private static DamageRequest Hit(float amount,int sequence)=>new DamageRequest {RequestedDamage=amount,BaseDamage=amount,DamageType=DamageType.Environment,Sequence=sequence};
    private static void Hurt(PlayerHealth health,float amount,int sequence)
    {Check(DamageReceiverUtility.TryApplyDamage(health.gameObject,Hit(amount,sequence),out var result)&&result.Accepted,"authoritative damage accepted "+sequence);}
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Edge verification Host":"Edge verification Client").AddComponent<NetworkRunner>();runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>input.Set(default(NetInput)));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs {GameMode=server?GameMode.Host:GameMode.Client,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner connects "+result.ShutdownReason);await Until(()=>runner.IsRunning&&runner.Tick.Raw>0,"first tick");return runner;
    }
    private static LocalPlayerScreenEdgeEffects Hud(NetworkRunner runner)
    {
        var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ScreenEdgeEffects.prefab"));temporary.Add(root);
        root.GetComponent<Canvas>().enabled=false; // 不覆蓋使用者視窗；材質／權重仍按正式元件更新。
        var hud=root.GetComponent<LocalPlayerScreenEdgeEffects>();hud.Configure(hud.Settings,runner);return hud;
    }
    public static async void Begin()
    {
        if(!Application.isPlaying || NetworkRunner.Instances.Any(r=>r.IsRunning))throw new InvalidOperationException("需要沒有運行中 Runner 的 Play Mode。");
        Status="執行中";report.Clear();session="edge-"+Guid.NewGuid().ToString("N");heldEnergy=-1;
        try
        {
            authored=UnityEngine.Object.FindObjectOfType<LocalPlayerScreenEdgeEffects>();Check(authored!=null,"authored HUD present");
            host=await Connect(true);host.AddGlobal(host.gameObject.AddComponent<ScreenEdgeRuntimeVerification>());
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var first=host.Spawn(prefab,new Vector3(7000,700,7000),Quaternion.identity,host.LocalPlayer);host.SetPlayerObject(host.LocalPlayer,first);hostPlayer=first.GetComponent<Player>();
            authored.Configure(authored.Settings,host);
            await Until(()=>authored.BoundHealth==hostPlayer.Health&&hostPlayer.Health.IsRescueReady,"bind host");
            Hurt(hostPlayer.Health,10,1);await Until(()=>authored.DamageEvents==1&&authored.Weights.y>.2f,"single host damage");
            Check(LocalPlayerScreenEdgeEffects.OwnsHealing(hostPlayer.Health),"new owner suppresses only legacy healing LGG");
            Check(hostPlayer.Health.RestoreHealth(5),"host healing applies");await Until(()=>authored.HealingEvents==1&&authored.Weights.x>0,"single host heal");
            await Until(()=>authored.Weights.sqrMagnitude<.0001f,"host transient clears");
            client=await Connect(false);await Until(()=>client.FindObject(first.Id)!=null,"late join host replica");
            var other=Hud(client);await Task.Delay(150);Check(other.HealingEvents==0&&other.DamageEvents==0&&other.Weights==Vector3.zero,"late join no old transient replay");
            var second=host.Spawn(prefab,new Vector3(7004,700,7000),Quaternion.identity,client.LocalPlayer);host.SetPlayerObject(client.LocalPlayer,second);clientOwnedPlayer=second.GetComponent<Player>();
            await Until(()=>other.BoundHealth!=null&&other.BoundHealth.IsRescueReady,"bind client owner");
            Check(other.BoundHealth.Object.HasInputAuthority&&!other.BoundHealth.Object.HasStateAuthority,"client is input authority only");
            int hostCount=authored.DamageEvents;
            Hurt(clientOwnedPlayer.Health,20,2);await Until(()=>other.DamageEvents==1&&other.Weights.y>.2f,"host result reaches victim client");
            Check(authored.DamageEvents==hostCount,"no damage event leaked to host viewer");
            Check(!other.BoundHealth.ReceiveDamage(Hit(1,3)).Accepted,"client cannot authorize damage");
            Check(!other.BoundHealth.RestoreHealth(1),"client cannot authorize healing");
            for(int i=0;i<5;i++) {Check(clientOwnedPlayer.Health.RestoreHealth(1),"repeated healing accepted");await Task.Delay(180);}
            await Until(()=>other.HealingEvents==5,"all healing events received");Check(other.Weights.x>0&&other.Weights.x<=1,"continuous healing bounded and visible");
            for(int i=0;i<5;i++) {Hurt(clientOwnedPlayer.Health,1,10+i);await Task.Delay(180);}
            Check(other.DamageEvents==6&&other.Weights.y>0&&other.Weights.y<=1,"continuous damage bounded and visible");
            await Until(()=>other.Weights.sqrMagnitude<.0001f,"last event tail clears");
            var blocker=clientOwnedPlayer.gameObject.AddComponent<DamageIndicatorVerificationBlocker>();
            int count=other.DamageEvents;DamageReceiverUtility.TryApplyDamage(clientOwnedPlayer.gameObject,Hit(1,30),out _);await Task.Delay(180);
            Check(other.DamageEvents==count,"fully blocked hit creates no red injury");UnityEngine.Object.Destroy(blocker);
            heldEnergy=.5f;await Until(()=>other.BoundHealth.GetComponent<PlayerGrappleMomentumEnergy>().NormalizedEnergy>.4f,"partial energy replicated");
            await Task.Delay(250);Check(other.Weights.z==0,"partial gauge does not start speed momentum boost");
            heldEnergy=.99f;await Until(()=>other.BoundHealth.GetComponent<PlayerGrappleMomentumEnergy>().NormalizedEnergy>.9f,"near-full energy replicated with normal simulation decay");
            await Task.Delay(250);Check(other.Weights.z==0,"near-full gauge does not start speed momentum boost");
            heldEnergy=1;await Until(()=>other.Weights.z>.9f,"replicated full momentum appears on client");
            Check(authored.Weights.z<.01f,"momentum belongs to correct local player");
            other.enabled=false;Check(other.Weights==Vector3.zero&&!LocalPlayerScreenEdgeEffects.OwnsHealing(other.BoundHealth),"disable clears and releases legacy healing ownership");
            other.enabled=true;await Until(()=>other.Weights.z>.9f,"rebind restores persistent momentum without replay");
            heldEnergy=.9f;await Until(()=>other.Weights.z<.001f,"leaving full gauge fades boost out even with remaining energy");
            Check(other.BoundHealth.GetComponent<PlayerGrappleMomentumEnergy>().NormalizedEnergy>.8f,"boost cleared while gauge remains partially charged");heldEnergy=-1;
            Hurt(clientOwnedPlayer.Health,10000,40);await Until(()=>other.Weights==Vector3.zero&&!other.BoundHealth.IsAlive,"death clears all effects");
            host.Despawn(second);clientOwnedPlayer=null;await Until(()=>other.BoundHealth==null,"despawn unbinds");
            var replacement=host.Spawn(prefab,new Vector3(7004,700,7000),Quaternion.identity,client.LocalPlayer);host.SetPlayerObject(client.LocalPlayer,replacement);clientOwnedPlayer=replacement.GetComponent<Player>();
            await Until(()=>other.BoundHealth!=null&&other.BoundHealth.IsRescueReady,"replacement binds");Check(other.Weights==Vector3.zero,"replacement has no old effect");
            await client.Shutdown();await Until(()=>other.Weights==Vector3.zero&&other.BoundHealth==null,"disconnect clears");client=null;
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[ScreenEdgeVerification] "+ex);}
        finally
        {
            heldEnergy=-1;hostPlayer=clientOwnedPlayer=null;
            if(client!=null){await client.Shutdown();client=null;}if(host!=null){await host.Shutdown();host=null;}
            foreach(var go in temporary)if(go!=null)UnityEngine.Object.Destroy(go);temporary.Clear();
            if(authored!=null)authored.Configure(authored.Settings,null);
            report.Add("RESULT "+Status+"; final stage="+Stage);
            Directory.CreateDirectory("Temp/ScreenEdgeEffects");File.WriteAllLines("Temp/ScreenEdgeEffects/host-client.txt",report);
        }
    }
}
#endif
