using Fusion;
using UnityEngine;


/// <summary>
/// Chase A／B 的移動介面。
/// Brain 保存同步目的地，Motor 只規劃路徑與推進 Root。
/// </summary>
public abstract class EnemyChaseMotor :
    MonoBehaviour
{
    [Header("追逐位移共用")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("追逐移動速度，公尺／秒。個體差異可直接在各 Prefab Variant Override。")]
    protected float chaseSpeed =
        4f;

    [SerializeField]
    [Min(1f)]
    [Tooltip("追逐期間 Root 水平朝向速度，度／秒。攻擊鎖定方向後由攻擊能力接管。")]
    protected float rotationSpeed =
        240f;

    [SerializeField]
    [Tooltip("只勾場景實體障礙物。不得勾 Enemy 或 Player，避免掃掠命中自己或隊友。")]
    protected LayerMask obstacleMask;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("追逐碰撞膠囊半徑，公尺。應與上一階段巡邏 Navigator 相同。")]
    protected float bodyRadius =
        0.35f;

    [SerializeField]
    [Min(0.02f)]
    [Tooltip("追逐碰撞膠囊高度，公尺。Root 視為膠囊腳底。")]
    protected float bodyHeight =
        1.8f;

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("碰撞保留間距，公尺。")]
    protected float collisionSkin =
        0.02f;

    protected EnemyActor Actor
    {
        get;
        private set;
    }

    protected EnemyMovementOwnership Ownership
    {
        get;
        private set;
    }

    protected NetworkRunner Runner =>
        Actor.Runner;

    public abstract EnemyChaseKind SupportedChaseKind
    {
        get;
    }

    protected virtual void Awake()
    {
        Actor = GetComponent<EnemyActor>();
        Ownership =
            GetComponent<EnemyMovementOwnership>();
    }

    protected virtual void Start()
    {
        if (obstacleMask.value == 0)
        {
            Debug.LogError(
                "[Enemy Chase Motor] Obstacle Mask 不可為空，追逐暫停。",
                this
            );
        }
    }

    public abstract bool TryPlanDestination(
        NetworkObject target,
        Vector3 targetPosition,
        bool hasDirectSight,
        float minimumAttackDistance,
        float maximumAttackDistance,
        out Vector3 destination
    );

    public abstract bool TickMove(
        Vector3 destination,
        float deltaTime,
        float stoppingDistance,
        out float actualSpeed
    );

    public abstract void Stop();

    protected bool CanMove =>
        Actor != null &&
        Actor.IsStateAuthorityOwner &&
        Ownership != null &&
        Ownership.CanMove &&
        obstacleMask.value != 0;

    protected bool TryMoveRoot(
        Vector3 desiredPosition,
        float deltaTime
    )
    {
        return TryMoveRootInternal(
            desiredPosition,
            deltaTime,
            false,
            default
        );
    }

    /// <summary>
    /// 移動 Root，但旋轉持續朝向指定世界座標。
    /// 用於需要後退／側移、又不能背對玩家的行為；一般 Chase 仍沿用朝移動方向旋轉。
    /// </summary>
    protected bool TryMoveRootFacingPoint(
        Vector3 desiredPosition,
        Vector3 facingPoint,
        float deltaTime
    )
    {
        return TryMoveRootInternal(
            desiredPosition,
            deltaTime,
            true,
            facingPoint
        );
    }

    /// <summary>不移動 Root，只在允許旋轉時平滑面向指定世界座標。</summary>
    protected void RotateRootTowardPoint(
        Vector3 facingPoint,
        float deltaTime
    )
    {
        if (Ownership == null ||
            Ownership.CanRotate == false)
        {
            return;
        }

        Vector3 horizontalFacing =
            Vector3.ProjectOnPlane(
                facingPoint - transform.position,
                Vector3.up
            );

        if (horizontalFacing.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(horizontalFacing),
            rotationSpeed * deltaTime
        );
    }

    private bool TryMoveRootInternal(
        Vector3 desiredPosition,
        float deltaTime,
        bool useFacingPoint,
        Vector3 facingPoint
    )
    {
        Vector3 from = transform.position;
        Vector3 delta = desiredPosition - from;
        float distance = delta.magnitude;

        if (distance <= 0.00001f)
        {
            return true;
        }

        PhysicsScene physics =
            Runner.GetPhysicsScene();

        Vector3 bottom =
            from +
            Vector3.up *
            (bodyRadius + collisionSkin);

        Vector3 top =
            from +
            Vector3.up *
            Mathf.Max(
                bodyRadius + collisionSkin,
                bodyHeight - bodyRadius
            );

        if (physics.CapsuleCast(
                bottom,
                top,
                bodyRadius,
                delta / distance,
                out RaycastHit hit,
                distance + collisionSkin,
                obstacleMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            float allowedDistance =
                Mathf.Max(
                    0f,
                    hit.distance - collisionSkin
                );

            if (allowedDistance <= 0.0001f)
            {
                return false;
            }

            desiredPosition =
                from +
                delta.normalized * allowedDistance;
        }

        transform.position =
            desiredPosition;

        if (useFacingPoint)
        {
            RotateRootTowardPoint(
                facingPoint,
                deltaTime
            );
        }
        else
        {
            Vector3 horizontalHeading =
                Vector3.ProjectOnPlane(
                    desiredPosition - from,
                    Vector3.up
                );

            if (Ownership.CanRotate &&
                horizontalHeading.sqrMagnitude > 0.0001f)
            {
                transform.rotation =
                    Quaternion.RotateTowards(
                        transform.rotation,
                        Quaternion.LookRotation(horizontalHeading),
                        rotationSpeed * deltaTime
                    );
            }
        }

        return true;
    }

    protected bool IsSegmentClear(
        Vector3 from,
        Vector3 to
    )
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
        {
            return true;
        }

        Vector3 bottom =
            from + Vector3.up * bodyRadius;

        Vector3 top =
            from + Vector3.up *
            Mathf.Max(bodyRadius, bodyHeight - bodyRadius);

        return Runner.GetPhysicsScene().CapsuleCast(
            bottom,
            top,
            bodyRadius,
            delta / distance,
            out _,
            distance + collisionSkin,
            obstacleMask,
            QueryTriggerInteraction.Ignore
        ) == false;
    }

    protected virtual void OnValidate()
    {
        chaseSpeed = Mathf.Max(0.01f, chaseSpeed);
        rotationSpeed = Mathf.Max(1f, rotationSpeed);
        bodyRadius = Mathf.Max(0.01f, bodyRadius);
        bodyHeight = Mathf.Max(bodyRadius * 2f, bodyHeight);
        collisionSkin = Mathf.Max(0.001f, collisionSkin);
    }
}
