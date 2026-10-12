using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>技能共用查詢與傷害送件；查詢只使用呼叫者的 Runner PhysicsScene。</summary>
public static class ActiveAbilityPhysics
{
    private sealed class HitComparer : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b)
        {
            int distance = a.distance.CompareTo(b.distance);
            return distance != 0 ? distance : a.collider.GetInstanceID().CompareTo(b.collider.GetInstanceID());
        }
    }
    private static readonly HitComparer Comparer = new HitComparer();

    /// <summary>容量滿時擴充並重查，避免未排序的固定緩衝漏掉最近牆面。</summary>
    public static int SphereCastSorted(PhysicsScene scene, Vector3 origin, Vector3 direction,
        float distance, float radius, LayerMask mask, NetworkObject ignore, ref RaycastHit[] buffer)
    {
        if (!scene.IsValid() || distance <= 0f || direction.sqrMagnitude < 0.000001f) return 0;
        if (buffer == null || buffer.Length == 0) buffer = new RaycastHit[16];
        int count;
        do
        {
            count = radius > 0f
                ? scene.SphereCast(origin, radius, direction.normalized, buffer, distance, mask, QueryTriggerInteraction.Ignore)
                : scene.Raycast(origin, direction.normalized, buffer, distance, mask, QueryTriggerInteraction.Ignore);
            if (count < buffer.Length) break;
            Array.Resize(ref buffer, buffer.Length * 2);
        } while (true);
        int retained = 0;
        for (int i = 0; i < count; i++)
            if (buffer[i].collider != null && !BelongsTo(buffer[i].collider, ignore)) buffer[retained++] = buffer[i];
        Array.Sort(buffer, 0, retained, Comparer);
        return retained;
    }

    public static int OverlapSphere(PhysicsScene scene, Vector3 position, float radius,
        LayerMask mask, ref Collider[] buffer)
    {
        if (!scene.IsValid()) return 0;
        if (buffer == null || buffer.Length == 0) buffer = new Collider[16];
        int count;
        do
        {
            count = scene.OverlapSphere(position, Mathf.Max(0.001f, radius), buffer, mask, QueryTriggerInteraction.Ignore);
            if (count < buffer.Length) return count;
            Array.Resize(ref buffer, buffer.Length * 2);
        } while (true);
    }

    public static bool BelongsTo(Collider collider, NetworkObject owner) =>
        collider != null && owner != null &&
        (collider.transform == owner.transform || collider.transform.IsChildOf(owner.transform));

    /// <summary>依角色／生命元件辨識玩家子碰撞器，不假設所有子物件都使用 Player Layer。</summary>
    public static bool IsPlayerCollider(Collider collider) => collider != null &&
        (collider.GetComponentInParent<Player>() != null || collider.GetComponentInParent<PlayerHealth>() != null);

    public static MonoBehaviour ResolveReceiver(GameObject hitObject)
    {
        if (hitObject == null) return null;
        foreach (var component in hitObject.GetComponentsInParent<MonoBehaviour>(true))
            if (component is IDamageReceiver) return component;
        return null;
    }

    public static DamageResult ApplyDamage(Player owner, GameObject target, Vector3 point,
        Vector3 normal, Vector3 direction, float distance, float damage, DamageType type, int sequence)
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasStateAuthority)
            return default;
        var request = new DamageRequest
        {
            RequestedDamage = Mathf.Max(0f, damage), BaseDamage = Mathf.Max(0f, damage), DamageType = type,
            FeedbackId = CombatFeedbackId.None, HitZone = DamageHitZoneType.Body, HeadshotDamageMultiplier = 1f,
            Attacker = owner.Object.InputAuthority, SourceNetworkObject = owner.Object, SourceObject = owner.gameObject,
            HitObject = target, HitPoint = point, HitNormal = normal,
            HitDirection = direction.sqrMagnitude > 0.00001f ? direction.normalized : Vector3.zero,
            Distance = distance, Sequence = sequence,
            HasSourceWorldPosition = type == DamageType.Explosion,
            SourceWorldPosition = point - direction.normalized * distance
        };
        DamageReceiverUtility.TryApplyDamage(target, request, out var result);
        if (result.Accepted && result.HasEffectiveDamage)
            owner.GetComponent<PlayerCombatFeedbackRelay>()?.ReportAbilityDamage(result);
        return result;
    }

    public static Vector3 ShotOrigin(Player owner, float height) =>
        (owner.Movement != null && owner.Movement.KCC != null
            ? owner.Movement.KCC.Data.TargetPosition : owner.transform.position) + Vector3.up * height;

    /// <summary>從目前膠囊中心移到設定發射點；蹲姿遇低矮障礙時截到近側，不改正常高度。</summary>
    public static bool TryGetShotOrigin(Player owner, float height, float radius, LayerMask mask, out Vector3 origin, bool ignorePlayers = false)
    {
        origin = ShotOrigin(owner, height);
        var kcc = owner.Movement != null ? owner.Movement.KCC : null;
        Vector3 anchor = kcc != null
            ? kcc.Data.TargetPosition + Vector3.up * (kcc.Settings.Height * 0.5f)
            : owner.transform.position;
        return TryResolveShotOrigin(owner.Runner.GetPhysicsScene(), anchor, origin, radius, mask, owner.Object, out origin, ignorePlayers);
    }

    /// <summary>純幾何起點解析，僅使用指定 PhysicsScene；安全起點已嵌入障礙時拒絕發射。</summary>
    public static bool TryResolveShotOrigin(PhysicsScene scene, Vector3 anchor, Vector3 desired,
        float radius, LayerMask mask, NetworkObject owner, out Vector3 origin, bool ignorePlayers = false)
    {
        origin = anchor;
        if (!scene.IsValid()) return false;
        const float skin = 0.002f;
        radius = Mathf.Max(0f, radius);
        Collider[] overlaps = new Collider[4];
        int overlapping = OverlapSphere(scene, anchor, Mathf.Max(skin, radius), mask, ref overlaps);
        for (int i = 0; i < overlapping; i++)
        {
            var collider = overlaps[i];
            if (collider == null || BelongsTo(collider, owner) || (ignorePlayers && IsPlayerCollider(collider))) continue;
            if ((collider.ClosestPoint(anchor) - anchor).sqrMagnitude <= radius * radius + 0.0000001f)
                return false;
        }
        Vector3 delta = desired - anchor;
        RaycastHit[] hits = new RaycastHit[4];
        int count = SphereCastSorted(scene, anchor, delta, delta.magnitude, radius, mask, owner, ref hits);
        origin = desired;
        for (int i = 0; i < count; i++)
        {
            if (ignorePlayers && IsPlayerCollider(hits[i].collider)) continue;
            origin = anchor + delta.normalized * Mathf.Max(0f, hits[i].distance - skin);
            break;
        }
        return true;
    }

    /// <summary>爆炸只由世界實體遮擋；其他傷害接收者不充當牆壁。</summary>
    public static bool IsWorldOccluded(PhysicsScene scene, Vector3 origin, Vector3 destination,
        LayerMask mask, ref RaycastHit[] buffer)
    {
        // PhysX 射線不回報包含起點的 Collider；先擋住牆內起點，避免穿薄牆爆炸。
        Collider[] containing = new Collider[4];
        int overlapping = OverlapSphere(scene, origin, 0.002f, mask, ref containing);
        for (int i = 0; i < overlapping; i++)
            if (containing[i] != null && ResolveReceiver(containing[i].gameObject) == null &&
                (containing[i].ClosestPoint(origin) - origin).sqrMagnitude < 0.0000001f) return true;
        Vector3 delta = destination - origin;
        int count = SphereCastSorted(scene, origin, delta, delta.magnitude, 0f, mask, null, ref buffer);
        for (int i = 0; i < count; i++)
            if (ResolveReceiver(buffer[i].collider.gameObject) == null) return true;
        return false;
    }
}
