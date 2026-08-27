using Fusion;
using System;
using UnityEngine;

/// <summary>
/// Attack Rifle 的備用彈藥模式。
/// </summary>
public enum RifleAmmoSupplyMode : byte
{
    /// <summary>
    /// 有限備用彈藥。
    ///
    /// 例如：
/// 30 / 180
///
/// 換彈會真正扣除 Reserve Ammo。
    /// </summary>
    ReserveAmmo = 0,

    /// <summary>
    /// 無限備用彈藥。
    ///
    /// 注意：
/// 彈匣本身仍然只有 30 發。
///
/// 打完仍需要換彈，
/// 只是換彈不會消耗 Reserve Ammo。
    /// </summary>
    InfiniteReserve = 1
}

/// <summary>
/// 單次步槍射擊使用的額外修正。
///
/// 普通射擊：
///
/// AmmoCost = 1
/// DamageMultiplier = 1
/// UseShotDirectionOverride = false
///
/// 專注射擊：
///
/// AmmoCost = 2
/// DamageMultiplier = 2
///
/// 如果成功找到 Auto Lock Target：
///
/// UseShotDirectionOverride = true
/// ShotDirectionOverride = 指向敵人胸口
///
/// 注意：
///
/// 這只修改「子彈方向」。
///
/// 不會修改：
/// Camera
/// KCC Look
/// 玩家準心
/// Mouse Input
/// </summary>
public struct RifleShotModifier
{
    /// <summary>
    /// 這次射擊需要消耗幾發子彈。
    /// </summary>
    public int AmmoCost;

    /// <summary>
    /// 這次射擊傷害倍率。
    /// </summary>
    public float DamageMultiplier;

    /// <summary>
    /// 是否覆蓋原本由玩家準心決定的射擊方向。
    ///
    /// 專注 Auto Lock 找到目標時才會是 true。
    /// </summary>
    public bool UseShotDirectionOverride;

    /// <summary>
    /// 專注 Auto Lock 修正後的世界射擊方向。
    ///
    /// 不應拿這個值去修改 Camera。
    /// </summary>
    public Vector3 ShotDirectionOverride;

    /// <summary>
    /// 這發射擊是否真的使用了 Auto Lock。
    ///
    /// 現在主要方便 Debug。
    ///
    /// 未來可以拿來做：
/// 特殊 Tracer
/// 鎖定音效
/// Focus Hit Marker
/// 專注子彈 VFX。
    /// </summary>
    public bool WasAutoLocked;

    /// <summary>
    /// 普通射擊。
    /// </summary>
    public static RifleShotModifier Normal
    {
        get
        {
            return new RifleShotModifier
            {
                AmmoCost =
                    1,

                DamageMultiplier =
                    1f,

                UseShotDirectionOverride =
                    false,

                ShotDirectionOverride =
                    default,

                WasAutoLocked =
                    false
            };
        }
    }
}

/// <summary>
/// 攻擊職業使用的 30 發全自動步槍。
///
/// 負責：
/// 1. 30 發彈匣。
/// 2. 有限備彈 / 無限備彈。
/// 3. 全自動射擊。
/// 4. Fire Rate。
/// 5. 換彈。
/// 6. Gameplay Recoil。
/// 7. Hitscan。
/// 8. Photon Fusion Lag Compensation。
/// 9. 距離傷害衰退。
/// 10. Headshot。
/// 11. 暴頭獎勵接口。
///
/// 不負責：
/// 1. ADS。
/// 2. 專注技能。
/// 3. 槍械動畫。
/// 4. 槍口特效。
/// 5. 音效。
///
/// 這些之後會由其他模組接入。
/// </summary>
[DisallowMultipleComponent]
public class AttackRifle : NetworkBehaviour,ICombatDamageFeedbackSource
{
    // =====================================================================
    #region 引用

    [Header("玩家引用")]

    [SerializeField]
    [Tooltip("玩家移動模組。步槍會從這裡取得 KCC 瞄準方向，並在射擊後加入後座力。若留空會自動取得。")]
    private PlayerMovement movement;

    #endregion

    // =====================================================================
    #region 世界槍聲

    [Header("世界槍聲")]

