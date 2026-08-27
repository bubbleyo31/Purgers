using Fusion;
using System;
using UnityEngine;

/// <summary>
/// 玩家受到一次正式傷害後，
/// 提供給「玩家本人 Presentation」使用的傷害資訊。
///
/// ====================================================================
///
/// 這不是 DamageRequest，也不是 DamageResult。
///
/// 這份資料的目的主要是：
///
/// 受傷 UI
/// 受傷 Camera FX
/// 傷害方向提示
/// 傷害來源提示
/// 低血量提示
/// 未來死亡畫面。
///
/// ====================================================================
///
/// 特別保留：
///
/// SourceNetworkObject
/// SourceWorldPosition
/// DirectionToSource
///
/// 讓之後可以建立：
///
/// 「傷害到底從玩家哪一邊來」
///
/// 的方向提示 UI。
/// </summary>
public struct PlayerDamageReceivedInfo
{
    // =====================================================================
    #region 傷害數值

    /// <summary>
    /// Guard / Shield 等 Modifier 處理完之後，
    /// 最後要求 PlayerHealth 承受多少傷害。
    /// </summary>
    public float RequestedDamage;

    /// <summary>
    /// 玩家 HP 真正減少多少。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// 剩餘 HP = 20
    /// Incoming Damage = 100
    ///
    /// 真正 Applied Damage 只會是 20。
    /// </summary>
    public float AppliedDamage;

    /// <summary>
    /// 在進入 PlayerHealth 前
    /// 已經被 Guard / Shield 等系統阻擋多少傷害。
    /// </summary>
    public float BlockedDamage;

    /// <summary>
    /// 這次是否曾經被部分阻擋。
    /// </summary>
    public bool WasBlocked;

    #endregion

    // =====================================================================
    #region 傷害類型

    /// <summary>
    /// Bullet / Melee / Ability 等
    /// Gameplay 傷害分類。
    /// </summary>
    public DamageType DamageType;

    /// <summary>
    /// 更精確的攻擊身份。
    ///
    /// 例如：
    ///
    /// AttackRifle
    /// TankHeavyMelee。
    ///
    /// 未來如果需要不同受傷效果
    /// 可以使用。
    /// </summary>
    public CombatFeedbackId FeedbackId;

    #endregion

    // =====================================================================
    #region 攻擊來源身份

    /// <summary>
    /// 如果傷害來源是玩家，
    /// 這裡是該玩家的 Fusion PlayerRef。
    ///
    /// AI / 環境傷害可能是 PlayerRef.None。
    /// </summary>
    public PlayerRef Attacker;

    /// <summary>
    /// 造成這次傷害的 NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來 Enemy Attack 建立 DamageRequest 時，
    /// 建議一定要把：
    ///
    /// SourceNetworkObject
    ///
    /// 設成 Enemy 的 NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如此傷害方向 UI 未來可以繼續追蹤
    /// 那隻敵人的實際位置。
    /// </summary>
    public NetworkObject SourceNetworkObject;

    #endregion

    // =====================================================================
    #region 傷害來源位置

    /// <summary>
    /// 產生傷害當下，
    /// 是否成功取得傷害來源世界位置。
    /// </summary>
    public bool HasSourceWorldPosition;

    /// <summary>
    /// 傷害發生當下的來源世界位置快照。
    ///
    /// ------------------------------------------------------------
    ///
    /// 即使：
    ///
    /// Projectile Destroy
    /// Enemy Death
    /// SourceNetworkObject 消失
    ///
    /// 這個位置仍然可以用來顯示
    /// 當下傷害方向。
    /// </summary>
    public Vector3 SourceWorldPosition;

    /// <summary>
    /// 從「受傷玩家」指向「傷害來源」的世界方向。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// (0, 0, -1)
    ///
    /// 代表來源在玩家世界 Z 負方向。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來 UI 只需要把這個世界方向
    /// 轉成 Camera Local Direction，
    /// 就能決定：
///
/// 上
/// 下
/// 左
/// 右
/// 後方。
    /// </summary>
    public Vector3 DirectionToSource;

