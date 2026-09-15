using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;


/// <summary>
/// 可掛在任一 Profession Runtime 的 GrappleAirborne 空中特殊能力。
///
/// ====================================================================
///
/// 第一階段規則：
///
/// 1. 玩家必須是 GrappleAirborne。
/// 2. 玩家必須按住 Aim。
/// 3. 技能不可處於冷卻。
/// 4. 每次最多持續 5 秒。
/// 5. 放開 Aim、落地、離開 GrappleAirborne 或時間到時結束。
/// 6. 技能結束後才開始完整 15 秒冷卻。
/// 7. 啟動瞬間記錄完整 KCC DynamicVelocity。
/// 8. Active 期間將記錄速度依倍率壓縮，形成整體慢動作飄移。
/// 9. Active 期間封鎖 KCC WASD Input Direction，但不封鎖 Look。
/// 10. 同 Runtime 若有 SupportSMG，Active 期間啟用其 Special Mode。
/// 11. 能力結束時完整返還啟動瞬間記錄的 DynamicVelocity。
/// 12. 能力結束時關閉可選的 SupportSMG Special Mode。
///
/// ====================================================================
///
/// 這支能力不直接修改 Transform，也不使用 Rigidbody。
///
/// Player 使用 Advanced KCC，因此完整速度壓縮與返還會透過：
///
/// KCC.Data.DynamicVelocity
/// ↓
/// KCC.SetDynamicVelocity(...)
///
/// 完成。
///
/// 水平 X / Z 與垂直 Y 會使用相同倍率，
/// 不再只限制向下墜落速度。
///
/// ====================================================================
///
/// SupportSMG Special Mode 目前統一處理：
///
/// SupportSMG 無限彈匣
/// SupportSMG 特殊射速
/// SupportSMG 零新增後座力
/// SupportSMG 特殊治療量。
/// </summary>
[DisallowMultipleComponent]
public class SupportAerialAbility :
    NetworkBehaviour,
    IPlayerProfessionRuntimeModule,
    IPlayerAbilityRuntimeModule,
    IPlayerMovementInputModifier
{
    /// <summary>
    /// 此能力由 GrappleAirborne 專注輸入管線驅動。
    /// </summary>
    public PlayerAbilityCategory AbilityCategory =>
        PlayerAbilityCategory.GrappleFocus;

    // =====================================================================
    #region Ability Timing


    [Header("技能時間")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Support 空中特殊能力每次最多可維持的時間，單位為秒。預設 5 秒。放開 Aim 或落地仍會提早結束。")]
    private float maximumActiveDuration =
        5f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("技能正式結束後才開始計算的完整冷卻時間，單位為秒。依目前 B 規則預設為 15 秒；技能 Active 的時間不會吃掉這段冷卻。設為 0 代表結束後可立即再次使用。")]
    private float cooldownDuration =
        15f;


    #endregion


    // =====================================================================
    #region Velocity Compression


    [Header("速度壓縮")]


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("能力 Active 期間保留多少啟動瞬間的完整 KCC DynamicVelocity。X、Y、Z 三軸都會一起乘上這個倍率。預設 0.1 代表速度變成原本的 10%；0 代表完全暫停 DynamicVelocity；1 代表不壓縮。能力結束時會返還尚未乘上倍率的原始完整速度。")]
    private float activeVelocityMultiplier =
        0.1f;


    #endregion


    // =====================================================================
    #region Support Weapon


    [Header("可選的 Support 武器連動")]


    [SerializeField]
    [Tooltip("可選連動：同一顆 Profession Runtime Root 若存在 SupportSMG，能力啟動時會開啟其特殊射速、特殊治療量、無限彈匣與零新增後座力，能力結束時切回普通模式。其他職業不需要 SupportSMG；留空會嘗試從同一個 Runtime Root 取得，找不到也不會阻止空中能力運作。")]
    private SupportSMG supportSMG;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後會在 State Authority 顯示能力開始、結束原因與冷卻完成訊息。只在狀態切換時輸出，不會每個 Tick 洗 Console。測試階段建議保持開啟。")]
    private bool debugAbility =
        true;


    #endregion


    // =====================================================================
    #region Networked State


    /// <summary>
    /// 技能目前是否正在生效。
    ///
    /// 下一階段 SupportSMG 只應讀取這個正式狀態，
    /// 不應自行重新判斷 Aim、GrappleAirborne 或 Timer。
    /// </summary>
    [Networked]
    public NetworkBool AbilityActive
    {
        get;
        private set;
    }


    /// <summary>
    /// 本次技能 Active 的最長持續時間。
    /// </summary>
    [Networked]
    private TickTimer ActiveTimer
    {
        get;
        set;
    }


    /// <summary>
    /// 技能結束後才建立的完整冷卻計時器。
    ///
    /// 這就是 B 規則的核心：
    /// BeginAbility() 不會啟動這顆 Timer；
    /// EndAbility() 才會啟動。
    /// </summary>
    [Networked]
    private TickTimer CooldownTimer
    {
        get;
        set;
    }


    /// <summary>
    /// 能力啟動瞬間記錄的完整 KCC DynamicVelocity。
    ///
    /// Active 期間：
    ///
    /// StoredDynamicVelocity
    /// ×
    /// Active Velocity Multiplier
    ///
    /// ------------------------------------------------------------
    ///
    /// 能力結束：
    ///
    /// 將這份未壓縮速度完整返還給 KCC。
    /// </summary>
    [Networked]
    private Vector3 StoredDynamicVelocity
    {
        get;
        set;
    }


    #endregion


    // =====================================================================
    #region Runtime References


    /// <summary>
    /// 這顆 Support Profession Runtime 真正所屬的 Player Core。
    /// </summary>
    private Player ownerPlayer;


    /// <summary>
    /// Owner Player 的移動模組。
    ///
    /// 由這裡取得 Advanced KCC。
    /// </summary>
    private PlayerMovement ownerMovement;


    /// <summary>
    /// Owner Player 的正式移動狀態機。
    ///
    /// 能力只允許在 GrappleAirborne 啟動與維持。
    /// </summary>
    private PlayerStateMachine ownerStateMachine;


    /// <summary>
    /// Owner Player 的統一操作封鎖管理器。
    ///
    /// 共用能力直接讀 NetInput 的 Aim，因此仍要尊重 Action Gate 對 Aim
    /// 的封鎖，避免換彈、Quick Action 或其他高權限行為期間誤啟動。
    /// </summary>
    private PlayerActionGate ownerActionGate;


    /// <summary>
    /// Ability Definition 的職業限制是否允許目前職業使用。
    /// 預設 true 讓尚未搬離舊 Profession Runtime 的場景保持相容。
    /// </summary>
    private bool professionAvailable =
        true;


    /// <summary>
    /// NetworkBehaviour 是否已完成 Fusion Spawned。
    ///
    /// 防止 Spawned 前讀取 Networked Property。
    /// </summary>
    private bool fusionSpawned;


    #endregion


    // =====================================================================
    #region Public State


    /// <summary>
    /// 能力是否正在正式 Active。
    ///
    /// Spawned 前一律回傳 false。
    /// </summary>
    public bool IsAbilityActive =>
        fusionSpawned &&
        AbilityActive;


    /// <summary>
    /// 能力目前是否仍在冷卻。
    /// </summary>
    public bool IsCoolingDown
    {
        get
        {
            if (fusionSpawned == false ||
                Runner == null)
            {
                return false;
            }


            return
                (CooldownTimer.RemainingTime(
                    Runner
                ) ?? 0f) > 0f;
        }
    }


    /// <summary>
    /// 目前技能剩餘 Active 秒數。
    ///
    /// 未啟動時回傳 0。
    /// </summary>
    public float ActiveRemainingSeconds
    {
        get
        {
            if (IsAbilityActive == false ||
                Runner == null)
            {
                return 0f;
            }


            return Mathf.Max(
                0f,
                ActiveTimer.RemainingTime(
                    Runner
                ) ?? 0f
            );
        }
    }


    /// <summary>
    /// 目前技能剩餘冷卻秒數。
    ///
    /// 未冷卻時回傳 0。
    /// </summary>
    public float CooldownRemainingSeconds
    {
        get
        {
            if (fusionSpawned == false ||
                Runner == null)
            {
                return 0f;
            }


            return Mathf.Max(
                0f,
                CooldownTimer.RemainingTime(
                    Runner
                ) ?? 0f
            );
        }
    }

    /// <summary>
    /// 提供給 PlayerProfessionRuntimeManager 的共用移動倍率。
    ///
    /// Support Aerial Ability Active 期間回傳 0，
    /// 讓 PlayerMovement 與 GrappleAirborne Momentum
    /// 都知道目前不可接受玩家主動移動。
    ///
    /// 尚未 Active 時維持 1。
    /// 能力啟動的第一個 Tick 仍由既有 ApplyCompressedVelocity()
    /// 立即清除 InputDirection；從下一 Tick 開始由此接口正式封鎖。
    /// </summary>
    public float GetMovementInputMultiplier(
        NetInput input
    )
    {
        return IsAbilityActive
            ? 0f
            : 1f;
    }

    #endregion


    // =====================================================================
    #region Fusion


    private void Awake()
    {
        if (supportSMG == null)
        {
            supportSMG =
                GetComponent<SupportSMG>();
        }
    }


    public override void Spawned()
    {
        fusionSpawned =
            true;


        if (Object.HasStateAuthority)
        {
            AbilityActive =
                false;


            ActiveTimer =
                TickTimer.None;


            CooldownTimer =
                TickTimer.None;


            StoredDynamicVelocity =
                Vector3.zero;
        }
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        if (hasState &&
            AbilityActive)
        {
            EndAbility(
                "Ability Runtime Despawned"
            );
        }


        fusionSpawned =
            false;


        ownerPlayer =
            null;


        ownerMovement =
            null;


        ownerStateMachine =
            null;


        ownerActionGate =
            null;
    }


    #endregion


    // =====================================================================
    #region Owner Binding


    /// <summary>
    /// 由 PlayerProfessionRuntime 自動將這顆共用 Runtime Ability
    /// 綁定到真正 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;


        ownerMovement =
            null;


        ownerStateMachine =
            null;


        ownerActionGate =
            null;


        if (ownerPlayer == null)
        {
            return;
        }


        ownerMovement =
            ownerPlayer.Movement;


        ownerStateMachine =
            ownerPlayer.StateMachine;


        ownerActionGate =
            ownerPlayer.GetComponent<PlayerActionGate>();


        if (ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            Debug.LogError(
                $"[{nameof(SupportAerialAbility)}] " +
                $"Owner Player 找不到 PlayerMovement 或 Advanced KCC。" +
                $"\nPlayer：{ownerPlayer.name}",
                ownerPlayer
            );
        }


        if (ownerStateMachine == null)
        {
            Debug.LogError(
                $"[{nameof(SupportAerialAbility)}] " +
                $"Owner Player 找不到 PlayerStateMachine，" +
                $"無法判斷 GrappleAirborne。" +
                $"\nPlayer：{ownerPlayer.name}",
                ownerPlayer
            );
        }


        if (supportSMG == null)
        {
            supportSMG =
                GetComponent<SupportSMG>();
        }


        // SupportSMG 是可選連動，不是能力成立條件。
        // Attack、Tank 或未來職業只要掛上本元件即可使用空中能力。
    }


    #endregion


    // =====================================================================
    #region Simulation


    /// <summary>
    /// 每個 Fusion Tick 由 PlayerProfessionRuntime 自動呼叫。
    /// 直接讀取同一 Tick 的 Aim Input，因此任何職業 Runtime 只要掛上
    /// 此元件，不需要再修改自己的 Driver。
    /// </summary>
    public void Simulate(
        NetInput input
    )
    {
        if (fusionSpawned == false ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        if (professionAvailable == false)
        {
            if (AbilityActive)
            {
                EndAbility(
                    "Profession Restricted"
                );
            }

            return;
        }


        if (ownerMovement == null ||
            ownerMovement.KCC == null ||
            ownerStateMachine == null)
        {
            return;
        }


        ClearExpiredCooldown();


        bool isAimActive =
            IsAimInputActive(
                input
            );


        // =============================================================
        // 已經 Active
        // =============================================================

        if (AbilityActive)
        {
            TickActiveAbility(
                isAimActive
            );


            return;
        }


        // =============================================================
        // 尚未 Active：嘗試開始
        // =============================================================

        if (CanBeginAbility(
                isAimActive
            ) == false)
        {
            return;
        }


        BeginAbility();


        /*
         * 開始當 Tick 立即套用完整速度壓縮，
         * 不多等一個 Fusion Tick。
         */
        ApplyCompressedVelocity();
    }


    /// <summary>
    /// 獨立 Ability Runtime 的統一 Tick 入口。
    /// </summary>
    public void SimulateAbility(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        Simulate(
            input
        );
    }


    /// <summary>
    /// 切到不允許的職業時安全關閉能力；裝備與既有冷卻仍保留。
    /// </summary>
    public void SetProfessionAvailable(
        bool isAvailable
    )
    {
        professionAvailable =
            isAvailable;

        if (isAvailable == false &&
            fusionSpawned &&
            Object != null &&
            Object.HasStateAuthority &&
            AbilityActive)
        {
            EndAbility(
                "Profession Restricted"
            );
        }
    }


    /// <summary>
    /// 取得本 Tick 是否允許將 Aim 視為能力 Hold 輸入。
    ///
    /// 不依賴某一職業是否配置 PlayerAimController，才能維持真正的
    /// 掛載即用；同時仍尊重 PlayerActionGate 的 Aim 封鎖。
    /// </summary>
    private bool IsAimInputActive(
        NetInput input
    )
    {
        bool aimBlocked =
            ownerActionGate != null &&
            ownerActionGate.IsBlocked(
                PlayerActionBlockMask.Aim
            );

        return
            aimBlocked == false &&
            input.Buttons.IsSet(
                InputButton.Aim
            );
    }


    /// <summary>
    /// 檢查尚未啟動的能力是否可以開始。
    /// </summary>
    private bool CanBeginAbility(
        bool isAimActive
    )
    {
        if (isAimActive == false)
        {
            return false;
        }


        if (IsCoolingDown)
        {
            return false;
        }


        KCC kcc =
            ownerMovement.KCC;


        if (kcc.Data.IsGrounded)
        {
            return false;
        }


        return
            ownerStateMachine.CurrentState ==
            PlayerMovementState.GrappleAirborne;
    }


    /// <summary>
    /// 更新已經 Active 的能力。
    /// </summary>
    private void TickActiveAbility(
        bool isAimActive
    )
    {
        KCC kcc =
            ownerMovement.KCC;


        // =============================================================
        // 放開 Aim：提前結束
        // =============================================================

        if (isAimActive == false)
        {
            EndAbility(
                "Aim Released"
            );


            return;
        }


        // =============================================================
        // 落地：提前結束
        // =============================================================

        if (kcc.Data.IsGrounded)
        {
            EndAbility(
                "Grounded"
            );


            return;
        }


        // =============================================================
        // 不再是 GrappleAirborne：安全結束
        // =============================================================

        /*
         * 正常情況 GrappleAirborne 會維持到落地。
         *
         * 這個檢查是防止其他高權限移動能力、死亡、
         * 傳送或未來狀態切換後，Support 飄落仍殘留。
         */
        if (ownerStateMachine.CurrentState !=
            PlayerMovementState.GrappleAirborne)
        {
            EndAbility(
                "Left GrappleAirborne"
            );


            return;
        }


        // =============================================================
        // 5 秒到：正常結束
        // =============================================================

        if (ActiveTimer.Expired(Runner))
        {
            EndAbility(
                "Maximum Duration"
            );


            return;
        }


        // =============================================================
        // Active：維持完整速度壓縮
        // =============================================================

        ApplyCompressedVelocity();
    }


    #endregion


    // =====================================================================
    #region Begin / End


    /// <summary>
    /// 正式啟動能力。
    ///
    /// 注意：這裡只建立 ActiveTimer，
    /// 不建立 CooldownTimer。
    /// </summary>
    private void BeginAbility()
    {
        KCC kcc =
            ownerMovement.KCC;


        // =============================================================
        // 先保存「尚未壓縮」的完整 DynamicVelocity
        // =============================================================

        /*
         * 必須在 AbilityActive 設為 true、
         * 以及第一次 ApplyCompressedVelocity() 以前保存。
         *
         * 否則後面可能誤把已乘上 0.1 的速度
         * 當成真正原始速度，解除時就無法完整返還。
         */
        StoredDynamicVelocity =
            kcc.Data.DynamicVelocity;


        AbilityActive =
            true;


        ActiveTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    0.01f,
                    maximumActiveDuration
                )
            );


        // =============================================================
        // 同一 Tick 啟用 SupportSMG Special Mode
        // =============================================================

        /*
         * PlayerProfessionRuntime 的順序必須保持：
         *
         * SupportAerialAbility
         * ↓
         * Profession Runtime Driver
         * ↓
         * Weapon。
         *
         * 因此玩家啟動能力的同一個 Tick，
         * 後面的 SupportSMG 射擊就會立即使用：
         *
         * Special Fire Rate
         * Special Heal Amount
         * Infinite Magazine
         * No New Recoil。
         */
        if (supportSMG != null)
        {
            supportSMG.SetSpecialModeActive(
                true
            );
        }


        if (debugAbility)
        {
            Debug.Log(
                $"[Support Aerial Ability] Active" +
                $"\nPlayer：{ownerPlayer.name}" +
                $"\nMaximum Duration：{maximumActiveDuration:0.###} 秒" +
                $"\nStored Dynamic Velocity：{StoredDynamicVelocity}" +
                $"\nActive Velocity Multiplier：{activeVelocityMultiplier:0.###}",
                this
            );
        }
    }


    /// <summary>
    /// 結束能力，並從這一刻才開始完整冷卻。
    /// </summary>
    private void EndAbility(
        string reason
    )
    {
        if (AbilityActive == false)
        {
            return;
        }


        KCC kcc =
            ownerMovement != null
                ? ownerMovement.KCC
                : null;


        Vector3 velocityToRestore =
            StoredDynamicVelocity;


        AbilityActive =
            false;


        ActiveTimer =
            TickTimer.None;


        // =============================================================
        // ★ B 規則：結束後才開始完整冷卻
        // =============================================================

        if (cooldownDuration > 0f)
        {
            CooldownTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    cooldownDuration
                );
        }
        else
        {
            CooldownTimer =
                TickTimer.None;
        }


        // =============================================================
        // 完整返還能力啟動瞬間保存的 DynamicVelocity
        // =============================================================

        /*
         * 不使用：
         *
         * 目前壓縮速度 ÷ 倍率。
         *
         * 因為倍率可能為 0，
         * 而且其他系統也可能在 Active 期間碰過 KCC。
         *
         * ------------------------------------------------------------
         *
         * 這裡只信任 BeginAbility() 保存的正式原始速度。
         *
         * 所以解除能力時，水平與垂直動量都會直接回到
         * 技能啟動瞬間的完整數值。
         */
        if (kcc != null)
        {
            kcc.SetDynamicVelocity(
                velocityToRestore
            );
        }


        // =============================================================
        // 同一 Tick 關閉 SupportSMG Special Mode
        // =============================================================

        /*
         * 放開 Aim、落地、離開 GrappleAirborne 或時間到，
         * 都會走同一個 EndAbility()。
         *
         * 所以不會留下某一條提前結束路徑
         * 忘記恢復普通 SMG 狀態。
         */
        if (supportSMG != null)
        {
            supportSMG.SetSpecialModeActive(
                false
            );
        }


        StoredDynamicVelocity =
            Vector3.zero;


        if (debugAbility)
        {
            Debug.Log(
                $"[Support Aerial Ability] End" +
                $"\nPlayer：{ownerPlayer.name}" +
                $"\nReason：{reason}" +
                $"\nRestored Dynamic Velocity：{velocityToRestore}" +
                $"\nCooldown Started：{cooldownDuration:0.###} 秒",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Velocity Compression


    /// <summary>
    /// Active 期間把啟動瞬間保存的完整 DynamicVelocity
    /// 乘上 Inspector 設定倍率後重新套用，
    /// 並在同一 Tick 把 KCC Input Direction 設為零。
    ///
    /// 例如：
    ///
    /// Stored Dynamic Velocity = (20, -5, 10)
    /// Active Velocity Multiplier = 0.1
    ///
    /// Active Dynamic Velocity = (2, -0.5, 1)。
    ///
    /// ------------------------------------------------------------
    ///
    /// 每個 Tick 都從 StoredDynamicVelocity 重新計算，
    /// 不會拿上一 Tick 已壓縮的速度再次乘上倍率。
    ///
    /// 否則 0.1 會變成：
    ///
    /// 0.1
    /// 0.01
    /// 0.001
    ///
    /// 最後錯誤趨近於完全停止。
    ///
    /// ------------------------------------------------------------
    ///
    /// KCC.SetInputDirection(Vector3.zero)
    /// 只封鎖 WASD 移動方向。
    ///
    /// 不修改 PlayerMovement 的 Look Input，
    /// 所以玩家 Active 期間仍可正常轉動視角、瞄準與射擊。
    /// </summary>
    private void ApplyCompressedVelocity()
    {
        KCC kcc =
            ownerMovement.KCC;


        float multiplier =
            Mathf.Clamp01(
                activeVelocityMultiplier
            );


        Vector3 compressedVelocity =
            StoredDynamicVelocity *
            multiplier;


        kcc.SetDynamicVelocity(
            compressedVelocity
        );


        // =============================================================
        // Active 期間封鎖 WASD
        // =============================================================

        /*
         * PlayerMovement 已在本 Tick 前面將 WASD Input Direction
         * 傳入 KCC。
         *
         * Support Profession Runtime 排在 PlayerMovement 後面執行，
         * 所以這裡用零方向覆寫即可讓本 Tick 的 WASD 不產生位移。
         *
         * 能力結束後不再執行這行；
         * 下一 Tick PlayerMovement 會自然重新寫入玩家輸入，
         * 不需要額外保存或返還 WASD Direction。
         */
        kcc.SetInputDirection(
            Vector3.zero
        );
    }


    #endregion


    // =====================================================================
    #region Cooldown


    /// <summary>
    /// 冷卻到期後把 Timer 清回 None。
    ///
    /// 這不是啟動必要條件，但能讓 Networked State
    /// 與之後 UI / Debug 顯示保持乾淨。
    /// </summary>
    private void ClearExpiredCooldown()
    {
        if (CooldownTimer.Expired(Runner) ==
            false)
        {
            return;
        }


        CooldownTimer =
            TickTimer.None;


        if (debugAbility)
        {
            Debug.Log(
                $"[Support Aerial Ability] Cooldown Ready" +
                $"\nPlayer：{ownerPlayer.name}",
                this
            );
        }
    }


    #endregion
}
