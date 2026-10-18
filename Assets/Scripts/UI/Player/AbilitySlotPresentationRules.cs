using UnityEngine;

public enum AbilitySlotVisualPhase { Unavailable, Ready, Cooling, Casting, Recast, Active, Awaiting }

/// <summary>技能槽唯讀顯示資料。時間來自技能權威快照；不決定技能能否執行。</summary>
public struct AbilitySlotPresentationState
{
    public AbilitySlotVisualPhase Phase;
    public float CooldownRemaining, CooldownDuration, PhaseRemaining, PhaseDuration;
    public bool CanPress, CanEnd;
    public bool Cooling => CooldownRemaining > 0f;
    public float CooldownRatio => AbilitySlotPresentationRules.RemainingRatio(CooldownRemaining, CooldownDuration);
    public float PhaseRatio => AbilitySlotPresentationRules.RemainingRatio(PhaseRemaining, PhaseDuration);
    public string Label => Phase == AbilitySlotVisualPhase.Recast ? (CanPress ? "再次施放" : "待命") :
        Phase == AbilitySlotVisualPhase.Casting ? "施放準備" :
        Phase == AbilitySlotVisualPhase.Active ? (CanEnd && CanPress ? "再次按下結束" : "效果持續") :
        Phase == AbilitySlotVisualPhase.Awaiting ? "等待拾取" : string.Empty;
}

public static class AbilitySlotPresentationRules
{
    public static float RemainingRatio(float remaining, float duration) =>
        float.IsNaN(remaining) || float.IsInfinity(remaining) || float.IsNaN(duration) ||
        float.IsInfinity(duration) || duration <= 0f ? 0f : Mathf.Clamp01(remaining / duration);

    public static bool ShouldFlash(bool wasCooling, bool cooling, bool usable) => wasCooling && !cooling && usable;
}

/// <summary>僅持有本機完成閃光；換裝、換觀戰對象或隱藏後不補播舊事件。</summary>
public sealed class AbilitySlotReadyPulse
{
    private object source;
    private bool observed, wasCooling;
    private float startedAt = float.NegativeInfinity;

    public void Reset() { source = null; observed = wasCooling = false; startedAt = float.NegativeInfinity; }

    public float Sample(object identity, bool cooling, bool ready, float now, float duration)
    {
        if (!ReferenceEquals(source, identity)) { Reset(); source = identity; }
        if (observed && AbilitySlotPresentationRules.ShouldFlash(wasCooling, cooling, ready)) startedAt = now;
        if (!ready || cooling) startedAt = float.NegativeInfinity;
        observed = true;
        wasCooling = cooling;
        return duration > 0f ? Mathf.Clamp01(1f - (now - startedAt) / duration) : 0f;
    }
}