    [SerializeField]
    [Tooltip(
        "AttackRifle 正式射擊成功時，由 State Authority 透過 Player Root 的 NetworkPlayerAudioEmitter 傳給所有 Client 的 3D 世界槍聲。\n\n" +
        "這裡必須指定 Network ID 大於 0 的 GameplayAudioCue，並且同一個 Cue 必須已加入 Player 使用的 GameplayAudioCatalog。\n\n" +
        "不要放第一人稱換彈、拉槍機等本機細節 Cue；那些聲音不應透過這條世界 RPC 傳送。")]
    private GameplayAudioCue worldGunshotCue;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "AttackRifle 呼叫世界槍聲時額外乘上的音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0.5 = Cue 音量的一半。\n" +
        "0 = 保留事件但靜音。\n\n" +
        "NetworkPlayerAudioEmitter 會把傳入值限制在 0～4，最終 AudioSource 音量仍會限制在 0～1。")]
    private float worldGunshotVolumeScale =
        1f;

    /// <summary>
    /// 真正 Player NetworkObject 上的世界聲音發送器。
    ///
    /// AttackRifle 位於 Attack Profession Runtime，
    /// NetworkPlayerAudioEmitter 則位於 Player Root，
    /// 所以不能使用 GetComponent 從 Runtime 自己尋找。
    ///
    /// 這份引用只在 BindOwnerPlayer 時快取，
    /// 不會在每一發射擊時進行全場搜尋。
    /// </summary>
    private NetworkPlayerAudioEmitter
        networkAudioEmitter;

    #endregion

    // =====================================================================
    #region Owner Player Binding

    /// <summary>
    /// 這把 Attack Rifle
    /// 實際屬於哪一個 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// AttackRifle 未來位於獨立的
    /// AttackProfessionRuntime NetworkObject。
    ///
    /// 所以：
    ///
    /// AttackRifle.Object
    ///
    /// 將不再等於：
    ///
    /// Player NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個差異非常重要。
    ///
    /// 不然像：
    ///
    /// 排除自己的 Hitbox
    /// Damage Source
    /// Player Movement
    ///
    /// 都會抓錯物件。
    /// </summary>
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 的 NetworkObject。
    ///
    /// 專門用於：
    ///
    /// Raycast 排除自己
    /// Damage Source
    /// Gameplay Owner 判斷。
    /// </summary>
    private NetworkObject ownerPlayerNetworkObject;

    /// <summary>
    /// 目前 Rifle 是否存在特殊命中接管器。
    ///
    /// ------------------------------------------------------------
    ///
    /// Attack Runtime：
    ///
    /// 通常為 null
    /// →
    /// 所有命中照正常 Damage 流程。
    ///
    /// ------------------------------------------------------------
    ///
    /// Support Runtime：
    ///
    /// 會找到 SupportRifleHealingAbility
    /// →
    /// Player Hit 可以轉成 Healing。
    ///
    /// ------------------------------------------------------------
    ///
    /// AttackRifle 本身完全不需要知道
    /// SupportRifleHealingAbility 的存在。
    /// </summary>
    private IRifleHitOverride
        rifleHitOverride;

    /// <summary>
    /// 目前這把槍真正所屬的 Player。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    /// <summary>
    /// 將 Attack Rifle 綁定到 Player Core。
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
             * 職業 Runtime 被解除綁定或準備銷毀時，
             * 不可留下上一名 Owner 的 Audio Emitter。
             */
            networkAudioEmitter =
                null;

            return;
        }

        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        /*
        * PlayerMovement 永遠屬於 Core。
        */
        movement =
            ownerPlayer.Movement;

        /*
         * NetworkPlayerAudioEmitter 固定存在 Player Network Prefab Root。
         *
         * AttackRifle 本身位於可被 Spawn / Despawn 的
         * Attack Profession Runtime，不能把 Emitter 掛在這裡，
         * 否則切換職業時會產生額外的網路音訊發送器。
         */
        networkAudioEmitter =
            ownerPlayer.GetComponent<
                NetworkPlayerAudioEmitter
            >();

        if (movement == null)
        {
            Debug.LogError(
                $"[{nameof(AttackRifle)}] " +
                $"Owner Player 找不到 PlayerMovement。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }

        if (networkAudioEmitter == null)
        {
            Debug.LogError(
                $"[{nameof(AttackRifle)}] " +
                $"Owner Player Root 找不到 " +
                $"{nameof(NetworkPlayerAudioEmitter)}，" +
                $"正式射擊仍可造成傷害，但無法送出世界槍聲。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }
    
    /// <summary>
    /// 尋找與 AttackRifle 位於同一個
    /// Profession Runtime Root 上的 Rifle Hit Override。
    ///
    /// ====================================================================
    ///
    /// 為什麼不用直接寫：
    ///
    /// GetComponent<SupportRifleHealingAbility>()
    ///
    /// 因為 AttackRifle 是共用武器 Gameplay。
    ///
    /// 它不應該知道：
    ///
    /// Support
    /// Healing
    /// 任何特定職業名稱。
    ///
    /// ====================================================================
    ///
    /// Attack Runtime：
    ///
    /// 找不到 IRifleHitOverride
    /// → rifleHitOverride = null。
    ///
    /// Support Runtime：
    ///
    /// 找到 SupportRifleHealingAbility
    /// → 讓它有機會在 Damage 前接管命中。
    /// </summary>
    private void ResolveRifleHitOverride()
    {
        rifleHitOverride =
            null;

        MonoBehaviour[] behaviours =
            GetComponents<MonoBehaviour>();

        for (int i = 0;
            i < behaviours.Length;
            i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IRifleHitOverride hitOverride)
            {
                rifleHitOverride =
                    hitOverride;

                break;
            }
        }
    }

    /// <summary>
    /// 取得這把武器真正所屬的 Player NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 優先：
    ///
    /// Runtime Binding 的 Owner。
    ///
    /// ↓
    ///
    /// 舊架構 PlayerMovement 所在 NetworkObject。
    ///
    /// ↓
    ///
    /// 最後才退回 AttackRifle.Object。
    ///
    /// ------------------------------------------------------------
    ///
    /// 最後的 Object fallback
    /// 主要只是讓搬家前的舊架構保持相容。
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

        if (movement != null)
        {
            NetworkObject movementNetworkObject =
                movement.GetComponent<NetworkObject>();

            if (movementNetworkObject != null)
            {
                return movementNetworkObject;
            }
        }

        return Object;
    }

    /// <summary>
    /// 判斷指定 NetworkObject
    /// 是否就是這把武器的 Owner Player。
    /// </summary>
    private bool IsOwnerPlayerObject(
        NetworkObject networkObject
    )
    {
        if (networkObject == null)
        {
            return false;
        }

        NetworkObject ownerObject =
            GetOwnerPlayerNetworkObject();

        return
            ownerObject != null &&
            networkObject ==
            ownerObject;
    }

    #endregion

    // =====================================================================

    #region 彈匣

    [Header("彈匣設定")]

    [SerializeField]
    [Min(1)]
    [Tooltip("步槍一個彈匣最多可裝多少發子彈。目前攻擊職業步槍設計為 30 發。")]
    private int magazineCapacity = 30;

    [SerializeField]
    [Tooltip("備用彈藥模式。Reserve Ammo 代表有真正的備用彈藥數量。Infinite Reserve 代表備彈無限，但彈匣仍會消耗，因此打完仍然需要換彈。")]
    private RifleAmmoSupplyMode ammoSupplyMode =
        RifleAmmoSupplyMode.ReserveAmmo;

    [SerializeField]
    [Min(0)]
    [Tooltip("使用 Reserve Ammo 模式時，玩家生成時擁有多少備用子彈。此數值不包含彈匣內的 30 發。Infinite Reserve 模式會忽略這個數值。")]
    private int startingReserveAmmo = 180;

    #endregion

    // =====================================================================
    #region 換彈

    [Header("換彈設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("完整換彈需要的秒數。目前沒有動畫，因此先單純使用時間控制。未來加入 Reload Animation 後，這個時間會與動畫同步。")]
    private float reloadDuration = 1.8f;

    [SerializeField]
    [Tooltip("開啟後，彈匣完全沒子彈且玩家仍按住射擊時，會自動開始換彈。")]
    private bool autoReloadWhenEmpty = true;

    #endregion

    // =====================================================================
    #region 射速

    [Header("射速設定")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("步槍基礎每秒射擊次數。例如 10 代表每秒最多射擊 10 發，也就是約 600 RPM。之後升級系統不需要直接改這個數值，而是修改 Runtime Fire Rate Multiplier。")]
    private float baseFireRate = 10f;

    #endregion

    // =====================================================================
    #region Hitscan

    [Header("Hitscan 設定")]

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("步槍最遠射擊距離。")]
    private float maxShotDistance = 200f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("射線起點相對玩家 KCC Target Position 的高度。目前沒有武器模型，因此 Gameplay Hitscan 直接從第一人稱視線高度射出。未來槍口位置只負責視覺特效，實際命中仍建議由玩家瞄準視線判斷。")]
    private float shotOriginHeight = 1.6f;

    [SerializeField]
    [Tooltip("步槍可以命中的 Layer。建議包含世界場景與敵人的 Photon Hitbox Layer。即使程式有額外排除自己的防呆，仍建議將玩家一般 Collider Layer 排除。")]
    private LayerMask hitMask = ~0;

    [SerializeField]
    [Tooltip("開啟後使用 Photon Fusion Subtick Accuracy，讓高速 FPS 的命中判定更接近玩家實際看到的插值位置。")]
    private bool useSubtickAccuracy = true;

    [SerializeField]
    [Tooltip("如果目前 NetworkProjectConfig 沒有啟用 Lag Compensation，是否暫時退回 Runner 對應 PhysicsScene 的普通 Raycast。這主要方便目前原型測試，不建議作為正式多人命中方案。")]
    private bool allowPhysicsFallback = true;

    #endregion

    // =====================================================================
    #region 傷害

    [Header("傷害設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("沒有距離衰退、沒有暴頭、沒有技能倍率時的一發基礎傷害。")]
    private float baseDamage = 20f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("距離小於此數值時完全不產生傷害衰退。")]
    private float damageFalloffStartDistance = 30f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("距離達到此數值後，傷害會降到 Minimum Damage Multiplier 所設定的最低倍率。")]
    private float damageFalloffEndDistance = 150f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("最遠距離時至少保留多少傷害。因為目前設計是『低傷害衰退』，建議先使用 0.8 到 0.9。例如 0.85 代表最遠仍保留 85% 傷害。")]
    private float minimumDamageMultiplier = 0.85f;

    [SerializeField]
    [Min(1f)]
    [Tooltip("命中 WeaponHitZone = Head 時額外乘上的暴頭倍率。例如 2 代表兩倍傷害。")]
    private float headshotMultiplier = 2f;

    #endregion

    // =====================================================================
    #region 後座力

    [Header("Gameplay 後座力")]

    [SerializeField]
    [Tooltip("每發子彈加入 KCC Look Pitch 的角度。負值通常會讓視角往上抬，因此可以先測試 -0.5 到 -1.5 左右。這是 Gameplay Recoil，之後 ViewModel 槍械動畫後座力會另外處理。")]
    private float recoilPitchPerShot = -0.8f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("每發子彈左右偏移的最大固定角度。目前使用可預測的左右交替模式，而不是 Unity Random，避免 Fusion Prediction 與 Re-simulation 因隨機結果不同而失去一致性。設為 0 可完全關閉水平後座力。")]
    private float recoilYawPerShot = 0.25f;

    [Header("後座力回正設定 (Recoil Recovery)")]
    
    [SerializeField]
    [Min(0f)]
    [Tooltip("停止射擊後，延遲多久(秒)才開始自動回正視角。")]
    private float recoilRecoveryDelay = 0.2f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("自動回正的速度(每秒恢復的角度)。數值越大，準心拉回原位的速度越快。")]
    private float recoilRecoverySpeed = 10f;

    #endregion

    // =====================================================================
    #region 彈道視覺化

    [Header("彈道視覺化")]

    [SerializeField]
    [Tooltip("用 LineRenderer 視覺化本地玩家的彈道。若留空會自動從同物件取得。")]
    private BulletTracerLineDrawer bulletTracerLineDrawer;

    [SerializeField]
    [Tooltip("第一人稱武器的槍口位置。LineRenderer 會從這個 Transform 的世界位置開始畫出彈道。這個位置只影響視覺，不會改變真正的 Hitscan 瞄準判定。若目前尚未指定槍口，就會退回 Gameplay Shot Origin。")]
    private Transform tracerMuzzlePoint;

    #endregion

    // =====================================================================

    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示射擊、命中、傷害、暴頭與換彈資訊。")]
    private bool debugRifle = true;

    [SerializeField]
    [Tooltip("開啟後會使用 Debug.DrawRay 顯示射擊方向。")]
    private bool drawShotRay = true;

    #endregion

    // =====================================================================
    #region Networked 武器狀態

    /// <summary>
    /// 目前彈匣剩餘子彈。
    /// </summary>
    [Networked]
    public int MagazineAmmo { get; private set; }

    /// <summary>
    /// 目前備用子彈。
    ///
    /// InfiniteReserve 模式不會真正消耗此欄位。
    /// </summary>
    [Networked]
    public int ReserveAmmo { get; private set; }

    /// <summary>
    /// 是否正在換彈。
    /// </summary>
    [Networked]
    public NetworkBool IsReloading { get; private set; }

    /// <summary>
    /// 換彈完成時間。
    /// </summary>
    [Networked]
    private TickTimer ReloadTimer { get; set; }

    /// <summary>
    /// 下一發允許開火的時間。
    /// </summary>
    [Networked]
    private TickTimer FireCooldownTimer { get; set; }

    /// <summary>
    /// 從這把槍開始生成後，
    /// 總共成功發射了幾發。
    ///
    /// 未來：
    /// Muzzle Flash
    /// Weapon Animation
    /// Tracer
    /// Fire Audio
    ///
    /// 都可以觀察這個 Networked Sequence。
    /// </summary>
    [Networked]
    public int ShotSequence { get; private set; }

    /// <summary>
    /// 射速的執行期倍率。
    ///
    /// 目前初始化為 1。
    ///
    /// 未來升級：
    /// +20% Fire Rate
    ///
    /// 就可以設定成：
    /// 1.2
    /// </summary>
    [Networked]
    public float RuntimeFireRateMultiplier { get; private set; }

    /// <summary>
    /// 武器資料是否已由 State Authority 初始化。
    /// </summary>
    [Networked]
    private NetworkBool IsInitialized { get; set; }

    /// <summary>
    /// 記錄因為開火而累積的向上後座力角度 (通常為負值)。
    /// 玩家手動壓槍會抵消此數值。
    /// </summary>
    [Networked] private float AccumulatedRecoilPitch { get; set; }

    /// <summary>
    /// 計算何時應該開始自動回正的計時器。
    /// </summary>
    [Networked] private TickTimer RecoilRecoveryTimer { get; set; }

    /// <summary>
    /// 記錄上一個 Tick 玩家視角的 Pitch，用來算出本 Tick 玩家有沒有「手動壓槍」。
    /// </summary>
    [Networked] private float LastAimPitch { get; set; }

    #endregion

    // =====================================================================
    #region 本地快取

    /// <summary>
    /// Lag Compensation RaycastAll 使用的重複利用陣列。
    ///
    /// 官方 API 明確指出 RaycastAll 的結果不會依距離排序，
    /// 因此我們會自己尋找最近的有效命中。
    /// </summary>
    private readonly System.Collections.Generic.List<LagCompensatedHit>
        lagCompensatedHits =
            new System.Collections.Generic.List<LagCompensatedHit>(16);

    #endregion

    // =====================================================================
    #region 傷害事件

    /// <summary>
    /// 每一次 Hitscan 碰到物件並完成 DamageSystem 處理後觸發。
    ///
    /// 不論傷害最後有沒有 Accepted，
    /// 都會有 DamageResult。
    ///
    /// 例如：
    ///
    /// 打到普通牆壁
    /// → RejectReason = NoReceiver
    ///
    /// 打到無敵敵人
    /// → RejectReason = Invulnerable
    ///
    /// 正常造成傷害
    /// → Accepted = true
    ///
    /// 這是未來除錯與完整 Combat Feedback
    /// 最底層的結果事件。
    ///
    /// 注意：
    /// 目前正式傷害只在 State Authority 計算，
    /// 所以這個事件目前也是在 State Authority 上觸發。
    /// </summary>
    public event Action<DamageResult>
        DamageResolved;

    /// <summary>
    /// 只有目標正式接受傷害時觸發。
    ///
    /// 未來：
    /// Hit Marker
    /// Hit Sound
    /// Camera Hit Shake
    ///
    /// 都可以建立在這個事件之上。
    ///
    /// 但多人 Client 的本地 UI
    /// 之後仍需要再做「State Authority → 攻擊者本人」
    /// 的網路回饋傳遞。
    /// </summary>
    public event Action<DamageResult>
        DamageConfirmed;

    /// <summary>
    /// 正式造成有效暴頭傷害後觸發。
    ///
    /// 未來 Attack 被動：
    ///
    /// Headshot
    /// ↓
    /// Heal
    ///
    /// 可以從這裡接。
    /// </summary>
    public event Action<DamageResult>
        HeadshotConfirmed;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 取得目前 LineRenderer 彈道真正使用的視覺起點。
    ///
    /// 優先使用：
    /// Tracer Muzzle Point。
    ///
    /// 如果尚未設定槍口：
    /// 退回原本 Gameplay Shot Origin。
    ///
    /// 注意：
    /// 這只影響視覺。
    /// 不影響真正 Hitscan 判定。
    /// </summary>
    public Vector3 GetTracerShotOrigin()
    {
        if (tracerMuzzlePoint != null)
        {
            return tracerMuzzlePoint.position;
        }

        return GetGameplayShotOrigin();
    }

    /// <summary>
    /// 彈匣容量。
    /// </summary>
    public int MagazineCapacity =>
        Mathf.Max(
            1,
            magazineCapacity
        );

    /// <summary>
    /// 是否使用無限備彈。
    /// </summary>
    public bool HasInfiniteReserveAmmo =>
        ammoSupplyMode ==
        RifleAmmoSupplyMode.InfiniteReserve;

    /// <summary>
    /// 目前實際射速。
    ///
    /// = Base Fire Rate
