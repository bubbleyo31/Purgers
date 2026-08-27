using Fusion;
using UnityEngine;


/// <summary>
/// Support 職業 Grapple Pull Ability。
///
/// ====================================================================
///
/// 這支 Ability 屬於：
///
/// SupportProfessionRuntime。
///
/// ====================================================================
///
/// 正式流程：
///
/// PlayerGrapple
/// ↓
/// Attached
/// ↓
/// PlayerGrappleInteractionController
/// ↓
/// SupportEnemyDetected
/// ↓
/// SupportGrapplePullAbility
/// ↓
/// Enemy SupportGrapplePullReceiver
/// ↓
/// Frozen
/// ↓
/// Pulling
/// ↓
/// Complete
/// ↓
/// PlayerGrapple 收繩。
///
/// ====================================================================
///
/// 注意：
///
/// PlayerGrapple 已經負責：
///
/// 1. Support 玩家本人不被拉。
/// 2. 保持 Attached Rope。
/// 3. Rope End 跟著 Target NetworkObject。
///
/// 所以這支 Ability 完全不修改：
///
/// Player KCC
/// LineRenderer。
/// </summary>
[DisallowMultipleComponent]
public class SupportGrapplePullAbility :
    NetworkBehaviour
{
    // =====================================================================
    #region Owner References


    [Header("Owner Player 引用")]


    [SerializeField]
    [Tooltip("Owner Player 的 Grapple Interaction Controller。SupportGrapplePullAbility 會監聽 SupportEnemyDetected。Profession Runtime 中通常由 BindOwnerPlayer 自動取得。")]
    private PlayerGrappleInteractionController
        interactionController;


    [SerializeField]
    [Tooltip("Owner Player 的 PlayerGrapple。Enemy Pull 完成、失敗或被取消時，Ability 會要求 PlayerGrapple 正常播放收繩。Profession Runtime 中通常由 BindOwnerPlayer 自動取得。")]
    private PlayerGrapple
        ownerGrapple;


    /// <summary>
    /// 真正 Owner Player Core。
    /// </summary>
    private Player
        ownerPlayer;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後顯示 Support Enemy Pull Event、Receiver 搜尋、Pull 開始與結束資訊。測試階段建議保持開啟。")]
    private bool debugPullAbility =
        true;


    #endregion


    // =====================================================================
    #region Network State


    /// <summary>
    /// 目前 Support 正在拉的 Enemy NetworkObject。
    ///
    /// null 代表目前沒有 Active Pull。
    /// </summary>
    [Networked]
    private NetworkObject ActivePullTarget
    {
        get;
        set;
    }

    /// <summary>
    /// 目前 Active Pull Target
    /// 是否是一名 Player。
    ///
    /// false：
    /// Enemy → SupportGrapplePullReceiver。
    ///
    /// true：
    /// Player → SupportGrapplePlayerPullReceiver。
    /// </summary>
    [Networked]
    private NetworkBool ActivePullTargetIsPlayer
    {
        get;
        set;
    }

    #endregion


    // =====================================================================
    #region Runtime State


    /// <summary>
    /// Runtime 是否已完成 Fusion Spawn。
    /// </summary>
    private bool fusionSpawned;


    /// <summary>
    /// 是否已經訂閱 Owner Interaction Controller。
    /// </summary>
    private bool isSubscribed;


    #endregion


    // =====================================================================
    #region Owner Binding


    /// <summary>
    /// 將 Support Grapple Ability
    /// 綁定到真正 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        // =============================================================
        // 先解除舊 Owner
        // =============================================================

        Unsubscribe();


        ownerPlayer =
            newOwnerPlayer;


        if (ownerPlayer == null)
        {
            interactionController =
                null;


            ownerGrapple =
                null;


            return;
        }


        // =============================================================
        // Owner Core
        // =============================================================

        interactionController =
            ownerPlayer
                .GetComponent<
                    PlayerGrappleInteractionController
                >();


        ownerGrapple =
            ownerPlayer
                .GetComponent<
                    PlayerGrapple
                >();


        // =============================================================
        // Validation
        // =============================================================

        if (interactionController == null)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Owner Player 找不到 " +
                $"{nameof(PlayerGrappleInteractionController)}。",
                ownerPlayer
            );
        }


        if (ownerGrapple == null)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Owner Player 找不到 " +
                $"{nameof(PlayerGrapple)}。",
                ownerPlayer
            );
        }


        // =============================================================
        // 如果 Runtime 已 Spawn
        // =============================================================

        if (fusionSpawned)
        {
            Subscribe();
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
            ActivePullTarget =
                null;


            ActivePullTargetIsPlayer =
                false;


            Subscribe();
        }
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        Unsubscribe();


        fusionSpawned =
            false;
    }


    private void OnDestroy()
    {
        Unsubscribe();
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
        // 沒有 Active Pull
        // =============================================================

        if (ActivePullTarget == null)
        {
            return;
        }


        // =============================================================
        // Support Grapple 已被手動取消
        // =============================================================

        if (ownerGrapple == null ||
            ownerGrapple
                .IsSupportInteractionTetherActive ==
                false)
        {
            CancelActiveReceiver();


            ClearActivePullTarget();


            return;
        }


        // =============================================================
        // Target Lost
        // =============================================================

        if (ActivePullTarget.IsValid ==
            false)
        {
            FinishSupportTether();


            ClearActivePullTarget();


            return;
        }


        // =============================================================
        // Active Receiver
        // =============================================================

        bool pullStillActive =
            false;


        // =============================================================
        // Player
        // =============================================================

        if (ActivePullTargetIsPlayer)
        {
            SupportGrapplePlayerPullReceiver
                playerReceiver =
                    ActivePullTarget
                        .GetComponent<
                            SupportGrapplePlayerPullReceiver
                        >();


            if (playerReceiver == null)
            {
                FinishSupportTether();


                ClearActivePullTarget();


                return;
            }


            pullStillActive =
                playerReceiver.IsPullActive;
        }


        // =============================================================
        // Enemy
        // =============================================================

        else
        {
            SupportGrapplePullReceiver
                enemyReceiver =
                    ActivePullTarget
                        .GetComponent<
                            SupportGrapplePullReceiver
                        >();


            if (enemyReceiver == null)
            {
                FinishSupportTether();


                ClearActivePullTarget();


                return;
            }


            pullStillActive =
                enemyReceiver.IsPullActive;
        }


        // =============================================================
        // Receiver 還在工作
        // =============================================================

        if (pullStillActive)
        {
            return;
        }


        // =============================================================
        // Pull 已完成
        // =============================================================

        if (debugPullAbility)
        {
            Debug.Log(
                $"[Support Grapple Pull Ability] " +
                $"Enemy Pull 已完成，開始收繩。" +
                $"\nTarget：{ActivePullTarget.name}",
                this
            );
        }


        FinishSupportTether();

        ClearActivePullTarget();

    }


    #endregion


    // =====================================================================
    #region Event Subscription


    private void Subscribe()
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


        if (interactionController == null)
        {
            return;
        }


        // =============================================================
        // Enemy
        // =============================================================

        interactionController.SupportEnemyDetected -=
            OnSupportEnemyDetected;


        interactionController.SupportEnemyDetected +=
            OnSupportEnemyDetected;


        // =============================================================
        // Player
        // =============================================================

        interactionController.SupportPlayerDetected -=
            OnSupportPlayerDetected;


        interactionController.SupportPlayerDetected +=
            OnSupportPlayerDetected;


        isSubscribed =
            true;
    }


    private void Unsubscribe()
    {
        if (interactionController != null)
        {
            interactionController.SupportEnemyDetected -=
                OnSupportEnemyDetected;


            interactionController.SupportPlayerDetected -=
                OnSupportPlayerDetected;
        }


        isSubscribed =
            false;
    }


    #endregion


    // =====================================================================
    #region Support Enemy Event


    /// <summary>
    /// Support Grapple
    /// 真正 Attached 到合法 Enemy 後進入。
    /// </summary>
    private void OnSupportEnemyDetected(
        GrappleInteractionContext context
    )
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
        // Profession
        // =============================================================

        if (context.SourceProfession !=
            PlayerProfessionType.Support)
        {
            return;
        }


        // =============================================================
        // Enemy
        // =============================================================

        if (context.Target == null ||
            context.TargetType !=
                GrappleInteractionTargetType.Enemy)
        {
            return;
        }


        // =============================================================
        // NetworkObject
        // =============================================================

        NetworkObject targetObject =
            context.TargetNetworkObject;


        if (targetObject == null ||
            targetObject.IsValid ==
                false)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Support Enemy Pull 找不到有效 Target NetworkObject。",
                context.Target
            );


            FinishSupportTether();


            return;
        }


        // =============================================================
        // Receiver
        // =============================================================

        SupportGrapplePullReceiver receiver =
            targetObject
                .GetComponent<
                    SupportGrapplePullReceiver
                >();


        if (receiver == null)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Enemy 允許 Support Pull，" +
                $"但 NetworkObject Root 上沒有 " +
                $"{nameof(SupportGrapplePullReceiver)}。" +
                $"\nEnemy：{targetObject.name}",
                targetObject
            );


            FinishSupportTether();


            return;
        }


        // =============================================================
        // Owner
        // =============================================================

        if (ownerPlayer == null ||
            ownerPlayer.Object == null)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Owner Player 尚未完成 Binding。",
                this
            );


            FinishSupportTether();


            return;
        }


        // =============================================================
        // Start Pull
        // =============================================================

        bool started =
            receiver.TryBeginPull(
                ownerPlayer.Object,
                context.SourcePlayer
            );


        if (started == false)
        {
            /*
             * Receiver 拒絕 Pull 時
             * 不能讓 Support Rope 永遠卡在 Attached。
             */
            FinishSupportTether();


            return;
        }


        ActivePullTarget =
            targetObject;

        ActivePullTargetIsPlayer =
            false;
    
        // =============================================================
        // Debug
        // =============================================================

        if (debugPullAbility)
        {
            Debug.Log(
                $"[Support Grapple Pull Ability] Enemy Pull 開始。" +
                $"\nSupport：{context.SourcePlayer}" +
                $"\nTarget：{targetObject.name}" +
                $"\nInteraction：{context.InteractionType}",
                targetObject
            );
        }
    }


    /// <summary>
    /// Support Grapple
    /// 真正 Attached 到其他 Player 後進入。
    ///
    /// ------------------------------------------------------------
    ///
    /// Player 不使用 Enemy 的：
    ///
    /// Frozen 0.5 秒。
    ///
    /// ------------------------------------------------------------
    ///
    /// Player 直接：
    ///
    /// Pulling 0.5 秒
    /// ↓
    /// KCC DynamicVelocity
    /// ↓
    /// Support 當前視角前方。
    /// </summary>
    private void OnSupportPlayerDetected(
        GrappleInteractionContext context
    )
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
        // Profession
        // =============================================================

        if (context.SourceProfession !=
            PlayerProfessionType.Support)
        {
            return;
        }


        // =============================================================
        // Player Target
        // =============================================================

        if (context.Target == null ||
            context.TargetType !=
                GrappleInteractionTargetType.Player)
        {
            return;
        }


        // =============================================================
        // Target NetworkObject
        // =============================================================

        NetworkObject targetObject =
            context.TargetNetworkObject;


        if (targetObject == null ||
            targetObject.IsValid ==
                false)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Support Player Pull 找不到有效 Target NetworkObject。",
                context.Target
            );


            FinishSupportTether();


            return;
        }


        // =============================================================
        // 防止自己
        // =============================================================

        if (ownerPlayer == null ||
            ownerPlayer.Object == null)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Owner Player 尚未完成 Binding。",
                this
            );


            FinishSupportTether();


            return;
        }


        if (targetObject ==
            ownerPlayer.Object)
        {
            Debug.LogWarning(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Support 不允許 Pull 自己。",
                this
            );


            FinishSupportTether();


            return;
        }


        // =============================================================
        // Player Receiver
        // =============================================================

        SupportGrapplePlayerPullReceiver receiver =
            targetObject
                .GetComponent<
                    SupportGrapplePlayerPullReceiver
                >();


        if (receiver == null)
        {
            Debug.LogError(
                $"[{nameof(SupportGrapplePullAbility)}] " +
                $"Player 允許 Support Pull，" +
                $"但 Player NetworkObject Root 上沒有 " +
                $"{nameof(SupportGrapplePlayerPullReceiver)}。" +
                $"\nTarget Player：{targetObject.InputAuthority}",
                targetObject
            );


            FinishSupportTether();


            return;
        }


        // =============================================================
        // Start Player Pull
        // =============================================================

        bool started =
            receiver.TryBeginPull(
                ownerPlayer.Object,
                context.SourcePlayer
            );


        if (started == false)
        {
            /*
            * Receiver 拒絕時，
            * Support Rope 不能永遠停在 Attached。
            */
            FinishSupportTether();


            return;
        }


        // =============================================================
        // Active Target
        // =============================================================

        ActivePullTarget =
            targetObject;


        ActivePullTargetIsPlayer =
            true;


        // =============================================================
        // Debug
        // =============================================================

        if (debugPullAbility)
        {
            Debug.Log(
                $"[Support Grapple Pull Ability] Player Pull 開始。" +
                $"\nSupport：{context.SourcePlayer}" +
                $"\nTarget Player：{targetObject.InputAuthority}" +
                $"\nInteraction：{context.InteractionType}",
                targetObject
            );
        }
    }

    #endregion


    // =====================================================================
    #region Finish / Cancel


    /// <summary>
    /// Support Tether 正常結束。
    ///
    /// ------------------------------------------------------------
    ///
    /// playRetractAnimation = true
    ///
    /// 所以 LineRenderer
    /// 會從目前 Enemy 位置正常收回。
    ///
    /// ------------------------------------------------------------
    ///
    /// clearExistingMomentum = false
    ///
    /// 因為這次 Support Tether
    /// 根本沒有控制 Support Player 移動。
    /// </summary>
    private void FinishSupportTether()
    {
        if (ownerGrapple == null)
        {
            return;
        }


        ownerGrapple
            .CancelFromSpecialAbility(
                playRetractAnimation: true,
                clearExistingMomentum: false
            );
    }


    /// <summary>
    /// Support 自己提前切斷繩索時，
    /// 同時取消目前 Enemy 或 Player Receiver。
    /// </summary>
    private void CancelActiveReceiver()
    {
        if (ActivePullTarget == null ||
            ActivePullTarget.IsValid ==
                false)
        {
            return;
        }


        // =============================================================
        // Player
        // =============================================================

        if (ActivePullTargetIsPlayer)
        {
            SupportGrapplePlayerPullReceiver
                playerReceiver =
                    ActivePullTarget
                        .GetComponent<
                            SupportGrapplePlayerPullReceiver
                        >();


            if (playerReceiver != null)
            {
                playerReceiver.CancelPull();
            }


            return;
        }


        // =============================================================
        // Enemy
        // =============================================================

        SupportGrapplePullReceiver
            enemyReceiver =
                ActivePullTarget
                    .GetComponent<
                        SupportGrapplePullReceiver
                    >();


        if (enemyReceiver != null)
        {
            enemyReceiver.CancelPull();
        }
    }

    /// <summary>
    /// 清除目前 Support Pull
    /// 對 Target 保存的所有 Network State。
    ///
    /// Receiver Cancel 與 Grapple 收繩由呼叫端負責；
    /// 這裡只清除 Ability 自己保存的目標資料。
    /// </summary>
    private void ClearActivePullTarget()
    {
        ActivePullTarget =
            null;


        ActivePullTargetIsPlayer =
            false;
    }

    #endregion
}