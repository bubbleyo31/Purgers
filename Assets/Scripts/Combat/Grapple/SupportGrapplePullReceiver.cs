using Fusion;
using UnityEngine;


/// <summary>
/// Support Grapple Pull 的目標移動階段。
/// </summary>
public enum SupportGrapplePullPhase : byte
{
    /// <summary>
    /// 沒有被 Support Grapple 控制。
    ///
    /// Enemy 可以正常執行自己的 AI / Movement。
    /// </summary>
    Idle = 0,


    /// <summary>
    /// Support 勾索成功附著後的定身階段。
    ///
    /// Enemy 會被固定在目前位置。
    /// </summary>
    Frozen = 1,


    /// <summary>
    /// 定身結束後，
    /// Enemy 正在被拉向 Support 玩家目前視角前方。
    /// </summary>
    Pulling = 2
}


/// <summary>
/// 可以被 Support Grapple 拉動的 Enemy Receiver。
///
/// ====================================================================
///
/// 這支元件掛在：
///
/// Enemy NetworkObject Root。
///
/// 不掛在 Support Player。
///
/// ====================================================================
///
/// 正式流程：
///
/// Support Grapple
/// ↓
/// SupportEnemyDetected
/// ↓
/// SupportGrapplePullAbility
/// ↓
/// TryBeginPull()
/// ↓
///
/// Frozen 0.5 秒
/// ↓
/// Pulling 0.5 秒
/// ↓
/// Support 玩家目前視角前方
/// ↓
/// Idle。
///
/// ====================================================================
///
/// 最大重點：
///
/// 拉動目的地不是 Grapple 命中瞬間固定下來。
///
/// 每個 Fusion Tick 都重新讀取：
///
/// Support Player
/// ↓
/// PlayerMovement.GetAimDirection()
///
/// 所以：
///
/// 拉動途中玩家轉身
/// ↓
/// Enemy 目的地也跟著改變。
///
/// ====================================================================
///
/// 這支 Receiver 目前先處理 Enemy。
///
/// Player 之後會另外使用 KCC 版本，
/// 不會拿 Enemy 的 Rigidbody / Transform
/// 移動方式硬套在 Player 上。
/// </summary>
[DisallowMultipleComponent]
public class SupportGrapplePullReceiver :
    NetworkBehaviour
{
    // =====================================================================
    #region Timing


    [Header("Support Grapple Pull 時間")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Support 勾索正式附著 Enemy 後，Enemy 在開始被拉動以前會被完全定身多久，單位為秒。依目前設計預設為 0.5 秒。設為 0 代表附著後立刻開始拉動。")]
    private float enemyFreezeDuration =
        0.5f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Enemy 從定身結束到抵達 Support 玩家面前所使用的拉動時間，單位為秒。依目前設計預設為 0.5 秒。這是一段固定時長，不是固定速度。")]
    private float enemyPullDuration =
        0.5f;


    [SerializeField]
    [Tooltip("Enemy 拉向 Support 玩家時使用的時間曲線。X 代表拉動時間進度，Y 代表位置進度。預設 EaseInOut 可以避免開始與結束瞬間過於生硬。")]
    private AnimationCurve pullEase =
        AnimationCurve.EaseInOut(
            0f,
            0f,
            1f,
            1f
        );


    #endregion


    // =====================================================================
    #region Destination


    [Header("Support 面前目的地")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("計算 Enemy 最終目的地時，從 Support Player 的 KCC Target Position 往上增加多少高度。這不是 Camera Transform，因為 Camera 屬於本地 Presentation，不能拿來做正式網路 Gameplay。第一輪建議使用 1.1。")]
    private float destinationOriginHeight =
        1.1f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Enemy 最後停在 Support 玩家目前完整瞄準方向前方多少距離。此方向包含 Pitch，因此玩家抬頭或低頭時，Enemy 的目的地也會跟著上下改變。第一輪建議使用 2.25。")]
    private float destinationForwardDistance =
        2.25f;


    #endregion


    // =====================================================================
    #region Rigidbody


    [Header("Enemy Rigidbody 相容")]


    [SerializeField]
    [Tooltip("如果 Enemy NetworkObject Root 有 Rigidbody，Support Pull 優先使用 Rigidbody.MovePosition。沒有 Rigidbody 時才直接修改 Transform Position。一般物理 Enemy 建議保持開啟。")]
    private bool useRigidbodyWhenAvailable =
        true;


    /// <summary>
    /// Enemy Root Rigidbody。
    ///
    /// 可以不存在。
    /// </summary>
    private Rigidbody cachedRigidbody;


    /// <summary>
    /// Enemy Grapple Gameplay Target。
    ///
    /// 如果 Enemy 在拉動期間死亡，
    /// IsInteractionAvailable 變成 false，
    /// Pull 會立即停止。
    /// </summary>
    private GrappleInteractionTarget
        interactionTarget;


    #endregion


    // =====================================================================
    #region Fusion Lifecycle Guard


    /// <summary>
    /// 這顆 NetworkBehaviour
    /// 是否已經真正完成 Fusion Spawned。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不可以在 Spawned 前讀取：
///
/// CurrentPhase
/// SourcePlayerObject
/// PhaseTimer
///
/// 等 Networked Property。
/// </summary>
    private bool fusionSpawned;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後顯示 Support Pull 開始、Frozen、Pulling、完成、取消與失敗原因。測試階段建議保持開啟。")]
    private bool debugPull =
        true;


    [SerializeField]
    [Tooltip("開啟後在 Scene View 畫出 Support 玩家目前的動態拉取目的地。")]
    private bool debugDrawDestination =
        true;


    #endregion


    // =====================================================================
    #region Network State


    /// <summary>
    /// Enemy 目前 Support Pull 階段。
    /// </summary>
    [Networked]
    public SupportGrapplePullPhase CurrentPhase
    {
        get;
        private set;
    }


    /// <summary>
    /// 發動這次 Pull 的 Support Player NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// Receiver 每 Tick 會從這顆 Player
    /// 重新取得目前 KCC Position 與 Aim Direction。
    /// </summary>
    [Networked]
    private NetworkObject SourcePlayerObject
    {
        get;
        set;
    }


    /// <summary>
    /// 發動這次 Pull 的 PlayerRef。
    ///
    /// 目前主要提供 Debug，
    /// 未來也可以拿來做 Attribution。
    /// </summary>
    [Networked]
    public PlayerRef SourcePlayer
    {
        get;
        private set;
    }


    /// <summary>
    /// Frozen 階段鎖定的位置。
    ///
    /// Enemy 在整個 Frozen Duration
    /// 都會被維持在這個位置。
    /// </summary>
    [Networked]
    private Vector3 FrozenPosition
    {
        get;
        set;
    }


    /// <summary>
    /// 正式開始 Pulling 時
    /// Enemy 所在位置。
    ///
    /// 整段 Pull
    /// 會從這個位置插值到
    /// Support 當前面前。
    /// </summary>
    [Networked]
    private Vector3 PullStartPosition
    {
        get;
        set;
    }


    /// <summary>
    /// Frozen / Pulling 階段計時器。
    /// </summary>
    [Networked]
    private TickTimer PhaseTimer
    {
        get;
        set;
    }


    #endregion


    // =====================================================================
    #region Public State


    /// <summary>
    /// Enemy 是否正在被 Support Grapple 控制位置。
    /// </summary>
    public bool IsPullActive
    {
        get
        {
            if (fusionSpawned == false)
            {
                return false;
            }


            return
                CurrentPhase !=
                SupportGrapplePullPhase.Idle;
        }
    }


    /// <summary>
    /// Enemy 的正常 AI / Movement
    /// 目前是否應該暫停。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來正式 Enemy Movement Controller
    /// 可以直接讀這個值：
///
/// if (supportPullReceiver.BlocksNormalMovement)
///     return;
///
/// ------------------------------------------------------------
    /// </summary>
    public bool BlocksNormalMovement =>
        IsPullActive;


    #endregion


    // =====================================================================
    #region Unity


    private void Awake()
    {
        cachedRigidbody =
            GetComponent<Rigidbody>();


        interactionTarget =
            GetComponentInChildren<
                GrappleInteractionTarget
            >(
                true
            );


        if (interactionTarget == null)
        {
            interactionTarget =
                GetComponentInParent<
                    GrappleInteractionTarget
                >();
        }
    }


    #endregion


    // =====================================================================
    #region Fusion


    public override void Spawned()
    {
        /*
         * 一定要先設成 true。
         *
         * 從這一刻開始，
         * 才可以安全讀取這支腳本的
         * Networked Property。
         */
        fusionSpawned =
            true;


        if (Object.HasStateAuthority)
        {
            ResetPullState();
        }


        if (debugPull)
        {
            Debug.Log(
                $"[Support Grapple Pull Receiver] Spawned" +
                $"\nEnemy：{gameObject.name}" +
                $"\nNetworkObject：{Object.name}" +
                $"\nObject Valid：{Object.IsValid}" +
                $"\nState Authority：{Object.HasStateAuthority}",
                this
            );
        }
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        /*
         * Despawn 後不可以再讀 Networked Property。
         */
        fusionSpawned =
            false;
    }


    public override void FixedUpdateNetwork()
    {
        // =============================================================
        // Authority
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        // =============================================================
        // Idle
        // =============================================================

        if (IsPullActive == false)
        {
            return;
        }


        // =============================================================
        // Target 已經不能互動
        // =============================================================

        /*
         * 例如：
         *
         * Enemy 被擊殺。
         * Enemy 正在 Despawn。
         * Enemy Gameplay 禁止互動。
         */
        if (interactionTarget != null &&
            interactionTarget.IsInteractionAvailable ==
                false)
        {
            CancelPull(
                "Enemy 已不可互動"
            );


            return;
        }


        // =============================================================
        // Source Player 遺失
        // =============================================================

        if (SourcePlayerObject == null ||
            SourcePlayerObject.IsValid ==
                false)
        {
            CancelPull(
                "Support Player 已失效"
            );


            return;
        }


        // =============================================================
        // Phase
        // =============================================================

        switch (CurrentPhase)
        {
            case SupportGrapplePullPhase.Frozen:
            {
                TickFrozen();


                break;
            }


            case SupportGrapplePullPhase.Pulling:
            {
                TickPulling();


                break;
            }
        }
    }


    #endregion


    // =====================================================================
    #region Begin Pull


    /// <summary>
    /// 要求這名 Enemy
    /// 開始被指定 Support Player 拉動。
    ///
    /// ------------------------------------------------------------
    ///
    /// 必須由 Enemy 的 State Authority 呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 成功：
    ///
    /// Idle
    /// ↓
/// Frozen
///
/// 或 Freeze Duration = 0：
///
/// Idle
/// ↓
/// Pulling。
/// </summary>
    public bool TryBeginPull(
        NetworkObject sourcePlayerObject,
        PlayerRef sourcePlayer
    )
    {
        // =============================================================
        // Spawn Guard
        // =============================================================

        if (fusionSpawned == false)
        {
            LogRejected(
                "Receiver 尚未完成 Fusion Spawned。"
            );


            return false;
        }


        // =============================================================
        // Authority
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            LogRejected(
                "目前不是 Enemy State Authority。"
            );


            return false;
        }


        // =============================================================
        // Source
        // =============================================================

        if (sourcePlayerObject == null ||
            sourcePlayerObject.IsValid ==
                false)
        {
            LogRejected(
                "Source Support Player NetworkObject 無效。"
            );


            return false;
        }


        // =============================================================
        // Source Movement
        // =============================================================

        PlayerMovement sourceMovement =
            sourcePlayerObject
                .GetComponent<
                    PlayerMovement
                >();


        if (sourceMovement == null)
        {
            LogRejected(
                "Source Support Player 找不到 PlayerMovement。"
            );


            return false;
        }


        // =============================================================
        // 已經正在被拉
        // =============================================================

        if (IsPullActive)
        {
            LogRejected(
                "Enemy 已經正在被另一個 Support Pull 控制。"
            );


            return false;
        }


        // =============================================================
        // Interaction
        // =============================================================

        if (interactionTarget != null &&
            interactionTarget.IsInteractionAvailable ==
                false)
        {
            LogRejected(
                "Enemy 目前不可進行 Grapple Interaction。"
            );


            return false;
        }


        // =============================================================
        // Save Source
        // =============================================================

        SourcePlayerObject =
            sourcePlayerObject;


        SourcePlayer =
            sourcePlayer;


        // =============================================================
        // Freeze Position
        // =============================================================

        FrozenPosition =
            GetCurrentTargetPosition();


        /*
         * 開始時先把既有 Rigidbody Velocity 清掉，
         * 避免 Enemy 在 Frozen 的第一 Tick
         * 還沿著舊速度繼續滑。
         */
        StopRigidbodyVelocity();


        // =============================================================
        // Frozen
        // =============================================================

        if (enemyFreezeDuration > 0f)
        {
            CurrentPhase =
                SupportGrapplePullPhase.Frozen;


            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    enemyFreezeDuration
                );


            if (debugPull)
            {
                Debug.Log(
                    $"[Support Grapple Pull] Enemy 進入 Frozen。" +
                    $"\nEnemy：{gameObject.name}" +
                    $"\nSupport：{sourcePlayer}" +
                    $"\nFreeze Duration：{enemyFreezeDuration:F2}" +
                    $"\nFrozen Position：{FrozenPosition}",
                    this
                );
            }


            return true;
        }


        // =============================================================
        // No Freeze
        // =============================================================

        BeginPulling();


        return true;
    }


    #endregion


    // =====================================================================
    #region Frozen


    private void TickFrozen()
    {
        // =============================================================
        // 強制維持定身位置
        // =============================================================

        ApplyTargetPosition(
            FrozenPosition
        );


        StopRigidbodyVelocity();


        // =============================================================
        // Timer
        // =============================================================

        if (PhaseTimer.Expired(
                Runner
            ) == false)
        {
            return;
        }


        // =============================================================
        // Pull
        // =============================================================

        BeginPulling();
    }


    #endregion


    // =====================================================================
    #region Pulling


    private void BeginPulling()
    {
        CurrentPhase =
            SupportGrapplePullPhase.Pulling;


        PullStartPosition =
            GetCurrentTargetPosition();


        PhaseTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    0.01f,
                    enemyPullDuration
                )
            );


        StopRigidbodyVelocity();


        if (debugPull)
        {
            Debug.Log(
                $"[Support Grapple Pull] Enemy 開始 Pulling。" +
                $"\nEnemy：{gameObject.name}" +
                $"\nSupport：{SourcePlayer}" +
                $"\nPull Duration：{enemyPullDuration:F2}" +
                $"\nPull Start：{PullStartPosition}",
                this
            );
        }
    }


    private void TickPulling()
    {
        // =============================================================
        // 目前 Support 面前位置
        // =============================================================

        if (TryGetCurrentDestination(
                out Vector3 destination
            ) == false)
        {
            CancelPull(
                "無法取得 Support 當前面前位置。"
            );


            return;
        }


        // =============================================================
        // Progress
        // =============================================================

        float validDuration =
            Mathf.Max(
                0.01f,
                enemyPullDuration
            );


        float remaining =
            PhaseTimer
                .RemainingTime(
                    Runner
                ) ?? 0f;


        float progress =
            1f -
            Mathf.Clamp01(
                remaining /
                validDuration
            );


        // =============================================================
        // Ease
        // =============================================================

        float easedProgress =
            pullEase != null
                ? pullEase.Evaluate(
                    progress
                )
                : progress;


        easedProgress =
            Mathf.Clamp01(
                easedProgress
            );


        // =============================================================
        // ★ Dynamic Destination
        // =============================================================

        /*
         * 這裡故意每 Tick
         * 都重新使用 destination。
         *
         * ------------------------------------------------------------
         *
         * 所以如果：
         *
         * Pull 開始
         * ↓
         * Support 往右轉
         * ↓
         * destination 往右移
         * ↓
         * Enemy 路徑跟著改。
         *
         * ------------------------------------------------------------
         *
         * 最後一定是 Support
         * 「現在」的面前，
         *
         * 不是 0.5 秒以前的面前。
         */
        Vector3 targetPosition =
            Vector3.Lerp(
                PullStartPosition,
                destination,
                easedProgress
            );


        ApplyTargetPosition(
            targetPosition
        );


        StopRigidbodyVelocity();


        // =============================================================
        // Debug
        // =============================================================

        if (debugDrawDestination)
        {
            Debug.DrawLine(
                targetPosition,
                destination,
                Color.green,
                Runner.DeltaTime
            );


            Debug.DrawRay(
                destination,
                Vector3.up *
                0.5f,
                Color.green,
                Runner.DeltaTime
            );
        }


        // =============================================================
        // Finished
        // =============================================================

        if (PhaseTimer.Expired(
                Runner
            ) == false)
        {
            return;
        }


        /*
         * 最後再強制放到這一 Tick
         * 最新的 Destination。
         */
        ApplyTargetPosition(
            destination
        );


        CompletePull(
            destination
        );
    }


    #endregion


    // =====================================================================
    #region Dynamic Destination


    /// <summary>
    /// 取得 Support 玩家「現在」的面前位置。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不使用 Camera Transform。
    ///
    /// 因為 CameraRig / CamTarget 視覺更新
    /// 屬於本地 Presentation。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式 Gameplay 使用：
