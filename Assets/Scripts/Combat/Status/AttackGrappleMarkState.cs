using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy 身上的 Attack Grapple Mark 狀態。
///
/// ------------------------------------------------------------
///
/// 一個 Enemy 可以同時被多名 Attack 玩家標記。
///
/// 例如：
///
/// Enemy
/// ├─ Player A → 剩 2.1 秒
/// └─ Player B → 剩 4.6 秒
///
/// A 與 B 完全互不覆蓋。
///
/// ------------------------------------------------------------
///
/// 同一名玩家再次標記：
///
/// 不建立第二份 Mark。
///
/// 而是：
///
/// 直接把自己的倒數刷新。
///
/// ------------------------------------------------------------
///
/// 這支腳本同時實作：
///
/// IDamageRequestModifier
///
/// 所以所有走正式 DamageSystem 的攻擊：
///
/// Rifle
/// Melee
/// 未來 Ability
///
/// 都可以自動吃到 Mark。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class AttackGrappleMarkState :
    NetworkBehaviour,
    IDamageRequestModifier
{
    // =====================================================================
    #region Network Capacity

    /*
     * NetworkDictionary 的 Capacity
     * 必須是固定容量。
     *
     * 目前保留最多 8 名不同玩家
     * 同時對同一敵人持有 Attack Mark。
     *
     * 如果你的正式房間永遠只有 4 人，
     * 之後可以縮成 4。
     */
    private const int
        MaximumConcurrentMarkers =
            8;

    #endregion

    // =====================================================================
    #region Networked Marks

    /// <summary>
    /// Key：
    /// 哪一名 Attack 玩家。
    ///
    /// Value：
    /// 該玩家對這隻 Enemy 的 Mark
    /// 還能持續到哪一個 Fusion Tick。
    ///
    /// ------------------------------------------------------------
///
/// NetworkDictionary 使用固定 Capacity。
///
/// PlayerRef 與 TickTimer
/// 都是 Fusion 支援的 Network Type。
    /// </summary>
    [Networked, Capacity(MaximumConcurrentMarkers)]
    private NetworkDictionary<PlayerRef, TickTimer>
        ActiveMarks =>
            default;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，Attack Grapple Mark 被新增、刷新、到期，以及成功把一筆 Body Damage 強制轉成 Headshot 時，都會在 State Authority Console 顯示資訊。")]
    private bool debugMark =
        true;

    #endregion

    // =====================================================================
    #region Runtime Cache

    /// <summary>
    /// 清理過期 Mark 時使用。
    ///
    /// 先記錄需要刪除的 PlayerRef，
    /// 避免直接在 foreach NetworkDictionary 時修改集合。
    /// </summary>
    private readonly List<PlayerRef>
        expiredMarkers =
            new List<PlayerRef>(
                MaximumConcurrentMarkers
            );

    #endregion

    // =====================================================================
    #region Fusion

    public override void FixedUpdateNetwork()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        RemoveExpiredMarks();
    }

    #endregion

    // =====================================================================
    #region Apply Mark

    /// <summary>
    /// 對這個 Enemy 新增或刷新某名玩家的 Attack Mark。
    ///
    /// ------------------------------------------------------------