    /// <summary>
    /// 真正命中玩家的世界位置。
    /// </summary>
    public Vector3 HitPoint;

    #endregion

    // =====================================================================
    #region 結果

    /// <summary>
    /// 這次傷害是否殺死玩家。
    /// </summary>
    public bool KilledPlayer;

    /// <summary>
    /// 攻擊 Sequence。
    ///
    /// 之後可以避免 Presentation
    /// 對同一傷害重複播放。
    /// </summary>
    public int Sequence;

    #endregion
}

/// <summary>
/// 全職業共用 Player Health。
///
/// ====================================================================
///
/// 這支腳本屬於：
///
/// Player Core。
///
/// Attack
/// Tank
/// Support
///
/// 都共用同一個 PlayerHealth。
///
/// ====================================================================
///
/// 正式傷害流程：
///
/// Enemy / Weapon / Ability
/// ↓
/// DamageRequest
/// ↓
/// PlayerIncomingDamageModifierBridge
/// ↓
/// Current Profession Runtime
/// ↓
/// 例如 TankGuardAbility
/// ↓
/// PlayerHealth.ReceiveDamage
/// ↓
/// HP 扣除。
///
/// ====================================================================
///
/// Tank 不會有 TankHealth。
///
/// Support 也不會有 SupportHealth。
///
/// 職業差異之後應透過：
///
/// Modifier
/// Buff
/// Profession Stats
///
/// 改變 PlayerHealth 的規則或最大值，
/// 而不是建立三套 Health。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerHealth :
    NetworkBehaviour,
    IDamageReceiver,
    ICombatLifeState
{
    // =====================================================================
    #region Health 設定

    [Header("生命值設定")]

    [SerializeField]
    [Min(1f)]
    [Tooltip("玩家目前基礎最大生命值。Attack、Tank、Support 現階段先共用這個值。之後如果職業、卡牌或升級會改最大生命，應透過 Runtime Modifier 或正式 Stats 系統修改，不要另外建立職業專屬 Health。")]
    private float maximumHealth =
        100f;

    [SerializeField]
    [Tooltip("開啟後，Player NetworkObject 第一次 Spawn 時會自動將 Current Health 設定為 Maximum Health。目前建議保持開啟。")]
    private bool startWithFullHealth =
        true;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，玩家受到正式傷害時會顯示原始進入傷害、實際 HP 損失、剩餘 HP、阻擋傷害、來源物件、來源位置與攻擊方向。Guard 測試階段建議開啟。")]
    private bool debugHealth =
        true;

    #endregion

    // =====================================================================
    #region Network Health State

    /// <summary>
    /// 玩家目前正式生命值。
    ///
    /// 只有 State Authority
    /// 可以正式修改。
    /// </summary>
    [Networked]
    public float CurrentHealth
    {
        get;
        private set;
    }

    /// <summary>
    /// 玩家是否死亡。
    ///
    /// ------------------------------------------------------------
    ///
    /// 使用 Networked State，
    /// 讓 Host / Client 都知道這名玩家是否已死亡。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前死亡只先保存狀態。
    ///
    /// 不會在這一步：
///
/// 關閉輸入
/// 播死亡動畫
/// 倒地
/// Respawn。
///
/// 那些之後再接。
    /// </summary>
    [Networked]
    private NetworkBool NetworkIsDead
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region Network Damage Source State

    /// <summary>
    /// 最後一次真正造成有效傷害的 PlayerRef。
    ///
    /// AI / 環境傷害可能為 None。
    /// </summary>
    [Networked]
    public PlayerRef LastDamageAttacker
    {
        get;
        private set;
    }

    /// <summary>
    /// 最後一次正式造成傷害的 NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 將來做：
    ///
    /// Damage Direction Indicator
    ///
    /// 時，如果這個 NetworkObject
    /// 還存在，就能持續取得它的位置。
    ///
    /// ------------------------------------------------------------
    ///
    /// Fusion 可以同步 NetworkObject Reference，
    /// 所以這裡可以安全使用 Networked NetworkObject。
    /// </summary>
    [Networked]
    public NetworkObject LastDamageSourceNetworkObject
    {
        get;
        private set;
    }

    /// <summary>
    /// 最後一次傷害是否成功取得來源位置。
    /// </summary>
    [Networked]
    public NetworkBool HasLastDamageSourcePosition
    {
        get;
        private set;
    }

    /// <summary>
    /// 最後一次傷害發生瞬間的來源位置快照。
    ///
    /// 即使 Source NetworkObject 之後消失，
    /// 這個位置仍然存在。
    /// </summary>
    [Networked]
    public Vector3 LastDamageSourcePosition
    {
        get;
        private set;
    }

    /// <summary>
    /// 最後一次傷害發生時，
    /// 從玩家指向傷害來源的世界方向。
    ///
    /// 將來傷害方向 UI
    /// 可以直接使用這份資料。
    /// </summary>
    [Networked]
    public Vector3 LastDamageDirectionToSource
    {
        get;
        private set;
    }

    /// <summary>
    /// 最後一次傷害的 Sequence。
    /// </summary>
    [Networked]
    public int LastDamageSequence
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Public Health Data

    /// <summary>
    /// 玩家最大生命值。
    /// </summary>
    public float MaximumHealth =>
        Mathf.Max(
            1f,
            maximumHealth
        );

    /// <summary>
    /// 玩家生命值百分比。
    ///
    /// 0 ~ 1。
    ///
    /// HUD 之後可以直接使用。
    /// </summary>
    public float HealthNormalized =>
        MaximumHealth > 0f
            ? Mathf.Clamp01(
                CurrentHealth /
                MaximumHealth
            )
            : 0f;

    /// <summary>
    /// ICombatLifeState。
    ///
    /// PlayerHealth 現在也可以被共用 Combat System
    /// 正確識別為 Alive / Dead。
    /// </summary>
    public bool IsAlive =>
        NetworkIsDead == false &&
        CurrentHealth > 0f;

    /// <summary>
    /// 玩家是否死亡。
    /// </summary>
    public bool IsDead =>
        NetworkIsDead;

    #endregion

    // =====================================================================
    #region Events

    /// <summary>
    /// State Authority 正式套用傷害後觸發。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這是 Gameplay Event。
    ///
    /// 未來可以接：
///
/// Threat
/// Aggro
/// Death System
/// Passive Ability
/// Statistics。
///
/// ------------------------------------------------------------
    ///
    /// 遠端 Client 不保證直接收到這個 C# Event。
    /// </summary>
    public event Action<DamageResult>
        DamageApplied;

    /// <summary>
    /// 只有「這名玩家本人」
    /// 會收到的受傷 Presentation Event。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來：
///
/// Damage Direction Indicator
/// Red Damage Vignette
/// Player Hurt Camera Shake
/// Hurt Sound
///
/// 應該訂閱這個事件。
///
/// ------------------------------------------------------------
///
/// 這份 Event 已經包含：
///
/// Source NetworkObject
/// Source Position
/// Direction To Source。
    /// </summary>
    public event Action<PlayerDamageReceivedInfo>
        LocalDamageReceived;

    /// <summary>
    /// 玩家正式死亡時由 State Authority 觸發。
    ///
    /// 下一階段死亡 / 倒地系統可以接這裡。
    /// </summary>
    public event Action
        Died;

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            if (startWithFullHealth)
            {
                CurrentHealth =
                    MaximumHealth;
            }
            else
            {
                CurrentHealth =
                    Mathf.Clamp(
                        CurrentHealth,
                        0f,
                        MaximumHealth
                    );
            }

            NetworkIsDead =
                CurrentHealth <= 0f;

            ClearLastDamageSourceState();
        }

        if (debugHealth)
        {
            Debug.Log(
                $"[Player Health] Spawned" +
                $"\nPlayer：{Object.InputAuthority}" +
                $"\nCurrent Health：{CurrentHealth:F1}" +
                $"\nMaximum Health：{MaximumHealth:F1}" +
                $"\nState Authority：{Object.HasStateAuthority}" +
                $"\nInput Authority：{Object.HasInputAuthority}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region IDamageReceiver

    /// <summary>
    /// 正式接收一次 Player Damage。
    ///
    /// ====================================================================
    ///
    /// 注意：
    ///
    /// 進到這裡以前，
    /// DamageReceiverUtility 已經先讓：
    ///
    /// PlayerIncomingDamageModifierBridge
    /// ↓
    /// TankGuardAbility
    ///
    /// 處理完成。
    ///
    /// 因此：
    ///
    /// request.RequestedDamage
    ///
    /// 已經是 Guard 等系統處理後的數值。
    /// </summary>
    public DamageResult ReceiveDamage(
        DamageRequest request
    )
    {
        // =============================================================
        // State Authority Only
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return
                DamageResult.CreateRejected(
                    request,
                    gameObject,
                    DamageRejectReason
                        .NotStateAuthority
                );
        }

        // =============================================================
        // 已死亡
        // =============================================================

        if (IsAlive == false)
        {
            return
                DamageResult.CreateRejected(
                    request,
                    gameObject,
                    DamageRejectReason
                        .AlreadyDead
                );
        }

        // =============================================================
        // 無效傷害
        // =============================================================

        float requestedDamage =
            Mathf.Max(
                0f,
                request.RequestedDamage
            );

        if (requestedDamage <= 0f)
        {
            return
                DamageResult.CreateRejected(
                    request,
                    gameObject,
                    DamageRejectReason
                        .InvalidRequest,

                    wasBlocked:
                        request.BlockedDamage > 0f
                );
        }

        // =============================================================
        // 真正 HP 損失
        // =============================================================

        /*
         * AppliedDamage 應代表：
         *
         * 「HP 真的掉了多少。」
         *
         * ------------------------------------------------------------
         *
         * 例如：
         *
         * Player HP = 15
         * Damage = 100
         *
         * HP 不可能真的掉 100，
         * 所以：
         *
         * AppliedDamage = 15。
         */
        float appliedDamage =
            Mathf.Min(
                CurrentHealth,
                requestedDamage
            );

        CurrentHealth =
            Mathf.Max(
                0f,
                CurrentHealth -
                appliedDamage
            );

        bool killedPlayer =
            CurrentHealth <= 0f;

        if (killedPlayer)
        {
            CurrentHealth =
                0f;

            NetworkIsDead =
                true;
        }

        bool wasBlocked =
            request.BlockedDamage >
            0.0001f;

        // =============================================================
        // 建立正式結果
        // =============================================================

        DamageResult result =
            DamageResult.CreateApplied(
                request,
                gameObject,
                appliedDamage,
                killedPlayer,
                wasBlocked
            );

        // =============================================================
        // 保存傷害來源
        // =============================================================

        DamageSourceSnapshot sourceSnapshot =
            BuildDamageSourceSnapshot(
                request
            );

        SaveLastDamageSource(
            request,
            sourceSnapshot
        );

        // =============================================================
        // Gameplay Event
        // =============================================================

        DamageApplied?.Invoke(
            result
        );

        // =============================================================
        // 送給受傷玩家本人
        // =============================================================

        /*
         * UI 不應該自己重新猜：
///
/// 誰打我？
/// 傷害從哪來？
///
/// PlayerHealth 在 Damage 發生的當下
/// 就把來源快照送過去。
         */
        RPC_ReceiveLocalDamage(
            requestedDamage,
            appliedDamage,
            Mathf.Max(
                0f,
                request.BlockedDamage
            ),
            wasBlocked,

            (byte)request.DamageType,
            (byte)request.FeedbackId,

            request.Attacker,
            request.SourceNetworkObject,

            sourceSnapshot.HasPosition,
            sourceSnapshot.Position,
            sourceSnapshot.DirectionToSource,

            request.HitPoint,

            killedPlayer,
            request.Sequence
        );

        // =============================================================
        // Debug
        // =============================================================

        if (debugHealth)
        {
            Debug.Log(
                $"[Player Health] Damage Applied" +
                $"\nPlayer：{Object.InputAuthority}" +
                $"\nRequested Damage：{requestedDamage:F2}" +
                $"\nApplied Damage：{appliedDamage:F2}" +
                $"\nBlocked Damage：{request.BlockedDamage:F2}" +
                $"\nWas Blocked：{wasBlocked}" +
                $"\nHealth：{CurrentHealth:F2} / {MaximumHealth:F2}" +
                $"\nKilled：{killedPlayer}" +
                $"\nDamage Type：{request.DamageType}" +
                $"\nFeedback ID：{request.FeedbackId}" +
                $"\nAttacker：{request.Attacker}" +
                $"\nSource NetworkObject：" +
                $"{(request.SourceNetworkObject != null ? request.SourceNetworkObject.name : "NULL")}" +
                $"\nHas Source Position：{sourceSnapshot.HasPosition}" +
                $"\nSource Position：{sourceSnapshot.Position}" +
                $"\nDirection To Source：{sourceSnapshot.DirectionToSource}" +
                $"\nSequence：{request.Sequence}",
                this
            );
        }

        // =============================================================
        // Death Event
        // =============================================================

        if (killedPlayer)
        {
            Died?.Invoke();
        }

        return result;
    }

    #endregion

    // =====================================================================
    #region Damage Source Snapshot

    /// <summary>
    /// PlayerHealth 內部使用的傷害來源快照。
    /// </summary>
    private struct DamageSourceSnapshot
    {
        public bool HasPosition;

        public Vector3 Position;

        public Vector3 DirectionToSource;
    }

    /// <summary>
    /// 嘗試取得這次傷害真正來源的位置與方向。
    ///
    /// ====================================================================
    ///
    /// 優先級：
    ///
    /// 1. SourceNetworkObject
    /// ↓
    /// 2. SourceObject
    /// ↓
    /// 3. HitDirection 反方向推算方向
    ///
    /// ====================================================================
    ///
    /// 為什麼還需要第三層？
    ///
    /// 有些攻擊來源可能是：
    ///
    /// Hitscan
    /// 已 Destroy Projectile
    /// Environment Damage。
    ///
    /// 即使沒有可以追蹤的 Source Object，
    /// 仍然可以從 HitDirection
    /// 知道攻擊大致從哪一邊飛來。
    /// </summary>
    private DamageSourceSnapshot
        BuildDamageSourceSnapshot(
            DamageRequest request
        )
    {
        DamageSourceSnapshot snapshot =
            default;

        Vector3 playerPosition =
            transform.position;

        // =============================================================
        // 1. Network Source
        // =============================================================

        if (request.SourceNetworkObject != null)
        {
            snapshot.HasPosition =
                true;

            snapshot.Position =
                request
                    .SourceNetworkObject
                    .transform
                    .position;

            snapshot.DirectionToSource =
                CalculateDirectionToSource(
                    playerPosition,
                    snapshot.Position
                );

            return snapshot;
        }

        // =============================================================
        // 2. GameObject Source
        // =============================================================

        if (request.SourceObject != null)
        {
            snapshot.HasPosition =
                true;

            snapshot.Position =
                request
                    .SourceObject
                    .transform
                    .position;

            snapshot.DirectionToSource =
                CalculateDirectionToSource(
                    playerPosition,
                    snapshot.Position
                );

            return snapshot;
        }

        // =============================================================
        // 3. Hit Direction Fallback
        // =============================================================

        /*
         * HitDirection：
         *
         * Attacker → Victim。
         *
         * ------------------------------------------------------------
         *
         * Damage Direction UI 要的是：
         *
         * Victim → Attacker。
         *
         * 所以取反。
         */
        if (request.HitDirection.sqrMagnitude >
            0.0001f)
        {
            snapshot.HasPosition =
                false;

            snapshot.Position =
                Vector3.zero;

            snapshot.DirectionToSource =
                -request
                    .HitDirection
                    .normalized;

            return snapshot;
        }

        // =============================================================
        // 完全不知道
        // =============================================================

        snapshot.HasPosition =
            false;

        snapshot.Position =
            Vector3.zero;

        snapshot.DirectionToSource =
            Vector3.zero;

        return snapshot;
    }

    private static Vector3
        CalculateDirectionToSource(
            Vector3 victimPosition,
            Vector3 sourcePosition
        )
    {
        Vector3 direction =
            sourcePosition -
            victimPosition;

        if (direction.sqrMagnitude <=
            0.0001f)
        {
            return Vector3.zero;
        }

        return
            direction.normalized;
    }

    /// <summary>
    /// 保存最後一次有效傷害來源。
    ///
    /// 這些是 Networked State，
    /// 所以其他需要 Gameplay 資料的系統
    /// 之後也能讀取。
    /// </summary>
    private void SaveLastDamageSource(
        DamageRequest request,
        DamageSourceSnapshot snapshot
    )
    {
        LastDamageAttacker =
            request.Attacker;

        LastDamageSourceNetworkObject =
            request.SourceNetworkObject;

        HasLastDamageSourcePosition =
            snapshot.HasPosition;

        LastDamageSourcePosition =
            snapshot.Position;

        LastDamageDirectionToSource =
            snapshot.DirectionToSource;

        LastDamageSequence =
            request.Sequence;
    }

    private void ClearLastDamageSourceState()
    {
        LastDamageAttacker =
            PlayerRef.None;

        LastDamageSourceNetworkObject =
            null;

        HasLastDamageSourcePosition =
            false;

        LastDamageSourcePosition =
            Vector3.zero;

        LastDamageDirectionToSource =
            Vector3.zero;

        LastDamageSequence =
            0;
    }

    #endregion

    // =====================================================================
    #region Local Damage RPC

    /// <summary>
    /// State Authority
    /// 將「受傷 Presentation 資料」
    /// 送給真正控制這名玩家的 Client。
    ///
    /// ------------------------------------------------------------
    ///
    /// NetworkObject 可以直接當 Fusion RPC 參數，
    /// Fusion 會以 NetworkId 傳輸。
    ///
    /// ------------------------------------------------------------
    ///
    /// 即使 sourceNetworkObject
    /// 在接收端已經無法解析，
    ///
    /// sourcePosition
    /// directionToSource
    ///
    /// 仍然保留傷害當下的方向快照。
    /// </summary>
    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.InputAuthority,
        TickAligned = false
    )]
    private void RPC_ReceiveLocalDamage(
        float requestedDamage,
        float appliedDamage,
        float blockedDamage,
        bool wasBlocked,

        byte damageType,
        byte feedbackId,

        PlayerRef attacker,
        NetworkObject sourceNetworkObject,

        bool hasSourcePosition,
        Vector3 sourcePosition,
        Vector3 directionToSource,

        Vector3 hitPoint,

        bool killedPlayer,
        int sequence
    )
    {
        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            return;
        }

        PlayerDamageReceivedInfo info =
            new PlayerDamageReceivedInfo
            {
                RequestedDamage =
                    requestedDamage,

                AppliedDamage =
                    appliedDamage,

                BlockedDamage =
                    blockedDamage,

                WasBlocked =
                    wasBlocked,

                DamageType =
                    (DamageType)damageType,

                FeedbackId =
                    (CombatFeedbackId)feedbackId,

                Attacker =
                    attacker,

                SourceNetworkObject =
                    sourceNetworkObject,

                HasSourceWorldPosition =
                    hasSourcePosition,

                SourceWorldPosition =
                    sourcePosition,

                DirectionToSource =
                    directionToSource,

                HitPoint =
                    hitPoint,

                KilledPlayer =
                    killedPlayer,

                Sequence =
                    sequence
            };

        LocalDamageReceived?.Invoke(
            info
        );

        if (debugHealth)
        {
            Debug.Log(
                $"[Player Health Local Damage]" +
                $"\nApplied Damage：{info.AppliedDamage:F2}" +
                $"\nBlocked Damage：{info.BlockedDamage:F2}" +
                $"\nDamage Type：{info.DamageType}" +
                $"\nAttacker：{info.Attacker}" +
                $"\nSource：" +
                $"{(info.SourceNetworkObject != null ? info.SourceNetworkObject.name : "NULL")}" +
                $"\nSource Position：{info.SourceWorldPosition}" +
                $"\nDirection To Source：{info.DirectionToSource}" +
                $"\nKilled：{info.KilledPlayer}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Health API

    /// <summary>
    /// 恢復玩家生命值。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這是給既有系統使用的簡化版本。
    ///
    /// 如果呼叫端只在乎：
    ///
    /// 有沒有成功恢復生命
    ///
    /// 可以繼續使用這個版本。
    ///
    /// ------------------------------------------------------------
    ///
    /// 真正的恢復計算統一交給：
    ///
    /// RestoreHealth(float amount, out float appliedAmount)
    ///
    /// 避免未來出現兩套不同的回血邏輯。
    /// </summary>
    public bool RestoreHealth(
        float amount
    )
    {
        return RestoreHealth(
            amount,
            out _
        );
    }

    /// <summary>
    /// 恢復玩家生命值，
    /// 並回傳這次「真正增加多少 HP」。
    ///
    /// ====================================================================
    ///
    /// requested amount 不一定等於 applied amount。
    ///
    /// 例如：
    ///
    /// Current Health = 95
    /// Maximum Health = 100
    /// Heal Amount = 20
    ///
    /// 最後真正恢復：
    ///
    /// Applied Amount = 5
    ///
    /// ====================================================================
    ///
    /// 這份資料之後可以直接提供給：
    ///
    /// Support Healing Number
    /// Healing Hit Marker
    /// 統計系統
    /// 任務計數
    /// Support 被動能力。
    ///
    /// ====================================================================
    ///
    /// 注意：
    ///
    /// Healing 不等於 Revive。
    ///
    /// 已死亡玩家不會因為這個 API
    /// 被重新救活。
    /// </summary>
    /// <param name="amount">
    /// 希望恢復的生命值。
    /// </param>
    /// <param name="appliedAmount">
    /// 這次真正增加的生命值。
    /// </param>
    /// <returns>
    /// true：實際增加了生命值。
    /// false：沒有增加生命值。
    /// </returns>
    public bool RestoreHealth(
        float amount,
        out float appliedAmount
    )
    {
        // =============================================================
        // 預設沒有成功治療
        // =============================================================

        appliedAmount =
            0f;

        // =============================================================
        // 1. 只有 State Authority 可以正式修改 HP
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return false;
        }

        // =============================================================
        // 2. 死亡玩家不能使用 Healing 復活
        // =============================================================

        if (IsAlive == false)
        {
            /*
            * 非常重要：
            *
            * Healing
            * ≠
            * Revive。
            *
            * ------------------------------------------------------------
            *
            * Support 普通治療子彈
            * 不能把死亡玩家直接拉回來。
            *
            * 未來如果要救人，
            * 應該走獨立的 Revive System。
            */
            return false;
        }

        // =============================================================
        // 3. 無效治療值
        // =============================================================

        if (amount <= 0f)
        {
            return false;
        }

        // =============================================================
        // 4. 保存治療前 HP
        // =============================================================

        float previousHealth =
            CurrentHealth;

        // =============================================================
        // 5. 正式恢復生命
        // =============================================================

        CurrentHealth =
            Mathf.Min(
                MaximumHealth,
                CurrentHealth +
                amount
            );

        // =============================================================
        // 6. 計算真正恢復量
        // =============================================================

        appliedAmount =
            Mathf.Max(
                0f,
                CurrentHealth -
                previousHealth
            );

        // =============================================================
        // 7. 是否真的有恢復
        // =============================================================

        return
            appliedAmount >
            0.0001f;
    }

    /// <summary>
    /// 測試 / Respawn 系統使用。
    ///
    /// 將生命值與死亡狀態完整重置。
    ///
    /// 正式死亡 / 復活流程完成後，
    /// 應由 Respawn System 呼叫這個入口，
    /// 不要讓 UI 自己修改 HP。
    /// </summary>
    public void ResetHealthToMaximum()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        CurrentHealth =
            MaximumHealth;

        NetworkIsDead =
            false;

        ClearLastDamageSourceState();
    }

    #endregion
}