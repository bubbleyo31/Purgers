using Fusion;
using UnityEngine;
using UnityEngine.AI;


/// <summary>
/// 近戰 A／B 共用的直線衝擊攻擊。
///
/// A 可設定成短距離快速突進；B 設成較長、前搖明顯的直線衝刺。
/// Startup 期間朝向目標，進入 Active 的瞬間鎖定方向，之後不可轉向。
/// Active 結束後先減速 Braking，再進 Recovery，不瞬間把速度清零。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMovementOwnership))]
public sealed class EnemyMeleeDashAttack :
    EnemyCombatOption
{
    [Header("近戰衝擊時間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "正式開始衝刺前的前搖秒數。\n" +
        "傷害與衝刺由 TickTimer 觸發，不使用 Animation Event。")]
    private float startupSeconds =
        0.45f;

    [SerializeField]
    [Min(0.02f)]
    [Tooltip("直線衝刺最多維持幾秒。即使沒有碰撞也會在此時間後開始減速。")]
    private float maximumActiveSeconds =
        0.8f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("減速階段最多維持幾秒。速度會依 Braking Deceleration 降到 0。")]
    private float maximumBrakingSeconds =
        0.25f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("減速完成後的收招秒數。期間仍不能再次攻擊或防禦。")]
    private float recoverySeconds =
        0.45f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("整個攻擊完成或取消後的冷卻秒數。冷卻從能力結束後開始。")]
    private float cooldownSeconds =
        2f;

    [Header("衝刺移動")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Active 直線衝刺速度，公尺／秒。")]
    private float dashSpeed =
        10f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("一次攻擊允許移動的最大累積距離，公尺。")]
    private float maximumDashDistance =
        7f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Braking 每秒減少多少速度。數值越高越快停下，但仍不是瞬間歸零。")]
    private float brakingDeceleration =
        40f;

    [SerializeField]
    [Min(1f)]
    [Tooltip("Startup 期間朝向目前玩家的旋轉速度，度／秒。Active 後完全不再轉向。")]
    private float startupTurnSpeed =
        360f;

    [SerializeField]
    [Range(1f, 180f)]
    [Tooltip("允許開始攻擊時，玩家與敵人正前方的最大水平夾角。90 代表前方 180 度。")]
    private float startFacingHalfAngle =
        70f;

    [Header("碰撞與傷害")]

    [SerializeField]
    [Tooltip(
        "衝刺會碰撞的 Layer。必須包含 Player 與場景實體，不可包含 Enemy 自身 Layer。\n" +
        "最先撞到牆或玩家就停止造成後續衝刺傷害。")]
    private LayerMask dashHitMask;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("衝撞傷害球半徑，公尺。")]
    private float hitRadius =
        0.55f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("傷害球中心相對 Enemy Root 的向上高度，公尺。")]
    private float hitCenterHeight =
        0.9f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("撞到玩家時造成的基礎近戰傷害。只會命中一次。")]
    private float damage =
        25f;

    [Header("NavMesh 邊界保護")]

    [SerializeField]
    [Tooltip(
        "啟用後，Active 與 Braking 每個 Tick 移動前都會使用 NavMesh.Raycast 檢查下一段路。\n" +
        "若即將離開同 Agent Type 的可行走區域，敵人會停在邊界前並進入 Braking。\n\n" +
        "地面近戰 B 應保持勾選；只有刻意允許離開 NavMesh 的特殊飛行／跳躍怪才關閉。")]
    private bool stopBeforeLeavingNavMesh = true;

    [SerializeField]
    [Tooltip(
        "衝刺使用的 NavMesh Agent Type ID，必須與此敵人的地面巡邏、追逐與 NavMesh Bake 完全相同。\n" +
        "Humanoid 通常是 0，但請以 Navigation／NavMeshSurface 實際設定為準。")]
    private int navMeshAgentTypeId = 0;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "每 Tick 檢查前，允許從 Enemy Root 附近搜尋目前 NavMesh 起點的最大距離，單位為公尺。\n" +
        "建議與 Ground Chase Motor 的 Nav Mesh Snap Distance 相同；多樓層場景不要設太大。")]
    private float navMeshStartSampleDistance = 0.5f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "傷害球中心與 NavMesh 邊界之間保留的安全距離，單位為公尺。\n" +
        "程式會至少使用 Hit Radius，避免 Root 尚未出界但傷害球與身體已跨出可行走區。")]
    private float navMeshEdgeSafetyDistance = 0.6f;

    [Header("近戰 B 轉身閃避規則")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Active 期間，鎖定玩家連續位於固定衝刺方向後方多久就取消衝刺。\n\n" +
        "近戰 B 設 1.5。近戰 A 若不需要此規則可設 0。\n" +
        "玩家回到前方時會重置連續計時。")]
    private float cancelWhenTargetBehindSeconds =
        1.5f;

    [Header("除錯")]

    [SerializeField]
    [Tooltip("開啟後輸出衝刺命中、碰牆、距離結束與玩家離開後方視野等原因。")]
    private bool debugMeleeDash;

    [Header("近戰 B Gizmos")]

    [SerializeField]
    [Tooltip("選取敵人時顯示目前正前方的最大衝刺距離與傷害球半徑。攻擊開始後實際方向會在 Active 瞬間鎖定。")]
    private bool drawDashGizmos =
        true;

    [SerializeField]
    [Tooltip("衝刺路徑與傷害球 Gizmos 的顏色。")]
    private Color dashGizmoColor =
        new Color(1f, 0.1f, 0.1f, 0.85f);

    [Networked]
    public TickTimer PhaseTimer
    {
        get;
        private set;
    }

    [Networked]
    public TickTimer CooldownTimer
    {
        get;
        private set;
    }

    [Networked]
    private TickTimer BehindTimer
    {
        get;
        set;
    }

    [Networked]
    public Vector3 LockedDashDirection
    {
        get;
        private set;
    }

    [Networked]
    public float CurrentDashSpeed
    {
        get;
        private set;
    }

    [Networked]
    public float TravelledDistance
    {
        get;
        private set;
    }

    [Networked]
    public int DamageSequence
    {
        get;
        private set;
    }

    [Networked]
    public bool HasDealtDamageThisDash
    {
        get;
        private set;
    }

    /// <summary>
    /// 每次近戰 B 正常完成 Recovery 時增加一次。
    /// EnemyChaseBrain 只消費新序號一次，用來啟動衝刺後專用重整；取消攻擊不會增加。
    /// </summary>
    [Networked]
    public int CompletedDashSequence
    {
        get;
        private set;
    }

    private EnemyMovementOwnership movementOwnership;
    private readonly Collider[] activeContactBuffer =
        new Collider[16];

    public override EnemyActionState ActionState =>
        EnemyActionState.Attack;

    public override EnemyActionLockFlags LocksWhileActive =>
        EnemyActionLockFlags.Movement |
        EnemyActionLockFlags.Rotation |
        EnemyActionLockFlags.Navigation |
        EnemyActionLockFlags.Attack |
        EnemyActionLockFlags.Defense;

    private void Awake()
    {
        movementOwnership =
            GetComponent<EnemyMovementOwnership>();
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority == false)
        {
            return;
        }

        PhaseTimer = TickTimer.None;
        CooldownTimer = TickTimer.None;
        BehindTimer = TickTimer.None;
        LockedDashDirection = transform.forward;
        CurrentDashSpeed = 0f;
        TravelledDistance = 0f;
        DamageSequence = 0;
        HasDealtDamageThisDash = false;
        CompletedDashSequence = 0;
    }

    public override bool IsCooldownReady(
        NetworkRunner runner
    )
    {
        return CooldownTimer.ExpiredOrNotRunning(runner);
    }

    public override bool CanStartOption(
        in EnemyCombatContext context
    )
    {
        Vector3 toTarget =
            Vector3.ProjectOnPlane(
                context.TargetPosition - transform.position,
                Vector3.up
            );

        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return Vector3.Angle(
                   transform.forward,
                   toTarget
               ) <= startFacingHalfAngle &&
               movementOwnership != null &&
               movementOwnership.IsExternallyMoved == false &&
               dashHitMask.value != 0;
    }

    public override void BeginOption(
        in EnemyCombatContext context
    )
    {
        TravelledDistance = 0f;
        CurrentDashSpeed = 0f;
        BehindTimer = TickTimer.None;
        HasDealtDamageThisDash = false;
        UpdateStartupFacing(context);

        PhaseTimer =
            startupSeconds > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    startupSeconds
                )
                : TickTimer.None;
    }

    public override EnemyCombatOptionTickResult TickOption(
        in EnemyCombatContext context
    )
    {
        if (movementOwnership == null ||
            movementOwnership.IsExternallyMoved)
        {
            StartCooldown();
            return EnemyCombatOptionTickResult.Cancelled;
        }

        switch (context.Controller.CurrentActionPhase)
        {
            case EnemyCombatActionPhase.Startup:
                return TickStartup(context);

            case EnemyCombatActionPhase.Active:
                return TickActive(context);

            case EnemyCombatActionPhase.Braking:
                return TickBraking(context);

            case EnemyCombatActionPhase.Recovery:
                if (PhaseTimer.ExpiredOrNotRunning(Runner))
                {
                    StartCooldown();
                    CompletedDashSequence++;
                    return EnemyCombatOptionTickResult.Completed;
                }

                return EnemyCombatOptionTickResult.Running;

            default:
                StartCooldown();
                return EnemyCombatOptionTickResult.Cancelled;
        }
    }

    public override void CancelOption(
        in EnemyCombatContext context
    )
    {
        CurrentDashSpeed = 0f;
        BehindTimer = TickTimer.None;
        PhaseTimer = TickTimer.None;
        StartCooldown();
    }

    private EnemyCombatOptionTickResult TickStartup(
        in EnemyCombatContext context
    )
    {
        UpdateStartupFacing(context);

        if (PhaseTimer.ExpiredOrNotRunning(Runner) == false)
        {
            return EnemyCombatOptionTickResult.Running;
        }

        Vector3 horizontal =
            Vector3.ProjectOnPlane(
                context.TargetPosition - transform.position,
                Vector3.up
            );

        if (horizontal.sqrMagnitude <= 0.0001f)
        {
            StartCooldown();
            return EnemyCombatOptionTickResult.Cancelled;
        }

        LockedDashDirection =
            horizontal.normalized;

        transform.rotation =
            Quaternion.LookRotation(LockedDashDirection);

        CurrentDashSpeed = dashSpeed;
        PhaseTimer = TickTimer.CreateFromSeconds(
            Runner,
            maximumActiveSeconds
        );

        context.Controller.TrySetActionPhase(
            this,
            EnemyCombatActionPhase.Active
        );

        return EnemyCombatOptionTickResult.Running;
    }

    private EnemyCombatOptionTickResult TickActive(
        in EnemyCombatContext context
    )
    {
        // Active 本身就是完整傷害窗口，不再使用額外的傷害開始時間。
        // 先檢查目前已重疊的玩家，避免低速、貼身起步或單 Tick 位移過短時漏判。
        if (TryDamageOverlappingPlayer(context))
        {
            BeginBraking("Hit Radius 接觸玩家");
            return EnemyCombatOptionTickResult.Running;
        }

        if (ShouldCancelForTargetBehind(context))
        {
            BeginBraking("玩家連續位於固定衝刺方向後方");
            return EnemyCombatOptionTickResult.Running;
        }

        if (PhaseTimer.ExpiredOrNotRunning(Runner) ||
            TravelledDistance >= maximumDashDistance)
        {
            BeginBraking("達到最大時間或距離");
            return EnemyCombatOptionTickResult.Running;
        }

        float step =
            Mathf.Min(
                CurrentDashSpeed * context.DeltaTime,
                maximumDashDistance - TravelledDistance
            );

        if (step <= 0f)
        {
            BeginBraking("移動距離為零");
            return EnemyCombatOptionTickResult.Running;
        }

        if (!TryLimitStepToNavMesh(
                step,
                out float navMeshAllowedStep,
                out bool reachesNavMeshEdge
            ))
        {
            BeginBraking("目前位置附近找不到指定 Agent Type 的 NavMesh");
            return EnemyCombatOptionTickResult.Running;
        }

        step = navMeshAllowedStep;

        if (step <= 0.0001f)
        {
            BeginBraking("即將離開 NavMesh 可行走區域");
            return EnemyCombatOptionTickResult.Running;
        }

        Vector3 origin =
            transform.position +
            Vector3.up * hitCenterHeight;

        if (Runner.GetPhysicsScene().SphereCast(
                origin,
                hitRadius,
                LockedDashDirection,
                out RaycastHit hit,
                step,
                dashHitMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            Vector3 allowedMove =
                LockedDashDirection *
                Mathf.Max(0f, hit.distance - 0.02f);

            transform.position += allowedMove;
            TravelledDistance += allowedMove.magnitude;

            if (TryDamagePlayerCollider(
                    context,
                    hit.collider,
                    hit.point,
                    hit.normal
                ))
            {
                BeginBraking("命中玩家");
            }
            else
            {
                BeginBraking("撞到場景障礙");
            }

            return EnemyCombatOptionTickResult.Running;
        }

        transform.position +=
            LockedDashDirection * step;

        TravelledDistance += step;

        if (reachesNavMeshEdge)
        {
            BeginBraking("抵達 NavMesh 邊界前安全距離");
        }

        return EnemyCombatOptionTickResult.Running;
    }

    private EnemyCombatOptionTickResult TickBraking(
        in EnemyCombatContext context
    )
    {
        CurrentDashSpeed =
            Mathf.MoveTowards(
                CurrentDashSpeed,
                0f,
                brakingDeceleration * context.DeltaTime
            );

        float step =
            CurrentDashSpeed * context.DeltaTime;

        float navMeshAllowedStep = 0f;
        bool reachesNavMeshEdge = false;
        bool canUseStep =
            step > 0f &&
            TryLimitStepToNavMesh(
                step,
                out navMeshAllowedStep,
                out reachesNavMeshEdge
            );

        step = canUseStep
            ? navMeshAllowedStep
            : 0f;

        if (step > 0f &&
            Runner.GetPhysicsScene().SphereCast(
                transform.position +
                Vector3.up * hitCenterHeight,
                hitRadius,
                LockedDashDirection,
                out RaycastHit hit,
                step,
                dashHitMask,
                QueryTriggerInteraction.Ignore
            ) == false)
        {
            transform.position +=
                LockedDashDirection * step;

            if (reachesNavMeshEdge)
            {
                CurrentDashSpeed = 0f;
            }
        }
        else if (CurrentDashSpeed > 0f)
        {
            CurrentDashSpeed = 0f;
        }

        if (CurrentDashSpeed <= 0.001f ||
            PhaseTimer.ExpiredOrNotRunning(Runner))
        {
            CurrentDashSpeed = 0f;
            BeginRecovery(context);
        }

        return EnemyCombatOptionTickResult.Running;
    }

    /// <summary>
    /// Active 衝刺期間檢查傷害球目前是否已與玩家重疊。
    /// 一次 Dash 最多成功選中一名玩家；Networked 防彈跳旗標會在送出傷害前先設為 true。
    /// </summary>
    private bool TryDamageOverlappingPlayer(
        in EnemyCombatContext context
    )
    {
        if (HasDealtDamageThisDash)
        {
            return false;
        }

        Vector3 center =
            transform.position +
            Vector3.up * hitCenterHeight;

        int count = Runner.GetPhysicsScene().OverlapSphere(
            center,
            hitRadius,
            activeContactBuffer,
            dashHitMask,
            QueryTriggerInteraction.Ignore
        );

        for (int index = 0; index < count; index++)
        {
            Collider candidate = activeContactBuffer[index];

            if (TryDamagePlayerCollider(
                    context,
                    candidate,
                    candidate != null
                        ? candidate.ClosestPoint(center)
                        : center,
                    -LockedDashDirection
                ))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 將一個碰撞器解析成存活 PlayerHealth，並執行本次 Dash 唯一一次傷害。
    /// 旗標必須先於 DamageRequest 設定，才能擋住同玩家多 Collider 與後續 Tick 的重複接觸。
    /// </summary>
    private bool TryDamagePlayerCollider(
        in EnemyCombatContext context,
        Collider candidate,
        Vector3 hitPoint,
        Vector3 hitNormal
    )
    {
        if (HasDealtDamageThisDash || candidate == null)
        {
            return false;
        }

        PlayerHealth playerHealth =
            candidate.GetComponentInParent<PlayerHealth>();

        if (playerHealth == null ||
            playerHealth.Object == null ||
            playerHealth.Object.IsValid == false ||
            playerHealth.IsAlive == false)
        {
            return false;
        }

        // 防彈跳的關鍵：先封鎖本次 Dash，再送出傷害。
        // 即使玩家同時有多個 Collider，或傷害流程在同 Tick 觸發其他回呼，也不會重複送出。
        HasDealtDamageThisDash = true;
        DamageSequence++;

        EnemyDamageUtility.TryDamagePlayer(
            context.Actor,
            playerHealth.Object,
            candidate.gameObject,
            hitPoint,
            hitNormal,
            LockedDashDirection,
            damage,
            DamageType.Melee,
            DamageSequence,
            out _
        );

        return true;
    }

    /// <summary>
    /// 使用與地面導航相同 Agent Type 的 NavMesh.Raycast 限制本 Tick 位移。
    /// 回傳 false 代表敵人目前附近連 NavMesh 起點都找不到，呼叫端必須停止衝刺。
    /// </summary>
    private bool TryLimitStepToNavMesh(
        float requestedStep,
        out float allowedStep,
        out bool reachesEdge
    )
    {
        allowedStep = requestedStep;
        reachesEdge = false;

        if (!stopBeforeLeavingNavMesh)
        {
            return true;
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = navMeshAgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit startHit,
                navMeshStartSampleDistance,
                filter
            ))
        {
            allowedStep = 0f;
            reachesEdge = true;
            return false;
        }

        float safetyDistance = Mathf.Max(
            hitRadius,
            navMeshEdgeSafetyDistance
        );

        // 不只看本 Tick 的步長，還要多探測一段安全距離。
        // 否則必須等 Root 幾乎到邊界時 Raycast 才會命中，無法真正停在 Hit Radius 之外。
        Vector3 requestedEnd =
            startHit.position +
            LockedDashDirection *
            (requestedStep + safetyDistance);

        if (!NavMesh.Raycast(
                startHit.position,
                requestedEnd,
                out NavMeshHit edgeHit,
                filter
            ))
        {
            return true;
        }

        float distanceToEdge = Mathf.Max(
            0f,
            Vector3.Dot(
                edgeHit.position - startHit.position,
                LockedDashDirection
            )
        );

        allowedStep = Mathf.Clamp(
            distanceToEdge - safetyDistance,
            0f,
            requestedStep
        );
        reachesEdge = true;
        return true;
    }

    private void UpdateStartupFacing(
        in EnemyCombatContext context
    )
    {
        Vector3 horizontal =
            Vector3.ProjectOnPlane(
                context.TargetPosition - transform.position,
                Vector3.up
            );

        if (horizontal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation =
            Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(horizontal),
                startupTurnSpeed * context.DeltaTime
            );
    }

    private bool ShouldCancelForTargetBehind(
        in EnemyCombatContext context
    )
    {
        if (cancelWhenTargetBehindSeconds <= 0f)
        {
            return false;
        }

        Vector3 toTarget =
            Vector3.ProjectOnPlane(
                context.TargetPosition - transform.position,
                Vector3.up
            );

        bool isBehind =
            toTarget.sqrMagnitude > 0.0001f &&
            Vector3.Dot(
                LockedDashDirection,
                toTarget.normalized
            ) < 0f;

        if (isBehind == false)
        {
            BehindTimer = TickTimer.None;
            return false;
        }

        if (BehindTimer.IsRunning == false)
        {
            BehindTimer = TickTimer.CreateFromSeconds(
                Runner,
                cancelWhenTargetBehindSeconds
            );
        }

        return BehindTimer.Expired(Runner);
    }

    private void BeginBraking(
        string reason
    )
    {
        BehindTimer = TickTimer.None;
        PhaseTimer =
            maximumBrakingSeconds > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    maximumBrakingSeconds
                )
                : TickTimer.None;

        GetComponent<EnemyCombatDecisionController>()
            .TrySetActionPhase(
                this,
                EnemyCombatActionPhase.Braking
            );

        if (debugMeleeDash)
        {
            Debug.Log(
                $"[{nameof(EnemyMeleeDashAttack)}] 開始減速：{reason}",
                this
            );
        }
    }

    private void BeginRecovery(
        in EnemyCombatContext context
    )
    {
        PhaseTimer =
            recoverySeconds > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    recoverySeconds
                )
                : TickTimer.None;

        context.Controller.TrySetActionPhase(
            this,
            EnemyCombatActionPhase.Recovery
        );
    }

    private void StartCooldown()
    {
        CooldownTimer =
            cooldownSeconds > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    cooldownSeconds
                )
                : TickTimer.None;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        startupSeconds = Mathf.Max(0f, startupSeconds);
        maximumActiveSeconds = Mathf.Max(0.02f, maximumActiveSeconds);
        maximumBrakingSeconds = Mathf.Max(0f, maximumBrakingSeconds);
        recoverySeconds = Mathf.Max(0f, recoverySeconds);
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        dashSpeed = Mathf.Max(0.01f, dashSpeed);
        maximumDashDistance = Mathf.Max(0.01f, maximumDashDistance);
        brakingDeceleration = Mathf.Max(0.01f, brakingDeceleration);
        hitRadius = Mathf.Max(0.01f, hitRadius);
        hitCenterHeight = Mathf.Max(0f, hitCenterHeight);
        damage = Mathf.Max(0f, damage);
        navMeshStartSampleDistance =
            Mathf.Max(0.01f, navMeshStartSampleDistance);
        navMeshEdgeSafetyDistance =
            Mathf.Max(hitRadius, navMeshEdgeSafetyDistance);
        cancelWhenTargetBehindSeconds =
            Mathf.Max(0f, cancelWhenTargetBehindSeconds);
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (drawDashGizmos == false)
        {
            return;
        }

        Vector3 start =
            transform.position +
            Vector3.up * hitCenterHeight;

        Vector3 end =
            start +
            transform.forward * maximumDashDistance;

        Gizmos.color = dashGizmoColor;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(start, hitRadius);
        Gizmos.DrawWireSphere(end, hitRadius);

        if (stopBeforeLeavingNavMesh)
        {
            Gizmos.color = new Color(
                dashGizmoColor.r,
                dashGizmoColor.g,
                dashGizmoColor.b,
                0.35f
            );
            Gizmos.DrawWireSphere(
                transform.position,
                Mathf.Max(hitRadius, navMeshEdgeSafetyDistance)
            );
        }
    }
}
