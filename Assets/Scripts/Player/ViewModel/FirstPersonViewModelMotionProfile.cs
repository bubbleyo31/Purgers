using UnityEngine;


/// <summary>
/// 第一人稱 ViewModel Motion 的職業調整資料。
///
/// ====================================================================
///
/// 這份 ScriptableObject 不負責真正移動 ViewModel。
///
/// 它只負責描述：
///
/// Attack
/// Tank
/// Support
///
/// 各自應該如何調整共用 Motion 的強度。
///
/// ====================================================================
///
/// 目前第一階段只有：
///
/// Look Sway。
///
/// 未來會繼續增加：
///
/// Idle Motion
/// Walk Bob
/// Run Bob
/// Grappling Motion
/// GrappleAirborne Motion。
///
/// ====================================================================
///
/// 最終概念：
///
/// 共用 Base Motion
/// ×
/// Profession Motion Profile
/// =
/// 這個職業真正看到的第一人稱晃動。
///
/// ====================================================================
///
/// 例如：
///
/// Attack：
///
/// Look Position = 1
/// Look Rotation = 1
///
/// ------------------------------------------------------------
///
/// Tank：
///
/// Look Position = 0.65
/// Look Rotation = 0.65
///
/// ------------------------------------------------------------
///
/// 同一套演算法，
/// Tank 就會顯得更重、更穩。
/// </summary>
[CreateAssetMenu(
    fileName = "ViewModelMotionProfile",
    menuName = "Player/ViewModel Motion/Profile"
)]
public class FirstPersonViewModelMotionProfile :
    ScriptableObject
{
    // =====================================================================
    #region Profession


    [Header("職業")]


    [SerializeField]
    [Tooltip("這份 ViewModel Motion Profile 屬於哪一個玩家職業。Controller 會根據 PlayerProfession.CurrentProfession 自動選擇對應 Profile。")]
    private PlayerProfessionType profession =
        PlayerProfessionType.Attack;


    /// <summary>
    /// 這份 Profile 所屬職業。
    /// </summary>
    public PlayerProfessionType Profession =>
        profession;


    #endregion


    // =====================================================================
    #region Look Sway


    [Header("Look Sway 總倍率")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業的 Look Sway 位移總倍率。1 代表完全使用共用 Base Motion；0.65 代表只保留 65% 的手部位移慣性；0 代表完全沒有 Look Position Sway。Tank 可以先使用 0.65。")]
    private float lookPositionMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業的 Look Sway 旋轉總倍率。1 代表完全使用共用 Base Motion；0.65 代表只保留 65% 的旋轉慣性；0 代表完全沒有 Look Rotation Sway。Tank 可以先使用 0.65。")]
    private float lookRotationMultiplier =
        1f;


    [Header("Look Sway 單軸倍率")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("水平方向 Position Sway 額外倍率。1 代表不修改。這個值會再乘上 Look Position Multiplier。未來如果某個職業希望左右很穩，但上下仍然保留較多晃動，可以單獨降低這個數值。")]
    private float horizontalPositionMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("垂直方向 Position Sway 額外倍率。1 代表不修改。這個值會再乘上 Look Position Multiplier。")]
    private float verticalPositionMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Look Sway Pitch 旋轉額外倍率。1 代表不修改。這個值會再乘上 Look Rotation Multiplier。")]
    private float pitchRotationMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Look Sway Yaw 旋轉額外倍率。1 代表不修改。這個值會再乘上 Look Rotation Multiplier。")]
    private float yawRotationMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Look Sway Roll 旋轉額外倍率。1 代表不修改。這個值會再乘上 Look Rotation Multiplier。例如 Tank 可以讓 Position 與 Yaw 很小，但 Roll 稍微保留，營造重型武器的重量感。")]
    private float rollRotationMultiplier =
        1f;


    #endregion

    // =====================================================================
    #region Locomotion Motion


    [Header("Idle Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業的 Idle 呼吸與微幅漂移總倍率。1 代表完整使用共用 Idle Motion，0.7 代表只保留 70%。")]
    private float idleMotionMultiplier =
        1f;


    [Header("Walk Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業的 Walk Bob 總倍率。1 代表完整使用共用 Walk Motion。Tank 可以降低這個值，讓較重的武器走路時更穩。")]
    private float walkMotionMultiplier =
        1f;


    [Header("Run Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業的 Run Bob 總倍率。1 代表完整使用共用 Run Motion。Tank 可以比 Walk 再低一些，避免重型武器跑步時晃動過大。")]
    private float runMotionMultiplier =
        1f;

    [Header("Air Motion")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業在 Jump、DoubleJump、Airborne 狀態中的垂直慣性總倍率。1 代表完整使用共用 Air Motion；數值越低，空中上下移動時手部越穩。")]
    private float airMotionMultiplier =
        1f;


    [Header("Grapple Air Motion")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業在 Grappling 與 GrappleAirborne 狀態中的垂直慣性總倍率。這個倍率與一般 Jump Air Motion 分開，因此 Attack 可以讓高速勾索更有速度感，而 Tank 可以保持較穩。")]
    private float grappleMotionMultiplier =
        1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業在 Grappling 與 GrappleAirborne 高速移動時，依整體移動速度產生的前後壓迫感倍率。1 代表完整使用共用設定；數值越低，武器在高速飛行時越穩。Tank 可以先使用 0.7。")]
    private float grappleForwardMotionMultiplier =
        1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業在 Grappling 與 GrappleAirborne 高速轉向時，ViewModel 左右位移與 Roll 側傾的倍率。1 代表完整使用共用設定。Tank 可以先使用 0.7。")]
    private float grappleTurnMotionMultiplier =
        1f;

    [Header("Landing Motion")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("這個職業落地瞬間的 Landing Compression 總倍率。1 代表完整使用共用 Landing Motion；Tank 可以降低這個數值避免重型 ViewModel 落地時甩動過大。")]
    private float landingMotionMultiplier =
        1f;

    #endregion

    // =====================================================================
    #region Public Look Sway Data


    public float LookPositionMultiplier =>
        lookPositionMultiplier;


    public float LookRotationMultiplier =>
        lookRotationMultiplier;


    public float HorizontalPositionMultiplier =>
        horizontalPositionMultiplier;


    public float VerticalPositionMultiplier =>
        verticalPositionMultiplier;


    public float PitchRotationMultiplier =>
        pitchRotationMultiplier;


    public float YawRotationMultiplier =>
        yawRotationMultiplier;


    public float RollRotationMultiplier =>
        rollRotationMultiplier;

    public float IdleMotionMultiplier =>
        idleMotionMultiplier;

    public float WalkMotionMultiplier =>
        walkMotionMultiplier;


    public float RunMotionMultiplier =>
        runMotionMultiplier;

    public float AirMotionMultiplier =>
        airMotionMultiplier;

    public float GrappleMotionMultiplier =>
        grappleMotionMultiplier;

    public float GrappleForwardMotionMultiplier =>
        grappleForwardMotionMultiplier;

    public float GrappleTurnMotionMultiplier =>
        grappleTurnMotionMultiplier;

    public float LandingMotionMultiplier =>
        landingMotionMultiplier;

    #endregion
}