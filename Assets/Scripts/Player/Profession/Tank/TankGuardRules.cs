using System;

/// <summary>既有電鋸格檔使用的純數值規則；不持有狀態、不自行計時或提交傷害。</summary>
public static class TankGuardRules
{
    public static float Reduction(float baseline, float bonus) => Math.Max(0, Math.Min(1, baseline + bonus));
    public static float Capacity(float baseline, float bonus) => Math.Max(1, baseline + Math.Max(0, bonus));
    public static float BlockedDamage(float incoming, float reduction, float stamina) =>
        Math.Min(Math.Max(0, incoming) * Reduction(reduction, 0), Math.Max(0, stamina));
    public static float Recovery(float current, float maximum, float fractionPerSecond, float seconds) =>
        Math.Min(maximum, Math.Max(0, current) + maximum * Math.Max(0, fractionPerSecond) * Math.Max(0, seconds));
    /// <summary>升階只補新增容量；破防中保持零，不提前解除破防。減少上限時僅截斷。</summary>
    public static float ResizeStamina(float current, float oldMaximum, float newMaximum, bool broken) =>
        broken ? 0 : Math.Min(newMaximum, Math.Max(0, current) + Math.Max(0, newMaximum - oldMaximum));
}
