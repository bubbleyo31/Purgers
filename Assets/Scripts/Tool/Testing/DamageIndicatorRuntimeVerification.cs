#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>明確啟動的真實傷害管線與同 Editor Host/Client 檢查；不寫正式場景或資產。</summary>
public static class DamageIndicatorRuntimeVerification
{
    public static string Status {get;private set;}="尚未執行";
    public static string Stage {get;private set;}="";
    private static NetworkRunner host,client;
    private static readonly List<string> report=new List<string>();
    private static readonly List<UnityEngine.Object> temporary=new List<UnityEngine.Object>();
    private static bool network;
    private static string session;
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool good,string text){if(!good)throw new InvalidOperationException(text);report.Add("PASS "+text);}
    private static async Task Until(Func<bool> condition,string stage)
    {Stage=stage;float end=Time.realtimeSinceStartup+25;while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(stage);await Task.Delay(20);}}
    private static CanvasGroup Marker(LocalPlayerDamageIndicator hud)=>hud.transform.Find("Damage Source 0").GetComponent<CanvasGroup>();
    public static async void Begin(bool multiplayer=false)
    {
        if(!Application.isPlaying || host!=null)throw new InvalidOperationException("需在空閒 Play Mode 啟動。");
        network=multiplayer;Status="執行中";report.Clear();session="damage-indicator-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var player=host.Spawn(playerPrefab.GetComponent<NetworkObject>(),new Vector3(1000,100,1000),Quaternion.identity,host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer,player);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(floor);
            floor.transform.position=new Vector3(1000,99,1000);floor.transform.localScale=new Vector3(100,1,100);temporary.Add(floor);
            var camObject=new GameObject("Damage verification camera");temporary.Add(camObject);var camera=camObject.AddComponent<Camera>();
            camera.transform.position=new Vector3(1000,102,996);camera.transform.rotation=Quaternion.identity;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.17f);camera.depth=100;
            var settings=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<DamageIndicatorSettings>("Assets/_Project_Assets/UI/BattleHUD/DamageIndicatorV1.asset"));temporary.Add(settings);
            settings.arc.hold=settings.marker.hold=20; // 只延長測試副本，讓工具截圖及人工讀取有時間。
            var hud=UnityEngine.Object.FindObjectOfType<LocalPlayerDamageIndicator>();
            Check(hud!=null,"authored Menu HUD exists");hud.Configure(settings,host,camera);
            await Until(()=>hud.BoundHealth!=null,"bind host HUD");
            var health=player.GetComponent<PlayerHealth>();PlayerDamageReceivedInfo received=default;int events=0;
            health.LocalDamageReceived+=info=>{received=info;events++;};
            Vector3 point=camera.transform.position+new Vector3(3,2,14);
            var source=GameObject.CreatePrimitive(PrimitiveType.Capsule);host.MoveToRunnerScene(source);temporary.Add(source);source.transform.position=point;source.layer=9;
            var request=new DamageRequest{RequestedDamage=1,BaseDamage=1,SourceObject=source,DamageType=DamageType.Bullet,Sequence=1};
            float hp=health.CurrentHealth;
            Check(DamageReceiverUtility.TryApplyDamage(health.gameObject,request,out var result)&&result.Accepted,"authoritative damage pipeline accepts hit");
            await Until(()=>events==1&&hud.ActiveCount==1,"host damage receipt");
            Check(health.CurrentHealth==hp-1,"health changes exactly once");
            Check(received.HasSourceWorldPosition&&Vector3.Distance(received.SourceWorldPosition,point)<.001f,"RPC contains authority position snapshot");
            await Until(()=>Marker(hud).alpha>.89f,"visible source");
            source.transform.position+=Vector3.right*20;
            Check(Vector3.Distance(received.SourceWorldPosition,point)<.001f,"source movement does not change received position snapshot");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(wall);temporary.Add(wall);
            wall.transform.position=Vector3.Lerp(camera.transform.position,point,.5f);wall.transform.localScale=new Vector3(7,7,.5f);
            float start=Time.realtimeSinceStartup;Physics.SyncTransforms();
            await Until(()=>Mathf.Abs(Marker(hud).alpha-.25f)<.01f,"wall enters after damage");
            report.Add("MEASURE wall entry response "+((Time.realtimeSinceStartup-start)*1000).ToString("F1")+" ms (20ms polling resolution)");
            settings.occlusionMode=DamageIndicatorOcclusionMode.HideBehindWalls;
            await Until(()=>Marker(hud).alpha==0,"hide mode live switch");
            wall.SetActive(false);Physics.SyncTransforms();await Until(()=>Marker(hud).alpha>.89f,"hidden marker resumes when wall leaves");
            Check(events==1,"occlusion transitions need no additional hit");
            settings.occlusionMode=DamageIndicatorOcclusionMode.FadeThroughWalls;
            int before=hud.ReceivedEventCount;hp=health.CurrentHealth;
            var blocker=player.gameObject.AddComponent<DamageIndicatorVerificationBlocker>();
            request.Sequence=2;request.RequestedDamage=request.BaseDamage=5;
            DamageReceiverUtility.TryApplyDamage(health.gameObject,request,out result);
            await Until(()=>hud.ReceivedEventCount==before+1,"fully blocked feedback");
            Check(!result.Accepted&&result.WasBlocked&&health.CurrentHealth==hp,"fully blocked result and HP remain unchanged");
            UnityEngine.Object.Destroy(blocker);
            await Task.Delay(50);hud.Clear();
            if(multiplayer)
            {
                client=await Connect(false);
                await Until(()=>client.FindObject(player.Id)!=null,"late join host player");
                int leaked=0;client.FindObject(player.Id).GetComponent<PlayerHealth>().LocalDamageReceived+=info=>leaked++;
                await Task.Delay(100);Check(leaked==0,"late join does not replay previous transient hit");
                var remote=host.Spawn(playerPrefab.GetComponent<NetworkObject>(),new Vector3(1003,100,1000),Quaternion.identity,client.LocalPlayer);
                host.SetPlayerObject(client.LocalPlayer,remote);
                await Until(()=>client.FindObject(remote.Id)!=null,"remote player");
                var clientPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/DamageIndicators.prefab");
                var clientRoot=UnityEngine.Object.Instantiate(clientPrefab);temporary.Add(clientRoot);
                var clientHud=clientRoot.GetComponent<LocalPlayerDamageIndicator>();clientHud.Configure(settings,client,camera);
                clientRoot.GetComponent<Canvas>().enabled=false;
                await Until(()=>clientHud.BoundHealth!=null,"client HUD binds input authority");
                Check(clientHud.BoundHealth.Object.HasInputAuthority&&!clientHud.BoundHealth.Object.HasStateAuthority,"client has input authority only");
                int hostBefore=hud.ReceivedEventCount;
                var remoteHealth=remote.GetComponent<PlayerHealth>();
                request.Sequence=3;request.RequestedDamage=request.BaseDamage=1;request.HasSourceWorldPosition=true;request.SourceWorldPosition=point;
                DamageReceiverUtility.TryApplyDamage(remoteHealth.gameObject,request,out result);
                await Until(()=>clientHud.ReceivedEventCount==1&&clientHud.ActiveCount==1,"host damage arrives at client HUD");
                Check(hud.ReceivedEventCount==hostBefore&&leaked==0,"other player HUD receives no victim-only event");
                var proxy=client.FindObject(remote.Id).GetComponent<PlayerHealth>();int remoteBefore=clientHud.ReceivedEventCount;
                var rejected=proxy.ReceiveDamage(request);await Task.Delay(100);
                Check(!rejected.Accepted&&clientHud.ReceivedEventCount==remoteBefore,"client cannot authorize damage or manufacture feedback");
                // 只在 Client 的 PhysicsScene 放牆，確認本機遮擋不借用 Host 場景。
                var clientWall=GameObject.CreatePrimitive(PrimitiveType.Cube);client.MoveToRunnerScene(clientWall);temporary.Add(clientWall);
                clientWall.transform.position=Vector3.Lerp(camera.transform.position,point,.5f);clientWall.transform.localScale=new Vector3(7,7,.5f);
                Physics.SyncTransforms();await Until(()=>Mathf.Abs(Marker(clientHud).alpha-.25f)<.01f,"client local physics occlusion");
                clientHud.Clear();await client.Shutdown();client=null;
                client=await Connect(false);await Until(()=>client.FindObject(player.Id)!=null,"reconnect host player");
                int replay=0;client.FindObject(player.Id).GetComponent<PlayerHealth>().LocalDamageReceived+=info=>replay++;
                await Task.Delay(150);Check(replay==0,"reconnect does not replay old damage RPC");
            }
            // Pool cleanup and replacement binding on respawn.
            host.Despawn(player);await Until(()=>hud.BoundHealth==null&&hud.ActiveCount==0,"despawn cleanup");
            var replacement=host.Spawn(playerPrefab.GetComponent<NetworkObject>(),new Vector3(1000,100,1000),Quaternion.identity,host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer,replacement);
            await Until(()=>hud.BoundHealth==replacement.GetComponent<PlayerHealth>(),"respawn rebind");
            Check(hud.ActiveCount==0,"respawn starts without old markers");
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[DamageIndicatorVerification] "+ex);}
        finally
        {
            if(client!=null){await client.Shutdown();client=null;}
            if(host!=null){await host.Shutdown();host=null;}
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj);temporary.Clear();
            Directory.CreateDirectory("Temp/DamageIndicator");File.WriteAllLines("Temp/DamageIndicator/"+(network?"host-client":"single")+".txt",report);
        }
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var r=new GameObject(server?"Damage verification Host":"Damage verification Client").AddComponent<NetworkRunner>();r.ProvideInput=true;
        var events=r.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((runner,input)=>input.Set(default(NetInput)));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var e in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(e.Item2);
        var manager=r.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var appSettings=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(appSettings);appSettings.FixedRegion="asia";
        var result=await r.StartGame(new StartGameArgs{GameMode=network?(server?GameMode.Host:GameMode.Client):GameMode.Single,
            SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=appSettings,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>r.IsRunning&&r.Tick.Raw>0,"first tick");return r;
    }
}
public sealed class DamageIndicatorVerificationBlocker : MonoBehaviour,IDamageRequestModifier
{
    public void ModifyDamageRequest(ref DamageRequest request){request.BlockedDamage+=request.RequestedDamage;request.RequestedDamage=0;}
}
#endif
