using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻擊職業「專注」技能目前階段。
/// </summary>
public enum AttackFocusPhase : byte
{
    /// <summary>
    /// 專注目前沒有作用。
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 專注正式作用中。
    ///
    /// 注意：
    /// ADS 的 Entering 狀態已經由
    /// PlayerAimController 負責。
    /// </summary>
    Active = 1
}

/// <summary>
/// 專注技能結束原因。
/// </summary>
public enum AttackFocusEndReason : byte
{
    /// <summary>
    /// 尚未結束。
    /// </summary>
    None = 0,

    /// <summary>
    /// 玩家主動放開 Aim。
    ///
    /// 不進入冷卻。
    /// </summary>
    AimReleased = 1,

    /// <summary>
    /// 專注自然到達最大時間。
    ///
    /// 不進入冷卻。
    /// </summary>
    Timeout = 2,

    /// <summary>
    /// 玩家成功執行專注射擊。
    ///
    /// 會進入冷卻。
    /// </summary>
    Fired = 3,

    /// <summary>
    /// 玩家已經不再處於
    /// Grappling 或 GrappleAirborne。
    /// </summary>
    StateInvalid = 4,

    /// <summary>
    /// 玩家不再是 Attack 職業。
    /// </summary>
    ProfessionChanged = 5,

    /// <summary>
    /// 其他系統強制取消。
    /// </summary>
    ExternalCancel = 6
}

/// <summary>
/// Attack 職業專注技能。
///
/// 新版設計：
///
/// 專注是一個「高速空戰射擊輔助能力」，
/// 不再是一個移動能力。
///
/// 啟動條件：
///
/// Attack
/// +
/// Grappling 或 GrappleAirborne
/// +
/// Aim Held
///
/// 專注期間：
///
/// 玩家移動       不變
/// 勾索速度       不變
/// Grapple Momentum 不變
/// WASD           不變
/// Jump           不變
/// Camera         不變
/// Crosshair      不變
///
/// 唯一改變：
///
/// Focus Shot 可以把「子彈方向」
/// 修正到準心附近最接近的合法敵人。
///
/// 重要：
///
/// 如果原始準心本來就直接命中敵人，
/// 不會啟用 Auto Lock。
///
/// 這樣玩家自己真正瞄準頭部時，
/// 仍然可以得到 Headshot。
/// </summary>
[DisallowMultipleComponent]

/*
 * AttackRifle 與 PlayerAimController
 * 仍然屬於同一個 Attack Runtime，
 * 所以可以繼續 Require。
 *
 * PlayerProfession / PlayerStateMachine
 * 則屬於 Player Core，
 * 不能再 Require。
 */
