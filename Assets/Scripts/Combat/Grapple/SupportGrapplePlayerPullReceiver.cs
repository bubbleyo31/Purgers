using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;


/// <summary>
/// 被 Support Grapple 勾中的 Player KCC Pull Receiver。
///
/// ====================================================================
///
/// 這支腳本掛在：
///
/// Player Network Prefab Root。
///
/// 每一名玩家都必須有這顆 Receiver，
/// 因為任何玩家都有可能成為 Support Grapple 的拉取目標。
///
/// ====================================================================
///
/// Player 版本與 Enemy 版本不同：
///
/// Enemy：
///
/// Frozen 0.5 秒
/// ↓
/// Pulling 0.5 秒。
///
/// ------------------------------------------------------------
///
/// Player：
///
/// 不進 Frozen。
/// ↓
/// 直接 Pulling 0.5 秒。
///
/// ====================================================================
///
/// Player 移動不能使用：
///
/// transform.position
/// Rigidbody.MovePosition()
///
/// 因為 Player 使用 Photon Fusion Advanced KCC。
///
/// 所以這裡正式使用：
///
/// KCC.SetDynamicVelocity()
///
/// 控制被拉玩家。
///
/// ====================================================================
///
/// 拉取期間：
///
/// 1. WASD 暫時被壓到 0。
/// 2. Look 不封鎖。
/// 3. Jump 會由 Player Core 視為外部移動控制期間。
/// 4. 被拉玩家自己的 Grapple 會被取消。
/// 5. 每 Tick 重新計算 Support 當前視角前方。
///
/// 因此 Support 在拉人途中轉動視角，
/// 被拉玩家也會改變移動方向。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KCC))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerGrapple))]
public class SupportGrapplePlayerPullReceiver :
    NetworkBehaviour
{
    // =====================================================================
    #region Player Core References


    [Header("Player Core 引用")]


    [SerializeField]
    [Tooltip("這名被拉玩家使用的 Advanced KCC。所有 Support Player Pull 位移都會透過 KCC DynamicVelocity 完成，不會直接修改 Transform。若留空會自動取得。")]
    private KCC kcc;


    [SerializeField]
    [Tooltip("這名被拉玩家的 PlayerMovement。主要提供 KCC 與玩家移動資料。若留空會自動取得。")]
    private PlayerMovement movement;


    [SerializeField]
    [Tooltip("這名被拉玩家自己的 PlayerGrapple。如果玩家自己正處於 Grapple、Grapple Momentum 或其他勾索控制期間，被 Support 拉取時會先取消自己的 Grapple，避免兩套 KCC 外力互相搶控制。若留空會自動取得。")]
    private PlayerGrapple playerGrapple;


    #endregion


    // =====================================================================
    #region Pull Timing


    [Header("Player Pull 時間")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("被 Support 勾中的 Player 從目前位置被拉到 Support 面前所使用的時間，單位為秒。目前設計為 0.5 秒。這是一個目標到達時間，而不是固定移動速度，因此距離越遠時系統會自動產生越高的 KCC Pull Velocity。")]
    private float playerPullDuration =
        0.5f;


    #endregion


    // =====================================================================
    #region Destination


    [Header("Support 面前目的地")]


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("被拉玩家最後會停在 Support 玩家目前完整 Aim Direction 前方多少公尺。使用 KCC Root 對 KCC Root 的位置計算，因此水平瞄準時兩名玩家基本維持相同站立高度。第一輪建議 2.25。")]
    private float destinationForwardDistance =
        2.25f;


    [SerializeField]
    [Tooltip("在 Support 玩家 KCC Root 計算出的目標位置上額外增加多少 Y 軸高度。預設 0。只有你之後覺得被拉玩家停得太高或太低時才需要調整。")]
    private float destinationVerticalOffset =
        0f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("被拉玩家與目前動態目的地距離小於此數值時，視為已經抵達，可以提前結束 Pull。設為 0 代表一定等完整 Pull Duration。建議 0.15。")]
    private float completionDistance =
        0.15f;


    #endregion


    // =====================================================================
    #region Pull Velocity


    [Header("KCC Pull Velocity")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Support 拉取玩家時允許的最大 KCC DynamicVelocity。0 代表不限制，系統會盡可能確保玩家在 Player Pull Duration 內抵達目的地。如果之後擔心極遠距離產生過高速度，再設定例如 100 或 150。")]
    private float maximumPullSpeed =
        0f;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("被 Support 拉取期間保留多少普通 WASD 輸入。0 代表完全由 Support Pull 控制，1 代表玩家仍保留完整 WASD。依目前設計建議保持 0。Look 不受這個數值影響。")]
    private float movementInputInfluenceWhilePulled =
        0f;


    [SerializeField]
    [Tooltip("開啟後，被 Support 拉取完成或取消時，會把玩家目前 KCC DynamicVelocity 歸零，避免玩家抵達 Support 面前後繼續沿拉取速度飛出去。建議保持開啟。")]
    private bool clearVelocityWhenPullEnds =
        true;


    #endregion


    // =====================================================================
    #region Conflict Handling


    [Header("其他移動狀態處理")]


    [SerializeField]
    [Tooltip("開啟後，如果被拉玩家自己正在 Grapple、Grapple Attached 或 Grapple Momentum，Support Pull 開始前會先取消該玩家自己的勾索移動，避免兩套 KCC DynamicVelocity 同時控制玩家。建議保持開啟。")]
    private bool cancelTargetGrappleWhenPullStarts =
        true;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後顯示 Player Pull 開始、目前目的地、速度、完成與取消原因。測試階段建議保持開啟。")]
    private bool debugPlayerPull =
        true;


    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出目前被拉 Player 到 Support 動態目的地的線，以及目的地位置。")]
    private bool debugDrawPull =
        true;


    #endregion


    // =====================================================================
    #region Fusion State


    /// <summary>
    /// 目前是否正在被 Support Grapple 拉取。
    /// </summary>
    [Networked]
    private NetworkBool PullActive
    {
        get;
        set;
    }


    /// <summary>
    /// 發動本次 Grapple Pull
    /// 的 Support Player NetworkObject。
    /// </summary>
    [Networked]
    private NetworkObject SourcePlayerObject
    {
        get;
        set;
    }


    /// <summary>
    /// 發動本次拉取的 Support Player。
    ///
    /// 主要提供 Debug 與未來 Attribution。
    /// </summary>
    [Networked]
    public PlayerRef SourcePlayer
    {
        get;
        private set;
    }


    /// <summary>
    /// 整段 Player Pull 的剩餘時間。
    /// </summary>
    [Networked]
    private TickTimer PullTimer
    {
        get;
        set;
    }


    #endregion


    // =====================================================================
    #region Runtime State


    /// <summary>
    /// NetworkBehaviour 是否已真正完成 Spawned。
    ///
    /// 防止在 Spawned 前讀取 Networked Property，
    /// 避免之前 Receiver 曾發生過的：
    ///
    /// Networked properties can only be accessed when Spawned()
    /// has been called。
    /// </summary>
    private bool fusionSpawned;


    #endregion


    // =====================================================================
    #region Public State


    /// <summary>
    /// 玩家目前是否正在被 Support 拉取。
    ///
    /// Spawned 前永遠回傳 false。
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
                PullActive;
        }
    }


    /// <summary>
    /// 被拉期間 PlayerMovement
    /// 應保留多少 WASD 控制。
    ///
    /// ------------------------------------------------------------
    ///
    /// 非 Pull：
    ///
    /// 1。
    ///
    /// Pull：
    ///
    /// 使用 Inspector 的
    /// Movement Input Influence While Pulled。
    /// </summary>
    public float MovementInputInfluence
    {
        get
        {
            if (IsPullActive == false)
            {
                return
                    1f;
            }


            return Mathf.Clamp01(
                movementInputInfluenceWhilePulled
            );
        }
    }


    /// <summary>
    /// 是否有外部 Support Pull
    /// 正在正式控制玩家 KCC。
    ///
    /// Player Core 會使用這個狀態
    /// 避免普通 Jump 與外部拉取互相衝突。
    /// </summary>
    public bool BlocksNormalMovement =>
        IsPullActive;


    #endregion


    // =====================================================================
    #region Unity


    private void Awake()
    {
        if (kcc == null)
        {
            kcc =
                GetComponent<KCC>();
        }


        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }


        if (playerGrapple == null)
        {
            playerGrapple =
                GetComponent<PlayerGrapple>();
        }
    }


    #endregion


    // =====================================================================
    #region Fusion


    public override void Spawned()
    {
        fusionSpawned =
            true;


        if (Object.HasStateAuthority)
        {
            ResetPullState();
        }


        if (debugPlayerPull)
        {
            Debug.Log(
                $"[Support Player Pull Receiver] Spawned" +
                $"\nPlayer：{gameObject.name}" +
                $"\nInput Authority：{Object.InputAuthority}" +
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
        fusionSpawned =
            false;
    }


    #endregion


    // =====================================================================
    #region Begin Pull


    /// <summary>
    /// 要求這名 Player
    /// 開始被指定 Support Player 拉取。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// 這個方法只建立 Pull State。
    ///
    /// 真正每 Tick KCC 位移
    /// 由 Player.FixedUpdateNetwork()
    /// 明確呼叫 SimulatePull()。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這樣可以確保執行順序固定為：
    ///
    /// PlayerMovement
    /// ↓
    /// PlayerGrapple
    /// ↓
    /// Profession Runtime
    /// ↓
    /// Support Player Pull
    ///
    /// 最後由 Pull 覆寫 KCC DynamicVelocity。
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
                "目前不是被拉 Player 的 State Authority。"
            );


            return false;
        }


        // =============================================================
        // Target Core
        // =============================================================

        if (kcc == null ||
            movement == null)
        {
            LogRejected(
                "被拉 Player 缺少 KCC 或 PlayerMovement。"
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
        // 防止自己拉自己
        // =============================================================

        if (sourcePlayerObject ==
            Object)
        {
            LogRejected(
                "Support 不可以拉取自己。"
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


        if (sourceMovement == null ||
            sourceMovement.KCC == null)
        {
            LogRejected(
                "Source Support Player 找不到有效 PlayerMovement / KCC。"
            );


            return false;
        }


        // =============================================================
        // Already Pulled
        // =============================================================

        if (PullActive)
        {
            LogRejected(
                "這名 Player 已經正在被另一個 Support 拉取。"
            );


            return false;
        }


        // =============================================================
        // 取消被拉玩家自己的 Grapple
        // =============================================================

        if (cancelTargetGrappleWhenPullStarts &&
            playerGrapple != null)
        {
            bool targetHasGrappleMovement =
                playerGrapple.CurrentPhase !=
                    GrapplePhase.Idle ||
                playerGrapple
                    .IsReleaseMomentumActive;


            if (targetHasGrappleMovement)
            {
                /*
                 * 被拉玩家自己的 Grapple
                 * 必須先退出。
                 *
                 * --------------------------------------------------------
                 *
                 * 否則：
                 *
                 * Target PlayerGrapple
                 * → SetDynamicVelocity()
                 *
                 * Support Pull
                 * → SetDynamicVelocity()
                 *
                 * 兩套外力會互相搶。
                 */
                playerGrapple
                    .CancelFromSpecialAbility(
                        playRetractAnimation: true,
                        clearExistingMomentum: true
                    );
            }
        }


        // =============================================================
        // Save Source
        // =============================================================

        SourcePlayerObject =
            sourcePlayerObject;


        SourcePlayer =
            sourcePlayer;


        // =============================================================
        // Timer
        // =============================================================

        PullTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    0.01f,
                    playerPullDuration
                )
            );


        PullActive =
            true;


        // =============================================================
        // 清除舊移動
        // =============================================================

        /*
         * 開始被拉時，
         * 不保留玩家原本：
         *
         * Jump Velocity
         * Dash Velocity
         * Grapple Velocity
         * Falling Velocity。
         *
         * Pull 立即取得 KCC DynamicVelocity 控制權。
         */
        kcc.SetInputDirection(
            Vector3.zero
        );


        kcc.SetDynamicVelocity(
            Vector3.zero
        );


        // =============================================================
        // Debug
        // =============================================================

        if (debugPlayerPull)
        {
            Debug.Log(
                $"[Support Player Pull] Pull Started。" +
                $"\nTarget Player：{Object.InputAuthority}" +
                $"\nSupport Player：{sourcePlayer}" +
                $"\nDuration：{playerPullDuration:F2}" +
                $"\nForward Distance：{destinationForwardDistance:F2}" +
                $"\nWASD Influence：{MovementInputInfluence:F2}",
                this
            );
        }


        return true;
    }


    #endregion


    // =====================================================================
    #region Simulation


    /// <summary>
    /// 每個 Fusion Tick
    /// 由被拉玩家自己的 Player Core 呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不使用這支 NetworkBehaviour
    /// 自己的 FixedUpdateNetwork，
    ///
    /// 原因是我們必須確保：
    ///
    /// PlayerMovement
    /// PlayerGrapple
    /// Profession Runtime
    ///
    /// 全部執行完成後，
    ///
    /// Support Pull 才最後寫入 KCC DynamicVelocity。
    /// </summary>
    public void SimulatePull()
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
        // Active
        // =============================================================

        if (IsPullActive == false)
        {
            return;
        }


        // =============================================================
        // Target KCC
        // =============================================================

        if (kcc == null)
        {
            CancelPullInternal(
                "Target KCC 遺失。"
            );


            return;
        }


        // =============================================================
        // Source Lost
        // =============================================================

        if (SourcePlayerObject == null ||
            SourcePlayerObject.IsValid ==
                false)
        {
            CancelPullInternal(
                "Support Player 已失效。"
            );


            return;
        }


        // =============================================================
        // Dynamic Destination
        // =============================================================

        if (TryGetCurrentDestination(
                out Vector3 destination
            ) == false)
        {
            CancelPullInternal(
                "無法取得 Support 當前面前位置。"
            );


            return;
        }


        Vector3 currentPosition =
            kcc.Data.TargetPosition;


        Vector3 toDestination =
            destination -
            currentPosition;


        float distance =
            toDestination.magnitude;


        // =============================================================
        // Already Arrived
        // =============================================================

        if (completionDistance > 0f &&
            distance <=
                completionDistance)
        {
            CompletePull(
                "已進入 Completion Distance"
            );


            return;
        }


        // =============================================================
        // Time
        // =============================================================

        float remainingTime =
            PullTimer
                .RemainingTime(
                    Runner
                ) ?? 0f;


        if (remainingTime <=
            0f)
        {
            /*
             * 不使用 KCC Teleport
             * 硬把玩家穿牆塞到目的地。
             *
             * --------------------------------------------------------
             *
             * 如果中間真的被牆壁阻擋，
             * 0.5 秒結束後就停在 KCC
             * 實際允許抵達的位置。
             */
            CompletePull(
                "Pull Duration 已結束"
            );


            return;
        }


        // =============================================================
        // Required Velocity
        // =============================================================

        /*
         * 使用：
         *
         * 剩餘距離
         * ÷
         * 剩餘時間
         *
         * ------------------------------------------------------------
         *
         * 目的：
         *
         * 即使 Support：
         *
         * 移動
         * 轉頭
         * 抬頭
         * 下降
         *
         * 目的地每 Tick 改變，
         *
         * 被拉玩家仍會重新計算
         *「要在剩餘時間內追上新目的地」
         * 所需要的速度。
         */
        float denominator =
            Mathf.Max(
                Runner.DeltaTime,
                remainingTime
            );


        Vector3 desiredVelocity =
            toDestination /
            denominator;


        // =============================================================
        // Optional Maximum Speed
        // =============================================================

        if (maximumPullSpeed > 0f)
        {
            float speed =
                desiredVelocity.magnitude;


            if (speed >
                maximumPullSpeed)
            {
                desiredVelocity =
                    desiredVelocity.normalized *
                    maximumPullSpeed;
            }
        }


        // =============================================================
        // ★ KCC Forced Movement
        // =============================================================

        /*
         * 先清掉 Input Direction。
         *
         * 雖然 Player Core 已經會把
         * MovementInputInfluence 壓低，
         * 這裡再做一次保險。
         */
        kcc.SetInputDirection(
            Vector3.zero
        );


        /*
         * 真正拉動玩家。
         *
         * KCC 仍然會處理自己的碰撞，
         * 所以我們不直接：
         *
         * transform.position = ...
         *
         * 也不 Teleport。
         */
        kcc.SetDynamicVelocity(
            desiredVelocity
        );


        // =============================================================
        // Debug Draw
        // =============================================================

        if (debugDrawPull)
        {
            Debug.DrawLine(
                currentPosition,
                destination,
                Color.cyan,
                Runner.DeltaTime
            );


            Debug.DrawRay(
                destination,
                Vector3.up *
                0.5f,
                Color.cyan,
                Runner.DeltaTime
            );
        }
    }


    #endregion


    // =====================================================================
    #region Destination


    /// <summary>
    /// 取得 Support 玩家「現在」的 KCC 前方位置。
    ///
    /// ====================================================================
    ///
    /// 使用：
