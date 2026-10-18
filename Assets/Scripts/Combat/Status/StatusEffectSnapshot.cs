using Fusion;
using UnityEngine;

/// <summary>供本機呈現使用的唯讀比例。沒有文字、層數或獨立倒數。</summary>
public readonly struct StatusEffectSnapshot
{
    public readonly StatusEffectKind Kind;
    public readonly float Ratio;
    public bool IsVisible => Ratio > 0f;
    private StatusEffectSnapshot(StatusEffectKind kind, float ratio) { Kind = kind; Ratio = ratio; }
    public static StatusEffectSnapshot FromTimer(StatusEffectKind kind, NetworkRunner runner, TickTimer timer, float span) =>
        new StatusEffectSnapshot(kind, runner == null ? 0f : StatusEffectRules.Ratio(timer.RemainingTime(runner) ?? 0f, span));
    public static StatusEffectSnapshot FromProgress(StatusEffectKind kind, float remainingFraction) =>
        new StatusEffectSnapshot(kind, StatusEffectRules.Ratio(remainingFraction, 1f));
}

/// <summary>State Authority 同步顯示分母，期限仍引用原狀態；避免重現／晚加入時重填滿格。</summary>
public struct StatusEffectClock : INetworkStruct
{
    public TickTimer Deadline;
    public float Span;
    public StatusEffectClock Updated(NetworkRunner runner, TickTimer next, float initialSpan)
    {
        float remaining = next.RemainingTime(runner) ?? 0f;
        if (remaining <= 0f) return default;
        float previous = Deadline.RemainingTime(runner) ?? 0f;
        float span = previous <= 0f || Span <= 0f ? Mathf.Max(remaining, initialSpan) :
            StatusEffectRules.SpanAfterUpdate(previous, Span, remaining);
        return new StatusEffectClock { Deadline = next, Span = span };
    }
    public StatusEffectSnapshot Snapshot(StatusEffectKind kind, NetworkRunner runner) =>
        StatusEffectSnapshot.FromTimer(kind, runner, Deadline, Span);
}