[RequireComponent(typeof(AttackRifle))]
[RequireComponent(typeof(PlayerAimController))]
public class AttackFocusAbility :
    NetworkBehaviour
{
    // =====================================================================
    #region 核心引用

    [Header("核心引用")]
    [SerializeField]
    [Tooltip("玩家職業資料。只有 Attack 職業可以使用專注。若留空會自動取得。")]
    private PlayerProfession profession;

    [SerializeField]
    [Tooltip("玩家狀態機。專注只允許在 Grappling 或 GrappleAirborne 狀態使用。若留空會自動取得。")]
    private PlayerStateMachine stateMachine;

    [SerializeField]
    [Tooltip("攻擊職業步槍。專注會從步槍取得射擊起點、正常瞄準方向、最大射程與命中 Layer。若留空會自動取得。")]
    private AttackRifle attackRifle;

    [SerializeField]
    [Tooltip("玩家統一瞄準控制器。AttackFocusAbility 不再自行讀取右鍵，而是只有在 PlayerAimController 已經正式完成 ADS，IsAiming 為 true 時，才允許進入專注。若留空會自動取得。")]
    private PlayerAimController aimController;

    #endregion

    // =====================================================================
    #region Owner Player Binding

    /// <summary>
    /// Attack Focus 真正所屬的 Player Core。
    /// </summary>
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 的 NetworkObject。
    ///
    /// 用於排除自己的 Target / Collider。
    /// </summary>
    private NetworkObject ownerPlayerNetworkObject;

    /// <summary>
    /// 將 Attack Focus 綁定到 Player Core。
    ///
    /// 同時由 Runtime Driver
    /// 傳入同一個 Runtime 上的：
    ///
    /// AttackRifle
    /// PlayerAimController。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer,
        AttackRifle runtimeRifle = null,
        PlayerAimController runtimeAimController = null
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        if (ownerPlayer == null)
        {
            ownerPlayerNetworkObject =
                null;

            profession =
                null;

            stateMachine =
                null;

            return;
        }

        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        /*
        * Core Dependencies。
        */
        profession =
            ownerPlayer.Profession;

        stateMachine =
            ownerPlayer.StateMachine;

        /*
        * Runtime Dependencies。
        */
        if (runtimeRifle != null)
        {
            attackRifle =
                runtimeRifle;
        }
        else if (attackRifle == null)
        {
            attackRifle =
                GetComponent<AttackRifle>();
        }

        if (runtimeAimController != null)
        {
            aimController =
                runtimeAimController;
        }
        else if (aimController == null)
        {
            aimController =
                GetComponent<PlayerAimController>();
        }

        if (profession == null)
        {
            Debug.LogError(
                $"[{nameof(AttackFocusAbility)}] " +
                $"Owner Player 找不到 PlayerProfession。",
                ownerPlayer
            );
        }

        if (stateMachine == null)
        {
            Debug.LogError(
                $"[{nameof(AttackFocusAbility)}] " +
                $"Owner Player 找不到 PlayerStateMachine。",
                ownerPlayer
            );
        }
    }

    /// <summary>
    /// 取得真正的 Owner Player NetworkObject。
    /// </summary>
    private NetworkObject GetOwnerPlayerNetworkObject()
    {
        if (ownerPlayerNetworkObject != null)
        {
            return ownerPlayerNetworkObject;
        }

        if (ownerPlayer != null &&
            ownerPlayer.Object != null)
        {
            return ownerPlayer.Object;
        }

        if (stateMachine != null)
        {
            NetworkObject stateNetworkObject =
                stateMachine.GetComponent<NetworkObject>();

            if (stateNetworkObject != null)
            {
                return stateNetworkObject;
            }
        }

        return Object;
    }

    #endregion

    // =====================================================================
    #region 專注時間設定

    [Header("專注時間設定")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("一次專注最多持續多少秒。目前設計為 0.7 秒。時間到後會自動結束，而且不進入冷卻。")]
    private float maximumFocusDuration =
        0.7f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("專注期間成功主動射擊後，技能進入多少秒冷卻。目前設計為 3 秒。只有成功射擊才產生這個冷卻。")]
    private float focusCooldownAfterFire =
        3f;

    #endregion

    // =====================================================================
    #region 專注射擊設定

    [Header("專注射擊設定")]

    [SerializeField]
    [Min(1)]
    [Tooltip("專注射擊一次需要消耗多少發彈匣子彈。目前設計為 2。彈匣不足時不會成功射擊，也不會消耗專注或進入冷卻。")]
    private int focusAmmoCost =
        2;

    [SerializeField]
    [Min(0f)]
    [Tooltip("專注射擊傷害倍率。目前設計為 2。這個倍率仍然會繼續和距離衰退、部位倍率與暴頭倍率一起計算。")]
    private float focusDamageMultiplier =
        2f;

    #endregion

    // =====================================================================
    #region 自動鎖定設定

    [Header("專注 Auto Lock")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("專注射擊最多可以搜尋多遠的敵人。即使這裡設定比步槍最大射程更遠，實際仍不會超過 AttackRifle 的 Max Shot Distance。建議先從 60 公尺測試。")]
    private float focusLockDistance =
        60f;

    [SerializeField]
    [Range(0.1f, 45f)]
    [Tooltip("敵人相對於玩家準心方向允許偏離的最大角度。系統會在這個角度範圍內選擇「最接近準心」的敵人，而不是選擇離玩家世界距離最近的敵人。建議高速遊戲先測試 6 到 10 度。")]
    private float focusLockAngle =
        8f;

    [SerializeField]
    [Tooltip("開啟後，如果玩家原始準心射線本來就直接命中一個可專注敵人，就完全不修改子彈方向。這可以保留玩家自己精準瞄頭造成 Headshot 的技術空間。強烈建議保持開啟。")]
    private bool preserveDirectCrosshairHit =
        true;

    [SerializeField]
    [Tooltip("開啟後，自動鎖定搜尋與可見性判定會使用 Photon Fusion Subtick Accuracy。建議高速 FPS 保持開啟。")]
    private bool useSubtickAccuracy =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("檢查自動鎖定目標是否真的能被子彈射到時，Raycast 會比 Focus Aim Point 多延伸多少距離。這是防止 Aim Point 非常接近 Hitbox 表面時因浮點誤差判定不到目標。建議 0.1 到 0.3。")]
    private float targetRayPadding =
        0.2f;

    [SerializeField]
    [Tooltip("如果目前 Photon Fusion Lag Compensation 沒有啟用，是否允許專注目標可見性判定退回 Runner 的 PhysicsScene Raycast。正式多人版本建議啟用 Lag Compensation；此選項主要方便目前使用 Collider 做原型測試。")]
    private bool allowPhysicsFallback =
        true;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後顯示專注準備、正式啟動、結束原因、冷卻與 Auto Lock 選到的目標。")]
    private bool debugFocus =
        true;

    [SerializeField]
    [Tooltip("開啟後，當專注射擊成功找到 Auto Lock 目標時，會使用 Debug.DrawRay 顯示實際被修正後的子彈方向。")]
    private bool drawAutoLockRay =
        true;

    #endregion

    // =====================================================================
    #region Fusion 狀態
    /// <summary>
    /// 這一次 Focus 啟動期間，
    /// 是否至少成功射出過一發 Focus Shot。
    ///
    /// 注意：
    /// 成功射擊只會把這個值設成 true，
    /// 不會立刻結束 Focus，也不會立刻開始冷卻。
    /// </summary>
    [Networked]
    private NetworkBool HasFiredDuringCurrentFocus
    {
        get;
        set;
    }

    /// <summary>
    /// 是否正在等待玩家離開空戰狀態後才開始冷卻。
    ///
    /// 例如：
    ///
    /// Grappling
    /// ↓
    /// Focus
    /// ↓
    /// 成功射擊
    /// ↓
    /// PendingCooldownAfterAerialEnd = true
    ///
    /// 但此時玩家仍然可以：
    /// 繼續勾索
    /// 繼續 GrappleAirborne
    /// 繼續目前這一次 Focus 的鎖定射擊
    ///
    /// 等玩家最後離開：
    /// Grappling / GrappleAirborne
    ///
    /// 才真正開始 3 秒冷卻。
    /// </summary>
    [Networked]
    private NetworkBool PendingCooldownAfterAerialEnd
    {
        get;
        set;
    }

    /// <summary>
    /// 專注目前階段。
    /// </summary>
    [Networked]
    public AttackFocusPhase CurrentPhase
    {
        get;
        private set;
    }

    /// <summary>
    /// 專注最大持續時間。
    /// </summary>
    [Networked]
    private TickTimer FocusDurationTimer
    {
        get;
        set;
    }

    /// <summary>
    /// 專注射擊成功後的冷卻。
    /// </summary>
    [Networked]
    private TickTimer FocusCooldownTimer
    {
        get;
        set;
    }

    /// <summary>
    /// 這一次右鍵 Hold 是否已經使用過一次專注。
    ///
    /// 例如：
