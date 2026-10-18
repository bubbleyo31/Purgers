using System;
using Fusion;
using UnityEngine;
using Purgers.Progression;

public partial class GameLogic
{
    [Networked, Capacity(16)] private NetworkDictionary<PlayerRef, PlayerGrowthState> PlayerGrowthStates => default;
    public PlayerGrowthState GetPlayerGrowth(PlayerRef player) => Object != null && Object.IsValid &&
        PlayerGrowthStates.TryGet(player, out var state) ? state : default;
    public float GetGrowthValue(PlayerRef player, PlayerGrowthKind kind)
    {
        int rank = GetPlayerGrowth(player).Rank(kind);
        var reward = rewardCatalog != null ? rewardCatalog.FindGrowth(kind) : null;
        return reward != null ? reward.GrowthValue(rank) : PlayerGrowthRules.Value(kind, rank);
    }
    public bool ConsumePassive(PlayerRef player, PlayerGrowthKind kind)
    {
        if (Object == null || !Object.HasStateAuthority) return false;
        var state = GetPlayerGrowth(player);
        if (!state.Consume(kind)) return false;
        PlayerGrowthStates.Set(player, state);
        ApplyGrowthResources(player, 0);
        return true;
    }
    public void LoseDownedPassives(PlayerRef player)
    {
        if (Object == null || !Object.HasStateAuthority) return;
        var state = GetPlayerGrowth(player); state.LoseDownedPassives();
        PlayerGrowthStates.Set(player, state); ApplyGrowthResources(player, 0);
    }
    private bool TryGrantGrowth(PlayerRef player, Player playerBehaviour, PlayerRewardDefinition reward, out string error)
    {
        error = string.Empty;
        var state = GetPlayerGrowth(player);
        if (!PlayerGrowthRules.CanOffer(state, reward.GrowthKind, PlayerAbilityQualification.HasRangedWeapon(playerBehaviour),
            PlayerAbilityQualification.HasGuardWeapon(playerBehaviour)))
        { error = "此屬性已滿階、被動已持有，或目前武器／前置資格不符。"; return false; }
        float oldHealth = GetGrowthValue(player, PlayerGrowthKind.MaximumHealth);
        if (!state.TryAcquire(reward.GrowthKind)) return false;
        PlayerGrowthStates.Set(player, state);
        ApplyGrowthResources(player, GetGrowthValue(player, PlayerGrowthKind.MaximumHealth) - oldHealth);
        return true;
    }
    private void ApplyGrowthResources(PlayerRef player, float healthDelta)
    {
        if (!Players.TryGet(player, out var owner) || owner == null) return;
        if (healthDelta > 0 && owner.Health != null && owner.Health.IsAlive) owner.Health.RestoreHealth(healthDelta, out _);
        var runtime = owner.ProfessionRuntimeManager != null ? owner.ProfessionRuntimeManager.CurrentRuntimeObject : null;
        if (runtime != null && runtime.IsValid)
        {
            runtime.GetComponent<AttackRifle>()?.RefreshGrowthMagazine();
            runtime.GetComponent<SupportSMG>()?.RefreshGrowthMagazine();
            runtime.GetComponent<TankGuardAbility>()?.RefreshGrowthCapacity();
        }
    }
    private void TickGrowthStateAuthority()
    {
        foreach (var pair in Players)
            if (pair.Value != null && pair.Value.Health != null) pair.Value.Health.TickGrowthStateAuthority();
    }
    public void RecordEquippedAbility(Player owner, PlayerAbilityRuntime runtime)
    {
        if (Object == null || !Object.HasStateAuthority || owner == null || runtime == null ||
            !PlayerRewardDrafts.TryGet(owner.Object.InputAuthority, out var draft) || rewardCatalog == null) return;
        foreach (var reward in rewardCatalog.Rewards)
        {
            if (reward == null || reward.AbilityDefinition != runtime.Definition) continue;
            if (reward.Category == RewardCategory.GrappleHit)
            { draft.EquippedGrappleHitId = reward.StableRewardId; draft.EquippedHitLevel = runtime.SkillLevel; }
            else
            { draft.EquippedGrappleFocusId = reward.StableRewardId; draft.EquippedFocusLevel = runtime.SkillLevel; }
            PlayerRewardDrafts.Set(owner.Object.InputAuthority, draft); return;
        }
    }
    public PlayerAbilityDefinition DrawBonusFocusAbility(Player owner, int sequence)
    {
        if (Object == null || !Object.HasStateAuthority || owner == null || rewardCatalog == null ||
            !TryGetPlayerExperience(owner.Object.InputAuthority, out var xp)) return null;
        var eligible = new System.Collections.Generic.List<PlayerRewardDefinition>();
        int weight = 0;
        foreach (var reward in rewardCatalog.Rewards)
            if (reward != null && reward.Category == RewardCategory.GrappleFocus && reward.IsImplementedAbilityReward &&
                reward.Weight > 0 && xp.Level >= reward.MinimumPlayerLevel && PlayerAbilityQualification.IsAllowed(owner, reward.AbilityDefinition))
            { eligible.Add(reward); weight += reward.Weight; }
        if (weight <= 0) return null;
        eligible.Sort((a,b) => StringComparer.Ordinal.Compare(a.StableRewardId,b.StableRewardId));
        ulong seed = RewardDraftSeed(owner.Object.InputAuthority, xp.Level, sequence ^ Runner.Tick.Raw);
        int pick = (int)(seed % (ulong)weight);
        foreach (var reward in eligible) { if (pick < reward.Weight) return reward.AbilityDefinition; pick -= reward.Weight; }
        return null;
    }
    private void WriteHostGrowth(GameSaveData save)
    {
        foreach (var entry in save.RunProgression.PlayerProgressionEntries)
            if (entry.StablePlayerId == GameSaveSchema.HostPlayerId)
            {
                var state = GetPlayerGrowth(Runner.LocalPlayer);
                entry.GrowthRanks = state.Ranks; entry.HeldPassiveBits = state.Passives;
                entry.DefenseGrowthRanks = state.DefenseRanks;
                if (PlayerRewardDrafts.TryGet(Runner.LocalPlayer, out var draft))
                { entry.EquippedHitLevel = draft.EquippedHitLevel; entry.EquippedFocusLevel = draft.EquippedFocusLevel; }
            }
    }
    private void ReadHostGrowth(PlayerRef player)
    {
        var state = default(PlayerGrowthState);
        if (player == Runner.LocalPlayer)
        {
            var context = Runner.GetComponent<GameSaveRuntimeContext>();
            if (context != null && context.HasWritableHostSave)
                foreach (var entry in context.ActiveSave.RunProgression.PlayerProgressionEntries)
                    if (entry.StablePlayerId == GameSaveSchema.HostPlayerId)
                        state = new PlayerGrowthState { Ranks = entry.GrowthRanks, Passives = entry.HeldPassiveBits,
                            DefenseRanks = entry.DefenseGrowthRanks };
        }
        PlayerGrowthStates.Set(player, state);
    }
}
