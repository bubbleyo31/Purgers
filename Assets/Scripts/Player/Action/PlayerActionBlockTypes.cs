using System;

/// <summary>
/// 玩家目前可能被其他 Gameplay 系統禁止的操作。
///
/// 使用 Flags，代表同一時間可以禁止多個操作。
///
/// 例如 Attack Quick Melee：
///
/// Fire
/// +
/// Aim
/// +
/// Reload
///
/// 但不禁止：
///
/// Movement
/// Grapple
/// Look。
/// </summary>
[Flags]
public enum PlayerActionBlockMask : int
{
    /// <summary>
    /// 沒有任何操作被禁止。
    /// </summary>
    None = 0,

    /// <summary>
    /// 禁止武器射擊。
    /// </summary>
    Fire = 1 << 0,

    /// <summary>
    /// 禁止 ADS 瞄準。
    /// </summary>
    Aim = 1 << 1,

    /// <summary>
    /// 禁止開始換彈。
    /// </summary>
    Reload = 1 << 2,

    /// <summary>
    /// 禁止一般移動輸入。
    ///
    /// 目前 Attack Quick Melee 不會使用。
    /// 未來 Dash、Stun 等系統可能使用。
    /// </summary>
    Movement = 1 << 3,

    /// <summary>
    /// 禁止勾索。
    ///
    /// 目前 Attack Quick Melee 不會使用。
    /// </summary>
    Grapple = 1 << 4,

    /// <summary>
    /// 禁止啟動職業 Quick Action。
    ///
    /// 未來死亡、暈眩等狀態可以使用。
    /// </summary>
    QuickAction = 1 << 5,

    /// <summary>
    /// 禁止 Look 視角輸入。
    ///
    /// 一般戰鬥能力應盡量避免使用。
    /// </summary>
    Look = 1 << 6
}

/// <summary>
/// 是哪一個 Gameplay 系統正在禁止玩家操作。
///
/// 為什麼需要 Source：
///
/// 如果未來：
///
/// QuickAction 禁止 Fire
/// Stun 也禁止 Fire
///
/// QuickAction 結束時不能直接把 Fire 解鎖，
/// 因為 Stun 還存在。
///
/// 所以每個系統擁有自己的 Block Source。
/// </summary>
public enum PlayerActionBlockSource : byte
{
    /// <summary>
    /// 職業 F 快速行動。
    /// </summary>
    QuickAction = 0,

    /// <summary>
    /// 暈眩、硬控等狀態。
    /// </summary>
    StatusEffect = 1,

    /// <summary>
    /// 玩家死亡狀態。
    /// </summary>
    Death = 2,

    /// <summary>
    /// 其他特殊能力。
    /// </summary>
    SpecialAbility = 3,

    /// <summary>
    /// 系統級封鎖。
    ///
    /// 例如進入過場、Loading、特殊流程。
    /// </summary>
    System = 4,

    /// <summary>
    /// 目前武器動作造成的操作封鎖。
    ///
    /// 第一個正式用途：
    ///
    /// AttackRifle / SupportSMG Reload
    /// → Block Aim。
    ///
    /// 這個來源必須與 QuickAction、SpecialAbility、System 分開，
    /// 避免換彈結束時誤清除其他系統仍然需要的 Aim Block。
    /// </summary>
    WeaponAction = 5
}

/// <summary>
/// 武器動作被外部系統中斷的原因。
///
/// 目前第一個正式用途：
/// Quick Action 中斷 Reload。
///
/// 未來可以繼續支援：
/// Weapon Switch
/// Death
/// Stun
/// Special Ability。
/// </summary>
public enum WeaponInterruptReason : byte
{
    /// <summary>
    /// 沒有指定。
    /// </summary>
    None = 0,

    /// <summary>
    /// 玩家啟動職業 Quick Action。
    /// </summary>
    QuickAction = 1,

    /// <summary>
    /// 玩家切換武器。
    /// </summary>
    WeaponSwitch = 2,

    /// <summary>
    /// 玩家死亡。
    /// </summary>
    Death = 3,

    /// <summary>
    /// 玩家被暈眩或其他硬控。
    /// </summary>
    Stun = 4,

    /// <summary>
    /// 其他特殊能力要求中斷武器。
    /// </summary>
    SpecialAbility = 5,

    /// <summary>
    /// 系統強制中斷。
    /// </summary>
    Forced = 6
}