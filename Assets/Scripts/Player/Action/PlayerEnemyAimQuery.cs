using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>共用直接瞄準查詢。權威端使用 Fusion Hitbox，本地提示使用同 Runner 的 PhysX Collider。</summary>
public static class PlayerEnemyAimQuery
{
    public static bool TryGetLivingEnemy(GameObject hit, LayerMask enemyMask, out TestDamageReceiver enemy)
    {
        enemy = null;
        if (hit == null || (enemyMask.value & (1 << hit.layer)) == 0 || hit.GetComponentInParent<Player>() != null) return false;
        enemy = hit.GetComponentInParent<TestDamageReceiver>();
        return enemy != null && enemy.Object != null && enemy.Object.IsValid && enemy.IsAlive;
    }

    public static bool TryGetAuthoritative(Player owner, Vector3 origin, Vector3 direction, float distance,
        LayerMask enemyMask, LayerMask obstructionMask, out TestDamageReceiver enemy, out Vector3 point)
    {
        enemy = null;
        point = default;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasStateAuthority ||
            owner.Runner == null || owner.Runner.LagCompensation == null || distance <= 0f) return false;
        if (!owner.Runner.LagCompensation.Raycast(origin, direction.normalized, distance, owner.Object.InputAuthority,
            out LagCompensatedHit hit, enemyMask.value | obstructionMask.value,
            HitOptions.SubtickAccuracy | HitOptions.IgnoreInputAuthority | HitOptions.IncludePhysX, QueryTriggerInteraction.Ignore)) return false;
        point = hit.Point;
        return TryGetLivingEnemy(hit.GameObject, enemyMask, out enemy);
    }

    public static bool IsInsideCone(Vector3 forward, Vector3 offset, float range, float halfAngle) =>
        forward.sqrMagnitude > .000001f && offset.sqrMagnitude > .000001f && range > 0f &&
        offset.sqrMagnitude <= range * range && Vector3.Angle(forward, offset) <= Mathf.Clamp(halfAngle, 0f, 90f) + .0001f;

    /// <summary>直線命中優先；否則只搜尋錐形內且射線可見的敵人。緩衝由呼叫者持有，UI 不使用寬鬆選敵。</summary>
    public static bool TryGetAuthoritativeWithTolerance(Player owner, Vector3 origin, Vector3 direction, float distance,
        float halfAngle, LayerMask enemyMask, LayerMask obstructionMask, List<LagCompensatedHit> candidates,
        out TestDamageReceiver enemy, out Vector3 point)
    {
        if (TryGetAuthoritative(owner, origin, direction, distance, enemyMask, obstructionMask, out enemy, out point)) return true;
        enemy = null; point = default;
        if (halfAngle <= 0f || candidates == null || owner == null || owner.Object == null || !owner.Object.IsValid ||
            !owner.Object.HasStateAuthority || owner.Runner == null || owner.Runner.LagCompensation == null || distance <= 0f) return false;
        candidates.Clear();
        owner.Runner.LagCompensation.OverlapSphere(origin, distance, owner.Object.InputAuthority, candidates, enemyMask,
            HitOptions.SubtickAccuracy | HitOptions.IgnoreInputAuthority | HitOptions.IncludePhysX, true, QueryTriggerInteraction.Collide);
        float bestAngle = float.PositiveInfinity, bestDistance = float.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (!TryGetLivingEnemy(candidate.GameObject, enemyMask, out var target)) continue;
            var collider = candidate.GameObject.GetComponent<Collider>();
            var hitbox = candidate.GameObject.GetComponent<Hitbox>();
            Vector3 center = hitbox != null ? hitbox.transform.TransformPoint(hitbox.Offset)
                : collider != null ? collider.bounds.center : candidate.GameObject.transform.position;
            Vector3 offset = center - origin;
            if (!IsInsideCone(direction, offset, distance, halfAngle)) continue;
            if (!TryGetAuthoritative(owner, origin, offset, distance, enemyMask, obstructionMask, out var visible, out var surface) ||
                visible != target || !IsInsideCone(direction, surface - origin, distance, halfAngle)) continue;
            float angle = Vector3.Angle(direction, offset), range = (surface - origin).magnitude;
            if (angle > bestAngle + .001f || (Mathf.Abs(angle - bestAngle) <= .001f && range >= bestDistance)) continue;
            enemy = target; point = surface; bestAngle = angle; bestDistance = range;
        }
        return enemy != null;
    }
    public static bool TryGetLocal(Player owner, Vector3 origin, Vector3 direction, float distance,
        LayerMask enemyMask, LayerMask obstructionMask, ref RaycastHit[] buffer)
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !LocalPlayerSpectatorController.IsViewedObject(owner.Object) ||
            owner.Runner == null || (owner.Health != null && !owner.Health.IsAlive)) return false;
        int count = ActiveAbilityPhysics.SphereCastSorted(owner.Runner.GetPhysicsScene(), origin, direction, distance,
            0f, enemyMask.value | obstructionMask.value, owner.Object, ref buffer);
        return count > 0 && TryGetLivingEnemy(buffer[0].collider.gameObject, enemyMask, out _);
    }
}
