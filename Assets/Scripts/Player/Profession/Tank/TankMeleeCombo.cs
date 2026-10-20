using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tank 左鍵近戰 Combo 的攻擊段數。
/// </summary>
public enum TankMeleeComboStep : byte
{
    /// <summary>
    /// 尚未攻擊。
    /// </summary>
    None = 0,

    /// <summary>
    /// 第一段輕攻擊。
    /// </summary>
    Light1 = 1,

    /// <summary>
    /// 第二段輕攻擊。
    /// </summary>
    Light2 = 2,

    /// <summary>
    /// 第三段重攻擊。
    /// </summary>
    Heavy = 3
}

/// <summary>
/// Tank 單次近戰攻擊目前階段。
///
/// ------------------------------------------------------------
///
/// 這幾個狀態就是未來動畫系統的 Gameplay 接口。
///
/// Startup
/// → 前搖。
///
/// Active
/// → 真正傷害幀。
///
/// Recovery
/// → 後搖 / 僵直。
///
/// ------------------------------------------------------------
///
/// 不建議未來讓 Animation Event
/// 直接成為 Damage Authority。
///
/// Animator 可以跟著這些 Networked State 播放，
/// 但真正傷害時間仍由 Fusion Gameplay Timer 決定。
/// </summary>
public enum TankMeleeAttackPhase : byte
{
    Idle = 0,

    Startup = 1,

    Active = 2,

    Recovery = 3,

    Preparing = 4,
    PreparedDashing = 5
}

/// <summary>
/// Tank 一段近戰攻擊的所有可調整資料。
///
/// Light1 / Light2 / Heavy
/// 各自保存一份。
/// </summary>
[Serializable]
public class TankMeleeAttackDefinition
{
    [Header("動畫幀時序（逐段配置）")]
    [Tooltip("啟用後，以原始動畫長度、FPS、倍率及指定幀換算時序，並開放按住預備。未啟用保留既有秒數，不改寫舊資產。")]
    public bool UseAnimationFrames;
    public AnimationFrameTiming FrameTiming = new AnimationFrameTiming();
    [Min(0f), Tooltip("按住時停在來源動畫第幾幀，從第 0 幀起算。必須小於或等於出傷幀；放開前不出傷。")]
    public float PreparationFrame;
    [Min(0f), Tooltip("來源動畫出傷幀（第 0 幀起算）。點按照常播到這幀；預備後放開直接跳到這幀，出傷及震動各一次。")]
    public float DamageFrame;
    [Min(0f), Tooltip("來源動畫開始收招的幀，必須不早於出傷幀且不超過總幀數。收招持續到動畫總長結束，之後才開始連段接續計時。")]
    public float RecoveryFrame;
    public bool HasValidFrameTiming => FrameTiming != null && FrameTiming.IsValid &&
        FrameTiming.ContainsFrame(PreparationFrame) && FrameTiming.ContainsFrame(DamageFrame) &&
        FrameTiming.ContainsFrame(RecoveryFrame) && PreparationFrame <= DamageFrame && DamageFrame <= RecoveryFrame;
    public float ResolvedStartup => UseAnimationFrames ? FrameTiming.FrameToSeconds(DamageFrame) : StartupDuration;
    public float ResolvedActive => UseAnimationFrames ? FrameTiming.FrameToSeconds(RecoveryFrame) - ResolvedStartup : ActiveDuration;
    public float ResolvedRecovery => UseAnimationFrames ? FrameTiming.DurationSeconds - FrameTiming.FrameToSeconds(RecoveryFrame) : RecoveryDuration;
    // =====================================================================
    #region 時間

    [Header("攻擊時間")]

    [Min(0f)]
    [Tooltip("這一段攻擊從玩家按下左鍵，到真正進入傷害判定 Active 的前搖時間，單位為秒。未來製作動畫後，這裡應對應動畫真正揮到敵人的時間點。")]
    public float StartupDuration =
        0.12f;

    [Min(0f)]
    [Tooltip("這一段攻擊的 Active 階段維持時間，單位為秒。真正傷害只會在『進入 Active 的瞬間』執行一次，不會在 Active 每個 Tick 重複造成傷害。")]
    public float ActiveDuration =
        0.04f;

    [Min(0f)]
    [Tooltip("這一段攻擊造成傷害後的後搖時間，單位為秒。Recovery 結束前攻擊不能被 F 或 Guard 取消，但 Movement、Look、Grapple 仍然保留。")]
    public float RecoveryDuration =
        0.22f;

    #endregion

    // =====================================================================
    #region 傷害

    [Header("攻擊傷害")]

    [Min(0f)]
    [Tooltip("這一段攻擊對每一個合法目標造成的基礎傷害。這只是目前測試初始值，之後可以再接 Tank 升級、傷害倍率或其他 Runtime Modifier。")]
    public float Damage =
        35f;

    [Min(0)]
    [Tooltip("這一段攻擊最多可以傷害多少個不同目標。設為 0 代表不限制，只要在扇形與距離內且沒有被牆擋住就全部攻擊。")]
    public int MaximumTargets =
        0;

    #endregion

    // =====================================================================
    #region 範圍

    [Header("攻擊範圍")]

    [Min(0.1f)]
    [Tooltip("這一段近戰真正允許命中的最大距離。Overlap Sphere 找到的目標如果超過這個距離仍然不會受到傷害。")]
    public float Range =
        2.6f;

    [Min(0.1f)]
    [Tooltip("這一段攻擊用來尋找候選敵人的 Overlap Sphere 半徑。數值越大越容易找到玩家正面左右兩側的敵人，但最後仍會再經過扇形角度與 Range 過濾。")]
    public float SearchRadius =
        1.5f;

    [Range(1f, 360f)]
    [Tooltip("這是完整攻擊扇形角度。Light 設為 120 代表玩家正前方左右各 60 度。Heavy 設為 160 代表左右各 80 度。")]
    public float FullAttackAngle =
        120f;

    #endregion
}

