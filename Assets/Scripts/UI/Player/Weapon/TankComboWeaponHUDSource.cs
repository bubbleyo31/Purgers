using Fusion;
using UnityEngine;


/// <summary>
/// 將 TankMeleeCombo 狀態轉成通用武器 HUD Snapshot。
///
/// 顯示格式：
///
/// 目前 Combo / 最高 Combo。
///
/// ====================================================================
///
/// TankMeleeCombo.CurrentStep 在一次攻擊完成後會回到 None，
/// 但 Combo Window 期間 NextComboStep 仍會保留下一段。
///
/// 因此 HUD 不能只顯示 CurrentStep：
///
/// Light1 完成、等待 Light2 輸入時
/// CurrentStep = None
/// NextComboStep = Light2
///
/// 正確 UI 應維持 1 / 3，而不是閃回 0 / 3。
/// </summary>
[DisallowMultipleComponent]
public class TankComboWeaponHUDSource :
    MonoBehaviour,
    IPlayerWeaponHUDSource
{
    // =====================================================================
    #region References

    [Header("Tank Combo HUD 資料來源")]

    [SerializeField]
    [Tooltip(
        "此 Tank Profession Runtime 使用的 TankMeleeCombo。\n\n" +
        "若留空，會從目前 Runtime Root 與子物件自動尋找。")]
    private TankMeleeCombo tankMeleeCombo;

    #endregion

    // =====================================================================
    #region Presentation Data

    [Header("Tank 武器 HUD 顯示")]

    [SerializeField]
    [Tooltip(
        "Tank 目前武器顯示在 HUD 上的 Sprite。\n\n" +
        "未來 Tank 更換武器圖片時，只需替換此 Runtime Prefab 的 Sprite。")]
    private Sprite weaponIcon;

    [SerializeField]
    [Min(1)]
    [Tooltip(
        "Tank HUD 顯示的最高 Combo。\n\n" +
        "目前 Light1、Light2、Heavy 共三段，因此設定為 3。\n\n" +
        "未來 Tank Combo 段數改變時，可以先調整此數值，" +
        "並同步更新 GetCurrentComboValue 的段數轉換。")]
    private int maximumCombo =
        3;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        ResolveTankMeleeCombo();
    }

    private void OnValidate()
    {
        maximumCombo =
            Mathf.Max(
                1,
                maximumCombo
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

        ResolveTankMeleeCombo();

        if (IsNetworkSourceReady(
                tankMeleeCombo
            ) == false)
        {
            return false;
        }

        int safeMaximumCombo =
            Mathf.Max(
                1,
                maximumCombo
            );

        int currentCombo =
            Mathf.Clamp(
                GetCurrentComboValue(),
                0,
                safeMaximumCombo
            );

        snapshot =
            new PlayerWeaponHUDSnapshot(
                weaponIcon,
                PlayerWeaponHUDValueMode.Combo,
                currentCombo,
                safeMaximumCombo,
                false,
                false,
                ShouldShowTankReloadReminder()
            );

        return true;
    }

    #endregion

    // =====================================================================
    #region Combo Value

    /// <summary>
    /// 取得玩家目前已推進到的 Combo 數值。
    ///
    /// 攻擊正在進行：
    /// 直接使用 CurrentStep。
    ///
    /// 攻擊間隔／Combo Window：
    /// 依照 NextComboStep 反推出已完成幾段。
    /// </summary>
    private int GetCurrentComboValue()
    {
        TankMeleeComboStep currentStep =
            tankMeleeCombo.CurrentStep;

        if (currentStep !=
            TankMeleeComboStep.None)
        {
            return (int)currentStep;
        }

        TankMeleeComboStep nextStep =
            tankMeleeCombo.NextComboStep;

        switch (nextStep)
        {
            case TankMeleeComboStep.Light2:
            {
                // Light1 已完成，正在等待第二段。
                return 1;
            }

            case TankMeleeComboStep.Heavy:
            {
                // Light1、Light2 已完成，正在等待第三段。
                return 2;
            }

            case TankMeleeComboStep.Light1:
            case TankMeleeComboStep.None:
            default:
            {
                return 0;
            }
        }
    }

    #endregion

    // =====================================================================
    #region Reserved Reload Reminder

    /// <summary>
    /// Tank 換彈提醒的正式保留位置。
    ///
    /// 目前 Tank 使用近戰 Combo，尚未定義：
    ///
    /// 耐久度不足
    /// 能量不足
    /// 武器過熱
    /// 特殊填裝
    ///
    /// 哪一種狀態應觸發換彈提醒。
    ///
    /// 所以本階段固定回傳 false，
    /// 但中央 HUD 與 Snapshot 已經完整保留接口。
    ///
    /// 未來規則確定後，只修改這個方法，
    /// 不需要修改 LocalPlayerWeaponHUD。
    /// </summary>
    private bool ShouldShowTankReloadReminder()
    {
        return false;
    }

    #endregion

    // =====================================================================
    #region Resolve / Safety

    private void ResolveTankMeleeCombo()
    {
        if (tankMeleeCombo != null)
        {
            return;
        }

        tankMeleeCombo =
            GetComponentInChildren<TankMeleeCombo>(
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