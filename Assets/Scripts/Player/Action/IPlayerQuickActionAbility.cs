/// <summary>
/// 所有職業 F Quick Action
/// 都必須遵守的標準接口。
///
/// ------------------------------------------------------------
///
/// Attack
/// → AttackQuickMelee
///
/// Tank
/// → TankQuickDash
///
/// Support
/// → 未來 Support Quick Action
///
/// ------------------------------------------------------------
///
/// PlayerQuickActionController
/// 不需要知道每個職業能力內部怎麼運作。
///
/// 它只負責：
///
/// Startup
/// Active
/// Recovery
/// Input
/// Action Block
/// Weapon Interrupt。
/// </summary>
public interface IPlayerQuickActionAbility
{
    /// <summary>
    /// 這個 Quick Action 屬於哪個職業。
    /// </summary>
    PlayerProfessionType Profession
    {
        get;
    }

    /// <summary>
    /// 從按下 F
    /// 到真正 Active 前的時間。
    /// </summary>
    float StartupDuration
    {
        get;
    }

    /// <summary>
    /// Active 階段持續多久。
    ///
    /// Attack Melee 真正造成範圍傷害
    /// 之後就是在進入 Active 時執行一次。
    /// </summary>
    float ActiveDuration
    {
        get;
    }

    /// <summary>
    /// 攻擊完成後不能 Fire、Aim、Reload
    /// 的 Recovery 時間。
    /// </summary>
    float RecoveryDuration
    {
        get;
    }

    /// <summary>
    /// 此能力目前是否允許啟動。
    ///
    /// 未來個別職業能力可以再增加：
    ///
    /// Cooldown
    /// Resource
    /// State Requirement。
    /// </summary>
    bool CanStartQuickAction();

    /// <summary>
    /// Quick Action 正式開始。
    /// </summary>
    void OnQuickActionStarted(
        int activationSequence
    );

    /// <summary>
    /// 進入 Active 的瞬間。
    ///
    /// Attack Quick Melee
    /// 下一步會在這裡做一次範圍傷害。
    /// </summary>
    void OnQuickActionActive(
        int activationSequence
    );

    /// <summary>
    /// 整個 Quick Action 結束。
    /// </summary>
    void OnQuickActionFinished(
        int activationSequence
    );
}