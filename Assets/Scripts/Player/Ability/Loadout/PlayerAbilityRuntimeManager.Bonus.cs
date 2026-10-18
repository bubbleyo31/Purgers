using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Purgers.Progression;
using Purgers.GameFlow.Control;

public partial class PlayerAbilityRuntimeManager
{
    [Header("真難抉擇")]
    [SerializeField, Min(.1f), Tooltip("成功額外施放後到下次抽取的秒數。預設 10；封鎖只保留一次待施放，不受冷卻減免。")]
    private float indecisionInterval = 10f;
    [Networked] private TickTimer BonusNextTimer { get; set; }
    [Networked] private NetworkObject PendingBonusObject { get; set; }
    [Networked, Capacity(16)] private NetworkDictionary<int, NetworkObject> BonusRuntimeObjects => default;
    [Networked] private int BonusSequence { get; set; }
    private readonly List<int> bonusCleanup = new List<int>();
    private void TickBonusAbility(NetInput input)
    {
        if (Object == null || !Object.HasStateAuthority) return;
        bonusCleanup.Clear();
        foreach (var pair in BonusRuntimeObjects)
        {
            var runtime = pair.Value != null && pair.Value.IsValid ? pair.Value.GetComponent<PlayerAbilityRuntime>() : null;
            if (runtime == null || !runtime.TryGetModule<IPlayerAbilityHudState>(out var hud) || !hud.IsAbilityActive) bonusCleanup.Add(pair.Key);
        }
        foreach (int key in bonusCleanup)
        {
            var obj = BonusRuntimeObjects[key]; BonusRuntimeObjects.Remove(key);
            if (obj != null && obj.IsValid) Runner.Despawn(obj);
        }
        var owner = GetComponent<Player>();
        if (!PlayerGrowth.Has(owner, PlayerGrowthKind.Indecision))
        {
            BonusNextTimer = TickTimer.None;
            if (PendingBonusObject != null && PendingBonusObject.IsValid) Runner.Despawn(PendingBonusObject);
            PendingBonusObject = null; return;
        }
        if (!BonusNextTimer.IsRunning) { BonusNextTimer = TickTimer.CreateFromSeconds(Runner, indecisionInterval); return; }
        if (!BonusNextTimer.Expired(Runner) || BonusRuntimeObjects.Count >= 16) return;
        var gate = owner.GetComponent<PlayerActionGate>();
        if (owner.Health == null || !owner.Health.IsAlive || PlayerControlLocks.BlocksGameplay(input.BlockedControls) ||
            (gate != null && (gate.IsBlocked(PlayerActionBlockMask.ActiveAbility) || gate.IsBlocked(PlayerActionBlockMask.QuickAction)))) return;
        var logic = GameLogic.GetPrimaryForRunner(Runner);
        if (logic == null) return;
        if (PendingBonusObject != null && PendingBonusObject.IsValid &&
            !PlayerAbilityQualification.IsAllowed(owner, PendingBonusObject.GetComponent<PlayerAbilityRuntime>().Definition))
        { Runner.Despawn(PendingBonusObject); PendingBonusObject = null; }
        if (PendingBonusObject == null || !PendingBonusObject.IsValid)
        {
            var definition = logic.DrawBonusFocusAbility(owner, ++BonusSequence);
            if (definition == null) return;
            PendingBonusObject = SpawnAbilityRuntime(new SpawnPlanEntry { SlotIndex = -1, Definition = definition });
            if (PendingBonusObject == null) return;
            PendingBonusObject.GetComponent<PlayerAbilityRuntime>().IsBonusCast = true;
        }
        var candidate = PendingBonusObject.GetComponent<PlayerAbilityRuntime>();
        candidate.RefreshProfessionAvailabilityNow();
        bool started;
        if (candidate.TryGetModule<PlayerActiveAbilityBase>(out var active)) started = active.TryAutomaticActivation(input);
        else
        {
            var automaticInput = input;
            automaticInput.Buttons.Set(InputButton.Ability1, true);
            candidate.Simulate(automaticInput, default);
            started = candidate.TryGetModule<IPlayerAbilityHudState>(out var hud) && hud.IsAbilityActive;
        }
        if (!started) return;
        for (int i = 0; i < 16; i++)
            if (!BonusRuntimeObjects.ContainsKey(i)) { BonusRuntimeObjects.Add(i, PendingBonusObject); break; }
        PendingBonusObject = null;
        BonusNextTimer = TickTimer.CreateFromSeconds(Runner, indecisionInterval);
    }
    public T GetActiveBonusModule<T>() where T : class
    {
        if (Object == null || !Object.IsValid) return null;
        foreach (var pair in BonusRuntimeObjects)
        {
            var runtime = pair.Value != null && pair.Value.IsValid ? pair.Value.GetComponent<PlayerAbilityRuntime>() : null;
            if (runtime != null && runtime.IsAvailableForCurrentProfession && runtime.TryGetModule<IPlayerAbilityHudState>(out var hud) &&
                hud.IsAbilityActive && runtime.TryGetModule<T>(out var module)) return module;
        }
        return null;
    }
    public void CollectBonusRuntimes(List<PlayerAbilityRuntime> destination)
    {
        if (Object == null || !Object.IsValid) return;
        foreach (var pair in BonusRuntimeObjects)
            if (pair.Value != null && pair.Value.IsValid) destination.Add(pair.Value.GetComponent<PlayerAbilityRuntime>());
    }
    private void DespawnBonusRuntimes(NetworkRunner runner)
    {
        if (PendingBonusObject != null && PendingBonusObject.IsValid && PendingBonusObject.HasStateAuthority) runner.Despawn(PendingBonusObject);
        foreach (var pair in BonusRuntimeObjects)
            if (pair.Value != null && pair.Value.IsValid && pair.Value.HasStateAuthority) runner.Despawn(pair.Value);
        BonusRuntimeObjects.Clear(); PendingBonusObject = null;
    }
}
