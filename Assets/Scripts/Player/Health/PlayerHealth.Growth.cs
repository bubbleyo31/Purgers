using Fusion;
using UnityEngine;
using Purgers.Progression;
using Purgers.GameFlow.Control;

public partial class PlayerHealth
{
    [Networked] private TickTimer NotTodayTimer { get; set; }
    [Networked] private TickTimer RegenerationWait { get; set; }
    [Networked] private NetworkBool RegenerationInitialized { get; set; }
    [Networked] public float SelfRescueElapsed { get; private set; }
    [Header("一次性保命被動")]
    [SerializeField, Min(0f), Tooltip("不是今天成功攔截致命傷後的無敵秒數。預設 3；墜出地圖不觸發。")]
    private float notTodayInvulnerabilitySeconds = 3f;
    [SerializeField, Range(0f, 1f), Tooltip("不是今天觸發後恢復至最大生命的比例。預設 0.25。")]
    private float notTodayHealthFraction = .25f;
    [SerializeField, Min(.1f), Tooltip("我自己來：倒地按住 E 自救秒數。預設 10；中斷歸零，成功才消耗。")]
    private float selfRescueDuration = 10f;
    private Player growthOwner;
    private Player GrowthOwner => growthOwner != null ? growthOwner : growthOwner = GetComponent<Player>();
    public bool IsNotTodayInvulnerable => IsRescueReady && NotTodayTimer.IsRunning && !NotTodayTimer.Expired(Runner);
    public float NotTodayRemainingSeconds => IsRescueReady ? Mathf.Max(0f, NotTodayTimer.RemainingTime(Runner) ?? 0f) : 0f;
    public float NotTodayDurationSeconds => Mathf.Max(0f, notTodayInvulnerabilitySeconds);
    public bool CanSelfRescue => IsRescueReady && IsDowned && !IsDead && PlayerGrowth.Has(GrowthOwner, PlayerGrowthKind.SelfRescue);
    public float SelfRescueDuration => Mathf.Max(.1f, selfRescueDuration);
    public float SelfRescueProgress => IsRescueReady ? Mathf.Clamp01(SelfRescueElapsed / SelfRescueDuration) : 0;
    public bool IsSelfRescuing => IsRescueReady && SelfRescueElapsed > 0;
    public void NotifyGrowthCombat()
    {
        if (!IsRescueReady || !Object.HasStateAuthority) return;
        float delay = PlayerGrowth.Value(GrowthOwner, PlayerGrowthKind.RegenerationDelay);
        if (delay <= 0f)
        {
            RegenerationWait = TickTimer.None;
            RegenerationInitialized = false;
            return;
        }
        RegenerationWait = TickTimer.CreateFromSeconds(Runner, delay);
        RegenerationInitialized = true;
    }
    internal void TickGrowthStateAuthority()
    {
        if (!IsRescueReady || !Object.HasStateAuthority || !IsAlive) return;
        float delay = PlayerGrowth.Value(GrowthOwner, PlayerGrowthKind.RegenerationDelay);
        if (delay <= 0) { RegenerationInitialized = false; return; }
        if (!RegenerationInitialized) NotifyGrowthCombat();
        if (GetInput(out NetInput input) && !PlayerControlLocks.BlocksCombat(input.BlockedControls) &&
            (input.Buttons.IsSet(InputButton.Fire) || input.Buttons.IsSet(InputButton.Ability1) || input.Buttons.IsSet(InputButton.QuickAction)))
            NotifyGrowthCombat();
        if (RegenerationWait.ExpiredOrNotRunning(Runner) && CurrentHealth < MaximumHealth)
            RestoreHealth(PlayerGrowth.Value(GrowthOwner, PlayerGrowthKind.RegenerationRate) * Runner.DeltaTime, out _);
    }
    private bool TryPreventLethalDamage()
    {
        var logic = GameLogic.GetPrimaryForRunner(Runner);
        if (logic == null || !logic.ConsumePassive(Object.InputAuthority, PlayerGrowthKind.NotToday)) return false;
        CurrentHealth = MaximumHealth * notTodayHealthFraction;
        NotTodayTimer = TickTimer.CreateFromSeconds(Runner, notTodayInvulnerabilitySeconds);
        GetComponent<PlayerActionGate>()?.ClearNegativeControlEffects();
        return true;
    }
    private bool TickSelfRescue(bool hasTeammateRescuer)
    {
        bool held = CanSelfRescue && !hasTeammateRescuer && GetInput(out NetInput input) &&
            !PlayerControlLocks.BlocksCombat(input.BlockedControls) && input.Buttons.IsSet(InputButton.Ability1);
        if (!held) { SelfRescueElapsed = 0; return false; }
        SelfRescueElapsed += Runner.DeltaTime;
        RescueEConsumed = true;
        if (SelfRescueElapsed + .00001f >= SelfRescueDuration)
        {
            var logic = GameLogic.GetPrimaryForRunner(Runner);
            if (logic != null && logic.ConsumePassive(Object.InputAuthority, PlayerGrowthKind.SelfRescue)) FinishRescueStateAuthority();
        }
        return true;
    }
    private void FinishRescueStateAuthority()
    {
        ClearRescuer(); SelfRescueElapsed = 0;
        IsDowned = false;
        CurrentHealth = PlayerRescueRules.RestoredHealth(MaximumHealth, rescuedHealthFraction);
        DownedRemainingSeconds = 0; NoHealthyMemberTimer = TickTimer.None;
        PreviousAggroRecoveryTimer = TickTimer.CreateFromSeconds(Runner, previousAggroDelaySeconds);
        NotifyGrowthCombat();
    }
}
