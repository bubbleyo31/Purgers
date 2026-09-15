using Fusion;
using UnityEngine;


/// <summary>
/// Chase State 的共用驅動器。
///
/// Definition 決定 Chase A／B；Prefab 上的 Motor 必須符合。
/// 目的地週期性重算，玩家小幅移動不會每 Tick 讓遠程敵人同步抖動。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyActor))]
[RequireComponent(typeof(EnemyPerceptionController))]
[RequireComponent(typeof(EnemyCombatDecisionController))]
public sealed class EnemyChaseBrain :
    NetworkBehaviour
{
    [Header("核心引用")]

    [SerializeField]
    [Tooltip("同 Root 的 EnemyActor。留空自動取得。")]
    private EnemyActor enemyActor;

    [SerializeField]
    [Tooltip("同 Root 的 EnemyPerceptionController。留空自動取得。")]
    private EnemyPerceptionController perception;

    [SerializeField]
    [Tooltip("同 Root 的 EnemyCombatDecisionController。留空自動取得。")]
    private EnemyCombatDecisionController combatDecision;

    [SerializeField]
    [Tooltip("同 Root、符合 Definition Chase Kind 的 Ground／Flying Chase Motor。留空自動取得。")]
    private EnemyChaseMotor chaseMotor;

    [Header("目的地更新")]

    [SerializeField]
    [Min(0.05f)]
    [Tooltip(
        "最短重新規劃間隔，秒。\n" +
        "Chase A 建議 0.15～0.25；Chase B 建議 0.3～0.5，降低同步跟隨感。")]
    private float repathIntervalSeconds =
        0.25f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家相對上次規劃位置移動超過多少公尺才提早重算。0 代表只依固定間隔。")]
    private float targetMovementRepathDistance =
        1f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("追逐 Root 距離規劃目的地多少公尺時停止本輪移動，等待下一次規劃。")]
    private float destinationStoppingDistance =
        0.2f;

    [SerializeField]
    [Tooltip("開啟後輸出目的地規劃失敗。大量敵人時建議關閉。")]
    private bool debugChase;

    [Header("近戰 B：衝刺後重整")]

    [SerializeField]
    [Tooltip(
        "只對同 Root 掛有 EnemyMeleeDashAttack 的地面近戰 B 生效。\n" +
        "Dash 正常完成 Recovery 後，在冷卻期間只規劃一次固定重整點，移動與等待時持續面向玩家。\n" +
        "近戰 A 與飛行遠程敵人不會進入此流程。")]
    private bool enableMeleeBDashReposition = true;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip(
        "Dash 完成後希望與玩家保持的水平距離，單位為公尺。\n" +
        "建議放在 Melee B 的 Minimum Start Distance 與 Maximum Start Distance 之間，讓冷卻結束後可再次進入衝刺前搖。")]
    private float meleeBRepositionDistance = 5.5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "目前水平距離與重整距離相差不超過此值時，不再移動，只原地面向玩家等待冷卻。\n" +
        "避免角色在理想距離附近為幾公分誤差反覆前後移動。")]
    private float meleeBRepositionDistanceTolerance = 0.4f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("衝刺後退讓／側移速度，公尺／秒。這只影響重整，不修改一般 Chase Speed。")]
    private float meleeBRepositionMoveSpeed = 3f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Root 距固定重整點小於此距離時視為抵達，之後停止位移並持續面向玩家。")]
    private float meleeBRepositionStoppingDistance = 0.2f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip(
        "重整期間玩家若已離敵人超過此水平距離，就提前放棄重整並交回一般 Chase。\n" +
        "避免玩家快速逃遠時，近戰 B 還固執地走向舊重整點。")]
    private float meleeBRepositionAbortTargetDistance = 12f;

    [SerializeField]
    [Tooltip("開啟後輸出近戰 B 重整開始、抵達、失敗與結束原因。大量敵人時建議關閉。")]
    private bool debugMeleeBReposition;

    [Header("追逐 Runtime Gizmos")]

    [SerializeField]
    [Tooltip("Play Mode 選取敵人時，顯示目前追逐目的地與 Root 之間的連線。")]
    private bool drawCurrentChaseDestination =
        true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("目前追逐目的地 Gizmos 球體半徑，公尺。")]
    private float chaseDestinationGizmoRadius =
        0.3f;

    [SerializeField]
    [Tooltip("目前追逐目的地與連線的 Gizmos 顏色。")]
    private Color chaseDestinationGizmoColor =
        new Color(1f, 0.2f, 0.8f, 0.9f);

    [Networked]
    public bool HasChaseDestination
    {
        get;
        private set;
    }

    [Networked]
    public Vector3 ChaseDestination
    {
        get;
        private set;
    }

    [Networked]
    public float MoveSpeed
    {
        get;
        private set;
    }

    [Networked]
    public int DestinationSequence
    {
        get;
        private set;
    }

    [Networked]
    private TickTimer RepathTimer
    {
        get;
        set;
    }

    [Networked]
    private Vector3 TargetPositionAtLastPlan
    {
        get;
        set;
    }

    [Networked]
    public bool IsMeleeBDashRepositioning
    {
        get;
        private set;
    }

    [Networked]
    public bool HasMeleeBRepositionDestination
    {
        get;
        private set;
    }

    [Networked]
    public Vector3 MeleeBRepositionDestination
    {
        get;
        private set;
    }

    [Networked]
    private bool MeleeBRepositionPlanResolved
    {
        get;
        set;
    }

    [Networked]
    private int HandledDashCompletionSequence
    {
        get;
        set;
    }

    private bool fusionSpawned;
    private bool configurationValid;
    private EnemyMeleeDashAttack meleeDashAttack;
    private EnemyGroundChaseMotor groundChaseMotor;

    public bool IsFusionSpawned =>
        fusionSpawned &&
        Object != null &&
        Object.IsValid;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
        repathIntervalSeconds =
            Mathf.Max(0.05f, repathIntervalSeconds);
        targetMovementRepathDistance =
            Mathf.Max(0f, targetMovementRepathDistance);
        destinationStoppingDistance =
            Mathf.Max(0.01f, destinationStoppingDistance);
        chaseDestinationGizmoRadius =
            Mathf.Max(0.01f, chaseDestinationGizmoRadius);
        meleeBRepositionDistance =
            Mathf.Max(0.1f, meleeBRepositionDistance);
        meleeBRepositionDistanceTolerance =
            Mathf.Max(0f, meleeBRepositionDistanceTolerance);
        meleeBRepositionMoveSpeed =
            Mathf.Max(0.01f, meleeBRepositionMoveSpeed);
        meleeBRepositionStoppingDistance =
            Mathf.Max(0.01f, meleeBRepositionStoppingDistance);
        meleeBRepositionAbortTargetDistance =
            Mathf.Max(
                meleeBRepositionDistance,
                meleeBRepositionAbortTargetDistance
            );
    }

    public override void Spawned()
    {
        fusionSpawned = true;
        ResolveReferences();

        configurationValid =
            enemyActor != null &&
            enemyActor.Definition != null &&
            chaseMotor != null &&
            chaseMotor.SupportedChaseKind ==
                enemyActor.Definition.ChaseKind &&
            GetComponents<EnemyChaseMotor>().Length == 1;

        if (Object.HasStateAuthority == false)
        {
            return;
        }

        HasChaseDestination = false;
        ChaseDestination = transform.position;
        MoveSpeed = 0f;
        DestinationSequence = 0;
        RepathTimer = TickTimer.None;
        TargetPositionAtLastPlan = transform.position;
        IsMeleeBDashRepositioning = false;
        HasMeleeBRepositionDestination = false;
        MeleeBRepositionDestination = transform.position;
        MeleeBRepositionPlanResolved = false;
        HandledDashCompletionSequence = 0;

        if (configurationValid == false)
        {
            string expectedKind =
                enemyActor != null &&
                enemyActor.Definition != null
                    ? enemyActor.Definition.ChaseKind.ToString()
                    : "缺少 EnemyDefinition";

            string actualMotor =
                chaseMotor != null
                    ? $"{chaseMotor.GetType().Name} / {chaseMotor.SupportedChaseKind}"
                    : "未指定 Chase Motor";

            Debug.LogError(
                "[Enemy Chase] 追逐設定無效，AI 追逐暫停。" +
                $"\nEnemy：{name}" +
                $"\nDefinition 需要：{expectedKind}" +
                $"\n目前 Motor：{actualMotor}" +
                $"\nRoot 上的 EnemyChaseMotor 數量：{GetComponents<EnemyChaseMotor>().Length}" +
                "\n地面近戰 ChaseA 必須使用 EnemyGroundChaseMotor；" +
                "自由飛行遠程 ChaseB 必須使用 EnemyFlyingChaseMotor。",
                this
            );
        }
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        chaseMotor?.Stop();
        fusionSpawned = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority == false ||
            configurationValid == false ||
            enemyActor == null ||
            perception == null ||
            combatDecision == null)
        {
            return;
        }

        MoveSpeed = 0f;

        EnemyStateController state =
            enemyActor.StateController;

        if (enemyActor.IsAlive == false ||
            state == null ||
            state.CanRunBrain == false ||
            perception.HasTarget == false ||
            (state.CurrentBrainState != EnemyBrainState.Chase &&
             state.CurrentBrainState != EnemyBrainState.Combat))
        {
            StopChasing();
            return;
        }

        Vector3 targetPosition =
            perception.HasDirectSight ||
            perception.IsInsideLockedTargetRetention
                ? perception.GetPlayerObservationPosition(
                    perception.CurrentTarget
                )
                : perception.LastKnownTargetPosition;

        TryBeginMeleeBDashReposition();

        if (IsMeleeBDashRepositioning)
        {
            if (TickMeleeBDashReposition(targetPosition))
            {
                return;
            }

            // 重整已結束或無法繼續時，同一 Tick 直接交回一般 Chase，
            // 不留下多餘一幀的停頓。
        }

        bool targetMovedEnough =
            targetMovementRepathDistance > 0f &&
            Vector3.SqrMagnitude(
                targetPosition -
                TargetPositionAtLastPlan
            ) >=
            targetMovementRepathDistance *
            targetMovementRepathDistance;

        if (HasChaseDestination == false ||
            RepathTimer.ExpiredOrNotRunning(Runner) ||
            targetMovedEnough)
        {
            TryRepath(targetPosition);
        }

        if (HasChaseDestination == false)
        {
            return;
        }

        if (chaseMotor.TickMove(
                ChaseDestination,
                Runner.DeltaTime,
                destinationStoppingDistance,
                out float actualSpeed
            ) == false)
        {
            HasChaseDestination = false;
            RepathTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    repathIntervalSeconds
                );
            chaseMotor.Stop();
            return;
        }

        MoveSpeed = actualSpeed;

        if (Vector3.Distance(
                transform.position,
                ChaseDestination
            ) <= destinationStoppingDistance)
        {
            HasChaseDestination = false;
            chaseMotor.Stop();
        }
    }

    private void TryRepath(
        Vector3 targetPosition
    )
    {
        TargetPositionAtLastPlan =
            targetPosition;

        RepathTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                repathIntervalSeconds
            );

        if (chaseMotor.TryPlanDestination(
                perception.CurrentTarget,
                targetPosition,
                perception.HasDirectSight,
                combatDecision.PreferredMinimumAttackDistance,
                combatDecision.PreferredMaximumAttackDistance,
                out Vector3 destination
            ))
        {
            ChaseDestination = destination;
            HasChaseDestination = true;
            DestinationSequence++;
            return;
        }

        HasChaseDestination = false;
        chaseMotor.Stop();

        if (debugChase)
        {
            Debug.Log(
                "[Enemy Chase] 目前找不到安全完整路徑，等待下次重算。",
                this
            );
        }
    }

    /// <summary>
    /// 消費 EnemyMeleeDashAttack 的正常完成序號。
    /// 同一序號只處理一次；外部拉動或目標失效造成的取消不會啟動重整。
    /// </summary>
    private void TryBeginMeleeBDashReposition()
    {
        if (meleeDashAttack == null ||
            meleeDashAttack.CompletedDashSequence ==
            HandledDashCompletionSequence)
        {
            return;
        }

        HandledDashCompletionSequence =
            meleeDashAttack.CompletedDashSequence;

        EndMeleeBDashReposition(null);

        if (!enableMeleeBDashReposition ||
            groundChaseMotor == null ||
            perception.HasTarget == false ||
            combatDecision.IsActionActive)
        {
            return;
        }

        HasChaseDestination = false;
        RepathTimer = TickTimer.None;
        chaseMotor.Stop();

        IsMeleeBDashRepositioning = true;
        HasMeleeBRepositionDestination = false;
        MeleeBRepositionPlanResolved = false;
        MeleeBRepositionDestination = transform.position;

        if (debugMeleeBReposition)
        {
            Debug.Log(
                "[Enemy Melee B Reposition] Dash Recovery 完成，開始固定重整。",
                this
            );
        }
    }

    /// <summary>
    /// 冷卻期間執行近戰 B 的固定重整。
    /// 回傳 true 代表本 Tick 已由重整行為處理；false 代表可立刻恢復一般 Chase。
    /// </summary>
    private bool TickMeleeBDashReposition(
        Vector3 targetPosition
    )
    {
        if (groundChaseMotor == null ||
            meleeDashAttack == null ||
            perception.HasTarget == false)
        {
            EndMeleeBDashReposition("必要引用或目標失效");
            return false;
        }

        // 冷卻完成後把控制權交回戰鬥選擇器與一般 Chase。
        if (meleeDashAttack.IsCooldownReady(Runner))
        {
            EndMeleeBDashReposition("Dash 冷卻完成");
            return false;
        }

        // 若其他戰鬥選項已搶先啟動，不與 ActionGate 爭奪位移／旋轉。
        if (combatDecision.IsActionActive)
        {
            EndMeleeBDashReposition("其他戰鬥行為已啟動");
            return true;
        }

        Vector3 horizontalToTarget =
            Vector3.ProjectOnPlane(
                targetPosition - transform.position,
                Vector3.up
            );

        float targetDistance =
            horizontalToTarget.magnitude;

        if (targetDistance >
            meleeBRepositionAbortTargetDistance)
        {
            EndMeleeBDashReposition("玩家已離開重整距離");
            return false;
        }

        if (!MeleeBRepositionPlanResolved)
        {
            MeleeBRepositionPlanResolved = true;

            bool alreadyInsideDistanceBand =
                Mathf.Abs(
                    targetDistance -
                    meleeBRepositionDistance
                ) <= meleeBRepositionDistanceTolerance;

            if (!alreadyInsideDistanceBand)
            {
                if (!groundChaseMotor.TryPlanPostDashReposition(
                        targetPosition,
                        meleeBRepositionDistance,
                        out Vector3 destination
                    ))
                {
                    EndMeleeBDashReposition("找不到固定重整點的完整 NavMesh 路徑");
                    return false;
                }

                MeleeBRepositionDestination = destination;
                HasMeleeBRepositionDestination = true;

                if (debugMeleeBReposition)
                {
                    Debug.Log(
                        "[Enemy Melee B Reposition] 已固定重整目的地：" +
                        $"{destination:F3}",
                        this
                    );
                }
            }
            else if (debugMeleeBReposition)
            {
                Debug.Log(
                    "[Enemy Melee B Reposition] 已在理想距離內，原地面向玩家等待冷卻。",
                    this
                );
            }
        }

        if (HasMeleeBRepositionDestination)
        {
            if (!groundChaseMotor.TickPostDashReposition(
                    MeleeBRepositionDestination,
                    targetPosition,
                    Runner.DeltaTime,
                    meleeBRepositionStoppingDistance,
                    meleeBRepositionMoveSpeed,
                    out float actualSpeed
                ))
            {
                EndMeleeBDashReposition("固定重整路徑被阻擋或失效");
                return false;
            }

            MoveSpeed = actualSpeed;

            if (Vector3.Distance(
                    transform.position,
                    MeleeBRepositionDestination
                ) <= meleeBRepositionStoppingDistance)
            {
                HasMeleeBRepositionDestination = false;
                groundChaseMotor.Stop();

                if (debugMeleeBReposition)
                {
                    Debug.Log(
                        "[Enemy Melee B Reposition] 抵達固定重整點，等待冷卻。",
                        this
                    );
                }
            }
        }
        else
        {
            groundChaseMotor.FacePostDashTarget(
                targetPosition,
                Runner.DeltaTime
            );
        }

        return true;
    }

    private void EndMeleeBDashReposition(
        string reason
    )
    {
        bool wasActive = IsMeleeBDashRepositioning;

        IsMeleeBDashRepositioning = false;
        HasMeleeBRepositionDestination = false;
        MeleeBRepositionPlanResolved = false;
        MeleeBRepositionDestination = transform.position;

        if (groundChaseMotor != null)
        {
            groundChaseMotor.Stop();
        }

        if (wasActive &&
            debugMeleeBReposition &&
            !string.IsNullOrEmpty(reason))
        {
            Debug.Log(
                $"[Enemy Melee B Reposition] 結束：{reason}",
                this
            );
        }
    }

    private void StopChasing()
    {
        EndMeleeBDashReposition(null);

        if (HasChaseDestination)
        {
            HasChaseDestination = false;
            RepathTimer = TickTimer.None;
            chaseMotor?.Stop();
        }
    }

    private void ResolveReferences()
    {
        if (enemyActor == null)
        {
            enemyActor = GetComponent<EnemyActor>();
        }

        if (perception == null)
        {
            perception =
                GetComponent<EnemyPerceptionController>();
        }

        if (combatDecision == null)
        {
            combatDecision =
                GetComponent<EnemyCombatDecisionController>();
        }

        if (chaseMotor == null)
        {
            chaseMotor =
                GetComponent<EnemyChaseMotor>();
        }

        if (meleeDashAttack == null)
        {
            meleeDashAttack =
                GetComponent<EnemyMeleeDashAttack>();
        }

        groundChaseMotor =
            chaseMotor as EnemyGroundChaseMotor;
    }

    private void OnDrawGizmosSelected()
    {
        // Edit Mode 不讀 Networked Property，避免尚未 Spawned 時發生非法存取。
        if (drawCurrentChaseDestination == false ||
            Application.isPlaying == false ||
            IsFusionSpawned == false)
        {
            return;
        }

        if (IsMeleeBDashRepositioning &&
            HasMeleeBRepositionDestination)
        {
            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.95f);
            Gizmos.DrawLine(
                transform.position,
                MeleeBRepositionDestination
            );
            Gizmos.DrawWireSphere(
                MeleeBRepositionDestination,
                chaseDestinationGizmoRadius
            );
            return;
        }

        if (HasChaseDestination == false)
        {
            return;
        }

        Gizmos.color = chaseDestinationGizmoColor;
        Gizmos.DrawLine(
            transform.position,
            ChaseDestination
        );
        Gizmos.DrawWireSphere(
            ChaseDestination,
            chaseDestinationGizmoRadius
        );
    }
}
