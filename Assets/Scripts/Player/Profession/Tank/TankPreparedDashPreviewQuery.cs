using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>權威寬鬆選敵的本機 PhysX 預覽。只查同 Runner，保留真正射線命中的子 Collider 供部位框使用。</summary>
public sealed class TankPreparedDashPreviewQuery
{
    RaycastHit[] rays = new RaycastHit[16];
    Collider[] candidates = new Collider[32];
    readonly List<Collider> body = new List<Collider>(24);
    public TestDamageReceiver Enemy { get; private set; }
    public Collider HitCollider { get; private set; }
    public Vector3 HitPoint { get; private set; }

    public void Clear() { Enemy = null; HitCollider = null; HitPoint = default; body.Clear(); }

    public bool TrySelect(Player owner, Vector3 origin, Vector3 forward, float range, float halfAngle,
        LayerMask enemyMask, LayerMask obstructionMask)
    {
        Clear();
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasInputAuthority ||
            owner.Runner == null || !owner.Runner.IsRunning || range <= 0f) return false;
        if (TryRay(owner, origin, forward, range, enemyMask, obstructionMask, out var direct, out var hit))
        { Select(direct, hit); return true; }
        if (halfAngle <= 0f) return false;
        int count = ActiveAbilityPhysics.OverlapSphere(owner.Runner.GetPhysicsScene(), origin, range, enemyMask, ref candidates);
        float bestAngle = float.PositiveInfinity, bestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = candidates[i];
            if (!PlayerEnemyAimQuery.TryGetLivingEnemy(candidate.gameObject, enemyMask, out var enemy) || enemy.Runner != owner.Runner) continue;
            var hitbox = candidate.GetComponent<Hitbox>();
            Vector3 center = hitbox != null ? hitbox.transform.TransformPoint(hitbox.Offset) : candidate.bounds.center;
            Vector3 offset = center - origin;
            if (!PlayerEnemyAimQuery.IsInsideCone(forward, offset, range, halfAngle) ||
                !TryRay(owner, origin, offset, range, enemyMask, obstructionMask, out var visible, out hit) || visible != enemy ||
                !PlayerEnemyAimQuery.IsInsideCone(forward, hit.point - origin, range, halfAngle)) continue;
            float angle = Vector3.Angle(forward, offset), distance = (hit.point - origin).magnitude;
            // Same priority and tolerance as PlayerEnemyAimQuery.TryGetAuthoritativeWithTolerance.
            if (angle > bestAngle + .001f || (Mathf.Abs(angle - bestAngle) <= .001f && distance >= bestDistance)) continue;
            bestAngle = angle; bestDistance = distance; Select(enemy, hit);
        }
        return Enemy != null;
    }

    void Select(TestDamageReceiver enemy, RaycastHit hit) { Enemy = enemy; HitCollider = hit.collider; HitPoint = hit.point; }

    bool TryRay(Player owner, Vector3 origin, Vector3 direction, float range, LayerMask enemyMask,
        LayerMask obstructionMask, out TestDamageReceiver enemy, out RaycastHit hit)
    {
        enemy = null; hit = default;
        int count = ActiveAbilityPhysics.SphereCastSorted(owner.Runner.GetPhysicsScene(), origin, direction, range,
            0f, enemyMask.value | obstructionMask.value, owner.Object, ref rays);
        if (count == 0) return false;
        hit = rays[0];
        return PlayerEnemyAimQuery.TryGetLivingEnemy(hit.collider.gameObject, enemyMask, out enemy) && enemy.Runner == owner.Runner;
    }

    public bool TryGetFrameGeometry(LayerMask hitMask, float largeSize, out Bounds bounds, out Matrix4x4 matrix)
    {
        bounds = default; matrix = Matrix4x4.identity;
        if (Enemy == null || Enemy.Object == null || !Enemy.Object.IsValid || !Enemy.IsAlive ||
            HitCollider == null || !HitCollider.enabled || !HitCollider.gameObject.activeInHierarchy) return false;
        body.Clear(); Enemy.GetComponentsInChildren(false, body);
        bool found = false;
        for (int i = 0; i < body.Count; i++)
        {
            var collider = body[i];
            if (!collider.enabled || (hitMask.value & (1 << collider.gameObject.layer)) == 0 ||
                collider.GetComponentInParent<TestDamageReceiver>() != Enemy) continue;
            if (!found) { bounds = collider.bounds; found = true; } else bounds.Encapsulate(collider.bounds);
        }
        if (!found) return false;
        var healthBar = Enemy.GetComponent<EnemyHealthBarTarget>();
        if (!TankPreparedDashIndicatorRules.UseHitRegion(healthBar != null && healthBar.IsLarge, bounds, largeSize)) return true;

        // Use the selected hit region, never all renderers or all hitboxes of a large enemy.
        var hitbox = HitCollider.GetComponent<Hitbox>();
        if (hitbox != null && hitbox.enabled)
        {
            bounds = GetHitboxLocalBounds(hitbox); matrix = hitbox.transform.localToWorldMatrix;
        }
        else if (HitCollider is BoxCollider box)
        { bounds = new Bounds(box.center, box.size); matrix = box.transform.localToWorldMatrix; }
        else bounds = HitCollider.bounds;
        return true;
    }

    /// <summary>
    /// 命中部位的局部包絡，旋轉與縮放仍由該 Hitbox 的矩陣處理。
    /// 此版 Fusion 的 CapsuleExtents 是完整高度；半高至少等於半徑，與 Fusion 球端中心計算一致。
    /// </summary>
    public static Bounds GetHitboxLocalBounds(Hitbox hitbox)
    {
        float radius = Mathf.Abs(hitbox.CapsuleRadius);
        Vector3 extents = hitbox.Type == HitboxTypes.Box ? hitbox.BoxExtents :
            hitbox.Type == HitboxTypes.Sphere ? Vector3.one * Mathf.Abs(hitbox.SphereRadius) :
            new Vector3(radius, Mathf.Max(hitbox.CapsuleExtents * .5f, radius), radius);
        return new Bounds(hitbox.Offset, extents * 2f);
    }
}
