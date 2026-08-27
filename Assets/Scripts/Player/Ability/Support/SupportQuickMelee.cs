using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Support 職業 F Quick Action：快速近戰。
///
/// ====================================================================
///
/// Support Quick Melee
/// 與 Attack Quick Melee
/// 目前使用相同的 Gameplay 規則：
///
/// F
/// ↓
/// Startup
/// ↓
/// Active
/// ↓
/// 執行一次多目標近戰傷害
/// ↓
/// Recovery
/// ↓
/// Idle。
///
/// ====================================================================
///
/// 不共用 AttackQuickMelee Component。
///
/// 原因：
///
/// IPlayerQuickActionAbility
/// 會依 Profession 決定
/// 目前 Runtime 應使用哪個 F Ability。
///
/// ------------------------------------------------------------
///
/// AttackQuickMelee：
/// Profession = Attack。
///
/// SupportQuickMelee：
/// Profession = Support。
///
/// ====================================================================
///
/// 真正傷害只有 State Authority 執行。
/// </summary>
[DisallowMultipleComponent]
public class SupportQuickMelee :
    NetworkBehaviour,
    IPlayerQuickActionAbility,
    ICombatDamageFeedbackSource
{
    // =====================================================================
    #region Candidate


    /// <summary>
    /// 一個已經去除重複 Hitbox 的合法近戰候選目標。
    /// </summary>
    private struct MeleeCandidate
    {
        public MonoBehaviour ReceiverBehaviour;

        public GameObject HitObject;

        public Vector3 TargetPoint;

        public float Distance;
    }


    #endregion


    // =====================================================================
    #region Quick Action Time


    [Header("Support 快速近戰時間")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家按下 F 後，到真正產生近戰傷害判定前的等待秒數。未來加入動畫後，這個值應對應真正攻擊幀以前的前搖時間。目前可以先與 Attack 相同，使用 0.08 秒。")]
    private float startupDuration =
        0.08f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("近戰 Active 階段維持時間。真正 Damage 只會在進入 Active 的瞬間執行一次，不會每個 Fusion Tick 重複造成傷害。目前可以先使用 0.02 秒。")]
    private float activeDuration =
        0.02f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("近戰完成後的 Recovery 秒數。這段時間 PlayerQuickActionController 仍會依共用規則封鎖 Fire、Aim 與 Reload，但 Movement、Look、Grapple 不受影響。目前可以先使用 0.20 秒。")]
    private float recoveryDuration =
        0.20f;


    #endregion


    // =====================================================================
    #region Damage


    [Header("Support 快速近戰傷害")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Support 快速近戰對每一個合法目標要求造成的基礎傷害。目前先與 Attack 相同使用 35。之後如果 Support 平衡需要不同，可以直接單獨調整這個 Prefab 數值。")]
    private float meleeDamage =
        35f;


    [SerializeField]
    [Min(1)]
    [Tooltip("一次 Support 快速近戰最多可以傷害多少個不同目標。同一敵人的多顆 Hitbox 會先去重，再依距離由近到遠排序。")]
    private int maximumTargets =
        3;


    #endregion


    // =====================================================================
    #region Search


    [Header("Support 快速近戰搜尋")]


    [SerializeField]
    [Tooltip("近戰範圍判定起始 Transform。只能引用 Network Player 階層中所有 Peer 都存在的 Transform，不可以引用本地 WeaponCamera 或 ViewModel。留空時會使用 Player KCC Position 加上 Origin Height。")]
    private Transform meleeOrigin;


    [SerializeField]
    [Min(0f)]
    [Tooltip("沒有指定 Melee Origin 時，從 Player KCC Position 向上增加的高度。通常放在胸口附近，例如 1.1。")]
    private float originHeight =
        1.1f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Support 快速近戰真正允許傷害到的最大距離。第一輪可以與 Attack 相同使用 2.5。")]
    private float meleeRange =
        2.5f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Fusion OverlapSphere 的搜尋半徑。Sphere 會放在玩家前方，用來形成較寬的近戰掃擊範圍。第一輪可以使用 1.4。")]
    private float searchRadius =
        1.4f;


    [SerializeField]
    [Range(1f, 180f)]
    [Tooltip("近戰完整扇形角度。例如 90 代表玩家正前方左右各約 45 度。")]
    private float maximumAttackAngle =
        90f;


    [SerializeField]
    [Tooltip("Support 快速近戰可以搜尋哪些 Layer。請與 Attack Quick Melee 使用相同的 Enemy Fusion Hitbox Layer。")]
    private LayerMask meleeHitMask =
        ~0;


    #endregion


    // =====================================================================
    #region Lag Compensation


    [Header("Photon Fusion Lag Compensation")]


    [SerializeField]
    [Tooltip("開啟後使用 HitOptions.SubtickAccuracy。高速勾索中使用 F 時建議保持開啟。")]
    private bool useSubtickAccuracy =
        true;


    [SerializeField]
    [Tooltip("開啟後除了 Fusion Hitbox，也會搜尋普通 Unity PhysX Collider。測試階段可以保持開啟。")]
    private bool includePhysX =
        true;


    #endregion


    // =====================================================================
    #region Obstruction


    [Header("近戰障礙物遮擋")]


    [SerializeField]
    [Tooltip("開啟後，正式造成近戰傷害以前會檢查玩家與目標之間是否被牆壁或大型場景幾何阻擋。")]
    private bool requireLineOfSight =
        true;


    [SerializeField]
    [Tooltip("會阻擋 Support 快速近戰的 Layer。建議只包含 Wall、Ground、WorldGeometry，不要包含 Enemy 或 Player Layer。")]
    private LayerMask obstructionMask =
        0;


    [SerializeField]
    [Min(0f)]
    [Tooltip("遮擋 Raycast 起點向攻擊方向稍微偏移的距離，用來降低射線從玩家自身碰撞體裡開始的機率。")]
    private float obstructionRayStartOffset =
        0.05f;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後，State Authority 會顯示 Support Quick Melee 的 Started、Active、Finished、候選數量與傷害結果。")]
    private bool debugQuickMelee =
        true;


    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出快速近戰 Forward 與搜尋範圍。")]
    private bool debugDrawMelee =
        true;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Debug Draw 在 Scene View 保留多久，單位為秒。")]
    private float debugDrawDuration =
        0.5f;


    #endregion


    // =====================================================================
    #region Owner Binding


    /// <summary>
    /// 真正擁有這顆 Support Runtime Ability 的 Player Core。
    /// </summary>
    private Player ownerPlayer;


    /// <summary>
    /// Owner Player 的共用移動模組。
    /// </summary>
    private PlayerMovement ownerMovement;


    /// <summary>
    /// 真正 Player Core NetworkObject。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;


    /// <summary>
    /// 目前綁定的 Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;


    /// <summary>
    /// 將 Support Quick Melee
    /// 綁定到真正 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;


        if (ownerPlayer == null)
        {
            ownerMovement =
                null;


            ownerPlayerNetworkObject =
                null;


            return;
        }


        ownerMovement =
            ownerPlayer.Movement;


        ownerPlayerNetworkObject =
            ownerPlayer.Object;


        if (ownerMovement == null)
        {
            Debug.LogError(
                $"[{nameof(SupportQuickMelee)}] " +
                $"Owner Player 找不到 PlayerMovement。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }


        if (ownerPlayerNetworkObject == null)
        {
            Debug.LogError(
                $"[{nameof(SupportQuickMelee)}] " +
                $"Owner Player 找不到有效 NetworkObject。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }


    #endregion


    // =====================================================================
    #region Unity


    private void Awake()
    {
        /*
         * 舊 Player Root 相容。
         *
         * 正式 Support Runtime 架構中：
         *
         * GetComponent<Player>()
         * 正常會找不到。
         *
         * SupportProfessionRuntimeDriver
         * 會再正式 BindOwnerPlayer()。
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
    #region Owner Helper


    private NetworkObject
        GetOwnerPlayerNetworkObject()
    {
        if (ownerPlayerNetworkObject != null)
        {
            return
                ownerPlayerNetworkObject;
        }


        if (ownerPlayer != null &&
            ownerPlayer.Object != null)
        {
            return
                ownerPlayer.Object;
        }


        Player localPlayer =
            GetComponent<Player>();


        if (localPlayer != null)
        {
            return
                localPlayer.Object;
        }


        return null;
    }


    private PlayerRef GetOwnerInputAuthority()
    {
        NetworkObject ownerObject =
            GetOwnerPlayerNetworkObject();


        if (ownerObject != null)
        {
            return
                ownerObject.InputAuthority;
        }


        if (Object != null)
        {
            return
                Object.InputAuthority;
        }


        return
            PlayerRef.None;
    }


    private bool IsOwnerPlayerObject(
        NetworkObject targetObject
    )
    {
        if (targetObject == null)
        {
            return false;
        }


        NetworkObject ownerObject =
            GetOwnerPlayerNetworkObject();


        return
            ownerObject != null &&
            targetObject ==
            ownerObject;
    }


    #endregion


    // =====================================================================
    #region Runtime Cache


    private readonly List<LagCompensatedHit>
        overlapHits =
            new List<LagCompensatedHit>(32);


    private readonly List<MeleeCandidate>
        candidates =
            new List<MeleeCandidate>(16);


    private readonly HashSet<MonoBehaviour>
        uniqueReceivers =
            new HashSet<MonoBehaviour>();


    #endregion


    // =====================================================================
    #region Damage Events


    public event Action<DamageResult>
        DamageResolved;


    public event Action<DamageResult>
        DamageConfirmed;


    #endregion


    // =====================================================================
    #region Public Data


    public float MeleeDamage =>
        meleeDamage;


    public int MaximumTargets =>
        Mathf.Max(
            1,
            maximumTargets
        );


    public float MeleeRange =>
        meleeRange;


    #endregion


    // =====================================================================
    #region IPlayerQuickActionAbility


    /// <summary>
    /// ★ 關鍵差異：
    ///
    /// 這顆 Quick Action 屬於 Support。
    ///
    /// PlayerQuickActionController
    /// 會在 SupportProfessionRuntime
    /// 找到這顆 Ability。
    /// </summary>
    public PlayerProfessionType Profession =>
        PlayerProfessionType.Support;


    public float StartupDuration =>
        Mathf.Max(
            0f,
            startupDuration
        );


    public float ActiveDuration =>
        Mathf.Max(
            0f,
            activeDuration
        );


    public float RecoveryDuration =>
        Mathf.Max(
            0f,
            recoveryDuration
        );


    public bool CanStartQuickAction()
    {
        if (ownerPlayer == null ||
            ownerMovement == null ||
            GetOwnerPlayerNetworkObject() == null)
        {
            return false;
        }


        return true;
    }


    public void OnQuickActionStarted(
        int activationSequence
    )
    {
        if (debugQuickMelee &&
            IsStateAuthority())
        {
            Debug.Log(
                $"[Support Quick Melee] Started" +
                $"\nSequence：{activationSequence}" +
                $"\nStartup：{StartupDuration:F3}",
                this
            );
        }
    }


    public void OnQuickActionActive(
        int activationSequence
    )
    {
        /*
         * Prediction 可以進 Active，
         * 但 Damage 只能由 State Authority 正式處理。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        PerformAuthoritativeMelee(
            activationSequence
        );
    }


    public void OnQuickActionFinished(
        int activationSequence
    )
    {
        if (debugQuickMelee &&
            IsStateAuthority())
        {
            Debug.Log(
                $"[Support Quick Melee] Finished" +
                $"\nSequence：{activationSequence}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Authoritative Melee


    private void PerformAuthoritativeMelee(
        int activationSequence
    )
    {
        if (Runner == null ||
            Runner.LagCompensation == null)
        {
            Debug.LogError(
                $"[{nameof(SupportQuickMelee)}] " +
                $"Runner.LagCompensation 不存在。" +
                $"\n請確認 Fusion Lag Compensation 已啟用。",
                this
            );


            return;
        }


        overlapHits.Clear();
        candidates.Clear();
        uniqueReceivers.Clear();


        // =============================================================
        // Attack Space
        // =============================================================

        Vector3 origin =
            GetMeleeOrigin();


        Vector3 forward =
            GetMeleeForward();


        Vector3 searchCenter =
            origin +
            forward *
            (
                meleeRange *
                0.5f
            );


        if (debugDrawMelee)
        {
            Debug.DrawRay(
                origin,
                forward *
                meleeRange,
                Color.red,
                debugDrawDuration
            );
        }


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
        // Owner
        // =============================================================

        PlayerRef ownerInputAuthority =
            GetOwnerInputAuthority();


        if (ownerInputAuthority.IsNone)
        {
            Debug.LogError(
                $"[{nameof(SupportQuickMelee)}] " +
                $"找不到有效 Owner Input Authority。",
                this
            );


            return;
        }


        // =============================================================
        // Overlap
        // =============================================================

        int hitCount =
            Runner.LagCompensation.OverlapSphere(
                searchCenter,
                searchRadius,
                ownerInputAuthority,
                overlapHits,
                meleeHitMask,
                hitOptions,
                true,
                QueryTriggerInteraction.Ignore
            );


        if (hitCount <= 0)
        {
            if (debugQuickMelee)
            {
                Debug.Log(
                    $"[Support Quick Melee] 沒有找到候選碰撞。" +
                    $"\nSequence：{activationSequence}" +
                    $"\nOrigin：{origin}" +
                    $"\nSearch Center：{searchCenter}",
                    this
                );
            }


            return;
        }


        // =============================================================
        // Candidate Build
        // =============================================================

        for (int i = 0;
             i < overlapHits.Count;
             i++)
        {
            LagCompensatedHit hit =
                overlapHits[i];


            GameObject hitObject =
                hit.GameObject;


            if (hitObject == null)
            {
                continue;
            }


            // ---------------------------------------------------------
            // Self
            // ---------------------------------------------------------

            NetworkObject hitNetworkObject =
                hitObject
                    .GetComponentInParent<
                        NetworkObject
                    >();


            if (IsOwnerPlayerObject(
                    hitNetworkObject
                ))
            {
                continue;
            }


            // ---------------------------------------------------------
            // Damage Receiver
            // ---------------------------------------------------------

            if (TryFindDamageReceiver(
                    hitObject,
                    out MonoBehaviour receiverBehaviour
                ) == false)
            {
                continue;
            }


            // ---------------------------------------------------------
            // Dead
            // ---------------------------------------------------------

            if (TryGetLifeState(
                    receiverBehaviour,
                    out ICombatLifeState lifeState
                ))
            {
                if (lifeState.IsAlive ==
                    false)
                {
                    continue;
                }
            }


            // ---------------------------------------------------------
            // Point / Distance
            // ---------------------------------------------------------

            Vector3 targetPoint =
                hitObject.transform.position;


            Vector3 toTarget =
                targetPoint -
                origin;


            float distance =
                toTarget.magnitude;


            if (distance >
                    meleeRange ||
                distance <=
                    0.0001f)
            {
                continue;
            }


            // ---------------------------------------------------------
            // Angle
            // ---------------------------------------------------------

            Vector3 directionToTarget =
                toTarget /
                distance;


            float angle =
                Vector3.Angle(
                    forward,
                    directionToTarget
                );


            float allowedHalfAngle =
                maximumAttackAngle *
                0.5f;


            if (angle >
                allowedHalfAngle)
            {
                continue;
            }


            // ---------------------------------------------------------
            // Duplicate Receiver
            // ---------------------------------------------------------

            if (uniqueReceivers.Contains(
                    receiverBehaviour
                ))
            {
                UpdateCandidateIfCloser(
                    receiverBehaviour,
                    hitObject,
                    targetPoint,
                    distance
                );


                continue;
            }


            uniqueReceivers.Add(
                receiverBehaviour
            );


            candidates.Add(
                new MeleeCandidate
                {
                    ReceiverBehaviour =
                        receiverBehaviour,

                    HitObject =
                        hitObject,

                    TargetPoint =
                        targetPoint,

                    Distance =
                        distance
                }
            );
        }


        // =============================================================
        // None
        // =============================================================

        if (candidates.Count <= 0)
        {
            if (debugQuickMelee)
            {
                Debug.Log(
                    $"[Support Quick Melee] Overlap 有命中，" +
                    $"但沒有合法 Damage Target。" +
                    $"\nSequence：{activationSequence}" +
                    $"\nRaw Hits：{overlapHits.Count}",
                    this
                );
            }


            return;
        }


        // =============================================================
        // Nearest First
        // =============================================================

        candidates.Sort(
            CompareCandidateDistance
        );


        int targetLimit =
            Mathf.Min(
                MaximumTargets,
                candidates.Count
            );


        int attemptedTargetCount =
            0;


        int successfulDamageCount =
            0;


        // =============================================================
        // Damage
        // =============================================================

        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            if (attemptedTargetCount >=
                targetLimit)
            {
                break;
            }


            MeleeCandidate candidate =
                candidates[i];


            if (candidate.ReceiverBehaviour ==
                    null ||
                candidate.HitObject ==
                    null)
            {
                continue;
            }


            // ---------------------------------------------------------
            // LOS
            // ---------------------------------------------------------

            if (IsTargetObstructed(
                    origin,
                    candidate.TargetPoint
                ))
            {
                continue;
            }


            attemptedTargetCount++;


            // ---------------------------------------------------------
            // Direction
            // ---------------------------------------------------------

            Vector3 hitDirection =
                candidate.TargetPoint -
                origin;


            if (hitDirection.sqrMagnitude >
                0.0001f)
            {
                hitDirection.Normalize();
            }
            else
            {
                hitDirection =
                    forward;
            }


            // ---------------------------------------------------------
            // Damage Request
            // ---------------------------------------------------------

            DamageRequest damageRequest =
                new DamageRequest
                {
                    RequestedDamage =
                        meleeDamage,

                    BaseDamage =
                        meleeDamage,

                    DamageType =
                        DamageType.Melee,

                    /*
                     * Support Quick Melee
                     * 目前沿用 Attack Quick Melee
                     * 的 Presentation Feedback。
                     *
                     * 所以：
                     *
                     * X Marker
                     * Camera Shake
                     * Kill Priority
                     *
                     * 都可以繼續直接使用現有設定。
                     *
                     * 未來如果 Support 要獨立手感，
                     * 再新增 SupportQuickMelee Feedback ID。
                     */
                    FeedbackId =
                        CombatFeedbackId
                            .AttackQuickMelee,

                    HitZone =
                        DamageHitZoneType.Body,

                    Attacker =
                        GetOwnerInputAuthority(),

                    SourceNetworkObject =
                        GetOwnerPlayerNetworkObject(),

                    SourceObject =
                        ownerPlayer != null
                            ? ownerPlayer.gameObject
                            : gameObject,

                    HitObject =
                        candidate.HitObject,

                    HitPoint =
                        candidate.TargetPoint,

                    HeadshotDamageMultiplier =
                        1f,

                    ForcedHeadshotSource =
                        DamageForcedHeadshotSource.None,

                    HitNormal =
                        -hitDirection,

                    HitDirection =
                        hitDirection,

                    Distance =
                        candidate.Distance,

                    Sequence =
                        activationSequence
                };


            bool receiverFound =
                DamageReceiverUtility
                    .TryApplyDamage(
                        candidate.HitObject,
                        damageRequest,
                        out DamageResult result
                    );


            // ---------------------------------------------------------
            // Feedback Pipeline
            // ---------------------------------------------------------

            DamageResolved?.Invoke(
                result
            );


            if (receiverFound &&
                result.Accepted)
            {
                successfulDamageCount++;


                DamageConfirmed?.Invoke(
                    result
                );
            }


            // ---------------------------------------------------------
            // Debug
            // ---------------------------------------------------------

            if (debugQuickMelee)
            {
                Debug.Log(
                    $"[Support Quick Melee] Damage Result" +
                    $"\nSequence：{activationSequence}" +
                    $"\n目標：{candidate.ReceiverBehaviour.name}" +
                    $"\n距離：{candidate.Distance:F2}" +
                    $"\nRequested Damage：{meleeDamage:F2}" +
                    $"\nReceiver Found：{receiverFound}" +
                    $"\nAccepted：{result.Accepted}" +
                    $"\nApplied Damage：{result.AppliedDamage:F2}" +
                    $"\nKilled：{result.KilledTarget}" +
                    $"\nReject Reason：{result.RejectReason}",
                    candidate.ReceiverBehaviour
                );
            }
        }


        // =============================================================
        // Summary
        // =============================================================

        if (debugQuickMelee)
        {
            Debug.Log(
                $"[Support Quick Melee] 本次近戰完成。" +
                $"\nSequence：{activationSequence}" +
                $"\nOverlap Hits：{overlapHits.Count}" +
                $"\n唯一合法候選：{candidates.Count}" +
                $"\n最多目標：{MaximumTargets}" +
                $"\n實際嘗試：{attemptedTargetCount}" +
                $"\n成功傷害：{successfulDamageCount}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Attack Space


    private Vector3 GetMeleeOrigin()
    {
        // =============================================================
        // Owner Override
        // =============================================================

        if (meleeOrigin != null &&
            ownerPlayer != null)
        {
            bool belongsToOwner =
                meleeOrigin ==
                    ownerPlayer.transform ||
                meleeOrigin.IsChildOf(
                    ownerPlayer.transform
                );


            if (belongsToOwner)
            {
                return
                    meleeOrigin.position;
            }
        }


        // =============================================================
        // KCC
        // =============================================================

        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            return
                ownerMovement
                    .KCC
                    .Data
                    .TargetPosition +
                Vector3.up *
                originHeight;
        }


        // =============================================================
        // Player Transform
        // =============================================================

        if (ownerPlayer != null)
        {
            return
                ownerPlayer.transform.position +
                Vector3.up *
                originHeight;
        }


        return
            transform.position +
            Vector3.up *
            originHeight;
    }


    private Vector3 GetMeleeForward()
    {
        Vector3 forward;


        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            forward =
                ownerMovement
                    .KCC
                    .Data
                    .TransformRotation *
                Vector3.forward;
        }
        else if (ownerPlayer != null)
        {
            forward =
                ownerPlayer
                    .transform
                    .forward;
        }
        else
        {
            forward =
                transform.forward;
        }


        forward.y =
            0f;


        if (forward.sqrMagnitude <=
            0.0001f)
        {
            forward =
                Vector3.forward;
        }


        return
            forward.normalized;
    }


    #endregion


    // =====================================================================
    #region Receiver


    private bool TryFindDamageReceiver(
        GameObject hitObject,
        out MonoBehaviour receiverBehaviour
    )
    {
        receiverBehaviour =
            null;


        if (hitObject == null)
        {
            return false;
        }


        MonoBehaviour[] behaviours =
            hitObject
                .GetComponentsInParent<
                    MonoBehaviour
                >(
                    true
                );


        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];


            if (behaviour == null)
            {
                continue;
            }


            if (behaviour is
                IDamageReceiver)
            {
                receiverBehaviour =
                    behaviour;


                return true;
            }
        }


        return false;
    }


    private bool TryGetLifeState(
        MonoBehaviour receiverBehaviour,
        out ICombatLifeState lifeState
    )
    {
        lifeState =
            null;


        if (receiverBehaviour == null)
        {
            return false;
        }


        if (receiverBehaviour is
            ICombatLifeState directLifeState)
        {
            lifeState =
                directLifeState;


            return true;
        }


        MonoBehaviour[] behaviours =
            receiverBehaviour
                .GetComponentsInParent<
                    MonoBehaviour
                >(
                    true
                );


        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];


            if (behaviour == null)
            {
                continue;
            }


            if (behaviour is
                ICombatLifeState foundLifeState)
            {
                lifeState =
                    foundLifeState;


                return true;
            }
        }


        return false;
    }


    #endregion


    // =====================================================================
    #region Candidate Update


    private void UpdateCandidateIfCloser(
        MonoBehaviour receiverBehaviour,
        GameObject hitObject,
        Vector3 targetPoint,
        float distance
    )
    {
        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            MeleeCandidate candidate =
                candidates[i];


            if (candidate.ReceiverBehaviour !=
                receiverBehaviour)
            {
                continue;
            }


            if (distance >=
                candidate.Distance)
            {
                return;
            }


            candidate.HitObject =
                hitObject;


            candidate.TargetPoint =
                targetPoint;


            candidate.Distance =
                distance;


            candidates[i] =
                candidate;


            return;
        }
    }


    private static int CompareCandidateDistance(
        MeleeCandidate a,
        MeleeCandidate b
    )
    {
        return
            a.Distance.CompareTo(
                b.Distance
            );
    }


    #endregion


    // =====================================================================
    #region Obstruction


    private bool IsTargetObstructed(
        Vector3 origin,
        Vector3 targetPoint
    )
    {
        if (requireLineOfSight == false)
        {
            return false;
        }


        if (obstructionMask.value == 0)
        {
            return false;
        }


        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();


        if (physicsScene.IsValid() ==
            false)
        {
            return false;
        }


        Vector3 toTarget =
            targetPoint -
            origin;


        float distance =
            toTarget.magnitude;


        if (distance <=
            0.0001f)
        {
            return false;
        }


        Vector3 direction =
            toTarget /
            distance;


        Vector3 rayOrigin =
            origin +
            direction *
            obstructionRayStartOffset;


        float rayDistance =
            Mathf.Max(
                0f,
                distance -
                obstructionRayStartOffset
            );


        if (rayDistance <= 0f)
        {
            return false;
        }


        return physicsScene.Raycast(
            rayOrigin,
            direction,
            out _,
            rayDistance,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );
    }


    #endregion


    // =====================================================================
    #region Helper


    private bool IsStateAuthority()
    {
        return
            Object != null &&
            Object.HasStateAuthority;
    }


    #endregion


    // =====================================================================
    #region Gizmos


