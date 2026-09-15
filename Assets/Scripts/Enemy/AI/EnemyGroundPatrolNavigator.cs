using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 地面巡邏：使用烘焙 NavMesh 計算完整路徑，由 Fusion Tick 推進路徑拐點。
/// 不需要 NavMeshAgent，也不允許另一個 Agent 自行更新同一個 Root。
/// 本階段不跨 OffMeshLink、不處理群體避讓；適合先驗證普通連通地面的巡邏。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMovementOwnership))]
public sealed class EnemyGroundPatrolNavigator : EnemyPatrolNavigator
{
    [Header("地面 NavMesh")]
    [SerializeField, Tooltip("NavMesh 的 Agent Type ID，預設 Humanoid 為 0。必須與烘焙的 Agent Type 一致。")]
    private int agentTypeId = 0;
    [SerializeField, Min(0.01f), Tooltip("從 Root 腳底／巡邏節點搜尋最近 NavMesh 的最大距離。建議 0.3，避免投影到另一層樓。")]
    private float navMeshSnapDistance = 0.3f;
    [SerializeField, Min(0.01f), Tooltip("允許每一步與 NavMesh 高度修正的最大距離。這不是跳躍高度；過大會跨樓層。")]
    private float maximumStepProjection = 0.25f;
    [SerializeField, Min(0f), Tooltip("離地後自行向下加速的重力，公尺／秒平方。Support／Tank 控制期間暫停本模組。")]
    private float gravity = 25f;
    [SerializeField, Min(0.01f), Tooltip("向下速度上限，公尺／秒。")]
    private float terminalFallSpeed = 30f;
    [SerializeField, Min(0.001f), Tooltip("離地小於這個距離時視為已落地，公尺。")]
    private float groundedProbeDistance = 0.08f;

    [Header("地面巡邏碰撞 Gizmos")]
    [SerializeField, Tooltip(
        "選取敵人時顯示實際用於 OverlapCapsule／CapsuleCast 的碰撞膠囊。\n" +
        "尺寸直接使用 Body Radius、Body Height 與 Collision Skin，不需要另外填一套 Gizmo 尺寸。")]
    private bool drawCollisionCapsuleGizmo = true;
    [SerializeField, Tooltip("Play Mode 顯示最近一次完整 NavMesh 路徑，以及每一段的膠囊掃掠通道。")]
    private bool drawLastNavMeshPathGizmo = true;
    [SerializeField, Tooltip("顯示腳底 Grounded Raycast 的起點、終點與方向。")]
    private bool drawGroundedProbeGizmo = true;
    [SerializeField, Tooltip("目前地面碰撞膠囊的 Gizmo 顏色。")]
    private Color collisionCapsuleGizmoColor =
        new Color(0.15f, 1f, 0.45f, 0.9f);
    [SerializeField, Tooltip("最近一次完整 NavMesh 路徑與合法碰撞通道的 Gizmo 顏色。")]
    private Color validPathGizmoColor =
        new Color(0.1f, 0.75f, 1f, 0.8f);
    [SerializeField, Tooltip("最近一次巡邏規劃失敗時，起點到要求目標的 Gizmo 顏色。")]
    private Color invalidPathGizmoColor =
        new Color(1f, 0.15f, 0.1f, 0.9f);
    [SerializeField, Tooltip("腳底確定接地時的 Ground Probe 顏色。Edit Mode 尚未執行物理檢查時也使用此顏色。")]
    private Color groundedProbeHitColor =
        new Color(0.2f, 1f, 0.2f, 0.9f);
    [SerializeField, Tooltip("Play Mode 腳底沒有命中 Obstacle Mask 時的 Ground Probe 顏色。")]
    private Color groundedProbeMissColor =
        new Color(1f, 0.25f, 0.1f, 0.9f);

    private NavMeshPath path;
    private Vector3[] corners;
    private int cornerIndex;
    private float fallSpeed;
    private bool grounded;
    private Vector3 destination;
    private bool hasLastPlanAttempt;
    private bool lastPlanWasComplete;
    private Vector3 lastPlanStart;
    private Vector3 lastPlanDestination;
    private Vector3[] lastPlannedCorners;
    public override EnemyLocomotionKind SupportedLocomotion => EnemyLocomotionKind.Ground;
    private NavMeshQueryFilter Filter => new NavMeshQueryFilter
        { agentTypeID = agentTypeId, areaMask = NavMesh.AllAreas };

