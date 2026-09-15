using System;


/// <summary>
/// 玩家可裝備能力的功能分類。
///
/// 這裡只描述能力由哪一條 Gameplay 管線驅動，
/// 不代表能力屬於 Attack、Tank 或 Support。
/// </summary>
public enum PlayerAbilityCategory : byte
{
    None = 0,

    /// <summary>
    /// 玩家進入 GrappleAirborne 後，透過專注輸入使用的能力。
    /// </summary>
    GrappleFocus = 1,

    /// <summary>
    /// 鈎索正式 Attached 到 Gameplay Target 時執行的能力。
    /// </summary>
    GrappleHit = 2
}


/// <summary>
/// 能力允許使用的職業集合。
///
/// 使用 Mask 而不是單一 Required Profession，讓能力可以只開放給
/// Attack + Support，或在未來直接取消職業限制。
/// </summary>
[Flags]
public enum PlayerProfessionMask : byte
{
    None = 0,
    Attack = 1 << 0,
    Tank = 1 << 1,
    Support = 1 << 2,
    All = Attack | Tank | Support
}


/// <summary>
/// 玩家能力職業限制的共用資料。
/// 每一個 Ability Definition 都保留這份設定。
/// </summary>
[Serializable]
public struct PlayerAbilityProfessionRule
{
    [UnityEngine.SerializeField]
    [UnityEngine.Tooltip("開啟後，能力只允許 Allowed Professions Mask 內的職業使用。關閉後忽略職業 Mask，任何目前職業都能使用。能力仍可保持已裝備，只是職業不合法時暫停使用。")]
    private bool restrictProfession;

    [UnityEngine.SerializeField]
    [UnityEngine.Tooltip("Restrict Profession 開啟時允許使用此能力的職業集合。可以同時勾選多個職業。")]
    private PlayerProfessionMask allowedProfessions;


    public bool RestrictProfession =>
        restrictProfession;


    public PlayerProfessionMask AllowedProfessions =>
        allowedProfessions;


    public bool IsAllowed(
        PlayerProfessionType profession
    )
    {
        if (restrictProfession == false)
        {
            return profession !=
                PlayerProfessionType.None;
        }

        PlayerProfessionMask professionMask =
            ToMask(
                profession
            );

        return professionMask !=
                PlayerProfessionMask.None &&
            (allowedProfessions & professionMask) != 0;
    }


    public static PlayerProfessionMask ToMask(
        PlayerProfessionType profession
    )
    {
        switch (profession)
        {
            case PlayerProfessionType.Attack:
                return PlayerProfessionMask.Attack;

            case PlayerProfessionType.Tank:
                return PlayerProfessionMask.Tank;

            case PlayerProfessionType.Support:
                return PlayerProfessionMask.Support;

            case PlayerProfessionType.None:
            default:
                return PlayerProfessionMask.None;
        }
    }
}


/// <summary>
/// 能力程式碼對外公布自己的固定分類。
///
/// 分類由程式實作，避免只靠 Inspector 把命中能力錯配成專注能力。
/// 實際槽位數量仍由 PlayerAbilitySlotLayoutDefinition 決定。
/// </summary>
public interface IPlayerAbilityCategorized
{
    PlayerAbilityCategory AbilityCategory
    {
        get;
    }
}
