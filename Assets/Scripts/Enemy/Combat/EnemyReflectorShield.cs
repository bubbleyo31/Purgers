using Fusion;
using UnityEngine;

/// <summary>全身不可擊穿護盾。只有自己的反彈彈觸發背部破綻；共用生命與受控 owner。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor))]
public sealed partial class EnemyReflectorShield : NetworkBehaviour, IDamageRequestModifier, IEnemyAbilityKnockbackCondition
{
    [Header("反彈破綻")]
    [SerializeField, Min(.02f), Tooltip("反彈彈命中後暈眩與背盾開啟秒數。預設 2；開始時取樣，期間改值不影響本次。")]
    private float stunSeconds = 2f;
    [SerializeField, Range(1f, 180f), Tooltip("背後可受傷的水平完整角度。預設 120；正面與正側面仍被擋。以傷害進入方向判定。")]
    private float rearOpeningAngle = 120f;
    [Header("護盾狀態呈現（不判定傷害）")]
    [SerializeField, Tooltip("前方護盾模型 Renderer；完整藍色、破綻期間橘色。可留空。")]
    private Renderer frontShield;
    [SerializeField, Tooltip("背方護盾模型 Renderer；破綻期間隱藏。可留空。")]
    private Renderer rearShield;
    [SerializeField, Tooltip("左右上下護盾 Renderer；完整時藍色、破綻期間仍顯示並轉橘色。只隱藏 Rear Shield。可留空。")]
    private Renderer[] additionalShields = System.Array.Empty<Renderer>();
    [SerializeField, Tooltip("完整護盾顏色；只改本機 MaterialPropertyBlock。")]
    private Color protectedColor = new Color(.1f, .6f, 1f, 1f);
    [SerializeField, Tooltip("暈眩時保留的五面護盾顏色；背盾同時隱藏。")]
    private Color openingColor = new Color(1f, .45f, .05f, 1f);
    [Networked] public TickTimer OpeningTimer { get; private set; }
    [Networked] private float OpeningHudSpan { get; set; }
    public StatusEffectSnapshot OpeningStatus => IsOpening
        ? StatusEffectSnapshot.FromTimer(StatusEffectKind.Vulnerability, Runner, OpeningTimer, OpeningHudSpan) : default;
    private bool ready;
    private EnemyActor actor;
    private MaterialPropertyBlock properties;
    public bool IsOpening => ready && Object != null && Object.IsValid && !OpeningTimer.ExpiredOrNotRunning(Runner);
    public float StunSeconds => Mathf.Max(.02f, stunSeconds);

    private void Awake() { actor = GetComponent<EnemyActor>(); }
    public override void Spawned()
    {
        ready = true;
        if(!ShotThreatTargets.Contains(this))ShotThreatTargets.Add(this);
        actor = GetComponent<EnemyActor>();
        if (Object.HasStateAuthority) OpeningTimer = TickTimer.None;
        Present();
    }
    public override void Despawned(NetworkRunner runner, bool hasState) { ready = false; ShotThreatTargets.Remove(this); }
    public override void FixedUpdateNetwork()
    {
        if (!ready || !Object.HasStateAuthority) return;
        if (!actor.IsAlive) { OpeningTimer = TickTimer.None; return; }
        if (actor.StateController.IsTimeFrozen)
            OpeningTimer = ActiveControlRules.PauseTimerForOneTick(OpeningTimer, Runner);
    }
    public bool TryReceiveReflectedProjectile(NetworkObject originalShooter)
    {
        if (!ready || !Object.HasStateAuthority || originalShooter != Object || !actor.IsAlive || IsOpening) return false;
        if (!actor.StateController.TryBeginTimedStun(StunSeconds)) return false;
        OpeningTimer = TickTimer.CreateFromSeconds(Runner, StunSeconds);
        OpeningHudSpan = OpeningTimer.RemainingTime(Runner) ?? 0f;
        return true;
    }
    public bool CanBeKnockedBack(EnemyStateController state) => !IsOpening;

    public void ModifyDamageRequest(ref DamageRequest request)
    {
        if (EnemyReflectorRules.AllowsDamage(IsOpening, transform.forward, request.HitDirection, rearOpeningAngle)) return;
        request.BlockedDamage += Mathf.Max(0f, request.RequestedDamage);
        request.RequestedDamage = 0f;
    }
    public override void Render() { if (ready) Present(); }
    private void Present()
    {
        bool alive = actor != null && actor.IsAlive;
        bool opening = IsOpening;
        if (frontShield != null) { frontShield.enabled = alive; SetColor(frontShield, opening ? openingColor : protectedColor); }
        if (additionalShields != null)
            foreach (var shield in additionalShields)
                if (shield != null) { shield.enabled = alive; SetColor(shield, opening ? openingColor : protectedColor); }
        if (rearShield != null) { rearShield.enabled = alive && !opening; SetColor(rearShield, protectedColor); }
    }
    private void SetColor(Renderer target, Color color)
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
        target.SetPropertyBlock(properties);
    }
}
