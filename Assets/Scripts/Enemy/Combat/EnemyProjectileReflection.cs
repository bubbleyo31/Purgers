using Fusion;
using UnityEngine;

/// <summary>由既有近戰／衝刺的權威命中時機呼叫；不讀輸入、不決定攻擊能否啟動。</summary>
public static class EnemyProjectileReflection
{
    private static RaycastHit[] sightHits = new RaycastHit[16];
    public static int ReflectMelee(Player player, Vector3 origin, Vector3 forward, float range, float searchRadius, float fullAngle)
    {
        if (!Ready(player)) return 0;
        int reflected = 0;

        foreach (var projectile in EnemyProjectile.ReflectableProjectiles)
        {
            if (!Candidate(player, projectile)) continue;
            Vector3 delta = projectile.transform.position - origin;
            if (!EnemyReflectorRules.WithinMeleeReflection(delta,forward,range,searchRadius,fullAngle,projectile.ReflectionCaptureRadius) || !Visible(player, origin, projectile.transform.position)) continue;
            if (projectile.TryReflect(player, player.Movement.GetAimDirection())) reflected++;
        }
        return reflected;
    }
    public static int ReflectDash(Player player, Vector3 center, Quaternion rotation, Vector3 extents, Vector3 aim)
    {
        if (!Ready(player)) return 0;
        int reflected = 0;
        foreach (var projectile in EnemyProjectile.ReflectableProjectiles)
        {
            if (!Candidate(player, projectile)) continue;
            Vector3 local = Quaternion.Inverse(rotation) * (projectile.transform.position - center);
            Vector3 padded=extents+Vector3.one*projectile.ReflectionCaptureRadius;
            if (Mathf.Abs(local.x) > padded.x || Mathf.Abs(local.y) > padded.y || Mathf.Abs(local.z) > padded.z ||
                !Visible(player, center, projectile.transform.position)) continue;
            if (projectile.TryReflect(player, aim)) reflected++;
        }
        return reflected;
    }
    private static bool Ready(Player player) => player != null && player.Object != null && player.Object.IsValid && player.Object.HasStateAuthority;
    private static bool Candidate(Player player, EnemyProjectile projectile) => projectile != null && projectile.Runner == player.Runner && projectile.CanReflect;
    private static bool Visible(Player player, Vector3 origin, Vector3 target)
    {
        Vector3 delta = target - origin;
        if (delta.sqrMagnitude < .000001f) return true;
        // 近戰與子彈間的實體世界必須暢通；不讓關閉近戰視線選項造成隔牆反彈。
        var hits = sightHits;
        int count;
        do {
            count = player.Runner.GetPhysicsScene().Raycast(origin, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count < hits.Length) break;
            System.Array.Resize(ref hits, hits.Length * 2); sightHits = hits;
        } while (true);
        for (int i = 0; i < count; i++)
            if (hits[i].collider != null && !hits[i].collider.transform.IsChildOf(player.transform) &&
                hits[i].collider.GetComponentInParent<EnemyProjectile>() == null) return false;
        return true;
    }
}
