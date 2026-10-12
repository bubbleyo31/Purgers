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

/// <summary>同程序雙 Runner 的受控 Host／Client 探測；不是獨立雙程序或實際手感驗收。</summary>
public static class AbilityRecoilNetworkVerification
{
    public static string Status { get; private set; }="尚未執行";
    public static string Stage { get; private set; }="";
    private static NetworkRunner host,client;
    private static RecoilNetworkFixedAction fixedActions;
    private static bool hostPress,clientPress;
    private static string session;
    private static readonly List<string> report=new List<string>();
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public static async void Begin()
    {
        if(!Application.isPlaying||host!=null||UnityEngine.Object.FindObjectsOfType<NetworkRunner>().Any(r=>r.IsRunning))
            throw new InvalidOperationException("需 Play Mode 且沒有其他運行中 Runner。");
        Status="執行中";report.Clear();hostPress=false;clientPress=false;session="purgers-recoil-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            fixedActions=host.gameObject.AddComponent<RecoilNetworkFixedAction>();host.AddGlobal(fixedActions);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var player=Spawn(prefab,host.LocalPlayer,new Vector3(1400,100,1400));
            var runtime=await Equip(player);
            var skill=runtime.GetComponent<PlayerActiveAbilityBase>();
            hostPress=true;
            await Until(()=> (int)Property(runtime,"CameraRecoilSequence")==2 && (float)Property(runtime,"PendingCameraPitch")==0,"host both events");
            hostPress=false;
            Check(Near(player.Movement.KCC.FixedData.LookPitch,-8),"Host input triggers both camera stages with total minus eight degrees");
            Check(player.Movement.KCC.FixedData.TargetPosition.z<1399.9f,"Host receives backward movement recoil");
            client=await Connect(false);
            await Until(()=>client.FindObject(player.Object.Id)!=null&&client.FindObject(runtime.Object.Id)!=null,"late join objects");
            var proxyPlayer=client.FindObject(player.Object.Id).GetComponent<Player>();
            var proxy=client.FindObject(runtime.Object.Id).GetComponent<PlayerAbilityRuntime>();
            await Until(()=>Near(proxyPlayer.Movement.KCC.FixedData.LookPitch,-8),"late join KCC look state");
            Check((int)Property(proxy,"CameraRecoilSequence")==2,"Late join receives existing sequence without replay");
            Check(!proxy.Object.HasStateAuthority,"Proxy has no gameplay authority");
            proxy.ApplyAbilityRecoil(PlayerAbilityRecoilEvent.BeforeCast);
            await Task.Delay(250);
            Check(Near(proxyPlayer.Movement.KCC.FixedData.LookPitch,-8)&&(int)Property(runtime,"CameraRecoilSequence")==2,"Proxy direct call cannot add recoil");
            SetProperty(runtime,"CameraRecoveryTimer",TickTimer.None);
            await Until(()=>Mathf.Abs(player.Movement.KCC.FixedData.LookPitch)<.01f,"host automatic recovery");
            await Until(()=>Mathf.Abs(proxyPlayer.Movement.KCC.FixedData.LookPitch)<.1f,"proxy recovery state");
            Check(true,"Authority recovery reaches proxy without extra event");
            await client.Shutdown();client=null;
            client=await Connect(false);
            await Until(()=>client.FindObject(player.Object.Id)!=null,"reconnect objects");
            proxyPlayer=client.FindObject(player.Object.Id).GetComponent<Player>();
            await Task.Delay(300);
            Check(Mathf.Abs(proxyPlayer.Movement.KCC.FixedData.LookPitch)<.1f,"Reconnect after recovery does not replay old kick");

            var remote=Spawn(prefab,client.LocalPlayer,new Vector3(1420,100,1400));
            var remoteRuntime=await Equip(remote);
            await Until(()=>client.FindObject(remote.Object.Id)!=null&&client.FindObject(remoteRuntime.Object.Id)!=null,"input authority objects");
            var own=client.FindObject(remote.Object.Id).GetComponent<Player>();
            Check(own.Object.HasInputAuthority&&!own.Object.HasStateAuthority,"Client player has only Input Authority");
            clientPress=true;
            await Until(()=>(int)Property(remoteRuntime,"CameraRecoilSequence")==2&&(float)Property(remoteRuntime,"PendingCameraPitch")==0,"client E reaches Host stages");
            clientPress=false;
            await Until(()=>Near(own.Movement.KCC.FixedData.LookPitch,-8),"client authoritative look correction");
            Check(Near(remote.Movement.KCC.FixedData.LookPitch,-8),"Client E executes exactly two kicks on Host");
            Check(own.Movement.KCC.FixedData.TargetPosition.z<1399.9f,"Client receives authoritative backward movement");
            await Task.Delay(250);
            Check((int)Property(remoteRuntime,"CameraRecoilSequence")==2,"Held client E does not repeatedly enqueue recoil");
            SetProperty(remoteRuntime,"CameraRecoveryTimer",TickTimer.None);
            await Until(()=>Mathf.Abs(remote.Movement.KCC.FixedData.LookPitch)<.01f&&Mathf.Abs(own.Movement.KCC.FixedData.LookPitch)<.1f,"remote recovery");
            Check(true,"Input Authority and Host converge after recovery");
            await Until(()=>!remoteRuntime.GetComponent<PlayerActiveAbilityBase>().IsAbilityActive,"shield ends before Blink");
            var blinkRuntime=await Equip(remote,"Blink");
            var blink=blinkRuntime.GetComponent<PlayerBlinkAbility>();
            var blinkSettings=blinkRuntime.GetType().GetField("recoil",Hidden).GetValue(blinkRuntime);
            Set(blinkSettings,"cameraTiming",PlayerAbilityRecoilTiming.AfterCast);
            Set(blinkSettings,"movementEnabled",false);
            await Until(()=>client.FindObject(blinkRuntime.Object.Id)!=null,"client receives Blink");
            clientPress=true;
            await Until(()=>blink.Phase==PlayerActiveAbilityPhase.Armed,"client first E arms Blink");
            await Task.Delay(250);
            Check((int)Property(blinkRuntime,"CameraRecoilSequence")==0 && Near(remote.Movement.KCC.FixedData.LookPitch,0),
                "Client Blink first E prepares without actual-release recoil");
            Check(blink.Phase==PlayerActiveAbilityPhase.Armed,"Held client E cannot release Blink");
            clientPress=false; await Task.Delay(250);
            clientPress=true;
            await Until(()=>(int)Property(blinkRuntime,"CameraRecoilSequence")==1 && (float)Property(blinkRuntime,"PendingCameraPitch")==0,
                "client second E commits Blink");
            clientPress=false;
            await Until(()=>Near(own.Movement.KCC.FixedData.LookPitch,-4),"Blink camera reaches Input Authority");
            Check(Near(remote.Movement.KCC.FixedData.LookPitch,-4),"Client Blink second E applies one authoritative camera recoil");
            await Task.Delay(250);
            Check((int)Property(blinkRuntime,"CameraRecoilSequence")==1,"Client Blink release recoil does not replay");
            await Until(()=>!blink.IsDashing,"Blink ends before displacement checks");
            foreach(bool air in new[]{false,true})
            {
                var motionRuntime=await Equip(remote,"Shield");
                var motionSkill=motionRuntime.GetComponent<PlayerActiveAbilityBase>();
                await Until(()=>!motionSkill.IsAbilityActive && motionSkill.CooldownRemainingSeconds<=0,"motion skill ready");
                var motionSettings=motionRuntime.GetType().GetField("recoil",Hidden).GetValue(motionRuntime);
                Set(motionSettings,"cameraEnabled",false);Set(motionSettings,"movementEnabled",true);
                Set(motionSettings,"movementTiming",PlayerAbilityRecoilTiming.AfterCast);Set(motionSettings,"movementSpeed",10f);
                Set(motionSettings,"movementDecayDuration",.4f);
                await fixedActions.Run(()=>{
                    remote.Movement.KCC.SetPosition(new Vector3(1420,air?150:100.03f,1400));
                    remote.Movement.KCC.SetLookRotation(0,0);remote.Movement.KCC.SetDynamicVelocity(Vector3.zero);remote.Movement.KCC.SetKinematicVelocity(Vector3.zero);
                });
                // Teleport retains the previous grounded flag until KCC simulates the new pose.
                await Task.Delay(150);
                if(!air)await Until(()=>remote.Movement.KCC.FixedData.IsGrounded,"motion ground contact");
                else await Until(()=>!remote.Movement.KCC.FixedData.IsGrounded && remote.Movement.KCC.FixedData.TargetPosition.y>120,"motion is airborne");
                report.Add("START air="+air+" grounded="+remote.Movement.KCC.FixedData.IsGrounded+" pitch="+remote.Movement.KCC.FixedData.LookPitch+" position="+remote.Movement.KCC.FixedData.TargetPosition);
                Check(remote.Movement.KCC.FixedData.IsGrounded==!air && Mathf.Abs(remote.Movement.KCC.FixedData.LookPitch)<.01f,
                    "Client trial starts with verified grounded state and horizontal aim air="+air);
                float start=remote.Movement.KCC.FixedData.TargetPosition.z;
                int sequence=motionSkill.ActivationSequence;
                clientPress=true;await Until(()=>motionSkill.ActivationSequence>sequence,"Client movement recoil press");clientPress=false;
                await Until(()=>motionSkill.Phase==PlayerActiveAbilityPhase.Active,"Client movement recoil release");
                int tick=host.Tick.Raw;
                await Until(()=>host.Tick.Raw>=tick+Mathf.CeilToInt(.7f/host.DeltaTime),"Client movement recoil ends");
                float distance=start-remote.Movement.KCC.FixedData.TargetPosition.z;
                report.Add("MEASURE Client recoil air="+air+" distance="+distance);
                Check(Mathf.Abs(distance-2)<.05f,"Client recoil has two metres air="+air);
                await Until(()=>Mathf.Abs(own.Movement.KCC.FixedData.TargetPosition.z-remote.Movement.KCC.FixedData.TargetPosition.z)<.05f,"Client motion correction");
                Check(true,"Input Authority receives authoritative recoil distance air="+air);
            }
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);}
        finally
        {
            hostPress=false;clientPress=false;
            if(client!=null){try{await client.Shutdown();}catch(Exception ex){Status="失敗";report.Add("FAIL client cleanup "+ex);}client=null;}
            if(host!=null){try{await host.Shutdown();}catch(Exception ex){Status="失敗";report.Add("FAIL host cleanup "+ex);}host=null;}
            report.Insert(0,DateTime.UtcNow.ToString("O")+" "+Status+"; controlled Host/Client in one Editor process");
            File.WriteAllLines(Path.GetFullPath("Temp/AbilityRecoilNetworkVerification.txt"),report);
            Debug.Log("[AbilityRecoilNetworkVerification] "+Status);
        }
    }
    private static Player Spawn(GameObject prefab,PlayerRef authority,Vector3 position)
    {
        var obj=host.Spawn(prefab.GetComponent<NetworkObject>(),position,Quaternion.identity,authority);host.SetPlayerObject(authority,obj);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(floor);
        floor.transform.position=position+Vector3.down;floor.transform.localScale=new Vector3(50,1,50);
        return obj.GetComponent<Player>();
    }
    private static async Task<PlayerAbilityRuntime> Equip(Player owner, string ability = "Shield")
    {
        await Until(()=>owner.AbilityRuntimeManager.LoadoutRevision>0,"loadout ready");
        var def=AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/"+ability+".asset");
        Check(owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(def,out string error),"equip "+ability+" "+error);
        await Until(()=>PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner)!=null && PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner).GetComponent<PlayerAbilityRuntime>().Definition==def,"skill ready");
        var skill=PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner);var runtime=skill.GetComponent<PlayerAbilityRuntime>();
        Set(skill,"castDelaySeconds",.2f,typeof(PlayerActiveAbilityBase));Set(skill,"cooldownSeconds",0f,typeof(PlayerActiveAbilityBase));
        var settings=runtime.GetType().GetField("recoil",Hidden).GetValue(runtime);
        Set(settings,"cameraEnabled",true);Set(settings,"cameraAngles",new Vector2(-4,0));Set(settings,"cameraTiming",(PlayerAbilityRecoilTiming)3);
        Set(settings,"cameraApplicationDuration",.1f);Set(settings,"cameraRecoveryDelay",60f);Set(settings,"cameraRecoverySpeed",40f);
        Set(settings,"movementEnabled",true);Set(settings,"movementTiming",(PlayerAbilityRecoilTiming)3);Set(settings,"movementSpeed",6f);
        return runtime;
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        Stage=server?"Host StartGame":"Client StartGame";
        var runner=new GameObject(server?"Recoil Host":"Recoil Client").AddComponent<NetworkRunner>();if(server)host=runner;else client=runner;
        runner.ProvideInput=true;var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r,input)=>{var data=new NetInput();data.Buttons.Set(InputButton.Ability1,server?hostPress:clientPress);input.Set(data);});
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var e in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(e.Item2);
        // 重用既有隔離測試場景管理器，防止測試 Runner 接管使用者 Menu 場景。
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var settings=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);settings.FixedRegion="asia";
        var task=runner.StartGame(new StartGameArgs {GameMode=server?GameMode.Host:GameMode.Client,SessionName=session,Config=config,SceneManager=manager,
            CustomPhotonAppSettings=settings,PlayerCount=2,IsVisible=false});
        if(await Task.WhenAny(task,Task.Delay(45000))!=task)throw new TimeoutException(Stage);
        var result=await task;Check(result.Ok,Stage+" "+result.ShutdownReason);
        await Until(()=>runner.IsRunning&&!runner.SceneManager.IsBusy&&runner.Tick.Raw>0,"initial network tick");return runner;
    }
    private static object Property(object obj,string name)=>obj.GetType().GetProperty(name,Hidden|BindingFlags.Public).GetValue(obj);
    private static void SetProperty(object obj,string name,object value)=>obj.GetType().GetProperty(name,Hidden|BindingFlags.Public).SetValue(obj,value);
    private static void Set(object obj,string name,object value,Type type=null)=>(type??obj.GetType()).GetField(name,Hidden).SetValue(obj,value);
    private static bool Near(float a,float b)=>Mathf.Abs(a-b)<.1f;
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);report.Add("PASS "+label);}
    private static async Task Until(Func<bool> predicate,string label)
    {Stage=label;float deadline=Time.realtimeSinceStartup+20;while(!predicate()){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(label);await Task.Delay(30);}}
}

/// <summary>測試專用：KCC 擺位只能在真正 FixedUpdateNetwork 執行，避免只改到 RenderData。</summary>
public sealed class RecoilNetworkFixedAction : SimulationBehaviour
{
    private Action pending;
    private TaskCompletionSource<bool> completion;
    public async Task Run(Action action)
    {
        if(pending!=null)throw new InvalidOperationException("Fixed action already pending");
        var done=new TaskCompletionSource<bool>();pending=action;completion=done;
        if(await Task.WhenAny(done.Task,Task.Delay(10000))!=done.Task)throw new TimeoutException("network fixed action");
        await done.Task;
    }
    public override void FixedUpdateNetwork()
    {
        if(pending==null)return;
        var action=pending;var done=completion;pending=null;completion=null;
        try{action();done.SetResult(true);}catch(Exception ex){done.SetException(ex);}
    }
}
#endif
