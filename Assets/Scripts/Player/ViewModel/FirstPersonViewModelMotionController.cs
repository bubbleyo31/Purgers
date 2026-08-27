using Fusion;
using UnityEngine;


/// <summary>
/// 第一人稱 ViewModel 共用 Motion Controller。
///
/// ====================================================================
///
/// 掛載位置建議：
///
/// WeaponCamera。
///
/// ====================================================================
///
/// 控制目標：
///
/// 你目前 WeaponCamera 底下共用的：
///
/// ViewModelRoot。
///
/// Attack / Tank / Support 的實際手部模型
/// 都應該生成在這個 Root 底下。
///
/// ====================================================================
///
/// 目前第一階段只實作：
///
/// Look Sway。
///
/// 玩家轉動第一人稱視角時：
///
/// 水平 Look
/// → ViewModel 產生左右 Position 慣性
/// → Yaw 慣性
/// → Roll 慣性。
///
/// 垂直 Look
/// → ViewModel 產生上下 Position 慣性
/// → Pitch 慣性。
///
/// ====================================================================
///
/// 這支 Controller 是純 Local Presentation。
///
/// 不使用：
///
/// [Networked]
/// FixedUpdateNetwork
/// RPC。
///
/// 其他玩家不需要知道
/// 你的第一人稱手部正在怎麼晃。
///
/// ====================================================================
///
/// 未來這支 Controller 會繼續組合：
///
/// Base Pose
/// +
/// Look Sway
/// +
/// Idle Motion
/// +
/// Walk Bob
/// +
/// Run Bob
/// +
/// Grapple Motion
/// +
/// GrappleAirborne Motion
/// +
/// Recoil
/// +
/// Landing
///
/// 最後才一次寫入 ViewModelRoot。
///
/// 因此未來不同 Motion 不會互相覆蓋 Transform。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class FirstPersonViewModelMotionController :
    MonoBehaviour
{
    // =====================================================================
    #region Transform References


    [Header("ViewModel Transform")]


    [SerializeField]
    [Tooltip("真正要套用第一人稱 Motion 的共用 ViewModel Root。Attack、Tank、Support 的第一人稱模型都應該位於這個 Transform 底下。請不要直接指定某個職業的武器 Prefab。")]
    private Transform viewModelMotionRoot;


    [SerializeField]
    [Tooltip("用來讀取玩家實際第一人稱觀看旋轉的 Transform。正常情況直接指定 WeaponCamera 自己的 Transform。Look Sway 會比較上一幀與目前的 Rotation，而不是直接讀 Mouse Delta。")]
    private Transform viewRotationSource;


    #endregion


    // =====================================================================
    #region Profession References


    [Header("職業資料")]


    [SerializeField]
    [Tooltip("目前本地玩家的 PlayerProfession。若留空，Controller 會自動尋找具有 Input Authority 的本地 PlayerProfession。未來 PlayerLocalView 也可以直接呼叫 BindOwnerProfession() 明確綁定。")]
    private PlayerProfession ownerProfession;

    /// <summary>
    /// 本地玩家的移動狀態機。
    ///
    /// Locomotion Motion 只相信這裡的正式狀態：
    ///
    /// Idle
    /// Walk
    /// Run。
    ///
    /// 下一階段空中 Motion 也會繼續使用：
    ///
    /// Jump
    /// DoubleJump
    /// Airborne
    /// Grappling
    /// GrappleAirborne。
    /// </summary>
    private PlayerStateMachine
        ownerStateMachine;

    /// <summary>
    /// 本地玩家的共用移動模組。
    ///
    /// Air Motion 會從這裡讀取 KCC：
    ///
    /// DynamicVelocity
    /// KinematicVelocity
    ///
    /// 藉此取得玩家目前真正的垂直運動速度。
    /// </summary>
    private PlayerMovement
        ownerMovement;

    [SerializeField]
    [Tooltip("Attack 職業使用的 ViewModel Motion Profile。")]
    private FirstPersonViewModelMotionProfile
        attackProfile;


    [SerializeField]
    [Tooltip("Tank 職業使用的 ViewModel Motion Profile。Tank 可以把 Look Sway 倍率設得比 Attack 小，讓重型近戰武器更穩。")]
    private FirstPersonViewModelMotionProfile
        tankProfile;


    [SerializeField]
    [Tooltip("Support 職業使用的 ViewModel Motion Profile。")]
    private FirstPersonViewModelMotionProfile
        supportProfile;


    #endregion


    // =====================================================================
    #region Base Look Sway


    [Header("Look Sway 基礎位移")]


    [SerializeField]
    [Tooltip("玩家每轉動 1 度水平視角時，ViewModel 在 Local X 軸產生多少位移慣性。程式會自動讓 ViewModel 往視角轉動的反方向拖動。第一輪建議 0.0015。")]
    private float horizontalPositionPerDegree =
        0.0015f;


    [SerializeField]
    [Tooltip("玩家每轉動 1 度垂直視角時，ViewModel 在 Local Y 軸產生多少位移慣性。第一輪建議 0.0012。")]
    private float verticalPositionPerDegree =
        0.0012f;


    [Header("Look Sway 基礎旋轉")]


    [SerializeField]
    [Tooltip("玩家每轉動 1 度垂直視角時，ViewModel 產生多少 Pitch 慣性旋轉。單位為角度。第一輪建議 0.10。")]
    private float pitchRotationPerDegree =
        0.10f;


    [SerializeField]
    [Tooltip("玩家每轉動 1 度水平視角時，ViewModel 產生多少 Yaw 慣性旋轉。單位為角度。第一輪建議 0.12。")]
    private float yawRotationPerDegree =
        0.12f;


    [SerializeField]
    [Tooltip("玩家每轉動 1 度水平視角時，ViewModel 產生多少 Roll 傾斜。單位為角度。第一輪建議 0.08。")]
    private float rollRotationPerDegree =
        0.08f;


    #endregion

    // =====================================================================
    #region Idle Motion Settings


    [Header("Idle Motion 位移")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家 Idle 時，ViewModel 上下呼吸的最大 Local Y 位移幅度。Idle 應該很小，只用來避免武器完全像黏死在畫面上。第一輪建議 0.004。")]
    private float idleVerticalAmplitude =
        0.004f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家 Idle 時，ViewModel 左右慢速漂移的最大 Local X 位移幅度。第一輪建議 0.002。")]
    private float idleHorizontalAmplitude =
        0.002f;


    [Header("Idle Motion 旋轉")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Idle 呼吸時的 Pitch 最大旋轉角度。第一輪建議 0.2 度。")]
    private float idlePitchAmplitude =
        0.2f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Idle 漂移時的 Roll 最大旋轉角度。第一輪建議 0.15 度。")]
    private float idleRollAmplitude =
        0.15f;


    [Header("Idle Motion 頻率")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Idle 呼吸循環速度。這不是每秒步數，而是 Sin 波的時間倍率。第一輪建議 1.5。")]
    private float idleFrequency =
        1.5f;


    #endregion
    // =====================================================================
    #region Walk Run Bob Settings


    [Header("Walk Bob")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Walk 時左右 Local X 位移幅度。第一輪建議 0.012。")]
    private float walkHorizontalAmplitude =
        0.012f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Walk 時上下 Local Y 位移幅度。第一輪建議 0.010。")]
    private float walkVerticalAmplitude =
        0.010f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Walk 時 Pitch 晃動最大角度。第一輪建議 0.6 度。")]
    private float walkPitchAmplitude =
        0.6f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Walk 時 Roll 左右擺動最大角度。第一輪建議 0.8 度。")]
    private float walkRollAmplitude =
        0.8f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Walk Bob 循環頻率。第一輪建議 7。")]
    private float walkFrequency =
        7f;


    [Header("Run Bob")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Run 時左右 Local X 位移幅度。第一輪建議 0.016。")]
    private float runHorizontalAmplitude =
        0.016f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Run 時上下 Local Y 位移幅度。第一輪建議 0.018。")]
    private float runVerticalAmplitude =
        0.018f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Run 時 Pitch 晃動最大角度。第一輪建議 1 度。")]
    private float runPitchAmplitude =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Run 時 Roll 左右擺動最大角度。第一輪建議 1.3 度。")]
    private float runRollAmplitude =
        1.3f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Run Bob 循環頻率。第一輪建議 10。")]
    private float runFrequency =
        10f;


    [Header("Locomotion Blend")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Idle、Walk、Run 切換時，ViewModel Motion 混合的速度。數值越高切換越快。第一輪建議 10。")]
    private float locomotionBlendSpeed =
        10f;


    #endregion

    // =====================================================================
    #region Air Vertical Motion Settings


    [Header("Air Vertical Sway")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家每 1 單位每秒的垂直速度，轉換成多少 ViewModel Local Y 慣性位移。正向上升會讓手向下沉，負向下降會讓手向上浮。第一輪建議使用 0.003。")]
    private float airVerticalPositionPerSpeed =
        0.003f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("一般 Jump、DoubleJump、Airborne 狀態下，ViewModel 垂直慣性最多允許偏移多少 Local Y 距離。第一輪建議使用 0.045。")]
    private float maximumAirVerticalPosition =
        0.045f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("一般空中垂直 Sway 追向目前垂直速度所對應目標位置的速度。數值越低越有重量與延遲感。第一輪建議使用 8。")]
    private float airVerticalFollowSpeed =
        8f;


    [Header("Air Vertical Rotation")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家垂直速度轉換成 ViewModel Pitch 慣性的比例。上升時武器會稍微向下拖，下降時則往反方向浮動。第一輪建議使用 0.18。")]
    private float airPitchPerSpeed =
        0.18f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("一般空中狀態的 Pitch 慣性最大角度。第一輪建議使用 3 度。")]
    private float maximumAirPitch =
        3f;


    [Header("Grapple Vertical Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grappling 與 GrappleAirborne 狀態額外乘上的垂直位置倍率。這是在職業 Grapple Motion Multiplier 之外的共用倍率。因勾索速度通常高於普通跳躍，第一輪建議使用 1.15。")]
    private float grappleVerticalPositionMultiplier =
        1.15f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grappling 與 GrappleAirborne 狀態額外乘上的 Pitch 慣性倍率。第一輪建議使用 1.15。")]
    private float grappleVerticalRotationMultiplier =
        1.15f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grappling / GrappleAirborne 的 Local Y 最大慣性偏移。因勾索速度較高，可以比一般 Airborne 稍大。第一輪建議使用 0.065。")]
    private float maximumGrappleVerticalPosition =
        0.065f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grappling / GrappleAirborne 的最大 Pitch 慣性角度。第一輪建議使用 4 度。")]
    private float maximumGrapplePitch =
        4f;


    [Header("Air Motion 回正")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("玩家離開空中狀態後，Air Vertical Sway 回到零點的速度。這不是 Landing Compression，單純負責清除空中慣性。第一輪建議使用 10。")]
    private float airReturnSpeed =
        10f;

    #endregion

    // =====================================================================
    #region Landing Motion Settings


    [Header("Landing Compression")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家落地瞬間每 1 單位每秒的下降速度，轉換成多少 ViewModel 向下壓的 Local Y 位移。只有真正從空中狀態落地時才會觸發。第一輪建議使用 0.004。")]
    private float landingPositionPerFallSpeed =
        0.004f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("落地瞬間 ViewModel 最多向下壓多少 Local Y 距離。避免高速 Grapple 落地時手部直接飛出畫面。第一輪建議使用 0.055。")]
    private float maximumLandingPosition =
        0.055f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("落地瞬間依下降速度產生的最大 Pitch 壓縮角度。第一輪建議使用 4 度。")]
    private float maximumLandingPitch =
        4f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("下降速度轉換成 Landing Pitch 的倍率。第一輪建議使用 0.25。")]
    private float landingPitchPerFallSpeed =
        0.25f;


    [Header("Landing 回彈")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Landing Compression 回到零點的速度。數值越低，落地後回彈越慢、重量感越強。第一輪建議使用 9。")]
    private float landingReturnSpeed =
        9f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("最低下降速度門檻。落地前下降速度沒有超過這個值就不觸發明顯 Landing Compression，避免走小坡或小台階也一直震。第一輪建議使用 2。")]
    private float minimumLandingFallSpeed =
        2f;


    #endregion

    // =====================================================================
    #region Grapple Speed Motion Settings


    [Header("Grapple 高速前後 Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家在 Grappling 或 GrappleAirborne 時，每 1 單位每秒的水平與空間速度會讓 ViewModel 往後推多少 Local Z 距離。速度越快，武器越像被慣性往身體方向壓。第一輪建議 0.0012。")]
    private float grappleBackwardPositionPerSpeed =
        0.0012f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 高速前後壓迫感最多允許 ViewModel 在 Local Z 往後偏移多少距離。避免超高速時武器縮進鏡頭。第一輪建議 0.045。")]
    private float maximumGrappleBackwardPosition =
        0.045f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 高速移動時，每 1 單位每秒速度產生多少 Pitch 壓迫感。這會讓高速飛行時武器稍微被壓低或抬起。第一輪建議 0.06。")]
    private float grappleSpeedPitchPerSpeed =
        0.06f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 高速移動造成的 Pitch 最大角度。第一輪建議 2.5 度。")]
    private float maximumGrappleSpeedPitch =
        2.5f;


    [Header("Grapple 高速轉向 Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 或 GrappleAirborne 狀態中，玩家視角每秒水平轉動速度轉換成 ViewModel Local X 側向慣性的比例。數值越大，高速甩向左右時武器偏移越明顯。第一輪建議 0.00008。")]
    private float grappleTurnPositionPerAngularSpeed =
        0.00008f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 高速轉向造成的最大 Local X 側向偏移。第一輪建議 0.025。")]
    private float maximumGrappleTurnPosition =
        0.025f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 高速水平轉向時，視角角速度轉換成 ViewModel Roll 的比例。第一輪建議 0.012。")]
    private float grappleTurnRollPerAngularSpeed =
        0.012f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Grapple 高速轉向時的最大 Roll 角度。第一輪建議 4 度。")]
    private float maximumGrappleTurnRoll =
        4f;


    [Header("Grapple Speed Motion 平滑")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Grapple Speed Motion 追向目前速度所對應目標 Offset 的速度。數值越低越有重量與延遲感。第一輪建議 7。")]
    private float grappleSpeedMotionFollowSpeed =
        7f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("離開 Grappling / GrappleAirborne 後，Grapple Speed Motion 回到零點的速度。第一輪建議 8。")]
    private float grappleSpeedMotionReturnSpeed =
        8f;


    #endregion

    // =====================================================================
    #region Look Sway Limits


    [Header("Look Sway 位移上限")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Look Sway 在 Local X 左右方向最多允許偏移多少距離。這可以防止玩家使用非常高的滑鼠靈敏度時把手部甩出畫面。第一輪建議 0.035。")]
    private float maximumHorizontalPosition =
        0.035f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Look Sway 在 Local Y 上下方向最多允許偏移多少距離。第一輪建議 0.025。")]
    private float maximumVerticalPosition =
        0.025f;


    [Header("Look Sway 旋轉上限")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("ViewModel Look Sway Pitch 最大旋轉角度。第一輪建議 4 度。")]
    private float maximumPitchRotation =
        4f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("ViewModel Look Sway Yaw 最大旋轉角度。第一輪建議 4 度。")]
    private float maximumYawRotation =
        4f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("ViewModel Look Sway Roll 最大旋轉角度。第一輪建議 3 度。")]
    private float maximumRollRotation =
        3f;


    #endregion


    // =====================================================================
    #region Smoothing


    [Header("Look Sway 平滑")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("玩家正在轉動視角時，ViewModel 追向目標 Sway Offset 的速度。數值越高越緊跟鏡頭，數值越低慣性感越重。第一輪建議 14。")]
    private float swayFollowSpeed =
        14f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("玩家停止轉動視角後，ViewModel 回到原始位置的速度。數值越低回正越慢，武器重量感越強。第一輪建議 10。")]
    private float swayReturnSpeed =
        10f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("單幀 Look Delta 小於這個角度時視為沒有有效視角移動，避免浮點誤差讓 ViewModel 永遠產生極小抖動。第一輪建議 0.001。")]
    private float angularDeadZone =
        0.001f;


    [SerializeField]
    [Min(1f)]
    [Tooltip("單幀最大允許使用多少度的 View Rotation Delta。這主要用來排除切場景、Camera Teleport、Alt Tab 等造成的異常大 Rotation Jump。第一輪建議 45 度。")]
    private float maximumAngularDeltaPerFrame =
        45f;


    #endregion


    // =====================================================================
    #region Profession Blend


    [Header("職業 Motion 切換")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("玩家切換 Attack、Tank、Support 時，ViewModel Motion 倍率過渡速度。數值越高切換越快。第一輪建議 12。")]
    private float professionBlendSpeed =
        12f;


    #endregion


    // =====================================================================
    #region Local Player Search


    [Header("本地玩家自動搜尋")]


    [SerializeField]
    [Min(0.05f)]
    [Tooltip("Owner Profession 尚未綁定時，每隔多少秒重新搜尋一次具有 Input Authority 的本地 PlayerProfession。這只在尚未找到 Owner 時執行，不會每幀掃描場景。")]
    private float ownerSearchInterval =
        0.5f;


    #endregion

    // =====================================================================
    #region ADS Motion Layer Suspension


    [Header("ADS Motion Layer 暫停")]


    [SerializeField]
    [Tooltip("開啟後，Attack 或 Support 按住 Aim 時，會暫停 Look Sway、Idle、Walk、Run、Air、Landing 與 Grapple 等程序化 Motion Layer，並將殘留 Offset 清回 Base Pose。FPSViewModelsProfile 的倍率資料不會被修改；放開 Aim 後 Motion Layer 會重新開始更新。")]
    private bool suspendMotionLayersWhileAiming =
        true;


    /// <summary>
    /// 本地 ViewModel Motion Layer 是否正因 ADS 暫停。
    ///
    /// 只用來偵測進入與離開 ADS 的邊緣，
    /// 避免每幀重複 Reset Runtime。
    /// </summary>
    private bool motionLayersSuspendedForAim;


    /// <summary>
    /// 目前是否因 Aim 暫停程序化 Motion Layer。
    /// 純本地 Presentation Debug 資料。
    /// </summary>
    public bool AreMotionLayersSuspendedForAim =>
        motionLayersSuspendedForAim;


    #endregion

    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後，找到本地 PlayerProfession、職業 Profile 切換或重要引用缺失時會輸出 Debug。調整手感完成後可以關閉。")]
    private bool debugViewModelMotion =
        false;


    #endregion


    // =====================================================================
    #region Base Pose


    /// <summary>
    /// ViewModelMotionRoot 原始 Local Position。
    ///
    /// Motion 永遠在這個 Pose 上加 Offset，
    /// 不會假設 Root 一定是 Vector3.zero。
    /// </summary>
    private Vector3 baseLocalPosition;


    /// <summary>
    /// ViewModelMotionRoot 原始 Local Rotation。
    /// </summary>
    private Quaternion baseLocalRotation;


    #endregion


    // =====================================================================
    #region Look Runtime


    /// <summary>
    /// 上一幀 View Rotation。
    /// </summary>
    private Vector3 previousViewEuler;


    /// <summary>
    /// 是否已經取得第一幀 Rotation。
    ///
    /// 避免 Enable 的第一幀
    /// 把 0 → Camera Rotation
    /// 當成超大 Sway。
    /// </summary>
    private bool hasPreviousViewRotation;


    /// <summary>
    /// 目前真正套用中的 Look Position Offset。
    /// </summary>
    private Vector3 currentLookPositionOffset;


    /// <summary>
    /// 目前真正套用中的 Look Rotation Offset。
    ///
    /// X = Pitch
    /// Y = Yaw
    /// Z = Roll。
    /// </summary>
    private Vector3 currentLookRotationOffset;

    /// <summary>
    /// 本幀真正的水平視角角速度，
    /// 單位為 Degree / Second。
    ///
    /// Grapple 高速轉向 Motion
    /// 會使用這個資料。
    /// </summary>
    private float currentLookAngularVelocityY;


    #endregion


    // =====================================================================
    #region Future Motion Layers


    /*
     * ★ 目前先保留 Motion Composition 位置。
     *
     * 這一輪不實作這些系統。
     *
     * 下一階段：
     *
     * locomotionPositionOffset
     * locomotionRotationOffset
     *
     * 會負責：
     *
     * Idle
     * Walk
     * Run。
     *
     * ------------------------------------------------------------
     *
     * 再下一階段：
     *
     * grapplePositionOffset
     * grappleRotationOffset
     *
     * 會負責：
     *
     * Grappling
     * GrappleAirborne。
     *
     * ------------------------------------------------------------
     *
     * 最後全部在 ApplyFinalPose()
     * 統一相加。
     */


    private Vector3 locomotionPositionOffset =
        Vector3.zero;


    private Vector3 locomotionRotationOffset =
        Vector3.zero;

    /// <summary>
    /// 地面 Locomotion Bob 的連續相位。
    ///
    /// Walk → Run 時不會重新從 0 開始，
    /// 避免腳步晃動突然跳相位。
    /// </summary>
    private float locomotionPhase;

    private Vector3 grapplePositionOffset =
        Vector3.zero;


    private Vector3 grappleRotationOffset =
        Vector3.zero;

    /// <summary>
    /// Jump / Airborne / Grapple
    /// 根據真正垂直速度產生的 ViewModel Offset。
    /// </summary>
    private Vector3 airPositionOffset =
        Vector3.zero;


    private Vector3 airRotationOffset =
        Vector3.zero;


    /// <summary>
    /// 真正落地瞬間的額外壓縮 Offset。
    ///
    /// Air Motion 與 Landing Motion 分開，
    /// 所以落地時可以先發生 Compression，
    /// 再各自平滑回正。
    /// </summary>
    private Vector3 landingPositionOffset =
        Vector3.zero;


    private Vector3 landingRotationOffset =
        Vector3.zero;

    /// <summary>
    /// 上一幀是否處於會計算 Air Motion 的狀態。
    ///
    /// 用來判斷：
    ///
    /// 空中
    /// ↓
    /// 地面
    ///
    /// 這一次轉換是否是真正 Landing。
    /// </summary>
    private bool wasInAirMotionState;


    /// <summary>
    /// 玩家仍在空中時記錄到的最後下降速度。
    ///
    /// ------------------------------------------------------------
    ///
    /// 因為玩家真正落地後，
    /// KCC 的 Vertical Velocity 很可能已經變成 0。
    ///
    /// 所以 Landing Compression
    /// 不能等落地後才讀速度。
    ///
    /// 必須保存「落地前最後一幀」的下降速度。
    /// </summary>
    private float lastAirVerticalVelocity;

    #endregion


    // =====================================================================
    #region Profession Runtime Multipliers


    private PlayerProfessionType
        lastProfession =
            PlayerProfessionType.None;


    private float currentLookPositionMultiplier =
        1f;


    private float currentLookRotationMultiplier =
        1f;


    private float currentHorizontalPositionMultiplier =
        1f;


    private float currentVerticalPositionMultiplier =
        1f;


    private float currentPitchRotationMultiplier =
        1f;


    private float currentYawRotationMultiplier =
        1f;


    private float currentRollRotationMultiplier =
        1f;

    private float currentIdleMotionMultiplier =
        1f;

    private float currentWalkMotionMultiplier =
        1f;


    private float currentRunMotionMultiplier =
        1f;

    private float currentAirMotionMultiplier =
        1f;


    private float currentGrappleMotionMultiplier =
        1f;

    private float currentGrappleForwardMotionMultiplier =
        1f;

    private float currentGrappleTurnMotionMultiplier =
        1f;

    private float currentLandingMotionMultiplier =
        1f;
    
    #endregion


    // =====================================================================
    #region Owner Search Runtime


    private float nextOwnerSearchTime;


    #endregion


    // =====================================================================
    #region Unity


    private void Awake()
    {
        // =============================================================
        // Rotation Source 預設
        // =============================================================

        /*
         * 如果這支腳本直接掛在 WeaponCamera，
         * View Rotation Source 可以不拖，
         * 直接使用自己的 Transform。
         */
        if (viewRotationSource == null)
        {
            viewRotationSource =
                transform;
        }


        // =============================================================
        // Capture Base Pose
        // =============================================================

        CaptureBasePose();
    }


    private void OnEnable()
    {
        motionLayersSuspendedForAim =
            false;


        ResetLookRuntime();


        /*
        * 如果 Owner 已經由外部系統綁定，
        * 直接套用當前 Profile。
        */
        RefreshProfessionImmediately();
    }


    private void OnDisable()
    {
        motionLayersSuspendedForAim =
            false;


        /*
        * 關閉 Controller 時，
        * 把 ViewModel Root 還原到基礎 Pose。
        */
        RestoreBasePose();
    }


    /// <summary>
    /// 使用 LateUpdate。
    ///
    /// ------------------------------------------------------------
    ///
    /// 原因：
    ///
    /// Camera / PlayerLocalView
    /// 通常會先更新本幀視角。
    ///
    /// ViewModel Motion 再讀取
    /// 「最後真正得到的 Camera Rotation」。
    ///
    /// ------------------------------------------------------------
    ///
    /// DefaultExecutionOrder(1000)
    /// 也會讓它比一般 MonoBehaviour 更晚執行。
    /// </summary>
    private void LateUpdate()
    {
        if (viewModelMotionRoot == null ||
            viewRotationSource == null)
        {
            return;
        }


        // =============================================================
        // Local Owner
        // =============================================================

        ResolveLocalOwnerIfNeeded();


        // =============================================================
        // Profession Modifier
        // =============================================================

        /*
        * 即使 ADS 正在暫停 Motion Layer，
        * Profile 倍率仍然正常更新。
        *
        * 因此職業切換或 Profile Blend
        * 不會因為瞄準而停止或被改成 0。
        */
        TickProfessionModifiers();


        // =============================================================
        // ADS Motion Layer Suspension
        // =============================================================

        bool shouldSuspendForAim =
            ShouldSuspendMotionLayersForAim();


        if (shouldSuspendForAim)
        {
            if (motionLayersSuspendedForAim ==
                false)
            {
                motionLayersSuspendedForAim =
                    true;


                /*
                * 進入 ADS 時只 Reset 一次。
                *
                * 這會清除：
                *
                * Look
                * Idle / Walk / Run
                * Air
                * Landing
                * Grapple
                *
                * 所有殘留 Position / Rotation Offset，
                * 避免暫停後卡在上一幀 Bob Pose。
                */
                ResetLookRuntime();
            }


            /*
            * Motion Layer 不再 Tick，
            * 但仍把已清零的結果寫回 Base Pose。
            */
            ApplyFinalPose();


            return;
        }


        // =============================================================
        // Exit ADS Suspension
        // =============================================================

        if (motionLayersSuspendedForAim)
        {
            motionLayersSuspendedForAim =
                false;


            /*
            * 放開 Aim 後重新建立 Look Rotation 基準，
            * 避免 ADS 期間累積的 Camera Rotation
            * 在第一幀被當成巨大 Look Sway。
            */
            ResetLookRuntime();
        }


        // =============================================================
        // Look Sway
        // =============================================================

        TickLookSway();


        // =============================================================
        // Ground Locomotion Motion
        // =============================================================

        TickGroundLocomotionMotion();


        // =============================================================
        // Air / Grapple Vertical Motion
        // =============================================================

        TickAirVerticalMotion();


        // =============================================================
        // Landing Compression
        // =============================================================

        TickLandingMotion();


        // =============================================================
        // Grapple Speed / Turn Motion
        // =============================================================

        TickGrappleSpeedMotion();


        // =============================================================
        // Final Composition
        // =============================================================

        ApplyFinalPose();
    }


    #endregion


    // =====================================================================
    #region Public Binding


    /// <summary>
    /// 外部 Local View 系統可以直接綁定
    /// 真正的本地 PlayerProfession。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來如果 PlayerLocalView
    /// 已經知道 Owner Player，
    /// 建議直接呼叫這個方法。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如果完全不呼叫，
    /// Controller 仍然會自己搜尋
    /// HasInputAuthority 的 PlayerProfession。
    /// </summary>
    public void BindOwnerProfession(
        PlayerProfession profession
    )
    {
        ownerProfession =
            profession;
        
        ownerStateMachine =
            null;


        ownerMovement =
            null;


        if (ownerProfession != null)
        {
            ownerStateMachine =
                ownerProfession
                    .GetComponent<PlayerStateMachine>();


            ownerMovement =
                ownerProfession
                    .GetComponent<PlayerMovement>();
        }

        lastProfession =
            PlayerProfessionType.None;


        RefreshProfessionImmediately();


        if (debugViewModelMotion)
        {
            Debug.Log(
                $"[ViewModel Motion] Owner Profession 綁定。" +
                $"\nPlayer：" +
                $"{(ownerProfession != null ? ownerProfession.name : "NULL")}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Base Pose


    /// <summary>
    /// 保存 ViewModel Root 原始 Pose。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個 Pose 就是所有 Motion 的零點。
    /// </summary>
    public void CaptureBasePose()
    {
        if (viewModelMotionRoot == null)
        {
            return;
        }


        baseLocalPosition =
            viewModelMotionRoot.localPosition;


        baseLocalRotation =
            viewModelMotionRoot.localRotation;
    }


    /// <summary>
    /// 還原 ViewModel Root 原始 Pose。
    /// </summary>
    private void RestoreBasePose()
    {
        if (viewModelMotionRoot == null)
        {
            return;
        }


        viewModelMotionRoot.localPosition =
            baseLocalPosition;


        viewModelMotionRoot.localRotation =
            baseLocalRotation;
    }


    #endregion


    // =====================================================================
    #region Local Owner Search


    private void ResolveLocalOwnerIfNeeded()
    {
        // =============================================================
        // 已經有合法 Owner
        // =============================================================

        if (ownerProfession != null)
        {
            NetworkObject ownerObject =
                ownerProfession.Object;


            if (ownerObject != null &&
                ownerObject.IsValid &&
                ownerObject.HasInputAuthority)
            {
                return;
            }


            ownerProfession =
                null;


            ownerStateMachine =
                null;


            ownerMovement =
                null;


            lastProfession =
                PlayerProfessionType.None;
        }


        // =============================================================
        // Search Interval
        // =============================================================

        if (Time.unscaledTime <
            nextOwnerSearchTime)
        {
            return;
        }


        nextOwnerSearchTime =
            Time.unscaledTime +
            ownerSearchInterval;


        PlayerProfession[] professions =
            FindObjectsByType<
                PlayerProfession
            >(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );


        for (int i = 0;
             i < professions.Length;
             i++)
        {
            PlayerProfession candidate =
                professions[i];


            if (candidate == null ||
                candidate.Object == null ||
                candidate.Object.IsValid == false ||
                candidate.Object.HasInputAuthority == false)
            {
                continue;
            }


            ownerProfession =
                candidate;

            ownerStateMachine =
                candidate
                    .GetComponent<PlayerStateMachine>();

            ownerMovement =
                candidate
                    .GetComponent<PlayerMovement>();

            lastProfession =
                PlayerProfessionType.None;

            RefreshProfessionImmediately();


            if (debugViewModelMotion)
            {
                Debug.Log(
                    $"[ViewModel Motion] 自動找到本地 PlayerProfession。" +
                    $"\nPlayer：{candidate.name}" +
                    $"\nProfession：{candidate.CurrentProfession}",
                    this
                );
            }


            return;
        }
    }


    #endregion


    // =====================================================================
    #region Profession Profile


    /// <summary>
    /// 根據職業取得對應 Motion Profile。
    /// </summary>
    private FirstPersonViewModelMotionProfile
        GetProfile(
            PlayerProfessionType profession
        )
    {
        switch (profession)
        {
            case PlayerProfessionType.Attack:
            {
                return
                    attackProfile;
            }


            case PlayerProfessionType.Tank:
            {
                return
                    tankProfile;
            }


            case PlayerProfessionType.Support:
            {
                return
                    supportProfile;
            }


            case PlayerProfessionType.None:
            default:
            {
                return
                    null;
            }
        }
    }


    /// <summary>
    /// 每幀平滑過渡到目前職業 Motion Profile。
    /// </summary>
    private void TickProfessionModifiers()
    {
        PlayerProfessionType profession =
            ownerProfession != null
                ? ownerProfession
                    .CurrentProfession
                : PlayerProfessionType.None;


        if (profession !=
            lastProfession)
        {
            if (debugViewModelMotion)
            {
                Debug.Log(
                    $"[ViewModel Motion] Profession Motion 切換。" +
                    $"\nFrom：{lastProfession}" +
                    $"\nTo：{profession}",
                    this
                );
            }


            lastProfession =
                profession;
        }


        FirstPersonViewModelMotionProfile
            profile =
                GetProfile(
                    profession
                );


        // =============================================================
        // Target Multipliers
        // =============================================================

        float targetPosition =
            profile != null
                ? profile.LookPositionMultiplier
                : 1f;


        float targetRotation =
            profile != null
                ? profile.LookRotationMultiplier
                : 1f;


        float targetHorizontal =
            profile != null
                ? profile.HorizontalPositionMultiplier
                : 1f;


        float targetVertical =
            profile != null
                ? profile.VerticalPositionMultiplier
                : 1f;


        float targetPitch =
            profile != null
                ? profile.PitchRotationMultiplier
                : 1f;


        float targetYaw =
            profile != null
                ? profile.YawRotationMultiplier
                : 1f;


        float targetRoll =
            profile != null
                ? profile.RollRotationMultiplier
                : 1f;

        float targetIdle =
            profile != null
                ? profile.IdleMotionMultiplier
                : 1f;


        float targetWalk =
            profile != null
                ? profile.WalkMotionMultiplier
                : 1f;


        float targetRun =
            profile != null
                ? profile.RunMotionMultiplier
                : 1f;

        float targetAir =
            profile != null
                ? profile.AirMotionMultiplier
                : 1f;


        float targetGrapple =
            profile != null
                ? profile.GrappleMotionMultiplier
                : 1f;

        float targetGrappleForward =
            profile != null
                ? profile.GrappleForwardMotionMultiplier
                : 1f;


        float targetGrappleTurn =
            profile != null
                ? profile.GrappleTurnMotionMultiplier
                : 1f;

        float targetLanding =
            profile != null
                ? profile.LandingMotionMultiplier
                : 1f;

        // =============================================================
        // Smooth Blend
        // =============================================================

        float blend =
            GetExponentialLerpFactor(
                professionBlendSpeed,
                Time.unscaledDeltaTime
            );


        currentLookPositionMultiplier =
            Mathf.Lerp(
                currentLookPositionMultiplier,
                targetPosition,
                blend
            );


        currentLookRotationMultiplier =
            Mathf.Lerp(
                currentLookRotationMultiplier,
                targetRotation,
                blend
            );


        currentHorizontalPositionMultiplier =
            Mathf.Lerp(
                currentHorizontalPositionMultiplier,
                targetHorizontal,
                blend
            );


        currentVerticalPositionMultiplier =
            Mathf.Lerp(
                currentVerticalPositionMultiplier,
                targetVertical,
                blend
            );


        currentPitchRotationMultiplier =
            Mathf.Lerp(
                currentPitchRotationMultiplier,
                targetPitch,
                blend
            );


        currentYawRotationMultiplier =
            Mathf.Lerp(
                currentYawRotationMultiplier,
                targetYaw,
                blend
            );


        currentRollRotationMultiplier =
            Mathf.Lerp(
                currentRollRotationMultiplier,
                targetRoll,
                blend
            );

        currentIdleMotionMultiplier =
            Mathf.Lerp(
                currentIdleMotionMultiplier,
                targetIdle,
                blend
            );


        currentWalkMotionMultiplier =
            Mathf.Lerp(
                currentWalkMotionMultiplier,
                targetWalk,
                blend
            );


        currentRunMotionMultiplier =
            Mathf.Lerp(
                currentRunMotionMultiplier,
                targetRun,
                blend
            );

        currentAirMotionMultiplier =
            Mathf.Lerp(
                currentAirMotionMultiplier,
                targetAir,
                blend
            );


        currentGrappleMotionMultiplier =
            Mathf.Lerp(
                currentGrappleMotionMultiplier,
                targetGrapple,
                blend
            );

        currentGrappleForwardMotionMultiplier =
            Mathf.Lerp(
                currentGrappleForwardMotionMultiplier,
                targetGrappleForward,
                blend
            );


        currentGrappleTurnMotionMultiplier =
            Mathf.Lerp(
                currentGrappleTurnMotionMultiplier,
                targetGrappleTurn,
                blend
            );

        currentLandingMotionMultiplier =
            Mathf.Lerp(
                currentLandingMotionMultiplier,
                targetLanding,
                blend
            );
    }


    /// <summary>
    /// 初次綁定 Player 時立即取得正確 Profile。
    ///
    /// 避免 Spawn 第一幀先使用錯誤職業倍率。
    /// </summary>
    private void RefreshProfessionImmediately()
    {
        PlayerProfessionType profession =
            ownerProfession != null
                ? ownerProfession
                    .CurrentProfession
                : PlayerProfessionType.None;


        lastProfession =
            profession;


        FirstPersonViewModelMotionProfile
            profile =
                GetProfile(
                    profession
                );


        currentLookPositionMultiplier =
            profile != null
                ? profile.LookPositionMultiplier
                : 1f;


        currentLookRotationMultiplier =
            profile != null
                ? profile.LookRotationMultiplier
                : 1f;


        currentHorizontalPositionMultiplier =
            profile != null
                ? profile.HorizontalPositionMultiplier
                : 1f;


        currentVerticalPositionMultiplier =
            profile != null
                ? profile.VerticalPositionMultiplier
                : 1f;


        currentPitchRotationMultiplier =
            profile != null
                ? profile.PitchRotationMultiplier
                : 1f;


        currentYawRotationMultiplier =
            profile != null
                ? profile.YawRotationMultiplier
                : 1f;


        currentRollRotationMultiplier =
            profile != null
                ? profile.RollRotationMultiplier
                : 1f;

        currentIdleMotionMultiplier =
            profile != null
                ? profile.IdleMotionMultiplier
                : 1f;


        currentWalkMotionMultiplier =
            profile != null
                ? profile.WalkMotionMultiplier
                : 1f;


        currentRunMotionMultiplier =
            profile != null
                ? profile.RunMotionMultiplier
                : 1f;

        currentAirMotionMultiplier =
            profile != null
                ? profile.AirMotionMultiplier
                : 1f;


        currentGrappleMotionMultiplier =
            profile != null
                ? profile.GrappleMotionMultiplier
                : 1f;

        currentGrappleForwardMotionMultiplier =
            profile != null
                ? profile.GrappleForwardMotionMultiplier
                : 1f;

        currentGrappleTurnMotionMultiplier =
            profile != null
                ? profile.GrappleTurnMotionMultiplier
                : 1f;

        currentLandingMotionMultiplier =
            profile != null
                ? profile.LandingMotionMultiplier
                : 1f;
    }


    #endregion


    // =====================================================================
    #region Look Sway


    /// <summary>
    /// 計算這一幀的第一人稱視角旋轉差，
    /// 並轉換成 ViewModel 慣性 Offset。
    /// </summary>
    private void TickLookSway()
    {
        Vector3 currentEuler =
            viewRotationSource.eulerAngles;


        // =============================================================
        // First Frame
        // =============================================================

        if (hasPreviousViewRotation == false)
        {
            previousViewEuler =
                currentEuler;


            hasPreviousViewRotation =
                true;


            return;
        }


        // =============================================================
        // Rotation Delta
        // =============================================================

        float pitchDelta =
            Mathf.DeltaAngle(
                previousViewEuler.x,
                currentEuler.x
            );


        float yawDelta =
            Mathf.DeltaAngle(
                previousViewEuler.y,
                currentEuler.y
            );

        currentLookAngularVelocityY =
            yawDelta /
            Mathf.Max(
                Time.unscaledDeltaTime,
                0.0001f
            );

        previousViewEuler =
            currentEuler;


        // =============================================================
        // Clamp Abnormal Jump
        // =============================================================

        pitchDelta =
            Mathf.Clamp(
                pitchDelta,
                -maximumAngularDeltaPerFrame,
                maximumAngularDeltaPerFrame
            );


        yawDelta =
            Mathf.Clamp(
                yawDelta,
                -maximumAngularDeltaPerFrame,
                maximumAngularDeltaPerFrame
            );


        // =============================================================
        // Dead Zone
        // =============================================================

        bool hasLookInput =
            Mathf.Abs(pitchDelta) >
                angularDeadZone ||
            Mathf.Abs(yawDelta) >
                angularDeadZone;

        if (Mathf.Abs(yawDelta) <=
            angularDeadZone)
        {
            currentLookAngularVelocityY =
                0f;
        }


        if (Mathf.Abs(pitchDelta) <=
            angularDeadZone)
        {
            pitchDelta =
                0f;
        }


        if (Mathf.Abs(yawDelta) <=
            angularDeadZone)
        {
            yawDelta =
                0f;
        }


        // =============================================================
        // Target Position Offset
        // =============================================================

        /*
         * 水平：
         *
         * Camera 往右
         * → ViewModel 往左拖。
         */
        float targetPositionX =
            -yawDelta *
            horizontalPositionPerDegree *
            currentLookPositionMultiplier *
            currentHorizontalPositionMultiplier;


        /*
         * 垂直：
         *
         * Camera 往上時，
         * Unity Pitch Delta 通常為負。
         *
         * 因此這裡直接使用 pitchDelta：
         *
         * 往上看
         * → Y 為負
         * → ViewModel 往下沉。
         */
        float targetPositionY =
            pitchDelta *
            verticalPositionPerDegree *
            currentLookPositionMultiplier *
            currentVerticalPositionMultiplier;


        targetPositionX =
            Mathf.Clamp(
                targetPositionX,
                -maximumHorizontalPosition,
                maximumHorizontalPosition
            );


        targetPositionY =
            Mathf.Clamp(
                targetPositionY,
                -maximumVerticalPosition,
                maximumVerticalPosition
            );


        Vector3 targetPositionOffset =
            new Vector3(
                targetPositionX,
                targetPositionY,
                0f
            );


        // =============================================================
        // Target Rotation Offset
        // =============================================================

        /*
         * Pitch：
         *
         * ViewModel 朝 Camera 旋轉反方向拖動。
         */
        float targetPitch =
            -pitchDelta *
            pitchRotationPerDegree *
            currentLookRotationMultiplier *
            currentPitchRotationMultiplier;


        /*
         * Yaw：
         *
         * Camera 右轉
         * → ViewModel 向左拖。
         */
        float targetYaw =
            -yawDelta *
            yawRotationPerDegree *
            currentLookRotationMultiplier *
            currentYawRotationMultiplier;


        /*
         * Roll：
         *
         * 水平甩視角時增加一點側傾。
         *
         * 如果你之後覺得方向相反，
         * 只需要把 Inspector 的
         * Roll Rotation Per Degree
         * 改成負值。
         */
        float targetRoll =
            yawDelta *
            rollRotationPerDegree *
            currentLookRotationMultiplier *
            currentRollRotationMultiplier;


        targetPitch =
            Mathf.Clamp(
                targetPitch,
                -maximumPitchRotation,
                maximumPitchRotation
            );


        targetYaw =
            Mathf.Clamp(
                targetYaw,
                -maximumYawRotation,
                maximumYawRotation
            );


        targetRoll =
            Mathf.Clamp(
                targetRoll,
                -maximumRollRotation,
                maximumRollRotation
            );


        Vector3 targetRotationOffset =
            new Vector3(
                targetPitch,
                targetYaw,
                targetRoll
            );


        // =============================================================
        // Follow / Return
        // =============================================================

        float smoothingSpeed =
            hasLookInput
                ? swayFollowSpeed
                : swayReturnSpeed;


        float smoothing =
            GetExponentialLerpFactor(
                smoothingSpeed,
                Time.unscaledDeltaTime
            );


        currentLookPositionOffset =
            Vector3.Lerp(
                currentLookPositionOffset,
                targetPositionOffset,
                smoothing
            );


        currentLookRotationOffset =
            Vector3.Lerp(
                currentLookRotationOffset,
                targetRotationOffset,
                smoothing
            );
    }


    #endregion


    // =====================================================================
    #region Ground Locomotion Motion


    /// <summary>
    /// 根據 PlayerStateMachine 的正式狀態
    /// 計算 Idle / Walk / Run ViewModel Motion。
    ///
    /// ====================================================================
    ///
    /// 這裡不自己推測玩家是不是在走路。
    ///
    /// 完全相信：
    ///
    /// PlayerMovementState.Idle
    /// PlayerMovementState.Walk
    /// PlayerMovementState.Run。
    ///
    /// ====================================================================
    ///
    /// Jump / Grappling / Airborne 等狀態：
    ///
    /// 本階段先讓 Ground Bob 平滑回到 0。
    ///
    /// 下一階段會另外加入專用 Air Motion。
    /// </summary>
    private void TickGroundLocomotionMotion()
    {
        // =============================================================
        // 沒有狀態機
        // =============================================================

        if (ownerStateMachine == null)
        {
            SmoothLocomotionTowardZero();

            return;
        }


        PlayerMovementState state =
            ownerStateMachine.CurrentState;


        Vector3 targetPosition =
            Vector3.zero;


        Vector3 targetRotation =
            Vector3.zero;


        float frequency =
            0f;


        // =============================================================
        // Idle
        // =============================================================

        if (state ==
            PlayerMovementState.Idle)
        {
            float multiplier =
                currentIdleMotionMultiplier;


            /*
            * Idle 使用非常緩慢的兩組不同頻率 Sin。
            *
            * X 與 Y 不完全同步，
            * 避免看起來像機械式上下移動。
            */
            float verticalWave =
                Mathf.Sin(
                    Time.unscaledTime *
                    idleFrequency
                );


            float horizontalWave =
                Mathf.Sin(
                    Time.unscaledTime *
                    idleFrequency *
                    0.55f
                );


            targetPosition =
                new Vector3(
                    horizontalWave *
                        idleHorizontalAmplitude *
                        multiplier,

                    verticalWave *
                        idleVerticalAmplitude *
                        multiplier,

                    0f
                );


            targetRotation =
                new Vector3(
                    verticalWave *
                        idlePitchAmplitude *
                        multiplier,

                    0f,

                    horizontalWave *
                        idleRollAmplitude *
                        multiplier
                );
        }


        // =============================================================
        // Walk / Run
        // =============================================================

        else if (
            state ==
                PlayerMovementState.Walk ||
            state ==
                PlayerMovementState.Run
        )
        {
            bool isRunning =
                state ==
                PlayerMovementState.Run;


            float multiplier =
                isRunning
                    ? currentRunMotionMultiplier
                    : currentWalkMotionMultiplier;


            float horizontalAmplitude =
                isRunning
                    ? runHorizontalAmplitude
                    : walkHorizontalAmplitude;


            float verticalAmplitude =
                isRunning
                    ? runVerticalAmplitude
                    : walkVerticalAmplitude;


            float pitchAmplitude =
                isRunning
                    ? runPitchAmplitude
                    : walkPitchAmplitude;


            float rollAmplitude =
                isRunning
                    ? runRollAmplitude
                    : walkRollAmplitude;


            frequency =
                isRunning
                    ? runFrequency
                    : walkFrequency;


            // =========================================================
            // Phase
            // =========================================================

            /*
            * 只要玩家仍處於 Walk / Run，
            * Bob 相位持續累積。
            *
            * Walk 切 Run 不重新歸零，
            * 所以不會突然跳一下。
            */
            locomotionPhase +=
                Time.unscaledDeltaTime *
                frequency;


            /*
            * 左右：
            *
            * 每個完整循環左右一次。
            */
            float horizontalWave =
                Mathf.Sin(
                    locomotionPhase
                );


            /*
            * 上下：
            *
            * 使用兩倍頻率。
            *
            * 因為走路左右各踩一步，
            * 一個左右循環會有兩次上下震動。
            */
            float verticalWave =
                -Mathf.Abs(
                    Mathf.Cos(
                        locomotionPhase
                    )
                );


            targetPosition =
                new Vector3(
                    horizontalWave *
                        horizontalAmplitude *
                        multiplier,

                    verticalWave *
                        verticalAmplitude *
                        multiplier,

                    0f
                );


            targetRotation =
                new Vector3(
                    verticalWave *
                        pitchAmplitude *
                        multiplier,

                    0f,

                    -horizontalWave *
                        rollAmplitude *
                        multiplier
                );
        }


        // =============================================================
        // 空中 / Grapple
        // =============================================================

        else
        {
            /*
            * 目前先不處理：
            *
            * Jump
            * DoubleJump
            * Airborne
            * Grappling
            * GrappleAirborne。
            *
            * 下一階段會直接利用 Vertical Velocity
            * 接專用 Air Sway。
            */
            targetPosition =
                Vector3.zero;


            targetRotation =
                Vector3.zero;
        }


        // =============================================================
        // Smooth Blend
        // =============================================================

        float blend =
            GetExponentialLerpFactor(
                locomotionBlendSpeed,
                Time.unscaledDeltaTime
            );


        locomotionPositionOffset =
            Vector3.Lerp(
                locomotionPositionOffset,
                targetPosition,
                blend
            );


        locomotionRotationOffset =
            Vector3.Lerp(
                locomotionRotationOffset,
                targetRotation,
                blend
            );
    }


    /// <summary>
    /// 沒有合法 Ground Motion 時
    /// 平滑回到 Ground Motion 零點。
    /// </summary>
    private void SmoothLocomotionTowardZero()
    {
        float blend =
            GetExponentialLerpFactor(
                locomotionBlendSpeed,
                Time.unscaledDeltaTime
            );


        locomotionPositionOffset =
            Vector3.Lerp(
                locomotionPositionOffset,
                Vector3.zero,
                blend
            );


        locomotionRotationOffset =
            Vector3.Lerp(
                locomotionRotationOffset,
                Vector3.zero,
                blend
            );
    }


    #endregion

    // =====================================================================
    #region Air Vertical Motion


    /// <summary>
    /// 處理：
    ///
    /// Jump
    /// DoubleJump
    /// Airborne
    /// Grappling
    /// GrappleAirborne
    ///
    /// 的 ViewModel 垂直慣性。
    ///
    /// ====================================================================
    ///
    /// 核心不是播放固定動畫。
    ///
    /// 而是讀取玩家目前真正的：
    ///
    /// Dynamic Velocity
    /// +
    /// Kinematic Velocity。
    ///
    /// ====================================================================
    ///
    /// 上升：
    ///
    /// Vertical Velocity > 0
    /// ↓
    /// ViewModel 往下沉。
    ///
    /// ------------------------------------------------------------
    ///
    /// 下降：
    ///
    /// Vertical Velocity < 0
    /// ↓
    /// ViewModel 往上浮。
    ///
    /// ------------------------------------------------------------
    ///
    /// 接近最高點：
    ///
    /// Vertical Velocity ≈ 0
    /// ↓
    /// ViewModel 自然回到中央附近。
    /// </summary>
    private void TickAirVerticalMotion()
    {
        // =============================================================
        // Reference
        // =============================================================

        if (ownerStateMachine == null ||
            ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            SmoothAirMotionTowardZero();

            wasInAirMotionState =
                false;

            return;
        }


        PlayerMovementState state =
            ownerStateMachine.CurrentState;


        // =============================================================
        // Air State
        // =============================================================

        bool isNormalAirState =
            state ==
                PlayerMovementState.Jump ||
            state ==
                PlayerMovementState.DoubleJump ||
            state ==
                PlayerMovementState.Airborne;


        bool isGrappleAirState =
            state ==
                PlayerMovementState.Grappling ||
            state ==
                PlayerMovementState.GrappleAirborne;


        bool isAirState =
            isNormalAirState ||
            isGrappleAirState;


        // =============================================================
        // 已經回到 Ground
        // =============================================================

        if (isAirState == false)
        {
            /*
            * 注意：
            *
            * 這裡不要把 lastAirVerticalVelocity 清成 0。
            *
            * TickLandingMotion()
            * 還需要使用它判斷剛才落地有多快。
            */
            SmoothAirMotionTowardZero();

            return;
        }


        // =============================================================
        // 真正 Movement Velocity
        // =============================================================

        /*
        * Advanced KCC 將：
        *
        * Gravity / Jump / External Force
        * 放在 DynamicVelocity。
        *
        * 普通 Movement / Dash 等運動
        * 可以反映在 KinematicVelocity。
        *
        * ------------------------------------------------------------
        *
        * 所以兩者相加後取 Y，
        * 比只看其中一個更適合目前玩家系統。
        */
        Vector3 gameplayVelocity =
            ownerMovement.KCC.Data.DynamicVelocity +
            ownerMovement.KCC.Data.KinematicVelocity;


        float verticalVelocity =
            gameplayVelocity.y;


        // =============================================================
        // 保存落地前速度
        // =============================================================

        lastAirVerticalVelocity =
            verticalVelocity;


        // =============================================================
        // Profession / State Multiplier
        // =============================================================

        float professionMultiplier =
            isGrappleAirState
                ? currentGrappleMotionMultiplier
                : currentAirMotionMultiplier;


        float positionStateMultiplier =
            isGrappleAirState
                ? grappleVerticalPositionMultiplier
                : 1f;


        float rotationStateMultiplier =
            isGrappleAirState
                ? grappleVerticalRotationMultiplier
                : 1f;


        // =============================================================
        // Position Target
        // =============================================================

        /*
        * verticalVelocity > 0
        * = 玩家往上。
        *
        * 所以加負號：
        *
        * 玩家往上
        * → 手向下沉。
        *
        * verticalVelocity < 0
        * = 玩家往下。
        *
        * 負 × 負
        * → 手往上浮。
        */
        float targetY =
            -verticalVelocity *
            airVerticalPositionPerSpeed *
            professionMultiplier *
            positionStateMultiplier;


        float positionLimit =
            isGrappleAirState
                ? maximumGrappleVerticalPosition
                : maximumAirVerticalPosition;


        targetY =
            Mathf.Clamp(
                targetY,
                -positionLimit,
                positionLimit
            );


        Vector3 targetPosition =
            new Vector3(
                0f,
                targetY,
                0f
            );


        // =============================================================
        // Pitch Target
        // =============================================================

        float targetPitch =
            -verticalVelocity *
            airPitchPerSpeed *
            professionMultiplier *
            rotationStateMultiplier;


        float pitchLimit =
            isGrappleAirState
                ? maximumGrapplePitch
                : maximumAirPitch;


        targetPitch =
            Mathf.Clamp(
                targetPitch,
                -pitchLimit,
                pitchLimit
            );


        Vector3 targetRotation =
            new Vector3(
                targetPitch,
                0f,
                0f
            );


        // =============================================================
        // Smooth
        // =============================================================

        float blend =
            GetExponentialLerpFactor(
                airVerticalFollowSpeed,
                Time.unscaledDeltaTime
            );


        airPositionOffset =
            Vector3.Lerp(
                airPositionOffset,
                targetPosition,
                blend
            );


        airRotationOffset =
            Vector3.Lerp(
                airRotationOffset,
                targetRotation,
                blend
            );


        // =============================================================
        // State
        // =============================================================

        wasInAirMotionState =
            true;
    }


    /// <summary>
    /// Air Motion 回到 0。
    /// </summary>
    private void SmoothAirMotionTowardZero()
    {
        float blend =
            GetExponentialLerpFactor(
                airReturnSpeed,
                Time.unscaledDeltaTime
            );


        airPositionOffset =
            Vector3.Lerp(
                airPositionOffset,
                Vector3.zero,
                blend
            );


        airRotationOffset =
            Vector3.Lerp(
                airRotationOffset,
                Vector3.zero,
                blend
            );
    }


    #endregion

    // =====================================================================
    #region Landing Motion


    /// <summary>
    /// 處理真正從空中回到 Ground 的 Landing Compression。
    ///
    /// ====================================================================
    ///
    /// Landing 判斷：
    ///
    /// 上一幀：
    /// Air / Grapple Air
    ///
    /// 這一幀：
    /// Ground State
    ///
    /// ↓
    ///
    /// 使用「落地前最後保存的下降速度」
    /// 計算 Landing 強度。
    /// </summary>
    private void TickLandingMotion()
    {
        if (ownerStateMachine == null)
        {
            SmoothLandingTowardZero();

            return;
        }


        PlayerMovementState state =
            ownerStateMachine.CurrentState;


        bool isCurrentAirState =
            state ==
                PlayerMovementState.Jump ||
            state ==
                PlayerMovementState.DoubleJump ||
            state ==
                PlayerMovementState.Airborne ||
            state ==
                PlayerMovementState.Grappling ||
            state ==
                PlayerMovementState.GrappleAirborne;


        // =============================================================
        // ★ Air → Ground
        // =============================================================

        if (wasInAirMotionState &&
            isCurrentAirState == false)
        {
            /*
            * 只處理下降。
            *
            * lastAirVerticalVelocity：
            *
            * -15
            *
            * ↓
            *
            * fallSpeed = 15。
            */
            float fallSpeed =
                Mathf.Max(
                    0f,
                    -lastAirVerticalVelocity
                );


            // =========================================================
            // Minimum Threshold
            // =========================================================

            if (fallSpeed >=
                minimumLandingFallSpeed)
            {
                float multiplier =
                    currentLandingMotionMultiplier;


                // =====================================================
                // Position
                // =====================================================

                float compressionY =
                    -fallSpeed *
                    landingPositionPerFallSpeed *
                    multiplier;


                compressionY =
                    Mathf.Clamp(
                        compressionY,
                        -maximumLandingPosition,
                        0f
                    );


                landingPositionOffset =
                    new Vector3(
                        0f,
                        compressionY,
                        0f
                    );


                // =====================================================
                // Rotation
                // =====================================================

                float compressionPitch =
                    fallSpeed *
                    landingPitchPerFallSpeed *
                    multiplier;


                compressionPitch =
                    Mathf.Clamp(
                        compressionPitch,
                        0f,
                        maximumLandingPitch
                    );


                landingRotationOffset =
                    new Vector3(
                        compressionPitch,
                        0f,
                        0f
                    );
            }


            /*
            * Landing 事件已處理。
            *
            * 不可以下一幀再觸發一次。
            */
            wasInAirMotionState =
                false;
        }


        // =============================================================
        // Return
        // =============================================================

        SmoothLandingTowardZero();
    }


    /// <summary>
    /// Landing Compression 平滑回正。
    /// </summary>
    private void SmoothLandingTowardZero()
    {
        float blend =
            GetExponentialLerpFactor(
                landingReturnSpeed,
                Time.unscaledDeltaTime
            );


        landingPositionOffset =
            Vector3.Lerp(
                landingPositionOffset,
                Vector3.zero,
                blend
            );


        landingRotationOffset =
            Vector3.Lerp(
                landingRotationOffset,
                Vector3.zero,
                blend
            );
    }


    #endregion

    // =====================================================================
    #region Grapple Speed Motion


    /// <summary>
    /// Grappling / GrappleAirborne 專屬高速 Motion。
    ///
    /// ====================================================================
    ///
    /// 這一層處理：
    ///
    /// 1. 整體速度越快
    ///    → ViewModel 往後沉。
    ///
    /// 2. 高速水平轉向
    ///    → ViewModel 側向拖動。
    ///
    /// 3. 高速水平轉向
    ///    → ViewModel Roll 傾斜。
    ///
    /// ====================================================================
    ///
    /// 注意：
    ///
    /// 垂直速度造成的上下 Sway
    /// 已經由 TickAirVerticalMotion() 處理。
    ///
    /// 所以這裡不重複修改 Local Y。
    /// </summary>
    private void TickGrappleSpeedMotion()
    {
        // =============================================================
        // Reference
        // =============================================================

        if (ownerStateMachine == null ||
            ownerMovement == null ||
            ownerMovement.KCC == null ||
            viewRotationSource == null)
        {
            SmoothGrappleSpeedMotionTowardZero();

            return;
        }


        PlayerMovementState state =
            ownerStateMachine.CurrentState;


        bool isGrappleState =
            state ==
                PlayerMovementState.Grappling ||
            state ==
                PlayerMovementState.GrappleAirborne;


        // =============================================================
        // 非 Grapple
        // =============================================================

        if (isGrappleState == false)
        {
            SmoothGrappleSpeedMotionTowardZero();

            return;
        }


        // =============================================================
        // Gameplay Velocity
        // =============================================================

        Vector3 gameplayVelocity =
            ownerMovement.KCC.Data.DynamicVelocity +
            ownerMovement.KCC.Data.KinematicVelocity;


        float speed =
            gameplayVelocity.magnitude;


        // =============================================================
        // Forward / Backward Motion
        // =============================================================

        /*
        * 玩家速度越快，
        * ViewModel Local Z 越往後。
        *
        * Unity 第一人稱模型一般：
        *
        * +Z = Camera Forward。
        *
        * 所以往後沉使用負 Z。
        */
        float backwardOffset =
            -speed *
            grappleBackwardPositionPerSpeed *
            currentGrappleForwardMotionMultiplier;


        backwardOffset =
            Mathf.Clamp(
                backwardOffset,
                -maximumGrappleBackwardPosition,
                0f
            );


        // =============================================================
        // Speed Pitch
        // =============================================================

        float speedPitch =
            speed *
            grappleSpeedPitchPerSpeed *
            currentGrappleForwardMotionMultiplier;


        speedPitch =
            Mathf.Clamp(
                speedPitch,
                0f,
                maximumGrappleSpeedPitch
            );


        // =============================================================
        // 水平視角角速度
        // =============================================================

        /*
        * Look Sway 已經計算：
        *
        * previousViewEuler
        * → current。
        *
        * 但那個 Delta 是每幀。
        *
        * 這裡需要的是：
        *
        * Degrees Per Second。
        *
        * ------------------------------------------------------------
        *
        * 因此我們額外從：
        *
        * currentLookAngularVelocityY
        *
        * 讀取由 TickLookSway()
        * 保存下來的 Yaw Angular Speed。
        */
        float yawAngularSpeed =
            currentLookAngularVelocityY;


        // =============================================================
        // Turn Position
        // =============================================================

        /*
        * Camera 往右甩：
        *
        * yawAngularSpeed > 0
        *
        * ViewModel 往左拖：
        *
        * Local X < 0。
        */
        float turnPositionX =
            -yawAngularSpeed *
            grappleTurnPositionPerAngularSpeed *
            currentGrappleTurnMotionMultiplier;


        turnPositionX =
            Mathf.Clamp(
                turnPositionX,
                -maximumGrappleTurnPosition,
                maximumGrappleTurnPosition
            );


        // =============================================================
        // Turn Roll
        // =============================================================

        float turnRoll =
            yawAngularSpeed *
            grappleTurnRollPerAngularSpeed *
            currentGrappleTurnMotionMultiplier;


        turnRoll =
            Mathf.Clamp(
                turnRoll,
                -maximumGrappleTurnRoll,
                maximumGrappleTurnRoll
            );


        // =============================================================
        // Target
        // =============================================================

        Vector3 targetPosition =
            new Vector3(
                turnPositionX,
                0f,
                backwardOffset
            );


        Vector3 targetRotation =
            new Vector3(
                speedPitch,
                0f,
                turnRoll
            );


        // =============================================================
        // Smooth
        // =============================================================

        float blend =
            GetExponentialLerpFactor(
                grappleSpeedMotionFollowSpeed,
                Time.unscaledDeltaTime
            );


        grapplePositionOffset =
            Vector3.Lerp(
                grapplePositionOffset,
                targetPosition,
                blend
            );


        grappleRotationOffset =
            Vector3.Lerp(
                grappleRotationOffset,
                targetRotation,
                blend
            );
    }


    /// <summary>
    /// 離開 Grapple 狀態後
    /// 平滑清除高速 Motion。
    /// </summary>
    private void SmoothGrappleSpeedMotionTowardZero()
    {
        float blend =
            GetExponentialLerpFactor(
                grappleSpeedMotionReturnSpeed,
                Time.unscaledDeltaTime
            );


        grapplePositionOffset =
            Vector3.Lerp(
                grapplePositionOffset,
                Vector3.zero,
                blend
            );


        grappleRotationOffset =
            Vector3.Lerp(
                grappleRotationOffset,
                Vector3.zero,
                blend
            );
    }


    #endregion

    // =====================================================================
    #region Final Pose Composition


    /// <summary>
    /// 判斷目前 Attack / Support 是否正按住 Aim，
    /// 並決定是否暫停程序化 Motion Layer。
    ///
    /// 這裡只讀取 FirstPersonViewModelAimAnimator
    /// 已安全保存的本地 Presentation 狀態，
    /// 不直接讀取任何 Fusion Networked Property。
    /// </summary>
    private bool ShouldSuspendMotionLayersForAim()
    {
        if (suspendMotionLayersWhileAiming ==
            false)
        {
            return false;
        }


        PlayerProfessionType profession =
            ownerProfession != null
                ? ownerProfession.CurrentProfession
                : PlayerProfessionType.None;


        bool usesAim =
            profession ==
                PlayerProfessionType.Attack ||
            profession ==
                PlayerProfessionType.Support;


        if (usesAim == false)
        {
            return false;
        }


        ProfessionViewModelManager manager =
            ProfessionViewModelManager.Singleton;


        if (manager == null)
        {
            return false;
        }


        WeaponViewModelReferences references =
            manager.CurrentWeaponReferences;


        if (references == null ||
            references.AimAnimator == null)
        {
            return false;
        }


        return
            references
                .AimAnimator
                .IsAimRequested;
    }

    /// <summary>
    /// 將所有 ViewModel Motion Layer
    /// 統一合成後再一次寫入 Transform。
    ///
    /// ------------------------------------------------------------
    ///
    /// 現在只有 Look Sway。
    ///
    /// 但架構已經保留：
    ///
    /// Look
    /// +
    /// Locomotion
    /// +
    /// Grapple。
    /// </summary>
    private void ApplyFinalPose()
    {
        // =============================================================
        // Motion Layer Composition
        // =============================================================

        Vector3 finalPositionOffset =
            currentLookPositionOffset +
            locomotionPositionOffset +
            grapplePositionOffset +
            airPositionOffset +
            landingPositionOffset;


        Vector3 finalRotationOffset =
            currentLookRotationOffset +
            locomotionRotationOffset +
            grappleRotationOffset +
            airRotationOffset +
            landingRotationOffset;


        // =============================================================
        // Final Pose
        // =============================================================

        viewModelMotionRoot.localPosition =
            baseLocalPosition +
            finalPositionOffset;


        viewModelMotionRoot.localRotation =
            baseLocalRotation *
            Quaternion.Euler(
                finalRotationOffset
            );
    }


    #endregion


    // =====================================================================
    #region Reset


    /// <summary>
    /// 重設 Look Sway Runtime。
    /// </summary>
    private void ResetLookRuntime()
    {
        hasPreviousViewRotation =
            false;


        currentLookPositionOffset =
            Vector3.zero;


        currentLookRotationOffset =
            Vector3.zero;


        locomotionPositionOffset =
            Vector3.zero;


        locomotionRotationOffset =
            Vector3.zero;


        grapplePositionOffset =
            Vector3.zero;


        grappleRotationOffset =
            Vector3.zero;

        locomotionPhase =
            0f;

        airPositionOffset =
            Vector3.zero;

        airRotationOffset =
            Vector3.zero;


        landingPositionOffset =
            Vector3.zero;


        landingRotationOffset =
            Vector3.zero;


        wasInAirMotionState =
            false;


        lastAirVerticalVelocity =
            0f;
        
        currentLookAngularVelocityY =
            0f;
    }


    #endregion


    // =====================================================================
    #region Utility


    /// <summary>
    /// Frame Rate Independent 的平滑比例。
    ///
    /// ------------------------------------------------------------
    ///
    /// 比單純：
///
/// Lerp(current, target, speed * deltaTime)
///
/// 更不容易因 FPS 不同產生不同手感。
    /// </summary>
    private static float GetExponentialLerpFactor(
        float speed,
        float deltaTime
    )
    {
        if (speed <= 0f)
        {
            return 1f;
        }


        return
            1f -
            Mathf.Exp(
                -speed *
                Mathf.Max(
                    0f,
                    deltaTime
                )
            );
    }


    #endregion
}