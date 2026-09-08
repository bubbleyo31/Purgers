using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;


/// <summary>
/// Player Core 共用的蹲下與動量滑鏟控制器。
///
/// ====================================================================
///
/// 這顆元件負責：
///
/// 1. Left Control 蹲下姿態。
/// 2. 縮短 Advanced KCC Capsule Height。
/// 3. 站起前檢查頭上空間。
/// 4. 根據真實入場速度進入滑鏟。
/// 5. 保留 GrappleAirborne 落地時的水平動量。
/// 6. 根據坡度重力、摩擦與有限轉向更新滑鏟速度。
/// 7. Slide Jump 時保留水平速度。
/// 8. 平滑降低本機第一人稱 Camera Target。
///
/// ====================================================================
///
/// 這顆元件同時是一個 KCC Processor。
///
/// 一般 EnvironmentProcessor 先完成標準地面加速與摩擦，
/// 本 Processor 以較低 Priority 在專用 Stage 後面執行，
/// 只在 IsSliding 期間改寫 Kinematic Direction / Velocity。
///
/// 如此可以避免 EnvironmentProcessor 的普通跑步速度上限
/// 把 Grapple 轉入的高速滑鏟每 Tick Clamp 回一般跑速。
///
/// ====================================================================
///
/// 重要權限：
///
/// Grapple Attached / Support Pull / Profession Hard Movement Lock
/// >
/// Sliding
/// >
/// Crouch Movement
/// >
/// Normal WASD。
///
/// ====================================================================
///
/// 這裡不直接讀取 Unity Input。
/// Left Control 必須先寫入 NetInput，
/// 才能同時支援 Fusion Prediction / Resimulation。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KCC))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(PlayerGrapple))]
[RequireComponent(typeof(PlayerSprintLatchController))]
public class PlayerSlideController :
    NetworkKCCProcessor,
    ISetDynamicVelocity,
    ISetKinematicDirection,
    ISetKinematicVelocity,
    IAfterMoveStep
{
    // =====================================================================
    #region Core References


    [Header("Player Core 引用")]


    [SerializeField]
    [Tooltip(
        "Player Root 上的 Advanced KCC。\n\n" +
        "留空時會在 Awake 自動取得。")]
    private KCC kcc;


    [SerializeField]
    [Tooltip(
        "Player Root 上的 PlayerMovement。\n\n" +
        "用來取得 Camera Target 與玩家基礎移動資料。\n\n" +
        "留空時會自動取得。")]
    private PlayerMovement movement;


    [SerializeField]
    [Tooltip(
        "Player Root 上的 PlayerStateMachine。\n\n" +
        "Player.cs 會將本元件的 Crouching / Sliding 狀態交給它統一更新。\n\n" +
        "留空時會自動取得。")]
    private PlayerStateMachine stateMachine;


    [SerializeField]
    [Tooltip(
        "Player Root 上的 PlayerGrapple。\n\n" +
        "當 GrappleAirborne 高速動量落地轉入滑鏟時，" +
        "本元件會先擷取速度，再通知 Grapple 放棄 Momentum 控制權，" +
        "但不先清除水平速度。\n\n" +
        "留空時會自動取得。")]
    private PlayerGrapple grapple;


    [SerializeField]
    [Tooltip(
        "Player Root 上的 Sprint 鎖定與加速控制器。\n\n" +
        "滑鏟成立時會消耗目前 Sprint Ramp，" +
        "防止玩家利用殘留速度立刻重複滑鏟。\n\n" +
        "留空時會自動取得。")]
    private PlayerSprintLatchController sprintController;


    [SerializeField]
    [Tooltip(
        "Player Root 上額外的 CapsuleCollider。\n\n" +
        "Advanced KCC 會自己建立一顆內部 KCCCollider，" +
        "但你目前 KCC_Player Prefab Root 還有一顆獨立 CapsuleCollider。\n\n" +
        "這個欄位用來讓額外 Collider 與 KCC 一起蹲下，" +
        "避免 KCC 已變矮，但其他 Physics Trigger 仍把玩家當成站立高度。\n\n" +
        "留空時會尋找 Player Root 上的 CapsuleCollider；" +
        "不會取用 Advanced KCC Runtime 建立在子物件的 KCCCollider。")]
    private CapsuleCollider auxiliaryRootCapsule;


    #endregion


    // =====================================================================
    #region Crouch Settings


    [Header("蹲下設定")]


    [SerializeField]
    [Min(0.1f)]
    [Tooltip(
        "玩家蹲下或滑鏟時的 Advanced KCC Capsule Height。\n\n" +
        "必須至少等於 KCC Radius 的兩倍，否則 KCC 無法建立合法 Capsule。\n\n" +
        "目前 Player 高度約 1.8～2 時，第一輪建議測試 1.15～1.25。")]
    private float crouchingHeight =
        1.2f;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "蹲著移動但沒有滑鏟時，保留多少普通 WASD 輸入速度。\n\n" +
        "1 = 和一般移動一樣快。\n" +
        "0.45 = 保留約 45% 移動輸入。\n" +
        "0 = 蹲下時完全不能移動。")]
    private float crouchMovementInputMultiplier =
        0.45f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "檢查是否能站起時，將測試 Capsule 底部稍微往上抬的距離。\n\n" +
        "用來避免測試 Capsule 把腳下地面誤判成頭上障礙。\n\n" +
        "建議 0.02～0.05。")]
    private float standCheckBottomLift =
        0.03f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "站起空間檢查時，從 KCC Radius 減掉的安全邊界。\n\n" +
        "可避免玩家只是貼著牆面時，因浮點誤差永遠無法站起。\n\n" +
        "不建議大於 0.05。")]
    private float standCheckRadiusInset =
        0.02f;


    #endregion


    // =====================================================================
    #region Slide Entry And Exit


    [Header("滑鏟進入與結束")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "開始滑鏟所需的最低入場速度，以 KCC 一般 Kinematic Speed 為基準。\n\n" +
        "0.85 = 實際水平速度至少為基礎速度的 85% 才會滑鏟。\n\n" +
        "速度不足時只會蹲下，不會免費獲得滑鏟加速。")]
    private float minimumEntrySpeedMultiplier =
        0.85f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "當滑鏟速度低於這個基礎速度倍率時，結束 Sliding 並轉為普通蹲行。\n\n" +
        "這個數值應該明顯低於 Minimum Entry Speed Multiplier，" +
        "否則滑鏟剛開始就會在門檻附近反覆開關。\n\n" +
        "建議先用 0.45。")]
    private float minimumExitSpeedMultiplier =
        0.45f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "進入滑鏟時保留的原始速度倍率。\n\n" +
        "1 = 完整保留跑步或 GrappleAirborne 落地速度。\n" +
        "小於 1 = 入場時立即損失一部分動量。\n" +
        "大於 1 = 每次滑鏟都會製造額外能量，不建議。")]
    private float entrySpeedRetention =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "滑鏟的絕對安全速度上限。\n\n" +
        "這不是一般手感上限，只是防止錯誤的 Grapple / Ability 速度將 KCC 送出地圖。\n\n" +
        "0 = 不使用絕對 Clamp。\n" +
        "你目前速度 UI 上限若是 100，第一輪可先設 100。")]
    private float absoluteSafetyMaximumSpeed =
        100f;


    [SerializeField]
    [Tooltip(
        "開啟後，玩家完成一次滑鏟後，" +
        "下一次滑鏟必須重新累積足夠的 Sprint Ramp Progress。\n\n" +
        "這不只把顯示進度歸零，也會真正阻止玩家使用上一次滑鏟" +
        "留下的高速反覆連滑。")]
    private bool requireSprintRechargeAfterSlide =
        true;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "完成一次滑鏟後，下一次滑鏟所需的最低 Sprint Ramp Progress。\n\n" +
        "1 = 必須重新完成整段 Sprint 加速。\n" +
        "0.75 = 兩秒加速設定下，約重新累積 1.5 秒即可。\n\n" +
        "第一輪建議使用 1，確實阻止連續滑鏟。")]
    private float minimumSprintRechargeForNextSlide =
        1f;


    [SerializeField]
    [Tooltip(
        "開啟後，只要玩家處於普通鈎索拉動、GrappleAirborne 或" +
        "鈎索釋放動量階段，" +
        "並且在落地前持續按住蹲下，落地時就會直接嘗試進入滑鏟。\n\n" +
        "這次鈎索落地滑鏟會略過一般 Minimum Entry Speed 與" +
        " Sprint Recharge 門檻，但不會憑空補上一段速度；" +
        "角色仍必須保有可用的水平移動速度。\n\n" +
        "放開蹲下會取消本次落地滑鏟預約。")]
    private bool allowGrapplePreLandingSlide =
        true;


    #endregion


    // =====================================================================
    #region Slide Jump Settings


    [Header("滑鏟跳躍")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "成功進入滑鏟後，至少要經過多少秒才允許執行 Slide Jump。\n\n" +
        "這能防止玩家在同一個瞬間按下蹲下與跳躍，" +
        "還沒形成滑行節奏就直接取得完整水平動量。\n\n" +
        "Apex 風格第一輪建議 0.10 秒；" +
        "設為 0 則滑鏟成立後可立即跳躍。")]
    private float minimumSlideTimeBeforeJump =
        0.10f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "玩家在 Slide Jump 尚未解鎖時提早按下跳躍，" +
        "這次輸入最多保留多少秒。\n\n" +
        "若解鎖時間在 Buffer 到期前抵達，會自動執行 Slide Jump，" +
        "減少網路 Tick 與人類按鍵誤差造成的 Dead Slide。\n\n" +
        "第一輪建議 0.12 秒；設為 0 代表不保留過早輸入。")]
    private float slideJumpInputBufferDuration =
        0.12f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Slide Jump 離地時保留多少水平滑鏟速度。\n\n" +
        "1 = 完整保留當下 Slide Velocity。\n" +
        "0.9 = 離地時損失 10% 水平速度。\n" +
        "大於 1 會讓每次滑跳製造額外能量，不建議。\n\n" +
        "第一輪建議 1。")]
    private float slideJumpHorizontalMomentumRetention =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Slide Jump 成功時，沿目前滑鏟方向額外增加的固定水平速度。\n\n" +
        "這不是倍率，單位與 KCC 速度相同。\n" +
        "0 = 不憑空製造速度，只承接原有滑鏟動量。\n\n" +
        "為避免普通滑跳或鈎索高速滑跳持續膨脹，第一輪建議保持 0。")]
    private float slideJumpAdditionalHorizontalSpeed =
        0f;


    [SerializeField]
    [Tooltip(
        "開啟後，Slide Jump 成功的同一個 Fusion Tick 會立即解除蹲下。\n\n" +
        "即使玩家仍按住 Left Control，也會暫時忽略這顆蹲下輸入，" +
        "直到玩家真正放開一次後，下一次按下才可再次蹲下。\n\n" +
        "這可防止鏟跳離地後下一個 Tick 又立刻縮回蹲下 Capsule。\n\n" +
        "若頭上空間不足，仍會保留蹲下並持續嘗試安全站起，" +
        "不會把站立 Capsule 強行穿進天花板。")]
    private bool standUpImmediatelyAfterSlideJump =
        true;


    #endregion


    // =====================================================================
    #region Slide Physics


    [Header("滑鏟物理")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "平地滑鏟每秒減少的速度。\n\n" +
        "這是基礎線性摩擦，數值越大，平地滑鏟越快停止。\n\n" +
        "KCC 基礎速度約 8 時，可先用 3.5。")]
    private float flatSlideFriction =
        3.5f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "下坡時，地面切線方向的重力加速保留倍率。\n\n" +
        "1 = 使用完整坡面重力。\n" +
        "0 = 下坡不會因重力加速。")]
    private float downhillGravityMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "上坡時，反向坡面重力使用的倍率。\n\n" +
        "大於 Downhill Gravity Multiplier 時，上坡會更快失去速度。\n\n" +
        "第一輪建議 1.15。")]
    private float uphillGravityMultiplier =
        1.15f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "滑鏟期間使用 WASD 每秒最多可以將速度方向轉動幾度。\n\n" +
        "這是有限 Steering，不會把滑鏟速度瞬間變成鏡頭方向。\n\n" +
        "速度越高時，程式還會自動降低實際轉向量。\n\n" +
        "第一輪建議 75～100。")]
    private float steeringDegreesPerSecond =
        90f;


    [SerializeField]
    [Range(-1f, 0f)]
    [Tooltip(
        "玩家輸入方向與目前滑鏟方向的 Dot 低於此值時，視為反向煞車。\n\n" +
        "-1 = 必須完全反向才煞車。\n" +
        "-0.25 = 明顯向後輸入就開始煞車。")]
    private float reverseBrakeDotThreshold =
        -0.25f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "玩家在滑鏟期間向目前速度反方向輸入時，每秒額外減少的速度。\n\n" +
        "反向輸入只會煞車，不會讓玩家在同一次滑鏟中瞬間倒著滑。")]
    private float reverseBrakePerSecond =
        8f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "滑鏟速度超過 KCC 基礎速度多少倍之後，開始施加額外軟摩擦。\n\n" +
        "2.5 = 基礎速度 8 時，超過 20 才開始增加額外衰減。\n\n" +
        "這不會立即 Clamp，所以 Grapple 高速仍會完整進入滑鏟。")]
    private float softSpeedLimitMultiplier =
        2.5f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "超過 Soft Speed Limit 的速度，每多出 1 單位就額外增加多少摩擦。\n\n" +
        "數值越大，Grapple 轉入的極高速越快回到一般可控範圍。\n\n" +
        "第一輪建議 0.6～1.2。")]
    private float excessSpeedFrictionMultiplier =
        0.8f;


    #endregion


    // =====================================================================
    #region First Person Camera


    [Header("第一人稱蹲下視角")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "蹲下或滑鏟時，本機 PlayerMovement CamTarget 往下移動的距離。\n\n" +
        "只影響 Input Authority 的第一人稱視覺，不會改變 KCC 實際位置。\n\n" +
        "第一輪建議 0.45～0.65。")]
    private float crouchCameraDrop =
        0.55f;


    [SerializeField]
    [Min(0.001f)]
    [Tooltip(
        "Camera Target 從站立高度過渡到蹲下高度所需的平滑時間。\n\n" +
        "數值太小會像瞬間 Teleport，太大則視覺會明顯落後於碰撞體。\n\n" +
        "建議 0.08～0.15 秒。")]
    private float cameraTransitionSmoothTime =
        0.1f;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip(
        "開啟後顯示滑鏟進入、結束、入場速度與頭上空間阻擋。\n\n" +
        "正式 Build 建議關閉。")]
    private bool debugSlide =
        false;


    #endregion


    // =====================================================================
    #region Grapple KCC Protection Settings


    [Header("Grapple KCC 速度保護")]


    [SerializeField]
    [Tooltip(
        "開啟後，普通地形 Grapple Attached 的 Grounded Tick " +
        "會在 EnvironmentProcessor 完成後恢復本 Tick 指定的拉動速度。\n\n" +
        "只抵消 Grapple 不應承受的 Dynamic Ground Friction，" +
        "不影響普通移動、跳躍、擊退或其他職業位移。")]
    private bool protectGrappleVelocityFromGroundFriction =
        true;


    [SerializeField]
    [Tooltip(
        "開啟後，普通地形 Grapple Attached 期間暫停 StepUpProcessor。\n\n" +
        "這可避免高速擦過牆角或小台階時，Step Up 結束邏輯" +
        "把整份 Grapple DynamicVelocity 清空。\n\n" +
        "Grapple 結束後 Step Up 會自動恢復。")]
    private bool suppressStepUpWhileGrappleAttached =
        true;


    [SerializeField]
    [Tooltip(
        "開啟後顯示 Grapple Ground Friction 保護與 Step Up 暫停訊息。\n\n" +
        "每 Tick 可能產生大量訊息，正式 Build 必須關閉。")]
    private bool debugGrappleKCCProtection =
        false;


    #endregion


    // =====================================================================
    #region Network State


    /// <summary>
    /// 玩家是否正在使用蹲下 Capsule。
    ///
    /// Sliding 一定同時是 Crouched。
    /// 玩家如果放開 Left Control 但頭上被擋住，
    /// 這個狀態仍會維持 true。
    /// </summary>
    [Networked]
    public NetworkBool IsCrouched
    {
        get;
        private set;
    }


    /// <summary>
    /// 玩家是否正在由本 Controller 接管水平 Kinematic Velocity。
    /// </summary>
    [Networked]
    public NetworkBool IsSliding
    {
        get;
        private set;
    }


    /// <summary>
    /// 本次滑鏟在世界空間中的地面切線速度。
    ///
    /// 這是 Gameplay State，需要進入 Fusion Prediction / Resimulation。
    /// </summary>
    [Networked]
    private Vector3 SlideVelocity
    {
        get;
        set;
    }


    /// <summary>
    /// 玩家是否已經完成過一次滑鏟，且下一次滑鏟前
    /// 必須先重新累積 Sprint Ramp。
    ///
    /// 這是 Gameplay State，必須參與 Fusion Prediction / Resimulation。
    /// </summary>
    [Networked]
    private NetworkBool SlideSprintRechargeRequired
    {
        get;
        set;
    }


    /// <summary>
    /// 玩家是否已在鈎索後滯空期間按住蹲下，
    /// 等待下一次接觸地面時直接轉入滑鏟。
    ///
    /// 這是 Gameplay State，必須參與 Fusion Prediction / Resimulation，
    /// 不能只用普通 bool 暫存在本機。
    /// </summary>
    [Networked]
    private NetworkBool GrapplePreLandingSlideArmed
    {
        get;
        set;
    }


    /// <summary>
    /// 本次滑鏟要經過多久才允許轉成 Slide Jump。
    ///
    /// 使用 Fusion TickTimer，確保 Host、Client Prediction
    /// 與 Resimulation 都依相同 Tick 得到結果。
    /// </summary>
    [Networked]
    private TickTimer SlideJumpUnlockTimer
    {
        get;
        set;
    }


    /// <summary>
    /// 玩家過早按下 Jump 時暫存輸入的有效期限。
    ///
    /// Buffer 只屬於目前這次滑鏟；滑鏟結束時一定清除。
    /// </summary>
    [Networked]
    private TickTimer SlideJumpBufferTimer
    {
        get;
        set;
    }


    /// <summary>
    /// Slide Jump 成功後，是否正在等待玩家把實體蹲下鍵放開。
    ///
    /// true 時即使 Left Control 仍維持按住，
    /// PlayerSlideController 也會把本 Tick 視為沒有蹲下輸入。
    ///
    /// 這會影響 Capsule 與移動狀態，因此必須參與
    /// Fusion Prediction / Resimulation。
    /// </summary>
    [Networked]
    private NetworkBool IgnoreCrouchUntilReleasedAfterSlideJump
    {
        get;
        set;
    }


    #endregion


    // =====================================================================
    #region Runtime State


    private readonly KCCOverlapInfo
        standOverlapInfo =
            new KCCOverlapInfo(32);


    private float standingHeight;

    private float auxiliaryStandingHeight;

    private Vector3 auxiliaryStandingCenter;

    private Transform cameraTarget;

    private Vector3 standingCameraLocalPosition;

    private float cameraVerticalSmoothVelocity;

    private bool processorRegistered;


    #endregion


    // =====================================================================
    #region Public Data


    /// <summary>
    /// PlayerMovement 的普通 WASD 保留倍率。
    /// </summary>
    public float MovementInputInfluence
    {
        get
        {
            if (IsSliding)
            {
                return 0f;
            }

            if (IsCrouched)
            {
                return Mathf.Clamp01(
                    crouchMovementInputMultiplier
                );
            }

            return 1f;
        }
    }


    /// <summary>
    /// 目前滑鏟速度，供 UI / Debug / Animation 使用。
    /// </summary>
    public Vector3 CurrentSlideVelocity =>
        SlideVelocity;


    /// <summary>
    /// 目前滑鏟速度大小。
    /// </summary>
    public float CurrentSlideSpeed =>
        SlideVelocity.magnitude;


    #endregion


    // =====================================================================
    #region Unity And Fusion


    private void Awake()
    {
        CacheReferences();

        /*
         * Awake 發生在 Fusion Spawn / Snapshot 套用之前。
         * 這裡先保存 Prefab 原始站立高度，
         * 避免 Late Join Client 加入時剛好看到玩家正在蹲下，
         * 把已經縮短的網路 KCC Height 誤當成站立高度。
         */
        CaptureStandingHeightIfNeeded();

        CaptureAuxiliaryCapsuleIfNeeded();
    }


    public override void Spawned()
    {
        CacheReferences();

        if (kcc == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerSlideController)}] " +
                $"Player Root 找不到 KCC。",
                this
            );

            return;
        }

        CaptureStandingHeightIfNeeded();

        TryRegisterProcessor();

        CacheCameraTarget();

        if (Object.HasStateAuthority)
        {
            IsCrouched =
                false;

            IsSliding =
                false;

            SlideVelocity =
                Vector3.zero;

            SlideSprintRechargeRequired =
                false;

            GrapplePreLandingSlideArmed =
                false;

            SlideJumpUnlockTimer =
                default;

            SlideJumpBufferTimer =
                default;

            IgnoreCrouchUntilReleasedAfterSlideJump =
                false;
        }

        ApplyCapsuleHeight(
            IsCrouched
                ? ResolveCrouchingHeight()
                : standingHeight
        );
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        UnregisterProcessor();
    }


    public override void Render()
    {
        /*
         * Spawned 時如果 KCC 尚未完成自己的初始化，
         * 這裡會在後續 Frame 重試。
         */
        if (processorRegistered == false)
        {
            TryRegisterProcessor();
        }

        /*
         * Advanced KCC Height 本身會透過 KCC Network Properties 同步，
         * 但 Player Root 上額外的 CapsuleCollider 不屬於 KCC 網路資料。
         *
         * State / Input Authority 已在 Simulate 立即調整；
         * Proxy 則在 Render 依同步後的 KCC Height 跟進，
         * 避免遠端玩家仍保留站立高度的額外 Physics Capsule。
         */
        if (kcc != null)
        {
            ApplyAuxiliaryCapsuleHeight(
                kcc.Settings.Height
            );
        }

        UpdateFirstPersonCamera();
    }


    private void OnDestroy()
    {
        UnregisterProcessor();
    }


    private void OnValidate()
    {
        crouchingHeight =
            Mathf.Max(
                0.1f,
                crouchingHeight
            );

        crouchMovementInputMultiplier =
            Mathf.Clamp01(
                crouchMovementInputMultiplier
            );

        minimumEntrySpeedMultiplier =
            Mathf.Max(
                0f,
                minimumEntrySpeedMultiplier
            );

        minimumExitSpeedMultiplier =
            Mathf.Clamp(
                minimumExitSpeedMultiplier,
                0f,
                minimumEntrySpeedMultiplier
            );

        minimumSprintRechargeForNextSlide =
            Mathf.Clamp01(
                minimumSprintRechargeForNextSlide
            );

        entrySpeedRetention =
            Mathf.Max(
                0f,
                entrySpeedRetention
            );

        minimumSlideTimeBeforeJump =
            Mathf.Max(
                0f,
                minimumSlideTimeBeforeJump
            );

        slideJumpInputBufferDuration =
            Mathf.Max(
                0f,
                slideJumpInputBufferDuration
            );

        slideJumpHorizontalMomentumRetention =
            Mathf.Max(
                0f,
                slideJumpHorizontalMomentumRetention
            );

        slideJumpAdditionalHorizontalSpeed =
            Mathf.Max(
                0f,
                slideJumpAdditionalHorizontalSpeed
            );
    }


    #endregion


    // =====================================================================
    #region Main Simulation


    /// <summary>
    /// 在 PlayerMovement 處理普通 Jump 之前，先判斷本 Tick 的 Jump
    /// 是否應該由目前滑鏟接管。
    ///
    /// 重要責任分工：
    /// - 本方法只判斷時間與 Input Buffer。
    /// - 真正的 KCC.Jump 仍只由 PlayerMovement 執行。
    /// - 成功跳躍後，再由 Simulate() 結束滑鏟並交接水平動量。
    ///
    /// 這能避免 PlayerMovement 與 PlayerSlideController
    /// 在同一個 Fusion Tick 各自施加一次跳躍。
    /// </summary>
    /// <param name="input">本 Tick Fusion Input。</param>
    /// <param name="previousButtons">上一 Tick 的 NetworkButtons。</param>
    /// <param name="slideJumpAllowed">
    /// false 代表 Grapple Attached、Support Pull 或職業能力等
    /// 高優先系統禁止本 Tick 的 Slide Jump。
    /// </param>
    /// <param name="slideOwnsGroundJumpInput">
    /// true 代表目前正在合法地面滑鏟，普通 Ground Jump 必須暫停，
    /// 等本控制器決定立即執行或暫存這次 Jump。
    /// </param>
    /// <returns>true 代表本 Tick 應由 PlayerMovement 執行一次 Ground Jump。</returns>
    public bool EvaluateSlideJumpInput(
        NetInput input,
        NetworkButtons previousButtons,
        bool slideJumpAllowed,
        out bool slideOwnsGroundJumpInput
    )
    {
        slideOwnsGroundJumpInput =
            false;

        if (kcc == null ||
            IsSliding == false)
        {
            SlideJumpBufferTimer =
                default;

            return false;
        }

        /*
         * IsSliding 可能在玩家滑出地面邊緣後，多保留到本 Tick 的
         * PlayerSlideController.Simulate() 才正式結束。
         *
         * 這種情況不能攔截空中的 Jump，否則會誤吃掉二段跳輸入。
         */
        if (kcc.Data.IsGrounded == false)
        {
            SlideJumpBufferTimer =
                default;

            return false;
        }

        slideOwnsGroundJumpInput =
            true;

        /*
         * 滑鏟仍然要持有 Ground Jump 輸入，
         * 但高優先移動鎖定時不能把它轉成 Slide Jump。
         *
         * 否則 PlayerMovement 會比本元件的 Simulate() 更早執行，
         * 有機會先跳起來，之後才被 Higher Priority Movement 結束滑鏟。
         */
        if (slideJumpAllowed == false)
        {
            SlideJumpBufferTimer =
                default;

            return false;
        }

        bool crouchHeld =
            input.Buttons.IsSet(
                InputButton.Crouch
            );

        if (crouchHeld == false)
        {
            SlideJumpBufferTimer =
                default;

            return false;
        }

        bool jumpPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Jump
            );

        if (jumpPressed)
        {
            float bufferDuration =
                Mathf.Max(
                    0f,
                    slideJumpInputBufferDuration
                );

            SlideJumpBufferTimer =
                bufferDuration > 0.0001f
                    ? TickTimer.CreateFromSeconds(
                        Runner,
                        bufferDuration
                    )
                    : default;

            if (debugSlide)
            {
                Debug.Log(
                    "[Player Slide] 收到 Slide Jump 輸入。" +
                    $"\nBuffer Duration：{bufferDuration:F3}",
                    this
                );
            }
        }

        bool jumpUnlocked =
            SlideJumpUnlockTimer
                .ExpiredOrNotRunning(
                    Runner
                );

        bool bufferedJumpAvailable =
            SlideJumpBufferTimer
                .ExpiredOrNotRunning(
                    Runner
                ) == false;

        if (jumpUnlocked &&
            (jumpPressed || bufferedJumpAvailable))
        {
            SlideJumpBufferTimer =
                default;

            return true;
        }

        return false;
    }


    /// <summary>
    /// 由 Player.FixedUpdateNetwork 每 Tick 呼叫。
    ///
    /// 必須排在：
    ///
    /// PlayerMovement.Simulate()
    /// 之後，
    /// PlayerGrapple.Simulate()
    /// 之前。
    /// </summary>
    /// <param name="input">本 Tick Fusion Input。</param>
    /// <param name="jumpedThisTick">PlayerMovement 是否在本 Tick 成功跳躍。</param>
    /// <param name="grapplePressedThisTick">本 Tick 是否主動按下 Grapple。</param>
    /// <param name="hardMovementControlActive">Attached Grapple 等系統是否已取得高優先移動權。</param>
    /// <param name="nonGrappleMovementAllowed">Support Pull / Profession Ability 是否仍允許玩家主動移動。</param>
    public void Simulate(
        NetInput input,
        bool jumpedThisTick,
        bool grapplePressedThisTick,
        bool hardMovementControlActive,
        bool nonGrappleMovementAllowed
    )
    {
        if (kcc == null)
        {
            return;
        }

        if (processorRegistered == false)
        {
            TryRegisterProcessor();
        }

        bool physicalCrouchHeld =
            input.Buttons.IsSet(
                InputButton.Crouch
            );

        bool crouchHeld =
            physicalCrouchHeld;

        /*
         * Slide Jump 成功後不能只把 IsCrouched 關閉一次。
         * 玩家通常仍按住 Left Control；若不建立 Release Latch，
         * 下一個 Tick 就會再次被 EnsureCrouched() 拉回蹲下。
         *
         * 因此鏟跳後持續忽略舊的 Hold，直到偵測到真正放開一次。
         */
        if (IgnoreCrouchUntilReleasedAfterSlideJump)
        {
            if (physicalCrouchHeld == false)
            {
                IgnoreCrouchUntilReleasedAfterSlideJump =
                    false;
            }
            else
            {
                crouchHeld =
                    false;
            }
        }

        bool movementHardLocked =
            hardMovementControlActive ||
            nonGrappleMovementAllowed == false;

        /*
         * 鈎索落地滑鏟使用「空中預約」而不是等落地後才讀按鍵。
         *
         * 玩家可能在碰地前數個 Tick 就先按住蹲下；
         * 若只在 Grounded 當下使用一般滑鏟規則，
         * 容易被普通入場速度或 Sprint Recharge 門檻擋掉。
         */
        UpdateGrapplePreLandingSlideArm(
            crouchHeld
        );

        // -------------------------------------------------------------
        // Existing Slide
        // -------------------------------------------------------------

        if (IsSliding)
        {
            /*
             * Jump / Grapple / 強制位移都比 Sliding 優先。
             *
             * Slide Jump 與主動 Grapple 會保留當下水平速度，
             * 讓後續系統從這份速度繼續處理。
             *
             * Support Pull / Profession Hard Lock 則不應保留滑鏟控制速度。
             */
            if (movementHardLocked)
            {
                EndSlide(
                    false,
                    "Higher Priority Movement"
                );
            }
            else if (jumpedThisTick ||
                     grapplePressedThisTick)
            {
                if (jumpedThisTick &&
                    standUpImmediatelyAfterSlideJump)
                {
                    IgnoreCrouchUntilReleasedAfterSlideJump =
                        true;

                    /*
                     * 本 Tick 的 crouchHeld 已在方法前段計算完成，
                     * 所以除了建立跨 Tick Latch，還必須立即改成本地 false，
                     * 才能在下方 Crouch Stance 階段馬上嘗試站起。
                     */
                    crouchHeld =
                        false;
                }

                EndSlide(
                    true,
                    jumpedThisTick
                        ? "Slide Jump"
                        : "Grapple Started",
                    jumpedThisTick
                        ? slideJumpHorizontalMomentumRetention
                        : 1f,
                    jumpedThisTick
                        ? slideJumpAdditionalHorizontalSpeed
                        : 0f
                );
            }
            else if (crouchHeld == false)
            {
                EndSlide(
                    true,
                    "Crouch Released"
                );
            }
            else if (kcc.Data.IsGrounded == false)
            {
                EndSlide(
                    true,
                    "Left Ground"
                );
            }
            else
            {
                EnsureCrouched();

                TickSlide(
                    input.Direction
                );

                return;
            }
        }

        // -------------------------------------------------------------
        // Crouch Stance
        // -------------------------------------------------------------

        if (crouchHeld)
        {
            EnsureCrouched();
        }
        else
        {
            TryStandUp();
        }

        // -------------------------------------------------------------
        // New Slide
        // -------------------------------------------------------------

        if (crouchHeld &&
            jumpedThisTick == false &&
            grapplePressedThisTick == false &&
            movementHardLocked == false &&
            kcc.Data.IsGrounded)
        {
            bool forceGrappleLandingSlide =
                GrapplePreLandingSlideArmed;

            TryStartSlide(
                forceGrappleLandingSlide
            );

            /*
             * 已接觸地面就代表本次預約已消耗。
             * 即使水平速度幾乎為零、滑鏟無法成立，
             * 也不能把舊預約帶到之後的普通落地。
             */
            GrapplePreLandingSlideArmed =
                false;
        }
    }


    #endregion


    // =====================================================================
    #region Slide Start


    private bool TryStartSlide(
        bool forceGrappleLandingSlide = false
    )
    {
        Vector3 groundNormal =
            ResolveGroundNormal();

        Vector3 entryVelocity =
            ResolveFastestEntryVelocity(
                groundNormal
            );

        float baseSpeed =
            ResolveBaseMovementSpeed();

        float requiredEntrySpeed =
            baseSpeed *
            Mathf.Max(
                0f,
                minimumEntrySpeedMultiplier
            );

        if (forceGrappleLandingSlide == false &&
            entryVelocity.magnitude <
                requiredEntrySpeed)
        {
            return false;
        }

        entryVelocity *=
            Mathf.Max(
                0f,
                entrySpeedRetention
            );

        entryVelocity =
            ClampToAbsoluteSafetySpeed(
                entryVelocity
            );

        if (entryVelocity.sqrMagnitude <=
            0.0001f)
        {
            return false;
        }

        /*
         * 只把 Sprint Ramp 歸零仍不足以阻止連滑：
         * 上一次滑鏟留下的 Kinematic / Real Velocity
         * 可能依然高於 Minimum Entry Speed。
         *
         * 因此滑鏟曾經消耗過動能後，
         * 下一次還必須確認 Sprint Ramp 已重新累積到門檻。
         */
        if (forceGrappleLandingSlide == false &&
            requireSprintRechargeAfterSlide &&
            SlideSprintRechargeRequired)
        {
            float currentSprintRamp =
                sprintController != null
                    ? sprintController
                        .SprintRampProgress
                    : 0f;

            float requiredSprintRamp =
                Mathf.Clamp01(
                    minimumSprintRechargeForNextSlide
                );

            bool sprintRecharged =
                sprintController != null &&
                sprintController.IsSprintActive &&
                currentSprintRamp >=
                    requiredSprintRamp;

            if (sprintRecharged == false)
            {
                if (debugSlide)
                {
                    Debug.Log(
                        "[Player Slide] 尚未重新累積足夠 Sprint 動能。" +
                        $"\nCurrent Ramp：{currentSprintRamp:F2}" +
                        $"\nRequired Ramp：{requiredSprintRamp:F2}",
                        this
                    );
                }

                return false;
            }
        }

        /*
         * 必須先擷取上面的 Entry Velocity，
         * 才能讓 Grapple 放棄 Momentum。
         *
         * clearHorizontalVelocity = false，
         * 因為程式下面會自己完成 Dynamic → Slide Kinematic 的單一次交接。
         */
        bool enteredFromGrappleMomentum =
            grapple != null &&
            grapple.IsReleaseMomentumActive;

        if (enteredFromGrappleMomentum)
        {
            grapple
                .CancelReleaseMomentumFromSpecialAbility(
                    false
                );
        }

        /*
         * 滑鏟在確定可以成立後才消耗 Sprint 動能。
         * 速度不足或充能不足而只進入 Crouching 時，不會誤消耗進度。
         */
        if (sprintController != null)
        {
            sprintController
                .ConsumeSprintMomentumForSlide();
        }

        SlideSprintRechargeRequired =
            true;

        Vector3 currentDynamicVelocity =
            kcc.Data.DynamicVelocity;

        /*
         * 水平動量已經收進 SlideVelocity，
         * DynamicVelocity 只保留世界 Y，
         * 避免同一份 Grapple 速度同時存在 Dynamic 與 Kinematic，
         * 造成兩倍速度。
         */
        kcc.SetDynamicVelocity(
            Vector3.up *
            currentDynamicVelocity.y
        );

        kcc.SetInputDirection(
            Vector3.zero
        );

        SlideVelocity =
            entryVelocity;

        IsSliding =
            true;

        float slideJumpUnlockDelay =
            Mathf.Max(
                0f,
                minimumSlideTimeBeforeJump
            );

        SlideJumpUnlockTimer =
            slideJumpUnlockDelay > 0.0001f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    slideJumpUnlockDelay
                )
                : default;

        SlideJumpBufferTimer =
            default;

        EnsureCrouched();

        kcc.SetKinematicVelocity(
            SlideVelocity
        );

        if (debugSlide)
        {
            Debug.Log(
                $"[Player Slide] Start" +
                $"\nEntry Speed：{entryVelocity.magnitude:F2}" +
                $"\nRequired Speed：{requiredEntrySpeed:F2}" +
                $"\nDirection：{entryVelocity.normalized}" +
                $"\nFrom Grapple Momentum：" +
                $"{enteredFromGrappleMomentum}" +
                $"\nGrapple Pre-Landing Bypass：" +
                $"{forceGrappleLandingSlide}" +
                $"\nSlide Jump Unlock Delay：" +
                $"{slideJumpUnlockDelay:F3}",
                this
            );
        }

        return true;
    }


    /// <summary>
    /// 維護「鈎索後滯空預先按住蹲下」的落地滑鏟預約。
    ///
    /// 成立條件：
    /// 1. Inspector 已開啟此功能。
    /// 2. 玩家尚未接觸地面。
    /// 3. 玩家仍按住蹲下。
    /// 4. 玩家正在被普通鈎索拉動、處於 GrappleAirborne，
    ///    或 Grapple Release Momentum 尚未結束。
    ///
    /// 放開蹲下會立即取消預約，避免玩家只點一下蹲下，
    /// 很久之後落地仍被強制送進滑鏟。
    /// </summary>
    private void UpdateGrapplePreLandingSlideArm(
        bool crouchHeld
    )
    {
        if (allowGrapplePreLandingSlide == false ||
            crouchHeld == false)
        {
            GrapplePreLandingSlideArmed =
                false;

            return;
        }

        if (kcc.Data.IsGrounded)
        {
            return;
        }

        bool hasGrappleLandingContext =
            grapple != null &&
            (grapple.IsNormalPlayerPullAttached ||
             grapple.IsReleaseMomentumActive);

        if (hasGrappleLandingContext == false &&
            stateMachine != null)
        {
            hasGrappleLandingContext =
                stateMachine.CurrentState ==
                PlayerMovementState.GrappleAirborne;
        }

        if (hasGrappleLandingContext)
        {
            bool wasAlreadyArmed =
                GrapplePreLandingSlideArmed;

            GrapplePreLandingSlideArmed =
                true;

            if (debugSlide &&
                wasAlreadyArmed == false)
            {
                Debug.Log(
                    "[Player Slide] 已預約鈎索落地滑鏟；" +
                    "持續按住蹲下即可在接地時轉入滑鏟。",
                    this
                );
            }
        }
    }


    private Vector3 ResolveFastestEntryVelocity(
        Vector3 groundNormal
    )
    {
        Vector3 realVelocity =
            Vector3.ProjectOnPlane(
                kcc.Data.RealVelocity,
                groundNormal
            );

        Vector3 dynamicVelocity =
            Vector3.ProjectOnPlane(
                kcc.Data.DynamicVelocity,
                groundNormal
            );

        Vector3 kinematicVelocity =
            Vector3.ProjectOnPlane(
                kcc.Data.KinematicVelocity,
                groundNormal
            );

        Vector3 selected =
            realVelocity;

        if (dynamicVelocity.sqrMagnitude >
            selected.sqrMagnitude)
        {
            selected =
                dynamicVelocity;
        }

        if (kinematicVelocity.sqrMagnitude >
            selected.sqrMagnitude)
        {
            selected =
                kinematicVelocity;
        }

        return selected;
    }


    #endregion


    // =====================================================================
    #region Slide Tick


    private void TickSlide(
        Vector2 rawMoveInput
    )
    {
        float deltaTime =
            Runner != null
                ? Runner.DeltaTime
                : Time.fixedDeltaTime;

        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 groundNormal =
            ResolveGroundNormal();

        Vector3 velocity =
            Vector3.ProjectOnPlane(
                SlideVelocity,
                groundNormal
            );

        if (velocity.sqrMagnitude <=
            0.0001f)
        {
            EndSlide(
                false,
                "Velocity Lost"
            );

            return;
        }

        Vector3 gravity =
            kcc.Data.Gravity;

        if (gravity.sqrMagnitude <=
            0.0001f)
        {
            gravity =
                Physics.gravity;
        }

        Vector3 slopeAcceleration =
            Vector3.ProjectOnPlane(
                gravity,
                groundNormal
            );

        float slopeAlongVelocity =
            Vector3.Dot(
                slopeAcceleration,
                velocity.normalized
            );

        float slopeMultiplier =
            slopeAlongVelocity >= 0f
                ? Mathf.Max(
                    0f,
                    downhillGravityMultiplier
                )
                : Mathf.Max(
                    0f,
                    uphillGravityMultiplier
                );

        velocity +=
            slopeAcceleration *
            slopeMultiplier *
            deltaTime;

        Vector2 moveInput =
            Vector2.ClampMagnitude(
                rawMoveInput,
                1f
            );

        bool reverseBraking =
            false;

        if (moveInput.sqrMagnitude >
            0.0001f &&
            velocity.sqrMagnitude >
            0.0001f)
        {
            Vector3 localDesiredDirection =
                new Vector3(
                    moveInput.x,
                    0f,
                    moveInput.y
                );

            Vector3 desiredDirection =
                kcc.Data.TransformRotation *
                localDesiredDirection;

            desiredDirection =
                Vector3.ProjectOnPlane(
                    desiredDirection,
                    groundNormal
                );

            if (desiredDirection.sqrMagnitude >
                0.0001f)
            {
                desiredDirection.Normalize();

                float directionDot =
                    Vector3.Dot(
                        velocity.normalized,
                        desiredDirection
                    );

                reverseBraking =
                    directionDot <=
                    reverseBrakeDotThreshold;

                if (reverseBraking == false)
                {
                    float currentSpeed =
                        velocity.magnitude;

                    float baseSpeed =
                        Mathf.Max(
                            0.01f,
                            ResolveBaseMovementSpeed()
                        );

                    /*
                     * 速度越高，轉向能力越弱。
                     *
                     * 這可防止 Grapple 高速落地後
                     * 只靠 WASD 一 Tick 就完全折返。
                     */
                    float highSpeedSteeringScale =
                        Mathf.Clamp01(
                            baseSpeed /
                            Mathf.Max(
                                baseSpeed,
                                currentSpeed
                            )
                        );

                    float maximumTurnRadians =
                        steeringDegreesPerSecond *
                        Mathf.Deg2Rad *
                        moveInput.magnitude *
                        highSpeedSteeringScale *
                        deltaTime;

                    Vector3 steeredDirection =
                        Vector3.RotateTowards(
                            velocity.normalized,
                            desiredDirection,
                            maximumTurnRadians,
                            0f
                        );

                    velocity =
                        steeredDirection.normalized *
                        currentSpeed;
                }
            }
        }

        float speed =
            velocity.magnitude;

        float frictionPerSecond =
            Mathf.Max(
                0f,
                flatSlideFriction
            );

        if (reverseBraking)
        {
            frictionPerSecond +=
                Mathf.Max(
                    0f,
                    reverseBrakePerSecond
                );
        }

        float softSpeedLimit =
            ResolveBaseMovementSpeed() *
            Mathf.Max(
                0f,
                softSpeedLimitMultiplier
            );

        if (softSpeedLimit > 0f &&
            speed > softSpeedLimit)
        {
            frictionPerSecond +=
                (speed - softSpeedLimit) *
                Mathf.Max(
                    0f,
                    excessSpeedFrictionMultiplier
                );
        }

        speed =
            Mathf.MoveTowards(
                speed,
                0f,
                frictionPerSecond *
                deltaTime
            );

        SlideVelocity =
            speed > 0f &&
            velocity.sqrMagnitude > 0.0001f
                ? ClampToAbsoluteSafetySpeed(
                    velocity.normalized *
                    speed
                )
                : Vector3.zero;

        float exitSpeed =
            ResolveBaseMovementSpeed() *
            Mathf.Max(
                0f,
                minimumExitSpeedMultiplier
            );

        if (speed <= exitSpeed)
        {
            EndSlide(
                true,
                "Below Exit Speed"
            );

            return;
        }

        kcc.SetInputDirection(
            Vector3.zero
        );

        kcc.SetKinematicVelocity(
            SlideVelocity
        );
    }


    private void EndSlide(
        bool preserveHorizontalVelocity,
        string reason,
        float horizontalVelocityRetention = 1f,
        float additionalHorizontalSpeed = 0f
    )
    {
        if (IsSliding == false)
        {
            return;
        }

        Vector3 finalVelocity =
            SlideVelocity;

        if (preserveHorizontalVelocity)
        {
            finalVelocity *=
                Mathf.Max(
                    0f,
                    horizontalVelocityRetention
                );

            float additionalSpeed =
                Mathf.Max(
                    0f,
                    additionalHorizontalSpeed
                );

            if (additionalSpeed > 0.0001f &&
                finalVelocity.sqrMagnitude >
                    0.0001f)
            {
                finalVelocity +=
                    finalVelocity.normalized *
                    additionalSpeed;
            }

            finalVelocity =
                ClampToAbsoluteSafetySpeed(
                    finalVelocity
                );
        }
        else
        {
            finalVelocity =
                Vector3.zero;
        }

        IsSliding =
            false;

        SlideVelocity =
            Vector3.zero;

        SlideJumpUnlockTimer =
            default;

        SlideJumpBufferTimer =
            default;

        kcc.SetInputDirection(
            Vector3.zero
        );

        kcc.SetKinematicVelocity(
            finalVelocity
        );

        if (debugSlide)
        {
            Debug.Log(
                $"[Player Slide] End" +
                $"\nReason：{reason}" +
                $"\nPreserve Velocity：{preserveHorizontalVelocity}" +
                $"\nRetention：{horizontalVelocityRetention:F2}" +
                $"\nAdditional Speed：{additionalHorizontalSpeed:F2}" +
                $"\nFinal Speed：{finalVelocity.magnitude:F2}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Crouch And Stand


    private void EnsureCrouched()
    {
        IsCrouched =
            true;

        ApplyCapsuleHeight(
            ResolveCrouchingHeight()
        );
    }


    private bool TryStandUp()
    {
        if (IsCrouched == false)
        {
            ApplyCapsuleHeight(
                standingHeight
            );

            return true;
        }

        if (CanUseStandingCapsule() ==
            false)
        {
            if (debugSlide)
            {
                Debug.Log(
                    "[Player Slide] 頭上空間不足，維持蹲下。",
                    this
                );
            }

            return false;
        }

        IsCrouched =
            false;

        ApplyCapsuleHeight(
            standingHeight
        );

        return true;
    }


    private bool CanUseStandingCapsule()
    {
        if (kcc == null)
        {
            return false;
        }

        float targetStandingHeight =
            Mathf.Max(
                kcc.Settings.Radius * 2f,
                standingHeight
            );

        float currentCrouchingHeight =
            ResolveCrouchingHeight();

        if (targetStandingHeight <=
            currentCrouchingHeight +
            0.001f)
        {
            return true;
        }

        float bottomLift =
            Mathf.Clamp(
                standCheckBottomLift,
                0f,
                targetStandingHeight -
                kcc.Settings.Radius * 2f
            );

        float checkRadius =
            Mathf.Max(
                0.01f,
                kcc.Settings.Radius -
                Mathf.Max(
                    0f,
                    standCheckRadiusInset
                )
            );

        float checkHeight =
            Mathf.Max(
                checkRadius * 2f,
                targetStandingHeight -
                bottomLift
            );

        Vector3 checkPosition =
            kcc.Data.TargetPosition +
            Vector3.up *
            bottomLift;

        bool hasBlockingOverlap =
            kcc.CapsuleOverlap(
                standOverlapInfo,
                checkPosition,
                checkRadius,
                checkHeight,
                QueryTriggerInteraction.Ignore
            );

        return hasBlockingOverlap ==
               false;
    }


    private float ResolveCrouchingHeight()
    {
        if (kcc == null)
        {
            return Mathf.Max(
                0.1f,
                crouchingHeight
            );
        }

        return Mathf.Clamp(
            crouchingHeight,
            kcc.Settings.Radius * 2f,
            Mathf.Max(
                kcc.Settings.Radius * 2f,
                standingHeight
            )
        );
    }


    private void ApplyCapsuleHeight(
        float targetHeight
    )
    {
        if (kcc == null ||
            targetHeight <= 0f)
        {
            return;
        }

        if (Mathf.Abs(
                kcc.Settings.Height -
                targetHeight
            ) > 0.001f)
        {
            kcc.SetHeight(
                targetHeight
            );
        }

        ApplyAuxiliaryCapsuleHeight(
            targetHeight
        );
    }


    #endregion


    // =====================================================================
    #region KCC Processor


    /// <summary>
    /// EnvironmentProcessor 的預設 Priority 是 1000。
    ///
    /// KCC 是高 Priority 先執行，
    /// 所以這裡使用 900，讓一般 Environment 先算完，
    /// 本 Controller 再在 Sliding 期間改寫最終 Kinematic 結果。
    /// </summary>
    public override float GetPriority(
        KCC targetKCC
    ) =>
        EnvironmentProcessor.DefaultPriority -
        100;


    /// <summary>
    /// 普通 Grapple Attached 剛離地的 Tick，
    /// 在 EnvironmentProcessor 套用地面摩擦後，
    /// 恢復 PlayerGrapple 本 Tick 明確要求的 DynamicVelocity。
    ///
    /// 真正離地後不覆寫，繼續保留 Environment 的 Gravity 與 Air Friction。
    /// </summary>
    public void Execute(
        ISetDynamicVelocity stage,
        KCC targetKCC,
        KCCData data
    )
    {
        if (protectGrappleVelocityFromGroundFriction ==
                false ||
            grapple == null ||
            targetKCC.FixedData.IsGrounded ==
                false)
        {
            return;
        }

        if (grapple
                .TryGetProtectedAttachedDynamicVelocity(
                    out Vector3 protectedVelocity
                ) == false)
        {
            return;
        }

        data.DynamicVelocity =
            protectedVelocity;

        if (debugGrappleKCCProtection)
        {
            Debug.Log(
                $"[Grapple KCC Protection] " +
                $"已抵消 Ground Friction。" +
                $"\nVelocity：{protectedVelocity}",
                this
            );
        }
    }


    public void Execute(
        ISetKinematicDirection stage,
        KCC targetKCC,
        KCCData data
    )
    {
        if (IsSliding == false)
        {
            return;
        }

        data.KinematicDirection =
            Vector3.zero;
    }


    public void Execute(
        ISetKinematicVelocity stage,
        KCC targetKCC,
        KCCData data
    )
    {
        if (IsSliding == false)
        {
            return;
        }

        data.KinematicVelocity =
            SlideVelocity;
    }


    /// <summary>
    /// 本 Processor Priority 為 900，會在 Priority -1000 的
    /// StepUpProcessor 以前進入 AfterMoveStep Stage。
    ///
    /// 普通 Grapple Attached 期間暫停 Step Up，
    /// 避免 Step Up 結束時清空 DynamicVelocity。
    /// </summary>
    public void Execute(
        AfterMoveStep stage,
        KCC targetKCC,
        KCCData data
    )
    {
        if (suppressStepUpWhileGrappleAttached ==
                false ||
            grapple == null ||
            grapple.ShouldProtectAttachedVelocity ==
                false)
        {
            return;
        }

        targetKCC
            .SuppressProcessors<StepUpProcessor>();

        if (debugGrappleKCCProtection)
        {
            Debug.Log(
                "[Grapple KCC Protection] " +
                "Attached 期間已暫停 StepUpProcessor。",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Helpers


    private void CacheReferences()
    {
        if (kcc == null)
        {
            kcc =
                GetComponent<KCC>();
        }

        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }

        if (stateMachine == null)
        {
            stateMachine =
                GetComponent<PlayerStateMachine>();
        }

        if (grapple == null)
        {
            grapple =
                GetComponent<PlayerGrapple>();
        }

        if (sprintController == null)
        {
            sprintController =
                GetComponent<
                    PlayerSprintLatchController
                >();
        }

        if (auxiliaryRootCapsule == null)
        {
            auxiliaryRootCapsule =
                GetComponent<CapsuleCollider>();
        }
    }


    private void CaptureStandingHeightIfNeeded()
    {
        if (standingHeight > 0.0001f ||
            kcc == null)
        {
            return;
        }

        standingHeight =
            Mathf.Max(
                kcc.Settings.Radius * 2f,
                kcc.Settings.Height
            );
    }


    private void CaptureAuxiliaryCapsuleIfNeeded()
    {
        if (auxiliaryRootCapsule == null ||
            auxiliaryStandingHeight > 0.0001f)
        {
            return;
        }

        auxiliaryStandingHeight =
            Mathf.Max(
                auxiliaryRootCapsule.radius * 2f,
                auxiliaryRootCapsule.height
            );

        auxiliaryStandingCenter =
            auxiliaryRootCapsule.center;
    }


    private void ApplyAuxiliaryCapsuleHeight(
        float targetKCCHeight
    )
    {
        if (auxiliaryRootCapsule == null)
        {
            return;
        }

        CaptureAuxiliaryCapsuleIfNeeded();

        if (auxiliaryStandingHeight <= 0.0001f ||
            standingHeight <= 0.0001f)
        {
            return;
        }

        float heightRatio =
            Mathf.Clamp01(
                targetKCCHeight /
                standingHeight
            );

        float targetAuxiliaryHeight =
            Mathf.Max(
                auxiliaryRootCapsule.radius * 2f,
                auxiliaryStandingHeight *
                heightRatio
            );

        /*
         * 保持 Capsule 底部高度不變，
         * 只讓上半部往下縮短。
         *
         * 否則只改 height 但不改 center，
         * Capsule 會同時從腳底與頭頂兩邊收縮，
         * 產生腳底離地的 Trigger 範圍。
         */
        float standingBottom =
            auxiliaryStandingCenter.y -
            auxiliaryStandingHeight *
            0.5f;

        Vector3 targetCenter =
            auxiliaryStandingCenter;

        targetCenter.y =
            standingBottom +
            targetAuxiliaryHeight *
            0.5f;

        auxiliaryRootCapsule.height =
            targetAuxiliaryHeight;

        auxiliaryRootCapsule.center =
            targetCenter;
    }


    private void CacheCameraTarget()
    {
        if (movement == null)
        {
            return;
        }

        cameraTarget =
            movement.CamTarget;

        if (cameraTarget != null)
        {
            standingCameraLocalPosition =
                cameraTarget.localPosition;
        }
    }


    private void UpdateFirstPersonCamera()
    {
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasInputAuthority == false)
        {
            return;
        }

        if (cameraTarget == null)
        {
            CacheCameraTarget();
        }

        if (cameraTarget == null)
        {
            return;
        }

        Vector3 targetLocalPosition =
            standingCameraLocalPosition;

        if (IsCrouched)
        {
            targetLocalPosition.y -=
                Mathf.Max(
                    0f,
                    crouchCameraDrop
                );
        }

        Vector3 currentLocalPosition =
            cameraTarget.localPosition;

        currentLocalPosition.y =
            Mathf.SmoothDamp(
                currentLocalPosition.y,
                targetLocalPosition.y,
                ref cameraVerticalSmoothVelocity,
                Mathf.Max(
                    0.001f,
                    cameraTransitionSmoothTime
                ),
                Mathf.Infinity,
                Time.deltaTime
            );

        cameraTarget.localPosition =
            currentLocalPosition;
    }


    private void TryRegisterProcessor()
    {
        if (processorRegistered ||
            kcc == null)
        {
            return;
        }

        if (kcc.HasProcessor(
                this
            ))
        {
            processorRegistered =
                true;

            return;
        }

        processorRegistered =
            kcc.AddLocalProcessor(
                this
            );
    }


    private void UnregisterProcessor()
    {
        if (processorRegistered == false ||
            kcc == null)
        {
            return;
        }

        kcc.RemoveLocalProcessor(
            this
        );

        processorRegistered =
            false;
    }


    private Vector3 ResolveGroundNormal()
    {
        Vector3 groundNormal =
            kcc.Data.GroundNormal;

        if (groundNormal.sqrMagnitude <=
            0.0001f)
        {
            return Vector3.up;
        }

        return groundNormal.normalized;
    }


    private float ResolveBaseMovementSpeed()
    {
        if (kcc != null &&
            kcc.Data.KinematicSpeed > 0.0001f)
        {
            return kcc.Data.KinematicSpeed;
        }

        return 8f;
    }


    private Vector3 ClampToAbsoluteSafetySpeed(
        Vector3 velocity
    )
    {
        float maximumSpeed =
            Mathf.Max(
                0f,
                absoluteSafetyMaximumSpeed
            );

        if (maximumSpeed <= 0f ||
            velocity.sqrMagnitude <=
            maximumSpeed *
            maximumSpeed)
        {
            return velocity;
        }

        return velocity.normalized *
               maximumSpeed;
    }


    #endregion
}
