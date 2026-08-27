using Fusion;
using UnityEngine;

/// <summary>
/// Enemy 被 Tank Grapple Gather 拉動時的狀態。
/// </summary>
public enum TankGatherMovementPhase : byte
{
    /// <summary>
    /// 沒有被 Tank Gather 控制。
    /// Enemy 可以正常執行自己的 AI / Movement。
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 正在快速往 Gather Anchor A 靠攏。
    /// </summary>
    Gathering = 1
}


/// <summary>
/// 可以被 Tank Grapple Gather 移動的 Enemy 接收器。
///
/// ====================================================================
///
/// 這支元件屬於：
///
/// Enemy。
///
/// 不是 Tank Player。
///
/// ====================================================================
///
/// 正式流程：
///
/// Tank 勾中 Enemy A
/// ↓
/// TankGrappleGatherAbility
/// ↓
/// 選出 B / C / D
/// ↓
///
/// B.TankGatherMovementReceiver.TryBeginGather(A)
/// C.TankGatherMovementReceiver.TryBeginGather(A)
/// D.TankGatherMovementReceiver.TryBeginGather(A)
///
/// ↓
///
/// 每隻 Enemy 自己在 FixedUpdateNetwork
/// 往 A 周圍移動。
///
/// ====================================================================
///
/// 為什麼不讓 Tank Ability 直接移動 Enemy？
///
/// 因為未來 Enemy 可能有：
///
/// Ground AI
/// Flying AI
/// Rigidbody
/// Boss
/// Elite
/// Knockback
/// Stun
/// Root Motion。
///
/// 把「被外力拉動」的權限留在 Enemy 自己身上，
/// 未來比較容易和 AI Movement 整合。
///
/// ====================================================================
///
/// 注意：
///
/// GrappleInteractionTarget.CanBeTankGathered
///
/// 仍然負責：
///
/// 「這種 Enemy 能不能被 Gather？」
///
/// 而這支 Receiver 負責：
///
/// 「如果可以，那要怎麼移動？」
/// </summary>
[DisallowMultipleComponent]
public class TankGatherMovementReceiver :
    NetworkBehaviour
{
    // =====================================================================
    #region Gather Movement

    [Header("Tank Gather 位移")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Enemy 被 Tank Gather 拉向 Anchor A 時的移動速度，單位為每秒 Unity 世界單位。這不是瞬間 Teleport，而是在 Fusion Tick 中快速移動。第一輪建議先使用 14。")]
    private float gatherMoveSpeed =
        14f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("被拉過來的 Enemy 最後會停在 Anchor A 外圍多少距離。不要設成 0，否則 B、C、D 全部會硬塞進 A 的中心，Collider 很容易互相擠壓。第一輪建議使用 1.5。")]
    private float gatherStopDistance =
        1.5f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Enemy 距離計算後的 Gather Destination 小於這個數值時，視為已成功聚集並結束外部移動控制。建議先使用 0.15。")]
    private float gatherArrivalDistance =
        0.15f;


    [SerializeField]
    [Min(0.05f)]
    [Tooltip("一次 Gather 最多允許持續多久，單位為秒。這是一層安全保護，如果 Enemy 被牆壁、Collider 或其他 Movement System 卡住，不會永遠保持 Gathering。第一輪建議使用 0.6 秒。")]
    private float maximumGatherDuration =
        0.6f;


    [SerializeField]
    [Tooltip("開啟後，Gather 只會修改 Enemy 的水平 XZ 位置，不會把地面 Enemy 往上或往下拉。一般地面怪建議開啟。未來飛行怪如果允許被聚集，可以在它自己的 Prefab 關閉。")]
    private bool preserveCurrentHeight =
        true;

    #endregion


    // =====================================================================
    #region 重複 Gather

    [Header("重複 Gather 控制")]

    [SerializeField]
    [Tooltip("開啟後，Enemy 已經正在被另一個 Tank Gather 拉動時，可以被新的 Gather 重新指定 Anchor。第一版建議關閉，避免兩個 Tank 同時把同一隻怪往不同方向拉。")]
    private bool allowRetargetWhileGathering =
        false;

    #endregion


    // =====================================================================
    #region Rigidbody

    [Header("Rigidbody 相容設定")]

    [SerializeField]
    [Tooltip("如果 Enemy Root 上有 Rigidbody，Gather 時優先使用 Rigidbody.MovePosition 而不是直接修改 Transform。這能比較安全地與物理 Enemy 共存。沒有 Rigidbody 的 Enemy 會自動使用 Transform 位移。")]
    private bool useRigidbodyWhenAvailable =
        true;

    /// <summary>
    /// Enemy Root Rigidbody。
    ///
    /// 可以不存在。
    /// </summary>
    private Rigidbody cachedRigidbody;

    /// <summary>
    /// Gameplay Target。
    ///
    /// 用來確認 Enemy 在 Gather 過程中
    /// 是否死亡或變成不可互動。
    /// </summary>
    private GrappleInteractionTarget
        interactionTarget;

    #endregion


    /// <summary>
    /// 這顆 TankGatherMovementReceiver
    /// 是否已經正式經過 Fusion Spawned。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這不是 Networked State。
    ///
    /// 它只是本機生命週期保護，
    /// 防止 NetworkBehaviour 尚未 Attached 時
    /// 提前讀取 [Networked] Property。
    /// </summary>
    private bool fusionSpawned;

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示 Gather 開始、完成、Timeout、Anchor 消失等資訊。測試階段建議開啟。")]
    private bool debugGatherMovement =
        true;


    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出 Enemy 目前被拉往的 Destination。")]
    private bool debugDrawMovement =
        true;

    #endregion


    // =====================================================================
    #region Network State

    /// <summary>
    /// 目前 Gather Movement 階段。
    /// </summary>
    [Networked]
    public TankGatherMovementPhase CurrentPhase
    {
        get;
        private set;
    }


    /// <summary>
    /// 目前 Gather 的 Anchor A。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如果 A 在 Gather 期間移動，
    /// B / C / D 會繼續追蹤 A 的新位置。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如果 A 中途 Despawn，
    /// 則使用 GatherAnchorSnapshot
    /// 作為最後位置備援。
    /// </summary>
    [Networked]
    private NetworkObject GatherAnchorObject
    {
        get;
        set;
    }


    /// <summary>
    /// 最近一次合法的 Anchor A 世界位置。
    ///
    /// A 如果中途消失，
    /// Gather 還能往最後已知位置完成。
    /// </summary>
    [Networked]
    private Vector3 GatherAnchorSnapshot
    {
        get;
        set;
    }


    /// <summary>
    /// Enemy 開始 Gather 時，
    /// 從 A 指向這名 Enemy 的方向。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    ///      B
    ///      ↑
    /// C ←  A  → D
    ///
    /// B 會記住 Up Side，
    /// C 記住 Left Side，
    /// D 記住 Right Side。
    ///
    /// ------------------------------------------------------------
    ///
    /// 最後不是全部移到 A 正中心，
    /// 而是停在：
    ///
    /// A + RadialDirection × StopDistance。
    ///
    /// 這樣 B/C/D 會聚在 A 周圍，
    /// 而不是完全疊在一起。
    /// </summary>
    [Networked]
    private Vector3 GatherRadialDirection
    {
        get;
        set;
    }


    /// <summary>
    /// 這次 Gather 是哪一名 Tank 玩家造成的。
    ///
    /// 目前主要方便 Debug，
    /// 未來如果需要 Attribution 也已經保留。
    /// </summary>
    [Networked]
    public PlayerRef GatherSourcePlayer
    {
        get;
        private set;
    }


    /// <summary>
    /// Gather 安全 Timeout。
    /// </summary>
    [Networked]
    private TickTimer GatherTimeoutTimer
    {
        get;
        set;
    }

    #endregion


    // =====================================================================
    #region Public State

    /// <summary>
    /// Enemy 是否正被 Tank Gather 控制位置。
    ///
    /// ------------------------------------------------------------
    ///
    /// NetworkBehaviour 尚未完成 Spawned 時，
    /// 一律視為沒有 Gathering。
    ///
    /// 這可以防止任何外部系統
    /// 在 Fusion 尚未 Attached 以前
    /// 不小心讀取 CurrentPhase。
    /// </summary>
    public bool IsBeingGathered
    {
        get
        {
            if (fusionSpawned == false)
            {
                return false;
            }

            return
                CurrentPhase ==
                TankGatherMovementPhase.Gathering;
        }
    }

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
            >();

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
        * ★ 一定先設 true。
        *
        * 從這一刻開始，
        * 才允許讀取這支 NetworkBehaviour
        * 裡面的 [Networked] Property。
        */
        fusionSpawned =
            true;

        if (Object.HasStateAuthority)
        {
            ResetGatherState();
        }

        if (debugGatherMovement)
        {
            Debug.Log(
                $"[Tank Gather Movement] Receiver Spawned" +
                $"\nEnemy：{gameObject.name}" +
                $"\nNetworkObject：{Object.name}" +
                $"\nObject Valid：{Object.IsValid}" +
                $"\nHas State Authority：{Object.HasStateAuthority}" +
                $"\nState Authority：{Object.StateAuthority}",
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
        * Despawn 之後不能再碰：
        *
        * CurrentPhase
        * GatherAnchorObject
        * GatherSourcePlayer
        *
        * 等任何 [Networked] Property。
        */
        fusionSpawned =
            false;
    }

    public override void FixedUpdateNetwork()
    {
        /*
         * Enemy 位置屬於 Gameplay State。
         *
         * 正式 Gather Movement
         * 只由這顆 Enemy 的 State Authority 執行。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (IsBeingGathered == false)
        {
            return;
        }

        TickGatherMovement();
    }

    #endregion


    // =====================================================================
    #region Begin Gather

    /// <summary>
    /// 要求這名 Enemy 開始被拉向 Anchor A。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個版本除了正式檢查之外，
    /// 會把每一個「拒絕 Gather」的真正原因
    /// 明確印到 Console。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前我們正在追查：
    ///
    /// TankGrappleGatherAbility
    /// 已成功找到合法 B / C / D，
    /// 但是 TryBeginGather() 全部回傳 false。
    ///
    /// 因此這裡暫時保留詳細 Debug，
    /// 等真正原因確認後再精簡。
    /// </summary>
    public bool TryBeginGather(
        NetworkObject anchorObject,
        PlayerRef sourcePlayer
    )
    {
        // =============================================================
        // 1. NetworkBehaviour / State Authority
        // =============================================================

        /*
        * Gather 會真正修改 Enemy 的 Gameplay Position，
        * 所以只能由這隻 Enemy 的 State Authority
        * 正式修改 Networked State。
        *
        * ------------------------------------------------------------
        *
        * 目前這是最需要確認的一個條件。
        */
        if (Object == null)
        {
            return LogGatherRejected(
                "Receiver 的 NetworkBehaviour.Object = NULL。" +
                "\n可能代表 TankGatherMovementReceiver 沒有正常隸屬於已 Spawn 的 Enemy NetworkObject。",
                anchorObject,
                sourcePlayer
            );
        }

        if (Object.HasStateAuthority == false)
        {
            return LogGatherRejected(
                "這台 Peer 沒有這隻 Enemy 的 State Authority。" +
                $"\nEnemy State Authority：{Object.StateAuthority}" +
                $"\nEnemy Input Authority：{Object.InputAuthority}",
                anchorObject,
                sourcePlayer
            );
        }

        // =============================================================
        // 2. Anchor
        // =============================================================

        if (anchorObject == null)
        {
            return LogGatherRejected(
                "Anchor NetworkObject = NULL。",
                anchorObject,
                sourcePlayer
            );
        }

        if (anchorObject.IsValid == false)
        {
            return LogGatherRejected(
                "Anchor NetworkObject 已經無效或 Despawn。",
                anchorObject,
                sourcePlayer
            );
        }

        // =============================================================
        // 3. 不能拉 Anchor 自己
        // =============================================================

        if (anchorObject ==
            Object)
        {
            return LogGatherRejected(
                "Candidate Enemy 就是 Anchor A 自己。",
                anchorObject,
                sourcePlayer
            );
        }

        // =============================================================
        // 4. Enemy Availability
        // =============================================================

        /*
        * 例如：
        *
        * Enemy 已死亡
        * Enemy 已禁用互動
        *
        * 都不允許開始 Gather。
        */
        if (interactionTarget != null &&
            interactionTarget.IsInteractionAvailable ==
            false)
        {
            return LogGatherRejected(
                "Enemy 的 GrappleInteractionTarget.IsInteractionAvailable = false。" +
                $"\nTarget：{interactionTarget.name}",
                anchorObject,
                sourcePlayer
            );
        }

        // =============================================================
        // 5. 已經被其他 Gather 控制
        // =============================================================

        if (IsBeingGathered &&
            allowRetargetWhileGathering == false)
        {
            return LogGatherRejected(
                "Enemy 已經處於 Gathering，且 Allow Retarget While Gathering = false。" +
                $"\n目前 Anchor：" +
                $"{(GatherAnchorObject != null ? GatherAnchorObject.name : "NULL")}",
                anchorObject,
                sourcePlayer
            );
        }

        // =============================================================
        // 6. Anchor Position
        // =============================================================

        Vector3 anchorPosition =
            anchorObject
                .transform
                .position;

        Vector3 enemyPosition =
            transform.position;

        // =============================================================
        // 7. 計算 Enemy 在 A 周圍的方向
        // =============================================================

        Vector3 radialDirection =
            enemyPosition -
            anchorPosition;

        if (preserveCurrentHeight)
        {
            radialDirection.y =
                0f;
        }

        /*
        * Candidate 剛好與 A 完全重疊時，
        * 使用固定方向作為 fallback。
        *
        * 不使用 Random，
        * 避免多人模擬結果不一致。
        */
        if (radialDirection.sqrMagnitude <=
            0.0001f)
        {
            radialDirection =
                Vector3.right;
        }

        radialDirection.Normalize();

        // =============================================================
        // 8. 正式建立 Gather State
        // =============================================================

        GatherAnchorObject =
            anchorObject;

        GatherAnchorSnapshot =
            anchorPosition;

        GatherRadialDirection =
            radialDirection;

        GatherSourcePlayer =
            sourcePlayer;

        CurrentPhase =
            TankGatherMovementPhase.Gathering;

        GatherTimeoutTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    0.05f,
                    maximumGatherDuration
                )
            );

        // =============================================================
        // 9. 成功 Debug
        // =============================================================

        if (debugGatherMovement)
        {
            Vector3 destination =
                CalculateGatherDestination(
                    anchorPosition
                );

            Debug.Log(
                $"[Tank Gather Movement] Gather ACCEPTED" +
                $"\nEnemy：{gameObject.name}" +
                $"\nEnemy Object Valid：{Object.IsValid}" +
                $"\nHas State Authority：{Object.HasStateAuthority}" +
                $"\nState Authority：{Object.StateAuthority}" +
                $"\nAnchor：{anchorObject.name}" +
                $"\nSource Player：{sourcePlayer}" +
                $"\nStart：{enemyPosition}" +
                $"\nDestination：{destination}" +
                $"\nSpeed：{gatherMoveSpeed:F2}" +
                $"\nStop Distance：{gatherStopDistance:F2}",
                this
            );
        }

        return true;
    }

    #endregion


    // =====================================================================
    #region Gather Tick

    /// <summary>
    /// 每個 Fusion Tick
    /// 把這名 Enemy 拉向 A 周圍。
    /// </summary>
    private void TickGatherMovement()
    {
        // =============================================================
        // Enemy 中途死亡 / 不可互動
        // =============================================================

        if (interactionTarget != null &&
            interactionTarget
                .IsInteractionAvailable ==
            false)
        {
            CompleteGather(
                "Target Unavailable"
            );

            return;
        }

        // =============================================================
        // Timeout
        // =============================================================

        if (GatherTimeoutTimer
            .Expired(Runner))
        {
            CompleteGather(
                "Timeout"
            );

            return;
        }

        // =============================================================
        // Anchor
        // =============================================================

        Vector3 anchorPosition =
            GatherAnchorSnapshot;

        /*
         * A 還存在：
         *
         * 每 Tick 更新 Anchor 位置。
         *
         * 這樣 A 如果因 Knockback、
         * AI、其他能力稍微移動，
         * B/C/D 仍然會往新的 A 靠攏。
         */
        if (GatherAnchorObject != null &&
            GatherAnchorObject.IsValid)
        {
            anchorPosition =
                GatherAnchorObject
                    .transform
                    .position;

            GatherAnchorSnapshot =
                anchorPosition;
        }

        // =============================================================
        // Destination
        // =============================================================

        Vector3 currentPosition =
            transform.position;

        Vector3 destination =
            CalculateGatherDestination(
                anchorPosition
            );

        /*
         * 地面 Enemy：
         *
         * 不改變原本高度。
         */
        if (preserveCurrentHeight)
        {
            destination.y =
                currentPosition.y;
        }

        Vector3 toDestination =
            destination -
            currentPosition;

        float remainingDistance =
            toDestination.magnitude;

        // =============================================================
        // Arrived
        // =============================================================

        if (remainingDistance <=
            gatherArrivalDistance)
        {
            CompleteGather(
                "Arrived"
            );

            return;
        }

        // =============================================================
        // Move
        // =============================================================

        float moveDistance =
            Mathf.Max(
                0.1f,
                gatherMoveSpeed
            ) *
            Runner.DeltaTime;

        Vector3 nextPosition =
            Vector3.MoveTowards(
                currentPosition,
                destination,
                moveDistance
            );

        ApplyGatherPosition(
            nextPosition
        );

        // =============================================================
        // Scene Debug
        // =============================================================

        if (debugDrawMovement)
        {
            Debug.DrawLine(
                currentPosition,
                destination,
                Color.magenta,
                Runner.DeltaTime * 2f
            );
        }
    }

    #endregion


    // =====================================================================
    #region Destination

    /// <summary>
    /// 計算這名 Enemy 最後應該停在 A 周圍哪裡。
    ///
    /// ------------------------------------------------------------
    ///
    /// Destination：
    ///
    /// Anchor
    /// +
    /// 初始 Radial Direction
    /// ×
    /// Stop Distance。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這樣：
    ///
    /// B
    /// C
    /// D
    ///
    /// 會保留各自原本位於 A 哪一側，
    /// 但全部被收縮到 A 周圍。
    /// </summary>
    private Vector3 CalculateGatherDestination(
        Vector3 anchorPosition
    )
    {
        Vector3 radialDirection =
            GatherRadialDirection;

        if (radialDirection.sqrMagnitude <=
            0.0001f)
        {
            radialDirection =
                Vector3.right;
        }

        radialDirection.Normalize();

        return
            anchorPosition +
            radialDirection *
            Mathf.Max(
                0f,
                gatherStopDistance
            );
    }

    #endregion


    // =====================================================================
    #region Apply Position

    /// <summary>
    /// 真正套用這個 Tick 的 Enemy 位置。
    ///
    /// ------------------------------------------------------------
    ///
    /// Rigidbody Enemy：
    /// 優先 MovePosition。
    ///
    /// 非 Rigidbody Enemy：
    /// 修改 Root Transform。
    ///
    /// ------------------------------------------------------------
    ///
    /// 之後如果某種 Enemy 使用：
    ///
    /// NavMeshAgent
    /// 自訂 KCC
    /// Flying Controller
    ///
    /// 可以再把這一層抽成
    /// Enemy Movement Driver，
    /// 上面的 Gather Gameplay 不需要重寫。
    /// </summary>
    private void ApplyGatherPosition(
        Vector3 nextPosition
    )
    {
        if (useRigidbodyWhenAvailable &&
            cachedRigidbody != null)
        {
            /*
             * Dynamic Rigidbody 如果還保留原速度，
             * 會在 Gather MovePosition 之外繼續漂移。
             *
             * 所以 Gather 控制期間
             * 先清掉線速度。
             */
            if (cachedRigidbody.isKinematic ==
                false)
            {
                cachedRigidbody.velocity =
                    Vector3.zero;
            }

            cachedRigidbody.MovePosition(
                nextPosition
            );

            return;
        }

        transform.position =
            nextPosition;
    }

    #endregion


    // =====================================================================
    #region Complete

    /// <summary>
    /// 結束這次 Enemy Gather Movement。
    /// </summary>
    private void CompleteGather(
        string reason
    )
    {
        if (IsBeingGathered == false)
        {
            return;
        }

        if (debugGatherMovement)
        {
            Debug.Log(
                $"[Tank Gather Movement] Gather Completed" +
                $"\nEnemy：{gameObject.name}" +
                $"\nReason：{reason}" +
                $"\nFinal Position：{transform.position}",
                this
            );
        }

        ResetGatherState();
    }


    private void ResetGatherState()
    {
        CurrentPhase =
            TankGatherMovementPhase.Idle;

        GatherAnchorObject =
            null;

        GatherAnchorSnapshot =
            Vector3.zero;

        GatherRadialDirection =
            Vector3.zero;

        GatherSourcePlayer =
            PlayerRef.None;

        GatherTimeoutTimer =
            TickTimer.None;
    }
    
    #endregion

    /// <summary>
    /// 統一輸出 Tank Gather 被拒絕的真正原因。
    ///
    /// ------------------------------------------------------------
    ///
    /// 一律回傳 false，
    /// 所以可以直接使用：
    ///
    /// return LogGatherRejected(...);
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個 Debug 對多人很重要，
    /// 因為「Candidate 合法」
    /// 不代表目前這台 Peer
    /// 一定具有 Candidate Enemy 的 State Authority。
    /// </summary>
    private bool LogGatherRejected(
        string reason,
        NetworkObject anchorObject,
        PlayerRef sourcePlayer
    )
    {
        if (debugGatherMovement)
        {
            Debug.LogWarning(
                $"[Tank Gather Movement] Gather REJECTED" +
                $"\nEnemy：{gameObject.name}" +
                $"\nReason：{reason}" +
                $"\nReceiver Object：" +
                $"{(Object != null ? Object.name : "NULL")}" +
                $"\nObject Valid：" +
                $"{(Object != null && Object.IsValid)}" +
                $"\nHas State Authority：" +
                $"{(Object != null && Object.HasStateAuthority)}" +
                $"\nState Authority：" +
                $"{(Object != null ? Object.StateAuthority.ToString() : "無")}" +
                $"\nInput Authority：" +
                $"{(Object != null ? Object.InputAuthority.ToString() : "無")}" +
                $"\nAnchor：" +
                $"{(anchorObject != null ? anchorObject.name : "NULL")}" +
                $"\nAnchor Valid：" +
                $"{(anchorObject != null && anchorObject.IsValid)}" +
                $"\nIs Being Gathered：{IsBeingGathered}" +
                $"\nAllow Retarget：{allowRetargetWhileGathering}" +
                $"\nInteraction Target：" +
                $"{(interactionTarget != null ? interactionTarget.name : "NULL")}" +
                $"\nInteraction Available：" +
                $"{(interactionTarget == null || interactionTarget.IsInteractionAvailable)}" +
                $"\nSource Player：{sourcePlayer}",
                this
            );
        }

        return false;
    }
}