///
/// 右鍵一直不放
/// → Focus
/// → 0.7 秒 Timeout
///
/// 系統不會立刻再次進入 Focus。
///
/// 玩家必須先放開右鍵，
/// 再重新按住才可以再次使用。
    /// </summary>
    [Networked]
    private NetworkBool FocusConsumedForCurrentAimHold
    {
        get;
        set;
    }

    /// <summary>
    /// 最近一次專注結束原因。
    /// </summary>
    [Networked]
    public AttackFocusEndReason LastEndReason
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 本地執行資料

    /// <summary>
    /// 最近一次 Focus Shot 在 State Authority
    /// 成功選到的自動鎖定目標。
    ///
    /// 目前只做除錯使用，
    /// 尚未同步給 UI。
    /// </summary>
    private FocusTarget lastResolvedAutoLockTarget;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 不包含 Auto Lock Direction 的基本 Focus Shot 設定。
    ///
    /// PlayerWeaponController 可以先取得這份資料，
    /// 等確定這個 Tick 真的能開火後，
    /// 才呼叫 BuildFocusShotModifier()
    /// 進行較昂貴的目標搜尋。
    /// </summary>
    public RifleShotModifier FocusShotModifier
    {
        get
        {
            return new RifleShotModifier
            {
                AmmoCost =
                    FocusAmmoCost,

                DamageMultiplier =
                    FocusDamageMultiplier,

                UseShotDirectionOverride =
                    false,

                ShotDirectionOverride =
                    default,

                WasAutoLocked =
                    false
            };
        }
    }

    /// <summary>
    /// Focus 是否已經使用過射擊，
    /// 正在等待玩家結束這一次空戰流程後開始冷卻。
    ///
    /// 未來 HUD 可以用這個狀態顯示：
    /// 「技能已使用，落地後進入冷卻」。
    /// </summary>
    public bool IsWaitingForAerialCooldown =>
        PendingCooldownAfterAerialEnd;
        
    /// <summary>
    /// 專注是否正式作用中。
    /// </summary>
    public bool IsFocusActive =>
        CurrentPhase ==
        AttackFocusPhase.Active;

    /// <summary>
    /// 專注是否處於射擊後冷卻。
    /// </summary>
    public bool IsFocusOnCooldown
    {
        get
        {
            if (Runner == null)
                return false;

            return FocusCooldownTimer
                .ExpiredOrNotRunning(Runner) ==
                false;
        }
    }

    /// <summary>
    /// 專注射擊彈藥消耗。
    /// </summary>
    public int FocusAmmoCost =>
        Mathf.Max(
            1,
            focusAmmoCost
        );

    /// <summary>
    /// 專注射擊傷害倍率。
    /// </summary>
    public float FocusDamageMultiplier =>
        Mathf.Max(
            0f,
            focusDamageMultiplier
        );

    /// <summary>
    /// 專注剩餘時間。
    ///
    /// 未來 HUD 可以直接讀。
    /// </summary>
    public float FocusRemainingSeconds
    {
        get
        {
            if (Runner == null ||
                IsFocusActive == false)
            {
                return 0f;
            }

            return FocusDurationTimer
                .RemainingTime(Runner) ?? 0f;
        }
    }

    /// <summary>
    /// 專注冷卻剩餘秒數。
    /// </summary>
    public float FocusCooldownRemainingSeconds
    {
        get
        {
            if (Runner == null ||
                IsFocusOnCooldown == false)
            {
                return 0f;
            }

            return FocusCooldownTimer
                .RemainingTime(Runner) ?? 0f;
        }
    }

    /// <summary>
    /// 最近一次自動鎖定的目標。
    ///
    /// 目前主要方便 Debug。
    /// </summary>
    public FocusTarget LastResolvedAutoLockTarget =>
        lastResolvedAutoLockTarget;

    #endregion

    // =====================================================================
    #region Unity / Fusion

    private void Awake()
    {
        /*
        * Runtime 內部元件。
        *
        * 這兩支未來仍然和 Focus
        * 放在同一個 Attack Runtime。
        */
        if (aimController == null)
        {
            aimController =
                GetComponent<PlayerAimController>();
        }

        if (attackRifle == null)
        {
            attackRifle =
                GetComponent<AttackRifle>();
        }

        /*
        * 舊 Player Root 架構相容。
        *
        * 真正搬到 Runtime 後，
        * profession / stateMachine
        * 不應再從自己取得。
        */
        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (stateMachine == null)
        {
            stateMachine =
                GetComponent<PlayerStateMachine>();
        }

        /*
        * 如果目前仍然掛在 Player Root，
        * 先完成一次 Legacy Binding。
        */
        if (ownerPlayer == null)
        {
            Player localPlayer =
                GetComponent<Player>();

            if (localPlayer != null)
            {
                BindOwnerPlayer(
                    localPlayer,
                    attackRifle,
                    aimController
                );
            }
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentPhase =
                AttackFocusPhase.Idle;

            FocusDurationTimer =
                TickTimer.None;

            FocusCooldownTimer =
                TickTimer.None;

            FocusConsumedForCurrentAimHold =
                false;

            HasFiredDuringCurrentFocus =
                false;

            PendingCooldownAfterAerialEnd =
                false;

            LastEndReason =
                AttackFocusEndReason.None;
        }
    }

    #endregion

    // =====================================================================
    #region 每 Tick 專注狀態

    /// <summary>
    /// 每個 Fusion Tick 更新 Attack Focus。
    ///
    /// 注意：
    ///
    /// 這支技能現在完全不讀 InputButton.Aim。
    ///
    /// 是否正在瞄準只認：
    ///
    /// PlayerAimController.IsAiming
    ///
    /// ------------------------------------------------------------
    ///
    /// 流程：
    ///
    /// 右鍵
    /// ↓
    /// PlayerAimController
    /// ↓
    /// ADS FOV 拉近
    /// ↓
    /// Aim Enter Duration 完成
    /// ↓
    /// IsAiming = true
    /// ↓
    /// AttackFocusAbility 才能啟動
    ///
    /// ------------------------------------------------------------
    ///
    /// Focus Shot 成功後：
    ///
    /// 不結束 Focus。
    /// 不立即開始 CD。
    ///
    /// 玩家離開 Grappling / GrappleAirborne 後，
    /// 才開始正式 Cooldown。
    /// </summary>
    public void Simulate()
    {
        // -------------------------------------------------------------
        // 1. 清除已完成的正式 Cooldown
        // -------------------------------------------------------------

        if (FocusCooldownTimer
            .Expired(Runner))
        {
            FocusCooldownTimer =
                TickTimer.None;
        }

        // -------------------------------------------------------------
        // 2. 延後 CD 是否應該正式開始
        // -------------------------------------------------------------

        if (PendingCooldownAfterAerialEnd &&
            IsValidFocusMovementState() == false)
        {
            if (IsFocusActive)
            {
                EndFocus(
                    AttackFocusEndReason.StateInvalid
                );
            }

            StartPendingCooldownNow();

            return;
        }

        // -------------------------------------------------------------
        // 3. 非 Attack 職業
        // -------------------------------------------------------------

        if (profession == null ||
            profession.CurrentProfession !=
            PlayerProfessionType.Attack)
        {
            if (IsFocusActive)
            {
                EndFocus(
                    AttackFocusEndReason.ProfessionChanged
                );
            }

            return;
        }

        // -------------------------------------------------------------
        // 4. 瞄準狀態
        // -------------------------------------------------------------

        /*
        * 這裡已經完全不讀：
        *
        * InputButton.Aim
        *
        * 只相信 PlayerAimController。
        */
        bool isAiming =
            aimController != null &&
            aimController.IsAiming;

        // -------------------------------------------------------------
        // 5. 玩家不再真正瞄準
        // -------------------------------------------------------------

        if (isAiming == false)
        {
            /*
            * 只有 ADS 已經完全回到 Hip，
            * 才代表這一次 Aim Hold 真正結束。
            *
            * Entering 階段不應該重置這個值，
            * 因為玩家只是正在進入 ADS。
            */
            if (aimController == null ||
                aimController.CurrentAimPhase ==
                PlayerAimPhase.Hip)
            {
                FocusConsumedForCurrentAimHold =
                    false;
            }

            if (IsFocusActive)
            {
                EndFocus(
                    AttackFocusEndReason.AimReleased
                );
            }

            return;
        }

        // -------------------------------------------------------------
        // 6. Focus Active
        // -------------------------------------------------------------

        if (IsFocusActive)
        {
            /*
            * Grappling → GrappleAirborne
            * 仍然是合法轉換。
            */
            if (IsValidFocusMovementState() == false)
            {
                EndFocus(
                    AttackFocusEndReason.StateInvalid
                );

                if (PendingCooldownAfterAerialEnd)
                {
                    StartPendingCooldownNow();
                }

                return;
            }

            /*
            * Focus 視窗時間到。
            *
            * 即使曾經射擊，
            * CD 仍然等待空戰流程結束。
            */
            if (FocusDurationTimer
                .Expired(Runner))
            {
                EndFocus(
                    AttackFocusEndReason.Timeout
                );
            }

            return;
        }

        // -------------------------------------------------------------
        // 7. 尚未 Focus → 判斷是否可以開始
        // -------------------------------------------------------------

        if (CanStartFocus())
        {
            BeginFocus();
        }
    }

    #endregion

    // =====================================================================
    #region Focus 資格

    /// <summary>
    /// 玩家目前是不是專注允許的空戰狀態。
    ///
    /// 新版規則：
    ///
    /// Grappling
    /// ✓
    ///
    /// GrappleAirborne
    /// ✓
    ///
    /// 其他狀態
    /// ×
        /// </summary>
        private bool IsValidFocusMovementState()
        {
            if (stateMachine == null)
                return false;

            PlayerMovementState state =
                stateMachine.CurrentState;

            return state ==
                    PlayerMovementState.Grappling ||
                state ==
                    PlayerMovementState.GrappleAirborne;
        }

        /// <summary>
    /// 判斷玩家目前是否允許開始 Attack Focus。
    ///
    /// 必須同時符合：
    ///
    /// Attack
    /// +
    /// ADS 已正式完成
    /// +
    /// Grappling / GrappleAirborne
    /// +
    /// 沒有正式 CD
    /// +
    /// 沒有等待落地 CD
    /// +
    /// 這次 ADS Hold 尚未使用過 Focus
    /// </summary>
    private bool CanStartFocus()
    {
        // -------------------------------------------------------------
        // Attack 職業
        // -------------------------------------------------------------

        if (profession == null ||
            profession.CurrentProfession !=
            PlayerProfessionType.Attack)
        {
            return false;
        }

        // -------------------------------------------------------------
        // ★ 必須真正完成 ADS
        // -------------------------------------------------------------

        /*
        * 玩家只是按下右鍵不算。
        *
        * FOV 還正在拉近也不算。
        *
        * 必須等 PlayerAimController：
        *
        * Entering
        * ↓
        * Aiming
        *
        * 才成立。
        */
        if (aimController == null ||
            aimController.IsAiming == false)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 空戰狀態
        // -------------------------------------------------------------

        if (IsValidFocusMovementState() == false)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 正式 CD
        // -------------------------------------------------------------

        if (IsFocusOnCooldown)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 已經用過技能，等待空戰結束
        // -------------------------------------------------------------

        if (PendingCooldownAfterAerialEnd)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 同一次 ADS Hold 已使用過 Focus
        // -------------------------------------------------------------

        if (FocusConsumedForCurrentAimHold)
        {
            return false;
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region Focus 開始 / 結束

    private void BeginFocus()
    {
        if (CanStartFocus() == false)
        {
            return;
        }

        CurrentPhase =
            AttackFocusPhase.Active;

        FocusConsumedForCurrentAimHold =
            true;

        HasFiredDuringCurrentFocus =
            false;

        FocusDurationTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                maximumFocusDuration
            );

        LastEndReason =
            AttackFocusEndReason.None;

        if (debugFocus &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Focus] 正式啟動。" +
                $"\n玩家狀態：{stateMachine.CurrentState}" +
                $"\n最大持續：{maximumFocusDuration:F2} 秒" +
                $"\n玩家移動：完全不受影響",
                this
            );
        }
    }

    /// <summary>
    /// 結束目前 Focus 視窗。
    ///
    /// 注意：
    /// 這個方法現在完全不會開始技能冷卻。
    ///
    /// 冷卻只有在：
    ///
    /// PendingCooldownAfterAerialEnd = true
    /// 並且
    /// 玩家離開 Grappling / GrappleAirborne
    ///
    /// 時才會正式開始。
    /// </summary>
    private void EndFocus(
        AttackFocusEndReason reason
    )
    {
        if (IsFocusActive == false)
            return;

        CurrentPhase =
            AttackFocusPhase.Idle;

        FocusDurationTimer =
            TickTimer.None;

        LastEndReason =
            reason;

        lastResolvedAutoLockTarget =
            null;

        if (debugFocus &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Focus] Focus 視窗結束。" +
                $"\n原因：{reason}" +
                $"\n本次是否曾成功射擊：{HasFiredDuringCurrentFocus}" +
                $"\n是否等待空戰結束後進入 CD：{PendingCooldownAfterAerialEnd}",
                this
            );
        }
    }

    /// <summary>
    /// 玩家已經真正離開這一次空戰狀態，
    /// 現在才正式開始 Focus Cooldown。
    ///
    /// 通常發生於：
    ///
    /// GrappleAirborne
    /// ↓
    /// 玩家碰到地面
    /// ↓
    /// PlayerStateMachine 變成 Idle / Walk / Run
    /// ↓
    /// 進入這裡
    /// </summary>
    private void StartPendingCooldownNow()
    {
        if (PendingCooldownAfterAerialEnd == false)
            return;

        PendingCooldownAfterAerialEnd =
            false;

        HasFiredDuringCurrentFocus =
            false;

        if (focusCooldownAfterFire <= 0f)
        {
            FocusCooldownTimer =
                TickTimer.None;

            return;
        }

        FocusCooldownTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                focusCooldownAfterFire
            );

        if (debugFocus &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Focus] 空戰流程結束。" +
                $"\n現在正式開始 Focus Cooldown。" +
                $"\n冷卻時間：{focusCooldownAfterFire:F2} 秒",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Focus Shot Modifier

    /// <summary>
    /// 建立這一發專注射擊使用的 RifleShotModifier。
    ///
    /// Input Authority：
    ///
    /// 仍會得到正確的
    /// AmmoCost = 2
    /// DamageMultiplier = 2
    ///
    /// State Authority：
    ///
    /// 額外執行真正的 Auto Lock 目標選擇，
    /// 並決定是否需要修改子彈方向。
    ///
    /// 這樣 Client 不負責決定自己打中誰。
    /// </summary>
    public RifleShotModifier BuildFocusShotModifier()
    {
        RifleShotModifier modifier =
            FocusShotModifier;

        if (IsFocusActive == false ||
            attackRifle == null)
        {
            return modifier;
        }

        /*
         * 真正的 Auto Lock 目標選擇
         * 交給 State Authority。
         *
         * Client 只預測 Ammo / Fire Rate / Recoil。
         */
        if (Object.HasStateAuthority == false)
        {
            return modifier;
        }

        if (TryResolveAutoLockDirection(
                out Vector3 autoLockDirection,
                out FocusTarget selectedTarget
            ))
        {
            modifier.UseShotDirectionOverride =
                true;

            modifier.ShotDirectionOverride =
                autoLockDirection;

            modifier.WasAutoLocked =
                true;

            lastResolvedAutoLockTarget =
                selectedTarget;

            if (drawAutoLockRay)
            {
                Debug.DrawRay(
                    attackRifle.GetGameplayShotOrigin(),
                    autoLockDirection *
                    attackRifle.MaxShotDistance,
                    Color.magenta,
                    0.5f
                );
            }

            if (debugFocus)
            {
                Debug.Log(
                    $"[Attack Focus] Auto Lock 成功。" +
                    $"\n目標：{selectedTarget.name}" +
                    $"\n修正方向：{autoLockDirection}",
                    selectedTarget
                );
            }
        }
        else
        {
            lastResolvedAutoLockTarget =
                null;
        }

        return modifier;
    }

    #endregion

    // =====================================================================
    #region Auto Lock 目標搜尋

    /// <summary>
    /// 嘗試找出專注射擊應該自動修正的方向。
    ///
    /// 順序：