/// × Runtime Fire Rate Multiplier
    /// </summary>
    public float EffectiveFireRate =>
        Mathf.Max(
            0.01f,
            baseFireRate *
            Mathf.Max(
                0.01f,
                RuntimeFireRateMultiplier
            )
        );

    /// <summary>
    /// 目前是否允許再次開火。
    /// </summary>
    public bool IsFireRateReady =>
        Runner != null &&
        FireCooldownTimer
            .ExpiredOrNotRunning(Runner);

    /// <summary>
    /// 換彈剩餘時間。
    /// </summary>
    public float ReloadRemainingSeconds
    {
        get
        {
            if (Runner == null ||
                IsReloading == false)
            {
                return 0f;
            }

            return ReloadTimer
                .RemainingTime(Runner) ?? 0f;
        }
    }

    /// <summary>
    /// 步槍目前設定的最大射擊距離。
    ///
    /// AttackFocusAbility 會使用這個數值，
    /// 避免 Focus Auto Lock 搜尋距離超過步槍真正射程。
    /// </summary>
    public float MaxShotDistance =>
        Mathf.Max(
            0.1f,
            maxShotDistance
        );

    /// <summary>
    /// 步槍使用的射擊 LayerMask。
    ///
    /// Focus Auto Lock 的視線檢查使用完全相同的 Mask，
    /// 避免 Focus 認為可以鎖定，
    /// 但 Rifle 實際射擊卻使用另一組 Layer 規則。
    /// </summary>
    public LayerMask HitMask =>
        hitMask;

    /// <summary>
    /// 取得真正 Gameplay Hitscan 使用的世界起點。
    ///
    /// Focus Auto Lock 必須使用跟 Rifle
    /// 完全一樣的射線起點。
    /// </summary>
    public Vector3 GetGameplayShotOrigin()
    {
        if (movement != null &&
            movement.KCC != null)
        {
            return
                movement.KCC.Data.TargetPosition +
                Vector3.up *
                shotOriginHeight;
        }

        /*
        * 理論上 Runtime Binding 完成後
        * movement 一定存在。
        *
        * 這裡只是安全 fallback。
        */
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

    /// <summary>
    /// 取得玩家目前真正的正常射擊方向。
    ///
    /// 這是 Camera / KCC Look 所決定的原始瞄準方向。
    ///
    /// Focus 必須先使用這個方向判斷：
    /// 玩家是不是本來就已經瞄中敵人。
    /// </summary>
    public Vector3 GetGameplayAimDirection()
    {
        if (movement != null)
        {
            return
                movement.GetAimDirection();
        }

        if (ownerPlayer != null)
        {
            return
                ownerPlayer.transform.forward;
        }

        return
            transform.forward;
    }

    #endregion

    // =====================================================================
    #region Unity / Fusion 生命週期

    private void Awake()
    {
        /*
        * 舊架構相容。
        *
        * 目前 AttackRifle 還在 Player Root，
        * 所以仍然可以從同物件找到 PlayerMovement。
        *
        * 真正搬到 Attack Runtime 後，
        * 這裡找不到是正常的，
        * Runtime Driver 會呼叫 BindOwnerPlayer()。
        */
        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }

        /*
        * 搬家前如果跟 Player 同物件，
        * 先建立 Owner Cache。
        */
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

        /*
        * BulletTracerLineDrawer 未來可以
        * 跟 Attack Runtime 一起存在。
        *
        * Tank Runtime 根本不需要生成它。
        */
        if (bulletTracerLineDrawer == null)
        {
            bulletTracerLineDrawer =
                GetComponent<BulletTracerLineDrawer>();
        }
    }

    public override void Spawned()
    {
        /*
         * 只有 State Authority 決定正式初始武器狀態。
         *
         * Client 之後會收到這些 Networked Property。
         */
        if (Object.HasStateAuthority)
        {
            InitializeWeapon();
        }
    }

    #endregion

    // =====================================================================
    #region 初始化

    /// <summary>
    /// 初始化步槍。
    /// </summary>
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
            
        // ▼ 新增：初始化後座力回正系統的狀態 ▼
        AccumulatedRecoilPitch = 0f;
        LastAimPitch = 0f;
        RecoilRecoveryTimer = TickTimer.None;
        // ▲ 新增結束 ▲

        IsInitialized = true;

        if (debugRifle)
        {
            Debug.Log(
                $"[Attack Rifle] 初始化完成。" +
                $"\n彈匣：{MagazineAmmo}/{MagazineCapacity}" +
                $"\n備彈模式：{ammoSupplyMode}" +
                $"\n備彈：{(HasInfiniteReserveAmmo ? "無限" : ReserveAmmo.ToString())}" +
                $"\n射速：{EffectiveFireRate:F2} 發/秒",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 武器模擬入口

    /// <summary>
    /// 每個 Fusion Tick 由 PlayerWeaponController 呼叫。
    /// </summary>
    /// <param name="fireHeld">
    /// 玩家是否正在按住左鍵。
    /// </param>
    /// <param name="reloadPressed">
    /// 本 Tick 是否按下 R。
    /// </param>
    /// <param name="shotModifier">
    /// 此次射擊使用的額外修正。
    ///
    /// 現在普通射擊：
/// 1 發 / 1 倍傷害。
///
/// 下一階段專注：
/// 2 發 / 2 倍傷害。
    /// </param>
    /// <returns>
    /// 本 Tick 是否真的成功發射一發。
    /// </returns>
    public bool Simulate(
        bool fireHeld,
        bool reloadPressed,
        RifleShotModifier shotModifier
    )
    {
        if (IsInitialized == false)
            return false;

        // ▼ 新增：每 Tick 優先處理後座力平滑回正與玩家手動壓槍偵測 ▼
        ProcessRecoilRecovery();
        // ▲ 新增結束 ▲
        
        // -------------------------------------------------------------
        // 先處理已完成的換彈
        // -------------------------------------------------------------

        UpdateReload();

        // -------------------------------------------------------------
        // 玩家手動要求換彈
        // -------------------------------------------------------------

        if (reloadPressed)
        {
            TryStartReload();
        }

        // -------------------------------------------------------------
        // 左鍵沒有按住
        // -------------------------------------------------------------

        if (fireHeld == false)
            return false;

        // -------------------------------------------------------------
        // 嘗試射擊
        // -------------------------------------------------------------

        bool fired =
            TryFire(
                shotModifier
            );

        /*
         * 如果只是因為彈匣不足而不能開火，
         * 可以自動換彈。
         */
        if (fired == false &&
            autoReloadWhenEmpty &&
            IsReloading == false)
        {
            int requiredAmmo =
                Mathf.Max(
                    1,
                    shotModifier.AmmoCost
                );

            if (MagazineAmmo <
                requiredAmmo)
            {
                TryStartReload();
            }
        }

        return fired;
    }

    #endregion

    // =====================================================================
    #region 射擊

    /// <summary>
    /// 嘗試真正發射一發。
    /// </summary>
    private bool TryFire(
        RifleShotModifier modifier
    )
    {
        if (IsReloading)
            return false;

        if (FireCooldownTimer
                .ExpiredOrNotRunning(Runner) ==
            false)
        {
            return false;
        }

        int ammoCost =
            Mathf.Max(
                1,
                modifier.AmmoCost
            );

        float damageMultiplier =
            Mathf.Max(
                0f,
                modifier.DamageMultiplier
            );

        /*
         * 專注技能之後會需要 2 發。
         *
         * 如果彈匣只剩 1 發，
         * 不允許用 1 發打出雙倍傷害。
         */
        if (MagazineAmmo <
            ammoCost)
        {
            return false;
        }

        // -------------------------------------------------------------
        // 消耗子彈
        // -------------------------------------------------------------

        MagazineAmmo -=
            ammoCost;

        // -------------------------------------------------------------
        // 設定下一發時間
        // -------------------------------------------------------------

        float secondsPerShot =
            1f /
            EffectiveFireRate;

        FireCooldownTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                secondsPerShot
            );

        // -------------------------------------------------------------
        // 射擊編號
        // -------------------------------------------------------------

        ShotSequence++;

        // -------------------------------------------------------------
        // 取得射擊方向
        // -------------------------------------------------------------

        Vector3 shotOrigin =
            GetGameplayShotOrigin();

        // -------------------------------------------------------------
        // Network World Gunshot
        // -------------------------------------------------------------

        /*
         * 這裡之前已經通過：
         *
         * Reload 檢查
         * Fire Cooldown 檢查
         * Magazine Ammo 檢查
         * Ammo 消耗
         * ShotSequence++。
         *
         * 因此走到這裡代表這一發真的成立，
         * 空彈與冷卻中的射擊嘗試都不會送出聲音。
         *
         * Client 預測端也會執行 TryFire，
         * 但 TryPlayWorldGunshot() 只允許 State Authority 發 RPC，
         * 所以同一發不會被預測端重複廣播。
         */
        TryPlayWorldGunshot(
            shotOrigin
        );

        /*
        * 正常情況：
        *
        * 子彈完全沿玩家準心方向射出。
        */
        Vector3 shotDirection =
            GetGameplayAimDirection();

        /*
        * Focus Auto Lock 成功時：
        *
        * 只在這裡修改「這一發子彈」的方向。
        *
        * 不會修改：
        /// KCC LookPitch
        /// KCC Yaw
        /// Camera Transform
        /// Crosshair
        ///
        /// 所以畫面本身完全不會被吸過去。
        */
        if (modifier.UseShotDirectionOverride &&
            modifier.ShotDirectionOverride.sqrMagnitude >
            0.0001f)
        {
            shotDirection =
                modifier
                    .ShotDirectionOverride
                    .normalized;
        }

        // -------------------------------------------------------------
        // Hitscan 傷害 + 彈道終點
        // -------------------------------------------------------------

        /*
        * 預設情況：
        *
        * 如果這發子彈什麼都沒有打到，
        * LineRenderer 就畫到武器最大射程。
        */
        Vector3 tracerEndPoint =
            shotOrigin +
            shotDirection *
            maxShotDistance;

        /*
        * State Authority 負責真正的命中判定。
        *
        * PerformAuthoritativeHitscan()
        * 現在除了計算傷害之外，
        * 還會把真正命中點回傳。
        */
        if (Object.HasStateAuthority)
        {
            tracerEndPoint =
                PerformAuthoritativeHitscan(
                    shotOrigin,
                    shotDirection,
                    damageMultiplier
                );
        }
        /*
        * Client 預測玩家不是 State Authority 時，
        * 為了讓本地玩家仍然可以立刻看見彈道，
        * 暫時使用目前 PhysicsScene 做「純視覺」Raycast。
        *
        * 注意：
        * 這個結果不負責傷害，
        * 真正傷害仍然完全由 State Authority 決定。
        */
        else if (Object.HasInputAuthority)
        {
            tracerEndPoint =
                GetLocalVisualTracerEndPoint(
                    shotOrigin,
                    shotDirection
                );
        }

        // -------------------------------------------------------------
        // 顯示本地彈道
        // -------------------------------------------------------------

        ShowLocalBulletTracer(
            tracerEndPoint
        );

        // -------------------------------------------------------------
        // Gameplay Recoil
        // -------------------------------------------------------------

        ApplyGameplayRecoil();

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

        if (debugRifle &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Rifle] Fire" +
                $"\nShot：{ShotSequence}" +
                $"\nAmmo Cost：{ammoCost}" +
                $"\nDamage Multiplier：{damageMultiplier:F2}" +
                $"\nMagazine：{MagazineAmmo}/{MagazineCapacity}",
                this
            );
        }

        return true;
    }

    /// <summary>
    /// 由 AttackRifle 的 State Authority 發送一次 3D 世界槍聲。
    ///
    /// 這裡只負責把已確認成立的射擊事件交給
    /// Player Root 的 NetworkPlayerAudioEmitter；
    /// Clip 變體、Pitch、RPC 可靠度、Catalog 解析與 AudioSource Pool
    /// 都仍由上一階段的聲音核心統一處理。
    /// </summary>
    /// <param name="worldPosition">
    /// 射擊成立瞬間的 Gameplay Shot Origin。
    /// 使用位置快照，而不是讓短促槍聲持續跟著玩家移動。
    /// </param>
    private void TryPlayWorldGunshot(
        Vector3 worldPosition
    )
    {
        /*
         * AttackRifle 會同時在預測端與 State Authority 模擬。
         * 世界事件只能由 State Authority 廣播一次。
         */
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        /*
         * Cue 未指定屬於 Inspector 設定問題。
         * 不讓它阻斷槍械傷害、彈藥或射擊動畫。
         */
        if (worldGunshotCue == null)
        {
            return;
        }

        /*
         * 正常情況已在 BindOwnerPlayer() 完成快取。
         * 這裡只做一次安全恢復，避免 Script 執行順序或
         * 舊 Prefab 遷移期間漏掉 Binding 就永久失去聲音。
         *
         * 這不是全場搜尋，只會查 Owner Player Root。
         */
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
                worldGunshotCue,
                worldPosition,
                worldGunshotVolumeScale
            );
    }

    #endregion

    // =====================================================================
    #region Lag Compensation

    /// <summary>
    /// 在 State Authority 上執行真正的 Hitscan。
    ///
    /// 除了處理傷害之外，
    /// 現在同時回傳這發子彈真正的終點。
    ///
    /// 有命中：
    /// → 回傳 Hit Point。
    ///
    /// 沒命中：
    /// → 回傳 Max Shot Distance 的終點。
    ///
    /// LineRenderer 會使用這個位置顯示實際彈道。
    /// </summary>
    private Vector3 PerformAuthoritativeHitscan(
        Vector3 origin,
        Vector3 direction,
        float shotDamageMultiplier
    )
    {
        /*
        * 預設沒有命中任何物件。
        *
        * 所以彈道先設定為：
        *
        * 射擊起點
        * +
        * 射擊方向
        * ×
        * 最大射程
        */
        Vector3 tracerEndPoint =
            origin +
            direction *
            maxShotDistance;

        // =============================================================
        // Photon Fusion Lag Compensation
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

            Runner.LagCompensation.RaycastAll(
                origin,
                direction,
                maxShotDistance,
                Object.InputAuthority,
                lagCompensatedHits,
                hitMask,
                true,
                options,
                QueryTriggerInteraction.Ignore
            );

            /*
            * RaycastAll 不保證按照距離排序。
            *
            * 所以繼續使用原本已經寫好的：
            *
            * TryGetNearestValidHit()
            *
            * 尋找第一個真正有效的命中。
            */
            if (TryGetNearestValidHit(
                    lagCompensatedHits,
                    out LagCompensatedHit hit
                ))
            {
                /*
                * ★ 這就是這發子彈真正的終點。
                */
                tracerEndPoint =
                    hit.Point;

                /*
                * 原本的傷害處理完全保留。
                */
                ProcessHit(
                    hit,
                    shotDamageMultiplier,
                    direction
                );
            }

            return tracerEndPoint;
        }

        // =============================================================
        // Physics Fallback
        // =============================================================

        if (allowPhysicsFallback == false)
        {
            return tracerEndPoint;
        }

        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() == false)
        {
            return tracerEndPoint;
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
            /*
            * ★ 普通 Physics Raycast
            * 命中的世界座標。
            */
            tracerEndPoint =
                physicsHit.point;

            /*
            * 保持你原本的傷害處理架構。
            */
            LagCompensatedHit convertedHit =
                (LagCompensatedHit)physicsHit;

            ProcessHit(
                convertedHit,
                shotDamageMultiplier,
                direction
            );
        }

        return tracerEndPoint;
    }

    /// <summary>
    /// 從 Lag Compensation 的所有結果中
    /// 找出距離最近且不是射擊者自己的命中。
    /// </summary>
    private bool TryGetNearestValidHit(
        System.Collections.Generic.List<LagCompensatedHit> hits,
        out LagCompensatedHit nearestHit
    )
    {
        nearestHit =
            default;

        bool found =
            false;

        float nearestDistance =
            float.MaxValue;

        for (int i = 0;
             i < hits.Count;
             i++)
        {
            LagCompensatedHit hit =
                hits[i];

            if (hit.GameObject == null)
                continue;

            /*
             * IgnoreInputAuthority 會排除 Fusion Hitbox，
             * 但 IncludePhysX 仍可能包含一般 Collider。
             *
             * 因此再做一次自己的 NetworkObject 防呆。
             */
            NetworkObject hitNetworkObject =
                hit.GameObject
                    .GetComponentInParent<NetworkObject>();

            /*
            * 注意：
            *
            * AttackRifle 未來的 Object
            * 是 AttackProfessionRuntime，
            *
            * 不是 Player NetworkObject。
            *
            * 因此不能再使用：
            *
            * hitNetworkObject == Object
            *
            * 來判斷自己。
            */
            if (IsOwnerPlayerObject(
                    hitNetworkObject
                ))
            {
                continue;
            }

            if (hit.Distance >=
                nearestDistance)
            {
                continue;
            }

            nearestDistance =
                hit.Distance;

            nearestHit =
                hit;

            found =
                true;
        }

        return found;
    }

    #endregion

    // =====================================================================
    #region 命中與傷害

    /// <summary>
    /// 處理一次 State Authority 已確認的正式命中。
    ///
    /// 這裡負責：
    ///
    /// Weapon Gameplay
    /// ↓
    /// 計算攻擊方應該造成多少傷害
    /// ↓
    /// 建立 DamageRequest
    /// ↓
    /// 送給 IDamageReceiver
    /// ↓
    /// 取得 DamageResult
    ///
    /// AttackRifle 不直接扣 HP。
    /// </summary>
    private void ProcessHit(
        LagCompensatedHit hit,
        float shotDamageMultiplier,
        Vector3 shotDirection
    )
    {
        if (hit.GameObject == null)
            return;

        // =============================================================
        // 特殊 Rifle Hit Override
        // =============================================================

        /*
        * 任何特殊職業命中處理，
        * 都必須發生在：
        *
        * WeaponHitZone
        * Damage Calculation
        * DamageRequest
        *
        * 之前。
        *
        * ------------------------------------------------------------
        *
        * 例如 Support：
        *
        * Rifle
        * ↓
        * Player
        * ↓
        * Healing Override
        * ↓
        * return
        *
        * 完全不會建立 DamageRequest。
        *
        * ------------------------------------------------------------
        *
        * Enemy：
        *
        * Healing Override 回傳 false
        * ↓
        * 繼續下面原本 Damage 流程。
        */
        if (rifleHitOverride != null)
        {
            RifleHitContext hitContext =
                new RifleHitContext
                {
                    OwnerPlayer =
                        ownerPlayer,

                    OwnerPlayerNetworkObject =
                        ownerPlayerNetworkObject,

                    HitObject =
                        hit.GameObject,

                    HitPoint =
                        hit.Point,

                    HitNormal =
                        hit.Normal,

                    ShotDirection =
                        shotDirection.sqrMagnitude >
                            0.0001f
                            ? shotDirection.normalized
                            : Vector3.zero,

                    Distance =
                        hit.Distance,

                    ShotDamageMultiplier =
                        shotDamageMultiplier,

                    ShotSequence =
                        ShotSequence
                };

            bool hitConsumed =
                rifleHitOverride
                    .TryConsumeHit(
                        hitContext
                    );

            /*
            * true：
            *
            * 特殊系統已經完整處理這次命中。
            *
            * Support Player Healing
            * 就會走這裡。
            *
            * --------------------------------------------------------
            *
            * 立即 return，
            * 絕對不能繼續建立 DamageRequest。
            */
            if (hitConsumed)
            {
                return;
            }
        }

        // =============================================================
        // 1. WeaponHitZone
        // =============================================================

        WeaponHitZone hitZone =
            hit.GameObject
                .GetComponent<WeaponHitZone>();

        bool isHeadshot =
            hitZone != null &&
            hitZone.IsHead;

        float zoneMultiplier =
            hitZone != null
                ? hitZone.DamageMultiplier
                : 1f;

        // =============================================================
        // 2. 共用 Damage Hit Zone
        // =============================================================

        DamageHitZoneType damageHitZone;

        if (hitZone == null)
        {
            /*
            * 沒有 WeaponHitZone。
            *
            * 例如：
            * 普通牆壁
            * 測試物件。
            */
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
        // 3. 距離衰退
        // =============================================================

        float distanceMultiplier =
            CalculateDistanceDamageMultiplier(
                hit.Distance
            );

        // =============================================================
        // 4. 攻擊端最終要求傷害
        // =============================================================

        /*
        * 這裡仍然是 AttackRifle 的責任。
        *
        * Base Damage
        * ×
        * Distance
        * ×
        * Hit Zone
        * ×
        * Focus / Skill Multiplier
        */
        float requestedDamage =
            baseDamage *
            distanceMultiplier *
            zoneMultiplier *
            shotDamageMultiplier;

        /*
        * Headshot Multiplier
        * 仍然屬於武器攻擊端計算。
        */
        if (isHeadshot)
        {
            requestedDamage *=
                headshotMultiplier;
        }

        // =============================================================
        // 5. 建立標準 DamageRequest
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
                * 這一發是 Attack 職業步槍造成的。
                *
                * DamageType 仍然保持 Bullet。
                *
                * FeedbackId 只是讓 Presentation
                * 知道這不是 Tank / Support 的攻擊。
                */
                FeedbackId =
                    CombatFeedbackId.AttackRifle,

                HitZone =
                    damageHitZone,

                HeadshotDamageMultiplier =
                    headshotMultiplier,

                ForcedHeadshotSource =
                    DamageForcedHeadshotSource.None,

                Attacker =
                    Object.InputAuthority,

                /*
                * Damage Gameplay Source
                * 目前保持代表「玩家本人」。
                *
                * 即使 AttackRifle 未來位於
                * AttackProfessionRuntime，
                * SourceNetworkObject 仍然指向 Player Core。
                */
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
                    shotDirection.sqrMagnitude > 0.0001f
                        ? shotDirection.normalized
                        : Vector3.zero,

                Distance =
                    hit.Distance,

                Sequence =
                    ShotSequence
            };

        // =============================================================
        // 6. 交給 Damage System
        // =============================================================

            bool receiverFound =
                DamageReceiverUtility.TryApplyDamage(
                    hit.GameObject,
                    damageRequest,
                    out DamageResult damageResult
                );

        // =============================================================
        // 7. 所有傷害解析結果
        // =============================================================

        /*
        * 即使是：
        *
        * NoReceiver
        * Invulnerable
        * Blocked
        *
        * DamageResolved 都會收到。
        *
        * 未來這很適合拿來做：
        * Debug
        * 特殊打擊音效
        * 護盾反應。
        */
        DamageResolved?.Invoke(
            damageResult
        );

        // =============================================================
        // 8. 正式有效命中
        // =============================================================

        if (receiverFound &&
            damageResult.Accepted)
        {
            /*
            * 這才是未來：
            *
            * Hit Marker
            * Camera Hit Shake
            * Hit Sound
            *
            * 應該使用的正式成功訊號。
            */
            DamageConfirmed?.Invoke(
                damageResult
            );
        }

        // =============================================================
        // 9. 暴頭 Gameplay Reward
        // =============================================================

        /*
        * 暴頭回血之類的 Gameplay Reward，
        * 我建議要求 AppliedDamage > 0。
        *
        * 避免：
        *
        * 對完全無敵的敵人暴頭
        * ↓
        * 沒造成任何傷害
        * ↓
        * 玩家卻一直回血。
        */
        if (receiverFound &&
            damageResult.HasEffectiveDamage &&
            damageResult.IsHeadshot)
        {
            HeadshotConfirmed?.Invoke(
                damageResult
            );
        }

        // =============================================================
        // 10. Debug
        // =============================================================

        if (debugRifle)
        {
            Debug.Log(
                $"[Attack Rifle] 傷害解析完成。" +
                $"\n命中物件：{hit.GameObject.name}" +
                $"\n距離：{hit.Distance:F2}" +
                $"\n命中區域：{damageHitZone}" +
                $"\n基礎傷害：{baseDamage:F2}" +
                $"\n距離倍率：{distanceMultiplier:F3}" +
                $"\n部位倍率：{zoneMultiplier:F2}" +
                $"\n技能倍率：{shotDamageMultiplier:F2}" +
                $"\nRequested Damage：{requestedDamage:F2}" +
                $"\n找到 Damage Receiver：{receiverFound}" +
                $"\nAccepted：{damageResult.Accepted}" +
                $"\nApplied Damage：{damageResult.AppliedDamage:F2}" +
                $"\nKilled Target：{damageResult.KilledTarget}" +
                $"\nReject Reason：{damageResult.RejectReason}",
                hit.GameObject
            );
        }
    }

    /// <summary>
    /// 計算距離傷害倍率。
    ///
    /// Start 之前 = 1。
