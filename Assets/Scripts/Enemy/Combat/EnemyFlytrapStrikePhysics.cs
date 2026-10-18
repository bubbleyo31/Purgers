using Fusion;
using UnityEngine;

/// <summary>Runner PhysicsScene 的連續藤蔓掃掠；玩家不依賴障礙 Layer，等距時牆優先。</summary>
public static class EnemyFlytrapStrikePhysics
{
    private static bool IsObstacle(Collider collider, LayerMask mask) =>
        !ActiveAbilityPhysics.IsPlayerCollider(collider) && (mask.value & (1 << collider.gameObject.layer)) != 0;
    public static Collider FirstContact(PhysicsScene scene, Vector3 start, Vector3 direction, float distance,
        float radius, LayerMask obstacleMask, NetworkObject owner, ref RaycastHit[] hits, ref Collider[] overlaps, out float hitDistance)
    {
        hitDistance = float.PositiveInfinity;
        Collider first = null;
        // SphereCast 不回報起點已重疊的碰撞，先補起點檢查。
        int count = ActiveAbilityPhysics.OverlapSphere(scene, start, radius, ~0, ref overlaps);
        for (int i = 0; i < count; i++)
        {
            var c = overlaps[i];
            if (ActiveAbilityPhysics.BelongsTo(c, owner) || (!IsObstacle(c, obstacleMask) && !ActiveAbilityPhysics.IsPlayerCollider(c))) continue;
            if (first == null || IsObstacle(c, obstacleMask) || (!IsObstacle(first, obstacleMask) && c.GetInstanceID() < first.GetInstanceID())) first = c;
        }
        if (first != null) { hitDistance = 0f; return first; }
        count = ActiveAbilityPhysics.SphereCastSorted(scene, start, direction, distance, radius, ~0, owner, ref hits);
        for (int i = 0; i < count; i++)
        {
            var c = hits[i].collider;
            bool wall = IsObstacle(c, obstacleMask);
            if (!wall && !ActiveAbilityPhysics.IsPlayerCollider(c)) continue;
            if (hits[i].distance < hitDistance || (Mathf.Abs(hits[i].distance - hitDistance) < .00001f && wall))
            { first = c; hitDistance = hits[i].distance; }
        }
        return first;
    }
}
