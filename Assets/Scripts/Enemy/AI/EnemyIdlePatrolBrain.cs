using Fusion;
using System.Text;
using UnityEngine;

/// <summary>
/// 待機／巡邏低優先度決策。人工節點提供候選位置，Navigator 實際驗證路徑並移動。
/// 狀態一旦成為 Alert／Chase／Combat 就取消巡邏，不覆寫高優先度狀態。
/// 所有位移與隨機決策只由 State Authority 執行。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor), typeof(EnemyMovementOwnership))]
public sealed class EnemyIdlePatrolBrain : NetworkBehaviour
{
    [Header("核心與場景引用")]
    [SerializeField, Tooltip("同 Root 的 EnemyActor；留空自動取得。")]
    private EnemyActor enemyActor;
    [SerializeField, Tooltip("場景人工巡邏區。Prefab 資產留空，使用 Patrol Area ID 於 Spawned 綁定。場景實例可以手動指定。")]
    private EnemyPatrolArea patrolArea;
    [SerializeField, Tooltip("自動綁定同 Unity Scene 中相同 ID 的唯一 Patrol Area，例如 ground_room_a 或 air_room_a。")]
    private string patrolAreaId = "default";
    [SerializeField, Tooltip("同 Root 的地面或飛行 Navigator。每隻敵人只可掛其中一種，必須符合 EnemyDefinition。留空自動取得。")]
    private EnemyPatrolNavigator navigator;

