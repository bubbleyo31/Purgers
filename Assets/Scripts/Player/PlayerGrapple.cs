using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

/// <summary>
/// 勾索目前流程階段。
/// </summary>
public enum GrapplePhase : byte
{
    /// <summary>
    /// 勾索待機，可再次使用。
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 已鎖定目標，等待射出前搖。
    /// </summary>
    PreFire = 1,

    /// <summary>
    /// 繩索正在射向目標。
    /// </summary>
    Shooting = 2,

    /// <summary>
    /// 勾索已附著並正在拉動玩家。
    /// </summary>
    Attached = 3,

    /// <summary>
    /// 勾索正在反向收回。
    /// </summary>
    Retracting = 4
}

public enum GrappleLockedLateralIntent : byte
{
    /// <summary>
    /// 命中後沒有明顯水平轉頭，使用一般直線拉動。
    /// </summary>
    Center = 0,

    /// <summary>
    /// 命中後明顯向左轉頭，本次拉動固定向左側移。
    /// </summary>
    Left = 1,

    /// <summary>
    /// 命中後明顯向右轉頭，本次拉動固定向右側移。
    /// </summary>
    Right = 2
}

/// <summary>
/// 勾索被取消或結束的原因。
/// </summary>
public enum GrappleCancelReason : byte
{
    /// <summary>
    /// 玩家再次按下勾索鍵。
    /// </summary>
    ManualToggle = 0,

    /// <summary>
    /// 玩家進入 Grapple Release Distance。
    /// </summary>
    AutoReleaseDistance = 1,

    /// <summary>
    /// 玩家已經飛過最近點並開始遠離。
    /// </summary>
    AutoReleasePassedPoint = 2,

    /// <summary>
    /// 特殊能力取消。
    /// </summary>
    SpecialAbility = 3,

    /// <summary>
    /// 目標消失。
    /// </summary>
    TargetLost = 4,

    /// <summary>
    /// 目標瞬移。
    /// </summary>
    TargetTeleport = 5,

    /// <summary>
    /// 其他系統強制取消。
    /// </summary>
    Forced = 6,

    /// <summary>
    /// 因職業專屬 Grapple Interaction
    /// 主動結束一般勾索流程。
    ///
    /// 例如：
    ///
    /// Attack
    /// → 勾中敵人
    /// → 套用五秒標記
    /// → 立即收繩
    ///
    /// 這種結束：
    ///
    /// 不應產生 Manual Toggle Momentum
    /// 不應產生 Distance Release Momentum。
    /// </summary>
    ProfessionInteraction = 7,

    /// <summary>
    /// 玩家在普通 Attached 拉動期間按下 Aim，
    /// 主動釋放 Grapple 並準備進入 GrappleAirborne。
    /// </summary>
    AimRelease = 8,

    /// <summary>
    /// 普通 Attached Grapple 的命中點被實體障礙物
    /// 連續遮蔽達指定時間，因此自動釋放。
    /// </summary>
    AutoReleaseOccluded = 9,

    /// <summary>
    /// 普通地形鈎索已進入固定平面的 Left／Right 擺盪，
    /// 玩家相對命中點的幾何擺角已達自動釋放角度。
    ///
    /// 此判斷不使用玩家速度作為觸發條件。
    /// </summary>
    AutoReleaseSwingAngle = 10
}

/// <summary>
/// 玩家勾索核心。
///
/// 負責：
/// 1. Raycast。
/// 2. PreFire。
/// 3. Shooting。
/// 4. Attached。
/// 5. Retracting。
/// 6. 移動 GrappleAnchor。
/// 7. KCC 拉動。
/// 8. 自動脫鉤。
/// 9. 手動取消。
/// 10. 取消後 Momentum。
/// 11. 特殊能力取消接口。
///
/// 不負責：
/// 1. LineRenderer。
/// 2. Camera。
/// 3. ViewModel。
/// 4. FOV。
/// 5. 勾索充能冷卻。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KCC))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(PlayerGrappleCharges))]
[RequireComponent(typeof(PlayerGrappleInteractionController))]
[RequireComponent(typeof(PlayerProfession))]
public class PlayerGrapple : NetworkBehaviour
{
    // =====================================================================
    #region 引用

    [Header("核心引用")]

    [SerializeField]
    [Tooltip("玩家 Advanced KCC。若留空會自動取得。")]
    private KCC kcc;

    [SerializeField]
    [Tooltip("玩家移動模組。提供瞄準方向與水平觀看方向。若留空會自動取得。")]
    private PlayerMovement movement;

    [SerializeField]
    [Tooltip("玩家狀態機。勾索結束後用來通知 GrappleAirborne。若留空會自動取得。")]
    private PlayerStateMachine stateMachine;

    [SerializeField]
    [Tooltip("勾索充能模組。成功命中目標後會消耗一格充能。若留空會自動取得。")]
    private PlayerGrappleCharges charges;

    [SerializeField]
    [Tooltip("玩家職業勾索互動路由器。PlayerGrapple 命中 GrappleInteractionTarget 後會通知它，由它依照玩家職業判斷 Attack、Tank 或 Support 的特殊勾索技能。若留空會自動取得。")]
    private PlayerGrappleInteractionController grappleInteractionController;

    [SerializeField]
    [Tooltip("玩家目前職業資料。PlayerGrapple 會用它判斷是否為 Support，只有 Support 會額外開啟 Support Additional Grapple Mask，例如其他玩家的 Hitbox Layer。若留空會自動取得。")]
    private PlayerProfession profession;

    #endregion

    // =====================================================================
    #region 勾索射線

