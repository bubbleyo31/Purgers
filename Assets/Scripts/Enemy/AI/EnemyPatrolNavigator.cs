using Fusion;
using UnityEngine;

public enum EnemyPatrolMoveResult { Moving, Arrived, Failed }

public enum EnemyPatrolPlanFailure : byte
{
    None = 0,
    ConfigurationInvalid = 1,
    NotGrounded = 2,
    StartNotOnNavMesh = 3,
    DestinationNotOnNavMesh = 4,
    PathCalculationFailed = 5,
    PathIncomplete = 6,
    PathHasNoCorners = 7,
    FlyingPathObstructed = 8
}

/// <summary>
/// 巡邏導航的可替換介面。Brain 決定「何時、去哪裡」，Navigator 處理路徑與位移。
/// 只有 Brain 在 State Authority 的 FixedUpdateNetwork 呼叫 TickMove，Proxy 不執行 AI。
/// 後續追逐導航可另行擴充，不需要把戰鬥規則塞進巡邏模組。
/// </summary>
public abstract class EnemyPatrolNavigator : MonoBehaviour
{
    [Header("巡邏位移")]
    [SerializeField, Min(0.01f), Tooltip("巡邏移動速度，公尺／秒。地面與飛行可分別設定。")]
    protected float patrolSpeed = 2f;
    [SerializeField, Min(1f), Tooltip("巡邏時水平朝向的最大角速度，度／秒。")]
    private float rotationSpeed = 180f;
    [SerializeField, Tooltip("只勾場景實體障礙物 Layer，不得勾 Enemy 或 Player。移動仍以掃掠碰撞檢查保護。")]
    protected LayerMask obstacleMask;
    [SerializeField, Min(0.01f), Tooltip("移動碰撞膠囊半徑，公尺。必須包住模型的主要身體寬度。")]
    protected float bodyRadius = 0.35f;
    [SerializeField, Min(0.02f), Tooltip("移動碰撞膠囊高度，公尺，至少兩倍半徑。敵人 Root 位於膠囊底部。")]
    protected float bodyHeight = 1.8f;
    [SerializeField, Min(0.001f), Tooltip("碰撞保留間距，公尺，避免角色貼牆後下一次移動直接穿入。")]
    protected float collisionSkin = 0.02f;

    protected EnemyActor Actor { get; private set; }
    protected NetworkRunner Runner => Actor.Runner;
    protected EnemyMovementOwnership Ownership { get; private set; }
    public float Speed => patrolSpeed;
    public abstract EnemyLocomotionKind SupportedLocomotion { get; }

    /// <summary>最近一次 TryBegin 失敗的穩定分類；成功時為 None。</summary>
    public EnemyPatrolPlanFailure LastPlanFailure { get; private set; }

    /// <summary>最近一次 TryBegin 的人類可讀細節，只用於 Inspector／Console 除錯。</summary>
    public string LastPlanFailureDetail { get; private set; } = string.Empty;

    protected virtual void Awake()
    {
        Actor = GetComponent<EnemyActor>();
        Ownership = GetComponent<EnemyMovementOwnership>();
    }

    protected virtual void Start()
    {
        if (obstacleMask.value == 0)
            Debug.LogError("[Enemy Navigator] 必須設定 Obstacle Mask 才能巡邏。", this);
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
            Debug.LogError("[Enemy Navigator] Root Rigidbody 必須 Is Kinematic，避免與 Tick Motor 搶寫位置。", this);
        UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled)
            Debug.LogError("[Enemy Navigator] 請停用既有 NavMeshAgent，本元件自行推進路徑。", this);
    }

    /// <summary>必須在進入 Patrol 前確認可達。只回傳完整合法路徑，失敗不改變位置。</summary>
    public abstract bool TryBegin(Vector3 destination, out Vector3 actualDestination);
    public abstract EnemyPatrolMoveResult TickMove(float deltaTime, float arrivalDistance);
    public abstract void Stop();

    /// <summary>地面版可在非巡邏時維持落地重力；飛行版預設懸浮。</summary>
    public virtual void TickSupport(float deltaTime) { }

    protected bool ConfigurationValid()
    {
        return TryValidateConfiguration(out _);
    }

    protected bool TryValidateConfiguration(out string detail)
    {
        if (Actor == null)
        {
            detail = "EnemyActor 引用為空。";
            return false;
        }

        if (!Actor.IsFusionSpawned)
        {
            detail = "EnemyActor 尚未完成 Spawned。";
            return false;
        }

        if (!Actor.IsStateAuthorityOwner)
        {
            detail = "目前執行端不是敵人的 State Authority。";
            return false;
        }

        if (Ownership == null)
        {
            detail = "缺少 EnemyMovementOwnership。";
            return false;
        }

        if (obstacleMask.value == 0)
        {
            detail = "Obstacle Mask 為空。地面版必須包含地板與牆壁。";
            return false;
        }

        Rigidbody body = GetComponent<Rigidbody>();
        // Transform Motor 與動態 Rigidbody 不可同時寫入。由 Inspector 決定，不暗中替使用者改元件。
        UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();

        if (body != null && !body.isKinematic)
        {
            detail = "Root Rigidbody 的 Is Kinematic 尚未開啟。";
            return false;
        }

        if (agent != null && agent.enabled)
        {
            detail = "Root 上仍有啟用中的 NavMeshAgent，會與 Tick Motor 搶寫位置。";
            return false;
        }

        detail = string.Empty;
        return true;
    }

    protected void ClearLastPlanFailure()
    {
        LastPlanFailure = EnemyPatrolPlanFailure.None;
        LastPlanFailureDetail = string.Empty;
    }

    protected void SetLastPlanFailure(
        EnemyPatrolPlanFailure failure,
        string detail
    )
    {
        LastPlanFailure = failure;
        LastPlanFailureDetail = detail ?? string.Empty;
    }

    /// <summary>
    /// 膠囊掃掠整段位移；先檢查起點重疊，再檢查路上碰撞。
    /// obstacleMask 必須排除自己的 Enemy Collider。
    /// </summary>
    protected bool IsSegmentClear(Vector3 from, Vector3 to)
    {
        Vector3 bottom = from + Vector3.up * (bodyRadius + collisionSkin);
        Vector3 top = from + Vector3.up * Mathf.Max(bodyRadius + collisionSkin, bodyHeight - bodyRadius);
        PhysicsScene physics = Runner.GetPhysicsScene();
        // 單一暫存槽已足以回答是否重疊；不需要列出全部碰撞器。
        if (physics.OverlapCapsule(bottom, top, bodyRadius, overlapBuffer,
            obstacleMask, QueryTriggerInteraction.Ignore) > 0) return false;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        return distance < 0.00001f || !physics.CapsuleCast(bottom, top, bodyRadius,
            delta / distance, out _, distance + collisionSkin, obstacleMask,
            QueryTriggerInteraction.Ignore);
    }
    private readonly Collider[] overlapBuffer = new Collider[1];

    protected bool TryMove(Vector3 position, float deltaTime)
    {
        if (!IsSegmentClear(transform.position, position)) return false;
        Vector3 heading = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
        transform.position = position;
        if (Ownership.CanRotate && heading.sqrMagnitude > 0.00001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(heading), rotationSpeed * deltaTime);
        return true;
    }

    protected virtual void OnValidate()
    {
        patrolSpeed = Mathf.Max(0.01f, patrolSpeed);
        bodyRadius = Mathf.Max(0.01f, bodyRadius);
        bodyHeight = Mathf.Max(bodyRadius * 2f, bodyHeight);
        collisionSkin = Mathf.Max(0.001f, collisionSkin);
    }
}