#if UNITY_EDITOR


    private void OnDrawGizmosSelected()
    {
        Vector3 origin;
        Vector3 forward;


        if (Application.isPlaying)
        {
            origin =
                GetMeleeOrigin();


            forward =
                GetMeleeForward();
        }
        else
        {
            origin =
                transform.position +
                Vector3.up *
                originHeight;


            forward =
                transform.forward;


            forward.y =
                0f;


            if (forward.sqrMagnitude <=
                0.0001f)
            {
                forward =
                    Vector3.forward;
            }


            forward.Normalize();
        }


        Vector3 center =
            origin +
            forward *
            (
                meleeRange *
                0.5f
            );


        Gizmos.DrawWireSphere(
            center,
            searchRadius
        );


        Gizmos.DrawLine(
            origin,
            origin +
            forward *
            meleeRange
        );


        float halfAngle =
            maximumAttackAngle *
            0.5f;


        Vector3 leftDirection =
            Quaternion.AngleAxis(
                -halfAngle,
                Vector3.up
            ) *
            forward;


        Vector3 rightDirection =
            Quaternion.AngleAxis(
                halfAngle,
                Vector3.up
            ) *
            forward;


        Gizmos.DrawLine(
            origin,
            origin +
            leftDirection *
            meleeRange
        );


        Gizmos.DrawLine(
            origin,
            origin +
            rightDirection *
            meleeRange
        );
    }


#endif


    #endregion
}