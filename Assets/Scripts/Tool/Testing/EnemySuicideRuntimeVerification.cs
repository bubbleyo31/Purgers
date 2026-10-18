#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Fusion.Addons.KCC;
using UnityEditor;
using UnityEngine;

/// <summary>明確啟動的隔離 Runner 驗證；不保存正式場景與 Prefab，不取代自然多人操作驗收。</summary>
public static partial class EnemySuicideRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool network;
    private static string session;
    private static EnemyPatrolArea area;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly Vector3 Origin = new Vector3(1500,100,1500);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); report.Add("PASS "+message); }
    private static void Set(object obj,string field,object value) { obj.GetType().GetField(field,Private).SetValue(obj,value); }
    private static object Invoke(object obj,string method,params object[] values) => obj.GetType().GetMethod(method,Private).Invoke(obj,values);
    private static async Task Until(Func<bool> condition,string stage)
    {
        Stage=stage; float end=Time.realtimeSinceStartup+35;
        while(!condition()) { if(Time.realtimeSinceStartup>end) throw new TimeoutException(stage); await Task.Delay(25); }
    }
    public static async void Begin(bool multiplayer=false)
    {
        if(!Application.isPlaying || host!=null || NetworkRunner.Instances.Any(r=>r.IsRunning)) throw new InvalidOperationException("需要沒有現存 Runner 的 Play Mode。");
        Status="執行中";report.Clear();network=multiplayer;session="suicide-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(floor);host.MoveToRunnerScene(floor);
            floor.transform.position=Origin-Vector3.up*.5f;floor.transform.localScale=new Vector3(100,1,100);
            var areaGo=new GameObject("Suicide test patrol area");temporary.Add(areaGo);host.MoveToRunnerScene(areaGo);areaGo.transform.position=Origin;
            area=areaGo.AddComponent<EnemyPatrolArea>();var points=new GameObject("Points").transform;points.SetParent(areaGo.transform,false);
            new GameObject("A").transform.SetParent(points,false);area.ConfigureRuntime("suicide-test",points,0);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var player=host.Spawn(prefab,Origin,Quaternion.identity,host.LocalPlayer);host.SetPlayerObject(host.LocalPlayer,player);
            await Task.Delay(200); player.GetComponent<Player>().enabled=false;
            var gate=player.GetComponent<PlayerActionGate>();var ph=player.GetComponent<PlayerHealth>();var grapple=player.GetComponent<PlayerGrapple>();var kcc=player.GetComponent<KCC>();
            await Task.Delay(200);
            Stage="player-triggered death";
            var shot=SpawnEnemy(Origin+Vector3.forward*20);int deaths=0;CombatDeathEventData death=default;
            shot.GetComponent<TestDamageReceiver>().Died += d=>{deaths++;death=d;};
            var rejected=shot.GetComponent<TestDamageReceiver>().ReceiveDamage(Request(player,shot,0));
            Check(!rejected.Accepted && shot.GetComponent<TestDamageReceiver>().IsAlive,"zero damage does not trigger");
            var hit=shot.GetComponent<TestDamageReceiver>().ReceiveDamage(Request(player,shot,1));
            Check(hit.KilledTarget && hit.AppliedDamage==1f && deaths==1,"one effective hit detonates without faking damage amount");
            Check(death.KillingDamage.Request.Attacker==host.LocalPlayer,"player-triggered death retains experience instigator");
            Check(shot.GetComponent<EnemySuicideAttack>().HasExploded,"player hit starts explosion");
            var duplicate=shot.GetComponent<TestDamageReceiver>().ReceiveDamage(Request(player,shot,1));
            Check(!duplicate.Accepted && deaths==1,"duplicate hit cannot detonate or award twice");
            var immune=SpawnEnemy(Origin+Vector3.right*20);immune.GetComponent<TestDamageReceiver>().SetInvulnerable(true);
            Check(!immune.GetComponent<TestDamageReceiver>().ReceiveDamage(Request(player,immune,1)).Accepted && !immune.GetComponent<EnemySuicideAttack>().HasExploded,"invulnerable target rejects trigger");host.Despawn(immune);

            Stage="blast, grapple and HUD";
            var charges=player.GetComponent<PlayerGrappleCharges>();
            typeof(PlayerGrappleCharges).GetProperty("CurrentEnergy").SetValue(charges,50f);
            typeof(PlayerGrappleCharges).GetProperty("MaximumEnergy").SetValue(charges,50f);
            typeof(PlayerGrappleCharges).GetProperty("IsInitialized",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(charges,(NetworkBool)true);
            float energy=charges.NormalizedEnergy;
            typeof(PlayerGrapple).GetProperty("CurrentPhase").SetValue(grapple,GrapplePhase.Attached);
            kcc.FixedData.DynamicVelocity=new Vector3(7,2,0);kcc.SetDynamicVelocity(new Vector3(7,2,0));Vector3 velocity=kcc.FixedData.DynamicVelocity;
            float hp=ph.CurrentHealth;
            var blast=SpawnEnemy(Origin+Vector3.forward*2);Set(blast.GetComponent<EnemySuicideAttack>(),"centerDamage",5f);
            PlayerRef cause=host.LocalPlayer;blast.GetComponent<TestDamageReceiver>().Died+=d=>cause=d.KillingDamage.Request.Attacker;
            report.Add("INFO blast player="+player.transform.position+" KCC="+kcc.FixedData.TargetPosition+" capsule="+(kcc.Collider!=null?kcc.Collider.bounds.ToString():"null")+" center="+blast.GetComponent<EnemySuicideAttack>().ExplosionCenter);
            var center=blast.GetComponent<EnemySuicideAttack>().ExplosionCenter;var closest=kcc.Collider.ClosestPoint(center);RaycastHit obstruction;
            bool blocked=host.GetPhysicsScene().Raycast(center,(closest-center).normalized,out obstruction,Vector3.Distance(center,closest),1,QueryTriggerInteraction.Ignore);
            report.Add("INFO active="+string.Join(",",host.ActivePlayers.Select(p=>p.ToString()))+" local="+host.LocalPlayer+" hit="+(blocked?obstruction.collider.name:"clear")+" point="+closest+" spawnedGate="+gate.IsFusionSpawned);
            Invoke(blast.GetComponent<EnemySuicideAttack>(),"Detonate");
            Check(cause.IsNone,"autonomous blast has no player experience instigator");
            Check(ph.CurrentHealth<hp && gate.IsExplosionGrappleBlocked,"blast damages and blocks grapple; hp="+hp+" -> "+ph.CurrentHealth+" blocked="+gate.IsExplosionGrappleBlocked);
            Check(grapple.CurrentPhase==GrapplePhase.Idle && !grapple.CanUseGrapple,"active grapple forcibly ends and cannot restart");
            Check(kcc.FixedData.DynamicVelocity==velocity,"forced release preserves existing dynamic velocity");
            Check(Mathf.Approximately(player.GetComponent<PlayerGrappleCharges>().NormalizedEnergy,energy),"block does not consume energy");
            var ui=new GameObject("Test Grapple HUD");temporary.Add(ui);var hud=ui.AddComponent<LocalPlayerBattleAbilityHUD>();hud.enabled=false;
            var mark=new GameObject("Unavailable mark");mark.transform.SetParent(ui.transform);Set(hud,"emptyGrappleMark",mark);
            Invoke(hud,"RefreshGrapple",player.GetComponent<PlayerGrappleCharges>());Check(mark.activeSelf,"existing unavailable UI shows during block");
            await Until(()=>!gate.IsExplosionGrappleBlocked,"block expiration");
            Invoke(hud,"RefreshGrapple",player.GetComponent<PlayerGrappleCharges>());Check(!mark.activeSelf,"UI clears after block with energy remaining");

            Stage="independent slow expiry";
            gate.ApplyExplosionStatus(0,.5f,.25f);gate.ApplyExplosionStatus(0,.2f,.85f);
            Check(Mathf.Approximately(gate.ExplosionGroundSpeedMultiplier,.5f),"strongest slow wins, not summed");
            await Task.Delay(400);Check(Mathf.Approximately(gate.ExplosionGroundSpeedMultiplier,.8f),"weaker slow remains after strong slow expires");
            await Task.Delay(600);Check(Mathf.Approximately(gate.ExplosionGroundSpeedMultiplier,1f),"all slow effects expire independently");
            gate.SetBlocks(PlayerActionBlockSource.SpecialAbility,PlayerActionBlockMask.Grapple);
            gate.ApplyExplosionStatus(.1f,0,0);await Task.Delay(220);
            Check(gate.IsBlocked(PlayerActionBlockMask.Grapple),"expiry preserves another source's grapple lock");gate.ClearBlocks(PlayerActionBlockSource.SpecialAbility);

            Stage="wall occlusion";
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(wall);host.MoveToRunnerScene(wall);wall.transform.position=Origin+new Vector3(0,1,1);wall.transform.localScale=new Vector3(5,3,.2f);Physics.SyncTransforms();
            var occluded=SpawnEnemy(Origin+Vector3.forward*2);hp=ph.CurrentHealth;Invoke(occluded.GetComponent<EnemySuicideAttack>(),"Detonate");
            Check(ph.CurrentHealth==hp && !gate.IsExplosionGrappleBlocked,"wall blocks blast damage and debuff");UnityEngine.Object.Destroy(wall);await Task.Delay(60);

            Stage="emergency approach";
            var surprised=SpawnEnemy(Origin+Vector3.forward*3);var surpriseAttack=surprised.GetComponent<EnemySuicideAttack>();
            surprised.GetComponent<EnemyCombatDecisionController>().enabled=true;
            await Until(()=>surpriseAttack.HasExploded,"unnoticed proximity via combat arbiter");
            Check(true,"unnoticed close player detonates through real combat arbiter");
            var crossing=SpawnEnemy(Origin+Vector3.forward*6);var crossingAttack=crossing.GetComponent<EnemySuicideAttack>();
            var crossingPerception=crossing.GetComponent<EnemyPerceptionController>();
            var crossingContext=new EnemyCombatContext(crossing.GetComponent<EnemyCombatDecisionController>(),crossing.GetComponent<EnemyActor>(),crossingPerception,player,Origin,6f,host.DeltaTime);
            Check(!crossingAttack.TryGetEmergencyContext(crossingContext,out _),"distant unobserved player does not trigger");
            Pose(kcc,Origin+Vector3.forward*12);Physics.SyncTransforms();
            Check(crossingAttack.TryGetEmergencyContext(crossingContext,out var sweptContext),"high speed crossing between ticks is detected");
            crossingAttack.BeginOption(sweptContext);Check(crossingAttack.HasExploded,"zero-delay crossing explodes at current location");
            Pose(kcc,Origin);Physics.SyncTransforms();
            await Until(()=>!gate.IsExplosionGrappleBlocked,"emergency debuff expiration");
            Stage="ground movement modifier";
            gate.ApplyExplosionStatus(0,.5f,1f);
            var motion=new KCCData { IsGrounded=true, DeltaTime=.1f, KinematicVelocity=Vector3.forward*6, DynamicVelocity=Vector3.right*3 };
            player.GetComponent<PlayerMovement>().ApplyActiveAbilityMotion(kcc,motion);
            Check(Vector3.Distance(motion.ExternalDelta,Vector3.back*.3f)<.001f && motion.KinematicVelocity==Vector3.forward*6 && motion.DynamicVelocity==Vector3.right*3,"50 percent ground movement correction preserves stored velocities");
            motion.IsGrounded=false;motion.ExternalDelta=Vector3.zero;
            player.GetComponent<PlayerMovement>().ApplyActiveAbilityMotion(kcc,motion);
            Check(motion.ExternalDelta==Vector3.zero,"air motion receives no explosion slow correction");
            Stage="bounded pounce";
            var jumper=SpawnEnemy(Origin+Vector3.forward*6);var attack=jumper.GetComponent<EnemySuicideAttack>();Set(attack,"requireNavMeshLanding",false);Set(attack,"preparationSeconds",0f);Set(attack,"useAnimationFrameTiming",false);
            var perception=jumper.GetComponent<EnemyPerceptionController>();typeof(EnemyPerceptionController).GetProperty("CurrentTarget").SetValue(perception,player);
            typeof(EnemyPerceptionController).GetProperty("HasDirectSight").SetValue(perception,true);
            var context=new EnemyCombatContext(jumper.GetComponent<EnemyCombatDecisionController>(),jumper.GetComponent<EnemyActor>(),perception,player,Origin,6f,host.DeltaTime);
            attack.BeginOption(context);attack.TickOption(context);Check(attack.IsAirborne,"pounce begins with a physical arc");
            Vector3 initial=attack.LandingPoint;Pose(kcc,Origin+Vector3.right*.5f);Physics.SyncTransforms();
            await Task.Delay(50);attack.TickOption(context);Check(attack.LandingPoint.x>initial.x,"early pounce corrects landing point toward moving target: "+initial+" -> "+attack.LandingPoint+" player="+player.transform.position);
            await Task.Delay(250);attack.TickOption(context);Vector3 locked=attack.LandingPoint;
            Pose(kcc,Origin+Vector3.left*10);await Task.Delay(100);attack.TickOption(context);Check(attack.LandingPoint==locked,"late pounce keeps committed landing point");
            await Task.Delay(400);attack.TickOption(context);Check(attack.HasExploded,"fixed deadline ends pounce even if target escapes");
            Pose(kcc,Origin);

            if(network)
            {
                Stage="late join status";gate.ApplyExplosionStatus(300f,.4f,300f);
                client=await Connect(false);NetworkObject replica=null;
                await Until(()=> (replica=client.FindObject(player.Id))!=null,"late join player replica");
                var remoteGate=replica.GetComponent<PlayerActionGate>();
                await Until(()=>remoteGate.IsExplosionGrappleBlocked,"late join block replication");
                Check(Mathf.Approximately(remoteGate.ExplosionGroundSpeedMultiplier,.6f),"late join receives current slow state");
                remoteGate.ApplyExplosionStatus(100,.9f,100);
                Check(Mathf.Approximately(gate.ExplosionGroundSpeedMultiplier,.6f),"client cannot apply authoritative debuff");
                await client.Shutdown();client=null;client=await Connect(false);
                await Until(()=>client.FindObject(player.Id)!=null,"reconnect replica");
                await Until(()=>client.FindObject(player.Id).GetComponent<PlayerActionGate>().IsExplosionGrappleBlocked,"reconnect retains remaining block");
                Check(true,"reconnect observes authoritative status");
                Stage="client input authority blast";
                var clientPlayer=host.Spawn(prefab,Origin+Vector3.right*12,Quaternion.identity,client.LocalPlayer);
                host.SetPlayerObject(client.LocalPlayer,clientPlayer);
                await Until(()=>client.FindObject(clientPlayer.Id)!=null,"input authority player replica");
                clientPlayer.GetComponent<Player>().enabled=false;
                var ownedReplica=client.FindObject(clientPlayer.Id);ownedReplica.GetComponent<Player>().enabled=false;
                var clientOrigin=Origin+Vector3.right*12;
                Pose(clientPlayer.GetComponent<KCC>(),clientOrigin);Physics.SyncTransforms();
                var ownedBlast=SpawnEnemy(clientOrigin+Vector3.forward*2);Set(ownedBlast.GetComponent<EnemySuicideAttack>(),"centerDamage",5f);
                Invoke(ownedBlast.GetComponent<EnemySuicideAttack>(),"Detonate");
                await Until(()=>ownedReplica.GetComponent<PlayerActionGate>().IsExplosionGrappleBlocked,"input authority receives block");
                Check(ownedReplica.HasInputAuthority && !ownedReplica.HasStateAuthority && ownedReplica.GetComponent<PlayerHealth>().CurrentHealth<100f,"host blast replicates damage to client-owned player");
                Check(!ownedReplica.GetComponent<PlayerGrapple>().CanUseGrapple,"input authority cannot grapple while blocked");
            }
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[Suicide Verification] "+ex);}
        finally
        {
            if(client!=null){await client.Shutdown();client=null;}if(host!=null){await host.Shutdown();host=null;}
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj);temporary.Clear();
            Directory.CreateDirectory("Temp/SuicideEnemy");File.WriteAllLines("Temp/SuicideEnemy/"+(network?"host-client":"single")+".txt",report);
        }
    }
    private static void Pose(KCC kcc,Vector3 position)
    { kcc.FixedData.BasePosition=kcc.FixedData.DesiredPosition=kcc.FixedData.TargetPosition=position;kcc.SetPosition(position); }
    private static NetworkObject SpawnEnemy(Vector3 position)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Suicide.prefab").GetComponent<NetworkObject>();
        return host.Spawn(prefab,position,Quaternion.identity,onBeforeSpawned:(r,obj)=>{
            obj.GetComponent<EnemyActor>().TryInitializePatrolBeforeSpawn(area);
            obj.GetComponent<EnemyIdlePatrolBrain>().enabled=false;obj.GetComponent<EnemyAwarenessBrain>().enabled=false;
            obj.GetComponent<EnemyChaseBrain>().enabled=false;obj.GetComponent<EnemyPerceptionController>().enabled=false;obj.GetComponent<EnemyCombatDecisionController>().enabled=false;
        });
    }
    private static DamageRequest Request(NetworkObject player,NetworkObject target,float damage) => new DamageRequest {
        RequestedDamage=damage,BaseDamage=damage,DamageType=DamageType.Bullet,Attacker=player.InputAuthority,SourceObject=player.gameObject,
        SourceNetworkObject=player,HitObject=target.gameObject,HitPoint=target.transform.position,HitDirection=Vector3.forward,HeadshotDamageMultiplier=1f };
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Suicide Test Host":"Suicide Test Client").AddComponent<NetworkRunner>();temporary.Add(runner.gameObject);runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>input.Set(default(NetInput)));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs{GameMode=network?(server?GameMode.Host:GameMode.Client):GameMode.Single,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>runner.IsRunning&&runner.Tick.Raw>0,"first tick");return runner;
    }
}
#endif
