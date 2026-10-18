#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEngine;

public static partial class RewardGrowthRuntimeVerification
{
    private static bool holdGuard;
    private static bool blockGuardGameplay;
    /// <summary>明確呼叫才執行；沿用既有隔離 Runner，不修改正式存檔、場景或資產。</summary>
    public static async void BeginGuard()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無 Runner 的 Play Mode");
        Status = "執行中"; report.Clear(); holdGuard = holdE = holdHostE = false;
        blockGuardGameplay=false;
        session = "guard-" + Guid.NewGuid().ToString("N");
        var hud=UnityEngine.Object.FindObjectOfType<LocalPlayerHealthSlider>();
        var oldHudRunner=hud!=null?SurvivalField<NetworkRunner>(hud,"runner"):null;
        try
        {
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var logic = CreateLogic(host);
            client = await Connect(false); CreateLogic(client);
            client.GetComponent<NetworkEvents>().OnInput.RemoveAllListeners();
            client.GetComponent<NetworkEvents>().OnInput.AddListener((r, input) => {
                var data = default(NetInput); data.Buttons.Set(InputButton.Aim, holdGuard);
                if(blockGuardGameplay)data.BlockedControls=Purgers.GameFlow.Control.PlayerControlMask.Gameplay;
                input.Set(data);
            });
            await Until(() => host.TryGetPlayerObject(client.LocalPlayer, out _) && client.TryGetPlayerObject(client.LocalPlayer, out _), "guard owner replicas");
            var owner = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            var replica = PlayerAbilityQualification.FindPlayer(client, client.LocalPlayer);
            Check(hud!=null,"production local health HUD exists");
            Field(hud,"runner",client);
            await Until(()=>SurvivalField<PlayerHealth>(hud,"boundHealth")==replica.Health,"HUD binds client Input Authority");
            await Until(() => owner.ProfessionRuntimeManager.CurrentRuntimeObject != null, "guard initial loadout");
            Check(!PlayerAbilityQualification.HasGuardWeapon(owner), "rifle excludes guard attributes");
            await driver.AtTick(() => Check(owner.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank, out string error), "equip chainsaw: " + error));
            await Until(() => PlayerAbilityQualification.HasGuardWeapon(owner) && PlayerAbilityQualification.HasGuardWeapon(replica), "guard runtime replicated");
            var guard = PlayerAbilityQualification.GetGuardWeapon(owner);
            var remote = PlayerAbilityQualification.GetGuardWeapon(replica);
            Field(guard,"debugGuard",false); Field(remote,"debugGuard",false);
            Check(Mathf.Abs(guard.GuardDamageReduction - .4f) < .001f && guard.MaximumGuardStamina == 100, "serialized base 40 percent / 100");
            holdGuard = true; await Until(() => guard.IsGuarding && remote.IsGuarding, "held guard from client input");
            await Until(()=>SurvivalField<LocalPlayerGuardHUD>(hud,"guardView")!=null && SurvivalField<LocalPlayerGuardHUD>(hud,"guardView").gameObject.activeSelf,"production HUD displays held guard");
            Check(true,"bound client HUD shows guard");
            await driver.AtTick(() => {
                var request = new DamageRequest { RequestedDamage=100, BaseDamage=100 };
                guard.ModifyIncomingDamage(ref request);
                Check(Mathf.Abs(request.RequestedDamage-60) < .001f && Mathf.Abs(guard.CurrentStamina-60) < .001f, "authority blocks 40 and spends 40");
            });
            await Until(() => Mathf.Abs(remote.CurrentStamina-60)<.01f, "stamina replicated");
            await Until(()=>SurvivalField<LocalPlayerGuardHUD>(hud,"guardView").transform.Find("Stamina or broken seconds").GetComponent<TMPro.TMP_Text>().text=="60 / 100","HUD reads actual replicated stamina");
            Check(true,"client HUD value follows replicated 60 / 100");
            var unauthorized = new DamageRequest { RequestedDamage=100, BaseDamage=100 };
            remote.ModifyIncomingDamage(ref unauthorized);
            Check(unauthorized.RequestedDamage==100, "Input Authority cannot apply damage modifier result");
            await Task.Delay(350);
            Check(Mathf.Abs(guard.CurrentStamina-60)<.01f, "guarding never regenerates");
            holdGuard=false; await Until(() => !guard.IsGuarding, "release guard");
            Check(guard.RecoveryDelayRemainingSeconds > 2.7f, "release starts base three second wait");
            blockGuardGameplay=true;
            await Until(() => guard.CurrentStamina>65, "normal recovery continues while gameplay input is blocked",5);
            Check(!guard.IsGuarding,"input block prevents guard but does not pause recovery");
            blockGuardGameplay=false;
            float before=guard.CurrentStamina; int tick=host.Tick.Raw;
            await Until(() => host.Tick.Raw>=tick+15,"measure recovery ticks");
            float elapsed=(host.Tick.Raw-tick)*host.DeltaTime;
            Check(Mathf.Abs((guard.CurrentStamina-before)-25*elapsed)<2, "base recovery is 25 per second");
            holdGuard=true; await Until(() => guard.IsGuarding, "guard for break");
            float brokenDuration=0;
            await driver.AtTick(() => {
                var request=new DamageRequest { RequestedDamage=10000, BaseDamage=10000 };
                guard.ModifyIncomingDamage(ref request);
                brokenDuration=guard.BrokenCooldownRemainingSeconds;
                Check(guard.IsBrokenCooldown && guard.CurrentStamina==0 && brokenDuration>4.9f, "zero stamina enters five second break");
                Grant(logic,owner,PlayerGrowthKind.GuardCooldown);
                Grant(logic,owner,PlayerGrowthKind.GuardStamina);
                Check(guard.CurrentStamina==0 && guard.MaximumGuardStamina==200, "capacity grant during break does not refill");
                Check(Mathf.Abs(guard.BrokenCooldownRemainingSeconds-brokenDuration)<.01f, "cooldown grant does not rewrite active timer");
            });
            await Task.Delay(400);
            Check(guard.IsBrokenCooldown && !guard.IsGuarding && guard.CurrentStamina==0,"held input cannot guard or recover during break");
            await Until(() => guard.IsGuarding, "held input automatically guards after break",7);
            Check(guard.CurrentStamina>0 && guard.CurrentStamina<10, "break expiry starts recovery without instant refill or extra delay");
            await Task.Delay(250);
            Check(guard.CurrentStamina<10,"auto guard stops regeneration at first positive stamina");
            holdGuard=false; await Until(()=>!guard.IsGuarding,"release auto guard");
            Check(guard.RecoveryDelayRemainingSeconds>2.6f && guard.RecoveryDelayRemainingSeconds<=2.86f,"next delay samples five percent reduction");
            await driver.AtTick(()=> {
                for(int i=0;i<5;i++) Grant(logic,owner,PlayerGrowthKind.GuardReduction);
                for(int i=1;i<3;i++) Grant(logic,owner,PlayerGrowthKind.GuardStamina);
                for(int i=1;i<5;i++) Grant(logic,owner,PlayerGrowthKind.GuardCooldown);
                Check(Mathf.Abs(guard.GuardDamageReduction-.9f)<.001f && guard.MaximumGuardStamina==400,"maximum growth 90 percent / 400");
            });
            await Until(()=>remote.MaximumGuardStamina==400 && Mathf.Abs(remote.GuardDamageReduction-.9f)<.001f,"growth and capacity replicate");
            holdGuard=true; await Until(()=>guard.IsGuarding,"max rank guard");
            await driver.AtTick(()=> {
                var request=new DamageRequest {RequestedDamage=10000,BaseDamage=10000}; guard.ModifyIncomingDamage(ref request);
                Check(guard.BrokenCooldownRemainingSeconds>3.4f && guard.BrokenCooldownRemainingSeconds<=3.51f,"max cooldown gives 3.5 second break");
            });
            holdGuard=false;
            blockGuardGameplay=false;
            await Until(()=>!guard.IsBrokenCooldown,"max rank recovery starts",5);
            before=guard.CurrentStamina; tick=host.Tick.Raw;
            await Until(()=>host.Tick.Raw>=tick+15,"measure upgraded recovery ticks");
            elapsed=(host.Tick.Raw-tick)*host.DeltaTime;
            Check(Mathf.Abs((guard.CurrentStamina-before)-100*elapsed)<5,"max capacity recovers 100 per second independent of cooldown");
            lateClient=await Connect(false); CreateLogic(lateClient);
            await Until(()=>PlayerAbilityQualification.GetGuardWeapon(PlayerAbilityQualification.FindPlayer(lateClient,client.LocalPlayer))?.MaximumGuardStamina==400,"late join capacity");
            var lateOwner=PlayerAbilityQualification.FindPlayer(lateClient,client.LocalPlayer);
            Check(PlayerGrowth.State(lateOwner).Rank(PlayerGrowthKind.GuardReduction)==5,"late join receives defense ranks");
            await driver.AtTick(()=>Check(owner.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Attack,out string error),"swap away: "+error));
            await Until(()=>!PlayerAbilityQualification.HasGuardWeapon(replica),"swap hides guard source");
            await Until(()=>!SurvivalField<LocalPlayerGuardHUD>(hud,"guardView").gameObject.activeSelf,"weapon swap clears production guard HUD");
            Check(true,"weapon swap clears client guard HUD");
            Check(PlayerGrowth.State(owner).Rank(PlayerGrowthKind.GuardStamina)==3,"swap retains attributes");
            await driver.AtTick(()=>Check(owner.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank,out string error),"swap back: "+error));
            await Until(()=>PlayerAbilityQualification.GetGuardWeapon(replica)?.MaximumGuardStamina==400,"swap back capacity restored");
            Status="通過 "+report.Count;
        }
        catch(Exception ex) {Status="失敗 "+ex.Message;report.Add("FAIL "+ex);Debug.LogException(ex);}
        finally
        {
            holdGuard=false;
            if(hud!=null)Field(hud,"runner",oldHudRunner);
            if(lateClient!=null&&lateClient.IsRunning)await lateClient.Shutdown();
            if(client!=null&&client.IsRunning)await client.Shutdown();
            if(host!=null&&host.IsRunning)await host.Shutdown();
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj); temporary.Clear();
            Directory.CreateDirectory("Temp/GuardGrowthVerification"); File.WriteAllLines("Temp/GuardGrowthVerification/host-client.txt",report);
        }
    }
}
#endif
