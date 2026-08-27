using Fusion;
using UnityEngine;


/// <summary>
/// 玩家武器總控制器。
///
/// ====================================================================
///
/// 這支 Controller 不負責真正的武器 Gameplay。
///
/// 它只負責：
///
/// 目前玩家是什麼職業
/// ↓
/// 將 Input 路由到正確的武器。
///
/// ====================================================================
///
/// 目前正式結構：
///
/// Attack
/// ├─ AttackRifle
/// └─ AttackFocusAbility
///
/// Support
/// └─ SupportSMG
///
/// Tank
/// └─ 沒有槍械武器
///
/// ====================================================================
///
/// Attack：
///
/// 普通狀態
/// → AttackRifle Normal Shot。
///
/// Focus Active
/// → AttackRifle Focus Shot。
///
/// --------------------------------------------------------------------
///
/// Support：
///
/// → SupportSMG。
///
/// SupportSMG 自己負責：
///
/// Enemy Damage
/// Player Healing
/// Special Infinite Magazine
/// Special Fire Rate
/// Special Healing
/// Special Recoil Rule。
///
/// --------------------------------------------------------------------
///
/// 因此 PlayerWeaponController
/// 完全不需要知道 Support Healing 的細節。
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponController :
    MonoBehaviour
{
    // =====================================================================
    #region Player Core References


    [Header("Player Core 引用")]


    [SerializeField]
    [Tooltip("玩家職業元件。Weapon Controller 會根據 Current Profession 決定目前應該驅動 AttackRifle、SupportSMG，或完全不驅動槍械。Profession Runtime 架構下通常由 BindOwnerPlayer 自動取得。")]
    private PlayerProfession profession;


    [SerializeField]
    [Tooltip("玩家統一操作封鎖管理器。所有槍械 Fire 與 Reload Input 都會先經過這裡檢查，避免 Quick Action、死亡、暈眩等狀態下仍然可以射擊或換彈。Profession Runtime 架構下通常由 BindOwnerPlayer 自動取得。")]
    private PlayerActionGate actionGate;


    #endregion


    // =====================================================================
    #region Attack Weapon


    [Header("Attack 武器")]


    [SerializeField]
    [Tooltip("Attack 職業使用的正式 AttackRifle。Attack Profession Runtime 會在 Owner Binding 時傳入。Support Runtime 不需要這個引用。")]
    private AttackRifle attackRifle;


    [SerializeField]
    [Tooltip("Attack 職業專屬 Focus Ability。只有 Attack Runtime 應該存在這顆能力。Support Runtime 不應擁有 AttackFocusAbility。")]
    private AttackFocusAbility attackFocusAbility;


    #endregion


    // =====================================================================
    #region Support Weapon


    [Header("Support 武器")]


    [SerializeField]
    [Tooltip("Support 職業使用的正式 SupportSMG。Support Profession Runtime 會在 Owner Binding 時傳入。Attack Runtime 不需要這個引用。")]
    private SupportSMG supportSMG;


    #endregion


    // =====================================================================
    #region Owner Player


    /// <summary>
    /// 這顆 Weapon Controller
    /// 真正所屬的 Player Core。
    ///
    /// 注意：
    ///
    /// PlayerWeaponController 現在通常位於
    /// Profession Runtime NetworkObject，
    /// 而不是 Player Root。
    /// </summary>
    private Player ownerPlayer;


    /// <summary>
    /// 目前真正擁有這顆 Weapon Controller 的 Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;


    #endregion


    // =====================================================================
    #region Attack Owner Binding


    /// <summary>
    /// Attack Profession Runtime 使用的 Owner Binding。
    ///
    /// ====================================================================
    ///
    /// 保留這個既有函式簽名，
    /// 是為了不破壞目前已經完成的：
///
/// AttackProfessionRuntimeDriver。
///
/// --------------------------------------------------------------------
///
/// Attack Runtime 傳入：
///
/// Owner Player
/// AttackRifle
/// AttackFocusAbility。
/// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer,
        AttackRifle runtimeRifle = null,
        AttackFocusAbility runtimeFocusAbility = null
    )
    {
        // =============================================================
        // Core
        // =============================================================

        BindCoreOwnerPlayer(
            newOwnerPlayer
        );


        if (ownerPlayer == null)
        {
            attackRifle =
                null;


            attackFocusAbility =
                null;


            return;
        }


        // =============================================================
        // Attack Rifle
        // =============================================================

        if (runtimeRifle != null)
        {
            attackRifle =
                runtimeRifle;
        }
        else
        {
            attackRifle =
                GetComponent<AttackRifle>();
        }


        // =============================================================
        // Attack Focus
        // =============================================================

        /*
         * Focus 是 Attack 可選依賴。
         *
         * 正常 Attack Runtime 應該會找到。
         *
         * 找不到不會讓 Controller 本身崩潰，
         * 只代表 Attack 無法進 Focus，
         * 普通 Rifle 仍然可以運作。
         */
        if (runtimeFocusAbility != null)
        {
            attackFocusAbility =
                runtimeFocusAbility;
        }
        else
        {
            attackFocusAbility =
                GetComponent<
                    AttackFocusAbility
                >();
        }
    }


    #endregion


    // =====================================================================
    #region Support Owner Binding


    /// <summary>
    /// Support Profession Runtime 使用的 Owner Binding。
    ///
    /// ====================================================================
    ///
    /// Support Runtime：
    ///
    /// Owner Player
    /// +
    /// SupportSMG。
    ///
    /// --------------------------------------------------------------------
