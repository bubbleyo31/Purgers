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

/// <summary>明確啟動的隔離 Runner 驗證。不保存場景、不代替人工操作及獨立程序多人驗收。</summary>
public static class EnemyFlytrapRuntimeVerification
{
    public static string Status {get;private set;}="尚未執行";
    public static string Stage {get;private set;}="";
    private static NetworkRunner host,client;
    private static bool network;
    private static string session;
    private static NetInput hostInput,clientInput,weaponInput;
    private static PlayerRef weaponInputOwner = PlayerRef.None;
    private static Action inputObserver;
    private static readonly List<string> report=new List<string>();
    private static readonly List<UnityEngine.Object> temporary=new List<UnityEngine.Object>();
    private static readonly Vector3 Origin=new Vector3(1800,100,1800);
    private static void Check(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);report.Add("PASS "+message);}
    private static void Set(object obj,string field,object value) {obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);}
    private static async Task Until(Func<bool> condition,string stage,float seconds=30)
    {Stage=stage;float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(stage);await Task.Delay(20);}}
    private static void Pose(Player player,Vector3 position)
    {var kcc=player.Movement.KCC;kcc.SetDynamicVelocity(Vector3.zero);kcc.SetKinematicVelocity(Vector3.zero);kcc.SetPosition(position);kcc.FixedData.BasePosition=kcc.FixedData.TargetPosition=kcc.FixedData.DesiredPosition=position;Physics.SyncTransforms();}
    private static void Aim(Player player, Vector3 target, float height)
    {
        var kcc=player.Movement.KCC;
        var rotation=Quaternion.LookRotation(target-(kcc.FixedData.TargetPosition+Vector3.up*height));
        kcc.SetLookRotation(rotation);
        kcc.FixedData.SetLookRotation(rotation);
    }
    public static async void Begin(bool multiplayer=false, bool counterattackOnly=false)
    {
        if(!Application.isPlaying || host!=null || NetworkRunner.Instances.Any(r=>r.IsRunning))throw new InvalidOperationException("需要無現存 Runner 的 Play Mode。");
        Status="執行中";inputObserver=null;weaponInputOwner=PlayerRef.None;report.Clear();hostInput=clientInput=default;network=multiplayer;session="flytrap-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var floor=Cube("Flytrap test floor",Origin-Vector3.up*.5f,new Vector3(100,1,100));
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var obj=host.Spawn(prefab,Origin+Vector3.forward*4,Quaternion.identity,host.LocalPlayer);host.SetPlayerObject(host.LocalPlayer,obj);
            var player=obj.GetComponent<Player>();var gate=obj.GetComponent<PlayerActionGate>();
            await Until(()=>gate.IsFusionSpawned && player.Health.IsAlive,"player ready");await Task.Delay(250);
            if(counterattackOnly) { await VerifyMultiplayerCounterattack(player,prefab);Status="通過";return; }
            var enemy=Spawn(Origin);var attack=enemy.GetComponent<EnemyFlytrapAttack>();
            await Task.Delay(300);Check(!gate.IsCaptured,"stationary silent standing player is safe");
            hostInput.Buttons.Set(InputButton.Crouch,true);hostInput.Direction=Vector2.right;
            await Task.Delay(350);Check(!gate.IsCaptured,"silent crouch walking is safe");hostInput.Direction=Vector2.zero;
            await Task.Delay(400);Pose(player,Origin+Vector3.forward*4);
            var wall=Cube("Test wall",Origin+new Vector3(0,1.5f,2),new Vector3(4,3,.2f));Physics.SyncTransforms();hostInput=default;
            gate.NotifyGunshot();await Task.Delay(120);Check(!gate.IsCaptured,"wall blocks gunshot detection");UnityEngine.Object.Destroy(wall);await Task.Delay(100);
            Pose(player,Origin+Vector3.forward*4);
            var rail=Cube("Low path blocker",Origin+new Vector3(0,1.05f,2),new Vector3(3,.2f,.2f));Physics.SyncTransforms();
            Check(!attack.HasClearPullPath(player,attack.MouthAnchor.position),"body path rejects obstacle even with eye line available");UnityEngine.Object.Destroy(rail);await Task.Delay(100);
            Check(attack.HasClearPullPath(player,attack.MouthAnchor.position),"open capsule path accepted");
            host.Despawn(enemy);
            await VerifyStrikeScenarios(player);
            Pose(player,Origin+Vector3.forward*4);hostInput=default;
            enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();
            float hp=player.Health.CurrentHealth;
            bool observedPreBite=false, prematureBite=false;
            float firstBiteHealth=float.NaN;
            inputObserver=()=>{
                if(attack.Phase!=EnemyFlytrapPhase.Feeding)return;
                if(attack.FeedingProgress>0f && attack.FeedingProgress<.1f)
                {observedPreBite=true;prematureBite|=player.Health.CurrentHealth!=hp;}
                if(float.IsNaN(firstBiteHealth) && player.Health.CurrentHealth<hp)firstBiteHealth=player.Health.CurrentHealth;
            };
            gate.NotifyGunshot();await Task.Delay(150);Check(!gate.IsCaptured && attack.Phase.ToString()=="Tracking","discovery starts tracking without locking player");await Until(()=>gate.IsCaptured,"vine impact starts capture");
            Check(gate.Captor==enemy && gate.IsBlocked(PlayerActionBlockMask.Fire|PlayerActionBlockMask.Grapple|PlayerActionBlockMask.QuickAction),"capture owns exclusive gate and blocks active actions");
            Check(!gate.IsBlocked(PlayerActionBlockMask.Fire),"solo capture allows primary weapon fire");
            var other=Spawn(Origin+Vector3.left);Check(!gate.TryBeginCapture(other),"another trap cannot acquire same victim");host.Despawn(other);
            await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"KCC reaches mouth",5);
            Check(Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,attack.MouthAnchor.position)<.1f,"player held at mouth");
            Check(player.Health.CurrentHealth==hp,"pull travel does not damage");
            await Until(()=>!float.IsNaN(firstBiteHealth),"first bite");
            inputObserver=null;
            Check(observedPreBite && !prematureBite,"first damage waits for half-second interval (tick observation)");
            Check(Mathf.Abs(firstBiteHealth-(hp-player.Health.MaximumHealth*.05f))<.01f,"first solo bite is five percent maximum health");
            Check(attack.FeedingProgress > 0f && attack.FeedingProgress < .3f,"animation progress tracks partial feeding time");
            hostInput.Direction=Vector2.one;hostInput.Buttons.Set(InputButton.Jump,true);hostInput.Buttons.Set(InputButton.Grapple,true);hostInput.Buttons.Set(InputButton.Ability1,true);hostInput.Buttons.Set(InputButton.QuickAction,true);hostInput.Buttons.Set(InputButton.Fire,true);
            await Task.Delay(150);Check(gate.IsCaptured && Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,attack.MouthAnchor.position)<.1f,"movement and active inputs cannot escape");hostInput=default;
            await Until(()=>attack.Phase==EnemyFlytrapPhase.Cooldown,"solo feeding finishes",8);
            Check(!gate.IsCaptured && Mathf.Abs(player.Health.CurrentHealth-(hp-player.Health.MaximumHealth*.5f))<.01f,"solo loses exactly half maximum health and releases");
            Check(Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,attack.MouthAnchor.position)<.2f,"normal release stays at mouth instead of capture start");
            Check(attack.CooldownRemaining>9.7f && attack.CooldownRemaining<=10f,"survival starts ten-second cooldown");
            gate.NotifyGunshot();await Task.Delay(100);Check(!gate.IsCaptured,"cooldown cannot capture");
            host.Despawn(enemy);player.Health.ResetHealthToMaximum();Pose(player,Origin+Vector3.back*4);
            enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();gate.NotifyGunshot();
            await Until(()=>gate.IsCaptured,"rear-side 360 capture");await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"rescue setup");
            gate.SetBlocks(PlayerActionBlockSource.System,PlayerActionBlockMask.Aim);
            await Task.Delay(600);hp=player.Health.CurrentHealth;
            Vector3 rescuePosition=player.Movement.KCC.FixedData.TargetPosition;
            enemy.GetComponent<TestDamageReceiver>().ReceiveDamage(Kill(obj,enemy));
            Check(Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,rescuePosition)<.001f,"early rescue never teleports");
            Check(!gate.IsCaptured,"kill immediately releases without waiting for death animation");
            Check(gate.IsBlocked(PlayerActionBlockMask.Aim),"release preserves another source's lock");gate.ClearBlocks(PlayerActionBlockSource.System);
            await Task.Delay(700);Check(player.Health.CurrentHealth==hp,"rescue cancels unpaid damage");
            Pose(player,Origin+Vector3.forward*4);enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();gate.NotifyGunshot();
            await Until(()=>gate.IsCaptured,"despawn setup");host.Despawn(enemy);await Task.Delay(100);Check(!gate.IsCaptured,"enemy despawn cannot strand victim");
            player.Health.ResetHealthToMaximum();Pose(player,Origin+Vector3.forward*4);enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();
            Set(attack,"feedingSeconds",.5f);Set(attack,"damageIntervalSeconds",.17f);gate.NotifyGunshot();
            await Until(()=>attack.Phase==EnemyFlytrapPhase.Cooldown,"remainder settlement");
            Check(Mathf.Abs(player.Health.CurrentHealth-player.Health.MaximumHealth*.5f)<.01f,"non-divisible timing preserves total budget");host.Despawn(enemy);
            await VerifyCapturedWeapons(player);
            if(network)
            {
                player.Health.ResetHealthToMaximum();Pose(player,Origin+Vector3.forward*4);enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",300f);
                gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"late join capture");
                client=await Connect(false);
                await Until(()=>client.FindObject(obj.Id)!=null && client.FindObject(enemy.Id)!=null,"late join objects");
                var remoteGate=client.FindObject(obj.Id).GetComponent<PlayerActionGate>();
                await Until(()=>remoteGate.IsCaptured,"late join capture state");Check(remoteGate.Captor!=null,"late join resolves captor");
                Check(gate.CanUseWeaponWhileCaptured && remoteGate.CanUseWeaponWhileCaptured && !gate.IsBlocked(PlayerActionBlockMask.Fire) && !remoteGate.IsBlocked(PlayerActionBlockMask.Fire),"multiplayer capture allows weapons on both peers");
                remoteGate.ReleaseCapture(remoteGate.Captor);Check(gate.IsCaptured,"client cannot release authoritative capture");
                await client.Shutdown();client=null;
                await Until(()=>gate.CanUseWeaponWhileCaptured,"disconnect restores solo weapons");
                Check(!gate.IsBlocked(PlayerActionBlockMask.Fire),"last remaining player regains weapon permission");
                client=await Connect(false);
                await Until(()=>client.FindObject(obj.Id)!=null && client.FindObject(obj.Id).GetComponent<PlayerActionGate>().IsCaptured,"reconnect capture state");
                Vector3 remoteRescuePosition=player.Movement.KCC.FixedData.TargetPosition;
            enemy.GetComponent<TestDamageReceiver>().ReceiveDamage(Kill(obj,enemy));
                Check(Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,remoteRescuePosition)<.001f,"network rescue never teleports");
                await Until(()=>!client.FindObject(obj.Id).GetComponent<PlayerActionGate>().IsCaptured,"replicated rescue");
                Pose(player,Origin+Vector3.right*20);
                var owned=host.Spawn(prefab,Origin+Vector3.forward*4,Quaternion.identity,client.LocalPlayer);host.SetPlayerObject(client.LocalPlayer,owned);
                await Until(()=>client.FindObject(owned.Id)!=null,"client-owned player");
                var ownedPlayer=owned.GetComponent<Player>();var ownedGate=owned.GetComponent<PlayerActionGate>();
                enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();
                Set(attack,"trackingSeconds",1f);Set(attack,"lockedSeconds",1f);
                ownedGate.NotifyGunshot();await Until(()=>client.FindObject(enemy.Id)!=null,"client sees telegraph enemy");
                var remoteAttack=client.FindObject(enemy.Id).GetComponent<EnemyFlytrapAttack>();
                await Until(()=>remoteAttack.Ready && remoteAttack.Phase==EnemyFlytrapPhase.Tracking,"replicated tracking");
                Check(!ownedGate.IsCaptured && !client.FindObject(owned.Id).GetComponent<PlayerActionGate>().IsCaptured,"both peers remain unlocked during tracking");
                await Until(()=>remoteAttack.Phase==EnemyFlytrapPhase.Locked,"replicated locked aim");
                Vector3 fixedDirection=attack.StrikeDirection;
                Check(Vector3.Distance(remoteAttack.StrikeDirection,fixedDirection)<.001f,"client receives same frozen strike direction");
                clientInput.Direction=Vector2.right;
                await Until(()=>ownedPlayer.Movement.KCC.FixedData.TargetPosition.x>Origin.x+2f,"client dodges with input",3);
                clientInput=default;
                await Until(()=>remoteAttack.Phase==EnemyFlytrapPhase.Recovering,"client receives missed strike recovery");
                Check(!ownedGate.IsCaptured && Vector3.Distance(fixedDirection,attack.StrikeDirection)<.001f,"client movement dodges authoritative straight vine");
                host.Despawn(enemy);await Task.Delay(400);Pose(ownedPlayer,Origin+Vector3.forward*4);
                enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",1f);Set(attack,"damageIntervalSeconds",.2f);
                ownedGate.NotifyGunshot();await Until(()=>ownedGate.IsCaptured,"host captures client-owned player");
                var ownedReplica=client.FindObject(owned.Id);
                await Until(()=>ownedReplica.GetComponent<PlayerActionGate>().IsCaptured,"input authority receives capture");
                Check(ownedReplica.HasInputAuthority && !ownedReplica.HasStateAuthority,"remote player authority separated");
                clientInput.Direction=Vector2.one;clientInput.Buttons.Set(InputButton.Grapple,true);clientInput.Buttons.Set(InputButton.Ability1,true);
                await Until(()=>!ownedPlayer.Health.IsAlive,"multiplayer full-health lethal feeding",5);clientInput=default;
                Check(attack.CooldownRemaining>9f,"kill starts same ten-second cooldown");
                await Until(()=>!ownedReplica.GetComponent<PlayerHealth>().IsAlive && !ownedReplica.GetComponent<PlayerActionGate>().IsCaptured,"death and unlock replicate");
                Check(true,"after impact client input cannot escape; multiplayer loses 100 percent maximum health");
            }
            Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+": "+ex);Debug.LogError("[Flytrap Verification] "+ex);}
        finally
        {
            inputObserver=null;weaponInputOwner=PlayerRef.None;
            if(client!=null){await client.Shutdown();client=null;}if(host!=null){await host.Shutdown();host=null;}
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj);temporary.Clear();
            Directory.CreateDirectory("Temp/FlytrapMultiplayer");File.WriteAllLines("Temp/FlytrapMultiplayer/"+(network?"host-client":"single")+".txt",report);
        }
    }
    private static async Task VerifyMultiplayerCounterattack(Player player, NetworkObject prefab)
    {
        if(network) client=await Connect(false);
        var enemy=Spawn(Origin);var attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",300f);
        var gate=player.GetComponent<PlayerActionGate>();
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"multiplayer counterattack capture");
        if(network)
            await Until(()=>client.FindObject(player.Object.Id)!=null && client.FindObject(player.Object.Id).GetComponent<PlayerActionGate>().IsCaptured,"client capture permission");
        Check(gate.CanUseWeaponWhileCaptured && !gate.IsBlocked(PlayerActionBlockMask.Fire | PlayerActionBlockMask.Reload),
            "multiplayer capture permits actual weapon counterattack");
        if(network)
            Check(client.FindObject(player.Object.Id).GetComponent<PlayerActionGate>().CanUseWeaponWhileCaptured,"client receives weapon permission");
        host.Despawn(enemy);
        await VerifyCapturedWeapons(player);
        if(!network)return;

        // 同一受困期間離線／重連，不取消正在預備的武器。
        player.Health.ResetHealthToMaximum();Pose(player,Origin+Vector3.forward*4);
        enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",300f);
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"membership capture");
        var melee=player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankMeleeCombo>();
        Aim(player,player.Movement.KCC.FixedData.TargetPosition+Vector3.up*10f,1.1f);
        hostInput.Buttons.Set(InputButton.Fire,true);await Until(()=>melee.IsPreparing,"membership Tank preparation");
        int interrupted=melee.InterruptedSequence;
        await client.Shutdown();client=null;
        await Until(()=>host.ActivePlayers.Count()==1,"member left");
        Check(gate.CanUseWeaponWhileCaptured && melee.IsPreparing && melee.InterruptedSequence==interrupted,"disconnect preserves weapon permission and preparation");
        client=await Connect(false);
        await Until(()=>client.FindObject(player.Object.Id)!=null && client.FindObject(player.Object.Id).GetComponent<PlayerActionGate>().IsCaptured,"reconnected capture");
        Check(gate.CanUseWeaponWhileCaptured && melee.IsPreparing && melee.InterruptedSequence==interrupted,"joining player preserves weapon permission and preparation");
        var replicaGate=client.FindObject(player.Object.Id).GetComponent<PlayerActionGate>();
        Check(replicaGate.CanUseWeaponWhileCaptured && !replicaGate.IsBlocked(PlayerActionBlockMask.Fire | PlayerActionBlockMask.Reload),"late join and reconnect preserve replicated weapon permission");
        replicaGate.ReleaseCapture(replicaGate.Captor);Check(gate.IsCaptured,"client cannot release authoritative capture");
        hostInput=default;host.Despawn(enemy);await Task.Delay(250);
        Pose(player,Origin+Vector3.right*20);

        var owned=host.Spawn(prefab,Origin+Vector3.forward*4,Quaternion.identity,client.LocalPlayer);
        host.SetPlayerObject(client.LocalPlayer,owned);
        await Until(()=>client.FindObject(owned.Id)!=null,"remote weapon owner");
        var remotePlayer=owned.GetComponent<Player>();
        await VerifyCapturedWeapons(remotePlayer);
        Check(client.FindObject(owned.Id).HasInputAuthority && !client.FindObject(owned.Id).HasStateAuthority,"remote attacks are input authority only; Host applies damage");

        remotePlayer.Health.ResetHealthToMaximum();Pose(remotePlayer,Origin+Vector3.forward*4);
        enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",1f);Set(attack,"damageIntervalSeconds",.2f);
        owned.GetComponent<PlayerActionGate>().NotifyGunshot();
        await Until(()=>!remotePlayer.Health.IsAlive,"multiplayer damage budget retained",10);
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Cooldown,"lethal feeding cooldown");
        Check(attack.CooldownRemaining>9f,"multiplayer total damage and ten-second cooldown remain unchanged");
    }
    private static async Task VerifyCapturedWeapons(Player player)
    {
        weaponInputOwner=player.Object.InputAuthority;weaponInput=default;
        report.Add("CONTEXT weapon inputs from "+(weaponInputOwner==host.LocalPlayer ? "Host" : "Client")+"; room players="+host.ActivePlayers.Count());
        player.Health.ResetHealthToMaximum();Pose(player,Origin+Vector3.forward*4);
        var gate=player.GetComponent<PlayerActionGate>();
        await Until(()=>player.ProfessionRuntimeManager.CurrentRuntimeObject!=null,"weapon runtime");
        string error = "";
        Check(player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>()!=null || player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Attack,out error),"equip rifle: "+error);
        await Until(()=>player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>()!=null,"rifle binding");
        var rifle=player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>();
        var enemy=Spawn(Origin);var attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",300f);
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"rifle capture");
        var health=enemy.GetComponent<TestDamageReceiver>();float hp=health.CurrentHealth;
        Vector3 position=player.Movement.KCC.FixedData.TargetPosition;
        Aim(player,enemy.transform.position+Vector3.up,1.6f);
        await Task.Delay(100);
        int seq=rifle.ShotSequence;
        weaponInput.Buttons.Set(InputButton.Fire,true);
        await Until(()=>rifle.ShotSequence>seq,"captured rifle fires",5);weaponInput=default;
        await Until(()=>health.CurrentHealth<hp,"captured rifle damages captor",5);
        Check(gate.IsCaptured && health.CurrentHealth<hp,"captured primary weapon applies real damage while held");
        weaponInput.Buttons.Set(InputButton.Reload,true);await Until(()=>rifle.IsReloading,"captured reload",5);weaponInput=default;
        Check(gate.IsCaptured && rifle.IsReloading,"captured weapon can reload");
        await Until(()=>!rifle.IsReloading,"reload completed",10);
        weaponInput.Buttons.Set(InputButton.Fire,true);await Until(()=>!health.IsAlive,"rifle self rescue",10);weaponInput=default;
        Check(!gate.IsCaptured && Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,position)<.2f,"actual weapon kill releases in place");
        if(enemy!=null && enemy.IsValid)host.Despawn(enemy);

        Pose(player,Origin+Vector3.forward*4);player.Health.ResetHealthToMaximum();
        Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank,out error),"equip captured Tank: "+error);
        await Until(()=>player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankMeleeCombo>()!=null &&
            player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankMeleeCombo>().OwnerPlayer!=null,"Tank binding");
        var melee=player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankMeleeCombo>();
        enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"feedingSeconds",300f);
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Feeding,"Tank capture");
        position=player.Movement.KCC.FixedData.TargetPosition;health=enemy.GetComponent<TestDamageReceiver>();hp=health.CurrentHealth;
        Aim(player,position+Vector3.up*10f,1.1f);
        await Task.Delay(100);
        seq=melee.SwingSequence;int charges=melee.GetComponent<TankQuickDashAbility>().CurrentCharges;
        weaponInput.Direction=Vector2.one;
        foreach(var button in new[]{InputButton.Fire,InputButton.Aim,InputButton.Grapple,InputButton.Ability1,InputButton.Ability2,InputButton.QuickAction,InputButton.Jump})
            weaponInput.Buttons.Set(button,true);
        await Until(()=>melee.IsPreparing,"captured Tank preparation",5);
        await Task.Delay(300);
        Check(gate.IsCaptured && !melee.GetComponent<TankGuardAbility>().IsGuarding &&
            melee.GetComponent<TankQuickDashAbility>().CurrentCharges==charges &&
            Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,position)<.1f,"captured Tank holds weapon but cannot guard, quick dash or move");
        weaponInput=default;
        bool dashed=false;float furthest=0f;float end=Time.realtimeSinceStartup+8f;
        while(melee.SwingSequence==seq && Time.realtimeSinceStartup<end)
        {
            dashed|=melee.IsPreparedDashing;furthest=Mathf.Max(furthest,Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,position));
            await Task.Delay(10);
        }
        Check(melee.SwingSequence>seq && !dashed && furthest<.1f,"Tank preparation release swings without weapon-linked dash");
        Check(health.CurrentHealth==hp,"captured Tank aimed away still misses (no automatic captor damage)");
        await Until(()=>melee.CurrentPhase==TankMeleeAttackPhase.Idle,"Tank miss recovery");
        Aim(player,enemy.transform.position+Vector3.up,1.1f);await Task.Delay(100);
        seq=melee.SwingSequence;
        weaponInput.Buttons.Set(InputButton.Fire,true);await Until(()=>melee.IsPreparing,"downward Tank preparation");
        weaponInput=default;await Until(()=>melee.SwingSequence>seq,"downward Tank swing");
        Check(health.CurrentHealth<hp,"captured Tank aimed down damages real captor through existing resolver");
        Check(Vector3.Distance(player.Movement.KCC.FixedData.TargetPosition,position)<.1f && !melee.IsPreparedDashing,"downward Tank attack remains stationary");
        if(enemy!=null && enemy.IsValid)host.Despawn(enemy);
        weaponInput=default;weaponInputOwner=PlayerRef.None;
    }
    private static async Task VerifyStrikeScenarios(Player player)
    {
        var gate=player.GetComponent<PlayerActionGate>();
        Pose(player,Origin+Vector3.forward*4);hostInput=default;
        var enemy=Spawn(Origin);var attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"vineSpeed",6f);
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Tracking,"tracking begins");
        Check(!gate.IsCaptured,"discovery does not acquire capture gate");
        Vector3 initial=attack.StrikeDirection;
        Pose(player,Origin+new Vector3(1,0,4));await Task.Delay(150);
        Check(Vector3.Angle(initial,attack.StrikeDirection)>5f,"early windup tracks moving player");
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Locked,"aim freezes");
        Vector3 locked=attack.StrikeDirection;
        if(!network)CaptureVisual("locked-direction");
        hostInput.Direction=Vector2.right;await Task.Delay(150);hostInput=default;
        Check(player.Movement.KCC.FixedData.TargetPosition.x>Origin.x+1.1f,"movement input remains available before impact");
        Pose(player,Origin+new Vector3(4,0,4));
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Striking,"straight strike launches");
        Check(Vector3.Distance(locked,attack.StrikeDirection)<.0001f && !gate.IsCaptured,"locked direction stays fixed and launch does not capture");
        await Task.Delay(500);if(!network)CaptureVisual("vine-strike");
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Recovering,"dodged strike retracts");
        Check(!gate.IsCaptured,"sidestep makes vine miss");int sequence=attack.AttackSequence;
        gate.NotifyGunshot();await Task.Delay(200);
        Check(attack.Phase==EnemyFlytrapPhase.Recovering && attack.AttackSequence==sequence,"miss recovery prevents immediate retry");
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Waiting,"miss recovery finishes");
        Check(attack.CooldownRemaining==0f,"miss recovery is independent of feeding cooldown");host.Despawn(enemy);

        Pose(player,Origin+Vector3.forward*4);enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();Set(attack,"vineSpeed",1000f);
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Locked,"wall after aim lock");
        var wall=Cube("Vine thin wall",Origin+new Vector3(0,1.5f,2),new Vector3(4,3,.02f));Physics.SyncTransforms();
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Recovering,"fast strike hits wall");
        Check(!gate.IsCaptured && attack.VineTip.z<Origin.z+2f,"continuous sweep stops at thin wall instead of hitting player behind it");
        host.Despawn(enemy);UnityEngine.Object.Destroy(wall);await Task.Delay(100);

        Pose(player,Origin+Vector3.forward*4);enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Locked,"interception setup");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
        var blocker=host.Spawn(prefab,Origin+Vector3.forward*2,Quaternion.identity);
        await Until(()=>blocker.GetComponent<PlayerActionGate>().IsCaptured,"first body intercepts vine");
        Check(!gate.IsCaptured && attack.CapturedPlayer==blocker,"first actual player is captured instead of original aimed player");
        host.Despawn(enemy);await Task.Delay(100);
        Check(!blocker.GetComponent<PlayerActionGate>().IsCaptured,"intercepted player releases on despawn");host.Despawn(blocker);

        Pose(player,Origin+Vector3.forward*4);enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();
        gate.NotifyGunshot();await Until(()=>attack.Phase==EnemyFlytrapPhase.Locked,"pull route changes after aim lock");
        var rail=Cube("Post lock pull blocker",Origin+new Vector3(0,1.65f,2),new Vector3(3,.2f,.1f));Physics.SyncTransforms();
        Check(!attack.HasClearPullPath(player,attack.MouthAnchor.position),"new high rail blocks full body pull route");
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Recovering,"impact rejects obstructed pull");
        Check(!gate.IsCaptured,"impact cannot pull player through newly blocked route");
        host.Despawn(enemy);UnityEngine.Object.Destroy(rail);await Task.Delay(100);

        enemy=Spawn(Origin);attack=enemy.GetComponent<EnemyFlytrapAttack>();gate.NotifyGunshot();
        await Until(()=>attack.Phase==EnemyFlytrapPhase.Tracking,"kill during preparation");
        enemy.GetComponent<TestDamageReceiver>().ReceiveDamage(Kill(player.Object,enemy));
        await Task.Delay(900);Check(!gate.IsCaptured && attack.Phase==EnemyFlytrapPhase.Dead,"death cancels prepared strike without delayed capture");
        if(enemy!=null && enemy.IsValid)host.Despawn(enemy);
    }
    private static void CaptureVisual(string label)
    {
        var obj=new GameObject("Flytrap verification camera");host.MoveToRunnerScene(obj);
        var camera=obj.AddComponent<Camera>();camera.enabled=false;
        camera.transform.position=Origin+new Vector3(7,5,7);camera.transform.LookAt(Origin+new Vector3(0,1,2));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);camera.fieldOfView=45;
        var lightObj=new GameObject("Flytrap verification light");host.MoveToRunnerScene(lightObj);
        var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var rt=new RenderTexture(960,640,24);var image=new Texture2D(960,640,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();
            Directory.CreateDirectory("Temp/FlytrapMultiplayer");File.WriteAllBytes("Temp/FlytrapMultiplayer/"+label+".png",image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=previous;rt.Release();
            UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(obj);UnityEngine.Object.Destroy(lightObj);
        }
    }
    private static GameObject Cube(string name,Vector3 position,Vector3 size)
    {var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;temporary.Add(obj);host.MoveToRunnerScene(obj);obj.transform.position=position;obj.transform.localScale=size;return obj;}
    private static NetworkObject Spawn(Vector3 position)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Flytrap.prefab").GetComponent<NetworkObject>();
        return host.Spawn(prefab,position,Quaternion.identity,onBeforeSpawned:(r,obj)=>{
            if(!obj.GetComponent<EnemyActor>().TryInitializePatrolBeforeSpawn(null))throw new InvalidOperationException("Stationary contract");
        });
    }
    private static DamageRequest Kill(NetworkObject player,NetworkObject target)=>new DamageRequest {RequestedDamage=10000,BaseDamage=10000,DamageType=DamageType.Bullet,Attacker=player.InputAuthority,SourceObject=player.gameObject,SourceNetworkObject=player,HitObject=target.gameObject,HeadshotDamageMultiplier=1f};
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Flytrap Test Host":"Flytrap Test Client").AddComponent<NetworkRunner>();temporary.Add(runner.gameObject);runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>{if(server)inputObserver?.Invoke();input.Set(weaponInputOwner!=PlayerRef.None && r.LocalPlayer==weaponInputOwner ? weaponInput : server?hostInput:clientInput);});
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var result=await runner.StartGame(new StartGameArgs{GameMode=network?(server?GameMode.Host:GameMode.Client):GameMode.Single,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=2,IsVisible=false});
        Check(result.Ok,"Runner starts: "+result.ShutdownReason);await Until(()=>runner.IsRunning&&runner.Tick.Raw>0,"first tick");return runner;
    }
}
#endif
