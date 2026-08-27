using UnityEngine;

/// <summary>
/// 勾索速度 FOV 效果。
///
/// 此元件不會直接修改 Camera.fieldOfView。
/// 它只會向 FirstPersonFovManager 提交一個 FOV 請求。
///
/// 建議掛在 CameraRig 根物件，
/// 與 FirstPersonFovManager 放在一起。
/// </summary>
[DisallowMultipleComponent]
public class GrappleFovEffect : MonoBehaviour
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 場景中唯一的本地勾索 FOV 控制器。
    /// </summary>
    public static GrappleFovEffect Singleton
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 管理器引用

    [Header("FOV 管理器")]

    [SerializeField]
    [Tooltip("場景中的 FirstPersonFovManager。若留空，Awake 與 Update 會嘗試從 Singleton 自動取得。")]
    private FirstPersonFovManager fovManager;

    #endregion

    // =====================================================================
    #region FOV 修改設定

    [Header("勾索 FOV 修改")]

    [SerializeField]
    [Tooltip("勾索 FOV 請求的優先權。數字越高越晚套用。建議低於武器瞄準，高於一般跑步。")]
    private int requestPriority = 200;

    [SerializeField]
    [Tooltip("勾索達到最高速度效果時，World Camera 額外增加多少 FOV。依目前基礎 FOV 約為 90，建議先使用 12 到 20。")]
    private float maximumWorldFovIncrease = 18f;

    [SerializeField]
    [Tooltip("勾索達到最高速度效果時，Weapon Camera 額外增加多少 FOV。建議明顯小於 World Camera，避免手部與武器嚴重變形。")]
    private float maximumWeaponFovIncrease = 3f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("勾索 FOV 從零提升到完整強度所需時間。")]
    private float blendInDuration = 0.12f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("勾索 FOV 從完整強度恢復到零所需時間。建議比漸入稍慢，讓拋出速度感平順消退。")]
    private float blendOutDuration = 0.3f;

    #endregion

    // =====================================================================
    #region 速度映射

    [Header("速度映射")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家實際速度低於此值時，不增加勾索 FOV。依目前一般 Kinematic Speed 為 20，建議先設為 25 到 35。")]
    private float minimumEffectSpeed = 30f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("玩家實際速度達到此值時，使用最大勾索 FOV 增加量。依目前 Grapple Max Speed 為 150，建議先設為 130 到 150。")]
    private float maximumEffectSpeed = 140f;

    [SerializeField]
    [Tooltip("將速度比例轉換成 FOV 強度的曲線。X 軸是速度比例，Y 軸是 FOV 效果比例。")]
    private AnimationCurve speedToFovCurve =
        AnimationCurve.EaseInOut(
            0f,
            0f,
            1f,
            1f
        );

    #endregion

    // =====================================================================
    #region 允許狀態

    [Header("允許顯示的狀態")]

    [SerializeField]
    [Tooltip("開啟後，勾索正式附著並拉動玩家時增加 FOV。")]
    private bool applyWhileGrapplePulling = true;

    [SerializeField]
    [Tooltip("開啟後，玩家結束勾索但尚未落地時，依目前速度繼續保留 FOV 效果。")]
    private bool applyWhileGrappleAirborne = true;

    [SerializeField]
    [Tooltip("開啟後，玩家再次按下勾索鍵取消後，在手動慣性推進期間保留 FOV 效果。")]
    private bool applyDuringManualCancelMomentum = true;

    #endregion

    // =====================================================================
    #region 執行時除錯

    [Header("執行時除錯")]

    [SerializeField]
    [Tooltip("開啟後，Inspector 會顯示目前讀取到的速度與 FOV 請求權重。")]
    private bool showDebugValues = false;

    [SerializeField]
    [Tooltip("目前讀取到的玩家實際速度。此欄位只供執行時觀察。")]
    private float debugCurrentSpeed;

    [SerializeField]
    [Tooltip("目前送給 FOV 管理器的權重。此欄位只供執行時觀察。")]
    private float debugRequestWeight;

    [SerializeField]
    [Tooltip("目前玩家狀態是否允許勾索 FOV。此欄位只供執行時觀察。")]
    private bool debugStateAllowed;

    #endregion

    // =====================================================================
    #region 執行資料

    /// <summary>
    /// 目前的本地玩家。
    /// </summary>
    private Player targetPlayer;

    /// <summary>
    /// 目前本地玩家的狀態機。
    /// </summary>
    private PlayerStateMachine targetStateMachine;

    /// <summary>
    /// 向 FOV 管理器取得的請求 Handle。
    /// </summary>
    private int fovRequestHandle =
        FirstPersonFovManager.InvalidHandle;

    #endregion

    // =====================================================================
    #region Unity 生命週期

    private void Awake()
    {
        RegisterSingleton();

        if (fovManager == null)
        {
            fovManager =
                FirstPersonFovManager.Singleton;
        }

        EnsureRequestCreated();
    }

    private void Update()
    {
        /*
         * 處理 Unity 腳本 Awake 順序不確定的情況。
         */
        if (fovManager == null)
        {
            fovManager =
                FirstPersonFovManager.Singleton;
        }

        EnsureRequestCreated();
        UpdateFovRequest();
    }

    private void OnDisable()
    {
        SetRequestWeight(
            0f
        );
    }

    private void OnDestroy()
    {
        if (fovManager != null &&
            fovRequestHandle !=
            FirstPersonFovManager.InvalidHandle)
        {
            fovManager.ReleaseRequest(
                fovRequestHandle
            );
        }

        if (Singleton == this)
        {
            Singleton = null;
        }
    }

    #endregion

    // =====================================================================
    #region Singleton 管理

    private void RegisterSingleton()
    {
        if (Singleton == null)
        {
            Singleton = this;
            return;
        }

        if (Singleton == this)
            return;

        Debug.LogError(
            $"場景中只能存在一個 {nameof(GrappleFovEffect)}。",
            this
        );

        Destroy(this);
    }

    #endregion

    // =====================================================================
    #region 建立 FOV 請求

    private void EnsureRequestCreated()
    {
        if (fovManager == null)
            return;

        if (fovRequestHandle !=
            FirstPersonFovManager.InvalidHandle)
        {
            return;
        }

        /*
         * 勾索屬於速度感效果，
         * 因此使用 Additive，而不是 Override。
         *
         * 未來武器瞄準可以使用較高優先權的 Override，
         * 自然壓過此效果。
         */
        fovRequestHandle =
            fovManager.CreateRequest(
                debugName: "Grapple Speed",
                mode: FovModifierMode.Additive,
                channels: FovCameraChannel.Both,
                priority: requestPriority,
                worldValue: maximumWorldFovIncrease,
                weaponValue: maximumWeaponFovIncrease,
                blendInDuration: blendInDuration,
                blendOutDuration: blendOutDuration
            );
    }

    #endregion

    // =====================================================================
    #region 玩家綁定

    /// <summary>
    /// 指定目前本地玩家。
    /// </summary>
    public void SetTarget(
        Player player
    )
    {
        targetPlayer =
            player;

        targetStateMachine =
            player != null
                ? player.StateMachine
                : null;

        SetRequestWeight(
            0f
        );
    }

    /// <summary>
    /// 清除本地玩家引用。
    /// </summary>
    public void ClearTarget(
        Player player
    )
    {
        if (targetPlayer != player)
            return;

        targetPlayer =
            null;

        targetStateMachine =
            null;

        SetRequestWeight(
            0f
        );
    }

    #endregion

    // =====================================================================
    #region FOV 更新

    private void UpdateFovRequest()
    {
        if (targetPlayer == null ||
            targetStateMachine == null)
        {
            SetRequestWeight(
                0f
            );

            UpdateDebugValues(
                0f,
                0f,
                false
            );

            return;
        }

        bool stateAllowed =
            IsCurrentStateAllowed();

        float currentSpeed =
            targetPlayer.CurrentWorldSpeed;

        float targetWeight =
            0f;

        if (stateAllowed)
        {
            float speedRatio =
                Mathf.InverseLerp(
                    minimumEffectSpeed,
                    Mathf.Max(
                        maximumEffectSpeed,
                        minimumEffectSpeed +
                        0.01f
                    ),
                    currentSpeed
                );

            targetWeight =
                speedToFovCurve != null
                    ? speedToFovCurve.Evaluate(
                        speedRatio
                    )
                    : speedRatio;

            targetWeight =
                Mathf.Clamp01(
                    targetWeight
                );
        }

        SetRequestWeight(
            targetWeight
        );

        UpdateDebugValues(
            currentSpeed,
            targetWeight,
            stateAllowed
        );
    }

    /// <summary>
    /// 判斷目前狀態是否允許勾索 FOV。
    /// </summary>
    private bool IsCurrentStateAllowed()
    {
        if (applyWhileGrapplePulling &&
            targetPlayer.IsGrapplePulling)
        {
            return true;
        }

        if (applyWhileGrappleAirborne &&
            targetStateMachine.CurrentState ==
            PlayerMovementState.GrappleAirborne)
        {
            return true;
        }

        if (applyDuringManualCancelMomentum &&
            targetPlayer
                .IsManualCancelMomentumActive)
        {
            return true;
        }

        return false;
    }

    private void SetRequestWeight(
        float weight
    )
    {
        if (fovManager == null ||
            fovRequestHandle ==
            FirstPersonFovManager.InvalidHandle)
        {
            return;
        }

        fovManager.SetRequestWeight(
            fovRequestHandle,
            weight
        );
    }

    #endregion

    // =====================================================================
    #region 除錯資訊

    private void UpdateDebugValues(
        float currentSpeed,
        float requestWeight,
        bool stateAllowed
    )
    {
        if (showDebugValues == false)
            return;

        debugCurrentSpeed =
            currentSpeed;

        debugRequestWeight =
            requestWeight;

        debugStateAllowed =
            stateAllowed;
    }

    #endregion
}