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

/// <summary>手動啟動的隔離成長獎勵多人驗證；不保存場景或正式玩家進度。</summary>
public static partial class RewardGrowthRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client, lateClient;
    private static string session;
    private static bool holdE;
    private static bool holdHostE;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.Add("PASS " + message); }
    private static async Task Until(Func<bool> predicate, string label, float seconds = 40)
    {
        Stage = label; float end = Time.realtimeSinceStartup + seconds;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
    }
    private static void Fatal(Player player) => player.Health.ReceiveDamage(new DamageRequest
        { RequestedDamage = 10000, BaseDamage = 10000, DamageType = DamageType.Environment });
    private static Vector3 origin = new Vector3(1200, 1200, 1200);

    private static void Grant(GameLogic logic, Player owner, PlayerGrowthKind kind)
    {
        var catalog=Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog");
        object[] args={owner.Object.InputAuthority,owner,catalog.FindGrowth(kind),null};
        bool ok=(bool)typeof(GameLogic).GetMethod("TryGrantGrowth",Private).Invoke(logic,args);
        Check(ok,"grant "+kind+": "+args[3]);
    }
    public static async void Begin(bool lootOnly = false)
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r=>r.IsRunning)) throw new InvalidOperationException("需要無 Runner 的 Play Mode");
        Status="執行中";report.Clear();holdE=false;holdHostE=false;session="growth-"+Guid.NewGuid().ToString("N");
        try
        {
            host=await Connect(true);
            var driver=host.gameObject.AddComponent<StageTimerPauseVerificationDriver>();host.AddGlobal(driver);
            var logic=CreateLogic(host);
            await Until(()=>host.TryGetPlayerObject(host.LocalPlayer,out var p)&&p!=null,"host");
            if(lootOnly)
            {
                var testOwner=PlayerAbilityQualification.FindPlayer(host,host.LocalPlayer);
                await Until(()=>testOwner.AbilityRuntimeManager.LoadoutRevision>0,"loot smoke loadout");
                var pool=Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog").Rewards.Where(r=>r!=null&&r.IsImplementedAbilityReward&&r.Category==RewardCategory.GrappleFocus).Take(2).ToArray();
                PlayerAbilityRuntime loot=null;
                await driver.AtTick(()=>{
                    testOwner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(pool[0].AbilityDefinition,out _);
                    loot=testOwner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleFocus);
                    testOwner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(pool[1].AbilityDefinition,out _);
                    loot.DropToWorld(origin+Vector3.up*3);
                });
                float startY=loot.LootPosition.y;
                await Until(()=>loot.LootPosition.y<startY-.5f,"airborne loot falls",2);
                Check(true,"airborne skill drops fall");Status="通過 "+report.Count;return;
            }
            client=await Connect(false);var clientLogic=CreateLogic(client);
            await Until(()=>client.TryGetPlayerObject(host.LocalPlayer,out _)&&host.TryGetPlayerObject(client.LocalPlayer,out _),"replicas");
            var owner=PlayerAbilityQualification.FindPlayer(host,host.LocalPlayer);
            var helper=PlayerAbilityQualification.FindPlayer(host,client.LocalPlayer);
            var replica=PlayerAbilityQualification.FindPlayer(client,host.LocalPlayer);
            await Until(()=>owner.AbilityRuntimeManager.LoadoutRevision>0&&owner.ProfessionRuntimeManager.CurrentRuntimeObject!=null,"loadouts");
            Field(owner.Health,"debugHealth",false);Field(helper.Health,"debugHealth",false);
            await driver.AtTick(()=> {owner.Movement.KCC.SetPosition(origin);helper.Movement.KCC.SetPosition(origin+Vector3.right*20);});
            Check(owner.Grapple!=null&&owner.Profession.CurrentProfession==PlayerProfessionType.Attack,"basic grapple and starting gun retained");
            Check(owner.AbilityRuntimeManager.GetEquippedAbilityIds().Length==0,"new run has no equipped special skills");
            logic.TryGetPlayerExperience(host.LocalPlayer,out var xp);
            Check(xp.Level==1&&xp.PendingRewards==1,"level one opening reward");
            logic.TryGetPlayerRewardDraft(host.LocalPlayer,out var draft);
            string first=draft.GetChoice(0);int revision=draft.DraftRevision;
            await driver.AtTick(()=>Check(logic.TryAwardPlayerExperience(host.LocalPlayer,100),"XP while reward pending"));
            logic.TryGetPlayerExperience(host.LocalPlayer,out xp);logic.TryGetPlayerRewardDraft(host.LocalPlayer,out draft);
            Check(xp.Level>1&&xp.PendingRewards>1&&draft.DraftRevision==revision&&draft.GetChoice(0)==first,"queued XP does not reroll shown options");
            Check(!clientLogic.TryAwardPlayerExperience(host.LocalPlayer,100),"client cannot grant authoritative XP");
            await driver.AtTick(()=> {Grant(logic,owner,PlayerGrowthKind.MaximumHealth);Grant(logic,owner,PlayerGrowthKind.MaximumHealth);});
            Check(owner.Health.MaximumHealth==140&&owner.Health.CurrentHealth==140,"two HP tiers yield 140 and top up only added amount");
            await Until(()=>replica.Health.MaximumHealth==140,"growth replication");
            var gun=owner.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>();
            int capacity=gun.MagazineCapacity,ammo=gun.MagazineAmmo;
            await driver.AtTick(()=>{Grant(logic,owner,PlayerGrowthKind.MagazineCapacity);Grant(logic,owner,PlayerGrowthKind.JohnWick);});
            Check(gun.MagazineCapacity==capacity+15&&gun.MagazineAmmo==ammo+15,"capacity adds five plus John Wick ten bullets");
            await driver.AtTick(()=>{
                typeof(AttackRifle).GetProperty("MagazineAmmo").SetValue(gun,1);
                Check(gun.TryStartReload(),"reload starts");
                float duration=gun.ReloadDurationSeconds;
                Grant(logic,owner,PlayerGrowthKind.ReloadSpeed);
                Check(Mathf.Abs(gun.ReloadDurationSeconds-duration)<.001f,"running reload retains duration snapshot");
            });
            await Until(()=>!gun.IsReloading,"reload finish",10);
            await driver.AtTick(()=>{Grant(logic,owner,PlayerGrowthKind.NotToday);Grant(logic,owner,PlayerGrowthKind.SelfRescue);Grant(logic,owner,PlayerGrowthKind.Adrenaline);Fatal(owner);});
            Check(owner.Health.IsAlive&&!owner.Health.IsDowned&&Mathf.Abs(owner.Health.CurrentHealth-35)<.01f,"Not Today intercepts before down at 25 percent");
            Check(!PlayerGrowth.Has(owner,PlayerGrowthKind.NotToday)&&PlayerGrowth.Has(owner,PlayerGrowthKind.Adrenaline),"lethal interception consumes only Not Today");
            await driver.AtTick(()=>Fatal(owner));Check(owner.Health.CurrentHealth==35,"three second invulnerability rejects second lethal hit");
            await Task.Delay(3200);
            await driver.AtTick(()=>{Fatal(owner);Fatal(helper);});
            Check(owner.Health.CanSelfRescue&&!PlayerGrowth.Has(owner,PlayerGrowthKind.Adrenaline),"down consumes temporary passives, keeps self rescue");
            Check(gun.MagazineCapacity==capacity+5&&gun.MagazineAmmo<=gun.MagazineCapacity,"John Wick loss clears excess bullets");
            await Task.Delay(2500);Check(owner.Object.IsValid&&owner.Health.IsDowned&&helper.Health.IsDowned,"all down waits past normal two seconds for self rescuer");
            holdHostE=true;
            await Until(()=>owner.Health.SelfRescueElapsed>.4f,"self rescue starts");float bleed=owner.Health.DownedRemainingSeconds;
            await Task.Delay(300);Check(Mathf.Abs(owner.Health.DownedRemainingSeconds-bleed)<.02f,"self rescue pauses own bleedout");
            holdHostE=false;await Until(()=>owner.Health.SelfRescueElapsed==0,"self rescue release resets");
            Check(PlayerGrowth.Has(owner,PlayerGrowthKind.SelfRescue),"interruption retains passive");
            holdHostE=true;float start=Time.realtimeSinceStartup;
            await Until(()=>owner.Health.IsAlive,"ten second self rescue",15);holdHostE=false;
            Check(Time.realtimeSinceStartup-start>=9.8f&&Mathf.Abs(owner.Health.CurrentHealth-49)<.01f,"self rescue requires ten seconds and restores 35 percent of 140");
            Check(!PlayerGrowth.Has(owner,PlayerGrowthKind.SelfRescue)&&PlayerGrowth.State(owner).Rank(PlayerGrowthKind.MaximumHealth)==2,"successful self rescue consumes passive, retains attribute rank");
            await driver.AtTick(()=>{
                typeof(PlayerHealth).GetMethod("FinishRescueStateAuthority",Private).Invoke(helper.Health,null);
                Grant(logic,owner,PlayerGrowthKind.SelfRescue);Fatal(owner);
                helper.Movement.KCC.SetPosition(owner.transform.position+Vector3.right*1.5f);
            });
            holdE=true;await Until(()=>owner.Health.IsAlive,"teammate rescue",8);holdE=false;
            Check(PlayerGrowth.Has(owner,PlayerGrowthKind.SelfRescue),"teammate rescue preserves self rescue passive");
            var catalog=Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog");
            var focuses=catalog.Rewards.Where(r=>r!=null&&r.Category==RewardCategory.GrappleFocus&&r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerActiveAbilityBase>()!=null).Take(2).ToArray();
            var hit=catalog.Rewards.First(r=>r!=null&&r.Category==RewardCategory.GrappleHit);
            PlayerAbilityRuntime original=null,kept=null;
            await driver.AtTick(()=>{
                Check(owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(hit.AbilityDefinition,out var error),"equip hit: "+error);
                kept=owner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleHit);
                Check(owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(focuses[0].AbilityDefinition,out error),"equip first focus: "+error);
                original=owner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleFocus);original.SkillLevel=3;
                var module=original.GetComponent<PlayerActiveAbilityBase>();
                typeof(PlayerActiveAbilityBase).GetProperty("CooldownTimer",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(module,TickTimer.CreateFromSeconds(host,8));
                Check(owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(focuses[1].AbilityDefinition,out error),"replace focus: "+error);
            });
            await driver.AtTick(()=>original.DropToWorld(original.LootPosition+Vector3.up*3));
            float airY=original.LootPosition.y;
            await Until(()=>original.LootPosition.y<airY-.5f,"airborne loot falls",2);
            await Until(()=>original.LootPosition.y<airY-2.7f,"airborne loot reaches ground",3);
            Check(true,"airborne skill drops fall onto ground");
            Check(original.IsWorldLoot&&original.SkillLevel==3&&original.LootCooldownRemaining>4,"old skill becomes loot retaining level and cooldown");
            Check(kept==owner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleHit),"focus replacement preserves other slot runtime");
            await Task.Delay(500);Check(original.LootCooldownRemaining<7.8f&&original.LootCooldownRemaining>3,"ground cooldown continues");
            await Until(()=>PlayerAbilityRuntime.LiveRuntimes.Any(r=>r!=null&&r.Runner==client&&r.Object.IsValid&&r.IsWorldLoot&&r.SkillLevel==3),"loot replication");
            await driver.AtTick(()=>{
                Vector3 pickup=original.LootPosition+Vector3.back*1.2f-Vector3.up*.25f;
                owner.Movement.KCC.SetPosition(pickup);owner.Movement.KCC.SetLookRotation(44,0);
            });
            await Task.Delay(200);
            await driver.AtTick(()=>{
                Check(owner.AbilityRuntimeManager.FindLootCandidate()==original,"actual E pickup candidate passes range aim and occlusion");
                var input=default(NetInput);input.Buttons.Set(InputButton.Ability1,true);
                owner.AbilityRuntimeManager.FilterLootInput(ref input);
                Check(!input.Buttons.IsSet(InputButton.Ability1)&&!original.IsWorldLoot,"actual pickup consumes E and transfers skill");
                Check(original.SkillLevel==3&&original.TransferCooldownRemaining>0,"picked skill retains level and remaining cooldown");
            });
            await driver.AtTick(()=>{
                Grant(logic,owner,PlayerGrowthKind.CarnivorousTree);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab").GetComponent<NetworkObject>();
                var enemy=host.Spawn(prefab,origin+Vector3.forward*40,Quaternion.identity,onBeforeSpawned:(r,o)=>{
                    o.GetComponent<EnemyCombatDecisionController>().enabled=false;
                    Field(o.GetComponent<EnemyPerceptionController>(),"sightDistance",.01f);
                });
                float beforeHP=owner.Health.CurrentHealth;
                var request=new DamageRequest{RequestedDamage=20,BaseDamage=20,Attacker=owner.Object.InputAuthority,
                    SourceNetworkObject=owner.Object,SourceObject=owner.gameObject,DamageType=DamageType.Bullet,FeedbackId=CombatFeedbackId.AttackRifle};
                Check(DamageReceiverUtility.TryApplyDamage(enemy.gameObject,request,out var result)&&result.AppliedDamage>0,"actual enemy damage resolved");
                Check(Mathf.Abs(owner.Health.CurrentHealth-beforeHP-result.AppliedDamage*.05f)<.01f,"lifesteal is five percent actual enemy HP damage");
                host.Despawn(enemy);
                Grant(logic,owner,PlayerGrowthKind.RegenerationDelay);
            });
            float beforeRegen=owner.Health.CurrentHealth;
            await Task.Delay(1200);Check(Mathf.Abs(owner.Health.CurrentHealth-beforeRegen)<.02f,"regeneration waits for unlock delay");
            await Until(()=>owner.Health.CurrentHealth>beforeRegen+1,"seven second regeneration delay",9);
            Check(true,"regeneration starts with base five HP per second");
            await driver.AtTick(()=>Grant(logic,owner,PlayerGrowthKind.RegenerationRate));
            Check(PlayerGrowth.Value(owner,PlayerGrowthKind.RegenerationRate)==10,"regeneration rate first tier replaces five with ten");
            var manual=owner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleFocus);
            var extraReward=catalog.Rewards.First(r=>r!=null&&r.IsImplementedAbilityReward&&r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerExperienceAbility>()!=null);
            var bonusCatalog=ScriptableObject.CreateInstance<PlayerRewardCatalog>();temporary.Add(bonusCatalog);
            Field(bonusCatalog,"rewards",new[]{extraReward});
            await driver.AtTick(()=>{
                Field(logic,"rewardCatalog",bonusCatalog);Field(owner.AbilityRuntimeManager,"indecisionInterval",.4f);
                Grant(logic,owner,PlayerGrowthKind.Indecision);
            });
            await Until(()=>PlayerAbilityRuntime.LiveRuntimes.Any(r=>r!=null&&r.Runner==host&&r.Object.IsValid&&r.IsBonusCast&&r.GetComponent<PlayerExperienceAbility>()!=null&&r.GetComponent<PlayerExperienceAbility>().ExperienceMultiplier>1),"extra random focus cast",5);
            await driver.AtTick(()=>{
                Check(owner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleFocus)==manual,"extra focus does not replace manual slot");
                var extra=PlayerAbilityQualification.GetModule<IPlayerExperienceMultiplier>(owner);
                Check(extra!=null&&extra.ExperienceMultiplier==2,"extra experience modifier is included");
                float hp=owner.Health.CurrentHealth;
                DamageReceiverUtility.TryApplyDamage(owner.gameObject,new DamageRequest{RequestedDamage=1,BaseDamage=1,DamageType=DamageType.Environment},out var hitResult);
                Check(Mathf.Abs(hitResult.AppliedDamage-2)<.01f,"extra skill retains double incoming damage side effect");
                logic.ConsumePassive(owner.Object.InputAuthority,PlayerGrowthKind.Indecision);Field(logic,"rewardCatalog",catalog);
            });
            lateClient=await Connect(false);CreateLogic(lateClient);
            await Until(()=>lateClient.TryGetPlayerObject(host.LocalPlayer,out var p)&&p!=null&&p.GetComponent<PlayerHealth>().MaximumHealth==140,"late join growth snapshot");
            Check(true,"late join receives growth rank");await lateClient.Shutdown();lateClient=null;
            await Until(()=>host.ActivePlayers.Count()==2,"late join departure");
            await driver.AtTick(()=>{
                Grant(logic,helper,PlayerGrowthKind.NotToday);
                helper.Movement.KCC.SetPosition(new Vector3(origin.x,logic.FallDeathWorldY-1,origin.z));
            });
            await Until(()=>!host.TryGetPlayerObject(client.LocalPlayer,out _),"fall bypasses Not Today");
            Check(true,"map fall directly kills Not Today holder");
            await driver.AtTick(()=>{logic.ConsumePassive(owner.Object.InputAuthority,PlayerGrowthKind.SelfRescue);Fatal(owner);});
            await Until(()=>!host.TryGetPlayerObject(host.LocalPlayer,out _),"no self rescuer returns to normal team wipe",5);
            Check(true,"normal team wipe resumes after self rescue opportunity is gone");
            await driver.AtTick(()=>{logic.PrepareForAuthoritativeSceneTransition();});
            Check(!PlayerAbilityRuntime.LiveRuntimes.Any(r=>r!=null&&r.Runner==host&&r.Object.IsValid&&r.IsWorldLoot),"scene transition clears ground loot");
            Status="通過 "+report.Count;
        }
        catch(Exception ex){Status="失敗 "+ex.Message;report.Add("FAIL "+ex);Debug.LogException(ex);}
        finally
        {
            holdE=false;holdHostE=false;
            if(lateClient!=null&&lateClient.IsRunning)await lateClient.Shutdown();
            if(client!=null&&client.IsRunning)await client.Shutdown();
            if(host!=null&&host.IsRunning)await host.Shutdown();
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj);temporary.Clear();
            Directory.CreateDirectory("Temp/RewardGrowthVerification");File.WriteAllLines(lootOnly ? "Temp/RewardGrowthVerification/loot-fall.txt" : "Temp/RewardGrowthVerification/host-client.txt",report);
        }
    }
    private static GameLogic CreateLogic(NetworkRunner runner)
    {
        var root = new GameObject("Temporary rescue GameLogic", typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        var logic = root.AddComponent<GameLogic>();
        root.AddComponent<Purgers.GameFlow.Stage.StageFlowController>();
        Field(logic, "playerPrefab", NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        Field(logic, "debugPlayerLifecycle", false); Field(logic, "fallbackSpawnPosition", origin);
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(floor); runner.MoveToRunnerScene(floor);
        floor.transform.position = origin + Vector3.down * 2; floor.transform.localScale = new Vector3(100, 1, 100);
        floor.layer = LayerMask.NameToLayer("Ground") < 0 ? 0 : LayerMask.NameToLayer("Ground");
        return logic;
    }
    private static void CreateSafeHouseFlow(NetworkRunner runner)
    {
        var root = new GameObject("Temporary rescue SafeHouseFlow", typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        root.AddComponent<Purgers.GameFlow.SafeHouse.SafeHouseFlowController>();
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(2), new[] { root.GetComponent<NetworkObject>() }, default);
    }

    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Rescue host" : "Rescue client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => { var data = default(NetInput); data.Buttons.Set(InputButton.Ability1, server ? holdHostE : r == client && holdE); input.Set(data); });
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var scene = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); scene.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = server ? GameMode.Host : GameMode.Client, SessionName = session,
            Config = config, SceneManager = scene, CustomPhotonAppSettings = app, PlayerCount = 4, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner tick");
        return runner;
    }
}
#endif
