using Fusion;
using System.Collections.Generic;
using UnityEngine;
using System;

/// <summary>
/// Tank Air Dash 目前 Gameplay 階段。
///
/// ------------------------------------------------------------
///
/// Ready：
///
/// 可以等待下一次 GrappleAirborne 右鍵。
///
/// ------------------------------------------------------------
///
/// Dashing：
///
/// 已經鎖定一次 Enemy 或 World Target，
/// 正在使用 KCC 進行 3D 高速位移。
/// </summary>
public enum TankAirDashPhase : byte
{
    Ready = 0,

    Dashing = 1
}

/// <summary>
/// Tank 空中特殊衝刺目前選到的目標類型。
///
/// ------------------------------------------------------------
///
/// 目前第一階段只做 Target Selection。
///
/// 下一階段才真正加入：
///
/// Enemy Dash
/// World Dash
/// Cooldown
/// AOE Damage。
/// </summary>
public enum TankAirDashTargetType : byte
{
    /// <summary>
    /// 沒有找到合法目標。
    /// </summary>
    None = 0,

    /// <summary>
    /// 找到合法 Enemy。
    /// </summary>
    Enemy = 1,

    /// <summary>
    /// 沒有 Enemy，但準心直接射到合法世界表面。
    /// </summary>
    World = 2
}

/// <summary>
/// Tank Air Dash 結束原因。
///
/// Gameplay 不使用字串判斷成功與否。
/// </summary>
public enum TankAirDashCompletionReason : byte
{
    Arrived = 0,

    Timeout = 1,

    KCCMissing = 2,

    InvalidTarget = 3
}

