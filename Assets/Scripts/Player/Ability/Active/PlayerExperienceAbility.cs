using UnityEngine;

/// <summary>只增加施放者收到的經驗，代價為承傷加倍。</summary>
public sealed class PlayerExperienceAbility : PlayerActiveAbilityBase, IPlayerIncomingDamageModifier, IPlayerExperienceMultiplier
{
    [Header("經驗增加")]
    [SerializeField, Min(0.01f), Tooltip("增益與承傷代價持續秒數；預設 5 秒，結束才開始冷卻。")]
    private float durationSeconds = 5f;
    public float ExperienceMultiplier => IsAbilityActive && Phase == PlayerActiveAbilityPhase.Active && ActiveRemainingSeconds > 0f ? 2f : 1f;
    public int IncomingDamageModifierPriority => 0;
    protected override void Activate() { BeginActive(durationSeconds); }
    public void ModifyIncomingDamage(ref DamageRequest request)
    {
        if (Authority && ExperienceMultiplier > 1f) request.RequestedDamage *= 2f;
    }
}
