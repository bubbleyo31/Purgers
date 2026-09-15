using Fusion;
using UnityEngine;


/// <summary>
/// Support 職業 Runtime Gameplay Driver。
///
/// ====================================================================
///
/// Support Runtime 目前正式負責：
///
/// PlayerAimController
/// ↓
/// PlayerWeaponController
/// ↓
/// SupportSMG
///
/// 以及：
///
/// SupportQuickMelee。
///
/// ====================================================================
///
/// 正式武器架構：
///
/// Attack
/// ├─ AttackRifle
/// └─ AttackFocusAbility
///
/// Support
/// └─ SupportSMG
///
/// Tank
/// └─ 無槍械武器。
///
/// ====================================================================
///
/// Support 不再使用：
///
/// AttackRifle
/// SupportRifleHealingAbility。
///
/// ====================================================================
///
/// SupportSMG 自己已經負責：
///
/// 1. Enemy Damage。
/// 2. Player Healing。
/// 3. 普通彈匣。
/// 4. Reload。
/// 5. Gameplay Recoil。
/// 6. Bullet Tracer。
/// 7. 特殊技能無限彈匣接口。
/// 8. 特殊技能獨立射速接口。
/// 9. 特殊技能零新增後座力。
/// 10. 特殊技能獨立治療量。
///
/// ====================================================================
///
/// Driver 只負責：
///
/// 1. 找到 Runtime Module。
/// 2. 將 Module 綁定到真正 Player Core。
/// 3. 每個 Fusion Tick 依正確順序驅動 Gameplay。
///
/// 不負責武器內部傷害或治療邏輯。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SupportSMG))]
[RequireComponent(typeof(PlayerWeaponController))]
[RequireComponent(typeof(PlayerAimController))]
[RequireComponent(typeof(SupportQuickMelee))]
public class SupportProfessionRuntimeDriver :
    PlayerProfessionRuntimeDriver
{
    // =====================================================================
    #region Support Runtime Modules


    [Header("Support 武器模組")]


    [SerializeField]
    [Tooltip("Support 職業使用的正式 SMG。SupportSMG 自己負責對 Enemy 造成傷害、對其他 Player 進行治療，以及未來空中特殊技能的無限彈匣、特殊射速、特殊治療量與零新增後座力。若留空會從 Support Profession Runtime Root 自動取得。")]
    private SupportSMG
        supportSMG;


    [SerializeField]
    [Tooltip("Support 使用的共用武器總控制器。PlayerWeaponController 只負責將 Fire 與 Reload Input 路由到 SupportSMG，不處理 SupportSMG 內部的治療或特殊技能規則。若留空會從 Support Profession Runtime Root 自動取得。")]
    private PlayerWeaponController
        weaponController;


    [Header("Support 共用戰鬥模組")]


    [SerializeField]
    [Tooltip("Support 使用的共用 ADS 控制器。負責右鍵瞄準與 ADS Gameplay。Support 與 Attack 共用 PlayerAimController，但 Support 不會因此取得 AttackFocusAbility。若留空會從 Support Profession Runtime Root 自動取得。")]
    private PlayerAimController
        aimController;

    [SerializeField]
    [Tooltip("Support 使用的 F 快速近戰。這顆 Ability 的 Profession 必須固定為 Support，讓 PlayerQuickActionController 能從目前 Support Runtime 正確找到它。若留空會從 Support Profession Runtime Root 自動取得。")]
    private SupportQuickMelee
        quickMelee;

    [SerializeField]
    [Tooltip("Support 的職業勾索拉取能力。負責監聽 Player Core 的 SupportEnemyDetected，並要求 Enemy 上的 SupportGrapplePullReceiver 執行定身與拉取。若留空會從 Support Profession Runtime Root 自動取得。")]
    private SupportGrapplePullAbility
        grapplePullAbility;

    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後，Support Runtime 完成 Owner Binding 時會顯示 SupportSMG、Aim、Weapon Controller、Quick Melee 是否正確找到，並檢查 Runtime 是否意外殘留 Attack 專屬元件。測試階段建議保持開啟。")]
    private bool debugSupportRuntime =
        true;


    #endregion


    // =====================================================================
    #region Profession


    /// <summary>
    /// 這顆 Runtime 永遠屬於 Support。
    /// </summary>
    public override PlayerProfessionType Profession =>
        PlayerProfessionType.Support;


    #endregion


    // =====================================================================
    #region Owner Binding


    /// <summary>
    /// Support Runtime 成功取得 Owner Player 後，
    /// 將所有 Runtime Module 綁定到真正 Player Core。
    ///
    /// ====================================================================
    ///
    /// 正式綁定順序：
    ///
    /// PlayerAimController
    /// ↓
    /// SupportSMG
    /// ↓
    /// SupportQuickMelee
    /// ↓
    /// PlayerWeaponController。
    ///
    /// ====================================================================
    ///
    /// 為什麼 Weapon Controller 最後綁？
    ///
    /// 因為 PlayerWeaponController 會保存：
    ///
    /// Owner Player
    /// SupportSMG。
    ///
    /// 所以先確保真正 SupportSMG
    /// 已完成自己的 Owner Binding，
    /// 再把它交給 Weapon Controller。
    /// </summary>
    protected override void OnOwnerBound()
    {
        // =============================================================
        // Owner Validation
        // =============================================================

        if (OwnerPlayer == null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"OnOwnerBound 被呼叫，但 OwnerPlayer = NULL。",
                this
            );


            return;
        }


        // =============================================================
        // 1. Runtime-local Module Resolve
        // =============================================================

        /*
         * Gameplay Module 全部存在：
         *
         * SupportProfessionRuntime Root。
         *
         * ------------------------------------------------------------
         *
         * 不從 Player Root 找：
         *
         * SupportSMG
         * PlayerWeaponController
         * PlayerAimController
         * SupportQuickMelee。
         *
         * ------------------------------------------------------------
         *
         * Player Root 只保存真正共用 Core。
         */
        supportSMG =
            GetComponent<SupportSMG>();


        weaponController =
            GetComponent<PlayerWeaponController>();


        aimController =
            GetComponent<PlayerAimController>();


        quickMelee =
            GetComponent<SupportQuickMelee>();


        grapplePullAbility = 
            GetComponent<SupportGrapplePullAbility>();

        // =============================================================
        // 2. Aim Owner Binding
        // =============================================================

        /*
         * PlayerAimController 是 Attack / Support
         * 共用的 ADS Gameplay。
         */
        if (aimController != null)
        {
            aimController.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        // =============================================================
        // 3. SupportSMG Owner Binding
        // =============================================================

        /*
         * SupportSMG 必須知道真正 Player Core，
         * 才能取得：
         *
         * PlayerMovement
         * Player NetworkObject
         * Input Authority
         * 自己的 Hitbox 排除判定。
         */
        if (supportSMG != null)
        {
            supportSMG.BindOwnerPlayer(
                OwnerPlayer
            );
        }


        // =============================================================
        // 4. Quick Melee Owner Binding
        // =============================================================

        if (quickMelee != null)
        {
            quickMelee.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        // =============================================================
        // Support Grapple Pull Owner Binding
        // =============================================================

        if (grapplePullAbility != null)
        {
            grapplePullAbility.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        // =============================================================
        // 5. Weapon Controller Owner Binding
        // =============================================================

        /*
         * ★ Support 現在使用的是新的 Overload：
         *
         * BindOwnerPlayer(
         *     Player,
         *     SupportSMG
         * )
         *
         * ------------------------------------------------------------
         *
         * 不再使用：
         *
         * BindOwnerPlayer(
         *     Player,
         *     AttackRifle,
         *     AttackFocusAbility
         * )
         *
         * ------------------------------------------------------------
         *
         * 所以 Support Runtime
         * 已正式與 AttackRifle 解耦。
         */
        if (weaponController != null)
        {
            weaponController.BindOwnerPlayer(
                OwnerPlayer,
                supportSMG
            );
        }


        // =============================================================
        // 6. Module Validation
        // =============================================================

        ValidateRuntimeModules();


        // =============================================================
        // 7. Attack-only Component Validation
        // =============================================================

        ValidateNoAttackOnlyComponents();


        // =============================================================
        // Debug
        // =============================================================

        if (debugSupportRuntime)
        {
            Debug.Log(
                $"[Support Runtime Driver] Owner Binding 完成。" +
                $"\nPlayer：{OwnerPlayer.name}" +
                $"\nProfession：" +
                $"{(OwnerProfession != null ? OwnerProfession.CurrentProfession.ToString() : "NULL")}" +
                $"\nSupportSMG：{(supportSMG != null)}" +
                $"\nAim：{(aimController != null)}" +
                $"\nWeapon Controller：{(weaponController != null)}" +
                $"\nQuick Melee：{(quickMelee != null)}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Runtime Validation


    /// <summary>
    /// 檢查 Support Runtime
    /// 所有必要 Gameplay Module。
    ///
    /// RequireComponent 已經能避免大部分缺失，
    /// 這裡仍保留明確 Error，
    /// 方便檢查 Prefab 或 Runtime Spawn 異常。
    /// </summary>
    private void ValidateRuntimeModules()
    {
        // =============================================================
        // SupportSMG
        // =============================================================

        if (supportSMG == null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"Support Profession Runtime Root 找不到 " +
                $"{nameof(SupportSMG)}。" +
                $"\n請確認 SupportSMG 與 Driver " +
                $"存在同一個 Runtime Root GameObject。",
                this
            );
        }


        // =============================================================
        // Weapon Controller
        // =============================================================

        if (weaponController == null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"Support Profession Runtime Root 找不到 " +
                $"{nameof(PlayerWeaponController)}。",
                this
            );
        }


        // =============================================================
        // Aim
        // =============================================================

        if (aimController == null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"Support Profession Runtime Root 找不到 " +
                $"{nameof(PlayerAimController)}。",
                this
            );
        }

        // =============================================================
        // Quick Melee
        // =============================================================

        if (quickMelee == null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"Support Profession Runtime Root 找不到 " +
                $"{nameof(SupportQuickMelee)}。",
                this
            );
        }
    }


    /// <summary>
    /// 防止 Support Runtime
    /// 因為以前複製 Attack Runtime
    /// 而殘留 Attack 專屬 Gameplay。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這裡只報錯。
    ///
    /// 不會在 Runtime 自動 Destroy Component。
    ///
    /// Prefab 結構錯誤
    /// 應該直接回 Prefab 修正，
    /// 不應該在遊戲執行時偷偷修改。
    /// </summary>
    private void ValidateNoAttackOnlyComponents()
    {
        // =============================================================
        // Attack Focus
        // =============================================================

        AttackFocusAbility accidentalFocus =
            GetComponent<AttackFocusAbility>();


        if (accidentalFocus != null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"Support Runtime 不應該存在 " +
                $"{nameof(AttackFocusAbility)}。" +
                $"\n目前物件：{gameObject.name}" +
                $"\n請從 SupportProfessionRuntime Prefab 移除該元件。",
                accidentalFocus
            );
        }


        // =============================================================
        // Attack Rifle
        // =============================================================

        AttackRifle accidentalAttackRifle =
            GetComponent<AttackRifle>();


        if (accidentalAttackRifle != null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"Support Runtime 已經正式改用 " +
                $"{nameof(SupportSMG)}，" +
                $"不應該再保留 {nameof(AttackRifle)}。" +
                $"\n請從 SupportProfessionRuntime Prefab " +
                $"移除舊的 AttackRifle。",
                accidentalAttackRifle
            );
        }


        // =============================================================
        // 舊 Support Healing Override
        // =============================================================

        SupportRifleHealingAbility
            accidentalOldHealingAbility =
                GetComponent<
                    SupportRifleHealingAbility
                >();


        if (accidentalOldHealingAbility != null)
        {
            Debug.LogError(
                $"[{nameof(SupportProfessionRuntimeDriver)}] " +
                $"SupportSMG 已經直接內建 Player Healing，" +
                $"所以 Support Runtime 不應再保留舊的 " +
                $"{nameof(SupportRifleHealingAbility)}。" +
                $"\n請從 SupportProfessionRuntime Prefab " +
                $"移除舊 Healing Override Component。",
                accidentalOldHealingAbility
            );
        }
    }


    #endregion


    // =====================================================================
    #region Simulation


    /// <summary>
    /// Support 職業每個 Fusion Tick 的 Gameplay。
    ///
    /// ====================================================================
    ///
    /// 執行順序：
    ///
    /// ① Aim
    /// ↓
    /// ② Weapon Controller
    ///
    /// ====================================================================
    ///
    /// Quick Melee 不在這裡直接 Simulate。
    ///
    /// 因為 F Quick Action
    /// 由 PlayerQuickActionController
    /// 透過 IPlayerQuickActionAbility
    /// 統一管理：
    ///
    /// Startup
    /// Active
    /// Recovery。
    ///
    /// ====================================================================
    ///
    /// 未來加入 Support 空中特殊技能後，
    /// 執行順序會再變成類似：
    ///
    /// Support Special Ability
    /// ↓
    /// Aim
    /// ↓
    /// Weapon。
    ///
    /// 但這一階段先不加入。
    /// </summary>
    public override void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        // =============================================================
        // Runtime 尚未完成 Owner Binding
        // =============================================================

        if (IsBound == false)
        {
            return;
        }


        // =============================================================
        // Profession Safety
        // =============================================================

        /*
         * 防止：
         *
         * Support Runtime
         * 已經準備 Despawn，
         *
         * 但同一個 Tick
         * 又多執行一次 Support Weapon。
         */
        if (OwnerProfession == null ||
            OwnerProfession.CurrentProfession !=
                PlayerProfessionType.Support)
        {
            return;
        }


        // =============================================================
        // 1. Aim
        // =============================================================

        if (aimController != null)
        {
            aimController.Simulate(
                input
            );
        }


        // =============================================================
        // 2. Weapon
        // =============================================================

        /*
         * PlayerWeaponController
         * 現在會依：
         *
         * CurrentProfession == Support
         *
         * 自動進：
         *
         * SimulateSupport()
         *
         * ↓
         *
         * SupportSMG.Simulate()
         */
        if (weaponController != null)
        {
            weaponController.Simulate(
                input,
                previousButtons
            );
        }
    }


    #endregion
}
