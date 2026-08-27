using Fusion;
using UnityEngine;


/// <summary>
/// Tank 第一人稱 ViewModel 的近戰動畫展示控制器。
///
/// ====================================================================
///
/// 負責：
///
/// Idle
/// Light1
/// Light2
/// Heavy
/// Tank F Quick Dash 的快速近戰動畫
/// Guard 防禦動畫
/// GrappleAirborne Air Dash 特殊能力動畫
/// ViewModel Appear 進場動畫。
///
/// ====================================================================
///
/// 這支腳本不讀取滑鼠左鍵，也不決定傷害幀。
///
/// 真正的攻擊是否合法、目前是哪一段 Combo、傷害何時發生，
/// 全部仍由 TankMeleeCombo 與 Fusion State Authority 決定。
///
/// 本腳本只觀察：
///
/// TankMeleeCombo.AttackSequence
/// TankMeleeCombo.CurrentStep
///
/// 當 AttackSequence 真正增加時，才播放該段動畫。
///
/// 因此以下情況不會誤播：
///
/// 攻擊仍在硬直
/// 輸入被 ActionGate 封鎖
/// Gameplay 拒絕開始攻擊
/// 職業 Runtime 尚未正式 Spawn。
///
/// ====================================================================
///
/// 注意：Animator 只能做 Presentation。
/// 不要在動畫事件內呼叫傷害、切換 Combo 或改寫 Networked State。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FirstPersonTankViewModelAnimator :
    MonoBehaviour
{
    // =====================================================================
    #region Animator Reference

    [Header("Animator 引用")]

    [SerializeField]
    [Tooltip("Tank 第一人稱 ViewModel 使用的 Animator。正常情況就是本元件所在物件上的 Animator；留空時會在 Awake 自動取得。")]
    private Animator animator;

    #endregion

    // =====================================================================
    #region Animator Parameter Names

    [Header("Animator Parameter 名稱")]

    [SerializeField]
    [Tooltip("播放第一段普通攻擊 Light1 使用的 Trigger 名稱。Tank Animator Controller 必須建立同名 Trigger。預設：TankAttack1。")]
    private string attack1TriggerParameter =
        "TankAttack1";

    [SerializeField]
    [Tooltip("播放第二段普通攻擊 Light2 使用的 Trigger 名稱。Tank Animator Controller 必須建立同名 Trigger。預設：TankAttack2。")]
    private string attack2TriggerParameter =
        "TankAttack2";

    [SerializeField]
    [Tooltip("播放第三段重攻擊 Heavy 使用的 Trigger 名稱。Tank Animator Controller 必須建立同名 Trigger。預設：TankAttack3。")]
    private string attack3TriggerParameter =
        "TankAttack3";

    [SerializeField]
    [Tooltip("Tank 的 F Quick Dash 正式啟動時，播放快速近戰動畫使用的 Trigger 名稱。對應目前 Tank.controller 的 melee State。預設：TankQuickMelee。")]
    private string quickMeleeTriggerParameter =
        "TankQuickMelee";

    [SerializeField]
    [Tooltip("控制 Tank 防禦動畫的 Bool 名稱。TankGuardAbility.IsGuarding 為 true 時進入防禦；變回 false 時離開。預設：TankGuarding。")]
    private string guardingBoolParameter =
        "TankGuarding";

    [SerializeField]
    [Tooltip("Tank 在 GrappleAirborne 正式發動 Air Dash 特殊能力時使用的 Trigger 名稱。對應目前 Tank.controller 的 attack4 State。預設：TankAirSpecial。")]
    private string airSpecialTriggerParameter =
        "TankAirSpecial";

    [SerializeField]
    [Tooltip("Tank ViewModel 建立完成後播放進場動畫使用的 Trigger 名稱。對應目前 Tank.controller 的 partition State。預設：TankAppear。")]
    private string appearTriggerParameter =
        "TankAppear";

    #endregion

    // =====================================================================
    #region Debug

    [Header("Debug")]

    [SerializeField]
    [Tooltip("開啟後會在 Console 顯示 Tank Runtime 綁定，以及普通攻擊、快速近戰、防禦、滯空特殊能力與進場動畫事件。功能確認後可以關閉。")]
    private bool debugAnimation;

    #endregion

    // =====================================================================
    #region Gameplay Source

    /// <summary>
    /// 目前 Tank Profession Runtime 內真正的近戰 Gameplay 來源。
    ///
    /// 不可以從 ViewModel Prefab 手動拖入，
    /// 因為 Profession Runtime 可能被刷新並產生新的 NetworkObject。
    /// 應由 ProfessionViewModelManager 在 Runtime 改變時重新綁定。
    /// </summary>
    private TankMeleeCombo meleeCombo;

    /// <summary>
    /// Player Root 上的共用 Quick Action Controller。
    ///
    /// Tank F Quick Dash 的 Startup / Active / Recovery 目前都是 0，
    /// 因此不依賴可能只有一幀的 CurrentPhase，
    /// 而是觀察每次成功開始都會增加的 ActivationSequence。
    /// </summary>
    private PlayerQuickActionController
        quickActionController;

    /// <summary>
    /// Tank Runtime 的正式防禦 Gameplay 來源。
    /// 動畫只跟隨 IsGuarding，不直接讀取右鍵。
    /// </summary>
    private TankGuardAbility guardAbility;

    /// <summary>
    /// Tank Runtime 的 GrappleAirborne Air Dash 特殊能力來源。
    /// 動畫使用 AirDashSequence 判斷一次正式啟動事件。
    /// </summary>
    private TankAirDashAbility airDashAbility;

    #endregion

    // =====================================================================
    #region Observation Cache

    /// <summary>
    /// 上一次已觀察到的正式攻擊序號。
    ///
    /// 初次綁定只建立基準，不會重播 Runtime 過去已發生的攻擊。
    /// </summary>
    private int observedAttackSequence;

    /// <summary>
    /// 是否已經成功讀取過一次有效的 Networked State。
    ///
    /// Runtime Component 可能已經存在，但 Spawned() 尚未完成；
    /// 這時不能讀取任何 [Networked] Property。
    /// </summary>
    private bool observationInitialized;

    /// <summary>
    /// 上一次已觀察到的 Tank Quick Action 正式啟動序號。
    /// </summary>
    private int observedQuickActionSequence;

    /// <summary>
    /// 是否已建立 Quick Action Sequence 基準。
    /// </summary>
    private bool quickActionObservationInitialized;

    /// <summary>
    /// 上一次同步到 Animator 的正式防禦狀態。
    /// </summary>
    private bool observedGuarding;

    /// <summary>
    /// 是否已建立 Guard 狀態基準。
    /// </summary>
    private bool guardObservationInitialized;

    /// <summary>
    /// 上一次已觀察到的 Air Dash 正式啟動序號。
    /// </summary>
    private int observedAirDashSequence;

    /// <summary>
    /// 是否已建立 Air Dash Sequence 基準。
    /// </summary>
    private bool airDashObservationInitialized;

    #endregion

    // =====================================================================
    #region Animator Parameter Cache

    private int attack1TriggerHash;
    private int attack2TriggerHash;
    private int attack3TriggerHash;

    private int quickMeleeTriggerHash;
    private int guardingBoolHash;
    private int airSpecialTriggerHash;
    private int appearTriggerHash;

    private bool hasAttack1Trigger;
    private bool hasAttack2Trigger;
    private bool hasAttack3Trigger;

    private bool hasQuickMeleeTrigger;
    private bool hasGuardingBool;
    private bool hasAirSpecialTrigger;
    private bool hasAppearTrigger;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (animator == null)
        {
            animator =
                GetComponent<Animator>();
        }

        CacheAndValidateAnimatorParameters();
    }

    private void Update()
    {
        ObserveMeleeAttack();
        ObserveQuickMelee();
        ObserveGuard();
        ObserveAirSpecial();
    }

    private void OnDisable()
    {
        ResetAnimatorRuntimeParameters();
    }

    #endregion

    // =====================================================================
    #region Public Binding

    /// <summary>
    /// 綁定目前 Tank Runtime／Player Root 真正使用的 Gameplay Sources。
    ///
    /// Runtime 被刷新時，Manager 必須再次呼叫本方法。
    /// 初次綁定只記住當前 Sequence，不會把舊攻擊當成新攻擊重播。
    /// </summary>
    public void BindGameplaySources(
        TankMeleeCombo newMeleeCombo,
        PlayerQuickActionController newQuickActionController,
        TankGuardAbility newGuardAbility,
        TankAirDashAbility newAirDashAbility
    )
    {
        ResetAnimatorRuntimeParameters();

        meleeCombo =
            newMeleeCombo;

        quickActionController =
            newQuickActionController;

        guardAbility =
            newGuardAbility;

        airDashAbility =
            newAirDashAbility;

        observationInitialized =
            TryReadMeleeState(
                out observedAttackSequence,
                out _
            );

        quickActionObservationInitialized =
            TryReadQuickActionSequence(
                out observedQuickActionSequence
            );

        guardObservationInitialized =
            TryReadGuardState(
                out observedGuarding
            );

        if (guardObservationInitialized &&
            hasGuardingBool)
        {
            animator.SetBool(
                guardingBoolHash,
                observedGuarding
            );
        }

        airDashObservationInitialized =
            TryReadAirDashSequence(
                out observedAirDashSequence
            );

        if (debugAnimation)
        {
            Debug.Log(
                $"[Tank ViewModel Animation 綁定]" +
                $"\nTankMeleeCombo：" +
                $"{(meleeCombo != null ? meleeCombo.name : "None")}" +
                $"\nQuickActionController：" +
                $"{(quickActionController != null ? quickActionController.name : "None")}" +
                $"\nTankGuardAbility：" +
                $"{(guardAbility != null ? guardAbility.name : "None")}" +
                $"\nTankAirDashAbility：" +
                $"{(airDashAbility != null ? airDashAbility.name : "None")}",
                this
            );
        }
    }

    /// <summary>
    /// 解除對舊 Tank Runtime 的觀察。
    ///
    /// 必須在舊 Runtime Despawn 或 ViewModel 被 Destroy 前呼叫，
    /// 避免下一幀讀取已失效 NetworkObject 的 [Networked] Property。
    /// </summary>
    public void UnbindGameplaySources()
    {
        ResetAnimatorRuntimeParameters();

        meleeCombo =
            null;

        quickActionController =
            null;

        guardAbility =
            null;

        airDashAbility =
            null;

        observedAttackSequence =
            0;

        observationInitialized =
            false;

        observedQuickActionSequence =
            0;

        quickActionObservationInitialized =
            false;

        observedGuarding =
            false;

        guardObservationInitialized =
            false;

        observedAirDashSequence =
            0;

        airDashObservationInitialized =
            false;
    }

    /// <summary>
    /// 從頭播放一次 Tank 進場動畫。
    ///
    /// 目前由 ProfessionViewModelManager 在 Tank ViewModel 建立完成後呼叫。
    /// 保留為 public，未來重生或重新持有武器時也能直接重用。
    /// </summary>
    public void PlayAppear()
    {
        FireOneShotTrigger(
            appearTriggerHash,
            hasAppearTrigger,
            "Appear / partition"
        );
    }

    #endregion

    // =====================================================================
    #region Gameplay Observation

    /// <summary>
    /// 監看正式攻擊序號。
    ///
    /// AttackSequence 每次只在 TankMeleeCombo.StartAttack() 成功時增加，
    /// 所以比讀取 Input 或只看 CurrentPhase 更適合當一次性動畫事件。
    /// </summary>
    private void ObserveMeleeAttack()
    {
        if (TryReadMeleeState(
                out int currentSequence,
                out TankMeleeComboStep currentStep
            ) == false)
        {
            return;
        }

        // Runtime 剛完成 Spawn 時，先建立安全基準。
        if (observationInitialized == false)
        {
            observedAttackSequence =
                currentSequence;

            observationInitialized =
                true;

            return;
        }

        if (currentSequence ==
            observedAttackSequence)
        {
            return;
        }

        // 先更新 Cache，避免缺少 Animator Parameter 時每幀重試同一刀。
        observedAttackSequence =
            currentSequence;

        switch (currentStep)
        {
            case TankMeleeComboStep.Light1:
            {
                FireOneShotTrigger(
                    attack1TriggerHash,
                    hasAttack1Trigger,
                    "Light1 / attack1"
                );

                break;
            }

            case TankMeleeComboStep.Light2:
            {
                FireOneShotTrigger(
                    attack2TriggerHash,
                    hasAttack2Trigger,
                    "Light2 / attack2"
                );

                break;
            }

            case TankMeleeComboStep.Heavy:
            {
                FireOneShotTrigger(
                    attack3TriggerHash,
                    hasAttack3Trigger,
                    "Heavy / attack3_001"
                );

                break;
            }

            case TankMeleeComboStep.None:
            default:
            {
                /*
                 * 正常本地 ViewModel 幾乎不會走到這裡，
                 * 因為每一刀會維持 Startup / Active / Recovery 一段時間。
                 *
                 * 仍然不替 None 猜測動畫，避免錯播上一段 Combo。
                 */
                if (debugAnimation)
                {
                    Debug.LogWarning(
                        $"[Tank ViewModel Animation] " +
                        $"AttackSequence 已改變，但 CurrentStep 為 None。" +
                        $"\nSequence：{currentSequence}",
                        this
                    );
                }

                break;
            }
        }
    }

    /// <summary>
    /// Tank 的 F Quick Dash 已由 PlayerQuickActionController 正式接受時，
    /// 播放一次 melee 快速近戰動畫。
    ///
    /// 不讀取 F 鍵，也不依賴只有一幀的 Quick Action Phase。
    /// </summary>
    private void ObserveQuickMelee()
    {
        if (TryReadQuickActionSequence(
                out int currentSequence
            ) == false)
        {
            return;
        }

        if (quickActionObservationInitialized == false)
        {
            observedQuickActionSequence =
                currentSequence;

            quickActionObservationInitialized =
                true;

            return;
        }

        if (currentSequence ==
            observedQuickActionSequence)
        {
            return;
        }

        observedQuickActionSequence =
            currentSequence;

        FireOneShotTrigger(
            quickMeleeTriggerHash,
            hasQuickMeleeTrigger,
            "Quick Melee / melee"
        );
    }

    /// <summary>
    /// 同步 TankGuardAbility.IsGuarding。
    ///
    /// 進入 Guard 時 Bool 變成 true，防禦動畫播放到最後並停住；
    /// 放開右鍵、耐力破防、勾索中斷或 Quick Action 強制結束 Guard 時，
    /// Gameplay 會先把 IsGuarding 改回 false，Animator 才離開防禦。
    /// </summary>
    private void ObserveGuard()
    {
        if (TryReadGuardState(
                out bool currentGuarding
            ) == false)
        {
            return;
        }

        if (guardObservationInitialized == false)
        {
            observedGuarding =
                currentGuarding;

            guardObservationInitialized =
                true;

            if (hasGuardingBool)
            {
                animator.SetBool(
                    guardingBoolHash,
                    currentGuarding
                );
            }

            return;
        }

        if (currentGuarding ==
            observedGuarding)
        {
            return;
        }

        observedGuarding =
            currentGuarding;

        if (hasGuardingBool)
        {
            animator.SetBool(
                guardingBoolHash,
                currentGuarding
            );
        }

        if (debugAnimation)
        {
            Debug.Log(
                $"[Tank ViewModel Animation] " +
                $"Guarding = {currentGuarding}",
                this
            );
        }
    }

    /// <summary>
    /// 每次 GrappleAirborne Air Dash 真正消耗能力時，
    /// 播放一次 attack4 特殊能力動畫。
    /// </summary>
    private void ObserveAirSpecial()
    {
        if (TryReadAirDashSequence(
                out int currentSequence
            ) == false)
        {
            return;
        }

        if (airDashObservationInitialized == false)
        {
            observedAirDashSequence =
                currentSequence;

            airDashObservationInitialized =
                true;

            return;
        }

        if (currentSequence ==
            observedAirDashSequence)
        {
            return;
        }

        observedAirDashSequence =
            currentSequence;

        FireOneShotTrigger(
            airSpecialTriggerHash,
            hasAirSpecialTrigger,
            "Air Special / attack4"
        );
    }

    /// <summary>
    /// 安全讀取 TankMeleeCombo 的 Networked State。
    ///
    /// 只檢查 meleeCombo != null 並不足夠；
    /// 職業切換或 Runtime 刷新同一幀，舊 C# Reference 可能仍存在，
    /// 但 NetworkObject 已經 Despawn。
    /// </summary>
    private bool TryReadMeleeState(
        out int attackSequence,
        out TankMeleeComboStep currentStep
    )
    {
        attackSequence =
            0;

        currentStep =
            TankMeleeComboStep.None;

        if (IsNetworkBehaviourReady(
                meleeCombo
            ) == false)
        {
            return false;
        }

        attackSequence =
            meleeCombo.AttackSequence;

        currentStep =
            meleeCombo.CurrentStep;

        return true;
    }

    /// <summary>
    /// 安全讀取 Player Root 的正式 Quick Action 啟動序號。
    /// Manager 只會在目前 ViewModel 為 Tank 時綁定本控制器，
    /// 所以序號增加代表 Tank F Quick Dash 已成功開始。
    /// </summary>
    private bool TryReadQuickActionSequence(
        out int activationSequence
    )
    {
        activationSequence =
            0;

        if (IsNetworkBehaviourReady(
                quickActionController
            ) == false)
        {
            return false;
        }

        activationSequence =
            quickActionController
                .ActivationSequence;

        return true;
    }

    /// <summary>
    /// 安全讀取 Tank 正式防禦狀態。
    /// </summary>
    private bool TryReadGuardState(
        out bool isGuarding
    )
    {
        isGuarding =
            false;

        if (IsNetworkBehaviourReady(
                guardAbility
            ) == false)
        {
            return false;
        }

        isGuarding =
            guardAbility.IsGuarding;

        return true;
    }

    /// <summary>
    /// 安全讀取 Tank Air Dash 正式啟動序號。
    /// </summary>
    private bool TryReadAirDashSequence(
        out int airDashSequence
    )
    {
        airDashSequence =
            0;

        if (IsNetworkBehaviourReady(
                airDashAbility
            ) == false)
        {
            return false;
        }

        airDashSequence =
            airDashAbility
                .AirDashSequence;

        return true;
    }

    /// <summary>
    /// Networked Property 的必要安全檢查。
    ///
    /// 防止：
    /// Networked properties can only be accessed when Spawned() has been called。
    /// </summary>
    private static bool IsNetworkBehaviourReady(
        NetworkBehaviour behaviour
    )
    {
        if (behaviour == null)
        {
            return false;
        }

        NetworkObject networkObject =
            behaviour.Object;

        return
            networkObject != null &&
            networkObject.IsValid;
    }

    #endregion

    // =====================================================================
    #region Animator Commands

    /// <summary>
    /// 重新建立清楚的 Trigger 邊緣。
    ///
    /// 先清除所有一次性 Trigger，再只送出本次需要的 Trigger，
    /// 避免同一幀狀態切換時 Animator 同時殘留兩個待處理 Trigger。
    /// </summary>
    private void FireOneShotTrigger(
        int triggerHash,
        bool triggerExists,
        string debugActionName
    )
    {
        if (animator == null ||
            triggerExists == false)
        {
            return;
        }

        ResetOneShotTriggers();

        animator.SetTrigger(
            triggerHash
        );

        if (debugAnimation)
        {
            Debug.Log(
                $"[Tank ViewModel Animation] " +
                $"Play {debugActionName}",
                this
            );
        }
    }

    /// <summary>
    /// 清除所有可能尚未被 Animator 消耗的一次性 Trigger。
    /// </summary>
    private void ResetOneShotTriggers()
    {
        if (animator == null)
        {
            return;
        }

        if (hasAttack1Trigger)
        {
            animator.ResetTrigger(
                attack1TriggerHash
            );
        }

        if (hasAttack2Trigger)
        {
            animator.ResetTrigger(
                attack2TriggerHash
            );
        }

        if (hasAttack3Trigger)
        {
            animator.ResetTrigger(
                attack3TriggerHash
            );
        }

        if (hasQuickMeleeTrigger)
        {
            animator.ResetTrigger(
                quickMeleeTriggerHash
            );
        }

        if (hasAirSpecialTrigger)
        {
            animator.ResetTrigger(
                airSpecialTriggerHash
            );
        }

        if (hasAppearTrigger)
        {
            animator.ResetTrigger(
                appearTriggerHash
            );
        }
    }

    /// <summary>
    /// 清除所有 Animator Runtime 狀態。
    /// Runtime 刷新或 ViewModel 停用時，不得把舊職業的 Guard Bool 帶到下一次。
    /// </summary>
    private void ResetAnimatorRuntimeParameters()
    {
        ResetOneShotTriggers();

        if (animator != null &&
            hasGuardingBool)
        {
            animator.SetBool(
                guardingBoolHash,
                false
            );
        }
    }

    #endregion

    // =====================================================================
    #region Animator Validation

    /// <summary>
    /// 快取 Parameter Hash 並在啟動時驗證名稱與型別。
    ///
    /// 如果 Animator 少建參數，應直接在 Console 報出明確錯誤，
    /// 不讓問題變成「Gameplay 有攻擊但動畫沒反應」的靜默失敗。
    /// </summary>
    private void CacheAndValidateAnimatorParameters()
    {
        if (animator == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonTankViewModelAnimator)}] " +
                $"找不到 Animator。",
                this
            );

            return;
        }

        attack1TriggerHash =
            Animator.StringToHash(
                attack1TriggerParameter
            );

        attack2TriggerHash =
            Animator.StringToHash(
                attack2TriggerParameter
            );

        attack3TriggerHash =
            Animator.StringToHash(
                attack3TriggerParameter
            );

        quickMeleeTriggerHash =
            Animator.StringToHash(
                quickMeleeTriggerParameter
            );

        guardingBoolHash =
            Animator.StringToHash(
                guardingBoolParameter
            );

        airSpecialTriggerHash =
            Animator.StringToHash(
                airSpecialTriggerParameter
            );

        appearTriggerHash =
            Animator.StringToHash(
                appearTriggerParameter
            );

        hasAttack1Trigger =
            HasAnimatorParameter(
                attack1TriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasAttack2Trigger =
            HasAnimatorParameter(
                attack2TriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasAttack3Trigger =
            HasAnimatorParameter(
                attack3TriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasQuickMeleeTrigger =
            HasAnimatorParameter(
                quickMeleeTriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasGuardingBool =
            HasAnimatorParameter(
                guardingBoolParameter,
                AnimatorControllerParameterType.Bool
            );

        hasAirSpecialTrigger =
            HasAnimatorParameter(
                airSpecialTriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasAppearTrigger =
            HasAnimatorParameter(
                appearTriggerParameter,
                AnimatorControllerParameterType.Trigger
            );
    }

    private bool HasAnimatorParameter(
        string parameterName,
        AnimatorControllerParameterType expectedType
    )
    {
        if (string.IsNullOrWhiteSpace(
                parameterName
            ))
        {
            Debug.LogError(
                $"[{nameof(FirstPersonTankViewModelAnimator)}] " +
                $"Animator Parameter 名稱不可為空。" +
                $"\n預期型別：{expectedType}",
                this
            );

            return false;
        }

        AnimatorControllerParameter[] parameters =
            animator.parameters;

        for (int index = 0;
             index < parameters.Length;
             index++)
        {
            AnimatorControllerParameter parameter =
                parameters[index];

            if (parameter.name !=
                parameterName)
            {
                continue;
            }

            if (parameter.type ==
                expectedType)
            {
                return true;
            }

            Debug.LogError(
                $"[{nameof(FirstPersonTankViewModelAnimator)}] " +
                $"Animator Parameter「{parameterName}」型別錯誤。" +
                $"\n目前：{parameter.type}" +
                $"\n需要：{expectedType}",
                this
            );

            return false;
        }

        Debug.LogError(
            $"[{nameof(FirstPersonTankViewModelAnimator)}] " +
            $"Animator 找不到 Parameter「{parameterName}」。" +
            $"\n需要型別：{expectedType}",
            this
        );

        return false;
    }

    #endregion

    // =====================================================================
    #region Inspector Validation

#if UNITY_EDITOR

    private void OnValidate()
    {
        if (animator == null)
        {
            animator =
                GetComponent<Animator>();
        }
    }

#endif

    #endregion
}
