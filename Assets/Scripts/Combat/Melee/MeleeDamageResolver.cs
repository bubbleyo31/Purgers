using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次共用近戰 Damage Query 的完整資料。
///
/// ------------------------------------------------------------
///
/// 這不是 MonoBehaviour。
///
/// 呼叫端只需要告訴 Resolver：
///
/// 誰攻擊
/// 從哪裡攻擊
/// 往哪裡攻擊
/// 傷害
/// 距離
/// 搜尋半徑
/// 扇形角度
/// 最大目標數
/// Hit Mask
/// LOS 設定。
///
/// ------------------------------------------------------------
///
/// Attack Quick Melee、Tank Combo、Tank Air Strike
/// 未來都可以共用同一套判定邏輯。
/// </summary>
public struct MeleeDamageQuery
{
    // =====================================================================
    #region Fusion

    /// <summary>
    /// 執行這次 Query 的 NetworkRunner。
    /// </summary>
    public NetworkRunner Runner;

    /// <summary>
    /// 真正發動攻擊的玩家。
    ///
    /// Lag Compensation 會依照這個玩家
    /// 當時看到的歷史狀態進行判定。
    /// </summary>
    public PlayerRef Attacker;

    #endregion

    // =====================================================================
    #region Damage Source

    /// <summary>
    /// 真正的 Player Core NetworkObject。
    ///
    /// 不應該是 Profession Runtime NetworkObject。
    /// </summary>
    public NetworkObject SourceNetworkObject;

    /// <summary>
    /// 真正 Player Core GameObject。
    /// </summary>
    public GameObject SourceObject;

    #endregion

    // =====================================================================
    #region 攻擊空間

    /// <summary>
    /// 近戰判定世界起點。
    /// </summary>
    public Vector3 Origin;

    /// <summary>
    /// 近戰攻擊世界方向。
    ///
    /// 呼叫前應 Normalize。
    /// </summary>
    public Vector3 Forward;

    #endregion

    // =====================================================================
    #region 傷害

    /// <summary>
    /// 每一個合法目標受到的要求傷害。
    /// </summary>
    public float Damage;

    /// <summary>
    /// DamageRequest DamageType。
    /// </summary>
    public DamageType DamageType;

    /// <summary>
    /// 這次 Melee Query
    /// 對應哪一種 Combat Feedback。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// Tank Light
    /// → TankLightMelee
    ///
    /// Tank Heavy
    /// → TankHeavyMelee。
    ///
    /// ------------------------------------------------------------
    ///
    /// MeleeDamageResolver 不負責決定這個值。
    ///
    /// 呼叫它的技能最清楚自己正在執行什麼攻擊，
    /// 所以應該由呼叫端傳入。
    /// </summary>
    public CombatFeedbackId FeedbackId;

    /// <summary>
    /// 這次攻擊的 Sequence。
    ///
    /// 同一刀攻擊所有敵人
    /// 都應使用同一 Sequence。
    /// </summary>
    public int Sequence;

    /// <summary>
    /// 這次範圍近戰是否因攻擊本身的 Gameplay 規則
    /// 被強制視為 Headshot。
    ///
    /// None：普通 Body Melee。
    /// TankHeavyMelee：Tank 第三段 Heavy。
    /// TankAirStrike：Tank GrappleAirborne Arrival Attack。
    ///
    /// Resolver 仍會把 HitZone 保存為 Body，
    /// 讓 DamageResult 能區分物理暴頭與 Forced Headshot。
    /// </summary>
    public DamageForcedHeadshotSource
        ForcedHeadshotSource;

    #endregion

    // =====================================================================
    #region 範圍

    /// <summary>
    /// 最大有效攻擊距離。
    /// </summary>
    public float Range;

    /// <summary>
    /// OverlapSphere 搜尋半徑。
    /// </summary>
    public float SearchRadius;

