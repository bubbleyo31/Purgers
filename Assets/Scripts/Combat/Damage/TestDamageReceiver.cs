using Fusion;
using System;
using UnityEngine;

/// <summary>
/// 戰鬥生命系統測試實作。
///
/// ------------------------------------------------------------
///
/// 現在已經負責：
///
/// Damage
/// Heal
/// Death
/// Revive
/// Invulnerability
/// Networked Health
/// Combat Life State
///
/// ------------------------------------------------------------
///
/// 雖然目前仍叫 TestDamageReceiver，
/// 但架構已經非常接近未來正式 EnemyHealth。
///
/// ------------------------------------------------------------
///
/// 不負責：
///
/// 死亡動畫
/// AI 停止
/// 掉落物
/// 屍體消失
/// 血條 UI
/// Hit Marker
/// Camera Shake。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class TestDamageReceiver :
    NetworkBehaviour,
    IDamageReceiver,
    IHealthReceiver,
    ICombatLifeState
{
    // =====================================================================
    #region 生命值設定

    [Header("生命值設定")]

    [SerializeField]
    [Min(1f)]
    [Tooltip("這個測試戰鬥目標的最大生命值。State Authority 在 NetworkObject Spawned 時會使用這個值初始化 Current Health。Current Health 是正式 Networked Gameplay State。")]
    private float maxHealth =
        100f;

    #endregion

    // =====================================================================
    #region 無敵設定

    [Header("無敵設定")]

    [SerializeField]
    [Tooltip("NetworkObject 第一次生成時是否處於無敵狀態。真正執行期間的無敵狀態以 Networked Is Invulnerable 為準。")]
    private bool startInvulnerable =
        false;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會輸出 Damage、Heal、Death、Revive 與生命值變化資訊。")]
    private bool debugHealth =
        true;

    [SerializeField]
    [Tooltip("開啟後，如果非 State Authority 嘗試直接修改生命值、回血、復活或無敵狀態，會輸出警告。")]
    private bool debugAuthorityWarning =
        true;

    #endregion

    // =====================================================================
    #region Fusion 狀態

    /// <summary>
    /// 正式目前生命值。
    ///
    /// 這是整個生命系統唯一的生命狀態真相。
    /// </summary>
    [Networked]
    public float CurrentHealth
    {
        get;
        private set;
    }

    /// <summary>
    /// 正式無敵狀態。
    /// </summary>
    [Networked]
    public NetworkBool IsInvulnerable
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 公開生命狀態

    /// <summary>
    /// 最大生命值。
    /// </summary>
    public float MaxHealth =>
        Mathf.Max(
            1f,
            maxHealth
        );

    /// <summary>
    /// 目前生命比例。
    ///
    /// 0 = 死亡。
    /// 1 = 滿血。
    /// </summary>
    public float HealthNormalized
    {
        get
        {
            return Mathf.Clamp01(
                CurrentHealth /
                MaxHealth
            );
        }
    }

    /// <summary>
    /// 是否死亡。
    ///
    /// 不建立第二個 Networked IsDead。
    /// 直接由 CurrentHealth 推導。
    /// </summary>
    public bool IsDead =>
        CurrentHealth <= 0f;

    /// <summary>
    /// 是否存活。
    /// </summary>
    public bool IsAlive =>
        IsDead == false;

    /// <summary>
    /// 是否已經滿血。
    /// </summary>
    public bool IsFullHealth =>
        CurrentHealth >=
        MaxHealth;

    #endregion

    // =====================================================================
    #region Gameplay Events

    /// <summary>
    /// 生命值真正發生變化後觸發。
    ///
    /// 包含：
///
/// Damage
/// Heal
/// Revive
/// Reset。
///
/// ------------------------------------------------------------
///
/// 目前這是 Gameplay Event，
/// 會在真正修改 Health 的 State Authority 上觸發。
///
/// 未來 Client HUD 的同步顯示
/// 不應假設這個 C# Event 自動跨網路傳遞。
/// </summary>
    public event Action<HealthChangeEventData>
        HealthChanged;

    /// <summary>
    /// 目標由 Alive → Dead 時觸發一次。
    ///
    /// 只會在真正造成死亡的那一筆 Damage 後觸發。
    /// </summary>
    public event Action<CombatDeathEventData>
        Died;

    /// <summary>
    /// 目標由 Dead → Alive 時觸發一次。
    /// </summary>
    public event Action<CombatReviveEventData>
        Revived;

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        /*
         * 初始 Gameplay State
         * 只由 State Authority 建立。
         */
        if (Object.HasStateAuthority)
        {
            CurrentHealth =
                MaxHealth;

            IsInvulnerable =
                startInvulnerable;
        }

        if (debugHealth &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[{nameof(TestDamageReceiver)}] 已生成。" +
                $"\n物件：{name}" +
                $"\nHP：{CurrentHealth:F2} / {MaxHealth:F2}" +
                $"\nInvulnerable：{IsInvulnerable}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Damage

    /// <summary>
    /// 接收正式 DamageRequest。
    /// </summary>
    public DamageResult ReceiveDamage(
        DamageRequest request
    )
    {
        // =============================================================
        // Authority
        // =============================================================

        if (HasValidStateAuthority() ==
            false)
        {
            return DamageResult.CreateRejected(
                request,
                gameObject,
                DamageRejectReason.NotStateAuthority
            );
        }

        // =============================================================
        // Request
        // =============================================================

        if (IsValidPositiveNumber(
                request.RequestedDamage
            ) == false)
        {
            return DamageResult.CreateRejected(
                request,
                gameObject,
                DamageRejectReason.InvalidRequest
            );
        }

        // =============================================================
        // Dead
        // =============================================================

        if (IsDead)
        {
            return DamageResult.CreateRejected(
                request,
                gameObject,
                DamageRejectReason.AlreadyDead
            );
        }

        // =============================================================
        // Invulnerable
        // =============================================================

        if (IsInvulnerable)
        {
            return DamageResult.CreateRejected(
                request,
                gameObject,
                DamageRejectReason.Invulnerable,
                wasBlocked: false,
                wasImmune: true
            );
        }

        // =============================================================
        // Apply
        // =============================================================

        float healthBefore =
            CurrentHealth;

        float appliedDamage =
            Mathf.Min(
                request.RequestedDamage,
                healthBefore
            );

        CurrentHealth =
            Mathf.Clamp(
                healthBefore -
                appliedDamage,
                0f,
                MaxHealth
            );

        bool killedTarget =
            healthBefore > 0f &&
            IsDead;

        DamageResult result =
            DamageResult.CreateApplied(
                request,
                gameObject,
                appliedDamage,
                killedTarget
            );

        // =============================================================
        // Health Changed
        // =============================================================

        RaiseHealthChanged(
            HealthChangeType.Damage,
            healthBefore,
            CurrentHealth,
            request.Attacker,
            request.Sequence
        );

        // =============================================================
        // Death
        // =============================================================

        /*
         * 一定要等 CurrentHealth 正式變成 0
         * 之後才送出 Died。
         *
         * 所以訂閱者進入 Died 時：
         *
         * IsDead = true
         * IsAlive = false
         *
         * 已經成立。
         */
        if (killedTarget)
        {
            CombatDeathEventData deathData =
                new CombatDeathEventData
                {
                    TargetObject =
                        gameObject,

                    TargetNetworkObject =
                        Object,

                    KillingDamage =
                        result
                };

            Died?.Invoke(
                deathData
            );
        }

        if (debugHealth)
        {
            Debug.Log(
                $"[{nameof(TestDamageReceiver)}] Damage。" +
                $"\n物件：{name}" +
                $"\nRequested：{request.RequestedDamage:F2}" +
                $"\nApplied：{appliedDamage:F2}" +
                $"\nHP：{healthBefore:F2} → {CurrentHealth:F2}" +
                $"\nKilled：{killedTarget}",
                this
            );
        }

        return result;
    }

    #endregion

    // =====================================================================
    #region Heal

    /// <summary>
    /// 接收正式回血要求。
    ///
    /// 普通 Heal 永遠不能復活死亡目標。
    ///
    /// Dead
    /// ↓
    /// 必須使用 ReceiveRevive。
    /// </summary>
    public HealResult ReceiveHeal(
        HealRequest request
    )
    {
        // =============================================================
        // Authority
        // =============================================================

        if (HasValidStateAuthority() ==
            false)
        {
            return HealResult.CreateRejected(
                request,
                gameObject,
                HealRejectReason.NotStateAuthority
            );
        }

        // =============================================================
        // Request
        // =============================================================

        if (IsValidPositiveNumber(
                request.RequestedHeal
            ) == false)
        {
            return HealResult.CreateRejected(
                request,
                gameObject,
                HealRejectReason.InvalidRequest
            );
        }

        // =============================================================
        // Dead
        // =============================================================

        /*
         * 這條規則很重要。
         *
         * 普通回血：
         * 不可以把 HP 0 的角色變成 HP > 0。
         *
         * 復活必須經過獨立 Revive 流程。
         */
        if (IsDead)
        {
            return HealResult.CreateRejected(
                request,
                gameObject,
                HealRejectReason.TargetDead
            );
        }

        // =============================================================
        // Full Health
        // =============================================================

        if (IsFullHealth)
        {
            return HealResult.CreateRejected(
                request,
                gameObject,
                HealRejectReason.AlreadyFullHealth
            );
        }

        // =============================================================
        // Apply
        // =============================================================

        float healthBefore =
            CurrentHealth;

        float availableMissingHealth =
            MaxHealth -
            healthBefore;

        float appliedHeal =
            Mathf.Min(
                request.RequestedHeal,
                availableMissingHealth
            );

        CurrentHealth =
            Mathf.Clamp(
                healthBefore +
                appliedHeal,
                0f,
                MaxHealth
            );

        HealResult result =
            HealResult.CreateApplied(
                request,
                gameObject,
                appliedHeal
            );

        // =============================================================
        // Health Changed
        // =============================================================

        RaiseHealthChanged(
            HealthChangeType.Heal,
            healthBefore,
            CurrentHealth,
            request.Healer,
            request.Sequence
        );

        if (debugHealth)
        {
            Debug.Log(
                $"[{nameof(TestDamageReceiver)}] Heal。" +
                $"\n物件：{name}" +
                $"\nRequested：{request.RequestedHeal:F2}" +
                $"\nApplied：{appliedHeal:F2}" +
                $"\nHP：{healthBefore:F2} → {CurrentHealth:F2}",
                this
            );
        }

        return result;
    }

    #endregion

    // =====================================================================
    #region Revive

    /// <summary>
    /// 接收正式復活要求。
    ///
    /// 只有死亡目標可以進入這裡。
    ///
    /// Revive 成功後：
///
/// CurrentHealth > 0
/// IsDead = false
/// IsAlive = true
///
/// FocusTarget 也會自然重新允許鎖定。
    /// </summary>
    public ReviveResult ReceiveRevive(
        ReviveRequest request
    )
    {
        // =============================================================
        // Authority
        // =============================================================

        if (HasValidStateAuthority() ==
            false)
        {
            return ReviveResult.CreateRejected(
                request,
                gameObject,
                ReviveRejectReason.NotStateAuthority
            );
        }

        // =============================================================
        // Request
        // =============================================================

        if (IsValidPositiveNumber(
                request.RestoreHealth
            ) == false)
        {
            return ReviveResult.CreateRejected(
                request,
                gameObject,
                ReviveRejectReason.InvalidRequest
            );
        }

        // =============================================================
        // 必須已死亡
        // =============================================================

        if (IsAlive)
        {
            return ReviveResult.CreateRejected(
                request,
                gameObject,
                ReviveRejectReason.TargetAlreadyAlive
            );
        }

        // =============================================================
        // Apply
        // =============================================================

        float healthBefore =
            CurrentHealth;

        float restoredHealth =
            Mathf.Clamp(
                request.RestoreHealth,
                1f,
                MaxHealth
            );

        /*
         * 這行就是真正從：
         *
         * Dead
         *
         * 轉回：
         *
         * Alive
         *
         * 的唯一正式入口。
         */
        CurrentHealth =
            restoredHealth;

        ReviveResult result =
            ReviveResult.CreateApplied(
                request,
                gameObject,
                CurrentHealth
            );

        // =============================================================
        // Health Changed
        // =============================================================

        RaiseHealthChanged(
            HealthChangeType.Revive,
            healthBefore,
            CurrentHealth,
            request.Reviver,
            request.Sequence
        );

        // =============================================================
        // Revived
        // =============================================================

        CombatReviveEventData reviveData =
            new CombatReviveEventData
            {
                TargetObject =
                    gameObject,

                TargetNetworkObject =
                    Object,

                ReviveResult =
                    result
            };

        Revived?.Invoke(
            reviveData
        );

        if (debugHealth)
        {
            Debug.Log(
                $"[{nameof(TestDamageReceiver)}] Revive。" +
                $"\n物件：{name}" +
                $"\nHP：{healthBefore:F2} → {CurrentHealth:F2}",
                this
            );
        }

        return result;
    }

    #endregion

    // =====================================================================
    #region Invulnerability

    /// <summary>
    /// 修改無敵狀態。
    ///
    /// 只有 State Authority 可以修改。
    /// </summary>
    public bool SetInvulnerable(
        bool value
    )
    {
        if (HasValidStateAuthority() ==
            false)
        {
            return false;
        }

        IsInvulnerable =
            value;

        if (debugHealth)
        {
            Debug.Log(
                $"[{nameof(TestDamageReceiver)}] " +
                $"Invulnerable → {value}",
                this
            );
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region Test Reset

    /// <summary>
    /// 測試用滿血重設。
    ///
    /// 注意：
///
/// 如果目標還活著：
/// → 單純 Reset 到滿血。
///
/// 如果目標已死亡：
/// → 走正式 Revive 流程。
///
/// 這樣就不會偷偷繞過 Revive Event。
    /// </summary>
    public bool ResetHealthForTesting()
    {
        if (HasValidStateAuthority() ==
            false)
        {
            return false;
        }

        // =============================================================
        // 死亡 → 正式 Revive
        // =============================================================

        if (IsDead)
        {
            ReviveRequest reviveRequest =
                new ReviveRequest
                {
                    RestoreHealth =
                        MaxHealth,

                    Reviver =
                        default,

                    SourceNetworkObject =
                        null,

                    SourceObject =
                        null,

                    Sequence =
                        0
                };

            ReviveResult result =
                ReceiveRevive(
                    reviveRequest
                );

            return result.Accepted;
        }

        // =============================================================
        // 活著 → Reset
        // =============================================================

        float healthBefore =
            CurrentHealth;

        CurrentHealth =
            MaxHealth;

        RaiseHealthChanged(
            HealthChangeType.Reset,
            healthBefore,
            CurrentHealth,
            default,
            0
        );

        if (debugHealth)
        {
            Debug.Log(
                $"[{nameof(TestDamageReceiver)}] 測試 Reset。" +
                $"\nHP：{healthBefore:F2} → {CurrentHealth:F2}",
                this
            );
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region Health Event

    /// <summary>
    /// 統一觸發生命值變化事件。
    ///
    /// Damage / Heal / Revive / Reset
    /// 都一定從這裡發送。
    ///
    /// 這樣未來血條只需要訂閱：
    ///
    /// HealthChanged
    ///
    /// 不需要分別監聽四種系統。
    /// </summary>
    private void RaiseHealthChanged(
        HealthChangeType changeType,
        float previousHealth,
        float currentHealth,
        PlayerRef instigator,
        int sequence
    )
    {
        /*
         * 如果生命值完全沒變，
         * 不產生 HealthChanged。
         */
        if (Mathf.Approximately(
                previousHealth,
                currentHealth
            ))
        {
            return;
        }

        HealthChangeEventData data =
            new HealthChangeEventData
            {
                ChangeType =
                    changeType,

                PreviousHealth =
                    previousHealth,

                CurrentHealth =
                    currentHealth,

                MaxHealth =
                    MaxHealth,

                Delta =
                    currentHealth -
                    previousHealth,

                TargetObject =
                    gameObject,

                Instigator =
                    instigator,

                Sequence =
                    sequence
            };

        HealthChanged?.Invoke(
            data
        );
    }

    #endregion

    // =====================================================================
    #region Validation

    /// <summary>
    /// 目前是否具有修改這個生命系統的權限。
    /// </summary>
    private bool HasValidStateAuthority()
    {
        bool valid =
            Object != null &&
            Object.HasStateAuthority;

        if (valid == false &&
            debugAuthorityWarning)
        {
            Debug.LogWarning(
                $"[{nameof(TestDamageReceiver)}] " +
                $"非 State Authority 嘗試修改正式生命狀態。" +
                $"\n物件：{name}",
                this
            );
        }

        return valid;
    }

    /// <summary>
    /// 驗證 Damage / Heal / Revive 數值。
    ///
    /// 同時排除：
///
/// 0
/// 負數
/// NaN
/// Infinity。
    /// </summary>
    private bool IsValidPositiveNumber(
        float value
    )
    {
        if (float.IsNaN(
                value
            ))
        {
            return false;
        }

        if (float.IsInfinity(
                value
            ))
        {
            return false;
        }

        return value > 0f;
    }

    #endregion
}