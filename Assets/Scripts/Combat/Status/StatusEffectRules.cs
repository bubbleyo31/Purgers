using System;

/// <summary>固定顯示順位；不改動任何異常的遊戲效果。</summary>
public enum StatusEffectKind { Stun, Root, BulletTime, Vulnerability, Mark, Captured, GrappleBlocked, Slow }

public static class StatusEffectRules
{
    public static float Ratio(float remaining, float span)
    {
        if (!Finite(remaining) || !Finite(span) || remaining <= 0f || span <= 0f) return 0f;
        return Math.Min(1f, remaining / span);
    }

    public static float SpanAfterUpdate(float previousRemaining, float previousSpan, float nextRemaining,
        bool refreshed = false, bool paused = false, float tolerance = .001f)
    {
        if (!Finite(nextRemaining) || nextRemaining <= 0f) return 0f;
        if (refreshed || previousRemaining <= 0f || !Finite(previousSpan) || previousSpan <= 0f ||
            (!paused && nextRemaining > previousRemaining + tolerance)) return nextRemaining;
        return previousSpan;
    }

    public static bool ShowEnemy(bool alive, bool recentHealthChange, bool hasVisibleStatus) =>
        alive && (recentHealthChange || hasVisibleStatus);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