    /// <summary>
    /// 完整扇形角度。
    ///
    /// 例如：
    ///
    /// 120
    ///
    /// 代表：
///
/// 左 60
/// 右 60。
    /// </summary>
    public float FullAttackAngle;

    /// <summary>
    /// 最多真正傷害多少個不同目標。
    ///
    /// 0 或負數代表不限制。
    /// </summary>
    public int MaximumTargets;

    /// <summary>
    /// 可以被近戰搜尋的 Layer。
    /// </summary>
    public LayerMask HitMask;

    #endregion

    // =====================================================================
    #region Lag Compensation

    /// <summary>
    /// 是否使用 Subtick Accuracy。
    /// </summary>
    public bool UseSubtickAccuracy;

    /// <summary>
    /// 是否把普通 PhysX Collider
    /// 一起包含在 Query。
    /// </summary>
    public bool IncludePhysX;

    #endregion

    // =====================================================================
    #region LOS

    /// <summary>
    /// 是否檢查牆壁遮擋。
    /// </summary>
    public bool RequireLineOfSight;

    /// <summary>
    /// 可以阻擋近戰的 Layer。
    /// </summary>
    public LayerMask ObstructionMask;

    /// <summary>
    /// LOS Raycast 起點向前偏移量。
    /// </summary>
    public float ObstructionRayStartOffset;

    #endregion
}

/// <summary>
/// 一次近戰 Query 的統計結果。
///
/// 主要提供 Debug 與後續 Gameplay 使用。
/// </summary>
public struct MeleeDamageSummary
{
    /// <summary>
    /// Lag Compensation 原始命中數。
    /// </summary>
    public int RawHitCount;

    /// <summary>
    /// 去重與基本篩選後的合法候選數量。
    /// </summary>
    public int CandidateCount;

    /// <summary>
    /// 最後真正送進 DamageSystem 的目標數。
    /// </summary>
    public int AttemptedTargetCount;

    /// <summary>
    /// DamageResult.Accepted 的數量。
    /// </summary>
    public int ConfirmedDamageCount;

    /// <summary>
    /// 這次攻擊擊殺的目標數量。
    /// </summary>
    public int KillCount;
}

/// <summary>
/// 共用近戰傷害解析器。
///
/// ------------------------------------------------------------
///
/// 判定流程沿用既有 AttackQuickMelee：
///
/// OverlapSphere
/// ↓
/// 排除自己
/// ↓
/// IDamageReceiver
/// ↓
/// 排除死亡
/// ↓
/// 多 Hitbox 去重
/// ↓
/// Range
/// ↓
/// Angle
/// ↓
/// Distance Sort
/// ↓
/// LOS
/// ↓
/// DamageRequest。
///
/// ------------------------------------------------------------
///
/// 這個類別本身沒有 Network State。
///
/// 真正 Damage 仍應只由
/// 呼叫它的 State Authority 執行。
/// </summary>
public sealed class MeleeDamageResolver
{
    // =====================================================================
    #region Candidate

    private struct MeleeCandidate
    {
        public MonoBehaviour ReceiverBehaviour;

        public GameObject HitObject;

        public Vector3 TargetPoint;

        public float Distance;
    }

    #endregion

    // =====================================================================
    #region Runtime Cache

    private readonly List<LagCompensatedHit>
        overlapHits =
            new List<LagCompensatedHit>(32);

    private readonly List<MeleeCandidate>
        candidates =
            new List<MeleeCandidate>(24);

    private readonly HashSet<MonoBehaviour>
        uniqueReceivers =
            new HashSet<MonoBehaviour>();

    #endregion

    // =====================================================================
    #region Resolve