///
/// 不需要：
///
/// AttackRifle
/// AttackFocusAbility。
///
/// --------------------------------------------------------------------
///
/// 下一步修改 SupportProfessionRuntimeDriver 時，
/// 就會正式改成呼叫這個 Overload。
/// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer,
        SupportSMG runtimeSMG
    )
    {
        // =============================================================
        // Core
        // =============================================================

        BindCoreOwnerPlayer(
            newOwnerPlayer
        );


        if (ownerPlayer == null)
        {
            supportSMG =
                null;


            return;
        }


        // =============================================================
        // Support SMG
        // =============================================================

        if (runtimeSMG != null)
        {
            supportSMG =
                runtimeSMG;
        }
        else
        {
            supportSMG =
                GetComponent<SupportSMG>();
        }


        /*
         * Support Runtime 不使用 Focus。
         *
         * 明確重新解析目前 Runtime，
         * 正常結果應該是 null。
         *
         * 這樣即使未來 Runtime Prefab
         * 曾經被錯誤複製過，
         * 也不會沿用某個舊 Reference。
         */
        attackFocusAbility =
            GetComponent<
                AttackFocusAbility
            >();
    }


    #endregion


    // =====================================================================
    #region Core Owner Binding


    /// <summary>
    /// Attack / Support 共用的 Player Core Binding。
    ///
    /// ====================================================================
    ///
    /// Weapon Controller 不論是哪個職業，
    /// 都需要：
    ///
    /// PlayerProfession
    /// PlayerActionGate。
    ///
    /// 所以集中在這裡處理，
    /// 避免兩個職業各複製一份。
    /// </summary>
    private void BindCoreOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        /*
        * Runtime 或 Owner 被替換前，
        * 先清除舊 PlayerActionGate 上
        * 可能殘留的 WeaponAction Source。
        */
        ClearWeaponActionBlocks();

        ownerPlayer =
            newOwnerPlayer;


        // =============================================================
        // Clear
        // =============================================================

        if (ownerPlayer == null)
        {
            profession =
                null;


            actionGate =
                null;


            return;
        }


        // =============================================================
        // Profession
        // =============================================================

        profession =
            ownerPlayer.Profession;


        // =============================================================
        // Action Gate
        // =============================================================

        actionGate =
            ownerPlayer
                .GetComponent<
                    PlayerActionGate
                >();


        // =============================================================
        // Validation
        // =============================================================

        if (profession == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerWeaponController)}] " +
                $"Owner Player 找不到 PlayerProfession。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }


        if (actionGate == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerWeaponController)}] " +
                $"Owner Player 找不到 PlayerActionGate。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }


    #endregion


    // =====================================================================
    #region Public Data


    /// <summary>
    /// Attack Runtime 使用的步槍。
    /// </summary>
    public AttackRifle AttackRifle =>
        attackRifle;


    /// <summary>
    /// Attack Runtime 使用的 Focus Ability。
    /// </summary>
    public AttackFocusAbility AttackFocusAbility =>
        attackFocusAbility;


    /// <summary>
    /// Support Runtime 使用的 SMG。
    /// </summary>
    public SupportSMG SupportSMG =>
        supportSMG;


    #endregion


    // =====================================================================
    #region Unity


    private void Awake()
    {
        // =============================================================
        // Runtime-local Weapon
        // =============================================================

        /*
         * 同一顆 PlayerWeaponController
         * 可以存在於：
         *
         * Attack Runtime
         * 或
         * Support Runtime。
         *
         * 因此兩種武器都只是「可選 Component」。
         *
         * 不再使用：
         *
         * RequireComponent(typeof(AttackRifle))
         *
         * 強迫所有職業擁有 AttackRifle。
         */
        if (attackRifle == null)
        {
            attackRifle =
                GetComponent<AttackRifle>();
        }


        if (supportSMG == null)
        {
            supportSMG =
                GetComponent<SupportSMG>();
        }


        if (attackFocusAbility == null)
        {
            attackFocusAbility =
                GetComponent<
                    AttackFocusAbility
                >();
        }


        // =============================================================
        // Legacy Player Root Compatibility
        // =============================================================

        if (profession == null)
        {
            profession =
                GetComponent<
                    PlayerProfession
                >();
        }


        if (actionGate == null)
        {
            actionGate =
                GetComponent<
                    PlayerActionGate
                >();
        }


        if (ownerPlayer == null)
        {
            Player localPlayer =
                GetComponent<Player>();


            if (localPlayer != null)
            {
                /*
                 * 舊架構通常只有 AttackRifle。
                 *
                 * 如果真的存在 SupportSMG
                 * 而不存在 AttackRifle，
                 * 則使用 Support Binding。
                 */
                if (supportSMG != null &&
                    attackRifle == null)
                {
                    BindOwnerPlayer(
                        localPlayer,
                        supportSMG
                    );
                }
                else
                {
                    BindOwnerPlayer(
                        localPlayer,
                        attackRifle,
                        attackFocusAbility
                    );
                }
            }
        }
    }


    #endregion


    // =====================================================================
    #region Simulation Entry


    /// <summary>
    /// 每個 Fusion Tick
    /// 由目前 Profession Runtime Driver 呼叫。
    ///
    /// ====================================================================
    ///
    /// Attack
    /// → SimulateAttack()
    ///
    /// Support
    /// → SimulateSupport()
    ///
    /// Tank
    /// → 不處理槍械。
    /// </summary>
    public bool Simulate(
    NetInput input,
    NetworkButtons previousButtons
    )
    {
        // =============================================================
        // Core Validation
        // =============================================================

        if (profession == null)
        {
            ClearWeaponActionBlocks();


            return false;
        }


        bool fired;


        // =============================================================
        // Profession Routing
        // =============================================================

        switch (profession.CurrentProfession)
        {
            // =========================================================
            // Attack
            // =========================================================

            case PlayerProfessionType.Attack:
            {
                fired =
                    SimulateAttack(
                        input,
                        previousButtons
                    );


                break;
            }


            // =========================================================
            // Support
            // =========================================================

            case PlayerProfessionType.Support:
            {
                fired =
                    SimulateSupport(
                        input,
                        previousButtons
                    );


                break;
            }


            // =========================================================
            // Tank / None
            // =========================================================

            case PlayerProfessionType.Tank:
            case PlayerProfessionType.None:
            default:
            {
                ClearWeaponActionBlocks();


                return false;
            }
        }


        // =============================================================
        // Reload → Block Aim
        // =============================================================

        /*
        * 必須放在武器 Simulate 完成後。
        *
        * 因為本 Tick 按下 Reload 時，
        * AttackRifle / SupportSMG 會在自己的 Simulate 裡
        * 正式把 IsReloading 設為 true。
        *
        * 之後再同步 ActionGate，
        * 才能只在 Reload 真正成功開始時封鎖 Aim。
        */
        SyncWeaponActionBlocks();


        return fired;
    }


    #endregion

    // =====================================================================
    #region Weapon Action Blocks


    /// <summary>
    /// 將目前正式武器的 Reload 狀態同步到 PlayerActionGate。
    ///
    /// Attack
    /// → AttackRifle.IsReloading。
    ///
    /// Support
    /// → SupportSMG.IsReloading。
    ///
    /// Reload 只擁有 WeaponAction Source，
    /// 不會覆蓋或清除 QuickAction、Death、Stun、SpecialAbility 等來源。
    /// </summary>
    private void SyncWeaponActionBlocks()
    {
        if (actionGate == null ||
            profession == null)
        {
            return;
        }


        bool isReloading =
            false;


        switch (profession.CurrentProfession)
        {
            case PlayerProfessionType.Attack:
            {
                isReloading =
                    attackRifle != null &&
                    attackRifle.IsReloading;


                break;
            }


            case PlayerProfessionType.Support:
            {
                isReloading =
                    supportSMG != null &&
                    supportSMG.IsReloading;


                break;
            }


            case PlayerProfessionType.Tank:
            case PlayerProfessionType.None:
            default:
            {
                isReloading =
                    false;


                break;
            }
        }


        actionGate.SetBlocks(
            PlayerActionBlockSource.WeaponAction,
            isReloading
                ? PlayerActionBlockMask.Aim
                : PlayerActionBlockMask.None
        );
    }


    /// <summary>
    /// 清除目前武器動作自己持有的封鎖。
    ///
    /// 只清除 WeaponAction Source，
    /// 不會誤解鎖其他 Gameplay Source。
    /// </summary>
    private void ClearWeaponActionBlocks()
    {
        if (actionGate == null)
        {
            return;
        }


        actionGate.ClearBlocks(
            PlayerActionBlockSource.WeaponAction
        );
    }


    #endregion

    // =====================================================================
    #region Shared Weapon Input


    /// <summary>
    /// 取得目前武器共用的 Fire / Reload Input。
    ///
    /// ====================================================================
    ///
    /// AttackRifle
    /// SupportSMG
    ///
    /// 都必須遵守：
    ///
    /// PlayerActionGate.Fire
    /// PlayerActionGate.Reload。
    ///
    /// --------------------------------------------------------------------