    protected override void Awake()
    {
        base.Awake();
        path = new NavMeshPath();
    }

    public override bool TryBegin(Vector3 target, out Vector3 actualDestination)
    {
        actualDestination = target;
        Stop();
        ClearLastPlanFailure();
        hasLastPlanAttempt = true;
        lastPlanWasComplete = false;
        lastPlanStart = transform.position;
        lastPlanDestination = target;
        lastPlannedCorners = null;

        if (!TryValidateConfiguration(out string configurationDetail))
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.ConfigurationInvalid,
                configurationDetail
            );
            return false;
        }

        // 不依賴 FixedUpdateNetwork 的元件執行順序。
        // 即使本 Tick 先做待機決策，也先在這裡刷新一次腳底狀態。
        RefreshGroundedState();

        if (!grounded)
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.NotGrounded,
                $"腳底射線未命中 Obstacle Mask。Root={transform.position:F3}，" +
                $"Grounded Probe Distance={groundedProbeDistance:F3}。" +
                "請確認地板 Collider 的 Layer 有被 Obstacle Mask 勾選。"
            );
            return false;
        }

        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit start,
                navMeshSnapDistance,
                Filter))
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.StartNotOnNavMesh,
                $"敵人 Root 附近找不到 Agent Type {agentTypeId} 的 NavMesh。" +
                $"Root={transform.position:F3}，Snap Distance={navMeshSnapDistance:F3}。"
            );
            return false;
        }

        lastPlanStart = start.position;

        if (!NavMesh.SamplePosition(
                target,
                out NavMeshHit end,
                navMeshSnapDistance,
                Filter))
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.DestinationNotOnNavMesh,
                $"巡邏點附近找不到 Agent Type {agentTypeId} 的 NavMesh。" +
                $"Point={target:F3}，Snap Distance={navMeshSnapDistance:F3}。"
            );
            return false;
        }

        lastPlanDestination = end.position;

        if (!NavMesh.CalculatePath(
                start.position,
                end.position,
                Filter,
                path))
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.PathCalculationFailed,
                $"NavMesh.CalculatePath 回傳 false。Start={start.position:F3}，" +
                $"End={end.position:F3}，Agent Type={agentTypeId}。"
            );
            return false;
        }

        if (path.status != NavMeshPathStatus.PathComplete)
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.PathIncomplete,
                $"路徑狀態為 {path.status}，不是 PathComplete。" +
                $"Start={start.position:F3}，End={end.position:F3}。" +
                "請檢查 NavMesh 是否被斷開、門口是否過窄或兩點是否位於不同孤島。"
            );
            return false;
        }

        corners = path.corners;
        if (corners.Length == 0)
        {
            SetLastPlanFailure(
                EnemyPatrolPlanFailure.PathHasNoCorners,
                "CalculatePath 成功但 Path Corners 為空。請重新 Bake NavMesh 並確認起終點。"
            );
            return false;
        }

        destination = end.position;
        actualDestination = destination;
        cornerIndex = corners.Length > 1 ? 1 : 0;
        lastPlanWasComplete = true;
        lastPlannedCorners = (Vector3[])corners.Clone();
        ClearLastPlanFailure();
        return true;
    }

    public override EnemyPatrolMoveResult TickMove(float deltaTime, float arrivalDistance)
    {
        if (corners == null || !ConfigurationValid()) return EnemyPatrolMoveResult.Failed;
        if (!Ownership.CanMove || !grounded) return EnemyPatrolMoveResult.Moving;
        if (Vector3.Distance(transform.position, destination) <= arrivalDistance)
            return EnemyPatrolMoveResult.Arrived;
        while (cornerIndex < corners.Length - 1 &&
            Vector3.Distance(transform.position, corners[cornerIndex]) < 0.06f) cornerIndex++;
        Vector3 proposed = Vector3.MoveTowards(transform.position, corners[cornerIndex], patrolSpeed * deltaTime);
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit currentSurface,
                navMeshSnapDistance, Filter) ||
            !NavMesh.SamplePosition(proposed, out NavMeshHit surface, maximumStepProjection, Filter) ||
            // 不允許一步跨出 NavMesh 邊界，包含缺口與未實作的跳躍 Link。
            NavMesh.Raycast(currentSurface.position, surface.position, out _, Filter))
            return EnemyPatrolMoveResult.Failed;
        if (!TryMove(surface.position, deltaTime)) return EnemyPatrolMoveResult.Failed;
        return Vector3.Distance(transform.position, destination) <= arrivalDistance ?
            EnemyPatrolMoveResult.Arrived : EnemyPatrolMoveResult.Moving;
    }

    public override void TickSupport(float deltaTime)
    {
        if (!ConfigurationValid() || Ownership.IsExternallyMoved) { fallSpeed = 0f; return; }
        // 腳底 Root 正上方往下探測，保留少許容差。不在空中直接 Warp 回 NavMesh。
        RefreshGroundedState();
        if (grounded)
        {
            fallSpeed = 0f;
            return;
        }

        Vector3 origin = transform.position + Vector3.up * 0.1f;
        fallSpeed = Mathf.Min(terminalFallSpeed, fallSpeed + gravity * deltaTime);
        float step = fallSpeed * deltaTime;
        if (Runner.GetPhysicsScene().Raycast(origin, Vector3.down, out RaycastHit support,
                0.1f + step, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 landing = transform.position;
            landing.y = support.point.y;
            // 腳底落地射線決定高度，身體膠囊仍需確認沒有撞進牆體。
            if (IsSegmentClear(transform.position, landing + Vector3.up * collisionSkin))
                transform.position = landing + Vector3.up * collisionSkin;
            fallSpeed = 0f;
        }
        else TryMove(transform.position + Vector3.down * step, deltaTime);
    }

    public override void Stop() { corners = null; cornerIndex = 0; }

    private void RefreshGroundedState()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        grounded = Runner.GetPhysicsScene().Raycast(
            origin,
            Vector3.down,
            out _,
            0.1f + groundedProbeDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmosSelected()
    {
        if (drawCollisionCapsuleGizmo)
        {
            Gizmos.color = collisionCapsuleGizmoColor;
            DrawCollisionCapsuleAt(transform.position);
        }

        if (drawGroundedProbeGizmo)
        {
            Vector3 probeStart =
                transform.position + Vector3.up * 0.1f;
            Vector3 probeEnd =
                probeStart -
                Vector3.up * (0.1f + groundedProbeDistance);

            Gizmos.color =
                !Application.isPlaying || grounded
                    ? groundedProbeHitColor
                    : groundedProbeMissColor;

            Gizmos.DrawLine(probeStart, probeEnd);
            Gizmos.DrawWireSphere(
                probeEnd,
                Mathf.Max(0.015f, bodyRadius * 0.08f)
            );
        }

        if (!drawLastNavMeshPathGizmo ||
            !Application.isPlaying ||
            !hasLastPlanAttempt)
        {
            return;
        }

        Gizmos.color = lastPlanWasComplete
            ? validPathGizmoColor
            : invalidPathGizmoColor;

        if (lastPlanWasComplete &&
            lastPlannedCorners != null &&
            lastPlannedCorners.Length > 0)
        {
            DrawCollisionCapsuleAt(lastPlannedCorners[0]);

            for (int index = 0;
                 index < lastPlannedCorners.Length - 1;
                 index++)
            {
                Vector3 from = lastPlannedCorners[index];
                Vector3 to = lastPlannedCorners[index + 1];

                Gizmos.DrawLine(from, to);
                DrawCapsulePassage(from, to);
                DrawCollisionCapsuleAt(to);
                Gizmos.DrawWireSphere(
                    to,
                    Mathf.Max(0.03f, bodyRadius * 0.15f)
                );
            }
        }
        else
        {
            Gizmos.DrawLine(
                lastPlanStart,
                lastPlanDestination
            );
            DrawCapsulePassage(
                lastPlanStart,
                lastPlanDestination
            );
            DrawCollisionCapsuleAt(lastPlanDestination);
        }
    }

    /// <summary>依正式碰撞公式畫出 Root 腳底向上的垂直膠囊。</summary>
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

    /// <summary>畫出每一段移動時，垂直膠囊外圍的掃掠通道。</summary>
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
