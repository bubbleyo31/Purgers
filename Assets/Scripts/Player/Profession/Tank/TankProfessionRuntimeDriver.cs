using Fusion;
using UnityEngine;

/// <summary>
/// Tank 職業 Runtime Driver。
///
/// ------------------------------------------------------------
///
/// Player Core 不知道 Tank 有：
///
/// Melee
/// Guard
/// Dash
/// Grapple Ability。
///
/// ------------------------------------------------------------
///
/// Player 只把 Input 傳給 Tank Runtime，
/// 再由這支 Driver 依固定順序驅動 Tank Gameplay。
///
/// ------------------------------------------------------------
///
/// 目前第一階段只加入：
///
/// TankMeleeCombo。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TankMeleeCombo))]
[RequireComponent(typeof(TankGuardAbility))]
[RequireComponent(typeof(TankQuickDashAbility))]
public class TankProfessionRuntimeDriver :
    PlayerProfessionRuntimeDriver
{
    // =====================================================================
    #region Tank Modules

    [Header("Tank Runtime 模組")]

    [SerializeField]
    [Tooltip("Tank 左鍵三段近戰 Combo。若留空會自動從 TankProfessionRuntime 取得。")]
    private TankMeleeCombo
        meleeCombo;

    [SerializeField]
    [Tooltip("Tank 右鍵防禦能力。若留空會自動從 Tank Profession Runtime 取得。")]
    private TankGuardAbility
        guardAbility;

    [SerializeField]
    [Tooltip("Tank 在 GrappleAirborne 狀態按右鍵時使用的空中特殊衝刺能力。目前第一階段只負責搜尋 Enemy 或 World Target，尚未真正移動玩家。若留空會自動取得。")]
    private TankAirDashAbility
        airDashAbility;
    
    [SerializeField]
    [Tooltip("Tank 勾索命中合法 Enemy Anchor 後的 Gather 能力。這支能力會監聽 Player Core 的 TankGatherAnchorDetected，並搜尋 Anchor 周圍最多三個可被聚集的 Enemy。若留空會自動取得。")]
    private TankGrappleGatherAbility
        grappleGatherAbility;

    [SerializeField]
    [Tooltip("Tank 的 F 短距離衝撞能力。擁有 3 格充能，每格獨立依序恢復，衝刺過程碰到 Enemy 會造成傷害。若留空會在 Owner Binding 時自動取得。")]
    private TankQuickDashAbility
        quickDashAbility;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會在 Tank Runtime 綁定 Owner Player 時顯示 Melee Combo 是否成功取得。")]
    private bool debugTankRuntime =
        true;

    #endregion

    // =====================================================================
    #region Profession

    public override PlayerProfessionType Profession =>
        PlayerProfessionType.Tank;

    #endregion

    // =====================================================================
    #region Public Modules

    /// <summary>
    /// Tank 左鍵近戰 Combo。
    ///
    /// 後面的：
    ///
    /// Guard
    /// Quick Dash
    /// Grapple Gather
    ///
    /// 都可以從 Runtime 取得這個模組。
    /// </summary>
    public TankMeleeCombo MeleeCombo =>
        meleeCombo;

    #endregion

    // =====================================================================
    #region Binding

    protected override void OnOwnerBound()
    {
        meleeCombo =
            GetComponent<TankMeleeCombo>();
        
        guardAbility =
            GetComponent<TankGuardAbility>();

        airDashAbility =
            GetComponent<TankAirDashAbility>();

        grappleGatherAbility = 
            GetComponent<TankGrappleGatherAbility>();

        quickDashAbility =
            GetComponent<TankQuickDashAbility>();

        if (meleeCombo == null)
        {
            Debug.LogError(
                $"[{nameof(TankProfessionRuntimeDriver)}] " +
                $"Tank Runtime 找不到 " +
                $"{nameof(TankMeleeCombo)}。",
                this
            );

            return;
        }

        if (meleeCombo != null)
        {
            meleeCombo.BindOwnerPlayer(
                OwnerPlayer
            );
        }
        
        if (guardAbility != null)
        {
            guardAbility.BindOwnerPlayer(
                OwnerPlayer,
                meleeCombo
            );
        }

        if (airDashAbility != null)
        {
            airDashAbility.BindOwnerPlayer(
                OwnerPlayer
            );
        }

        if (grappleGatherAbility != null)
        {
            grappleGatherAbility
                .BindOwnerPlayer(
                    OwnerPlayer
                );
        }

        if (quickDashAbility != null)
        {
            quickDashAbility
                .BindOwnerPlayer(
                    OwnerPlayer
                );
        }

        if (debugTankRuntime)
        {
            Debug.Log(
                $"[Tank Runtime Driver] Owner Binding 完成。" +
                $"\nPlayer：{OwnerPlayer.name}" +
                $"\nMelee Combo：{(meleeCombo != null)}" +
                $"\nGuard：{(guardAbility != null)}" +
                $"\nAir Dash：{(airDashAbility != null)}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Simulation

    public override void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        if (IsBound == false)
        {
            return;
        }

        RefreshLoadoutAbilityReferences();

        if (OwnerProfession.CurrentProfession !=
            PlayerProfessionType.Tank)
        {
            return;
        }

        // =============================================================
        // Tank F Quick Dash Runtime
        // =============================================================

        /*
        * F 按鍵是否能啟動技能，
        * 已經由 PlayerQuickActionController 處理。
        *
        * 這裡只負責：
        *
        * 1. Charge 恢復。
        * 2. Dash 持續位移。
        * 3. Dash 路徑傷害判定。
        *
        * 因為 TankProfessionRuntimeDriver
        * 在 Player 普通 Movement 後面執行，
        * 所以 Dash 可以取得更高的 KCC 位移優先權。
        */
        if (quickDashAbility != null)
        {
            quickDashAbility
                .SimulateRuntime();
        }

        // =============================================================
        // 1. Tank Air Special
        // =============================================================

        /*
        * 右鍵的最高優先路由。
        *
        * 目前只有：
        *
        * GrappleAirborne
        * +
        * Aim WasPressed
        *
        * 才會執行 Target Selection。
        *
        * ------------------------------------------------------------
        *
        * 普通狀態按右鍵：
        *
        * Air Dash 不做任何事情，
        * 繼續交給下面 Guard。
        */
        if (airDashAbility != null &&
            (quickDashAbility == null ||
            quickDashAbility.IsDashing == false))
        {
            airDashAbility.Simulate(
                input,
                previousButtons
            );
        }

        // =============================================================
        // 2. Guard
        // =============================================================

        /*
        * GrappleAirborne 時
        * TankGuardAbility 自己會拒絕 Guard。
        *
        * 所以同一顆右鍵：
        *
        * 普通狀態
        * → Guard。
        *
        * GrappleAirborne
        * → Air Special。
        */
        if (guardAbility != null &&
            (quickDashAbility == null ||
            quickDashAbility.IsCombatActionLocked == false) &&
            (airDashAbility == null ||
            airDashAbility.IsDashing == false) &&
            (grappleGatherAbility == null ||
            grappleGatherAbility
                .IsPlayerGatherDashing == false))
        {
            guardAbility.Simulate(
                input
            );
        }

        // =============================================================
        // 3. Tank Melee
        // =============================================================

        if (meleeCombo != null &&
            (quickDashAbility == null ||
            quickDashAbility.IsCombatActionLocked == false) &&
            (airDashAbility == null ||
            airDashAbility.IsDashing == false) &&
            (grappleGatherAbility == null ||
            grappleGatherAbility.IsPlayerGatherDashing == false) &&
            (guardAbility == null ||
            guardAbility.IsGuarding == false))
        {
            meleeCombo.Simulate(
                input,
                previousButtons
            );
        }
    }


    /// <summary>
    /// Air Dash 與 Gather 已屬玩家 Loadout；每 Tick 重新查詢可處理
    /// Loadout 同數量換裝與 Runtime 延後同步。Guard、Melee、Quick Dash
    /// 仍然只取 Tank Profession Runtime，不會被此查詢開放。
    /// </summary>
    private void RefreshLoadoutAbilityReferences()
    {
        PlayerAbilityRuntimeManager manager =
            OwnerPlayer != null
                ? OwnerPlayer.AbilityRuntimeManager
                : null;

        if (manager == null)
        {
            return;
        }

        if (manager.TryGetActiveModule(
                out TankAirDashAbility loadoutAirDash
            ))
        {
            airDashAbility =
                loadoutAirDash;
        }
        else
        {
            airDashAbility =
                GetComponent<TankAirDashAbility>();
        }

        if (manager.TryGetActiveModule(
                out TankGrappleGatherAbility loadoutGather
            ))
        {
            grappleGatherAbility =
                loadoutGather;
        }
        else
        {
            grappleGatherAbility =
                GetComponent<TankGrappleGatherAbility>();
        }
    }

    #endregion
}
