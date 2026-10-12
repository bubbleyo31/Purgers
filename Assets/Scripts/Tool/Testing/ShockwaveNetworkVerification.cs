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

/// <summary>同 Editor 雙 Runner 的受控 Host／Client 探測；不等同獨立執行檔或自然實戰。</summary>
public static class ShockwaveNetworkVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static string session;
    private static bool remotePress;
    private static readonly List<string> report = new List<string>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static async void Begin()
    {
        if (!Application.isPlaying || host != null) throw new InvalidOperationException("需在 Play Mode 且無驗證進行。");
        Status="執行中"; report.Clear(); remotePress=false;
        session="purgers-wave-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var playerObject=host.Spawn(prefab.GetComponent<NetworkObject>(),new Vector3(1000,100,1000),Quaternion.identity,host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer,playerObject);
            var player=playerObject.GetComponent<Player>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            host.MoveToRunnerScene(floor);
            floor.transform.position=new Vector3(1000,99,1000);floor.transform.localScale=new Vector3(100,1,100);
            await Until(()=>player.AbilityRuntimeManager.LoadoutRevision>0,"host loadout");
            var definition=AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Shockwave.asset");
            Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition,out string error),"host equip "+error);
            await Until(()=>PlayerAbilityQualification.GetModule<PlayerShockwaveAbility>(player)!=null,"host module");
            var wave=PlayerAbilityQualification.GetModule<PlayerShockwaveAbility>(player);
            Set(wave,"castDelaySeconds",0f,typeof(PlayerActiveAbilityBase));
            Set(wave,"cooldownSeconds",0f,typeof(PlayerActiveAbilityBase));
            Set(wave,"radius",4f);Set(wave,"travelDistance",100f);Set(wave,"waveHeight",6f);Set(wave,"waveSpeed",2f);Set(wave,"waveThickness",2f);

            Set(wave,"originOffset",new Vector3(.4f,1.5f,.6f));
            var kcc=player.Movement.KCC;kcc.SetLookRotation(-30,70);
            kcc.FixedData.LookPitch=-30;kcc.FixedData.LookYaw=70;
            Press(wave);
            await Until(()=>wave.CurrentWaveFront>2f,"host wave travelling");
            var waveId=wave.Object.Id;
            client=await Connect(false);
            await Until(()=>client.FindObject(waveId)!=null,"late join wave object");
            var proxy=client.FindObject(waveId).GetComponent<PlayerShockwaveAbility>();
            await Until(()=>proxy.IsWaveRunning && proxy.CurrentWaveFront>1f,"late join active wave snapshot");
            Check(!proxy.Object.HasStateAuthority,"client has no State Authority");
            Check(proxy.CurrentWaveRange==100f && proxy.CurrentWaveRadius==4f && proxy.CurrentWaveHeight==6f && proxy.CurrentWaveThickness==2f,"host geometry overrides client prefab defaults");
            Check(Mathf.Abs(proxy.CurrentWaveFront-wave.CurrentWaveFront)<3f,"late join continues near host progress");

            Check(Quaternion.Angle(wave.CurrentWaveRotation,Quaternion.Euler(-30,70,0))<.1f,"host captures full aim rotation");
            Check(Quaternion.Angle(proxy.CurrentWaveRotation,wave.CurrentWaveRotation)<.1f &&
                Vector3.Distance(proxy.CurrentWaveOrigin,wave.CurrentWaveOrigin)<.01f &&
                Vector3.Distance(proxy.CurrentWaveDirection,wave.CurrentWaveDirection)<.001f,
                "late join receives complete aim and resolved offset snapshot");
            int sequence=proxy.ActivationSequence;Press(proxy);
            Check(proxy.ActivationSequence==sequence,"client direct call cannot activate or advance sequence");
            await Until(()=>Visuals(client)>0,"client local presentation");
            Check(Visuals(host)==1 && Visuals(client)==1,"each Runner owns one local visual");

            var clientVisual=Resources.FindObjectsOfTypeAll<PlayerShockwaveVisual>().First(v=>
                v.gameObject.scene.IsValid()&&v.gameObject.scene.GetPhysicsScene()==client.GetPhysicsScene());
            Check(Quaternion.Angle(clientVisual.transform.rotation,wave.CurrentWaveRotation)<.1f,"client mesh tilts with authoritative aim");
            await Task.Delay(400);
            Check(Mathf.Abs(proxy.CurrentWaveFront-wave.CurrentWaveFront)<3f,"ongoing wave progress replicates");

            typeof(PlayerShockwaveAbility).GetMethod("FinishAbility",Private).Invoke(wave,null);
            await Until(()=>!proxy.IsWaveRunning && Visuals(host)==0 && Visuals(client)==0,"cancellation replicates and clears both visuals");
            Check(true,"cancellation replicated without old wave replay");
            await client.Shutdown();client=null;
            client=await Connect(false);
            await Until(()=>client.FindObject(waveId)!=null,"reconnect object");
            proxy=client.FindObject(waveId).GetComponent<PlayerShockwaveAbility>();
            await Task.Delay(500);
            Check(!proxy.IsWaveRunning && Visuals(client)==0,"reconnect after completion does not replay expired wave");

            // 真正由 Client 的 NetInput 送到 Host，再由既有 Player 更新路由到技能。
            var remoteObject=host.Spawn(prefab.GetComponent<NetworkObject>(),new Vector3(1020,100,1000),Quaternion.identity,client.LocalPlayer);
            host.SetPlayerObject(client.LocalPlayer,remoteObject);
            var remotePlayer=remoteObject.GetComponent<Player>();
            await Until(()=>remotePlayer.AbilityRuntimeManager.LoadoutRevision>0,"remote loadout");
            Check(remotePlayer.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition,out error),"remote equip "+error);
            await Until(()=>PlayerAbilityQualification.GetModule<PlayerShockwaveAbility>(remotePlayer)!=null,"remote host module");
            var remoteWave=PlayerAbilityQualification.GetModule<PlayerShockwaveAbility>(remotePlayer);
            Set(remoteWave,"castDelaySeconds",0f,typeof(PlayerActiveAbilityBase));Set(remoteWave,"waveSpeed",4f);
            await Until(()=>client.FindObject(remoteWave.Object.Id)!=null,"input-authority proxy");
            var ownProxy=client.FindObject(remoteWave.Object.Id).GetComponent<PlayerShockwaveAbility>();
            Check(ownProxy.Object.HasInputAuthority && !ownProxy.Object.HasStateAuthority,"remote player is Input Authority only");
            int remoteSequenceBefore=remoteWave.ActivationSequence;
            remotePress=true;
            try { await Until(()=>remoteWave.ActivationSequence==remoteSequenceBefore+1 && remoteWave.IsWaveRunning,"client E reaches authority"); }
            catch
            {
                var rk=remotePlayer.Movement.KCC;
                Vector3 anchor=rk.Data.TargetPosition+Vector3.up*(rk.Settings.Height*.5f);
                var overlaps=new Collider[64];
                int count=host.GetPhysicsScene().OverlapSphere(anchor,.01f,overlaps,~0,QueryTriggerInteraction.Ignore);
                report.Add("DIAG sequence="+remoteWave.ActivationSequence+" phase="+remoteWave.Phase+
                    " usable="+remoteWave.IsUsable+" position="+rk.Data.TargetPosition+" anchor="+anchor+
                    " KCC layers="+rk.Settings.CollisionLayerMask.value+" overlaps="+
                    string.Join(",",overlaps.Take(count).Select(c=>c.name+" layer="+c.gameObject.layer+
                    " owner="+c.transform.IsChildOf(remotePlayer.transform))));
                throw;
            }
            remotePress=false;
            await Until(()=>ownProxy.IsWaveRunning,"own client renders confirmed wave");
            Check(true,"client input traverses existing authority path once");
            var remoteId = remoteWave.Object.Id;
            host.Despawn(remoteWave.Object);
            await Until(()=>client.FindObject(remoteId)==null,"despawn replicated");
            Check(true,"runtime despawn replicates");
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[ShockwaveNetworkVerification] "+ex);}
        finally
        {
            remotePress=false;
            if(client!=null){try{await client.Shutdown();}catch(Exception ex){report.Add("client cleanup "+ex);}client=null;}
            if(host!=null){try{await host.Shutdown();}catch(Exception ex){report.Add("host cleanup "+ex);}host=null;}
            Directory.CreateDirectory("Temp/ShockwaveVfxValidation");
            File.WriteAllLines("Temp/ShockwaveVfxValidation/host-client.txt",report);
            Debug.Log("[ShockwaveNetworkVerification] "+Status);
        }
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        Stage=server?"Host StartGame":"Client StartGame";
        var runner=new GameObject(server?"Wave test Host":"Wave test Client").AddComponent<NetworkRunner>();
        if(server)host=runner;else client=runner;
        runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>();
        events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r,input)=>{var data=new NetInput();data.Buttons.Set(InputButton.Ability1,!server&&remotePress);input.Set(data);});
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var settings=new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);settings.FixedRegion="asia";
        var start=runner.StartGame(new StartGameArgs{GameMode=server?GameMode.Host:GameMode.Client,
            SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=settings,PlayerCount=2,IsVisible=false});
        if(await Task.WhenAny(start,Task.Delay(45000))!=start)throw new TimeoutException(Stage);
        var result=await start;Check(result.Ok,Stage+" "+result.ShutdownReason);
        await Until(()=>runner.IsRunning&&!runner.SceneManager.IsBusy&&runner.Tick.Raw>0,"initial network tick");
        return runner;
    }
    private static int Visuals(NetworkRunner runner) => Resources.FindObjectsOfTypeAll<PlayerShockwaveVisual>()
        .Count(v=>v.gameObject.scene.IsValid()&&v.gameObject.scene.GetPhysicsScene()==runner.GetPhysicsScene());
    private static void Set(object target,string name,object value,Type type=null)=>
        (type??target.GetType()).GetField(name,Private).SetValue(target,value);
    private static void Press(PlayerShockwaveAbility wave)
    {var input=new NetInput();input.Buttons.Set(InputButton.Ability1,true);wave.SimulateAbility(input,default);}
    private static void Check(bool value,string message)
    {if(!value)throw new InvalidOperationException(message);report.Add("PASS "+message);}
    private static async Task Until(Func<bool> condition,string label)
    {Stage=label;float deadline=Time.realtimeSinceStartup+20f;while(!condition()){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(label);await Task.Delay(30);}}
}
#endif
