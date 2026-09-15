using UnityEngine;
using UnityEngine.Events;


/// <summary>
/// Chase 與 Combat 的本地 Animator 橋接器。
/// 使用持續參數還原當前姿勢，使用 ActionSequence 觸發一次本機呈現事件。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyCombatAnimatorDriver :
    MonoBehaviour
{
    [Header("引用")]

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyCombatDecisionController。留空往父階層尋找。")]
    private EnemyCombatDecisionController combatDecision;

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyChaseBrain。留空往父階層尋找。")]
    private EnemyChaseBrain chaseBrain;

    [SerializeField]
    [Tooltip("模型 Animator。留空在本物件與子物件尋找。Apply Root Motion 必須關閉。")]
    private Animator animator;

    [Header("Animator 參數")]

    [SerializeField]
    [Tooltip("Float：追逐的實際速度，公尺／秒。預設 ChaseSpeed。")]
    private string chaseSpeedName =
        "ChaseSpeed";

    [SerializeField]
    [Tooltip("Bool：目前是否有戰鬥選項正在執行。預設 IsUsingCombatAction。")]
    private string usingActionBoolName =
        "IsUsingCombatAction";

    [SerializeField]
    [Tooltip("Int：EnemyCombatOptionId 的整數值。預設 CombatOptionId。")]
    private string optionIdIntName =
        "CombatOptionId";

    [SerializeField]
    [Tooltip("Int：EnemyCombatActionPhase 的整數值。預設 CombatActionPhase。")]
    private string phaseIntName =
        "CombatActionPhase";

    [Header("本機一次性事件")]

    [SerializeField]
    [Tooltip("每次新戰鬥動作開始時，在各端本地觸發一次。可接起手音效，不可接傷害或狀態切換。")]
    private UnityEvent onCombatActionStarted =
        new UnityEvent();

    private int chaseSpeedHash;
    private int usingActionHash;
    private int optionIdHash;
    private int phaseHash;
    private int observedActionSequence;
    private bool initialized;
    private bool parametersCached;

    private void Awake()
    {
        ResolveReferences();
        CacheParameters();
    }

    private void OnEnable()
    {
        initialized = false;
        CacheParameters();
    }

    private void LateUpdate()
    {
        if (combatDecision == null ||
            chaseBrain == null ||
            combatDecision.IsFusionSpawned == false ||
            chaseBrain.IsFusionSpawned == false ||
            animator == null ||
            parametersCached == false)
        {
            return;
        }

        bool isUsingAction =
            combatDecision.IsActionActive;

        animator.SetFloat(
            chaseSpeedHash,
            chaseBrain.MoveSpeed
        );

        animator.SetBool(
            usingActionHash,
            isUsingAction
        );

        animator.SetInteger(
            optionIdHash,
            (int)combatDecision.ActiveOptionId
        );

        animator.SetInteger(
            phaseHash,
            (int)combatDecision.CurrentActionPhase
        );

        int sequence =
            combatDecision.ActionSequence;

        if (initialized == false)
        {
            observedActionSequence = sequence;
            initialized = true;
            return;
        }

        if (sequence != observedActionSequence)
        {
            observedActionSequence = sequence;
            onCombatActionStarted?.Invoke();
        }
    }

    private void ResolveReferences()
    {
        if (combatDecision == null)
        {
            combatDecision =
                GetComponentInParent<EnemyCombatDecisionController>();
        }

        if (chaseBrain == null)
        {
            chaseBrain =
                GetComponentInParent<EnemyChaseBrain>();
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
        parametersCached =
            animator != null &&
            animator.runtimeAnimatorController != null;

        if (parametersCached == false)
        {
            return;
        }

        chaseSpeedHash =
            Animator.StringToHash(chaseSpeedName);
        usingActionHash =
            Animator.StringToHash(usingActionBoolName);
        optionIdHash =
            Animator.StringToHash(optionIdIntName);
        phaseHash =
            Animator.StringToHash(phaseIntName);

        parametersCached =
            HasParameter(
                chaseSpeedHash,
                AnimatorControllerParameterType.Float
            ) &&
            HasParameter(
                usingActionHash,
                AnimatorControllerParameterType.Bool
            ) &&
            HasParameter(
                optionIdHash,
                AnimatorControllerParameterType.Int
            ) &&
            HasParameter(
                phaseHash,
                AnimatorControllerParameterType.Int
            );

        if (parametersCached == false)
        {
            Debug.LogWarning(
                "[Enemy Combat Animator] Animator 缺少或使用錯誤型別的戰鬥參數，已停止寫入。",
                this
            );
        }
    }

    private bool HasParameter(
        int hash,
        AnimatorControllerParameterType type
    )
    {
        AnimatorControllerParameter[] parameters =
            animator.parameters;

        for (int index = 0;
             index < parameters.Length;
             index++)
        {
            if (parameters[index].nameHash == hash &&
                parameters[index].type == type)
            {
                return true;
            }
        }

        return false;
    }

    private void OnValidate()
    {
        ResolveReferences();
    }
}
