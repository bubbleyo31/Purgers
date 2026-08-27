using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attack 職業的 F Quick Action：快速近戰。
///
/// ------------------------------------------------------------
///
/// 流程：
///
/// F
/// ↓
/// Startup
/// ↓
/// Active
/// ↓
/// ★ 在進入 Active 的瞬間執行一次近戰傷害
/// ↓
/// Recovery
/// ↓
/// Idle
///
/// ------------------------------------------------------------
///
/// 傷害判定使用 Photon Fusion Lag Compensation。
///
/// 流程：
///
/// 玩家前方範圍搜尋
/// ↓
/// 排除自己
/// ↓
/// 找到 IDamageReceiver
/// ↓
/// 排除死亡目標
/// ↓
/// 同一目標多 Hitbox 去重
/// ↓
/// 前方角度篩選
/// ↓
/// 最大距離篩選
/// ↓
/// 障礙物遮擋檢查
/// ↓
/// 依距離排序
/// ↓
/// 取得最近的 Max Targets
/// ↓
/// DamageRequest
/// ↓
/// IDamageReceiver
///
/// ------------------------------------------------------------
///
/// 非常重要：
///
/// 真正 Damage 只由 State Authority 執行。
///
/// Input Authority 可以預測：
///
/// Startup
/// Active
/// Recovery
///
/// 但不會在 Client Prediction 時自行扣敵人 HP。
/// </summary>
[DisallowMultipleComponent]
public class AttackQuickMelee :
    NetworkBehaviour,
    IPlayerQuickActionAbility,
    ICombatDamageFeedbackSource
{
    // =====================================================================
    #region 內部候選目標資料

    /// <summary>
    /// 經過初步去重後的一個近戰候選目標。
    ///
    /// 一隻敵人可能同時存在：
    ///
    /// Body Hitbox
    /// Head Hitbox
    /// Legs Hitbox
    /// Unity Collider
    ///
    /// 但最後只會形成一個 MeleeCandidate。
    /// </summary>
    private struct MeleeCandidate
    {
        /// <summary>
        /// DamageReceiver 所在的 MonoBehaviour。
        ///
        /// 同時拿來當同一目標的去重 Key。
        /// </summary>
        public MonoBehaviour ReceiverBehaviour;

        /// <summary>
        /// Lag Compensation 實際找到的某個命中 GameObject。
        ///
        /// 最後 DamageReceiverUtility
        /// 會從這個物件向父階層尋找 IDamageReceiver。
        /// </summary>
        public GameObject HitObject;

        /// <summary>
        /// 目前拿來代表這個目標的世界位置。
        ///
        /// 如果同一敵人有多個 Hitbox，
        /// 會保留距離玩家最近的那一個。
        /// </summary>
        public Vector3 TargetPoint;

        /// <summary>
        /// 玩家近戰 Origin 到目標的距離。
        /// </summary>
        public float Distance;
    }

    #endregion

    // =====================================================================
    #region Quick Action 時間

    [Header("快速近戰時間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家按下 F 後，到真正執行近戰傷害判定之前的等待時間。未來有動畫後，這個數值應對應從起手到真正揮擊命中時間點。目前建議先使用 0.08 秒。")]
    private float startupDuration =
        0.08f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("近戰 Active 階段維持時間。注意真正傷害只會在進入 Active 的瞬間執行一次，不會在 Active 的每個 Fusion Tick 重複造成傷害。目前建議先使用 0.02 秒。")]
    private float activeDuration =
        0.02f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("近戰傷害判定完成後的收招時間。這段期間仍禁止 Fire、Aim、Reload，但 Movement、Look、Grapple 保持正常。目前建議先使用 0.20 秒。")]
    private float recoveryDuration =
        0.20f;

    #endregion

    // =====================================================================
    #region 近戰傷害

    [Header("近戰傷害")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Attack 快速近戰對每一個成功命中的目標要求造成的基礎傷害。目前沒有距離衰退，也不會因命中頭部而造成 Headshot。之後 Roguelike 升級系統可以再從這裡建立 Runtime Damage Multiplier。")]
    private float meleeDamage =
        35f;

    [SerializeField]
    [Min(1)]
    [Tooltip("一次快速近戰最多可以傷害多少個不同目標。判定會先把同一敵人的多個 Hitbox 去重，再依距離由近到遠排序，最後取這個數量。例如設為 3，前方即使有 8 隻敵人也只會攻擊最近的 3 隻。")]
    private int maximumTargets =
        3;

    #endregion

    // =====================================================================
    #region 近戰搜尋範圍

    [Header("近戰搜尋範圍")]

    [SerializeField]
    [Tooltip("近戰範圍判定的起始 Transform。可以放在玩家胸口或未來第一人稱近戰武器附近。注意這必須是 Network Player 階層中所有 Peer 都存在的 Transform，不要引用只有本地 CameraRig 才存在的 ViewModel。留空時會使用玩家根物件位置加上 Origin Height。")]
    private Transform meleeOrigin;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Melee Origin 沒有指定時，從玩家根物件向上增加多少高度作為近戰判定起點。通常設在胸口附近，例如 1.0 到 1.4。")]
    private float originHeight =
        1.1f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("近戰真正允許攻擊到的最大距離，單位為 Unity 世界單位。目標即使被 Overlap Sphere 找到，只要距離超過這個值仍然不會受到傷害。建議先從 2.5 測試。")]
    private float meleeRange =
        2.5f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip("Photon Fusion Overlap Sphere 的搜尋半徑。Sphere 的中心會放在玩家前方，用來形成比較寬的近戰掃擊範圍。建議先從 1.3 到 1.6 測試。")]
    private float searchRadius =
        1.4f;

    [SerializeField]
    [Range(1f, 180f)]
    [Tooltip("玩家正前方允許命中的最大夾角，單位為度。這裡表示整個攻擊扇形的完整角度。例如設為 90，代表玩家 Forward 左右各允許約 45 度。數值越大越容易掃到側面的敵人。")]
    private float maximumAttackAngle =
        90f;

    [SerializeField]
    [Tooltip("近戰可以搜尋哪些 Layer。建議包含敵人的 Fusion Hitbox Layer。如果開啟 Include PhysX，普通 Unity Collider 也會按照這個 Layer Mask 進行查詢。")]
    private LayerMask meleeHitMask =
        ~0;

    #endregion

    // =====================================================================
    #region Lag Compensation

    [Header("Photon Fusion Lag Compensation")]

    [SerializeField]
    [Tooltip("開啟後，近戰範圍搜尋會加入 HitOptions.SubtickAccuracy，讓 Server 使用玩家輸入被採樣時更精確的插值位置進行判定。高速勾索中的近戰建議保持開啟。")]
    private bool useSubtickAccuracy =
        true;

    [SerializeField]
    [Tooltip("開啟後，Lag Compensation 查詢除了 Fusion Hitbox 外，也包含普通 Unity PhysX Collider。測試階段可以開啟。如果正式敵人都使用 Fusion Hitbox，之後可以視需求關閉。")]
    private bool includePhysX =
        true;

    #endregion

    // =====================================================================
    #region 障礙物遮擋

    [Header("近戰障礙物遮擋")]

    [SerializeField]
    [Tooltip("開啟後，正式造成傷害前會檢查玩家與敵人之間是否存在牆壁等障礙物，避免 Overlap Sphere 穿過薄牆直接攻擊牆後敵人。建議正式遊戲保持開啟。")]
    private bool requireLineOfSight =
        true;

    [SerializeField]
    [Tooltip("哪些 Layer 會阻擋近戰。這裡建議只包含牆壁、地形、場景大型障礙物等 World Geometry Layer。不要包含 Enemy 或 Player Layer，否則敵人的 Collider 本身可能會被當成遮擋。")]
    private LayerMask obstructionMask =
        0;

    [SerializeField]
    [Min(0f)]
    [Tooltip("遮擋 Raycast 的起點稍微向前偏移多少距離，用來避免射線從玩家自己的碰撞體內部開始。")]
    private float obstructionRayStartOffset =
        0.05f;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，State Authority 會輸出快速近戰 Started、Active、Finished 以及這次找到多少候選目標、最後攻擊多少個目標。")]
    private bool debugQuickMelee =
        true;

    [SerializeField]
    [Tooltip("開啟後會在 Scene View 畫出這次快速近戰的搜尋 Sphere、Forward 與主要判定資訊。僅供開發測試使用。")]
    private bool debugDrawMelee =
        true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Debug Draw 在 Scene View 中保留多久，單位為秒。")]
    private float debugDrawDuration =
        0.5f;

    #endregion

    // =====================================================================
    #region Owner Player Binding

    /// <summary>
    /// 這個 Attack Quick Melee
    /// 真正屬於哪一個 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// AttackQuickMelee 現在會存在：
    ///
    /// AttackProfessionRuntime
    ///
    /// 而不是 Player NetworkObject。
    ///
    /// 因此不能再把：
    ///
    /// Object
    /// transform
    ///
    /// 當作真正玩家。
    ///
    /// ------------------------------------------------------------
    ///
    /// 真正玩家的位置、方向、Input Authority、
    /// NetworkObject 都必須從 Owner Player 取得。
    /// </summary>
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 的共用移動模組。
    ///
    /// 主要用來取得：
    ///
    /// KCC TargetPosition
    /// KCC TransformRotation。
    ///
    /// 真正近戰 Damage 判定必須使用
    /// Player Simulation Position，
    /// 而不是 Profession Runtime 的 Transform。
    /// </summary>
    private PlayerMovement ownerMovement;

    /// <summary>
    /// 真正 Player Core 的 NetworkObject。
    ///
    /// 用於：
    ///
    /// 1. 排除自己的 Hitbox。
    /// 2. DamageRequest SourceNetworkObject。
    /// 3. 取得 InputAuthority。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;

    /// <summary>
    /// 目前綁定的 Player Core。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    /// <summary>
    /// 將 Attack Quick Melee
    /// 綁定到真正的 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個方法會由：
    ///
    /// AttackProfessionRuntimeDriver
    ///
    /// 在 Runtime Spawn 完成後呼叫。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        if (ownerPlayer == null)
        {
            ownerMovement =
                null;

            ownerPlayerNetworkObject =
                null;

            return;
        }

        ownerMovement =
            ownerPlayer.Movement;

        ownerPlayerNetworkObject =
            ownerPlayer.Object;

        if (ownerMovement == null)
        {
            Debug.LogError(
                $"[{nameof(AttackQuickMelee)}] " +
                $"Owner Player 找不到 PlayerMovement。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }

        if (ownerPlayerNetworkObject == null)
        {
            Debug.LogError(
                $"[{nameof(AttackQuickMelee)}] " +
                $"Owner Player 找不到有效 NetworkObject。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }

    // =====================================================================
    #region Unity

    private void Awake()
    {
        /*
        * 遷移期間相容。
        *
        * 如果這支目前仍然直接掛在 Player Root，
        * 可以先自己完成一次 Binding。
        *
        * ------------------------------------------------------------
        *
        * 正式搬進 AttackProfessionRuntime 後：
        *
        * GetComponent<Player>()
        * 會找不到，
        *
        * 這是正常的。
        *
        * AttackProfessionRuntimeDriver
        * 之後會正式呼叫 BindOwnerPlayer()。
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
    }

    #endregion

    /// <summary>
    /// 取得真正 Player Core NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式 Runtime 架構下應該永遠使用：
    ///
    /// ownerPlayerNetworkObject。
    ///
    /// 後面的 fallback
    /// 只是避免遷移期間出現 NullReference。
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

        /*
        * 舊架構相容。
        *
        * 如果 AttackQuickMelee
        * 暫時仍然直接掛在 Player Root，
        * 那 Object 本身就是 Player。
        */
        Player localPlayer =
            GetComponent<Player>();

        if (localPlayer != null)
        {
            return
                localPlayer.Object;
        }

        return null;
    }

    /// <summary>
    /// 取得真正執行這個 Attack Ability 的玩家。
    ///
    /// Lag Compensation 必須使用這個 PlayerRef。
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

        /*
        * 最後安全 fallback。
        *
        * 正式 Runtime Binding 完成後
        * 不應走到這裡。
        */
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
    /// 是否就是這個技能真正的 Owner Player。
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
    #region Runtime Cache

    /// <summary>
    /// Photon Fusion Lag Compensation
    /// 每次 OverlapSphere 的結果。
    ///
    /// 重複使用 List，
    /// 避免每次按 F 都建立新集合。
    /// </summary>
    private readonly List<LagCompensatedHit>
        overlapHits =
            new List<LagCompensatedHit>(32);

    /// <summary>
    /// 去重並通過基本檢查後的候選目標。
    /// </summary>
    private readonly List<MeleeCandidate>
        candidates =
            new List<MeleeCandidate>(16);

    /// <summary>
    /// 已經加入 Candidates 的 Damage Receiver。
    ///
    /// 防止：
    ///
    /// Enemy
    /// ├─ Head Hitbox
    /// ├─ Body Hitbox
    /// └─ Legs Hitbox
    ///
    /// 被同一次 F 算成三個不同敵人。
    /// </summary>
    private readonly HashSet<MonoBehaviour>
        uniqueReceivers =
            new HashSet<MonoBehaviour>();

    #endregion

    // =====================================================================
    #region 傷害事件

    /// <summary>
    /// 每一個真正完成 DamageSystem 處理的近戰目標
    /// 都會觸發一次。
    ///
    /// 目前先保留接口。
    ///
    /// 之後如果要讓 Quick Melee 也使用：
    ///
    /// Hit Marker
    /// Camera Shake
    /// Hit Sound
    ///
    /// 可以從這裡接到統一 Combat Feedback Relay，
    /// 而不需要讓 AttackQuickMelee 直接找 UI。
    ///
    /// 注意：
    /// 目前事件只會在 State Authority
    /// 正式執行近戰 Damage 時觸發。
    /// </summary>
    public event Action<DamageResult>
        DamageResolved;

    /// <summary>
    /// 只有 DamageResult.Accepted == true
    /// 才會觸發。
    ///
    /// 這就是之後近戰 Hit Confirm Feedback
    /// 應該使用的接口。
    /// </summary>
    public event Action<DamageResult>
        DamageConfirmed;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 目前快速近戰傷害。
    /// </summary>
    public float MeleeDamage =>
        meleeDamage;

    /// <summary>
    /// 一次最多命中目標數量。
    /// </summary>
    public int MaximumTargets =>
        Mathf.Max(
            1,
            maximumTargets
        );

    /// <summary>
    /// 最大近戰距離。
    /// </summary>
    public float MeleeRange =>
        meleeRange;

    #endregion

    // =====================================================================
    #region IPlayerQuickActionAbility

    /// <summary>
    /// 這個 Quick Action 屬於 Attack。
    /// </summary>
    public PlayerProfessionType Profession =>
        PlayerProfessionType.Attack;

    /// <summary>
    /// 起手時間。
    /// </summary>
    public float StartupDuration =>
        Mathf.Max(
            0f,
            startupDuration
        );

    /// <summary>
    /// Active 時間。
    /// </summary>
    public float ActiveDuration =>
        Mathf.Max(
            0f,
            activeDuration
        );

    /// <summary>
    /// Recovery 時間。
    /// </summary>
    public float RecoveryDuration =>
        Mathf.Max(
            0f,
            recoveryDuration
        );

    /// <summary>
    /// 目前是否允許啟動快速近戰。
    ///
    /// 現在尚未增加：
    ///
    /// Cooldown
    /// Resource
    /// Stamina。
    ///
    /// PlayerQuickActionController
    /// 本身會另外檢查 ActionGate。
    /// </summary>
    public bool CanStartQuickAction()
    {
        /*
        * AttackQuickMelee 已經存在 Profession Runtime。
        *
        * 如果 Owner Binding 尚未完成，
        * 寧可讓這一 Tick 的 F 無法啟動，
        * 也不能使用 Profession Runtime 自己的位置
        * 執行錯誤近戰判定。
        */
        if (ownerPlayer == null ||
            ownerMovement == null ||
            GetOwnerPlayerNetworkObject() == null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Quick Action 正式開始。
    ///
    /// 現在只做流程通知，
    /// 不在這裡造成傷害。
    /// </summary>
    public void OnQuickActionStarted(
        int activationSequence
    )
    {
        if (debugQuickMelee &&
            IsStateAuthority())
        {
            Debug.Log(
                $"[Attack Quick Melee] Started" +
                $"\nSequence：{activationSequence}" +
                $"\nStartup：{StartupDuration:F3}",
                this
            );
        }
    }

    /// <summary>
    /// Quick Action 進入真正 Active 的瞬間。
    ///
    /// ★ 真正近戰傷害只在這裡執行一次。
    ///
    /// 不會在整個 ActiveDuration
    /// 每 Tick 重複造成傷害。
    /// </summary>
    public void OnQuickActionActive(
        int activationSequence
    )
    {
        // =============================================================
        // Presentation / Prediction 可以進 Active
        // 但真正 Damage 只能由 State Authority 執行
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        PerformAuthoritativeMelee(
            activationSequence
        );
    }

    /// <summary>
    /// 整個 Quick Action 正式完成。
    /// </summary>
    public void OnQuickActionFinished(
        int activationSequence
    )
    {
        if (debugQuickMelee &&
            IsStateAuthority())
        {
            Debug.Log(
                $"[Attack Quick Melee] Finished" +
                $"\nSequence：{activationSequence}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 正式近戰入口

    /// <summary>
    /// State Authority 執行一次快速近戰。
    ///
    /// 整個一次性傷害流程集中在這裡。
    /// </summary>
    private void PerformAuthoritativeMelee(
        int activationSequence
    )
    {
        if (Runner == null ||
            Runner.LagCompensation == null)
        {
            Debug.LogError(
                $"[{nameof(AttackQuickMelee)}] " +
                $"Runner.LagCompensation 不存在。" +
                $"\n請確認 Fusion Lag Compensation 已啟用，" +
                $"並且敵人正式 Hitbox 已註冊到 HitboxRoot。",
                this
            );

            return;
        }

        // -------------------------------------------------------------
        // 清除上一次結果
        // -------------------------------------------------------------

        overlapHits.Clear();
        candidates.Clear();
        uniqueReceivers.Clear();

        // -------------------------------------------------------------
        // 取得攻擊空間
        // -------------------------------------------------------------

        Vector3 origin =
            GetMeleeOrigin();

        Vector3 forward =
            GetMeleeForward();

        /*
         * Overlap Sphere 不放在玩家正中心。
         *
         * 我們把它往前移動，
         * 讓搜尋區域更集中於玩家前方。
         *
         * 真正最大距離與角度
         * 後面仍會再次精確篩選。
         */
        Vector3 searchCenter =
            origin +
            forward *
            (meleeRange * 0.5f);

        // -------------------------------------------------------------
        // Debug
        // -------------------------------------------------------------

        if (debugDrawMelee)
        {
            Debug.DrawRay(
                origin,
                forward *
                meleeRange,
                Color.red,
                debugDrawDuration
            );
        }

        // -------------------------------------------------------------
        // Hit Options
        // -------------------------------------------------------------

        HitOptions hitOptions =
            HitOptions.IgnoreInputAuthority;

        if (useSubtickAccuracy)
        {
            hitOptions |=
                HitOptions.SubtickAccuracy;
        }

        if (includePhysX)
        {
            hitOptions |=
                HitOptions.IncludePhysX;
        }

        // =============================================================
        // Fusion Lag Compensation Overlap
        // =============================================================

        // -------------------------------------------------------------
        // 取得真正 Owner Player
        // -------------------------------------------------------------

        PlayerRef ownerInputAuthority =
            GetOwnerInputAuthority();

        if (ownerInputAuthority.IsNone)
        {
            Debug.LogError(
                $"[{nameof(AttackQuickMelee)}] " +
                $"找不到有效的 Owner Input Authority，" +
                $"無法執行 Lag Compensation。",
                this
            );

            return;
        }

        // =============================================================
        // Fusion Lag Compensation Overlap
        // =============================================================

        int hitCount =
            Runner.LagCompensation.OverlapSphere(
                searchCenter,
                searchRadius,
                ownerInputAuthority,
                overlapHits,
                meleeHitMask,
                hitOptions,
                true,
                QueryTriggerInteraction.Ignore
            );

        if (hitCount <= 0)
        {
            if (debugQuickMelee)
            {
                Debug.Log(
                    $"[Attack Quick Melee] 沒有找到任何候選碰撞。" +
                    $"\nSequence：{activationSequence}" +
                    $"\nOrigin：{origin}" +
                    $"\nSearch Center：{searchCenter}" +
                    $"\nRange：{meleeRange:F2}" +
                    $"\nRadius：{searchRadius:F2}",
                    this
                );
            }

            return;
        }

        // =============================================================
        // 建立唯一候選目標
        // =============================================================

        for (int i = 0;
             i < overlapHits.Count;
             i++)
        {
            LagCompensatedHit hit =
                overlapHits[i];

            GameObject hitObject =
                hit.GameObject;

            if (hitObject == null)
                continue;

            // ---------------------------------------------------------
            // 防止攻擊自己
            // ---------------------------------------------------------

            NetworkObject hitNetworkObject =
                hitObject
                    .GetComponentInParent<NetworkObject>();

            /*
            * AttackQuickMelee 現在位於
            * AttackProfessionRuntime。
            *
            * 所以不能再：
            *
            * hitNetworkObject == Object
            *
            * 判斷自己。
            *
            * 必須與真正的 Player Core
            * NetworkObject 比較。
            */
            if (IsOwnerPlayerObject(
                    hitNetworkObject
                ))
            {
                continue;
            }

            // ---------------------------------------------------------
            // 尋找 IDamageReceiver
            // ---------------------------------------------------------

            if (TryFindDamageReceiver(
                    hitObject,
                    out MonoBehaviour receiverBehaviour
                ) == false)
            {
                /*
                 * 例如：
                 * 牆壁
                 * 地板
                 * 普通場景 Collider
                 *
                 * 可以出現在 Overlap 裡，
                 * 但不是傷害目標。
                 */
                continue;
            }

            // ---------------------------------------------------------
            // 死亡目標
            // ---------------------------------------------------------

            if (TryGetLifeState(
                    receiverBehaviour,
                    out ICombatLifeState lifeState
                ))
            {
                if (lifeState.IsAlive ==
                    false)
                {
                    continue;
                }
            }

            // ---------------------------------------------------------
            // 取得候選點
            // ---------------------------------------------------------

            /*
             * Fusion Hitbox overlap 可以找到真正的
             * Lag Compensated Hitbox。
             *
             * 但 IncludePhysX 的 Overlap
             * 不保證擁有完整 Point / Normal 資料，
             * 所以這裡使用實際命中 GameObject Transform
             * 作為共通候選位置。
             */
            Vector3 targetPoint =
                hitObject.transform.position;

            Vector3 toTarget =
                targetPoint -
                origin;

            float distance =
                toTarget.magnitude;

            // ---------------------------------------------------------
            // 最大距離
            // ---------------------------------------------------------

            if (distance >
                meleeRange)
            {
                continue;
            }

            if (distance <= 0.0001f)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 正前方角度
            // ---------------------------------------------------------

            Vector3 directionToTarget =
                toTarget /
                distance;

            float angle =
                Vector3.Angle(
                    forward,
                    directionToTarget
                );

            /*
             * Maximum Attack Angle
             * 表示完整攻擊扇形。
             *
             * 例如：
             *
             * 90°
             *
             * 實際允許：
             *
             * 左 45°
             * 右 45°。
             */
            float allowedHalfAngle =
                maximumAttackAngle *
                0.5f;

            if (angle >
                allowedHalfAngle)
            {
                continue;
            }

            // ---------------------------------------------------------
            // 同一 DamageReceiver 去重
            // ---------------------------------------------------------

            if (uniqueReceivers.Contains(
                    receiverBehaviour
                ))
            {
                /*
                 * 同一敵人的另一個 Hitbox。
                 *
                 * 如果現在這個 Hitbox
                 * 比原本記錄的位置更靠近玩家，
                 * 更新候選位置。
                 */
                UpdateCandidateIfCloser(
                    receiverBehaviour,
                    hitObject,
                    targetPoint,
                    distance
                );

                continue;
            }

            uniqueReceivers.Add(
                receiverBehaviour
            );

            candidates.Add(
                new MeleeCandidate
                {
                    ReceiverBehaviour =
                        receiverBehaviour,

                    HitObject =
                        hitObject,

                    TargetPoint =
                        targetPoint,

                    Distance =
                        distance
                }
            );
        }

        // =============================================================
        // 沒有合法敵人
        // =============================================================

        if (candidates.Count <= 0)
        {
            if (debugQuickMelee)
            {
                Debug.Log(
                    $"[Attack Quick Melee] Overlap 有命中，" +
                    $"但沒有合法傷害目標。" +
                    $"\nSequence：{activationSequence}" +
                    $"\nRaw Hits：{overlapHits.Count}",
                    this
                );
            }

            return;
        }

        // =============================================================
        // 依距離排序
        // =============================================================

        candidates.Sort(
            CompareCandidateDistance
        );

        // =============================================================
        // 最多攻擊 Maximum Targets
        // =============================================================

        int targetLimit =
            Mathf.Min(
                MaximumTargets,
                candidates.Count
            );

        int successfulDamageCount =
            0;

        int attemptedTargetCount =
            0;

        // =============================================================
        // 正式傷害
        // =============================================================

        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            /*
             * MaximumTargets 是「最多真正嘗試傷害的目標數」。
             *
             * 被牆擋住的候選人不佔名額，
             * 系統會繼續找下一個較遠的合法目標。
             */
            if (attemptedTargetCount >=
                targetLimit)
            {
                break;
            }

            MeleeCandidate candidate =
                candidates[i];

            if (candidate.ReceiverBehaviour ==
                    null ||
                candidate.HitObject ==
                    null)
            {
                continue;
            }

            // ---------------------------------------------------------
            // LOS
            // ---------------------------------------------------------

            if (IsTargetObstructed(
                    origin,
                    candidate.TargetPoint
                ))
            {
                if (debugQuickMelee)
                {
                    Debug.Log(
                        $"[Attack Quick Melee] 目標被障礙物阻擋。" +
                        $"\n目標：{candidate.ReceiverBehaviour.name}" +
                        $"\n距離：{candidate.Distance:F2}",
                        candidate.ReceiverBehaviour
                    );
                }

                continue;
            }

            attemptedTargetCount++;

            // ---------------------------------------------------------
            // Damage Request
            // ---------------------------------------------------------

            Vector3 hitDirection =
                candidate.TargetPoint -
                origin;

            if (hitDirection.sqrMagnitude >
                0.0001f)
            {
                hitDirection.Normalize();
            }
            else
            {
                hitDirection =
                    forward;
            }

            DamageRequest damageRequest =
                new DamageRequest
                {
                    RequestedDamage =
                        meleeDamage,

                    BaseDamage =
                        meleeDamage,

                    DamageType =
                        DamageType.Melee,

                    FeedbackId =
                        CombatFeedbackId.AttackQuickMelee,

                    /*
                     * Quick Melee 是範圍近戰。
                     *
                     * 不因 Overlap 剛好掃到 Head Hitbox
                     * 就當作 Headshot。
                     *
                     * 所以統一視為 Body。
                     */
                    HitZone =
                        DamageHitZoneType.Body,

                    /*
                    * 真正造成攻擊的是 Owner Player。
                    *
                    * AttackQuickMelee 即使存在
                    * AttackProfessionRuntime，
                    * DamageSystem 的 Gameplay Owner
                    * 仍然保持是玩家本人。
                    */
                    Attacker =
                        GetOwnerInputAuthority(),

                    SourceNetworkObject =
                        GetOwnerPlayerNetworkObject(),

                    SourceObject =
                        ownerPlayer != null
                            ? ownerPlayer.gameObject
                            : gameObject,

                    HitObject =
                        candidate.HitObject,

                    HitPoint =
                        candidate.TargetPoint,

                    /*
                    * Quick Melee 本身目前沒有暴頭傷害倍率設計。
                    *
                    * 所以即使被 Attack Grapple Mark
                    * 視為 Headshot，
                    * 傷害倍率目前仍然是 1。
                    *
                    * 但是：
                    *
                    * DamageResult.IsHeadshot
                    * 仍然會成立。
                    */
                    HeadshotDamageMultiplier =
                        1f,

                    ForcedHeadshotSource =
                        DamageForcedHeadshotSource.None,

                    /*
                     * 目前 Area Melee 沒有真正表面 Normal。
                     *
                     * 使用攻擊方向反向
                     * 作為合理的近似值。
                     */
                    HitNormal =
                        -hitDirection,

                    HitDirection =
                        hitDirection,

                    Distance =
                        candidate.Distance,

                    /*
                     * 直接沿用 Quick Action
                     * Activation Sequence。
                     *
                     * 同一次 F 的所有目標
                     * 都會擁有相同 Sequence。
                     */
                    Sequence =
                        activationSequence
                };

            // ---------------------------------------------------------
            // Damage System
            // ---------------------------------------------------------

            bool receiverFound =
                DamageReceiverUtility.TryApplyDamage(
                    candidate.HitObject,
                    damageRequest,
                    out DamageResult result
                );

            // ---------------------------------------------------------
            // Damage Resolved
            // ---------------------------------------------------------

            DamageResolved?.Invoke(
                result
            );

            // ---------------------------------------------------------
            // Damage Confirmed
            // ---------------------------------------------------------

            if (receiverFound &&
                result.Accepted)
            {
                successfulDamageCount++;

                DamageConfirmed?.Invoke(
                    result
                );
            }

            // ---------------------------------------------------------
            // Debug
            // ---------------------------------------------------------

            if (debugQuickMelee)
            {
                Debug.Log(
                    $"[Attack Quick Melee] Damage Result" +
                    $"\nSequence：{activationSequence}" +
                    $"\n目標：{candidate.ReceiverBehaviour.name}" +
                    $"\n距離：{candidate.Distance:F2}" +
                    $"\nRequested Damage：{meleeDamage:F2}" +
                    $"\nReceiver Found：{receiverFound}" +
                    $"\nAccepted：{result.Accepted}" +
                    $"\nApplied Damage：{result.AppliedDamage:F2}" +
                    $"\nKilled：{result.KilledTarget}" +
                    $"\nReject Reason：{result.RejectReason}",
                    candidate.ReceiverBehaviour
                );
            }
        }

        // =============================================================
        // Summary
        // =============================================================

        if (debugQuickMelee)
        {
            Debug.Log(
                $"[Attack Quick Melee] 本次近戰完成。" +
                $"\nSequence：{activationSequence}" +
                $"\nOverlap Hits：{overlapHits.Count}" +
                $"\n唯一合法候選：{candidates.Count}" +
                $"\n最多目標：{MaximumTargets}" +
                $"\n實際嘗試目標：{attemptedTargetCount}" +
                $"\n成功接受傷害：{successfulDamageCount}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 攻擊空間

    /// <summary>
    /// 取得真正的近戰判定起點。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式優先使用：
    ///
    /// Player KCC TargetPosition。
    ///
    /// 原因：
    ///
    /// 真正 Damage 是 State Authority
    /// 在 Fusion Simulation 中判定，
    /// 所以應以 KCC Simulation Position
    /// 作為攻擊起點。
    ///
    /// ------------------------------------------------------------
    ///
    /// meleeOrigin 現在只允許引用
    /// Owner Player 階層中的 Transform。
    ///
    /// 不能引用：
    ///
    /// CameraRig
    /// ViewModel
    /// AttackProfessionRuntime 子物件。
    /// </summary>
    private Vector3 GetMeleeOrigin()
    {
        // =============================================================
        // 1. Owner Player 階層中的 Override
        // =============================================================

        if (meleeOrigin != null &&
            ownerPlayer != null)
        {
            bool belongsToOwnerPlayer =
                meleeOrigin ==
                    ownerPlayer.transform ||
                meleeOrigin.IsChildOf(
                    ownerPlayer.transform
                );

            if (belongsToOwnerPlayer)
            {
                return
                    meleeOrigin.position;
            }
        }

        // =============================================================
        // 2. KCC Simulation Position
        // =============================================================

        if (ownerMovement != null &&
            ownerMovement.KCC != null)
        {
            return
                ownerMovement.KCC.Data.TargetPosition +
                Vector3.up *
                originHeight;
        }

        // =============================================================
        // 3. Player Transform Fallback
        // =============================================================

        if (ownerPlayer != null)
        {
            return
                ownerPlayer.transform.position +
                Vector3.up *
                originHeight;
        }

        // =============================================================
        // 4. 最後安全 Fallback
        // =============================================================

        return
            transform.position +
            Vector3.up *
            originHeight;
    }

    /// <summary>
    /// 取得 Attack Quick Melee
    /// 真正的水平攻擊方向。
    ///
    /// ------------------------------------------------------------
    ///
    /// 優先使用 Player KCC TransformRotation。
    ///
    /// 所以：
    ///
    /// Player Yaw
    /// → 會影響近戰方向。
    ///
    /// Player Pitch
    /// → 不影響。
    ///
    /// ------------------------------------------------------------
    ///
    /// 與目前既有設計保持一致：
    ///
    /// 看天空按 F
    /// 不會往天空揮。
    /// </summary>
    private Vector3 GetMeleeForward()
    {
        Vector3 forward;

        // =============================================================
        // 1. KCC Simulation Rotation
        // =============================================================

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

        // =============================================================
        // 2. Player Transform
        // =============================================================

        else if (ownerPlayer != null)
        {
            forward =
                ownerPlayer.transform.forward;
        }

        // =============================================================
        // 3. 安全 Fallback
        // =============================================================

        else
        {
            forward =
                transform.forward;
        }

        // =============================================================
        // 只保留水平
        // =============================================================

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
    #region Damage Receiver 搜尋

    /// <summary>
    /// 從實際 Hit GameObject
    /// 往父物件搜尋第一個 IDamageReceiver。
    ///
    /// 同時回傳真正承載接口的 MonoBehaviour，
    /// 讓它可以作為同一敵人的去重 Key。
    /// </summary>
    private bool TryFindDamageReceiver(
        GameObject hitObject,
        out MonoBehaviour receiverBehaviour
    )
    {
        receiverBehaviour =
            null;

        if (hitObject == null)
            return false;

        MonoBehaviour[] behaviours =
            hitObject
                .GetComponentsInParent<MonoBehaviour>(
                    true
                );

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
                continue;

            if (behaviour is
                IDamageReceiver)
            {
                receiverBehaviour =
                    behaviour;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 嘗試取得目標生命狀態。
    ///
    /// 如果目標沒有 ICombatLifeState，
    /// 不會直接判它無效。
    ///
    /// 這樣未來可破壞物件即使沒有
    /// Alive / Dead 概念，
    /// 仍然可以實作 IDamageReceiver
    /// 並受到近戰傷害。
    /// </summary>
    private bool TryGetLifeState(
        MonoBehaviour receiverBehaviour,
        out ICombatLifeState lifeState
    )
    {
        lifeState =
            null;

        if (receiverBehaviour == null)
            return false;

        /*
         * 最常見情況：
         *
         * TestDamageReceiver
         * 同時實作：
         *
         * IDamageReceiver
         * ICombatLifeState
         */
        if (receiverBehaviour is
            ICombatLifeState directLifeState)
        {
            lifeState =
                directLifeState;

            return true;
        }

        MonoBehaviour[] behaviours =
            receiverBehaviour
                .GetComponentsInParent<MonoBehaviour>(
                    true
                );

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
                continue;

            if (behaviour is
                ICombatLifeState foundLifeState)
            {
                lifeState =
                    foundLifeState;

                return true;
            }
        }

        return false;
    }

    #endregion

    // =====================================================================
    #region 去重

    /// <summary>
    /// 同一 DamageReceiver 的第二、第三個 Hitbox
    /// 被搜尋到時，
    /// 如果這個位置比目前 Candidate 更靠近玩家，
    /// 就更新 Candidate。
    /// </summary>
    private void UpdateCandidateIfCloser(
        MonoBehaviour receiverBehaviour,
        GameObject hitObject,
        Vector3 targetPoint,
        float distance
    )
    {
        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            MeleeCandidate candidate =
                candidates[i];

            if (candidate.ReceiverBehaviour !=
                receiverBehaviour)
            {
                continue;
            }

            if (distance >=
                candidate.Distance)
            {
                return;
            }

            candidate.HitObject =
                hitObject;

            candidate.TargetPoint =
                targetPoint;

            candidate.Distance =
                distance;

            candidates[i] =
                candidate;

            return;
        }
    }

    #endregion

    // =====================================================================
    #region 排序

    /// <summary>
    /// 依照距離由近到遠排序。
    ///
    /// MaximumTargets 永遠優先攻擊最近敵人。
    /// </summary>
    private static int CompareCandidateDistance(
        MeleeCandidate a,
        MeleeCandidate b
    )
    {
        return
            a.Distance.CompareTo(
                b.Distance
            );
    }

    #endregion

    // =====================================================================
    #region 遮擋

    /// <summary>
    /// 判斷玩家到候選目標之間
    /// 是否被牆壁或場景幾何擋住。
    ///
    /// Obstruction Mask 應只放：
///
/// Wall
/// Ground
/// WorldGeometry。
///
/// 不要放 Enemy / Player。
    /// </summary>
    private bool IsTargetObstructed(
        Vector3 origin,
        Vector3 targetPoint
    )
    {
        if (requireLineOfSight == false)
        {
            return false;
        }

        /*
         * 沒有設定任何 Obstruction Layer。
         *
         * 視為沒有啟用實際遮擋 Layer。
         */
        if (obstructionMask.value == 0)
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

        Vector3 toTarget =
            targetPoint -
            origin;

        float distance =
            toTarget.magnitude;

        if (distance <=
            0.0001f)
        {
            return false;
        }

        Vector3 direction =
            toTarget /
            distance;

        Vector3 rayOrigin =
            origin +
            direction *
            obstructionRayStartOffset;

        float rayDistance =
            Mathf.Max(
                0f,
                distance -
                obstructionRayStartOffset
            );

        if (rayDistance <= 0f)
            return false;

        bool hasObstruction =
            physicsScene.Raycast(
                rayOrigin,
                direction,
                out _,
                rayDistance,
                obstructionMask,
                QueryTriggerInteraction.Ignore
            );

        return
            hasObstruction;
    }

    #endregion

    // =====================================================================
    #region Helper

    /// <summary>
    /// 是否為真正可以修改 Gameplay State 的 State Authority。
    /// </summary>
    private bool IsStateAuthority()
    {
        return
            Object != null &&
            Object.HasStateAuthority;
    }

    #endregion

    // =====================================================================
    #region Gizmos

#if UNITY_EDITOR

    /// <summary>
    /// 在 Editor Scene View
    /// 預覽近戰搜尋範圍。
    ///
    /// 注意：
    /// Gizmos 只是近似範圍。
    ///
    /// 真正傷害還會經過：
    ///
    /// Max Range
    /// Angle
    /// LOS
    /// Target Limit。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 origin;
        Vector3 forward;

        if (Application.isPlaying)
        {
            /*
            * Play Mode：
            * 使用真正 Owner Player 的攻擊空間。
            */
            origin =
                GetMeleeOrigin();

            forward =
                GetMeleeForward();
        }
        else
        {
            /*
            * Prefab / Edit Mode：
            * Runtime 尚未綁定 Owner，
            * 所以只提供近似預覽。
            */
            origin =
                transform.position +
                Vector3.up *
                originHeight;

            forward =
                transform.forward;

            forward.y =
                0f;

            if (forward.sqrMagnitude <=
                0.0001f)
            {
                forward =
                    Vector3.forward;
            }

            forward.Normalize();
        }

        forward.y =
            0f;

        if (forward.sqrMagnitude <=
            0.0001f)
        {
            forward =
                Vector3.forward;
        }

        forward.Normalize();

        Vector3 center =
            origin +
            forward *
            (meleeRange * 0.5f);

        Gizmos.DrawWireSphere(
            center,
            searchRadius
        );

        Gizmos.DrawLine(
            origin,
            origin +
            forward *
            meleeRange
        );

        // -------------------------------------------------------------
        // 左右攻擊角
        // -------------------------------------------------------------

        float halfAngle =
            maximumAttackAngle *
            0.5f;

        Vector3 leftDirection =
            Quaternion.AngleAxis(
                -halfAngle,
                Vector3.up
            ) *
            forward;

        Vector3 rightDirection =
            Quaternion.AngleAxis(
                halfAngle,
                Vector3.up
            ) *
            forward;

        Gizmos.DrawLine(
            origin,
            origin +
            leftDirection *
            meleeRange
        );

        Gizmos.DrawLine(
            origin,
            origin +
            rightDirection *
            meleeRange
        );
    }

#endif

    #endregion
}