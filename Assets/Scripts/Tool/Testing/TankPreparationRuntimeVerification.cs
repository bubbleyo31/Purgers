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
public static class TankPreparationRuntimeVerification
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
    private static Action<NetworkRunner> inputObserver;
    private static int swings;
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
    public static async void Begin(bool multiplayer=false)
    {
        if(!Application.isPlaying || host!=null)throw new InvalidOperationException("需在空閒 Play Mode 啟動。");
        Status="執行中";inputObserver=null;report.Clear();hostButtons=default;clientButtons=default;hostDirection=Vector2.zero;network=multiplayer;swings=0;
        session="tank-prep-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj=host.Spawn(prefab.GetComponent<NetworkObject>(),new Vector3(1000,100,1000),Quaternion.identity,host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer,obj);var player=obj.GetComponent<Player>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);host.MoveToRunnerScene(floor);
            floor.transform.position=new Vector3(1000,99,1000);floor.transform.localScale=new Vector3(100,1,100);
            for(int l=0;l<32;l++)if((player.Movement.KCC.Settings.CollisionLayerMask.value&(1<<l))!=0 && l!=player.gameObject.layer){floor.layer=l;break;}
            await Until(()=>player.ProfessionRuntimeManager.CurrentRuntimeObject!=null,"initial runtime");
            Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank,out string error),"equip Tank: "+error);
            await Until(()=>Melee(player)!=null && Melee(player).OwnerPlayer!=null,"Tank binding");
            var m=Melee(player);Configure(m);
            player.GetComponent<PlayerCombatFeedbackRelay>().LocalAttackPerformed+=id=>{if(id==CombatFeedbackId.TankLightMelee||id==CombatFeedbackId.TankHeavyMelee)swings++;};
            await Task.Delay(800);
            // Real enemy health is checked, not just the attack sequence.
            var ep=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab");
            var eo=host.Spawn(ep.GetComponent<NetworkObject>(),player.transform.position+Vector3.forward*2,Quaternion.identity,onBeforeSpawned:(r,o)=>{
                foreach(var b in o.GetComponents<MonoBehaviour>())if(b is EnemyIdlePatrolBrain||b is EnemyAwarenessBrain||b is EnemyPerceptionController||b is EnemyCombatDecisionController||b is EnemyChaseBrain||b is EnemyPatrolNavigator)b.enabled=false;
            });
            eo.gameObject.AddComponent<ActiveAbilityVerificationKnockbackBlocker>();var enemy=eo.GetComponent<EnemyActor>();
            var hit=new GameObject("Tank verification collider");hit.transform.SetParent(eo.transform,false);hit.transform.position=eo.transform.position+Vector3.up;
            var scale=eo.transform.lossyScale;hit.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);hit.AddComponent<SphereCollider>().radius=.5f;
            await Task.Delay(100);float hp=enemy.Health.CurrentHealth;int seq=m.SwingSequence;
            hostButtons.Set(InputButton.Fire,true);
            await Until(()=>m.IsPreparing,"first hold");await Task.Delay(1600);
            Check(m.SwingSequence==seq && enemy.Health.CurrentHealth==hp && swings==0,"hold beyond combo timeout causes no damage or shake");
            Check(m.PreparedSeconds>1f && m.ButtonHeldSeconds>m.PreparedSeconds,"both timers advance from distinct origins");
            var vm=Resources.FindObjectsOfTypeAll<FirstPersonTankViewModelAnimator>().FirstOrDefault(v=>v.gameObject.scene.IsValid() && typeof(FirstPersonTankViewModelAnimator).GetField("meleeCombo",Private).GetValue(v)==m);
            Check(vm!=null,"local Tank ViewModel is bound");var animator=(Animator)typeof(FirstPersonTankViewModelAnimator).GetField("animator",Private).GetValue(vm);
            bool canSample=m.TryGetAnimationPosition(out var frameStep,out float frameTime);
            report.Add("DIAG sample="+canSample+" step="+frameStep+" time="+frameTime+" sampled="+typeof(FirstPersonTankViewModelAnimator).GetField("samplingAttack",Private).GetValue(vm)+" hasState="+animator.HasState(0,Animator.StringToHash("Tank_Base Layer.attack1"))+" fps="+Time.frameCount);
            Check(animator.speed==0f&&Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime-.2f)<.03f,"Animator freezes exactly at configured preparation frame: speed="+animator.speed+" normalized="+animator.GetCurrentAnimatorStateInfo(0).normalizedTime+" state="+animator.GetCurrentAnimatorStateInfo(0).fullPathHash+" desired="+Animator.StringToHash("Tank_Base Layer.attack1")+" driver="+vm.enabled+" active="+vm.gameObject.activeInHierarchy);
            hostButtons.Set(InputButton.Aim,true);await Task.Delay(100);
            Check(m.IsPreparing&&!m.GetComponent<TankGuardAbility>().IsGuarding,"RMB neither cancels nor guards");hostButtons.Set(InputButton.Aim,false);
            hostButtons.Set(InputButton.Fire,false);
            await Until(()=>m.SwingSequence==seq+1,"release damage");
            Check(enemy.Health.CurrentHealth<hp,"release damages enemy through authoritative resolver");
            Check(swings==1,"one shake event at actual release swing");
            await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"first completion");
            Check(m.NextComboStep==TankMeleeComboStep.Light2&&m.ComboResetRemainingSeconds>0,"completion starts combo window for atk2");
            await Task.Delay(50);Check(animator.speed>0f,"Animator speed restored after complete attack");
            host.Despawn(eo);
            hostButtons.Set(InputButton.Fire,true);await Until(()=>m.IsPreparing,"atk2 hold");await Task.Delay(1600);
            Vector3 beforeMove=player.Movement.KCC.FixedData.TargetPosition;hostDirection=Vector2.right;await Task.Delay(200);hostDirection=Vector2.zero;
            Check(m.IsPreparing&&Vector3.Distance(beforeMove,player.Movement.KCC.FixedData.TargetPosition)>.1f,"preparation permits normal movement");
            hostButtons.Set(InputButton.Fire,false);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"atk2 completion");
            Check(m.NextComboStep==TankMeleeComboStep.Heavy&&swings==2,"long atk2 hold preserves atk3 and whiff shakes");
            hostButtons.Set(InputButton.Fire,true);await Until(()=>m.IsPreparing,"atk3 hold");
            hostButtons.Set(InputButton.Fire,false);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"atk3 completion");
            Check(m.NextComboStep==TankMeleeComboStep.Light1&&swings==3,"third stage supports hold and wraps to atk1");
            // Drive a one-tick tap at the input callback; wall-clock Task.Delay can resume after the damage frame.
            int tapAttack=m.AttackSequence,tapSwing=m.SwingSequence,tapInputs=0;
            bool releasedInWindup=false,prematureStrike=false;
            inputObserver=runner=>{
                if(runner!=host)return;
                hostButtons.Set(InputButton.Fire,tapInputs++==0);
                if(m.Preparation.Released&&m.CurrentPhase==TankMeleeAttackPhase.Startup&&m.Preparation.CursorSeconds<.5f)
                    releasedInWindup=true;
                if(m.SwingSequence>tapSwing&&m.Preparation.CursorSeconds<.5f-.0001f)prematureStrike=true;
            };
            await Until(()=>m.AttackSequence==tapAttack+1&&m.CurrentPhase==TankMeleeAttackPhase.Idle,"one-tick tap complete");
            inputObserver=null;hostButtons.Set(InputButton.Fire,false);
            Check(releasedInWindup&&!prematureStrike,"early release preserves original windup (observed per input tick)");
            Check(m.NextComboStep==TankMeleeComboStep.Light2,"tap advances one step");
            // Successful F during windup preserves the current step; no-charge F leaves preparation intact.
            var dash=m.GetComponent<TankQuickDashAbility>();int charges=dash.CurrentCharges;int cancel=m.InterruptedSequence;
            hostButtons.Set(InputButton.Fire,true);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Startup,"F windup start");
            hostButtons.Set(InputButton.QuickAction,true);await Until(()=>m.InterruptedSequence>cancel,"F windup cancel");hostButtons.Set(InputButton.QuickAction,false);
            Check(dash.CurrentCharges==charges-1&&m.NextComboStep==TankMeleeComboStep.Light2,"successful F consumes charge and retains atk2");
            Check(m.ButtonHeldSeconds==0&&m.PreparedSeconds==0,"F clears cancelled timers");hostButtons.Set(InputButton.Fire,false);
            await Until(()=>!dash.IsCombatActionLocked,"F recovery");
            // Drain remaining charges through the actual resource function, without starting additional dashes.
            var consume=typeof(TankQuickDashAbility).GetMethod("ConsumeCharge",Private);
            while(dash.CurrentCharges>0)consume.Invoke(dash,null);
            hostButtons.Set(InputButton.Fire,true);await Until(()=>m.IsPreparing,"no charge hold");cancel=m.InterruptedSequence;
            hostButtons.Set(InputButton.QuickAction,true);await Task.Delay(100);hostButtons.Set(InputButton.QuickAction,false);
            Check(m.IsPreparing&&m.InterruptedSequence==cancel,"F without charge cannot cancel");
            if(multiplayer)
            {
                client=await Connect(false);var mid=m.Object.Id;
                await Until(()=>client.FindObject(mid)!=null,"late join Tank runtime");var proxy=client.FindObject(mid).GetComponent<TankMeleeCombo>();Configure(proxy);
                await Until(()=>proxy.IsPreparing,"late join held state");
                Check(!proxy.Object.HasStateAuthority&&proxy.CurrentStep==m.CurrentStep&&proxy.PreparedSeconds>0,"late join receives step and preparation timer");
                hostButtons.Set(InputButton.Fire,false);await Until(()=>!proxy.IsPreparing,"host release replication");
                await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"host attack complete");
                await client.Shutdown();client=null;client=await Connect(false);await Until(()=>client.FindObject(mid)!=null,"reconnect runtime");
                proxy=client.FindObject(mid).GetComponent<TankMeleeCombo>();Configure(proxy);await Task.Delay(100);
                Check(!proxy.IsPreparing&&proxy.CurrentPhase==TankMeleeAttackPhase.Idle,"reconnect cannot replay stale preparation");
                var remote=host.Spawn(prefab.GetComponent<NetworkObject>(),new Vector3(1020,100,1000),Quaternion.identity,client.LocalPlayer);host.SetPlayerObject(client.LocalPlayer,remote);
                var rp=remote.GetComponent<Player>();await Until(()=>rp.ProfessionRuntimeManager.CurrentRuntimeObject!=null,"remote runtime");
                Check(rp.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank,out error),"remote equip Tank: "+error);
                await Until(()=>Melee(rp)!=null&&Melee(rp).OwnerPlayer!=null,"remote Tank bind");var rm=Melee(rp);Configure(rm);
                await Until(()=>client.FindObject(rm.Object.Id)!=null,"remote own Tank");var cm=client.FindObject(rm.Object.Id).GetComponent<TankMeleeCombo>();Configure(cm);
                clientButtons.Set(InputButton.Fire,true);await Until(()=>rm.IsPreparing&&cm.IsPreparing,"client input reaches authority hold");
                int before=rm.SwingSequence;await Task.Delay(300);Check(rm.SwingSequence==before,"remote hold does not strike");
                clientButtons.Set(InputButton.Fire,false);await Until(()=>rm.SwingSequence==before+1&&cm.SwingSequence==before+1,"client release strikes once on Host");
                Check(rm.Object.HasStateAuthority&&!cm.Object.HasStateAuthority,"Host alone owns remote gameplay result");
                await VerifyInputLock(rm, true);
                await Until(()=>cm.AttackSequence==rm.AttackSequence&&cm.CurrentPhase==TankMeleeAttackPhase.Idle,"remote lock replication");
                Check(true,"client input lock sequence converges without queued attacks");
            }
            await VerifyInputLock(m, false);
            // Weapon replacement destroys preparation and never transfers the pending swing to another weapon.
            hostButtons=default; m.ResetForInterruption();await Task.Delay(100);hostButtons.Set(InputButton.Fire,true);await Until(()=>m.IsPreparing,"replacement hold");
            var oldId=m.Object.Id;
            Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Attack,out error),"replace held weapon: "+error);hostButtons=default;
            await Until(()=>host.FindObject(oldId)==null,"old held runtime despawned");
            Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank,out error),"re-equip Tank: "+error);
            await Until(()=>Melee(player)!=null&&Melee(player).OwnerPlayer!=null,"new Tank bind");m=Melee(player);Configure(m);
            Check(m.NextComboStep==TankMeleeComboStep.Light1&&m.PreparedSeconds==0,"replacement resets combo and timers");
            await Task.Delay(150);hostButtons.Set(InputButton.Fire,true);await Until(()=>m.IsPreparing,"death hold");
            player.Health.ReceiveDamage(new DamageRequest {BaseDamage=100000,RequestedDamage=100000,DamageType=DamageType.Environment});
            await Until(()=>m==null||m.Object==null||!m.Object.IsValid||m.CurrentPhase==TankMeleeAttackPhase.Idle,"death resets preparation");
            Check(true,"death clears pending attack");Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[TankPreparationVerification] "+ex);}
        finally
        {
            inputObserver=null;hostButtons=default;clientButtons=default;hostDirection=Vector2.zero;
            if(client!=null){await client.Shutdown();client=null;}
            if(host!=null){await host.Shutdown();host=null;}
            Directory.CreateDirectory("Temp/TankInputLock");File.WriteAllLines("Temp/TankInputLock/"+(network?"host-client":"single")+".txt",report);
        }
    }
    private static async Task VerifyInputLock(TankMeleeCombo m, bool remote)
    {
        Action<bool> fire=value=>{if(remote)clientButtons.Set(InputButton.Fire,value);else hostButtons.Set(InputButton.Fire,value);};
        // Old assets may still serialize the former buffer toggle as true. It must no longer queue attacks.
        Set(m,"allowAttackInputBuffer",true);
        fire(false);m.ResetForInterruption();await Task.Delay(150);
        int lockStart=m.AttackSequence;
        fire(true);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Startup,"lock first start");fire(false);
        await Task.Delay(40);fire(true);await Task.Delay(40);fire(false);
        await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Active,"lock active");
        fire(true);await Task.Delay(40);fire(false);
        await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Recovery,"lock recovery");
        fire(true);await Task.Delay(40);fire(false);
        await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"lock complete");await Task.Delay(100);
        Check(m.AttackSequence==lockStart+1,"rapid presses throughout windup active recovery cannot queue another attack");
        Check(m.NextComboStep==TankMeleeComboStep.Light2,"ignored presses advance combo only once");
        // A press begun before completion must not start a new attack merely because it remains held.
        fire(true);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Startup,"lock second start");fire(false);
        await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Recovery,"lock second recovery");
        fire(true);await Task.Delay(650);
        Check(m.CurrentPhase==TankMeleeAttackPhase.Idle&&m.AttackSequence==lockStart+2,"holding an ignored recovery press across completion cannot auto-start");
        fire(false);await Task.Delay(80);fire(true);
        await Until(()=>m.IsPreparing,"fresh third press");
        Check(m.CurrentStep==TankMeleeComboStep.Heavy&&m.AttackSequence==lockStart+3,"fresh press after completion starts the correct next combo and can prepare");
        fire(false);await Until(()=>m.CurrentPhase==TankMeleeAttackPhase.Idle,"fresh third complete");
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var r=new GameObject(server?"Tank test Host":"Tank test Client").AddComponent<NetworkRunner>();r.ProvideInput=true;
        var events=r.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();
        events.OnInput.AddListener((runner,input)=>{inputObserver?.Invoke(runner);input.Set(new NetInput{Buttons=server?hostButtons:clientButtons,Direction=server?hostDirection:Vector2.zero});});
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
