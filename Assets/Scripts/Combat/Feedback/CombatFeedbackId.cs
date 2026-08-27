/// <summary>
/// 戰鬥命中回饋的攻擊身份。
///
/// ====================================================================
///
/// 這個 Enum 不負責決定：
///
/// 傷害如何計算
/// 是否為暴頭
/// 是否擊殺
/// Damage Type
/// Enemy HP。
///
/// ====================================================================
///
/// 它只回答一件事情：
///
/// 「這次造成傷害的攻擊，到底是哪一種攻擊？」
///
/// ====================================================================
///
/// 例如：
///
/// DamageType.Melee
///
/// 可能同時包含：
///
/// AttackQuickMelee
/// TankLightMelee
/// TankHeavyMelee
/// TankQuickDash
/// TankAirStrike。
///
/// 所以不能只依靠 DamageType
/// 決定 Camera Shake / Hit Marker。
///
/// ====================================================================
///
/// 注意：
///
/// 數值之後可能會經過 RPC 傳送。
///
/// 因此已經使用的數值不要任意交換。
/// </summary>
public enum CombatFeedbackId : byte
{
    /// <summary>
    /// 沒有指定特殊命中回饋。
    ///
    /// Presentation 可以退回使用
    /// DamageType / Headshot / Kill 的共用規則。
    /// </summary>
    None = 0,

    // =================================================================
    // Attack
    // =================================================================

    /// <summary>
    /// Attack 職業步槍。
    /// </summary>
    AttackRifle = 1,

    /// <summary>
    /// Attack 職業 F 快速近戰。
    /// </summary>
    AttackQuickMelee = 2,

    // =================================================================
    // Tank
    // =================================================================

    /// <summary>
    /// Tank 第一、二段普通輕攻擊。
    ///
    /// Light1 與 Light2
    /// 目前使用相同 Hit Feedback，
    /// 所以先共用同一個 ID。
    /// </summary>
    TankLightMelee = 10,

    /// <summary>
    /// Tank 第三段重攻擊。
    /// </summary>
    TankHeavyMelee = 11,

    /// <summary>
    /// Tank F 快速衝撞。
    ///
    /// 目前尚未實作，
    /// 先保留正式編號。
    /// </summary>
    TankQuickDash = 12,

    /// <summary>
    /// Tank GrappleAirborne
    /// 對敵人發動的高速衝刺重擊。
    /// </summary>
    TankAirStrike = 13,

    /// <summary>
    /// Tank GrappleAirborne 對 Enemy
    /// 成功發動特殊衝刺時的「衝刺動作回饋」。
    ///
    /// 注意：
    ///
    /// 這不是命中回饋。
    ///
    /// 就算最後沒有造成傷害，
    /// 只要 Enemy Dash 正式發動，
    /// 就可以播放衝刺 Camera Shake。
    /// </summary>
    TankAirDashEnemy = 14,

    /// <summary>
    /// Tank GrappleAirborne 對 World
    /// 成功發動特殊衝刺時的「衝刺動作回饋」。
    ///
    /// World Dash 本身沒有傷害，
    /// 所以這個 ID 只會用於本地衝刺 Presentation。
    /// </summary>
    TankAirDashWorld = 15,

    // =================================================================
    // Support
    // =================================================================

    /*
     * Support 之後從 20 開始。
     *
     * 這樣 Debug 時一眼就能知道：
     *
     * 1~9
     * Attack
     *
     * 10~19
     * Tank
     *
     * 20~29
     * Support。
     */
}