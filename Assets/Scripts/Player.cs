using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

/// <summary>
/// 玩家 Core 根控制器。
///
/// ------------------------------------------------------------
///
/// Player 不再直接知道任何職業的詳細戰鬥邏輯。
///
/// Player Core 只負責：
///
/// 1. Fusion NetInput。
/// 2. 共用勾索充能。
/// 3. 共用 Quick Action Slot。
/// 4. 共用 Movement。
/// 5. 共用 Jump / Grapple Momentum。
/// 6. 共用 Grapple。
/// 7. 驅動目前 Profession Runtime。
/// 8. 共用 Player State Machine。
///
/// ------------------------------------------------------------
///
/// 職業玩法：
///
/// Attack
/// → AttackProfessionRuntimeDriver
///
/// Tank
/// → TankProfessionRuntimeDriver
///
/// Support
/// → SupportProfessionRuntimeDriver
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// PlayerWeaponController
/// AttackFocusAbility
/// PlayerAimController
/// AttackQuickMelee
///
/// 目前暫時仍掛在 Player Prefab，
/// 這只是職業 Runtime 遷移的中間階段。
///
/// 下一階段才會真正從 Player Prefab 搬走。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KCC))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(PlayerProfession))]
[RequireComponent(typeof(PlayerGrapple))]
[RequireComponent(typeof(PlayerGrappleCharges))]
[RequireComponent(typeof(PlayerGrappleVisual))]
[RequireComponent(typeof(PlayerLocalView))]
[RequireComponent(typeof(PlayerActionGate))]
[RequireComponent(typeof(PlayerQuickActionController))]
[RequireComponent(typeof(PlayerProfessionRuntimeManager))]
[RequireComponent(typeof(SupportGrapplePlayerPullReceiver))]
[RequireComponent(typeof(PlayerIncomingDamageModifierBridge))]
[RequireComponent(typeof(PlayerSlideController))]
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerSprintLatchController))]

