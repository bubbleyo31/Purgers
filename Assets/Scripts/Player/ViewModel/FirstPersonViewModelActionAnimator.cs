using Fusion;
using UnityEngine;


/// <summary>
/// Attack / Support 第一人稱 ViewModel 的基礎動作動畫控制器。
///
/// ====================================================================
///
/// 這支腳本只負責本地第一人稱 Presentation：
///
/// Appear
/// Idle
/// Shoot
/// Reload
/// Melee
///
/// 不負責傷害、彈藥、射速、換彈計時或近戰判定。
///
/// ====================================================================
///
/// 動畫資料來源：
///
/// Shoot
/// → AttackRifle / SupportSMG 的 ShotSequence。
///
/// Reload
/// → AttackRifle / SupportSMG 的 IsReloading。
///
/// Melee
/// → PlayerQuickActionController 的 ActivationSequence 與 CurrentPhase。
///
/// Appear
/// → ProfessionViewModelManager 在 ViewModel 建立完成後呼叫 PlayAppear()。
///
/// ====================================================================
///
/// 重要：
///
/// 不直接監聽滑鼠左鍵、R 或 F。
///
/// 因為按下輸入不代表 Gameplay 動作真的成功，例如：
///
/// 1. 沒有子彈。
/// 2. 正在換彈。
/// 3. 被 PlayerActionGate 封鎖。
/// 4. Quick Action 尚未正式啟動。
///
/// 使用正式 Networked Gameplay State，才能避免動畫誤播。
///
/// ====================================================================
///
/// ADS 動畫仍由 FirstPersonViewModelAimAnimator 的 ADS Layer 處理。
/// 本腳本只驅動 Base Layer，不會取代或修改目前已經通過的 ADS 架構。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FirstPersonViewModelActionAnimator :
    MonoBehaviour
{
    // =====================================================================
    #region Animator Reference

    [Header("Animator 引用")]

    [SerializeField]
    [Tooltip("目前 Attack / Support ViewModel 使用的 Animator。通常就是這個 Prefab Root 上，同時包含 Base Layer 與 ADS Layer 的 Animator。若留空，Awake 時會自動從同一物件取得。")]
    private Animator animator;

    #endregion

    // =====================================================================
    #region Animator Parameter Names

    [Header("Animator Parameter 名稱")]

    [SerializeField]
    [Tooltip("播放 Appear 動畫使用的 Trigger 名稱。Animator 內必須建立同名 Trigger。預設：Appear。")]
    private string appearTriggerParameter =
        "Appear";

    [SerializeField]
    [Tooltip("播放 Shoot 動畫使用的 Trigger 名稱。Animator 內必須建立同名 Trigger。預設：Shoot。全自動武器需要允許 Any State → Shoot 自我轉場，才能讓每次成功射擊都重新起播。")]
    private string shootTriggerParameter =
        "Shoot";

    [SerializeField]
    [Tooltip("控制 Reload 動畫狀態的 Bool 名稱。Animator 內必須建立同名 Bool。預設：Reloading。Gameplay 的 IsReloading 為 true 時進入 Reload，變回 false 時立即退出。")]
    private string reloadingBoolParameter =
        "Reloading";

    [SerializeField]
    [Tooltip("播放 Melee 動畫使用的 Trigger 名稱。Animator 內必須建立同名 Trigger。預設：Melee。只有正式 ActivationSequence 增加時才會觸發。")]
    private string meleeTriggerParameter =
        "Melee";

    [SerializeField]
    [Tooltip("表示近戰 Quick Action 是否仍在 Startup、Active 或 Recovery 的 Bool 名稱。Animator 內必須建立同名 Bool。預設：MeleeActive。用來讓 Melee 在 Gameplay 動作結束後才返回 Idle。")]
    private string meleeActiveBoolParameter =
        "MeleeActive";

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，會在 Console 顯示 Appear、Shoot、Reload、Melee 的動畫觸發資訊。確認功能後可關閉。")]
    private bool debugAnimation;

    #endregion

    // =====================================================================
    #region Bound Gameplay Sources

    /// <summary>
    /// 目前 ViewModel 所屬職業。
    ///
    /// 只允許 Attack / Support 使用本控制器的槍械與近戰路由。
    /// </summary>
    private PlayerProfessionType boundProfession =
        PlayerProfessionType.None;

    /// <summary>
    /// Attack Runtime 的正式武器。
    ///
    /// Support 時必須為 null。
    /// </summary>
    private AttackRifle attackRifle;

    /// <summary>
    /// Support Runtime 的正式武器。
    ///
    /// Attack 時必須為 null。
    /// </summary>
    private SupportSMG supportSMG;

    /// <summary>
    /// 本地 Player Root 上的 Quick Action Controller。
    ///
    /// 它提供正式的 ActivationSequence 與 CurrentPhase，
    /// 本腳本不直接讀取 F 輸入。
    /// </summary>
    private PlayerQuickActionController
        quickActionController;

    #endregion

    // =====================================================================
    #region Observation Cache

    /// <summary>
    /// 上一次已處理的成功射擊序號。
    /// </summary>
    private int observedShotSequence;

    /// <summary>
    /// 上一次已處理的 Quick Action 啟動序號。
    /// </summary>
    private int observedQuickActionSequence;

    /// <summary>
    /// 上一次已同步到 Animator 的 Reload 狀態。
    /// </summary>
    private bool observedReloading;

    /// <summary>
    /// 上一次已同步到 Animator 的 Melee Active 狀態。
    /// </summary>
    private bool observedMeleeActive;

    /// <summary>
    /// 是否已經取得第一份武器快照。
    ///
    /// 初次 Bind 時只建立基準，不把武器生成前累積的 Sequence 誤播成 Shoot。
    /// </summary>
    private bool weaponObservationInitialized;

    /// <summary>
    /// 是否已經取得第一份 Quick Action 快照。
    /// </summary>
    private bool quickActionObservationInitialized;

    #endregion

    // =====================================================================
    #region Animator Parameter Cache

    private int appearTriggerHash;
    private int shootTriggerHash;
    private int reloadingBoolHash;
    private int meleeTriggerHash;
    private int meleeActiveBoolHash;

    private bool hasAppearTrigger;
    private bool hasShootTrigger;
    private bool hasReloadingBool;
    private bool hasMeleeTrigger;
    private bool hasMeleeActiveBool;

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
        if (animator == null)
        {
            return;
        }

        ObserveReloadState();
        ObserveMeleeState();
        ObserveSuccessfulShot();
    }

    private void OnDisable()
    {
        ResetAnimatorRuntimeParameters();
    }

    #endregion

    // =====================================================================
    #region Public Binding

    /// <summary>
    /// 綁定目前 ViewModel 真正對應的 Gameplay 狀態來源。
    ///
    /// Profession Runtime 被刷新時，
    /// ProfessionViewModelManager 會重新呼叫本方法。
    ///
    /// 初次綁定只記錄目前 Sequence，
    /// 不會把過去已發生的射擊或近戰重新播放一次。
    /// </summary>
    public void BindGameplaySources(
        PlayerProfessionType profession,
        AttackRifle newAttackRifle,
        SupportSMG newSupportSMG,
        PlayerQuickActionController newQuickActionController
    )
    {
        ResetAnimatorRuntimeParameters();

        boundProfession =
            profession;

        attackRifle =
            profession == PlayerProfessionType.Attack
                ? newAttackRifle
                : null;

        supportSMG =
            profession == PlayerProfessionType.Support
                ? newSupportSMG
                : null;

        quickActionController =
            newQuickActionController;

        InitializeWeaponObservation();
        InitializeQuickActionObservation();

        if (debugAnimation)
        {
            Debug.Log(
                $"[ViewModel Action Animation 綁定]" +
                $"\n職業：{boundProfession}" +
                $"\nAttackRifle：" +
                $"{(attackRifle != null ? attackRifle.name : "None")}" +
                $"\nSupportSMG：" +
                $"{(supportSMG != null ? supportSMG.name : "None")}" +
                $"\nQuickAction：" +
                $"{(quickActionController != null ? quickActionController.name : "None")}",
                this
            );
        }
    }

    /// <summary>
    /// 解除舊 Runtime / Player 的 Gameplay 引用。
    ///
    /// 必須在舊 Runtime 被替換或 ViewModel 被 Destroy 前呼叫。
    /// </summary>
    public void UnbindGameplaySources()
    {
        ResetAnimatorRuntimeParameters();

        boundProfession =
            PlayerProfessionType.None;

        attackRifle =
            null;

        supportSMG =
            null;

        quickActionController =
            null;

        weaponObservationInitialized =
            false;

        quickActionObservationInitialized =
            false;
    }

    #endregion

    // =====================================================================
    #region Public Animation Commands

    /// <summary>
    /// 從頭播放一次 Appear。
    ///
    /// 目前由 ProfessionViewModelManager 在職業 ViewModel 建立後呼叫。
    /// 保留為 public，未來重生、重新持槍或其他展示流程也可以安全重用，
    /// 不必偽造一次職業切換。
    /// </summary>
    public void PlayAppear()
    {
        FireTrigger(
            appearTriggerHash,
            hasAppearTrigger,
            "Appear"
        );
    }

    #endregion

    // =====================================================================
    #region Initial Snapshot

    /// <summary>
    /// 建立武器觀察基準。
    /// </summary>
    private void InitializeWeaponObservation()
    {
        weaponObservationInitialized =
            TryReadWeaponState(
                out observedShotSequence,
                out observedReloading
            );

        if (weaponObservationInitialized &&
            hasReloadingBool)
        {
            animator.SetBool(
                reloadingBoolHash,
                observedReloading
            );
        }
    }

    /// <summary>
    /// 建立 Quick Action 觀察基準。
    ///
    /// 若 ViewModel 綁定時 Quick Action 已經在進行，
    /// 仍會進入 Melee，避免切換或重建後完全沒有動作畫面。
    /// </summary>
    private void InitializeQuickActionObservation()
    {
        if (IsNetworkBehaviourReady(
                quickActionController
            ) == false)
        {
            quickActionObservationInitialized =
                false;

            observedMeleeActive =
                false;

            return;
        }

        observedQuickActionSequence =
            quickActionController
                .ActivationSequence;

        observedMeleeActive =
            IsCurrentProfessionMeleeActive();

        quickActionObservationInitialized =
            true;

        if (hasMeleeActiveBool)
        {
            animator.SetBool(
                meleeActiveBoolHash,
                observedMeleeActive
            );
        }

        if (observedMeleeActive)
        {
            FireTrigger(
                meleeTriggerHash,
                hasMeleeTrigger,
                "Melee（綁定時已在進行）"
            );
        }
    }

    #endregion

    // =====================================================================
    #region Reload Observation

    /// <summary>
    /// 將 Gameplay IsReloading 同步到 Animator。
    ///
    /// 使用 Bool 而不是 Trigger，因為換彈可能被 Quick Action、
    /// 職業切換或其他 Gameplay 規則提前中斷。
    /// </summary>
    private void ObserveReloadState()
    {
        if (TryReadWeaponState(
                out _,
                out bool currentReloading
            ) == false)
        {
            return;
        }

        if (weaponObservationInitialized == false)
        {
            InitializeWeaponObservation();

            return;
        }

        if (currentReloading ==
            observedReloading)
        {
            return;
        }

        observedReloading =
            currentReloading;

        if (hasReloadingBool)
        {
            animator.SetBool(
                reloadingBoolHash,
                currentReloading
            );
        }

        if (debugAnimation)
        {
            Debug.Log(
                $"[ViewModel Action Animation] " +
                $"Reloading = {currentReloading}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Melee Observation

    /// <summary>
    /// 觀察正式 Quick Action 狀態。
    ///
    /// ActivationSequence 增加時播放一次 Melee；
    /// CurrentPhase 回到 Idle 時解除 MeleeActive。
    /// </summary>
    private void ObserveMeleeState()
    {
        if (IsNetworkBehaviourReady(
                quickActionController
            ) == false ||
            IsRifleProfession() == false)
        {
            return;
        }

        int currentSequence =
            quickActionController
                .ActivationSequence;

        bool currentMeleeActive =
            IsCurrentProfessionMeleeActive();

        if (quickActionObservationInitialized == false)
        {
            InitializeQuickActionObservation();

            return;
        }

        if (currentMeleeActive !=
            observedMeleeActive)
        {
            observedMeleeActive =
                currentMeleeActive;

            if (hasMeleeActiveBool)
            {
                animator.SetBool(
                    meleeActiveBoolHash,
                    currentMeleeActive
                );
            }
        }

        if (currentSequence ==
            observedQuickActionSequence)
        {
            return;
        }

        /*
         * 先更新 Cache。
         *
         * 即使這次 Sequence 變化不是目前 Attack / Support 的 Melee，
         * 也不能讓舊序號留著，否則之後可能延遲誤播。
         */
        observedQuickActionSequence =
            currentSequence;

        if (currentMeleeActive == false)
        {
            return;
        }

        FireTrigger(
            meleeTriggerHash,
            hasMeleeTrigger,
            "Melee"
        );
    }

    #endregion

    // =====================================================================
    #region Shoot Observation

    /// <summary>
    /// 只有 ShotSequence 真正改變時才播放 Shoot。
    ///
    /// 射擊被 Reload 或 Melee 封鎖時不會誤播。
    /// </summary>
    private void ObserveSuccessfulShot()
    {
        if (TryReadWeaponState(
                out int currentShotSequence,
                out bool currentReloading
            ) == false)
        {
            return;
        }

        if (weaponObservationInitialized == false)
        {
            InitializeWeaponObservation();

            return;
        }

        if (currentShotSequence ==
            observedShotSequence)
        {
            return;
        }

        /*
         * Runtime Refresh 會重新 Bind 並建立新基準。
         * 同一個 Runtime 內只要 Sequence 不同，
         * 就代表至少有一次新的成功射擊需要呈現。
         */
        observedShotSequence =
            currentShotSequence;

        bool meleeActive =
            IsCurrentProfessionMeleeActive();

        if (currentReloading ||
            meleeActive)
        {
            return;
        }

        FireTrigger(
            shootTriggerHash,
            hasShootTrigger,
            "Shoot"
        );
    }

    #endregion

    // =====================================================================
    #region Gameplay State Reading

    /// <summary>
    /// 依照目前職業讀取正式武器狀態。
    ///
    /// 不允許 Support 偷讀 AttackRifle，反之亦然。
    /// </summary>
    private bool TryReadWeaponState(
        out int shotSequence,
        out bool isReloading
    )
    {
        shotSequence =
            0;

        isReloading =
            false;

        switch (boundProfession)
        {
            case PlayerProfessionType.Attack:
            {
                if (IsNetworkBehaviourReady(
                        attackRifle
                    ) == false)
                {
                    return false;
                }

                shotSequence =
                    attackRifle
                        .ShotSequence;

                isReloading =
                    attackRifle
                        .IsReloading;

                return true;
            }

            case PlayerProfessionType.Support:
            {
                if (IsNetworkBehaviourReady(
                        supportSMG
                    ) == false)
                {
                    return false;
                }

                shotSequence =
                    supportSMG
                        .ShotSequence;

                isReloading =
                    supportSMG
                        .IsReloading;

                return true;
            }

            case PlayerProfessionType.Tank:
            case PlayerProfessionType.None:
            default:
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Attack / Support 的 Quick Action 目前是否仍在進行。
    ///
    /// 目前這兩個職業的 Quick Action 都是 Melee，
    /// 因此 ViewModel 可以共用 Melee 呈現。
    ///
    /// 未來若 Support 的 Quick Action 改成非近戰，
    /// 這裡不能繼續硬套 Melee；屆時應讓 Gameplay 提供動作種類。
    /// </summary>
    private bool IsCurrentProfessionMeleeActive()
    {
        if (IsNetworkBehaviourReady(
                quickActionController
            ) == false ||
            IsRifleProfession() == false)
        {
            return false;
        }

        return
            quickActionController
                .CurrentPhase !=
            PlayerQuickActionPhase.Idle;
    }

    private bool IsRifleProfession()
    {
        return
            boundProfession ==
                PlayerProfessionType.Attack ||
            boundProfession ==
                PlayerProfessionType.Support;
    }


    /// <summary>
    /// 確認 Fusion NetworkBehaviour 已經正式 Spawn，
    /// 而且仍然屬於目前有效的 NetworkObject。
    ///
    /// ====================================================================
    ///
    /// 只檢查 C# Reference != null 並不夠。
    ///
    /// 職業切換同一幀可能發生：
    ///
    /// 舊 Profession Runtime 已 Despawn
    /// ↓
    /// ViewModel 尚未收到 Manager Unbind
    /// ↓
    /// C# Reference 仍然存在
    /// ↓
    /// 讀取 [Networked] Property 直接拋出 InvalidOperationException。
    ///
    /// ====================================================================
    ///
    /// 因此任何 Presentation 腳本在讀取 Networked Property 前，
    /// 都必須同時確認：
    ///
    /// NetworkBehaviour.Object != null
    /// NetworkBehaviour.Object.IsValid == true。
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
    /// 安全地重新觸發 Animator Trigger。
    ///
    /// 先 Reset 再 Set，可讓全自動武器連續成功射擊時，
    /// 每次都留下明確的新 Trigger 邊緣。
    /// </summary>
    private void FireTrigger(
        int parameterHash,
        bool parameterExists,
        string debugActionName
    )
    {
        if (animator == null ||
            parameterExists == false)
        {
            return;
        }

        animator.ResetTrigger(
            parameterHash
        );

        animator.SetTrigger(
            parameterHash
        );

        if (debugAnimation)
        {
            Debug.Log(
                $"[ViewModel Action Animation] " +
                $"Play {debugActionName}",
                this
            );
        }
    }

    /// <summary>
    /// 清除可能殘留的 Trigger 與持續狀態。
    ///
    /// Runtime Refresh 或切換職業時，
    /// 舊 Runtime 的 Reload / Melee 不可以污染新 ViewModel 狀態。
    /// </summary>
    private void ResetAnimatorRuntimeParameters()
    {
        if (animator == null)
        {
            return;
        }

        if (hasAppearTrigger)
        {
            animator.ResetTrigger(
                appearTriggerHash
            );
        }

        if (hasShootTrigger)
        {
            animator.ResetTrigger(
                shootTriggerHash
            );
        }

        if (hasMeleeTrigger)
        {
            animator.ResetTrigger(
                meleeTriggerHash
            );
        }

        if (hasReloadingBool)
        {
            animator.SetBool(
                reloadingBoolHash,
                false
            );
        }

        if (hasMeleeActiveBool)
        {
            animator.SetBool(
                meleeActiveBoolHash,
                false
            );
        }

        observedReloading =
            false;

        observedMeleeActive =
            false;
    }

    #endregion

    // =====================================================================
    #region Animator Validation

    /// <summary>
    /// 快取 Animator StringToHash 並驗證名稱與型別。
    ///
    /// 參數少打一個字時直接在 Console 報錯，
    /// 不讓問題變成「按鍵有效但動畫沒反應」的靜默失敗。
    /// </summary>
    private void CacheAndValidateAnimatorParameters()
    {
        if (animator == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonViewModelActionAnimator)}] " +
                $"找不到 Animator。",
                this
            );

            return;
        }

        appearTriggerHash =
            Animator.StringToHash(
                appearTriggerParameter
            );

        shootTriggerHash =
            Animator.StringToHash(
                shootTriggerParameter
            );

        reloadingBoolHash =
            Animator.StringToHash(
                reloadingBoolParameter
            );

        meleeTriggerHash =
            Animator.StringToHash(
                meleeTriggerParameter
            );

        meleeActiveBoolHash =
            Animator.StringToHash(
                meleeActiveBoolParameter
            );

        hasAppearTrigger =
            HasAnimatorParameter(
                appearTriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasShootTrigger =
            HasAnimatorParameter(
                shootTriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasReloadingBool =
            HasAnimatorParameter(
                reloadingBoolParameter,
                AnimatorControllerParameterType.Bool
            );

        hasMeleeTrigger =
            HasAnimatorParameter(
                meleeTriggerParameter,
                AnimatorControllerParameterType.Trigger
            );

        hasMeleeActiveBool =
            HasAnimatorParameter(
                meleeActiveBoolParameter,
                AnimatorControllerParameterType.Bool
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
                $"[{nameof(FirstPersonViewModelActionAnimator)}] " +
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
                $"[{nameof(FirstPersonViewModelActionAnimator)}] " +
                $"Animator Parameter「{parameterName}」型別錯誤。" +
                $"\n目前：{parameter.type}" +
                $"\n需要：{expectedType}",
                this
            );

            return false;
        }

        Debug.LogError(
            $"[{nameof(FirstPersonViewModelActionAnimator)}] " +
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
