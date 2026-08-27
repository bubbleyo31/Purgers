using Fusion;
using System;
using UnityEngine;

/// <summary>
/// 玩家 Grapple 職業互動總控制器。
///
/// ------------------------------------------------------------
///
/// 這支腳本位於 Player NetworkObject。
///
/// 它負責：
///
/// PlayerGrapple
/// ↓
/// 命中 GrappleInteractionTarget
/// ↓
/// 讀取 PlayerProfession
/// ↓
/// 根據職業與 Target Capability
/// ↓
/// 路由給對應職業技能。
///
/// ------------------------------------------------------------
///
/// 非常重要：
///
/// 這支腳本不負責：
///
/// 勾索拉動
/// LineRenderer
/// Grapple Charge
/// Attack Mark
/// Tank Gather
/// Support Pull。
///
/// 它只是一個「路由器」。
///
/// ------------------------------------------------------------
///
/// 未來：
///
/// AttackGrappleMarkAbility
/// TankGrappleGatherAbility
/// SupportGrapplePullAbility
///
/// 都可以訂閱這裡的 Event。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
public class PlayerGrappleInteractionController :
    NetworkBehaviour
{
    // =====================================================================
    #region 引用

    [Header("玩家引用")]

    [SerializeField]
    [Tooltip("玩家職業資料。Grapple Interaction Controller 會依照 Current Profession 選擇 Attack、Tank 或 Support 的職業勾索互動。若留空會自動取得。")]
    private PlayerProfession profession;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，State Authority 在 Grapple 命中 Gameplay Target 時會顯示目標類型、職業、能力判斷以及最後路由結果。這一階段建議保持開啟。")]
    private bool debugInteraction =
        true;

    #endregion

    // =====================================================================
    #region Events

    /// <summary>
    /// 任何有效職業 Grapple Interaction
    /// 被成功辨識後都會觸發。
    ///
    /// 之後如果需要統一監控所有職業勾索技能，
    /// 可以訂閱這個 Event。
    /// </summary>
    public event Action<GrappleInteractionContext>
        InteractionDetected;

    /// <summary>
    /// Attack 勾中可標記 Enemy。
    ///
    /// 下一階段 AttackGrappleMarkAbility
    /// 會訂閱這裡。
    /// </summary>
    public event Action<GrappleInteractionContext>
        AttackEnemyDetected;

    /// <summary>
    /// Tank 勾中合法 Gather Anchor。
    /// </summary>
    public event Action<GrappleInteractionContext>
        TankGatherAnchorDetected;

    /// <summary>
    /// Support 勾中可拉動 Enemy。
    /// </summary>
    public event Action<GrappleInteractionContext>
        SupportEnemyDetected;

    /// <summary>
    /// Support 勾中可拉動 Player。
    /// </summary>
    public event Action<GrappleInteractionContext>
        SupportPlayerDetected;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }
    }

    #endregion

    // =====================================================================
    #region Grapple Hit 入口

    /// <summary>
    /// 由 PlayerGrapple 在「勾索 Raycast 已經確認成功」
    /// 後呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// PlayerGrapple 仍然負責原本的 Grapple：
    ///
    /// GrappleWorldPoint
    /// GrappleAnchor
    /// Rope Shooting
    /// Pull
    /// Retract。
    ///
    /// 這裡只是額外收到一次通知。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前真正職業 Gameplay Routing
    /// 只由 State Authority 執行。
    ///
    /// 避免 Client Prediction 或 Re-simulation
    /// 重複施加：
    ///
    /// Mark
    /// Pull
    /// Gather
    ///
    /// 等高權限 Gameplay 效果。
    /// </summary>
    /// <returns>
    /// 最後判斷出的職業 Grapple Interaction。
    ///
    /// None 代表這次命中沒有特殊職業效果。
    /// </returns>
    public GrappleProfessionInteractionType NotifyGrappleHit(
        GrappleInteractionTarget target,
        Collider hitCollider,
        Vector3 hitPoint,
        Vector3 hitNormal,
        NetworkObject hitNetworkObject
    )
    {
        // =============================================================
        // Authority
        // =============================================================

        /*
         * 目前職業特殊 Gameplay
         * 一律由 State Authority 決定。
         *
         * 第一人稱繩索視覺仍然可以照原本
         * PlayerGrapple Prediction 執行。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // 新 Grapple Hit → 清除上一筆 Pending
        // =============================================================

        /*
        * 每一次新的 Grapple Hit
        * 都代表上一筆尚未 Commit 的命中資料
        * 不應再被使用。
        *
        * 所以先清除，再處理新的 Target。
        */
        ClearPendingInteraction();

        // =============================================================
        // Target
        // =============================================================

        if (target == null)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // Target Gameplay Availability
        // =============================================================

        if (target.IsInteractionAvailable ==
            false)
        {
            if (debugInteraction)
            {
                Debug.Log(
                    $"[Grapple Interaction] 目標目前不可互動。" +
                    $"\n目標：{target.name}" +
                    $"\nTarget Type：{target.TargetType}",
                    target
                );
            }

            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // Profession
        // =============================================================

        if (profession == null)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        PlayerProfessionType currentProfession =
            profession.CurrentProfession;

        // =============================================================
        // Route
        // =============================================================

        GrappleProfessionInteractionType interactionType =
            ResolveInteractionType(
                currentProfession,
                target
            );

        // =============================================================
        // 沒有特殊 Interaction
        // =============================================================

        if (interactionType ==
            GrappleProfessionInteractionType.None)
        {
            if (debugInteraction)
            {
                Debug.Log(
                    $"[Grapple Interaction] 沒有符合的職業特殊互動。" +
                    $"\n玩家職業：{currentProfession}" +
                    $"\n目標：{target.name}" +
                    $"\nTarget Type：{target.TargetType}" +
                    $"\nAttack Mark：{target.CanReceiveAttackMark}" +
                    $"\nTank Gather Anchor：{target.CanActAsTankGatherAnchor}" +
                    $"\nTank Gathered：{target.CanBeTankGathered}" +
                    $"\nSupport Pull：{target.CanBeSupportPulled}",
                    target
                );
            }

            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // Build Context
        // =============================================================

        GrappleInteractionContext context =
            new GrappleInteractionContext
            {
                SourcePlayer =
                    Object.InputAuthority,

                SourceProfession =
                    currentProfession,

                Target =
                    target,

                TargetType =
                    target.TargetType,

                TargetNetworkObject =
                    hitNetworkObject != null
                        ? hitNetworkObject
                        : target.OwnerNetworkObject,

                HitCollider =
                    hitCollider,

                HitPoint =
                    hitPoint,

                HitNormal =
                    hitNormal,

                InteractionType =
                    interactionType
            };

        // =============================================================
        // Pending
        // =============================================================

        /*
        * 現在只代表：
        *
        * Raycast 已鎖定合法 Gameplay Target。
        *
        * 還不能真的發動技能。
        *
        * 等到 PlayerGrapple 正式進入 Attached
        * 才會 Commit。
        */
        pendingInteraction =
            context;

        hasPendingInteraction =
            true;
        
        // =============================================================
        // Debug
        // =============================================================

        if (debugInteraction)
        {
            Debug.Log(
                $"[Grapple Interaction] 路由已準備，等待 Attached。" +
                $"\n玩家職業：{currentProfession}" +
                $"\n目標：{target.name}" +
                $"\nTarget Type：{target.TargetType}" +
                $"\nInteraction：{interactionType}" +
                $"\nTarget NetworkObject：" +
                $"{(context.TargetNetworkObject != null ? context.TargetNetworkObject.name : "無")}" +
                $"\nHit Point：{hitPoint}",
                target
            );
        }

        return
            interactionType;
    }

    #endregion

    // =====================================================================
    #region Pending Interaction Control

    /// <summary>
    /// 繩索真正完成 Shooting 並進入 Attached 時呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 只有到這一刻：
    ///
    /// Attack Mark
    /// Tank Gather
    /// Support Pull
    ///
    /// 才會正式開始。
    ///
    /// ------------------------------------------------------------
    ///
    /// 回傳值非常重要：
    ///
    /// PlayerGrapple 可以知道剛才真正 Commit 的
    /// 職業互動是哪一種。
    ///
    /// 例如：
    ///
    /// AttackMarkCandidate
    ///
    /// PlayerGrapple 就可以在標記完成後
    /// 立即開始收繩，
    /// 而不進入一般 Grapple Pull。
    /// </summary>
    /// <returns>
    /// 這次真正 Commit 的職業 Grapple Interaction。
    ///
    /// 如果沒有有效 Pending、目標失效或沒有 Authority，
    /// 回傳 None。
    /// </returns>
    public GrappleProfessionInteractionType
        CommitPendingInteraction()
    {
        // =============================================================
        // Authority
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // 沒有 Pending
        // =============================================================

        if (hasPendingInteraction == false)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        /*
        * 先把 Context 保存起來。
        *
        * 接著立刻清除 Pending，
        * 避免同一筆 Grapple Interaction
        * 被 Commit 第二次。
        */
        GrappleInteractionContext context =
            pendingInteraction;

        ClearPendingInteraction();

        // =============================================================
        // Target 已消失
        // =============================================================

        if (context.Target == null)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // Target 在繩索飛行途中已失效
        // =============================================================

        /*
        * 例如：
        *
        * 繩索還在 Shooting
        * ↓
        * Enemy 被隊友殺死
        * ↓
        * 繩索抵達
        *
        * 這時不能再套 Mark。
        */
        if (context.Target.IsInteractionAvailable ==
            false)
        {
            if (debugInteraction)
            {
                Debug.Log(
                    $"[Grapple Interaction] " +
                    $"Attached 時目標已不可互動，取消職業效果。" +
                    $"\n目標：{context.Target.name}",
                    context.Target
                );
            }

            return
                GrappleProfessionInteractionType.None;
        }

        // =============================================================
        // Generic Event
        // =============================================================

        InteractionDetected?.Invoke(
            context
        );

        // =============================================================
        // Profession Event
        // =============================================================

        /*
        * Attack：
        * → AttackEnemyDetected
        *
        * Tank：
        * → TankGatherAnchorDetected
        *
        * Support：
        * → SupportEnemyDetected / SupportPlayerDetected
        */
        DispatchProfessionEvent(
            context
        );

        // =============================================================
        // Debug
        // =============================================================

        if (debugInteraction)
        {
            Debug.Log(
                $"[Grapple Interaction] " +
                $"Attached，正式執行職業互動。" +
                $"\n玩家職業：{context.SourceProfession}" +
                $"\n目標：{context.Target.name}" +
                $"\nInteraction：{context.InteractionType}",
                context.Target
            );
        }

        // =============================================================
        // 回傳真正執行的 Interaction
        // =============================================================

        return
            context.InteractionType;
    }

    /// <summary>
    /// 如果 Grapple 在 Attached 前被取消，
    /// 把尚未正式執行的職業互動丟棄。
    /// </summary>
    public void CancelPendingInteraction()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        ClearPendingInteraction();
    }

    /// <summary>
    /// 只負責清除本地 Pending Cache。
    /// </summary>
    private void ClearPendingInteraction()
    {
        hasPendingInteraction =
            false;

        pendingInteraction =
            default;
    }

    #endregion

    // =====================================================================
    #region Interaction Routing

    /// <summary>
    /// 根據玩家職業與 Target Capability
    /// 決定這次 Grapple Hit 應該變成什麼職業互動。
    /// </summary>
    private GrappleProfessionInteractionType ResolveInteractionType(
        PlayerProfessionType professionType,
        GrappleInteractionTarget target
    )
    {
        if (target == null)
        {
            return
                GrappleProfessionInteractionType.None;
        }

        switch (professionType)
        {
            // =========================================================
            // Attack
            // =========================================================

            case PlayerProfessionType.Attack:
            {
                /*
                 * Attack 目前只有勾中 Enemy
                 * 才產生五秒獵殺標記候選。
                 */
                if (target.TargetType ==
                        GrappleInteractionTargetType.Enemy &&
                    target.CanReceiveAttackMark)
                {
                    return
                        GrappleProfessionInteractionType
                            .AttackMarkCandidate;
                }

                break;
            }

            // =========================================================
            // Tank
            // =========================================================

            case PlayerProfessionType.Tank:
            {
                /*
                 * Tank 直接勾中的 Enemy A
                 * 必須允許成為 Gather Anchor。
                 *
                 * CanBeTankGathered
                 * 是之後搜尋附近 B、C、D 時使用，
                 * 不是這裡判斷 A 的條件。
                 */
                if (target.TargetType ==
                        GrappleInteractionTargetType.Enemy &&
                    target.CanActAsTankGatherAnchor)
                {
                    return
                        GrappleProfessionInteractionType
                            .TankGatherAnchorCandidate;
                }

                break;
            }

            // =========================================================
            // Support
            // =========================================================

            case PlayerProfessionType.Support:
            {
                if (target.CanBeSupportPulled ==
                    false)
                {
                    break;
                }

                // -----------------------------------------------------
                // Enemy
                // -----------------------------------------------------

                if (target.TargetType ==
                    GrappleInteractionTargetType.Enemy)
                {
                    return
                        GrappleProfessionInteractionType
                            .SupportEnemyPullCandidate;
                }

                // -----------------------------------------------------
                // Player
                // -----------------------------------------------------

                if (target.TargetType ==
                    GrappleInteractionTargetType.Player)
                {
                    return
                        GrappleProfessionInteractionType
                            .SupportPlayerPullCandidate;
                }

                break;
            }

            // =========================================================
            // None
            // =========================================================

            case PlayerProfessionType.None:
            default:
            {
                break;
            }
        }

        return
            GrappleProfessionInteractionType.None;
    }

    #endregion

    // =====================================================================
    #region Event Dispatch

    /// <summary>
    /// 把統一 Context 路由到個別職業 Event。
    ///
    /// PlayerGrappleInteractionController
    /// 不需要知道未來能力元件的實際類型。
    /// </summary>
    private void DispatchProfessionEvent(
        GrappleInteractionContext context
    )
    {
        switch (context.InteractionType)
        {
            case GrappleProfessionInteractionType
                .AttackMarkCandidate:
            {
                AttackEnemyDetected?.Invoke(
                    context
                );

                break;
            }

            case GrappleProfessionInteractionType
                .TankGatherAnchorCandidate:
            {
                TankGatherAnchorDetected?.Invoke(
                    context
                );

                break;
            }

            case GrappleProfessionInteractionType
                .SupportEnemyPullCandidate:
            {
                SupportEnemyDetected?.Invoke(
                    context
                );

                break;
            }

            case GrappleProfessionInteractionType
                .SupportPlayerPullCandidate:
            {
                SupportPlayerDetected?.Invoke(
                    context
                );

                break;
            }
        }
    }

    #endregion

    // =====================================================================
    #region Pending Interaction

    /// <summary>
    /// 是否存在一筆已經辨識完成，
    /// 但還在等待繩索真正 Attached 的職業互動。
    ///
    /// 這份資料只由 State Authority 使用。
    /// </summary>
    private bool hasPendingInteraction;

    /// <summary>
    /// 等待真正 Attached 的 Grapple Interaction。
    /// </summary>
    private GrappleInteractionContext
        pendingInteraction;

    #endregion
}