///
/// Player KCC TargetPosition
/// +
/// Aim Direction。
///
/// ------------------------------------------------------------
///
/// PlayerMovement.GetAimDirection()
/// 本身就是依 KCC Look Pitch / Yaw
/// 計算正式世界瞄準方向。
/// </summary>
    private bool TryGetCurrentDestination(
        out Vector3 destination
    )
    {
        destination =
            default;


        if (SourcePlayerObject == null ||
            SourcePlayerObject.IsValid ==
                false)
        {
            return false;
        }


        PlayerMovement sourceMovement =
            SourcePlayerObject
                .GetComponent<
                    PlayerMovement
                >();


        if (sourceMovement == null)
        {
            return false;
        }


        // =============================================================
        // Origin
        // =============================================================

        Vector3 origin;


        if (sourceMovement.KCC != null)
        {
            origin =
                sourceMovement
                    .KCC
                    .Data
                    .TargetPosition +
                Vector3.up *
                destinationOriginHeight;
        }
        else
        {
            origin =
                SourcePlayerObject
                    .transform
                    .position +
                Vector3.up *
                destinationOriginHeight;
        }


        // =============================================================
        // Aim Direction
        // =============================================================

        Vector3 aimDirection =
            sourceMovement
                .GetAimDirection();


        if (aimDirection.sqrMagnitude <=
            0.0001f)
        {
            aimDirection =
                SourcePlayerObject
                    .transform
                    .forward;
        }


        aimDirection.Normalize();


        // =============================================================
        // Destination
        // =============================================================

        destination =
            origin +
            aimDirection *
            destinationForwardDistance;


        return true;
    }


    #endregion


    // =====================================================================
    #region Position Apply


    private Vector3 GetCurrentTargetPosition()
    {
        if (cachedRigidbody != null &&
            useRigidbodyWhenAvailable)
        {
            return
                cachedRigidbody.position;
        }


        return
            transform.position;
    }


    /// <summary>
    /// 正式移動 Enemy。
    ///
    /// 有 Rigidbody：
    /// → MovePosition。
    ///
    /// 沒 Rigidbody：
    /// → Transform Position。
    /// </summary>
    private void ApplyTargetPosition(
        Vector3 targetPosition
    )
    {
        if (cachedRigidbody != null &&
            useRigidbodyWhenAvailable)
        {
            cachedRigidbody.MovePosition(
                targetPosition
            );


            return;
        }


        transform.position =
            targetPosition;
    }


    /// <summary>
    /// 將 Enemy Rigidbody 目前的線性速度與旋轉速度全部歸零。
    ///
    /// ------------------------------------------------------------
    ///
    /// Support Grapple 進入：
    ///
    /// Frozen
    /// Pulling
    ///
    /// 時都會呼叫這個函式。
    ///
    /// 目的：
    ///
    /// 防止 Enemy 原本正在：
    ///
    /// 移動
    /// 被擊飛
    /// 下墜
    /// 旋轉
    ///
    /// 導致 Support 拉取的位置又被 Rigidbody
    /// 原本的物理速度帶走。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// 此專案目前使用：
    ///
    /// Unity 2022.3.62f1
    ///
    /// 因此 Rigidbody 線性速度 API 是：
    ///
    /// velocity
    ///
    /// 不是 Unity 6 的：
    ///
    /// linearVelocity。
    /// </summary>
    private void StopRigidbodyVelocity()
    {
        if (cachedRigidbody == null)
        {
            return;
        }


        // =============================================================
        // 線性速度
        // =============================================================

        /*
        * Unity 2022.3 使用 velocity。
        *
        * 例如 Enemy 原本：
        *
        * 往前跑
        * 正在掉落
        * 被擊退
        *
        * 都會在這裡先歸零。
        */
        cachedRigidbody.velocity =
            Vector3.zero;


        // =============================================================
        // 旋轉速度
        // =============================================================

        /*
        * angularVelocity 在 Unity 2022.3
        * 一樣可以正常使用。
        */
        cachedRigidbody.angularVelocity =
            Vector3.zero;
    }


    #endregion


    // =====================================================================
    #region Complete / Cancel


    private void CompletePull(
        Vector3 finalDestination
    )
    {
        if (debugPull)
        {
            Debug.Log(
                $"[Support Grapple Pull] Enemy Pull 完成。" +
                $"\nEnemy：{gameObject.name}" +
                $"\nSupport：{SourcePlayer}" +
                $"\nFinal Destination：{finalDestination}",
                this
            );
        }


        ResetPullState();
    }


    /// <summary>
    /// 外部系統取消 Support Pull。
    ///
    /// 例如：
    ///
    /// Support 手動收繩。
    /// Source Player 切職業。
    /// Target 失效。
    /// </summary>
    public void CancelPull()
    {
        CancelPull(
            "外部系統取消"
        );
    }


    private void CancelPull(
        string reason
    )
    {
        if (fusionSpawned == false)
        {
            return;
        }


        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        if (debugPull)
        {
            Debug.Log(
                $"[Support Grapple Pull] Pull Cancelled。" +
                $"\nEnemy：{gameObject.name}" +
                $"\nReason：{reason}",
                this
            );
        }


        ResetPullState();
    }


    private void ResetPullState()
    {
        CurrentPhase =
            SupportGrapplePullPhase.Idle;


        SourcePlayerObject =
            null;


        SourcePlayer =
            PlayerRef.None;


        FrozenPosition =
            default;


        PullStartPosition =
            default;


        PhaseTimer =
            TickTimer.None;
    }


    #endregion


    // =====================================================================
    #region Debug Helper


    private void LogRejected(
        string reason
    )
    {
        if (debugPull == false)
        {
            return;
        }


        Debug.LogWarning(
            $"[Support Grapple Pull] TryBeginPull REJECTED。" +
            $"\nEnemy：{gameObject.name}" +
            $"\nReason：{reason}" +
            $"\nFusion Spawned：{fusionSpawned}" +
            $"\nObject：{(Object != null ? Object.name : "NULL")}",
            this
        );
    }


    #endregion
}