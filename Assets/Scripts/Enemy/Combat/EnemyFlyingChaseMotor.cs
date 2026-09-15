using Fusion;
using UnityEngine;


/// <summary>
/// 追逐 B：自由飛行遠程敵人的距離帶控制。
///
/// 太遠時接近，太近時後退，射程帶內以個人方向做緩慢側移；
/// 候選點必須同時具備移動通道與對玩家的視線。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMovementOwnership))]
public sealed class EnemyFlyingChaseMotor :
    EnemyChaseMotor
{
    [Header("Chase B：射程帶")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "射程帶內額外保留的距離緩衝，公尺。\n" +
        "敵人不會因玩家只移動幾公分就立刻在前進／後退間切換。")]
    private float rangeHysteresis =
        1f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("理想距離在最小與最大攻擊距離之間的位置。0.6 代表略偏向外側。")]
    private float idealRangeRatio =
        0.6f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("處於理想射程時，每次重新規劃嘗試繞玩家側移多少公尺。0 代表原地維持。")]
    private float orbitStepDistance =
        2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("尋找安全射擊點時允許向上／下嘗試的高度，公尺。")]
    private float verticalSearchOffset =
        2f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("不同個體選擇相反環繞方向的比例來源。保持預設即可；每個實例生成時只決定一次。")]
    private float clockwiseChance =
        0.5f;

    [Header("飛行追逐碰撞 Gizmos")]

    [SerializeField]
    [Tooltip(
        "選取敵人時顯示目前實際用於 CapsuleCast 的碰撞膠囊。\n" +
        "尺寸直接使用 Body Radius、Body Height 與 Collision Skin。")]
    private bool drawCollisionCapsuleGizmo = true;

    [SerializeField]
    [Tooltip("Play Mode 顯示最近一次 Chase B 採用或拒絕的候選移動通道。")]
    private bool drawLastChasePassageGizmo = true;

    [SerializeField]
    [Tooltip("目前飛行碰撞膠囊的 Gizmo 顏色。")]
    private Color collisionCapsuleGizmoColor =
        new Color(0.15f, 1f, 0.45f, 0.9f);

    [SerializeField]
    [Tooltip("最近一次合法追逐通道的 Gizmo 顏色。")]
    private Color clearPassageGizmoColor =
        new Color(1f, 0.2f, 0.85f, 0.8f);

    [SerializeField]
    [Tooltip("本輪所有候選通道都失敗時使用的 Gizmo 顏色。")]
    private Color blockedPassageGizmoColor =
        new Color(1f, 0.15f, 0.1f, 0.85f);

    private float orbitSign;
    private bool hasLastPlanAttempt;
    private bool lastPlanWasClear;
    private Vector3 lastPlannedDestination;

    public override EnemyChaseKind SupportedChaseKind =>
        EnemyChaseKind.ChaseB;

    protected override void Awake()
    {
        base.Awake();
        orbitSign =
            Random.value < clockwiseChance
                ? 1f
                : -1f;
    }

    public override bool TryPlanDestination(
        NetworkObject target,
        Vector3 targetPosition,
        bool hasDirectSight,
        float minimumAttackDistance,
        float maximumAttackDistance,
        out Vector3 destination
    )
    {
        destination = transform.position;

        if (CanMove == false)
        {
            return false;
        }

        hasLastPlanAttempt = true;
        lastPlanWasClear = false;
        lastPlannedDestination = transform.position;

        Vector3 toTarget =
            targetPosition - transform.position;

        float distance = toTarget.magnitude;

        if (distance <= 0.001f)
        {
            toTarget = transform.forward;
            distance = 0.001f;
        }

        Vector3 direction =
            toTarget / distance;

        float idealDistance =
            Mathf.Lerp(
                minimumAttackDistance,
                maximumAttackDistance,
                idealRangeRatio
            );

        Vector3 primary;

        if (distance > maximumAttackDistance + rangeHysteresis ||
            hasDirectSight == false)
        {
            primary =
                targetPosition -
                direction * idealDistance;
        }
        else if (distance <
                 Mathf.Max(
                     0f,
                     minimumAttackDistance - rangeHysteresis
                 ))
        {
            primary =
                transform.position -
                direction *
                Mathf.Max(
                    orbitStepDistance,
                    idealDistance - distance
                );
        }
        else
        {
            Vector3 side =
                Vector3.Cross(Vector3.up, direction).normalized *
                orbitSign;

            primary =
                transform.position +
                side * orbitStepDistance;
        }

        // 若本輪所有候選點都失敗，Gizmo 至少保留主要候選通道供定位。
        lastPlannedDestination = primary;

        Vector3[] candidates =
        {
            primary,
            primary + Vector3.up * verticalSearchOffset,
            primary - Vector3.up * verticalSearchOffset,
            transform.position
        };

        for (int index = 0;
             index < candidates.Length;
             index++)
        {
            Vector3 candidate =
                candidates[index];

            if (IsSegmentClear(
                    transform.position,
                    candidate
                ) &&
                HasLineOfSightFrom(
                    candidate,
                    target,
                    targetPosition
                ))
            {
                destination = candidate;
                lastPlannedDestination = candidate;
                lastPlanWasClear = true;
                return true;
            }
        }

        return false;
    }

    public override bool TickMove(
        Vector3 destination,
        float deltaTime,
        float stoppingDistance,
        out float actualSpeed
    )
    {
        actualSpeed = 0f;

        if (CanMove == false)
        {
            return false;
        }

        Vector3 before = transform.position;

        if (Vector3.Distance(before, destination) <=
            stoppingDistance)
        {
            return true;
        }

        Vector3 next =
            Vector3.MoveTowards(
                before,
                destination,
                chaseSpeed * deltaTime
            );

        if (TryMoveRoot(next, deltaTime) == false)
        {
            return false;
        }

        actualSpeed =
            Vector3.Distance(before, transform.position) /
            Mathf.Max(0.0001f, deltaTime);

        return true;
    }

    public override void Stop()
    {
    }

    private bool HasLineOfSightFrom(
        Vector3 candidate,
        NetworkObject target,
        Vector3 targetPosition
    )
    {
        Vector3 eye =
            candidate +
            Vector3.up *
            Mathf.Max(bodyRadius, bodyHeight * 0.75f);

        Vector3 offset =
            targetPosition - eye;

        float distance = offset.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        if (Runner.GetPhysicsScene().Raycast(
                eye,
                offset / distance,
                out RaycastHit hit,
                distance,
                obstacleMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            return hit.transform != null &&
                   target != null &&
                   hit.transform.IsChildOf(target.transform);
        }

        return true;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        rangeHysteresis = Mathf.Max(0f, rangeHysteresis);
        orbitStepDistance = Mathf.Max(0f, orbitStepDistance);
        verticalSearchOffset = Mathf.Max(0f, verticalSearchOffset);
    }

    private void OnDrawGizmosSelected()
    {
        if (drawCollisionCapsuleGizmo)
        {
            Gizmos.color = collisionCapsuleGizmoColor;
            DrawCollisionCapsuleAt(transform.position);
        }

        if (!drawLastChasePassageGizmo ||
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
            lastPlannedDestination
        );
        DrawCollisionCapsuleAt(lastPlannedDestination);
        Gizmos.DrawLine(
            transform.position,
            lastPlannedDestination
        );
    }

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
