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

/// <summary>手動觸發的隔離 Host/Client 驗證；不保存正式場景、Prefab 或存檔。</summary>
public static class TeammateRuntimeVerification
{
    public static string Status{get;private set;}="尚未執行";
    public static string Stage{get;private set;}="";
    private static NetworkRunner host,client;
    private static string session;
    private static readonly List<string> report=new List<string>();
    private static readonly List<UnityEngine.Object> temporary=new List<UnityEngine.Object>();
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    private static void Check(bool result,string label){if(!result)throw new Exception(label);report.Add("PASS "+label);}
    private static async Task Until(Func<bool> condition,string label)
    {Stage=label;float end=Time.realtimeSinceStartup+35;while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(label);await Task.Delay(25);}}
    public static async void Begin()
    {
        if(!Application.isPlaying || host!=null || NetworkRunner.Instances.Any(r=>r.IsRunning))throw new InvalidOperationException("需要沒有 Runner 的 Play Mode。");
        Status="執行中";report.Clear();session="teammate-ui-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var driver=host.gameObject.AddComponent<StageTimerPauseVerificationDriver>();host.AddGlobal(driver);
            var server=CreateLogic(host);
            await Until(()=>server.IsTeammateRosterReady && host.TryGetPlayerObject(host.LocalPlayer,out _),"host roster ready");
            await driver.AtTick(()=>Check(server.SetTeammateNameStateAuthority(host.LocalPlayer,"HOST 名稱"),"authority accepts existing name pipeline"));
            Check(server.TryGetTeammateIdentity(host.LocalPlayer,out var first) && first.ColorSlot==0,"first player is red");
            client=await Connect(false);var replica=CreateLogic(client);
            await Until(()=>replica.IsTeammateRosterReady && replica.TryGetTeammateIdentity(host.LocalPlayer,out var state) && state.Name.ToString()=="HOST 名稱","late join receives existing name snapshot");
            await Until(()=>host.TryGetPlayerObject(client.LocalPlayer,out _) && client.TryGetPlayerObject(client.LocalPlayer,out _),"client player replicated");
            await driver.AtTick(()=>Check(server.SetTeammateNameStateAuthority(client.LocalPlayer,"CLIENT 名稱"),"authority names second player"));
            await Until(()=>replica.TryGetTeammateIdentity(client.LocalPlayer,out var state) && state.Name.ToString()=="CLIENT 名稱","client receives its own registered name");
            Check(!replica.SetTeammateNameStateAuthority(host.LocalPlayer,"ILLEGAL"),"client cannot overwrite authoritative names");
            Check(replica.TryGetTeammateIdentity(client.LocalPlayer,out var second) && second.ColorSlot==1,"same blue identity on all peers");
            var remote=PlayerAbilityQualification.FindPlayer(host,client.LocalPlayer);
            var hostPlayer=PlayerAbilityQualification.FindPlayer(host,host.LocalPlayer);
            var hud=CreateHud(host);
            var camera=(Camera)typeof(LocalTeammateHUD).GetField("worldCamera",Private).GetValue(hud);
            // Keep a stationary temporary camera looking at the remote actor.
            camera.transform.position=remote.transform.position+new Vector3(0,1,-8);camera.transform.rotation=Quaternion.identity;
            await Until(()=>hud.NameplateCount==1,"local HUD presents exactly one teammate");
            Check(hud.ArrowCount==0,"onscreen teammate has no duplicate arrow");
            camera.transform.rotation=Quaternion.Euler(0,180,0);
            await Until(()=>hud.ArrowCount==1,"behind-camera teammate becomes one arrow");
            Check(hud.NameplateCount==0,"arrow has no duplicate nameplate");
            var oldId=remote.Object.Id;int stableSlot=second.ColorSlot;
            Vector3 deathPosition=remote.transform.position;
            await driver.AtTick(()=>
            {
                remote.Health.ReceiveDamage(new DamageRequest{RequestedDamage=10000,BaseDamage=10000,Attacker=host.LocalPlayer,DamageType=DamageType.Environment});
            });
            await Until(()=>replica.TryGetTeammateIdentity(client.LocalPlayer,out var state) && state.IsDead,"death snapshot replicated");
            Check(replica.TryGetTeammateTarget(client.LocalPlayer,out var absent,out var retained,out bool dead) && dead && absent==null,"death marker survives player despawn");
            await Until(()=>host.TryGetPlayerObject(client.LocalPlayer,out var obj) && obj!=null && obj.Id!=oldId,"existing GameLogic respawns player");
            await Until(()=>replica.TryGetTeammateIdentity(client.LocalPlayer,out var state) && !state.IsDead,"respawn clears death marker");
            Check(replica.TryGetTeammateIdentity(client.LocalPlayer,out var after) && after.ColorSlot==stableSlot && after.Name.ToString()=="CLIENT 名稱","respawn retains identity and color");
            PlayerRef departed=client.LocalPlayer;
            await client.Shutdown();client=null;
            await Until(()=>!server.TryGetTeammateIdentity(departed,out _),"disconnect removes roster and releases slot");
            client=await Connect(false);replica=CreateLogic(client);
            await Until(()=>replica.IsTeammateRosterReady && replica.TryGetTeammateIdentity(host.LocalPlayer,out var state) && state.Name.ToString()=="HOST 名稱","reconnect gets current roster snapshot");
            Check(server.TryGetTeammateIdentity(host.LocalPlayer,out var unchanged) && unchanged.ColorSlot==0,"other player's color never renumbered");
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError(ex);}
        finally
        {
            if(client!=null){await client.Shutdown();client=null;}
            if(host!=null){await host.Shutdown();host=null;}
            foreach(var item in temporary)if(item!=null)UnityEngine.Object.Destroy(item);temporary.Clear();
            Directory.CreateDirectory("Temp/Teammates");File.WriteAllLines("Temp/Teammates/host-client.txt",report);
        }
    }
    private static GameLogic CreateLogic(NetworkRunner runner)
    {
        var root=new GameObject("Temporary teammate GameLogic",typeof(NetworkObject));temporary.Add(root);runner.MoveToRunnerScene(root);
        var logic=root.AddComponent<GameLogic>();
        typeof(GameLogic).GetField("playerPrefab",Private).SetValue(logic,NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        typeof(GameLogic).GetField("debugPlayerLifecycle",Private).SetValue(logic,false);
        typeof(GameLogic).GetField("fallbackSpawnPosition",Private).SetValue(logic,new Vector3(1200,1200,1200));
        new NetworkObjectBaker().Bake(root);runner.RegisterSceneObjects(SceneRef.FromIndex(0),new[]{root.GetComponent<NetworkObject>()},default);return logic;
    }
    private static LocalTeammateHUD CreateHud(NetworkRunner runner)
    {
        var root=new GameObject("Temporary teammate Canvas",typeof(Canvas));temporary.Add(root);root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/TeammateDisplay.prefab"),root.transform);
        var cameraRoot=new GameObject("Temporary teammate camera",typeof(Camera));temporary.Add(cameraRoot);
        var camera=cameraRoot.GetComponent<Camera>();camera.depth=20;
        var hud=obj.GetComponent<LocalTeammateHUD>();hud.Configure(AssetDatabase.LoadAssetAtPath<TeammateDisplaySettings>("Assets/_Project_Assets/UI/BattleHUD/TeammateDisplayV1.asset"),runner,camera);return hud;
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Teammate test host":"Teammate test client").AddComponent<NetworkRunner>();temporary.Add(runner.gameObject);
        runner.ProvideInput=true;var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>input.Set(default(NetInput)));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var scene=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();scene.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs{GameMode=server?GameMode.Host:GameMode.Client,SessionName=session,Config=config,SceneManager=scene,CustomPhotonAppSettings=app,PlayerCount=4,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>runner.IsRunning && runner.Tick.Raw>0,"runner first tick");return runner;
    }
}
#endif
