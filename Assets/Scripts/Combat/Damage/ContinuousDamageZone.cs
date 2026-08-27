using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 持續傷害區。
///
/// ====================================================================
///
/// 這個版本不依賴 Unity：
///
/// OnTriggerEnter
/// OnTriggerStay
/// OnTriggerExit。
///
/// ====================================================================
///
/// 原因：
///
/// 玩家目前使用 Photon Fusion Advanced KCC。
///
/// KCC 的角色碰撞體由 KCC 自己管理，
/// 因此 Damage Zone 不應該把 Gameplay 傷害
/// 建立在普通 Unity Trigger Callback 是否有成功觸發上。
///
/// ====================================================================
///
/// 正式流程：
///
/// FixedUpdateNetwork
/// ↓
/// PhysicsScene.OverlapBox
/// ↓
/// 找 PlayerHealth
/// ↓
/// DamageReceiverUtility
/// ↓
/// PlayerIncomingDamageModifierBridge
/// ↓
/// TankGuardAbility
/// ↓
/// PlayerHealth。
///
/// ====================================================================
///
/// 所有真正傷害只會由 State Authority 執行。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BoxCollider))]
public class ContinuousDamageZone :
    NetworkBehaviour
{
    // =====================================================================
    #region 傷害設定

    [Header("持續傷害設定")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("每一次 Damage Tick 原始要求造成多少傷害。這個數值會先經過正式 Damage Pipeline，因此 Tank Guard、護盾、裝甲等系統仍然可以修改最後傷害。測試 Guard 建議先設為 20。")]
    private float damagePerTick =
        20f;

    [SerializeField]
    [Min(0.02f)]
    [Tooltip("玩家持續待在傷害區裡時，每隔多少秒受到一次傷害。0.5 代表每 0.5 秒造成一次傷害。")]
    private float damageInterval =
        0.5f;

    [SerializeField]
    [Tooltip("開啟後，玩家第一次進入傷害區的那個偵測 Tick 就會立即受到一次傷害。關閉後會先等待完整 Damage Interval。")]
    private bool damageImmediatelyOnEnter =
        true;

    #endregion

    // =====================================================================
    #region 搜尋設定

    [Header("玩家搜尋設定")]

    [SerializeField]
    [Tooltip("Damage Zone 的 OverlapBox 可以搜尋哪些 Layer。測試階段如果不確定 Player KCC Collider 在哪個 Layer，可以先設 Everything。之後確認 Player Layer 後再縮小。")]
    private LayerMask playerSearchMask =
        ~0;

    [SerializeField]
    [Min(8)]
    [Tooltip("一次 OverlapBox 最多可以暫存多少個 Collider。這不是最多玩家數，因為一名玩家可能有多個 Collider。一般測試使用 64 已經非常充足。")]
    private int overlapBufferSize =
        64;

    #endregion

    // =====================================================================
    #region Damage Request

    [Header("Damage Request 設定")]

    [SerializeField]
    [Tooltip("這個區域造成的 DamageType。一般場景傷害建議使用 Environment；如果未來是燃燒、毒素之類持續狀態則可以使用 DamageOverTime。")]
    private DamageType damageType =
        DamageType.Environment;

    [SerializeField]
    [Tooltip("這個 Damage Zone 目前沒有專屬攻擊 Hit Feedback，因此使用 None。這不影響玩家自己的受傷方向提示。")]
    private CombatFeedbackId feedbackId =
        CombatFeedbackId.None;

    #endregion

    // =====================================================================
    #region 傷害來源

    [Header("傷害來源")]

    [SerializeField]
    [Tooltip("未來傷害來源方向提示應該認為傷害從哪個位置傳來。若留空，使用 Damage Zone 自己的位置。可以另外建立一個子物件來精確指定來源中心。")]
    private Transform damageSourcePoint;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示 Damage Zone 是否成功 Spawn、偵測到哪些 PlayerHealth，以及每一次正式 DamageRequest 的結果。測試 Guard 期間建議保持開啟。")]
    private bool debugDamageZone =
        true;

    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出 Damage Zone 實際使用的 OverlapBox。若玩家明明站在方塊內但偵測不到，可以用這個確認搜尋範圍。")]
    private bool debugDrawBounds =
        true;

    #endregion

    // =====================================================================
    #region Component

    /// <summary>
    /// Damage Zone 實際搜尋範圍。
    ///
    /// BoxCollider 只拿來描述大小，
    /// 不依靠它的 Trigger Callback。
    /// </summary>
    private BoxCollider zoneCollider;

    /// <summary>
    /// PhysicsScene OverlapBox 使用的 Collider Buffer。
    /// </summary>
    private Collider[] overlapBuffer;

    #endregion

    // =====================================================================
    #region Runtime Target

    /// <summary>
    /// 目前已經確認仍待在 Damage Zone 裡的玩家。
    ///
    /// HashSet 可以自動避免：
    ///
    /// KCC Collider
    /// Hurtbox
    /// 其他 Player Collider
    ///
    /// 導致同一名玩家被算很多次。
    /// </summary>
    private readonly HashSet<PlayerHealth>
        playersInsideThisTick =
            new HashSet<PlayerHealth>();

    /// <summary>
    /// 每名玩家下一次允許受到傷害的 Timer。
    /// </summary>
    private readonly Dictionary<PlayerHealth, TickTimer>
        damageTimers =
            new Dictionary<PlayerHealth, TickTimer>();

    /// <summary>
    /// Dictionary 清理用暫存。
    /// </summary>
    private readonly List<PlayerHealth>
        removeCache =
            new List<PlayerHealth>(8);

    #endregion

    // =====================================================================
    #region Network State

    /// <summary>
    /// Damage Zone 的傷害流水號。
    ///
    /// 每一次正式 DamageRequest +1。
    /// </summary>
    [Networked]
    private int DamageSequence
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        zoneCollider =
            GetComponent<BoxCollider>();

        AllocateOverlapBuffer();
    }

    private void AllocateOverlapBuffer()
    {
        int size =
            Mathf.Max(
                8,
                overlapBufferSize
            );

        overlapBuffer =
            new Collider[size];
    }

