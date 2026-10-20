#if UNITY_EDITOR
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEngine;

/// <summary>Reuses the existing isolated Runner fixture. Does not touch saved games or scenes.</summary>
public static partial class RewardGrowthRuntimeVerification
{
    private static NetworkButtons focusHostButtons, focusClientButtons;
    private static NetInput focusClientPlacement;
    private static void FocusInput(NetworkRunner runner, bool server)
    {
        var events = runner.GetComponent<NetworkEvents>();
        events.OnInput.RemoveAllListeners();
        events.OnInput.AddListener((r, packet) => { var input = server ? default : focusClientPlacement; input.Buttons = server ? focusHostButtons : focusClientButtons; packet.Set(input); });
    }
    private static async Task PressFocus(bool onHost, InputButton button)
    {
        if (onHost) focusHostButtons.Set(button, true); else focusClientButtons.Set(button, true);
        int until = host.Tick.Raw + 5; await Until(() => host.Tick.Raw >= until, "pressed input", 5);
        if (onHost) focusHostButtons.Set(button, false); else focusClientButtons.Set(button, false);
        until = host.Tick.Raw + 5; await Until(() => host.Tick.Raw >= until, "released input", 5);
    }
    public static async void BeginFocusSlots()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無 Runner 的 Play Mode");
        Status = "執行中"; report.Clear(); holdE = holdHostE = false;
        host = client = lateClient = null; focusHostButtons = focusClientButtons = default; focusClientPlacement = default;
        session = "focus-slots-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true); FocusInput(host, true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var logic = CreateLogic(host);
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null, "host player");
            client = await Connect(false); FocusInput(client, false); CreateLogic(client);
            await Until(() => client.TryGetPlayerObject(host.LocalPlayer, out var p) && p != null &&
                host.TryGetPlayerObject(client.LocalPlayer, out var q) && q != null, "client players");
            var owner = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            var remote = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            var replica = PlayerAbilityQualification.FindPlayer(client, host.LocalPlayer);
            var ownClient = PlayerAbilityQualification.FindPlayer(client, client.LocalPlayer);
            await Until(() => owner.AbilityRuntimeManager.LoadoutRevision > 0 && remote.AbilityRuntimeManager.LoadoutRevision > 0, "loadouts");
            var catalog = Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog");
            var shield = catalog.Rewards.First(r => r != null && r.AbilityDefinition != null && r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerShieldAbility>() != null);
            var experience = catalog.Rewards.First(r => r != null && r.AbilityDefinition != null && r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerExperienceAbility>() != null);
            var bullet = catalog.Rewards.First(r => r != null && r.AbilityDefinition != null && r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerBulletTimeAbility>() != null);
            var grenade = catalog.Rewards.First(r => r != null && r.AbilityDefinition != null && r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerProjectileAbility>() != null);
            var choices = new[] { shield, experience, bullet };
            var manager = owner.AbilityRuntimeManager;
            Check(manager.GetEquippedAbilityIds().Length == 0, "new run begins with empty skill slots");
            Check(!manager.TryEquipFocusRewardStateAuthority(shield.AbilityDefinition, -1, out _), "empty slots still require explicit focus reward destination");
            await driver.AtTick(() => {
                foreach (var reward in choices)
                    Check(manager.TryEquipRewardAbilityStateAuthority(reward.AbilityDefinition, out var error), "fill E/Z/X: " + error);
                for (int slot = 0; slot < 3; slot++)
                {
                    var runtime = manager.GetFocusRuntime(slot);
                    typeof(PlayerActiveAbilityBase).GetField("castDelaySeconds", Private).SetValue(runtime.GetComponent<PlayerActiveAbilityBase>(), 0f);
                    runtime.SkillLevel = slot + 1;
                    Check(runtime.Definition == choices[slot].AbilityDefinition, "stable focus slot " + slot);
                }
                logic.RecordEquippedAbilities(owner);
                foreach (var reward in choices)
                    Check(remote.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(reward.AbilityDefinition, out var error), "remote fill: " + error);
            });
            await Until(() => replica.AbilityRuntimeManager.GetFocusRuntime(2) != null &&
                ownClient.AbilityRuntimeManager.GetFocusRuntime(2) != null, "three slots replicate");
            Check(!ownClient.AbilityRuntimeManager.TryDropFocusStateAuthority(0), "Input Authority cannot directly drop");
            var e = manager.GetFocusRuntime(0); var z = manager.GetFocusRuntime(1); var x = manager.GetFocusRuntime(2);
            var eAbility = e.GetComponent<PlayerActiveAbilityBase>(); var zAbility = z.GetComponent<PlayerActiveAbilityBase>(); var xAbility = x.GetComponent<PlayerActiveAbilityBase>();
            await PressFocus(true, InputButton.Ability2);
            Check(zAbility.ActivationSequence == 1 && eAbility.ActivationSequence == 0 && xAbility.ActivationSequence == 0, "Z only activates its own skill once");
            await PressFocus(true, InputButton.Ability1);
            Check(eAbility.IsAbilityActive && zAbility.IsAbilityActive, "nonconflicting E and Z effects coexist");
            await PressFocus(true, InputButton.Ability3);
            Check(xAbility.IsAbilityActive, "X activates third skill");
            await PressFocus(true, InputButton.Ability3);
            Check(!xAbility.IsAbilityActive && xAbility.CooldownRemainingSeconds > 0, "X recast ends only X and starts its cooldown");
            await PressFocus(false, InputButton.Ability2);
            await Until(() => remote.AbilityRuntimeManager.GetFocusRuntime(1).GetComponent<PlayerActiveAbilityBase>().ActivationSequence == 1, "client Z reaches authority");
            Check(remote.AbilityRuntimeManager.GetFocusRuntime(0).GetComponent<PlayerActiveAbilityBase>().ActivationSequence == 0, "client Z does not trigger E");
            await PressFocus(true, InputButton.DropFocusZ);
            Check(manager.GetFocusRuntime(1) == null && z.IsWorldLoot, "ALT Z intent drops only Z");
            Check(manager.GetFocusRuntime(0) == e && manager.GetFocusRuntime(2) == x, "drop preserves E/X runtime identity");
            Check(z.SkillLevel == 2 && z.LootCooldownRemaining > 0, "drop retains level and enters transfer cooldown");
            float remaining = z.LootCooldownRemaining;
            await Until(() => replica.AbilityRuntimeManager.GetFocusRuntime(1) == null, "drop replicates");
            logic.TryGetPlayerRewardDraft(host.LocalPlayer, out var draft);
            Check(draft.GetFocusId(1) == "" && draft.GetFocusId(0) == shield.StableRewardId && draft.GetFocusId(2) == bullet.StableRewardId, "remembered loadout preserves hole");
            await driver.AtTick(() => {
                z.DropToWorld(owner.transform.position + Vector3.up * 1.4f + owner.Movement.GetAimDirection() * 1.5f);
                Check(manager.FindLootCandidate() == z, "focus loot remains eligible with selected empty slot");
                var openingInput = new NetInput(); openingInput.Buttons.Set(InputButton.Ability1, true);
                manager.FilterLootInput(ref openingInput);
                Check(z.IsWorldLoot && manager.GetFocusRuntime(1) == null, "E opening never auto-equips focus loot");
                Check(!manager.TryPickupFocusLootStateAuthority(z.Object.Id, -1, manager.LoadoutRevision, out _), "pickup without destination rejected");
                Check(!manager.TryPickupFocusLootStateAuthority(z.Object.Id, 1, manager.LoadoutRevision - 1, out _), "stale loadout selection rejected");
                Check(!manager.TryPickupFocusLootStateAuthority(default, 1, manager.LoadoutRevision, out _), "wrong loot identity rejected");
                Check(!ownClient.AbilityRuntimeManager.TryPickupFocusLootStateAuthority(z.Object.Id, 1, ownClient.AbilityRuntimeManager.LoadoutRevision, out _), "client cannot directly award world loot");
                bool picked = manager.TryPickupFocusLootStateAuthority(z.Object.Id, 1, manager.LoadoutRevision, out var pickupError);
                Check(picked && manager.GetFocusRuntime(1) == z, "explicit world transfer fills Z with same runtime: " + pickupError);
                logic.RecordEquippedAbilities(owner);
                Check(!manager.TryEquipRewardAbilityStateAuthority(experience.AbilityDefinition, out _), "owned skill acquisition rejected");
                Check(!manager.TryEquipRewardAbilityStateAuthority(grenade.AbilityDefinition, out _), "full loadout cannot silently replace");
            });
            Check(z.LootCooldownRemaining <= remaining && z.LootCooldownRemaining > 0, "pickup does not refresh cooldown");
            await driver.AtTick(() => {
                var property = typeof(GameLogic).GetProperty("PlayerRewardDrafts", Private);
                var drafts = (NetworkDictionary<PlayerRef, PlayerRewardNetworkState>)property.GetValue(logic);
                var selected = drafts[host.LocalPlayer]; selected.SetChoices(new[] { grenade.StableRewardId }); drafts.Set(host.LocalPlayer, selected);
                Check(!logic.TryClaimRewardStateAuthority(host.LocalPlayer, selected.DraftRevision, 0, out _), "focus reward without slot preserves draft");
                Check(logic.TryClaimRewardStateAuthority(host.LocalPlayer, selected.DraftRevision, 0, out var error, 1), "full reward replaces selected Z: " + error);
                Check(!logic.TryClaimRewardStateAuthority(host.LocalPlayer, selected.DraftRevision, 0, out _, 0), "stale replacement revision rejected");
            });
            Check(manager.GetFocusRuntime(1).Definition == grenade.AbilityDefinition && z.IsWorldLoot, "reward replacement drops old skill");
            Check(manager.GetFocusRuntime(0) == e && manager.GetFocusRuntime(2) == x, "reward replacement preserves other runtime identities");
            await driver.AtTick(() => {
                z.DropToWorld(owner.transform.position + Vector3.up * 1.4f + owner.Movement.GetAimDirection() * 1.5f);
                Check(manager.FindLootCandidate() == z, "full loadout still offers ground focus loot");
                int rev=manager.LoadoutRevision;
                Check(manager.TryPickupFocusLootStateAuthority(z.Object.Id, 0, rev, out var error), "ground pickup can replace selected occupied E: " + error);
                Check(manager.GetFocusRuntime(0)==z && e.IsWorldLoot, "ground replacement drops only selected E");
                Check(!manager.TryPickupFocusLootStateAuthority(z.Object.Id, 2, rev, out _), "replayed confirmation cannot acquire twice");
            });
            var remoteManager=remote.AbilityRuntimeManager;
            var remoteLoot=remoteManager.GetFocusRuntime(1);
            await driver.AtTick(() => {
                Check(remoteManager.TryDropFocusStateAuthority(1), "prepare remote empty Z");
                remoteLoot.DropToWorld(remote.transform.position+Vector3.up*1.4f+remote.Movement.GetAimDirection()*1.5f);
                Check(remoteManager.FindLootCandidate()==remoteLoot, "remote ground candidate");
                focusClientPlacement = new NetInput { FocusLootTarget=remoteLoot.Object.Id, FocusLootSlot=2, FocusLootLoadoutRevision=remoteManager.LoadoutRevision };
            });
            await Until(()=>remoteManager.GetFocusRuntime(2)==remoteLoot,"client confirmed ground placement reaches Host");
            focusClientPlacement=default;
            Check(remoteManager.GetFocusRuntime(1)==null,"client chose occupied X while empty Z remained empty");
            await Until(()=>ownClient.AbilityRuntimeManager.GetFocusRuntime(2)!=null &&
                ownClient.AbilityRuntimeManager.GetFocusRuntime(2).Definition==experience.AbilityDefinition,"client sees selected X");
            Check(ownClient.AbilityRuntimeManager.GetFocusRuntime(1)==null,"client empty slot replicated");
            lateClient = await Connect(false); CreateLogic(lateClient);
            await Until(() => PlayerAbilityQualification.FindPlayer(lateClient, host.LocalPlayer)?.AbilityRuntimeManager.GetFocusRuntime(2) != null, "late join loadout");
            var late = PlayerAbilityQualification.FindPlayer(lateClient, host.LocalPlayer).AbilityRuntimeManager;
            Check(late.GetFocusRuntime(0).Definition == experience.AbilityDefinition &&
                late.GetFocusRuntime(1).Definition == grenade.AbilityDefinition &&
                late.GetFocusRuntime(2).Definition == bullet.AbilityDefinition, "late join sees three correct slot identities");
            Status = "通過 " + report.Count;
        }
        catch (Exception ex) { Status = "失敗 " + ex.Message; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            focusHostButtons = focusClientButtons = default;
            if (lateClient != null && lateClient.IsRunning) await lateClient.Shutdown();
            if (client != null && client.IsRunning) await client.Shutdown();
            if (host != null && host.IsRunning) await host.Shutdown();
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj); temporary.Clear();
            Directory.CreateDirectory("Temp/FocusSlotsVerification");
            File.WriteAllLines("Temp/FocusSlotsVerification/selection-host-client.txt", report);
        }
    }
}
#endif
