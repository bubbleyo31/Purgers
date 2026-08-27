using UnityEngine;


/// <summary>
/// 武器 HUD 數值的顯示模式。
///
/// 目前 Ammo 與 Combo 都使用「目前 / 第二數值」格式，
/// 但仍保留模式，讓未來可以針對能量、熱量或特殊資源
/// 擴充不同的顯示動畫與文字樣式。
/// </summary>
public enum PlayerWeaponHUDValueMode : byte
{
    Ammunition = 0,
    Combo = 1
}


/// <summary>
/// 一個畫面 Frame 所需的完整武器 HUD 唯讀資料。
///
/// 使用 Snapshot 的原因：
///
/// 1. HUD 一次取得同一批資料。
/// 2. HUD 不需要知道真正武器類別。
/// 3. 未來新增職業時不必修改中央 HUD。
/// 4. Gameplay 元件不需要直接操作 Image 或 TMP。
/// </summary>
public readonly struct PlayerWeaponHUDSnapshot
{
    /// <summary>
    /// 目前職業武器圖片。
    /// </summary>
    public Sprite WeaponIcon
    {
        get;
    }

    /// <summary>
    /// 這份數值代表彈藥或 Combo。
    /// </summary>
    public PlayerWeaponHUDValueMode ValueMode
    {
        get;
    }

    /// <summary>
    /// 左側數值。
    ///
    /// Ammunition：目前彈匣。
    /// Combo：目前 Combo。
    /// </summary>
    public int CurrentValue
    {
        get;
    }

    /// <summary>
    /// 右側數值。
    ///
    /// Ammunition：備用彈藥。
    /// Combo：最高 Combo。
    /// </summary>
    public int SecondaryValue
    {
        get;
    }

    /// <summary>
    /// 左側是否應顯示無限符號。
    ///
    /// 目前用於 Support Infinite Magazine。
    /// </summary>
    public bool IsCurrentValueInfinite
    {
        get;
    }

    /// <summary>
    /// 右側是否應顯示無限符號。
    ///
    /// 目前用於 Attack / Support Infinite Reserve。
    /// </summary>
    public bool IsSecondaryValueInfinite
    {
        get;
    }

    /// <summary>
    /// 是否顯示共用換彈提醒物件包。
    ///
    /// Attack / Support：彈匣低於指定值。
    /// Tank：本階段保留接口但固定 false。
    /// </summary>
    public bool ShowReloadReminder
    {
        get;
    }

    public PlayerWeaponHUDSnapshot(
        Sprite weaponIcon,
        PlayerWeaponHUDValueMode valueMode,
        int currentValue,
        int secondaryValue,
        bool isCurrentValueInfinite,
        bool isSecondaryValueInfinite,
        bool showReloadReminder
    )
    {
        WeaponIcon =
            weaponIcon;

        ValueMode =
            valueMode;

        CurrentValue =
            currentValue;

        SecondaryValue =
            secondaryValue;

        IsCurrentValueInfinite =
            isCurrentValueInfinite;

        IsSecondaryValueInfinite =
            isSecondaryValueInfinite;

        ShowReloadReminder =
            showReloadReminder;
    }
}


/// <summary>
/// 任何職業只要能提供武器 HUD Snapshot，
/// 就可以被 LocalPlayerWeaponHUD 顯示。
///
/// 未來新增職業時：
///
/// 1. 在新 Runtime 建立新的 Source MonoBehaviour。
/// 2. 實作這個介面。
/// 3. 將 Source 掛進新職業 Runtime Prefab。
///
/// 不需要修改 LocalPlayerWeaponHUD。
/// </summary>
public interface IPlayerWeaponHUDSource
{
    /// <summary>
    /// 嘗試取得目前安全可讀的 HUD Snapshot。
    ///
    /// Runtime 或武器 NetworkBehaviour 尚未 Spawned 時必須回傳 false，
    /// 不可直接讀取 Networked Property。
    /// </summary>
    bool TryGetWeaponHUDSnapshot(
        out PlayerWeaponHUDSnapshot snapshot
    );
}