    /// <summary>
    /// 執行一次正式近戰判定。
    ///
    /// ------------------------------------------------------------
///
/// resolvedResults：
/// 所有真正送進 DamageSystem 的結果。
///
/// confirmedResults：
/// 只有 Accepted == true 的結果。
///
/// ------------------------------------------------------------
///
/// 兩個 List 都由呼叫端提供並重複使用，
/// 避免每一次近戰建立新的 List。
    /// </summary>
    public MeleeDamageSummary Resolve(
        in MeleeDamageQuery query,
        List<DamageResult> resolvedResults,
        List<DamageResult> confirmedResults
    )
    {
        MeleeDamageSummary summary =
            default;

        // =============================================================
        // 基本檢查
        // =============================================================

        if (query.Runner == null ||
            query.Runner.LagCompensation == null)
        {
            return summary;
        }

        if (query.Attacker.IsNone)
        {
            return summary;
        }

        if (query.Range <= 0f ||
            query.SearchRadius <= 0f)
        {
            return summary;
        }

        // =============================================================
        // 清除 Cache
        // =============================================================

        overlapHits.Clear();
        candidates.Clear();
        uniqueReceivers.Clear();

        resolvedResults?.Clear();
        confirmedResults?.Clear();

        // =============================================================
        // Forward
        // =============================================================

        Vector3 forward =
            query.Forward;

        if (forward.sqrMagnitude <=
            0.0001f)
        {
            return summary;
        }

        forward.Normalize();

        // =============================================================
        // Search Center
        // =============================================================

        Vector3 searchCenter =
            query.Origin +
            forward *
            (query.Range * 0.5f);

        // =============================================================
        // Hit Options
        // =============================================================

        HitOptions hitOptions =
            HitOptions.IgnoreInputAuthority;

        if (query.UseSubtickAccuracy)
        {
            hitOptions |=
                HitOptions.SubtickAccuracy;
        }

        if (query.IncludePhysX)
        {
            hitOptions |=
                HitOptions.IncludePhysX;
        }

        // =============================================================
        // Lag Compensation
        // =============================================================

        int hitCount =
            query.Runner
                .LagCompensation
                .OverlapSphere(
                    searchCenter,
                    query.SearchRadius,
                    query.Attacker,
                    overlapHits,
                    query.HitMask,
                    hitOptions,
                    true,
                    QueryTriggerInteraction.Ignore
                );

        summary.RawHitCount =
            hitCount;

        if (hitCount <= 0)
        {
            return summary;
        }

        // =============================================================
        // Build Candidates
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
            // 排除自己
            // ---------------------------------------------------------

            NetworkObject hitNetworkObject =
                hitObject
                    .GetComponentInParent<
                        NetworkObject
                    >();

            if (query.SourceNetworkObject != null &&
                hitNetworkObject ==
                    query.SourceNetworkObject)
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
            // Dead Target
            // ---------------------------------------------------------

            if (TryGetLifeState(
                    receiverBehaviour,
                    out ICombatLifeState lifeState
                ))
            {
                if (lifeState.IsAlive == false)
                {
                    continue;
                }
            }

            // ---------------------------------------------------------
            // Target Point
            // ---------------------------------------------------------

            Vector3 targetPoint =
                hitObject.transform.position;

            Vector3 toTarget =
                targetPoint -
                query.Origin;

            float distance =
                toTarget.magnitude;

            if (distance <= 0.0001f ||
                distance > query.Range)
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
                Mathf.Clamp(
                    query.FullAttackAngle,
                    0f,
                    360f
                ) *
                0.5f;

            if (angle >
                allowedHalfAngle)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 同一敵人 Hitbox 去重
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

        summary.CandidateCount =
            candidates.Count;

        if (candidates.Count <= 0)
        {
            return summary;
        }

        // =============================================================
        // Distance Sort
        // =============================================================

        candidates.Sort(
            CompareCandidateDistance
        );

        // =============================================================
        // Maximum Targets
        // =============================================================

        int targetLimit;

        if (query.MaximumTargets <= 0)
        {
            /*
             * 0：
             *
             * 不限制。
             *
             * Tank Heavy / 特殊攻擊
             * 都可以使用。
             */
            targetLimit =
                candidates.Count;
        }
        else
        {
            targetLimit =
                Mathf.Min(
                    query.MaximumTargets,
                    candidates.Count
                );
        }

        // =============================================================
        // Damage
        // =============================================================

        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            if (summary.AttemptedTargetCount >=
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
                    query,
                    candidate.TargetPoint
                ))
            {
                /*
                 * 被牆擋住不佔 MaximumTargets 名額。
                 */
                continue;
            }

