using UnityEngine;

/// <summary>權威技能階段的本機顯示快照；不擁有時鐘、不修改施法或效果結果。</summary>
public readonly struct PlayerAbilityTimedHudState
{
    public PlayerActiveAbilityPhase Phase { get; }
    public float RemainingSeconds { get; }
    public float DurationSeconds { get; }
    public bool IsVisible => DurationSeconds > 0f && RemainingSeconds > 0f &&
        (Phase == PlayerActiveAbilityPhase.Casting || Phase == PlayerActiveAbilityPhase.Armed ||
         Phase == PlayerActiveAbilityPhase.Active);
    public float NormalizedRemaining => IsVisible ? Mathf.Clamp01(RemainingSeconds / DurationSeconds) : 0f;
    public string StateLabel => Phase == PlayerActiveAbilityPhase.Casting ? "施放準備" :
        Phase == PlayerActiveAbilityPhase.Armed ? "待命" : Phase == PlayerActiveAbilityPhase.Active ? "效果持續" : string.Empty;

    public PlayerAbilityTimedHudState(PlayerActiveAbilityPhase phase, float remaining, float duration)
    {
        Phase = phase;
        RemainingSeconds = remaining;
        DurationSeconds = duration;
    }
}

public static class ActiveAbilityHudRules
{
    public static PlayerAbilityTimedHudState Create(PlayerActiveAbilityPhase phase, float remaining, float duration)
    {
        if (float.IsNaN(remaining) || float.IsInfinity(remaining) ||
            float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
            return default;
        return new PlayerAbilityTimedHudState(phase, Mathf.Clamp(remaining, 0f, duration), duration);
    }
}