/// End 之後 = Minimum Damage Multiplier。
    /// </summary>
    private float CalculateDistanceDamageMultiplier(
        float distance
    )
    {
        if (distance <=
            damageFalloffStartDistance)
        {
            return 1f;
        }

        float validEndDistance =
            Mathf.Max(
                damageFalloffStartDistance +
                0.01f,

                damageFalloffEndDistance
            );

        float progress =
            Mathf.InverseLerp(
                damageFalloffStartDistance,
                validEndDistance,
                distance
            );

        return Mathf.Lerp(
            1f,
            minimumDamageMultiplier,
            progress
        );
    }

    #endregion

    // =====================================================================
    #region Recoil

    /// <summary>
    /// 將後座力加入 KCC Look Rotation。
    /// 並記錄到回正累積池中。
    /// </summary>
    private void ApplyGameplayRecoil()
    {
        float horizontalSign = (ShotSequence % 2 == 0) ? 1f : -1f;
        float yawRecoil = recoilYawPerShot * horizontalSign;

        // 對 KCC 加上這發子彈的後座力 Impulse
        movement.AddLookRotationImpulse(recoilPitchPerShot, yawRecoil);

        // ▼ 新增：記錄回正狀態與計時 ▼
        
        // 1. 記錄累積向上的後座力
        // (recoilPitchPerShot 通常為負值，代表視角往上，累積池會變成例如 -0.8, -1.6...)
        AccumulatedRecoilPitch += recoilPitchPerShot;

        // 2. 刷新自動回正的延遲計時器。只有在停止射擊經過這段時間後，才會開始回正。
        RecoilRecoveryTimer = TickTimer.CreateFromSeconds(Runner, recoilRecoveryDelay);

        // 3. 因為我們程式主動給了向上 Impulse，下一幀的 AimDirection Pitch 會變小（往上）。
        // 為了不讓 ProcessRecoilRecovery() 誤判，我們提前把這個位移加到 LastAimPitch 內。
        LastAimPitch += recoilPitchPerShot;
        
        // ▲ 新增結束 ▲
    }

    /// <summary>
    /// 每幀執行：偵測玩家手動壓槍，並在沒有射擊時平滑拉回視角。
    /// </summary>
    private void ProcessRecoilRecovery()
    {
        // 1. 取得當前的瞄準方向，並轉換為 Pitch 角度
        // Unity 中 forward.y 的 Asin 範圍是 -PI/2 到 PI/2。
        // 為了對應前面 recoilPitchPerShot 負值代表「抬高視角」，這裡我們掛上負號轉譯。
        // （結果：往上瞄準 = 負 Pitch，往下瞄準 = 正 Pitch）
        Vector3 aimDir = GetGameplayAimDirection();
        float currentPitch = Mathf.Asin(aimDir.y) * -Mathf.Rad2Deg;

        // 防呆：如果是剛生成或是剛初始化，直接同步
        if (ShotSequence == 0 && AccumulatedRecoilPitch == 0f)
        {
            LastAimPitch = currentPitch;
            return;
        }

        // 2. 計算與上一幀的 Pitch 差異 (Delta)
        float pitchDelta = currentPitch - LastAimPitch;

        // 3. 偵測玩家手動下壓視角 (Player Input Override)
        // 如果 pitchDelta > 0，代表玩家正把滑鼠/搖桿往下壓。
        // 這時我們應該把玩家下壓的量從「累積後座力池」裡面扣除！
        // 這樣可以達到「玩家手動壓槍抵消回正力道」的效果，避免程式後續硬拉視角。
        if (pitchDelta > 0f && AccumulatedRecoilPitch < 0f)
        {
            // 例如累積了 -1.6 的後座力，玩家這幀壓槍壓了 +0.5。
            // 累積池就只剩下 -1.1 需要程式回正。
            AccumulatedRecoilPitch = Mathf.Min(0f, AccumulatedRecoilPitch + pitchDelta);
        }

        float appliedRecovery = 0f;

        // 4. 執行平滑回正
        // 當累積池還有殘留 (小於 0)，且已經過了延遲時間 (未開火)
        if (AccumulatedRecoilPitch < 0f && RecoilRecoveryTimer.ExpiredOrNotRunning(Runner))
        {
            // 本 Tick 預計要回正的總量
            float recoveryAmount = recoilRecoverySpeed * Runner.DeltaTime;
            
            // 實際回正量不能超過剩餘的累積後座力 (注意 AccumulatedRecoilPitch 是負值)
            appliedRecovery = Mathf.Min(recoveryAmount, -AccumulatedRecoilPitch);

            // 加入正值 Impulse，讓視角往下回歸
            if (movement != null)
            {
                movement.AddLookRotationImpulse(appliedRecovery, 0f);
            }

            // 將已經回正的量從池子裡扣除
            AccumulatedRecoilPitch += appliedRecovery;
        }

        // 5. 更新上一幀的 Pitch
        // 注意：因為我們可能在第 4 步加入了 appliedRecovery (Impulse)，
        // KCC 實際的 Pitch 已經被我們往下推了。為了避免下一幀把「程式的回正」
        // 誤認成「玩家的手動壓槍」，我們必須把這個量預先加給 LastAimPitch。
        LastAimPitch = currentPitch + appliedRecovery;
    }

    #endregion

    // =====================================================================
    #region Reload

    /// <summary>
    /// 檢查換彈是否完成。
    /// </summary>
    private void UpdateReload()
    {
        if (IsReloading == false)
            return;

        if (ReloadTimer.Expired(Runner) ==
            false)
        {
            return;
        }

        CompleteReload();
    }

    /// <summary>
    /// 嘗試開始換彈。
    /// </summary>
    public bool TryStartReload()
    {
        if (IsReloading)
            return false;

        if (MagazineAmmo >=
            MagazineCapacity)
        {
            return false;
        }

        /*
         * 有限備彈模式且完全沒有備彈，
         * 不能換彈。
         */
        if (HasInfiniteReserveAmmo == false &&
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

        if (debugRifle &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Rifle] 開始換彈。" +
                $"\n目前：{MagazineAmmo}/{MagazineCapacity}" +
                $"\n時間：{reloadDuration:F2} 秒",
                this
            );
        }

        return true;
    }

    /// <summary>
    /// 完成換彈。
    /// </summary>
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
            /*
             * 無限備彈。
             *
             * 直接補滿彈匣。
             */
            MagazineAmmo =
                MagazineCapacity;
        }
        else
        {
            /*
             * 有限備彈。
             */
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

        if (debugRifle &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[Attack Rifle] 換彈完成。" +
                $"\n彈匣：{MagazineAmmo}/{MagazineCapacity}" +
                $"\n備彈：{(HasInfiniteReserveAmmo ? "無限" : ReserveAmmo.ToString())}",
                this
            );
        }
    }

    /// <summary>
    /// 外部 Gameplay 系統要求中斷目前武器正在執行的動作。
    ///
    /// 這是一個「統一武器中斷接口」。
    ///
    /// 外部系統不應該直接操作：
    ///
    /// ReloadTimer
    /// IsReloading
    /// MagazineAmmo
    /// ReserveAmmo
    ///
    /// 而是統一呼叫：
    ///
    /// InterruptWeaponAction(...)
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前支援：
    ///
    /// Reload
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如玩家：
    ///
    /// Reload 中
    /// ↓
    /// 按下 F Quick Action
    /// ↓
    /// PlayerQuickActionController
    /// ↓
    /// PlayerWeaponController
    /// ↓
    /// AttackRifle.InterruptWeaponAction(
    ///     WeaponInterruptReason.QuickAction
    /// )
    /// ↓
    /// StopReload()
    ///
    /// ------------------------------------------------------------
    ///
    /// 非常重要：
    ///
    /// 這裡只會「取消換彈狀態」。
    ///
    /// 不會呼叫 CompleteReload()。
    ///
    /// 因此：
    ///
    /// 原本彈匣 = 5 / 30
    /// 備彈 = 100
    ///
    /// Reload 到一半按 F
    ///
    /// 結果仍然是：
    ///
    /// 5 / 30
    /// 備彈 = 100
    ///
    /// 不會偷偷完成換彈。
    ///
    /// ------------------------------------------------------------
    ///
    /// 同時這裡也不會清除 FireCooldownTimer。
    ///
    /// 避免玩家利用：
    ///
    /// 射擊
    /// ↓
    /// F
    /// ↓
    /// 重置射速冷卻
    ///
    /// 來繞過武器 Fire Rate。
    /// </summary>
    /// <param name="reason">
    /// 是哪個外部系統要求中斷武器。
    ///
    /// 目前主要是：
    /// QuickAction。
    ///
    /// 未來還能使用：
    /// WeaponSwitch
    /// Death
    /// Stun
    /// SpecialAbility
    /// Forced。
    /// </param>
    /// <returns>
    /// true：
    /// 至少成功中斷了一個武器動作。
    ///
    /// false：
    /// 目前沒有可以被中斷的武器動作。
    /// </returns>
    public bool InterruptWeaponAction(
        WeaponInterruptReason reason
    )
    {
        // =============================================================
        // 武器尚未初始化
        // =============================================================

        /*
        * AttackRifle 尚未完成 Network Spawn / 初始化時，
        * 不允許外部修改武器狀態。
        */
        if (IsInitialized == false)
        {
            return false;
        }

        bool interrupted =
            false;

        // =============================================================
        // Reload
        // =============================================================

        if (IsReloading)
        {
            /*
            * 只取消 Reload。
            *
            * 注意：
            *
            * 不可以呼叫：
            *
            * CompleteReload();
            *
            * 因為 CompleteReload()
            * 會真正把備用彈藥轉移進彈匣。
            *
            * 我們現在的需求是：
            *
            * F Quick Action
            * →
            * 直接取消換彈。
            */
            StopReload();

            interrupted =
                true;

            // ---------------------------------------------------------
            // Debug
            // ---------------------------------------------------------

            if (debugRifle &&
                Object != null &&
                Object.HasInputAuthority)
            {
                Debug.Log(
                    $"[Attack Rifle] 武器動作已被中斷。" +
                    $"\n中斷原因：{reason}" +
                    $"\n中斷動作：Reload" +
                    $"\n目前彈匣：{MagazineAmmo}/{MagazineCapacity}" +
                    $"\n備用彈藥：" +
                    $"{(HasInfiniteReserveAmmo ? "無限" : ReserveAmmo.ToString())}",
                    this
                );
            }
        }

        return interrupted;
    }

    /// <summary>
    /// 停止換彈狀態。
    /// </summary>
    private void StopReload()
    {
        IsReloading =
            false;

        ReloadTimer =
            TickTimer.None;
    }

    #endregion