///
/// Source KCC TargetPosition
/// +
/// Source PlayerMovement.GetAimDirection()
/// ×
/// Forward Distance。
///
/// ====================================================================
///
/// 注意：
///
/// 這裡故意使用 KCC Gameplay Aim，
/// 不使用 Camera Transform。
///
/// 因此 Host / Client
/// 會使用相同正式 Gameplay 資料。
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


        if (sourceMovement == null ||
            sourceMovement.KCC == null)
        {
            return false;
        }


        // =============================================================
        // Source Position
        // =============================================================

        Vector3 sourcePosition =
            sourceMovement
                .KCC
                .Data
                .TargetPosition;


        // =============================================================
        // Current Aim Direction
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
        // Final Destination
        // =============================================================

        destination =
            sourcePosition +
            aimDirection *
            destinationForwardDistance +
            Vector3.up *
            destinationVerticalOffset;


        return true;
    }


    #endregion


    // =====================================================================
    #region Complete / Cancel


    /// <summary>
    /// Player Pull 正常完成。
    /// </summary>
    private void CompletePull(
        string reason
    )
    {
        if (debugPlayerPull)
        {
            Debug.Log(
                $"[Support Player Pull] Pull Completed。" +
                $"\nTarget：{Object.InputAuthority}" +
                $"\nSupport：{SourcePlayer}" +
                $"\nReason：{reason}" +
                $"\nFinal Position：{kcc.Data.TargetPosition}",
                this
            );
        }


        StopForcedVelocity();


        ResetPullState();
    }


    /// <summary>
    /// 外部系統取消 Player Pull。
    ///
    /// 例如：