#if UNITY_EDITOR

    private void OnValidate()
    {
        damagePerTick =
            Mathf.Max(
                0.01f,
                damagePerTick
            );

        damageInterval =
            Mathf.Max(
                0.02f,
                damageInterval
            );

        overlapBufferSize =
            Mathf.Max(
                8,
                overlapBufferSize
            );
    }

#endif

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (zoneCollider == null)
        {
            zoneCollider =
                GetComponent<BoxCollider>();
        }

        AllocateOverlapBuffer();

        if (Object.HasStateAuthority)
        {
            DamageSequence =
                0;

            damageTimers.Clear();
            playersInsideThisTick.Clear();
        }

        // =============================================================
        // 這個 Log 很重要
        // =============================================================

        if (debugDamageZone)
        {
            Debug.Log(
                $"[Continuous Damage Zone] Spawned。" +
                $"\nZone：{gameObject.name}" +
                $"\nState Authority：{Object.HasStateAuthority}" +
                $"\nRunner：{(Runner != null ? Runner.name : "NULL")}",
                this
            );
        }
    }

    public override void FixedUpdateNetwork()
    {
        // =============================================================
        // 正式傷害只由 State Authority 執行
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (Runner == null ||
            zoneCollider == null)
        {
            return;
        }

        // =============================================================
        // 1. 找目前待在 Damage Zone 裡的玩家
        // =============================================================

        FindPlayersInsideZone();

        // =============================================================
        // 2. 處理每個玩家 Damage Timer
        // =============================================================

        ProcessPlayersInsideZone();

        // =============================================================
        // 3. 清除已經離開的玩家
        // =============================================================

        CleanupPlayersThatLeft();
    }

    #endregion

    // =====================================================================
    #region Player Detection

    /// <summary>
    /// 使用 Runner 所屬 PhysicsScene
    /// 主動搜尋 Damage Zone 裡的所有 Collider。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不使用：
