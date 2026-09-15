using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Tank 職業 F Quick Action：短距離衝撞。
///
/// ====================================================================
///
/// F
/// ↓
/// 如果正在 Guard
/// → 先結束 Guard
/// ↓
/// 消耗 1 Charge
/// ↓
/// 沿玩家水平面向快速 Dash
/// ↓
/// Dash 路徑碰到 Enemy HitMask
/// → 每個 Enemy 最多受到一次傷害
///
/// ====================================================================
///
/// 正式規則：
///
/// 最大 Charge：3
///
/// 每使用一次：
/// -1 Charge
///
/// 每少一格：
/// 每 5 秒依序恢復 1 Charge
///
/// 擊殺 Enemy：
/// +1 Charge
///
/// ====================================================================
///
/// 重要：
///
/// 這個 F Dash 沒有：
///
/// Startup 僵直
/// Recovery 僵直
/// 攻擊動畫。
///
/// 所以：
///
/// F 本身不會長時間禁止玩家攻擊。
///
/// ====================================================================
///
/// 但是：
///
/// Tank 正在普通近戰攻擊僵直時
/// → 不可以用 F 取消攻擊。
///
/// Tank Air Dash 中
/// → 不可以使用。
///
/// Tank Grapple Gather Dash 中
/// → 不可以使用。
///
/// Guard 中
/// → 可以使用
/// → Guard 先結束
/// → F Dash。
///
/// ====================================================================
///
/// 傷害只由 State Authority 正式結算。
/// </summary>
[DisallowMultipleComponent]
public class TankQuickDashAbility :
    NetworkBehaviour,
    IPlayerQuickActionAbility,
    ICombatDamageFeedbackSource
{
    // =====================================================================
    #region Owner Player


    [Header("Owner Player Binding")]


    [SerializeField]
    [Tooltip("這個 Tank Quick Dash 真正所屬的 Player Core。正常情況不需要手動指定，由 TankProfessionRuntimeDriver 在 Runtime Spawn 後自動綁定。")]
    private Player ownerPlayer;

    /// <summary>
    /// 真正掛在 Player Network Prefab Root 的網路世界聲 Emitter。
    ///
    /// Tank Runtime 本身不新增 Emitter，
    /// 否則會讓每個職業 Runtime 各自擁有一套世界聲網路節點。
    /// </summary>
    private NetworkPlayerAudioEmitter
        networkAudioEmitter;

    /// <summary>
    /// Owner Player NetworkObject。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;


    /// <summary>
    /// 玩家共用 Movement。
    ///
    /// 用來取得：
    ///
    /// KCC
    /// Gameplay Aim Direction。
    /// </summary>
    private PlayerMovement
        ownerMovement;

    /// <summary>
    /// Owner Player 的移動狀態機。
    ///
    /// Tank F Quick Dash 會用它確認玩家目前是否：
    ///
    /// Airborne
    /// Grappling
    /// GrappleAirborne。
    ///
    /// 以上狀態都禁止使用 F。
    /// </summary>
    private PlayerStateMachine
        ownerStateMachine;


    /// <summary>
    /// Tank Guard。
    ///
    /// F：
    ///
    /// Guard
    /// ↓
    /// End Guard
    /// ↓
    /// Dash。
    /// </summary>
    private TankGuardAbility
        guardAbility;


    /// <summary>
    /// Tank 普通三段近戰。
    ///
    /// 攻擊僵直期間不能被 F Dash 取消。
    /// </summary>
    private TankMeleeCombo
        meleeCombo;


    /// <summary>
    /// Tank GrappleAirborne 右鍵特殊 Dash。
    /// </summary>
    private TankAirDashAbility
        airDashAbility;


    /// <summary>
    /// Tank Grapple Gather 玩家 Dash。
    /// </summary>
    private TankGrappleGatherAbility
        grappleGatherAbility;


    #endregion


    // =====================================================================
    #region Charge


    [Header("F Dash 充能")]


    [SerializeField]
    [Range(1, 10)]
    [Tooltip("Tank F Dash 最大充能數。目前正式設計為 3 格。每次成功開始 Dash 消耗 1 格。")]
    private int maximumCharges =
        3;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("當 F Dash Charge 未滿時，每恢復 1 格需要多少秒。目前正式設計為 5 秒。恢復方式為依序恢復，例如 0 格時需要 5 秒回到 1，再過 5 秒回到 2。")]
    private float rechargeDuration =
        5f;


    #endregion


    // =====================================================================
    #region Dash Movement


    [Header("F Dash 位移")]


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank F Dash 的移動速度，單位為每秒 Unity 世界單位。這不是一般移動速度，而是 Dash 期間直接交給 KCC 的高速位移。第一輪建議使用 24。")]
    private float dashSpeed =
        24f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Tank 每一次 F Dash 預計向前移動的總距離。這是『小衝刺』，第一輪建議使用 3。碰到牆壁時 KCC 仍然會阻止玩家穿過牆壁。")]
    private float dashDistance =
        3f;


    #endregion

    // =====================================================================
    #region Quick Dash World Audio

    [Header("Quick Dash 世界聲音")]

    [SerializeField]
    [Tooltip(
        "Tank F Quick Dash 成功消耗 Charge 並正式開始位移的瞬間，" +
        "由 State Authority 廣播給所有 Client 的 3D 世界衝刺聲。\n\n" +
        "這是動作啟動聲，不是命中聲；即使 Dash 沒有撞到 Enemy 也會播放。\n\n" +
        "必須指定 Network ID 大於 0、且已加入 GameplayAudioCatalog 的 Cue。")]
    private GameplayAudioCue
        quickDashWorldStartCue;

    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Quick Dash 世界啟動聲的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0 = 靜音。")]
    private float quickDashWorldStartVolumeScale =
        1f;

    #endregion

    // =====================================================================
    #region Dash Damage


    [Header("F Dash 衝撞傷害")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank F Dash 撞到每一個合法 Enemy 時造成的基礎傷害。每一名 Enemy 在同一次 Dash 中最多只會受到一次傷害。第一輪建議先使用 30。")]
    private float dashDamage =
        30f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Dash 傷害掃掠區域的完整寬度。這不是半徑。1.4 代表玩家左右總共約 1.4 單位寬。")]
    private float dashDamageWidth =
        1.4f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Dash 傷害掃掠區域的完整高度。判定 Box 會從玩家腳底附近向上覆蓋這個高度。第一輪建議使用 1.8。")]
    private float dashDamageHeight =
        1.8f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("每一個 Tick 的 Dash 傷害 Box 在前後方向額外增加多少安全距離，用來避免高速移動時因 Tick 間隙漏掉剛好擦過的 Enemy。建議 0.15。")]
    private float dashDamageForwardPadding =
        0.15f;


    [SerializeField]
    [Tooltip("Tank F Dash 可以撞擊哪些 Layer。請指定真正 Enemy Fusion Hitbox 所使用的 HitMask Layer。")]
    private LayerMask dashHitMask =
        ~0;


    #endregion


    // =====================================================================
    #region Lag Compensation


    [Header("Fusion Hit Detection")]


    [SerializeField]
    [Tooltip("開啟後，F Dash 的碰撞傷害搜尋使用 Fusion Subtick Accuracy。高速移動技能建議保持開啟。")]
    private bool useSubtickAccuracy =
        true;


    [SerializeField]
    [Tooltip("開啟後，Dash 傷害搜尋除了 Fusion Hitbox，也會包含普通 PhysX Collider。測試階段可以保持開啟。")]
    private bool includePhysX =
        true;


    #endregion


    // =====================================================================
    #region Obstruction


    [Header("衝撞障礙物")]


    [SerializeField]
    [Tooltip("開啟後，在正式造成 F Dash 傷害以前會檢查玩家與 Enemy 之間是否隔著牆壁，避免高速掃掠 Box 穿過薄牆傷到牆後敵人。")]
    private bool requireLineOfSight =
        true;


    [SerializeField]
    [Tooltip("哪些 Layer 可以阻擋 F Dash 衝撞傷害。建議只包含 Ground、Wall、WorldGeometry 等場景障礙物，不要包含 Enemy HitMask。")]
    private LayerMask obstructionMask =
        0;


    [SerializeField]
    [Min(0f)]
    [Tooltip("障礙物 Raycast 從玩家位置往前偏移多少距離，避免射線從玩家自己碰撞體內開始。")]
    private float obstructionRayStartOffset =
        0.05f;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後會顯示 F Dash 開始、結束、Charge 消耗、Charge 恢復、命中 Enemy 與擊殺回充等資訊。測試階段建議保持開啟。")]
    private bool debugQuickDash =
        true;


    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出 Dash 路徑與每 Tick 傷害掃掠方向。")]
    private bool debugDrawDash =
        true;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Debug 線段在 Scene View 保留多久，單位為秒。")]
    private float debugDrawDuration =
        0.3f;


    #endregion

    // =====================================================================
    #region Post Dash Recovery

    [Header("Dash 結束後僵直")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank F Quick Dash 真正結束後，禁止普通攻擊、防禦與再次 F Dash 的時間，單位為秒。這段時間從 Dash 因距離完成、碰撞或強制中止的當下才開始計算；不會封鎖 WASD、Look 或 Grapple。正式初始值為 0.5 秒。")]
    private float postDashRecoveryDuration =
        0.5f;

    #endregion

    // =====================================================================
    #region Network State


    /// <summary>
    /// 目前剩餘 F Dash Charge。
    /// </summary>
    [Networked]
    public int CurrentCharges
    {
        get;
        private set;
    }


    /// <summary>
    /// 下一格 Charge 的恢復 Timer。
    ///
    /// Charge 是依序恢復，
    /// 與目前 Grapple Charge 概念一致。
    /// </summary>
    [Networked]
    private TickTimer RechargeTimer
    {
        get;
        set;
    }


    /// <summary>
    /// 玩家是否正在執行 F Dash 位移。
    /// </summary>
    [Networked]
    private NetworkBool DashActive
    {
        get;
        set;
    }


    /// <summary>
    /// 這次 F Dash 固定使用的世界方向。
    ///
    /// Dash 開始後不會因玩家甩動鏡頭而改方向。
    /// </summary>
    [Networked]
    private Vector3 DashDirection
    {
        get;
        set;
    }


    /// <summary>
    /// 這次 Dash 還剩多少預計位移距離。
    /// </summary>
    [Networked]
    private float DashRemainingDistance
    {
        get;
        set;
    }


    /// <summary>
    /// 這次 Dash 使用的攻擊 Sequence。
    ///
    /// 直接沿用 PlayerQuickActionController
    /// 給進來的 Activation Sequence。
    /// </summary>
    [Networked]
    private int DashSequence
    {
        get;
        set;
    }

    /// <summary>
    /// Dash 真正結束後的攻防僵直 Timer。
    ///
    /// 這顆 Timer 與 PlayerQuickActionController 的 PhaseTimer 分開，
    /// 因為 Quick Action Slot 早已結束，但 Dash Runtime 位移仍可能持續。
    /// </summary>
    [Networked]
    private TickTimer PostDashRecoveryTimer
    {
        get;
        set;
    }

    #endregion


    // =====================================================================
    #region Runtime Cache


    /// <summary>
    /// 每 Tick Fusion OverlapBox 結果。
    /// </summary>
    private readonly List<LagCompensatedHit>
        dashHits =
            new List<LagCompensatedHit>(32);


    /// <summary>
    /// 同一次 Dash 已經受過傷害的 Enemy。
    ///
    /// 防止：
    ///
    /// Enemy 有多顆 Hitbox
    ///
    /// 或
    ///
    /// Dash 持續多個 Tick
    ///
    /// 導致同一 Enemy 被重複扣血。
    /// </summary>
    private readonly HashSet<NetworkObject>
        damagedTargetsThisDash =
            new HashSet<NetworkObject>();


    #endregion


    // =====================================================================
    #region Combat Feedback Event


    /// <summary>
    /// 所有完成 Damage Pipeline 的結果。
    /// </summary>
    public event Action<DamageResult>
        DamageResolved;


    /// <summary>
    /// 只有正式 Accepted Damage
    /// 才會送進統一 Combat Feedback Pipeline。
    /// </summary>
    public event Action<DamageResult>
        DamageConfirmed;


    #endregion


    // =====================================================================
    #region Public State


    /// <summary>
    /// F Dash 是否正在移動。
    /// </summary>
    public bool IsDashing =>
        DashActive;

    /// <summary>
    /// Dash 已經停止，但仍在 Dash 後 0.5 秒攻防僵直。
    /// </summary>
    public bool IsPostDashRecoveryLocked
    {
        get
        {
            if (Runner == null)
            {
                return false;
            }

            return
                PostDashRecoveryTimer
                    .ExpiredOrNotRunning(
                        Runner
                    ) == false;
        }
    }

    /// <summary>
    /// Tank Quick Dash 是否正在禁止普通攻擊與防禦。
    ///
    /// 包含：
    /// 1. Dash 位移期間。
    /// 2. Dash 結束後的 Post Dash Recovery。
    ///
    /// Guard、Melee 與 Runtime Driver 都應讀這一個統一入口，
    /// 不要各自重新計算 Timer。
    /// </summary>
    public bool IsCombatActionLocked =>
        IsDashing ||
        IsPostDashRecoveryLocked;

    /// <summary>
    /// Dash 後攻防僵直剩餘秒數，只供 UI 與 Debug 顯示。
    /// </summary>
    public float PostDashRecoveryRemainingSeconds =>
        Runner != null
            ? PostDashRecoveryTimer
                .RemainingTime(Runner) ?? 0f
            : 0f;

    /// <summary>
    /// 最大 Charge。
    /// </summary>
    public int MaximumCharges =>
        Mathf.Max(
            1,
            maximumCharges
        );


    /// <summary>
    /// 是否至少還有一格 F Dash。
    /// </summary>
    public bool HasCharge =>
        CurrentCharges > 0;


    /// <summary>
    /// 下一格 Charge 剩餘恢復秒數。
    ///
    /// 未來 UI 可以直接使用。
    /// </summary>
    public float RechargeRemainingSeconds =>
        Runner != null
            ? RechargeTimer
                .RemainingTime(Runner)
                ?? 0f
            : 0f;


    #endregion


    // =====================================================================
    #region Binding


    /// <summary>
    /// Tank Runtime Spawn 後，
    /// 由 TankProfessionRuntimeDriver
    /// 綁定真正 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        ownerPlayerNetworkObject =
            null;

        ownerMovement =
            null;

        ownerStateMachine =
            null;

        networkAudioEmitter =
            null;

        if (ownerPlayer == null)
        {
            return;
        }


        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        networkAudioEmitter =
            ownerPlayer.GetComponent<
                NetworkPlayerAudioEmitter
            >();

        ownerMovement =
            ownerPlayer.Movement;

        ownerStateMachine =
            ownerPlayer.StateMachine;

        /*
         * 這些模組全部跟 Quick Dash
         * 位於同一個 Tank Profession Runtime。
         */
        guardAbility =
            GetComponent<TankGuardAbility>();


        meleeCombo =
            GetComponent<TankMeleeCombo>();


        airDashAbility =
            GetComponent<TankAirDashAbility>();


        grappleGatherAbility =
            GetComponent<
                TankGrappleGatherAbility
            >();

        /*
         * Air Dash / Gather 已可由玩家 Loadout 獨立生成，不一定與
         * Tank Quick Dash 位於同一個 Profession Runtime。只有找不到
         * 舊同 Root 元件時才查 Loadout，維持資產遷移前的相容。
         */
        PlayerAbilityRuntimeManager abilityManager =
            ownerPlayer.AbilityRuntimeManager;

        if (airDashAbility == null &&
            abilityManager != null)
        {
            abilityManager.TryGetActiveModule(
                out airDashAbility
            );
        }

        if (grappleGatherAbility == null &&
            abilityManager != null)
        {
            abilityManager.TryGetActiveModule(
                out grappleGatherAbility
            );
        }


        if (ownerMovement == null)
        {
            Debug.LogError(
                $"[{nameof(TankQuickDashAbility)}] " +
                $"Owner Player 找不到 PlayerMovement。",
                ownerPlayer
            );
        }

        if (ownerStateMachine == null)
        {
            Debug.LogError(
                $"[{nameof(TankQuickDashAbility)}] " +
                $"Owner Player 找不到 PlayerStateMachine。" +
                $"\nTank F Quick Dash 無法確認玩家目前是否在空中或勾索狀態。",
                ownerPlayer
            );
        }

        if (networkAudioEmitter == null)
        {
            Debug.LogError(
                $"[{nameof(TankQuickDashAbility)}] " +
                $"Owner Player Root 找不到 " +
                $"{nameof(NetworkPlayerAudioEmitter)}，" +
                $"Quick Dash 仍可移動與造成傷害，" +
                $"但其他玩家聽不到世界衝刺聲。",
                ownerPlayer
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
            CurrentCharges =
                MaximumCharges;


            RechargeTimer =
                TickTimer.None;


            DashActive =
                false;


            DashDirection =
                Vector3.zero;


            DashRemainingDistance =
                0f;


            DashSequence =
                0;
            
            PostDashRecoveryTimer =
                TickTimer.None;
        }
    }


    #endregion


    // =====================================================================
    #region IPlayerQuickActionAbility


    /// <summary>
    /// 這個 F Ability 屬於 Tank。
    /// </summary>
    public PlayerProfessionType Profession =>
        PlayerProfessionType.Tank;


    /*
     * Tank F Dash 沒有動畫前搖、Active 僵直、Recovery。
     *
     * PlayerQuickActionController
     * 只負責在這個 Tick 正式觸發能力。
     *
     * 真正 Dash Movement
     * 會繼續由 Tank Runtime 更新。
     */
    public float StartupDuration =>
        0f;


    public float ActiveDuration =>
        0f;


    public float RecoveryDuration =>
        0f;


    /// <summary>
    /// 是否允許開始 Tank F Dash。
    /// </summary>
    public bool CanStartQuickAction()
    {
        // -------------------------------------------------------------
        // Binding
        // -------------------------------------------------------------

        if (ownerPlayer == null ||
            ownerMovement == null ||
            ownerMovement.KCC == null||
            ownerStateMachine == null)
        {
            return false;
        }

        PlayerAbilityRuntimeManager abilityManager =
            ownerPlayer.AbilityRuntimeManager;

        if (abilityManager != null)
        {
            if (abilityManager.TryGetActiveModule(
                    out TankAirDashAbility loadoutAirDash
                ))
            {
                airDashAbility =
                    loadoutAirDash;
            }

            if (abilityManager.TryGetActiveModule(
                    out TankGrappleGatherAbility loadoutGather
                ))
            {
                grappleGatherAbility =
                    loadoutGather;
            }
        }

        // -------------------------------------------------------------
        // ★ 空中 / 勾索狀態禁止 F
        // -------------------------------------------------------------

        /*
        * Tank F 是地面用的短距離衝撞。
        *
        * ------------------------------------------------------------
        *
        * 禁止：
        *
        * Airborne
        * → 普通跳躍或掉落中的空中狀態。
        *
        * Grappling
        * → 勾索目前仍然連接並拉動玩家。
        *
        * GrappleAirborne
        * → 勾索結束後保留 Momentum 的特殊滯空狀態。
        *
        * ------------------------------------------------------------
        *
        * 特別注意：
        *
        * GrappleAirborne 的右鍵
        * 已經有 Tank Air Dash Special。
        *
        * 如果這時還允許 F，
        * 就會同時存在兩套高權限空中位移能力，
        * 很容易互相搶 KCC Velocity。
        */
        PlayerMovementState movementState =
            ownerStateMachine.CurrentState;

        if (movementState ==
                PlayerMovementState.Airborne ||
            movementState ==
                PlayerMovementState.Grappling ||
            movementState ==
                PlayerMovementState.GrappleAirborne)
        {
            return false;
        }


        // -------------------------------------------------------------
        // 沒 Charge
        // -------------------------------------------------------------

        if (HasCharge == false)
        {
            return false;
        }


        // -------------------------------------------------------------
        // 不能在自己的 Dash 中再開一次
        // -------------------------------------------------------------

        if (IsDashing)
        {
            return false;
        }

        // -------------------------------------------------------------
        // Dash 結束後攻防僵直期間不能再次 F Dash
        // -------------------------------------------------------------

        if (IsPostDashRecoveryLocked)
        {
            return false;
        }

        // -------------------------------------------------------------
        // ★ 普通 Melee 不可被 F 取消
        // -------------------------------------------------------------

        if (meleeCombo != null &&
            meleeCombo.IsAttackLocked)
        {
            return false;
        }


        // -------------------------------------------------------------
        // Tank Air Special 已經擁有高權限位移
        // -------------------------------------------------------------

        if (airDashAbility != null &&
            airDashAbility.IsDashing)
        {
            return false;
        }


        // -------------------------------------------------------------
        // Grapple Gather Dash 已經擁有高權限位移
        // -------------------------------------------------------------

        if (grappleGatherAbility != null &&
            grappleGatherAbility
                .IsPlayerGatherDashing)
        {
            return false;
        }


        /*
         * Guard 不在這裡擋。
         *
         * 因為正式規則就是：
         *
         * F
         * ↓
         * End Guard
         * ↓
         * Dash。
         */


        return true;
    }


    /// <summary>
    /// F 成功開始。
    ///
    /// Guard 在這個時間點先被關掉。
    /// </summary>
    public void OnQuickActionStarted(
        int activationSequence
    )
    {
        // =============================================================
        // Guard → End → Dash
        // =============================================================

        if (guardAbility != null &&
            guardAbility.IsGuarding)
        {
            guardAbility
                .ForceEndGuardForQuickAction();
        }


        if (debugQuickDash &&
            Object != null &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Quick Dash] Quick Action Started" +
                $"\nSequence：{activationSequence}" +
                $"\nCurrent Charges：{CurrentCharges}/{MaximumCharges}",
                this
            );
        }
    }


    /// <summary>
    /// 進入 Active 的瞬間真正消耗 Charge
    /// 並開始 Dash。
    /// </summary>
    public void OnQuickActionActive(
        int activationSequence
    )
    {
        /*
         * Gameplay State 與 Damage
         * 只由 State Authority 正式建立。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        StartDash(
            activationSequence
        );
    }


    /// <summary>
    /// QuickActionController 本身這個 Tick
    /// 已經結束 Slot 流程。
    ///
    /// 真正 Dash 位移仍由 IsDashing 繼續控制。
    /// </summary>
    public void OnQuickActionFinished(
        int activationSequence
    )
    {
        if (debugQuickDash &&
            Object != null &&
            Object.HasStateAuthority)
        {
            Debug.Log(
                $"[Tank Quick Dash] Quick Action Slot Finished" +
                $"\nSequence：{activationSequence}" +
                $"\nDash Active：{IsDashing}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Runtime Tick


    /// <summary>
    /// 每個 Fusion Tick
    /// 由 TankProfessionRuntimeDriver 呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這裡處理：
///
/// Charge Recharge
/// Dash Movement。
    /// </summary>
    public void SimulateRuntime()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        TickRecharge();

        TickPostDashRecovery();


        if (IsDashing)
        {
            TickDash();
        }
    }


    #endregion


    // =====================================================================
    #region Charge


    /// <summary>
    /// 正式消耗一格 F Dash。
    /// </summary>
    private bool ConsumeCharge()
    {
        if (CurrentCharges <= 0)
        {
            return false;
        }


        CurrentCharges--;


        /*
         * 從「滿格 → 少一格」的瞬間
         * 啟動第一個 5 秒 Timer。
         */
        if (CurrentCharges <
                MaximumCharges &&
            RechargeTimer
                .ExpiredOrNotRunning(
                    Runner
                ))
        {
            RechargeTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    rechargeDuration
                );
        }


        if (debugQuickDash)
        {
            Debug.Log(
                $"[Tank Quick Dash] Consume Charge" +
                $"\nCharges：{CurrentCharges}/{MaximumCharges}" +
                $"\nNext Recharge：{rechargeDuration:F2} 秒",
                this
            );
        }


        return true;
    }


    /// <summary>
    /// 每 5 秒依序恢復一格。
    /// </summary>
    private void TickRecharge()
    {
        // =============================================================
        // 已滿
        // =============================================================

        if (CurrentCharges >=
            MaximumCharges)
        {
            CurrentCharges =
                MaximumCharges;


            RechargeTimer =
                TickTimer.None;


            return;
        }


        // =============================================================
        // Timer 還沒開始
        // =============================================================

        if (RechargeTimer
            .ExpiredOrNotRunning(Runner))
        {
            /*
             * 如果是真的「已到期」，
             * 先恢復一格。
             */
            if (RechargeTimer
                .Expired(Runner))
            {
                CurrentCharges =
                    Mathf.Min(
                        MaximumCharges,
                        CurrentCharges + 1
                    );


                if (debugQuickDash)
                {
                    Debug.Log(
                        $"[Tank Quick Dash] Recharge +1" +
                        $"\nCharges：{CurrentCharges}/{MaximumCharges}",
                        this
                    );
                }


                if (CurrentCharges >=
                    MaximumCharges)
                {
                    RechargeTimer =
                        TickTimer.None;


                    return;
                }
            }


            /*
             * 還沒滿：
             *
             * 下一格重新倒數 5 秒。
             */
            RechargeTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    rechargeDuration
                );
        }
    }


    /// <summary>
    /// 外部 Gameplay Reward 恢復 Charge。
    ///
    /// Tank Quick Dash 擊殺時使用。
    /// </summary>
    public bool RestoreCharge(
        int amount
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return false;
        }


        if (amount <= 0 ||
            CurrentCharges >=
                MaximumCharges)
        {
            return false;
        }


        int previous =
            CurrentCharges;


        CurrentCharges =
            Mathf.Min(
                MaximumCharges,
                CurrentCharges +
                amount
            );


        if (CurrentCharges >=
            MaximumCharges)
        {
            RechargeTimer =
                TickTimer.None;
        }
        else if (RechargeTimer
            .ExpiredOrNotRunning(Runner))
        {
            RechargeTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    rechargeDuration
                );
        }


        if (debugQuickDash)
        {
            Debug.Log(
                $"[Tank Quick Dash] Restore Charge" +
                $"\nBefore：{previous}" +
                $"\nAfter：{CurrentCharges}" +
                $"\nMaximum：{MaximumCharges}",
                this
            );
        }


        return
            CurrentCharges >
            previous;
    }


    #endregion


    // =====================================================================
    #region Dash Start


    /// <summary>
    /// 正式開始一次 F Dash。
    /// </summary>
    private void StartDash(
        int activationSequence
    )
    {
        if (CanStartQuickAction() ==
            false)
        {
            return;
        }


        if (ConsumeCharge() ==
            false)
        {
            return;
        }


        // =============================================================
        // 清除前一個 Grapple Release Momentum
        // =============================================================

        /*
         * 不取消目前真的還 Attached 的 Grapple。
         *
         * 只清理舊的 Release Momentum，
         * 避免舊速度與 F Dash 疊加。
         */
        if (ownerPlayer != null)
        {
            ownerPlayer
                .CancelManualMomentumFromSpecialAbility(
                    true
                );
        }


        // =============================================================
        // Dash Direction
        // =============================================================

        /*
         * F 小衝刺目前使用：
         *
         * 玩家「水平面向」。
         *
         * ------------------------------------------------------------
         *
         * 和 GrappleAirborne 右鍵特殊能力不同：
         *
         * Air Special
         * → 完整 3D。
         *
         * F 小 Dash
         * → 水平移動。
         *
         * ------------------------------------------------------------
         *
         * 這樣玩家低頭或抬頭時
         * 不會因為普通 F Dash
         * 突然往地面或天空飛。
         */
        Vector3 direction =
            ownerMovement
                .GetAimDirection();


        direction.y =
            0f;


        if (direction.sqrMagnitude <=
            0.0001f)
        {
            direction =
                ownerPlayer.transform.forward;


            direction.y =
                0f;
        }


        if (direction.sqrMagnitude <=
            0.0001f)
        {
            return;
        }


        direction.Normalize();


        // =============================================================
        // State
        // =============================================================

        DashDirection =
            direction;


        DashRemainingDistance =
            Mathf.Max(
                0.1f,
                dashDistance
            );


        DashSequence =
            activationSequence;


        DashActive =
            true;

        damagedTargetsThisDash.Clear();

        // Charge 已經消耗、Dash State 也已正式建立，
        // 這個位置才是唯一的世界啟動聲播放點。
        TryPlayQuickDashWorldStart(
            ownerMovement.KCC.Data.TargetPosition
        );


        if (debugQuickDash)
        {
            Debug.Log(
                $"[Tank Quick Dash] Dash Started" +
                $"\nSequence：{DashSequence}" +
                $"\nDirection：{DashDirection}" +
                $"\nDistance：{DashRemainingDistance:F2}" +
                $"\nSpeed：{dashSpeed:F2}" +
                $"\nCharges：{CurrentCharges}/{MaximumCharges}",
                this
            );
        }
    }
    
    /// <summary>
    /// 由 State Authority 廣播一次 Tank F Quick Dash 世界啟動聲。
    /// </summary>
    private void TryPlayQuickDashWorldStart(
        Vector3 worldPosition
    )
    {
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasStateAuthority == false ||
            quickDashWorldStartCue == null)
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
                quickDashWorldStartCue,
                worldPosition,
                quickDashWorldStartVolumeScale
            );
    }
    
    #endregion


    // =====================================================================
    #region Dash Tick


    /// <summary>
    /// 每個 Tick 執行 F Dash。
    /// </summary>
    private void TickDash()
    {
        if (ownerMovement == null ||
            ownerMovement.KCC == null)
        {
            CompleteDash(
                "KCC Missing"
            );


            return;
        }


        // =============================================================
        // 前一 Tick 已完成最後距離
        // =============================================================

        if (DashRemainingDistance <=
            0.0001f)
        {
            CompleteDash(
                "Distance Completed"
            );


            return;
        }


        Vector3 currentPosition =
            ownerMovement
                .KCC
                .Data
                .TargetPosition;


        // =============================================================
        // 本 Tick 移動距離
        // =============================================================

        float requestedStep =
            Mathf.Max(
                0.1f,
                dashSpeed
            ) *
            Runner.DeltaTime;


        float stepDistance =
            Mathf.Min(
                requestedStep,
                DashRemainingDistance
            );


        Vector3 intendedNextPosition =
            currentPosition +
            DashDirection *
            stepDistance;


        // =============================================================
        // ★ 先掃過本 Tick Dash 路徑
        // =============================================================

        ResolveDashDamageSegment(
            currentPosition,
            intendedNextPosition
        );


        // =============================================================
        // Dash 擁有 KCC 位移權限
        // =============================================================

        ownerMovement
            .KCC
            .SetInputDirection(
                Vector3.zero
            );


        float actualSpeed =
            stepDistance /
            Mathf.Max(
                Runner.DeltaTime,
                0.0001f
            );


        ownerMovement
            .KCC
            .SetKinematicVelocity(
                DashDirection *
                actualSpeed
            );


        // =============================================================
        // Distance
        // =============================================================

        DashRemainingDistance =
            Mathf.Max(
                0f,
                DashRemainingDistance -
                stepDistance
            );


        // =============================================================
        // Debug
        // =============================================================

        if (debugDrawDash)
        {
            Debug.DrawLine(
                currentPosition +
                    Vector3.up * 0.8f,

                intendedNextPosition +
                    Vector3.up * 0.8f,

                Color.yellow,

                debugDrawDuration
            );
        }
    }


    #endregion


    // =====================================================================
    #region Dash Damage


    /// <summary>
    /// 搜尋玩家這一個 Tick Dash 經過的空間。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不是只搜尋：
    ///
    /// Dash 起點
    ///
    /// 或
    ///
    /// Dash 終點。
    ///
    /// ------------------------------------------------------------
    ///
    /// 而是使用一個朝 Dash Direction
    /// 旋轉的長方形 Box，
    /// 覆蓋 Start → End 的整段路徑。
    /// </summary>
    private void ResolveDashDamageSegment(
        Vector3 start,
        Vector3 end
    )
    {
        if (Runner == null ||
            Runner.LagCompensation == null ||
            ownerPlayerNetworkObject == null)
        {
            return;
        }


        Vector3 segment =
            end -
            start;


        float segmentLength =
            segment.magnitude;


        if (segmentLength <=
            0.0001f)
        {
            return;
        }


        Vector3 direction =
            segment /
            segmentLength;


        // =============================================================
        // Box Center
        // =============================================================

        Vector3 center =
            (
                start +
                end
            ) *
            0.5f;


        /*
         * KCC TargetPosition 通常接近玩家腳底。
         *
         * 把傷害 Box 向上抬半個高度。
         */
        center +=
            Vector3.up *
            (
                dashDamageHeight *
                0.5f
            );


        // =============================================================
        // Box Rotation
        // =============================================================

        Quaternion rotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            );


        // =============================================================
        // Box Half Extents
        // =============================================================

        Vector3 extents =
            new Vector3(
                dashDamageWidth *
                    0.5f,

                dashDamageHeight *
                    0.5f,

                segmentLength *
                    0.5f +
                dashDamageForwardPadding
            );


        // =============================================================
        // Fusion Options
        // =============================================================

        HitOptions options =
            HitOptions.IgnoreInputAuthority;


        if (useSubtickAccuracy)
        {
            options |=
                HitOptions.SubtickAccuracy;
        }


        if (includePhysX)
        {
            options |=
                HitOptions.IncludePhysX;
        }


        dashHits.Clear();


        Runner
            .LagCompensation
            .OverlapBox(
                center,
                extents,
                rotation,
                ownerPlayerNetworkObject
                    .InputAuthority,
                dashHits,
                dashHitMask,
                options,
                true,
                QueryTriggerInteraction.Collide
            );


        // =============================================================
        // Process Hits
        // =============================================================

        for (int i = 0;
             i < dashHits.Count;
             i++)
        {
            LagCompensatedHit hit =
                dashHits[i];


            GameObject hitObject =
                hit.GameObject;


            if (hitObject == null)
            {
                continue;
            }


            // ---------------------------------------------------------
            // Enemy NetworkObject
            // ---------------------------------------------------------

            NetworkObject targetObject =
                hitObject
                    .GetComponentInParent<
                        NetworkObject
                    >();


            if (targetObject == null ||
                targetObject ==
                    ownerPlayerNetworkObject)
            {
                continue;
            }


            // ---------------------------------------------------------
            // 同一次 Dash 只打一次
            // ---------------------------------------------------------

            if (damagedTargetsThisDash
                .Contains(
                    targetObject
                ))
            {
                continue;
            }


            // ---------------------------------------------------------
            // 如果存在 GrappleInteractionTarget
            // 必須確認它真的是 Enemy
            // ---------------------------------------------------------

            GrappleInteractionTarget
                interactionTarget =
                    hitObject
                        .GetComponentInParent<
                            GrappleInteractionTarget
                        >();


            if (interactionTarget != null &&
                interactionTarget.TargetType !=
                    GrappleInteractionTargetType
                        .Enemy)
            {
                continue;
            }


            // ---------------------------------------------------------
            // Target Point
            // ---------------------------------------------------------

            Vector3 targetPoint =
                targetObject
                    .transform
                    .position +
                Vector3.up *
                    (
                        dashDamageHeight *
                        0.5f
                    );


            // ---------------------------------------------------------
            // Wall Check
            // ---------------------------------------------------------

            if (requireLineOfSight &&
                IsTargetBlocked(
                    start,
                    targetPoint
                ))
            {
                continue;
            }


            /*
             * ★ 從現在開始，
             * 這名 Enemy 已經算本次 Dash 撞過。
             *
             * 即使 Receiver 最後拒絕傷害，
             * 也不會每 Tick 再重新嘗試。
             */
            damagedTargetsThisDash.Add(
                targetObject
            );


            // ---------------------------------------------------------
            // Damage
            // ---------------------------------------------------------

            ApplyDashDamage(
                hitObject,
                targetObject,
                targetPoint
            );
        }
    }


    /// <summary>
    /// 正式送出一次 Dash DamageRequest。
    /// </summary>
    private void ApplyDashDamage(
        GameObject hitObject,
        NetworkObject targetObject,
        Vector3 targetPoint
    )
    {
        Vector3 origin =
            ownerMovement
                .KCC
                .Data
                .TargetPosition +
            Vector3.up *
                (
                    dashDamageHeight *
                    0.5f
                );


        Vector3 hitDirection =
            targetPoint -
            origin;


        float distance =
            hitDirection.magnitude;


        if (hitDirection.sqrMagnitude >
            0.0001f)
        {
            hitDirection.Normalize();
        }
        else
        {
            hitDirection =
                DashDirection;
        }


        DamageRequest request =
            new DamageRequest
            {
                RequestedDamage =
                    dashDamage,


                BaseDamage =
                    dashDamage,


                BlockedDamage =
                    0f,


                DamageType =
                    DamageType.Melee,


                FeedbackId =
                    CombatFeedbackId
                        .TankQuickDash,


                /*
                 * Dash Collision
                 * 不會因為剛好掃到 Head Hitbox
                 * 就變成 Headshot。
                 */
                HitZone =
                    DamageHitZoneType.Body,


                HeadshotDamageMultiplier =
                    1f,


                ForcedHeadshotSource =
                    DamageForcedHeadshotSource.None,


                Attacker =
                    ownerPlayerNetworkObject
                        .InputAuthority,


                SourceNetworkObject =
                    ownerPlayerNetworkObject,


                SourceObject =
                    ownerPlayer.gameObject,


                HitObject =
                    hitObject,


                HitPoint =
                    targetPoint,


                HitNormal =
                    -hitDirection,


                HitDirection =
                    hitDirection,


                Distance =
                    distance,


                Sequence =
                    DashSequence
            };


        bool receiverFound =
            DamageReceiverUtility
                .TryApplyDamage(
                    hitObject,
                    request,
                    out DamageResult result
                );


        // =============================================================
        // Result
        // =============================================================

        DamageResolved?.Invoke(
            result
        );


        if (receiverFound &&
            result.Accepted)
        {
            DamageConfirmed?.Invoke(
                result
            );
        }


        // =============================================================
        // ★ Kill → Restore 1 Charge
        // =============================================================

        if (receiverFound &&
            result.HasEffectiveDamage &&
            result.KilledTarget)
        {
            RestoreCharge(
                1
            );


            if (debugQuickDash)
            {
                Debug.Log(
                    $"[Tank Quick Dash] Kill Restore" +
                    $"\nEnemy：{targetObject.name}" +
                    $"\nRestore：1 Charge" +
                    $"\nCurrent：{CurrentCharges}/{MaximumCharges}",
                    targetObject
                );
            }
        }


        if (debugQuickDash)
        {
            Debug.Log(
                $"[Tank Quick Dash] Damage Result" +
                $"\nEnemy：{targetObject.name}" +
                $"\nSequence：{DashSequence}" +
                $"\nRequested Damage：{dashDamage:F2}" +
                $"\nReceiver Found：{receiverFound}" +
                $"\nAccepted：{result.Accepted}" +
                $"\nApplied Damage：{result.AppliedDamage:F2}" +
                $"\nKilled：{result.KilledTarget}" +
                $"\nCharges：{CurrentCharges}/{MaximumCharges}",
                targetObject
            );
        }
    }


    #endregion


    // =====================================================================
    #region Obstruction


    /// <summary>
    /// 確認 Enemy 與玩家之間
    /// 是否真的隔著 World Geometry。
    /// </summary>
    private bool IsTargetBlocked(
        Vector3 start,
        Vector3 targetPoint
    )
    {
        if (Runner == null)
        {
            return false;
        }


        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();


        if (physicsScene.IsValid() ==
            false)
        {
            return false;
        }


        Vector3 origin =
            start +
            Vector3.up *
                (
                    dashDamageHeight *
                    0.5f
                );


        Vector3 direction =
            targetPoint -
            origin;


        float distance =
            direction.magnitude;


        if (distance <=
            0.0001f)
        {
            return false;
        }


        direction /=
            distance;


        origin +=
            direction *
            obstructionRayStartOffset;


        distance =
            Mathf.Max(
                0f,
                distance -
                obstructionRayStartOffset
            );


        return
            physicsScene.Raycast(
                origin,
                direction,
                distance,
                obstructionMask,
                QueryTriggerInteraction.Ignore
            );
    }


    #endregion


    // =====================================================================
    #region Post Dash Recovery Runtime

    /// <summary>
    /// Timer 到期後清成 TickTimer.None。
    ///
    /// 即使不清除，ExpiredOrNotRunning 也會回傳已結束；
    /// 主動清除是為了讓 Network State 與 Debug 更乾淨。
    /// </summary>
    private void TickPostDashRecovery()
    {
        if (PostDashRecoveryTimer
            .Expired(Runner))
        {
            PostDashRecoveryTimer =
                TickTimer.None;

            if (debugQuickDash)
            {
                Debug.Log(
                    $"[Tank Quick Dash] " +
                    $"Post Dash Recovery Finished",
                    this
                );
            }
        }
    }

    /// <summary>
    /// 從 Dash 真正結束的這一刻開始計算攻防僵直。
    /// </summary>
    private void StartPostDashRecovery()
    {
        float duration =
            Mathf.Max(
                0f,
                postDashRecoveryDuration
            );

        if (duration <= 0f)
        {
            PostDashRecoveryTimer =
                TickTimer.None;

            return;
        }

        PostDashRecoveryTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                duration
            );

        if (debugQuickDash)
        {
            Debug.Log(
                $"[Tank Quick Dash] " +
                $"Post Dash Recovery Started" +
                $"\nDuration：{duration:F2} 秒",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Complete Dash


    private void CompleteDash(
        string reason
    )
    {
        if (IsDashing == false)
        {
            return;
        }


        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            ownerMovement
                .KCC
                .SetKinematicVelocity(
                    Vector3.zero
                );
        }


        DashActive =
            false;


        DashDirection =
            Vector3.zero;


        DashRemainingDistance =
            0f;


        damagedTargetsThisDash.Clear();

        /*
         * 必須在 DashActive 已經改成 false 後才開始。
         * Recovery 的 0.5 秒是從 Dash 真正停止時計算。
         */
        StartPostDashRecovery();


        if (debugQuickDash)
        {
            Debug.Log(
                $"[Tank Quick Dash] Dash Completed" +
                $"\nReason：{reason}" +
                $"\nCharges：{CurrentCharges}/{MaximumCharges}" +
                $"\nRecharge Remaining：{RechargeRemainingSeconds:F2}" +
                $"\nPost Dash Recovery：{PostDashRecoveryRemainingSeconds:F2}",
                this
            );
        }
    }


    /// <summary>
    /// 外部高權限系統需要時
    /// 可以強制中止 F Dash。
    /// </summary>
    public void ForceCancelDash()
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        CompleteDash(
            "Forced"
        );
    }


    #endregion
}
