using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 槍械共用的命中選取與距離衰減計算。
/// 不持有 Networked 狀態，不決定職業的傷害、治療、彈藥或特殊模式。
/// </summary>
public static class WeaponHitUtility
{
    public static bool TryGetNearestValidHit(
        List<LagCompensatedHit> hits,
        NetworkObject ownerPlayerObject,
        out LagCompensatedHit nearestHit)
    {
        nearestHit = default;
        bool found = false;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hits.Count; i++)
        {
            LagCompensatedHit hit = hits[i];
            if (hit.GameObject == null)
                continue;

            // IncludePhysX 可能把自己的普通 Collider 也帶入結果。
            NetworkObject target = hit.GameObject.GetComponentInParent<NetworkObject>();
            if (ownerPlayerObject != null && target == ownerPlayerObject)
                continue;

            if (hit.Distance >= nearestDistance)
                continue;

            nearestDistance = hit.Distance;
            nearestHit = hit;
            found = true;
        }

        return found;
    }

    public static float CalculateDistanceDamageMultiplier(
        float distance, float startDistance, float endDistance, float minimumMultiplier)
    {
        if (distance <= startDistance)
            return 1f;

        float validEnd = Mathf.Max(startDistance + 0.01f, endDistance);
        float progress = Mathf.InverseLerp(startDistance, validEnd, distance);
        return Mathf.Lerp(1f, minimumMultiplier, progress);
    }
}