///
/// Support 自己切斷繩索。
/// Support 切職業。
/// Source Player Despawn。
/// </summary>
    public void CancelPull()
    {
        CancelPullInternal(
            "外部系統取消"
        );
    }


    private void CancelPullInternal(
        string reason
    )
    {
        if (fusionSpawned == false)
        {
            return;
        }


        if (Object == null ||
            Object.HasStateAuthority ==
                false)
        {
            return;
        }


        if (debugPlayerPull)
        {
            Debug.Log(
                $"[Support Player Pull] Pull Cancelled。" +
                $"\nTarget：{Object.InputAuthority}" +
                $"\nReason：{reason}",
                this
            );
        }


        StopForcedVelocity();


        ResetPullState();
    }


    /// <summary>
    /// 拉取結束時移除 Support Pull
    /// 留下的 DynamicVelocity。
    /// </summary>
    private void StopForcedVelocity()
    {
        if (kcc == null)
        {
            return;
        }


        kcc.SetInputDirection(
            Vector3.zero
        );


        if (clearVelocityWhenPullEnds)
        {
            kcc.SetDynamicVelocity(
                Vector3.zero
            );
        }
    }


    private void ResetPullState()
    {
        PullActive =
            false;


        SourcePlayerObject =
            null;


        SourcePlayer =
            PlayerRef.None;


        PullTimer =
            TickTimer.None;
    }


    #endregion


    // =====================================================================
    #region Debug


    private void LogRejected(
        string reason
    )
    {
        if (debugPlayerPull == false)
        {
            return;
        }


        Debug.LogWarning(
            $"[Support Player Pull] TryBeginPull REJECTED。" +
            $"\nTarget：" +
            $"{(Object != null ? Object.InputAuthority.ToString() : "NULL")}" +
            $"\nReason：{reason}" +
            $"\nFusion Spawned：{fusionSpawned}",
            this
        );
    }


    #endregion
}