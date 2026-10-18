#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

// Explicit isolated verification; never saves scenes or player progress.
public static class JellyfishCollisionRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    static NetworkRunner host, client, late;
    static string session;
    static readonly List<string> report = new List<string>();
    static readonly List<GameObject> temporary = new List<GameObject>();
    static readonly Vector3 Origin = new Vector3(1400,1400,1400);
    static void Check(bool ok,string label) { if(!ok)throw new Exception(label);report.Add("PASS "+label); }
    static async Task Until(Func<bool> condition,string label) {
        Stage=label;float end=Time.realtimeSinceStartup+40;
        while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(label);await Task.Delay(25);}
    }
    public static async void Begin(string prefabName = "Enemy_Ranged_A") {
        if(prefabName != "Enemy_Ranged_A" && prefabName != "Enemy_Ranged_B") throw new ArgumentException("Unknown jellyfish prefab");
        if(!Application.isPlaying||NetworkRunner.Instances.Any(r=>r.IsRunning))throw new InvalidOperationException("需要無 Runner 的 Play Mode");
        Status="執行中";report.Clear();session="jellyfish-"+Guid.NewGuid().ToString("N");
        try {
            host=await Connect(true);
            var driver=host.gameObject.AddComponent<StageTimerPauseVerificationDriver>();host.AddGlobal(driver);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/"+prefabName+".prefab").GetComponent<NetworkObject>();
            var areaObject=new GameObject("Jellyfish test patrol area");temporary.Add(areaObject);host.MoveToRunnerScene(areaObject);areaObject.transform.position=Origin;
            var area=areaObject.AddComponent<EnemyPatrolArea>();var points=new GameObject("Points").transform;points.SetParent(areaObject.transform,false);
            new GameObject("A").transform.SetParent(points,false);var second=new GameObject("B").transform;second.SetParent(points,false);second.localPosition=Vector3.forward*4;area.ConfigureRuntime("jellyfish-test",points,0);
            var enemy=host.Spawn(prefab,Origin,Quaternion.identity,onBeforeSpawned:(r,obj)=>{
                Check(obj.GetComponent<EnemyActor>().TryInitializePatrolBeforeSpawn(area),"existing patrol spawn contract binds");
                obj.GetComponent<EnemyIdlePatrolBrain>().enabled=false;obj.GetComponent<EnemyAwarenessBrain>().enabled=false;
                obj.GetComponent<EnemyChaseBrain>().enabled=false;obj.GetComponent<EnemyPerceptionController>().enabled=false;
                obj.GetComponent<EnemyCombatDecisionController>().enabled=false;
            });
            await Until(()=>enemy.GetComponent<EnemyActor>().IsFusionSpawned,"Host spawn");
            client=await Connect(false);NetworkObject replica=null;
            await Until(()=>client.TryFindObject(enemy.Id,out replica)&&replica.GetComponent<EnemyActor>().IsFusionSpawned,"Client replica");
            Check(enemy.HasStateAuthority&&!replica.HasStateAuthority,"Host owns NPC results; Client is proxy");
            Parts(enemy);Parts(replica);
            var moving=enemy.GetComponentsInChildren<Hitbox>().First(h=>h.name.Contains("LongTentacles_right"));
            var previous=moving.transform.position;await Task.Delay(600);
            Check(Vector3.Distance(previous,moving.transform.position)>.001f,"live Animator changes collision pose");
            Pairs(enemy);Pairs(replica);
            // Matching controlled poses isolate damage wiring from natural latency/presentation acceptance.
            Freeze(enemy);Freeze(replica);Physics.SyncTransforms();await Task.Delay(300);
            var health=enemy.GetComponent<TestDamageReceiver>();float hp=health.CurrentHealth;
            var head=enemy.GetComponentsInChildren<Hitbox>().Single(h=>h.name.EndsWith("SmallBell"));
            var body=enemy.GetComponentsInChildren<Hitbox>().Single(h=>h.name.EndsWith("MainBell"));
            await driver.AtTick(()=>{
                var hh=Ray(head,client.LocalPlayer);var bh=Ray(body,client.LocalPlayer);
                Check(hh.GameObject.GetComponent<WeaponHitZone>().IsHead,"Client lag ray identifies upper small bell as Head");
                Check(!bh.GameObject.GetComponent<WeaponHitZone>().IsHead,"large bell stays Body");
                Check(DamageReceiverUtility.TryApplyDamage(bh.GameObject,Request(bh.GameObject,5,false),out var normal)&&normal.Accepted&&!normal.IsHeadshot&&Mathf.Abs(normal.AppliedDamage-5)<.001f,"normal damage through existing receiver");
                Check(DamageReceiverUtility.TryApplyDamage(hh.GameObject,Request(hh.GameObject,10,true),out var critical)&&critical.Accepted&&critical.IsHeadshot&&Mathf.Abs(critical.AppliedDamage-10)<.001f,"headshot classification through existing receiver");
            });
            await Until(()=>Mathf.Abs(replica.GetComponent<TestDamageReceiver>().CurrentHealth-health.CurrentHealth)<.001f,"health replication");
            Check(Mathf.Abs(health.CurrentHealth-(hp-15))<.001f,"multiple hitboxes do not duplicate selected-hit damage");
            Check(!replica.GetComponent<TestDamageReceiver>().ReceiveDamage(Request(replica.gameObject,50,false)).Accepted,"proxy cannot commit health damage");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(wall);host.MoveToRunnerScene(wall);
            wall.transform.position=Origin+Vector3.right*10;wall.transform.localScale=new Vector3(1,40,40);Physics.SyncTransforms();
            var patrol=enemy.GetComponent<EnemyFlyingPatrolNavigator>();var chase=enemy.GetComponent<EnemyFlyingChaseMotor>();var physics=host.GetPhysicsScene();
            Check(patrol.IsPassageClear(physics,Origin,Origin+Vector3.forward*4)&&chase.IsPassageClear(physics,Origin,Origin+Vector3.forward*4),"both allow clear passage");
            Check(!patrol.IsPassageClear(physics,Origin,Origin+Vector3.right*15)&&!chase.IsPassageClear(physics,Origin,Origin+Vector3.right*15),"both reject wall");
            Check(!patrol.IsPassageClear(physics,Origin+Vector3.right*10,Origin+Vector3.right*10)&&!chase.IsPassageClear(physics,Origin+Vector3.right*10,Origin+Vector3.right*10),"both reject occupied start");
            late=await Connect(false);NetworkObject lateEnemy=null;
            await Until(()=>late.TryFindObject(enemy.Id,out lateEnemy)&&lateEnemy.GetComponent<EnemyActor>().IsFusionSpawned,"late join");Parts(lateEnemy);Pairs(lateEnemy);
            Check(Mathf.Abs(lateEnemy.GetComponent<TestDamageReceiver>().CurrentHealth-health.CurrentHealth)<.001f,"late join receives health");
            await driver.AtTick(()=>health.ReceiveDamage(Request(enemy.gameObject,100000,false)));
            await Until(()=>replica==null||!replica.IsValid||!replica.GetComponent<TestDamageReceiver>().IsAlive,"death replication");
            foreach(var obj in new[]{enemy,replica,lateEnemy})if(obj!=null&&obj.IsValid){
                Check(obj.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"death disables every part on "+obj.Runner.name);
                Check(!obj.GetComponent<HitboxRoot>().HitboxRootActive,"death disables Fusion root on "+obj.Runner.name);
            }
            Status="通過 "+report.Count(line => line.StartsWith("PASS "));
        } catch(Exception error){Status="失敗 "+Stage+": "+error.Message;report.Add(error.ToString());}
        finally {
            foreach(var r in new[]{late,client,host})if(r!=null)await r.Shutdown();
            foreach(var go in temporary)if(go!=null)UnityEngine.Object.Destroy(go);
            temporary.Clear();host=client=late=null;
            Directory.CreateDirectory("Temp/JellyfishCollision");File.WriteAllLines("Temp/JellyfishCollision/runtime-"+prefabName+".txt",new[]{Status}.Concat(report));
        }
    }
    static void Parts(NetworkObject obj){
        var root=obj.GetComponent<HitboxRoot>();var hits=obj.GetComponentsInChildren<Hitbox>(true);
        report.Add("INFO "+obj.Runner.name+" uniqueIndices="+hits.Select(h=>h.HitboxIndex).Distinct().Count()+" manager="+(root.Manager!=null));
        Check(hits.Length==8&&root.Hitboxes.Length==8&&hits.All(h=>h.Root==root)&&(!obj.HasStateAuthority||hits.Select(h=>h.HitboxIndex).Distinct().Count()==8),"8 hitboxes register under one root on "+obj.Runner.name);
        Check(hits.Count(h=>h.GetComponent<WeaponHitZone>().IsHead)==1&&hits.All(h=>h.GetComponent<WeaponHitZone>().DamageMultiplier==1),"only small bell is Head; part multiplier is one");
        Check(obj.GetComponent<EnemyJellyfishCollisionPose>()!=null,"pose binding survives spawn");
    }
    static void Pairs(NetworkObject obj){
        foreach(var hit in obj.GetComponentsInChildren<Hitbox>()){
            var b=hit.GetComponent<BoxCollider>();var c=hit.GetComponent<CapsuleCollider>();
            if(b!=null&&(hit.BoxExtents*2-b.size).sqrMagnitude>.000001f)throw new Exception("Box mismatch");
            if(c!=null&&(Mathf.Abs(hit.CapsuleRadius-c.radius)>.0001f||Mathf.Abs(hit.CapsuleExtents-c.height)>.0001f))throw new Exception("Capsule mismatch");
        }
        Check(true,"PhysX and Fusion dimensions agree on "+obj.Runner.name);
    }
    static void Freeze(NetworkObject obj){
        var a=obj.GetComponentInChildren<Animator>();a.enabled=false;
        a.runtimeAnimatorController.animationClips.First(c=>c.name=="骨架|idle").SampleAnimation(a.gameObject,0);
        obj.GetComponent<EnemyJellyfishCollisionPose>().RefreshPose();
    }
    static LagCompensatedHit Ray(Hitbox part,PlayerRef player){
        if(!host.LagCompensation.Raycast(part.transform.position-Vector3.forward*20,Vector3.forward,40,player,out var hit,1<<part.gameObject.layer,(HitOptions)0))throw new Exception("Lag ray missed "+part.name);
        return hit;
    }
    static DamageRequest Request(GameObject hit,float damage,bool head)=>new DamageRequest {
        RequestedDamage=damage,BaseDamage=head?damage/2:damage,DamageType=DamageType.Bullet,
        HitZone=head?DamageHitZoneType.Head:DamageHitZoneType.Body,HeadshotDamageMultiplier=2,
        HitObject=hit,HitPoint=hit.transform.position,HitDirection=Vector3.forward,Attacker=PlayerRef.None
    };
    static async Task<NetworkRunner> Connect(bool server){
        var r=new GameObject(server?"Jellyfish Host":"Jellyfish Client").AddComponent<NetworkRunner>();temporary.Add(r.gameObject);r.ProvideInput=true;
        var ev=r.gameObject.AddComponent<NetworkEvents>();ev.OnInput=new NetworkEvents.InputEvent();ev.OnInput.AddListener((runner,input)=>input.Set(default(NetInput)));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=r.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await r.StartGame(new StartGameArgs {GameMode=server?GameMode.Host:GameMode.Client,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=3,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>r.IsRunning&&r.Tick.Raw>0,"first tick");return r;
    }
}
#endif
