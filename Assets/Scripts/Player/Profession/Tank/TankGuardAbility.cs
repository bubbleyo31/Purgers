using Fusion;
using UnityEngine;

/// <summary>
/// Tank 防禦目前主要階段。
/// </summary>
public enum TankGuardPhase : byte
{
    /// <summary>
    /// 沒有防禦。
    /// 如果其他條件允許，可以按住右鍵進入 Guard。
    /// </summary>
    Ready = 0,

    /// <summary>
    /// 正在防禦。
    /// </summary>
    Guarding = 1,

    /// <summary>
    /// 防禦耐力耗盡後的破防冷卻。
    ///
    /// 這個階段下一步接 Damage Pipeline 時才會真正使用。
    /// </summary>
    BrokenCooldown = 2
}

/// <summary>
/// Tank 職業右鍵防禦能力。
///
/// ====================================================================
///
/// 基本輸入：
///
/// Aim Button Hold
/// → Guard。
///
/// ====================================================================
///
/// Guard 可以：
///
/// Movement
/// Look
/// Jump
/// Grapple。
///
/// ====================================================================
///
/// Guard 不可以：
///
/// 在 Tank Melee 攻擊僵直中啟動。
///
/// Grappling 中啟動。
///
/// GrappleAirborne 中啟動。
///
/// Broken Cooldown 中啟動。
///
/// ====================================================================
///
/// Guard 本身存在：
///
/// TankProfessionRuntime
///
/// 而不是 Player Core。
///
/// 所以所有 Player 資料都透過
/// BindOwnerPlayer 取得。
/// </summary>
[DisallowMultipleComponent]
public class TankGuardAbility :
    NetworkBehaviour,
    IPlayerMovementInputModifier,
    IPlayerIncomingDamageModifier
{
    // =====================================================================
    #region Owner Player

    [Header("Owner Player Binding")]

    [SerializeField]
    [Tooltip("這個 Tank Guard Runtime 真正所屬的 Player Core。正常情況不需要手動指定，Tank Profession Runtime Driver 會在 Runtime Spawn 後自動綁定。")]
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 狀態機。
    /// </summary>
    private PlayerStateMachine
        ownerStateMachine;

    /// <summary>
    /// Owner Player 共用 Grapple。
    /// </summary>
    private PlayerGrapple
        ownerGrapple;

    /// <summary>
    /// Tank 同 Runtime 上的三段近戰。
    ///
    /// Guard 必須知道目前是不是攻擊僵直。
    /// </summary>
    private TankMeleeCombo
        meleeCombo;

    /// <summary>
    /// Tank F Quick Dash。
    /// Guard 用它確認 Dash 位移與 Dash 後攻防僵直是否仍在進行。
    /// </summary>
    private TankQuickDashAbility
        quickDashAbility;

    /// <summary>
    /// 目前真正 Owner Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    #endregion

    // =====================================================================
    #region Guard 移動設定

    [Header("Guard 移動設定")]

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Tank 正在防禦時保留多少普通移動速度。0.3 代表只保留原本 30%，也就是降低 70%。之後升級系統可以修改這個倍率。")]
    private float guardMovementInputMultiplier =
        0.3f;

    #endregion

    // =====================================================================
    #region Guard 耐力設定

    [Header("Guard 耐力設定")]

    [SerializeField]
    [Min(1f)]
    [Tooltip("Tank 防禦耐力最大值。目前先使用 100。下一階段接上 Damage Pipeline 後，被成功阻擋的傷害會消耗這個數值。")]
    private float maximumGuardStamina =
        100f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Guard 可以阻擋多少比例的進入傷害。0.7 代表理論上最多阻擋 70% 傷害。下一階段接入 Damage Pipeline 時才正式使用。")]
    private float guardDamageReduction =
        0.7f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家主動結束 Guard 且耐力尚未耗盡後，需要等待多少秒才開始恢復耐力。目前設計為 3 秒。")]
    private float staminaRecoveryDelay =
        3f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Guard 耐力每秒恢復最大耐力的比例。0.25 代表最大耐力 100 時每秒恢復 25。")]
    private float staminaRecoveryFractionPerSecond =
        0.25f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Guard 耐力完全耗盡後的破防冷卻時間，單位為秒。目前設計為 10 秒。冷卻完成後會直接恢復完整耐力。")]
    private float brokenCooldownDuration =
        10f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示 Guard 開始、結束、被勾索中斷、被攻擊狀態拒絕等主要狀態。")]
    private bool debugGuard =
        true;

    #endregion

    // =====================================================================
    #region Network State

    /// <summary>
    /// Guard 目前階段。
    /// </summary>
    [Networked]
    public TankGuardPhase CurrentPhase
    {
        get;
        private set;
    }

    /// <summary>
    /// Guard 目前剩餘耐力。
    ///
    /// 下一階段真正受到傷害時才會開始消耗。
    /// </summary>
    [Networked]
    public float CurrentStamina
    {
        get;
        private set;
    }

    /// <summary>
    /// 主動停止 Guard 後的耐力恢復等待時間。
    /// </summary>
    [Networked]
    private TickTimer StaminaRecoveryDelayTimer
    {
        get;
        set;
    }

    /// <summary>
    /// 破防冷卻。
    /// </summary>
    [Networked]
    private TickTimer BrokenCooldownTimer
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region Public State

    /// <summary>
    /// Tank 現在是否真正處於 Guard。
    /// </summary>
    public bool IsGuarding =>
        CurrentPhase ==
        TankGuardPhase.Guarding;

    /// <summary>
    /// 是否正在破防冷卻。
    /// </summary>
    public bool IsBrokenCooldown =>
        CurrentPhase ==
        TankGuardPhase.BrokenCooldown;

    /// <summary>
    /// Guard 最大耐力。
    /// </summary>
    public float MaximumGuardStamina =>
        maximumGuardStamina;

    /// <summary>
    /// Guard 傷害減免比例。
    ///
    /// 下一階段 Damage Bridge 會使用。
    /// </summary>
    public float GuardDamageReduction =>
        guardDamageReduction;

    /// <summary>
    /// 目前 Guard 耐力百分比。
    ///
    /// 未來 HUD 可以直接使用。
    /// </summary>
    public float StaminaNormalized =>
        maximumGuardStamina > 0f
            ? Mathf.Clamp01(
                CurrentStamina /
                maximumGuardStamina
            )
            : 0f;

    /// <summary>
    /// 破防剩餘秒數。
    /// </summary>
    public float BrokenCooldownRemainingSeconds =>
        Runner != null
            ? BrokenCooldownTimer
                .RemainingTime(Runner)
                ?? 0f
            : 0f;

    /// <summary>
    /// Tank Guard 在 Player Incoming Damage Pipeline
    /// 裡面的處理順序。
    ///
    /// 數字越小越早執行。
    ///
    /// 目前先使用 100。
    /// </summary>
    public int IncomingDamageModifierPriority =>
        100;

    #endregion

    // =====================================================================
    #region Binding

    /// <summary>
    /// 將 Tank Guard 綁定到真正的 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer,
        TankMeleeCombo runtimeMeleeCombo = null
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        ownerStateMachine =
            null;

        ownerGrapple =
            null;

        meleeCombo =
            runtimeMeleeCombo;

        quickDashAbility =
            null;

        if (ownerPlayer == null)
        {
            return;
        }

        ownerStateMachine =
            ownerPlayer.StateMachine;

        ownerGrapple =
            ownerPlayer.Grapple;

        if (meleeCombo == null)
        {
            meleeCombo =
                GetComponent<TankMeleeCombo>();
        }

        quickDashAbility = GetComponent<TankQuickDashAbility>();

        if (quickDashAbility == null)
        {
            Debug.LogError(
                $"[{nameof(TankGuardAbility)}] " +
                $"Tank Runtime 找不到 " +
                $"{nameof(TankQuickDashAbility)}，" +
                $"Guard 無法套用 Dash 後攻防僵直。",
                this
            );
        }

        if (ownerStateMachine == null)
        {
            Debug.LogError(
                $"[{nameof(TankGuardAbility)}] " +
                $"Owner Player 找不到 PlayerStateMachine。",
                ownerPlayer
            );
        }

        if (ownerGrapple == null)
        {
            Debug.LogError(
                $"[{nameof(TankGuardAbility)}] " +
                $"Owner Player 找不到 PlayerGrapple。",
                ownerPlayer
            );
        }

        if (meleeCombo == null)
        {
            Debug.LogError(
                $"[{nameof(TankGuardAbility)}] " +
                $"Tank Runtime 找不到 TankMeleeCombo。",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentPhase =
                TankGuardPhase.Ready;

            CurrentStamina =
                Mathf.Max(
                    1f,
                    maximumGuardStamina
                );

            StaminaRecoveryDelayTimer =
                TickTimer.None;

            BrokenCooldownTimer =
                TickTimer.None;
        }
    }

    #endregion

    // =====================================================================
    #region Simulation

    /// <summary>
    /// 每個 Fusion Tick 由
    /// TankProfessionRuntimeDriver 呼叫。
    /// </summary>
    public void Simulate(
        NetInput input
    )
    {
        if (ownerPlayer == null ||
            ownerStateMachine == null)
        {
            return;
        }

        // =============================================================
        // 1. Broken Cooldown
        // =============================================================

        TickBrokenCooldown();

        // =============================================================
        // 2. Stamina Recovery
        // =============================================================

        TickStaminaRecovery();

        // =============================================================
        // 3. Right Mouse Hold
        // =============================================================

        bool guardHeld =
            input.Buttons.IsSet(
                InputButton.Aim
            );

        // =============================================================
        // 4. 正在 Guard
        // =============================================================

        if (IsGuarding)
        {
            /*
             * 如果 Guard 期間開始 Grapple，
             * Guard 必須立即結束。
             */
            if (IsGrappleBlockingGuard())
            {
                EndGuard(
                    "Grapple"
                );

                return;
            }

            /*
             * 玩家放開右鍵。
             */
            if (guardHeld == false)
            {
                EndGuard(
                    "Aim Released"
                );

                return;
            }

            /*
             * 右鍵仍然按住，
             * Guard 繼續維持。
             */
            return;
        }

        // =============================================================
        // 5. 沒有按右鍵
        // =============================================================

        if (guardHeld == false)
        {
            return;
        }

        // =============================================================
        // 6. 嘗試進入 Guard
        // =============================================================

        if (CanStartGuard())
        {
            StartGuard();
        }
    }

    #endregion

    // =====================================================================
    #region Guard Start Conditions

    /// <summary>
    /// Tank 現在是否可以正式開始 Guard。
    /// </summary>
    private bool CanStartGuard()
    {
        // -------------------------------------------------------------
        // Quick Dash 位移／Dash 後 Recovery 禁止 Guard
        // -------------------------------------------------------------

        if (quickDashAbility != null &&
            quickDashAbility.IsCombatActionLocked)
        {
            return false;
        }
        
        // -------------------------------------------------------------
        // Broken Cooldown
        // -------------------------------------------------------------

        if (IsBrokenCooldown)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 沒有耐力
        // -------------------------------------------------------------

        if (CurrentStamina <= 0f)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 攻擊不可被 Guard 取消
        // -------------------------------------------------------------

        if (meleeCombo != null &&
            meleeCombo.IsAttackLocked)
        {
            return false;
        }

        // -------------------------------------------------------------
        // Grapple
        // -------------------------------------------------------------

        if (IsGrappleBlockingGuard())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Guard 耐力耗盡後正式進入破防。
    ///
    /// ------------------------------------------------------------
    ///
    /// 破防時：
    ///
    /// Guard 立即結束。
    /// Stamina = 0。
    /// 不進普通三秒恢復。
    /// 10 秒內不能再次 Guard。
    ///
    /// ------------------------------------------------------------
    ///
    /// 10 秒結束後：
    ///
    /// Stamina 直接回滿。
    /// Phase → Ready。
    /// </summary>
    private void EnterBrokenCooldown()
    {
        /*
        * 已經 Broken 就不用重複建立 Timer。
        */
        if (IsBrokenCooldown)
        {
            return;
        }

        CurrentStamina =
            0f;

        CurrentPhase =
            TankGuardPhase.BrokenCooldown;

        StaminaRecoveryDelayTimer =
            TickTimer.None;

        BrokenCooldownTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                brokenCooldownDuration
            );

        if (debugGuard &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Guard] Guard Broken" +
                $"\nCooldown：{brokenCooldownDuration:F2} 秒" +
                $"\nStamina：0",
                this
            );
        }
    }

    /// <summary>
    /// 是否正處於不能使用 Guard 的 Grapple 狀態。
    /// </summary>
    private bool IsGrappleBlockingGuard()
    {
        // =============================================================
        // Grapple Core
        // =============================================================

        /*
         * 使用 Grapple Core 的即時狀態，
         * 讓玩家剛按 Q 的同一個 Tick
         * Guard 就可以立即取消。
         */
        if (ownerGrapple != null &&
            ownerGrapple.IsGrappleControlActive)
        {
            return true;
        }

        // =============================================================
        // Grapple Momentum
        // =============================================================

        /*
         * 勾索剛結束的瞬間，
         * PlayerStateMachine 可能還要到 Tick 後段
         * 才正式切成 GrappleAirborne。
         *
         * 所以這裡額外檢查 Release Momentum。
         */
        if (ownerPlayer != null &&
            ownerPlayer
                .IsGrappleReleaseMomentumActive)
        {
            return true;
        }

        // =============================================================
        // Player State
        // =============================================================

        if (ownerStateMachine == null)
        {
            return false;
        }

        return
            ownerStateMachine.CurrentState ==
                PlayerMovementState.Grappling ||
            ownerStateMachine.CurrentState ==
                PlayerMovementState.GrappleAirborne;
    }

    #endregion

    // =====================================================================
    #region Start End

    private void StartGuard()
    {
        CurrentPhase =
            TankGuardPhase.Guarding;

        /*
         * Guard 再次開始後，
         * 尚未完成的自然恢復等待取消。
         */
        StaminaRecoveryDelayTimer =
            TickTimer.None;

        if (debugGuard &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Guard] Guard Started" +
                $"\nStamina：{CurrentStamina:F1}" +
                $"\nMovement Multiplier：{guardMovementInputMultiplier:F2}",
                this
            );
        }
    }

    private void EndGuard(
        string reason
    )
    {
        if (IsGuarding == false)
        {
            return;
        }

        CurrentPhase =
            TankGuardPhase.Ready;

        /*
         * 耐力不是滿的時候，
         * 才需要啟動三秒恢復等待。
         */
        if (CurrentStamina <
            maximumGuardStamina)
        {
            StaminaRecoveryDelayTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    staminaRecoveryDelay
                );
        }

        if (debugGuard &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Guard] Guard Ended" +
                $"\nReason：{reason}" +
                $"\nStamina：{CurrentStamina:F1}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Movement Modifier

    /// <summary>
    /// Player Core 在 Movement Simulation 前呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 為了讓「剛按右鍵」的同一個 Tick
    /// 就能開始減速，
    ///
    /// 這裡除了 IsGuarding，
    /// 也會直接檢查目前右鍵 Hold
    /// 是否符合 Guard 啟動條件。
    /// </summary>
    public float GetMovementInputMultiplier(
        NetInput input
    )
    {
        // =============================================================
        // 已經 Guard
        // =============================================================

        if (IsGuarding)
        {
            return
                Mathf.Clamp01(
                    guardMovementInputMultiplier
                );
        }

        // =============================================================
        // 這個 Tick 正準備開始 Guard
        // =============================================================

        bool guardHeld =
            input.Buttons.IsSet(
                InputButton.Aim
            );

        if (guardHeld &&
            CanStartGuard())
        {
            return
                Mathf.Clamp01(
                    guardMovementInputMultiplier
                );
        }

        return 1f;
    }

    #endregion

    // =====================================================================
    #region Stamina Recovery

    /// <summary>
    /// 目前先建立完整恢復邏輯。
    ///
    /// 下一階段 Guard 真正消耗 Stamina 後
    /// 就會直接開始工作。
    /// </summary>
    private void TickStaminaRecovery()
    {
        if (IsGuarding ||
            IsBrokenCooldown)
        {
            return;
        }

        if (CurrentStamina >=
            maximumGuardStamina)
        {
            CurrentStamina =
                maximumGuardStamina;

            StaminaRecoveryDelayTimer =
                TickTimer.None;

            return;
        }

        // -------------------------------------------------------------
        // 仍在三秒恢復等待
        // -------------------------------------------------------------

        if (StaminaRecoveryDelayTimer
            .ExpiredOrNotRunning(Runner) ==
            false)
        {
            return;
        }

        StaminaRecoveryDelayTimer =
            TickTimer.None;

        float recoveryPerSecond =
            maximumGuardStamina *
            staminaRecoveryFractionPerSecond;

        CurrentStamina =
            Mathf.Min(
                maximumGuardStamina,
                CurrentStamina +
                recoveryPerSecond *
                Runner.DeltaTime
            );
    }

    private void TickBrokenCooldown()
    {
        if (IsBrokenCooldown == false)
        {
            return;
        }

        if (BrokenCooldownTimer
            .Expired(Runner) == false)
        {
            return;
        }

        BrokenCooldownTimer =
            TickTimer.None;

        CurrentStamina =
            maximumGuardStamina;

        CurrentPhase =
            TankGuardPhase.Ready;

        if (debugGuard &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Guard] Broken Cooldown Finished" +
                $"\nStamina 已恢復到：{CurrentStamina:F1}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 未來 Quick Action API

    /// <summary>
    /// 未來 Tank F Quick Dash 使用。
    ///
    /// ------------------------------------------------------------
    ///
    /// 規則：
    ///
    /// Guard
    /// ↓
    /// F
    /// ↓
    /// Guard 結束
    /// ↓
    /// Dash。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前只是正式接口，
    /// 這一階段不會主動呼叫。
    /// </summary>
    public void ForceEndGuardForQuickAction()
    {
        if (IsGuarding == false)
        {
            return;
        }

        EndGuard(
            "Quick Action"
        );
    }

    #endregion

    // =====================================================================
    #region Incoming Damage

    /// <summary>
    /// Tank 正在 Guard 時修改進入玩家的傷害。
    ///
    /// ====================================================================
    ///
    /// 目前規則：
    ///
    /// Incoming Damage = 100
    /// Reduction = 70%
    /// Stamina = 100
    ///
    /// ↓
    ///
    /// 想阻擋 70
    /// 實際阻擋 70
    ///
    /// ↓
    ///
    /// RequestedDamage = 30
    /// Stamina = 30
    /// BlockedDamage += 70。
    ///
    /// ====================================================================
    ///
    /// 如果：
    ///
    /// Incoming Damage = 100
    /// Reduction = 70%
    /// Stamina = 20
    ///
    /// ↓
    ///
    /// 理論想擋 70
    /// 但只有 20 Stamina
    ///
    /// ↓
    ///
    /// 實際只擋 20
    ///
    /// RequestedDamage = 80
    /// Stamina = 0
    ///
    /// ↓
    ///
    /// Broken Cooldown 10 秒。
    ///
    /// ====================================================================
    ///
    /// 非常重要：
    ///
    /// 這裡只由 State Authority
    /// 真正修改 Stamina 與 Damage。
    /// </summary>
    public void ModifyIncomingDamage(
        ref DamageRequest request
    )
    {
        // =============================================================
        // State Authority Only
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // =============================================================
        // 必須真的正在 Guard
        // =============================================================

        if (IsGuarding == false)
        {
            return;
        }

        // =============================================================
        // 沒有耐力
        // =============================================================

        if (CurrentStamina <= 0f)
        {
            EnterBrokenCooldown();

            return;
        }

        // =============================================================
        // 進入傷害
        // =============================================================

        float incomingDamage =
            Mathf.Max(
                0f,
                request.RequestedDamage
            );

        if (incomingDamage <= 0f)
        {
            return;
        }

        // =============================================================
        // 理論阻擋量
        // =============================================================

        float reductionFraction =
            Mathf.Clamp01(
                guardDamageReduction
            );

        float desiredBlockedDamage =
            incomingDamage *
            reductionFraction;

        // =============================================================
        // 實際能阻擋多少
        // =============================================================

        /*
        * Guard Stamina 的單位目前直接與 Damage 相同。
        *
        * 例如：
        *
        * Stamina 20
        * 最多就只能再阻擋 20 Damage。
        */
        float actualBlockedDamage =
            Mathf.Min(
                desiredBlockedDamage,
                Mathf.Max(
                    0f,
                    CurrentStamina
                )
            );

        if (actualBlockedDamage <= 0f)
        {
            return;
        }

        // =============================================================
        // 修改 Damage Request
        // =============================================================

        request.RequestedDamage =
            Mathf.Max(
                0f,
                incomingDamage -
                actualBlockedDamage
            );

        /*
        * 使用 +=。
        *
        * 未來如果前面還有 Shield 等 Modifier，
        * BlockedDamage 可以正確累積。
        */
        request.BlockedDamage +=
            actualBlockedDamage;

        // =============================================================
        // 消耗 Guard Stamina
        // =============================================================

        CurrentStamina =
            Mathf.Max(
                0f,
                CurrentStamina -
                actualBlockedDamage
            );

        // =============================================================
        // Debug
        // =============================================================

        if (debugGuard)
        {
            Debug.Log(
                $"[Tank Guard] Incoming Damage Blocked" +
                $"\nIncoming Damage：{incomingDamage:F2}" +
                $"\nReduction：{reductionFraction:P0}" +
                $"\nDesired Block：{desiredBlockedDamage:F2}" +
                $"\nActual Block：{actualBlockedDamage:F2}" +
                $"\nRemaining Damage：{request.RequestedDamage:F2}" +
                $"\nRemaining Stamina：{CurrentStamina:F2}",
                this
            );
        }

        // =============================================================
        // Stamina 歸零 → Broken
        // =============================================================

        if (CurrentStamina <= 0.0001f)
        {
            EnterBrokenCooldown();
        }
    }

    #endregion

}