///
/// 1. 先確認原始準心有沒有直接命中敵人。
///
/// 如果有：
/// 不 Auto Lock。
///
/// 2. 搜尋準心角度範圍內所有 FocusTarget。
///
/// 3. 排除：
/// 自己
/// 太遠
/// 超過角度
/// 被牆擋住
/// 被其他敵人擋住
///
/// 4. 選擇「離準心角度最小」的敵人。
    /// </summary>
    private bool TryResolveAutoLockDirection(
        out Vector3 resultDirection,
        out FocusTarget selectedTarget
    )
    {
        resultDirection =
            default;

        selectedTarget =
            null;

        Vector3 origin =
            attackRifle.GetGameplayShotOrigin();

        Vector3 normalAimDirection =
            attackRifle.GetGameplayAimDirection();

        // -------------------------------------------------------------
        // 原準心已經直接打中敵人
        // -------------------------------------------------------------

        if (preserveDirectCrosshairHit &&
            DoesRawCrosshairHitFocusTarget(
                origin,
                normalAimDirection
            ))
        {
            /*
             * 玩家自己已經瞄準成功。
             *
             * 不替他改方向。
             *
             * 特別是玩家自己瞄到 Head 時，
             * 必須保留 Headshot。
             */
            return false;
        }

        // -------------------------------------------------------------
        // 搜尋範圍
        // -------------------------------------------------------------

        float maximumDistance =
            Mathf.Min(
                focusLockDistance,
                attackRifle.MaxShotDistance
            );

        float bestAngle =
            float.MaxValue;

        float bestDistance =
            float.MaxValue;

        Vector3 bestAimPosition =
            default;

        IReadOnlyList<FocusTarget> targets =
            FocusTarget.ActiveTargets;

        for (int i = 0;
             i < targets.Count;
             i++)
        {
            FocusTarget candidate =
                targets[i];

            if (candidate == null ||
                candidate.IsTargetable == false)
            {
                continue;
            }

            /*
             * 同一個 Unity Instance 多 Peer 測試時，
             * 不要選到其他 Peer Scene 裡的敵人。
             */
            if (candidate.gameObject.scene !=
                gameObject.scene)
            {
                continue;
            }

            /*
             * 防止玩家自己的物件被設成 FocusTarget
             * 時鎖到自己。
            */
            NetworkObject ownerObject =
                GetOwnerPlayerNetworkObject();

            if (ownerObject != null &&
                candidate.OwnerNetworkObject ==
                    ownerObject)
            {
                continue;
            }

            Vector3 aimPosition =
                candidate.GetAimPosition(
                    Runner,
                    Object.InputAuthority,
                    useSubtickAccuracy
                );

            Vector3 toTarget =
                aimPosition -
                origin;

            float distance =
                toTarget.magnitude;

            if (distance <= 0.0001f ||
                distance >
                maximumDistance)
            {
                continue;
            }

            Vector3 direction =
                toTarget /
                distance;

            /*
             * 我們真正衡量的是：
             *
             * 「這個敵人離準心方向多遠？」
             *
             * 不是：
             *
             * 「這個敵人離玩家世界距離多近？」
             */
            float angle =
                Vector3.Angle(
                    normalAimDirection,
                    direction
                );

            if (angle >
                focusLockAngle)
            {
                continue;
            }

            /*
             * 確認子彈真的能沿這個方向
             * 第一個命中這個候選目標。
             *
             * 如果中間有牆或其他敵人，
             * 這個 Candidate 淘汰。
             */
            if (CanShotReachTarget(
                    origin,
                    direction,
                    distance,
                    candidate
                ) == false)
            {
                continue;
            }

            bool angleIsBetter =
                angle <
                bestAngle;

            bool sameAngleButCloser =
                Mathf.Abs(
                    angle -
                    bestAngle
                ) <= 0.001f &&
                distance <
                bestDistance;

            if (angleIsBetter == false &&
                sameAngleButCloser == false)
            {
                continue;
            }

            bestAngle =
                angle;

            bestDistance =
                distance;

            bestAimPosition =
                aimPosition;

            selectedTarget =
                candidate;
        }

        if (selectedTarget == null)
        {
            return false;
        }

        Vector3 finalDirection =
            bestAimPosition -
            origin;

        if (finalDirection.sqrMagnitude <=
            0.0001f)
        {
            selectedTarget =
                null;

            return false;
        }

        resultDirection =
            finalDirection.normalized;

        return true;
    }

    #endregion

    // =====================================================================
    #region 原始準心判定

    /// <summary>
    /// 檢查原始準心是不是已經直接射中
    /// 一個 FocusTarget。
    ///
    /// 如果是，就不啟用 Auto Lock。
    /// </summary>
    private bool DoesRawCrosshairHitFocusTarget(
        Vector3 origin,
        Vector3 direction
    )
    {
        if (TryRaycastShotPath(
                origin,
                direction,
                attackRifle.MaxShotDistance,
                out GameObject hitObject
            ) == false)
        {
            return false;
        }

        if (hitObject == null)
            return false;

        FocusTarget focusTarget =
            hitObject.GetComponentInParent<FocusTarget>();

        return focusTarget != null &&
               focusTarget.IsTargetable;
    }

    #endregion

    // =====================================================================
    #region Candidate 可見性

    /// <summary>
    /// 確認沿著候選目標方向射出的子彈，
    /// 第一個碰到的是不是該候選敵人。
    ///
    /// 這同時處理：
