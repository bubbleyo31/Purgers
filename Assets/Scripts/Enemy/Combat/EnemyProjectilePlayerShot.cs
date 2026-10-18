using UnityEngine;

/// <summary>在原武器已截斷的權威 Hitscan 線段內處理投射物槍擊反彈與近失彈；不另算敵人傷害。</summary>
public static class EnemyProjectilePlayerShot
{
    public static bool Resolve(Player player, Vector3 origin, Vector3 shotDirection, ref Vector3 endpoint)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority ||
            shotDirection.sqrMagnitude < .000001f) return false;

        EnemyProjectile nearest = null;
        float best = float.PositiveInfinity;
        foreach (var projectile in EnemyProjectile.ReflectableProjectiles)
        {
            if (projectile == null || !projectile.CanBeReflectedByPlayerShot || projectile.Runner != player.Runner) continue;
            if (EnemyReflectorRules.IntersectsSphereSegment(origin, endpoint, projectile.transform.position,
                    projectile.CollisionRadius, out float distance) && distance < best)
            {
                nearest = projectile;
                best = distance;
            }
        }

        bool consumed = false;
        if (nearest != null)
        {
            Vector3 hit = origin + (endpoint - origin).normalized * best;
            consumed = nearest.TryReflectByPlayerShot(player, shotDirection);
            if (consumed) endpoint = hit;
        }
        EnemyReflectorShield.NotifyPlayerShotSegment(player, origin, endpoint);
        return consumed;
    }
}
