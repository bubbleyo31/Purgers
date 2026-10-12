using UnityEngine;

/// <summary>受傷提示共用純規則；不判斷傷害或修改世界。</summary>
public static class DamageIndicatorRules
{
    public static Vector2 ScreenDirection(Vector3 cameraLocalDirection)
    {
        var direction = new Vector2(cameraLocalDirection.x, cameraLocalDirection.z);
        if (direction.sqrMagnitude < .00001f)
            direction = new Vector2(0, cameraLocalDirection.y);
        return direction.sqrMagnitude > .00001f ? direction.normalized : Vector2.zero;
    }
    public static float Envelope(float age, float fadeIn, float hold, float fadeOut)
    {
        fadeIn = Mathf.Max(0,fadeIn); hold = Mathf.Max(0,hold); fadeOut = Mathf.Max(0,fadeOut);
        if (age < 0 || age >= fadeIn+hold+fadeOut) return 0;
        if (fadeIn > 0 && age < fadeIn) return age/fadeIn;
        if (age < fadeIn+hold) return 1;
        return fadeOut > 0 ? Mathf.Clamp01(1-(age-fadeIn-hold)/fadeOut) : 0;
    }
    public static bool ShouldProbe(bool active, bool inViewport, float now, float nextProbe)
        => active && inViewport && now >= nextProbe;
}

/// <summary>固定緩衝的本機查詢；不得以預設 PhysicsScene 代替指定 Runner 的場景。</summary>
public sealed class DamageIndicatorOcclusionQuery
{
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private readonly Collider[] overlaps = new Collider[16];
    public int PhysicsQueryCount { get; private set; }
    public bool IsOccluded(PhysicsScene scene, Vector3 origin, Vector3 target, int mask, Transform receiver, Transform source)
    {
        PhysicsQueryCount = 0;
        if (!scene.IsValid()) return true;
        if (mask == 0) return false;
        // Raycast 起點位於牆內時不會命中該牆，額外檢查相機起點。
        PhysicsQueryCount++;
        int count = scene.OverlapSphere(origin, .01f, overlaps, mask, QueryTriggerInteraction.Ignore);
        for (int i=0;i<count;i++)
            if (!Ignore(overlaps[i], receiver, source)) return true;
        if (count == overlaps.Length) return true; // 飽和時保守遮擋，不配置更大陣列。
        Vector3 delta = target-origin;
        if (delta.sqrMagnitude < .000001f) return false;
        PhysicsQueryCount++;
        count = scene.Raycast(origin, delta.normalized, hits, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
        for (int i=0;i<count;i++)
            if (!Ignore(hits[i].collider, receiver, source)) return true;
        return count == hits.Length;
    }
    private static bool Ignore(Collider collider, Transform receiver, Transform source)
    {
        if (collider == null) return true;
        var t = collider.transform;
        return (receiver != null && t.IsChildOf(receiver)) || (source != null && t.IsChildOf(source));
    }
}
