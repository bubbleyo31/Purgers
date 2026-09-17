using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Support SMG 的備用彈藥模式。
/// </summary>
public enum SupportSMGAmmoSupplyMode : byte
{
    /// <summary>
    /// 有限備用彈藥。
    ///
    /// 換彈會真正消耗 Reserve Ammo。
    /// </summary>
    ReserveAmmo = 0,


    /// <summary>
    /// 無限備用彈藥。
    ///
    /// 注意：
    ///
    /// 這與 Support 特殊技能的
    /// 「無限彈匣」不同。
    ///
    /// InfiniteReserve：
    ///
    /// 彈匣仍會消耗，
    /// 打完仍然要換彈，
    /// 只是 Reserve Ammo 不會耗盡。
    ///
    /// 特殊技能 Infinite Magazine：
    ///
    /// 射擊連 MagazineAmmo 都不會消耗。
    /// </summary>
    InfiniteReserve = 1
}


/// <summary>
/// Support 職業使用的全自動 SMG。
///
/// ====================================================================
///
/// 一般戰鬥規則：
///
/// 命中 Enemy
/// →
/// 正常 Bullet Damage。
///
/// ------------------------------------------------------------
///
/// 命中其他 Player
/// →
/// 不造成 Damage。
/// →
/// 改為恢復 PlayerHealth。
///
/// ------------------------------------------------------------
///
/// 命中自己
/// →
/// 忽略。
///
/// ====================================================================
///
/// Support 特殊技能期間：
///
/// Infinite Magazine
/// → 不消耗 MagazineAmmo。
///
/// Special Fire Rate
/// → 預設 3 發 / 秒。
///
/// No Recoil
/// → 不新增 Gameplay Recoil。
///
/// Special Heal Amount
/// → 使用獨立特殊治療值。
///
/// ====================================================================
///
/// 注意：
///
/// 這支武器只負責「武器本身」。
///
/// 不負責：
///
/// 1. ADS。
/// 2. Support 空中特殊技能何時開始。
/// 3. 特殊技能 5 秒持續時間。
/// 4. 特殊技能 15 秒 CD。
/// 5. Grapple。
///
/// 未來 Support 特殊能力只需要呼叫：
///
/// SetSpecialModeActive(true)
///
/// 或：
///
/// SetSpecialModeActive(false)
///
/// 就能切換武器狀態。
/// </summary>
[DisallowMultipleComponent]
public class SupportSMG :
    NetworkBehaviour,
    ICombatDamageFeedbackSource
{
    // =====================================================================
    #region Player Reference

    [Header("玩家引用")]

    [SerializeField]
    [Tooltip("玩家移動模組。SupportSMG 會從 PlayerMovement 取得 KCC 瞄準方向，並在普通射擊後加入 Gameplay Recoil。若留空，Runtime Driver 綁定 Owner Player 時會自動取得。")]
    private PlayerMovement movement;

    #endregion

    // =====================================================================
    #region World Gunshot Audio


    [Header("世界槍聲")]


    [SerializeField]
    [Tooltip(
        "SupportSMG 正式射擊成功時，由 State Authority 透過 Player Root 的 NetworkPlayerAudioEmitter 傳給所有 Client 的 3D 世界槍聲。\n\n" +
        "這裡必須指定 Network ID 大於 0 的 GameplayAudioCue，並且同一個 Cue 必須加入 Player 使用的 GameplayAudioCatalog。\n\n" +
        "普通模式與 Support 空中特殊模式目前共用這一個槍聲 Cue；特殊模式只改射速、治療量、彈匣規則與後座力，不會額外重複播放聲音。")]
    private GameplayAudioCue
        worldGunshotCue;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "SupportSMG 呼叫世界槍聲時額外乘上的音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0.5 = Cue 音量的一半。\n" +
        "0 = 保留事件但靜音。\n\n" +
        "NetworkPlayerAudioEmitter 會把傳入值限制在 0～4，最終 AudioSource 音量仍會限制在 0～1。")]
    private float worldGunshotVolumeScale =
        1f;


    /// <summary>
    /// 真正 Player NetworkObject 上的世界聲音發送器。
    ///
    /// SupportSMG 位於 Support Profession Runtime，
    /// Emitter 位於 Player Root。
    ///
    /// 只在 Owner Binding 時快取，
    /// 不會每發射擊都搜尋場景。
    /// </summary>
    private NetworkPlayerAudioEmitter
        networkAudioEmitter;


    #endregion    

    // =====================================================================
    #region Owner Player Binding


    /// <summary>
    /// 這把 SupportSMG
    /// 真正所屬的 Player Core。
    ///
    /// SupportSMG 自己存在：
    ///
    /// SupportProfessionRuntime
    ///
    /// 而不是：
    ///
    /// Player NetworkObject。
    ///
    /// 因此所有 Owner 判斷
    /// 必須使用這個 Player。
    /// </summary>
    private Player ownerPlayer;


    /// <summary>
    /// Owner Player 的 NetworkObject。
    ///
    /// 用途：
    ///
    /// 排除自己的 Hitbox。
    /// Damage Source。
    /// Healing Self Check。
    /// Input Authority。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;


    /// <summary>
    /// 目前真正擁有這把 SMG 的 Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;


    /// <summary>
    /// 將 SupportSMG
    /// 綁定到真正 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;


        if (ownerPlayer == null)
        {
            ownerPlayerNetworkObject =
                null;


            movement =
                null;


            /*
             * Runtime 解除 Owner Binding 時，
             * 不可保留上一個 Player 的世界聲音發送器。
             */
            networkAudioEmitter =
                null;


            return;
        }


        ownerPlayerNetworkObject =
            ownerPlayer.Object;


        movement =
            ownerPlayer.Movement;

        /*
         * NetworkPlayerAudioEmitter 固定掛在 Player Root，
         * 不掛在會隨職業切換而 Spawn / Despawn 的 Support Runtime。
         */
        networkAudioEmitter =
            ownerPlayer.GetComponent<
                NetworkPlayerAudioEmitter
            >();

        if (movement == null)
        {
            Debug.LogError(
                $"[{nameof(SupportSMG)}] " +
                $"Owner Player 找不到 PlayerMovement。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }

        if (ownerPlayerNetworkObject == null)
        {
            Debug.LogError(
                $"[{nameof(SupportSMG)}] " +
                $"Owner Player 沒有有效 NetworkObject。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }

        if (networkAudioEmitter == null)
        {
            Debug.LogError(
                $"[{nameof(SupportSMG)}] " +
                $"Owner Player Root 找不到 " +
                $"{nameof(NetworkPlayerAudioEmitter)}，" +
                $"正式射擊仍可造成傷害或治療，但無法送出世界槍聲。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }


    /// <summary>
    /// 取得真正 Player NetworkObject。
    ///
    /// Runtime Weapon 自己的 Object
    /// 不是 Player Object。
    /// </summary>
    private NetworkObject
        GetOwnerPlayerNetworkObject()
    {
        if (ownerPlayerNetworkObject != null)
        {
            return
                ownerPlayerNetworkObject;
        }


        if (ownerPlayer != null &&
            ownerPlayer.Object != null)
        {
            return
                ownerPlayer.Object;
        }


        if (movement != null)
        {
            NetworkObject movementObject =
                movement
                    .GetComponent<NetworkObject>();


            if (movementObject != null)
            {
                return
                    movementObject;
            }
        }


        /*
         * 最後 fallback。
         *
         * 正式 Profession Runtime 架構
         * 正常不應該依賴這裡。
         */
        return
            Object;
    }


    /// <summary>
    /// 取得真正 Player 的 Input Authority。
    /// </summary>
    private PlayerRef GetOwnerInputAuthority()
    {
        NetworkObject ownerObject =
            GetOwnerPlayerNetworkObject();


        if (ownerObject != null)
        {
            return
                ownerObject.InputAuthority;
        }


        if (Object != null)
        {
            return
                Object.InputAuthority;
        }


        return
            PlayerRef.None;
    }


    /// <summary>
    /// 判斷指定 NetworkObject
    /// 是否就是這把 SupportSMG 的 Owner Player。
    /// </summary>
    private bool IsOwnerPlayerObject(
        NetworkObject targetObject
    )
    {
        if (targetObject == null)
        {
            return false;
        }


        NetworkObject ownerObject =
            GetOwnerPlayerNetworkObject();


        return
            ownerObject != null &&
            targetObject ==
            ownerObject;
    }


    #endregion


    // =====================================================================
    #region Magazine


    [Header("彈匣設定")]


    [SerializeField]
    [Min(1)]
    [Tooltip("SupportSMG 一個普通彈匣的最大子彈數。特殊技能期間雖然會進入無限彈匣模式，但不會真的修改這個容量，也不會把 MagazineAmmo 改成非常大的假數字。特殊技能結束後會保留啟動技能以前剩餘的實際彈藥。")]
    private int magazineCapacity =
        30;


    [SerializeField]
    [Tooltip("普通狀態的備用彈藥模式。ReserveAmmo 代表有限備彈；InfiniteReserve 代表備彈無限，但普通狀態的彈匣仍會正常消耗並需要換彈。")]
    private SupportSMGAmmoSupplyMode
        ammoSupplyMode =
            SupportSMGAmmoSupplyMode.ReserveAmmo;


    [SerializeField]
    [Min(0)]
    [Tooltip("使用有限備彈模式時，Support 玩家生成時擁有多少備用子彈。這個數值不包含目前彈匣內的子彈。")]
    private int startingReserveAmmo =
        180;


    #endregion


    // =====================================================================
    #region Reload


    [Header("換彈設定")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("普通狀態完整換彈所需要的時間，單位為秒。特殊技能 Infinite Magazine Active 時不需要換彈。")]
    private float reloadDuration =
        1.8f;


    [SerializeField]
    [Tooltip("普通狀態下，如果彈匣已經沒有子彈，而且玩家仍按住射擊，是否自動開始換彈。特殊技能期間會忽略這項設定，因為 Special Mode 不消耗彈匣。")]
    private bool autoReloadWhenEmpty =
        true;


    #endregion


    // =====================================================================
    #region Fire Rate


    [Header("普通射速設定")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("SupportSMG 普通狀態的基礎每秒射擊次數。例如 10 代表每秒最多射擊 10 發，也就是約 600 RPM。未來普通武器升級不要直接修改這個 Inspector 基礎值，而是使用 Runtime Fire Rate Multiplier。")]
    private float baseFireRate =
        10f;


    [Header("特殊技能射速設定")]


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Support 空中特殊技能 Active 時的基礎每秒射擊次數。這是一個獨立數值，不是普通射速倍率。依目前設計預設為 3 發每秒。未來技能升級可以再透過 Runtime Special Fire Rate Multiplier 調整。")]
    private float baseSpecialFireRate =
        3f;


    #endregion


    // =====================================================================
    #region Hitscan


    [Header("Hitscan 設定")]


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("SupportSMG 最遠射擊距離。Enemy Damage 與 Player Healing 都使用相同射程。")]
    private float maxShotDistance =
        200f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Gameplay Hitscan 起點相對 Player KCC Target Position 的高度。真正傷害與治療仍由玩家瞄準視線判定，ViewModel MuzzlePoint 只負責 Tracer 視覺。")]
    private float shotOriginHeight =
        1.6f;


    [SerializeField]
    [Tooltip("SupportSMG 可以命中的 Layer。非常重要：如果希望子彈可以治療其他玩家，這個 Hit Mask 必須包含其他玩家的 Fusion Hitbox 或可被 Lag Compensation 命中的玩家 Layer，同時也必須包含 Enemy 與 World Layer。")]
    private LayerMask hitMask =
        ~0;


    [SerializeField]
    [Tooltip("開啟後使用 Photon Fusion Subtick Accuracy。高速勾索與高速移動射擊時建議保持開啟。")]
    private bool useSubtickAccuracy =
        true;


    [SerializeField]
    [Tooltip("如果 Lag Compensation 不可用，是否允許暫時使用 Runner PhysicsScene 的普通 Raycast。這主要是測試 fallback，正式多人命中仍建議依賴 Fusion Lag Compensation。")]
    private bool allowPhysicsFallback =
        true;


    #endregion


    // =====================================================================
    #region Enemy Damage


    [Header("對敵傷害設定")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("SupportSMG 命中非玩家 Damage Target 時，每發子彈的基礎傷害。Support 對 Enemy 仍然會正常造成傷害。")]
    private float baseDamage =
        20f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("命中距離小於或等於這個值時完全不產生傷害衰退。")]
    private float damageFalloffStartDistance =
        30f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip("命中距離達到這個值後，傷害會降到 Minimum Damage Multiplier 所設定的最低倍率。")]
    private float damageFalloffEndDistance =
        150f;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("SupportSMG 最遠距離至少保留多少對 Enemy 傷害。例如 0.85 代表最遠距離仍然保留 85% 傷害。")]
    private float minimumDamageMultiplier =
        0.85f;


    [SerializeField]
    [Min(1f)]
    [Tooltip("SupportSMG 對 Enemy 命中 Head WeaponHitZone 時使用的暴頭倍率。這個倍率只影響 Enemy Damage，不影響 Player Healing。")]
    private float headshotMultiplier =
        2f;


    #endregion


    // =====================================================================
    #region Player Healing


    [Header("隊友治療設定")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("SupportSMG 普通狀態命中其他 Player 時，每發嘗試恢復多少生命值。這不是 Damage 的倍率，而是完全獨立的固定治療數值。PlayerHealth 本身會限制不能超過 Maximum Health。")]
    private float normalHealAmount =
        10f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("Support 空中特殊技能 Active 時，SupportSMG 命中其他 Player 每發使用的治療量。這是一個獨立數值，不會由 Normal Heal Amount 乘倍率得到，方便未來普通治療與特殊治療分開升級。")]
    private float specialHealAmount =
        20f;


    #endregion


    // =====================================================================
    #region Gameplay Recoil


    [Header("普通 Gameplay 後座力")]


    [SerializeField]
    [Tooltip("普通狀態每發子彈加入 KCC Look Pitch 的角度。負值通常代表讓視角向上抬。特殊技能 Active 時不會新增這個 Recoil。")]
    private float recoilPitchPerShot =
        -0.8f;


    [SerializeField]
    [Min(0f)]
    [Tooltip("普通狀態每發子彈左右偏移的最大角度。目前使用可預測的左右交替模式，避免 Fusion Prediction 因 Random 結果不一致。特殊技能期間不會新增水平 Recoil。")]
    private float recoilYawPerShot =
        0.25f;


    [Header("後座力回正設定")]


    [SerializeField]
    [Min(0f)]
    [Tooltip("停止新增普通後座力後，延遲多久才開始自動將尚未被玩家手動壓槍抵消的 Recoil 拉回。")]
    private float recoilRecoveryDelay =
        0.2f;


    [SerializeField]
    [Min(0.1f)]
    [Tooltip("普通 Gameplay Recoil 自動回正速度，單位為每秒角度。特殊模式雖然不會新增 Recoil，但技能啟動前已存在的 Recoil 仍可以正常完成回正。")]
    private float recoilRecoverySpeed =
        10f;


    #endregion


    // =====================================================================
    #region Bullet Tracer


    [Header("彈道視覺化")]


    [SerializeField]
    [Tooltip("負責顯示本地 SupportSMG Hitscan Tracer 的 BulletTracerLineDrawer。若留空會自動從同一個 Support Runtime Root 取得。")]
    private BulletTracerLineDrawer
        bulletTracerLineDrawer;


    [SerializeField]
    [Tooltip("Support 第一人稱 ViewModel 真正的槍口 Transform。這個引用由 ProfessionViewModelManager 在 Support ViewModel 建立完成後動態傳入，只影響 Tracer 視覺起點，不會改變真正 Hitscan 判定。")]
    private Transform tracerMuzzlePoint;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip("開啟後會顯示 SupportSMG 初始化、射擊、Enemy Damage、Player Healing、Reload 與特殊模式切換資訊。")]
    private bool debugSMG =
        true;


    [SerializeField]
    [Tooltip("開啟後，本地玩家射擊時會使用 Debug.DrawRay 顯示真正 Gameplay Hitscan 方向。")]
    private bool drawShotRay =
        true;


    #endregion


    // =====================================================================
    #region Networked Weapon State


    /// <summary>
    /// 普通狀態真正剩餘的彈匣子彈。
    ///
    /// Special Mode Active 時不會消耗。
    /// </summary>
    [Networked]
    public int MagazineAmmo
    {
        get;
        private set;
    }


    /// <summary>
    /// 普通備用子彈。
    /// </summary>
    [Networked]
    public int ReserveAmmo
    {
        get;
        private set;
    }


    /// <summary>
    /// 是否正在普通 Reload。
    /// </summary>
    [Networked]
    public NetworkBool IsReloading
    {
        get;
        private set;
    }


    [Networked]
    private TickTimer ReloadTimer
    {
        get;
        set;
    }


    [Networked]
    private TickTimer FireCooldownTimer
    {
        get;
        set;
    }


    /// <summary>
    /// SupportSMG 成功發射過幾發。
    /// </summary>
    [Networked]
    public int ShotSequence
    {
        get;
        private set;
    }


    /// <summary>
    /// 普通狀態射速倍率。
    /// </summary>
    [Networked]
    public float RuntimeFireRateMultiplier
    {
        get;
        private set;
    }


    /// <summary>
    /// 特殊技能射速倍率。
    ///
    /// Special Effective Fire Rate
    ///
    /// =
    ///
    /// Base Special Fire Rate
    /// ×
    /// Runtime Special Fire Rate Multiplier。
    /// </summary>
    [Networked]
    public float RuntimeSpecialFireRateMultiplier
    {
        get;
        private set;
    }


    /// <summary>
    /// Support 空中特殊技能
    /// 是否正在改變 SMG Gameplay。
    ///
    /// Active 時：
    ///
    /// Infinite Magazine
    /// Special Fire Rate
    /// No New Recoil
    /// Special Heal Amount。
    /// </summary>
    [Networked]
    public NetworkBool IsSpecialModeActive
    {
        get;
        private set;
    }


    [Networked]
    private NetworkBool IsInitialized
    {
        get;
        set;
    }


    [Networked]
    private float AccumulatedRecoilPitch
    {
        get;
        set;
    }


    [Networked]
    private TickTimer RecoilRecoveryTimer
    {
        get;
        set;
    }


    [Networked]
    private float LastAimPitch
    {
        get;
        set;
    }


    #endregion


    // =====================================================================
    #region Hit Cache


    /// <summary>
    /// Fusion RaycastAll 重複使用的結果 List。
    ///
    /// LagCompensatedHit 不保證距離排序，
    /// 所以我們會自己找最近合法目標。
    /// </summary>
    private readonly List<LagCompensatedHit>
        lagCompensatedHits =
            new List<LagCompensatedHit>(16);


    #endregion


    // =====================================================================
    #region Combat Events


    /// <summary>
    /// Enemy Damage 經過 DamageSystem 解析後觸發。
    /// </summary>
    public event Action<DamageResult>
        DamageResolved;


    /// <summary>
    /// Enemy 正式接受 SupportSMG Damage 時觸發。
    /// </summary>
    public event Action<DamageResult>
        DamageConfirmed;


    /// <summary>
    /// Enemy 正式受到有效 Headshot Damage 時觸發。
    /// </summary>
    public event Action<DamageResult>
        HeadshotConfirmed;


    /// <summary>
    /// SupportSMG 真正讓其他玩家增加 HP 時觸發。
    ///
    /// 第一個參數：
    /// 被治療的 PlayerHealth。
    ///
    /// 第二個參數：
    /// 這一發真正增加的 HP。
    ///
    /// 未來可以接：
    ///
    /// Healing Number
    /// Healing Hit Marker
    /// 治療音效
    /// Support 被動
    /// 統計。
    /// </summary>
    public event Action<PlayerHealth, float>
        HealingConfirmed;


    #endregion


    // =====================================================================
    #region Public Data


    public int MagazineCapacity =>
        Mathf.Max(
            1,
            magazineCapacity
        );


    public bool HasInfiniteReserveAmmo =>
        ammoSupplyMode ==
        SupportSMGAmmoSupplyMode.InfiniteReserve;


    /// <summary>
    /// 特殊技能期間是否為無限彈匣。
    ///
    /// 目前設計：
    ///
    /// Special Mode Active
    /// = Infinite Magazine。
    /// </summary>
    public bool HasInfiniteMagazine =>
        IsSpecialModeActive;


    /// <summary>
    /// 目前真正使用的每秒射擊次數。
    ///
    /// 普通：
    ///
    /// Base Fire Rate
    /// × Normal Runtime Multiplier。
    ///
    /// 特殊：
    ///
    /// Base Special Fire Rate
    /// × Special Runtime Multiplier。
    /// </summary>
    public float EffectiveFireRate
    {
        get
        {
            if (IsSpecialModeActive)
            {
                return Mathf.Max(
                    0.01f,
                    baseSpecialFireRate *
                    Mathf.Max(
                        0.01f,
                        RuntimeSpecialFireRateMultiplier
                    )
                );
            }


            return Mathf.Max(
                0.01f,
                baseFireRate *
                Mathf.Max(
                    0.01f,
                    RuntimeFireRateMultiplier
                )
            );
        }
    }


    /// <summary>
    /// 目前這發 Support Healing
    /// 應使用的固定治療量。
    /// </summary>
    public float CurrentHealAmount =>
        IsSpecialModeActive
            ? Mathf.Max(
                0f,
                specialHealAmount
            )
            : Mathf.Max(
                0f,
                normalHealAmount
            );


    public bool IsFireRateReady =>
        Runner != null &&
        FireCooldownTimer
            .ExpiredOrNotRunning(
                Runner
            );


    public float MaxShotDistance =>
        Mathf.Max(
            0.1f,
            maxShotDistance
        );


    public LayerMask HitMask =>
        hitMask;


    public float ReloadRemainingSeconds
    {
        get
        {
            if (Runner == null ||
                IsReloading == false)
            {
                return
                    0f;
            }


            return
                ReloadTimer
                    .RemainingTime(
                        Runner
                    ) ?? 0f;
        }
    }


    #endregion


    // =====================================================================
    #region Unity / Fusion


    private void Awake()
    {
        /*
         * 舊 Root 架構相容。
         *
         * 正式 Support Runtime
         * 正常會由 Driver BindOwnerPlayer。
         */
        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }


        if (ownerPlayer == null)
        {
            Player localPlayer =
                GetComponent<Player>();


            if (localPlayer != null)
            {
                BindOwnerPlayer(
                    localPlayer
                );
            }
        }


        if (bulletTracerLineDrawer == null)
        {
            bulletTracerLineDrawer =
                GetComponent<
                    BulletTracerLineDrawer
                >();
        }
    }


    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            InitializeWeapon();
        }
    }


    #endregion


    // =====================================================================
    #region Initialize


    private void InitializeWeapon()
    {
        MagazineAmmo =
            MagazineCapacity;


        ReserveAmmo =
            HasInfiniteReserveAmmo
                ? 0
                : Mathf.Max(
                    0,
                    startingReserveAmmo
                );


        IsReloading =
            false;


        ReloadTimer =
            TickTimer.None;


        FireCooldownTimer =
            TickTimer.None;


        ShotSequence =
            0;


        RuntimeFireRateMultiplier =
            1f;


        RuntimeSpecialFireRateMultiplier =
            1f;


        IsSpecialModeActive =
            false;


        AccumulatedRecoilPitch =
            0f;


        LastAimPitch =
            0f;


        RecoilRecoveryTimer =
            TickTimer.None;


        IsInitialized =
            true;


        if (debugSMG)
        {
            Debug.Log(
                $"[Support SMG] 初始化完成。" +
                $"\n彈匣：{MagazineAmmo}/{MagazineCapacity}" +
                $"\n備彈模式：{ammoSupplyMode}" +
                $"\n普通射速：{EffectiveFireRate:F2}" +
                $"\n普通治療：{normalHealAmount:F2}" +
                $"\n特殊治療：{specialHealAmount:F2}" +
                $"\n特殊基礎射速：{baseSpecialFireRate:F2}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Simulation


    /// <summary>
    /// 每個 Fusion Tick
    /// 由 PlayerWeaponController 呼叫。
    /// </summary>
    public bool Simulate(
        bool fireHeld,
        bool reloadPressed
    )
    {
        if (IsInitialized == false)
        {
            return false;
        }


        // =============================================================
        // Recoil Recovery
        // =============================================================

        ProcessRecoilRecovery();


        // =============================================================
        // Reload
        // =============================================================

        UpdateReload();


        /*
         * Special Mode 為無限彈匣，
         * 完全不需要 Reload。
         */
        if (reloadPressed &&
            IsSpecialModeActive == false)
        {
            TryStartReload();
        }


        // =============================================================
        // Fire
        // =============================================================

        if (fireHeld == false)
        {
            return false;
        }


        bool fired =
            TryFire();


        // =============================================================
        // Auto Reload
        // =============================================================

        if (fired == false &&
            IsSpecialModeActive == false &&
            autoReloadWhenEmpty &&
            IsReloading == false &&
            MagazineAmmo <= 0)
        {
            TryStartReload();
        }


        return
            fired;
    }


    #endregion


    // =====================================================================
    #region Fire


    private bool TryFire()
    {
        if (IsReloading)
        {
            return false;
        }


        if (FireCooldownTimer
                .ExpiredOrNotRunning(
                    Runner
                ) == false)
        {
            return false;
        }


        // =============================================================
        // Ammo
        // =============================================================

        /*
         * 普通狀態：
         *
         * 至少要有一發 Magazine Ammo。
         *
         * ------------------------------------------------------------
         *
         * Special：
         *
         * Infinite Magazine。
         *
         * 即使進技能前 MagazineAmmo = 0，
         * 技能期間仍然允許射擊。
         */
        if (IsSpecialModeActive == false &&
            MagazineAmmo <= 0)
        {
            return false;
        }


        if (IsSpecialModeActive == false)
        {
            MagazineAmmo--;
        }


        // =============================================================
        // Fire Cooldown
        // =============================================================

        float secondsPerShot =
            1f /
            EffectiveFireRate;


        FireCooldownTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                secondsPerShot
            );


        // =============================================================
        // Sequence
        // =============================================================

        ShotSequence++;


        // =============================================================
        // Aim
        // =============================================================

        Vector3 shotOrigin =
            GetGameplayShotOrigin();


        // =============================================================
        // Network World Gunshot
        // =============================================================

        /*
         * 走到這裡代表本次射擊已經通過：
         *
         * Reload
         * Fire Cooldown
         * 普通彈匣或 Special Infinite Magazine
         * ShotSequence++。
         *
         * 所以普通與特殊模式都只會在真正射出一發時發聲。
         * 空彈或射速尚未 Ready 的輸入不會製造假槍聲。
         */
        TryPlayWorldGunshot();


        Vector3 shotDirection =
            GetGameplayAimDirection();


        if (shotDirection.sqrMagnitude <=
            0.0001f)
        {
            shotDirection =
                transform.forward;
        }


        shotDirection.Normalize();


        // =============================================================
        // Tracer Default
        // =============================================================

        Vector3 tracerEndPoint =
            shotOrigin +
            shotDirection *
            maxShotDistance;


        // =============================================================
        // Authority Hit
        // =============================================================

        if (Object.HasStateAuthority)
        {
            tracerEndPoint =
                PerformAuthoritativeHitscan(
                    shotOrigin,
                    shotDirection
                );
        }


        // =============================================================
        // Client Visual Prediction
        // =============================================================

        else if (Object.HasInputAuthority)
        {
            tracerEndPoint =
                GetLocalVisualTracerEndPoint(
                    shotOrigin,
                    shotDirection
                );
        }


        // =============================================================
        // Tracer
        // =============================================================

        ShowLocalBulletTracer(
            tracerEndPoint
        );


        // =============================================================
        // Recoil
        // =============================================================

        /*
         * Special Mode：
         *
         * 不新增 Gameplay Recoil。
         *
         * ------------------------------------------------------------
         *
         * 技能開始以前已累積的 Recoil
         * 仍然會由 ProcessRecoilRecovery()
         * 自然完成回正。
         */
        if (IsSpecialModeActive == false)
        {
            ApplyGameplayRecoil();
        }


        // =============================================================
        // Debug Ray
        // =============================================================

        if (drawShotRay &&
            Object.HasInputAuthority)
        {
            Debug.DrawRay(
                shotOrigin,
                shotDirection *
                maxShotDistance,
                Color.yellow,
                0.3f
            );
        }


        if (debugSMG &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Support SMG] Fire" +
                $"\nShot：{ShotSequence}" +
                $"\nSpecial：{IsSpecialModeActive}" +
                $"\nFire Rate：{EffectiveFireRate:F2}" +
                $"\nInfinite Magazine：{HasInfiniteMagazine}" +
                $"\nMagazine：{MagazineAmmo}/{MagazineCapacity}" +
                $"\nCurrent Heal：{CurrentHealAmount:F2}",
                this
            );
        }


        return true;
    }


    /// <summary>
    /// 由 SupportSMG 的 State Authority 發送一次 3D 世界槍聲。
    ///
    /// 普通與 Support 空中特殊射擊都共用這條路徑。
    /// 每一發音檔都會在結束前跟隨 Player Audio Origin。
    /// </summary>
    private void TryPlayWorldGunshot()
    {
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (worldGunshotCue == null)
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
                worldGunshotCue,
                worldGunshotVolumeScale
            );
    }


    #endregion


    // =====================================================================
    #region Lag Compensation


    private Vector3 PerformAuthoritativeHitscan(
        Vector3 origin,
        Vector3 direction
    )
    {
        Vector3 tracerEndPoint =
            origin +
            direction *
            maxShotDistance;


        // =============================================================
        // Fusion Lag Compensation
        // =============================================================

        if (Runner.LagCompensation != null)
        {
            HitOptions options =
                HitOptions.IncludePhysX |
                HitOptions.IgnoreInputAuthority;


            if (useSubtickAccuracy)
            {
                options |=
                    HitOptions.SubtickAccuracy;
            }


            lagCompensatedHits.Clear();


            PlayerRef ownerAuthority =
                GetOwnerInputAuthority();


            Runner.LagCompensation.RaycastAll(
                origin,
                direction,
                maxShotDistance,
                ownerAuthority,
                lagCompensatedHits,
                hitMask,
                true,
                options,
                QueryTriggerInteraction.Ignore
            );


            if (TryGetNearestValidHit(
                    lagCompensatedHits,
                    out LagCompensatedHit hit
                ))
            {
                tracerEndPoint =
                    hit.Point;


                ProcessHit(
                    hit,
                    direction
                );
            }


            return
                tracerEndPoint;
        }


        // =============================================================
        // Physics Fallback
        // =============================================================

        if (allowPhysicsFallback == false)
        {
            return
                tracerEndPoint;
        }


        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();


        if (physicsScene.IsValid() ==
            false)
        {
            return
                tracerEndPoint;
        }


        if (physicsScene.Raycast(
                origin,
                direction,
                out RaycastHit physicsHit,
                maxShotDistance,
                hitMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            NetworkObject hitNetworkObject =
                physicsHit.collider
                    .GetComponentInParent<
                        NetworkObject
                    >();


            /*
             * Fallback 也不允許打到自己。
             */
            if (IsOwnerPlayerObject(
                    hitNetworkObject
                ) == false)
            {
                tracerEndPoint =
                    physicsHit.point;


                LagCompensatedHit convertedHit =
                    (LagCompensatedHit)
                    physicsHit;


                ProcessHit(
                    convertedHit,
                    direction
                );
            }
        }


        return
            tracerEndPoint;
    }


    /// <summary>
    /// 從所有 Lag Compensation 結果中
    /// 找出最近且不是 Owner 自己的命中。
    /// </summary>
    private bool TryGetNearestValidHit(
        System.Collections.Generic.List<LagCompensatedHit> hits,
        out LagCompensatedHit nearestHit)
    {
        return WeaponHitUtility.TryGetNearestValidHit(
            hits, GetOwnerPlayerNetworkObject(), out nearestHit);
    }


    #endregion


    // =====================================================================
    #region Hit Processing


    /// <summary>
    /// SupportSMG 正式命中處理。
    ///
    /// 判定順序非常重要：
    ///
    /// ① 先確認是不是其他 Player。
    ///
    /// 是：
    /// → Healing
    /// → return。
    ///
    /// 否：
    /// → Enemy / Damageable
    /// → Damage Pipeline。
    ///
    /// ------------------------------------------------------------
    ///
    /// 因此 Player 絕對不會先受到 Damage
    /// 再事後補回 HP。
    /// </summary>
    private void ProcessHit(
        LagCompensatedHit hit,
        Vector3 shotDirection
    )
    {
        if (hit.GameObject == null)
        {
            return;
        }


        // =============================================================
        // 1. Player Healing
        // =============================================================

        PlayerHealth playerHealth =
            ResolvePlayerHealth(
                hit.GameObject
            );


        if (playerHealth != null)
        {
            ProcessPlayerHealing(
                playerHealth,
                hit
            );


            /*
             * 非常重要：
             *
             * 只要被辨認成 Player，
             * 就不可以繼續進 Damage Pipeline。
             *
             * 即使：
             *
             * Player 已滿血。
             * Player 已死亡。
             * RestoreHealth 回傳 false。
             *
             * 都不能因此變成 Friendly Fire。
             */
            return;
        }


        // =============================================================
        // 2. Enemy / Damageable Damage
        // =============================================================

        ProcessEnemyDamage(
            hit,
            shotDirection
        );
    }


    /// <summary>
    /// 嘗試從 SupportSMG 命中的物件
    /// 找出真正 PlayerHealth。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不只單純使用：
    ///
    /// HitObject.GetComponentInParent&lt;PlayerHealth&gt;()
    ///
    /// ------------------------------------------------------------
    ///
    /// 因為 Photon Hitbox / PhysX Collider
    /// 有可能存在不同子階層。
    ///
    /// 所以增加：
    ///
    /// 1. Parent Search。
    /// 2. NetworkObject Root Search。
    /// 3. NetworkObject Children Search。
    ///
    /// 讓玩家 Hitbox 架構比較有彈性。
    /// </summary>
    private PlayerHealth ResolvePlayerHealth(
        GameObject hitObject
    )
    {
        if (hitObject == null)
        {
            return null;
        }


        // =============================================================
        // 1. Parent Chain
        // =============================================================

        PlayerHealth health =
            hitObject
                .GetComponentInParent<
                    PlayerHealth
                >();


        if (health != null)
        {
            return
                health;
        }


        // =============================================================
        // 2. Network Object
        // =============================================================

        NetworkObject networkObject =
            hitObject
                .GetComponentInParent<
                    NetworkObject
                >();


        if (networkObject == null)
        {
            return null;
        }


        health =
            networkObject
                .GetComponent<
                    PlayerHealth
                >();


        if (health != null)
        {
            return
                health;
        }


        // =============================================================
        // 3. Network Object Children
        // =============================================================

        return networkObject
            .GetComponentInChildren<
                PlayerHealth
            >(
                true
            );
    }


    /// <summary>
    /// 對其他玩家執行正式治療。
    /// </summary>
    private void ProcessPlayerHealing(
        PlayerHealth targetHealth,
        LagCompensatedHit hit
    )
    {
        if (targetHealth == null)
        {
            return;
        }


        // =============================================================
        // Self Healing 防呆
        // =============================================================

        NetworkObject targetObject =
            targetHealth.Object;


        if (IsOwnerPlayerObject(
                targetObject
            ))
        {
            if (debugSMG)
            {
                Debug.LogWarning(
                    "[Support SMG] " +
                    "射線意外命中 Owner 自己，已忽略。",
                    targetHealth
                );
            }


            return;
        }


        // =============================================================
        // State Authority
        // =============================================================

        if (Object.HasStateAuthority ==
            false)
        {
            return;
        }


        float healthBefore =
            targetHealth.CurrentHealth;


        float requestedHeal =
            CurrentHealAmount;


        bool healed =
            targetHealth.RestoreHealth(
                requestedHeal
            );


        float healthAfter =
            targetHealth.CurrentHealth;


        float appliedHeal =
            Mathf.Max(
                0f,
                healthAfter -
                healthBefore
            );


        // =============================================================
        // Healing Event
        // =============================================================

        if (healed &&
            appliedHeal >
                0.0001f)
        {
            HealingConfirmed?.Invoke(
                targetHealth,
                appliedHeal
            );
        }


        // =============================================================
        // Debug
        // =============================================================

        if (debugSMG)
        {
            Debug.Log(
                $"[Support SMG Healing]" +
                $"\n目標：{targetHealth.name}" +
                $"\n目標玩家：{targetObject.InputAuthority}" +
                $"\nSpecial Mode：{IsSpecialModeActive}" +
                $"\nRequested Heal：{requestedHeal:F2}" +
                $"\nApplied Heal：{appliedHeal:F2}" +
                $"\nHealth Before：{healthBefore:F2}" +
                $"\nHealth After：{healthAfter:F2}" +
                $"\nMaximum Health：{targetHealth.MaximumHealth:F2}" +
                $"\nHeal Success：{healed}" +
                $"\nAlive：{targetHealth.IsAlive}" +
                $"\nHit Object：{hit.GameObject.name}" +
                $"\nShot：{ShotSequence}",
                targetHealth
            );
        }
    }


    /// <summary>
    /// 對非 Player Damage Target
    /// 執行原本 Bullet Damage Pipeline。
    /// </summary>
    private void ProcessEnemyDamage(
        LagCompensatedHit hit,
        Vector3 shotDirection
    )
    {
        // =============================================================
        // Weapon Hit Zone
        // =============================================================

        WeaponHitZone hitZone =
            hit.GameObject
                .GetComponent<
                    WeaponHitZone
                >();


        bool isHeadshot =
            hitZone != null &&
            hitZone.IsHead;


        float zoneMultiplier =
            hitZone != null
                ? hitZone.DamageMultiplier
                : 1f;


        // =============================================================
        // Damage Hit Zone
        // =============================================================

        DamageHitZoneType
            damageHitZone;


        if (hitZone == null)
        {
            damageHitZone =
                DamageHitZoneType.None;
        }
        else if (isHeadshot)
        {
            damageHitZone =
                DamageHitZoneType.Head;
        }
        else
        {
            damageHitZone =
                DamageHitZoneType.Body;
        }


        // =============================================================
        // Distance Falloff
        // =============================================================

        float distanceMultiplier =
            CalculateDistanceDamageMultiplier(
                hit.Distance
            );


        // =============================================================
        // Damage
        // =============================================================

        float requestedDamage =
            baseDamage *
            distanceMultiplier *
            zoneMultiplier;


        if (isHeadshot)
        {
            requestedDamage *=
                headshotMultiplier;
        }


        // =============================================================
        // Damage Request
        // =============================================================

        DamageRequest damageRequest =
            new DamageRequest
            {
                RequestedDamage =
                    requestedDamage,

                BaseDamage =
                    baseDamage,

                DamageType =
                    DamageType.Bullet,

                /*
                 * 目前 CombatFeedbackId
                 * 還沒有確認是否已新增 SupportSMG。
                 *
                 * 為了這一版可以直接編譯，
                 * 暫時沿用 AttackRifle Feedback。
                 *
                 * ----------------------------------------------------
                 *
                 * 等 SupportSMG Gameplay 通過後，
                 * 下一階段可以正式新增：
                 *
                 * CombatFeedbackId.SupportSMG
                 *
                 * 再把這裡切過去。
                 */
                FeedbackId =
                    CombatFeedbackId
                        .AttackRifle,

                HitZone =
                    damageHitZone,

                HeadshotDamageMultiplier =
                    headshotMultiplier,

                ForcedHeadshotSource =
                    DamageForcedHeadshotSource.None,

                Attacker =
                    GetOwnerInputAuthority(),

                SourceNetworkObject =
                    GetOwnerPlayerNetworkObject(),

                SourceObject =
                    ownerPlayer != null
                        ? ownerPlayer.gameObject
                        : movement != null
                            ? movement.gameObject
                            : gameObject,

                HitObject =
                    hit.GameObject,

                HitPoint =
                    hit.Point,

                HitNormal =
                    hit.Normal,

                HitDirection =
                    shotDirection.sqrMagnitude >
                        0.0001f
                        ? shotDirection.normalized
                        : Vector3.zero,

                Distance =
                    hit.Distance,

                Sequence =
                    ShotSequence
            };


        bool receiverFound =
            DamageReceiverUtility
                .TryApplyDamage(
                    hit.GameObject,
                    damageRequest,
                    out DamageResult damageResult
                );


        // =============================================================
        // Damage Events
        // =============================================================

        DamageResolved?.Invoke(
            damageResult
        );


        if (receiverFound &&
            damageResult.Accepted)
        {
            DamageConfirmed?.Invoke(
                damageResult
            );
        }


        if (receiverFound &&
            damageResult.HasEffectiveDamage &&
            damageResult.IsHeadshot)
        {
            HeadshotConfirmed?.Invoke(
                damageResult
            );
        }


        // =============================================================
        // Debug
        // =============================================================

        if (debugSMG)
        {
            Debug.Log(
                $"[Support SMG Damage]" +
                $"\n命中：{hit.GameObject.name}" +
                $"\n距離：{hit.Distance:F2}" +
                $"\nHit Zone：{damageHitZone}" +
                $"\nBase Damage：{baseDamage:F2}" +
                $"\nDistance Multiplier：{distanceMultiplier:F3}" +
                $"\nZone Multiplier：{zoneMultiplier:F2}" +
                $"\nRequested Damage：{requestedDamage:F2}" +
                $"\nReceiver Found：{receiverFound}" +
                $"\nAccepted：{damageResult.Accepted}" +
                $"\nApplied Damage：{damageResult.AppliedDamage:F2}" +
                $"\nKilled：{damageResult.KilledTarget}" +
                $"\nReject Reason：{damageResult.RejectReason}",
                hit.GameObject
            );
        }
    }


    private float CalculateDistanceDamageMultiplier(float distance)
    {
        return WeaponHitUtility.CalculateDistanceDamageMultiplier(
            distance, damageFalloffStartDistance,
            damageFalloffEndDistance, minimumDamageMultiplier);
    }


    #endregion


    // =====================================================================
    #region Gameplay Aim


    public Vector3 GetGameplayShotOrigin()
    {
        if (movement != null &&
            movement.KCC != null)
        {
            return
                movement
                    .KCC
                    .Data
                    .TargetPosition +
                Vector3.up *
                shotOriginHeight;
        }


        if (ownerPlayer != null)
        {
            return
                ownerPlayer.transform.position +
                Vector3.up *
                shotOriginHeight;
        }


        return
            transform.position +
            Vector3.up *
            shotOriginHeight;
    }


    public Vector3 GetGameplayAimDirection()
    {
        if (movement != null)
        {
            return
                movement
                    .GetAimDirection();
        }


        if (ownerPlayer != null)
        {
            return
                ownerPlayer
                    .transform
                    .forward;
        }


        return
            transform.forward;
    }


    #endregion


    // =====================================================================
    #region Recoil


    private void ApplyGameplayRecoil()
    {
        if (movement == null)
        {
            return;
        }


        float horizontalSign =
            ShotSequence % 2 == 0
                ? 1f
                : -1f;


        float yawRecoil =
            recoilYawPerShot *
            horizontalSign;


        movement.AddLookRotationImpulse(
            recoilPitchPerShot,
            yawRecoil
        );


        AccumulatedRecoilPitch +=
            recoilPitchPerShot;


        RecoilRecoveryTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                recoilRecoveryDelay
            );


        LastAimPitch +=
            recoilPitchPerShot;
    }


    private void ProcessRecoilRecovery()
    {
        if (movement == null)
        {
            return;
        }


        Vector3 aimDirection =
            GetGameplayAimDirection();


        float currentPitch =
            Mathf.Asin(
                Mathf.Clamp(
                    aimDirection.y,
                    -1f,
                    1f
                )
            ) *
            -Mathf.Rad2Deg;


        if (ShotSequence == 0 &&
            Mathf.Approximately(
                AccumulatedRecoilPitch,
                0f
            ))
        {
            LastAimPitch =
                currentPitch;


            return;
        }


        float pitchDelta =
            currentPitch -
            LastAimPitch;


        // =============================================================
        // Player 手動壓槍
        // =============================================================

        if (pitchDelta > 0f &&
            AccumulatedRecoilPitch < 0f)
        {
            AccumulatedRecoilPitch =
                Mathf.Min(
                    0f,
                    AccumulatedRecoilPitch +
                    pitchDelta
                );
        }


        float appliedRecovery =
            0f;


        // =============================================================
        // Automatic Recovery
        // =============================================================

        if (AccumulatedRecoilPitch < 0f &&
            RecoilRecoveryTimer
                .ExpiredOrNotRunning(
                    Runner
                ))
        {
            float recoveryAmount =
                recoilRecoverySpeed *
                Runner.DeltaTime;


            appliedRecovery =
                Mathf.Min(
                    recoveryAmount,
                    -AccumulatedRecoilPitch
                );


            movement.AddLookRotationImpulse(
                appliedRecovery,
                0f
            );


            AccumulatedRecoilPitch +=
                appliedRecovery;
        }


        LastAimPitch =
            currentPitch +
            appliedRecovery;
    }


    #endregion


    // =====================================================================
    #region Reload


    private void UpdateReload()
    {
        if (IsReloading == false)
        {
            return;
        }


        /*
         * 如果特殊技能在 Reload 中途啟動，
         * SetSpecialModeActive(true)
         * 會先 StopReload。
         *
         * 這裡再留一道防呆。
         */
        if (IsSpecialModeActive)
        {
            StopReload();


            return;
        }


        if (ReloadTimer
                .Expired(
                    Runner
                ) == false)
        {
            return;
        }


        CompleteReload();
    }


    public bool TryStartReload()
    {
        if (IsInitialized == false)
        {
            return false;
        }


        if (IsSpecialModeActive)
        {
            return false;
        }


        if (IsReloading)
        {
            return false;
        }


        if (MagazineAmmo >=
            MagazineCapacity)
        {
            return false;
        }


        if (HasInfiniteReserveAmmo ==
                false &&
            ReserveAmmo <= 0)
        {
            return false;
        }


        if (reloadDuration <= 0f)
        {
            CompleteReload();


            return true;
        }


        IsReloading =
            true;


        ReloadTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                reloadDuration
            );


        if (debugSMG &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Support SMG] 開始換彈。" +
                $"\nMagazine：{MagazineAmmo}/{MagazineCapacity}" +
                $"\nDuration：{reloadDuration:F2}",
                this
            );
        }


        return true;
    }


    private void CompleteReload()
    {
        int missingAmmo =
            MagazineCapacity -
            MagazineAmmo;


        if (missingAmmo <= 0)
        {
            StopReload();


            return;
        }


        if (HasInfiniteReserveAmmo)
        {
            MagazineAmmo =
                MagazineCapacity;
        }
        else
        {
            int ammoToLoad =
                Mathf.Min(
                    missingAmmo,
                    ReserveAmmo
                );


            MagazineAmmo +=
                ammoToLoad;


            ReserveAmmo -=
                ammoToLoad;
        }


        StopReload();


        if (debugSMG &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Support SMG] 換彈完成。" +
                $"\nMagazine：{MagazineAmmo}/{MagazineCapacity}" +
                $"\nReserve：" +
                $"{(HasInfiniteReserveAmmo ? "無限" : ReserveAmmo.ToString())}",
                this
            );
        }
    }


    private void StopReload()
    {
        IsReloading =
            false;


        ReloadTimer =
            TickTimer.None;
    }


    /// <summary>
    /// 外部 Gameplay 系統要求中斷目前 SMG 武器動作。
    ///
    /// 目前支援：
    ///
    /// Reload Cancel。
    /// </summary>
    public bool InterruptWeaponAction(
        WeaponInterruptReason reason
    )
    {
        if (IsInitialized == false)
        {
            return false;
        }


        bool interrupted =
            false;


        if (IsReloading)
        {
            StopReload();


            interrupted =
                true;


            if (debugSMG &&
                Object.HasInputAuthority)
            {
                Debug.Log(
                    $"[Support SMG] 武器動作中斷。" +
                    $"\nReason：{reason}" +
                    $"\nAction：Reload" +
                    $"\nMagazine：{MagazineAmmo}/{MagazineCapacity}",
                    this
                );
            }
        }


        return
            interrupted;
    }


    #endregion


    // =====================================================================
    #region Special Mode


    /// <summary>
    /// 切換 Support 特殊技能的 SMG 模式。
    ///
    /// ====================================================================
    ///
    /// Active：
    ///
    /// 1. Infinite Magazine。
    /// 2. 使用 Special Fire Rate。
    /// 3. 不新增 Gameplay Recoil。
    /// 4. 使用 Special Heal Amount。
    ///
    /// ====================================================================
    ///
    /// Deactive：
    ///
    /// 完整恢復普通武器規則。
    ///
    /// ====================================================================
    ///
    /// 非常重要：
    ///
    /// 這裡不會：
    ///
    /// MagazineAmmo = 999999。
    ///
    /// 所以：
    ///
    /// 技能開始前 13 / 30
    ///
    /// ↓
    ///
    /// 特殊技能期間無限射
    ///
    /// ↓
    ///
    /// 技能結束
    ///
    /// 仍然是 13 / 30。
    /// </summary>
    public void SetSpecialModeActive(
        bool active
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        if (IsSpecialModeActive ==
            active)
        {
            return;
        }


        IsSpecialModeActive =
            active;


        // =============================================================
        // 進入 Special
        // =============================================================

        if (active)
        {
            /*
             * 無限彈匣模式不需要 Reload。
             *
             * 如果玩家正在換彈，
             * 直接取消，
             * 但完全不補子彈。
             */
            if (IsReloading)
            {
                StopReload();
            }
        }


        if (debugSMG)
        {
            Debug.Log(
                $"[Support SMG] Special Mode Changed" +
                $"\nActive：{active}" +
                $"\nEffective Fire Rate：{EffectiveFireRate:F2}" +
                $"\nCurrent Heal：{CurrentHealAmount:F2}" +
                $"\nInfinite Magazine：{HasInfiniteMagazine}" +
                $"\nMagazine：{MagazineAmmo}/{MagazineCapacity}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Local Tracer


    public Vector3 GetTracerShotOrigin()
    {
        if (tracerMuzzlePoint != null)
        {
            return
                tracerMuzzlePoint.position;
        }


        return
            GetGameplayShotOrigin();
    }


    private Vector3
        GetLocalVisualTracerEndPoint(
            Vector3 origin,
            Vector3 direction
        )
    {
        Vector3 endPoint =
            origin +
            direction *
            maxShotDistance;


        if (Runner == null)
        {
            return
                endPoint;
        }


        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();


        if (physicsScene.IsValid() ==
            false)
        {
            return
                endPoint;
        }


        bool hasHit =
            physicsScene.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                maxShotDistance,
                hitMask,
                QueryTriggerInteraction.Ignore
            );


        if (hasHit)
        {
            NetworkObject hitNetworkObject =
                hit.collider
                    .GetComponentInParent<
                        NetworkObject
                    >();


            if (IsOwnerPlayerObject(
                    hitNetworkObject
                ) == false)
            {
                endPoint =
                    hit.point;
            }
        }


        return
            endPoint;
    }


    private void ShowLocalBulletTracer(
        Vector3 shotEndPoint
    )
    {
        if (bulletTracerLineDrawer == null)
        {
            return;
        }


        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            return;
        }


        bulletTracerLineDrawer.DrawTracer(
            GetTracerShotOrigin(),
            shotEndPoint
        );
    }


    #endregion


    // =====================================================================
    #region Upgrade API


    /// <summary>
    /// 修改普通狀態射速倍率。
    ///
    /// 例如：
    ///
    /// +20%
    /// → 1.2。
    /// </summary>
    public void SetFireRateMultiplier(
        float multiplier
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        RuntimeFireRateMultiplier =
            Mathf.Max(
                0.01f,
                multiplier
            );
    }


    /// <summary>
    /// 修改特殊技能期間的射速倍率。
    ///
    /// 例如：
    ///
    /// Base Special Fire Rate = 3
    ///
    /// 升級 +20%
    ///
    /// → multiplier = 1.2
    ///
    /// → 最終 3.6 發 / 秒。
    /// </summary>
    public void SetSpecialFireRateMultiplier(
        float multiplier
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }


        RuntimeSpecialFireRateMultiplier =
            Mathf.Max(
                0.01f,
                multiplier
            );
    }


    #endregion


    // =====================================================================
    #region Dynamic ViewModel Muzzle


    /// <summary>
    /// 動態指定目前 Support ViewModel 的真正槍口。
    ///
    /// 只影響 BulletTracerLineDrawer。
    /// 不影響 Gameplay Hitscan。
    /// </summary>
    public void SetTracerMuzzlePoint(
        Transform newMuzzlePoint
    )
    {
        tracerMuzzlePoint =
            newMuzzlePoint;
    }


    /// <summary>
    /// 清除目前 Support ViewModel 提供的 MuzzlePoint。
    ///
    /// 只允許目前真的被使用的 Transform
    /// 清除自己。
    /// </summary>
    public void ClearTracerMuzzlePoint(
        Transform currentMuzzlePoint
    )
    {
        if (tracerMuzzlePoint !=
            currentMuzzlePoint)
        {
            return;
        }


        tracerMuzzlePoint =
            null;
    }


    #endregion
}