/// <summary>
/// Tank GrappleAirborne 右鍵特殊能力。
///
/// ====================================================================
///
/// 目前第一階段只負責：
///
/// GrappleAirborne
/// +
/// 右鍵剛按下
///
/// ↓
///
/// 搜尋 Enemy / World Target。
///
/// ====================================================================
///
/// 這一步完全不會：
///
/// 移動玩家
/// 造成傷害
/// 開始 Cooldown
/// 改變 Camera
/// 強制轉動準心。
///
/// ====================================================================
///
/// Target 優先級：
///
/// 1. Enemy
/// 2. World
/// 3. None。
///
/// ====================================================================
///
/// Enemy 判定是「寬鬆準心判定」。
///
/// 不要求準心射線精準碰到 Enemy Hitbox。
///
/// 只要 Enemy：
///
/// 距離合法
/// 角度合法
/// GrappleInteractionTarget = Enemy
/// IsInteractionAvailable
/// 沒有被牆擋住
///
/// 就可以成為候選。
/// </summary>
[DisallowMultipleComponent]
public class TankAirDashAbility :
    NetworkBehaviour,
    ICombatDamageFeedbackSource
{
    // =====================================================================
    #region Owner Player

    [Header("Owner Player Binding")]

    [SerializeField]
    [Tooltip("這個 Tank Air Dash Ability 真正所屬的 Player Core。正常情況不需要手動指定，由 TankProfessionRuntimeDriver 在 Runtime Spawn 後自動綁定。")]
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 的共用移動模組。
    ///
    /// 主要提供：
    ///
    /// KCC
    /// GetAimDirection()。
    /// </summary>
    private PlayerMovement
        ownerMovement;

    /// <summary>
    /// Owner Player 狀態機。
    ///
    /// 特殊能力目前只允許：
    ///
    /// GrappleAirborne。
    /// </summary>
    private PlayerStateMachine
        ownerStateMachine;

    /// <summary>
    /// 真正 Player Core NetworkObject。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;

    /// <summary>
    /// Player Root 上唯一的世界聲音發送器。
    /// </summary>
    private NetworkPlayerAudioEmitter
        networkAudioEmitter;

    /// <summary>
    /// 綁定真正 Owner Player。
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

        ownerPlayerNetworkObject =
            null;

        ownerCombatFeedbackRelay =
            null;

        networkAudioEmitter =
            null;

        if (ownerPlayer == null)
        {
            return;
        }

        ownerMovement =
            ownerPlayer.Movement;

        ownerStateMachine =
            ownerPlayer.StateMachine;

        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        networkAudioEmitter =
            ownerPlayer.GetComponent<
                NetworkPlayerAudioEmitter
            >();

        if (ownerMovement == null)
        {
            Debug.LogError(
                $"[{nameof(TankAirDashAbility)}] " +
                $"Owner Player 找不到 PlayerMovement。",
                ownerPlayer
            );
        }

        if (ownerStateMachine == null)
        {
            Debug.LogError(
                $"[{nameof(TankAirDashAbility)}] " +
                $"Owner Player 找不到 PlayerStateMachine。",
                ownerPlayer
            );
        }

        ownerCombatFeedbackRelay =
            ownerPlayer
                .GetComponent<
                    PlayerCombatFeedbackRelay
                >();

        if (ownerCombatFeedbackRelay == null)
        {
            Debug.LogError(
                $"[{nameof(TankAirDashAbility)}] " +
                $"Owner Player 找不到 " +
                $"{nameof(PlayerCombatFeedbackRelay)}。" +
                $"\n因此 Air Dash 可以正常 Gameplay，" +
                $"但無法播放本地衝刺 Presentation。",
                ownerPlayer
            );
        }

        if (networkAudioEmitter == null)
        {
            Debug.LogError(
                $"[{nameof(TankAirDashAbility)}] " +
                $"Owner Player Root 找不到 " +
                $"{nameof(NetworkPlayerAudioEmitter)}，" +
                $"Air Strike 仍可造成傷害，但其他玩家聽不到世界重擊聲。",
                ownerPlayer
            );
        }
    }

    #endregion

    // =====================================================================
    #region 特殊能力冷卻

    [Header("特殊能力冷卻")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Air Dash 成功鎖定 Enemy 並正式發動後的冷卻時間，單位為秒。目前設計為 10 秒。冷卻從技能正式開始衝刺時就開始計算，避免玩家利用碰撞或 Timeout 取消技能來規避冷卻。")]
    private float enemyDashCooldownDuration =
        10f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Air Dash 沒有 Enemy Target，而是成功對 World Target 發動衝刺後的冷卻時間，單位為秒。目前設計為 3 秒。")]
    private float worldDashCooldownDuration =
        3f;

    #endregion

    // =====================================================================
    #region Enemy Dash 到達攻擊

    [Header("Enemy Dash 到達攻擊")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Enemy Dash 成功到達目標後，對正面扇形內每一個合法敵人造成的基礎傷害。這是特殊能力的高傷害攻擊，目前先使用 120 測試，之後再進行正式平衡。")]
    private float enemyArrivalDamage =
        120f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Enemy Dash 到達後，正面 AOE 最遠可以命中多少距離內的敵人。這個 Range 是從 Tank 到達位置開始計算。建議先使用 4。")]
    private float enemyArrivalRange =
        4f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Enemy Dash AOE 用來搜尋候選敵人的 Overlap Sphere 半徑。最後仍會經過 Range 與扇形角度過濾。建議先使用 2.5。")]
    private float enemyArrivalSearchRadius =
        2.5f;

    [SerializeField]
    [Range(1f, 360f)]
    [Tooltip("Enemy Dash 到達後的完整 3D 攻擊扇形角度。160 代表以衝刺方向為中心，左右總共 160 度。這次判定保留完整 3D 方向，不會強制壓到水平面，所以往高處或低處 Enemy 衝刺也能正常命中。")]
    private float enemyArrivalFullAttackAngle =
        160f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("從 Player KCC TargetPosition 往上增加多少高度，作為 Enemy Dash AOE 的攻擊起點。建議先使用 1.1。")]
    private float enemyArrivalOriginHeight =
        1.1f;

    [SerializeField]
    [Tooltip("Enemy Dash 到達攻擊可以搜尋哪些敵人 Hitbox Layer。請使用與 TankMeleeCombo、AttackQuickMelee 相同的 Enemy HitMask Layer。")]
    private LayerMask enemyArrivalHitMask =
        ~0;


    [Header("Enemy Dash AOE 障礙物")]

    [SerializeField]
    [Tooltip("開啟後，Enemy Dash AOE 不可以隔著牆壁傷害敵人。建議保持開啟。")]
    private bool enemyArrivalRequireLineOfSight =
        true;

    [SerializeField]
    [Tooltip("哪些 Layer 可以阻擋 Enemy Dash AOE。建議只有 Ground、Wall、WorldGeometry，不要包含 Enemy。")]
    private LayerMask enemyArrivalObstructionMask =
        0;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Enemy Dash AOE 的 LOS Raycast 起點往目標方向偏移多少距離，避免射線從 Player Collider 內部開始。")]
    private float enemyArrivalObstructionRayStartOffset =
        0.05f;

    #endregion

    // =====================================================================
    #region Air Strike World Audio

    [Header("Air Strike 世界聲音")]

    [SerializeField]
    [Tooltip(
        "Tank 對 Enemy 完成 GrappleAirborne Dash，並正式執行 Arrival AOE 的瞬間播放的 3D 世界重擊聲。\n\n" +
        "這是能力抵達與攻擊動作聲；即使 AOE 最後沒有找到合法傷害目標，只要 Arrival Attack 正式執行仍會播放一次。\n\n" +
        "必須使用 Network ID 大於 0、且已加入 GameplayAudioCatalog 的 Cue。")]
    private GameplayAudioCue
        airStrikeWorldImpactCue;

    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Air Strike 世界重擊聲的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0 = 靜音。")]
    private float airStrikeWorldImpactVolumeScale =
        1f;

    #endregion

    // =====================================================================
    #region 瞄準起點

    [Header("Gameplay Aim Origin")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("從 Player KCC TargetPosition 向上增加多少高度，作為 Tank 特殊能力搜尋射線的世界起點。這是 Gameplay 判定位置，不依賴本地 Camera Transform。建議先使用 1.6。")]
    private float aimOriginHeight =
        1.6f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("從玩家位置沿準心方向額外往前偏移多少距離，避免搜尋射線從玩家自己的碰撞體內部開始。建議使用 0.1。")]
    private float aimOriginForwardOffset =
        0.1f;

    #endregion

    // =====================================================================
    #region Enemy 搜尋

    [Header("Enemy 寬鬆搜尋")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank 空中特殊能力最多可以鎖定多遠的 Enemy。這是 Enemy Dash 的目標搜尋距離，目前先用 40 測試，之後可以依關卡與 Dash 手感調整。")]
    private float enemySearchDistance =
        40f;

    [SerializeField]
    [Range(0.1f, 45f)]
    [Tooltip("Enemy 相對於玩家準心方向最多允許偏離幾度。系統會在這個角度內選『最接近準心』的 Enemy。這就是避免玩家瞄怪群卻被判成牆壁 Dash 的寬鬆判定。建議先測 10 度。")]
    private float enemySearchAngle =
        10f;

    [SerializeField]
    [Tooltip("哪些 Layer 可能包含 Enemy Hitbox。建議直接設定成你目前 Enemy 使用的 HitMask Layer，不要包含整個 World，否則搜尋候選 Collider 會做很多無效工作。")]
    private LayerMask enemySearchMask =
        ~0;

    [SerializeField]
    [Min(8)]
    [Tooltip("一次 Enemy OverlapSphere 最多暫存多少個 Collider。這不是最多敵人數，因為一隻 Enemy 可能有很多 Hitbox。一般使用 64 已足夠。")]
    private int enemySearchBufferSize =
        64;

    #endregion

    // =====================================================================
    #region Enemy 可見性

    [Header("Enemy 可見性")]

    [SerializeField]
    [Tooltip("用來檢查玩家到 Enemy 之間是否被牆壁或其他場景物件擋住的 Layer。這個 Mask 應該同時包含 Enemy Hitbox 與會遮擋 Dash 的 World Layer，否則無法確認第一個射線命中的是不是候選 Enemy。")]
    private LayerMask enemyVisibilityMask =
        ~0;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Enemy 可見性 Raycast 比目標中心額外延伸多少距離，避免因浮點誤差造成射線剛好停在 Collider 表面前。建議 0.15 到 0.3。")]
    private float enemyVisibilityPadding =
        0.2f;

    #endregion

    // =====================================================================
    #region World 搜尋

    [Header("World Dash Target")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("當沒有找到合法 Enemy 時，準心最多可以搜尋多遠的牆壁、地板或其他 World Dash 表面。這個數值之後也會成為 World Dash 的最大距離。")]
    private float worldSearchDistance =
        35f;

    [SerializeField]
    [Tooltip("哪些 Layer 可以作為 Tank World Dash 的目標。例如 Ground、Wall、WorldGeometry。建議不要包含 Enemy Layer，因為 Enemy 已經由更高優先級的 Enemy Search 處理。")]
    private LayerMask worldSearchMask =
        ~0;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，右鍵嘗試使用 Tank 空中特殊能力時會顯示目前 Movement State、Target Type、Enemy、角度、距離與 Target Point。這一階段建議保持開啟。")]
    private bool debugTargetSelection =
        true;

    [SerializeField]
    [Tooltip("開啟後會用 Debug.DrawRay 畫出準心方向、Enemy Target Direction 或 World Target Direction，方便在 Scene View 檢查選敵結果。")]
    private bool drawTargetDebug =
        true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Debug Draw 線段保留多久，單位為秒。")]
    private float debugDrawDuration =
        1f;

    #endregion

    // =====================================================================
    #region Runtime Cache

    /// <summary>
    /// 同一隻 Enemy 可能有多顆 Collider。
    ///
    /// 使用 GrappleInteractionTarget 去重，
    /// 避免同一隻 Enemy 被計算很多次。
    /// </summary>
    private readonly HashSet<GrappleInteractionTarget>
        processedEnemyTargets =
            new HashSet<GrappleInteractionTarget>();

    /// <summary>
    /// Fusion Lag Compensation
    /// Enemy 搜尋結果。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這裡可以同時收到：
    ///
    /// Fusion Hitbox
    ///
    /// 以及在指定 IncludePhysX 後收到：
    ///
    /// Unity Collider。
    ///
    /// ------------------------------------------------------------
    ///
    /// Tank Enemy Targeting
    /// 主要依靠 Fusion Hitbox。
    /// </summary>
    private readonly List<LagCompensatedHit>
        enemyLagHits =
            new List<LagCompensatedHit>(64);

    /// <summary>
    /// Enemy Visibility Raycast 使用。
    /// </summary>
    private readonly List<LagCompensatedHit>
        visibilityLagHits =
            new List<LagCompensatedHit>(32);

    /// <summary>
    /// Owner Player 的本地戰鬥回饋 Relay。
    ///
    /// TankAirDashAbility 不直接控制：
    ///
    /// Camera
    /// Camera Shake
    /// Hit Marker。
    ///
    /// 它只通知：
    ///
    /// 「Enemy / World Dash 正式發動了。」
    ///
    /// 再由 Presentation Pipeline 決定實際效果。
    /// </summary>
    private PlayerCombatFeedbackRelay
        ownerCombatFeedbackRelay;

    #endregion

    // =====================================================================
    #region Network State

    /// <summary>
    /// Air Dash 特殊能力目前剩餘冷卻。
    ///
    /// Enemy Dash：10 秒。
    /// World Dash：3 秒。
    ///
    /// ------------------------------------------------------------
    ///
    /// Cooldown 與 Dashing 分開保存。
    ///
    /// 也就是技能開始 Dash 的瞬間
    /// Cooldown 就可以開始倒數，
    /// 不需要等位移完成才開始。
    /// </summary>
    [Networked]
    private TickTimer AbilityCooldownTimer
    {
        get;
        set;
    }

    /// <summary>
    /// Enemy Dash 成功到達後的 AOE Sequence。
    ///
    /// 每一次正式 AOE +1。
    /// </summary>
    [Networked]
    private int AirStrikeSequence
    {
        get;
        set;
    }

    /// <summary>
    /// Tank GrappleAirborne Air Dash 已經正式消耗並發動過多少次。
    ///
    /// 與 AirStrikeSequence 不同：
    ///
    /// AirDashSequence
    /// → Enemy Dash、World Dash，以及敵人已在極近距離時的直接 Arrival Attack
    ///   都會增加一次。
    ///
    /// AirStrikeSequence
    /// → 只屬於真正的 Enemy Arrival AOE Damage。
    ///
    /// 第一人稱 ViewModel 使用本序號播放一次 attack4，
    /// 不直接讀取右鍵 Input，也不依賴可能很短的 Dashing Phase。
    /// </summary>
    [Networked]
    public int AirDashSequence
    {
        get;
        private set;
    }

    /// <summary>
    /// Air Dash 目前階段。
    /// </summary>
    [Networked]
    public TankAirDashPhase CurrentPhase
    {
        get;
        private set;
    }

    /// <summary>
    /// 這次正在執行的 Dash 是 Enemy 還是 World。
    /// </summary>
    [Networked]
    public TankAirDashTargetType ActiveDashTargetType
    {
        get;
        private set;
    }

    /// <summary>
    /// 施放當下保存的 Target Point。
    ///
    /// ------------------------------------------------------------
    ///
    /// World：
    /// 就是 Raycast Hit Point。
    ///
    /// Enemy：
    /// 主要作為 Enemy NetworkObject 消失時的備援位置。
    /// </summary>
    [Networked]
    private Vector3 ActiveDashTargetPoint
    {
        get;
        set;
    }

    /// <summary>
    /// Enemy Dash 真正鎖定的 Enemy NetworkObject。
    ///
    /// World Dash 時為 null。
    ///
    /// ------------------------------------------------------------
    ///
    /// Enemy 如果在 Dash 過程中移動，
    /// 我們會繼續使用它目前的位置更新 Destination，
    /// 而不是永遠衝向施放瞬間的舊位置。
    /// </summary>
    [Networked]
    private NetworkObject ActiveEnemyNetworkObject
    {
        get;
        set;
    }

    /// <summary>
    /// Dash 啟動瞬間的接近方向。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個方向很重要。
    ///
    /// Enemy Destination 會是：
    ///
    /// Enemy Position
    /// -
    /// Approach Direction × Enemy Stop Distance。
    ///
    /// ------------------------------------------------------------
    ///
    /// 因此玩家會停在「原本接近 Enemy 的前方」，
    /// 不會因為每 Tick 重新計算方向而繞著敵人轉。
    /// </summary>
    [Networked]
    private Vector3 DashApproachDirection
    {
        get;
        set;
    }

    /// <summary>
    /// Dash 安全 Timeout。
    /// </summary>
    [Networked]
    private TickTimer DashTimeoutTimer
    {
        get;
        set;
    }

    /// <summary>
    /// Dash 是否正在進行。
    /// </summary>
    public bool IsDashing =>
        CurrentPhase ==
        TankAirDashPhase.Dashing;

    #endregion

    // =====================================================================
    #region 最近一次選擇結果

    /// <summary>
    /// 最近一次右鍵成功辨識出的 Target Type。
    ///
    /// 目前只做 Debug。
    /// </summary>
    public TankAirDashTargetType LastTargetType
    {
        get;
        private set;
    }

    /// <summary>
    /// 最近一次選到的 Enemy。
    ///
    /// World / None 時為 null。
    /// </summary>
    public GrappleInteractionTarget LastEnemyTarget
    {
        get;
        private set;
    }

    /// <summary>
    /// 最近一次正式選出的世界 Target Point。
    ///
    /// Enemy：
    /// Enemy Hitbox / Candidate Point。
    ///
    /// World：
    /// World Raycast Hit Point。
    /// </summary>
    public Vector3 LastTargetPoint
    {
        get;
        private set;
    }

    /// <summary>
    /// 最近一次 Enemy 與準心的角度。
    ///
    /// World / None 時為 0。
    /// </summary>
    public float LastEnemyAimAngle
    {
        get;
        private set;
    }

    /// <summary>
    /// 最近一次 Target 距離。
    /// </summary>
    public float LastTargetDistance
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Unity


#if UNITY_EDITOR

    private void OnValidate()
    {
        enemySearchBufferSize =
            Mathf.Max(
                8,
                enemySearchBufferSize
            );

        enemySearchDistance =
            Mathf.Max(
                0.1f,
                enemySearchDistance
            );

        worldSearchDistance =
            Mathf.Max(
                0.1f,
                worldSearchDistance
            );
    }

#endif

    #endregion

    // =====================================================================
    #region Simulation

    /// <summary>
    /// 每個 Fusion Tick
    /// 由 TankProfessionRuntimeDriver 呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 優先級：
    ///
    /// 1. 已經 Dashing
    ///    → 持續執行 Dash。
    ///
    /// 2. 沒在 Dashing
    ///    → 等待 GrappleAirborne + 右鍵。
    /// </summary>
    public void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        if (ownerPlayer == null ||
            ownerMovement == null ||
            ownerStateMachine == null ||
            ownerPlayerNetworkObject == null)
        {
            return;
        }

        // =============================================================
        // 1. 已經正在 Dash
        // =============================================================

        if (IsDashing)
        {
            TickDash();

            return;
        }

        // =============================================================
        // Cooldown 清理
        // =============================================================

        if (AbilityCooldownTimer
            .Expired(Runner))
        {
            AbilityCooldownTimer =
                TickTimer.None;
        }

        // =============================================================
        // 冷卻中不能重新施放
        // =============================================================

        if (IsOnCooldown)
        {
            /*
            * 注意：
            *
            * 只有 Air Dash 特殊能力不能用。
            *
            * 玩家回到正常狀態後，
            * Guard 仍然可以正常使用。
            */
            return;
        }

        // =============================================================
        // 2. 右鍵剛按下
        // =============================================================

        bool specialPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Aim
            );

        if (specialPressed == false)
        {
            return;
        }

        // =============================================================
        // 3. 只允許 GrappleAirborne
        // =============================================================

        if (ownerStateMachine.CurrentState !=
            PlayerMovementState.GrappleAirborne)
        {
            return;
        }

        // =============================================================
        // 4. State Authority 正式決定 Dash
        // =============================================================

        /*
        * 目前這一階段先確保：
        *
        * Dash 方向
        * Dash Destination
        * KCC 碰撞
        * Enemy / World Stop Distance
        *
        * 完全正確。
        *
        * Client Prediction
        * 我們會在 Dash 本體確認後單獨測試，
        * 不和 AOE / Cooldown 混在一起。
        */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // =============================================================
        // 5. 搜尋 Target
        // =============================================================

        ResolveTarget();

        // =============================================================
        // 6. 沒 Target
        // =============================================================

        if (LastTargetType ==
            TankAirDashTargetType.None)
        {
            return;
        }

        // =============================================================
        // 7. 正式開始 Dash
        // =============================================================

        StartDashFromResolvedTarget();
    }

    #endregion

    // =====================================================================
    #region Dash Start

    /// <summary>
    /// 使用剛剛 ResolveTarget() 的結果
    /// 正式啟動一次 Tank Air Dash。
    /// </summary>
    private void StartDashFromResolvedTarget()
    {
        if (ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            return;
        }

        Vector3 currentPosition =
            ownerMovement
                .KCC
                .Data
                .TargetPosition;

        // =============================================================
        // Target Type
        // =============================================================

        ActiveDashTargetType =
            LastTargetType;

        ActiveDashTargetPoint =
            LastTargetPoint;

        ActiveEnemyNetworkObject =
            null;

        // =============================================================
        // Enemy NetworkObject
        // =============================================================

        if (LastTargetType ==
                TankAirDashTargetType.Enemy &&
            LastEnemyTarget != null)
        {
            /*
            * 優先使用 GrappleInteractionTarget
            * 已經保存的正式 Gameplay Owner。
            */
            ActiveEnemyNetworkObject =
                LastEnemyTarget
                    .OwnerNetworkObject;

            /*
            * 防呆：
            *
            * 如果 GrappleInteractionTarget
            * 沒有指定 OwnerNetworkObject，
            * 就往父階層找 NetworkObject。
            */
            if (ActiveEnemyNetworkObject == null)
            {
                ActiveEnemyNetworkObject =
                    LastEnemyTarget
                        .GetComponentInParent<
                            NetworkObject
                        >();
            }
        }

        // =============================================================
        // 真正 Dash Target Anchor
        // =============================================================

        Vector3 targetAnchor =
            GetCurrentDashTargetAnchor();

        Vector3 toTarget =
            targetAnchor -
            currentPosition;

        float targetDistance =
            toTarget.magnitude;

        if (targetDistance <=
            0.0001f)
        {
            ResetDashState();

            return;
        }

        DashApproachDirection =
            toTarget.normalized;

        // =============================================================
        // 計算實際停止位置
        // =============================================================

        Vector3 destination =
            CalculateDashDestination(
                targetAnchor
            );

        float travelDistance =
            Vector3.Distance(
                currentPosition,
                destination
            );

        // =============================================================
        // Target 已經近到不需要 Dash
        // =============================================================

        if (travelDistance <
            minimumDashTravelDistance)
        {
            // ---------------------------------------------------------
            // Enemy：
            // 已經貼在敵人旁邊
            // 仍然允許直接觸發特殊攻擊。
            // ---------------------------------------------------------

            if (ActiveDashTargetType ==
                TankAirDashTargetType.Enemy)
            {
                /*
                 * 雖然距離近到不需要位移，
                 * 但這次特殊能力已正式消耗並執行 Arrival Attack，
                 * 所以仍然必須播放一次特殊能力動畫。
                 */
                AirDashSequence++;

                StartAbilityCooldown(
                    TankAirDashTargetType.Enemy
                );

                PerformEnemyArrivalAttack();

                ResetDashState();

                if (debugTargetSelection)
                {
                    Debug.Log(
                        $"[Tank Air Dash] Enemy 已在近距離，" +
                        $"直接執行 Arrival Attack。" +
                        $"\nTravel Distance：{travelDistance:F2}",
                        this
                    );
                }

                return;
            }

            // ---------------------------------------------------------
            // World：
            // 已經貼著牆壁就不浪費技能與 CD。
            // ---------------------------------------------------------

            ResetDashState();

            return;
        }

        // =============================================================
        // ★ 清除 Grapple Release Momentum
        // =============================================================

        /*
        * 這個接口目前 Player Core 已經存在。
        *
        * 特殊 Dash 開始時：
        *
        * 不應該再把前面的 Grapple Momentum
        * 疊在 Dash 上。
        */
        ownerPlayer
            .CancelManualMomentumFromSpecialAbility(
                true
            );

        // =============================================================
        // 正式消耗特殊能力
        // =============================================================

        /*
         * 所有檢查均通過，特殊能力現在才算正式消耗。
         * 必須在這裡增加 Sequence，不能在讀到右鍵時就增加。
         */
        AirDashSequence++;

        StartAbilityCooldown(
            ActiveDashTargetType
        );

        // =============================================================
        // 開始 Dash

        CurrentPhase =
            TankAirDashPhase.Dashing;

        // =============================================================
        // 本地 Air Dash Presentation
        // =============================================================

        /*
        * 只有真正控制這名 Player 的本機
        * 才播放第一人稱衝刺 Camera Shake。
        *
        * ------------------------------------------------------------
        *
        * 這裡使用 Owner Player 的 Input Authority，
        * 不能使用 Tank Runtime 自己的
        * Object.HasInputAuthority。
        *
        * ------------------------------------------------------------
        *
        * Runner.IsForward：
        *
        * 防止 Fusion Resimulation
        * 讓同一次 Dash 重複播放本地效果。
        */
        bool ownerHasInputAuthority =
            ownerPlayerNetworkObject != null &&
            ownerPlayerNetworkObject.HasInputAuthority;

        if (ownerHasInputAuthority &&
            Runner != null &&
            Runner.IsForward &&
            ownerCombatFeedbackRelay != null)
        {
            CombatFeedbackId dashFeedbackId;

            switch (ActiveDashTargetType)
            {
                case TankAirDashTargetType.Enemy:
                {
                    dashFeedbackId =
                        CombatFeedbackId
                            .TankAirDashEnemy;

                    break;
                }

                case TankAirDashTargetType.World:
                {
                    dashFeedbackId =
                        CombatFeedbackId
                            .TankAirDashWorld;

                    break;
                }

                case TankAirDashTargetType.None:
                default:
                {
                    dashFeedbackId =
                        CombatFeedbackId.None;

                    break;
                }
            }

            if (dashFeedbackId !=
                CombatFeedbackId.None)
            {
                ownerCombatFeedbackRelay
                    .NotifyLocalAttackPerformed(
                        dashFeedbackId
                    );
            }
        }

        DashTimeoutTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                maximumDashDuration
            );

        if (debugTargetSelection)
        {
            Debug.Log(
                $"[Tank Air Dash] Dash Started" +
                $"\nTarget Type：{ActiveDashTargetType}" +
                $"\nEnemy：" +
                $"{(ActiveEnemyNetworkObject != null ? ActiveEnemyNetworkObject.name : "無")}" +
                $"\nStart：{currentPosition}" +
                $"\nTarget Anchor：{targetAnchor}" +
                $"\nDestination：{destination}" +
                $"\nTravel Distance：{travelDistance:F2}" +
                $"\nApproach Direction：{DashApproachDirection}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Dash Tick

    /// <summary>
    /// 每個 Fusion Tick
    /// 持續推進 Tank Air Dash。
    ///
    /// ====================================================================
    ///
    /// 這不是 Teleport。
    ///
    /// 而是：
    ///
    /// KCC
    /// ↓
    /// 3D Kinematic Velocity
    /// ↓
    /// 高速朝 Destination 移動。
    ///
    /// ====================================================================
    ///
    /// 因此仍保留 KCC 自己的：
    ///
    /// Collider
    /// Depenetration
    /// CCD
    /// Collision。
    /// </summary>
    private void TickDash()
    {
        if (ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            CompleteDash(
                TankAirDashCompletionReason.KCCMissing
            );

            return;
        }

        // =============================================================
        // Timeout
        // =============================================================

        if (DashTimeoutTimer
            .Expired(Runner))
        {
            CompleteDash(
                TankAirDashCompletionReason.Timeout
            );

            return;
        }

        // =============================================================
        // Current Position
        // =============================================================

        Vector3 currentPosition =
            ownerMovement
                .KCC
                .Data
                .TargetPosition;

        // =============================================================
        // Target Anchor
        // =============================================================

        /*
        * Enemy：
        *
        * 每 Tick 重新讀 Enemy NetworkObject 位置。
        *
        * 所以敵人在 Dash 過程中移動，
        * Tank 不會永遠飛向舊位置。
        *
        * ------------------------------------------------------------
        *
        * World：
        *
        * 使用施放瞬間保存的 Raycast Hit Point。
        */
        Vector3 targetAnchor =
            GetCurrentDashTargetAnchor();

        Vector3 destination =
            CalculateDashDestination(
                targetAnchor
            );

        Vector3 toDestination =
            destination -
            currentPosition;

        float remainingDistance =
            toDestination.magnitude;

        // =============================================================
        // 已到達
        // =============================================================

        if (remainingDistance <=
            dashArrivalDistance)
        {
            if (remainingDistance <=
                dashArrivalDistance)
            {
                CompleteDash(
                    TankAirDashCompletionReason.Arrived
                );

                return;
            }

            return;
        }

        // =============================================================
        // Dash Speed
        // =============================================================

        float speed;

        switch (ActiveDashTargetType)
        {
            case TankAirDashTargetType.Enemy:
            {
                speed =
                    enemyDashSpeed;

                break;
            }

            case TankAirDashTargetType.World:
            {
                speed =
                    worldDashSpeed;

                break;
            }

            case TankAirDashTargetType.None:
            default:
            {
                CompleteDash(
                    TankAirDashCompletionReason.InvalidTarget
                );

                return;
            }
        }

        speed =
            Mathf.Max(
                0.1f,
                speed
            );

        // =============================================================
        // 完整 3D Dash Direction
        // =============================================================

        Vector3 dashDirection =
            toDestination /
            remainingDistance;

        // =============================================================
        // 避免 Overshoot
        // =============================================================

        /*
        * 假設：
        *
        * 剩 0.3m
        * Dash Speed = 30m/s
        *
        * 下一 Tick 不應該直接衝過 Destination。
        *
        * 所以最後一 Tick
        * 會把速度限制成：
        *
        * 剩餘距離 ÷ DeltaTime。
        */
        float maximumSpeedWithoutOvershoot =
            remainingDistance /
            Mathf.Max(
                Runner.DeltaTime,
                0.0001f
            );

        float actualSpeed =
            Mathf.Min(
                speed,
                maximumSpeedWithoutOvershoot
            );

        Vector3 dashVelocity =
            dashDirection *
            actualSpeed;

        // =============================================================
        // ★ Dash 期間禁止 WASD 干擾
        // =============================================================

        /*
        * PlayerMovement 在這個 Tick
        * 已經先寫入普通 InputDirection。
        *
        * Tank Runtime 現在具有更高移動權限，
        * 所以把普通 WASD 清掉。
        */
        ownerMovement
            .KCC
            .SetInputDirection(
                Vector3.zero
            );

        // =============================================================
        // ★ 正式 3D KCC Velocity
        // =============================================================

        /*
        * 這裡不 ProjectOnPlane。
        *
        * 所以：
        *
        * 前
        * 後
        * 上
        * 下
        * 斜上
        * 斜下
        *
        * 全部都是合法 Dash。
        */
        ownerMovement
            .KCC
            .SetKinematicVelocity(
                dashVelocity
            );

        // =============================================================
        // Debug
        // =============================================================

        if (drawTargetDebug)
        {
            Debug.DrawLine(
                currentPosition,
                destination,
                ActiveDashTargetType ==
                    TankAirDashTargetType.Enemy
                        ? Color.red
                        : Color.cyan,
                Runner.DeltaTime * 2f
            );
        }
    }

    #endregion

    // =====================================================================
    #region Target Resolve

    /// <summary>
    /// 正式選擇這一次 Tank Air Dash Target。
    ///
    /// 優先級固定：
    ///
    /// Enemy
    /// ↓
    /// World
    /// ↓
    /// None。
    /// </summary>
    private void ResolveTarget()
    {
        ClearLastTarget();

        Vector3 origin =
            GetGameplayAimOrigin();

        Vector3 aimDirection =
            ownerMovement
                .GetAimDirection();

        if (aimDirection.sqrMagnitude <=
            0.0001f)
        {
            return;
        }

        aimDirection.Normalize();

        // =============================================================
        // Debug 原始準心
        // =============================================================

        if (drawTargetDebug)
        {
            Debug.DrawRay(
                origin,
                aimDirection *
                Mathf.Max(
                    enemySearchDistance,
                    worldSearchDistance
                ),
                Color.white,
                debugDrawDuration
            );
        }

        // =============================================================
        // 1. Enemy 優先
        // =============================================================

        if (TryResolveEnemyTarget(
                origin,
                aimDirection,
                out GrappleInteractionTarget enemyTarget,
                out Vector3 enemyTargetPoint,
                out float enemyAngle,
                out float enemyDistance
            ))
        {
            LastTargetType =
                TankAirDashTargetType.Enemy;

            LastEnemyTarget =
                enemyTarget;

            LastTargetPoint =
                enemyTargetPoint;

            LastEnemyAimAngle =
                enemyAngle;

            LastTargetDistance =
                enemyDistance;

            if (drawTargetDebug)
            {
                Debug.DrawLine(
                    origin,
                    enemyTargetPoint,
                    Color.red,
                    debugDrawDuration
                );
            }

            LogResolvedTarget();

            return;
        }

        // =============================================================
        // 2. World
        // =============================================================

        if (TryResolveWorldTarget(
                origin,
                aimDirection,
                out Vector3 worldPoint,
                out float worldDistance
            ))
        {
            LastTargetType =
                TankAirDashTargetType.World;

            LastTargetPoint =
                worldPoint;

            LastTargetDistance =
                worldDistance;

            if (drawTargetDebug)
            {
                Debug.DrawLine(
                    origin,
                    worldPoint,
                    Color.cyan,
                    debugDrawDuration
                );
            }

            LogResolvedTarget();

            return;
        }

        // =============================================================
        // 3. None
        // =============================================================

        LogResolvedTarget();
    }

    #endregion

    // =====================================================================
    #region Enemy Search

    /// <summary>
    /// 使用 Fusion Lag Compensation
    /// 寬鬆搜尋準心附近的 Enemy。
    ///
    /// ====================================================================
    ///
    /// 為什麼不用 PhysicsScene.OverlapSphere？
    ///
    /// Enemy 的正式戰鬥 HitMask
    /// 主要是 Fusion Hitbox。
    ///
    /// 普通 Unity Physics Overlap
    /// 不保證可以搜尋到 Fusion Hitbox。
    ///
    /// ====================================================================
    ///
    /// 排序：
    ///
    /// 1. Aim Angle 越小越好。
    /// 2. 如果角度非常接近，距離越近越好。
    ///
    /// ====================================================================
    ///
    /// 所以這不是：
    ///
    /// 找離玩家最近的怪。
    ///
    /// 而是：
    ///
    /// 找玩家最像正在瞄準的怪。
    /// </summary>
    private bool TryResolveEnemyTarget(
        Vector3 origin,
        Vector3 aimDirection,
        out GrappleInteractionTarget selectedTarget,
        out Vector3 selectedPoint,
        out float selectedAngle,
        out float selectedDistance
    )
    {
        selectedTarget =
            null;

        selectedPoint =
            default;

        selectedAngle =
            float.MaxValue;

        selectedDistance =
            float.MaxValue;

        // =============================================================
        // 基本檢查
        // =============================================================

        if (Runner == null ||
            Runner.LagCompensation == null ||
            ownerPlayerNetworkObject == null)
        {
            return false;
        }

        PlayerRef inputAuthority =
            ownerPlayerNetworkObject
                .InputAuthority;

        if (inputAuthority.IsNone)
        {
            return false;
        }

        enemyLagHits.Clear();

        // =============================================================
        // Hit Options
        // =============================================================

        /*
        * SubtickAccuracy：
        *
        * 高速 Grapple 過程中，
        * Enemy 的判定會更接近玩家真正看到的位置。
        *
        * ------------------------------------------------------------
        *
        * IgnoreInputAuthority：
        *
        * 不搜尋 Owner Player 自己的 Fusion Hitbox。
        *
        * ------------------------------------------------------------
        *
        * IncludePhysX：
        *
        * 如果目前測試 Enemy
        * 同時還有普通 Collider，
        * 也允許一起搜尋。
        */
        HitOptions hitOptions =
            HitOptions.SubtickAccuracy |
            HitOptions.IgnoreInputAuthority |
            HitOptions.IncludePhysX;

        // =============================================================
        // Fusion Overlap Sphere
        // =============================================================

        int rawHitCount =
            Runner.LagCompensation
                .OverlapSphere(
                    origin,
                    enemySearchDistance,
                    inputAuthority,
                    enemyLagHits,
                    enemySearchMask,
                    hitOptions,
                    true,
                    QueryTriggerInteraction.Collide
                );

        // =============================================================
        // Debug：非常重要
        // =============================================================

        if (debugTargetSelection)
        {
            Debug.Log(
                $"[Tank Air Dash Enemy Search]" +
                $"\nRaw Hits：{rawHitCount}" +
                $"\nSearch Distance：{enemySearchDistance:F2}" +
                $"\nSearch Angle：{enemySearchAngle:F2}" +
                $"\nEnemy Mask Value：{enemySearchMask.value}" +
                $"\nOrigin：{origin}" +
                $"\nAim Direction：{aimDirection}",
                this
            );
        }

        if (rawHitCount <= 0)
        {
            return false;
        }

        // =============================================================
        // Candidate
        // =============================================================

        for (int i = 0;
            i < enemyLagHits.Count;
            i++)
        {
            LagCompensatedHit hit =
                enemyLagHits[i];

            GameObject hitObject =
                hit.GameObject;

            if (hitObject == null)
            {
                continue;
            }

            // =========================================================
            // Grapple Gameplay Target
            // =========================================================

            GrappleInteractionTarget target =
                hitObject
                    .GetComponentInParent<
                        GrappleInteractionTarget
                    >();

            if (target == null)
            {
                continue;
            }

            // =========================================================
            // Enemy Only
            // =========================================================

            if (target.TargetType !=
                GrappleInteractionTargetType.Enemy)
            {
                continue;
            }

            // =========================================================
            // 死亡 / 不可互動
            // =========================================================

            if (target.IsInteractionAvailable ==
                false)
            {
                continue;
            }

            // =========================================================
            // 排除自己
            // =========================================================

            if (target.OwnerNetworkObject != null &&
                target.OwnerNetworkObject ==
                    ownerPlayerNetworkObject)
            {
                continue;
            }

            // =========================================================
            // Candidate Point
            // =========================================================

            /*
            * LagCompensation Overlap
            * 不需要再依靠 Collider Bounds。
            *
            * 我們直接使用這顆 Hitbox GameObject
            * 當下的世界位置作為準心候選點。
            *
            * --------------------------------------------------------
            *
            * 例如：
            *
            * Head Hitbox
            * Chest Hitbox
            *
            * 都可以各自參與角度競爭。
            */
            Vector3 candidatePoint =
                hitObject.transform.position;

            Vector3 toTarget =
                candidatePoint -
                origin;

            float distance =
                toTarget.magnitude;

            if (distance <= 0.0001f ||
                distance >
                    enemySearchDistance)
            {
                continue;
            }

            Vector3 direction =
                toTarget /
                distance;

            // =========================================================
            // Aim Angle
            // =========================================================

            float angle =
                Vector3.Angle(
                    aimDirection,
                    direction
                );

            if (angle >
                enemySearchAngle)
            {
                continue;
            }

            // =========================================================
            // Visibility
            // =========================================================

            if (CanSeeEnemyTarget(
                    origin,
                    candidatePoint,
                    distance,
                    target
                ) == false)
            {
                continue;
            }

            // =========================================================
            // Best Candidate
            // =========================================================

            bool angleIsBetter =
                angle <
                selectedAngle;

            /*
            * 不需要精確到完全相同角度。
            *
            * 0.25 度內視為幾乎一樣，
            * 這時改用距離決定。
            */
            bool similarAngleButCloser =
                Mathf.Abs(
                    angle -
                    selectedAngle
                ) <= 0.25f &&
                distance <
                    selectedDistance;

            if (angleIsBetter == false &&
                similarAngleButCloser == false)
            {
                continue;
            }

            selectedTarget =
                target;

            selectedPoint =
                candidatePoint;

            selectedAngle =
                angle;

            selectedDistance =
                distance;
        }

        // =============================================================
        // Debug Candidate Result
        // =============================================================

        if (debugTargetSelection)
        {
            Debug.Log(
                $"[Tank Air Dash Enemy Search Result]" +
                $"\nFound：{(selectedTarget != null)}" +
                $"\nEnemy：" +
                $"{(selectedTarget != null ? selectedTarget.name : "無")}" +
                $"\nAngle：" +
                $"{(selectedTarget != null ? selectedAngle : 0f):F2}" +
                $"\nDistance：" +
                $"{(selectedTarget != null ? selectedDistance : 0f):F2}",
                selectedTarget != null
                    ? selectedTarget
                    : this
            );
        }

        return
            selectedTarget != null;
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentPhase =
                TankAirDashPhase.Ready;

            ActiveDashTargetType =
                TankAirDashTargetType.None;

            ActiveDashTargetPoint =
                Vector3.zero;

            ActiveEnemyNetworkObject =
                null;

            DashApproachDirection =
                Vector3.zero;

            DashTimeoutTimer =
                TickTimer.None;

            AbilityCooldownTimer =
                TickTimer.None;

            AirStrikeSequence =
                0;

            AirDashSequence =
                0;
        }
    }

    #endregion

    // =====================================================================
    #region Enemy Visibility

    /// <summary>
    /// 確認 Enemy 沒有被牆壁擋住。
    ///
    /// ====================================================================
    ///
    /// 使用 Fusion Lag Compensation Raycast。
    ///
    /// Raycast 同時允許：
    ///
    /// Fusion Hitbox
    /// +
    /// PhysX World Collider。
    ///
    /// ====================================================================
    ///
    /// 因此可以判斷：
    ///
    /// Player
    /// ↓
    /// Enemy
    /// ↓
    /// Wall
    ///
    /// 第一個命中是 Enemy
    /// → 可見。
    ///
    /// ------------------------------------------------------------
    ///
    /// Player
    /// ↓
    /// Wall
    /// ↓
    /// Enemy
    ///
    /// 第一個命中是 Wall
    /// → 不可見。
    /// </summary>
    private bool CanSeeEnemyTarget(
        Vector3 origin,
        Vector3 candidatePoint,
        float candidateDistance,
        GrappleInteractionTarget expectedTarget
    )
    {
        if (Runner == null ||
            Runner.LagCompensation == null ||
            ownerPlayerNetworkObject == null ||
            expectedTarget == null)
        {
            return false;
        }

        Vector3 direction =
            candidatePoint -
            origin;

        if (direction.sqrMagnitude <=
            0.0001f)
        {
            return false;
        }

        direction.Normalize();

        PlayerRef inputAuthority =
            ownerPlayerNetworkObject
                .InputAuthority;

        if (inputAuthority.IsNone)
        {
            return false;
        }

        // =============================================================
        // Raycast
        // =============================================================

        HitOptions options =
            HitOptions.SubtickAccuracy |
            HitOptions.IgnoreInputAuthority |
            HitOptions.IncludePhysX;

        bool hasHit =
            Runner.LagCompensation
                .Raycast(
                    origin,
                    direction,
                    candidateDistance +
                        enemyVisibilityPadding,
                    inputAuthority,
                    out LagCompensatedHit hit,
                    enemyVisibilityMask,
                    options,
                    QueryTriggerInteraction.Ignore
                );

        if (hasHit == false)
        {
            return false;
        }

        GameObject hitObject =
            hit.GameObject;

        if (hitObject == null)
        {
            return false;
        }

        GrappleInteractionTarget hitTarget =
            hitObject
                .GetComponentInParent<
                    GrappleInteractionTarget
                >();

        /*
        * 第一個被看到的 Gameplay Enemy
        * 必須就是我們正在檢查的 Candidate。
        */
        return
            hitTarget ==
            expectedTarget;
    }

    #endregion

    // =====================================================================
    #region World Search

    /// <summary>
    /// 沒有合法 Enemy 時，
    /// 沿真正瞄準方向搜尋 World Dash Target。
    ///
    /// ------------------------------------------------------------
    ///
    /// World Geometry 使用普通 PhysX Collider。
    ///
    /// 所以 Fusion Raycast
    /// 必須包含：
    ///
    /// HitOptions.IncludePhysX。
    /// </summary>
    private bool TryResolveWorldTarget(
        Vector3 origin,
        Vector3 aimDirection,
        out Vector3 targetPoint,
        out float distance
    )
    {
        targetPoint =
            default;

        distance =
            0f;

        if (Runner == null ||
            Runner.LagCompensation == null ||
            ownerPlayerNetworkObject == null)
        {
            return false;
        }

        PlayerRef inputAuthority =
            ownerPlayerNetworkObject
                .InputAuthority;

        if (inputAuthority.IsNone)
        {
            return false;
        }

        HitOptions options =
            HitOptions.SubtickAccuracy |
            HitOptions.IgnoreInputAuthority |
            HitOptions.IncludePhysX;

        // =============================================================
        // World Ray
        // =============================================================

        bool hasHit =
            Runner.LagCompensation
                .Raycast(
                    origin,
                    aimDirection,
                    worldSearchDistance,
                    inputAuthority,
                    out LagCompensatedHit hit,
                    worldSearchMask,
                    options,
                    QueryTriggerInteraction.Ignore
                );

        // =============================================================
        // Debug
        // =============================================================

        if (debugTargetSelection)
        {
            Debug.Log(
                $"[Tank Air Dash World Search]" +
                $"\nHas Hit：{hasHit}" +
                $"\nWorld Mask Value：{worldSearchMask.value}" +
                $"\nDistance：{worldSearchDistance:F2}" +
                $"\nOrigin：{origin}" +
                $"\nDirection：{aimDirection}",
                this
            );
        }

        if (hasHit == false)
        {
            return false;
        }

        GameObject hitObject =
            hit.GameObject;

        if (hitObject == null)
        {
            return false;
        }

        // =============================================================
        // Enemy 不能被當 World
        // =============================================================

        GrappleInteractionTarget interactionTarget =
            hitObject
                .GetComponentInParent<
                    GrappleInteractionTarget
                >();

        if (interactionTarget != null &&
            interactionTarget.TargetType ==
                GrappleInteractionTargetType.Enemy)
        {
            return false;
        }

        // =============================================================
        // Result
        // =============================================================

        targetPoint =
            hit.Point;

        distance =
            Vector3.Distance(
                origin,
                targetPoint
            );

        return true;
    }

    #endregion

    // =====================================================================
    #region Dash 位移設定

    [Header("Enemy Dash 位移")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank 鎖定 Enemy 後進行特殊衝刺時的移動速度，單位為每秒 Unity 世界單位。這是完整 3D 速度，可以向上、向下或斜向飛行。第一輪建議先使用 30。")]
    private float enemyDashSpeed =
        30f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Enemy Dash 最後會停在 Enemy 前方多少距離。這個距離要替下一步的大範圍近戰留下空間。第一輪建議先使用 1.6。")]
    private float enemyStopDistance =
        1.6f;

    [SerializeField]
    [Tooltip("Enemy Dash 目前以 Enemy NetworkObject Root 作為真正到達基準。如果某類敵人的 Root 在腳底或其他特殊位置，可以使用這個 Y 偏移修正 Dash 到達高度。一般敵人先保持 0。")]
    private float enemyTargetHeightOffset =
        0f;


    [Header("World Dash 位移")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank 沒有找到 Enemy，而是鎖定牆壁、地板或其他 World Target 時的 3D 衝刺速度。第一輪建議先使用 28。")]
    private float worldDashSpeed =
        28f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("World Dash 不會把 KCC Root 直接塞進牆壁表面，而會提前多少距離停止。建議先使用 0.65。")]
    private float worldStopDistance =
        0.65f;


    [Header("Dash 結束判定")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank 與計算後的 Dash Destination 距離小於這個值時，視為成功到達並結束 Dash。建議先使用 0.2。")]
    private float dashArrivalDistance =
        0.2f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("目標如果近到連這個距離都不到，就不需要真的開始高速 Dash。主要避免玩家已經貼著牆或敵人時產生非常短的異常位移。")]
    private float minimumDashTravelDistance =
        0.25f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("單次 Air Dash 最多允許持續多久。這是安全保護，防止玩家因為牆壁阻擋、目標移動或其他碰撞問題而永久卡在 Dashing。第一輪建議 1.5 秒。")]
    private float maximumDashDuration =
        1.5f;

#endregion

    // =====================================================================
    #region Aim Origin

    /// <summary>
    /// 取得 Tank 特殊能力的 Gameplay 瞄準起點。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不使用第一人稱 Camera。
    ///
    /// 使用：
///
/// KCC TargetPosition
/// +
/// Aim Origin Height
/// +
/// Aim Direction Forward Offset。
///
/// ------------------------------------------------------------
///
/// 因此 Host / Client Gameplay
/// 不依賴本地 Presentation Transform。
    /// </summary>
    private Vector3 GetGameplayAimOrigin()
    {
        Vector3 aimDirection =
            ownerMovement != null
                ? ownerMovement
                    .GetAimDirection()
                : Vector3.forward;

        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            return
                ownerMovement
                    .KCC
                    .Data
                    .TargetPosition +
                Vector3.up *
                    aimOriginHeight +
                aimDirection *
                    aimOriginForwardOffset;
        }

        if (ownerPlayer != null)
        {
            return
                ownerPlayer.transform.position +
                Vector3.up *
                    aimOriginHeight +
                aimDirection *
                    aimOriginForwardOffset;
        }

        return
            transform.position +
            Vector3.up *
                aimOriginHeight;
    }

    #endregion

    // =====================================================================
    #region Dash Destination

    /// <summary>
    /// 取得這一次 Dash 現在真正追蹤的 Target Anchor。
    /// </summary>
    private Vector3 GetCurrentDashTargetAnchor()
    {
        // =============================================================
        // Enemy
        // =============================================================

        if (ActiveDashTargetType ==
                TankAirDashTargetType.Enemy &&
            ActiveEnemyNetworkObject != null &&
            ActiveEnemyNetworkObject.IsValid)
        {
            /*
            * Enemy Search 時用 Hitbox
            * 判斷「玩家正在瞄誰」。
            *
            * 真正飛行則改用 Enemy Root，
            * 避免某顆胸口 Hitbox
            * 讓 Player KCC Root 飛到過高的位置。
            */
            return
                ActiveEnemyNetworkObject
                    .transform
                    .position +
                Vector3.up *
                    enemyTargetHeightOffset;
        }

        // =============================================================
        // Snapshot Fallback / World
        // =============================================================

        return
            ActiveDashTargetPoint;
    }

    /// <summary>
    /// 根據 Target Anchor
    /// 計算 Player KCC 真正應該停止的位置。
    /// </summary>
    private Vector3 CalculateDashDestination(
        Vector3 targetAnchor
    )
    {
        float stopDistance;

        switch (ActiveDashTargetType)
        {
            case TankAirDashTargetType.Enemy:
            {
                stopDistance =
                    enemyStopDistance;

                break;
            }

            case TankAirDashTargetType.World:
            {
                stopDistance =
                    worldStopDistance;

                break;
            }

            case TankAirDashTargetType.None:
            default:
            {
                return
                    targetAnchor;
            }
        }

        Vector3 approachDirection =
            DashApproachDirection;

        if (approachDirection.sqrMagnitude <=
            0.0001f)
        {
            return
                targetAnchor;
        }

        return
            targetAnchor -
            approachDirection.normalized *
            Mathf.Max(
                0f,
                stopDistance
            );
    }

    #endregion

    // =====================================================================
    #region Dash Complete

    /// <summary>
    /// 結束目前 Air Dash。
    ///
    /// ------------------------------------------------------------
    ///
    /// Enemy + Arrived
    /// → 執行高傷害 AOE。
    ///
    /// World + Arrived
    /// → 只結束位移。
    ///
    /// Timeout / Invalid
    /// → 不造成 Enemy Arrival AOE。
    ///
    /// ------------------------------------------------------------
    ///
    /// Cooldown 已經在技能正式開始時啟動，
    /// 所以這裡不重新開始 CD。
    /// </summary>
    private void CompleteDash(
        TankAirDashCompletionReason reason
    )
    {
        TankAirDashTargetType completedType =
            ActiveDashTargetType;

        bool successfulArrival =
            reason ==
            TankAirDashCompletionReason.Arrived;

        // =============================================================
        // 清除 Dash 強制速度
        // =============================================================

        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            ownerMovement
                .KCC
                .SetKinematicVelocity(
                    Vector3.zero
                );
        }

        // =============================================================
        // ★ Enemy 成功抵達
        // =============================================================

        /*
        * 必須在 Reset State 前執行。
        *
        * 因為 PerformEnemyArrivalAttack()
        * 還需要：
        *
        * DashApproachDirection。
        */
        if (successfulArrival &&
            completedType ==
                TankAirDashTargetType.Enemy)
        {
            PerformEnemyArrivalAttack();
        }

        // =============================================================
        // 清除 Dash State
        // =============================================================

        CurrentPhase =
            TankAirDashPhase.Ready;

        ActiveDashTargetType =
            TankAirDashTargetType.None;

        ActiveDashTargetPoint =
            Vector3.zero;

        ActiveEnemyNetworkObject =
            null;

        DashApproachDirection =
            Vector3.zero;

        DashTimeoutTimer =
            TickTimer.None;

        // =============================================================
        // Debug
        // =============================================================

        if (debugTargetSelection)
        {
            Vector3 finalPosition =
                ownerMovement != null &&
                ownerMovement.KCC != null
                    ? ownerMovement
                        .KCC
                        .Data
                        .TargetPosition
                    : Vector3.zero;

            Debug.Log(
                $"[Tank Air Dash] Dash Completed" +
                $"\nTarget Type：{completedType}" +
                $"\nReason：{reason}" +
                $"\nSuccessful Arrival：{successfulArrival}" +
                $"\nFinal Position：{finalPosition}" +
                $"\nCooldown Remaining：{CooldownRemainingSeconds:F2}",
                this
            );
        }
    }

    /// <summary>
    /// 初始化或發生異常時清除 Dash 狀態。
    /// </summary>
    private void ResetDashState()
    {
        CurrentPhase =
            TankAirDashPhase.Ready;

        ActiveDashTargetType =
            TankAirDashTargetType.None;

        ActiveDashTargetPoint =
            Vector3.zero;

        ActiveEnemyNetworkObject =
            null;

        DashApproachDirection =
            Vector3.zero;

        DashTimeoutTimer =
            TickTimer.None;
    }

    #endregion

    /// <summary>
    /// Air Dash 特殊能力目前是否仍在冷卻。
    /// </summary>
    public bool IsOnCooldown
    {
        get
        {
            if (Runner == null)
            {
                return false;
            }

            return
                AbilityCooldownTimer
                    .ExpiredOrNotRunning(
                        Runner
                    ) == false;
        }
    }

    /// <summary>
    /// Air Dash 特殊能力剩餘冷卻秒數。
    ///
    /// 未來 HUD 可以直接使用。
    /// </summary>
    public float CooldownRemainingSeconds =>
        Runner != null
            ? AbilityCooldownTimer
                .RemainingTime(Runner)
                ?? 0f
            : 0f;
    
    // =====================================================================
    #region Enemy Arrival Damage Runtime

    /// <summary>
    /// 共用近戰 / 扇形傷害解析器。
    ///
    /// Tank 普通三段近戰與 Air Strike
    /// 現在可以使用同一套傷害標準。
    /// </summary>
    private readonly MeleeDamageResolver
        enemyArrivalDamageResolver =
            new MeleeDamageResolver();

    private readonly List<DamageResult>
        enemyArrivalResolvedResults =
            new List<DamageResult>(24);

    private readonly List<DamageResult>
        enemyArrivalConfirmedResults =
            new List<DamageResult>(24);

    /// <summary>
    /// Enemy Dash AOE 真正造成有效傷害時觸發。
    ///
    /// PlayerCombatFeedbackRelay
    /// 會接手 X、Camera Shake 等 Presentation。
    /// </summary>
    public event Action<DamageResult>
        DamageConfirmed;

    #endregion

    // =====================================================================
    #region Cooldown

    /// <summary>
    /// 根據這次真正成功發動的 Target Type
    /// 啟動對應冷卻。
    /// </summary>
    private void StartAbilityCooldown(
        TankAirDashTargetType targetType
    )
    {
        float duration;

        switch (targetType)
        {
            case TankAirDashTargetType.Enemy:
            {
                duration =
                    enemyDashCooldownDuration;

                break;
            }

            case TankAirDashTargetType.World:
            {
                duration =
                    worldDashCooldownDuration;

                break;
            }

            case TankAirDashTargetType.None:
            default:
            {
                return;
            }
        }

        if (duration <= 0f)
        {
            AbilityCooldownTimer =
                TickTimer.None;

            return;
        }

        AbilityCooldownTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                duration
            );

        if (debugTargetSelection)
        {
            Debug.Log(
                $"[Tank Air Dash] Cooldown Started" +
                $"\nTarget Type：{targetType}" +
                $"\nDuration：{duration:F2}",
                this
            );
        }
    }

    #endregion


    // =====================================================================
    #region Enemy Arrival Attack

    /// <summary>
    /// 由 State Authority 廣播一次 Air Strike 世界重擊聲。
    /// </summary>
    private void TryPlayAirStrikeWorldImpact(
        Vector3 worldPosition
    )
    {
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasStateAuthority == false ||
            airStrikeWorldImpactCue == null)
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
            .PlayWorldOneShotFromStateAuthority(
                airStrikeWorldImpactCue,
                worldPosition,
                airStrikeWorldImpactVolumeScale
            );
    }

    /// <summary>
    /// Tank Enemy Dash 成功抵達後，
    /// 沿衝刺方向執行一次高傷害 3D 扇形 AOE。
    ///
    /// ====================================================================
    ///
    /// 不限制目標數量。
    ///
    /// 10 隻敵人在範圍內：
    /// → 10 隻全部受到傷害。
    ///
    /// ====================================================================
    ///
    /// 攻擊方向使用：
    ///
    /// DashApproachDirection。
    ///
    /// 不使用玩家到達後的新 Camera Direction。
    /// </summary>
    private void PerformEnemyArrivalAttack()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (Runner == null ||
            ownerMovement == null ||
            ownerMovement.KCC == null ||
            ownerPlayerNetworkObject == null)
        {
            return;
        }

        // =============================================================
        // Attack Sequence
        // =============================================================

        AirStrikeSequence++;

        // =============================================================
        // Origin
        // =============================================================

        Vector3 origin =
            ownerMovement
                .KCC
                .Data
                .TargetPosition +
            Vector3.up *
                enemyArrivalOriginHeight;

        // =============================================================
        // Network World Impact Audio
        // =============================================================

        TryPlayAirStrikeWorldImpact(
            origin
        );

        // =============================================================
        // Full 3D Forward
        // =============================================================

        Vector3 forward =
            DashApproachDirection;

        if (forward.sqrMagnitude <=
            0.0001f)
        {
            /*
            * 理論上不應該發生。
            *
            * 真的失效時退回玩家完整 Aim Direction。
            */
            forward =
                ownerMovement
                    .GetAimDirection();
        }

        if (forward.sqrMagnitude <=
            0.0001f)
        {
            return;
        }

        forward.Normalize();

        // =============================================================
        // Query
        // =============================================================

        MeleeDamageQuery query =
            new MeleeDamageQuery
            {
                Runner =
                    Runner,

                Attacker =
                    ownerPlayerNetworkObject
                        .InputAuthority,

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
                    enemyArrivalDamage,

                /*
                * 目前仍屬於近距離衝撞 / 重擊型傷害。
                *
                * Hit Marker / Camera Feedback
                * 在沒有專屬 AirStrike 樣式前
                * 仍可以安全退回一般 Melee。
                */
                DamageType =
                    DamageType.Melee,

                FeedbackId =
                    CombatFeedbackId.TankAirStrike,

                Sequence =
                    AirStrikeSequence,

                /*
                 * Tank GrappleAirborne Enemy Arrival Attack
                 * 固定視為 Forced Headshot。
                 *
                 * 不額外乘傷害，enemyArrivalDamage 保持原值。
                 */
                ForcedHeadshotSource =
                    DamageForcedHeadshotSource
                        .TankAirStrike,

                Range =
                    enemyArrivalRange,

                SearchRadius =
                    enemyArrivalSearchRadius,

                FullAttackAngle =
                    enemyArrivalFullAttackAngle,

                /*
                * ★ 0 = 不限制目標數。
                */
                MaximumTargets =
                    0,

                HitMask =
                    enemyArrivalHitMask,

                UseSubtickAccuracy =
                    true,

                IncludePhysX =
                    true,

                RequireLineOfSight =
                    enemyArrivalRequireLineOfSight,

                ObstructionMask =
                    enemyArrivalObstructionMask,

                ObstructionRayStartOffset =
                    enemyArrivalObstructionRayStartOffset
            };

        MeleeDamageSummary summary =
            enemyArrivalDamageResolver
                .Resolve(
                    query,
                    enemyArrivalResolvedResults,
                    enemyArrivalConfirmedResults
                );

        // =============================================================
        // Combat Feedback
        // =============================================================

        DamageResult? feedbackResult =
            SelectEnemyArrivalFeedbackResult();

        if (feedbackResult.HasValue)
        {
            /*
            * 即使 AOE 打到很多敵人，
            *
            * 一次技能仍然只播放一次：
            *
            * X
            * Camera Shake。
            *
            * --------------------------------------------------------
            *
            * 如果其中任何一隻被擊殺，
            * Kill Feedback 仍然最高優先。
            */
            DamageConfirmed?.Invoke(
                feedbackResult.Value
            );
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugTargetSelection)
        {
            Debug.Log(
                $"[Tank Air Dash] Enemy Arrival AOE" +
                $"\nSequence：{AirStrikeSequence}" +
                $"\nDamage：{enemyArrivalDamage:F2}" +
                $"\nRange：{enemyArrivalRange:F2}" +
                $"\nFull Angle：{enemyArrivalFullAttackAngle:F1}" +
                $"\nRaw Hits：{summary.RawHitCount}" +
                $"\nCandidates：{summary.CandidateCount}" +
                $"\nAttempted：{summary.AttemptedTargetCount}" +
                $"\nConfirmed：{summary.ConfirmedDamageCount}" +
                $"\nKills：{summary.KillCount}" +
                $"\nForward：{forward}",
                this
            );
        }
    }

    /// <summary>
    /// 從一次無上限 AOE 的所有有效 DamageResult 中，
    /// 選出只播放一次的 Presentation Result。
    ///
    /// Kill 永遠最高優先。
    /// </summary>
    private DamageResult?
        SelectEnemyArrivalFeedbackResult()
    {
        if (enemyArrivalConfirmedResults.Count <= 0)
        {
            return null;
        }

        // Kill
        for (int i = 0;
            i < enemyArrivalConfirmedResults.Count;
            i++)
        {
            if (enemyArrivalConfirmedResults[i]
                .KilledTarget)
            {
                return
                    enemyArrivalConfirmedResults[i];
            }
        }

        // Headshot
        for (int i = 0;
            i < enemyArrivalConfirmedResults.Count;
            i++)
        {
            if (enemyArrivalConfirmedResults[i]
                .IsHeadshot)
            {
                return
                    enemyArrivalConfirmedResults[i];
            }
        }

        // Normal Hit
        return
            enemyArrivalConfirmedResults[0];
    }

    #endregion
    // =====================================================================
    #region Clear / Debug

    private void ClearLastTarget()
    {
        LastTargetType =
            TankAirDashTargetType.None;

        LastEnemyTarget =
            null;

        LastTargetPoint =
            default;

        LastEnemyAimAngle =
            0f;

        LastTargetDistance =
            0f;
    }

    private void LogResolvedTarget()
    {
        if (debugTargetSelection ==
            false)
        {
            return;
        }

        string enemyName =
            LastEnemyTarget != null
                ? LastEnemyTarget.name
                : "無";

        Debug.Log(
            $"[Tank Air Dash Target]" +
            $"\nMovement State：{ownerStateMachine.CurrentState}" +
            $"\nTarget Type：{LastTargetType}" +
            $"\nEnemy：{enemyName}" +
            $"\nAim Angle：{LastEnemyAimAngle:F2}" +
            $"\nDistance：{LastTargetDistance:F2}" +
            $"\nTarget Point：{LastTargetPoint}",
            LastEnemyTarget != null
                ? LastEnemyTarget
                : this
        );
    }

    #endregion
}