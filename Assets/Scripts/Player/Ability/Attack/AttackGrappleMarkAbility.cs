using Fusion;
using UnityEngine;

/// <summary>
/// Attack 職業勾索標記能力。
///
/// ====================================================================
///
/// 正式 Runtime 架構：
///
/// Player Core
/// │
/// ├─ PlayerGrapple
/// └─ PlayerGrappleInteractionController
///          │
///          │ AttackEnemyDetected
///          ▼
/// AttackProfessionRuntime
/// └─ AttackGrappleMarkAbility
///          │
///          ▼
/// Enemy
/// └─ AttackGrappleMarkState
///
/// ====================================================================
///
/// Attack 勾索命中可標記敵人後：
///
/// 繩索抵達 Enemy
/// ↓
/// AttackMarkCandidate Commit
/// ↓
/// AttackEnemyDetected
/// ↓
/// AttackGrappleMarkAbility
/// ↓
/// ApplyOrRefreshMark
/// ↓
/// 五秒內該 Attack 玩家
/// 對此 Enemy 的傷害視為 Headshot。
///
/// ====================================================================
///
/// 注意：
///
/// 這支能力現在存在：
///
/// AttackProfessionRuntime
///
/// 而不是 Player Core。
///
/// 因此不能再使用：
///
/// GetComponent<PlayerGrappleInteractionController>()
///
/// 假設路由器和自己在同一個 GameObject。
///
/// 必須透過 Owner Player Binding
/// 找到真正 Player Core 上的 Grapple Interaction Controller。
/// </summary>
[DisallowMultipleComponent]
public class AttackGrappleMarkAbility :
    NetworkBehaviour,
    IPlayerAbilityRuntimeModule
{
    /// <summary>
    /// 此能力在鈎索正式命中並 Attached 後執行。
    /// </summary>
    public PlayerAbilityCategory AbilityCategory =>
        PlayerAbilityCategory.GrappleHit;

    // =====================================================================
    #region Owner Player Binding

    [Header("Owner Player Binding")]

    [SerializeField]
    [Tooltip("這個 Attack Grapple Mark Ability 所屬的玩家。正常情況不需要手動指定，AttackProfessionRuntimeDriver 會在 Runtime Spawn 後自動綁定。")]
    private Player ownerPlayer;

    [SerializeField]
    [Tooltip("Owner Player 上的 PlayerGrappleInteractionController。這個元件屬於 Player Core，不屬於 Attack Profession Runtime。正常情況會由 BindOwnerPlayer 自動取得。")]
    private PlayerGrappleInteractionController
        interactionController;

    /// <summary>
    /// 真正 Owner Player 的 NetworkObject。
    ///
    /// 主要用來驗證：
    ///
    /// AttackEnemyDetected
    ///
    /// 是否真的屬於這個 Runtime 的玩家。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;

    private bool professionAvailable =
        true;

    /// <summary>
    /// 目前 Attack Runtime 所屬的 Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    /// <summary>
    /// 將這個 Attack Grapple Mark Ability
    /// 綁定到真正的 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// 呼叫者：
///
/// AttackProfessionRuntimeDriver。
///
/// ------------------------------------------------------------
///
/// 這裡會：
///
/// 1. 解除舊 Player 的事件。
/// 2. 保存新的 Owner Player。
/// 3. 從 Owner Player 取得 Grapple Interaction Controller。
/// 4. 如果 Runtime 已經 Spawn 且具有 State Authority，
///    立刻訂閱 AttackEnemyDetected。
/// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        // =============================================================
        // 同一 Owner
        // =============================================================

        /*
         * 即使是同一個 Owner，
         * 仍然重新確認一次事件訂閱。
         *
         * 可以避免：
         *
         * Spawned
         * 與
         * Runtime Driver Binding
         *
         * 執行順序不同時漏掉 Subscribe。
         */
        if (ownerPlayer ==
            newOwnerPlayer)
        {
            TrySubscribe();

            return;
        }

        // =============================================================
        // 先解除舊 Owner
        // =============================================================

        Unsubscribe();

        // =============================================================
        // 設定新 Owner
        // =============================================================

        ownerPlayer =
            newOwnerPlayer;

        ownerPlayerNetworkObject =
            null;

        interactionController =
            null;

        // =============================================================
        // 沒有 Owner
        // =============================================================

        if (ownerPlayer == null)
        {
            return;
        }

        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        // =============================================================
        // 取得 Player Core Grapple Interaction
        // =============================================================

        interactionController =
            ownerPlayer
                .GetComponent<
                    PlayerGrappleInteractionController
                >();

        if (interactionController == null)
        {
            Debug.LogError(
                $"[{nameof(AttackGrappleMarkAbility)}] " +
                $"Owner Player 找不到 " +
                $"{nameof(PlayerGrappleInteractionController)}。" +
                $"\nOwner Player：{ownerPlayer.name}",
                ownerPlayer
            );

            return;
        }

        // =============================================================
        // Runtime 如果已經 Spawn
        // 就立刻訂閱
        // =============================================================

        TrySubscribe();
    }

    #endregion

    // =====================================================================
    #region Mark 設定

    [Header("Attack 勾索標記")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Attack 勾索成功命中可標記敵人後，這名 Attack 玩家對該敵人的傷害會被視為暴頭多久，單位為秒。同一名玩家再次勾中同一敵人時，不會疊加第二層，而是把自己的標記時間重新刷新。")]
    private float markDuration =
        5f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示 Runtime Owner 綁定、事件訂閱、事件解除，以及 Attack Grapple Mark 的實際套用結果。")]
    private bool debugMarkAbility =
        true;

    #endregion

    // =====================================================================
    #region Runtime Event State

    /// <summary>
    /// 是否已經正式訂閱：
    ///
    /// PlayerGrappleInteractionController
    /// .AttackEnemyDetected。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不只依靠 -= / +=。
    ///
    /// 額外保存狀態可以讓：
    ///
    /// Spawn
    /// Bind
    /// Despawn
    /// Destroy
    ///
    /// 的生命週期更容易檢查。
    /// </summary>
    private bool isSubscribed;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        /*
         * ------------------------------------------------------------
         * Legacy 相容
         * ------------------------------------------------------------
         *
         * 如果這支腳本還暫時掛在 Player Root，
         * 允許自己找到 Player。
         *
         * ------------------------------------------------------------
         *
         * 正式搬進 AttackProfessionRuntime 後：
         *
         * GetComponent<Player>()
         *
         * 會找不到。
         *
         * 這是正常的。
         *
         * 到時會由 AttackProfessionRuntimeDriver
         * 呼叫 BindOwnerPlayer()。
         */
        if (ownerPlayer == null)
        {
            Player localPlayer =
                GetComponent<Player>();

            if (localPlayer != null)
            {
                BindOwnerPlayer(
                    localPlayer
                );
            }
        }
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        /*
         * NetworkBehaviour Spawned 的執行順序
         * 不應該和 Runtime Driver Binding
         * 建立硬性依賴。
         *
         * 所以：
         *
         * Spawned
         * → TrySubscribe
         *
         * BindOwnerPlayer
         * → 也 TrySubscribe。
         *
         * 不管哪個先發生，
         * 最後都會成功建立一次訂閱。
         */
        TrySubscribe();

        if (debugMarkAbility &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Attack Grapple Mark Ability] Runtime Spawned。" +
                $"\nOwner Player：" +
                $"{(ownerPlayer != null ? ownerPlayer.name : "尚未綁定")}" +
                $"\nInteraction Controller：" +
                $"{(interactionController != null ? interactionController.name : "尚未取得")}" +
                $"\nSubscribed：{isSubscribed}",
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
         * Runtime Despawn 時一定解除事件。
         *
         * ------------------------------------------------------------
         *
         * 例如：
         *
         * Attack
         * ↓
         * F2 Tank
         *
         * 舊 Attack Runtime 不能繼續監聽
         * Player Core 的 AttackEnemyDetected。
         */
        Unsubscribe();
    }

    private void OnDestroy()
    {
        /*
         * 額外保險。
         *
         * 如果某種 Unity 流程沒有正常經過
         * Fusion Despawned，
         * Destroy 時仍然解除 Event。
         */
        Unsubscribe();
    }

    #endregion

    // =====================================================================
    #region Event Subscription

    /// <summary>
    /// 在條件允許時訂閱 AttackEnemyDetected。
    ///
    /// ------------------------------------------------------------
    ///
    /// 必須同時滿足：
    ///
    /// 1. NetworkObject 已經存在。
    /// 2. 這個 Runtime 是 State Authority。
    /// 3. 已完成 Owner Binding。
    /// 4. 已取得 Player Core Interaction Controller。
    ///
    /// ------------------------------------------------------------
    ///
    /// Proxy / Input Authority 不需要訂閱。
    ///
    /// 真正 Attack Mark Gameplay
    /// 只由 State Authority 執行。
    /// </summary>
    private void TrySubscribe()
    {
        // =============================================================
        // 已訂閱
        // =============================================================

        if (isSubscribed)
        {
            return;
        }

        if (professionAvailable == false)
        {
            return;
        }

        // =============================================================
        // Runtime 尚未正式 Spawn
        // =============================================================

        if (Object == null)
        {
            return;
        }

        // =============================================================
        // State Authority Only
        // =============================================================

        if (Object.HasStateAuthority ==
            false)
        {
            return;
        }

        // =============================================================
        // Owner 尚未 Bind
        // =============================================================

        if (ownerPlayer == null ||
            ownerPlayerNetworkObject == null)
        {
            return;
        }

        // =============================================================
        // Interaction 尚未取得
        // =============================================================

        if (interactionController == null)
        {
            return;
        }

        // =============================================================
        // Subscribe
        // =============================================================

        /*
         * 先 -= 再 +=。
         *
         * 即使外部生命週期意外重複呼叫，
         * 也不會建立重複訂閱。
         */
        interactionController
            .AttackEnemyDetected -=
                OnAttackEnemyDetected;

        interactionController
            .AttackEnemyDetected +=
                OnAttackEnemyDetected;

        isSubscribed =
            true;

        if (debugMarkAbility)
        {
            Debug.Log(
                $"[Attack Grapple Mark Ability] 已訂閱 AttackEnemyDetected。" +
                $"\nOwner：{ownerPlayer.name}" +
                $"\nPlayer：{ownerPlayerNetworkObject.InputAuthority}",
                this
            );
        }
    }

    /// <summary>
    /// 解除 Player Core 的 AttackEnemyDetected。
    /// </summary>
    private void Unsubscribe()
    {
        if (interactionController != null)
        {
            interactionController
                .AttackEnemyDetected -=
                    OnAttackEnemyDetected;
        }

        if (isSubscribed &&
            debugMarkAbility)
        {
            Debug.Log(
                $"[Attack Grapple Mark Ability] 已解除 AttackEnemyDetected。" +
                $"\nOwner：" +
                $"{(ownerPlayer != null ? ownerPlayer.name : "無")}",
                this
            );
        }

        isSubscribed =
            false;
    }

    #endregion

    // =====================================================================
    #region Apply Mark

    /// <summary>
    /// Owner Player 的 Grapple
    /// 真正命中合法 Attack Mark Enemy 後進入。
    /// </summary>
    private void OnAttackEnemyDetected(
        GrappleInteractionContext context
    )
    {
        if (professionAvailable == false)
        {
            return;
        }

        // =============================================================
        // State Authority Only
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // =============================================================
        // Owner Binding 必須存在
        // =============================================================

        if (ownerPlayer == null ||
            ownerPlayerNetworkObject == null)
        {
            return;
        }

        // =============================================================
        // 防止錯誤 Player 的事件
        // =============================================================

        /*
         * 理論上 interactionController
         * 本來就是 Owner Player 自己那一支，
         * 所以正常不會收到別人的 Context。
         *
         * 這裡再驗證一次：
         *
         * Context Source Player
         *
         * 必須等於：
         *
         * Runtime Owner Player。
         *
         * 可以防止未來重構 Grapple Router
         * 或共享事件時出現跨玩家標記。
         */
        if (context.SourcePlayer !=
            ownerPlayerNetworkObject
                .InputAuthority)
        {
            if (debugMarkAbility)
            {
                Debug.LogWarning(
                    $"[{nameof(AttackGrappleMarkAbility)}] " +
                    $"收到不屬於這個 Runtime Owner 的 Grapple Interaction。" +
                    $"\nRuntime Owner：{ownerPlayerNetworkObject.InputAuthority}" +
                    $"\nContext Source：{context.SourcePlayer}",
                    this
                );
            }

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
        // Target 是否允許 Attack Mark
        // =============================================================

        if (context.Target.CanReceiveAttackMark ==
            false)
        {
            return;
        }

        // =============================================================
        // Network Object
        // =============================================================

        NetworkObject targetNetworkObject =
            context.TargetNetworkObject;

        if (targetNetworkObject == null)
        {
            Debug.LogError(
                $"[{nameof(AttackGrappleMarkAbility)}] " +
                $"Enemy 沒有有效的 NetworkObject。" +
                $"\n目標：{context.Target.name}",
                context.Target
            );

            return;
        }

        // =============================================================
        // Mark State
        // =============================================================

        AttackGrappleMarkState markState =
            targetNetworkObject
                .GetComponent<
                    AttackGrappleMarkState
                >();

        if (markState == null)
        {
            Debug.LogError(
                $"[{nameof(AttackGrappleMarkAbility)}] " +
                $"目標允許 Attack Mark，" +
                $"但 NetworkObject Root 上沒有 " +
                $"{nameof(AttackGrappleMarkState)}。" +
                $"\n目標：{targetNetworkObject.name}",
                targetNetworkObject
            );

            return;
        }

        // =============================================================
        // Apply / Refresh
        // =============================================================

        /*
         * 這裡使用 Context SourcePlayer，
         * 而不是 Attack Runtime 自己的 NetworkObject。
         *
         * ------------------------------------------------------------
         *
         * Enemy Mark 保存的是：
         *
         * 哪一名玩家標記了我。
         *
         * 不是：
         *
         * 哪一個 Profession Runtime 標記了我。
         *
         * ------------------------------------------------------------
         *
         * 所以 Attack → Tank → Attack
         * Runtime 即使重建，
         *
         * PlayerRef 身分仍然保持正確。
         */
        bool applied =
            markState.ApplyOrRefreshMark(
                context.SourcePlayer,
                markDuration
            );

        // =============================================================
        // Debug
        // =============================================================

        if (debugMarkAbility)
        {
            Debug.Log(
                $"[Attack Grapple Mark Ability] 標記處理完成。" +
                $"\nAttack Player：{context.SourcePlayer}" +
                $"\nOwner Player：{ownerPlayerNetworkObject.InputAuthority}" +
                $"\n目標：{targetNetworkObject.name}" +
                $"\nDuration：{markDuration:F2}" +
                $"\nApplied：{applied}",
                targetNetworkObject
            );
        }
    }


    public void SimulateAbility(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        // GrappleHit 類別由 Attached Event 驅動，不需要逐 Tick 執行。
    }


    public void SetProfessionAvailable(
        bool isAvailable
    )
    {
        professionAvailable =
            isAvailable;

        if (isAvailable)
        {
            TrySubscribe();
        }
        else
        {
            Unsubscribe();
        }
    }

    #endregion
}
