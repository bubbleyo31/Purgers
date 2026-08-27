using Fusion;
using UnityEngine;


/// <summary>
/// 將 AttackRifle 的 Networked 彈藥狀態轉成通用武器 HUD Snapshot。
///
/// 本元件只讀資料，不修改射擊、彈匣、備彈或換彈流程。
/// </summary>
[DisallowMultipleComponent]
public class AttackRifleWeaponHUDSource :
    MonoBehaviour,
    IPlayerWeaponHUDSource
{
    // =====================================================================
    #region References

    [Header("Attack Rifle HUD 資料來源")]

    [SerializeField]
    [Tooltip(
        "此 Attack Profession Runtime 使用的 AttackRifle。\n\n" +
        "若留空，會從目前 Runtime Root 與子物件自動尋找。")]
    private AttackRifle attackRifle;

    #endregion

    // =====================================================================
    #region Presentation Data

    [Header("Attack 武器 HUD 顯示")]

    [SerializeField]
    [Tooltip(
        "Attack 目前武器顯示在 HUD 上的 Sprite。\n\n" +
        "未來 Attack 更換武器圖片時，只需替換此 Runtime Prefab 的 Sprite，" +
        "不需要修改中央 HUD。")]
    private Sprite weaponIcon;

    [SerializeField]
    [Min(0)]
    [Tooltip(
        "Attack 彈匣必須嚴格低於多少發，才顯示換彈提醒。\n\n" +
        "設定 5 代表剩餘 0、1、2、3、4 發時顯示；" +
        "剩餘 5 發時不顯示。")]
    private int reloadReminderBelowAmmo =
        5;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        ResolveAttackRifle();
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

        ResolveAttackRifle();

        /*
         * 必須先確認 AttackRifle 已正式 Spawned，
         * 才可以讀 MagazineAmmo、ReserveAmmo 等 Networked Property。
         *
         * 這一層就是為了防止職業切換交界出現：
         * Networked properties can only be accessed when Spawned() has been called。
         */
        if (IsNetworkSourceReady(
                attackRifle
            ) == false)
        {
            return false;
        }

        int currentAmmo =
            Mathf.Max(
                0,
                attackRifle.MagazineAmmo
            );

        int reserveAmmo =
            Mathf.Max(
                0,
                attackRifle.ReserveAmmo
            );

        bool infiniteReserve =
            attackRifle.HasInfiniteReserveAmmo;

        bool showReloadReminder =
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
                false,
                infiniteReserve,
                showReloadReminder
            );

        return true;
    }

    #endregion

    // =====================================================================
    #region Resolve / Safety

    private void ResolveAttackRifle()
    {
        if (attackRifle != null)
        {
            return;
        }

        attackRifle =
            GetComponentInChildren<AttackRifle>(
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