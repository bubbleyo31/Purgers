using System;
using Fusion;
using UnityEngine;
using Purgers.GameFlow.Control;

/// <summary>獨立 E 技能的施放階段；技能結果只由 State Authority 執行。</summary>
[RequireComponent(typeof(PlayerAbilityRuntime))]
public abstract class PlayerActiveAbilityBase : NetworkBehaviour, IPlayerAbilityRuntimeModule, IPlayerAbilityHudState, IPlayerAbilityCooldownSource
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
    [Networked] private float CooldownDuration { get; set; }
    public float CooldownDurationSeconds => Ready ? CooldownDuration : 0f;
    [Networked] public int ActivationSequence { get; protected set; }

    protected Player Owner { get; private set; }
    protected bool ProfessionAvailable { get; private set; } = true;
    protected bool Ready => Object != null && Object.IsValid && Owner != null &&
        Owner.Object != null && Owner.Object.IsValid;
    protected bool Authority => Ready && Object.HasStateAuthority;
    public PlayerAbilityCategory AbilityCategory => PlayerAbilityCategory.GrappleFocus;
    public bool HasReservedEnhancement => GetComponent<PlayerAbilityRuntime>().Definition != null && GetComponent<PlayerAbilityRuntime>().Definition.HasReservedEnhancement;
    public float CooldownRemainingSeconds => Ready ? CooldownTimer.RemainingTime(Runner) ?? 0f : 0f;
    public bool HudCanEndActive => Ready && Phase == PlayerActiveAbilityPhase.Active && CanEndOnActivePress;
    public virtual bool HudCanRecast => false;
    public bool HudCanPress
    {
        get
        {
            if (!IsUsable || (Owner.GetComponent<PlayerActionGate>()?.IsBlocked(PlayerActionBlockMask.QuickAction) ?? false)) return false;
            if (Phase == PlayerActiveAbilityPhase.Armed) return HudCanRecast;
            if (Phase == PlayerActiveAbilityPhase.Active) return CanEndOnActivePress;
            if (Phase != PlayerActiveAbilityPhase.Ready || CooldownRemainingSeconds > 0f) return false;
            foreach (var condition in useConditions)
                if (condition != null && (!(condition is IPlayerAbilityUseCondition rule) || !rule.CanActivate(Owner))) return false;
            // Current CanBegin overrides only read qualification/configuration; never submit an activation from HUD.
            return CanBegin(default);
        }
    }
    public float ActiveRemainingSeconds => Ready ? PhaseTimer.RemainingTime(Runner) ?? 0f : 0f;
    public bool IsAbilityActive => Ready && Phase != PlayerActiveAbilityPhase.Ready;
    public PlayerAbilityTimedHudState TimedHudState => Ready
        ? ActiveAbilityHudRules.Create(Phase, ActiveRemainingSeconds, PhaseDurationSeconds) : default;
    public virtual bool IsUsable => Ready && ProfessionAvailable && Owner.Health != null &&
        Owner.Health.IsAlive &&
        !(Owner.GetComponent<PlayerActionGate>()?.IsBlocked(PlayerActionBlockMask.ActiveAbility) ?? false) &&
        PlayerAbilityQualification.IsAllowed(Owner, GetComponent<PlayerAbilityRuntime>().Definition);

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
        if (Phase == PlayerActiveAbilityPhase.Active)
        {
            // 自願結束不算新施放，不重播後座力，也不重設作用時間。
            if (CanEndOnActivePress) FinishAbility();
            return;
        }
        if (Phase == PlayerActiveAbilityPhase.Armed)
        {
            Vector2? aim = GetReleaseAim();
            if (OnArmedPress(input))
                GetComponent<PlayerAbilityRuntime>().ApplyAbilityRecoil(PlayerAbilityRecoilEvent.AfterCast, aim);
            return;
        }
        if (Phase != PlayerActiveAbilityPhase.Ready || !CooldownTimer.ExpiredOrNotRunning(Runner)) return;
        foreach (var condition in useConditions)
            if (condition != null && (!(condition is IPlayerAbilityUseCondition rule) || !rule.CanActivate(Owner))) return;
        if (!CanBegin(input)) return;
        ActivationSequence++;
        OnActivationAccepted(input);
        // 成功受理是準備事件；待命型技能須等後續主動操作成功才送出實際施放事件。
        GetComponent<PlayerAbilityRuntime>().ApplyAbilityRecoil(PlayerAbilityRecoilEvent.BeforeCast);
        if (Phase != PlayerActiveAbilityPhase.Ready) return;
        Phase = PlayerActiveAbilityPhase.Casting;
        PhaseDurationSeconds = Mathf.Max(0f, castDelaySeconds);
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, castDelaySeconds));
        if (castDelaySeconds <= 0f) CompleteCast();
    }
    public bool TryAutomaticActivation(NetInput input)
    {
        if (!Authority || !IsUsable) return false;
        input.Buttons.Set(InputButton.Ability1, true);
        if (Phase != PlayerActiveAbilityPhase.Armed)
        {
            int before = ActivationSequence;
            SimulateAbility(input, default);
            if (Phase != PlayerActiveAbilityPhase.Armed) return ActivationSequence != before;
        }
        // 待命型（閃現）直接使用這個 tick 的方向；失敗保留一次待施放。
        Vector2? aim = GetReleaseAim();
        bool released = OnArmedPress(input);
        if (released) GetComponent<PlayerAbilityRuntime>().ApplyAbilityRecoil(PlayerAbilityRecoilEvent.AfterCast, aim);
        return released;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Authority) return;
        if (!IsUsable) { if (IsAbilityActive) FinishAbility(); return; }
        if (Phase == PlayerActiveAbilityPhase.Casting && PhaseTimer.ExpiredOrNotRunning(Runner)) CompleteCast();
        else TickAbility();
    }
    /// <summary>先讓正式技能取樣本次準星，再追加後座力，避免這次攻擊被自身視角後座力踢偏。</summary>
    private void CompleteCast()
    {
        Vector2? aim = GetReleaseAim();
        if (Activate())
            GetComponent<PlayerAbilityRuntime>().ApplyAbilityRecoil(PlayerAbilityRecoilEvent.AfterCast, aim);
    }
    private Vector2? GetReleaseAim()
    {
        var kcc = Owner.Movement != null ? Owner.Movement.KCC : null;
        return kcc != null ? new Vector2(kcc.Data.LookPitch, kcc.Data.LookYaw) : (Vector2?)null;
    }
    /// <summary>預設不允許再次按下結束；只有明確支援的技能覆寫，結束仍走原 FinishAbility。</summary>
    protected virtual bool CanEndOnActivePress => false;
    protected virtual bool CanBegin(NetInput input) => true;
    /// <summary>僅處理按下當刻的受理／待命；有前搖的攻擊不得在此快取射出位置或方向。</summary>
    protected virtual void OnActivationAccepted(NetInput input) { }
    /// <returns>只有真正提交後續施放才回傳 true；阻擋、逾時、取消回傳 false。</returns>
    protected virtual bool OnArmedPress(NetInput input) => false;
    /// <summary>前搖到期後的權威結果入口。投擲／飛行物／射線必須在此讀取當前 Owner KCC 位置與準星，不能沿用按鍵時快照；Spawn 與命中查詢使用同一份射出資料。</summary>
    /// <returns>正式效果已建立才回傳 true；不要求命中，但失敗返回不得產生施放後座力。</returns>
    protected abstract bool Activate();
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
        CooldownDuration = Purgers.Progression.PlayerGrowth.Cooldown(Owner, AbilityCategory, cooldownSeconds);
        CooldownTimer = TickTimer.CreateFromSeconds(Runner, CooldownDuration);
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