///
/// 所以不讓兩把武器各自複製：
///
/// IsBlocked(Fire)
/// IsBlocked(Reload)
///
/// 的判斷。
/// </summary>
    private void ResolveWeaponInput(
        NetInput input,
        NetworkButtons previousButtons,
        out bool fireHeld,
        out bool reloadPressed
    )
    {
        // =============================================================
        // Fire Block
        // =============================================================

        bool fireBlocked =
            actionGate != null &&
            actionGate.IsBlocked(
                PlayerActionBlockMask.Fire
            );


        // =============================================================
        // Reload Block
        // =============================================================

        bool reloadBlocked =
            actionGate != null &&
            actionGate.IsBlocked(
                PlayerActionBlockMask.Reload
            );


        // =============================================================
        // Fire
        // =============================================================

        fireHeld =
            fireBlocked == false &&
            input.Buttons.IsSet(
                InputButton.Fire
            );


        // =============================================================
        // Reload
        // =============================================================

        reloadPressed =
            reloadBlocked == false &&
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Reload
            );
    }


    #endregion


    // =====================================================================
    #region Attack


    /// <summary>
    /// Attack 職業武器模擬。
    ///
    /// ====================================================================
    ///
    /// 普通：
    ///
    /// AttackRifle
    /// +
    /// RifleShotModifier.Normal。
    ///
    /// --------------------------------------------------------------------
