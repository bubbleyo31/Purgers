using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FOV 修改模式。
/// </summary>
public enum FovModifierMode
{
    /// <summary>
    /// 在目前 FOV 上增加指定角度。
    ///
    /// 適合：
/// 勾索、跑步、衝刺、爆炸震撼等速度效果。
    /// </summary>
    Additive = 0,

    /// <summary>
    /// 將目前 FOV 混合到指定目標值。
    ///
    /// 適合：
/// 武器瞄準、狙擊鏡、過場鏡頭等需要精確 FOV 的功能。
    /// </summary>
    Override = 1
}

/// <summary>
/// FOV 修改要影響的攝影機。
/// </summary>
[Flags]
public enum FovCameraChannel
{
    None = 0,

    /// <summary>
    /// 場景世界攝影機。
    /// </summary>
    World = 1,

    /// <summary>
    /// 第一人稱手部與武器攝影機。
    /// </summary>
    Weapon = 2,

    /// <summary>
    /// 同時影響世界與武器攝影機。
    /// </summary>
    Both = World | Weapon
}

/// <summary>
/// 第一人稱 FOV 集中管理器。
///
/// 所有需要修改 FOV 的功能都必須向此管理器提出請求，
/// 不可自行直接修改 Camera.fieldOfView。
///
/// 支援：
/// 1. World Camera 與 Weapon Camera 分開管理。
/// 2. Additive 疊加模式。
/// 3. Override 覆蓋模式。
/// 4. 優先權排序。
/// 5. 每個效果獨立的漸入與漸出時間。
/// 6. 多個效果同時存在。
///
/// 建議掛在 CameraRig 根物件。
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonFovManager : MonoBehaviour
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 場景中唯一的 FOV 管理器。
    /// </summary>
    public static FirstPersonFovManager Singleton
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 內部請求資料

    /// <summary>
    /// 單一 FOV 修改請求。
    /// </summary>
    private sealed class FovRequest
    {
        /// <summary>
        /// 請求的唯一編號。
        /// </summary>
        public int Handle;

        /// <summary>
        /// 除錯用名稱。
        /// </summary>
        public string DebugName;

        /// <summary>
        /// 增加模式或覆蓋模式。
        /// </summary>
        public FovModifierMode Mode;

        /// <summary>
        /// 要影響哪些攝影機。
        /// </summary>
        public FovCameraChannel Channels;

        /// <summary>
        /// 優先權。數字越高越晚套用。
        /// </summary>
        public int Priority;

        /// <summary>
        /// World Camera 使用的值。
        ///
        /// Additive 模式代表增加多少角度。
        /// Override 模式代表目標 FOV。
        /// </summary>
        public float WorldValue;

        /// <summary>
        /// Weapon Camera 使用的值。
        ///
        /// Additive 模式代表增加多少角度。
        /// Override 模式代表目標 FOV。
        /// </summary>
        public float WeaponValue;

        /// <summary>
        /// 目前已經平滑後的權重。
        /// </summary>
        public float CurrentWeight;

        /// <summary>
        /// 外部系統指定的目標權重。
        /// </summary>
        public float TargetWeight;

        /// <summary>
        /// 權重從零增加到一所需時間。
        /// </summary>
        public float BlendInDuration;

        /// <summary>
        /// 權重從一降低到零所需時間。
        /// </summary>
        public float BlendOutDuration;

        /// <summary>
        /// 權重歸零後是否自動移除此請求。
        /// </summary>
        public bool RemoveWhenWeightReachesZero;
    }

    #endregion

    // =====================================================================
    #region 攝影機引用

    [Header("攝影機引用")]

    [SerializeField]
    [Tooltip("負責渲染場景世界的 Base Camera。此管理器是唯一應該修改它 FOV 的物件。")]
    private Camera worldCamera;

    [SerializeField]
    [Tooltip("負責渲染第一人稱手部與武器的 Overlay Camera。若目前不需要管理武器 FOV，可以留空。")]
    private Camera weaponCamera;

    #endregion

    // =====================================================================
    #region 基礎 FOV

    [Header("基礎 FOV")]

    [SerializeField]
    [Tooltip("開啟後，遊戲開始時會讀取兩台 Camera 目前的 FOV，並將它們作為基礎值。適合直接使用 Camera Inspector 已經設定好的數值。")]
    private bool captureCameraFovOnAwake = true;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("沒有任何效果時，World Camera 使用的基礎 FOV。若開啟 Capture Camera Fov On Awake，遊戲開始時會由 Camera 目前數值覆蓋。")]
    private float baseWorldFov = 90f;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("沒有任何效果時，Weapon Camera 使用的基礎 FOV。若開啟 Capture Camera Fov On Awake，遊戲開始時會由 Camera 目前數值覆蓋。")]
    private float baseWeaponFov = 60f;

    #endregion

    // =====================================================================
    #region FOV 安全範圍

    [Header("FOV 安全範圍")]

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("World Camera 最低允許的 FOV。避免多個效果組合後出現不合理的極小角度。")]
    private float minimumWorldFov = 20f;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("World Camera 最高允許的 FOV。避免多個效果組合後產生嚴重畫面扭曲。")]
    private float maximumWorldFov = 130f;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("Weapon Camera 最低允許的 FOV。")]
    private float minimumWeaponFov = 20f;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("Weapon Camera 最高允許的 FOV。")]
    private float maximumWeaponFov = 100f;

    #endregion

    // =====================================================================
    #region 時間設定

    [Header("時間設定")]

    [SerializeField]
    [Tooltip("開啟後，FOV 漸變使用不受 Time Scale 影響的時間。子彈時間期間，FOV 仍會以正常視覺速度完成漸變。")]
    private bool useUnscaledTime = true;

    #endregion

    // =====================================================================
    #region 執行時除錯

    [Header("執行時除錯")]

    [SerializeField]
    [Tooltip("開啟後，Inspector 會顯示目前管理器計算出的兩台攝影機 FOV。")]
    private bool showDebugValues = false;

    [SerializeField]
    [Tooltip("目前計算完成的 World Camera FOV。此欄位只供執行時觀察。")]
    private float debugCurrentWorldFov;

    [SerializeField]
    [Tooltip("目前計算完成的 Weapon Camera FOV。此欄位只供執行時觀察。")]
    private float debugCurrentWeaponFov;

    [SerializeField]
    [Tooltip("目前仍存在於管理器中的 FOV 請求數量。此欄位只供執行時觀察。")]
    private int debugActiveRequestCount;

    #endregion

    // =====================================================================
    #region 執行資料

    /// <summary>
    /// 所有已建立的 FOV 請求。
    /// </summary>
    private readonly List<FovRequest> requests =
        new List<FovRequest>();

    /// <summary>
    /// 透過 Handle 快速找到請求。
    /// </summary>
    private readonly Dictionary<int, FovRequest> requestLookup =
        new Dictionary<int, FovRequest>();

    /// <summary>
    /// 下一個可用的請求編號。
    /// </summary>
    private int nextHandle = 1;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 無效的 FOV 請求編號。
    /// </summary>
    public const int InvalidHandle = 0;

    /// <summary>
    /// 目前基礎 World Camera FOV。
    /// </summary>
    public float BaseWorldFov =>
        baseWorldFov;

    /// <summary>
    /// 目前基礎 Weapon Camera FOV。
    /// </summary>
    public float BaseWeaponFov =>
        baseWeaponFov;

    #endregion

    // =====================================================================
    #region Unity 生命週期

    private void Awake()
    {
        RegisterSingleton();

        if (captureCameraFovOnAwake)
        {
            if (worldCamera != null)
            {
                baseWorldFov =
                    worldCamera.fieldOfView;
            }

            if (weaponCamera != null)
            {
                baseWeaponFov =
                    weaponCamera.fieldOfView;
            }
        }

        ApplyImmediateBaseFov();
    }

    private void LateUpdate()
    {
        float deltaTime =
            useUnscaledTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

        UpdateRequestWeights(
            deltaTime
        );

        EvaluateAndApplyFov();

        RemoveFinishedRequests();
    }

    private void OnDisable()
    {
        ApplyImmediateBaseFov();
    }

    private void OnDestroy()
    {
        if (Singleton == this)
        {
            Singleton = null;
        }
    }

    private void OnValidate()
    {
        minimumWorldFov =
            Mathf.Clamp(
                minimumWorldFov,
                1f,
                179f
            );

        maximumWorldFov =
            Mathf.Clamp(
                maximumWorldFov,
                minimumWorldFov,
                179f
            );

        minimumWeaponFov =
            Mathf.Clamp(
                minimumWeaponFov,
                1f,
                179f
            );

        maximumWeaponFov =
            Mathf.Clamp(
                maximumWeaponFov,
                minimumWeaponFov,
                179f
            );
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
            $"場景中只能存在一個 {nameof(FirstPersonFovManager)}。",
            this
        );

        Destroy(this);
    }

    #endregion

    // =====================================================================
    #region 建立與移除請求

    /// <summary>
    /// 建立一個新的 FOV 修改請求。
    /// </summary>
    /// <param name="debugName">
    /// 除錯用名稱，例如 Grapple、Sprint、Aim。
    /// </param>
    /// <param name="mode">
    /// 增加模式或覆蓋模式。
    /// </param>
    /// <param name="channels">
    /// 要影響的攝影機。
    /// </param>
    /// <param name="priority">
    /// 優先權。數字越高越晚套用。
    /// </param>
    /// <param name="worldValue">
    /// World Camera 使用的增加角度或覆蓋目標。
    /// </param>
    /// <param name="weaponValue">
    /// Weapon Camera 使用的增加角度或覆蓋目標。
    /// </param>
    /// <param name="blendInDuration">
    /// 權重從零增加到一所需秒數。
    /// </param>
    /// <param name="blendOutDuration">
    /// 權重從一降低到零所需秒數。
    /// </param>
    /// <returns>
    /// 後續控制此請求使用的唯一 Handle。
    /// </returns>
    public int CreateRequest(
        string debugName,
        FovModifierMode mode,
        FovCameraChannel channels,
        int priority,
        float worldValue,
        float weaponValue,
        float blendInDuration,
        float blendOutDuration
    )
    {
        int handle =
            nextHandle++;

        FovRequest request =
            new FovRequest
            {
                Handle = handle,
                DebugName = debugName,
                Mode = mode,
                Channels = channels,
                Priority = priority,
                WorldValue = worldValue,
                WeaponValue = weaponValue,
                CurrentWeight = 0f,
                TargetWeight = 0f,
                BlendInDuration = Mathf.Max(
                    0f,
                    blendInDuration
                ),
                BlendOutDuration = Mathf.Max(
                    0f,
                    blendOutDuration
                ),
                RemoveWhenWeightReachesZero = false
            };

        requests.Add(
            request
        );

        requestLookup.Add(
            handle,
            request
        );

        /*
         * 低優先權先計算，
         * 高優先權最後計算。
         */
        requests.Sort(
            (left, right) =>
                left.Priority.CompareTo(
                    right.Priority
                )
        );

        return handle;
    }

    /// <summary>
    /// 修改請求的目標權重。
    ///
    /// 零代表完全不套用。
    /// 一代表完整套用。
    /// </summary>
    public void SetRequestWeight(
        int handle,
        float targetWeight
    )
    {
        if (requestLookup.TryGetValue(
                handle,
                out FovRequest request
            ) == false)
        {
            return;
        }

        request.TargetWeight =
            Mathf.Clamp01(
                targetWeight
            );

        request.RemoveWhenWeightReachesZero =
            false;
    }

    /// <summary>
    /// 修改既有請求的 FOV 數值。
    ///
    /// 武器切換後，瞄準系統可以用這個方法
    /// 更新不同武器的瞄準 FOV。
    /// </summary>
    public void SetRequestValues(
        int handle,
        float worldValue,
        float weaponValue
    )
    {
        if (requestLookup.TryGetValue(
                handle,
                out FovRequest request
            ) == false)
        {
            return;
        }

        request.WorldValue =
            worldValue;

        request.WeaponValue =
            weaponValue;
    }

    /// <summary>
    /// 讓請求平滑淡出，淡出完成後自動移除。
    /// </summary>
    public void ReleaseRequest(
        int handle
    )
    {
        if (requestLookup.TryGetValue(
                handle,
                out FovRequest request
            ) == false)
        {
            return;
        }

        request.TargetWeight =
            0f;

        request.RemoveWhenWeightReachesZero =
            true;
    }

    /// <summary>
    /// 立即移除指定請求，不播放淡出。
    /// </summary>
    public void RemoveRequestImmediately(
        int handle
    )
    {
        if (requestLookup.TryGetValue(
                handle,
                out FovRequest request
            ) == false)
        {
            return;
        }

        requests.Remove(
            request
        );

        requestLookup.Remove(
            handle
        );
    }

    /// <summary>
    /// 移除所有 FOV 請求並立刻恢復基礎 FOV。
    /// </summary>
    public void ClearAllRequests()
    {
        requests.Clear();
        requestLookup.Clear();

        ApplyImmediateBaseFov();
    }

    #endregion

    // =====================================================================
    #region 基礎 FOV

    /// <summary>
    /// 修改兩台攝影機的基礎 FOV。
    ///
    /// 未來玩家在設定選單調整視野時，
    /// 應呼叫這個方法，不要直接修改 Camera。
    /// </summary>
    public void SetBaseFov(
        float newWorldFov,
        float newWeaponFov
    )
    {
        baseWorldFov =
            Mathf.Clamp(
                newWorldFov,
                minimumWorldFov,
                maximumWorldFov
            );

        baseWeaponFov =
            Mathf.Clamp(
                newWeaponFov,
                minimumWeaponFov,
                maximumWeaponFov
            );
    }

    #endregion

    // =====================================================================
    #region 權重更新

    private void UpdateRequestWeights(
        float deltaTime
    )
    {
        for (int i = 0;
             i < requests.Count;
             i++)
        {
            FovRequest request =
                requests[i];

            if (Mathf.Approximately(
                    request.CurrentWeight,
                    request.TargetWeight
                ))
            {
                request.CurrentWeight =
                    request.TargetWeight;

                continue;
            }

            bool isIncreasing =
                request.TargetWeight >
                request.CurrentWeight;

            float duration =
                isIncreasing
                    ? request.BlendInDuration
                    : request.BlendOutDuration;

            if (duration <= 0f)
            {
                request.CurrentWeight =
                    request.TargetWeight;

                continue;
            }

            /*
             * 由零到一會剛好花費 Duration 秒。
             */
            float maximumWeightChange =
                deltaTime /
                duration;

            request.CurrentWeight =
                Mathf.MoveTowards(
                    request.CurrentWeight,
                    request.TargetWeight,
                    maximumWeightChange
                );
        }
    }

    #endregion

    // =====================================================================
    #region FOV 計算

    private void EvaluateAndApplyFov()
    {
        float calculatedWorldFov =
            baseWorldFov;

        float calculatedWeaponFov =
            baseWeaponFov;

        for (int i = 0;
             i < requests.Count;
             i++)
        {
            FovRequest request =
                requests[i];

            float weight =
                request.CurrentWeight;

            if (weight <= 0.0001f)
                continue;

            if ((request.Channels &
                 FovCameraChannel.World) != 0)
            {
                calculatedWorldFov =
                    EvaluateSingleChannel(
                        calculatedWorldFov,
                        request.WorldValue,
                        weight,
                        request.Mode
                    );
            }

            if ((request.Channels &
                 FovCameraChannel.Weapon) != 0)
            {
                calculatedWeaponFov =
                    EvaluateSingleChannel(
                        calculatedWeaponFov,
                        request.WeaponValue,
                        weight,
                        request.Mode
                    );
            }
        }

        calculatedWorldFov =
            Mathf.Clamp(
                calculatedWorldFov,
                minimumWorldFov,
                maximumWorldFov
            );

        calculatedWeaponFov =
            Mathf.Clamp(
                calculatedWeaponFov,
                minimumWeaponFov,
                maximumWeaponFov
            );

        if (worldCamera != null)
        {
            worldCamera.fieldOfView =
                calculatedWorldFov;
        }

        if (weaponCamera != null)
        {
            weaponCamera.fieldOfView =
                calculatedWeaponFov;
        }

        if (showDebugValues)
        {
            debugCurrentWorldFov =
                calculatedWorldFov;

            debugCurrentWeaponFov =
                calculatedWeaponFov;

            debugActiveRequestCount =
                requests.Count;
        }
    }

    /// <summary>
    /// 計算單一攝影機通道的 FOV。
    /// </summary>
    private static float EvaluateSingleChannel(
        float currentFov,
        float requestValue,
        float weight,
        FovModifierMode mode
    )
    {
        switch (mode)
        {
            case FovModifierMode.Additive:
            {
                return currentFov +
                       requestValue *
                       weight;
            }

            case FovModifierMode.Override:
            {
                return Mathf.Lerp(
                    currentFov,
                    requestValue,
                    weight
                );
            }

            default:
            {
                return currentFov;
            }
        }
    }

    #endregion

    // =====================================================================
    #region 請求清理

    private void RemoveFinishedRequests()
    {
        for (int i = requests.Count - 1;
             i >= 0;
             i--)
        {
            FovRequest request =
                requests[i];

            if (request.RemoveWhenWeightReachesZero == false)
                continue;

            if (request.TargetWeight > 0f ||
                request.CurrentWeight > 0.0001f)
            {
                continue;
            }

            requestLookup.Remove(
                request.Handle
            );

            requests.RemoveAt(
                i
            );
        }
    }

    #endregion

    // =====================================================================
    #region 基礎值套用

    private void ApplyImmediateBaseFov()
    {
        if (worldCamera != null)
        {
            worldCamera.fieldOfView =
                baseWorldFov;
        }

        if (weaponCamera != null)
        {
            weaponCamera.fieldOfView =
                baseWeaponFov;
        }
    }

    #endregion
}