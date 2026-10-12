#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>明確啟動的正式場景 Single Runner 驗證；隔離存檔，不寫 Scene/Prefab。</summary>
public static class WeaponRewardRuntimeVerification
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Status { get; private set; } = "尚未執行";
    private static NetworkRunner runner;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static string Root => Path.GetFullPath("Temp/WeaponRewards/Runtime");
    private static GameSaveData saved;
    private static Player Own => runner != null && runner.IsRunning &&
        runner.TryGetPlayerObject(runner.LocalPlayer, out var obj) && obj != null && obj.IsValid ? obj.GetComponent<Player>() : null;
    private static GameLogic Logic => GameLogic.GetPrimaryForRunner(runner);

    public static async void Begin()
    {
        if (!Application.isPlaying || runner != null) throw new InvalidOperationException("Requires idle Play Mode.");
        Directory.CreateDirectory(Root); report.Clear(); Status = "執行中";
        try
        {
            await Connect(null);
            Check(Own.Profession.IsAttack, "new run starts with Attack");
            var player = Own;
            var shieldDef = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Shield.asset");
            Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(shieldDef, out string error), "equip shield: " + error);
            await Wait(() => PlayerAbilityQualification.GetModule<PlayerShieldAbility>(Own) != null, "shield bind");
            var shield = PlayerAbilityQualification.GetModule<PlayerShieldAbility>(player);
            Set(shield, "levelOneSeconds", 30f);
            var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
            shield.SimulateAbility(input, default);
            await Task.Delay(100);
            Check(shield.CurrentShield > 0, "shield active before weapon replacement");
            int revision = player.AbilityRuntimeManager.LoadoutRevision;
            var abilityIds = new List<NetworkId>();
            for (int i=0;i<PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots;i++)
            {
                var runtime = player.AbilityRuntimeManager.GetEquippedRuntimeAtSlot(i);
                if (runtime != null) abilityIds.Add(runtime.Object.Id);
            }
            var originalRuntime = player.ProfessionRuntimeManager.CurrentRuntimeObject;
            var drawer = originalRuntime.GetComponent<BulletTracerLineDrawer>();
            Set(drawer, "travelSpeed", 1f);
            drawer.DrawTracer(Vector3.zero, Vector3.forward * 1000);
            var tracers = (HashSet<GameObject>)drawer.GetType().GetField("liveTracers", Private).GetValue(drawer);
            var tracer = tracers.First();
            var quick = player.GetComponent<PlayerQuickActionController>();
            var melee = originalRuntime.GetComponent<AttackQuickMelee>();
            Set(melee, "recoveryDuration", 5f);
            Check((bool)Invoke(quick, "TryStartQuickAction"), "Attack F starts");
            await Choose(PlayerProfessionType.Tank);
            Check(tracer == null || !tracer.activeSelf, "old detached Tracer cleaned during replacement");
            Check(quick.CurrentPhase == PlayerQuickActionPhase.Idle, "old F action ends before new weapon");
            Check(player.AbilityRuntimeManager.LoadoutRevision == revision && shield.CurrentShield > 0,
                "weapon swap preserves active shield and Q/E revision");
            foreach (var id in abilityIds) Check(runner.TryFindObject(id, out _), "Q/E Runtime identity preserved: " + id);

            var dash = player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankQuickDashAbility>();
            Set(dash, "rechargeDuration", 1f);
            Invoke(dash, "ConsumeCharge"); Invoke(dash, "ConsumeCharge");
            int before = dash.CurrentCharges;
            float remaining = dash.RechargeRemainingSeconds;
            await Choose(PlayerProfessionType.Support);
            Check(player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<SupportSMG>().MagazineAmmo ==
                player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<SupportSMG>().MagazineCapacity, "SMG starts full");
            await Task.Delay(1150);
            await Choose(PlayerProfessionType.Tank);
            dash = player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankQuickDashAbility>();
            Check(dash.CurrentCharges > before && dash.CurrentCharges <= dash.MaximumCharges,
                "Tank F recharges while unequipped without unconditional refill");
            await Choose(PlayerProfessionType.Attack);
            Check(!(bool)Invoke(quick, "TryStartQuickAction"), "Attack F remaining recovery survives round trip");
            await Task.Delay(5200);
            Check((bool)Invoke(quick, "TryStartQuickAction"), "Attack F becomes available after elapsed cooldown");

            var precision = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/PrecisionLock.asset");
            Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(precision, out error), "equip precision");
            await Task.Delay(100);
            var precisionSkill = PlayerAbilityQualification.GetModule<PlayerPrecisionLockAbility>(player);
            precisionSkill.SimulateAbility(input, default);
            await Task.Delay(100);
            await Choose(PlayerProfessionType.Tank);
            await Task.Delay(150);
            Check(!precisionSkill.IsUsable && !precisionSkill.IsAbilityActive,
                "melee keeps precision equipped but ends and blocks it");

            await Choose(PlayerProfessionType.Support);
            var oldPlayerId = player.Object.Id;
            player.Health.ReceiveDamage(new DamageRequest { BaseDamage=100000, RequestedDamage=100000, DamageType=DamageType.Environment });
            await Wait(() => Own != null && Own.Object.Id != oldPlayerId && Own.Health.IsAlive, "death and respawn");
            Check(Own.Profession.IsSupport, "weapon survives death");
            await Wait(() => Own.ProfessionRuntimeManager.CurrentRuntimeObject != null, "respawn runtime");
            Check(Logic.TryWriteHostExperienceToSave(saved), "write Host equipment snapshot");
            Check(PlayerRewardSaveBridge.ReadHost(saved).EquippedWeaponId == "weapon.support", "saved weapon is Support");
            var context = runner.GetComponent<GameSaveRuntimeContext>();
            Check(context.WriteActiveSave().Success, "isolated save writes successfully");

            Logic.PrepareForAuthoritativeSceneTransition();
            await runner.LoadScene(SceneRef.FromIndex(2), LoadSceneMode.Single);
            await Wait(() => Own != null && Own.Health.IsAlive && !runner.SceneManager.IsBusy, "Game transition");
            Check(Own.Profession.IsSupport, "weapon survives scene transition");
            // Stop scene simulation promptly; network map/encounter validation is outside this test.
            await runner.Shutdown(); runner = null;
            await Connect(saved);
            Check(Own.Profession.IsSupport, "fresh Runner restores saved Support before first Runtime");
            Logic.ResetPlayerExperiencesForFailedRun();
            await Task.Delay(150);
            Check(Own.Profession.IsAttack, "failed run resets live weapon to Attack");
            Check(Logic.TryWriteHostExperienceToSave(saved) &&
                PlayerRewardSaveBridge.ReadHost(saved).EquippedWeaponId == "weapon.attack", "failed run saves Attack");
            Status = "通過";
        }
        catch (Exception exception) { Status = "失敗：" + exception; report.Add(Status); }
        finally
        {
            if (runner != null && runner.IsRunning) await runner.Shutdown();
            runner = null;
            foreach (var item in temporary) if (item != null) UnityEngine.Object.Destroy(item);
            temporary.Clear();
            File.WriteAllLines(Path.Combine(Root, "report.txt"), report);
        }
    }

    private static async Task Connect(GameSaveData resume)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<NetworkRunner>("Assets/Prefabs/Runner.prefab");
        runner = UnityEngine.Object.Instantiate(prefab);
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Single;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var repository = new JsonGameSaveRepository(Path.Combine(Root, "Saves"));
        saved = resume ?? repository.CreateNew("Weapon reward isolated verification").Value;
        var context = runner.gameObject.AddComponent<GameSaveRuntimeContext>();
        context.InitializeHost(repository, saved);
        var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        manager.IsSceneTakeOverEnabled = false;
        var scene = new NetworkSceneInfo(); scene.AddSceneRef(SceneRef.FromIndex(1), LoadSceneMode.Additive);
        var result = await runner.StartGame(new StartGameArgs { GameMode=GameMode.Single, Config=config, Scene=scene, SceneManager=manager });
        Check(result.Ok, "Single Runner starts");
        await Wait(() => Own != null && Own.Health != null && Own.Health.IsAlive &&
            Own.AbilityRuntimeManager.LoadoutRevision > 0 &&
            Own.ProfessionRuntimeManager.CurrentRuntimeObject != null, "formal player spawned");
        await Task.Delay(200);
    }

    private static async Task Choose(PlayerProfessionType target)
    {
        var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>("Assets/_Project_Assets/Data/Progression/Reward_Weapon"+target+".asset");
        var catalog = ScriptableObject.CreateInstance<PlayerRewardCatalog>(); temporary.Add(catalog);
        Set(catalog, "rewards", new[] {reward}); Set(Logic, "rewardCatalog", catalog);
        Check((bool)Invoke(Logic, "TryDebugLevelUpStateAuthority", runner.LocalPlayer), "gain one pending reward");
        Check(Logic.TryGetPlayerRewardDraft(runner.LocalPlayer, out var draft) && draft.ChoiceCount == 1 &&
            draft.GetChoice(0) == reward.StableRewardId, "Host drafts " + target);
        Check(!Logic.TryClaimRewardStateAuthority(runner.LocalPlayer, draft.DraftRevision - 1, 0, out _),
            "stale draft rejected without changing weapon");
        var player = Own;
        // Failure injection validates that a missing target cannot destroy the old runtime or consume the reward.
        var weapons = player.ProfessionRuntimeManager;
        string field = target.ToString().ToLowerInvariant()+"RuntimePrefab";
        var original = weapons.GetType().GetField(field, Private).GetValue(weapons);
        var oldId = weapons.CurrentRuntimeObject.Id;
        Set(weapons, field, null);
        Check(!Logic.TryClaimRewardStateAuthority(runner.LocalPlayer, draft.DraftRevision, 0, out _) &&
            weapons.CurrentRuntimeObject.Id == oldId, "failed replacement preserves old Runtime");
        Set(weapons, field, original);
        Check(Logic.TryClaimRewardStateAuthority(runner.LocalPlayer, draft.DraftRevision, 0, out string error), "claim "+target+": "+error);
        Check(!Logic.TryClaimRewardStateAuthority(runner.LocalPlayer, draft.DraftRevision, 0, out _), "duplicate claim rejected");
        await Task.Delay(100);
        Check(Own.Profession.CurrentProfession == target, "equipped " + target);
        Check(Logic.TryGetPlayerExperience(runner.LocalPlayer, out var xp) && xp.PendingRewards == 0, "exactly one reward consumed");
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target,args);
    private static async Task Wait(Func<bool> predicate, string label)
    {
        float deadline=Time.realtimeSinceStartup+35;
        while(!predicate()){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(label);await Task.Delay(50);}
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);report.Add("PASS "+message);}
}
#endif
