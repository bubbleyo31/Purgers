using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum GrappleSpikePhase : byte { Ready, Active, Cooldown }

/// <summary>尖刺規則；期限與目標間隔不依賴是否正在鈎索。</summary>
public static class GrappleSpikeRules
{
    public static bool IsEligible(bool attached, bool momentum, bool includeMomentum)
        => attached || (includeMomentum && momentum);
    public static GrappleSpikePhase GetPhase(int now, int activeEnd, int cooldownEnd)
        => now < activeEnd ? GrappleSpikePhase.Active :
           now < cooldownEnd ? GrappleSpikePhase.Cooldown : GrappleSpikePhase.Ready;
    public static int DurationTicks(float seconds, float delta)
        => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, seconds) / Mathf.Max(0.000001f, delta)));
    public static float Damage(float baseDamage, float multiplier)
        => Mathf.Max(0f, baseDamage) * Mathf.Max(1f, multiplier);
    public static Vector3 ClosestCenter(Vector3 start, Vector3 end, Vector3 point)
    {
        Vector3 travel = end - start;
        float t = travel.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(point - start, travel) / travel.sqrMagnitude) : 0f;
        return start + travel * t;
    }
    public static Vector3 OutwardDirection(Vector3 center, Vector3 target, Vector3 fallback)
    {
        Vector3 delta = Vector3.ProjectOnPlane(target - center, Vector3.up);
        if (delta.sqrMagnitude < 0.000001f) delta = Vector3.ProjectOnPlane(fallback, Vector3.up);
        return delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector3.forward;
    }
}

/// <summary>僅 State Authority 使用；以 NetworkId 去重，與 Collider 數量及技能輪次無關。</summary>
public sealed class GrappleSpikeHitLedger
{
    private readonly Dictionary<NetworkId, int> nextHits = new Dictionary<NetworkId, int>();
    private readonly List<NetworkId> expired = new List<NetworkId>();
    public bool CanHit(NetworkId id, int now) => !nextHits.TryGetValue(id, out int next) || now >= next;
    public void Record(NetworkId id, int now, int interval) => nextHits[id] = now + Mathf.Max(1, interval);
    public void Prune(int now)
    {
        expired.Clear();
        foreach (var pair in nextHits) if (now >= pair.Value) expired.Add(pair.Key);
        foreach (var id in expired) nextHits.Remove(id);
    }
    public void Clear() => nextHits.Clear();
}

