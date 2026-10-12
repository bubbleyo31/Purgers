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

/// <summary>明確啟動的真實 NetInput 探測；只改 Runtime Clone，不寫 Scene／Prefab。</summary>
public static class TankPreparedDashRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static NetworkButtons hostButtons, clientButtons;
    private static Vector2 hostDirection;
    private static readonly List<string> report = new List<string>();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string session;
    private static bool network;
    private static TankMeleeCombo Melee(Player p) => p.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankMeleeCombo>();
    private static void Set(object o,string field,object value) => o.GetType().GetField(field,Private).SetValue(o,value);
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message);report.Add("PASS "+message); }
    private static async Task Until(Func<bool> check,string stage)
    { Stage=stage;float end=Time.realtimeSinceStartup+20f;while(!check()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(stage);await Task.Delay(20);} }
    private static void Configure(TankMeleeCombo m)
    {
        foreach(string name in new[]{"lightAttack1","lightAttack2","heavyAttack"})
        {
            var d=(TankMeleeAttackDefinition)m.GetType().GetField(name,Private).GetValue(m);
            d.UseAnimationFrames=true;d.FrameTiming=new AnimationFrameTiming {ClipDuration=1f,FramesPerSecond=30f,PlaybackSpeed=1f};
            d.PreparationFrame=6f;d.DamageFrame=15f;d.RecoveryFrame=21f;d.Damage=1f;d.Range=5f;d.SearchRadius=5f;
        }
        Set(m,"requireLineOfSight",false);Set(m,"meleeHitMask",(LayerMask)(~0));Set(m,"debugTankMelee",false);
    }
    private static NetworkObject SpawnEnemy(Player p, float distance, float lateral = 0f)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab");
        var obj=host.Spawn(prefab.GetComponent<NetworkObject>(),p.Movement.KCC.FixedData.TargetPosition+Vector3.forward*distance+Vector3.right*lateral,Quaternion.identity,onBeforeSpawned:(r,o)=>{
            foreach(var b in o.GetComponents<MonoBehaviour>())
                if(b is EnemyIdlePatrolBrain||b is EnemyAwarenessBrain||b is EnemyPerceptionController||b is EnemyCombatDecisionController||b is EnemyChaseBrain||b is EnemyPatrolNavigator)b.enabled=false;
        });
        obj.gameObject.AddComponent<ActiveAbilityVerificationKnockbackBlocker>();
        ConfigureTargetGeometry(obj);
        return obj;
    }
    private static void ConfigureTargetGeometry(NetworkObject obj)
    {
        // Controlled target geometry: current production model is giant-sized and the forward ray crosses its leg gap.
        // Keep real NetworkObject/health/damage receiver, replace geometry only on these disposable test clones.
        foreach(var c in obj.GetComponentsInChildren<Collider>())c.enabled=false;
        foreach(var h in obj.GetComponentsInChildren<Hitbox>())h.enabled=false;
        var hit=new GameObject("Prepared dash controlled target");hit.layer=9;hit.transform.SetParent(obj.transform,false);
        hit.transform.position=obj.transform.position+Vector3.up*1.6f;
        var scale=obj.transform.lossyScale;hit.transform.localScale=new Vector3(1f/scale.x,1f/scale.y,1f/scale.z);
        hit.AddComponent<SphereCollider>().radius=.5f;
    }
    public static async void Begin(bool multiplayer=false)
    {
        if(!Application.isPlaying || host!=null)throw new InvalidOperationException("需在空閒 Play Mode 啟動。");
        Status="執行中";report.Clear();hostButtons=default;clientButtons=default;network=multiplayer;
        session="TankDash_"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            if(network) client=await Connect(false);
            var owner=network?client.LocalPlayer:host.LocalPlayer;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj=host.Spawn(prefab.GetComponent<NetworkObject>(),new Vector3(1000,100,1000),Quaternion.identity,owner);
            host.SetPlayerObject(owner,obj);var player=obj.GetComponent<Player>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(floor);
            floor.transform.position=new Vector3(1000,99,1000);floor.transform.localScale=new Vector3(100,1,100);floor.layer=0;
            await Until(()=>player.ProfessionRuntimeManager.CurrentRuntimeObject!=null,"initial runtime");
            Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank,out string error),"equip Tank: "+error);
            await Until(()=>Melee(player)!=null&&Melee(player).OwnerPlayer!=null,"Tank binding");
            var m=Melee(player);Configure(m);Set(m,"requireLineOfSight",true);
            TankMeleeCombo remote=null;Player local=player;
            if(network) {
                await Until(()=>client.FindObject(obj.Id)!=null,"client replica");local=client.FindObject(obj.Id).GetComponent<Player>();
                await Until(()=>local.ProfessionRuntimeManager.CurrentRuntimeObject!=null&&Melee(local)!=null&&Melee(local).OwnerPlayer!=null,"client Tank binding");remote=Melee(local);Configure(remote);
            }
            await Task.Delay(800);
            var enemy=SpawnEnemy(player,8f);await Task.Delay(250);
            if(network){await Until(()=>client.FindObject(enemy.Id)!=null,"client enemy replica" );ConfigureTargetGeometry(client.FindObject(enemy.Id));await Task.Delay(100);}
            float hp=enemy.GetComponent<TestDamageReceiver>().CurrentHealth;
            Vector3 start=player.Movement.KCC.FixedData.TargetPosition;
            Vector3 origin=start+Vector3.up*1.6f;
            report.Add("DIAG origin="+origin+" enemy="+enemy.transform.position+" hp="+hp);
            foreach(var col in enemy.GetComponentsInChildren<Collider>()) if(col.enabled) report.Add("DIAG collider "+col.name+" bounds="+col.bounds);
            if(host.LagCompensation.Raycast(origin,Vector3.forward,12f,owner,out LagCompensatedHit first,~((1<<5)|(1<<7)|(1<<8)),HitOptions.IncludePhysX|HitOptions.IgnoreInputAuthority|HitOptions.SubtickAccuracy,QueryTriggerInteraction.Ignore))
                report.Add("DIAG first="+first.GameObject.name+" layer="+first.GameObject.layer+" point="+first.Point);
            Check(PlayerEnemyAimQuery.TryGetAuthoritative(player,origin,Vector3.forward,12f,1<<9,~((1<<5)|(1<<7)|(1<<8)),out _,out _),"authoritative direct aim finds controlled enemy collider");
            RaycastHit[] hits=null;
            Check(PlayerEnemyAimQuery.TryGetLocal(local,origin,Vector3.forward,100f,1<<9,~((1<<5)|(1<<7)|(1<<8)),ref hits),"local enemy crosshair uses local Runner colliders");
            int seq=m.SwingSequence;
            SetFire(true);await Until(()=>m.IsPreparing,"hold freeze");await Task.Delay(200);
            Check(m.SwingSequence==seq&&enemy.GetComponent<TestDamageReceiver>().CurrentHealth==hp,"hold has no damage");
            SetFire(false);await Until(()=>m.IsPreparedDashing,"released dash");
            Check(m.SwingSequence==seq&&enemy.GetComponent<TestDamageReceiver>().CurrentHealth==hp,"dash starts without damage");
            float observedRelease = host.Tick.Raw * host.DeltaTime - m.PreparedDashElapsedSeconds;
            Check(Mathf.Abs(m.PreparedDashDurationSeconds-.3f)<.001f,"dash duration derives from 9 source frames at 30 FPS");
            await Until(()=>m.PreparedDashElapsedSeconds>=.1f,"animation resumes during dash");
            m.TryGetAnimationPosition(out _,out float sample);
            Check(sample>.25f&&sample<.5f&&m.SwingSequence==seq,"animation advances before damage frame without early strike");
            if(network){await Until(()=>remote.IsPreparedDashing,"client dash replication");Check(remote.Object.HasInputAuthority&&!remote.Object.HasStateAuthority,"client input, Host authority");}
            await Until(()=>m.SwingSequence==seq+1,"arrival strike");
            Check(player.Movement.KCC.FixedData.TargetPosition.z>start.z+3f,"KCC moves player toward target");
            Check(Mathf.Abs(host.Tick.Raw*host.DeltaTime-observedRelease-.3f)<=host.DeltaTime*3f,"strike respects animation deadline within polling/tick tolerance");
            Check(Mathf.Abs(player.Movement.KCC.FixedData.TargetPosition.z-(enemy.transform.position.z-1.5f))<.15f,"arrives at stop distance by damage frame");
            Check(enemy.GetComponent<TestDamageReceiver>().CurrentHealth<hp,"arrival strike damages using original melee resolver");
            Check(!player.GetComponent<PlayerActionGate>().IsBlocked(PlayerActionBlockMask.Movement|PlayerActionBlockMask.QuickAction),"arrival clears only weapon movement/action lock");
            await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"complete first");
            Check(m.NextComboStep==TankMeleeComboStep.Light2&&m.ComboResetRemainingSeconds>0,"combo window starts after recovery");
            host.Despawn(enemy);await Task.Delay(150);
            // Early tap cannot dash even with a distant valid enemy.
            enemy=SpawnEnemy(player,8f);await Task.Delay(150);seq=m.SwingSequence;start=player.Movement.KCC.FixedData.TargetPosition;
            SetFire(true);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Startup,"tap start");SetFire(false);
            await Until(()=>m.SwingSequence==seq+1,"tap strike");
            Check(Vector3.Distance(start,player.Movement.KCC.FixedData.TargetPosition)<.5f,"tap never dashes");
            await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"tap complete");
            // Occluding wall blocks selection and red UI.
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(wall);wall.layer=0;
            wall.transform.position=player.Movement.KCC.FixedData.TargetPosition+new Vector3(0,1.5f,3);wall.transform.localScale=new Vector3(4,3,.2f);
            await Task.Delay(150);origin=player.Movement.KCC.FixedData.TargetPosition+Vector3.up*1.6f;
            Check(!PlayerEnemyAimQuery.TryGetAuthoritative(player,origin,Vector3.forward,12,1<<9,~((1<<5)|(1<<7)|(1<<8)),out _,out _),"wall blocks target selection");
            if(!network)Check(!PlayerEnemyAimQuery.TryGetLocal(player,origin,Vector3.forward,100,1<<9,~((1<<5)|(1<<7)|(1<<8)),ref hits),"wall blocks red crosshair");
            seq=m.SwingSequence;start=player.Movement.KCC.FixedData.TargetPosition;SetFire(true);await Until(()=>m.IsPreparing,"wall hold");SetFire(false);
            await Until(()=>m.SwingSequence==seq+1,"wall fallback swing");Check(Vector3.Distance(start,player.Movement.KCC.FixedData.TargetPosition)<.5f,"occluded preparation releases in place");
            UnityEngine.Object.Destroy(wall);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"wall complete");
            // A new obstacle after release stops the dash; no teleport through wall.
            // Dynamic speed is intentionally not overridden; the frame deadline also applies to collisions.
            SetFire(true);await Until(()=>m.IsPreparing,"blocked travel hold");SetFire(false);await Until(()=>m.IsPreparedDashing,"blocked travel dash");
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(wall);wall.layer=0;
            float wallZ=player.Movement.KCC.FixedData.TargetPosition.z+2f;
            wall.transform.position=new Vector3(start.x,start.y+1.5f,wallZ);wall.transform.localScale=new Vector3(4,3,.2f);
            await Until(()=>!m.IsPreparedDashing,"collision ends dash");Check(player.Movement.KCC.FixedData.TargetPosition.z<wallZ,"dash cannot cross new wall");
            UnityEngine.Object.Destroy(wall);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"collision recovery");
            // Off-axis target: direct ray misses but the configured half-angle can acquire it.
            host.Despawn(enemy);enemy=SpawnEnemy(player,8f,1.1f);
            await Task.Delay(150);origin=player.Movement.KCC.FixedData.TargetPosition+Vector3.up*1.6f;
            var candidates=new List<LagCompensatedHit>();
            Check(!PlayerEnemyAimQuery.TryGetAuthoritative(player,origin,Vector3.forward,12,1<<9,~((1<<5)|(1<<7)|(1<<8)),out _,out _),"off-axis target is outside direct ray");
            Check(!PlayerEnemyAimQuery.TryGetAuthoritativeWithTolerance(player,origin,Vector3.forward,12,0,1<<9,~((1<<5)|(1<<7)|(1<<8)),candidates,out _,out _),"zero tolerance retains direct-only selection");
            Check(PlayerEnemyAimQuery.TryGetAuthoritativeWithTolerance(player,origin,Vector3.forward,12,10,1<<9,~((1<<5)|(1<<7)|(1<<8)),candidates,out var selected,out _)&&selected.Object==enemy,"10 degree cone selects visible off-axis enemy");
            seq=m.SwingSequence;start=player.Movement.KCC.FixedData.TargetPosition;
            SetFire(true);await Until(()=>m.IsPreparing,"cone hold");SetFire(false);await Until(()=>m.IsPreparedDashing,"cone dash");
            await Until(()=>m.SwingSequence==seq+1,"cone strike");
            Check(player.Movement.KCC.FixedData.TargetPosition.x>start.x+.5f,"cone selection dashes toward actual target direction");
            await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"cone recovery");
            // Interruption releases movement gate and does not perform a pending attack.
            host.Despawn(enemy);enemy=SpawnEnemy(player,8f);await Task.Delay(150);seq=m.SwingSequence;
            SetFire(true);await Until(()=>m.IsPreparing,"interrupt hold");SetFire(false);await Until(()=>m.IsPreparedDashing,"interrupt dash");
            m.ResetForInterruption();await Task.Delay(100);
            Check(m.CurrentPhase==TankMeleeAttackPhase.Idle&&m.SwingSequence==seq&&m.PreparedSeconds==0,"interruption cancels pending strike and clears counters");
            Check(!player.GetComponent<PlayerActionGate>().IsBlocked(PlayerActionBlockMask.Movement|PlayerActionBlockMask.QuickAction),"interruption releases weapon lock");
            if(network) {
                await client.Shutdown();client=null;client=await Connect(false);
                await Until(()=>client.FindObject(obj.Id)!=null,"rejoin observer");
                var observer=client.FindObject(obj.Id).GetComponent<Player>();
                await Until(()=>observer.ProfessionRuntimeManager.CurrentRuntimeObject!=null,"rejoin runtime");
                Check(!Melee(observer).IsPreparedDashing&&Melee(observer).SwingSequence==seq,"rejoin receives cleared state without replaying strike");
            }
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[TankPreparedDashVerification] "+ex);}
        finally {
            hostButtons=default;clientButtons=default;
            if(client!=null){await client.Shutdown();client=null;}
            if(host!=null){await host.Shutdown();host=null;}
            Directory.CreateDirectory("Temp/TankDashTiming");File.WriteAllLines("Temp/TankDashTiming/"+(network?"host-client":"single")+".txt",report);
        }
    }
    private static void SetFire(bool value) {if(network)clientButtons.Set(InputButton.Fire,value);else hostButtons.Set(InputButton.Fire,value);}
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var r=new GameObject(server?"Tank test Host":"Tank test Client").AddComponent<NetworkRunner>();r.ProvideInput=true;
        var events=r.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();
        events.OnInput.AddListener((runner,input)=>input.Set(new NetInput{Buttons=server?hostButtons:clientButtons,Direction=server?hostDirection:Vector2.zero}));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var e in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(e.Item2);
        var manager=r.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var settings=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);settings.FixedRegion="asia";
        var result=await r.StartGame(new StartGameArgs{GameMode=network?(server?GameMode.Host:GameMode.Client):GameMode.Single,
            SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=settings,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>r.IsRunning&&r.Tick.Raw>0,"first tick");return r;
    }
}
#endif