///
/// Focus：
///
/// AttackFocusAbility
/// ↓
/// Focus Shot Modifier
/// ↓
/// AttackRifle。
/// </summary>
    private bool SimulateAttack(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        // =============================================================
        // Rifle Validation
        // =============================================================

        if (attackRifle == null)
        {
            return false;
        }


        // =============================================================
        // Input
        // =============================================================

        ResolveWeaponInput(
            input,
            previousButtons,
            out bool fireHeld,
            out bool reloadPressed
        );


        // =============================================================
        // Focus
        // =============================================================

        bool focusActive =
            attackFocusAbility != null &&
            attackFocusAbility
                .IsFocusActive;


        // =============================================================
        // Normal Attack Rifle
        // =============================================================

        if (focusActive == false)
        {
            return attackRifle.Simulate(
                fireHeld,
                reloadPressed,
                RifleShotModifier.Normal
            );
        }


        // =============================================================
        // Focus Modifier
        // =============================================================

        RifleShotModifier modifier =
            attackFocusAbility
                .FocusShotModifier;


        /*
         * 只有這一 Tick
         * 真的可能發射 Focus Shot 時，
         * 才執行 Auto Lock 搜尋。
         */
        bool canActuallyAttemptShot =
            fireHeld &&
            attackRifle.IsFireRateReady &&
            attackRifle.IsReloading ==
                false &&
            attackRifle.MagazineAmmo >=
                modifier.AmmoCost;


        if (canActuallyAttemptShot)
        {
            modifier =
                attackFocusAbility
                    .BuildFocusShotModifier();
        }


        // =============================================================
        // Focus Fire
        // =============================================================

        /*
         * Focus 狀態下
         * 不處理普通 Reload Input。
         *
         * 保持你目前 Attack 的既有規則。
         */
        bool fired =
            attackRifle.Simulate(
                fireHeld,
                false,
                modifier
            );


        // =============================================================
        // Focus Successful Shot
        // =============================================================

        if (fired)
        {
            attackFocusAbility
                .NotifySuccessfulFocusShot();
        }


        return fired;
    }


    #endregion


    // =====================================================================
    #region Support


    /// <summary>
    /// Support 職業武器模擬。
    ///
    /// ====================================================================
    ///
    /// Support 不再：
    ///
    /// AttackRifle
    /// +
    /// RifleShotModifier.Normal。
    ///
    /// --------------------------------------------------------------------
