using Fusion;
using UnityEngine;

/// <summary>減傷後吸收的暫時護盾；不直接修改玩家生命。</summary>
public sealed class PlayerShieldAbility : PlayerActiveAbilityBase, IPlayerIncomingDamageModifier
{
    [Header("快速護盾")]
    [SerializeField, Range(0f, 1f), Tooltip("護盾占最大生命值比例；0.25 為 25%。再次賦予覆蓋，不疊加。")]
    private float maximumHealthFraction = 0.25f;
    [SerializeField, Min(0f), Tooltip("1 級持續秒數；預設 1.5 秒。施放時取樣等級。")]
    private float levelOneSeconds = 1.5f;
    [SerializeField, Min(0f), Tooltip("每升一級增加秒數；預設 0.5 秒。")]
    private float secondsPerLevel = 0.5f;
    [Networked] public float ShieldHealth { get; private set; }
    public float CurrentShield => Ready && Phase == PlayerActiveAbilityPhase.Active ? ShieldHealth : 0f;
    public int IncomingDamageModifierPriority => 1000;

    protected override void Activate()
    {
        ShieldHealth = Owner.Health.MaximumHealth * Mathf.Clamp01(maximumHealthFraction);
        BeginActive(ActiveAbilityRules.ShieldDuration(PlayerLevel, levelOneSeconds, secondsPerLevel));
    }
    public void ModifyIncomingDamage(ref DamageRequest request)
    {
        if (!Authority || Phase != PlayerActiveAbilityPhase.Active || PhaseTimer.ExpiredOrNotRunning(Runner)) return;
        float remaining = ShieldHealth;
        request.RequestedDamage = ActiveAbilityRules.AbsorbShield(request.RequestedDamage, ref remaining, out float blocked);
        ShieldHealth = remaining;
        request.BlockedDamage += blocked;
        if (ShieldHealth <= 0f) FinishAbility();
    }
    protected override void FinishAbility()
    {
        if (Authority) ShieldHealth = 0f;
        base.FinishAbility();
    }
}
