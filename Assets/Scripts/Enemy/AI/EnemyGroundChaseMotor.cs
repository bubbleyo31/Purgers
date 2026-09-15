using Fusion;
using UnityEngine;
using UnityEngine.AI;


/// <summary>
/// 追逐 A：地面 NavMesh 最短完整路徑，加上每隻敵人的環形接近偏移。
///
/// 偏移只避免所有近戰敵人瞄準完全相同的玩家 Root；
/// 本階段尚未實作動態群體避讓與正式位置預約。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMovementOwnership))]
public sealed class EnemyGroundChaseMotor :
    EnemyChaseMotor
{
    [Header("Chase A：NavMesh")]

    [SerializeField]
    [Tooltip("NavMesh Agent Type ID。必須與地面巡邏使用的烘焙類型一致；預設 Humanoid 通常為 0。")]
    private int agentTypeId;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("將玩家周圍接近點投影到最近 NavMesh 的最大距離，公尺。過大可能投影到另一層樓。")]
    private float navMeshSnapDistance =
        1f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "近戰接近位置使用最大攻擊距離的比例。\n" +
        "0.7 代表移動到最大攻擊距離內約 70% 的位置，留出網路與移動緩衝。")]
    private float attackRangeApproachRatio =
        0.7f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "玩家周圍環形位置的側向擾動比例。\n" +
        "數值越大，不同敵人越會從不同角度接近；0 代表沿當前最短方向。")]
    private float surroundVariation =
        0.35f;

    private NavMeshPath path;
    private Vector3[] corners;
    private int cornerIndex;
    private float personalAngleOffset;

    public override EnemyChaseKind SupportedChaseKind =>
        EnemyChaseKind.ChaseA;

    protected override void Awake()
    {
        base.Awake();
        path = new NavMeshPath();

        // 只有 State Authority 使用規劃結果；角度不需要額外同步。
        personalAngleOffset =
            Random.Range(-90f, 90f);
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
        destination = targetPosition;

        if (CanMove == false ||
            path == null)
        {
            return false;
        }

        Vector3 playerRootPosition =
            hasDirectSight &&
            target != null &&
            target.IsValid
                ? target.transform.position
                : targetPosition;

        NavMeshQueryFilter filter =
            new NavMeshQueryFilter
            {
                agentTypeID = agentTypeId,
                areaMask = NavMesh.AllAreas
            };

        if (NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit start,
                navMeshSnapDistance,
                filter
            ) == false)
        {
            corners = null;
            return false;
        }

        // 地面敵人只追玩家的 XZ。玩家跳躍或使用鈎索時，不能拿玩家的空中 Y
        // 去搜尋 NavMesh，否則超過 Snap Distance 後會錯誤判定無路可走。
        // 使用敵人目前所在 NavMesh 表面的高度，也避免多樓層場景投影到玩家所在的另一層。
        playerRootPosition.y = start.position.y;

        Vector3 awayFromPlayer =
            Vector3.ProjectOnPlane(
                transform.position - playerRootPosition,
                Vector3.up
            );

        if (awayFromPlayer.sqrMagnitude <= 0.0001f)
        {
            awayFromPlayer = -transform.forward;
        }

        float approachDistance =
            Mathf.Max(
                0.15f,
                Mathf.Lerp(
                    minimumAttackDistance,
                    maximumAttackDistance,
                    attackRangeApproachRatio
                )
            );

        float appliedAngle =
            personalAngleOffset *
            surroundVariation;

        Vector3 ringDirection =
            Quaternion.AngleAxis(
                appliedAngle,
                Vector3.up
            ) * awayFromPlayer.normalized;

        Vector3 desired =
            playerRootPosition +
            ringDirection * approachDistance;

        if (NavMesh.SamplePosition(
                desired,
                out NavMeshHit end,
                navMeshSnapDistance,
                filter
            ) == false ||
            NavMesh.CalculatePath(
                start.position,
                end.position,
                filter,
                path
            ) == false ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            corners = null;
            return false;
        }

        corners = path.corners;
        cornerIndex =
            corners.Length > 1
                ? 1
                : 0;

        destination = end.position;
        return corners.Length > 0;
    }

    public override bool TickMove(
        Vector3 destination,
        float deltaTime,
        float stoppingDistance,
        out float actualSpeed
    )
    {
        actualSpeed = 0f;

        if (CanMove == false ||
            corners == null ||
            corners.Length == 0)
        {
            return false;
        }

        if (Vector3.Distance(
                transform.position,
                destination
            ) <= stoppingDistance)
        {
            return true;
        }

        while (cornerIndex < corners.Length - 1 &&
               Vector3.Distance(
                   transform.position,
                   corners[cornerIndex]
               ) <= 0.08f)
        {
            cornerIndex++;
        }

        Vector3 before = transform.position;
        Vector3 next =
            Vector3.MoveTowards(
                before,
                corners[cornerIndex],
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

    /// <summary>
    /// 近戰 B 衝刺後只規劃一次固定重整點。
    /// 優先沿玩家到敵人的反方向退到指定距離；直退沒有完整 NavMesh 路徑時，
    /// 才依固定順序嘗試右 45、左 45、右 90、左 90 度，不使用每 Tick 隨機環形點。
    /// </summary>
    public bool TryPlanPostDashReposition(
        Vector3 targetPosition,
        float desiredDistance,
        out Vector3 destination
    )
    {
        destination = transform.position;

        if (CanMove == false ||
            path == null)
        {
            return false;
        }

        NavMeshQueryFilter filter =
            new NavMeshQueryFilter
            {
                agentTypeID = agentTypeId,
                areaMask = NavMesh.AllAreas
            };

        if (NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit start,
                navMeshSnapDistance,
                filter
            ) == false)
        {
            corners = null;
            return false;
        }

        Vector3 groundedTarget = targetPosition;
        groundedTarget.y = start.position.y;

        Vector3 awayFromTarget =
            Vector3.ProjectOnPlane(
                start.position - groundedTarget,
                Vector3.up
            );

        if (awayFromTarget.sqrMagnitude <= 0.0001f)
        {
            awayFromTarget = -transform.forward;
        }

        awayFromTarget.Normalize();

        // 固定搜尋順序是刻意的：同一次重整只採用第一個完整路徑，
        // 不會像一般包圍 Chase 一樣隨敵人位置持續旋轉目的地。
        float[] candidateAngles =
        {
            0f,
            45f,
            -45f,
            90f,
            -90f
        };

        for (int index = 0;
             index < candidateAngles.Length;
             index++)
        {
            Vector3 direction =
                Quaternion.AngleAxis(
                    candidateAngles[index],
                    Vector3.up
                ) * awayFromTarget;

            Vector3 requested =
                groundedTarget +
                direction * desiredDistance;

            if (NavMesh.SamplePosition(
                    requested,
                    out NavMeshHit end,
                    navMeshSnapDistance,
                    filter
                ) == false ||
                NavMesh.CalculatePath(
                    start.position,
                    end.position,
                    filter,
                    path
                ) == false ||
                path.status != NavMeshPathStatus.PathComplete ||
                path.corners == null ||
                path.corners.Length == 0)
            {
                continue;
            }

            corners = path.corners;
            cornerIndex =
                corners.Length > 1
                    ? 1
                    : 0;

            destination = end.position;
            return true;
        }

        corners = null;
        cornerIndex = 0;
        return false;
    }

    /// <summary>
    /// 沿已規劃的固定重整路徑移動，但不朝路徑方向轉身；
    /// Root 會持續面向玩家，因此呈現後退或側移，而不是背對玩家逃跑。
    /// </summary>
    public bool TickPostDashReposition(
        Vector3 destination,
        Vector3 targetPosition,
        float deltaTime,
        float stoppingDistance,
        float movementSpeed,
        out float actualSpeed
    )
    {
        actualSpeed = 0f;

        if (CanMove == false ||
            corners == null ||
            corners.Length == 0)
        {
            return false;
        }

        if (Vector3.Distance(
                transform.position,
                destination
            ) <= stoppingDistance)
        {
            RotateRootTowardPoint(
                targetPosition,
                deltaTime
            );
            return true;
        }

        while (cornerIndex < corners.Length - 1 &&
               Vector3.Distance(
                   transform.position,
                   corners[cornerIndex]
               ) <= 0.08f)
        {
            cornerIndex++;
        }

        Vector3 before = transform.position;
        Vector3 next = Vector3.MoveTowards(
            before,
            corners[cornerIndex],
            Mathf.Max(0.01f, movementSpeed) * deltaTime
        );

        if (TryMoveRootFacingPoint(
                next,
                targetPosition,
                deltaTime
            ) == false)
        {
            return false;
        }

        actualSpeed =
            Vector3.Distance(before, transform.position) /
            Mathf.Max(0.0001f, deltaTime);

        return true;
    }

    /// <summary>重整點已抵達時原地面向目前玩家。</summary>
    public void FacePostDashTarget(
        Vector3 targetPosition,
        float deltaTime
    )
    {
        RotateRootTowardPoint(
            targetPosition,
            deltaTime
        );
    }

    public override void Stop()
    {
        corners = null;
        cornerIndex = 0;
    }
}
