using Fusion;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// 顯示本地玩家目前世界速度的 HUD Slider。
///
/// ====================================================================
///
/// 這是一個純本地 Presentation 元件：
///
/// 1. 不需要 NetworkObject。
/// 2. 不修改 Player 或 PlayerMovement。
/// 3. 不傳送 RPC。
/// 4. 不限制玩家真正的移動速度。
/// 5. 只負責把 Player.CurrentWorldSpeed 顯示到 Slider。
///
/// ====================================================================
///
/// CurrentWorldSpeed 目前來自：
///
/// KCC.Data.RealVelocity.magnitude
///
/// 因此會同時計算水平與垂直方向的世界速度。
/// 玩家跳躍、下墜、鈎索拉動或被其他力量推動時，
/// 都會反映在這個速度表上。
///
/// ====================================================================
///
/// Maximum Displayed Speed 只控制 UI 刻度：
///
/// 真實速度超過最高值時，Slider 顯示會停在最右端，
/// 但不會反向修改 KCC 或玩家速度。
///
/// ====================================================================
///
/// 玩家死亡後舊 Player NetworkObject 會被 Despawn，
/// 三秒後會產生全新的 Player 實例。
///
/// 因此 HUD 不可永遠保存第一次取得的 Player，
/// 必須持續透過 Runner PlayerObject 檢查：
///
/// 舊 Player 消失 → 自動解除綁定。
/// 新 Player 生成 → 自動重新綁定。
/// </summary>
[DisallowMultipleComponent]
public class LocalPlayerSpeedSlider :
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

    [Header("速度 Slider")]

    [SerializeField]
    [Tooltip(
        "顯示本地玩家 CurrentWorldSpeed 的 Unity UI Slider。\n\n" +
        "Runtime 會自動設定：\n" +
        "Min Value = 0\n" +
        "Max Value = Maximum Displayed Speed\n" +
        "Value = 經過封頂後的 CurrentWorldSpeed\n\n" +
        "請將 Slider 的 Interactable 關閉並移除 Handle，" +
        "避免玩家誤以為速度表可以拖曳。")]
    private Slider speedSlider;

    [SerializeField]
    [Tooltip(
        "速度表的視覺 Root。死亡等待重生、尚未生成本地玩家或離開 Session 時會隱藏。\n\n" +
        "這個物件可以是 SpeedBar 子物件，但不可填入掛有 LocalPlayerSpeedSlider 的同一個物件，" +
        "否則 SetActive(false) 會連控制器本身一起停用，導致重生後無法自動顯示。\n\n" +
        "若留空，會改用 Speed Slider 的 GameObject。")]
    private GameObject speedVisualRoot;

    [SerializeField]
    [Tooltip(
        "開啟後，本地 PlayerObject 不存在時隱藏速度表。\n\n" +
        "目前玩家死亡後會 Despawn 並等待三秒重生，建議保持開啟。")]
    private bool hideWhilePlayerMissing =
        true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "速度表能顯示的最高速度，單位為 Unity 世界單位／秒。\n\n" +
        "例如設定為 100：\n" +
        "玩家速度到達或超過 100 時，Slider 會封頂在最右端。\n\n" +
        "這個數值只影響 UI，不會限制玩家、KCC 或鈎索的真實速度。")]
    private float maximumDisplayedSpeed =
        100f;

    #endregion

    // =====================================================================
    #region Change Detection

    [Header("數值變動判斷")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "封頂後的顯示速度與上次數值相差多少以上，才刷新 Slider。\n\n" +
        "KCC 速度通常每個畫面都會出現極小浮點變動；" +
        "此值可避免無意義的 UI 重寫。\n\n" +
        "建議先使用 0.01。若希望速度表反應更細，可以調小或設為 0。")]
    private float speedChangeEpsilon =
        0.01f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip(
        "開啟後顯示速度 HUD 綁定與解除本地 Player 的資訊。\n\n" +
        "不會在每次速度改變時輸出 Log，避免移動期間洗滿 Console。")]
    private bool debugSpeedUI =
        false;

    #endregion

    // =====================================================================
    #region Runtime State

    /// <summary>
    /// 目前速度表正在讀取的本地 Player。
    ///
    /// 玩家死亡 Despawn 後會清除；
    /// 重生後會換成全新的 Player 實例。
    /// </summary>
    private Player boundPlayer;

    /// <summary>
    /// 上一次真正寫入 Slider 的封頂後速度。
    ///
    /// NaN 代表下一次必須強制刷新。
    /// </summary>
    private float lastDisplayedSpeed =
        float.NaN;

    /// <summary>
    /// 上一次套用到 Slider.maxValue 的最高顯示速度。
    ///
    /// 讓 Runtime 在 Inspector 改值時也可以正確更新刻度。
    /// </summary>
    private float lastAppliedMaximumSpeed =
        float.NaN;

    /// <summary>
    /// 避免重複呼叫 SetActive；
    /// 同時仍會核對 GameObject 真正的 activeSelf 狀態。
    /// </summary>
    private bool visualVisible;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        if (speedVisualRoot == null &&
            speedSlider != null)
        {
            speedVisualRoot =
                speedSlider.gameObject;
        }

        /*
         * Controller 自己不可成為 Visual Root。
         *
         * 如果直接關閉掛有本腳本的 GameObject，
         * Update 不會再執行，重生後也無法重新綁定玩家。
         */
        if (speedVisualRoot == gameObject)
        {
            Debug.LogError(
                "[Speed UI] Speed Visual Root 不可是掛有 LocalPlayerSpeedSlider 的同一個 GameObject。" +
                "請建立一個 SpeedBar 子物件並指定該子物件。",
                this
            );

            speedVisualRoot =
                null;
        }

        if (speedSlider == null)
        {
            Debug.LogError(
                "[Speed UI] 尚未指定 Speed Slider。",
                this
            );
        }
        else
        {
            /*
             * Speed Slider 只負責顯示，玩家不可操作。
             */
            speedSlider.interactable =
                false;

            speedSlider.wholeNumbers =
                false;

            speedSlider.minValue =
                0f;

            speedSlider.maxValue =
                GetSafeMaximumDisplayedSpeed();

            speedSlider.SetValueWithoutNotify(
                0f
            );
        }

        TryResolveRunner();

        SetVisualVisible(
            hideWhilePlayerMissing == false
        );
    }

    private void Update()
    {
        if (speedSlider == null)
        {
            return;
        }

        if (TryResolveRunner() == false ||
            TryResolveLocalPlayer(
                out Player currentPlayer
            ) == false)
        {
            UnbindCurrentPlayer();
            return;
        }

        if (boundPlayer !=
            currentPlayer)
        {
            BindPlayer(
                currentPlayer
            );
        }

        RefreshSliderIfChanged();
    }

    private void OnDisable()
    {
        boundPlayer =
            null;

        ResetDisplayedValueCache();
    }

    private void OnValidate()
    {
        maximumDisplayedSpeed =
            Mathf.Max(
                0.01f,
                maximumDisplayedSpeed
            );

        speedChangeEpsilon =
            Mathf.Max(
                0f,
                speedChangeEpsilon
            );
    }

    #endregion

    // =====================================================================
    #region Runner / Player Resolve

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

    private bool TryResolveLocalPlayer(
        out Player player
    )
    {
        player =
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

        player =
            playerObject.GetComponent<Player>();

        return
            player != null;
    }

    #endregion

    // =====================================================================
    #region Bind / Unbind

    private void BindPlayer(
        Player newPlayer
    )
    {
        boundPlayer =
            newPlayer;

        ResetDisplayedValueCache();

        SetVisualVisible(
            true
        );

        if (debugSpeedUI)
        {
            Debug.Log(
                "[Speed UI] 已綁定本地 Player。" +
                $"\nPlayer Object：{boundPlayer.name}",
                this
            );
        }
    }

    private void UnbindCurrentPlayer()
    {
        if (boundPlayer != null &&
            debugSpeedUI)
        {
            Debug.Log(
                "[Speed UI] 本地 PlayerObject 不存在，解除舊 Player。",
                this
            );
        }

        boundPlayer =
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
            speedSlider.minValue =
                0f;

            speedSlider.maxValue =
                GetSafeMaximumDisplayedSpeed();

            speedSlider.SetValueWithoutNotify(
                0f
            );

            SetVisualVisible(
                true
            );
        }
    }

    private void ResetDisplayedValueCache()
    {
        lastDisplayedSpeed =
            float.NaN;

        lastAppliedMaximumSpeed =
            float.NaN;
    }

    #endregion

    // =====================================================================
    #region Slider Refresh

    /// <summary>
    /// 只有封頂後的顯示速度或最高顯示速度真正改變時，
    /// 才寫入 Slider。
    ///
    /// 例如玩家真實速度已超過 100：
    ///
    /// 120 → 140 → 160
    ///
    /// UI 顯示值一直是 100，因此不需要反覆重寫 Slider。
    /// </summary>
    private void RefreshSliderIfChanged()
    {
        if (boundPlayer == null ||
            speedSlider == null)
        {
            return;
        }

        float safeMaximumSpeed =
            GetSafeMaximumDisplayedSpeed();

        float currentWorldSpeed =
            boundPlayer.CurrentWorldSpeed;

        /*
         * 正常情況不應出現 NaN 或 Infinity。
         * 這裡仍做 Presentation 防護，避免異常物理值污染 UI。
         */
        if (float.IsNaN(currentWorldSpeed) ||
            float.IsInfinity(currentWorldSpeed))
        {
            currentWorldSpeed =
                0f;
        }

        currentWorldSpeed =
            Mathf.Max(
                0f,
                currentWorldSpeed
            );

        float displayedSpeed =
            Mathf.Clamp(
                currentWorldSpeed,
                0f,
                safeMaximumSpeed
            );

        float epsilon =
            Mathf.Max(
                0f,
                speedChangeEpsilon
            );

        bool maximumSpeedChanged =
            float.IsNaN(
                lastAppliedMaximumSpeed
            ) ||
            Mathf.Approximately(
                safeMaximumSpeed,
                lastAppliedMaximumSpeed
            ) == false;

        bool displayedSpeedChanged =
            float.IsNaN(
                lastDisplayedSpeed
            ) ||
            Mathf.Abs(
                displayedSpeed -
                lastDisplayedSpeed
            ) >
            epsilon;

        if (maximumSpeedChanged == false &&
            displayedSpeedChanged == false)
        {
            return;
        }

        if (maximumSpeedChanged)
        {
            speedSlider.minValue =
                0f;

            speedSlider.maxValue =
                safeMaximumSpeed;
        }

        /*
         * UI 只能讀取玩家速度，不能反向影響 Gameplay。
         *
         * 使用 SetValueWithoutNotify，避免顯示刷新觸發
         * Slider.onValueChanged 中可能存在的其他邏輯。
         */
        speedSlider.SetValueWithoutNotify(
            displayedSpeed
        );

        lastDisplayedSpeed =
            displayedSpeed;

        lastAppliedMaximumSpeed =
            safeMaximumSpeed;
    }

    private float GetSafeMaximumDisplayedSpeed()
    {
        return Mathf.Max(
            0.01f,
            maximumDisplayedSpeed
        );
    }

    #endregion

    // =====================================================================
    #region Visual

    private void SetVisualVisible(
        bool visible
    )
    {
        /*
         * 除了檢查快取，也核對真正的 activeSelf。
         *
         * 場景初始狀態可能與 bool 預設值不同，
         * 不可只看 visualVisible 就直接 return。
         */
        if (visualVisible == visible &&
            (speedVisualRoot == null ||
             speedVisualRoot.activeSelf == visible))
        {
            return;
        }

        if (speedVisualRoot != null &&
            speedVisualRoot.activeSelf !=
                visible)
        {
            speedVisualRoot.SetActive(
                visible
            );
        }

        visualVisible =
            visible;
    }

    #endregion
}