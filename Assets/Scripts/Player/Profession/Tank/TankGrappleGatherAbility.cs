using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tank 職業勾索聚怪能力。
///
/// ====================================================================
///
/// 目前第一階段負責：
///
/// Tank Grapple
/// ↓
/// 命中合法 Enemy A
/// ↓
/// 繩索真正 Attached
/// ↓
/// PlayerGrappleInteractionController
/// .TankGatherAnchorDetected
/// ↓
/// 立即中斷原本 Grapple
/// ↓
/// 以 Enemy A 為中心
/// 搜尋附近最多三隻可被 Gather 的 Enemy。
///
/// ====================================================================
///
/// 這一階段還不負責：
///
/// B / C / D 位移
/// 玩家衝向 Enemy A
/// 下一刀強制 Heavy。
///
/// ====================================================================
///
/// 先把：
///
/// Anchor A
/// Candidate B/C/D
///
/// 的辨識完全測穩，
/// 下一階段才開始真正移動物件。
/// </summary>
[DisallowMultipleComponent]
public class TankGrappleGatherAbility :
    NetworkBehaviour
{
    // =====================================================================
    #region Owner Player Binding

    [Header("Owner Player Binding")]

    [SerializeField]
    [Tooltip("這個 Tank Grapple Gather Ability 真正所屬的 Player Core。正常情況不需要手動指定，由 TankProfessionRuntimeDriver 在 Runtime Spawn 後自動綁定。")]
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 的 NetworkObject。
    ///
    /// 用來確認收到的 Grapple Event
    /// 真的屬於這名 Tank 玩家。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;

    /// <summary>
    /// Owner Player 共用勾索核心。
    ///
    /// Tank 勾到 Enemy A 後
    /// 需要立刻斷開原本 Grapple。
    /// </summary>
    private PlayerGrapple
        ownerGrapple;

    /// <summary>
    /// Owner Player 共用移動模組。
    ///
    /// Tank Gather Dash 會透過這裡的 KCC
    /// 執行真正的 3D 高速位移。
    /// </summary>
    private PlayerMovement
        ownerMovement;

    /// <summary>
    /// Player Core 上的 Grapple Profession Router。
    ///
    /// Tank Runtime 會訂閱：
///
/// TankGatherAnchorDetected。
    /// </summary>
    private PlayerGrappleInteractionController
        interactionController;

    /// <summary>
    /// 目前真正 Owner Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    #endregion

    // =====================================================================
    #region Gather Search

    [Header("Tank Gather 搜尋")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank 勾中 Enemy A 後，以 A 為圓心搜尋附近可被聚集敵人的最大半徑，單位為 Unity 世界單位。這只決定 B/C/D 的搜尋範圍，不影響玩家之後衝向 A 的距離。第一輪建議先使用 8。")]
    private float gatherSearchRadius =
        8f;

    [SerializeField]
    [Range(0, 3)]
    [Tooltip("一次 Tank Grapple Gather 最多選取多少隻 A 以外的 Enemy。依目前正式設計上限為 3，因此這個欄位最多只能設定到 3。設為 0 可以暫時關閉附近敵人搜尋。")]
    private int maximumGatherTargets =
        3;

    [SerializeField]
    [Tooltip("哪些 Layer 包含可以被 Tank Gather 搜尋到的 Enemy Hitbox。請使用與 Tank Melee、Tank Air Strike 相同的 Enemy HitMask Layer。不要只看 Enemy Root 的 Layer，真正 Fusion Hitbox 所在 GameObject 的 Layer 也必須包含在這裡。")]
    private LayerMask gatherEnemyMask =
        ~0;

    #endregion

    // =====================================================================
    #region Lag Compensation

    [Header("Fusion 搜尋設定")]

    [SerializeField]
    [Tooltip("開啟後，Tank Gather 搜尋使用 Fusion Subtick Accuracy。因為玩家可能在高速 Grapple 中觸發 Gather，建議保持開啟。")]
    private bool useSubtickAccuracy =
        true;

    [SerializeField]
    [Tooltip("開啟後，除了 Fusion Hitbox，也允許搜尋普通 Unity Collider。測試階段建議保持開啟；真正 Enemy 最終仍會再經過 GrappleInteractionTarget 能力檢查。")]
    private bool includePhysX =
        true;

    #endregion

    // =====================================================================
    #region Grapple Cancel

    [Header("Tank 勾中 Enemy 後斷索")]

    [SerializeField]
    [Tooltip("開啟後，Tank 的勾索真正 Attached 到 Gather Anchor Enemy A 時會立即中斷原本 Grapple，不讓普通勾索繼續拉玩家。這是 Tank 特殊勾索的正式規則，建議保持開啟。")]
    private bool cancelGrappleOnGather =
        true;

    [SerializeField]
    [Tooltip("Tank Gather 斷開原本 Grapple 時是否播放正常收繩動畫。關閉代表 Gameplay 立即結束勾索，但第一人稱繩索會直接回收。第一輪建議關閉，避免普通 Grapple 拉動殘留。")]
    private bool playRetractAnimation =
        false;

    #endregion

    // =====================================================================
    #region Tank 玩家 Gather Dash


    [Header("Tank 玩家 Gather Dash")]


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank 勾中 Enemy A 並觸發 Gather 後，玩家自己衝向 A 的 3D 移動速度，單位為每秒 Unity 世界單位。這個 Dash 不限制水平面，因此 Enemy A 在高處或低處時也可以直接斜向衝刺。第一輪建議使用 30。")]
    private float playerGatherDashSpeed =
        30f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Gather Dash 最後停在 Enemy A 前方多少距離。不要直接衝進 Enemy Collider 中心。這個距離也會讓之後的 Heavy Attack 有適合的攻擊位置。第一輪建議使用 1.6。")]
    private float playerGatherDashStopDistance =
        1.6f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("玩家距離計算後的 Gather Dash Destination 小於這個距離時，視為成功抵達並結束 Dash。第一輪建議使用 0.2。")]
    private float playerGatherDashArrivalDistance =
        0.2f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("如果玩家本來就已經非常靠近 Enemy A，實際需要移動的距離小於這個值，就不再啟動高速 Dash。第一輪建議使用 0.25。")]
    private float minimumPlayerGatherDashDistance =
        0.25f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank Gather Dash 最多允許持續多久，單位為秒。這是安全保護，避免玩家被牆壁、碰撞或其他狀況卡住後永遠保持 Dash 狀態。第一輪建議使用 1.5 秒。")]
    private float maximumPlayerGatherDashDuration =
        1.5f;


    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示 Tank Gather Anchor、Fusion 原始搜尋數、合法候選數量，以及最後選中的 B/C/D 名稱與距離。這一階段建議保持開啟。")]
    private bool debugGather =
        true;

    [SerializeField]
    [Tooltip("開啟後會從 Enemy A 畫線到最後選中的 B/C/D。方便在 Scene View 確認到底選到了哪些敵人。")]
    private bool debugDrawSelectedTargets =
        true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank Gather Debug 線段在 Scene View 保留多久，單位為秒。")]
    private float debugDrawDuration =
        1.5f;

    #endregion


    // =====================================================================
    #region Player Gather Dash Network State


    /// <summary>
    /// Tank 玩家是否正在執行
    /// Grapple Gather 專屬 Dash。
    /// </summary>
    [Networked]
    private NetworkBool PlayerGatherDashActive
    {
        get;
        set;
    }


    /// <summary>
    /// 這次 Gather Dash 正在追蹤的 Enemy A。
    ///
    /// Enemy A 如果在 Dash 期間移動，
    /// 玩家會繼續追蹤它的新位置。
    /// </summary>
    [Networked]
    private NetworkObject PlayerGatherDashAnchor
    {
        get;
        set;
    }


    /// <summary>
    /// Enemy A 如果在 Dash 中途失效，
    /// 保留最後一次已知位置作為備援。
    /// </summary>
    [Networked]
    private Vector3 PlayerGatherDashAnchorSnapshot
    {
        get;
        set;
    }


    /// <summary>
    /// 玩家開始 Dash 時，
    /// 從玩家指向 Enemy A 的接近方向。
    ///
    /// ------------------------------------------------------------
    ///
    /// 最終 Destination：
    ///
    /// Enemy A
    /// -
    /// Approach Direction
    /// ×
    /// Stop Distance。
    ///
    /// ------------------------------------------------------------
    ///
    /// 使用施放瞬間固定的 Approach Direction，
    /// 可以避免 A 移動時玩家繞著它旋轉。
    /// </summary>
    [Networked]
    private Vector3 PlayerGatherDashApproachDirection
    {
        get;
        set;
    }


    /// <summary>
    /// Gather Dash 安全 Timeout。
    /// </summary>
    [Networked]
    private TickTimer PlayerGatherDashTimeoutTimer
    {
        get;
        set;
    }


    /// <summary>
    /// Tank 玩家目前是否正在 Gather Dash。
    /// </summary>
    public bool IsPlayerGatherDashing =>
        PlayerGatherDashActive;


    #endregion
    // =====================================================================
    #region Runtime Event

    /// <summary>
    /// 是否已經訂閱 Owner Player 的
    /// TankGatherAnchorDetected。
    /// </summary>
    private bool isSubscribed;

    #endregion

    // =====================================================================
    #region Search Cache

    /// <summary>
    /// Fusion Lag Compensation 原始搜尋結果。
    /// </summary>
    private readonly List<LagCompensatedHit>
        gatherHits =
            new List<LagCompensatedHit>(64);

    /// <summary>
    /// 同一隻 Enemy 可能擁有多顆 Hitbox。
    ///
    /// 這裡使用 NetworkObject 去重，
    /// 確保一隻 Enemy 最多只佔一個 Candidate。
    /// </summary>
    private readonly HashSet<NetworkObject>
        uniqueEnemyObjects =
            new HashSet<NetworkObject>();

    /// <summary>
    /// 所有合法 Gather Candidate。
    /// </summary>
    private readonly List<GatherCandidate>
        gatherCandidates =
            new List<GatherCandidate>(16);

    /// <summary>
    /// 最後真正選中的最多三個 Enemy。
    ///
    /// 下一階段會直接使用這份結果
    /// 開始把 B/C/D 拉向 A。
    /// </summary>
    private readonly List<NetworkObject>
        selectedGatherTargets =
            new List<NetworkObject>(3);

    /// <summary>
    /// 最近一次 Tank Grapple Gather
    /// 真正選中的 Anchor A。
    /// </summary>
    private NetworkObject
        lastGatherAnchor;

    /// <summary>
    /// Tank Gather 搜尋到的一個合法候選 Enemy。
    /// </summary>
    private struct GatherCandidate
    {
        /// <summary>
        /// Enemy 正式 NetworkObject。
        /// </summary>
        public NetworkObject NetworkObject;

        /// <summary>
        /// Enemy Gameplay Target。
        /// </summary>
        public GrappleInteractionTarget Target;

        /// <summary>
        /// Enemy 自己的 Gather Movement Receiver。
        ///
        /// 沒有這個 Receiver，
        /// 就代表即使 Capability 被勾成可 Gather，
        /// 目前仍沒有正式移動實作。
        /// </summary>
        public TankGatherMovementReceiver
            MovementReceiver;

        /// <summary>
        /// 距離 Anchor A 多遠。
        /// </summary>
        public float Distance;
    }

    #endregion

    // =====================================================================
    #region Public Debug Data

    /// <summary>
    /// 最近一次 Tank Gather Anchor。
    ///
    /// 目前主要供 Debug。
    /// </summary>
    public NetworkObject LastGatherAnchor =>
        lastGatherAnchor;

    /// <summary>
    /// 最近一次成功選中的 B/C/D 數量。
    /// </summary>
    public int LastSelectedTargetCount =>
        selectedGatherTargets.Count;

    /// <summary>
    /// 最近一次選中的 B/C/D。
    ///
    /// 下一階段會從這裡延伸正式 Gather Movement。
    /// </summary>
    public IReadOnlyList<NetworkObject>
        LastSelectedTargets =>
            selectedGatherTargets;

    #endregion

    // =====================================================================
    #region Binding

    /// <summary>
    /// 將 Tank Grapple Gather Runtime
    /// 綁定到真正的 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        // =============================================================
        // 先解除上一個 Owner
        // =============================================================

        Unsubscribe();

        // =============================================================
        // Reset References
        // =============================================================

        ownerPlayer =
            newOwnerPlayer;

        ownerPlayerNetworkObject =
            null;

        ownerGrapple =
            null;

        ownerMovement =
            null;

        interactionController =
            null;

        if (ownerPlayer == null)
        {
            return;
        }

        // =============================================================
        // Player Core
        // =============================================================

        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        ownerGrapple =
            ownerPlayer.Grapple;

        ownerMovement =
            ownerPlayer.Movement;

        interactionController =
            ownerPlayer
                .GetComponent<
                    PlayerGrappleInteractionController
                >();

        // =============================================================
        // Validation
        // =============================================================

        if (ownerGrapple == null)
        {
            Debug.LogError(
                $"[{nameof(TankGrappleGatherAbility)}] " +
                $"Owner Player 找不到 PlayerGrapple。",
                ownerPlayer
            );
        }

        if (ownerMovement == null)
        {
            Debug.LogError(
                $"[{nameof(TankGrappleGatherAbility)}] " +
                $"Owner Player 找不到 PlayerMovement。" +
                $"\n因此 Gather 搜尋仍可執行，" +
                $"但玩家無法 Dash 到 Enemy A。",
                ownerPlayer
            );
        }

        if (interactionController == null)
        {
            Debug.LogError(
                $"[{nameof(TankGrappleGatherAbility)}] " +
                $"Owner Player 找不到 " +
                $"{nameof(PlayerGrappleInteractionController)}。",
                ownerPlayer
            );
        }

        // =============================================================
        // 如果 Runtime 已 Spawn
        // 現在就嘗試訂閱
        // =============================================================

        TrySubscribe();
    }

    #endregion

    // =====================================================================
    #region Fusion Life Cycle

    public override void Spawned()
    {
        // =============================================================
        // State Authority 初始化
        // =============================================================

        if (Object.HasStateAuthority)
        {
            ResetPlayerGatherDashState();
        }


        /*
        * Spawn 與 Runtime Owner Binding
        * 誰先發生不建立硬性依賴。
        *
        * 兩邊都會呼叫 TrySubscribe()。
        */
        TrySubscribe();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        Unsubscribe();
    }

    /// <summary>
    /// Tank Grapple Gather 的玩家 Dash
    /// 每個 Fusion Tick 在這裡更新。
    ///
    /// ------------------------------------------------------------
    ///
    /// Gather Target Search 本身仍然是 Event 驅動。
    ///
    /// 只有真正開始 Player Dash 後，
    /// 才需要每 Tick 控制 KCC。
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        if (PlayerGatherDashActive == false)
        {
            return;
        }


        TickPlayerGatherDash();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    #endregion

    // =====================================================================
    #region Event Subscription

    private void TrySubscribe()
    {
        if (isSubscribed)
        {
            return;
        }

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (ownerPlayer == null ||
            ownerPlayerNetworkObject == null ||
            interactionController == null)
        {
            return;
        }

        /*
         * 先 -= 再 +=，
         * 避免職業 Runtime 重建或重複 Binding
         * 產生同一事件訂閱兩次。
         */
        interactionController
            .TankGatherAnchorDetected -=
                OnTankGatherAnchorDetected;

        interactionController
            .TankGatherAnchorDetected +=
                OnTankGatherAnchorDetected;

        isSubscribed =
            true;

        if (debugGather)
        {
            Debug.Log(
                $"[Tank Grapple Gather] 已訂閱 TankGatherAnchorDetected。" +
                $"\nPlayer：{ownerPlayerNetworkObject.InputAuthority}",
                this
            );
        }
    }

    private void Unsubscribe()
    {
        if (interactionController != null)
        {
            interactionController
                .TankGatherAnchorDetected -=
                    OnTankGatherAnchorDetected;
        }

        isSubscribed =
            false;
    }

    #endregion

    // =====================================================================
    #region Tank Grapple Event

    /// <summary>
    /// Tank 的 Grapple 真正 Attached
    /// 到合法 Enemy A 後進入。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// Raycast 剛碰到 A 時不會進這裡。
    ///
    /// 必須等 Rope Shooting 完成，
    /// Grapple 正式 Attached，
    /// PlayerGrappleInteractionController
    /// Commit Pending Interaction 後才會觸發。
    /// </summary>
    private void OnTankGatherAnchorDetected(
        GrappleInteractionContext context
    )
    {
        // =============================================================
        // State Authority
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // =============================================================
        // Owner Validation
        // =============================================================

        if (ownerPlayerNetworkObject == null)
        {
            return;
        }

        if (context.SourcePlayer !=
            ownerPlayerNetworkObject.InputAuthority)
        {
            return;
        }

        // =============================================================
        // Profession Validation
        // =============================================================

        if (context.SourceProfession !=
            PlayerProfessionType.Tank)
        {
            return;
        }

        // =============================================================
        // Target Validation
        // =============================================================

        GrappleInteractionTarget anchorTarget =
            context.Target;

        if (anchorTarget == null ||
            context.TargetType !=
                GrappleInteractionTargetType.Enemy)
        {
            return;
        }

        if (anchorTarget.IsInteractionAvailable ==
            false)
        {
            return;
        }

        if (anchorTarget.CanActAsTankGatherAnchor ==
            false)
        {
            return;
        }

        // =============================================================
        // Anchor NetworkObject
        // =============================================================

        NetworkObject anchorObject =
            context.TargetNetworkObject;

        if (anchorObject == null)
        {
            anchorObject =
                anchorTarget.OwnerNetworkObject;
        }

        if (anchorObject == null)
        {
            anchorObject =
                anchorTarget
                    .GetComponentInParent<
                        NetworkObject
                    >();
        }

        if (anchorObject == null)
        {
            Debug.LogError(
                $"[{nameof(TankGrappleGatherAbility)}] " +
                $"Tank Gather Anchor 找不到 NetworkObject。" +
                $"\nTarget：{anchorTarget.name}",
                anchorTarget
            );

            return;
        }

        lastGatherAnchor =
            anchorObject;

        // =============================================================
        // ★ 立即取消普通 Grapple
        // =============================================================

        /*
         * Tank 勾到 Enemy A 後：
         *
         * 不應該繼續使用普通 Grapple
         * 把玩家往 A 拉。
         *
         * ------------------------------------------------------------
         *
         * 下一階段會由 Tank Gather Ability
         * 自己控制：
         *
         * 玩家 Dash 到 A。
         *
         * ------------------------------------------------------------
         *
         * clearExistingMomentum = true：
         *
         * 避免原本 Grapple Momentum
         * 和之後的 Tank Dash 疊在一起。
         */
        if (cancelGrappleOnGather &&
            ownerGrapple != null)
        {
            ownerGrapple
                .CancelFromSpecialAbility(
                    playRetractAnimation,
                    true
                );
        }

        // =============================================================
        // 1. B / C / D 開始聚集
        // =============================================================

        ResolveGatherTargets(
            anchorTarget,
            anchorObject
        );


        // =============================================================
        // 2. Tank 玩家自己 Dash 到 A
        // =============================================================

        StartPlayerGatherDash(
            anchorObject
        );
    }

    #endregion

    // =====================================================================
    #region Player Gather Dash


    /// <summary>
    /// Tank 勾中 Enemy A 後，
    /// 正式開始把 Owner Player 衝向 A 面前。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個 Dash：
    ///
    /// 不消耗 Tank Air Dash Cooldown。
    ///
    /// 不需要玩家再按右鍵。
    ///
    /// 不限制水平面。
    ///
    /// 可以：
    ///
    /// 向前
    /// 向上
    /// 向下
    /// 斜向。
    /// </summary>
    private void StartPlayerGatherDash(
        NetworkObject anchorObject
    )
    {
        // =============================================================
        // 基本檢查
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        if (anchorObject == null ||
            anchorObject.IsValid == false)
        {
            return;
        }


        if (ownerPlayer == null ||
            ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            Debug.LogError(
                $"[{nameof(TankGrappleGatherAbility)}] " +
                $"無法開始 Player Gather Dash。" +
                $"\nOwner Player：" +
                $"{(ownerPlayer != null ? ownerPlayer.name : "NULL")}" +
                $"\nOwner Movement：" +
                $"{(ownerMovement != null ? "找到" : "NULL")}" +
                $"\nOwner KCC：" +
                $"{(ownerMovement != null && ownerMovement.KCC != null ? "找到" : "NULL")}",
                this
            );


            return;
        }


        // =============================================================
        // Current Position
        // =============================================================

        Vector3 playerPosition =
            ownerMovement
                .KCC
                .Data
                .TargetPosition;


        Vector3 anchorPosition =
            anchorObject
                .transform
                .position;


        // =============================================================
        // Approach Direction
        // =============================================================

        Vector3 toAnchor =
            anchorPosition -
            playerPosition;


        if (toAnchor.sqrMagnitude <=
            0.0001f)
        {
            return;
        }


        Vector3 approachDirection =
            toAnchor.normalized;


        // =============================================================
        // Destination
        // =============================================================

        Vector3 destination =
            anchorPosition -
            approachDirection *
            Mathf.Max(
                0f,
                playerGatherDashStopDistance
            );


        float travelDistance =
            Vector3.Distance(
                playerPosition,
                destination
            );


        // =============================================================
        // 已經夠近
        // =============================================================

        if (travelDistance <=
            minimumPlayerGatherDashDistance)
        {
            if (debugGather)
            {
                Debug.Log(
                    $"[Tank Grapple Gather Dash] 玩家已經靠近 Anchor，" +
                    $"不需要啟動 Dash。" +
                    $"\nAnchor：{anchorObject.name}" +
                    $"\nTravel Distance：{travelDistance:F2}",
                    this
                );
            }


            ResetPlayerGatherDashState();


            return;
        }


        // =============================================================
        // 清除原 Grapple Momentum
        // =============================================================

        /*
        * OnTankGatherAnchorDetected()
        * 前面雖然已經透過：
        *
        * CancelFromSpecialAbility(..., true)
        *
        * 清掉普通 Grapple Momentum，
        *
        * 這裡再透過 Player Core 正式接口
        * 做一次高權限位移前的保護。
        */
        ownerPlayer
            .CancelManualMomentumFromSpecialAbility(
                true
            );


        // =============================================================
        // Network State
        // =============================================================

        PlayerGatherDashAnchor =
            anchorObject;


        PlayerGatherDashAnchorSnapshot =
            anchorPosition;


        PlayerGatherDashApproachDirection =
            approachDirection;


        PlayerGatherDashActive =
            true;


        PlayerGatherDashTimeoutTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    0.1f,
                    maximumPlayerGatherDashDuration
                )
            );


        // =============================================================
        // Debug
        // =============================================================

        if (debugGather)
        {
            Debug.Log(
                $"[Tank Grapple Gather Dash] Dash Started" +
                $"\nPlayer：{ownerPlayerNetworkObject.InputAuthority}" +
                $"\nAnchor：{anchorObject.name}" +
                $"\nStart：{playerPosition}" +
                $"\nAnchor Position：{anchorPosition}" +
                $"\nDestination：{destination}" +
                $"\nTravel Distance：{travelDistance:F2}" +
                $"\nSpeed：{playerGatherDashSpeed:F2}",
                this
            );
        }
    }


    /// <summary>
    /// 每個 Fusion Tick
    /// 推進 Tank 玩家 Gather Dash。
    /// </summary>
    private void TickPlayerGatherDash()
    {
        // =============================================================
        // KCC
        // =============================================================

        if (ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            CompletePlayerGatherDash(
                "KCC Missing"
            );


            return;
        }


        // =============================================================
        // Timeout
        // =============================================================

        if (PlayerGatherDashTimeoutTimer
            .Expired(Runner))
        {
            CompletePlayerGatherDash(
                "Timeout"
            );


            return;
        }


        // =============================================================
        // Anchor Position
        // =============================================================

        Vector3 anchorPosition =
            PlayerGatherDashAnchorSnapshot;


        /*
        * A 還活著：
        *
        * 每 Tick 更新 Anchor 的目前位置。
        */
        if (PlayerGatherDashAnchor != null &&
            PlayerGatherDashAnchor.IsValid)
        {
            anchorPosition =
                PlayerGatherDashAnchor
                    .transform
                    .position;


            PlayerGatherDashAnchorSnapshot =
                anchorPosition;
        }


        // =============================================================
        // Current Player Position
        // =============================================================

        Vector3 playerPosition =
            ownerMovement
                .KCC
                .Data
                .TargetPosition;


        // =============================================================
        // Destination
        // =============================================================

        Vector3 approachDirection =
            PlayerGatherDashApproachDirection;


        if (approachDirection.sqrMagnitude <=
            0.0001f)
        {
            CompletePlayerGatherDash(
                "Invalid Approach Direction"
            );


            return;
        }


        approachDirection.Normalize();


        Vector3 destination =
            anchorPosition -
            approachDirection *
            Mathf.Max(
                0f,
                playerGatherDashStopDistance
            );


        // =============================================================
        // Remaining
        // =============================================================

        Vector3 toDestination =
            destination -
            playerPosition;


        float remainingDistance =
            toDestination.magnitude;


        // =============================================================
        // Arrived
        // =============================================================

        if (remainingDistance <=
            playerGatherDashArrivalDistance)
        {
            CompletePlayerGatherDash(
                "Arrived"
            );


            return;
        }


        // =============================================================
        // Direction
        // =============================================================

        Vector3 dashDirection =
            toDestination /
            remainingDistance;


        // =============================================================
        // 避免 Overshoot
        // =============================================================

        float normalDashSpeed =
            Mathf.Max(
                0.1f,
                playerGatherDashSpeed
            );


        float maximumSpeedWithoutOvershoot =
            remainingDistance /
            Mathf.Max(
                Runner.DeltaTime,
                0.0001f
            );


        float actualSpeed =
            Mathf.Min(
                normalDashSpeed,
                maximumSpeedWithoutOvershoot
            );


        Vector3 dashVelocity =
            dashDirection *
            actualSpeed;


        // =============================================================
        // ★ Dash 擁有移動控制權
        // =============================================================

        /*
        * Player.cs 的普通 Movement
        * 可能已經在這個 Tick 寫入 WASD。
        *
        * Gather Dash 現在屬於更高權限位移，
        * 所以清除普通 Input Direction。
        */
        ownerMovement
            .KCC
            .SetInputDirection(
                Vector3.zero
            );


        // =============================================================
        // ★ 完整 3D Kinematic Velocity
        // =============================================================

        /*
        * 不把 Y 清掉。
        *
        * 所以 A：
        *
        * 在高處 → 斜上 Dash
        * 在低處 → 斜下 Dash
        * 同高度 → 正常水平 Dash。
        */
        ownerMovement
            .KCC
            .SetKinematicVelocity(
                dashVelocity
            );


        // =============================================================
        // Debug
        // =============================================================

        if (debugDrawSelectedTargets)
        {
            Debug.DrawLine(
                playerPosition,
                destination,
                Color.yellow,
                Runner.DeltaTime * 2f
            );
        }
    }


    /// <summary>
    /// 結束 Tank 玩家 Gather Dash。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前這一步只結束位移。
    ///
    /// 下一步才會在 Arrived 後加入：
    ///
    /// ForceNextHeavyAttack(3 秒)。
    /// </summary>
    private void CompletePlayerGatherDash(
        string reason
    )
    {
        if (PlayerGatherDashActive ==
            false)
        {
            return;
        }


        // =============================================================
        // 清除 Dash Velocity
        // =============================================================

        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            ownerMovement
                .KCC
                .SetKinematicVelocity(
                    Vector3.zero
                );
        }


        // =============================================================
        // Debug
        // =============================================================

        if (debugGather)
        {
            Vector3 finalPosition =
                ownerMovement != null &&
                ownerMovement.KCC != null
                    ? ownerMovement
                        .KCC
                        .Data
                        .TargetPosition
                    : Vector3.zero;


            Debug.Log(
                $"[Tank Grapple Gather Dash] Dash Completed" +
                $"\nReason：{reason}" +
                $"\nFinal Position：{finalPosition}",
                this
            );
        }


        // =============================================================
        // Reset
        // =============================================================

        ResetPlayerGatherDashState();
    }


    /// <summary>
    /// 清除 Player Gather Dash Network State。
    /// </summary>
    private void ResetPlayerGatherDashState()
    {
        PlayerGatherDashActive =
            false;


        PlayerGatherDashAnchor =
            null;


        PlayerGatherDashAnchorSnapshot =
            Vector3.zero;


        PlayerGatherDashApproachDirection =
            Vector3.zero;


        PlayerGatherDashTimeoutTimer =
            TickTimer.None;
    }


    #endregion

    // =====================================================================
    #region Gather Search

    /// <summary>
    /// 以 Enemy A 為中心搜尋附近
    /// 最多三隻合法 Gather Enemy。
    /// </summary>
    private void ResolveGatherTargets(
        GrappleInteractionTarget anchorTarget,
        NetworkObject anchorObject
    )
    {
        selectedGatherTargets.Clear();
        gatherCandidates.Clear();
        gatherHits.Clear();
        uniqueEnemyObjects.Clear();

        if (maximumGatherTargets <= 0)
        {
            LogGatherResult(
                anchorObject,
                0
            );

            return;
        }

        if (Runner == null ||
            Runner.LagCompensation == null)
        {
            return;
        }

        if (ownerPlayerNetworkObject == null)
        {
            return;
        }

        PlayerRef sourcePlayer =
            ownerPlayerNetworkObject
                .InputAuthority;

        if (sourcePlayer.IsNone)
        {
            return;
        }

        // =============================================================
        // Search Center
        // =============================================================

        /*
         * 這裡使用 Enemy A 的 NetworkObject Root。
         *
         * 下一階段真正把 B/C/D 拉向 A 時，
         * 也會使用同一個 Anchor 基準，
         * 避免 Hitbox 點與 Enemy Root
         * 產生兩套不同座標。
         */
        Vector3 anchorPosition =
            anchorObject
                .transform
                .position;

        // =============================================================
        // Hit Options
        // =============================================================

        HitOptions hitOptions =
            HitOptions.IgnoreInputAuthority;

        if (useSubtickAccuracy)
        {
            hitOptions |=
                HitOptions.SubtickAccuracy;
        }

        if (includePhysX)
        {
            hitOptions |=
                HitOptions.IncludePhysX;
        }

        // =============================================================
        // Fusion Overlap Sphere
        // =============================================================

        int rawHitCount =
            Runner
                .LagCompensation
                .OverlapSphere(
                    anchorPosition,
                    gatherSearchRadius,
                    sourcePlayer,
                    gatherHits,
                    gatherEnemyMask,
                    hitOptions,
                    true,
                    QueryTriggerInteraction.Collide
                );

        // =============================================================
        // Candidate Build
        // =============================================================

        for (int i = 0;
             i < gatherHits.Count;
             i++)
        {
            LagCompensatedHit hit =
                gatherHits[i];

            GameObject hitObject =
                hit.GameObject;

            if (hitObject == null)
            {
                continue;
            }

            // ---------------------------------------------------------
            // Gameplay Target
            // ---------------------------------------------------------

            GrappleInteractionTarget target =
                hitObject
                    .GetComponentInParent<
                        GrappleInteractionTarget
                    >();

            if (target == null)
            {
                continue;
            }

            // ---------------------------------------------------------
            // Enemy Only
            // ---------------------------------------------------------

            if (target.TargetType !=
                GrappleInteractionTargetType.Enemy)
            {
                continue;
            }

            // ---------------------------------------------------------
            // ★ A 自己不能被 Gather
            // ---------------------------------------------------------

            if (target ==
                anchorTarget)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 必須允許 Tank Gather
            // ---------------------------------------------------------

            /*
             * 注意：
             *
             * Enemy A 使用：
             *
             * CanActAsTankGatherAnchor
             *
             * B/C/D 使用：
             *
             * CanBeTankGathered。
             *
             * 兩個 Capability 不混用。
             */
            if (target.CanBeTankGathered ==
                false)
            {
                continue;
            }

            // ---------------------------------------------------------
            // Dead / Disabled
            // ---------------------------------------------------------

            if (target.IsInteractionAvailable ==
                false)
            {
                continue;
            }

            // ---------------------------------------------------------
            // Candidate NetworkObject
            // ---------------------------------------------------------

            NetworkObject candidateObject =
                target.OwnerNetworkObject;

            if (candidateObject == null)
            {
                candidateObject =
                    hitObject
                        .GetComponentInParent<
                            NetworkObject
                        >();
            }

            if (candidateObject == null)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 再次排除 A
            // ---------------------------------------------------------

            if (candidateObject ==
                anchorObject)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 多 Hitbox 去重
            // ---------------------------------------------------------

            if (uniqueEnemyObjects.Add(
                    candidateObject
                ) == false)
            {
                continue;
            }

            // ---------------------------------------------------------
            // Gather Movement Receiver
            // ---------------------------------------------------------

            /*
            * CanBeTankGathered = true
            *
            * 代表：
            *
            * Gameplay 規則允許被 Tank 拉。
            *
            * --------------------------------------------------------
            *
            * TankGatherMovementReceiver
            *
            * 代表：
            *
            * 這隻 Enemy 真的有實作
            * 「被拉動」的方法。
            *
            * --------------------------------------------------------
            *
            * 兩者缺一不可。
            */
            TankGatherMovementReceiver movementReceiver =
                candidateObject
                    .GetComponent<
                        TankGatherMovementReceiver
                    >();

            if (movementReceiver == null)
            {
                if (debugGather)
                {
                    Debug.LogWarning(
                        $"[{nameof(TankGrappleGatherAbility)}] " +
                        $"Enemy「{candidateObject.name}」" +
                        $"已設定 CanBeTankGathered，" +
                        $"但 Root 上沒有 " +
                        $"{nameof(TankGatherMovementReceiver)}。" +
                        $"\n這名 Enemy 不會被算入正式 Gather Candidate。",
                        candidateObject
                    );
                }

                continue;
            }

            // ---------------------------------------------------------
            // Distance
            // ---------------------------------------------------------

            float distance =
                Vector3.Distance(
                    anchorPosition,
                    candidateObject
                        .transform
                        .position
                );

            if (distance >
                gatherSearchRadius)
            {
                continue;
            }

            gatherCandidates.Add(
                new GatherCandidate
                {
                    NetworkObject =
                        candidateObject,

                    Target =
                        target,

                    MovementReceiver =
                        movementReceiver,

                    Distance =
                        distance
                }
            );
        }

        // =============================================================
        // 距離排序
        // =============================================================

        /*
         * Tank Gather 目前規則：
         *
         * A 周圍如果有超過三隻合法 Enemy，
         * 優先抓「離 A 最近」的三隻。
         */
        gatherCandidates.Sort(
            CompareCandidateDistance
        );

        // =============================================================
        // 正式啟動最多三隻 Gather
        // =============================================================

        /*
        * 不直接：
        *
        * Take 前三名。
        *
        * ------------------------------------------------------------
        *
        * 而是按照距離從近到遠，
        * 一隻一隻要求它開始 Gather。
        *
        * ------------------------------------------------------------
        *
        * 只有：
        *
        * TryBeginGather() == true
        *
        * 才真正佔一個 B/C/D 名額。
        *
        * ------------------------------------------------------------
        *
        * 所以某隻 Enemy 如果：
        *
        * 已經被另一個 Tank 控制
        * State Authority 不合法
        * 中途死亡
        *
        * 就會跳過它並繼續找下一隻。
        */
        for (int i = 0;
            i < gatherCandidates.Count;
            i++)
        {
            // ---------------------------------------------------------
            // 已經滿三隻
            // ---------------------------------------------------------

            if (selectedGatherTargets.Count >=
                maximumGatherTargets)
            {
                break;
            }

            GatherCandidate candidate =
                gatherCandidates[i];

            if (candidate.NetworkObject == null ||
                candidate.MovementReceiver == null)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 正式要求 Enemy 自己開始 Gather
            // ---------------------------------------------------------

            bool gatherStarted =
                candidate
                    .MovementReceiver
                    .TryBeginGather(
                        anchorObject,
                        ownerPlayerNetworkObject
                            .InputAuthority
                    );

            if (gatherStarted == false)
            {
                if (debugGather)
                {
                    Debug.Log(
                        $"[Tank Grapple Gather] Candidate 拒絕 Gather。" +
                        $"\nEnemy：{candidate.NetworkObject.name}" +
                        $"\nDistance：{candidate.Distance:F2}",
                        candidate.NetworkObject
                    );
                }

                continue;
            }

            // ---------------------------------------------------------
            // 正式加入 B/C/D
            // ---------------------------------------------------------

            selectedGatherTargets.Add(
                candidate.NetworkObject
            );

            // ---------------------------------------------------------
            // Debug Line
            // ---------------------------------------------------------

            if (debugDrawSelectedTargets)
            {
                Debug.DrawLine(
                    anchorPosition,
                    candidate.NetworkObject
                        .transform
                        .position,
                    Color.magenta,
                    debugDrawDuration
                );
            }
        }

        // =============================================================
        // Debug
        // =============================================================

        LogGatherResult(
            anchorObject,
            rawHitCount
        );
    }

    /// <summary>
    /// Candidate 依照離 Enemy A 的距離排序。
    /// </summary>
    private static int CompareCandidateDistance(
        GatherCandidate a,
        GatherCandidate b
    )
    {
        return
            a.Distance.CompareTo(
                b.Distance
            );
    }

    #endregion

    // =====================================================================
    #region Debug

    private void LogGatherResult(
        NetworkObject anchorObject,
        int rawHitCount
    )
    {
        if (debugGather == false)
        {
            return;
        }

        string selectedText =
            string.Empty;

        for (int i = 0;
             i < selectedGatherTargets.Count;
             i++)
        {
            NetworkObject target =
                selectedGatherTargets[i];

            if (target == null)
            {
                continue;
            }

            float distance =
                anchorObject != null
                    ? Vector3.Distance(
                        anchorObject
                            .transform
                            .position,
                        target
                            .transform
                            .position
                    )
                    : 0f;

            selectedText +=
                $"\n[{i + 1}] " +
                $"{target.name} " +
                $"Distance：{distance:F2}";
        }

        if (string.IsNullOrEmpty(
                selectedText
            ))
        {
            selectedText =
                "\n無";
        }

        Debug.Log(
            $"[Tank Grapple Gather]" +
            $"\nAnchor A：" +
            $"{(anchorObject != null ? anchorObject.name : "NULL")}" +
            $"\nSearch Radius：{gatherSearchRadius:F2}" +
            $"\nRaw Hits：{rawHitCount}" +
            $"\nLegal Candidates：{gatherCandidates.Count}" +
            $"\nMaximum Targets：{maximumGatherTargets}" +
            $"\nSelected Count：{selectedGatherTargets.Count}" +
            $"\nSelected B/C/D：" +
            $"{selectedText}",
            anchorObject != null
                ? anchorObject
                : this
        );
    }

    #endregion
}