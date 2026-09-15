using UnityEngine;
using UnityEngine.Events;


/// <summary>
/// 敵人 Animator 的單一呈現入口。
///
/// 同時讀取待機巡邏、警戒、追逐與戰鬥的 Networked 狀態，避免兩顆 Driver
/// 在同一幀分別寫入 MoveSpeed，或讓 Idle／Walk／Chase 使用彼此重疊的狀態機。
/// 本元件只做本地動畫、音效與 VFX 呈現，不反向控制 AI 或傷害。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyPresentationAnimatorDriver :
    MonoBehaviour
{
    [Header("核心引用")]

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyActor。若留空會往父階層搜尋。")]
    private EnemyActor actor;

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyAwarenessBrain。若留空會往父階層搜尋。")]
    private EnemyAwarenessBrain awareness;

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyIdlePatrolBrain。提供巡邏實際速度；若留空會往父階層搜尋。")]
    private EnemyIdlePatrolBrain idlePatrol;

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyChaseBrain。提供追逐實際速度；若留空會往父階層搜尋。")]
    private EnemyChaseBrain chase;

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyCombatDecisionController。提供攻擊 ID 與階段；若留空會往父階層搜尋。")]
    private EnemyCombatDecisionController combat;

    [SerializeField]
    [Tooltip("模型 Animator。若留空會在本物件與子物件搜尋。Apply Root Motion 必須關閉。")]
    private Animator animator;

    [Header("Animator 參數名稱")]

    [SerializeField]
    [Tooltip("Bool：敵人是否存活。預設 IsAlive。留空代表不寫入此參數。")]
    private string aliveBoolName =
        "IsAlive";

    [SerializeField]
    [Tooltip("Bool：敵人是否已進入發現、警戒或搜索階段。預設 IsAlerted。留空代表不寫入。")]
    private string alertedBoolName =
        "IsAlerted";

    [SerializeField]
    [Tooltip("Bool：是否正在播放唯一呼喚者的發現演出。預設 IsAnnouncing。留空代表不寫入。")]
    private string announcingBoolName =
        "IsAnnouncing";

    [SerializeField]
    [Tooltip("Float：巡邏與追逐共用的實際移動速度，公尺／秒。預設 MoveSpeed。這是 Locomotion Blend Tree 唯一速度來源。")]
    private string moveSpeedName =
        "MoveSpeed";

    [SerializeField]
    [Tooltip("Float：巡邏 Walk 動畫的播放倍率。預設 PatrolPlaybackSpeed；只指定給未警戒 Locomotion State 的 Speed Parameter。")]
    private string patrolPlaybackSpeedName =
        "PatrolPlaybackSpeed";

    [SerializeField]
    [Tooltip("Float：追逐 Run／Fly 動畫的播放倍率。預設 ChasePlaybackSpeed；只指定給警戒 Locomotion State 的 Speed Parameter。")]
    private string chasePlaybackSpeedName =
        "ChasePlaybackSpeed";

    [SerializeField]
    [Tooltip("Int：目前 EnemyBrainState 整數值。預設 BrainState；Idle=1、Patrol=2、Alert=3、Chase=4、Combat=5、Investigate=6、Dead=8。")]
    private string brainStateIntName =
        "BrainState";

    [SerializeField]
    [Tooltip("Bool：目前是否正在使用任一攻擊或未來防禦。預設 IsUsingCombatAction。")]
    private string usingActionBoolName =
        "IsUsingCombatAction";

    [SerializeField]
    [Tooltip("Int：目前 EnemyCombatOptionId。預設 CombatOptionId；1 近戰A、2 近戰B、3 遠程A、4 遠程B。")]
    private string combatOptionIdIntName =
        "CombatOptionId";

    [SerializeField]
    [Tooltip("Int：目前 EnemyCombatActionPhase。預設 CombatActionPhase；1 Startup、2 Active、3 Braking、4 Recovery、5 Tracking、6 LockedDelay。")]
    private string combatPhaseIntName =
        "CombatActionPhase";

    [Header("Locomotion 平滑與動畫基準")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("MoveSpeed 上升時的 Animator 阻尼秒數。只影響 Blend Tree 畫面，不改變敵人實際移動。建議 0.05～0.12。")]
    private float moveSpeedIncreaseDampSeconds =
        0.08f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("MoveSpeed 下降時的 Animator 阻尼秒數。巡邏結束回 Idle 太突然時提高此值；建議先用 0.25。")]
    private float moveSpeedDecreaseDampSeconds =
        0.25f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Walk Clip 在 Animator 播放倍率 1 時所對應的世界巡邏速度，公尺／秒。例如 Clip 基準速度是 2，就填 2。")]
    private float patrolAnimationReferenceMoveSpeed =
        2f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Chase Run／Fly Clip 在 Animator 播放倍率 1 時所對應的世界追逐速度，公尺／秒。與 Walk 基準分開，避免兩種動畫被迫共用倍率。")]
    private float chaseAnimationReferenceMoveSpeed =
        5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Patrol／Chase Playback Speed 改變時的阻尼秒數，減少動畫播放倍率突然跳動。0 代表立即套用。")]
    private float playbackSpeedDampSeconds =
        0.08f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("移動動畫允許的最低播放倍率。只在角色實際移動時限制；完全停止時會回到 1，避免 Idle 被凍結。")]
    private float minimumPlaybackSpeed =
        0.2f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("移動動畫允許的最高播放倍率。例如基準 2、實際速度 6 需要倍率 3，因此此值不可低於 3。建議 4。")]
    private float maximumPlaybackSpeed =
        4f;

    [Header("本機一次性呈現事件")]

    [SerializeField]
    [Tooltip("本機觀察到新的 Announcement Sequence 時觸發一次。只接世界呼喚聲或 VFX，不可接 AI 狀態與傷害。")]
    private UnityEvent onAnnouncement =
        new UnityEvent();

    [SerializeField]
    [Tooltip("本機觀察到新的 Combat Action Sequence 時觸發一次。只接起手聲或 VFX，不可接傷害。")]
    private UnityEvent onCombatActionStarted =
        new UnityEvent();

    private int aliveHash;
    private int alertedHash;
    private int announcingHash;
    private int moveSpeedHash;
    private int patrolPlaybackSpeedHash;
    private int chasePlaybackSpeedHash;
    private int brainStateHash;
    private int usingActionHash;
    private int optionIdHash;
    private int combatPhaseHash;

    private bool announcementInitialized;
    private int observedAnnouncementSequence;
    private bool combatInitialized;
    private int observedCombatSequence;

    private void Awake()
    {
        ResolveReferences();
        CacheParameters();
    }

    private void OnEnable()
    {
        announcementInitialized = false;
        combatInitialized = false;
        ResolveReferences();
        CacheParameters();

        // Animator Controller 新增的 Float 預設值可能仍是 0。
        // 播放倍率 0 會凍結整個 Locomotion State，因此啟用時先安全設成 1。
        SetFloat(patrolPlaybackSpeedHash, 1f);
        SetFloat(chasePlaybackSpeedHash, 1f);
    }

    private void LateUpdate()
    {
        if (actor == null ||
            actor.IsFusionSpawned == false ||
            actor.Object == null ||
            actor.Object.IsValid == false ||
            animator == null)
        {
            announcementInitialized = false;
            combatInitialized = false;
            return;
        }

        bool alive = actor.IsAlive;

        bool awarenessReady =
            awareness != null &&
            awareness.IsFusionSpawned;

        bool isAnnouncing =
            alive &&
            awarenessReady &&
            awareness.CurrentAwarenessPhase ==
                EnemyAwarenessBrain.EnemyAwarenessPhase.Announcing;

        bool isAlerted =
            alive &&
            awarenessReady &&
            awareness.CurrentAwarenessPhase !=
                EnemyAwarenessBrain.EnemyAwarenessPhase.Unaware;

        float patrolSpeed =
            alive &&
            idlePatrol != null &&
            idlePatrol.IsFusionSpawned
                ? idlePatrol.MoveSpeed
                : 0f;

        float chaseSpeed =
            alive &&
            chase != null &&
            chase.IsFusionSpawned
                ? chase.MoveSpeed
                : 0f;

        // 巡邏與追逐在狀態上互斥。取最大值可防止狀態交接的同一幀
        // 因其中一顆 Driver 較晚寫入 0 而使動畫閃回 Idle。
        float locomotionSpeed =
            Mathf.Max(patrolSpeed, chaseSpeed);

        bool combatReady =
            combat != null &&
            combat.IsFusionSpawned;

        bool isUsingAction =
            alive &&
            combatReady &&
            combat.IsActionActive;

        SetBool(aliveHash, alive);
        SetBool(alertedHash, isAlerted);
        SetBool(announcingHash, isAnnouncing);
        SetDampedFloat(
            moveSpeedHash,
            locomotionSpeed,
            GetMoveSpeedDampTime(locomotionSpeed)
        );

        SetDampedFloat(
            patrolPlaybackSpeedHash,
            CalculatePlaybackSpeed(
                patrolSpeed,
                patrolAnimationReferenceMoveSpeed
            ),
            playbackSpeedDampSeconds
        );

        SetDampedFloat(
            chasePlaybackSpeedHash,
            CalculatePlaybackSpeed(
                chaseSpeed,
                chaseAnimationReferenceMoveSpeed
            ),
            playbackSpeedDampSeconds
        );
        SetInteger(
            brainStateHash,
            actor.StateController != null &&
            actor.StateController.IsFusionSpawned
                ? (int)actor.StateController.CurrentBrainState
                : 0
        );
        SetBool(usingActionHash, isUsingAction);
        SetInteger(
            optionIdHash,
            combatReady
                ? (int)combat.ActiveOptionId
                : 0
        );
        SetInteger(
            combatPhaseHash,
            combatReady
                ? (int)combat.CurrentActionPhase
                : 0
        );

        ObserveAnnouncementEvent(
            awarenessReady,
            isAnnouncing
        );

        ObserveCombatEvent(combatReady);
    }

    private void ObserveAnnouncementEvent(
        bool awarenessReady,
        bool isAnnouncing
    )
    {
        if (awarenessReady == false)
        {
            announcementInitialized = false;
            return;
        }

        int sequence =
            awareness.AnnouncementSequence;

        if (announcementInitialized == false)
        {
            observedAnnouncementSequence = sequence;
            announcementInitialized = true;
            return;
        }

        if (sequence == observedAnnouncementSequence)
        {
            return;
        }

        observedAnnouncementSequence = sequence;

        // 中途加入時若演出已過期，不補播歷史呼喚聲。
        if (isAnnouncing)
        {
            onAnnouncement?.Invoke();
        }
    }

    private void ObserveCombatEvent(
        bool combatReady
    )
    {
        if (combatReady == false)
        {
            combatInitialized = false;
            return;
        }

        int sequence =
            combat.ActionSequence;

        if (combatInitialized == false)
        {
            observedCombatSequence = sequence;
            combatInitialized = true;
            return;
        }

        if (sequence == observedCombatSequence)
        {
            return;
        }

        observedCombatSequence = sequence;
        onCombatActionStarted?.Invoke();
    }

    private void ResolveReferences()
    {
        if (actor == null)
        {
            actor = GetComponentInParent<EnemyActor>();
        }

        if (awareness == null)
        {
            awareness =
                GetComponentInParent<EnemyAwarenessBrain>();
        }

        if (idlePatrol == null)
        {
            idlePatrol =
                GetComponentInParent<EnemyIdlePatrolBrain>();
        }

        if (chase == null)
        {
            chase =
                GetComponentInParent<EnemyChaseBrain>();
        }

        if (combat == null)
        {
            combat =
                GetComponentInParent<EnemyCombatDecisionController>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();

            if (animator == null)
            {
                animator =
                    GetComponentInChildren<Animator>(true);
            }
        }
    }

    private void CacheParameters()
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            return;
        }

        aliveHash = FindParameter(
            aliveBoolName,
            AnimatorControllerParameterType.Bool
        );
        alertedHash = FindParameter(
            alertedBoolName,
            AnimatorControllerParameterType.Bool
        );
        announcingHash = FindParameter(
            announcingBoolName,
            AnimatorControllerParameterType.Bool
        );
        moveSpeedHash = FindParameter(
            moveSpeedName,
            AnimatorControllerParameterType.Float
        );
        patrolPlaybackSpeedHash = FindParameter(
            patrolPlaybackSpeedName,
            AnimatorControllerParameterType.Float
        );
        chasePlaybackSpeedHash = FindParameter(
            chasePlaybackSpeedName,
            AnimatorControllerParameterType.Float
        );
        brainStateHash = FindParameter(
            brainStateIntName,
            AnimatorControllerParameterType.Int
        );
        usingActionHash = FindParameter(
            usingActionBoolName,
            AnimatorControllerParameterType.Bool
        );
        optionIdHash = FindParameter(
            combatOptionIdIntName,
            AnimatorControllerParameterType.Int
        );
        combatPhaseHash = FindParameter(
            combatPhaseIntName,
            AnimatorControllerParameterType.Int
        );
    }

    private int FindParameter(
        string parameterName,
        AnimatorControllerParameterType expectedType
    )
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return 0;
        }

        int hash =
            Animator.StringToHash(parameterName.Trim());

        AnimatorControllerParameter[] parameters =
            animator.parameters;

        for (int index = 0;
             index < parameters.Length;
             index++)
        {
            if (parameters[index].nameHash == hash &&
                parameters[index].type == expectedType)
            {
                return hash;
            }
        }

        Debug.LogWarning(
            $"[Enemy Presentation Animator] 缺少 {expectedType} 參數 {parameterName}，只略過這個欄位。",
            this
        );

        return 0;
    }

    private void SetBool(
        int hash,
        bool value
    )
    {
        if (hash != 0)
        {
            animator.SetBool(hash, value);
        }
    }

    private void SetFloat(
        int hash,
        float value
    )
    {
        if (hash != 0)
        {
            animator.SetFloat(hash, value);
        }
    }

    private void SetDampedFloat(
        int hash,
        float value,
        float dampSeconds
    )
    {
        if (hash == 0)
        {
            return;
        }

        if (dampSeconds <= 0f ||
            Time.deltaTime <= 0f)
        {
            animator.SetFloat(hash, value);
            return;
        }

        animator.SetFloat(
            hash,
            value,
            dampSeconds,
            Time.deltaTime
        );
    }

    private float GetMoveSpeedDampTime(
        float targetSpeed
    )
    {
        if (moveSpeedHash == 0)
        {
            return 0f;
        }

        float currentSpeed =
            animator.GetFloat(moveSpeedHash);

        return targetSpeed >= currentSpeed
            ? moveSpeedIncreaseDampSeconds
            : moveSpeedDecreaseDampSeconds;
    }

    private float CalculatePlaybackSpeed(
        float actualMoveSpeed,
        float referenceMoveSpeed
    )
    {
        // 完全停止時回到 1，確保同一 Locomotion State 內的 Idle Clip
        // 不會因移動倍率變成 0 而凍結在單一姿勢。
        if (actualMoveSpeed <= 0.01f)
        {
            return 1f;
        }

        return Mathf.Clamp(
            actualMoveSpeed /
            Mathf.Max(0.01f, referenceMoveSpeed),
            minimumPlaybackSpeed,
            maximumPlaybackSpeed
        );
    }

    private void SetInteger(
        int hash,
        int value
    )
    {
        if (hash != 0)
        {
            animator.SetInteger(hash, value);
        }
    }

    private void OnValidate()
    {
        ResolveReferences();
        moveSpeedIncreaseDampSeconds =
            Mathf.Max(0f, moveSpeedIncreaseDampSeconds);
        moveSpeedDecreaseDampSeconds =
            Mathf.Max(0f, moveSpeedDecreaseDampSeconds);
        patrolAnimationReferenceMoveSpeed =
            Mathf.Max(0.01f, patrolAnimationReferenceMoveSpeed);
        chaseAnimationReferenceMoveSpeed =
            Mathf.Max(0.01f, chaseAnimationReferenceMoveSpeed);
        playbackSpeedDampSeconds =
            Mathf.Max(0f, playbackSpeedDampSeconds);
        minimumPlaybackSpeed =
            Mathf.Max(0.01f, minimumPlaybackSpeed);
        maximumPlaybackSpeed =
            Mathf.Max(
                minimumPlaybackSpeed,
                maximumPlaybackSpeed
            );
    }
}
