#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

public static partial class RewardGrowthRuntimeVerification
{
    public static async void BeginPresentation()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無 Runner 的 Play Mode");
        Status = "執行中"; report.Clear(); holdE = holdHostE = false;
        session = "loot-presentation-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var logic = CreateLogic(host);
            client = await Connect(false); CreateLogic(client);
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out _) && client.TryGetPlayerObject(host.LocalPlayer, out _), "presentation replicas");
            var owner = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            var replica = PlayerAbilityQualification.FindPlayer(client, host.LocalPlayer);
            await Until(() => owner.AbilityRuntimeManager.LoadoutRevision > 0 && owner.ProfessionRuntimeManager.CurrentRuntimeObject != null &&
                replica.ProfessionRuntimeManager.CurrentRuntimeObject != null, "presentation weapons");
            var gun = owner.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>();
            var remoteGun = replica.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>();
            var view = CreatePresentationAnimator(gun, host);
            var remoteView = CreatePresentationAnimator(remoteGun, client);
            await driver.AtTick(() => { Grant(logic, owner, PlayerGrowthKind.ReloadSpeed); typeof(AttackRifle).GetProperty("MagazineAmmo").SetValue(gun, 1); Check(gun.TryStartReload(), "accelerated reload starts"); });
            await Until(() => remoteGun.IsReloading && Mathf.Abs(remoteView.GetFloat("ReloadPlaybackSpeed") - 1.1f) < .001f, "client reload speed");
            Check(Mathf.Abs(view.GetFloat("ReloadPlaybackSpeed") - 1.1f) < .001f, "host animation follows 10 percent reload bonus");
            Check(Mathf.Abs(remoteView.GetFloat("ReloadPlaybackSpeed") - 1.1f) < .001f, "client animation follows replicated cycle snapshot");
            await driver.AtTick(() => Grant(logic, owner, PlayerGrowthKind.ReloadSpeed));
            await Task.Delay(100);
            Check(gun.IsReloading && Mathf.Abs(view.GetFloat("ReloadPlaybackSpeed") - 1.1f) < .001f, "mid-reload growth does not speed up current animation");
            await Until(() => !gun.IsReloading && !remoteGun.IsReloading && view.GetFloat("ReloadPlaybackSpeed") == 1f && remoteView.GetFloat("ReloadPlaybackSpeed") == 1f, "reload completion resets presentation");
            Check(true, "both animators reset multiplier after reload completion");
            await driver.AtTick(() => { typeof(AttackRifle).GetProperty("MagazineAmmo").SetValue(gun, 1); Check(gun.TryStartReload(), "next reload starts"); });
            await Until(() => Mathf.Abs(remoteView.GetFloat("ReloadPlaybackSpeed") - 1.15f) < .001f, "next cycle uses new tier");
            Check(Mathf.Abs(view.GetFloat("ReloadPlaybackSpeed") - 1.15f) < .001f, "next reload applies 15 percent tier on both peers");
            view.GetComponent<FirstPersonViewModelActionAnimator>().UnbindGameplaySources();
            Check(view.GetFloat("ReloadPlaybackSpeed") == 1f, "unbinding clears old multiplier");
            await Until(() => !gun.IsReloading, "reload completes before loot checks");
            var pool = Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog").Rewards
                .Where(r => r != null && r.IsImplementedAbilityReward && r.Category == RewardCategory.GrappleFocus).Take(2).ToArray();
            PlayerAbilityRuntime loot = null;
            await driver.AtTick(() => {
                owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(pool[0].AbilityDefinition, out _);
                loot = owner.AbilityRuntimeManager.GetCategoryRuntime(PlayerAbilityCategory.GrappleFocus);
                owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(pool[1].AbilityDefinition, out _);
                loot.DropToWorld(origin + Vector3.right * 4 + Vector3.up * 3);
            });
            await Until(() => PlayerAbilityRuntime.LiveRuntimes.Any(r => r != null && r.Runner == client && r.Object.IsValid && r.IsWorldLoot), "client loot marker source");
            var remoteLoot = PlayerAbilityRuntime.LiveRuntimes.First(r => r != null && r.Runner == client && r.Object.IsValid && r.IsWorldLoot);
            var read = typeof(LocalMinimapController).GetMethod("TryReadLootPosition", BindingFlags.NonPublic | BindingFlags.Static);
            object[] hostArgs = { loot, host, Vector3.zero }, clientArgs = { remoteLoot, client, Vector3.zero };
            Check((bool)read.Invoke(null, hostArgs) && (bool)read.Invoke(null, clientArgs), "both peers expose dropped skill map position");
            Check((Vector3)hostArgs[2] == loot.LootPosition && (Vector3)clientArgs[2] == remoteLoot.LootPosition, "map reads authoritative loot coordinate instead of owner transform");
            Check(!(bool)read.Invoke(null, new object[] { loot, client, Vector3.zero }), "other Runner loot is excluded");
            await Task.Delay(250);
            var visual = (PlayerAbilityLootVisual)typeof(PlayerAbilityRuntime).GetField("lootVisual", Private).GetValue(remoteLoot);
            Check(visual != null && visual.GetComponentsInChildren<SpriteRenderer>().Length == 4, "client creates icon plus gold frame backing and beacon");
            Check(visual.GetComponentsInChildren<Collider>().Length == 0 && visual.GetComponentsInChildren<NetworkObject>().Length == 0, "world presentation adds no colliders or network objects");
            await driver.AtTick(() => loot.EquipFromWorld(owner.Object, 0));
            await Until(() => !remoteLoot.IsWorldLoot, "pickup replicated");
            Check(!(bool)read.Invoke(null, hostArgs) && !(bool)read.Invoke(null, clientArgs), "picked-up skills immediately cease to be map candidates");
            Status = "通過 " + report.Count;
        }
        catch (Exception ex) { Status = "失敗 " + ex.Message; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            holdE = holdHostE = false;
            if (client != null && client.IsRunning) await client.Shutdown();
            if (host != null && host.IsRunning) await host.Shutdown();
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj); temporary.Clear();
            Directory.CreateDirectory("Temp/LootPresentationVerification");
            File.WriteAllLines("Temp/LootPresentationVerification/host-client.txt", report);
        }
    }

    private static Animator CreatePresentationAnimator(AttackRifle gun, NetworkRunner runner)
    {
        var root = new GameObject("Temporary reload presentation"); temporary.Add(root); runner.MoveToRunnerScene(root);
        var animator = root.AddComponent<Animator>();
        animator.fireEvents = false; // Dummy view tests timing only; production ViewModel keeps its audio events.
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/_Project_Assets/Models/Player/Profession/Attack/Attack_FPS.controller");
        root.AddComponent<FirstPersonViewModelActionAnimator>().BindGameplaySources(PlayerProfessionType.Attack, gun, null, null);
        return animator;
    }
}
#endif