///
/// 牆壁遮擋
/// 其他敵人擋在前面
/// 場景物件遮擋
    /// </summary>
    private bool CanShotReachTarget(
        Vector3 origin,
        Vector3 direction,
        float distance,
        FocusTarget target
    )
    {
        float rayDistance =
            distance +
            targetRayPadding;

        if (TryRaycastShotPath(
                origin,
                direction,
                rayDistance,
                out GameObject hitObject
            ) == false)
        {
            return false;
        }

        return target.OwnsHit(
            hitObject
        );
    }

    #endregion

    // =====================================================================
    #region 共用射線

    /// <summary>
    /// 使用和 AttackRifle 一致的 Hit Mask
    /// 檢查一條射擊路徑的第一個命中。
    ///
    /// 優先：
/// Photon Fusion Lag Compensation
///
/// 備用：
/// Runner PhysicsScene
    /// </summary>
    private bool TryRaycastShotPath(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out GameObject hitObject
    )
    {
        hitObject =
            null;

        direction.Normalize();

        // -------------------------------------------------------------
        // Fusion Lag Compensation
        // -------------------------------------------------------------

        if (Runner != null &&
            Runner.LagCompensation != null)
        {
            HitOptions options =
                HitOptions.IncludePhysX |
                HitOptions.IgnoreInputAuthority;

            if (useSubtickAccuracy)
            {
                options |=
                    HitOptions.SubtickAccuracy;
            }

            bool hasHit =
                Runner.LagCompensation.Raycast(
                    origin,
                    direction,
                    distance,
                    Object.InputAuthority,
                    out LagCompensatedHit hit,
                    attackRifle.HitMask,
                    options,
                    QueryTriggerInteraction.Ignore
                );

            if (hasHit)
            {
                hitObject =
                    hit.GameObject;

                return hitObject != null;
            }

            return false;
        }

        // -------------------------------------------------------------
        // PhysicsScene Fallback
        // -------------------------------------------------------------

        if (allowPhysicsFallback == false ||
            Runner == null)
        {
            return false;
        }

        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() == false)
        {
            return false;
        }

        bool physicsHitFound =
            physicsScene.Raycast(
                origin,
                direction,
                out RaycastHit physicsHit,
                distance,
                attackRifle.HitMask,
                QueryTriggerInteraction.Ignore
            );

        if (physicsHitFound == false)
        {
            return false;
        }

        hitObject =
            physicsHit.collider.gameObject;

        /*
         * 防止普通 PhysX Collider
         * 射到玩家自己。
         */
        NetworkObject hitNetworkObject =
            hitObject.GetComponentInParent<NetworkObject>();

        NetworkObject ownerObject =
            GetOwnerPlayerNetworkObject();

        if (ownerObject != null &&
            hitNetworkObject ==
                ownerObject)
        {
            hitObject =
                null;

            return false;
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region Focus Shot 成功

    /// <summary>
    /// 通知 Focus：
    /// AttackRifle 剛剛真的成功射出一發 Focus Shot。
    ///
    /// 新版規則：
    ///
    /// 成功射擊
    /// ↓
    /// 不結束 Focus
    /// ↓
    /// 不開始冷卻
    /// ↓
    /// 只記錄這次技能已經真正使用過
    ///
    /// 玩家可以繼續按住左鍵，
    /// 依 AttackRifle Fire Rate
    /// 繼續進行 Auto Lock Focus Shot。
    ///
    /// 冷卻會等到玩家最後離開：
    /// Grappling / GrappleAirborne
    ///
    /// 才正式開始。
    /// </summary>
    public void NotifySuccessfulFocusShot()
    {
        if (IsFocusActive == false)
            return;

        /*
        * 標記目前這一次 Focus
        * 至少已經成功射擊。
        */
        HasFiredDuringCurrentFocus =
            true;

        /*
        * 從現在開始，
        * 這次空戰結束後需要進入冷卻。
        *
        * 但現在不啟動 TickTimer。
        */
        PendingCooldownAfterAerialEnd =
            true;

        if (debugFocus &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Focus] Focus Shot 成功。" +
                $"\nFocus 不會因此結束。" +
                $"\n可以繼續鎖定射擊。" +
                $"\n冷卻狀態：等待空戰結束",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 外部取消

    /// <summary>
    /// 外部系統強制取消 Focus。
    ///
    /// 如果目前 Focus 正在作用，
    /// 會立即結束 Focus。
    ///
    /// 如果 Focus 本來就沒有啟動，
    /// 則不需要做任何處理。
    ///
    /// 注意：
    /// ADS 的 Entering / Aiming 狀態
    /// 屬於 PlayerAimController 管理，
    /// AttackFocusAbility 不會去取消 ADS。
    /// </summary>
    public void CancelFocusFromExternalSystem()
    {
        if (IsFocusActive == false)
        {
            return;
        }

        EndFocus(
            AttackFocusEndReason.ExternalCancel
        );
    }

    #endregion
}