/// <summary>
/// Tank 職業左鍵三段近戰 Combo。
///
/// ====================================================================
///
/// 基本流程：
///
/// Light 1
/// ↓
/// Light 2
/// ↓
/// Heavy
/// ↓
/// Light 1。
///
/// ====================================================================
///
/// 每一段都有（幀模式按住可在 Startup 與 Active 之間停於 Preparing）：
///
/// Startup
/// ↓
/// Active
/// ↓
/// Recovery。
///
/// 真正 Damage
/// 只在進入 Active 的瞬間執行一次。
///
/// ====================================================================
///
/// Combo Reset：
///
/// Light 1 完成
/// ↓
/// 一段時間沒有再攻擊
/// ↓
/// 下一次重新 Light 1。
///
/// ====================================================================
///
/// 攻擊輸入鎖：整刀（含 Recovery）完成前不預存下一刀。
/// 必須回到 Idle 後重新按下左鍵；按住跨過完成時點不自動出手。
///
/// ====================================================================
///
/// Grapple：
///
/// 攻擊僵直期間仍然允許 Q Grapple。
///
/// 這支腳本不會 Block：
///
/// Movement
/// Look
/// Grapple。
///
/// ====================================================================
///
/// 未來 Tank Grapple 聚怪成功後可以呼叫：
///
/// ForceNextHeavyAttack()
///
/// 讓三秒內下一次左鍵直接 Heavy。
/// </summary>
[DisallowMultipleComponent]
public partial class TankMeleeCombo :
    NetworkBehaviour,
    ICombatDamageFeedbackSource
{
    // =====================================================================
    #region Owner Player

    [Header("擁有者玩家綁定")]

    [SerializeField]
    [Tooltip("這個 Tank Melee Combo 真正所屬的 Player Core。正常情況不需要手動指定，TankProfessionRuntimeDriver 會在 Runtime Spawn 後自動綁定。")]
    private Player ownerPlayer;

    private PlayerMovement
        ownerMovement;

    /// <summary>
    /// Owner Player 上的本地戰鬥回饋 Relay。
    ///
    /// ------------------------------------------------------------
    ///
    /// Tank Melee 不會直接知道：
    ///
    /// Camera
    /// Camera Shake
    /// Hit Marker。
    ///
    /// ------------------------------------------------------------
    ///
    /// 它只告訴 PlayerCombatFeedbackRelay：
    ///
    /// 「這一次 Tank 攻擊真的揮出去了。」
    ///
    /// Presentation 再自己決定要播放什麼。
    /// </summary>
    private PlayerCombatFeedbackRelay
        ownerCombatFeedbackRelay;

    private NetworkObject
        ownerPlayerNetworkObject;

    /// <summary>
    /// Player Network Prefab Root 上唯一的世界聲音發送器。
    ///
    /// TankMeleeCombo 位於 Tank Profession Runtime，
    /// 不可在 Runtime 再掛第二顆 NetworkPlayerAudioEmitter。
    /// </summary>
    private NetworkPlayerAudioEmitter
        networkAudioEmitter;

    /// <summary>
    /// 同一個 Tank Runtime 上的 F Quick Dash。
    /// 普通攻擊用它確認 Dash 與 Dash 後攻防僵直。
    /// </summary>
    private TankQuickDashAbility
        quickDashAbility;

    /// <summary>
    /// 目前真正 Owner Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    /// <summary>
    /// Runtime Driver 綁定真正 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        ownerMovement =
            null;

        ownerPlayerNetworkObject =
            null;

        ownerCombatFeedbackRelay =
            null;

        networkAudioEmitter =
            null;

        quickDashAbility =
            null;
    
        if (ownerPlayer == null)
        {
            return;
        }

        quickDashAbility = GetComponent<TankQuickDashAbility>();

        if (quickDashAbility == null)
        {
            Debug.LogError(
                $"[{nameof(TankMeleeCombo)}] " +
                $"Tank Runtime 找不到 " +
                $"{nameof(TankQuickDashAbility)}，" +
                $"普通攻擊無法套用 Dash 後攻防僵直。",
                this
            );
        }

        ownerMovement =
            ownerPlayer.Movement;

        ownerPlayerNetworkObject =
            ownerPlayer.Object;
        
        networkAudioEmitter =
            ownerPlayer.GetComponent<
                NetworkPlayerAudioEmitter
            >();

        /*
        * 取得 Player Core 上的 Combat Feedback Relay。
        *
        * TankProfessionRuntime 本身不直接管理
        * 第一人稱 Camera Shake。
        */
        ownerCombatFeedbackRelay =
            ownerPlayer
                .GetComponent<
                    PlayerCombatFeedbackRelay
                >();

        if (ownerCombatFeedbackRelay == null)
        {
            Debug.LogError(
                $"[{nameof(TankMeleeCombo)}] " +
                $"Owner Player 找不到 " +
                $"{nameof(PlayerCombatFeedbackRelay)}。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }

        if (ownerMovement == null)
        {
            Debug.LogError(
                $"[{nameof(TankMeleeCombo)}] " +
                $"Owner Player 找不到 PlayerMovement。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }

        if (networkAudioEmitter == null)
        {
            Debug.LogError(
                $"[{nameof(TankMeleeCombo)}] " +
                $"Owner Player Root 找不到 " +
                $"{nameof(NetworkPlayerAudioEmitter)}，" +
                $"Tank Melee 仍可造成傷害，但其他玩家聽不到世界揮擊聲。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }

    #endregion

    // =====================================================================
    #region Combo 設定

    [Header("坦克 三段 連段")]

    [SerializeField]
    [Tooltip("第一段輕攻擊設定。建議 Full Attack Angle 初始設為 120，也就是玩家左右各 60 度。")]
    private TankMeleeAttackDefinition
        lightAttack1 =
            new TankMeleeAttackDefinition
            {
                StartupDuration = 0.12f,
                ActiveDuration = 0.04f,
                RecoveryDuration = 0.22f,
                Damage = 35f,
                MaximumTargets = 0,
                Range = 2.6f,
                SearchRadius = 1.5f,
                FullAttackAngle = 120f
            };

    [SerializeField]
    [Tooltip("第二段輕攻擊設定。數值目前可以與第一段相同，但獨立保存是為了之後 Light1 與 Light2 有不同動畫、前搖、後搖時不需要修改架構。")]
    private TankMeleeAttackDefinition
        lightAttack2 =
            new TankMeleeAttackDefinition
            {
                StartupDuration = 0.12f,
                ActiveDuration = 0.04f,
                RecoveryDuration = 0.22f,
                Damage = 35f,
                MaximumTargets = 0,
                Range = 2.6f,
                SearchRadius = 1.5f,
                FullAttackAngle = 120f
            };

    [SerializeField]
    [Tooltip("第三段重攻擊設定。建議 Full Attack Angle 初始設為 160，也就是玩家左右各 80 度。Damage、Range 與 Search Radius 可以明顯高於 Light。")]
    private TankMeleeAttackDefinition
        heavyAttack =
            new TankMeleeAttackDefinition
            {
                StartupDuration = 0.22f,
                ActiveDuration = 0.05f,
                RecoveryDuration = 0.38f,
                Damage = 70f,
                MaximumTargets = 0,
                Range = 3.1f,
                SearchRadius = 1.8f,
                FullAttackAngle = 160f
            };

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("上一段攻擊完全結束後，玩家最多可以等待多久再接下一段 Combo。超過這個時間沒有左鍵輸入，就會把 Combo 重置回 Light1。建議先從 0.8 秒測試。")]
    private float comboResetDuration =
        0.8f;

    // 僅保留舊 Prefab 序列化；現在固定於整刀完成後才接受新按鍵，不再啟用輸入緩衝。
#pragma warning disable CS0414
    [SerializeField, HideInInspector]
    private bool allowAttackInputBuffer = true;
#pragma warning restore CS0414

    #endregion

    // =====================================================================
    #region 世界攻擊聲

    [Header("坦克 三段世界揮擊聲")]

    [SerializeField]
    [Tooltip(
        "Tank 第一段普通攻擊進入 Gameplay Active 的瞬間，由 State Authority 傳給所有 Client 的 3D 世界揮擊聲。\n\n" +
        "必須指定 Network ID 大於 0、且已加入 GameplayAudioCatalog 的 Cue。")]
    private GameplayAudioCue
        light1WorldSwingCue;

    [SerializeField]
    [Tooltip(
        "Tank 第二段普通攻擊進入 Gameplay Active 的瞬間播放的 3D 世界揮擊聲。\n\n" +
        "如果第一、二段暫時使用同一個音檔，也可以讓兩個欄位引用同一個 Cue；未來有獨立聲音時不需要改程式。")]
    private GameplayAudioCue
        light2WorldSwingCue;

    [SerializeField]
    [Tooltip(
        "Tank 第三段 Heavy 進入 Gameplay Active 的瞬間播放的 3D 世界重揮聲。\n\n" +
        "這是攻擊動作聲，不是 Headshot Hit Feedback；即使 Heavy 揮空仍然會播放。")]
    private GameplayAudioCue
        heavyWorldSwingCue;

    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Tank 三段世界揮擊 Cue 共用的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0 = 靜音。")]
    private float worldSwingVolumeScale =
        1f;

    #endregion

    // =====================================================================
    #region 強制 Heavy 預留

    [Header("勾索強制重擊預留")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank 未來勾索聚怪成功後，下一次左鍵可以直接變成 Heavy 的有效時間。依目前設計預設為 3 秒。")]
    private float defaultForcedHeavyDuration =
        3f;

    #endregion

    // =====================================================================
    #region 共用攻擊空間

    [Header("近戰攻擊空間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("從 Player KCC TargetPosition 向上增加多少高度作為 Tank 近戰判定起點。不要引用只有本地玩家存在的 ViewModel Transform，因為真正傷害由 State Authority 判定。")]
    private float originHeight =
        1.1f;

    [SerializeField]
    [Tooltip("Tank 近戰可以搜尋哪些 Layer。建議與目前 AttackQuickMelee 使用的敵人 Hitbox Layer 設定一致。")]
    private LayerMask meleeHitMask =
        ~0;

    #endregion

    // =====================================================================
    #region Lag Compensation

    [Header("Photon Fusion 延遲補償")]

    [SerializeField]
    [Tooltip("開啟後使用 SubtickAccuracy。Tank 可以在高速 Grapple 中攻擊，建議目前保持開啟。")]
    private bool useSubtickAccuracy =
        true;

    [SerializeField]
    [Tooltip("開啟後除了 Fusion Hitbox，也會包含普通 Unity PhysX Collider。測試階段可以保持與 AttackQuickMelee 相同。")]
    private bool includePhysX =
        true;

    #endregion

    // =====================================================================
    #region LOS

    [Header("近戰障礙物遮擋")]

    [SerializeField]
    [Tooltip("開啟後會在造成 Tank 近戰傷害前檢查玩家與敵人之間是否被牆壁或其他 World Geometry 遮擋。")]
    private bool requireLineOfSight =
        true;

    [SerializeField]
    [Tooltip("哪些 Layer 會阻擋 Tank 近戰。建議只包含 Wall、Ground、WorldGeometry，不要包含 Player 或 Enemy。")]
    private LayerMask obstructionMask =
        0;

    [SerializeField]
    [Min(0f)]
    [Tooltip("LOS Raycast 起點往攻擊方向偏移多少距離，避免射線從玩家碰撞體內部開始。")]
    private float obstructionRayStartOffset =
        0.05f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示 Tank Combo 段數、Phase、Damage、候選敵人數與擊殺數。")]
    private bool debugTankMelee =
        true;

    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出每一次 Tank 近戰的正前方方向。")]
    private bool debugDrawAttack =
        true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank 近戰 Debug Draw 保留時間。")]
    private float debugDrawDuration =
        0.5f;

    #endregion

    // =====================================================================
    #region Network State

    /// <summary>
    /// 目前正在執行哪一段攻擊。
    ///
    /// Idle 時為 None。
    /// </summary>
    [Networked]
    public TankMeleeComboStep CurrentStep
    {
        get;
        private set;
    }

    /// <summary>
    /// 下一次普通左鍵應該從哪一段開始。
    /// </summary>
    [Networked]
    public TankMeleeComboStep NextComboStep
    {
        get;
        private set;
    }

    /// <summary>
    /// 目前攻擊階段。
    /// </summary>
    [Networked]
    public TankMeleeAttackPhase CurrentPhase
    {
        get;
        private set;
    }

    /// <summary>
    /// 目前 Startup / Active / Recovery Timer。
    /// </summary>
    [Networked]
    private TickTimer PhaseTimer
    {
        get;
        set;
    }

    /// <summary>
    /// Combo 允許接下一刀的剩餘時間。
    /// </summary>
    [Networked]
    private TickTimer ComboResetTimer
    {
        get;
        set;
    }

    /// <summary>
    /// Grapple 強制 Heavy 的有效 Timer。
    ///
    /// Timer 有效時：
    ///
    /// 下一次真正開始的左鍵攻擊
    /// 直接變成 Heavy。
    /// </summary>
    [Networked]
    private TickTimer ForcedHeavyTimer
    {
        get;
        set;
    }

    /// <summary>
    /// 玩家這次 Runtime
    /// 已經正式開始過多少次 Tank Melee。
    /// </summary>
    [Networked]
    public int AttackSequence
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Runtime Cache

    private readonly MeleeDamageResolver
        damageResolver =
            new MeleeDamageResolver();

    private readonly List<DamageResult>
        resolvedResults =
            new List<DamageResult>(16);

    private readonly List<DamageResult>
        confirmedResults =
            new List<DamageResult>(16);

    #endregion

    // =====================================================================
    [Networked] public TankMeleePreparationProgress Preparation { get; private set; }
    [Networked] private float SwingSpeedSnapshot { get; set; }
    private float GrowthSwingSpeed => Mathf.Max(1f, SwingSpeedSnapshot);
    [Networked] public int SwingSequence { get; private set; }
    [Networked] public int InterruptedSequence { get; private set; }

    public bool IsPreparing => CurrentPhase == TankMeleeAttackPhase.Preparing;
    public bool CanCancelPreparation => (CurrentPhase == TankMeleeAttackPhase.Startup || IsPreparing) &&
        GetDefinition(CurrentStep) != null && GetDefinition(CurrentStep).UseAnimationFrames && !Preparation.Released;
    public float ButtonHeldSeconds => Preparation.ButtonHeldSeconds;
    public float PreparedSeconds => Preparation.PreparedSeconds;

    // 動畫與 Gameplay 共用同一時間座標；不從 Animator 反推傷害。
    public bool TryGetAnimationPosition(out TankMeleeComboStep step, out float normalizedTime)
    {
        step = CurrentStep;
        normalizedTime = 0f;
        var definition = GetDefinition(step);
        if (definition == null || !definition.UseAnimationFrames || !definition.HasValidFrameTiming) return false;
        float seconds = Preparation.CursorSeconds;
        if (CurrentPhase == TankMeleeAttackPhase.Active)
            seconds = definition.ResolvedStartup + definition.ResolvedActive - (PhaseTimer.RemainingTime(Runner) ?? 0f) * GrowthSwingSpeed;
        else if (CurrentPhase == TankMeleeAttackPhase.Recovery)
            seconds = definition.ResolvedStartup + definition.ResolvedActive + definition.ResolvedRecovery - (PhaseTimer.RemainingTime(Runner) ?? 0f) * GrowthSwingSpeed;
        normalizedTime = Mathf.Clamp01(seconds / definition.FrameTiming.DurationSeconds);
        return true;
    }

    /// <summary>只在已成功消耗 F 充能後呼叫；保留取消的當前段，不啟動連段逾時。</summary>
    public bool CancelPreparationForQuickAction()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !CanCancelPreparation) return false;
        var retainedStep = CurrentStep;
        ResetAttackState();
        NextComboStep = retainedStep;
        InterruptedSequence++;
        return true;
    }

    /// <summary>死亡、換武器、控制封鎖等中斷：清除段數、按住時間、緩衝及強制重擊。</summary>
    public void ResetForInterruption()
    {
        if (Object == null || !Object.IsValid) return;
        if (!Object.HasStateAuthority && !Object.HasInputAuthority) return;
        if (CurrentPhase != TankMeleeAttackPhase.Idle) InterruptedSequence++;
        ResetAttackState();
        ForcedHeavyTimer = TickTimer.None;
    }

    public override void FixedUpdateNetwork()
    {
        // 沒有輸入的死亡 Tick 也必須清理，不能等玩家再次送入按鍵。
        if (ownerPlayer != null && ownerPlayer.Object != null && ownerPlayer.Object.IsValid &&
            ((ownerPlayer.Health != null && !ownerPlayer.Health.IsAlive) || !ownerPlayer.Profession.IsTank))
            ResetForInterruption();
    }

    #region Damage Feedback

    /// <summary>
    /// PlayerCombatFeedbackRelay
    /// 會自動訂閱這個事件。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
///
/// Tank 是 AOE。
///
/// 一刀打 8 個敵人時
/// 不會送 8 次 Camera Shake。
///
/// 每一次攻擊只會挑一個最重要的 DamageResult
/// 發送一次 Feedback。
///
/// Kill
/// 優先於
/// 普通 Hit。
    /// </summary>
    public event Action<DamageResult>
        DamageConfirmed;

    #endregion

    // =====================================================================
    #region 公開狀態

    /// <summary>
    /// Tank 現在是否仍處於
    /// Startup / Active / Recovery。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來：
///
/// Tank F Quick Dash
/// Tank Guard
///
/// 都應該讀這個狀態。
///
/// true 時不允許那些能力取消攻擊。
///
/// Grapple 不讀這個值，
/// 所以 Q 仍然可以正常使用。
    /// </summary>
    public bool IsAttackLocked =>
        CurrentPhase !=
        TankMeleeAttackPhase.Idle;

    /// <summary>
    /// 是否正在真正傷害判定階段。
    /// </summary>
    public bool IsAttackActive =>
        CurrentPhase ==
        TankMeleeAttackPhase.Active;

    /// <summary>
    /// 是否存在 Grapple 強制 Heavy。
    /// </summary>
    public bool HasForcedHeavy
    {
        get
        {
            if (Runner == null)
            {
                return false;
            }

            return
                ForcedHeavyTimer
                    .ExpiredOrNotRunning(
                        Runner
                    ) == false;
        }
    }

    /// <summary>
    /// Combo Reset 剩餘秒數。
    ///
    /// HUD / Debug 可以使用。
    /// </summary>
    public float ComboResetRemainingSeconds =>
        Runner != null
            ? ComboResetTimer
                .RemainingTime(Runner)
                ?? 0f
            : 0f;

    /// <summary>
    /// Grapple 強制 Heavy 剩餘秒數。
    /// </summary>
    public float ForcedHeavyRemainingSeconds =>
        Runner != null
            ? ForcedHeavyTimer
                .RemainingTime(Runner)
                ?? 0f
            : 0f;

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentStep =
                TankMeleeComboStep.None;

            NextComboStep =
                TankMeleeComboStep.Light1;

            CurrentPhase =
                TankMeleeAttackPhase.Idle;

            PhaseTimer =
                TickTimer.None;

            ComboResetTimer =
                TickTimer.None;

            ForcedHeavyTimer =
                TickTimer.None;

            AttackSequence = 0;
            SwingSequence = 0;
            InterruptedSequence = 0;
            Preparation = default;
        }
    }

    #endregion

    // =====================================================================
    #region Simulation

    /// <summary>
    /// 每個 Fusion Tick
    /// 由 TankProfessionRuntimeDriver 呼叫。
    /// </summary>
    public void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        if (Object == null || !Object.IsValid || (!Object.HasStateAuthority && !Object.HasInputAuthority)) return;
        if (ownerPlayer != null && ownerPlayer.Health != null && !ownerPlayer.Health.IsAlive)
        {
            ResetForInterruption();
            return;
        }
        if (IsPreparedDashing && ((input.BlockedControls & Purgers.GameFlow.Control.PlayerControlMask.Movement) != 0 || PreparedDashDisplaced()))
        {
            ResetForInterruption();
            return;
        }
        if (ownerPlayer == null ||
            ownerMovement == null ||
            ownerPlayerNetworkObject == null)
        {
            return;
        }

        // =============================================================
        // 強制 Heavy 過期
        // =============================================================

        if (ForcedHeavyTimer
            .Expired(Runner))
        {
            ForcedHeavyTimer =
                TickTimer.None;
        }

        // =============================================================
        // Combo Reset
        // =============================================================

        if (CurrentPhase ==
                TankMeleeAttackPhase.Idle &&
            ComboResetTimer
                .Expired(Runner))
        {
            ResetComboToLight1();
        }

        // =============================================================
        // 左鍵 Press
        // =============================================================

        bool held = input.Buttons.IsSet(InputButton.Fire);
        bool attackPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Fire
            );

        // =============================================================
        // 現在正在攻擊
        // =============================================================

        if (CurrentPhase !=
            TankMeleeAttackPhase.Idle)
        {
            // 攻擊中的 Press 一律丟棄；仍讀取 Hold／Release 以完成當前預備。
            // 即使本 Tick 收招完成，也只返回 Idle，不能使用本 Tick 的提前按鍵。
            TickAttackPhase(held, allowPreparedDash: (input.BlockedControls & Purgers.GameFlow.Control.PlayerControlMask.Movement) == 0);

            return;
        }

        // =============================================================
        // Quick Dash 位移／Dash 後 Recovery
        // =============================================================

        if (quickDashAbility != null &&
            quickDashAbility.IsCombatActionLocked)
        {
            /*
             * Recovery 期間按下的 Fire 不保存 Buffer。
             * 玩家必須在 Recovery 結束後重新按下左鍵。
             */
            return;
        }

        // =============================================================
        // Idle
        // =============================================================

        if (attackPressed)
        {
            StartNextAttack(held);
        }
    }

    #endregion

    // =====================================================================
    #region Attack Start

    /// <summary>
    /// 根據目前 Combo / Forced Heavy
    /// 開始下一次攻擊。
    /// </summary>
    private void StartNextAttack(bool held = false)
    {
        TankMeleeComboStep step;

        // =============================================================
        // Grapple Forced Heavy
        // =============================================================

        if (HasForcedHeavy)
        {
            step =
                TankMeleeComboStep.Heavy;

            /*
             * 真正使用 Heavy 時才消耗。
             */
            ForcedHeavyTimer =
                TickTimer.None;
        }
        else
        {
            step =
                NextComboStep;
        }

        StartAttack(step, held);
    }

    /// <summary>
    /// 正式開始指定 Combo Step。
    /// </summary>
    private void StartAttack(
        TankMeleeComboStep step, bool held = false
    )
    {
        TankMeleeAttackDefinition definition =
            GetDefinition(
                step
            );

        if (definition == null)
        {
            ResetAttackState();

            return;
        }

        if (definition.UseAnimationFrames && !definition.HasValidFrameTiming)
        {
            Debug.LogError("Tank 動畫幀設定無效：需 0 <= 預備幀 <= 出傷幀 <= 收招幀 <= 原始總幀數，長度／FPS／倍率皆須大於 0。", this);
            ResetAttackState();
            return;
        }
        SwingSpeedSnapshot = Purgers.Progression.PlayerGrowth.MeleeSpeed(ownerPlayer);
        Preparation = TankMeleePreparationProgress.Begin(held);
        AttackSequence++;

        CurrentStep =
            step;

        ComboResetTimer =
            TickTimer.None;

        // =============================================================
        // Startup
        // =============================================================

        CurrentPhase =
            TankMeleeAttackPhase.Startup;

        if (definition.UseAnimationFrames)
        {
            PhaseTimer = TickTimer.None;
            TickAttackPhase(held, 0f);
        }
        else if (definition.ResolvedStartup > 0f)
        {
            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    definition.ResolvedStartup / GrowthSwingSpeed
                );
        }
        else
        {
            EnterActivePhase();
        }

        if (debugTankMelee &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Melee] Attack Started" +
                $"\nStep：{CurrentStep}" +
                $"\nSequence：{AttackSequence}" +
                $"\nStartup：{definition.ResolvedStartup:F3}" +
                $"\nDamage：{definition.Damage:F2}" +
                $"\nAngle：{definition.FullAttackAngle:F1}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Attack Phase

    private void TickAttackPhase(bool held, float deltaTime = -1f, bool allowPreparedDash = true)
    {
        if (IsPreparedDashing) { TickPreparedDash(); return; }
        var definition = GetDefinition(CurrentStep);
        if (definition != null && definition.UseAnimationFrames &&
            (CurrentPhase == TankMeleeAttackPhase.Startup || CurrentPhase == TankMeleeAttackPhase.Preparing))
        {
            if (TankPreparedDashRules.ShouldDash(Preparation.IsHolding, held) && enablePreparedDash && allowPreparedDash)
            {
                // 只有 Host 可以授權目標與延後出傷；Client 等待同步的 Dash／Active。
                if (!Object.HasStateAuthority) return;
                if (TryStartPreparedDash()) return;
            }
            var progress = Preparation;
            bool strike = progress.Advance(deltaTime < 0f ? Runner.DeltaTime : deltaTime, held,
                definition.FrameTiming.FrameToSeconds(definition.PreparationFrame), definition.ResolvedStartup, GrowthSwingSpeed);
            Preparation = progress;
            CurrentPhase = progress.IsHolding ? TankMeleeAttackPhase.Preparing : TankMeleeAttackPhase.Startup;
            if (strike) EnterActivePhase();
            return;
        }
        switch (CurrentPhase)
        {
            case TankMeleeAttackPhase.Startup:
            {
                if (PhaseTimer.Expired(Runner))
                {
                    EnterActivePhase();
                }

                break;
            }

            case TankMeleeAttackPhase.Active:
            {
                if (PhaseTimer.Expired(Runner))
                {
                    EnterRecoveryPhase();
                }

                break;
            }

            case TankMeleeAttackPhase.Recovery:
            {
                if (PhaseTimer.Expired(Runner))
                {
                    CompleteCurrentAttack();
                }

                break;
            }
        }
    }

    private void EnterActivePhase()
    {
        TankMeleeAttackDefinition definition =
            GetDefinition(
                CurrentStep
            );

        if (definition == null)
        {
            ResetAttackState();

            return;
        }

        CurrentPhase = TankMeleeAttackPhase.Active;
        SwingSequence++;

        // =============================================================
        // 1. Network World Swing Audio
        // =============================================================

        /*
         * 世界揮擊聲與真正傷害幀使用同一個 Gameplay Active 時點。
         *
         * 它代表攻擊動作已經真正揮出，
         * 所以即使沒有命中任何目標也會播放。
         *
         * 只有 State Authority 可以透過 Player Root Emitter 廣播，
         * Client Prediction 不會再送出第二次。
         */
        TryPlayWorldSwing(
            CurrentStep
        );

        // =============================================================
        // 2. 本地揮砍 Presentation

        /*
        * Tank Light / Heavy 的基礎震動
        * 屬於「攻擊動作本身」。
        *
        * 所以即使完全揮空，
        * 只要進入 Active 就會播放。
        */
        bool ownerHasInputAuthority =
            ownerPlayerNetworkObject != null &&
            ownerPlayerNetworkObject.HasInputAuthority;

        if (ownerHasInputAuthority &&
            Runner != null &&
            Runner.IsForward)
        {
            CombatFeedbackId feedbackId =
                GetFeedbackId(
                    CurrentStep
                );

            if (debugTankMelee)
            {
                Debug.Log(
                    $"[Tank Melee] 準備播放本地 Swing Feedback。" +
                    $"\nStep：{CurrentStep}" +
                    $"\nFeedback ID：{feedbackId}" +
                    $"\nOwner Input Authority：{ownerHasInputAuthority}" +
                    $"\nRunner Is Forward：{Runner.IsForward}" +
                    $"\nFeedback Relay：" +
                    $"{(ownerCombatFeedbackRelay != null ? "找到" : "NULL")}",
                    this
                );
            }

            if (ownerCombatFeedbackRelay != null)
            {
                ownerCombatFeedbackRelay
                    .NotifyLocalAttackPerformed(
                        feedbackId
                    );
            }
        }

        // =============================================================
        // 2. 真正 Damage
        // =============================================================

        /*
        * ★ 整個 EnterActivePhase
        * 只能有這一次 PerformAuthoritativeAttack。
        */
        if (Object.HasStateAuthority)
        {
            PerformAuthoritativeAttack(
                definition
            );
        }

        // =============================================================
        // 3. Active Timer
        // =============================================================

        if (definition.ResolvedActive >
            0f)
        {
            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    definition.ResolvedActive / GrowthSwingSpeed
                );
        }
        else
        {
            EnterRecoveryPhase();
        }
    }

    private void EnterRecoveryPhase()
    {
        TankMeleeAttackDefinition definition =
            GetDefinition(
                CurrentStep
            );

        if (definition == null)
        {
            ResetAttackState();

            return;
        }

        CurrentPhase =
            TankMeleeAttackPhase.Recovery;

        if (definition.ResolvedRecovery >
            0f)
        {
            PhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    definition.ResolvedRecovery / GrowthSwingSpeed
                );

            return;
        }

        CompleteCurrentAttack();
    }

    /// <summary>
    /// 依 Tank Combo Step 廣播世界揮擊聲，音檔播放期間持續跟隨 Player。
    /// </summary>
    private void TryPlayWorldSwing(
        TankMeleeComboStep step
    )
    {
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        GameplayAudioCue selectedCue;

        switch (step)
        {
            case TankMeleeComboStep.Light1:
            {
                selectedCue =
                    light1WorldSwingCue;

                break;
            }

            case TankMeleeComboStep.Light2:
            {
                selectedCue =
                    light2WorldSwingCue;

                break;
            }

            case TankMeleeComboStep.Heavy:
            {
                selectedCue =
                    heavyWorldSwingCue;

                break;
            }

            case TankMeleeComboStep.None:
            default:
            {
                return;
            }
        }

        if (selectedCue == null)
        {
            return;
        }

        if (networkAudioEmitter == null &&
            ownerPlayer != null)
        {
            networkAudioEmitter =
                ownerPlayer.GetComponent<
                    NetworkPlayerAudioEmitter
                >();
        }

        if (networkAudioEmitter == null)
        {
            return;
        }

        networkAudioEmitter
            .PlayFollowingWorldOneShotFromStateAuthority(
                selectedCue,
                worldSwingVolumeScale
            );
    }
    
    #endregion

    // =====================================================================
    #region Complete

    private void CompleteCurrentAttack()
    {
        TankMeleeComboStep completedStep =
            CurrentStep;

        // =============================================================
        // 決定普通 Combo 下一段
        // =============================================================

        switch (completedStep)
        {
            case TankMeleeComboStep.Light1:
            {
                NextComboStep =
                    TankMeleeComboStep.Light2;

                break;
            }

            case TankMeleeComboStep.Light2:
            {
                NextComboStep =
                    TankMeleeComboStep.Heavy;

                break;
            }

            case TankMeleeComboStep.Heavy:
            case TankMeleeComboStep.None:
            default:
            {
                NextComboStep =
                    TankMeleeComboStep.Light1;

                break;
            }
        }

        // =============================================================
        // Idle
        // =============================================================

        CurrentStep =
            TankMeleeComboStep.None;

        CurrentPhase =
            TankMeleeAttackPhase.Idle;

        PhaseTimer =
            TickTimer.None;

        // =============================================================
        // Combo Reset Window
        // =============================================================

        if (comboResetDuration > 0f)
        {
            ComboResetTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    comboResetDuration
                );
        }
        else
        {
            ResetComboToLight1();
        }

        if (debugTankMelee &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Melee] Attack Finished" +
                $"\nCompleted：{completedStep}" +
                $"\nNext：{NextComboStep}" +
                $"\nCombo Window：{comboResetDuration:F2}",
                this
            );
        }
    }

    private void ResetComboToLight1()
    {
        NextComboStep =
            TankMeleeComboStep.Light1;

        ComboResetTimer =
            TickTimer.None;
    }

    private void ResetAttackState()
    {
        ClearPreparedDash();
        CurrentStep =
            TankMeleeComboStep.None;

        CurrentPhase =
            TankMeleeAttackPhase.Idle;

        PhaseTimer =
            TickTimer.None;

        Preparation = default;
        ResetComboToLight1();
    }

    #endregion

    // =====================================================================
    #region Grapple Forced Heavy API

    /// <summary>
    /// 讓 Tank 下一次左鍵
    /// 在指定時間內直接變成 Heavy。
    ///
    /// ------------------------------------------------------------
