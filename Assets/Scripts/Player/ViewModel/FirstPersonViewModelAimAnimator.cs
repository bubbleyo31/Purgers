using Fusion;
using UnityEngine;


/// <summary>
/// 第一人稱 ViewModel 的 ADS 動畫播放控制器。
///
/// ====================================================================
///
/// 這支腳本掛在：
///
/// Attack ViewModel Prefab Root
/// 或
/// Support ViewModel Prefab Root。
///
/// ====================================================================
///
/// 它只負責本地 Presentation：
///
/// PlayerAimController.IsAimRequested = true
/// ↓
/// AimPlaybackSpeed = +1
/// ↓
/// Aim 動畫由 0 正向播放到 1
/// ↓
/// 最後一幀停止並維持瞄準姿勢。
///
/// ------------------------------------------------------------
///
/// PlayerAimController.IsAimRequested = false
/// ↓
/// AimPlaybackSpeed = -1
/// ↓
/// Aim 動畫從目前進度反向播放到 0
/// ↓
/// 第一幀停止並回到腰射姿勢。
///
/// ====================================================================
///
/// 非常重要：
///
/// 這裡不會設定 Animator.speed。
///
/// Animator.speed 是整顆 Animator 的全域速度，
/// 直接設成 -1 會連 Shoot、Reload、Melee 都一起倒播。
///
/// 本腳本只控制 ADS Layer 上 Aim State 的：
///
/// AimPlaybackSpeed
///
/// Float Parameter。
///
/// 因此其他 Animator Layer 不受影響。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(900)]
public class FirstPersonViewModelAimAnimator :
    MonoBehaviour
{
    // =====================================================================
    #region Animator Reference


    [Header("Animator 引用")]


    [SerializeField]
    [Tooltip("這個職業 ViewModel 使用的 Animator。可以位於 Prefab Root 或子物件。若留空會從目前 ViewModel Prefab 階層自動取得第一顆 Animator。")]
    private Animator viewModelAnimator;


    #endregion


    // =====================================================================
    #region Animator ADS Contract


    [Header("Animator ADS Layer")]


    [SerializeField]
    [Tooltip("Animator Controller 中專門播放開鏡動畫的 Layer 名稱。必須與 Animator 視窗完全一致，包含大小寫。建議固定使用 ADS。")]
    private string aimLayerName =
        "ADS";


    [SerializeField]
    [Tooltip("ADS Layer 裡唯一 Aim State 的名稱。這個 State 使用從腰射姿勢移動到瞄準姿勢的動畫 Clip。必須與 Animator 視窗完全一致，包含大小寫。建議固定使用 Aim。")]
    private string aimStateName =
        "Aim";


    [SerializeField]
    [Tooltip("控制 Aim State 正播、停止與倒播的 Animator Float Parameter 名稱。Animator Controller 必須建立同名 Float，預設值設為 0。建議固定使用 AimPlaybackSpeed。")]
    private string aimPlaybackSpeedParameter =
        "AimPlaybackSpeed";


    [SerializeField]
    [Tooltip("開啟後會在初始化時強制把 ADS Layer Weight 設為 1。ADS Layer 建議使用 Additive，並用 Avatar Mask 限制只影響第一人稱手臂與武器。")]
    private bool forceAimLayerWeight =
        true;


    #endregion


    // =====================================================================
    #region Endpoint Settings


    [Header("動畫端點")]


    [SerializeField]
    [Range(0.0001f, 0.05f)]
    [Tooltip("判斷 Aim 動畫是否已到第一幀或最後一幀的 Normalized Time 容許值。預設 0.001。通常不需要調整；只有動畫在端點附近因浮點誤差持續抖動時才略微提高。")]
    private float endpointTolerance =
        0.001f;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後會顯示 Aim Animator 綁定、正播、倒播與端點維持資訊。只在狀態切換時輸出，不會每幀洗 Console。測試階段建議開啟。")]
    private bool debugAimAnimation =
        true;


    #endregion


    // =====================================================================
    #region Runtime Binding


    /// <summary>
    /// 目前職業 Runtime 的正式 Aim Controller。
    ///
    /// Attack 與 Support 都使用 PlayerAimController，
    /// 但各自來自自己的 Profession Runtime。
    /// </summary>
    private PlayerAimController aimController;


    #endregion


    // =====================================================================
    #region Animator Runtime


    private int aimLayerIndex =
        -1;


    private int aimStateFullPathHash;


    private int aimStateShortNameHash;


    private int aimPlaybackSpeedHash;


    private bool animatorInitialized;


    private bool lastAimRequested;


    private float aimNormalizedProgress;


    #endregion


    // =====================================================================
    #region Public Data


    /// <summary>
    /// Aim 動畫目前的 Normalized Progress。
    ///
    /// 0：完整腰射。
    /// 1：完整 ADS。
    ///
    /// 可供其他純本地 Presentation
    /// 讀取目前開關鏡動畫進度。
    /// </summary>
    public float AimNormalizedProgress =>
        Mathf.Clamp01(
            aimNormalizedProgress
        );


    /// <summary>
    /// Aim 動畫是否已停在最後 ADS 姿勢。
    /// </summary>
    public bool IsHoldingAimPose =>
        AimNormalizedProgress >=
        1f - endpointTolerance;


    /// <summary>
    /// 本地 ViewModel 目前是否仍要求維持瞄準。
    ///
    /// 這是經過 TryReadAimRequested() 安全驗證後保存的 Presentation 狀態，
    /// 外部本地動畫／Motion 系統不需要再次直接讀取 Networked Property。
    ///
    /// true：按住 Aim，包含開鏡動畫播放中與完整瞄準。
    /// false：已放開 Aim、Runtime 尚未 Spawn 或舊 Runtime 已失效。
    /// </summary>
    public bool IsAimRequested =>
        lastAimRequested;


    /// <summary>
    /// 目前綁定的正式 Aim Controller。
    /// </summary>
    public PlayerAimController AimController =>
        aimController;


    #endregion


    // =====================================================================
    #region Unity


    private void Awake()
    {
        ResolveAnimator();
    }


    private void OnEnable()
    {
        InitializeAnimatorContract();
    }


    /// <summary>
    /// Update 先設定本幀應使用的播放方向。
    ///
    /// Animator 會在 Update 與 LateUpdate 之間評估動畫，
    /// 因此 LateUpdate 可以取得更新後的 Normalized Time。
    /// </summary>
    private void Update()
    {
        if (InitializeAnimatorContract() ==
            false)
        {
            return;
        }


        bool aimRequested =
            TryReadAimRequested(
                out bool currentAimRequested
            ) &&
            currentAimRequested;


        float playbackSpeed;


        if (aimRequested)
        {
            playbackSpeed =
                AimNormalizedProgress <
                    1f - endpointTolerance
                    ? 1f
                    : 0f;
        }
        else
        {
            playbackSpeed =
                AimNormalizedProgress >
                    endpointTolerance
                    ? -1f
                    : 0f;
        }


        viewModelAnimator.SetFloat(
            aimPlaybackSpeedHash,
            playbackSpeed
        );


        if (aimRequested !=
            lastAimRequested)
        {
            lastAimRequested =
                aimRequested;


            if (debugAimAnimation)
            {
                Debug.Log(
                    $"[ViewModel ADS Animation] " +
                    $"{(aimRequested ? "Forward" : "Reverse")}" +
                    $"\nViewModel：{gameObject.name}" +
                    $"\nCurrent Progress：{AimNormalizedProgress:0.###}" +
                    $"\nPlayback Speed：{playbackSpeed:0.###}",
                    this
                );
            }
        }
    }


    /// <summary>
    /// 讀取 Animator 本幀真正到達的進度，
    /// 並在 0 / 1 端點把 State 精確停住。
    /// </summary>
    private void LateUpdate()
    {
        if (animatorInitialized == false ||
            viewModelAnimator == null)
        {
            return;
        }


        AnimatorStateInfo stateInfo =
            viewModelAnimator
                .GetCurrentAnimatorStateInfo(
                    aimLayerIndex
                );


        // =============================================================
        // ADS Layer 被意外切到其他 State
        // =============================================================

        /*
         * 正式 ADS Layer 只應該有一顆 Aim State，
         * 而且不需要 Transition。
         *
         * 如果 Animator Controller 被誤改，
         * 這裡會回到 Aim State 的目前進度，
         * 避免 Motion Weight 與 Animator Pose 分離。
         */
        if (stateInfo.shortNameHash !=
            aimStateShortNameHash)
        {
            ForceAimStateAt(
                AimNormalizedProgress
            );


            return;
        }


        aimNormalizedProgress =
            Mathf.Clamp01(
                stateInfo.normalizedTime
            );


        bool aimRequested =
            TryReadAimRequested(
                out bool currentAimRequested
            ) &&
            currentAimRequested;


        if (aimRequested &&
            aimNormalizedProgress >=
                1f - endpointTolerance)
        {
            HoldAtEndpoint(
                1f
            );


            return;
        }


        if (aimRequested == false &&
            aimNormalizedProgress <=
                endpointTolerance)
        {
            HoldAtEndpoint(
                0f
            );
        }
    }


    #endregion


    // =====================================================================
    #region Public Binding


    /// <summary>
    /// 由 ProfessionViewModelManager
    /// 綁定目前職業 Runtime 的 PlayerAimController。
    ///
    /// 傳入 null 時會安全地要求 Aim 動畫倒播回 0。
    /// </summary>
    public void BindAimController(
        PlayerAimController newAimController
    )
    {
        aimController =
            newAimController;


        lastAimRequested =
            TryReadAimRequested(
                out bool currentAimRequested
            ) &&
            currentAimRequested;


        if (debugAimAnimation)
        {
            Debug.Log(
                $"[ViewModel ADS Animation] Aim Controller Binding" +
                $"\nViewModel：{gameObject.name}" +
                $"\nAim Controller：" +
                $"{(aimController != null ? aimController.name : "NULL")}" +
                $"\nAim Requested：{lastAimRequested}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Fusion Network State Safety


    /// <summary>
    /// 安全讀取目前 Runtime Aim Controller 的 Aim 狀態。
    ///
    /// ====================================================================
    ///
    /// 職業切換同一幀可能發生：
    ///
    /// 舊 Profession Runtime 已 Despawn
    /// ↓
    /// ViewModel Aim Animator 尚未收到 Manager Unbind
    /// ↓
    /// aimController 的 C# Reference 仍然不為 null
    /// ↓
    /// 直接讀取 IsAimRequested
    /// ↓
    /// CurrentAimPhase 尚未 Spawn 或已失效
    /// ↓
    /// Fusion 拋出 InvalidOperationException。
    ///
    /// ====================================================================
    ///
    /// 所以不能只判斷 aimController != null，
    /// 還必須確認它所屬的 NetworkObject 仍然有效。
    ///
    /// 無效期間回傳 false，讓 ADS 動畫安全倒播回腰射；
    /// 新 Runtime Spawn 後會由 ProfessionViewModelManager 重新綁定。
    /// </summary>
    private bool TryReadAimRequested(
        out bool aimRequested
    )
    {
        aimRequested =
            false;


        if (aimController == null)
        {
            return false;
        }


        NetworkObject networkObject =
            aimController.Object;


        if (networkObject == null ||
            networkObject.IsValid == false)
        {
            return false;
        }


        aimRequested =
            aimController.IsAimRequested;


        return true;
    }


    #endregion


    // =====================================================================
    #region Animator Initialization


    private void ResolveAnimator()
    {
        if (viewModelAnimator != null)
        {
            return;
        }


        viewModelAnimator =
            GetComponentInChildren<Animator>(
                true
            );
    }


    /// <summary>
    /// 驗證 Animator Layer、State 與 Parameter Contract。
    /// </summary>
    private bool InitializeAnimatorContract()
    {
        if (animatorInitialized)
        {
            return true;
        }


        ResolveAnimator();


        if (viewModelAnimator == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonViewModelAimAnimator)}] " +
                $"ViewModel 找不到 Animator。" +
                $"\nViewModel：{gameObject.name}",
                this
            );


            return false;
        }


        if (viewModelAnimator.runtimeAnimatorController ==
            null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonViewModelAimAnimator)}] " +
                $"ViewModel Animator 尚未指定 Runtime Animator Controller。" +
                $"\nAnimator：{viewModelAnimator.name}",
                viewModelAnimator
            );


            return false;
        }


        aimLayerIndex =
            viewModelAnimator.GetLayerIndex(
                aimLayerName
            );


        if (aimLayerIndex < 0)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonViewModelAimAnimator)}] " +
                $"Animator 找不到 ADS Layer。" +
                $"\nAnimator：{viewModelAnimator.name}" +
                $"\nExpected Layer：{aimLayerName}",
                viewModelAnimator
            );


            return false;
        }


        if (HasAnimatorFloatParameter(
                aimPlaybackSpeedParameter
            ) == false)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonViewModelAimAnimator)}] " +
                $"Animator 找不到 Float Parameter。" +
                $"\nAnimator：{viewModelAnimator.name}" +
                $"\nExpected Parameter：{aimPlaybackSpeedParameter}",
                viewModelAnimator
            );


            return false;
        }


        aimStateFullPathHash =
            Animator.StringToHash(
                $"{aimLayerName}.{aimStateName}"
            );


        aimStateShortNameHash =
            Animator.StringToHash(
                aimStateName
            );


        aimPlaybackSpeedHash =
            Animator.StringToHash(
                aimPlaybackSpeedParameter
            );


        if (viewModelAnimator.HasState(
                aimLayerIndex,
                aimStateFullPathHash
            ) == false)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonViewModelAimAnimator)}] " +
                $"Animator ADS Layer 找不到指定 Aim State。" +
                $"\nAnimator：{viewModelAnimator.name}" +
                $"\nExpected State Path：" +
                $"{aimLayerName}.{aimStateName}",
                viewModelAnimator
            );


            return false;
        }


        if (forceAimLayerWeight)
        {
            viewModelAnimator.SetLayerWeight(
                aimLayerIndex,
                1f
            );
        }


        aimNormalizedProgress =
            0f;


        viewModelAnimator.SetFloat(
            aimPlaybackSpeedHash,
            0f
        );


        ForceAimStateAt(
            0f
        );


        animatorInitialized =
            true;


        if (debugAimAnimation)
        {
            Debug.Log(
                $"[ViewModel ADS Animation] Initialized" +
                $"\nViewModel：{gameObject.name}" +
                $"\nAnimator：{viewModelAnimator.name}" +
                $"\nLayer：{aimLayerName}" +
                $"\nState：{aimStateName}" +
                $"\nSpeed Parameter：{aimPlaybackSpeedParameter}",
                this
            );
        }


        return true;
    }


    private bool HasAnimatorFloatParameter(
        string parameterName
    )
    {
        if (string.IsNullOrWhiteSpace(
                parameterName
            ))
        {
            return false;
        }


        int parameterHash =
            Animator.StringToHash(
                parameterName
            );


        AnimatorControllerParameter[] parameters =
            viewModelAnimator.parameters;


        for (int i = 0;
             i < parameters.Length;
             i++)
        {
            AnimatorControllerParameter parameter =
                parameters[i];


            if (parameter.nameHash ==
                    parameterHash &&
                parameter.type ==
                    AnimatorControllerParameterType.Float)
            {
                return true;
            }
        }


        return false;
    }


    #endregion


    // =====================================================================
    #region Animator Endpoint Control


    private void HoldAtEndpoint(
        float normalizedTime
    )
    {
        float endpoint =
            Mathf.Clamp01(
                normalizedTime
            );


        bool alreadyHolding =
            Mathf.Abs(
                aimNormalizedProgress -
                endpoint
            ) <= endpointTolerance &&
            Mathf.Abs(
                viewModelAnimator.GetFloat(
                    aimPlaybackSpeedHash
                )
            ) <= 0.0001f;


        aimNormalizedProgress =
            endpoint;


        viewModelAnimator.SetFloat(
            aimPlaybackSpeedHash,
            0f
        );


        ForceAimStateAt(
            endpoint
        );


        if (alreadyHolding == false &&
            debugAimAnimation)
        {
            Debug.Log(
                $"[ViewModel ADS Animation] Hold Endpoint" +
                $"\nViewModel：{gameObject.name}" +
                $"\nProgress：{endpoint:0.###}",
                this
            );
        }
    }


    private void ForceAimStateAt(
        float normalizedTime
    )
    {
        viewModelAnimator.Play(
            aimStateFullPathHash,
            aimLayerIndex,
            Mathf.Clamp01(
                normalizedTime
            )
        );


        /*
         * 使用 0 Delta 只要求 Animator 立即評估目前指定 Pose，
         * 不會讓其他 Layer 額外前進時間。
         */
        viewModelAnimator.Update(
            0f
        );
    }


    #endregion
}
