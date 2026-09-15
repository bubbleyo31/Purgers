using UnityEngine;

/// <summary>
/// 自由飛行巡邏：XYZ 直線前往人工節點，整段先以膠囊掃掠確認通道暢通。
/// 牆後節點視為本次不可直達，Brain 改選其他候選點；每一步再掃掠，保護動態障礙。
/// 這是局部巡邏，不是完整三維尋路。複雜室內飛行請先放連續可視節點；
/// 日後可替換此 Navigator 為飛行節點圖／Voxel 路徑，不影響警戒系統。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMovementOwnership))]
public sealed class EnemyFlyingPatrolNavigator : EnemyPatrolNavigator
{
    [Header("飛行巡邏碰撞 Gizmos")]

    [SerializeField]
    [Tooltip(
        "選取敵人時顯示目前實際用於 OverlapCapsule／CapsuleCast 的碰撞膠囊。\n" +
        "膠囊尺寸直接使用 Body Radius、Body Height 與 Collision Skin，不是另外一套視覺數值。")]
    private bool drawCollisionCapsuleGizmo = true;

    [SerializeField]
    [Tooltip("Play Mode 顯示最近一次巡邏候選點，以及起點到終點的膠囊掃掠通道。")]
    private bool drawLastPatrolPassageGizmo = true;

    [SerializeField]
    [Tooltip("目前飛行碰撞膠囊的 Gizmo 顏色。")]
    private Color collisionCapsuleGizmoColor =
        new Color(0.15f, 1f, 0.45f, 0.9f);

    [SerializeField]
    [Tooltip("最近一次合法巡邏通道的 Gizmo 顏色。")]
    private Color clearPassageGizmoColor =
        new Color(0.1f, 0.85f, 1f, 0.75f);

    [SerializeField]
    [Tooltip("最近一次被障礙阻擋或設定無效的巡邏通道 Gizmo 顏色。")]
    private Color blockedPassageGizmoColor =
        new Color(1f, 0.15f, 0.1f, 0.85f);

    private Vector3 destination;
    private bool hasPath;
    private bool hasLastPlanAttempt;
    private bool lastPlanWasClear;
    public override EnemyLocomotionKind SupportedLocomotion => EnemyLocomotionKind.FreeFlying;

    public override bool TryBegin(Vector3 target, out Vector3 actualDestination)
    {
        actualDestination = target;
        destination = target;
        hasLastPlanAttempt = true;
        ClearLastPlanFailure();

        if (!TryValidateConfiguration(out string configurationDetail))
        {
            hasPath = false;
            lastPlanWasClear = false;
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.ConfigurationInvalid,
                configurationDetail
            );
            return false;
        }

        hasPath = IsSegmentClear(transform.position, target);
        lastPlanWasClear = hasPath;
        if (!hasPath)
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.FlyingPathObstructed,
                $"Enemy Root 到巡邏點的完整膠囊通道被 Obstacle Mask 擋住。" +
                $"Start={transform.position:F3}，End={target:F3}。"
            );
        }

        return hasPath;
    }

    public override EnemyPatrolMoveResult TickMove(float deltaTime, float arrivalDistance)
    {
        if (!hasPath || !ConfigurationValid()) return EnemyPatrolMoveResult.Failed;
        if (!Ownership.CanMove) return EnemyPatrolMoveResult.Moving;
        if (Vector3.Distance(transform.position, destination) <= arrivalDistance)
            return EnemyPatrolMoveResult.Arrived;
        Vector3 next = Vector3.MoveTowards(transform.position, destination, patrolSpeed * deltaTime);
        if (!TryMove(next, deltaTime)) return EnemyPatrolMoveResult.Failed;
        return Vector3.Distance(transform.position, destination) <= arrivalDistance ?
            EnemyPatrolMoveResult.Arrived : EnemyPatrolMoveResult.Moving;
    }

    public override void Stop() { hasPath = false; }

    private void OnDrawGizmosSelected()
    {
        if (drawCollisionCapsuleGizmo)
        {
            Gizmos.color = collisionCapsuleGizmoColor;
            DrawCollisionCapsuleAt(transform.position);
        }

        if (!drawLastPatrolPassageGizmo ||
            !Application.isPlaying ||
            !hasLastPlanAttempt)
        {
            return;
        }

        Gizmos.color = lastPlanWasClear
            ? clearPassageGizmoColor
            : blockedPassageGizmoColor;

        DrawCapsulePassage(
            transform.position,
            destination
        );
        DrawCollisionCapsuleAt(destination);
        Gizmos.DrawLine(
            transform.position,
            destination
        );
    }

    /// <summary>以與實際碰撞查詢完全相同的中心與半徑畫出垂直膠囊。</summary>
    private void DrawCollisionCapsuleAt(Vector3 rootPosition)
    {
        Vector3 bottom =
            rootPosition +
            Vector3.up * (bodyRadius + collisionSkin);

        Vector3 top =
            rootPosition +
            Vector3.up * Mathf.Max(
                bodyRadius + collisionSkin,
                bodyHeight - bodyRadius
            );

        Gizmos.DrawWireSphere(bottom, bodyRadius);
        Gizmos.DrawWireSphere(top, bodyRadius);

        Gizmos.DrawLine(bottom + Vector3.right * bodyRadius,
            top + Vector3.right * bodyRadius);
        Gizmos.DrawLine(bottom - Vector3.right * bodyRadius,
            top - Vector3.right * bodyRadius);
        Gizmos.DrawLine(bottom + Vector3.forward * bodyRadius,
            top + Vector3.forward * bodyRadius);
        Gizmos.DrawLine(bottom - Vector3.forward * bodyRadius,
            top - Vector3.forward * bodyRadius);
    }

    /// <summary>畫出膠囊從起點掃掠到終點時的外側通道線。</summary>
    private void DrawCapsulePassage(Vector3 from, Vector3 to)
    {
        Vector3 fromBottom =
            from + Vector3.up * (bodyRadius + collisionSkin);
        Vector3 fromTop =
            from + Vector3.up * Mathf.Max(
                bodyRadius + collisionSkin,
                bodyHeight - bodyRadius
            );
        Vector3 toBottom =
            to + Vector3.up * (bodyRadius + collisionSkin);
        Vector3 toTop =
            to + Vector3.up * Mathf.Max(
                bodyRadius + collisionSkin,
                bodyHeight - bodyRadius
            );

        Vector3[] offsets =
        {
            Vector3.right * bodyRadius,
            -Vector3.right * bodyRadius,
            Vector3.forward * bodyRadius,
            -Vector3.forward * bodyRadius
        };

        for (int index = 0; index < offsets.Length; index++)
        {
            Gizmos.DrawLine(
                fromBottom + offsets[index],
                toBottom + offsets[index]
            );
            Gizmos.DrawLine(
                fromTop + offsets[index],
                toTop + offsets[index]
            );
        }
    }
}