///
/// 第一次：
///
/// Player A
/// → 5 秒。
///
/// 過 3 秒再次勾中：
///
/// 不會變成兩層。
///
/// 而是重新：
///
/// Player A
/// → 5 秒。
    /// </summary>
    public bool ApplyOrRefreshMark(
        PlayerRef attacker,
        float duration
    )
    {
        // =============================================================
        // Authority
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return false;
        }

        // =============================================================
        // Player
        // =============================================================

        if (attacker.IsNone)
        {
            return false;
        }

        // =============================================================
        // Duration
        // =============================================================

        if (duration <= 0f)
        {
            return false;
        }

        /*
         * 新增之前先清掉已經過期的 Entry，
         * 避免過期 Mark 佔用固定 Capacity。
         */
        RemoveExpiredMarks();

        NetworkDictionary<PlayerRef, TickTimer>
            marks =
                ActiveMarks;

        TickTimer newTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                duration
            );

        // =============================================================
        // Refresh
        // =============================================================

        if (marks.TryGet(
                attacker,
                out _
            ))
        {
            marks[attacker] =
                newTimer;

            if (debugMark)
            {
                Debug.Log(
                    $"[Attack Grapple Mark] 標記已刷新。" +
                    $"\n目標：{name}" +
                    $"\nAttack Player：{attacker}" +
                    $"\nDuration：{duration:F2} 秒",
                    this
                );
            }

            return true;
        }

        // =============================================================
        // Capacity
        // =============================================================

        if (marks.Count >=
            marks.Capacity)
        {
            Debug.LogError(
                $"[{nameof(AttackGrappleMarkState)}] " +
                $"ActiveMarks 已達最大容量。" +
                $"\n目標：{name}" +
                $"\nCapacity：{marks.Capacity}" +
                $"\n無法加入 Player：{attacker}",
                this
            );

            return false;
        }

        // =============================================================
        // Add
        // =============================================================

        marks.Add(
            attacker,
            newTimer
        );

        if (debugMark)
        {
            Debug.Log(
                $"[Attack Grapple Mark] 新標記已加入。" +
                $"\n目標：{name}" +
                $"\nAttack Player：{attacker}" +
                $"\nDuration：{duration:F2} 秒" +
                $"\n目前 Mark 數量：{marks.Count}",
                this
            );
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region Query

    /// <summary>
    /// 判斷指定玩家目前是否仍然擁有
    /// 對這個 Enemy 的有效 Attack Mark。
    /// </summary>
    public bool HasActiveMark(
        PlayerRef attacker
    )
    {
        if (Runner == null ||
            attacker.IsNone)
        {
            return false;
        }

        NetworkDictionary<PlayerRef, TickTimer>
            marks =
                ActiveMarks;

        if (marks.TryGet(
                attacker,
                out TickTimer timer
            ) == false)
        {
            return false;
        }

        /*
         * ExpiredOrNotRunning：
         *
         * None
         * 或
         * 已經超過目標 Tick
         *
         * 都視為無效。
         */
        return
            timer.ExpiredOrNotRunning(
                Runner
            ) == false;
    }

    /// <summary>
    /// 取得指定玩家的剩餘 Mark 時間。
    ///
    /// 目前主要方便 Debug。
    ///
    /// 沒有 Mark 時回傳 0。
    /// </summary>
    public float GetRemainingMarkTime(
        PlayerRef attacker
    )
    {
        if (HasActiveMark(
                attacker
            ) == false)
        {
            return 0f;
        }

        NetworkDictionary<PlayerRef, TickTimer>
            marks =
                ActiveMarks;

        if (marks.TryGet(
                attacker,
                out TickTimer timer
            ) == false)
        {
            return 0f;
        }

        return
            timer.RemainingTime(
                Runner
            ) ?? 0f;
    }

    #endregion

    // =====================================================================
    #region Damage Modifier

    /// <summary>
    /// DamageRequest 正式進入 IDamageReceiver 前呼叫。
    ///
    /// ------------------------------------------------------------
///
/// 如果：
///
/// 1. Attacker 對這個 Enemy 有有效 Mark。
///
/// 2. 這一擊還不是 Headshot。
///
/// 那：
///
/// Body Damage
/// ↓
/// Forced Headshot
/// ↓
/// 套用該攻擊來源自己的
/// Headshot Damage Multiplier。
///
/// ------------------------------------------------------------
///
/// 如果本來就是物理 Headshot：
///
/// 不再額外乘一次。
    /// </summary>
    public void ModifyDamageRequest(
        ref DamageRequest request
    )
    {
        // =============================================================
        // 只在 State Authority 正式結算
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // =============================================================
        // 這個玩家沒有 Mark
        // =============================================================

        if (HasActiveMark(
                request.Attacker
            ) == false)
        {
            return;
        }

        // =============================================================
        // 本來已經是 Headshot
        // =============================================================

        /*
         * 例如 Rifle 真正射中 Head。
         *
         * RequestedDamage 已經在 AttackRifle
         * 乘過一次 Headshot Multiplier。
         *
         * 所以這裡絕對不能再乘。
         */
        if (request.IsHeadshot)
        {
            return;
        }

        // =============================================================
        // Headshot Multiplier
        // =============================================================

        float effectiveMultiplier =
            request.HeadshotDamageMultiplier;

        /*
         * 某些 Damage Source
         * 可能根本沒有暴頭倍率概念。
         *
         * 如果沒有設定或 <= 0，
         * 就使用 1。
         *
         * 仍然會被「算成暴頭」，
         * 但不會憑空把傷害變成 0。
         */
        if (effectiveMultiplier <= 0f)
        {
            effectiveMultiplier =
                1f;
        }

        // =============================================================
        // Apply
        // =============================================================

        request.RequestedDamage *=
            effectiveMultiplier;

        request.ForcedHeadshotSource =
            DamageForcedHeadshotSource
                .AttackGrappleMark;

        // =============================================================
        // Debug
        // =============================================================

        if (debugMark)
        {
            Debug.Log(
                $"[Attack Grapple Mark] Damage 強制視為 Headshot。" +
                $"\n目標：{name}" +
                $"\nAttack Player：{request.Attacker}" +
                $"\n實際 Hit Zone：{request.HitZone}" +
                $"\nForced Source：{request.ForcedHeadshotSource}" +
                $"\nHeadshot Multiplier：{effectiveMultiplier:F2}" +
                $"\n新的 Requested Damage：{request.RequestedDamage:F2}" +
                $"\n剩餘 Mark：" +
                $"{GetRemainingMarkTime(request.Attacker):F2} 秒",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Cleanup

    /// <summary>
    /// 清除所有已經失效的 Attack Mark。
    /// </summary>
    private void RemoveExpiredMarks()
    {
        if (Object == null ||
            Object.HasStateAuthority == false ||
            Runner == null)
        {
            return;
        }

        expiredMarkers.Clear();

        NetworkDictionary<PlayerRef, TickTimer>
            marks =
                ActiveMarks;

        foreach (
            KeyValuePair<PlayerRef, TickTimer>
                pair
            in marks
        )
        {
            if (pair.Value.ExpiredOrNotRunning(
                    Runner
                ))
            {
                expiredMarkers.Add(
                    pair.Key
                );
            }
        }

        for (int i = 0;
             i < expiredMarkers.Count;
             i++)
        {
            PlayerRef player =
                expiredMarkers[i];

            marks.Remove(
                player
            );

            if (debugMark)
            {
                Debug.Log(
                    $"[Attack Grapple Mark] 標記到期。" +
                    $"\n目標：{name}" +
                    $"\nAttack Player：{player}",
                    this
                );
            }
        }
    }

    #endregion
}