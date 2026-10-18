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

/// <summary>隔離 Runner 受控驗證；不保存正式 Scene，不取代獨立程序／自然操作验收。</summary>
public static class EnemyReflectorRuntimeVerification
{
    public static string Status { get; private set; } = "未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool network;
    private static string session;
    private static NetInput hostInput, clientInput;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly Vector3 Origin = new Vector3(1800,100,1800);
    private static void Check(bool ok,string name) { if(!ok)throw new InvalidOperationException(name);report.Add("PASS "+name); }
    private static void Set(object obj,string field,object value) { obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value); }
    private static object Call(object obj,string method,params object[] args) => obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
    private static async Task Until(Func<bool> condition,string stage,float seconds=20)
    { Stage=stage;float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(stage);await Task.Delay(20);} }
    public static async void Begin(bool multiplayer=false)
    {
        if(!Application.isPlaying || host!=null || NetworkRunner.Instances.Any(r=>r.IsRunning))throw new InvalidOperationException("需要無現存 Runner 的 Play Mode");
        network=multiplayer;session="reflector-"+Guid.NewGuid().ToString("N");report.Clear();Status="執行中";hostInput=clientInput=default;
        try
        {
            host=await Connect(true);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(floor);host.MoveToRunnerScene(floor);floor.transform.position=Origin-Vector3.up*.5f;floor.transform.localScale=new Vector3(100,1,100);
            var playerObject=host.Spawn(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>(),Origin,Quaternion.identity,host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer,playerObject);var player=playerObject.GetComponent<Player>();
            await Until(()=>player.Health.IsAlive && player.GetComponent<PlayerProfessionRuntimeManager>().CurrentRuntime!=null,"player runtime");
            await Task.Delay(250);
            await VerifyGroundStationaryAI(playerObject);
            var enemy=SpawnEnemy(Origin+Vector3.forward*10);
            var shield=enemy.GetComponent<EnemyReflectorShield>();var health=enemy.GetComponent<TestDamageReceiver>();var state=enemy.GetComponent<EnemyStateController>();
            await Task.Delay(100); float initial=health.CurrentHealth;
            var request=Request(playerObject,enemy,Vector3.forward);
            DamageReceiverUtility.TryApplyDamage(enemy.gameObject,request,out var result);
            Check(health.CurrentHealth==initial && result.AppliedDamage==0,"full shield blocks rear damage");
            request.HitDirection=Vector3.back;DamageReceiverUtility.TryApplyDamage(enemy.gameObject,request,out result);
            Check(health.CurrentHealth==initial,"full shield blocks front damage");
            var other=SpawnEnemy(Origin+Vector3.right*20);
            Check(!shield.TryReceiveReflectedProjectile(other),"another shooter's projectile cannot open this shield");host.Despawn(other);

            var projectile=SpawnShot(enemy,Origin+Vector3.up+Vector3.forward*2,Vector3.back,0.01f,playerObject);
            await Task.Delay(100);Check(projectile.HomingStarted,"near-target homing activates");
            Vector3 aim=(enemy.transform.position+Vector3.up*2.2f-projectile.transform.position).normalized;
            Check(EnemyProjectileReflection.ReflectMelee(player,Origin+Vector3.up,aim,4,4,180)==1,"authoritative melee volume reflects projectile");
            Check(projectile.IsReflected && projectile.HomingTarget==null && Vector3.Angle(projectile.TravelDirection,player.Movement.GetAimDirection())<.01f,"reflection follows aim and clears homing");
            Check(!projectile.TryReflect(player,Vector3.left),"same projectile cannot reflect twice");
            // 保留正式反彈方向，用重新 Initialize 以外的方法調速會測到不公開欄位；直接等低速太久。
            host.Despawn(projectile.Object);
            projectile=SpawnShot(enemy,enemy.transform.position+Vector3.up*2.2f-Vector3.forward*3,Vector3.forward,18,null);
            Check(projectile.TryReflect(player,Vector3.forward),"reflection accepted with arbitrary aimed direction");
            await Until(()=>shield.IsOpening,"reflected shot reaches original shooter");
            Check(health.CurrentHealth==initial,"reflected impact causes no HP damage");
            Check(state.IsAbilityStunned && !enemy.GetComponent<EnemyActionGate>().CanMove && !enemy.GetComponent<EnemyActionGate>().CanRotate && !enemy.GetComponent<EnemyActionGate>().CanAttack,"stun locks movement rotation and attacks");
            Check(!state.CanReceiveAbilityKnockback,"reflection opening rejects knockback without changing other enemies");
            Vector3 position=enemy.transform.position;Quaternion rotation=enemy.transform.rotation;
            await Task.Delay(150);Check(Vector3.Distance(position,enemy.transform.position)<.001f&&Quaternion.Angle(rotation,enemy.transform.rotation)<.01f,"stunned transform remains still");
            shield.Render();if(!network) Capture(enemy,"opening");var data=new SerializedObject(shield);var front=(Renderer)data.FindProperty("frontShield").objectReferenceValue;var rear=(Renderer)data.FindProperty("rearShield").objectReferenceValue;
            var block=new MaterialPropertyBlock();front.GetPropertyBlock(block);
            Check(front.enabled&&!rear.enabled&&block.GetColor("_BaseColor").r>.9f,"opening has orange front shield and hidden rear shield");
            CheckAdditionalShields(shield,true);
            foreach(var direction in new[]{Vector3.back,Vector3.left,Vector3.right,Vector3.up,Vector3.down})
            {request.HitDirection=direction;DamageReceiverUtility.TryApplyDamage(enemy.gameObject,request,out result);Check(health.CurrentHealth==initial,"five remaining sides block damage: "+direction);}
            request.HitDirection=Vector3.forward;DamageReceiverUtility.TryApplyDamage(enemy.gameObject,request,out result);Check(result.AppliedDamage>0&&health.CurrentHealth<initial,"rear hit damages existing health during opening");
            await Until(()=>!shield.IsOpening&&!state.IsAbilityStunned,"configured recovery",shield.StunSeconds+2);
            shield.Render();if(!network) Capture(enemy,"protected");front.GetPropertyBlock(block);Check(rear.enabled&&block.GetColor("_BaseColor").b>.9f,"recovery restores blue rear shield");
            CheckAdditionalShields(shield,false);
            float after=health.CurrentHealth;DamageReceiverUtility.TryApplyDamage(enemy.gameObject,request,out result);Check(health.CurrentHealth==after,"restored shield blocks rear damage again");
            await VerifyRifleCounterplay(player,enemy);
            await VerifyAttackEntrypoints(player,enemy);
            if(network)
            {
                Set(shield,"stunSeconds",300f);Check(shield.TryReceiveReflectedProjectile(enemy),"long opening for late join");
                client=await Connect(false);
                await Until(()=>client.FindObject(enemy.Id)!=null&&client.FindObject(enemy.Id).GetComponent<EnemyReflectorShield>().IsOpening,"late join opening");
                var remote=client.FindObject(enemy.Id).GetComponent<EnemyReflectorShield>();
                Check(!remote.TryReceiveReflectedProjectile(remote.Object),"client cannot author stun");
                remote.Render();CheckAdditionalShields(remote,true);var remoteData=new SerializedObject(remote);Check(!((Renderer)remoteData.FindProperty("rearShield").objectReferenceValue).enabled,"late join hides rear shield locally");
                await client.Shutdown();client=null;client=await Connect(false);
                await Until(()=>client.FindObject(enemy.Id)!=null&&client.FindObject(enemy.Id).GetComponent<EnemyReflectorShield>().IsOpening,"reconnect opening");
                Check(true,"reconnect restores remaining shield state");
                var reconnected=client.FindObject(enemy.Id).GetComponent<EnemyReflectorShield>();reconnected.Render();CheckAdditionalShields(reconnected,true);
                var owned=host.Spawn(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>(),Origin+Vector3.right*5,Quaternion.identity,client.LocalPlayer);
                host.SetPlayerObject(client.LocalPlayer,owned);
                await Until(()=>owned.GetComponent<PlayerProfessionRuntimeManager>().CurrentRuntime!=null&&client.FindObject(owned.Id)!=null,"client owned player");await Task.Delay(300);
                var ownedPlayer=owned.GetComponent<Player>();var ownedMelee=owned.GetComponent<PlayerProfessionRuntimeManager>().CurrentRuntime.GetComponent<AttackQuickMelee>();
                Check(client.FindObject(owned.Id).HasInputAuthority&&!client.FindObject(owned.Id).HasStateAuthority,"remote Input Authority separated from State Authority");
                var shot=SpawnShot(enemy,(Vector3)Call(ownedMelee,"GetMeleeOrigin")+(Vector3)Call(ownedMelee,"GetMeleeForward"),Vector3.back,.01f,null);
                await Until(()=>client.FindObject(shot.Object.Id)!=null,"client projectile replica");
                Check(!client.FindObject(shot.Object.Id).GetComponent<EnemyProjectile>().TryReflectByPlayerShot(client.FindObject(owned.Id).GetComponent<Player>(),Vector3.forward),"Input Authority cannot directly author gun reflection");
                Check(!client.FindObject(shot.Object.Id).GetComponent<EnemyProjectile>().TryReflect(client.FindObject(owned.Id).GetComponent<Player>(),Vector3.forward),"Input Authority cannot directly author reflection");
                clientInput.Buttons.Set(InputButton.QuickAction,true);
                await Until(()=>shot.IsReflected,"client F input reaches host reflection",5);clientInput.Buttons.Set(InputButton.QuickAction,false);
                await Until(()=>client.FindObject(shot.Object.Id).GetComponent<EnemyProjectile>().IsReflected,"reflected state replicates");
                Check(true,"client F input reflects through normal quick action and replication");
                host.Despawn(shot.Object);await Task.Delay(1500);
                var remoteRifle=owned.GetComponent<PlayerProfessionRuntimeManager>().CurrentRuntime.GetComponent<AttackRifle>();
                Vector3 fireOrigin=remoteRifle.GetGameplayShotOrigin();
                var targetShot=SpawnShot(enemy,fireOrigin+Vector3.forward*3,Vector3.back,.01f,null);var targetId=targetShot.Object.Id;
                await Until(()=>client.FindObject(targetId)!=null,"client gun reflection target replica");
                clientInput.Buttons.Set(InputButton.Fire,true);
                await Until(()=>targetShot.IsReflected,"client Fire reflects projectile on host",5);
                clientInput.Buttons.Set(InputButton.Fire,false);
                await Until(()=>client.FindObject(targetId)!=null&&client.FindObject(targetId).GetComponent<EnemyProjectile>().IsReflected,"client receives gun reflection state",5);
                Check(!targetShot.HasImpacted && Vector3.Angle(targetShot.TravelDirection,Vector3.forward)<.01f && Vector3.Angle(client.FindObject(targetId).GetComponent<EnemyProjectile>().TravelDirection,targetShot.TravelDirection)<.01f,"client Fire NetInput reflects along shot direction and replicates flight");
            }
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[Reflector Verification] "+ex);}
        finally
        {
            if(client!=null){await client.Shutdown();client=null;}if(host!=null){await host.Shutdown();host=null;}
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj);temporary.Clear();
            Directory.CreateDirectory("Temp/Reflector");File.WriteAllLines("Temp/Reflector/"+(network?"host-client":"single")+".txt",report);
        }
    }
    private static async Task VerifyRifleCounterplay(Player player,NetworkObject enemy)
    {
        var rifle=player.GetComponent<PlayerProfessionRuntimeManager>().CurrentRuntime.GetComponent<AttackRifle>();
        Vector3 origin=Origin+Vector3.up*1.6f;
        var front=SpawnShot(enemy,origin+Vector3.forward*3,Vector3.back,.01f,null);
        var back=SpawnShot(enemy,origin+Vector3.forward*5,Vector3.back,.01f,null);
        Vector3 endpoint=(Vector3)Call(rifle,"PerformAuthoritativeHitscan",origin,Vector3.forward,1f);
        Check(front.IsReflected&&!front.HasImpacted&&!back.IsReflected&&!back.HasImpacted&&endpoint.z<Origin.z+3,"Rifle reflects nearest projectile without destroying it or piercing");
        Check(!enemy.GetComponent<EnemyReflectorShield>().IsOpening,"gun contact alone does not open shield before returning impact");host.Despawn(front.Object);host.Despawn(back.Object);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(wall);host.MoveToRunnerScene(wall);wall.transform.position=origin+Vector3.forward*2;wall.transform.localScale=new Vector3(4,5,.2f);Physics.SyncTransforms();
        var blocked=SpawnShot(enemy,origin+Vector3.forward*4,Vector3.back,.01f,null);
        Call(rifle,"PerformAuthoritativeHitscan",origin,Vector3.forward,1f);
        Check(!blocked.HasImpacted&&!blocked.IsReflected,"wall blocks remote projectile gun reflection");host.Despawn(blocked.Object);UnityEngine.Object.Destroy(wall);await Task.Delay(100);
        // 以與玩家視角不同的帶俯仰射線測試，避免誤用當下準星或自動回瞄射手。
        Vector3 shotDirection=new Vector3(.3f,.4f,1f).normalized;
        var steered=SpawnShot(enemy,origin+shotDirection*3,Vector3.back,.01f,player.Object);
        Vector3 originalPosition=steered.transform.position;float originalSpeed=steered.TravelSpeed;
        Vector3 invalidEnd=origin+shotDirection*20;
        Check(!EnemyProjectilePlayerShot.Resolve(player,origin,Vector3.zero,ref invalidEnd)&&!steered.IsReflected,"zero shot direction cannot reflect or consume projectile");
        Call(rifle,"PerformAuthoritativeHitscan",origin,shotDirection,1f);
        Check(steered.IsReflected&&!steered.HasImpacted&&Vector3.Angle(steered.TravelDirection,shotDirection)<.01f,"Rifle reflection includes pitch and actual per-shot direction");
        Check(steered.HomingTarget==null&&!steered.HomingStarted&&steered.OwnerEnemy==enemy&&steered.TravelSpeed==originalSpeed&&steered.transform.position==originalPosition,"gun reflection clears homing and preserves shooter speed and position");
        var remaining=steered.LifetimeTimer;Vector3 redirect=new Vector3(-.4f,.2f,1f).normalized;
        Call(rifle,"PerformAuthoritativeHitscan",steered.transform.position-redirect*3,redirect,1f);
        Check(Vector3.Angle(steered.TravelDirection,redirect)<.01f&&steered.LifetimeTimer.Equals(remaining),"repeated gun hit redirects reflected shot without extending lifetime");
        host.Despawn(steered.Object);
        var disabled=SpawnShot(enemy,origin+Vector3.forward*3,Vector3.back,.01f,null);Set(disabled,"destroyableByPlayerShots",false);
        Call(rifle,"PerformAuthoritativeHitscan",origin,Vector3.forward,1f);
        Check(!disabled.IsReflected&&!disabled.HasImpacted,"projectile without gun opt-in is unaffected");host.Despawn(disabled.Object);
        var returning=SpawnShot(enemy,enemy.transform.position+Vector3.up*2.2f-Vector3.forward*3,Vector3.back,18,null);
        float beforeReturn=enemy.GetComponent<TestDamageReceiver>().CurrentHealth;
        Call(rifle,"PerformAuthoritativeHitscan",returning.transform.position-Vector3.forward*3,Vector3.forward,1f);
        Check(returning.IsReflected&&enemy.GetComponent<TestDamageReceiver>().CurrentHealth==beforeReturn,"intercepting bullet prevents damage to target behind projectile");
        await Until(()=>enemy.GetComponent<EnemyReflectorShield>().IsOpening,"gun-reflected projectile opens original shooter");
        Check(enemy.GetComponent<TestDamageReceiver>().CurrentHealth==beforeReturn&&enemy.GetComponent<EnemyStateController>().IsAbilityStunned,"gun-reflected impact stuns and opens rear without HP damage");
        await Until(()=>!enemy.GetComponent<EnemyReflectorShield>().IsOpening,"gun-reflected opening recovers",enemy.GetComponent<EnemyReflectorShield>().StunSeconds+2);
        var padded=SpawnShot(enemy,origin+Vector3.forward*3.6f,Vector3.back,.01f,null);
        Check(EnemyProjectileReflection.ReflectMelee(player,origin,Vector3.forward,3,2,120)==1&&padded.IsReflected,"expanded capture catches projectile beyond original melee range");host.Despawn(padded.Object);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_Reflector.prefab").GetComponent<NetworkObject>();
        var area=CreatePatrolArea();var target=host.Spawn(prefab,Origin+Vector3.right*10+Vector3.forward*20,Quaternion.identity,onBeforeSpawned:(r,obj)=>{obj.GetComponent<EnemyActor>().TryInitializePatrolBeforeSpawn(area);Set(obj.GetComponent<EnemyPerceptionController>(),"horizontalFieldOfView",90f);});
        await Task.Delay(150);
        var perception=target.GetComponent<EnemyPerceptionController>();var health=target.GetComponent<TestDamageReceiver>();float hp=health.CurrentHealth;
        Check(!perception.HasTarget,"rear threat fixture starts unaware");
        Vector3 near=target.transform.position+Vector3.right*1.4f+Vector3.up*2;
        wall=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(wall);host.MoveToRunnerScene(wall);wall.transform.position=Vector3.Lerp(origin,near,.5f);wall.transform.localScale=new Vector3(4,5,.2f);Physics.SyncTransforms();
        Call(rifle,"PerformAuthoritativeHitscan",origin,(near-origin).normalized,1f);await Task.Delay(150);
        Check(!perception.HasTarget,"wall-truncated shot cannot create threat behind wall");UnityEngine.Object.Destroy(wall);await Task.Delay(100);
        Call(rifle,"PerformAuthoritativeHitscan",origin,(near-origin).normalized,1f);
        await Until(()=>perception.CurrentTarget==player.Object,"passing shot alerts through existing awareness");
        Check(health.CurrentHealth==hp,"passing shot alerts without damage");
        await Until(()=>target.GetComponent<EnemyRangedProjectileAttack>().ProjectileSequence>0,"remembered attacker leads to turn and visible return fire",10);
        Check(Vector3.Distance(target.transform.position,Origin+Vector3.right*10+Vector3.forward*20)<.03f,"threat turn never moves fixed Ground enemy");
        foreach(var shot in EnemyProjectileReflectionSnapshot(target))host.Despawn(shot.Object);host.Despawn(target);
    }
    private static async Task VerifyAttackEntrypoints(Player player,NetworkObject enemy)
    {
        var manager=player.GetComponent<PlayerProfessionRuntimeManager>();
        var melee=manager.CurrentRuntime.GetComponent<AttackQuickMelee>();
        if(melee!=null)
        {
            Vector3 origin=(Vector3)Call(melee,"GetMeleeOrigin"),forward=(Vector3)Call(melee,"GetMeleeForward");
            var shot=SpawnShot(enemy,origin+forward, -forward,.01f,null);
            melee.OnQuickActionActive(9001);Check(shot.IsReflected,"Attack F Active callback reflects");host.Despawn(shot.Object);
        }
        foreach(var profession in new[]{PlayerProfessionType.Support,PlayerProfessionType.Tank})
        {
            string failure;Check(manager.TryEquipRewardWeaponStateAuthority(profession,out failure),"equip "+profession+": "+failure);
            await Until(()=>manager.CurrentRuntime!=null&&manager.CurrentRuntimeProfession==profession,"runtime "+profession);await Task.Delay(100);
            player.Movement.KCC.SetLookRotation(-20,0);
            if(profession==PlayerProfessionType.Support)
            {
                var smg=manager.CurrentRuntime.GetComponent<SupportSMG>();
                var gunShot=SpawnShot(enemy,Origin+Vector3.up*1.6f+Vector3.forward*3,Vector3.back,.01f,null);
                var gunEnd=(Vector3)Call(smg,"PerformAuthoritativeHitscan",Origin+Vector3.up*1.6f,Vector3.forward);
                Check(!gunShot.HasImpacted&&gunShot.IsReflected&&Vector3.Angle(gunShot.TravelDirection,Vector3.forward)<.01f&&gunEnd.z<Origin.z+3,"Support SMG reflects along actual shot rather than pitched player aim");host.Despawn(gunShot.Object);
                var support=manager.CurrentRuntime.GetComponent<SupportQuickMelee>();
                Vector3 origin=(Vector3)Call(support,"GetMeleeOrigin"),forward=(Vector3)Call(support,"GetMeleeForward");
                var shot=SpawnShot(enemy,origin+forward,-forward,.01f,null);support.OnQuickActionActive(9002);
                Check(shot.IsReflected && Vector3.Angle(shot.TravelDirection,player.Movement.GetAimDirection())<.01f,"Support F Active callback reflects with pitch");host.Despawn(shot.Object);
            }
            else
            {
                var tank=manager.CurrentRuntime.GetComponent<TankMeleeCombo>();
                Vector3 origin=(Vector3)Call(tank,"GetMeleeOrigin"),forward=(Vector3)Call(tank,"GetMeleeForward");
                var shot=SpawnShot(enemy,origin+forward,-forward,.01f,null);Call(tank,"PerformAuthoritativeAttack",new TankMeleeAttackDefinition());
                Check(shot.IsReflected&&Vector3.Angle(shot.TravelDirection,player.Movement.GetAimDirection())<.01f,"Tank chainsaw damage callback reflects with pitch");host.Despawn(shot.Object);
                var dash=manager.CurrentRuntime.GetComponent<TankQuickDashAbility>();
                Vector3 start=player.Movement.KCC.FixedData.TargetPosition;shot=SpawnShot(enemy,start+Vector3.up+Vector3.forward*.5f,Vector3.back,.01f,null);
                Call(dash,"ResolveDashDamageSegment",start,start+Vector3.forward);
                Check(shot.IsReflected&&Vector3.Angle(shot.TravelDirection,player.Movement.GetAimDirection())<.01f,"Tank F damage segment reflects with pitch");host.Despawn(shot.Object);
            }
        }
        player.Movement.KCC.SetLookRotation(0,0);
        // 共用衝刺盒使用相同準星方向，不沿水平衝刺方向反彈。
        var dashShot=SpawnShot(enemy,Origin+Vector3.up+Vector3.forward,Vector3.back,.01f,null);
        Vector3 aim=new Vector3(.2f,.4f,1).normalized;
        Check(EnemyProjectileReflection.ReflectDash(player,Origin+Vector3.up+Vector3.forward,Quaternion.identity,Vector3.one,aim)==1&&Vector3.Angle(dashShot.TravelDirection,aim)<.01f,"dash reflection uses full 3D aim");host.Despawn(dashShot.Object);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(wall);host.MoveToRunnerScene(wall);wall.transform.position=Origin+Vector3.up+Vector3.forward;wall.transform.localScale=new Vector3(3,3,.1f);Physics.SyncTransforms();
        var blocked=SpawnShot(enemy,Origin+Vector3.up+Vector3.forward*2,Vector3.back,.01f,null);
        Check(EnemyProjectileReflection.ReflectMelee(player,Origin+Vector3.up,Vector3.forward,3,3,180)==0&&!blocked.IsReflected,"wall blocks melee reflection");host.Despawn(blocked.Object);UnityEngine.Object.Destroy(wall);
        await Task.Delay(50);
    }
    private static void Capture(NetworkObject enemy,string name)
    {
        var go=new GameObject("Reflector screenshot camera");host.MoveToRunnerScene(go);var camera=go.AddComponent<Camera>();
        var old=RenderTexture.active;var rt=new RenderTexture(640,640,24);var texture=new Texture2D(640,640,TextureFormat.RGB24,false);
        try
        {
            camera.transform.position=enemy.transform.position+new Vector3(5,4.5f,-7);camera.transform.LookAt(enemy.transform.position+Vector3.up*2.03f);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.18f);camera.fieldOfView=35;camera.targetTexture=rt;
            camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,640,640),0,0);texture.Apply();Directory.CreateDirectory("Temp/Reflector");File.WriteAllBytes("Temp/Reflector/"+name+".png",texture.EncodeToPNG());
        }
        finally {RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(go);}
    }
    private static EnemyPatrolArea CreatePatrolArea()
    {
        var go=new GameObject("Reflector test patrol area");temporary.Add(go);host.MoveToRunnerScene(go);go.transform.position=Origin;
        return go.AddComponent<EnemyPatrolArea>();
    }
    private static void CheckAdditionalShields(EnemyReflectorShield shield,bool opening)
    {
        var p=new SerializedObject(shield).FindProperty("additionalShields");var block=new MaterialPropertyBlock();
        for(int i=0;i<p.arraySize;i++)
        {
            var renderer=(Renderer)p.GetArrayElementAtIndex(i).objectReferenceValue;renderer.GetPropertyBlock(block);var color=block.GetColor("_BaseColor");
            Check(renderer.enabled&&(opening?color.r>.9f:color.b>.9f),renderer.name+(opening?" stays orange during opening":" recovers blue"));
        }
    }
    private static async Task VerifyGroundStationaryAI(NetworkObject player)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_Reflector.prefab").GetComponent<NetworkObject>();
        var area=CreatePatrolArea();var start=Origin+Vector3.forward*15;
        var enemy=host.Spawn(prefab,start,Quaternion.Euler(0,150,0),onBeforeSpawned:(r,obj)=>
        {
            Check(obj.GetComponent<EnemyActor>().TryInitializePatrolBeforeSpawn(area),"Ground spawn accepts existing patrol contract");
            var option=obj.GetComponent<EnemyRangedProjectileAttack>();Set(option,"projectileSpeed",.01f);Set(option,"projectileLifetimeSeconds",15f);Set(option,"cooldownSeconds",.1f);Set(option,"activeDelaySeconds",.01f);Set(option,"recoverySeconds",0f);
        });
        await Until(()=>enemy.GetComponent<EnemyRangedProjectileAttack>().ProjectileSequence>0,"unmodified AI fires from fixed ground position",10);
        Check(Vector3.Distance(enemy.transform.position,start)<.03f,"active Ground AI does not patrol or chase");
        Check(Quaternion.Angle(enemy.transform.rotation,Quaternion.Euler(0,150,0))>5,"stationary Ground enemy still turns to shoot");
        Check(!enemy.GetComponent<EnemyMovementOwnership>().CanMove,"autonomous movement disabled by existing owner");
        var attack=enemy.GetComponent<EnemyRangedProjectileAttack>();
        await Task.Delay(450);
        Check(attack.ProjectileSequence==1&&attack.HasOutstandingProjectile,"one living shot blocks repeated attacks beyond cooldown");
        var first=attack.OutstandingProjectile.GetComponent<EnemyProjectile>();
        Check(first.TryReflectByPlayerShot(player.GetComponent<Player>(),Vector3.right)&&first.IsReflected&&!first.HasImpacted,"gun hit reflects outstanding shot without destroying it");
        await Task.Delay(300);
        Check(attack.ProjectileSequence==1&&!attack.CanStartOption(default),"gun-reflected living shot still blocks next attack");
        typeof(EnemyProjectile).GetProperty("LifetimeTimer").SetValue(first,TickTimer.CreateFromSeconds(host,.05f));
        await Until(()=>first.HasImpacted,"gun-reflected shot enters Impact at expiry");
        Check(!attack.CanStartOption(default),"Impact lifetime still blocks a new attack");
        await Until(()=>attack.ProjectileSequence==2,"gun-reflected shot releases attack after despawn");
        var second=attack.OutstandingProjectile.GetComponent<EnemyProjectile>();Check(second.TryReflect(player.GetComponent<Player>(),Vector3.right),"reflect live outstanding shot");
        await Task.Delay(300);Check(attack.ProjectileSequence==2,"reflected shot continues to block next attack");
        typeof(EnemyProjectile).GetProperty("LifetimeTimer").SetValue(second,TickTimer.CreateFromSeconds(host,.05f));
        await Until(()=>attack.ProjectileSequence==3,"expiry releases next shot");
        Check(true,"shot expiry releases existing attack controller");
        var shots=EnemyProjectileReflectionSnapshot(enemy);
        host.Despawn(enemy);foreach(var shot in shots)if(shot!=null&&shot.Object!=null&&shot.Object.IsValid)host.Despawn(shot.Object);
        await Task.Delay(100);
    }
    private static EnemyProjectile[] EnemyProjectileReflectionSnapshot(NetworkObject enemy) =>
        UnityEngine.Object.FindObjectsOfType<EnemyProjectile>().Where(p=>p.Object!=null&&p.Object.IsValid&&p.OwnerEnemy==enemy).ToArray();
    private static NetworkObject SpawnEnemy(Vector3 position)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_Reflector.prefab").GetComponent<NetworkObject>();
        return host.Spawn(prefab,position,Quaternion.identity,onBeforeSpawned:(r,obj)=>{
            // 保留正式地面巡邏／追擊 owner；反彈 fixture 只停索敵，避免額外射擊干擾個案。
            obj.GetComponent<EnemyActor>().TryInitializePatrolBeforeSpawn(CreatePatrolArea());
            foreach(var behaviour in obj.GetComponents<MonoBehaviour>())
                if(behaviour is EnemyAwarenessBrain||behaviour is EnemyPerceptionController) behaviour.enabled=false;
        });
    }
    private static EnemyProjectile SpawnShot(NetworkObject enemy,Vector3 position,Vector3 direction,float speed,NetworkObject target)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Projectiles/EnemyProjectile_Reflector.prefab").GetComponent<NetworkObject>();
        return host.Spawn(prefab,position,Quaternion.LookRotation(direction),onBeforeSpawned:(r,obj)=>{
            var projectile=obj.GetComponent<EnemyProjectile>();projectile.Initialize(r,enemy,direction,speed,25,30,1);projectile.SetHomingTargetBeforeSpawn(target);
        }).GetComponent<EnemyProjectile>();
    }
    private static DamageRequest Request(NetworkObject player,NetworkObject enemy,Vector3 direction) => new DamageRequest{RequestedDamage=10,BaseDamage=10,DamageType=DamageType.Bullet,Attacker=player.InputAuthority,SourceNetworkObject=player,SourceObject=player.gameObject,HitObject=enemy.gameObject,HitDirection=direction,HitPoint=enemy.transform.position,HeadshotDamageMultiplier=1};
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Reflector Test Host":"Reflector Test Client").AddComponent<NetworkRunner>();temporary.Add(runner.gameObject);runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>input.Set(server?hostInput:clientInput));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs{GameMode=network?(server?GameMode.Host:GameMode.Client):GameMode.Single,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>runner.IsRunning&&runner.Tick.Raw>0,"first tick");return runner;
    }
}
#endif
