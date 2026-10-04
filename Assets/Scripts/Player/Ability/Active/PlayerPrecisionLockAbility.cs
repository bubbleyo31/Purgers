using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>獨立 E 精準鎖敵；只修正子彈方向，不承接舊 Attack 耗彈或傷害倍率。</summary>
public sealed class PlayerPrecisionLockAbility : PlayerActiveAbilityBase
{
    [Header("精準鎖敵（沿用 Attack 鎖定規則）")]
    [SerializeField, Min(0.01f), Tooltip("啟用後作用秒數；本版規格為 2 秒，期間每次真正射擊都重新選合法目標。")]
    private float durationSeconds = 2f;
    [SerializeField, Min(0.1f), Tooltip("搜尋距離，公尺；沿用舊 Attack 預設 60，且不超過目前武器實際射程。")]
    private float focusLockDistance = 60f;
    [SerializeField, Range(0.1f, 45f), Tooltip("相對原準星方向的最大夾角，度；沿用舊 Attack 預設 8，優先選最接近準星的敵人。")]
    private float focusLockAngle = 8f;
    [SerializeField, Tooltip("原準星已直接命中合法敵人時保留方向，保留手動瞄頭。玩家治療命中一律保留。")]
    private bool preserveDirectCrosshairHit = true;
    [SerializeField, Tooltip("使用 Fusion Subtick Accuracy 取得目標歷史位置與檢查遮擋。")]
    private bool useSubtickAccuracy = true;
    [SerializeField, Min(0f), Tooltip("可見性射線在目標瞄準點後多檢查的距離，公尺；沿用 0.2 避免表面浮點誤差。")]
    private float targetRayPadding = 0.2f;
    [SerializeField, Tooltip("只有 Lag Compensation 未啟用時允許使用 Runner PhysicsScene；Lag 射線未命中時不改用現時位置。")]
    private bool allowPhysicsFallback = true;

    private readonly List<LagCompensatedHit> lagHits = new List<LagCompensatedHit>();
    private RaycastHit[] physicsHits = new RaycastHit[16];
    public override bool IsUsable => base.IsUsable && PlayerAbilityQualification.HasRangedWeapon(Owner);
    public bool IsLockActive => Ready && Phase == PlayerActiveAbilityPhase.Active;

    protected override void Activate() => BeginActive(durationSeconds);

    public static bool TryModifyShot(Player owner, Vector3 origin, Vector3 rawDirection,
        float weaponRange, LayerMask weaponMask, out Vector3 direction)
    {
        direction = rawDirection;
        var module = PlayerAbilityQualification.GetModule<PlayerPrecisionLockAbility>(owner);
        return module != null && module.Authority && module.IsLockActive && module.IsUsable &&
            module.TryResolve(origin, rawDirection, weaponRange, weaponMask, out direction);
    }

    private bool TryResolve(Vector3 origin, Vector3 rawDirection, float range, LayerMask mask, out Vector3 direction)
    {
        direction = rawDirection;
        if (rawDirection.sqrMagnitude < 0.000001f) return false;
        rawDirection.Normalize();
        if (TryFirstHit(origin, rawDirection, range, mask, out var direct))
        {
            if (direct.GetComponentInParent<PlayerHealth>() != null) return false;
            var directTarget = direct.GetComponentInParent<FocusTarget>();
            if (preserveDirectCrosshairHit && directTarget != null && directTarget.IsTargetable) return false;
        }
        float bestAngle = float.MaxValue, bestDistance = float.MaxValue;
        FocusTarget selected = null;
        foreach (var target in FocusTarget.ActiveTargets)
        {
            if (target == null || !target.IsTargetable) continue;
            NetworkObject targetObject = target.OwnerNetworkObject;
            if (targetObject != null)
            {
                if (!targetObject.IsValid || targetObject.Runner != Runner || targetObject == Owner.Object) continue;
            }
            else if (target.gameObject.scene != Owner.gameObject.scene) continue;
            Vector3 aim = target.GetAimPosition(Runner, Owner.Object.InputAuthority, useSubtickAccuracy);
            Vector3 delta = aim - origin;
            float distance = delta.magnitude;
            if (distance < 0.0001f || distance > Mathf.Min(range, focusLockDistance)) continue;
            Vector3 candidateDirection = delta / distance;
            float angle = Vector3.Angle(rawDirection, candidateDirection);
            if (angle > focusLockAngle) continue;
            if (!TryFirstHit(origin, candidateDirection, distance + targetRayPadding, mask, out var hitObject) ||
                !target.OwnsHit(hitObject)) continue;
            if (angle >= bestAngle && !(Mathf.Abs(angle - bestAngle) <= 0.001f && distance < bestDistance)) continue;
            selected = target;
            bestAngle = angle;
            bestDistance = distance;
            direction = candidateDirection;
        }
        return selected != null;
    }

    private bool TryFirstHit(Vector3 origin, Vector3 direction, float distance, LayerMask mask, out GameObject hitObject)
    {
        hitObject = null;
        if (Runner.LagCompensation != null)
        {
            HitOptions options = HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority;
            if (useSubtickAccuracy) options |= HitOptions.SubtickAccuracy;
            lagHits.Clear();
            Runner.LagCompensation.RaycastAll(origin, direction, distance, Owner.Object.InputAuthority,
                lagHits, mask, true, options, QueryTriggerInteraction.Ignore);
            if (WeaponHitUtility.TryGetNearestValidHit(lagHits, Owner.Object, out var hit)) hitObject = hit.GameObject;
        }
        else if (allowPhysicsFallback)
        {
            int count = ActiveAbilityPhysics.SphereCastSorted(Runner.GetPhysicsScene(), origin, direction,
                distance, 0f, mask, Owner.Object, ref physicsHits);
            if (count > 0) hitObject = physicsHits[0].collider.gameObject;
        }
        return hitObject != null;
    }
}