public class Player :
    NetworkBehaviour
{
    // =====================================================================
    #region Player Core 引用

    [Header("Player Core 模組引用")]

    [SerializeField]
    [Tooltip("玩家共用移動模組。若留空會自動取得。")]
    private PlayerMovement movement;

    [SerializeField]
    [Tooltip(
        "玩家共用的 Sprint 鎖定與兩秒加速控制器。\n\n" +
        "Shift 只啟動 Sprint；停止 WASD 才解除。\n\n" +
        "若留空會自動取得。")]
    private PlayerSprintLatchController sprintController;

    [SerializeField]
    [Tooltip(
        "玩家共用的蹲下與動量滑鏟控制器。\n\n" +
        "負責 KCC 蹲姿高度、滑鏟速度、坡面物理與第一人稱鏡頭高度。\n\n" +
        "若留空會自動取得。")]
    private PlayerSlideController slideController;

    [SerializeField]
    [Tooltip("玩家共用狀態機。若留空會自動取得。")]
    private PlayerStateMachine stateMachine;

    [SerializeField]
    [Tooltip("玩家目前正式職業資料。若留空會自動取得。")]
    private PlayerProfession profession;

    [SerializeField]
    [Tooltip("玩家共用勾索核心。若留空會自動取得。")]
    private PlayerGrapple grapple;

    [SerializeField]
    [Tooltip("玩家共用勾索充能系統。若留空會自動取得。")]
    private PlayerGrappleCharges grappleCharges;

    [SerializeField]
    [Tooltip("玩家共用 F Quick Action Slot 控制器。若留空會自動取得。")]
    private PlayerQuickActionController quickActionController;

    [SerializeField]
    [Tooltip("目前職業 Runtime 管理器。Player 不再直接驅動 Attack Aim、Focus 或 Weapon，而是把 NetInput 交給這個 Manager 的 Current Runtime。若留空會自動取得。")]
    private PlayerProfessionRuntimeManager professionRuntimeManager;

    [SerializeField]
    [Tooltip("玩家作為 Support Grapple 目標時使用的 KCC Pull Receiver。任何職業的玩家都可能被 Support 勾中，因此這顆屬於 Player Core，而不是 Support Profession Runtime。若留空會自動取得。")]
    private SupportGrapplePlayerPullReceiver supportGrapplePlayerPullReceiver;

    [SerializeField]
    [Tooltip("玩家共用生命值系統。Attack、Tank、Support 都使用同一套 PlayerHealth。若留空會自動取得。")]
    private PlayerHealth health;

    #endregion

    // =====================================================================
    #region Fusion 輸入歷史

    /// <summary>
    /// 上一個 Fusion Tick 的 NetworkButtons。
    ///
    /// Jump
    /// Grapple
    /// Reload
    /// QuickAction
    ///
    /// 等 WasPressed 判斷都使用這份資料。
    /// </summary>
    [Networked]
    private NetworkButtons PreviousButtons
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region 公開 Core 模組

    public PlayerMovement Movement =>
        movement;

    public PlayerSlideController SlideController =>
        slideController;

    public PlayerStateMachine StateMachine =>
        stateMachine;

    public PlayerProfession Profession =>
        profession;

    public PlayerGrapple Grapple =>
        grapple;

    public PlayerGrappleCharges GrappleCharges =>
        grappleCharges;

    public PlayerProfessionRuntimeManager ProfessionRuntimeManager =>
        professionRuntimeManager;
    
    /// <summary>
    /// 玩家作為 Support Grapple Target
    /// 使用的 KCC Pull Receiver。
    /// </summary>
    public SupportGrapplePlayerPullReceiver SupportGrapplePlayerPullReceiver =>
        supportGrapplePlayerPullReceiver;

    /// <summary>
    /// 玩家共用生命系統。
    /// </summary>
    public PlayerHealth Health =>
        health;

    #endregion

    // =====================================================================
    #region 相容 Facade

    public float CurrentWorldSpeed =>
        movement != null
            ? movement.CurrentWorldSpeed
            : 0f;

    public bool IsGrapplePulling =>
        grapple != null &&
        grapple.IsGrapplePulling;

    public bool IsManualCancelMomentumActive =>
        grapple != null &&
        grapple.IsReleaseMomentumActive;

    public bool IsGrappleReleaseMomentumActive =>
        grapple != null &&
        grapple.IsReleaseMomentumActive;

    public bool CanUseGrapple =>
        grapple != null &&
        grapple.CanUseGrapple;

    public int GrappleChargesCurrent =>
        grappleCharges != null
            ? grappleCharges.CurrentCharges
            : 0;

    public int GrappleChargesMaximum =>
        grappleCharges != null
            ? grappleCharges.MaxCharges
            : 0;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }

        if (slideController == null)
        {
            slideController =
                GetComponent<PlayerSlideController>();
        }

        if (sprintController == null)
        {
            sprintController =
                GetComponent<PlayerSprintLatchController>();
        }

        if (stateMachine == null)
        {
            stateMachine =
                GetComponent<PlayerStateMachine>();
        }

        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (grapple == null)
        {
            grapple =
                GetComponent<PlayerGrapple>();
        }

        if (grappleCharges == null)
        {
            grappleCharges =
                GetComponent<PlayerGrappleCharges>();
        }

        if (quickActionController == null)
        {
            quickActionController =
                GetComponent<PlayerQuickActionController>();
        }

        if (professionRuntimeManager == null)
        {
            professionRuntimeManager =
                GetComponent<
                    PlayerProfessionRuntimeManager
                >();
        }

        if (supportGrapplePlayerPullReceiver == null)
        {
            supportGrapplePlayerPullReceiver =
                GetComponent<
                    SupportGrapplePlayerPullReceiver
                >();
        }

        if (health == null)
        {
            health =
                GetComponent<PlayerHealth>();
        }
    }

    #endregion

    // =====================================================================
    #region Fusion Simulation

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetInput input) ==
            false)
        {
            return;
        }

        // -------------------------------------------------------------
        // 1. 共用勾索充能
        // -------------------------------------------------------------

        grappleCharges.TickRecharge();

        // -------------------------------------------------------------
        // 2. 共用 Quick Action Slot
        // -------------------------------------------------------------

        /*
         * 目前 QuickActionController
         * 還處於下一個待重構項目。
         *
         * Attack：
         * → AttackQuickMelee
         *
         * Tank：
         * → 未來 TankQuickDash。
         *
         * 現在先保持原本已測通行為。
         */
        quickActionController.Simulate(
            input,
            PreviousButtons
        );

        // -------------------------------------------------------------
        // 3. 共用玩家移動
        // -------------------------------------------------------------

        /*
         * 共用外部 Movement Influence。
         *
         * ------------------------------------------------------------
         *
         * 第一層：
         *
         * Grapple。
         *
         * ------------------------------------------------------------
         *
         * 第二層：
         *
         * Current Profession Runtime。
         *
         * Player 不知道這是 Tank Guard，
         * 只知道目前職業 Runtime
         * 提供了一個共用移動倍率。
         */
        // -------------------------------------------------------------
        // Grapple / Support Pull
        // 對普通 WASD 的共同影響
        // -------------------------------------------------------------

        /*
         * Grapple 以外的系統是否允許主動移動。
         *
         * 這個值會另外交給 PlayerGrapple，
         * 避免 GrappleAirborne 高速方向控制
         * 繞過 Support Ability、Support Pull 或職業移動封鎖。
         */
        float nonGrappleMovementInfluence =
            1f;

        if (supportGrapplePlayerPullReceiver !=
            null)
        {
            nonGrappleMovementInfluence *=
                supportGrapplePlayerPullReceiver
                    .MovementInputInfluence;
        }

        if (professionRuntimeManager != null)
        {
            nonGrappleMovementInfluence *=
                professionRuntimeManager
                    .GetCurrentMovementInputMultiplier(
                        input
                    );
        }

        /*
        * SlideController 對普通 PlayerMovement 的影響：
        *
        * Normal    = 1
        * Crouching = Inspector 設定的蹲走倍率
        * Sliding   = 0，由 Slide Processor 接管水平 Kinematic Velocity
        */
        float slideMovementInfluence =
            slideController != null
                ? slideController.MovementInputInfluence
                : 1f;

        float externalMovementInfluence =
            grapple.MovementInputInfluence *
            nonGrappleMovementInfluence *
            slideMovementInfluence;


        // =========================================================
        // 修正 CS0103：在此宣告 externalMovementControlActive
        // =========================================================
        
        /// <summary>
        /// 判斷目前是否有外部系統完全接管了移動控制（例如：勾索飛行中）。
        /// 若為 true，PlayerMovement 通常會暫停基礎的 WASD 加速度與重力運算。
        /// </summary>
        bool externalMovementControlActive = 
            grapple.IsGrappleControlActive;
        
        /*
        * 蹲下或滑鏟期間不允許 Sprint Ramp 在背景累積。
        *
        * 特別注意：
        * Crouching 的 MovementInputInfluence 通常不是 0，
        * 所以只檢查 externalMovementInfluence > 0 仍會偷偷累積。
        * 必須另外明確排除 IsCrouched 與 IsSliding。
        */
        bool crouchOrSlideActive =
            slideController != null &&
            (slideController.IsCrouched ||
            slideController.IsSliding);

        bool normalMovementCanAccelerate =
            externalMovementControlActive == false &&
            externalMovementInfluence > 0.0001f &&
            crouchOrSlideActive == false;

        if (sprintController != null)
        {
            sprintController.Simulate(
                input,
                PreviousButtons,
                normalMovementCanAccelerate
            );
        }

        bool sprintActive =
            sprintController != null
                ? sprintController.IsSprintActive
                : input.Buttons.IsSet(
                    InputButton.Sprint
                );

        float sprintRampProgress =
            sprintController != null
                ? sprintController.SprintRampProgress
                : 1f;

        /* 
         * [詳細註解] 
         * 若您的 SupportGrapplePlayerPullReceiver 也有類似「強制接管位移」的狀態（例如 IsBeingPulled），
         * 可以將其狀態一併聯集進來。請根據您實際的屬性名稱解開並修改以下註解：
         */
        // if (supportGrapplePlayerPullReceiver != null && supportGrapplePlayerPullReceiver.IsBeingPulled)
        // {
        //     externalMovementControlActive = true;
        // }
        // =========================================================

        /*
        * Slide Jump 的時間門檻與 Input Buffer
        * 必須在 PlayerMovement 處理普通 Jump 之前先判斷。
        *
        * PlayerSlideController 只決定：
        * 1. 本 Tick 的 Ground Jump 是否由滑鏟接管。
        * 2. 本 Tick 是否已符合 Slide Jump 執行條件。
        *
        * 真正的 KCC.Jump 仍只交給 PlayerMovement，
        * 避免兩套系統同一 Tick 重複施加跳躍。
        */
        bool slideOwnsGroundJumpInput =
            false;

        bool slideJumpRequested =
            false;

        if (slideController != null)
        {
            slideJumpRequested =
                slideController.EvaluateSlideJumpInput(
                    input,
                    PreviousButtons,
                    externalMovementControlActive == false &&
                    nonGrappleMovementInfluence > 0.0001f,
                    out slideOwnsGroundJumpInput
                );
        }

        PlayerMovement.FrameResult movementResult =
            movement.Simulate(
            input,
            PreviousButtons,
            sprintActive,
            sprintRampProgress,
            externalMovementControlActive,
            externalMovementInfluence,
            slideOwnsGroundJumpInput,
            slideJumpRequested
        );

        // -------------------------------------------------------------
        // 4. Jump 與 Grapple Momentum
        // -------------------------------------------------------------

        if (movementResult.GroundJumped ||
            movementResult.DoubleJumped)
        {
            grapple.NotifyPlayerJumped();
        }

        // -------------------------------------------------------------
        // 5. 共用 Grapple
        // -------------------------------------------------------------

        bool grapplePressed =
            input.Buttons.WasPressed(
                PreviousButtons,
                InputButton.Grapple
            );

        bool aimPressed =
            input.Buttons.WasPressed(
                PreviousButtons,
                InputButton.Aim
            );

        bool aimHeld =
            input.Buttons.IsSet(
                InputButton.Aim
            );

        /*
        * 順序不能任意移動：
        *
        * PlayerMovement.Simulate
        * → PlayerSlideController.Simulate
        * → PlayerGrapple.Simulate
        *
        * 原因是 GrappleAirborne Momentum 碰地時，
        * PlayerGrapple 原本會結束 Momentum 並清除水平 DynamicVelocity。
        * SlideController 必須先擷取該速度，才能把高速安全交接成滑鏟速度。
        */
        if (slideController != null)
        {
            slideController.Simulate(
                input,
                movementResult.GroundJumped ||
                movementResult.DoubleJumped,
                grapplePressed,
                grapple.IsGrappleControlActive,
                nonGrappleMovementInfluence > 0.0001f
            );
        }
        
        grapple.Simulate(
            grapplePressed,
            aimPressed,
            aimHeld,
            input.Direction,
            nonGrappleMovementInfluence
        );

        /*
         * 原始 NetInput 仍然保存真正的實體按鍵狀態，
         * PreviousButtons 也繼續使用原始按鍵歷史。
         *
         * 只有交給 Profession Runtime 的副本會暫時關閉 Aim。
         *
         * 這可以防止：
         *
         * Attached 按 Aim
         * → 斷繩
         * → 同一顆 Aim 又啟動 Tank / Support 特殊能力。
         */
        NetInput professionInput =
            input;

        if (grapple.BlocksProfessionAimInput)
        {
            professionInput.Buttons.Set(
                InputButton.Aim,
                false
            );
        }

        // -------------------------------------------------------------
        // 6. 目前職業 Gameplay
        // -------------------------------------------------------------

        /*
         * ★ Player 現在不再知道：
         *
         * Aim
         * Focus
         * Rifle
         * Tank Guard
         * Tank Melee
         * Support Ability。
         *
         * Player 只知道：
         *
         * 「把 Input 給現在的 Profession Runtime。」
         */
        if (professionRuntimeManager != null)
        {
            professionRuntimeManager
                .SimulateCurrentRuntime(
                    professionInput,
                    PreviousButtons
                );
        }

        // -------------------------------------------------------------
        // 7. Support Grapple Player Pull
        // -------------------------------------------------------------

        /*
        * ★ 必須排在 Profession Runtime 後面。
        *
        * ------------------------------------------------------------
        *
        * 原因：
        *
        * 被拉玩家可能是：
        *
        * Attack
        * Tank
        * Support。
        *
        * 他自己的職業能力可能也會在這個 Tick
        * 修改 KCC DynamicVelocity。
        *
        * ------------------------------------------------------------
        *
        * Support Player Pull 是外部強制位移，
        * 所以本 Tick 最後由它取得
        * DynamicVelocity 的控制權。
        */
        if (supportGrapplePlayerPullReceiver !=
            null)
        {
            supportGrapplePlayerPullReceiver
                .SimulatePull();
        }

        // -------------------------------------------------------------
        // 7. 共用 Player State Machine
        // -------------------------------------------------------------

        stateMachine.TickState(
            movement.IsGrounded,
            movementResult.RawMoveInput,
            movementResult.SprintHeld,
            grapple.IsGrappleControlActive,
            slideController != null &&
            slideController.IsCrouched,
            slideController != null &&
            slideController.IsSliding,
            movementResult.GroundJumped,
            movementResult.DoubleJumped,
            movement.VerticalVelocity,
            movement.HorizontalSpeed
        );

        // -------------------------------------------------------------
        // 8. 保存按鍵歷史
        // -------------------------------------------------------------

        PreviousButtons =
            input.Buttons;
    }

    #endregion

    // =====================================================================
    #region Grapple 特殊能力接口

    public void CancelGrappleFromSpecialAbility(
        bool playRetractAnimation = false,
        bool clearExistingMomentum = true
    )
    {
        if (grapple == null)
            return;

        grapple.CancelFromSpecialAbility(
            playRetractAnimation,
            clearExistingMomentum
        );
    }

    public void CancelManualMomentumFromSpecialAbility(
        bool clearHorizontalVelocity = true
    )
    {
        if (grapple == null)
            return;

        grapple.CancelReleaseMomentumFromSpecialAbility(
            clearHorizontalVelocity
        );
    }

    #endregion

    // =====================================================================
    #region Grapple Charge 接口

    public void RestoreGrappleCharge(
        int amount
    )
    {
        if (grappleCharges == null)
            return;

        grappleCharges.RestoreCharge(
            amount
        );
    }

    public void RestoreGrappleChargeFromKill()
    {
        if (grappleCharges == null)
            return;

        grappleCharges
            .RestoreChargeFromKill();
    }

    #endregion
}