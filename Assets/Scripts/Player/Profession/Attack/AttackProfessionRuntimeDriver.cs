using Fusion;
using UnityEngine;

/// <summary>
/// Attack 職業 Runtime Gameplay Driver。
///
/// ------------------------------------------------------------
///
/// 目前負責 Attack 每 Tick 的執行順序：
///
/// Aim
/// ↓
/// Focus
/// ↓
/// Weapon
///
/// ------------------------------------------------------------
///
/// 這些邏輯原本全部直接寫在 Player.cs。
///
/// 現在開始搬到 Attack Runtime。
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// 第一階段：
///
/// PlayerAimController
/// AttackFocusAbility
/// PlayerWeaponController
///
/// 暫時仍然掛在 Player Root。
///
/// Driver 會從 Owner Player 找到它們。
///
/// 下一階段才會正式將它們
/// 移到 AttackProfessionRuntime Prefab。
/// </summary>
[DisallowMultipleComponent]
public class AttackProfessionRuntimeDriver :
    PlayerProfessionRuntimeDriver
{
    // =====================================================================
    #region 暫時引用

    [Header("Attack Runtime 模組")]

    [SerializeField]
    [Tooltip("Attack ADS 控制器。第一階段會從 Owner Player Root 自動取得。下一階段實際搬進 Attack Runtime 後會改成從 Runtime 取得。")]
    private PlayerAimController aimController;

    [SerializeField]
    [Tooltip("Attack 專注技能。第一階段會從 Owner Player Root 自動取得。")]
    private AttackFocusAbility focusAbility;

    [SerializeField]
    [Tooltip("Attack 武器總控制器。第一階段會從 Owner Player Root 自動取得。")]
    private PlayerWeaponController weaponController;

    [SerializeField]
    [Tooltip("Attack 職業步槍。遷移階段會優先從 Attack Runtime 取得，找不到才暫時退回 Owner Player。")]
    private AttackRifle attackRifle;

    [SerializeField]
    [Tooltip("Attack 職業的 F 快速近戰。正式版本存在 Attack Profession Runtime 中，Driver 會在 Runtime Spawn 後將它綁定到真正 Player Core。若留空會自動取得。")]
    private AttackQuickMelee
        quickMelee;

    [SerializeField]
    [Tooltip("Attack 職業的勾索敵人標記能力。正式存在 Attack Profession Runtime 中，Driver 會在 Runtime Spawn 後將它綁定到 Owner Player 的 Grapple Interaction Controller。若留空會自動取得。")]
    private AttackGrappleMarkAbility
        grappleMarkAbility;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，Runtime 成功綁定 Attack Player 時會顯示找到哪些 Attack Component。建議目前測試階段保持開啟。")]
    private bool debugAttackRuntime =
        true;

    #endregion

    // =====================================================================
    #region Profession

    public override PlayerProfessionType Profession =>
        PlayerProfessionType.Attack;

    #endregion

    // =====================================================================
    #region Binding

    /// <summary>
    /// Attack Runtime 成功取得 Owner Player 後，
    /// 從 Player Root 取得目前既有 Attack 模組。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這只是遷移期間的橋接。
    ///
    /// 等下一階段真的把 Attack Component
    /// 搬進 Runtime Prefab 後，
    /// 這裡會改成：
    protected override void OnOwnerBound()
    {
        if (OwnerPlayer == null)
        {
            return;
        }

        // =============================================================
        // 1. 優先尋找 Runtime 自己的 Attack Components
        // =============================================================

        /*
        * 正式目標：
        *
        * AttackProfessionRuntime
        * ├─ PlayerAimController
        * ├─ AttackRifle
        * ├─ AttackFocusAbility
        * └─ PlayerWeaponController
        */
        aimController =
            GetComponent<PlayerAimController>();

        attackRifle =
            GetComponent<AttackRifle>();

        focusAbility =
            GetComponent<AttackFocusAbility>();

        weaponController =
            GetComponent<PlayerWeaponController>();

        quickMelee =
            GetComponent<AttackQuickMelee>();

        grappleMarkAbility =
            GetComponent<AttackGrappleMarkAbility>();
    
        // =============================================================
        // 2. 遷移期間 fallback
        // =============================================================

        /*
        * 目前這一步還沒有真的把 Attack Components
        * 從 Player Prefab 搬走。
        *
        * 所以如果 Runtime 找不到，
        * 暫時退回 Owner Player。
        *
        * ------------------------------------------------------------
        *
        * 等下一階段真正搬完後，
        * 這四個 fallback 才會刪除。
        */
        if (aimController == null)
        {
            aimController =
                OwnerPlayer
                    .GetComponent<PlayerAimController>();
        }

        if (attackRifle == null)
        {
            attackRifle =
                OwnerPlayer
                    .GetComponent<AttackRifle>();
        }

        if (focusAbility == null)
        {
            focusAbility =
                OwnerPlayer
                    .GetComponent<AttackFocusAbility>();
        }

        if (weaponController == null)
        {
            weaponController =
                OwnerPlayer
                    .GetComponent<PlayerWeaponController>();
        }

        // =============================================================
        // 3. Owner Binding
        // =============================================================

        /*
        * 順序不要亂。
        *
        * Aim
        * ↓
        * Rifle
        * ↓
        * Focus
        * ↓
        * Weapon Controller
        *
        * Focus 需要 Aim + Rifle。
        * Weapon Controller 又需要 Focus + Rifle。
        */

        if (aimController != null)
        {
            aimController.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        if (attackRifle != null)
        {
            attackRifle.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        if (focusAbility != null)
        {
            focusAbility.BindOwnerPlayer(
                OwnerPlayer,
                attackRifle,
                aimController
            );
        }

        if (weaponController != null)
        {
            weaponController.BindOwnerPlayer(
                OwnerPlayer,
                attackRifle,
                focusAbility
            );
        }
        
        // =============================================================
        // Attack Quick Melee
        // =============================================================

        if (quickMelee != null)
        {
            quickMelee.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        // =============================================================
        // Attack Grapple Mark Ability
        // =============================================================

        /*
        * Grapple Interaction Controller
        * 屬於 Player Core。
        *
        * AttackGrappleMarkAbility
        * 會從 OwnerPlayer 自動取得它，
        * 並在 State Authority 訂閱 AttackEnemyDetected。
        */
        if (grappleMarkAbility != null)
        {
            grappleMarkAbility.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        // =============================================================
        // 4. 驗證
        // =============================================================

        if (aimController == null)
        {
            Debug.LogError(
                $"[{nameof(AttackProfessionRuntimeDriver)}] " +
                $"找不到 PlayerAimController。",
                this
            );
        }

        if (attackRifle == null)
        {
            Debug.LogError(
                $"[{nameof(AttackProfessionRuntimeDriver)}] " +
                $"找不到 AttackRifle。",
                this
            );
        }

        if (focusAbility == null)
        {
            Debug.LogError(
                $"[{nameof(AttackProfessionRuntimeDriver)}] " +
                $"找不到 AttackFocusAbility。",
                this
            );
        }

        if (weaponController == null)
        {
            Debug.LogError(
                $"[{nameof(AttackProfessionRuntimeDriver)}] " +
                $"找不到 PlayerWeaponController。",
                this
            );
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugAttackRuntime)
        {
            Debug.Log(
                $"[Attack Runtime Driver] Owner Binding 完成。" +
                $"\nPlayer：{OwnerPlayer.name}" +
                $"\nAim：{(aimController != null)}" +
                $"\nRifle：{(attackRifle != null)}" +
                $"\nFocus：{(focusAbility != null)}" +
                $"\nWeapon Controller：{(weaponController != null)}" +
                $"\nQuick Melee：{(quickMelee != null)}" +
                $"\nGrapple Mark：{(grappleMarkAbility != null)}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Simulation

    /// <summary>
    /// Attack 職業每個 Fusion Tick 的玩法順序。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這就是原本 Player.cs 裡面的：
    ///
    /// 6. ADS
    /// 7. Focus
    /// 8. Weapon
    ///
    /// 現在全部搬到這裡。
    ///
    /// ------------------------------------------------------------
    ///
    /// Quick Action 仍然比這裡更早由 Player Core 處理。
    ///
    /// 所以如果 Attack F Quick Melee
    /// 已經透過 PlayerActionGate：
    ///
    /// Block Aim
    /// Block Fire
    /// Block Reload
    ///
    /// Aim 與 Weapon 在這裡仍會正確看到封鎖狀態。
    /// </summary>
    public override void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        if (IsBound == false)
        {
            return;
        }

        /*
         * 這層再檢查一次。
         *
         * Runtime 本身理論上只會在
         * CurrentProfession = Attack 時存在。
         *
         * 但多一層防呆可防止切換職業的同 Tick
         * 舊 Runtime 又執行一次 Attack。
         */
        if (OwnerProfession.CurrentProfession !=
            PlayerProfessionType.Attack)
        {
            return;
        }

        // =============================================================
        // 1. ADS
        // =============================================================

        if (aimController != null)
        {
            aimController.Simulate(
                input
            );
        }

        // =============================================================
        // 2. Attack Focus
        // =============================================================

        /*
         * Focus 不直接讀 Aim Input。
         *
         * 它認 PlayerAimController.IsAiming。
         *
         * 所以一定要排在 AimController 後面。
         */
        if (focusAbility != null)
        {
            focusAbility.Simulate();
        }

        // =============================================================
        // 3. Attack Weapon
        // =============================================================

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