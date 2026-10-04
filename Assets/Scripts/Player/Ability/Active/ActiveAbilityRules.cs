using UnityEngine;

/// <summary>主動技能共用數值規則；不依賴 Scene、輸入或網路生命週期。</summary>
public static class ActiveAbilityRules
{
    public static float ShieldDuration(int level, float baseSeconds, float perLevelSeconds) =>
        Mathf.Max(0f, baseSeconds) + (Mathf.Max(1, level) - 1) * Mathf.Max(0f, perLevelSeconds);

    public static float AbsorbShield(float incoming, ref float shield, out float blocked)
    {
        incoming = Mathf.Max(0f, incoming);
        shield = Mathf.Max(0f, shield);
        blocked = Mathf.Min(incoming, shield);
        shield -= blocked;
        return incoming - blocked;
    }

    public static float RicochetDamage(float damage, int bounces) =>
        Mathf.Max(0f, damage) * (1 << Mathf.Clamp(bounces, 0, 3));
    public static float PiercingDamage(float damage, int penetrations) =>
        Mathf.Max(0f, damage) * Mathf.Pow(0.75f, Mathf.Max(0, penetrations));
    public static float HealingAmount(int throwLevel, bool self) =>
        Mathf.Max(1, throwLevel) * (self ? 5f : 10f);
    public static int EqualShare(int total, int recipients, int ordinal) =>
        total <= 0 || recipients <= 0 || ordinal < 0 || ordinal >= recipients
            ? 0 : total / recipients + (ordinal < total % recipients ? 1 : 0);
}