            summary.AttemptedTargetCount++;

            // ---------------------------------------------------------
            // Hit Direction
            // ---------------------------------------------------------

            Vector3 hitDirection =
                candidate.TargetPoint -
                query.Origin;

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

            DamageRequest request =
                new DamageRequest
                {
                    RequestedDamage =
                        query.Damage,

                    BaseDamage =
                        query.Damage,

                    DamageType =
                        query.DamageType,

                    /*
                    * 保留這次攻擊真正的 Feedback 身份。
                    *
                    * Resolver 不解析它，
                    * 只是忠實傳進 Damage Pipeline。
                    */
                    FeedbackId =
                        query.FeedbackId,

                    /*
                     * 範圍近戰目前一律視為 Body。
                     *
                     * 不因為 Overlap 剛好掃到 Head Hitbox
                     * 就自動算 Headshot。
                     */
                    HitZone =
                        DamageHitZoneType.Body,

                    /*
                     * Tank Heavy 與 Tank Air Strike
                     * 已經各自擁有平衡完成的 Damage。
                     *
                     * 這裡保持 1，代表 Forced Headshot
                     * 不會再額外乘傷害，只提供正式 Headshot 身分。
                     */
                    HeadshotDamageMultiplier =
                        1f,

                    /*
                     * 呼叫端最清楚這次是哪種攻擊。
                     * Resolver 只忠實把來源傳入 Damage Pipeline。
                     */
                    ForcedHeadshotSource =
                        query.ForcedHeadshotSource,

                    Attacker =
                        query.Attacker,

                    SourceNetworkObject =
                        query.SourceNetworkObject,

                    SourceObject =
                        query.SourceObject,

                    HitObject =
                        candidate.HitObject,

                    HitPoint =
                        candidate.TargetPoint,

                    HitNormal =
                        -hitDirection,

                    HitDirection =
                        hitDirection,

                    Distance =
                        candidate.Distance,

                    Sequence =
                        query.Sequence
                };

            // ---------------------------------------------------------
            // Damage System
            // ---------------------------------------------------------

            bool receiverFound =
                DamageReceiverUtility
                    .TryApplyDamage(
                        candidate.HitObject,
                        request,
                        out DamageResult result
                    );

            resolvedResults?.Add(
                result
            );

            if (receiverFound &&
                result.Accepted)
            {
                summary.ConfirmedDamageCount++;

                if (result.KilledTarget)
                {
                    summary.KillCount++;
                }

                confirmedResults?.Add(
                    result
                );
            }
        }

        return summary;
    }

    #endregion

    // =====================================================================
    #region Damage Receiver

    private static bool TryFindDamageReceiver(
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

    private static bool TryGetLifeState(
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
    #region Candidate

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
    #region LOS

    private static bool IsTargetObstructed(
        in MeleeDamageQuery query,
        Vector3 targetPoint
    )
    {
        if (query.RequireLineOfSight ==
            false)
        {
            return false;
        }

        if (query.ObstructionMask.value == 0)
        {
            return false;
        }

        PhysicsScene physicsScene =
            query.Runner
                .GetPhysicsScene();

        if (physicsScene.IsValid() ==
            false)
        {
            return false;
        }

        Vector3 toTarget =
            targetPoint -
            query.Origin;

        float distance =
            toTarget.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        Vector3 direction =
            toTarget /
            distance;

        Vector3 rayOrigin =
            query.Origin +
            direction *
            query.ObstructionRayStartOffset;

        float rayDistance =
            Mathf.Max(
                0f,
                distance -
                query.ObstructionRayStartOffset
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
            query.ObstructionMask,
            QueryTriggerInteraction.Ignore
        );
    }

    #endregion
}