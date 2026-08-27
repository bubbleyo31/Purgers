using Fusion;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// 顯示本地玩家目前生命值的 HUD Slider。
///
/// ====================================================================
///
/// 這是一個純本地 Presentation 元件：
///
/// 1. 不需要 NetworkObject。
/// 2. 不修改 PlayerHealth。
/// 3. 不傳送 RPC。
/// 4. 只讀取本地 Player NetworkObject 的 Networked Health。
///
/// ====================================================================
///
/// 為什麼不訂閱 PlayerHealth.Died：
///
/// PlayerHealth.Died 是 State Authority Gameplay Event，
/// 遠端 Client 不保證直接收到相同 C# Event。
///
/// HUD 應直接讀取已同步的：
///
/// CurrentHealth
/// MaximumHealth
///
/// 並只在數值真正改變時刷新 Slider。
///
/// ====================================================================
///
/// 為什麼不長期保存第一次取得的 PlayerHealth：
///
/// 玩家死亡後舊 Player NetworkObject 會被 Despawn，
/// 三秒後會生成全新的 PlayerHealth 實例。
///
/// 因此 HUD 必須透過 Runner PlayerObject：
///
/// 自動解除舊 Health
/// 自動綁定新 Health。
/// </summary>
[DisallowMultipleComponent]
public class LocalPlayerHealthSlider :
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
    #region Slider

    [Header("血量 Slider")]

    [SerializeField]
    [Tooltip(
        "顯示本地玩家 CurrentHealth 的 Unity UI Slider。\n\n" +
        "Runtime 會自動設定：\n" +
        "Min Value = 0\n" +
        "Max Value = PlayerHealth.MaximumHealth\n" +
        "Value = PlayerHealth.CurrentHealth\n\n" +
        "請將 Slider 的 Interactable 關閉並移除 Handle，避免玩家誤以為可以拖曳血量。")]
    private Slider healthSlider;

    [SerializeField]
    [Tooltip(
        "血量長條的視覺 Root。死亡等待重生、尚未生成本地玩家或離開 Session 時會隱藏。\n\n" +
        "這個物件可以是 HealthBar 子物件，但不可填入掛有 LocalPlayerHealthSlider 的同一個物件，" +
        "否則 SetActive(false) 會連控制器本身一起停用，導致重生後無法自動顯示。\n\n" +
        "若留空，會改用 Health Slider 的 GameObject。")]
    private GameObject healthVisualRoot;

    [SerializeField]
    [Tooltip(
        "開啟後，本地 PlayerObject 不存在時隱藏血量長條。\n\n" +
        "目前玩家死亡後會 Despawn 並等待三秒重生，建議保持開啟。")]
    private bool hideWhilePlayerMissing =
        true;

    #endregion

    // =====================================================================
    #region Change Detection

    [Header("數值變動判斷")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "目前血量與上次已顯示血量相差多少以上，才視為需要刷新 Slider。\n\n" +
        "用途是忽略極小的浮點誤差，不是限制真正傷害或治療。" +
        "一般 HP 建議使用 0.001。")]
    private float healthChangeEpsilon =
        0.001f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip(
        "開啟後顯示 HUD 綁定新 PlayerHealth、解除舊 PlayerHealth 與血量刷新資訊。" +
        "確認死亡重生綁定正常後可以關閉。")]
    private bool debugHealthUI =
        false;

    #endregion

    // =====================================================================
    #region Runtime State

    /// <summary>
    /// 目前 HUD 正在讀取的本地 PlayerHealth。
    ///
    /// 玩家死亡 Despawn 後會清除；
    /// 重生後會換成全新的 PlayerHealth 實例。
    /// </summary>
    private PlayerHealth boundHealth;

    /// <summary>
    /// 上一次真正寫入 Slider 的 Current Health。
    ///
    /// NaN 代表下一次必須強制刷新。
    /// </summary>
    private float lastDisplayedHealth =
        float.NaN;

    /// <summary>
    /// 上一次真正寫入 Slider 的 Maximum Health。
    ///
    /// 未來職業、卡牌或 Buff 改變最大血量時，
    /// Slider Max Value 也能只在需要時重新設定。
    /// </summary>
    private float lastDisplayedMaximumHealth =
        float.NaN;

    /// <summary>
    /// 避免每幀重複呼叫 SetActive。
    /// </summary>
    private bool visualVisible;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        if (healthVisualRoot == null &&
            healthSlider != null)
        {
            healthVisualRoot =
                healthSlider.gameObject;
        }

        /*
         * 防止使用者把 Controller 自己指定成 Visual Root。
         *
         * 如果直接關閉本物件，Update 不會再執行，
         * 重生時也無法重新綁定新 PlayerHealth。
         */
        if (healthVisualRoot == gameObject)
        {
            Debug.LogError(
                "[Health UI] Health Visual Root 不可是掛有 LocalPlayerHealthSlider 的同一個 GameObject。" +
                "請建立一個 HealthBar 子物件並指定該子物件。",
                this
            );

            healthVisualRoot =
                null;
        }

        if (healthSlider == null)
        {
            Debug.LogError(
                "[Health UI] 尚未指定 Health Slider。",
                this
            );
        }
        else
        {
            /*
             * Health Slider 不是玩家可操作的 UI。
             */
            healthSlider.interactable =
                false;

            healthSlider.wholeNumbers =
                false;

            healthSlider.minValue =
                0f;
        }

        TryResolveRunner();

        SetVisualVisible(
            hideWhilePlayerMissing == false
        );
    }

    private void Update()
    {
        if (healthSlider == null)
        {
            return;
        }

        if (TryResolveRunner() == false ||
            TryResolveLocalPlayerHealth(
                out PlayerHealth currentHealth
            ) == false)
        {
            UnbindCurrentHealth();
            return;
        }

        if (boundHealth !=
            currentHealth)
        {
            BindHealth(
                currentHealth
            );
        }

        RefreshSliderIfChanged();
    }

    private void OnDisable()
    {
        boundHealth =
            null;

        ResetDisplayedValueCache();
    }

    #endregion

    // =====================================================================
    #region Runner / Player Health Resolve

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

    private bool TryResolveLocalPlayerHealth(
        out PlayerHealth playerHealth
    )
    {
        playerHealth =
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

        playerHealth =
            playerObject.GetComponent<PlayerHealth>();

        return
            playerHealth != null;
    }

    #endregion

    // =====================================================================
    #region Bind / Unbind

    private void BindHealth(
        PlayerHealth newHealth
    )
    {
        boundHealth =
            newHealth;

        ResetDisplayedValueCache();

        SetVisualVisible(
            true
        );

        if (debugHealthUI)
        {
            Debug.Log(
                $"[Health UI] 已綁定本地 PlayerHealth。" +
                $"\nPlayer：{boundHealth.Object.InputAuthority}" +
                $"\nHealth Object：{boundHealth.name}",
                this
            );
        }
    }

    private void UnbindCurrentHealth()
    {
        if (boundHealth != null &&
            debugHealthUI)
        {
            Debug.Log(
                "[Health UI] 本地 PlayerObject 不存在，解除舊 PlayerHealth。",
                this
            );
        }

        boundHealth =
            null;

        ResetDisplayedValueCache();

        if (hideWhilePlayerMissing)
        {
            SetVisualVisible(
                false
            );
        }
        else
        {
            /*
             * 選擇不隱藏時，顯示空血量作為等待重生狀態。
             */
            healthSlider.minValue =
                0f;

            healthSlider.maxValue =
                1f;

            healthSlider.SetValueWithoutNotify(
                0f
            );

            SetVisualVisible(
                true
            );
        }
    }

    private void ResetDisplayedValueCache()
    {
        lastDisplayedHealth =
            float.NaN;

        lastDisplayedMaximumHealth =
            float.NaN;
    }

    #endregion

    // =====================================================================
    #region Slider Refresh

    /// <summary>
    /// 只有 CurrentHealth 或 MaximumHealth 真正改變時，
    /// 才寫入 Slider。
    ///
    /// Update 每幀只做便宜的數值比較，
    /// 不會在沒有變化時重複觸發 UI Layout 或 Slider Callback。
    /// </summary>
    private void RefreshSliderIfChanged()
    {
        if (boundHealth == null ||
            healthSlider == null)
        {
            return;
        }

        float maximumHealth =
            Mathf.Max(
                1f,
                boundHealth.MaximumHealth
            );

        float currentHealth =
            Mathf.Clamp(
                boundHealth.CurrentHealth,
                0f,
                maximumHealth
            );

        float epsilon =
            Mathf.Max(
                0f,
                healthChangeEpsilon
            );

        bool maximumHealthChanged =
            float.IsNaN(
                lastDisplayedMaximumHealth
            ) ||
            Mathf.Abs(
                maximumHealth -
                lastDisplayedMaximumHealth
            ) >
            epsilon;

        bool currentHealthChanged =
            float.IsNaN(
                lastDisplayedHealth
            ) ||
            Mathf.Abs(
                currentHealth -
                lastDisplayedHealth
            ) >
            epsilon;

        if (maximumHealthChanged == false &&
            currentHealthChanged == false)
        {
            return;
        }

        if (maximumHealthChanged)
        {
            healthSlider.minValue =
                0f;

            healthSlider.maxValue =
                maximumHealth;
        }

        /*
         * 使用 SetValueWithoutNotify，
         * 避免 HUD 顯示更新誤觸 Slider.onValueChanged Gameplay 邏輯。
         *
         * UI 永遠只能讀取 PlayerHealth，不能反向修改血量。
         */
        healthSlider.SetValueWithoutNotify(
            currentHealth
        );

        lastDisplayedHealth =
            currentHealth;

        lastDisplayedMaximumHealth =
            maximumHealth;

        if (debugHealthUI)
        {
            Debug.Log(
                $"[Health UI] 血量 Slider 已刷新。" +
                $"\nHealth：{currentHealth:F2} / {maximumHealth:F2}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Visual

    private void SetVisualVisible(
        bool visible
    )
    {
        /*
         * 不能只依賴 visualVisible 快取判斷是否 return。
         *
         * 因為場景一開始 HealthBar 可能仍是 Active，
         * 但 visualVisible 的 C# 預設值卻是 false。
         * 如果只比較快取，第一次要求隱藏時會直接 return，
         * 導致血量條沒有真的被關閉。
         */
        if (visualVisible == visible &&
            (healthVisualRoot == null ||
             healthVisualRoot.activeSelf == visible))
        {
            return;
        }

        if (healthVisualRoot != null &&
            healthVisualRoot.activeSelf !=
                visible)
        {
            healthVisualRoot.SetActive(
                visible
            );
        }

        visualVisible =
            visible;
    }

    #endregion
}