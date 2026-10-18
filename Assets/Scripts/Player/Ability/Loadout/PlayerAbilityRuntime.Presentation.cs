using UnityEngine;

/// <summary>只讀本次權威冷卻的分母，不以開窗時剩餘秒數重建。</summary>
public interface IPlayerAbilityCooldownSource
{
    float CooldownRemainingSeconds { get; }
    float CooldownDurationSeconds { get; }
}
public readonly struct PlayerAbilityPresentation
{
    public readonly float Remaining, Duration;
    public readonly bool Active, Usable;
    public PlayerAbilityPresentation(float remaining, float duration, bool active, bool usable)
    { Remaining = Mathf.Max(0, remaining); Duration = Mathf.Max(0, duration); Active = active; Usable = usable; }
}
public partial class PlayerAbilityRuntime
{
    public bool TryReadPresentation(out PlayerAbilityPresentation value)
    {
        value = default;
        if (!spawned || Object == null || !Object.IsValid || !moduleBound || IsWorldLoot || IsBonusCast) return false;
        var source = module as IPlayerAbilityCooldownSource;
        var active = module as IPlayerAbilityHudState;
        float remaining = source != null ? source.CooldownRemainingSeconds : active != null ? active.CooldownRemainingSeconds : 0;
        float duration = source != null ? source.CooldownDurationSeconds : 0;
        if (TransferCooldownRemaining > remaining) { remaining = TransferCooldownRemaining; duration = TransferCooldownDuration; }
        value = new PlayerAbilityPresentation(remaining, duration, (active != null && active.IsAbilityActive) || (module is TankAirDashAbility dash && dash.IsDashing),
            IsAvailableForCurrentProfession && (active == null || active.IsUsable));
        return true;
    }
}
