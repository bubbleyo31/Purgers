using System;
using Fusion;
using UnityEngine;
using Purgers.GameFlow.Control;

/// <summary>獨立 E 技能的施放階段；技能結果只由 State Authority 執行。</summary>
[RequireComponent(typeof(PlayerAbilityRuntime))]
public abstract class PlayerActiveAbilityBase : NetworkBehaviour, IPlayerAbilityRuntimeModule, IPlayerAbilityHudState
{
    [Header("共用 E 技能設定")]
    [SerializeField, Min(0f), Tooltip("冷卻秒數；各技能在自己的結束事件才開始倒數。0 代表無冷卻。")]
    private float cooldownSeconds = 12f;
    [SerializeField, Min(0f), Tooltip("按 E 到正式施放之間的動畫延遲，秒；0 立即施放。由 Fusion 計時，不依賴動畫事件。")]
    private float castDelaySeconds = 0.25f;
    [SerializeField, Tooltip("可選限制元件，需實作 IPlayerAbilityUseCondition；預設空陣列，不新增地面或空中限制。")]
    private MonoBehaviour[] useConditions = Array.Empty<MonoBehaviour>();

#if UNITY_EDITOR
    [Header("技能範圍 Gizmos（僅 Editor 顯示）")]
    [SerializeField] private ActiveAbilityGizmoSettings rangeGizmos = new ActiveAbilityGizmoSettings();
    public ActiveAbilityGizmoSettings RangeGizmos => rangeGizmos ?? (rangeGizmos = new ActiveAbilityGizmoSettings());
    // Owner 由既有 Runtime.Spawned 綁定；Prefab/尚未綁定時不查任何 Networked。
    public Player GizmoOwner => Application.isPlaying && Ready ? Owner : null;
#endif

    [Networked] public PlayerActiveAbilityPhase Phase { get; protected set; }
    [Networked] protected TickTimer PhaseTimer { get; set; }
    [Networked] protected float PhaseDurationSeconds { get; set; }
    [Networked] protected TickTimer CooldownTimer { get; set; }
    [Networked] public int ActivationSequence { get; protected set; }

    protected Player Owner { get; private set; }
    protected bool ProfessionAvailable { get; private set; } = true;
    protected bool Ready => Object != null && Object.IsValid && Owner != null &&
        Owner.Object != null && Owner.Object.IsValid;
    protected bool Authority => Ready && Object.HasStateAuthority;
    public PlayerAbilityCategory AbilityCategory => PlayerAbilityCategory.GrappleFocus;
    public bool HasReservedEnhancement => GetComponent<PlayerAbilityRuntime>().Definition != null && GetComponent<PlayerAbilityRuntime>().Definition.HasReservedEnhancement;
    public float CooldownRemainingSeconds => Ready ? CooldownTimer.RemainingTime(Runner) ?? 0f : 0f;
    public float ActiveRemainingSeconds => Ready ? PhaseTimer.RemainingTime(Runner) ?? 0f : 0f;
    public bool IsAbilityActive => Ready && Phase != PlayerActiveAbilityPhase.Ready;
    public PlayerAbilityTimedHudState TimedHudState => Ready
        ? ActiveAbilityHudRules.Create(Phase, ActiveRemainingSeconds, PhaseDurationSeconds) : default;
    public virtual bool IsUsable => Ready && ProfessionAvailable && Owner.Health != null &&
        Owner.Health.IsAlive && PlayerAbilityQualification.IsAllowed(Owner, GetComponent<PlayerAbilityRuntime>().Definition);

    public virtual void BindOwnerPlayer(Player ownerPlayer) { Owner = ownerPlayer; }
    public virtual void SetProfessionAvailable(bool isAvailable)
    {
        ProfessionAvailable = isAvailable;
        if (!isAvailable && Authority && IsAbilityActive) FinishAbility();
    }
    public void SimulateAbility(NetInput input, NetworkButtons previousButtons)
    {
        if (!Authority) return;
        if ((input.BlockedControls & (PlayerControlMask.AllInput | PlayerControlMask.Gameplay)) != 0) return;
        if (!input.Buttons.WasPressed(previousButtons, InputButton.Ability1)) return;
        if (!IsUsable) return;
        var gate = Owner.GetComponent<PlayerActionGate>();
        if (gate != null && gate.IsBlocked(PlayerActionBlockMask.QuickAction)) return;
        if (Phase == PlayerActiveAbilityPhase.Armed) { OnArmedPress(input); return; }
        if (Phase != PlayerActiveAbilityPhase.Ready || !CooldownTimer.ExpiredOrNotRunning(Runner)) return;
        foreach (var condition in useConditions)
            if (condition != null && (!(condition is IPlayerAbilityUseCondition rule) || !rule.CanActivate(Owner))) return;
        if (!CanBegin(input)) return;
        ActivationSequence++;
        OnActivationAccepted(input);
        if (Phase != PlayerActiveAbilityPhase.Ready) return;
        Phase = PlayerActiveAbilityPhase.Casting;
        PhaseDurationSeconds = Mathf.Max(0f, castDelaySeconds);
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, castDelaySeconds));
        if (castDelaySeconds <= 0f) Activate();
    }
    public override void FixedUpdateNetwork()
    {
        if (!Authority) return;
        if (!IsUsable) { if (IsAbilityActive) FinishAbility(); return; }
        if (Phase == PlayerActiveAbilityPhase.Casting && PhaseTimer.ExpiredOrNotRunning(Runner)) Activate();
        else TickAbility();
    }
    protected virtual bool CanBegin(NetInput input) => true;
    protected virtual void OnActivationAccepted(NetInput input) { }
    protected virtual void OnArmedPress(NetInput input) { }
    protected abstract void Activate();
    protected virtual void TickAbility()
    {
        if (Phase == PlayerActiveAbilityPhase.Active && PhaseTimer.ExpiredOrNotRunning(Runner)) FinishAbility();
    }
    protected void BeginActive(float seconds)
    {
        Phase = PlayerActiveAbilityPhase.Active;
        PhaseDurationSeconds = Mathf.Max(0.01f, seconds);
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, seconds));
    }
    protected virtual void FinishAbility()
    {
        if (!Authority) return;
        Phase = PlayerActiveAbilityPhase.Ready;
        PhaseDurationSeconds = 0f;
        PhaseTimer = TickTimer.None;
        CooldownTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, cooldownSeconds));
    }
    protected int PlayerLevel
    {
        get
        {
            var logic = Ready ? GameLogic.GetPrimaryForRunner(Runner) : null;
            return logic != null && logic.TryGetPlayerExperience(Owner.Object.InputAuthority, out var state)
                ? Mathf.Max(1, state.Level) : 1;
        }
    }
}