///
/// 這是專門保留給之後：
///
/// Tank Grapple Gather Ability。
///
/// ------------------------------------------------------------
///
/// 這個接口：
///
/// 不會中斷目前正在進行的攻擊。
///
/// 如果玩家目前正在 Light1：
///
/// Light1 繼續完整結束。
///
/// 下一次真正開始的攻擊
/// 才會吃 Forced Heavy。
    /// </summary>
    public void ForceNextHeavyAttack(
        float duration = -1f
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        float validDuration =
            duration > 0f
                ? duration
                : defaultForcedHeavyDuration;

        if (validDuration <= 0f)
        {
            return;
        }

        ForcedHeavyTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                validDuration
            );

        if (debugTankMelee)
        {
            Debug.Log(
                $"[Tank Melee] 下一次攻擊已強制設為 Heavy。" +
                $"\n有效時間：{validDuration:F2} 秒",
                this
            );
        }
    }

    /// <summary>
    /// 外部系統清除 Forced Heavy。
    /// </summary>
    public void ClearForcedHeavyAttack()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        ForcedHeavyTimer =
            TickTimer.None;
    }

    #endregion

    // =====================================================================
    #region Damage

    private void PerformAuthoritativeAttack(
        TankMeleeAttackDefinition definition
    )
    {
        if (Runner == null ||
            Runner.LagCompensation == null)
        {
            return;
        }

        PlayerRef attacker =
            ownerPlayerNetworkObject != null
                ? ownerPlayerNetworkObject
                    .InputAuthority
                : PlayerRef.None;

        if (attacker.IsNone)
        {
            return;
        }

        Vector3 origin =
            GetMeleeOrigin();

        Vector3 forward =
            GetMeleeForward();

        EnemyProjectileReflection.ReflectMelee(ownerPlayer, origin, forward, definition.Range, definition.SearchRadius, definition.FullAttackAngle);

        if (debugDrawAttack)
        {
            Debug.DrawRay(
                origin,
                forward *
                definition.Range,
                Color.red,
                debugDrawDuration
            );
        }

        MeleeDamageQuery query =
            new MeleeDamageQuery
            {
                Runner =
                    Runner,

                Attacker =
                    attacker,

                SourceNetworkObject =
                    ownerPlayerNetworkObject,

                SourceObject =
                    ownerPlayer != null
                        ? ownerPlayer.gameObject
                        : gameObject,

                Origin =
                    origin,

                Forward =
                    forward,

                Damage =
                    definition.Damage,

                DamageType =
                    DamageType.Melee,
                
                /*
                * Light1 / Light2
                * → TankLightMelee
                *
                * Heavy
                * → TankHeavyMelee。
                */
                FeedbackId =
                    GetFeedbackId(
                        CurrentStep
                    ),

                Sequence =
                    AttackSequence,

                /*
                 * Tank 第三段 Heavy 固定視為 Forced Headshot。
                 *
                 * Light1 / Light2 保持 None。
                 * HitZone 仍由 Resolver 保存為 Body，
                 * 不會偽裝成物理打中 Head Hitbox。
                 */
                ForcedHeadshotSource =
                    CurrentStep ==
                        TankMeleeComboStep.Heavy
                            ? DamageForcedHeadshotSource
                                .TankHeavyMelee
                            : DamageForcedHeadshotSource
                                .None,

                Range =
                    definition.Range,

                SearchRadius =
                    definition.SearchRadius,

                FullAttackAngle =
                    definition.FullAttackAngle,

                MaximumTargets =
                    definition.MaximumTargets,

                HitMask =
                    meleeHitMask,

                UseSubtickAccuracy =
                    useSubtickAccuracy,

                IncludePhysX =
                    includePhysX,

                RequireLineOfSight =
                    requireLineOfSight,

                ObstructionMask =
                    obstructionMask,

                ObstructionRayStartOffset =
                    obstructionRayStartOffset
            };

        MeleeDamageSummary summary =
            damageResolver.Resolve(
                query,
                resolvedResults,
                confirmedResults
            );

        // =============================================================
        // Feedback
        // =============================================================

        /*
         * Tank 是 AOE。
         *
         * 一刀命中很多敵人：
         *
         * Damage 全部正常結算。
         *
         * 但是 Camera Shake / X
         * 只播放一次。
         */
        DamageResult? feedbackResult =
            SelectFeedbackResult();

        if (feedbackResult.HasValue)
        {
            DamageConfirmed?.Invoke(
                feedbackResult.Value
            );
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugTankMelee)
        {
            Debug.Log(
                $"[Tank Melee] Damage Complete" +
                $"\nStep：{CurrentStep}" +
                $"\nSequence：{AttackSequence}" +
                $"\nDamage：{definition.Damage:F2}" +
                $"\nAngle：{definition.FullAttackAngle:F1}" +
                $"\nRaw Hits：{summary.RawHitCount}" +
                $"\nCandidates：{summary.CandidateCount}" +
                $"\nAttempted：{summary.AttemptedTargetCount}" +
                $"\nConfirmed：{summary.ConfirmedDamageCount}" +
                $"\nKills：{summary.KillCount}",
                this
            );
        }
    }

    /// <summary>
    /// 從一刀命中的多個 DamageResult 中
    /// 選出「只播放一次」的 Feedback。
    ///
    /// ------------------------------------------------------------
    ///
    /// 優先：
    ///
    /// Kill
    /// ↓
    /// 普通有效命中。
    ///
    /// ------------------------------------------------------------
    ///
    /// 因此一刀：
    ///
    /// 打 8 隻
    /// 殺 2 隻
    ///
    /// 只會播放一次 Kill Feedback。
    /// </summary>
    /// <summary>
    /// 從同一刀命中的所有 DamageResult 中，
    /// 選出唯一一筆最高優先 Presentation Result。
    ///
    /// Kill > Headshot > Normal Hit。
    /// </summary>
    private DamageResult?
        SelectFeedbackResult()
    {
        if (confirmedResults.Count <= 0)
        {
            return null;
        }

        // =============================================================
        // 1. Kill
        // =============================================================

        for (int i = 0;
             i < confirmedResults.Count;
             i++)
        {
            if (confirmedResults[i]
                .KilledTarget)
            {
                return
                    confirmedResults[i];
            }
        }

        // =============================================================
        // 2. Headshot
        // =============================================================

        for (int i = 0;
             i < confirmedResults.Count;
             i++)
        {
            if (confirmedResults[i]
                .IsHeadshot)
            {
                return
                    confirmedResults[i];
            }
        }

        // =============================================================
        // 3. Normal Hit
        // =============================================================

        return
            confirmedResults[0];
    }

    #endregion

    // =====================================================================
    #region Attack Space

    private Vector3 GetMeleeOrigin()
    {
        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            return
                ownerMovement
                    .KCC
                    .Data
                    .TargetPosition +
                Vector3.up *
                originHeight;
        }

        if (ownerPlayer != null)
        {
            return
                ownerPlayer.transform.position +
                Vector3.up *
                originHeight;
        }

        return
            transform.position +
            Vector3.up *
            originHeight;
    }

    /// <summary>
    /// Tank 普通電鋸近戰使用水平 Yaw。
    ///
    /// 玩家上下看不會把整個扇形
    /// 朝天空或地板旋轉；受困反擊例外，使用實際瞄準方向。
    /// </summary>
    private Vector3 GetMeleeForward()
    {
        // 嘴邊掛點可能位於敵人上方；受困時可瞄準下方反擊，但仍走原近戰距離／扇形／LOS。
        if (ownerPlayer != null && ownerMovement != null &&
            ownerPlayer.GetComponent<PlayerActionGate>()?.CanUseWeaponWhileCaptured == true)
            return ownerMovement.GetAimDirection();

        Vector3 forward;

        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            forward =
                ownerMovement
                    .KCC
                    .Data
                    .TransformRotation *
                Vector3.forward;
        }
        else if (ownerPlayer != null)
        {
            forward =
                ownerPlayer.transform.forward;
        }
        else
        {
            forward =
                transform.forward;
        }

        forward.y =
            0f;

        if (forward.sqrMagnitude <=
            0.0001f)
        {
            forward =
                Vector3.forward;
        }

        return
            forward.normalized;
    }

    #endregion

    // =====================================================================
    #region Definition

    private TankMeleeAttackDefinition
        GetDefinition(
            TankMeleeComboStep step
        )
    {
        switch (step)
        {
            case TankMeleeComboStep.Light1:
                return lightAttack1;

            case TankMeleeComboStep.Light2:
                return lightAttack2;

            case TankMeleeComboStep.Heavy:
                return heavyAttack;

            case TankMeleeComboStep.None:
            default:
                return null;
        }
    }

    #endregion

    /// <summary>
    /// 根據目前 Tank Combo Step
    /// 回傳對應的 Combat Feedback 身份。
    ///
    /// ------------------------------------------------------------
    ///
    /// Light1
    /// Light2
    ///
    /// 目前使用同一套輕攻擊回饋。
    ///
    /// Heavy
    ///
    /// 使用獨立重攻擊回饋。
    /// </summary>
    private CombatFeedbackId
        GetFeedbackId(
            TankMeleeComboStep step
        )
    {
        switch (step)
        {
            case TankMeleeComboStep.Light1:
            case TankMeleeComboStep.Light2:
            {
                return
                    CombatFeedbackId
                        .TankLightMelee;
            }

            case TankMeleeComboStep.Heavy:
            {
                return
                    CombatFeedbackId
                        .TankHeavyMelee;
            }

            case TankMeleeComboStep.None:
            default:
            {
                return
                    CombatFeedbackId.None;
            }
        }
    }
}