///
/// OnTriggerEnter
/// OnTriggerStay
/// OnTriggerExit。
    /// </summary>
    private void FindPlayersInsideZone()
    {
        playersInsideThisTick.Clear();

        if (overlapBuffer == null ||
            overlapBuffer.Length !=
            Mathf.Max(8, overlapBufferSize))
        {
            AllocateOverlapBuffer();
        }

        // =============================================================
        // Fusion Physics Scene
        // =============================================================

        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() ==
            false)
        {
            if (debugDamageZone)
            {
                Debug.LogError(
                    $"[{nameof(ContinuousDamageZone)}] " +
                    $"Runner PhysicsScene 無效。",
                    this
                );
            }

            return;
        }

        // =============================================================
        // 世界座標 Box
        // =============================================================

        Vector3 worldCenter =
            transform.TransformPoint(
                zoneCollider.center
            );

        Vector3 lossyScale =
            transform.lossyScale;

        Vector3 absoluteScale =
            new Vector3(
                Mathf.Abs(lossyScale.x),
                Mathf.Abs(lossyScale.y),
                Mathf.Abs(lossyScale.z)
            );

        Vector3 halfExtents =
            Vector3.Scale(
                zoneCollider.size * 0.5f,
                absoluteScale
            );

        Quaternion worldRotation =
            transform.rotation;

        // =============================================================
        // OverlapBox
        // =============================================================

        /*
         * QueryTriggerInteraction.Collide：
         *
         * 即使 Player 的某些 Hurtbox
         * 本身是 Trigger，
         * 我們仍然允許搜尋到它。
         */
        int hitCount =
            physicsScene.OverlapBox(
                worldCenter,
                halfExtents,
                overlapBuffer,
                worldRotation,
                playerSearchMask,
                QueryTriggerInteraction.Collide
            );

        // =============================================================
        // Debug Bounds
        // =============================================================

        if (debugDrawBounds)
        {
            Debug.DrawLine(
                worldCenter,
                worldCenter +
                Vector3.up *
                halfExtents.y,
                Color.yellow
            );
        }

        // =============================================================
        // Collider → PlayerHealth
        // =============================================================

        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                overlapBuffer[i];

            if (hitCollider == null)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 排除 Damage Zone 自己
            // ---------------------------------------------------------

            if (hitCollider ==
                zoneCollider)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 往父階層找 PlayerHealth
            // ---------------------------------------------------------

            /*
             * Advanced KCC 的 Capsule Collider
             * 可能位於玩家子物件，
             * 所以一定使用 GetComponentInParent。
             */
            PlayerHealth playerHealth =
                hitCollider
                    .GetComponentInParent<
                        PlayerHealth
                    >();

            if (playerHealth == null)
            {
                continue;
            }

            NetworkObject playerObject =
                playerHealth.Object;

            if (playerObject == null ||
                playerObject.IsValid == false)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 只處理這台 Peer 擁有 State Authority 的 Player
            // ---------------------------------------------------------

            /*
             * Host：
             *
             * 對 Host Player
             * 與所有 Client Player
             * 通常都有 State Authority。
             *
             * Client 不會在自己的 Peer
             * 重複扣血。
             */
            if (playerObject.HasStateAuthority ==
                false)
            {
                continue;
            }

            bool firstFound =
                playersInsideThisTick.Add(
                    playerHealth
                );

            if (firstFound &&
                debugDamageZone)
            {
                Debug.Log(
                    $"[Continuous Damage Zone] 偵測到 PlayerHealth。" +
                    $"\nZone：{gameObject.name}" +
                    $"\nPlayer：{playerObject.InputAuthority}" +
                    $"\nCollider：{hitCollider.name}",
                    this
                );
            }
        }
    }

    #endregion

    // =====================================================================
    #region Damage Tick

    private void ProcessPlayersInsideZone()
    {
        foreach (
            PlayerHealth playerHealth
            in playersInsideThisTick
        )
        {
            if (playerHealth == null)
            {
                continue;
            }

            if (playerHealth.IsAlive ==
                false)
            {
                continue;
            }

            // =========================================================
            // 第一次進入
            // =========================================================

            if (damageTimers.TryGetValue(
                    playerHealth,
                    out TickTimer timer
                ) == false)
            {
                if (damageImmediatelyOnEnter)
                {
                    ApplyDamage(
                        playerHealth
                    );
                }

                damageTimers[playerHealth] =
                    TickTimer.CreateFromSeconds(
                        Runner,
                        damageInterval
                    );

                continue;
            }

            // =========================================================
            // 還沒到下一次傷害
            // =========================================================

            if (timer.Expired(Runner) ==
                false)
            {
                continue;
            }

            // =========================================================
            // 正式下一 Tick Damage
            // =========================================================

            ApplyDamage(
                playerHealth
            );

            damageTimers[playerHealth] =
                TickTimer.CreateFromSeconds(
                    Runner,
                    damageInterval
                );
        }
    }

    /// <summary>
    /// 對一名玩家建立一次正式 DamageRequest。
    /// </summary>
    private void ApplyDamage(
        PlayerHealth playerHealth
    )
    {
        if (playerHealth == null ||
            playerHealth.Object == null)
        {
            return;
        }

        NetworkObject playerObject =
            playerHealth.Object;

        // =============================================================
        // Sequence
        // =============================================================

        DamageSequence++;

        // =============================================================
        // Source
        // =============================================================

        Vector3 sourcePosition =
            GetDamageSourcePosition();

        Vector3 playerPosition =
            playerObject.transform.position;

        Vector3 hitDirection =
            playerPosition -
            sourcePosition;

        float distance =
            hitDirection.magnitude;

        if (hitDirection.sqrMagnitude >
            0.0001f)
        {
            hitDirection.Normalize();
        }
        else
        {
            hitDirection =
                Vector3.up;
        }

        // =============================================================
        // Damage Request
        // =============================================================

        DamageRequest request =
            new DamageRequest
            {
                RequestedDamage =
                    damagePerTick,

                BaseDamage =
                    damagePerTick,

                /*
                 * 如果你目前 DamageRequest
                 * 已經有 BlockedDamage，
                 * 每一次新傷害一定從 0 開始。
                 */
                BlockedDamage =
                    0f,

                DamageType =
                    damageType,

                FeedbackId =
                    feedbackId,

                HitZone =
                    DamageHitZoneType.None,

                /*
                 * 這不是另一名 Player 造成的傷害。
                 */
                Attacker =
                    PlayerRef.None,

                /*
                 * ★ 傷害來源保存。
                 *
                 * 之後 PlayerHealth
                 * 可以知道是這個 Damage Zone 傷害自己。
                 */
                SourceNetworkObject =
                    Object,

                SourceObject =
                    gameObject,

                /*
                 * 直接指定 Player Root。
                 *
                 * DamageReceiverUtility
                 * 就會找到：
                 *
                 * PlayerIncomingDamageModifierBridge
                 * PlayerHealth。
                 */
                HitObject =
                    playerHealth.gameObject,

                HitPoint =
                    playerPosition,

                HitNormal =
                    -hitDirection,

                /*
                 * Source → Player。
                 */
                HitDirection =
                    hitDirection,

                Distance =
                    distance,

                Sequence =
                    DamageSequence,

                HeadshotDamageMultiplier =
                    1f,

                ForcedHeadshotSource =
                    DamageForcedHeadshotSource.None
            };

        // =============================================================
        // 正式 Damage Pipeline
        // =============================================================

        bool receiverFound =
            DamageReceiverUtility
                .TryApplyDamage(
                    playerHealth.gameObject,
                    request,
                    out DamageResult result
                );

        // =============================================================
        // Debug
        // =============================================================

        if (debugDamageZone)
        {
            Debug.Log(
                $"[Continuous Damage Zone] Damage Tick。" +
                $"\nPlayer：{playerObject.InputAuthority}" +
                $"\nReceiver Found：{receiverFound}" +
                $"\nAccepted：{result.Accepted}" +
                $"\nRequested Damage：{result.Request.RequestedDamage:F2}" +
                $"\nApplied Damage：{result.AppliedDamage:F2}" +
                $"\nWas Blocked：{result.WasBlocked}" +
                $"\nKill：{result.KilledTarget}" +
                $"\nReject Reason：{result.RejectReason}" +
                $"\nSequence：{DamageSequence}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Cleanup

    /// <summary>
    /// 清除本 Tick 已經不在 OverlapBox 裡，
    /// 或其 Player NetworkObject 已因死亡／離線而 Despawn 的玩家。
    ///
    /// 注意：
    ///
    /// Unity Component 參考與 Fusion NetworkBehaviour.Object
    /// 並不保證在同一個時間點變成 Null。
    ///
    /// 玩家死亡 Despawn 後可能短暫出現：
    ///
    /// playerHealth != null
    /// 但
    /// playerHealth.Object == null
    ///
    /// 因此清理時不能只檢查 PlayerHealth，
    /// 便直接讀取 Object.InputAuthority。
    /// </summary>
    private void CleanupPlayersThatLeft()
    {
        removeCache.Clear();

        foreach (
            KeyValuePair<PlayerHealth, TickTimer>
                pair
            in damageTimers
        )
        {
            PlayerHealth playerHealth =
                pair.Key;

            /*
            * 以下任一條件成立，都應移除 Damage Timer：
            *
            * 1. PlayerHealth Unity Component 已失效。
            * 2. PlayerHealth 所屬 NetworkObject 已被 Despawn。
            * 3. Player 本 Tick 已不在 Damage Zone 搜尋結果內。
            */
            bool healthMissing =
                playerHealth == null;

            bool networkObjectMissing =
                healthMissing == false &&
                (
                    playerHealth.Object == null ||
                    playerHealth.Object.IsValid == false
                );

            bool noLongerInsideZone =
                healthMissing ||
                playersInsideThisTick.Contains(
                    playerHealth
                ) == false;

            if (networkObjectMissing ||
                noLongerInsideZone)
            {
                removeCache.Add(
                    playerHealth
                );
            }
        }

        for (int i = 0;
            i < removeCache.Count;
            i++)
        {
            PlayerHealth playerHealth =
                removeCache[i];

            /*
            * Dictionary Key 原本一定是有效 PlayerHealth 才能被加入。
            *
            * 即使 Unity 的 overloaded == 已將已銷毀元件判定為 null，
            * removeCache 保存的仍是原本 Dictionary Key 參考，
            * 可以用來移除舊 Entry。
            */
            damageTimers.Remove(
                playerHealth
            );

            if (debugDamageZone == false)
            {
                continue;
            }

            /*
            * 不再假設 playerHealth.Object 一定存在。
            *
            * 玩家只是正常走出 Damage Zone 時可以顯示 PlayerRef；
            * 玩家已因死亡 Despawn 時則顯示失效狀態，
            * 絕不再解參考 null Object。
            */
            string playerDescription =
                "Despawned / Invalid Player";

            if (playerHealth != null &&
                playerHealth.Object != null &&
                playerHealth.Object.IsValid)
            {
                playerDescription =
                    playerHealth.Object
                        .InputAuthority
                        .ToString();
            }

            Debug.Log(
                $"[Continuous Damage Zone] 玩家已離開搜尋區或 NetworkObject 已被 Despawn。" +
                $"\nPlayer：{playerDescription}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Damage Source

    private Vector3 GetDamageSourcePosition()
    {
        if (damageSourcePoint != null)
        {
            return
                damageSourcePoint.position;
        }

        if (zoneCollider != null)
        {
            return
                transform.TransformPoint(
                    zoneCollider.center
                );
        }

        return
            transform.position;
    }

    #endregion

    // =====================================================================
    #region Gizmos

#if UNITY_EDITOR

    private void OnDrawGizmosSelected()
    {
        if (debugDrawBounds == false)
        {
            return;
        }

        BoxCollider box =
            GetComponent<BoxCollider>();

        if (box == null)
        {
            return;
        }

        Matrix4x4 previousMatrix =
            Gizmos.matrix;

        /*
         * 直接使用物件 Local Space，
         * 所以看到的框會跟 BoxCollider
         * 實際旋轉與縮放一致。
         */
        Gizmos.matrix =
            transform.localToWorldMatrix;

        Gizmos.DrawWireCube(
            box.center,
            box.size
        );

        Gizmos.matrix =
            previousMatrix;
    }

#endif

    #endregion
}