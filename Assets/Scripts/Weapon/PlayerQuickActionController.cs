using Fusion;
using UnityEngine;

/// <summary>
/// 玩家職業 Quick Action 的共用階段。
/// </summary>
public enum PlayerQuickActionPhase : byte
{
    /// <summary>
    /// 沒有 Quick Action。
    /// F 可以重新使用。
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 技能起手階段。
    ///
    /// 未來通常對應動畫起手。
    /// </summary>
    Startup = 1,

    /// <summary>
    /// 技能真正作用的階段。
    ///
    /// Attack：
    /// 快速近戰傷害判定。
    ///
    /// Tank：
    /// 未來 Tank Quick Dash。
    ///
    /// Support：
    /// 未來 Support Quick Action。
    /// </summary>
    Active = 2,

    /// <summary>
    /// 技能結束後的收招時間。
    ///
    /// 是否封鎖 Fire、Aim、Reload
    /// 仍然由 Quick Action Controller 統一管理。
    /// </summary>
    Recovery = 3
}

/// <summary>
/// 玩家 F Quick Action 總控制器。
///
/// ------------------------------------------------------------
///
/// F 不代表「近戰」。
///
/// F 代表：
///
/// Player Profession Quick Action Slot。
///
/// ------------------------------------------------------------
///
/// Attack Runtime
/// → AttackQuickMelee
///
/// Tank Runtime
/// → TankQuickDash
///
/// Support Runtime
/// → SupportQuickAction
///
/// ------------------------------------------------------------
///
/// ★ PlayerQuickActionController 不再知道
/// 任何職業 Ability 的具體類別。
///
/// 它只認：
///
/// IPlayerQuickActionAbility
///
/// ------------------------------------------------------------
///
/// Controller 負責：
///
/// 1. Quick Action Input。
/// 2. Startup / Active / Recovery。
/// 3. ActionGate。
/// 4. 中斷目前職業武器動作。
/// 5. 從 Current Profession Runtime 找 Ability。
/// 6. 職業切換與 Runtime Refresh 安全處理。
///
/// ------------------------------------------------------------
///
/// Controller 不負責：
///
/// AttackQuickMelee 傷害內容。
/// TankQuickDash 位移內容。
/// SupportQuickAction 技能內容。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
[RequireComponent(typeof(PlayerActionGate))]
[RequireComponent(typeof(PlayerProfessionRuntimeManager))]
public class PlayerQuickActionController :
    NetworkBehaviour
{
    // =====================================================================
    #region Player Core 引用

    [Header("Player Core 引用")]

    [SerializeField]
    [Tooltip("玩家職業資料。Quick Action Controller 只用它確認目前正式職業，不再直接知道 Attack、Tank、Support 的具體 Ability。若留空會自動取得。")]
    private PlayerProfession profession;

    [SerializeField]
    [Tooltip("玩家統一操作封鎖管理器。Quick Action 使用期間會透過它禁止 Fire、Aim 與 Reload，但不禁止 Movement、Look 與 Grapple。若留空會自動取得。")]
    private PlayerActionGate actionGate;

    [SerializeField]
    [Tooltip("玩家職業 Runtime 管理器。Quick Action Controller 會從目前 Runtime 尋找 IPlayerQuickActionAbility 與目前職業的 Weapon Controller，而不是直接引用 AttackQuickMelee。若留空會自動取得。")]
    private PlayerProfessionRuntimeManager
        professionRuntimeManager;

    #endregion

    // =====================================================================
    #region 遷移階段設定

    [Header("職業 Runtime 遷移階段")]

    [SerializeField]
    [Tooltip("開啟後，如果目前 Profession Runtime 還找不到 IPlayerQuickActionAbility，會暫時從 Player Root 搜尋。這是讓目前尚未搬進 Runtime 的 AttackQuickMelee 繼續正常工作的過渡設定。等 AttackQuickMelee 真正搬進 Attack Runtime 後，這個選項會刪除。")]
    private bool allowLegacyPlayerRootAbilityFallback =
        true;

    [SerializeField]
    [Tooltip("開啟後，如果目前 Profession Runtime 還找不到 PlayerWeaponController，會暫時從 Player Root 搜尋。這是遷移期間讓 Attack Reload Interrupt 繼續工作的過渡設定。等 PlayerWeaponController 真正搬進 Attack Runtime 後，這個選項會刪除。")]
    private bool allowLegacyPlayerRootWeaponFallback =
        true;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會在 State Authority 顯示 Quick Action 階段切換、Runtime、職業與 Ability 資訊。")]
    private bool debugQuickAction =
        true;

    [SerializeField]
    [Tooltip("開啟後，如果使用了 Player Root 的舊版 Ability 或 Weapon Fallback，會額外顯示警告。等所有 Attack 元件搬完後，正常情況不應再看到這些訊息。")]
    private bool debugLegacyFallback =
        true;

    #endregion

    // =====================================================================
    #region Fusion State

    /// <summary>
    /// Quick Action 目前階段。
    /// </summary>
    [Networked]
    public PlayerQuickActionPhase CurrentPhase
    {
        get;
        private set;
    }

    /// <summary>
    /// 目前階段的計時器。
    /// </summary>
    [Networked]
    private TickTimer PhaseTimer
    {
        get;
        set;
    }

    /// <summary>
    /// 玩家生成後成功啟動過多少次 Quick Action。
    ///
    /// DamageRequest 等系統可以沿用這個 Sequence，
    /// 判斷同一次技能造成的多目標傷害。
    /// </summary>
    [Networked]
    public int ActivationSequence
    {
        get;
        private set;
    }

    /// <summary>
    /// 目前這一次 Quick Action
    /// 開始時的職業。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// Attack Quick Melee
    /// 正在 Recovery
    ///
    /// ↓
    ///
    /// 玩家突然切 Tank
    ///
    /// 系統會知道這是 Attack 的舊 Action，
    /// 必須直接結束。
    /// </summary>
    [Networked]
    private PlayerProfessionType ActiveProfession
    {
        get;
        set;
    }

    /// <summary>
    /// 這一次 Quick Action 開始時
    /// 所屬的 Profession Runtime。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個欄位主要防止：
    ///
    /// Attack Runtime A
    /// 正在 Quick Action
    ///
    /// ↓
    ///
    /// 玩家 F1 強制刷新 Attack 職業
    ///
    /// ↓
    ///
    /// Runtime A Despawn
    /// Runtime B Spawn
    ///
    /// ------------------------------------------------------------
    ///
    /// 雖然職業仍然都是 Attack，
    /// 但這已經不是同一個 Runtime。
    ///
    /// 舊 Quick Action 必須立即結束，
    /// 不能把舊階段接到新 Runtime Ability。
    /// </summary>
    [Networked]
    private NetworkObject ActiveRuntimeObject
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region 公開狀態

    /// <summary>
    /// 玩家目前是否正在執行任何 Quick Action。
    /// </summary>
    public bool IsQuickActionActive =>
        CurrentPhase !=
        PlayerQuickActionPhase.Idle;

    /// <summary>
    /// Quick Action 是否正在真正 Active 階段。
    /// </summary>
    public bool IsInActivePhase =>
        CurrentPhase ==
        PlayerQuickActionPhase.Active;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (actionGate == null)
        {
            actionGate =
                GetComponent<PlayerActionGate>();
        }

        if (professionRuntimeManager == null)
        {
            professionRuntimeManager =
                GetComponent<
                    PlayerProfessionRuntimeManager
                >();
        }
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentPhase =
                PlayerQuickActionPhase.Idle;

            PhaseTimer =
                TickTimer.None;

            ActivationSequence =
                0;

            ActiveProfession =
                PlayerProfessionType.None;

            ActiveRuntimeObject =
                null;
        }
    }

    #endregion

    // =====================================================================
    #region Simulation

    /// <summary>
    /// 每個 Fusion Tick 由 Player Core 呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// Quick Action 必須排在目前職業 Runtime
    /// 的主要 Combat Simulation 前面。
    ///
    /// 例如 Attack：
    ///
    /// F
    /// ↓
    /// Quick Action Controller
    /// ↓
    /// Block Fire / Aim / Reload
    /// ↓
    /// Attack Runtime
    /// ↓
    /// Aim / Focus / Rifle
    ///
    /// ------------------------------------------------------------
    ///
    /// 所以同一 Tick 按下 F 時，
    /// Attack Rifle 不會再多射一發。
    /// </summary>
    public void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        bool quickActionPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.QuickAction
            );

        // =============================================================
        // 已經正在執行 Quick Action
        // =============================================================

        if (CurrentPhase !=
            PlayerQuickActionPhase.Idle)
        {
            TickCurrentAction();

            return;
        }

        // =============================================================
        // 沒有按 F
        // =============================================================

        if (quickActionPressed == false)
        {
            return;
        }

        // =============================================================
        // 嘗試開始
        // =============================================================

        TryStartQuickAction();
    }

    #endregion

    // =====================================================================
    #region Start

    /// <summary>
    /// 嘗試開始目前職業 Runtime 提供的 Quick Action。
    /// </summary>
    private bool TryStartQuickAction()
    {
        if (profession == null ||
            actionGate == null ||
            professionRuntimeManager == null)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 其他系統禁止 F
        // -------------------------------------------------------------

        if (actionGate.IsBlocked(
                PlayerActionBlockMask.QuickAction
            ))
        {
            return false;
        }

        PlayerProfessionType currentProfession =
            profession.CurrentProfession;

        // -------------------------------------------------------------
        // 找目前職業真正 Ability
        // -------------------------------------------------------------

        IPlayerQuickActionAbility ability =
            GetCurrentQuickActionAbility(
                currentProfession
            );

        if (ability == null)
        {
            if (debugQuickAction &&
                Object.HasStateAuthority)
            {
                Debug.LogWarning(
                    $"[Player Quick Action] " +
                    $"目前職業找不到 IPlayerQuickActionAbility。" +
                    $"\nProfession：{currentProfession}" +
                    $"\nRuntime：" +
                    $"{GetCurrentRuntimeDebugName()}",
                    this
                );
            }

            return false;
        }

        // -------------------------------------------------------------
        // Ability 自己的啟動條件
        // -------------------------------------------------------------

        if (ability.CanStartQuickAction() ==
            false)
        {
            return false;
        }

        // -------------------------------------------------------------
        // ① Quick Action 優先中斷目前武器動作
        // -------------------------------------------------------------

        /*
         * 目前 Attack：
         *
         * Reload
         * ↓
         * F
         * ↓
         * Reload Cancel
         * ↓
         * Quick Melee
         *
         * ------------------------------------------------------------
         *
         * 未來 Tank：
         *
         * 如果它沒有 PlayerWeaponController，
         * 這裡就單純沒有 Weapon 可以 Interrupt。
         *
         * 不代表 F 不能使用。
         */
        InterruptCurrentProfessionWeapon(
            WeaponInterruptReason.QuickAction
        );

        // -------------------------------------------------------------
        // ② 封鎖 Fire、Aim、Reload
        // -------------------------------------------------------------

        /*
         * 目前沿用 Attack Quick Action 的共用規則。
         *
         * Tank Quick Dash 下一階段如果需要：
         *
         * Guard → F
         *
         * 這部分會由 Tank Ability
         * 在 CanStart / OnStart 處理 Guard 結束。
         */
        actionGate.SetBlocks(
            PlayerActionBlockSource.QuickAction,

            PlayerActionBlockMask.Fire |
            PlayerActionBlockMask.Aim |
            PlayerActionBlockMask.Reload
        );

        // -------------------------------------------------------------
        // ③ 建立 Action Sequence
        // -------------------------------------------------------------

        ActivationSequence++;

        ActiveProfession =
            currentProfession;

        /*
         * 保存這一次 Action
         * 是在哪一個 Runtime 開始。
         *
         * 即使之後同職業 Runtime 被刷新，
         * 也會被視為完全不同的一次 Runtime。
         */
        ActiveRuntimeObject =
            professionRuntimeManager
                .CurrentRuntimeObject;

        // -------------------------------------------------------------
        // ④ Ability Start
        // -------------------------------------------------------------

        ability.OnQuickActionStarted(
            ActivationSequence
        );

        // -------------------------------------------------------------
        // ⑤ Startup
        // -------------------------------------------------------------

        if (ability.StartupDuration >
            0f)
        {
            CurrentPhase =
                PlayerQuickActionPhase.Startup;

            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    ability.StartupDuration
                );
        }
        else
        {
            EnterActivePhase(
                ability
            );
        }

        DebugPhase(
            "Quick Action Started"
        );

        return true;
    }

    #endregion

    // =====================================================================
    #region Tick Phase

    /// <summary>
    /// 更新正在進行中的 Quick Action。
    /// </summary>
    private void TickCurrentAction()
    {
        // =============================================================
        // 職業被切換
        // =============================================================

        if (profession == null ||
            profession.CurrentProfession !=
                ActiveProfession)
        {
            ForceEndQuickAction();

            return;
        }

        // =============================================================
        // Runtime 被換掉
        // =============================================================

        /*
         * 即使：
         *
         * Attack
         * ↓
         * F1
         * ↓
         * Attack
         *
         * 職業 Enum 沒有改，
         *
         * Runtime 本身仍然已經刷新。
         *
         * 舊 Action 不能跑到新 Runtime。
         */
        NetworkObject currentRuntimeObject =
            professionRuntimeManager != null
                ? professionRuntimeManager
                    .CurrentRuntimeObject
                : null;

        if (ActiveRuntimeObject !=
            currentRuntimeObject)
        {
            ForceEndQuickAction();

            return;
        }

        // =============================================================
        // 找目前 Ability
        // =============================================================

        IPlayerQuickActionAbility ability =
            GetCurrentQuickActionAbility(
                ActiveProfession
            );

        if (ability == null)
        {
            ForceEndQuickAction();

            return;
        }

        // =============================================================
        // Phase
        // =============================================================

        switch (CurrentPhase)
        {
            // =========================================================
            // Startup
            // =========================================================

            case PlayerQuickActionPhase.Startup:
            {
                if (PhaseTimer.Expired(Runner))
                {
                    EnterActivePhase(
                        ability
                    );
                }

                break;
            }

            // =========================================================
            // Active
            // =========================================================

            case PlayerQuickActionPhase.Active:
            {
                if (PhaseTimer.Expired(Runner))
                {
                    EnterRecoveryPhase(
                        ability
                    );
                }

                break;
            }

            // =========================================================
            // Recovery
            // =========================================================

            case PlayerQuickActionPhase.Recovery:
            {
                if (PhaseTimer.Expired(Runner))
                {
                    CompleteQuickAction(
                        ability
                    );
                }

                break;
            }
        }
    }

    #endregion

    // =====================================================================
    #region Active

    /// <summary>
    /// 進入技能真正生效的時間點。
    ///
    /// ------------------------------------------------------------
    ///
    /// Attack Quick Melee：
    ///
    /// → 多目標近戰 Damage。
    ///
    /// Tank Quick Dash：
    ///
    /// → 未來開始短距離 Dash。
    ///
    /// ------------------------------------------------------------
    ///
    /// Ability 自己決定真正 Gameplay。
    /// Controller 不知道技能內容。
    /// </summary>
    private void EnterActivePhase(
        IPlayerQuickActionAbility ability
    )
    {
        CurrentPhase =
            PlayerQuickActionPhase.Active;

        ability.OnQuickActionActive(
            ActivationSequence
        );

        if (ability.ActiveDuration >
            0f)
        {
            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    ability.ActiveDuration
                );

            DebugPhase(
                "Active"
            );

            return;
        }

        EnterRecoveryPhase(
            ability
        );
    }

    #endregion

    // =====================================================================
    #region Recovery

    private void EnterRecoveryPhase(
        IPlayerQuickActionAbility ability
    )
    {
        CurrentPhase =
            PlayerQuickActionPhase.Recovery;

        if (ability.RecoveryDuration >
            0f)
        {
            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    ability.RecoveryDuration
                );

            DebugPhase(
                "Recovery"
            );

            return;
        }

        CompleteQuickAction(
            ability
        );
    }

    #endregion

    // =====================================================================
    #region Complete

    /// <summary>
    /// 正常完成 Quick Action。
    /// </summary>
    private void CompleteQuickAction(
        IPlayerQuickActionAbility ability
    )
    {
        ability?.OnQuickActionFinished(
            ActivationSequence
        );

        ResetQuickActionState();

        DebugPhase(
            "Finished"
        );
    }

    /// <summary>
    /// 外部系統或職業切換
    /// 強制結束目前 Quick Action。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如果舊 Runtime 已經 Despawn，
    /// ability 可能根本找不到。
    ///
    /// 這沒關係。
    ///
    /// 最重要的是：
    ///
    /// Phase
    /// ActionGate
    /// ActiveProfession
    /// ActiveRuntime
    ///
    /// 必須全部清乾淨。
    /// </summary>
    public void ForceEndQuickAction()
    {
        IPlayerQuickActionAbility ability =
            null;

        /*
         * 只有 Runtime 還是同一個時，
         * 才嘗試通知 Ability Finished。
         *
         * 如果 Runtime 已經換掉，
         * 不要拿新 Runtime Ability
         * 幫舊 Runtime 收尾。
         */
        if (professionRuntimeManager != null &&
            ActiveRuntimeObject ==
                professionRuntimeManager
                    .CurrentRuntimeObject)
        {
            ability =
                GetCurrentQuickActionAbility(
                    ActiveProfession
                );
        }

        ability?.OnQuickActionFinished(
            ActivationSequence
        );

        ResetQuickActionState();

        DebugPhase(
            "Force Finished"
        );
    }

    /// <summary>
    /// 清除 Controller 自己的所有 Quick Action 狀態。
    /// </summary>
    private void ResetQuickActionState()
    {
        CurrentPhase =
            PlayerQuickActionPhase.Idle;

        PhaseTimer =
            TickTimer.None;

        ActiveProfession =
            PlayerProfessionType.None;

        ActiveRuntimeObject =
            null;

        /*
         * 只移除 Quick Action 自己造成的 Block。
         *
         * StatusEffect
         * Death
         * Stun
         *
         * 等其他來源完全不受影響。
         */
        if (actionGate != null)
        {
            actionGate.ClearBlocks(
                PlayerActionBlockSource.QuickAction
            );
        }
    }

    #endregion

    // =====================================================================
    #region Profession Runtime Ability Routing

    /// <summary>
    /// 取得目前職業真正的 Quick Action Ability。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式架構：
    ///
    /// Current Profession Runtime
    /// ↓
    /// 搜尋 IPlayerQuickActionAbility
    ///
    /// ------------------------------------------------------------
    ///
    /// 不再：
    ///
    /// switch Attack
    /// → AttackQuickMelee
    ///
    /// switch Tank
    /// → TankQuickDash
    ///
    /// ------------------------------------------------------------
    ///
    /// 因此未來新增 Tank / Support
    /// 不需要再修改這支 Controller。
    /// </summary>
    private IPlayerQuickActionAbility
        GetCurrentQuickActionAbility(
            PlayerProfessionType professionType
        )
    {
        // =============================================================
        // 1. Current Runtime
        // =============================================================

        if (professionRuntimeManager != null)
        {
            NetworkObject runtimeObject =
                professionRuntimeManager
                    .CurrentRuntimeObject;

            if (runtimeObject != null &&
                runtimeObject.IsValid &&
                professionRuntimeManager
                    .CurrentRuntimeProfession ==
                    professionType)
            {
                IPlayerQuickActionAbility runtimeAbility =
                    FindQuickActionAbilityOnObject(
                        runtimeObject.gameObject,
                        professionType
                    );

                if (runtimeAbility != null)
                {
                    return runtimeAbility;
                }
            }
        }

        // =============================================================
        // 2. Legacy Player Root Fallback
        // =============================================================

        /*
         * ★ 遷移期間專用。
         *
         * 目前 AttackQuickMelee
         * 還沒有真正搬進 Attack Runtime。
         *
         * 所以先從 Player Root 找。
         *
         * ------------------------------------------------------------
         *
         * 等下一階段搬完：
         *
         * 這整段會刪除。
         */
        if (allowLegacyPlayerRootAbilityFallback)
        {
            IPlayerQuickActionAbility legacyAbility =
                FindQuickActionAbilityOnObject(
                    gameObject,
                    professionType
                );

            if (legacyAbility != null)
            {
                if (debugLegacyFallback &&
                    Object != null &&
                    Object.HasStateAuthority)
                {
                    Debug.LogWarning(
                        $"[Player Quick Action] " +
                        $"目前正在使用 Player Root 的舊版 Quick Action Fallback。" +
                        $"\nProfession：{professionType}" +
                        $"\nAbility：{legacyAbility.GetType().Name}" +
                        $"\n這在 AttackQuickMelee 搬進 Runtime 前屬於正常現象。",
                        this
                    );
                }

                return legacyAbility;
            }
        }

        return null;
    }

    /// <summary>
    /// 從指定 GameObject 找出
    /// 符合職業的 IPlayerQuickActionAbility。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不直接使用具體類別：
    ///
    /// AttackQuickMelee
    /// TankQuickDash
    ///
    /// 所以 Controller 保持職業無關。
    /// </summary>
    private IPlayerQuickActionAbility
        FindQuickActionAbilityOnObject(
            GameObject targetObject,
            PlayerProfessionType professionType
        )
    {
        if (targetObject == null)
        {
            return null;
        }

        /*
         * 使用 MonoBehaviour 掃描，
         * 再判斷 interface。
         *
         * 這樣不依賴 Unity 對 Interface
         * GetComponent<T>() 的版本差異。
         */
        MonoBehaviour[] behaviours =
            targetObject.GetComponents<MonoBehaviour>();

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IPlayerQuickActionAbility ability)
            {
                /*
                 * Ability 自己必須宣告
                 * 它是哪個 Profession。
                 */
                if (ability.Profession ==
                    professionType)
                {
                    return ability;
                }
            }
        }

        return null;
    }

    #endregion

    // =====================================================================
    #region Weapon Interrupt Routing

    /// <summary>
    /// 要求目前職業中斷可中斷的武器動作。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式架構：
    ///
    /// Current Runtime
    /// ↓
    /// PlayerWeaponController
    /// ↓
    /// InterruptCurrentWeapon()
    ///
    /// ------------------------------------------------------------
    ///
    /// Tank 如果未來沒有 PlayerWeaponController：
    ///
    /// → 直接視為沒有需要中斷的 Weapon Action。
    ///
    /// Quick Action 仍然可以正常執行。
    /// </summary>
    private bool InterruptCurrentProfessionWeapon(
        WeaponInterruptReason reason
    )
    {
        // =============================================================
        // 1. Current Profession Runtime
        // =============================================================

        if (professionRuntimeManager != null)
        {
            NetworkObject runtimeObject =
                professionRuntimeManager
                    .CurrentRuntimeObject;

            if (runtimeObject != null &&
                runtimeObject.IsValid)
            {
                PlayerWeaponController
                    runtimeWeaponController =
                        runtimeObject
                            .GetComponent<
                                PlayerWeaponController
                            >();

                if (runtimeWeaponController != null)
                {
                    return
                        runtimeWeaponController
                            .InterruptCurrentWeapon(
                                reason
                            );
                }
            }
        }

        // =============================================================
        // 2. Legacy Player Root
        // =============================================================

        /*
         * 遷移期間 Attack 的
         * PlayerWeaponController
         * 還在 Player Root。
         */
        if (allowLegacyPlayerRootWeaponFallback)
        {
            PlayerWeaponController
                legacyWeaponController =
                    GetComponent<
                        PlayerWeaponController
                    >();

            if (legacyWeaponController != null)
            {
                if (debugLegacyFallback &&
                    Object != null &&
                    Object.HasStateAuthority)
                {
                    Debug.LogWarning(
                        $"[Player Quick Action] " +
                        $"目前正在使用 Player Root 的舊版 Weapon Fallback。" +
                        $"\nProfession：{profession?.CurrentProfession}" +
                        $"\n這在 PlayerWeaponController 搬進 Runtime 前屬於正常現象。",
                        this
                    );
                }

                return
                    legacyWeaponController
                        .InterruptCurrentWeapon(
                            reason
                        );
            }
        }

        /*
         * 沒有 Weapon Controller
         * 不代表 Quick Action 失敗。
         *
         * 例如 Tank 完全可能是純近戰職業。
         */
        return false;
    }

    #endregion

    // =====================================================================
    #region Debug Helper

    private string GetCurrentRuntimeDebugName()
    {
        if (professionRuntimeManager == null)
        {
            return "Runtime Manager 不存在";
        }

        NetworkObject runtimeObject =
            professionRuntimeManager
                .CurrentRuntimeObject;

        if (runtimeObject == null)
        {
            return "無 Runtime";
        }

        return
            $"{runtimeObject.name} " +
            $"({professionRuntimeManager.CurrentRuntimeProfession})";
    }

    private void DebugPhase(
        string message
    )
    {
        if (debugQuickAction == false)
        {
            return;
        }

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        Debug.Log(
            $"[Player Quick Action] {message}" +
            $"\nProfession：{ActiveProfession}" +
            $"\nPhase：{CurrentPhase}" +
            $"\nSequence：{ActivationSequence}" +
            $"\nRuntime：" +
            $"{(ActiveRuntimeObject != null ? ActiveRuntimeObject.name : "無")}",
            this
        );
    }

    #endregion
}