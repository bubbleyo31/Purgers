using UnityEngine;

/// <summary>自爆共用純規則；遊戲傷害、Inspector 與 Gizmo 使用同一個半徑定義。</summary>
public static class EnemySuicideRules
{
    public static float Falloff(float distance, float radius, float center, float edge)
    {
        if (radius <= 0f || distance > radius || float.IsNaN(distance)) return 0f;
        return Mathf.Lerp(Mathf.Max(0f, center), Mathf.Clamp(edge, 0f, Mathf.Max(0f, center)), Mathf.Clamp01(distance / radius));
    }
    public static float SegmentDistance(Vector3 from, Vector3 to, Vector3 point)
    {
        Vector3 delta = to - from;
        float t = delta.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(point - from, delta) / delta.sqrMagnitude) : 0f;
        return Vector3.Distance(from + delta * t, point);
    }
    public static float VfxScaleFactor(float radius, float referenceDiameter) => 2f * Mathf.Max(0f, radius) / Mathf.Max(.001f, referenceDiameter);
    public static bool IsPlayerTrigger(in DamageRequest request) => !request.Attacker.IsNone && request.RequestedDamage > 0f && !float.IsNaN(request.RequestedDamage) && !float.IsInfinity(request.RequestedDamage);
}

/// <summary>生命模組完成權威、無敵與正傷害檢查後才詢問；不重入傷害、不偽造額外傷害數字。</summary>
public interface IEnemyFatalHitPolicy
{
    bool IsFatalHit(in DamageRequest request);
}