///
/// 現在正式變成：
///
/// SupportSMG.Simulate()
///
/// --------------------------------------------------------------------
///
/// SupportSMG 自己決定：
///
/// Enemy → Damage
/// Player → Heal
/// Special → Infinite Magazine
/// Special → Fire Rate
/// Special → Heal Amount
/// Special → Recoil。
/// </summary>
    private bool SimulateSupport(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        // =============================================================
        // SMG Validation
        // =============================================================

        if (supportSMG == null)
        {
            return false;
        }


        // =============================================================
        // Input
        // =============================================================

        ResolveWeaponInput(
            input,
            previousButtons,
            out bool fireHeld,
            out bool reloadPressed
        );


        // =============================================================
        // Support SMG
        // =============================================================

        return supportSMG.Simulate(
            fireHeld,
            reloadPressed
        );
    }


    #endregion


    // =====================================================================
    #region Weapon Interrupt


    /// <summary>
    /// 外部系統要求中斷目前職業武器。
    ///
    /// ====================================================================
    ///
    /// 例如：
    ///
    /// Reload
    /// ↓
    /// F Quick Action
    /// ↓
    /// PlayerQuickActionController
    /// ↓
    /// PlayerWeaponController
    /// ↓
    /// 目前職業真正 Weapon。
    ///
    /// --------------------------------------------------------------------
///
/// Attack
/// → AttackRifle.InterruptWeaponAction()
///
/// Support
/// → SupportSMG.InterruptWeaponAction()
///
/// Tank
/// → 沒有槍械動作。
/// </summary>
    public bool InterruptCurrentWeapon(
        WeaponInterruptReason reason
    )
    {
        if (profession == null)
        {
            return false;
        }


        switch (profession.CurrentProfession)
        {
            // =========================================================
            // Attack
            // =========================================================

            case PlayerProfessionType.Attack:
            {
                if (attackRifle == null)
                {
                    return false;
                }


                return attackRifle
                    .InterruptWeaponAction(
                        reason
                    );
            }


            // =========================================================
            // Support
            // =========================================================

            case PlayerProfessionType.Support:
            {
                if (supportSMG == null)
                {
                    return false;
                }


                return supportSMG
                    .InterruptWeaponAction(
                        reason
                    );
            }


            // =========================================================
            // Tank / None
            // =========================================================

            case PlayerProfessionType.Tank:
            case PlayerProfessionType.None:
            default:
            {
                return false;
            }
        }
    }


    #endregion
}