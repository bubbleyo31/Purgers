using UnityEngine;

/// <summary>可選使用條件；預設陣列為空。不得取代既有死亡、控制鎖與權威檢查。</summary>
public interface IPlayerAbilityUseCondition
{
    bool CanActivate(Player owner);
}

/// <summary>保留未來強化設定的查詢契約。本版不選取強化、不消耗動能。</summary>
public interface IPlayerAbilityEnhancementProvider
{
    bool HasEnhancedVersion { get; }
}

public interface IPlayerAbilityHudState
{
    float CooldownRemainingSeconds { get; }
    float ActiveRemainingSeconds { get; }
    bool IsAbilityActive { get; }
    bool IsUsable { get; }
    PlayerAbilityTimedHudState TimedHudState { get; }
}

public interface IPlayerExperienceMultiplier
{
    float ExperienceMultiplier { get; }
}

public enum PlayerActiveAbilityPhase : byte
{
    Ready = 0, Casting = 1, Active = 2, AwaitingPickup = 3, Armed = 4, Dashing = 5
}