// =====================================================================
    #region 只幫「本地玩家」畫彈道
    
    /// <summary>
    /// Client 本地用的「純視覺彈道終點」計算。
    ///
    /// 這個 Raycast：
    ///
    /// 不會造成傷害。
    /// 不會判斷 Headshot。
    /// 不會取代 Fusion Lag Compensation。
    ///
    /// 它唯一的用途就是：
    ///
    /// 讓 Client 按下左鍵的當下，
    /// 立即看到 LineRenderer 大約停在哪裡。
    ///
    /// 真正多人遊戲命中結果仍然由
    /// State Authority 的 PerformAuthoritativeHitscan()
    /// 決定。
    /// </summary>
    private Vector3 GetLocalVisualTracerEndPoint(
        Vector3 origin,
        Vector3 direction
    )
    {
        /*
        * 預設沒有命中：
        * 畫到最大射程。
        */
        Vector3 endPoint =
            origin +
            direction *
            maxShotDistance;

        if (Runner == null)
        {
            return endPoint;
        }

        PhysicsScene physicsScene =
            Runner.GetPhysicsScene();

        if (physicsScene.IsValid() == false)
        {
            return endPoint;
        }

        /*
        * 使用和真正 AttackRifle 一樣的 Hit Mask。
        *
        * 這樣視覺與 Gameplay Layer 規則
        * 至少保持一致。
        */
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
            /*
            * 防止本地視覺 Raycast
            * 意外撞到玩家自己的普通 Collider。
            *
            * 正式環境仍然建議：
            *
            * Player Layer
            * 不包含在 AttackRifle Hit Mask。
            */
            NetworkObject hitNetworkObject =
                hit.collider
                    .GetComponentInParent<NetworkObject>();

            /*
            * 只有不是 Owner Player
            * 才允許當作本地視覺彈道終點。
            */
            if (IsOwnerPlayerObject(
                    hitNetworkObject
                ) == false)
            {
                endPoint =
                    hit.point;
            }
        }

        return endPoint;
    }

    /// <summary>
    /// 顯示本地玩家自己的彈道。
    ///
    /// 真正命中判定仍然使用 Gameplay Shot Origin。
    ///
    /// 但是 LineRenderer 的視覺起點會改成
    /// Tracer Muzzle Point。
    /// </summary>
    private void ShowLocalBulletTracer(
        Vector3 shotEndPoint
    )
    {
        if (bulletTracerLineDrawer == null)
            return;

        if (Object == null)
            return;

        /*
        * 只顯示本地玩家自己的第一人稱彈道。
        */
        if (Object.HasInputAuthority == false)
            return;

        Vector3 tracerStartPoint =
            GetTracerShotOrigin();

        bulletTracerLineDrawer.DrawTracer(
            tracerStartPoint,
            shotEndPoint
        );
    }

    #endregion

    // =====================================================================
    #region 升級系統接口

    /// <summary>
    /// 修改執行期間的射速倍率。
    ///
    /// 未來升級系統例如：
    ///
    /// +20% 射速
    /// → SetFireRateMultiplier(1.2f)
    ///
    /// +50% 射速
    /// → SetFireRateMultiplier(1.5f)
    ///
    /// 建議正式升級系統只由 State Authority
    /// 決定最終倍率。
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
    /// 動態指定目前第一人稱武器的槍口。
    ///
    /// 未來 ProfessionViewModelManager
    /// 生成 Attack ViewModel 後，
    /// 可以把該武器上的 MuzzlePoint 傳進來。
    ///
    /// 這只影響視覺 Tracer 起點。
    /// </summary>
    public void SetTracerMuzzlePoint(
        Transform newMuzzlePoint
    )
    {
        tracerMuzzlePoint =
            newMuzzlePoint;
    }

    /// <summary>
    /// 清除目前第一人稱武器提供的槍口。
    ///
    /// 例如：
    /// 武器切換
    /// ViewModel 被銷毀
    /// 玩家換職業
    /// </summary>
    public void ClearTracerMuzzlePoint(
        Transform currentMuzzlePoint
    )
    {
        /*
        * 只允許目前真的正在使用的槍口清除自己，
        * 避免舊 ViewModel 被銷毀時，
        * 把新武器剛設定好的 MuzzlePoint 一起清掉。
        */
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