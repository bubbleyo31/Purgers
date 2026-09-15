using Fusion;
using UnityEngine;


/// <summary>
/// 敵人戰鬥選項的唯一仲裁者。
///
/// 攻擊與未來防禦不各自在 FixedUpdateNetwork 搶著開始；
/// 本元件每 Tick 蒐集合法選項，選最高 Priority，並獨占 Active Option。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyActor))]
[RequireComponent(typeof(EnemyPerceptionController))]
[RequireComponent(typeof(EnemyMovementOwnership))]
public sealed class EnemyCombatDecisionController :
    NetworkBehaviour
{
    [Header("核心引用")]

    [SerializeField]
    [Tooltip("同 Root 的 EnemyActor。若留空會自動取得。")]
    private EnemyActor enemyActor;

    [SerializeField]
    [Tooltip("同 Root 的 EnemyPerceptionController。若留空會自動取得。")]
    private EnemyPerceptionController perception;

    [SerializeField]
    [Tooltip("同 Root 的 EnemyMovementOwnership。用來確認敵人目前是否正被 Support 鈎索或 Tank 集怪能力接管移動；若留空會自動取得。")]
    private EnemyMovementOwnership movementOwnership;

    [Header("除錯")]

    [SerializeField]
    [Tooltip("開啟後輸出能力開始、階段切換、完成與取消。大量敵人時建議關閉。")]
    private bool debugCombatDecision;

    [Networked]
    public EnemyCombatOptionId ActiveOptionId
    {
        get;
        private set;
    }

    [Networked]
    public EnemyCombatActionPhase CurrentActionPhase
    {
        get;
        private set;
    }

    [Networked]
    public int ActionSequence
    {
        get;
        private set;
    }

    private EnemyCombatOption[] options;
    private EnemyCombatOption activeOption;
    private bool fusionSpawned;

    public bool IsFusionSpawned =>
        fusionSpawned &&
        Object != null &&
        Object.IsValid;

    public bool IsActionActive =>
        IsFusionSpawned &&
        ActiveOptionId != EnemyCombatOptionId.None;

    /// <summary>
    /// 追逐器以所有攻擊的最大啟動距離作為接近目標。
    /// </summary>
    public float PreferredMaximumAttackDistance
    {
        get
        {
            float maximum = 1.5f;

            if (options == null)
            {
                return maximum;
            }

            for (int index = 0;
                 index < options.Length;
                 index++)
            {
                if (options[index] != null)
                {
                    maximum = Mathf.Max(
                        maximum,
                        options[index].MaximumStartDistance
                    );
                }
            }

            return maximum;
        }
    }

    public float PreferredMinimumAttackDistance
    {
        get
        {
            float minimum = float.PositiveInfinity;

            if (options != null)
            {
                for (int index = 0;
                     index < options.Length;
                     index++)
                {
                    if (options[index] != null)
                    {
                        minimum = Mathf.Min(
                            minimum,
                            options[index].MinimumStartDistance
                        );
                    }
                }
            }

            return float.IsPositiveInfinity(minimum)
                ? 0f
                : minimum;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        CacheAndValidateOptions();
    }

    private void OnValidate()
    {
        ResolveReferences();
        CacheAndValidateOptions();
    }

    public override void Spawned()
    {
        fusionSpawned = true;
        ResolveReferences();
        CacheAndValidateOptions();

        if (Object.HasStateAuthority == false)
        {
            return;
        }

        ActiveOptionId =
            EnemyCombatOptionId.None;

        CurrentActionPhase =
            EnemyCombatActionPhase.None;

        ActionSequence = 0;
        activeOption = null;
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        fusionSpawned = false;
        activeOption = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority == false ||
            enemyActor == null ||
            perception == null ||
            movementOwnership == null ||
            enemyActor.IsAlive == false)
        {
            if (Object.HasStateAuthority &&
                activeOption != null)
            {
                CancelActiveOption();
            }

            return;
        }

        // 外部位移擁有最高優先權。敵人一旦被鈎索或集怪能力接管，
        // 目前攻擊必須取消，也不能在被拉動途中開始下一次攻擊。
        if (movementOwnership.IsExternallyMoved)
        {
            if (activeOption != null)
            {
                CancelActiveOption();
            }

            return;
        }

        EnemyCombatContext context =
            CreateContext();

        if (activeOption != null)
        {
            if (IsTargetValid(context.Target) == false)
            {
                CancelActiveOption();
                return;
            }

            EnemyCombatOptionTickResult result =
                activeOption.TickOption(context);

            if (result !=
                EnemyCombatOptionTickResult.Running)
            {
                FinishActiveOption(
                    result ==
                    EnemyCombatOptionTickResult.Cancelled
                );
            }

            return;
        }

        if (CanChooseOption() == false ||
            IsTargetValid(context.Target) == false)
        {
            return;
        }

        EnemyCombatOption selected =
            SelectBestOption(context);

        if (selected != null)
        {
            StartOption(selected, context);
        }
    }

    public bool TrySetActionPhase(
        EnemyCombatOption source,
        EnemyCombatActionPhase phase
    )
    {
        if (Object.HasStateAuthority == false ||
            activeOption != source ||
            phase == EnemyCombatActionPhase.None)
        {
            return false;
        }

        if (CurrentActionPhase == phase)
        {
            return true;
        }

        CurrentActionPhase = phase;

        if (debugCombatDecision)
        {
            Debug.Log(
                $"[{nameof(EnemyCombatDecisionController)}] Action Phase：{phase}",
                this
            );
        }

        return true;
    }

    private bool CanChooseOption()
    {
        EnemyStateController state =
            enemyActor.StateController;

        EnemyActionGate gate =
            enemyActor.ActionGate;

        if (state == null ||
            gate == null ||
            state.CanRunBrain == false ||
            state.IsUsingAction ||
            gate.CanAttack == false)
        {
            return false;
        }

        return state.CurrentBrainState == EnemyBrainState.Chase ||
               state.CurrentBrainState == EnemyBrainState.Combat;
    }

    private EnemyCombatOption SelectBestOption(
        in EnemyCombatContext context
    )
    {
        EnemyCombatOption best = null;
        int bestPriority = int.MinValue;

        for (int index = 0;
             index < options.Length;
             index++)
        {
            EnemyCombatOption option =
                options[index];

            if (option == null ||
                option.OptionId == EnemyCombatOptionId.None ||
                option.Priority < bestPriority ||
                option.CanStart(context) == false)
            {
                continue;
            }

            best = option;
            bestPriority = option.Priority;
        }

        return best;
    }

    private void StartOption(
        EnemyCombatOption option,
        in EnemyCombatContext context
    )
    {
        activeOption = option;
        ActiveOptionId = option.OptionId;
        CurrentActionPhase =
            EnemyCombatActionPhase.Startup;
        ActionSequence++;

        enemyActor.StateController.TrySetActionState(
            option.ActionState
        );

        enemyActor.StateController.TrySetBrainState(
            EnemyBrainState.Combat
        );

        enemyActor.ActionGate.SetLocks(
            EnemyActionLockSource.Ability,
            option.LocksWhileActive
        );

        option.BeginOption(context);

        if (debugCombatDecision)
        {
            Debug.Log(
                $"[{nameof(EnemyCombatDecisionController)}] 開始戰鬥選項。" +
                $"\nOption：{option.OptionId}" +
                $"\nSequence：{ActionSequence}",
                this
            );
        }
    }

    private void FinishActiveOption(
        bool cancelled
    )
    {
        EnemyCombatOptionId completedId =
            ActiveOptionId;

        activeOption = null;
        ActiveOptionId = EnemyCombatOptionId.None;
        CurrentActionPhase =
            EnemyCombatActionPhase.None;

        enemyActor.ActionGate.ClearLocks(
            EnemyActionLockSource.Ability
        );

        enemyActor.StateController.TrySetActionState(
            EnemyActionState.None
        );

        if (enemyActor.IsAlive)
        {
            enemyActor.StateController.TrySetBrainState(
                perception.HasTarget
                    ? EnemyBrainState.Chase
                    : EnemyBrainState.Investigate
            );
        }

        if (debugCombatDecision)
        {
            Debug.Log(
                $"[{nameof(EnemyCombatDecisionController)}] " +
                $"{(cancelled ? "取消" : "完成")}：{completedId}",
                this
            );
        }
    }

    private void CancelActiveOption()
    {
        if (activeOption != null)
        {
            EnemyCombatContext context =
                CreateContext();

            activeOption.CancelOption(context);
        }

        FinishActiveOption(true);
    }

    private EnemyCombatContext CreateContext()
    {
        NetworkObject target =
            perception.CurrentTarget;

        Vector3 targetPosition =
            target != null && target.IsValid
                ? perception.GetPlayerObservationPosition(target)
                : perception.LastKnownTargetPosition;

        return new EnemyCombatContext(
            this,
            enemyActor,
            perception,
            target,
            targetPosition,
            Vector3.Distance(
                transform.position,
                targetPosition
            ),
            Runner.DeltaTime
        );
    }

    private bool IsTargetValid(
        NetworkObject target
    )
    {
        return perception.IsValidAlivePlayer(target);
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


        if (movementOwnership == null)
        {
            movementOwnership =
                GetComponent<EnemyMovementOwnership>();
        }
    }

    private void CacheAndValidateOptions()
    {
        options =
            GetComponents<EnemyCombatOption>();

        for (int first = 0;
             first < options.Length;
             first++)
        {
            if (options[first] == null ||
                options[first].OptionId ==
                    EnemyCombatOptionId.None)
            {
                continue;
            }

            for (int second = first + 1;
                 second < options.Length;
                 second++)
            {
                if (options[second] != null &&
                    options[first].OptionId ==
                    options[second].OptionId)
                {
                    Debug.LogError(
                        $"[{nameof(EnemyCombatDecisionController)}] " +
                        $"同一 Prefab 重複 Option ID：{options[first].OptionId}",
                        this
                    );
                }
            }
        }
    }
}