    [Header("勾索射線設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("沒有可用 CameraTarget 時，勾索射線與拉動計算使用的玩家高度。")]
    private float rayOriginHeight = 1.6f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("勾索最遠命中距離，同時作為射出與收回動畫的最大距離基準。")]
    private float grappleDistance = 50f;

    [SerializeField]
    [Tooltip("所有職業共用的勾索可命中 Layer。建議這裡仍然排除 Player Layer，避免 Attack 與 Tank 一般勾索誤抓其他玩家。Support 需要額外命中的 Player Layer 請設定在 Support Additional Grapple Mask。")]
    private LayerMask grappleMask = ~0;

    [SerializeField]
    [Tooltip("只有 Support 職業會額外加入的勾索 Layer。建議只放其他玩家可被勾索命中的 Fusion Hitbox 或 Player Hitbox Layer。Support 實際射線 Mask 會是 Grapple Mask 與此 Mask 的聯集；Attack 與 Tank 完全不受這個設定影響。")]
    private LayerMask supportAdditionalGrappleMask = 0;

    #endregion

    // =====================================================================
    #region 射出設定

    [Header("勾索射出設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("勾索成功鎖定後，在繩索真正射出前等待的秒數。")]
    private float grapplePreFireDelay = 0.06f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("當目標位於 Grapple Distance 最遠距離時，繩索抵達目標需要的秒數。較近目標依距離比例縮短。")]
    private float ropeShootTimeAtMaxDistance = 0.25f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("近距離繩索射出動畫允許的最短秒數。")]
    private float ropeShootMinimumDuration = 0.03f;

    [SerializeField]
    [Tooltip("繩索射出進度曲線。雖然 LineRenderer 已拆到 PlayerGrappleVisual，但這條曲線屬於勾索時序資料，並用來計算中途取消時目前繩索實際伸出比例。")]
    private AnimationCurve ropeShootEase =
        AnimationCurve.Linear(
            0f,
            0f,
            1f,
            1f
        );

    #endregion

    // =====================================================================
    #region 收回設定

    [Header("勾索收回設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("當目前可見繩索長度等於 Grapple Distance 時，完全收回所需秒數。")]
    private float ropeRetractTimeAtMaxDistance = 0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("近距離收繩動畫允許的最短秒數。")]
    private float ropeRetractMinimumDuration = 0.03f;

    [SerializeField]
    [Tooltip("繩索收回動畫進度曲線。")]
    private AnimationCurve ropeRetractEase =
        AnimationCurve.EaseInOut(
            0f,
            0f,
            1f,
            1f
        );

    #endregion

    // =====================================================================
    #region 拉動設定

    [Header("勾索拉動設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("每秒增加多少沿勾索方向的速度。依目前測試可以設定為 150。")]
    private float grappleAcceleration = 150f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("沿勾索方向的最大拉動速度。依目前測試可以設定為 150。")]
    private float grappleMaxSpeed = 150f;
    
    // =====================================================================
    // Grapple Fixed-Plane Swing
    // =====================================================================

    [Header("鈎索固定平面擺盪")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Left／Right 擺盪期間，繩索每秒把玩家的向內速度推向目標值的加速度，" +
        "單位為世界單位/秒平方。\n\n" +
        "這不是 Center 直線拉動的 Grapple Acceleration；" +
        "只影響已鎖定左擺或右擺的弧形路徑。\n" +
        "數值過高會再次變成被吸向命中點；過低則會像完全沒有繩索張力。" +
        "建議第一輪使用 45。")]
    private float grappleSwingInwardAcceleration = 45f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Left／Right 擺盪期間允許的最高向內速度，" +
        "單位為世界單位/秒。\n\n" +
        "這個上限用來避免玩家在形成弧線以前就被快速拉回命中點。" +
        "它應明顯低於 Center 使用的 Grapple Max Speed。" +
        "建議第一輪使用 12。")]
    private float grappleSwingMaximumInwardSpeed = 12f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Left／Right 擺盪期間，玩家速度追向固定擺盪切線方向的加速度，" +
        "單位為世界單位/秒平方。\n\n" +
        "方向由 Attached 時建立的擺盪平面決定，之後不會跟著玩家目前視角反轉。" +
        "建議第一輪使用 90。")]
    private float grappleSwingTangentialAcceleration = 90f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Left／Right 擺盪期間，由鈎索系統主動維持的最高切線速度，" +
        "單位為世界單位/秒。\n\n" +
        "這只負責形成擺盪弧線，不是斷繩判斷條件。" +
        "斷繩完全由幾何擺角決定。建議第一輪使用 30。")]
    private float grappleSwingMaximumTangentialSpeed = 30f;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip(
        "Left／Right 擺盪從 Attached 初始繩索方向開始，" +
        "繞固定擺盪軸旋轉多少度後自動斷繩。\n\n" +
        "這是幾何角度，不讀取拉動速度或側移速度。" +
        "85 度會在接近四分之一圓弧時釋放；" +
        "數值越低越早甩出，越高則繞命中點更久。" +
        "建議第一輪使用 85。")]
    private float grappleSwingReleaseAngle = 85f;

    [SerializeField]
    [Min(0.05f)]
    [Tooltip(
        "Left／Right 擺盪時，玩家中心與命中點小於此距離便進行安全釋放，" +
        "避免距離接近零時繩索方向與旋轉軸失去穩定性。\n\n" +
        "這只是數學退化保護，不是主要甩出判斷。" +
        "建議第一輪使用 1。")]
    private float grappleSwingEmergencyReleaseDistance = 1f;

    // =====================================================================
    // Grapple Movement Mode Availability
    // =====================================================================

    [Header("鈎索拉動模式開關")]

    [SerializeField]
    [Tooltip(
        "是否允許普通地形鈎索使用 Center 直線拉動。\n\n" +
        "開啟：命中後沒有明顯左右轉頭時，可以正常直線拉向命中點。\n" +
        "關閉：Center 判定會改選目前仍被允許的左甩或右甩。\n\n" +
        "如果三個模式只開啟其中一個，玩家所有普通地形鈎索都會被強制使用該模式。")]
    private bool enableGrappleStraightPull = true;

    [SerializeField]
    [Tooltip(
        "是否允許普通地形鈎索使用 Left 固定平面左甩。\n\n" +
        "開啟：命中後明顯向左轉頭時，可以建立左側擺盪平面。\n" +
        "關閉：Left 判定會優先退回 Center；若 Center 也被關閉，便改用 Right。")]
    private bool enableGrappleLeftSwing = true;

    [SerializeField]
    [Tooltip(
        "是否允許普通地形鈎索使用 Right 固定平面右甩。\n\n" +
        "開啟：命中後明顯向右轉頭時，可以建立右側擺盪平面。\n" +
        "關閉：Right 判定會優先退回 Center；若 Center 也被關閉，便改用 Left。\n\n" +
        "例如只開啟此欄位、關閉 Straight 與 Left，" +
        "玩家不論是否轉頭或往哪側轉頭，最後都會使用 Right。")]
    private bool enableGrappleRightSwing = true;

    // =====================================================================
    // 鈎索命中後轉頭側移判斷
    // =====================================================================

    [Header("鈎索命中後轉頭側移判斷")]
    [SerializeField]
    [Range(0f, 180f)]
    [Tooltip(
        "鈎索命中成功後，到真正開始拉動玩家前，玩家至少需要水平轉動多少度，" +
        "才會鎖定左側移或右側移。\n\n" +
        "視角往右超過此角度：整段拉動固定右側移。\n" +
        "視角往左超過此角度：整段拉動固定左側移。\n" +
        "未超過門檻：一般直線拉動，不施加側移。\n\n" +
        "建議先使用 15 度。若很難在拉動前觸發，可降到 10 度；" +
        "若輕微手部晃動常誤觸，可提高到 20～25 度。")]
    private float grapplePostHitYawIntentThreshold = 15f;

    // =====================================================================
    // Grapple Point Occlusion Release
    // =====================================================================

    [Header("勾索命中點遮蔽自動釋放")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("普通 Attached Grapple 的命中點被實體障礙物連續遮蔽多久後自動釋放，單位為秒。預設 0.1 秒可容忍單一 Tick、牆角擦過與短暫誤判；設為 0 代表第一次確認遮蔽就立即釋放。")]
    private float grappleOcclusionReleaseDelay =
        0.1f;

    [SerializeField]
    [Tooltip("判斷玩家是否還看得到 Grapple 命中點時，哪些 Layer 會被視為遮蔽物。只應包含場景實體障礙物，例如 Ground、Wall、Environment；務必排除 Player、Enemy、Grapple Target、Trigger 與純視覺 Layer，避免命中目標本身或玩家自己造成誤斷繩。")]
    private LayerMask grappleOcclusionObstacleMask;

    [SerializeField]
    [Min(0f)]
    [Tooltip("遮蔽射線在抵達命中點前預留的安全距離，單位為世界單位。射線只檢查到『命中點距離減去此值』，避免命中點所在牆面自己的 Collider 被當成遮蔽物。第一輪建議 0.05。")]
    private float grappleOcclusionTargetSkin =
        0.05f;

    [Header("釋放判定")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("距離勾索點小於此距離後自動結束 Attached 並開始收繩。")]
    private float grappleReleaseDistance = 2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家朝勾索點的相對速度超過此值後，才開始啟用自動脫鉤判斷。")]
    private float grappleApproachSpeedThreshold = 0.5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家曾經朝目標接近後，開始以此速度遠離目標時，判定為已飛過最近點。")]
    private float grappleReleaseAwaySpeed = 0.1f;

    #endregion

    // =====================================================================
    #region 取消後 Momentum

    [Header("勾索釋放 Momentum")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("取消前多少秒內的高速可被視為近期最高速度。")]
    private float releasePeakSampleWindow = 0.12f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("擷取到的勾索速度總倍率。1 代表完整保留。")]
    private float releaseSpeedMultiplier = 1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("釋放後允許保留的最高水平速度。0 代表不限制。依目前 Grapple Max Speed 為 150，可以先設定 150。")]
    private float releaseMaximumSpeed = 150f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("GrappleAirborne Momentum 的重力衰退倍率。實際每秒水平減速量 = 目前 KCC Gravity 的向量長度 × 此倍率。依目前 Environment Processor Gravity Y = -40，倍率 1 代表每秒降低 40 速度；倍率 0.5 代表每秒降低 20；0 代表完全不依重力衰退。第一輪建議 1。")]
    private float releaseGravityDecayMultiplier =
        1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("無法從 KCC Data 取得有效 Kinematic Speed 時使用的普通移動速度備援值。依目前 Environment Processor Kinematic Speed = 20，因此第一輪保持 20。這不是 Grapple 最大速度。")]
    private float releaseBaseMovementSpeedFallback =
        20f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Momentum 水平速度低於此值後停止控制。")]
    private float releaseStopSpeed = 0.1f;

    [SerializeField]
    [Tooltip("開啟後，玩家在 Momentum 期間成功跳躍或二段跳後會停止持續 Momentum 控制，但保留當下速度。")]
    private bool cancelReleaseMomentumOnJump = true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Momentum 前方障礙物 SphereCast 半徑。")]
    private float releaseObstacleCheckRadius = 0.25f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Momentum 前方障礙物偵測額外安全距離。")]
    private float releaseObstacleCheckSkin = 0.1f;

    [SerializeField]
    [Tooltip("Momentum 前方哪些 Layer 被視為障礙物。務必排除 Player。")]
    private LayerMask releaseObstacleMask = ~0;

    #endregion

    // =====================================================================
    #region 距離自動脫鉤 Momentum

    [Header("抵達勾索點後的自動推進")]

    [SerializeField]
    [Tooltip("開啟後，因進入 Grapple Release Distance 而自動收繩時，也啟動釋放 Momentum。")]
    private bool enableMomentumOnDistanceAutoRelease = true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("距離自動脫鉤使用的額外速度倍率。1 代表與主動取消相同。")]
    private float distanceAutoReleaseMomentumMultiplier = 1f;

    [SerializeField]
    [Tooltip("開啟時，自動距離脫鉤會把速度導向玩家水平觀看方向。關閉時會優先沿玩家原本水平移動方向繼續飛行。")]
    private bool distanceAutoReleaseUseLookDirection = false;

    #endregion

    // =====================================================================
    #region 移動錨點

    [Header("移動錨點設定")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("移動錨點單 Tick 位移超過此距離後視為瞬移，停止追蹤並開始收繩。")]
    private float movingAnchorTeleportBreakDistance = 5f;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後顯示勾索命中、收繩等資訊。")]
    private bool debugGrapple = true;

    [SerializeField]
    [Tooltip("開啟後顯示 Momentum 啟動、碰撞與停止資訊。")]
    private bool debugMomentum = true;

    #endregion

    // =====================================================================
    #region Fusion 勾索狀態

    [Networked]
    public GrapplePhase CurrentPhase { get; private set; }

    [Networked]
    private TickTimer PhaseTimer { get; set; }

    [Networked]
    private float CurrentShootDuration { get; set; }

    [Networked]
    private float CurrentRetractDuration { get; set; }

    [Networked]
    private float ExtensionAtRetractStart { get; set; }

    [Networked]
    private NetworkBool HasApproachedPoint { get; set; }

    /// <summary>
    /// 本次鈎索確認命中成功時，玩家的水平視角角度。
    ///
    /// 等到 BeginAttached() 真正準備施加拉動速度時，
    /// 會用目前 LookYaw 與這個角度比較，
    /// 判斷玩家在命中後究竟往左看、往右看，或沒有明顯轉頭。
    /// </summary>
    [Networked]
    private float GrappleHitLookYaw { get; set; }

    /// <summary>
    /// 本次普通 Grapple 在正式 Attached 前
    /// 已經鎖定的水平側移意圖。
    ///
    /// 只會在 BeginAttached() 的普通玩家拉動流程寫入一次，
    /// Attached 期間只能讀取，不會重新判斷。
    /// </summary>
    [Networked]
    private GrappleLockedLateralIntent
        LockedGrappleLateralIntent
    {
        get;
        set;
    }

    /// <summary>
    /// 本次普通地形鈎索是否已經建立固定平面的 Left／Right 擺盪。
    ///
    /// Center 直線拉動、Attack Interaction 與 Support Tether 都會保持 false。
    /// </summary>
    [Networked]
    private NetworkBool GrappleSwingActive { get; set; }

    /// <summary>
    /// Attached 當下，從鈎索命中點指向玩家拉動參考位置的單位方向。
    ///
    /// 後續會把目前方向與此方向比較，取得本次擺過的幾何角度。
    /// </summary>
    [Networked]
    private Vector3 GrappleSwingInitialRadialDirection { get; set; }

    /// <summary>
    /// 本次 Left／Right 擺盪的固定世界空間旋轉軸。
    ///
    /// 它在 BeginAttached() 建立一次，直到鈎索釋放前都不重新依視角計算，
    /// 因此玩家後續轉頭不會讓已鎖定的右擺突然變成左擺。
    /// </summary>
    [Networked]
    private Vector3 GrappleSwingAxis { get; set; }

    /// <summary>
    /// Grapple 命中點開始被遮蔽後的寬限 Timer。
    ///
    /// 視線重新暢通時會立即清回 None。
    /// </summary>
    [Networked]
    private TickTimer GrappleOcclusionTimer
    {
        get;
        set;
    }

    /// <summary>
    /// 命中點目前是否正在累積連續遮蔽時間。
    ///
    /// 使用獨立 NetworkBool，不依賴不同 Fusion 版本
    /// 對 TickTimer None／IsRunning 的 API 差異。
    /// </summary>
    [Networked]
    private NetworkBool GrappleOcclusionPending
    {
        get;
        set;
    }

    [Networked]
    private NetworkBool TrackingDynamicAnchor { get; set; }

    [Networked]
    private Vector3 GrappleWorldPoint { get; set; }

    [Networked]
    private GrappleAnchor TrackedAnchor { get; set; }

    [Networked]
    private Vector3 GrappleAnchorLocalPoint { get; set; }

    [Networked]
    private Vector3 PreviousGrapplePoint { get; set; }

    [Networked]
    private Vector3 GrapplePointVelocity { get; set; }

    /// <summary>
    /// 這次 Grapple Raycast 真正命中的 NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// Support 特殊 Grapple 需要知道：
    ///
    /// 「繩索現在是連在哪一個會移動的 NetworkObject 上」。
    ///
    /// ------------------------------------------------------------
    ///
    /// 一般 Grapple 仍然優先使用原本的 GrappleAnchor。
    /// 這個引用主要提供 Support Interaction Tether 使用。
    /// </summary>
    [Networked]
    private NetworkObject GrappleHitNetworkObject
    {
        get;
        set;
    }


    /// <summary>
    /// 原始 Hit Point
    /// 相對於 GrappleHitNetworkObject Root 的 Local Position。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這樣 Enemy / Player 被拉動時，
    /// 繩索終點可以持續跟著目標移動，
    /// 而不是永遠停在第一次命中的世界座標。
    /// </summary>
    [Networked]
    private Vector3 GrappleHitObjectLocalPoint
    {
        get;
        set;
    }


    /// <summary>
    /// 目前是否正在執行 Support 專屬 Tether。
    ///
    /// ------------------------------------------------------------
    ///
    /// true 時：
    ///
    /// CurrentPhase 仍然維持 Attached
    ///
    /// 但是：
    ///
    /// PlayerGrapple 不會執行 PullTowardPoint()。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這可以讓：
    ///
    /// Gameplay 玩家拉動 ×
    /// LineRenderer Attached ✓
    /// Target Tracking ✓
    ///
    /// 同時成立。
    /// </summary>
    [Networked]
    private NetworkBool SupportInteractionTetherActive
    {
        get;
        set;
    }

    /// <summary>
    /// Aim 是否已被「Attached 期間斷繩」消耗。
    ///
    /// true 時，Player 仍會保留 Look 與其他 Input，
    /// 但不會把 Aim 交給目前 Profession Runtime。
    ///
    /// 玩家必須真正放開 Aim，這個鎖才會解除。
    /// 下一次重新按下 Aim 才能啟動職業能力。
    /// </summary>
    [Networked]
    private NetworkBool AimConsumedUntilReleased
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region Fusion Momentum 狀態

    [Networked]
    private float RecentPeakHorizontalSpeed { get; set; }

    [Networked]
    private TickTimer RecentPeakTimer { get; set; }

    [Networked]
    private NetworkBool ReleaseMomentumActive { get; set; }

    [Networked]
    private Vector3 ReleaseMomentumDirection { get; set; }

    [Networked]
    private float ReleaseMomentumSpeed { get; set; }

    /// <summary>
    /// 本次 Release Momentum 應該衰退回去的普通 KCC 移動速度。
    ///
    /// 在斷繩瞬間從 KCC Data KinematicSpeed 保存，
    /// 避免衰退期間因為其他系統改動速度基準而跳動。
    /// </summary>
    [Networked]
    private float ReleaseMomentumBaseSpeed
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region 公開狀態

    /// <summary>
    /// PreFire、Shooting、Attached 都屬於勾索控制期間。
    /// Retracting 不算。
    /// </summary>
    public bool IsGrappleControlActive =>
        CurrentPhase == GrapplePhase.PreFire ||
        CurrentPhase == GrapplePhase.Shooting ||
        CurrentPhase == GrapplePhase.Attached;

    /// <summary>
    /// 是否正式 Attached。
    /// </summary>
    public bool IsGrapplePulling =>
        CurrentPhase ==
        GrapplePhase.Attached;

    /// <summary>
    /// 是否為真正拉動玩家本人的普通 Attached Grapple。
    ///
    /// Support Tether 雖然也維持 Attached，
    /// 但只連接並拉動目標，不拉動 Support 玩家本人，
    /// 所以不能封鎖 Support 的普通 WASD。
    /// </summary>
    public bool IsNormalPlayerPullAttached =>
        CurrentPhase ==
            GrapplePhase.Attached &&
        SupportInteractionTetherActive ==
            false;

    /// <summary>
    /// 是否正在執行勾索釋放 Momentum。
    /// </summary>
    public bool IsReleaseMomentumActive =>
        ReleaseMomentumActive;

    /// <summary>
    /// PlayerMovement 的普通 Kinematic WASD 保留倍率。
    ///
    /// Release Momentum Active 時回傳 0，
    /// 是為了避免普通 KCC 速度與 Momentum 速度直接相加。
    ///
    /// 玩家原始 WASD 仍會另外傳進 PlayerGrapple，
    /// 用來優先決定 GrappleAirborne 的高速移動方向。
    /// </summary>
    public float MovementInputInfluence
    {
        get
        {
            if (ReleaseMomentumActive)
            {
                return 0f;
            }

            /*
            * 普通 Attached Grapple 的方向控制
            * 仍由 PlayerGrapple 自己處理 A/D。
            */
            if (IsNormalPlayerPullAttached)
            {
                return 0f;
            }

            return 1f;
        }
    }

    /// <summary>
    /// 勾索是否可以重新使用。
    /// </summary>
    public bool CanUseGrapple =>
        CurrentPhase ==
            GrapplePhase.Idle &&
        charges != null &&
        charges.HasCharge;

    /// <summary>
    /// 目前真正的勾索世界錨點。
    /// </summary>
    public Vector3 CurrentGrapplePoint =>
        GetCurrentGrapplePoint();

    /// <summary>
    /// 目前視覺繩索伸出比例。
    ///
    /// 0 = 完全收起。
    /// 1 = 完整連接目標。
    /// </summary>
    public float CurrentRopeExtension =>
        GetCurrentRopeExtension();

    /// <summary>
    /// Support 是否正在維持
    /// 「不拉玩家、只連著目標」的特殊 Tether。
    ///
    /// 下一步 SupportGrapplePullAbility
    /// 會使用這個狀態判斷特殊拉取是否仍然有效。
    /// </summary>
    public bool IsSupportInteractionTetherActive =>
        SupportInteractionTetherActive;


    /// <summary>
    /// Support Tether 目前連接的 NetworkObject。
    ///
    /// 如果目前不是 Support Tether，
    /// 回傳 null。
    /// </summary>
    public NetworkObject SupportTetherTargetObject =>
        SupportInteractionTetherActive
            ? GrappleHitNetworkObject
            : null;


    /// <summary>
    /// Support Tether 現在真正的世界連接點。
    /// </summary>
    public Vector3 CurrentSupportTetherPoint =>
        GetCurrentGrapplePoint();
    
    /// <summary>
    /// Player Core 是否應暫時阻止 Aim 傳給 Profession Runtime。
    ///
    /// 只阻止職業 Aim Gameplay；
    /// 不會停止滑鼠 Look，也不會改寫原始 NetInput 歷史。
    /// </summary>
    public bool BlocksProfessionAimInput =>
        AimConsumedUntilReleased;

    #endregion

    // =====================================================================
    #region Grapple Aim Query

    /// <summary>
    /// 檢查玩家目前準心是否命中一個真正合法的勾索點。
    ///
    /// ====================================================================
    ///
    /// 這是一個純查詢：
    ///
    /// 1. 不消耗 Grapple Charge。
    /// 2. 不改變 Grapple Phase。
    /// 3. 不建立 Rope。
    /// 4. 不通知職業 Grapple Interaction。
    /// 5. 不改變玩家速度。
    ///
    /// ====================================================================
    ///
    /// 實際出鈎與本地 HUD 都必須使用這個查詢，
    /// 確保兩者共用：
    ///
    /// Gameplay Aim Direction
    /// Runner PhysicsScene
    /// Grapple Distance
    /// 目前職業 Grapple Mask
    /// 不可命中自己規則。
    /// </summary>
    /// <param name="hit">
    /// 查詢成功時，回傳真正命中的 RaycastHit。
    /// 查詢失敗時為 default。
    /// </param>
    /// <returns>
    /// true：目前準心命中合法勾索點。
    /// false：沒有命中、距離超過、Layer 不合法或命中自己。
    /// </returns>
    public bool TryGetValidGrappleAimHit(
        out RaycastHit hit
    )
    {
        return TryGetValidGrappleAimHit(
            out hit,
            out _,
            out _
        );
    }

    /// <summary>
    /// 共用查詢的內部版本。
    ///
    /// 除了 Hit，還回傳本次實際使用的射線起點與方向，
    /// 讓 TryStartGrapple 可以繼續畫出原本的 Debug Ray。
    /// </summary>
    private bool TryGetValidGrappleAimHit(
        out RaycastHit hit,
        out Vector3 rayOrigin,
        out Vector3 aimDirection
    )
    {
        hit =
            default;

        rayOrigin =
            default;

        aimDirection =
            default;

        /*
        * HUD 可能正處於死亡 Despawn 與重生的交界 Frame。
        * 這裡必須完整防守引用，不能假設 Player 永遠已 Spawned。
        */
        if (movement == null ||
            kcc == null ||
            Runner == null ||
            Runner.IsRunning == false ||
            Object == null ||
            Object.IsValid == false)
        {
            return false;
        }

        aimDirection =
            movement.GetAimDirection();

        if (aimDirection.sqrMagnitude <=
            0.000001f)
        {
            return false;
        }

        aimDirection.Normalize();

        /*
        * 完全沿用原本 TryStartGrapple 的射線起點規則。
        *
        * 有 Camera Target：
        * 從 Camera Target 往 Aim Direction 前移 0.1，
        * 降低射線從自身 Collider 裡開始的機率。
        *
        * 沒有 Camera Target：
        * 使用 KCC Target Position 加固定眼睛高度。
        */
        if (movement.CamTarget != null)
        {
            rayOrigin =
                movement.CamTarget.position +
                aimDirection *
                0.1f;
        }
        else
        {
            rayOrigin =
                kcc.Data.TargetPosition +
                Vector3.up *
                rayOriginHeight;
        }

        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() == false)
        {
            return false;
        }

        int activeGrappleMask =
            GetActiveGrappleMask();

        bool hasHit =
            physicsScene.Raycast(
                rayOrigin,
                aimDirection,
                out hit,
                Mathf.Max(
                    0.1f,
                    grappleDistance
                ),
                activeGrappleMask,
                QueryTriggerInteraction.Ignore
            );

        if (hasHit == false ||
            hit.collider == null)
        {
            hit =
                default;

            return false;
        }

        NetworkObject hitNetworkObject =
            hit.collider
                .GetComponentInParent<NetworkObject>();

        /*
        * 場景地形通常沒有 NetworkObject，仍然是合法目標。
        * 只有命中的 NetworkObject 正好是玩家自己時才拒絕。
        */
        if (hitNetworkObject == Object)
        {
            hit =
                default;

            return false;
        }

        return true;
    }

    /// <summary>
    /// 取得目前職業真正使用的 Grapple LayerMask。
    ///
    /// 一般職業只使用 Grapple Mask；
    /// Support 額外加入 Support Additional Grapple Mask。
    /// </summary>
    private int GetActiveGrappleMask()
    {
        int activeGrappleMask =
            grappleMask.value;

        if (profession != null &&
            profession.CurrentProfession ==
                PlayerProfessionType.Support)
        {
            activeGrappleMask |=
                supportAdditionalGrappleMask.value;
        }

        return activeGrappleMask;
    }

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
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

        if (charges == null)
        {
            charges =
                GetComponent<PlayerGrappleCharges>();
        }

        if (grappleInteractionController == null)
        {
            grappleInteractionController =
                GetComponent<PlayerGrappleInteractionController>();
        }

        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }
    }

    #endregion

    // =====================================================================
    #region 模擬入口

    /// <summary>
    /// 每個 Fusion Tick 由 Player 根控制器呼叫。
    /// </summary>
    /// <param name="moveInput">
    /// 玩家本 Tick 的完整二維移動輸入。
    /// X 是 A/D，Y 是 W/S。
    ///
    /// Attached 時只使用 X；
    /// Release Momentum 時會使用完整 X/Y 接管高速方向。
    /// </param>
    /// <param name="activeMovementInfluence">
    /// Grapple 以外的系統是否允許玩家主動移動。
    ///
    /// 例如 Support 空中特殊能力或被 Support 拉取時會是 0。
    /// 這時 Momentum 暫停衰退與方向更新，避免繞過外部移動封鎖。
    /// </param>
    public void Simulate(
        bool grapplePressed,
        bool aimPressed,
        bool aimHeld,
        Vector2 moveInput,
        float activeMovementInfluence
    )
    {
        // =============================================================
        // 1. 玩家真正放開 Aim 後解除消耗鎖
        // =============================================================

        if (AimConsumedUntilReleased &&
            aimHeld == false)
        {
            AimConsumedUntilReleased =
                false;
        }

        // =============================================================
        // 2. 普通 Attached Grapple：Aim 主動斷繩
        // =============================================================

        bool canReleaseWithAim =
            aimPressed &&
            CurrentPhase ==
                GrapplePhase.Attached &&
            SupportInteractionTetherActive ==
                false;

        if (canReleaseWithAim)
        {
            /*
             * 必須先建立 Aim 消耗鎖，再改變 Grapple Phase。
             * 後面的 Profession Runtime 會在同一 Tick 讀取這個狀態，
             * 防止同一顆 Aim 同時斷繩並啟動職業技能。
             */
            AimConsumedUntilReleased =
                true;

            BeginRetract(
                GrappleCancelReason.AimRelease
            );
        }
        else if (grapplePressed)
        {
            /*
             * Aim Release 優先於 Q Toggle。
             * 如果同一 Tick 同時按 Q 與 Aim，只執行 Aim Release，
             * 不重複呼叫兩次 BeginRetract()。
             */
            HandleToggle();
        }

        // =============================================================
        // 3. Grapple Phase
        // =============================================================

        if (CurrentPhase !=
            GrapplePhase.Idle)
        {
            /*
            * Attached 的側移方向已在 BeginAttached() 鎖定，
            * 不再把玩家目前 A/D 傳入 Grapple Phase。
            */
            TickGrapplePhase();
        }

        // =============================================================
        // 4. Release Momentum
        // =============================================================

        if (ReleaseMomentumActive)
        {
            TickReleaseMomentum(
                moveInput,
                activeMovementInfluence
            );
        }
    }

    /// <summary>
    /// PlayerMovement 成功執行跳躍後通知勾索模組。
    /// </summary>
    public void NotifyPlayerJumped()
    {
        if (cancelReleaseMomentumOnJump == false ||
            ReleaseMomentumActive == false)
        {
            return;
        }

        /*
        * GrappleAirborne 期間使用二段跳後，
        * Gameplay 狀態仍然是 GrappleAirborne。
        *
        * 因此這次跳躍只改變垂直速度，
        * 不應取消仍在衰退中的水平 Momentum。
        */
        if (stateMachine != null &&
            stateMachine.CurrentState ==
                PlayerMovementState.GrappleAirborne)
        {
            return;
        }

        StopReleaseMomentum(
            false
        );
    }

    #endregion

    // =====================================================================
    #region 切換式勾索

    private void HandleToggle()
    {
        switch (CurrentPhase)
        {
            case GrapplePhase.Idle:
            {
                TryStartGrapple();
                break;
            }

            case GrapplePhase.PreFire:
            case GrapplePhase.Shooting:
            case GrapplePhase.Attached:
            {
                BeginRetract(
                    GrappleCancelReason.ManualToggle
                );

                break;
            }

            case GrapplePhase.Retracting:
            {
                /*
                 * 收繩完成前忽略輸入。
                 */
                break;
            }
        }
    }

    #endregion

    // =====================================================================
    #region 發射勾索

    private void TryStartGrapple()
    {
        if (CurrentPhase !=
            GrapplePhase.Idle)
        {
            return;
        }

        if (charges == null ||
            charges.HasCharge == false)
        {
            return;
        }

        /*
         * 新的一次勾索開始時，
         * 停止舊 Momentum 的持續控制，
         * 但保留當下速度。
         */
        if (ReleaseMomentumActive)
        {
            StopReleaseMomentum(
                false
            );
        }

        /*
         * 實際出鈎與 HUD 共用完全相同的合法命中查詢。
         *
         * 最大距離、職業 Mask、射線起點與不可命中自己，
         * 不再由兩個系統各自維護。
         */
        bool hasValidHit =
            TryGetValidGrappleAimHit(
                out RaycastHit hit,
                out Vector3 rayOrigin,
                out Vector3 aimDirection
            );

        if (debugGrapple)
        {
            Debug.DrawRay(
                rayOrigin,
                aimDirection *
                grappleDistance,
                hasValidHit
                    ? Color.green
                    : Color.red,
                2f
            );
        }

        if (hasValidHit == false)
        {
            if (debugGrapple)
            {
                Debug.LogWarning(
                    "[勾索] 準心沒有命中合法勾索點，或目標超出最大距離。",
                    this
                );
            }

            return;
        }

        NetworkObject hitNetworkObject =
            hit.collider
                .GetComponentInParent<NetworkObject>();

        // =============================================================
        // 先確認這次 Grapple 正式成立
        // =============================================================

        /*
        * Raycast 已經確認命中有效目標。
        *
        * 現在先正式消耗 Grapple Charge。
        *
        * 如果因為任何原因消耗失敗，
        * 本次 Grapple 就不會建立：
        *
        * Pending Interaction
        * Grapple Point
        * Rope Shooting。
        */
        if (charges.ConsumeCharge() == false)
        {
            return;
        }

        // 只有鈎索命中有效目標，而且成功消耗使用次數後，
        // 才把此刻的水平視角當成本次判斷基準。
        //
        // 不在按鍵剛按下時記錄，避免射線尚未確認命中，
        // 或命中被規則拒絕時，留下無效的視角狀態。
        GrappleHitLookYaw = kcc != null ? kcc.Data.LookYaw : 0f;

        // 新的一次鈎索命中尚未進入 Attached，
        // 所以先維持 Center；真正開始拉動前才只判斷一次。
        LockedGrappleLateralIntent = GrappleLockedLateralIntent.Center;

        // 新的一次命中尚未進入 Attached，先清除上一輪擺盪資料。
        GrappleSwingActive = false;
        GrappleSwingInitialRadialDirection = Vector3.zero;
        GrappleSwingAxis = Vector3.zero;

        // =============================================================
        // 保存本次 Grapple 真正命中的 NetworkObject
        // =============================================================

        /*
        * Support 如果最後 Commit 成：
        *
        * SupportEnemyPullCandidate
        * 或
        * SupportPlayerPullCandidate
        *
        * 就會使用這份資料讓 Rope End
        * 持續跟著目標移動。
        */
        GrappleHitNetworkObject =
            hitNetworkObject;


        if (GrappleHitNetworkObject != null)
        {
            GrappleHitObjectLocalPoint =
                GrappleHitNetworkObject
                    .transform
                    .InverseTransformPoint(
                        hit.point
                    );
        }
        else
        {
            GrappleHitObjectLocalPoint =
                default;
        }


        /*
        * 新的一次 Grapple 開始時
        * 一定先清除上一個 Support Tether 狀態。
        *
        * 是否真正啟用，
        * 要等 BeginAttached()
        * Commit Profession Interaction 後才能決定。
        */
        SupportInteractionTetherActive =
            false;

        // =============================================================
        // 職業 Grapple Interaction Target
        // =============================================================

        /*
        * Charge 已正式消耗，
        * 代表這次 Grapple 一定會開始。
        *
        * 現在才建立 Pending Interaction。
        */
        GrappleInteractionTarget interactionTarget =
            hit.collider
                .GetComponentInParent<GrappleInteractionTarget>();

        if (grappleInteractionController != null &&
            interactionTarget != null)
        {
            grappleInteractionController.NotifyGrappleHit(
                interactionTarget,
                hit.collider,
                hit.point,
                hit.normal,
                hitNetworkObject
            );
        }

        GrappleWorldPoint =
            hit.point;

        PreviousGrapplePoint =
            hit.point;

        GrapplePointVelocity =
            Vector3.zero;

        TrackedAnchor =
            hit.collider
                .GetComponentInParent<GrappleAnchor>();

        if (TrackedAnchor != null)
        {
            Transform anchorSpace =
                TrackedAnchor.AnchorSpace;

            GrappleAnchorLocalPoint =
                anchorSpace.InverseTransformPoint(
                    hit.point
                );

            TrackingDynamicAnchor =
                true;
        }
        else
        {
            TrackingDynamicAnchor =
                false;

            GrappleAnchorLocalPoint =
                default;
        }

        HasApproachedPoint =
            false;

        CurrentShootDuration =
            0f;

        CurrentRetractDuration =
            0f;

        ExtensionAtRetractStart =
            0f;

        ResetRecentSpeedPeak();

        if (grapplePreFireDelay > 0f)
        {
            CurrentPhase =
                GrapplePhase.PreFire;

            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    grapplePreFireDelay
                );
        }
        else
        {
            BeginShooting();
        }

        if (debugGrapple)
        {
            Debug.Log(
                $"[勾索] 鎖定成功。" +
                $"\n物件：{hit.collider.name}" +
                $"\n距離：{hit.distance:F2}" +
                $"\n剩餘充能：{charges.CurrentCharges}/{charges.MaxCharges}",
                hit.collider
            );
        }
    }

    #endregion

    // =====================================================================
    #region 錨點

    private Vector3 GetCurrentGrapplePoint()
    {
        // =============================================================
        // 1. Support Interaction Tether
        // =============================================================

        /*
        * Support 拉 Enemy / Player 時，
        * 目標本身會移動。
        *
        * 所以 Rope End
        * 不可以繼續使用第一次命中的 GrappleWorldPoint。
        *
        * ------------------------------------------------------------
        *
        * 而是每次都重新：
        *
        * Target NetworkObject Transform
        * ×
        * 原始 Local Hit Point。
        */
        if (SupportInteractionTetherActive &&
            GrappleHitNetworkObject != null &&
            GrappleHitNetworkObject.IsValid)
        {
            return GrappleHitNetworkObject
                .transform
                .TransformPoint(
                    GrappleHitObjectLocalPoint
                );
        }


        // =============================================================
        // 2. 原本 GrappleAnchor
        // =============================================================

        if (TrackingDynamicAnchor &&
            TrackedAnchor != null)
        {
            Transform anchorSpace =
                TrackedAnchor.AnchorSpace;


            if (anchorSpace != null)
            {
                return anchorSpace.TransformPoint(
                    GrappleAnchorLocalPoint
                );
            }
        }


        // =============================================================
        // 3. Static World Point
        // =============================================================

        return GrappleWorldPoint;
    }

    private bool UpdateGrapplePointSimulation(
        out Vector3 currentPoint
    )
    {
        currentPoint =
            GrappleWorldPoint;

        // =============================================================
        // Support Interaction Tether Tracking
        // =============================================================

        if (SupportInteractionTetherActive)
        {
            // ---------------------------------------------------------
            // Target Lost
            // ---------------------------------------------------------

            if (GrappleHitNetworkObject == null ||
                GrappleHitNetworkObject.IsValid ==
                    false)
            {
                /*
                * 保留最後一個合法世界座標，
                * 讓 Retract 可以從最後位置開始收回。
                */
                GrappleWorldPoint =
                    PreviousGrapplePoint;


                SupportInteractionTetherActive =
                    false;


                GrappleHitNetworkObject =
                    null;


                GrappleHitObjectLocalPoint =
                    default;


                if (CurrentPhase !=
                    GrapplePhase.Retracting)
                {
                    BeginRetract(
                        GrappleCancelReason.TargetLost,
                        notifyStateMachine: false
                    );


                    return false;
                }


                return true;
            }


            // ---------------------------------------------------------
            // Current Target Point
            // ---------------------------------------------------------

            currentPoint =
                GetCurrentGrapplePoint();


            Vector3 supportPointDelta =
                currentPoint -
                PreviousGrapplePoint;


            // ---------------------------------------------------------
            // Teleport Protection
            // ---------------------------------------------------------

            float maxSupportDelta =
                movingAnchorTeleportBreakDistance;


            if (supportPointDelta.sqrMagnitude >
                maxSupportDelta *
                maxSupportDelta)
            {
                /*
                * 如果目標真的瞬移，
                * 不讓繩索瞬間跨整張地圖。
                *
                * --------------------------------------------------------
                *
                * 注意：
                *
                * 下一步 Support Pull Ability
                * 的正常 0.5 秒拉動，
                * 每 Tick 位移正常不會大到超過此值。
                */
                GrappleWorldPoint =
                    PreviousGrapplePoint;


                SupportInteractionTetherActive =
                    false;


                GrappleHitNetworkObject =
                    null;


                GrappleHitObjectLocalPoint =
                    default;


                if (CurrentPhase !=
                    GrapplePhase.Retracting)
                {
                    BeginRetract(
                        GrappleCancelReason.TargetTeleport,
                        notifyStateMachine: false
                    );


                    return false;
                }


                return true;
            }


            // ---------------------------------------------------------
            // Point Velocity
            // ---------------------------------------------------------

            if (Runner.DeltaTime > 0f)
            {
                GrapplePointVelocity =
                    supportPointDelta /
                    Runner.DeltaTime;
            }
            else
            {
                GrapplePointVelocity =
                    Vector3.zero;
            }


            // ---------------------------------------------------------
            // Cache
            // ---------------------------------------------------------

            GrappleWorldPoint =
                currentPoint;


            PreviousGrapplePoint =
                currentPoint;


            return true;
        }

        if (TrackingDynamicAnchor == false)
        {
            GrapplePointVelocity =
                Vector3.zero;

            PreviousGrapplePoint =
                GrappleWorldPoint;

            return true;
        }

        if (TrackedAnchor == null)
        {
            TrackingDynamicAnchor =
                false;

            GrapplePointVelocity =
                Vector3.zero;

            currentPoint =
                GrappleWorldPoint;

            if (CurrentPhase !=
                GrapplePhase.Retracting)
            {
                BeginRetract(
                    GrappleCancelReason.TargetLost
                );

                return false;
            }

            return true;
        }

        currentPoint =
            GetCurrentGrapplePoint();

        Vector3 pointDelta =
            currentPoint -
            PreviousGrapplePoint;

        float maxDelta =
            movingAnchorTeleportBreakDistance;

        if (pointDelta.sqrMagnitude >
            maxDelta *
            maxDelta)
        {
            GrappleWorldPoint =
                PreviousGrapplePoint;

            TrackingDynamicAnchor =
                false;

            TrackedAnchor =
                null;

            GrapplePointVelocity =
                Vector3.zero;

            currentPoint =
                GrappleWorldPoint;

            if (CurrentPhase !=
                GrapplePhase.Retracting)
            {
                BeginRetract(
                    GrappleCancelReason.TargetTeleport
                );

                return false;
            }

            return true;
        }

        if (Runner.DeltaTime > 0f)
        {
            GrapplePointVelocity =
                pointDelta /
                Runner.DeltaTime;
        }
        else
        {
            GrapplePointVelocity =
                Vector3.zero;
        }

        GrappleWorldPoint =
            currentPoint;

        PreviousGrapplePoint =
            currentPoint;

        return true;
    }

    #endregion

    // =====================================================================
    #region 階段更新

    private void TickGrapplePhase()
    {
        switch (CurrentPhase)
        {
            case GrapplePhase.PreFire:
            {
                if (UpdateGrapplePointSimulation(
                        out _
                    ) == false)
                {
                    return;
                }

                if (PhaseTimer.Expired(Runner))
                {
                    BeginShooting();
                }

                break;
            }

            case GrapplePhase.Shooting:
            {
                if (UpdateGrapplePointSimulation(
                        out _
                    ) == false)
                {
                    return;
                }

                if (PhaseTimer.Expired(Runner))
                {
                    BeginAttached();
                }

                break;
            }

            case GrapplePhase.Attached:
            {
                // =============================================================
                // Support Interaction Tether
                // =============================================================

                /*
                * Support 特殊 Grapple：
                *
                * CurrentPhase 仍然維持 Attached。
                *
                * ------------------------------------------------------------
                *
                * 原因：
                *
                * PlayerGrappleVisual
                * 需要 Attached 才會維持：
                *
                * CurrentRopeExtension = 1。
                *
                * ------------------------------------------------------------
                *
                * 但是：
                *
                * 絕對不執行 PullTowardPoint()。
                *
                * 所以 Support 玩家本人不會被拉走。
                */
                if (SupportInteractionTetherActive)
                {
                    UpdateGrapplePointSimulation(
                        out _
                    );


                    break;
                }


                // =============================================================
                // Normal Grapple
                // =============================================================

                RecordRecentSpeed();


                PullTowardPoint();

                break;
            }

            case GrapplePhase.Retracting:
            {
                UpdateGrapplePointSimulation(
                    out _
                );

                if (PhaseTimer.Expired(Runner))
                {
                    CompleteRetract();
                }

                break;
            }
        }
    }

    private void BeginShooting()
    {
        Vector3 start =
            GetSimulationRopeStartPosition();

        Vector3 target =
            GetCurrentGrapplePoint();

        float distance =
            Vector3.Distance(
                start,
                target
            );

        CurrentShootDuration =
            CalculateDistanceBasedDuration(
                distance,
                ropeShootTimeAtMaxDistance,
                ropeShootMinimumDuration
            );

        if (CurrentShootDuration <= 0f)
        {
            BeginAttached();
            return;
        }

        CurrentPhase =
            GrapplePhase.Shooting;

        PhaseTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                CurrentShootDuration
            );
    }

    /// <summary>
    /// 繩索真正抵達目標，進入 Attached。
    ///
    /// ------------------------------------------------------------
    ///
    /// 一般 Grapple：
    ///
    /// Shooting
    /// ↓
    /// Attached
    /// ↓
    /// PullTowardPoint()
    ///
    /// ------------------------------------------------------------
    ///
    /// Attack 勾中 Enemy：
    ///
    /// Shooting
    /// ↓
    /// Attached
    /// ↓
    /// Commit Attack Mark
    /// ↓
    /// 立即 Retract
    ///
    /// 完全不進入 PullTowardPoint()。
    ///
    /// ------------------------------------------------------------
    ///
    /// 所以 Attack：
    ///
    /// 勾牆壁
    /// → 正常 Grapple 移動。
    ///
    /// 勾 Enemy
    /// → 標記敵人
    /// → 立即收繩
    /// → 玩家不被拉動。
    /// </summary>
    private void BeginAttached()
    {
        // =============================================================
        // 1. 正式進入 Attached
        // =============================================================

        CurrentPhase =
            GrapplePhase.Attached;

        PhaseTimer =
            TickTimer.None;

        HasApproachedPoint =
            false;

        // =============================================================
        // 2. 更新 Grapple Point
        // =============================================================

        Vector3 point =
            GetCurrentGrapplePoint();

        PreviousGrapplePoint =
            point;

        GrapplePointVelocity =
            Vector3.zero;

        // =============================================================
        // 3. Commit 職業 Grapple Interaction
        // =============================================================

        GrappleProfessionInteractionType
            committedInteraction =
                GrappleProfessionInteractionType.None;

        if (grappleInteractionController != null)
        {
            committedInteraction =
                grappleInteractionController
                    .CommitPendingInteraction();
        }

        // =============================================================
        // 4. Attack Enemy Mark
        // =============================================================

        /*
        * Attack 勾中可標記 Enemy 時：
        *
        * CommitPendingInteraction()
        *
        * 已經先觸發：
        *
        * AttackEnemyDetected
        *
        * 所以未來：
        *
        * AttackGrappleMarkAbility
        *
        * 會在這裡正式把五秒 Mark
        * 掛到 Enemy 身上。
        *
        * Mark 完成後：
        *
        * 不開始 PullTowardPoint()，
        * 而是直接開始收繩。
        */
        if (committedInteraction ==
            GrappleProfessionInteractionType
                .AttackMarkCandidate)
        {
            /*
            * 不需要建立 Grapple Momentum。
            *
            * 也不通知：
            *
            * NotifyGrappleEnded()
            *
            * 因為這次玩家根本沒有進入
            * 真正的 Grapple 移動。
            *
            * 特別是在空中使用時，
            * 我們也不希望它錯誤把玩家切成：
            *
            * GrappleAirborne。
            */
            BeginRetract(
                GrappleCancelReason
                    .ProfessionInteraction,

                notifyStateMachine: false
            );

            /*
            * 非常重要：
            *
            * 直接 Return。
            *
            * 本次 Simulation 不會繼續建立
            * Grapple Pull 的速度取樣。
            */
            return;
        }

        // =============================================================
        // 5. Support Enemy / Player Tether
        // =============================================================

        bool isSupportTetherInteraction =
            committedInteraction ==
                GrappleProfessionInteractionType
                    .SupportEnemyPullCandidate ||
            committedInteraction ==
                GrappleProfessionInteractionType
                    .SupportPlayerPullCandidate;


        if (isSupportTetherInteraction)
        {
            // ---------------------------------------------------------
            // 必須有 NetworkObject
            // ---------------------------------------------------------

            /*
            * Support 要拉的是會移動的 Network Target。
            *
            * 如果沒有 NetworkObject，
            * 就不能安全進入 Support Pull 流程。
            */
            if (GrappleHitNetworkObject == null ||
                GrappleHitNetworkObject.IsValid ==
                    false)
            {
                if (debugGrapple)
                {
                    Debug.LogWarning(
                        "[Support Grapple] " +
                        "Interaction 已成立，" +
                        "但目標沒有有效 NetworkObject。" +
                        "\n本次直接收繩。",
                        this
                    );
                }


                BeginRetract(
                    GrappleCancelReason
                        .ProfessionInteraction,

                    notifyStateMachine: false
                );


                return;
            }


            // ---------------------------------------------------------
            // 啟用 Support Tether
            // ---------------------------------------------------------

            SupportInteractionTetherActive =
                true;


            // ---------------------------------------------------------
            // 更新真正 Rope Point
            // ---------------------------------------------------------

            point =
                GetCurrentGrapplePoint();


            GrappleWorldPoint =
                point;


            PreviousGrapplePoint =
                point;


            GrapplePointVelocity =
                Vector3.zero;


            // ---------------------------------------------------------
            // 不建立 Grapple Momentum Sample
            // ---------------------------------------------------------

            ResetRecentSpeedPeak();


            // ---------------------------------------------------------
            // Debug
            // ---------------------------------------------------------

            if (debugGrapple)
            {
                Debug.Log(
                    $"[Support Grapple] 特殊 Tether 已建立。" +
                    $"\nInteraction：{committedInteraction}" +
                    $"\nTarget：{GrappleHitNetworkObject.name}" +
                    $"\nTarget Object：{GrappleHitNetworkObject.Id}" +
                    $"\nRope Point：{point}" +
                    $"\n玩家 PullTowardPoint：停用" +
                    $"\nRope Attached：保持",
                    GrappleHitNetworkObject
                );
            }


            /*
            * ★ 非常重要
            *
            * Return。
            *
            * 不走下面的一般 Grapple：
            *
            * ResetRecentSpeedPeak()
            * RecordRecentSpeed()
            * PullTowardPoint()。
            */
            return;
        }

        // =============================================================
        // 普通玩家移動 Grapple：重新武裝二段跳
        // =============================================================

        /*
         * [修正 CS0103 錯誤 1]
         * 將 GrappleCommittedInteraction.None 更改為 GrappleProfessionInteractionType.None。
         * 因為 committedInteraction 的宣告型別為 GrappleProfessionInteractionType。
         *
         * 詳細註解：
         * 這裡確認本次勾索沒有觸發任何特殊的職業互動（例如：Attack 的標記、Support 的拉人），
         * 屬於一般地形勾索，因此開始執行普通玩家的拉動邏輯。
         */
        if (committedInteraction == GrappleProfessionInteractionType.None)
        {
            stateMachine.NotifyPlayerPullGrappleAttached();

            // 一般地形鈎索開始拉動前，如果玩家仍在地面，
            // 先使用 PlayerMovement 既有的 Jump Impulse 把角色抬起。
            //
            // 已在空中時不疊加；也不消耗二段跳。
            bool appliedAttachJump =
                movement != null &&
                movement.TryApplyGrappleAttachJumpImpulse();

            /*
             * [修正 CS0103 錯誤 2]
             * 將 debugLogs 更改為 debugGrapple。
             * 這是腳本頂端 [Header("除錯設定")] 中宣告的變數名稱。
             */
            if (debugGrapple && appliedAttachJump)
            {
                Debug.Log(
                    "[PlayerGrapple] 一般地形鈎索開始拉動前，已施加一次既有 Jump Impulse。",
                    this);
            }
        }

        
        // =============================================================
        // 6. 一般 Grapple
        // =============================================================

        // 命中點位置不再參與左、中、右判斷。
        // 只比較命中後到拉動前的水平視角轉動量。
        LockInitialGrappleLateralIntent();


        /*
        * Left／Right 會在這裡建立一次固定擺盪平面。
        * Center 則保持 GrappleSwingActive = false，繼續使用原本直線拉動。
        */
        InitializeGrappleSwing(
            point
        );
        
        /*  
        * 新的一次普通 Attached 尚未發生遮蔽，
        * 清除上一輪可能殘留的 Timer。
        */
        GrappleOcclusionTimer =
            TickTimer.None;

        GrappleOcclusionPending =
            false;

        /*
        * 沒有特殊 Interaction
        * 或目前是未實作完成的其他職業：
        *
        * 繼續原本 Grapple 行為。
        */
        ResetRecentSpeedPeak();

        RecordRecentSpeed();

        // =============================================================
        // Debug
        // =============================================================

        if (debugGrapple)
        {
            Debug.Log(
                $"[勾索] 正式 Attached。" +
                $"\nInteraction：{committedInteraction}" +
                $"\nGrapple Point：{point}",
                this
            );
        }
    }

    private void BeginRetract(
        GrappleCancelReason cancelReason,
        bool notifyStateMachine = true
    )
    {
        if (CurrentPhase ==
                GrapplePhase.Idle ||
            CurrentPhase ==
                GrapplePhase.Retracting)
        {
            return;
        }

        GrapplePhase phaseBeforeRetract =
            CurrentPhase;

        /*
        * 方向只允許存活到本次 Grapple 釋放。
        *
        * StartReleaseMomentum() 不依賴此值，
        * 所以可以在 BeginRetract() 一開始安全清除。
        */
        LockedGrappleLateralIntent =
            GrappleLockedLateralIntent.Center;

        // 本次鈎索已經開始釋放，立即清除命中時的視角基準。
        // StartReleaseMomentum() 不會使用這個數值，因此可安全清除。
        GrappleHitLookYaw = 0f;

        // 本次繩索已經釋放，固定擺盪資料不得殘留到下一次鈎索。
        GrappleSwingActive = false;
        GrappleSwingInitialRadialDirection = Vector3.zero;
        GrappleSwingAxis = Vector3.zero;

        GrappleOcclusionTimer =
            TickTimer.None;

        GrappleOcclusionPending =
            false;

        // =============================================================
        // Support Interaction Tether
        // =============================================================

        bool wasSupportInteractionTether =
            SupportInteractionTetherActive;


        /*
        * Support Tether 根本沒有拉動 Player。
        *
        * 所以它結束時不能：
        *
        * NotifyGrappleEnded()
        * → GrappleAirborne
        *
        * 也不能：
        *
        * StartReleaseMomentum()。
        */
        if (wasSupportInteractionTether)
        {
            notifyStateMachine =
                false;
        }

        // =============================================================
        // Attached 前取消職業 Interaction
        // =============================================================

        /*
        * 如果玩家在：
        *
        * PreFire
        * 或
        * Shooting
        *
        * 就再次按 Q，
        *
        * 那代表繩索還沒有真正碰到 Enemy。
        *
        * 所以不能得到 Attack Mark。
        */
        if (phaseBeforeRetract ==
                GrapplePhase.PreFire ||
            phaseBeforeRetract ==
                GrapplePhase.Shooting)
        {
            if (grappleInteractionController != null)
            {
                grappleInteractionController
                    .CancelPendingInteraction();
            }
        }

        // =============================================================
        // Attached 前取消 → 丟棄 Pending Interaction
        // =============================================================

        /*
        * 如果勾索是在：
        *
        * PreFire
        * 或
        * Shooting
        *
        * 階段被取消，
        *
        * 代表繩索根本還沒有正式碰到目標。
        *
        * 因此先前 NotifyGrappleHit()
        * 建立的 Pending Interaction
        * 必須直接取消。
        *
        * ------------------------------------------------------------
        *
        * 例如：
        *
        * Q
        * ↓
        * 鎖定 Enemy
        * ↓
        * Shooting
        * ↓
        * 再次 Q
        *
        * Attack Mark
        * × 不應該發生
        *
        * ------------------------------------------------------------
        *
        * Attached 已經發生的情況則不用取消 Pending，
        * 因為 BeginAttached() 已經 Commit 並清掉了。
        */
        if (phaseBeforeRetract ==
                GrapplePhase.PreFire ||
            phaseBeforeRetract ==
                GrapplePhase.Shooting)
        {
            if (grappleInteractionController != null)
            {
                grappleInteractionController
                    .CancelPendingInteraction();
            }
        }

        bool wasAttached =
            phaseBeforeRetract ==
            GrapplePhase.Attached;

        /*
        * 只有真正進入 Attached、而且不是 Support 拉取目標的 Tether，
        * 才屬於玩家自己的普通地形鈎索移動。
        */
        bool isNormalAttachedPlayerGrapple =
            wasAttached &&
            wasSupportInteractionTether ==
                false;


        /*
        * Q2 主動取消：
        *
        * 玩家在 Attached 期間再次按下鈎索鍵。
        *
        * 這次不再把保留速度重新轉向玩家目前的觀看方向，
        * 而是沿用釋放前的實際水平移動方向，
        * 之後交給既有 GrappleAirborne Momentum 依重力逐步衰退。
        */
        bool manualToggleRelease =
            isNormalAttachedPlayerGrapple &&
            cancelReason ==
                GrappleCancelReason.ManualToggle;


        /*
        * Aim 主動取消：
        *
        * 目前仍保留原本規則，
        * 將釋放 Momentum 導向玩家目前的水平觀看方向。
        *
        * 這次只修改 Q2，因此不連帶更改 Aim Release。
        */
        bool aimRelease =
            isNormalAttachedPlayerGrapple &&
            cancelReason ==
                GrappleCancelReason.AimRelease;


        /*
        * 玩家進入 Grapple Release Distance 後的普通自動釋放。
        */
        bool distanceRelease =
            isNormalAttachedPlayerGrapple &&
            enableMomentumOnDistanceAutoRelease &&
            cancelReason ==
                GrappleCancelReason.AutoReleaseDistance;


        /*
        * 命中點遮蔽自動釋放屬於真正的玩家移動 Grapple 結束，
        * 必須保留釋放當下速度並通知狀態機進入 GrappleAirborne。
        */
        bool occlusionRelease =
            isNormalAttachedPlayerGrapple &&
            cancelReason ==
                GrappleCancelReason.AutoReleaseOccluded;

        /*
        * Left／Right 擺盪已經到達設定的幾何角度。
        *
        * 斷繩時不增加速度，也不改成玩家目前準心方向；
        * 只保留擺盪形成的實際水平速度並進入既有 Momentum。
        */
        bool swingAngleRelease =
            isNormalAttachedPlayerGrapple &&
            cancelReason ==
                GrappleCancelReason.AutoReleaseSwingAngle;


        /*
        * Q2 不再使用 Look Direction。
        *
        * 第一個參數 1f：
        * 不額外放大本次來源速度。
        *
        * 第二個參數 false：
        * 優先使用玩家釋放前的實際水平移動方向，
        * 不把速度重新轉向準心方向。
        */
        if (manualToggleRelease)
        {
            StartReleaseMomentum(
                1f,
                false
            );
        }
        else if (aimRelease)
        {
            /*
            * Aim Release 暫時維持原規則：
            * 使用玩家目前的水平觀看方向。
            */
            StartReleaseMomentum(
                1f,
                true
            );
        }
        else if (swingAngleRelease)
        {
            StartReleaseMomentum(
                1f,
                false
            );
        }
        else if (occlusionRelease)
        {
            /*
            * 遮蔽屬於被動自動斷繩，
            * 保留目前實際水平飛行方向，
            * 不強制轉成玩家視角前方。
            */
            StartReleaseMomentum(
                1f,
                false
            );
        }
        else if (distanceRelease)
        {
            /*
            * 距離自動釋放繼續使用原本的 Inspector 設定。
            */
            StartReleaseMomentum(
                distanceAutoReleaseMomentumMultiplier,
                distanceAutoReleaseUseLookDirection
            );
        }

        ExtensionAtRetractStart =
            GetCurrentRopeExtension();

        if (notifyStateMachine)
        {
            stateMachine.NotifyGrappleEnded(
                kcc.Data.IsGrounded
            );
        }

        if (ExtensionAtRetractStart <=
            0.0001f)
        {
            CompleteRetract();
            return;
        }

        Vector3 start =
            GetSimulationRopeStartPosition();

        Vector3 target =
            GetCurrentGrapplePoint();

        float visibleDistance =
            Vector3.Distance(
                start,
                target
            ) *
            ExtensionAtRetractStart;

        CurrentRetractDuration =
            CalculateDistanceBasedDuration(
                visibleDistance,
                ropeRetractTimeAtMaxDistance,
                ropeRetractMinimumDuration
            );

        if (CurrentRetractDuration <= 0f)
        {
            CompleteRetract();
            return;
        }

        CurrentPhase =
            GrapplePhase.Retracting;

        PhaseTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                CurrentRetractDuration
            );

        HasApproachedPoint =
            false;

        if (debugGrapple)
        {
            Debug.Log(
                $"[勾索] 開始收繩。" +
                $"\n原因：{cancelReason}" +
                $"\n伸出比例：{ExtensionAtRetractStart:F2}" +
                $"\n收回時間：{CurrentRetractDuration:F3}",
                this
            );
        }
    }

    private void CompleteRetract()
    {
        CurrentPhase =
            GrapplePhase.Idle;

        PhaseTimer =
            TickTimer.None;

        CurrentShootDuration =
            0f;

        CurrentRetractDuration =
            0f;

        ExtensionAtRetractStart =
            0f;

        HasApproachedPoint =
            false;

        TrackingDynamicAnchor =
            false;

        TrackedAnchor =
            null;

        GrappleAnchorLocalPoint =
            default;

        GrappleWorldPoint =
            default;

        PreviousGrapplePoint =
            default;

        GrapplePointVelocity =
            default;
        
        // =============================================================
        // Support Tether Cleanup
        // =============================================================

        SupportInteractionTetherActive =
            false;


        GrappleHitNetworkObject =
            null;


        GrappleHitObjectLocalPoint =
            default;

        ResetRecentSpeedPeak();

        // =============================================================
        // Pending Interaction 保險清理
        // =============================================================

        /*
        * 正常情況下：
        *
        * Attached 成功
        * → BeginAttached()
        * → CommitPendingInteraction()
        *
        * Attached 前取消
        * → BeginRetract()
        * → CancelPendingInteraction()
        *
        * 所以走到這裡理論上不應該還有 Pending。
        *
        * 但 CompleteRetract() 同時也可能被：
        *
        * Special Ability
        * Forced Reset
        * Target Lost
        * 特殊流程
        *
        * 呼叫。
        *
        * 因此最後再做一次保險清理，
        * 防止舊的 Interaction 殘留到下一次 Grapple。
        */
        if (grappleInteractionController != null)
        {
            grappleInteractionController
                .CancelPendingInteraction();
        }

        LockedGrappleLateralIntent =
            GrappleLockedLateralIntent.Center;
        
        // 本次繩索已經釋放，固定擺盪資料不得殘留到下一次鈎索。
        GrappleSwingActive = false;
        GrappleSwingInitialRadialDirection = Vector3.zero;
        GrappleSwingAxis = Vector3.zero;

        // 本次鈎索已經開始釋放，立即清除命中時的視角基準。
        // StartReleaseMomentum() 不會使用這個數值，因此可安全清除。
        GrappleHitLookYaw = 0f;

        GrappleOcclusionTimer =
            TickTimer.None;

        GrappleOcclusionPending =
            false;

        if (debugGrapple)
        {
            Debug.Log(
                "[勾索] 收繩完成。",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 拉動

    /// <summary>
    /// Gameplay 視線判斷使用的穩定世界起點。
    ///
    /// 不使用 CamTarget.position，因為 CamTarget 是本地 LateUpdate 視覺；
    /// Fusion 模擬改用 KCC TargetPosition 加固定眼睛高度。
    /// </summary>
    private Vector3 GetGrappleViewOrigin()
    {
        return
            kcc.Data.TargetPosition +
            Vector3.up *
            rayOriginHeight;
    }

    /// <summary>
    /// 更新普通 Attached Grapple 的命中點遮蔽狀態。
    ///
    /// 回傳 true：仍可繼續拉動。
    /// 回傳 false：已經自動釋放，本 Tick 必須停止拉動。
    /// </summary>
    private bool TickGrapplePointOcclusion(
        Vector3 grapplePoint
    )
    {
        bool isOccluded =
            HasPhysicalOcclusionToGrapplePoint(
                grapplePoint
            );

        if (isOccluded == false)
        {
            /*
            * 只要中途任何一個 Tick 重新看見命中點，
            * 必須重新累積完整 0.1 秒。
            */
            GrappleOcclusionTimer =
                TickTimer.None;

            GrappleOcclusionPending =
                false;

            return true;
        }

        float releaseDelay =
            Mathf.Max(
                0f,
                grappleOcclusionReleaseDelay
            );

        if (releaseDelay <= 0f)
        {
            BeginRetract(
                GrappleCancelReason
                    .AutoReleaseOccluded
            );

            return false;
        }

        if (GrappleOcclusionPending ==
            false)
        {
            GrappleOcclusionPending =
                true;

            GrappleOcclusionTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    releaseDelay
                );

            if (debugGrapple)
            {
                Debug.Log(
                    $"[勾索] 命中點開始被遮蔽。" +
                    $"\n寬限時間：{releaseDelay:F3} 秒",
                    this
                );
            }

            return true;
        }

        if (GrappleOcclusionTimer.Expired(
                Runner
            ) == false)
        {
            return true;
        }

        if (debugGrapple)
        {
            Debug.Log(
                "[勾索] 命中點連續遮蔽時間已到，自動釋放。",
                this
            );
        }

        BeginRetract(
            GrappleCancelReason
                .AutoReleaseOccluded
        );

        return false;
    }

    /// <summary>
    /// 判斷玩家視線高度到 Grapple 命中點之間
    /// 是否存在指定 Layer 的實體障礙物。
    ///
    /// 只檢查到命中點前的 Skin 距離，
    /// 避免命中點所在牆面被誤判為遮蔽物。
    /// </summary>
    private bool HasPhysicalOcclusionToGrapplePoint(
        Vector3 grapplePoint
    )
    {
        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() == false)
        {
            return false;
        }

        Vector3 origin =
            GetGrappleViewOrigin();

        Vector3 toPoint =
            grapplePoint -
            origin;

        float distance =
            toPoint.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        float checkDistance =
            distance -
            Mathf.Max(
                0f,
                grappleOcclusionTargetSkin
            );

        if (checkDistance <= 0f)
        {
            return false;
        }

        Vector3 direction =
            toPoint /
            distance;

        bool hasObstacle =
            physicsScene.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                checkDistance,
                grappleOcclusionObstacleMask,
                QueryTriggerInteraction.Ignore
            );

        if (hasObstacle &&
            debugGrapple)
        {
            Debug.DrawLine(
                origin,
                hit.point,
                Color.yellow,
                Runner.DeltaTime
            );
        }

        return hasObstacle;
    }

    /// <summary>
    /// 將玩家依照命中後視角轉動得到的原始意圖，
    /// 轉換成目前 Inspector 真正允許使用的 Grapple 拉動模式。
    ///
    /// 過濾規則：
    ///
    /// 1. 原始模式有開啟時，直接保留。
    /// 2. Left／Right 被關閉時，優先退回 Straight。
    /// 3. Straight 也關閉時，才改用另一個仍開啟的擺盪方向。
    /// 4. Straight 被關閉、Left 與 Right 都開啟時，
    ///    依照實際 yawDelta 的正負選擇左右。
    /// 5. 三個模式若意外全部關閉，Runtime 使用 Straight 作為安全備援，
    ///    避免普通地形鈎索進入沒有合法移動模式的狀態。
    /// </summary>
    private GrappleLockedLateralIntent ResolveEnabledGrappleMode(
        GrappleLockedLateralIntent requestedIntent,
        float yawDelta
    )
    {
        // -------------------------------------------------------------
        // 原始要求的模式仍被允許：直接保留
        // -------------------------------------------------------------

        if (requestedIntent == GrappleLockedLateralIntent.Center &&
            enableGrappleStraightPull)
        {
            return GrappleLockedLateralIntent.Center;
        }

        if (requestedIntent == GrappleLockedLateralIntent.Left &&
            enableGrappleLeftSwing)
        {
            return GrappleLockedLateralIntent.Left;
        }

        if (requestedIntent == GrappleLockedLateralIntent.Right &&
            enableGrappleRightSwing)
        {
            return GrappleLockedLateralIntent.Right;
        }

        // -------------------------------------------------------------
        // Left 被關閉
        // -------------------------------------------------------------

        if (requestedIntent == GrappleLockedLateralIntent.Left)
        {
            // 左甩不可用時，優先退回不改變方向的直線拉動。
            if (enableGrappleStraightPull)
            {
                return GrappleLockedLateralIntent.Center;
            }

            // Straight 也被關閉時，只能改用仍允許的右甩。
            if (enableGrappleRightSwing)
            {
                return GrappleLockedLateralIntent.Right;
            }
        }

        // -------------------------------------------------------------
        // Right 被關閉
        // -------------------------------------------------------------

        if (requestedIntent == GrappleLockedLateralIntent.Right)
        {
            // 右甩不可用時，優先退回不改變方向的直線拉動。
            if (enableGrappleStraightPull)
            {
                return GrappleLockedLateralIntent.Center;
            }

            // Straight 也被關閉時，只能改用仍允許的左甩。
            if (enableGrappleLeftSwing)
            {
                return GrappleLockedLateralIntent.Left;
            }
        }

        // -------------------------------------------------------------
        // Center 被關閉
        // -------------------------------------------------------------

        if (requestedIntent == GrappleLockedLateralIntent.Center)
        {
            /*
            * 玩家雖然沒有超過正式轉頭門檻，
            * 但只要仍存在很小的 yawDelta，便優先尊重其方向。
            */
            if (yawDelta < 0f &&
                enableGrappleLeftSwing)
            {
                return GrappleLockedLateralIntent.Left;
            }

            if (yawDelta > 0f &&
                enableGrappleRightSwing)
            {
                return GrappleLockedLateralIntent.Right;
            }

            /*
            * 視角完全沒有水平變化，或其偏向側剛好被關閉時，
            * 使用固定備援順序：Right → Left。
            *
            * 這讓只開啟 Right 的設定必定得到右甩，
            * 同時避免零角度時在左右之間產生不穩定切換。
            */
            if (enableGrappleRightSwing)
            {
                return GrappleLockedLateralIntent.Right;
            }

            if (enableGrappleLeftSwing)
            {
                return GrappleLockedLateralIntent.Left;
            }
        }

        // -------------------------------------------------------------
        // 全部關閉的 Runtime 安全備援
        // -------------------------------------------------------------

        if (debugGrapple)
        {
            Debug.LogWarning(
                "[PlayerGrapple] Straight、Left、Right 三個拉動模式全部被關閉。" +
                "本次普通地形鈎索暫時使用 Straight，請檢查 PlayerGrapple Inspector。",
                this
            );
        }

        return GrappleLockedLateralIntent.Center;
    }

    /// <summary>
    /// 在普通地形鈎索真正開始拉動玩家前，只判斷一次原始視角意圖，
    /// 再依照 Inspector 的 Straight／Left／Right Bool 過濾成最終模式。
    ///
    /// 最終結果一旦寫入 LockedGrappleLateralIntent，
    /// 本次鈎索釋放以前都不再重新選擇，也不允許左右反轉。
    /// </summary>
    private void LockInitialGrappleLateralIntent()
    {
        if (kcc == null)
        {
            LockedGrappleLateralIntent =
                ResolveEnabledGrappleMode(
                    GrappleLockedLateralIntent.Center,
                    0f
                );

            return;
        }

        float currentLookYaw =
            kcc.Data.LookYaw;

        /*
        * DeltaAngle 回傳 -180～180 度的最短角度差：
        *
        * 正值：命中後往右轉頭。
        * 負值：命中後往左轉頭。
        */
        float yawDelta =
            Mathf.DeltaAngle(
                GrappleHitLookYaw,
                currentLookYaw
            );

        float yawThreshold =
            Mathf.Clamp(
                grapplePostHitYawIntentThreshold,
                0f,
                180f
            );

        GrappleLockedLateralIntent requestedIntent;

        if (yawDelta < -yawThreshold)
        {
            requestedIntent =
                GrappleLockedLateralIntent.Left;
        }
        else if (yawDelta > yawThreshold)
        {
            requestedIntent =
                GrappleLockedLateralIntent.Right;
        }
        else
        {
            requestedIntent =
                GrappleLockedLateralIntent.Center;
        }

        /*
        * requestedIntent 是玩家原本的視角意圖。
        * 最後還要通過三個模式 Bool，才能成為本次真正鎖定的結果。
        */
        LockedGrappleLateralIntent =
            ResolveEnabledGrappleMode(
                requestedIntent,
                yawDelta
            );

        if (debugGrapple)
        {
            Debug.Log(
                $"[PlayerGrapple] Grapple 模式已鎖定。" +
                $"\nYaw Delta：{yawDelta:F1}°" +
                $"\n原始意圖：{requestedIntent}" +
                $"\n最終模式：{LockedGrappleLateralIntent}" +
                $"\nStraight：{enableGrappleStraightPull}" +
                $"\nLeft：{enableGrappleLeftSwing}" +
                $"\nRight：{enableGrappleRightSwing}",
                this
            );
        }
    }

    /// <summary>
    /// 為本次普通地形鈎索建立固定擺盪平面。
    ///
    /// 只有 LockedGrappleLateralIntent 為 Left 或 Right 時才會成功。
    /// Center 仍走原本的直線拉動，不建立擺盪軸。
    ///
    /// 建立後會固定：
    ///
    /// 1. 命中點指向玩家的初始繩索方向。
    /// 2. 本次左擺或右擺所使用的世界空間旋轉軸。
    ///
    /// 後續不再使用玩家每個 Tick 的 View Right 重新決定側移方向。
    /// </summary>
    private void InitializeGrappleSwing(
        Vector3 grapplePoint
    )
    {
        GrappleSwingActive = false;
        GrappleSwingInitialRadialDirection = Vector3.zero;
        GrappleSwingAxis = Vector3.zero;

        float lockedLateralInput =
            GetLockedGrappleLateralInput();

        // Center 代表一般直線拉動，不建立擺盪模型。
        if (Mathf.Abs(lockedLateralInput) <= 0.001f)
        {
            return;
        }

        Vector3 playerPullPosition =
            kcc.Data.TargetPosition +
            Vector3.up *
            rayOriginHeight *
            0.5f;

        Vector3 radialFromAnchor =
            playerPullPosition -
            grapplePoint;

        if (radialFromAnchor.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 initialRadialDirection =
            radialFromAnchor.normalized;

        /*
        * BeginAttached() 當下才使用一次目前的畫面右方，
        * 將它投影到垂直於繩索的切面。
        *
        * Right 使用正方向；Left 使用反方向。
        */
        Vector3 viewRight =
            kcc.Data.TransformRotation *
            Vector3.right;

        Vector3 initialTangentDirection =
            Vector3.ProjectOnPlane(
                viewRight,
                initialRadialDirection
            );

        if (initialTangentDirection.sqrMagnitude <= 0.0001f)
        {
            // 極端角度備援：使用世界 Up 與繩索建立合法切線。
            initialTangentDirection =
                Vector3.Cross(
                    Vector3.up,
                    initialRadialDirection
                );
        }

        if (initialTangentDirection.sqrMagnitude <= 0.0001f)
        {
            initialTangentDirection =
                Vector3.Cross(
                    Vector3.forward,
                    initialRadialDirection
                );
        }

        if (initialTangentDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        initialTangentDirection.Normalize();
        initialTangentDirection *= lockedLateralInput;

        /*
        * radial × tangent 會得到擺盪平面的固定旋轉軸。
        *
        * 之後使用 axis × currentRadial，
        * 便能在玩家繞過命中點時持續取得同一旋轉方向的切線。
        */
        Vector3 swingAxis =
            Vector3.Cross(
                initialRadialDirection,
                initialTangentDirection
            );

        if (swingAxis.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        GrappleSwingInitialRadialDirection =
            initialRadialDirection;

        GrappleSwingAxis =
            swingAxis.normalized;

        GrappleSwingActive = true;
    }

    /// <summary>
    /// 計算玩家從 Attached 初始繩索方向開始，
    /// 沿本次固定擺盪軸已經旋轉多少度。
    ///
    /// 回傳值使用本次鎖定的正向擺盪方向：
    /// 正值代表正在朝選定方向繞行；負值代表受到碰撞或外力推向反方向。
    /// </summary>
    private float CalculateCurrentGrappleSwingAngle(
        Vector3 currentRadialDirection
    )
    {
        Vector3 swingAxis =
            GrappleSwingAxis;

        if (swingAxis.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        swingAxis.Normalize();

        /*
        * 將初始與目前繩索方向都投影回固定擺盪平面，
        * 避免重力、斜牆碰撞或 KCC 修正造成少量離面位移時，
        * 角度判斷跟著漂移。
        */
        Vector3 initialOnSwingPlane =
            Vector3.ProjectOnPlane(
                GrappleSwingInitialRadialDirection,
                swingAxis
            );

        Vector3 currentOnSwingPlane =
            Vector3.ProjectOnPlane(
                currentRadialDirection,
                swingAxis
            );

        if (initialOnSwingPlane.sqrMagnitude <= 0.0001f ||
            currentOnSwingPlane.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        return Vector3.SignedAngle(
            initialOnSwingPlane.normalized,
            currentOnSwingPlane.normalized,
            swingAxis
        );
    }

    /// <summary>
    /// 模擬已鎖定 Left／Right 的固定平面鈎索擺盪。
    ///
    /// 與 Center 直線拉動不同：
    ///
    /// 1. 不把整個速度持續重建為指向命中點。
    /// 2. 只逐步建立有限的向內張力。
    /// 3. 使用 Attached 時固定的旋轉軸計算目前切線方向。
    /// 4. 保留不屬於向內與切線控制的其他速度分量。
    /// 5. 自動釋放只看幾何擺角，不使用速度門檻。
    /// </summary>
    /// <returns>
    /// true：本 Tick 已由擺盪流程完整處理，呼叫端不可再執行 Center 直線拉動。
    /// false：擺盪資料失效，呼叫端可以退回原本直線拉動作為安全備援。
    /// </returns>
    private bool TickFixedPlaneGrappleSwing(
        Vector3 currentPoint,
        Vector3 playerPullPosition,
        float distance,
        Vector3 pullDirection
    )
    {
        if (GrappleSwingActive == false)
        {
            return false;
        }

        float emergencyDistance =
            Mathf.Max(
                0.05f,
                grappleSwingEmergencyReleaseDistance
            );

        /*
        * 這只是避免玩家與命中點幾乎重疊時，
        * radial direction 因浮點誤差不穩定。
        * 主要甩出仍然由下方幾何角度判斷。
        */
        if (distance <= emergencyDistance)
        {
            BeginRetract(
                GrappleCancelReason.AutoReleaseDistance
            );

            return true;
        }

        Vector3 currentRadialDirection =
            playerPullPosition -
            currentPoint;

        if (currentRadialDirection.sqrMagnitude <= 0.0001f)
        {
            BeginRetract(
                GrappleCancelReason.AutoReleaseDistance
            );

            return true;
        }

        currentRadialDirection.Normalize();

        float currentSwingAngle =
            CalculateCurrentGrappleSwingAngle(
                currentRadialDirection
            );

        float releaseAngle =
            Mathf.Clamp(
                grappleSwingReleaseAngle,
                1f,
                179f
            );

        /*
        * 甩出判斷只使用幾何擺角。
        * 不讀取目前速度、最近最高速度或切線速度。
        */
        if (currentSwingAngle >= releaseAngle)
        {
            if (debugGrapple)
            {
                Debug.Log(
                    $"[勾索擺盪] 幾何擺角達到 {currentSwingAngle:F1}°，自動斷繩甩出。",
                    this
                );
            }

            BeginRetract(
                GrappleCancelReason.AutoReleaseSwingAngle
            );

            return true;
        }

        Vector3 swingAxis =
            GrappleSwingAxis;

        if (swingAxis.sqrMagnitude <= 0.0001f)
        {
            GrappleSwingActive = false;
            return false;
        }

        swingAxis.Normalize();

        /*
        * axis × radial 會取得本次固定旋轉方向的圓弧切線。
        *
        * 不再使用玩家目前 View Right，
        * 因此後續轉頭不會讓已鎖定方向突然反轉。
        */
        Vector3 swingTangentDirection =
            Vector3.Cross(
                swingAxis,
                currentRadialDirection
            );

        if (swingTangentDirection.sqrMagnitude <= 0.0001f)
        {
            GrappleSwingActive = false;
            return false;
        }

        swingTangentDirection.Normalize();

        Vector3 dynamicVelocity =
            kcc.Data.DynamicVelocity;

        /*
        * 在移動命中點的情況下，先轉成相對於 Grapple Point 的速度，
        * 完成擺盪控制後再把錨點速度加回去。
        */
        Vector3 relativeVelocity =
            dynamicVelocity -
            GrapplePointVelocity;

        // -------------------------------------------------------------
        // 有限向內張力
        // -------------------------------------------------------------

        float currentInwardSpeed =
            Vector3.Dot(
                relativeVelocity,
                pullDirection
            );

        float targetInwardSpeed =
            Mathf.Max(
                0f,
                grappleSwingMaximumInwardSpeed
            );

        float newInwardSpeed =
            Mathf.MoveTowards(
                currentInwardSpeed,
                targetInwardSpeed,
                Mathf.Max(
                    0f,
                    grappleSwingInwardAcceleration
                ) *
                Runner.DeltaTime
            );

        relativeVelocity +=
            pullDirection *
            (
                newInwardSpeed -
                currentInwardSpeed
            );

        // -------------------------------------------------------------
        // 固定平面的切線引導
        // -------------------------------------------------------------

        float currentTangentialSpeed =
            Vector3.Dot(
                relativeVelocity,
                swingTangentDirection
            );

        float targetTangentialSpeed =
            Mathf.Max(
                0f,
                grappleSwingMaximumTangentialSpeed
            );

        float newTangentialSpeed =
            Mathf.MoveTowards(
                currentTangentialSpeed,
                targetTangentialSpeed,
                Mathf.Max(
                    0f,
                    grappleSwingTangentialAcceleration
                ) *
                Runner.DeltaTime
            );

        relativeVelocity +=
            swingTangentDirection *
            (
                newTangentialSpeed -
                currentTangentialSpeed
            );

        /*
        * 只修改向內張力與固定切線上的分量。
        * 其他由重力、Jump Impulse、碰撞或外力造成的速度仍保留。
        */
        kcc.SetDynamicVelocity(
            relativeVelocity +
            GrapplePointVelocity
        );

        return true;
    }

    private void PullTowardPoint()
    {
        /*
         * PlayerMovement 位於本 Tick 較前面，
         * MovementInputInfluence 已經會讓普通 WASD 歸零。
         *
         * 這裡再直接清除一次 InputDirection，
         * 用來保證「Shooting → Attached」的第一個 Tick
         * 也不會殘留先前 W/S Input。
         */
        kcc.SetInputDirection(
            Vector3.zero
        );

        if (UpdateGrapplePointSimulation(
                out Vector3 currentPoint
            ) == false)
        {
            return;
        }

        /*
        * 必須在本 Tick 施加任何拉動速度以前完成遮蔽判斷。
        *
        * false 代表遮蔽寬限已到，
        * 方法內已經 BeginRetract()，本 Tick 不可再施加拉力。
        */
        if (TickGrapplePointOcclusion(
                currentPoint
            ) == false)
        {
            return;
        }

        Vector3 playerPullPosition =
            kcc.Data.TargetPosition +
            Vector3.up *
            rayOriginHeight *
            0.5f;

        Vector3 toPoint =
            currentPoint -
            playerPullPosition;

        float distance =
            toPoint.magnitude;

        if (distance <= 0.0001f)
        {
            BeginRetract(
                GrappleCancelReason.AutoReleaseDistance
            );

            return;
        }

        Vector3 pullDirection =
            toPoint /
            distance;

        /*
        * Left／Right 使用固定平面擺盪。
        *
        * true 代表本 Tick 已經完成速度控制，
        * 不可繼續執行下面 Center 使用的直線拉動，
        * 否則直線拉力會再次把玩家拉回命中點。
        */
        if (TickFixedPlaneGrappleSwing(
                currentPoint,
                playerPullPosition,
                distance,
                pullDirection
            ))
        {
            return;
        }

        Vector3 relativeVelocity =
            kcc.Data.RealVelocity -
            GrapplePointVelocity;

        float speedTowardPoint =
            Vector3.Dot(
                relativeVelocity,
                pullDirection
            );

        if (HasApproachedPoint == false &&
            speedTowardPoint >=
            grappleApproachSpeedThreshold)
        {
            HasApproachedPoint =
                true;
        }

        bool reachedReleaseDistance =
            HasApproachedPoint &&
            distance <=
            grappleReleaseDistance;

        bool passedClosestPoint =
            HasApproachedPoint &&
            speedTowardPoint <=
            -grappleReleaseAwaySpeed;

        if (reachedReleaseDistance)
        {
            BeginRetract(
                GrappleCancelReason.AutoReleaseDistance
            );

            return;
        }

        if (passedClosestPoint)
        {
            BeginRetract(
                GrappleCancelReason.AutoReleasePassedPoint
            );

            return;
        }

        Vector3 dynamicVelocity =
            kcc.Data.DynamicVelocity;

        float currentPullSpeed =
            Vector3.Dot(
                dynamicVelocity,
                pullDirection
            );

        float newPullSpeed =
            Mathf.MoveTowards(
                currentPullSpeed,
                grappleMaxSpeed,
                grappleAcceleration *
                Runner.DeltaTime
            );

        Vector3 remainingVelocity =
            dynamicVelocity -
            pullDirection *
            currentPullSpeed;


        Vector3 newVelocity =
            remainingVelocity +
            pullDirection *
            newPullSpeed;

        // =========================================================
        // 把原先放錯位置的側移甩出判斷移到這裡
        // =========================================================

        /*
        * 先把本 Tick 完成計算的前拉速度與側移速度寫回 KCC。
        *
        * 如果本 Tick 達到甩出門檻，後面的 BeginRetract()
        * 會從 DynamicVelocity 擷取這個完整合成速度，
        * 而不是使用上一個 Tick 尚未完成側移加速的舊速度。
        */
        kcc.SetDynamicVelocity(
            newVelocity
        );

    }

    /// <summary>
    /// 取得本次一般地形鈎索已鎖定的側移輸入。
    ///
    /// 這個值是在 BeginAttached() 時，
    /// 依照命中後的水平視角轉動量決定一次。
    /// 拉動期間不再讀取即時 A/D，也不允許玩家反轉方向。
    /// </summary>
    private float GetLockedGrappleLateralInput()
    {
        switch (LockedGrappleLateralIntent)
        {
            case GrappleLockedLateralIntent.Left:
                return -1f;

            case GrappleLockedLateralIntent.Right:
                return 1f;

            case GrappleLockedLateralIntent.Center:
            default:
                return 0f;
        }
    }

    /// <summary>
    /// 取得「畫面右方」投影到繩索切面後的方向。
    ///
    /// 這讓 A/D 始終盡量符合玩家目前視角左右，
    /// 同時不會直接增加或減少沿繩索方向的速度。
    /// </summary>
    private Vector3 ResolveGrappleLateralDirection(
        Vector3 pullDirection
    )
    {
        Vector3 viewRight =
            kcc.Data.TransformRotation *
            Vector3.right;

        Vector3 lateralDirection =
            Vector3.ProjectOnPlane(
                viewRight,
                pullDirection
            );

        if (lateralDirection.sqrMagnitude >
            0.0001f)
        {
            return
                lateralDirection.normalized;
        }

        /*
         * 極端情況：視角右方幾乎與繩索方向平行。
         * 改用世界 Up 與繩索建立一個合法切面方向。
         */
        lateralDirection =
            Vector3.Cross(
                Vector3.up,
                pullDirection
            );

        if (lateralDirection.sqrMagnitude <=
            0.0001f)
        {
            lateralDirection =
                Vector3.Cross(
                    Vector3.forward,
                    pullDirection
                );
        }

        if (lateralDirection.sqrMagnitude <=
            0.0001f)
        {
            return
                Vector3.zero;
        }

        lateralDirection.Normalize();

        /*
         * 讓 fallback 的正方向盡量仍然朝畫面右方，
         * 避免接近退化角度時 A/D 突然完全反向。
         */
        if (Vector3.Dot(
                lateralDirection,
                viewRight
            ) < 0f)
        {
            lateralDirection =
                -lateralDirection;
        }

        return
            lateralDirection;
    }

    #endregion

    // =====================================================================
    #region Momentum 速度取樣

    private void ResetRecentSpeedPeak()
    {
        RecentPeakHorizontalSpeed =
            0f;

        RecentPeakTimer =
            TickTimer.None;
    }

    private void RecordRecentSpeed()
    {
        Vector3 realPlanar =
            Vector3.ProjectOnPlane(
                kcc.Data.RealVelocity,
                Vector3.up
            );

        Vector3 dynamicPlanar =
            Vector3.ProjectOnPlane(
                kcc.Data.DynamicVelocity,
                Vector3.up
            );

        float sample =
            Mathf.Max(
                realPlanar.magnitude,
                dynamicPlanar.magnitude
            );

        float remaining =
            RecentPeakTimer
                .RemainingTime(Runner) ?? 0f;

        if (remaining <= 0f)
        {
            RecentPeakHorizontalSpeed =
                sample;

            RecentPeakTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    releasePeakSampleWindow
                );

            return;
        }

        if (sample >
            RecentPeakHorizontalSpeed)
        {
            RecentPeakHorizontalSpeed =
                sample;

            RecentPeakTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    releasePeakSampleWindow
                );
        }
    }

    #endregion

    // =====================================================================
    #region Release Momentum

    private void StartReleaseMomentum(
        float sourceSpeedMultiplier,
        bool useHorizontalLookDirection
    )
    {
        RecordRecentSpeed();

        float baseMovementSpeed =
            ResolveReleaseBaseMovementSpeed();

        if (kcc.Data.IsGrounded)
        {
            StopReleaseMomentum(
                false
            );

            return;
        }

        Vector3 currentPlanarVelocity =
            Vector3.ProjectOnPlane(
                kcc.Data.RealVelocity,
                Vector3.up
            );

        Vector3 currentDynamicPlanarVelocity =
            Vector3.ProjectOnPlane(
                kcc.Data.DynamicVelocity,
                Vector3.up
            );

        float capturedSpeed =
            Mathf.Max(
                currentPlanarVelocity.magnitude,
                currentDynamicPlanarVelocity.magnitude,
                RecentPeakHorizontalSpeed
            );

        capturedSpeed *=
            releaseSpeedMultiplier *
            Mathf.Max(
                0f,
                sourceSpeedMultiplier
            );

        if (releaseMaximumSpeed > 0f)
        {
            capturedSpeed =
                Mathf.Min(
                    capturedSpeed,
                    releaseMaximumSpeed
                );
        }

        /*
        * 如果斷繩速度本來就沒有高於普通 KCC 速度，
        * 不需要啟動高速 Momentum Controller。
        */
        if (capturedSpeed <=
            baseMovementSpeed +
            releaseStopSpeed)
        {
            return;
        }

        Vector3 direction;

        if (useHorizontalLookDirection)
        {
            direction =
                movement
                    .GetHorizontalLookDirection();
        }
        else
        {
            direction =
                currentPlanarVelocity;

            if (direction.sqrMagnitude <=
                0.0001f)
            {
                direction =
                    currentDynamicPlanarVelocity;
            }

            if (direction.sqrMagnitude <=
                0.0001f)
            {
                direction =
                    movement
                        .GetHorizontalLookDirection();
            }
        }

        direction.y =
            0f;

        if (direction.sqrMagnitude <=
            0.0001f)
        {
            return;
        }

        direction.Normalize();

        ReleaseMomentumDirection =
            direction;

        ReleaseMomentumSpeed =
            capturedSpeed;

        ReleaseMomentumBaseSpeed =
            baseMovementSpeed;

        ReleaseMomentumActive =
            true;

        ApplyReleaseMomentumVelocity();

        if (debugMomentum)
        {
            Debug.Log(
                $"[勾索 Momentum] 啟動。" +
                $"\n速度：{capturedSpeed:F2}" +
                $"\n方向：{direction}",
                this
            );
        }
    }

    /// <summary>
    /// 更新 GrappleAirborne 的水平 Momentum。
    ///
    /// 優先規則：
    /// 1. 外部系統封鎖移動時，暫停本系統更新。
    /// 2. 玩家有 WASD 時，WASD 方向高於原 Momentum 方向。
    /// 3. 玩家沒有 WASD 時，沿用原 Momentum 方向。
    /// 4. 水平速度依 KCC Gravity 強度下降到普通移動速度。
    /// </summary>
    private void TickReleaseMomentum(
        Vector2 moveInput,
        float activeMovementInfluence
    )
    {
        if (ReleaseMomentumActive == false)
            return;

        if (kcc.Data.IsGrounded)
        {
            StopReleaseMomentum(
                true
            );

            return;
        }

        if (ReleaseMomentumDirection.sqrMagnitude <=
            0.0001f)
        {
            StopReleaseMomentum(
                true
            );

            return;
        }

        if (HasReleaseObstacleAhead())
        {
            StopReleaseMomentum(
                true
            );

            return;
        }

        /*
        * Support Aerial Ability、Support Pull
        * 或其他職業狀態如果正在封鎖主動移動，
        * 就暫停 Momentum 的方向更新與速度衰退。
        *
        * 這點很重要：
        * Support Aerial Ability 會保存並壓縮 DynamicVelocity，
        * 如果這裡仍持續衰退，能力解除時就可能返還過期舊速度。
        */
        if (activeMovementInfluence <=
            0.0001f)
        {
            return;
        }

        UpdateReleaseMomentumDirectionFromInput(
            moveInput
        );

        float baseMovementSpeed =
            Mathf.Max(
                0f,
                ReleaseMomentumBaseSpeed
            );

        float gravityMagnitude =
            kcc.Data.Gravity.magnitude;

        /*
        * Spawn 初期或 Processor 尚未提供 Gravity 時使用備援。
        */
        if (gravityMagnitude <= 0.0001f)
        {
            gravityMagnitude =
                Physics.gravity.magnitude;
        }

        float decayPerSecond =
            gravityMagnitude *
            Mathf.Max(
                0f,
                releaseGravityDecayMultiplier
            );

        if (decayPerSecond > 0f)
        {
            ReleaseMomentumSpeed =
                Mathf.MoveTowards(
                    ReleaseMomentumSpeed,
                    baseMovementSpeed,
                    decayPerSecond *
                    Runner.DeltaTime
                );
        }

        if (ReleaseMomentumSpeed <=
            baseMovementSpeed +
            releaseStopSpeed)
        {
            CompleteReleaseMomentumToBaseSpeed();
            return;
        }

        ApplyReleaseMomentumVelocity();
    }

    /// <summary>
    /// 玩家有主動 WASD 時，使用目前玩家 Yaw
    /// 把二維輸入轉成世界水平移動方向，
    /// 並取代本 Tick 的 Momentum 方向。
    ///
    /// 沒有輸入時完全不修改方向，
    /// 因此仍會保留斷繩時的 Momentum。
    /// </summary>
    private void UpdateReleaseMomentumDirectionFromInput(
        Vector2 moveInput
    )
    {
        Vector2 clampedInput =
            Vector2.ClampMagnitude(
                moveInput,
                1f
            );

        if (clampedInput.sqrMagnitude <=
            0.0001f)
        {
            return;
        }

        Vector3 localDirection =
            new Vector3(
                clampedInput.x,
                0f,
                clampedInput.y
            );

        Vector3 worldDirection =
            kcc.Data.TransformRotation *
            localDirection;

        worldDirection =
            Vector3.ProjectOnPlane(
                worldDirection,
                Vector3.up
            );

        if (worldDirection.sqrMagnitude <=
            0.0001f)
        {
            return;
        }

        /*
        * 主動移動的方向優先於舊 Momentum 方向。
        *
        * 注意：只替換方向，不額外增加速度。
        */
        ReleaseMomentumDirection =
            worldDirection.normalized;
    }

    private void ApplyReleaseMomentumVelocity()
    {
        Vector3 currentDynamicVelocity =
            kcc.Data.DynamicVelocity;

        /*
        * Release Momentum Active 期間，
        * 水平移動由這個單一 Controller 負責。
        *
        * 同時清除 InputDirection 與舊 KinematicVelocity，
        * 避免斷繩的第一個 Tick 或後續 Tick
        * 又由 Environment Processor 加上一層普通移動。
        * 垂直重力與跳躍仍保存在 DynamicVelocity Y。
        */
        kcc.SetInputDirection(
            Vector3.zero
        );

        kcc.SetKinematicVelocity(
            Vector3.zero
        );

        /*
         * 依照你目前確認的需求：
         *
         * 完整保留目前 Y 軸 DynamicVelocity。
         *
         * 不再 Clamp 最大向上／向下速度。
         */
        float protectedVerticalSpeed =
            currentDynamicVelocity.y;

        Vector3 horizontalVelocity =
            ReleaseMomentumDirection.normalized *
            ReleaseMomentumSpeed;

        // =========================================================
        // 修正 CS0103: 恢復正確的 newVelocity 計算
        // =========================================================
        Vector3 newVelocity =
            horizontalVelocity +
            Vector3.up *
            protectedVerticalSpeed;

        kcc.SetDynamicVelocity(
            newVelocity
        );
    }

    /// <summary>
    /// 取得本次 Momentum 應衰退回去的普通 KCC 速度。
    /// </summary>
    private float ResolveReleaseBaseMovementSpeed()
    {
        if (kcc != null &&
            kcc.Data.KinematicSpeed > 0.0001f)
        {
            return kcc.Data.KinematicSpeed;
        }

        return Mathf.Max(
            0f,
            releaseBaseMovementSpeedFallback
        );
    }

    /// <summary>
    /// Momentum 已經下降到普通移動速度。
    ///
    /// 把水平速度從 DynamicVelocity 交接到 KinematicVelocity，
    /// 避免下一 Tick 恢復普通 WASD 時突然歸零，
    /// 也避免 Dynamic 20 + Kinematic 20 變成 40。
    /// </summary>
    private void CompleteReleaseMomentumToBaseSpeed()
    {
        Vector3 handoffDirection =
            ReleaseMomentumDirection;

        handoffDirection.y =
            0f;

        if (handoffDirection.sqrMagnitude <=
            0.0001f)
        {
            handoffDirection =
                movement.GetHorizontalLookDirection();
        }

        handoffDirection.Normalize();

        float handoffSpeed =
            Mathf.Max(
                0f,
                ReleaseMomentumBaseSpeed
            );

        Vector3 currentDynamicVelocity =
            kcc.Data.DynamicVelocity;

        Vector3 verticalDynamicVelocity =
            Vector3.Project(
                currentDynamicVelocity,
                Vector3.up
            );

        /*
        * 清掉 Dynamic 的水平分量，
        * 防止交接後和普通 Kinematic 移動相加。
        */
        kcc.SetDynamicVelocity(
            verticalDynamicVelocity
        );

        /*
        * 以普通速度建立一個 Kinematic 交接值。
        * 下一 Tick PlayerMovement 會恢復正常 WASD 控制。
        */
        kcc.SetKinematicVelocity(
            handoffDirection *
            handoffSpeed
        );

        ResetReleaseMomentumState();

        if (debugMomentum)
        {
            Debug.Log(
                $"[勾索 Momentum] 已衰退回普通 KCC 速度。" +
                $"\n交接速度：{handoffSpeed:F2}" +
                $"\n交接方向：{handoffDirection}",
                this
            );
        }
    }

    private bool HasReleaseObstacleAhead()
    {
        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() == false)
            return false;

        Vector3 direction =
            ReleaseMomentumDirection.normalized;

        Vector3 origin =
            kcc.Data.TargetPosition +
            Vector3.up *
            rayOriginHeight *
            0.5f;

        float checkDistance =
            ReleaseMomentumSpeed *
            Runner.DeltaTime +
            releaseObstacleCheckSkin;

        bool hasHit =
            physicsScene.SphereCast(
                origin,
                releaseObstacleCheckRadius,
                direction,
                out RaycastHit hit,
                checkDistance,
                releaseObstacleMask,
                QueryTriggerInteraction.Ignore
            );

        if (hasHit == false)
            return false;

        NetworkObject hitNetworkObject =
            hit.collider
                .GetComponentInParent<NetworkObject>();

        if (hitNetworkObject == Object)
            return false;

        if (debugMomentum)
        {
            Debug.Log(
                $"[勾索 Momentum] 前方障礙物。" +
                $"\n物件：{hit.collider.name}" +
                $"\n距離：{hit.distance:F2}",
                hit.collider
            );
        }

        return true;
    }

    /// <summary>
    /// 只重置 Release Momentum 的 Fusion 狀態，
    /// 不自行修改任何 KCC Velocity。
    /// </summary>
    private void ResetReleaseMomentumState()
    {
        ReleaseMomentumActive =
            false;

        ReleaseMomentumDirection =
            default;

        ReleaseMomentumSpeed =
            0f;

        ReleaseMomentumBaseSpeed =
            0f;
    }

    private void StopReleaseMomentum(
        bool clearHorizontalVelocity
    )
    {
        bool wasActive =
            ReleaseMomentumActive;

        if (clearHorizontalVelocity &&
            kcc != null)
        {
            Vector3 currentVelocity =
                kcc.Data.DynamicVelocity;

            Vector3 verticalVelocity =
                Vector3.Project(
                    currentVelocity,
                    Vector3.up
                );

            kcc.SetDynamicVelocity(
                verticalVelocity
            );
        }

        ResetReleaseMomentumState();

        if (wasActive &&
            debugMomentum)
        {
            Debug.Log(
                $"[勾索 Momentum] 停止。" +
                $"\n清除水平速度：{clearHorizontalVelocity}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 特殊能力接口

    /// <summary>
    /// 特殊能力取消目前勾索。
    ///
    /// 不會觸發 ManualToggle Momentum。
    /// </summary>
    public void CancelFromSpecialAbility(
        bool playRetractAnimation = false,
        bool clearExistingMomentum = true
    )
    {
        if (clearExistingMomentum)
        {
            StopReleaseMomentum(
                true
            );
        }

        if (CurrentPhase ==
            GrapplePhase.Idle)
        {
            return;
        }

        /*
        * Support Interaction Tether
        * 沒有真正 Grapple Player Movement，
        * 所以取消時不能通知狀態機：
        *
        * Grapple Ended
        * → GrappleAirborne。
        */
        if (SupportInteractionTetherActive ==
            false)
        {
            stateMachine.NotifyGrappleEnded(
                kcc.Data.IsGrounded
            );
        }

        if (playRetractAnimation)
        {
            BeginRetract(
                GrappleCancelReason.SpecialAbility,
                false
            );
        }
        else
        {
            CompleteRetract();
        }
    }

    /// <summary>
    /// 特殊能力只取消 Momentum。
    /// </summary>
    public void CancelReleaseMomentumFromSpecialAbility(
        bool clearHorizontalVelocity = true
    )
    {
        StopReleaseMomentum(
            clearHorizontalVelocity
        );
    }

    #endregion

    // =====================================================================
    #region 繩索時序資料

    /// <summary>
    /// 取得目前繩索伸出比例。
    ///
    /// PlayerGrappleVisual 只讀取這個結果，
/// 不需要自己理解 TickTimer。
    /// </summary>
    private float GetCurrentRopeExtension()
    {
        switch (CurrentPhase)
        {
            case GrapplePhase.PreFire:
            {
                return 0f;
            }

            case GrapplePhase.Shooting:
            {
                float progress =
                    GetTimerProgress(
                        CurrentShootDuration
                    );

                float eased =
                    ropeShootEase != null
                        ? ropeShootEase.Evaluate(
                            progress
                        )
                        : progress;

                return Mathf.Clamp01(
                    eased
                );
            }

            case GrapplePhase.Attached:
            {
                return 1f;
            }

            case GrapplePhase.Retracting:
            {
                float progress =
                    GetTimerProgress(
                        CurrentRetractDuration
                    );

                float eased =
                    ropeRetractEase != null
                        ? ropeRetractEase.Evaluate(
                            progress
                        )
                        : progress;

                eased =
                    Mathf.Clamp01(
                        eased
                    );

                return ExtensionAtRetractStart *
                       (1f - eased);
            }

            default:
            {
                return 0f;
            }
        }
    }

    private float GetTimerProgress(
        float totalDuration
    )
    {
        if (totalDuration <= 0f)
            return 1f;

        float remaining =
            PhaseTimer
                .RemainingTime(Runner) ?? 0f;

        return 1f -
               Mathf.Clamp01(
                   remaining /
                   totalDuration
               );
    }

    private float CalculateDistanceBasedDuration(
        float distance,
        float maxDistanceDuration,
        float minimumDuration
    )
    {
        if (maxDistanceDuration <= 0f)
            return 0f;

        float ratio =
            grappleDistance > 0.0001f
                ? Mathf.Clamp01(
                    distance /
                    grappleDistance
                )
                : 1f;

        float calculated =
            maxDistanceDuration *
            ratio;

        float validMinimum =
            Mathf.Clamp(
                minimumDuration,
                0f,
                maxDistanceDuration
            );

        return Mathf.Clamp(
            calculated,
            validMinimum,
            maxDistanceDuration
        );
    }

    /// <summary>
    /// 模擬層使用的繩索起點。
    ///
    /// 真正 LineRenderer 的 RopeOrigin
    /// 已經交給 PlayerGrappleVisual。
    ///
    /// 這裡只拿來估算射出與收回時間。
    /// </summary>
    private Vector3 GetSimulationRopeStartPosition()
    {
        if (movement.CamTarget != null)
        {
            return movement.CamTarget.position;
        }

        return kcc.Data.TargetPosition +
               Vector3.up *
               rayOriginHeight;
    }

    #endregion
}