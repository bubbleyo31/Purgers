using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// 顯示本地玩家目前職業 Runtime 所提供的武器 HUD。
///
/// ====================================================================
///
/// 本腳本不認識：
///
/// AttackRifle
/// SupportSMG
/// TankMeleeCombo
/// PlayerProfessionType。
///
/// 它只尋找目前 Profession Runtime 中的：
///
/// IPlayerWeaponHUDSource
///
/// 因此新增職業時不需要修改本腳本。
///
/// ====================================================================
///
/// 更新原則：
///
/// 每個畫面 Frame 取得 Snapshot。
///
/// 圖片沒變
/// 數值沒變
/// 無限狀態沒變
/// 換彈提醒沒變
/// → 不重寫 Image、TMP 或 SetActive。
///
/// 只有資料真正變動時才更新對應 UI。
///
/// ====================================================================
///
/// Networked Property 安全：
///
/// 1. 先確認本地 Player NetworkObject 有效。
/// 2. 再確認 PlayerProfessionRuntimeManager 已 Spawned。
/// 3. 再取得有效的 CurrentRuntimeObject。
/// 4. 最後由各 Source 確認武器 NetworkBehaviour 已 Spawned。
///
/// 職業切換的 Runtime Despawn / Spawn 交界不會直接讀取舊武器。
/// </summary>
[DisallowMultipleComponent]
public class LocalPlayerWeaponHUD :
    MonoBehaviour
{
    // =====================================================================
    #region Runner

    [Header("Fusion Runner")]

    [SerializeField]
    [Tooltip(
        "目前 Gameplay Session 使用的 NetworkRunner。\n\n" +
        "如果 Runner 是執行期間建立，可以留空；" +
        "HUD 會自動尋找目前正在執行的 NetworkRunner。")]
    private NetworkRunner runner;

    #endregion

    // =====================================================================
    #region UI References

    [Header("武器 HUD 物件")]

    [SerializeField]
    [Tooltip(
        "整組武器 HUD 的視覺 Root，內含武器圖片、數值 TMP 與換彈提醒。\n\n" +
        "職業 Runtime 尚未準備完成、玩家死亡或正在重生時可隱藏。\n\n" +
        "不可指定掛有 LocalPlayerWeaponHUD 的同一個 GameObject，" +
        "否則隱藏視覺時會連控制腳本一起停止。")]
    private GameObject weaponHUDVisualRoot;

    [SerializeField]
    [Tooltip(
        "顯示目前職業武器 Sprite 的 Unity UI Image。\n\n" +
        "圖片由目前 Runtime 的 IPlayerWeaponHUDSource 提供。")]
    private Image weaponIconImage;

    [SerializeField]
    [Tooltip(
        "顯示目前數值的 TextMeshPro 文字。\n\n" +
        "槍械：目前彈匣 / 備用彈藥。\n" +
        "Tank：目前 Combo / 最高 Combo。\n" +
        "無限數值會改用 Infinity Symbol。")]
    private TMP_Text weaponValueText;

    [SerializeField]
    [Tooltip(
        "換彈提醒的整組 UI Root。\n\n" +
        "可以包含 TMP、Image、Animator 或其他純視覺子物件，本腳本只控制整組顯示與隱藏。\n\n" +
        "Attack / Support 在目前彈匣低於 5 時顯示。\n" +
        "Tank 已保留接口，但本階段不會觸發。\n\n" +
        "不可指定 PlayerHUDController 或 Weapon HUD Visual Root 本身。")]
    private GameObject reloadReminderRoot;

    [SerializeField]
    [Tooltip(
        "開啟後，本地玩家不存在、死亡等待重生、職業 Runtime 切換中，" +
        "或目前 Runtime 沒有合法 HUD Source 時隱藏整組武器 HUD。\n\n" +
        "建議保持開啟。")]
    private bool hideWhileSourceMissing =
        true;

    #endregion

    // =====================================================================
    #region Formatting

    [Header("文字格式")]

    [SerializeField]
    [Tooltip(
        "數值為無限時顯示的符號。\n\n" +
        "預設使用 ∞。若目前 TMP Font Asset 沒有此字元，" +
        "請替換字型、加入 Fallback Font Asset，或暫時改成 INF。")]
    private string infinitySymbol =
        "∞";

    [SerializeField]
    [Tooltip(
        "左右數值中間的分隔文字。\n\n" +
        "預設為空格、斜線、空格：  /  ")]
    private string valueSeparator =
        " / ";

    // 加入這個新的序列化欄位
    [SerializeField]
    [Tooltip(
        "數值顯示的格式字串。\n\n" +
        "預設為 \"00\"，代表當數值為個位數時會自動在前面補零 (例如：09, 08, 00)。\n" +
        "若不需要補零，請將其改為 \"0\"。")]
    private string numberFormat = 
        "00";

    #endregion

    // =====================================================================
    #region Runtime Binding

    /// <summary>
    /// 目前 HUD 綁定的職業 Runtime NetworkObject。
    /// 職業切換時會換成全新物件。
    /// </summary>
    private NetworkObject boundRuntimeObject;

    /// <summary>
    /// 目前 Runtime 中提供武器 HUD 資料的介面。
    /// </summary>
    private IPlayerWeaponHUDSource boundSource;

    /// <summary>
    /// IPlayerWeaponHUDSource 對應的 Unity MonoBehaviour。
    ///
    /// Interface 本身不支援 Unity Destroy Null 判斷，
    /// 所以必須額外保存 MonoBehaviour 參考。
    /// </summary>
    private MonoBehaviour boundSourceBehaviour;

    /// <summary>
    /// 避免同一個配置錯誤的 Runtime 每個 Frame 重複輸出 Error。
    /// </summary>
    private bool missingSourceWarningSent;

    #endregion

    // =====================================================================
    #region Display Cache

    private bool hasDisplayedSnapshot;

    private Sprite lastWeaponIcon;

    private PlayerWeaponHUDValueMode
        lastValueMode;

    private int lastCurrentValue;

    private int lastSecondaryValue;

    private bool lastCurrentInfinite;

    private bool lastSecondaryInfinite;

    private bool lastReloadReminderVisible;

    private bool visualVisible;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        ValidateUIReferences();

        if (weaponIconImage != null)
        {
            weaponIconImage.raycastTarget =
                false;

            weaponIconImage.preserveAspect =
                true;
        }

        if (weaponValueText != null)
        {
            weaponValueText.raycastTarget =
                false;
        }

        TryResolveRunner();

        ClearDisplayedUI();

        SetWeaponHUDVisible(
            hideWhileSourceMissing == false
        );
    }

    private void Update()
    {
        if (weaponIconImage == null ||
            weaponValueText == null)
        {
            return;
        }

        if (TryResolveRunner() == false ||
            TryResolveLocalRuntime(
                out NetworkObject currentRuntimeObject
            ) == false)
        {
            UnbindCurrentRuntime();
            return;
        }

        bool runtimeChanged =
            boundRuntimeObject !=
            currentRuntimeObject;

        /*
         * 每個 Runtime 只搜尋一次 Source。
         *
         * 如果 Prefab 忘記掛 Source，不能因為 boundSourceBehaviour 為 null
         * 就在每個 Frame 重新搜尋並重複輸出 Error。
         *
         * Runtime 內的 HUD Source 是 Prefab 固定配置，
         * 不應在同一個 Runtime 存活期間動態新增或移除。
         */
        if (runtimeChanged)
        {
            BindRuntime(
                currentRuntimeObject
            );
        }

        if (boundSource == null ||
            boundSourceBehaviour == null ||
            boundSource.TryGetWeaponHUDSnapshot(
                out PlayerWeaponHUDSnapshot snapshot
            ) == false)
        {
            HideForMissingSource();
            return;
        }

        RefreshUIIfChanged(
            snapshot
        );
    }

    private void OnDisable()
    {
        boundRuntimeObject =
            null;

        boundSource =
            null;

        boundSourceBehaviour =
            null;

        missingSourceWarningSent =
            false;

        /*
         * HUD Controller 被停用時清除 Presentation 快取與內容。
         * 下一次重新啟用後，會從當時的正式 Runtime 重新刷新。
         */
        ClearDisplayedUI();

        SetWeaponHUDVisible(
            false
        );
    }

    #endregion

    // =====================================================================
    #region Validation

    private void ValidateUIReferences()
    {
        if (weaponHUDVisualRoot == null)
        {
            Debug.LogError(
                "[Weapon HUD] 尚未指定 Weapon HUD Visual Root。",
                this
            );
        }
        else if (weaponHUDVisualRoot ==
                 gameObject)
        {
            Debug.LogError(
                "[Weapon HUD] Weapon HUD Visual Root 不可是掛有 LocalPlayerWeaponHUD 的同一個 GameObject。" +
                "請指定 PlayerHUDController 底下的 WeaponHUDRoot 子物件。",
                this
            );

            weaponHUDVisualRoot =
                null;
        }

        if (weaponIconImage == null)
        {
            Debug.LogError(
                "[Weapon HUD] 尚未指定 Weapon Icon Image。",
                this
            );
        }

        if (weaponValueText == null)
        {
            Debug.LogError(
                "[Weapon HUD] 尚未指定 Weapon Value Text。",
                this
            );
        }

        if (reloadReminderRoot ==
            gameObject)
        {
            Debug.LogError(
                "[Weapon HUD] Reload Reminder Root 不可是掛有 LocalPlayerWeaponHUD 的同一個 GameObject。",
                this
            );

            reloadReminderRoot =
                null;
        }

        if (reloadReminderRoot != null &&
            reloadReminderRoot ==
                weaponHUDVisualRoot)
        {
            Debug.LogError(
                "[Weapon HUD] Reload Reminder Root 不可等於整個 Weapon HUD Visual Root。" +
                "請建立獨立的 ReloadReminder 子物件。",
                this
            );

            reloadReminderRoot =
                null;
        }
    }

    #endregion

    // =====================================================================
    #region Runner / Runtime Resolve

    private bool TryResolveRunner()
    {
        if (runner != null &&
            runner.IsRunning)
        {
            return true;
        }

        runner =
            FindFirstObjectByType<NetworkRunner>();

        return
            runner != null &&
            runner.IsRunning;
    }

    /// <summary>
    /// 安全取得本地玩家目前正式的 Profession Runtime。
    ///
    /// 只有確認 Runtime Manager 的 NetworkObject 已 Spawned 後，
    /// 才讀取 Networked CurrentRuntimeObject。
    /// </summary>
    private bool TryResolveLocalRuntime(
        out NetworkObject runtimeObject
    )
    {
        runtimeObject =
            null;

        if (runner == null ||
            runner.IsRunning == false ||
            runner.LocalPlayer.IsRealPlayer == false)
        {
            return false;
        }

        if (runner.TryGetPlayerObject(
                runner.LocalPlayer,
                out NetworkObject playerObject
            ) == false ||
            playerObject == null ||
            playerObject.IsValid == false)
        {
            return false;
        }

        PlayerProfessionRuntimeManager runtimeManager =
            playerObject
                .GetComponent<
                    PlayerProfessionRuntimeManager
                >();

        if (runtimeManager == null ||
            runtimeManager.Object == null ||
            runtimeManager.Object.IsValid == false ||
            runtimeManager.Runner == null ||
            runtimeManager.Runner.IsRunning == false)
        {
            return false;
        }

        runtimeObject =
            runtimeManager.CurrentRuntimeObject;

        return
            runtimeObject != null &&
            runtimeObject.IsValid;
    }

    #endregion

    // =====================================================================
    #region Source Binding

    private void BindRuntime(
        NetworkObject runtimeObject
    )
    {
        boundRuntimeObject =
            runtimeObject;

        boundSource =
            null;

        boundSourceBehaviour =
            null;

        missingSourceWarningSent =
            false;

        /*
         * 職業切換時立即清除上一個職業的圖片與數值，
         * 避免新 Runtime 尚未準備完成時短暫殘留舊武器 UI。
         *
         * BindRuntime 只會在 Runtime 真正改變時執行一次。
         */
        ClearDisplayedUI();

        if (boundRuntimeObject == null ||
            boundRuntimeObject.IsValid == false)
        {
            HideForMissingSource();
            return;
        }

        MonoBehaviour[] behaviours =
            boundRuntimeObject
                .GetComponentsInChildren<MonoBehaviour>(
                    true
                );

        int sourceCount =
            0;

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            IPlayerWeaponHUDSource source =
                behaviour as
                    IPlayerWeaponHUDSource;

            if (source == null)
            {
                continue;
            }

            sourceCount++;

            if (boundSource == null)
            {
                boundSource =
                    source;

                boundSourceBehaviour =
                    behaviour;
            }
        }

        if (sourceCount == 0)
        {
            WarnMissingSourceOnce();
            HideForMissingSource();
            return;
        }

        if (sourceCount > 1)
        {
            Debug.LogError(
                $"[Weapon HUD] Runtime 上找到 {sourceCount} 個 IPlayerWeaponHUDSource。" +
                "每一個 Profession Runtime 只能有一個正式武器 HUD Source；" +
                "目前暫時使用找到的第一個。" +
                $"\nRuntime：{boundRuntimeObject.name}",
                boundRuntimeObject
            );
        }
    }

    private void UnbindCurrentRuntime()
    {
        boundRuntimeObject =
            null;

        boundSource =
            null;

        boundSourceBehaviour =
            null;

        missingSourceWarningSent =
            false;

        HideForMissingSource();
    }

    private void WarnMissingSourceOnce()
    {
        if (missingSourceWarningSent ||
            boundRuntimeObject == null)
        {
            return;
        }

        missingSourceWarningSent =
            true;

        Debug.LogError(
            "[Weapon HUD] 目前 Profession Runtime 沒有 IPlayerWeaponHUDSource。" +
            "請依職業掛上 AttackRifleWeaponHUDSource、SupportSMGWeaponHUDSource、" +
            "TankComboWeaponHUDSource 或未來的新實作。" +
            $"\nRuntime：{boundRuntimeObject.name}",
            boundRuntimeObject
        );
    }

    #endregion

    // =====================================================================
    #region UI Refresh

    /// <summary>
    /// 只有 Snapshot 對應資料真正改變時，
    /// 才更新 Image、TMP 或 Reload Reminder。
    /// </summary>
    private void RefreshUIIfChanged(
        PlayerWeaponHUDSnapshot snapshot
    )
    {
        SetWeaponHUDVisible(
            true
        );

        bool iconChanged =
            hasDisplayedSnapshot == false ||
            lastWeaponIcon !=
                snapshot.WeaponIcon;

        if (iconChanged)
        {
            weaponIconImage.sprite =
                snapshot.WeaponIcon;

            weaponIconImage.enabled =
                snapshot.WeaponIcon != null;
        }

        bool valuesChanged =
            hasDisplayedSnapshot == false ||
            lastValueMode !=
                snapshot.ValueMode ||
            lastCurrentValue !=
                snapshot.CurrentValue ||
            lastSecondaryValue !=
                snapshot.SecondaryValue ||
            lastCurrentInfinite !=
                snapshot.IsCurrentValueInfinite ||
            lastSecondaryInfinite !=
                snapshot.IsSecondaryValueInfinite;

        if (valuesChanged)
        {
            string currentText =
                FormatValue(
                    snapshot.CurrentValue,
                    snapshot.IsCurrentValueInfinite
                );

            string secondaryText =
                FormatValue(
                    snapshot.SecondaryValue,
                    snapshot.IsSecondaryValueInfinite
                );

            string separator =
                string.IsNullOrEmpty(
                    valueSeparator
                )
                    ? " / "
                    : valueSeparator;

            weaponValueText.SetText(
                currentText +
                separator +
                secondaryText
            );
        }

        bool reloadReminderChanged =
            hasDisplayedSnapshot == false ||
            lastReloadReminderVisible !=
                snapshot.ShowReloadReminder;

        if (reloadReminderChanged)
        {
            SetReloadReminderVisible(
                snapshot.ShowReloadReminder
            );
        }

        lastWeaponIcon =
            snapshot.WeaponIcon;

        lastValueMode =
            snapshot.ValueMode;

        lastCurrentValue =
            snapshot.CurrentValue;

        lastSecondaryValue =
            snapshot.SecondaryValue;

        lastCurrentInfinite =
            snapshot.IsCurrentValueInfinite;

        lastSecondaryInfinite =
            snapshot.IsSecondaryValueInfinite;

        lastReloadReminderVisible =
            snapshot.ShowReloadReminder;

        hasDisplayedSnapshot =
            true;
    }

    /// <summary>
    /// 將傳入的整數數值格式化為顯示用的字串。
    /// 若設定為無限，則回傳無限符號；
    /// 否則將數值限制在 0 以上，並依照 numberFormat (預設 "00") 進行補零格式化。
    /// </summary>
    /// <param name="value">當前要顯示的數值 (例如目前彈藥量或備用彈藥量)</param>
    /// <param name="infinite">該數值是否處於無限狀態</param>
    /// <returns>格式化完成後的字串 (例如 "09", "12", "∞")</returns>
    private string FormatValue(
        int value,
        bool infinite
    )
    {
        if (infinite)
        {
            return string.IsNullOrEmpty(
                infinitySymbol
            )
                ? "∞"
                : infinitySymbol;
        }

        // Mathf.Max(0, value) 確保不會因為資料異常而顯示負數彈藥。
        // ToString(numberFormat) 會根據設定的格式輸出，傳入 "00" 就會強制保持兩位數。
        return Mathf.Max(
                0,
                value
            )
            .ToString(numberFormat);
    }

    #endregion

    // =====================================================================
    #region Missing Source / Cache

    private void HideForMissingSource()
    {
        /*
         * 只有上一個 Snapshot 曾經真正顯示過時才清空內容。
         *
         * Source 在多個 Frame 都尚未準備完成時，
         * 不應每幀反覆把相同的空字串與 null Sprite 寫入 UI。
         */
        if (hasDisplayedSnapshot)
        {
            ClearDisplayedUI();
        }
        else
        {
            SetReloadReminderVisible(
                false
            );
        }

        if (hideWhileSourceMissing)
        {
            SetWeaponHUDVisible(
                false
            );
        }
        else
        {
            SetWeaponHUDVisible(
                true
            );
        }
    }

    private void ClearDisplayedUI()
    {
        if (weaponIconImage != null)
        {
            weaponIconImage.sprite =
                null;

            weaponIconImage.enabled =
                false;
        }

        if (weaponValueText != null)
        {
            weaponValueText.SetText(
                string.Empty
            );
        }

        SetReloadReminderVisible(
            false
        );

        ResetDisplayCache();
    }

    private void ResetDisplayCache()
    {
        hasDisplayedSnapshot =
            false;

        lastWeaponIcon =
            null;

        lastValueMode =
            default;

        lastCurrentValue =
            0;

        lastSecondaryValue =
            0;

        lastCurrentInfinite =
            false;

        lastSecondaryInfinite =
            false;

        lastReloadReminderVisible =
            false;
    }

    #endregion

    // =====================================================================
    #region Visual

    private void SetWeaponHUDVisible(
        bool visible
    )
    {
        if (visualVisible == visible &&
            (weaponHUDVisualRoot == null ||
             weaponHUDVisualRoot.activeSelf == visible))
        {
            return;
        }

        if (weaponHUDVisualRoot != null &&
            weaponHUDVisualRoot.activeSelf !=
                visible)
        {
            weaponHUDVisualRoot.SetActive(
                visible
            );
        }

        visualVisible =
            visible;
    }

    private void SetReloadReminderVisible(
        bool visible
    )
    {
        if (reloadReminderRoot == null ||
            reloadReminderRoot.activeSelf ==
                visible)
        {
            return;
        }

        reloadReminderRoot.SetActive(
            visible
        );
    }

    #endregion
}