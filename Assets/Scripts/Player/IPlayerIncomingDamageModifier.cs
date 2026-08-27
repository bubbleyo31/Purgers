/// <summary>
/// 目前 Profession Runtime
/// 可以提供給 Player Core 的「受到傷害修改器」。
///
/// ====================================================================
///
/// Player Core 不需要知道：
///
/// TankGuardAbility
/// TankArmorAbility
/// SupportBarrier
///
/// 等具體職業能力。
///
/// ====================================================================
///
/// PlayerIncomingDamageModifierBridge
/// 只會問目前 Profession Runtime：
///
/// 「你有沒有 IPlayerIncomingDamageModifier？」
///
/// 有的話就把即將進入 Player Health 的
/// DamageRequest 交給它處理。
///
/// ====================================================================
///
/// 注意：
///
/// 這是「受到傷害」修改。
///
/// 與目前已有的：
///
/// IDamageRequestModifier
///
/// 不同。
///
/// IDamageRequestModifier 是 Damage Pipeline
/// 從被命中物件父階層自動找到的入口。
///
/// IPlayerIncomingDamageModifier 則是
/// Player Core → Profession Runtime
/// 之間的內部接口。
/// </summary>
public interface IPlayerIncomingDamageModifier
{
    /// <summary>
    /// 修改器執行優先度。
    ///
    /// 數字越小越早執行。
    ///
    /// 目前 Tank Guard 使用 100。
    ///
    /// 未來如果有：
    ///
    /// Shield = 50
    /// Guard = 100
    /// Armor = 200
    ///
    /// 就可以明確控制處理順序。
    /// </summary>
    int IncomingDamageModifierPriority
    {
        get;
    }

    /// <summary>
    /// 修改即將進入 Player Damage Receiver 的傷害。
    ///
    /// 只能由正式 Gameplay Authority
    /// 修改會影響 Gameplay 的狀態。
    /// </summary>
    void ModifyIncomingDamage(
        ref DamageRequest request
    );
}