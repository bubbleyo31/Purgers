using Fusion;
using UnityEngine;


/// <summary>
/// 將 SupportSMG 的 Networked 彈藥狀態轉成通用武器 HUD Snapshot。
///
/// 支援：
///
/// 普通彈匣
/// Infinite Reserve
/// Support 特殊能力 Infinite Magazine。
/// </summary>
[DisallowMultipleComponent]
public class SupportSMGWeaponHUDSource :
    MonoBehaviour,
    IPlayerWeaponHUDSource
{
    // =====================================================================
    #region References

    [Header("Support SMG HUD 資料來源")]

    [SerializeField]
    [Tooltip(
        "此 Support Profession Runtime 使用的 SupportSMG。\n\n" +
        "若留空，會從目前 Runtime Root 與子物件自動尋找。")]
    private SupportSMG supportSMG;

    #endregion

    // =====================================================================
    #region Presentation Data

    [Header("Support 武器 HUD 顯示")]

    [SerializeField]
    [Tooltip(
        "Support 目前武器顯示在 HUD 上的 Sprite。\n\n" +
        "未來 Support 更換武器圖片時，只需替換此 Runtime Prefab 的 Sprite。")]
    private Sprite weaponIcon;

    [SerializeField]
    [Min(0)]
    [Tooltip(
        "Support 普通彈匣必須嚴格低於多少發，才顯示換彈提醒。\n\n" +
        "設定 5 代表剩餘 0、1、2、3、4 發時顯示。\n\n" +
        "特殊能力 Infinite Magazine Active 時不會顯示低彈藥提醒。")]
    private int reloadReminderBelowAmmo =
        5;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        ResolveSupportSMG();
    }

    private void OnValidate()
    {
        reloadReminderBelowAmmo =
            Mathf.Max(
                0,
                reloadReminderBelowAmmo
            );
    }

    #endregion

    // =====================================================================
    #region IPlayerWeaponHUDSource

    public bool TryGetWeaponHUDSnapshot(
        out PlayerWeaponHUDSnapshot snapshot
    )
    {
        snapshot =
            default;

        if (isActiveAndEnabled == false)
        {
            return false;
        }

        ResolveSupportSMG();

        if (IsNetworkSourceReady(
                supportSMG
            ) == false)
        {
            return false;
        }

        int currentAmmo =
            Mathf.Max(
                0,
                supportSMG.MagazineAmmo
            );

        int reserveAmmo =
            Mathf.Max(
                0,
                supportSMG.ReserveAmmo
            );

        bool infiniteMagazine =
            supportSMG.HasInfiniteMagazine;

        bool infiniteReserve =
            supportSMG.HasInfiniteReserveAmmo;

        bool showReloadReminder =
            infiniteMagazine == false &&
            currentAmmo <
            Mathf.Max(
                0,
                reloadReminderBelowAmmo
            );

        snapshot =
            new PlayerWeaponHUDSnapshot(
                weaponIcon,
                PlayerWeaponHUDValueMode.Ammunition,
                currentAmmo,
                reserveAmmo,
                infiniteMagazine,
                infiniteReserve,
                showReloadReminder
            );

        return true;
    }

    #endregion

    // =====================================================================
    #region Resolve / Safety

    private void ResolveSupportSMG()
    {
        if (supportSMG != null)
        {
            return;
        }

        supportSMG =
            GetComponentInChildren<SupportSMG>(
                true
            );
    }

    private static bool IsNetworkSourceReady(
        NetworkBehaviour source
    )
    {
        return
            source != null &&
            source.Object != null &&
            source.Object.IsValid &&
            source.Runner != null &&
            source.Runner.IsRunning;
    }

    #endregion
}