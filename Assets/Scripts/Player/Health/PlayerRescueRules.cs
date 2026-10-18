using UnityEngine;

/// <summary>倒地計時純規則；實際數值與狀態仍由 PlayerHealth 的 State Authority 擁有。</summary>
public static class PlayerRescueRules
{
    public static float Remaining(float remaining, float deltaTime, bool beingRescued) =>
        Mathf.Max(0f, remaining - (beingRescued ? 0f : Mathf.Max(0f, deltaTime)));
    public static bool RequiresExtraction(bool isDead) => !isDead;
    public static float RestoredHealth(float maximum, float fraction) => Mathf.Max(1f, maximum) * Mathf.Clamp01(fraction);
}