    [Header("待機觀察")]
    [SerializeField, Min(0.1f), Tooltip("每次觀察／巡邏決策的最短等待秒數。")]
    private float minimumIdleDecisionSeconds = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("每次觀察／巡邏決策的最長等待秒數。不得小於最短時間。")]
    private float maximumIdleDecisionSeconds = 3.5f;
    [SerializeField, Range(0f, 1f), Tooltip("每次待機決策選擇巡邏的機率；0.6 等於 60%。其餘機率原地觀察。即使機率未抽中，達到 Maximum Consecutive Look Decisions 後仍會強制嘗試巡邏。")]
    private float patrolDecisionChance = 0.6f;
    [SerializeField, Min(0), Tooltip("最多允許連續幾次只原地觀察。達到次數後，下一次待機決策會強制嘗試巡邏；0 代表停用強制巡邏。建議 2。")]
    private int maximumConsecutiveLookDecisions = 2;
    [SerializeField, Range(0f, 1f), Tooltip("原地觀察時選擇 180 度回頭的機率；其餘情況小幅左右轉向。")]
    private float turnAroundChance = 0.15f;
    [SerializeField, Range(1f, 179f), Tooltip("小幅觀察最少旋轉幾度。")]
    private float minimumLookTurnDegrees = 25f;
    [SerializeField, Range(1f, 179f), Tooltip("小幅觀察最多旋轉幾度。")]
    private float maximumLookTurnDegrees = 75f;
    [SerializeField, Min(1f), Tooltip("待機水平旋轉速度，度／秒。角度不受 Animator Root Motion 控制。")]
    private float turnSpeedDegreesPerSecond = 120f;

    [Header("巡邏容錯")]
    [SerializeField, Min(0.01f), Tooltip("距離最後巡邏目的地小於此公尺數即抵達；飛行包含 Y 高度差。")]
    private float patrolArrivalDistance = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("單次巡邏最長秒數，避免無效路徑或控制異常使 Patrol 永久卡住。")]
    private float maximumPatrolSeconds = 30f;
    [SerializeField, Tooltip("輸出巡邏目的地、路徑失敗與待機轉向。")]
    private bool debugIdlePatrol = false;

    [Header("巡邏 Runtime Gizmos")]
    [SerializeField, Tooltip("Play Mode 選取敵人時，顯示這隻敵人目前採用的巡邏目的地與連線。")]
    private bool drawCurrentPatrolDestination = true;
    [SerializeField, Min(0.01f), Tooltip("目前巡邏目的地 Gizmos 球體半徑，公尺。")]
    private float currentPatrolDestinationGizmoRadius = 0.3f;
    [SerializeField, Tooltip("目前巡邏目的地與連線的 Gizmos 顏色。")]
    private Color currentPatrolDestinationGizmoColor = Color.green;

    [Networked] public TickTimer IdleDecisionTimer { get; private set; }
    [Networked] public float DesiredYaw { get; private set; }
    [Networked] public bool HasPatrolDestination { get; private set; }
    [Networked] public Vector3 PatrolDestination { get; private set; }
    [Networked] public int PatrolDestinationSequence { get; private set; }
    [Networked] private TickTimer PatrolTimer { get; set; }
    [Networked] private int LastPatrolPointIndex { get; set; }
    [Networked] private int ConsecutiveLookDecisionCount { get; set; }
    [Networked] public float MoveSpeed { get; private set; }

    private EnemyMovementOwnership ownership;
    private bool routeNeedsRebuild;
    private bool spawned;
    private bool configurationReady;
    public bool IsFusionSpawned => spawned && Object != null && Object.IsValid;
    public float PatrolArrivalDistance => patrolArrivalDistance;

    public override void Spawned()
    {
        Resolve();
        spawned = true;
        if (patrolArea == null)
        {
            foreach (EnemyPatrolArea candidate in FindObjectsOfType<EnemyPatrolArea>())
            {
                if (candidate.gameObject.scene != gameObject.scene || candidate.AreaId != patrolAreaId) continue;
                if (patrolArea != null)
                {
                    Debug.LogError("[Enemy Patrol] 同場景 Patrol Area ID 重複，請明確指定場景引用。", this);
                    patrolArea = null;
                    break;
                }
                patrolArea = candidate;
            }
        }
        configurationReady = navigator != null && enemyActor != null &&
            enemyActor.Definition != null &&
            navigator.SupportedLocomotion == enemyActor.Definition.LocomotionKind &&
            GetComponents<EnemyPatrolNavigator>().Length == 1;
        if (!HasStateAuthority) return;
        if (!configurationReady)
            Debug.LogError("[Enemy Patrol] 必須有一個符合 Definition 的 Navigator；巡邏暫停。", this);
        if (patrolArea == null)
            Debug.LogWarning("[Enemy Patrol] 找不到指定 Patrol Area，仍可待機觀察與發現玩家。", this);
        DesiredYaw = transform.eulerAngles.y;
        HasPatrolDestination = false;
        PatrolDestination = transform.position;
        PatrolDestinationSequence = 0;
        LastPatrolPointIndex = -1;
        ConsecutiveLookDecisionCount = 0;
        MoveSpeed = 0f;
        PatrolTimer = TickTimer.None;
        routeNeedsRebuild = false;
        ScheduleNextIdleDecision();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        navigator?.Stop();
        spawned = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsFusionSpawned || !HasStateAuthority || enemyActor == null) return;
        MoveSpeed = 0f;
        if (!enemyActor.IsAlive)
        {
            CancelPatrolForCombat();
            return;
        }
        // 地面落下重力與主動巡邏分離；被 Support／Tank 接管時 Navigator 會自行讓出控制。
        if (configurationReady) navigator.TickSupport(Runner.DeltaTime);
        EnemyStateController state = enemyActor.StateController;
        if (state == null || !state.IsFusionSpawned) return;

        if (state.CurrentBrainState != EnemyBrainState.Idle &&
            state.CurrentBrainState != EnemyBrainState.Patrol)
        {
            if (HasPatrolDestination) CancelPatrolForCombat();
            return;
        }
        if (!state.CanRunBrain || state.IsUsingAction || !ownership.CanMove)
        {
            if (HasPatrolDestination) routeNeedsRebuild = true;
            // 受控時計時仍有上限；解除後若已超時，回 Idle 重新決策。
            return;
        }
        if (state.CurrentBrainState == EnemyBrainState.Idle)
        {
            if (ownership.CanRotate)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.Euler(0f, DesiredYaw, 0f), turnSpeedDegreesPerSecond * Runner.DeltaTime);
            if (!IdleDecisionTimer.IsRunning) ScheduleNextIdleDecision();
            if (IdleDecisionTimer.Expired(Runner)) MakeIdleDecision();
            return;
        }

        if (!HasPatrolDestination || !configurationReady || PatrolTimer.ExpiredOrNotRunning(Runner))
        {
            ReturnToIdle();
            return;
        }
        if (routeNeedsRebuild)
        {
            routeNeedsRebuild = false;
            if (!navigator.TryBegin(PatrolDestination, out Vector3 rebuilt))
            {
                ReturnToIdle();
                return;
            }
            PatrolDestination = rebuilt;
        }
        Vector3 before = transform.position;
        EnemyPatrolMoveResult result = navigator.TickMove(Runner.DeltaTime, patrolArrivalDistance);
        MoveSpeed = Vector3.Distance(before, transform.position) / Runner.DeltaTime;
        if (result == EnemyPatrolMoveResult.Arrived) TryReportPatrolReached();
        else if (result == EnemyPatrolMoveResult.Failed)
        {
            if (debugIdlePatrol) Debug.Log("[Enemy Patrol] 路徑受阻，回 Idle，稍後重新選點。", this);
            ReturnToIdle();
        }
    }

    public bool TryReportPatrolReached()
    {
        if (!CanWrite || !HasPatrolDestination ||
            enemyActor.StateController.CurrentBrainState != EnemyBrainState.Patrol) return false;
        ReturnToIdle();
        return true;
    }

    public void CancelPatrolForCombat()
    {
        if (!IsFusionSpawned || !HasStateAuthority) return;
        HasPatrolDestination = false;
        IdleDecisionTimer = TickTimer.None;
        PatrolTimer = TickTimer.None;
        routeNeedsRebuild = false;
        navigator?.Stop();
    }

    public void RestartIdleSchedule()
    {
        if (!CanWrite) return;
        CancelPatrolForCombat();
        ConsecutiveLookDecisionCount = 0;
        DesiredYaw = transform.eulerAngles.y;
        ScheduleNextIdleDecision();
    }

    private void ReturnToIdle()
    {
        CancelPatrolForCombat();
        enemyActor.StateController.TrySetBrainState(EnemyBrainState.Idle);
        DesiredYaw = transform.eulerAngles.y;
        ScheduleNextIdleDecision();
    }

    private void MakeIdleDecision()
    {
        bool reachedLookLimit =
            maximumConsecutiveLookDecisions > 0 &&
            ConsecutiveLookDecisionCount >= maximumConsecutiveLookDecisions;

        bool wantsToPatrol =
            reachedLookLimit ||
            Random.value < patrolDecisionChance;

        if (wantsToPatrol &&
            configurationReady &&
            patrolArea != null &&
            TryCreatePatrolRequest())
        {
            ConsecutiveLookDecisionCount = 0;
            return;
        }

        if (wantsToPatrol && debugIdlePatrol)
        {
            if (!configurationReady)
            {
                Debug.LogWarning(
                    "[Enemy Patrol] 本輪想要巡邏，但基礎設定無效。" +
                    $"\nEnemy={name}" +
                    $"\nEnemyActor={(enemyActor != null ? "存在" : "缺少")}" +
                    $"\nDefinition={(enemyActor != null && enemyActor.Definition != null ? enemyActor.Definition.name : "缺少")}" +
                    $"\nNavigator={(navigator != null ? navigator.GetType().Name : "缺少")}" +
                    $"\nNavigator Count={GetComponents<EnemyPatrolNavigator>().Length}" +
                    $"\nDefinition Locomotion={(enemyActor != null && enemyActor.Definition != null ? enemyActor.Definition.LocomotionKind.ToString() : "無法讀取")}" +
                    $"\nNavigator Locomotion={(navigator != null ? navigator.SupportedLocomotion.ToString() : "無法讀取")}",
                    this
                );
            }
            else if (patrolArea == null)
            {
                Debug.LogWarning(
                    "[Enemy Patrol] 本輪想要巡邏，但找不到 Patrol Area。" +
                    $"\nEnemy={name}" +
                    $"\nRequested Area Id={patrolAreaId}" +
                    $"\nUnity Scene={gameObject.scene.name}" +
                    "\n請確認同一 Scene 中存在且只有一個完全相同 Area Id 的 EnemyPatrolArea。",
                    this
                );
            }
        }

        // 抽到原地觀察，或本次所有巡邏點都不可達，都算一次連續觀察。
        // 達到上限後會持續強制嘗試巡邏，直到真的找到合法完整路徑。
        ConsecutiveLookDecisionCount =
            maximumConsecutiveLookDecisions > 0
                ? Mathf.Min(
                    ConsecutiveLookDecisionCount + 1,
                    maximumConsecutiveLookDecisions
                )
                : 0;
        float angle = Random.value < turnAroundChance ? 180f :
            Random.Range(minimumLookTurnDegrees, maximumLookTurnDegrees);
        DesiredYaw = Mathf.Repeat(transform.eulerAngles.y + (Random.value < 0.5f ? -angle : angle), 360f);
        ScheduleNextIdleDecision();
        if (debugIdlePatrol) Debug.Log($"[Enemy Patrol] 待機新朝向：{DesiredYaw:F1}", this);
    }

    private bool TryCreatePatrolRequest()
    {
        int count = patrolArea.PointCount;
        if (count == 0)
        {
            if (debugIdlePatrol)
                Debug.LogWarning("[Enemy Patrol] Patrol Area 沒有任何有效巡邏點。", this);
            return false;
        }

        StringBuilder diagnostic =
            debugIdlePatrol
                ? new StringBuilder(768)
                : null;

        int skippedLastPoint = 0;
        int missingReference = 0;
        int beyondMaximumDistance = 0;
        int tooClose = 0;
        int navigationRejected = 0;
        int stateRejected = 0;

        // 從隨機索引起輪流檢查，每個候選最多一次。不可達點不會造成無限抽選。
        int start = Random.Range(0, count);
        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;

            EnemyPatrolPointQueryResult query =
                patrolArea.QueryPoint(
                    index,
                    transform.position,
                    out Vector3 point,
                    out string pointName,
                    out float pointDistance,
                    out float allowedMaximumDistance
                );

            if (index == LastPatrolPointIndex)
            {
                skippedLastPoint++;
                diagnostic?.AppendLine(
                    $"  [{index}] {pointName}：略過，這是上一個成功使用的巡邏點。"
                );
                continue;
            }

            if (query == EnemyPatrolPointQueryResult.IndexOutOfRange)
            {
                missingReference++;
                diagnostic?.AppendLine(
                    $"  [{index}]：拒絕，索引超出 Patrol Points 陣列。"
                );
                continue;
            }

            if (query == EnemyPatrolPointQueryResult.MissingTransform)
            {
                missingReference++;
                diagnostic?.AppendLine(
                    $"  [{index}] {pointName}：拒絕，Inspector 的 Transform 引用為空。"
                );
                continue;
            }

            if (query == EnemyPatrolPointQueryResult.BeyondMaximumDistance)
            {
                beyondMaximumDistance++;
                diagnostic?.AppendLine(
                    $"  [{index}] {pointName}：拒絕，距離 {pointDistance:F2}m " +
                    $"> Maximum Point Distance {allowedMaximumDistance:F2}m。" +
                    $"Point={point:F3}"
                );
                continue;
            }

            if (pointDistance <= patrolArrivalDistance)
            {
                tooClose++;
                diagnostic?.AppendLine(
                    $"  [{index}] {pointName}：拒絕，距離 {pointDistance:F2}m " +
                    $"<= Patrol Arrival Distance {patrolArrivalDistance:F2}m。" +
                    $"Point={point:F3}"
                );
                continue;
            }

            if (!navigator.TryBegin(
                    point,
                    out Vector3 reachable))
            {
                navigationRejected++;
                diagnostic?.AppendLine(
                    $"  [{index}] {pointName}：導航拒絕。" +
                    $"Reason={navigator.LastPlanFailure}。" +
                    $"{navigator.LastPlanFailureDetail}"
                );
                continue;
            }

            // 先確認狀態真的交接成功，再提交巡邏目的地。
            // 否則會留下 HasPatrolDestination=true、Brain 卻仍是 Idle 的半完成狀態。
            if (!enemyActor.StateController.TrySetBrainState(EnemyBrainState.Patrol))
            {
                stateRejected++;
                diagnostic?.AppendLine(
                    $"  [{index}] {pointName}：路徑合法，但 EnemyStateController 拒絕切換成 Patrol。" +
                    $"Brain={enemyActor.StateController.CurrentBrainState}，" +
                    $"Control={enemyActor.StateController.CurrentControlState}，" +
                    $"Action={enemyActor.StateController.CurrentActionState}。"
                );
                navigator.Stop();
                continue;
            }

            PatrolDestination = reachable;
            LastPatrolPointIndex = index;
            HasPatrolDestination = true;
            PatrolDestinationSequence++;
            routeNeedsRebuild = false;
            PatrolTimer = TickTimer.CreateFromSeconds(Runner, maximumPatrolSeconds);
            IdleDecisionTimer = TickTimer.None;
            if (debugIdlePatrol) Debug.Log($"[Enemy Patrol] 目的地：{reachable}", this);
            return true;
        }

        if (debugIdlePatrol)
            Debug.LogWarning(
                "[Enemy Patrol] 本輪沒有可用巡邏點。" +
                $"\nEnemy={name}" +
                $"\nRoot={transform.position:F3}" +
                $"\nArea={patrolArea.AreaId}" +
                $"\nNavigator={navigator.GetType().Name}" +
                $"\nPoint Count={count}，Start Index={start}，Last Point Index={LastPatrolPointIndex}" +
                $"\n統計：上一點={skippedLastPoint}，空引用／索引={missingReference}，" +
                $"超出距離={beyondMaximumDistance}，太近={tooClose}，" +
                $"導航拒絕={navigationRejected}，狀態拒絕={stateRejected}" +
                "\n逐點結果：\n" +
                diagnostic,
                this);

        return false;
    }

    private void ScheduleNextIdleDecision()
    {
        IdleDecisionTimer = TickTimer.CreateFromSeconds(Runner,
            Random.Range(minimumIdleDecisionSeconds, maximumIdleDecisionSeconds));
    }
    private bool CanWrite => IsFusionSpawned && HasStateAuthority && enemyActor != null && enemyActor.IsAlive;
    private void Resolve()
    {
        if (enemyActor == null) enemyActor = GetComponent<EnemyActor>();
        if (navigator == null) navigator = GetComponent<EnemyPatrolNavigator>();
        ownership = GetComponent<EnemyMovementOwnership>();
    }
    private void Awake() { Resolve(); }
    private void OnValidate()
    {
        Resolve();
        minimumIdleDecisionSeconds = Mathf.Max(0.1f, minimumIdleDecisionSeconds);
        maximumIdleDecisionSeconds = Mathf.Max(minimumIdleDecisionSeconds, maximumIdleDecisionSeconds);
        maximumLookTurnDegrees = Mathf.Max(minimumLookTurnDegrees, maximumLookTurnDegrees);
        maximumConsecutiveLookDecisions = Mathf.Max(0, maximumConsecutiveLookDecisions);
        patrolArrivalDistance = Mathf.Max(0.01f, patrolArrivalDistance);
        maximumPatrolSeconds = Mathf.Max(0.1f, maximumPatrolSeconds);
        currentPatrolDestinationGizmoRadius =
            Mathf.Max(0.01f, currentPatrolDestinationGizmoRadius);
    }

    private void OnDrawGizmosSelected()
    {
        // Networked Property 只能在 Spawned 後讀取；Edit Mode 不碰它們。
        if (!drawCurrentPatrolDestination ||
            !Application.isPlaying ||
            !IsFusionSpawned ||
            !HasPatrolDestination)
            return;

        Gizmos.color = currentPatrolDestinationGizmoColor;
        Gizmos.DrawLine(transform.position, PatrolDestination);
        Gizmos.DrawWireSphere(
            PatrolDestination,
            currentPatrolDestinationGizmoRadius);
    }
}
