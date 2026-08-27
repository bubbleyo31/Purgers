using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次已經被 State Authority 正式確認的
/// 「本地攻擊命中回饋資料」。
///
/// ------------------------------------------------------------
///
/// 這不是 DamageResult 本體。
///
/// DamageResult
/// → Gameplay / Damage Layer。
///
/// CombatHitFeedbackData
/// → Presentation Layer 真正需要的簡化資料。
///
/// ------------------------------------------------------------
///
/// 可以提供給：
///
/// Hit Marker
/// Camera Shake
/// Hit Sound
/// Damage Number
/// Kill Marker
/// Headshot Feedback。
///
/// ------------------------------------------------------------
///
/// 這裡絕對不能再次修改：
///
/// HP
/// Damage
/// Enemy State
/// Gameplay Rule。
/// </summary>
public struct CombatHitFeedbackData
{
    // =====================================================================
    #region 傷害資訊

    /// <summary>
    /// 攻擊端最後要求造成的傷害。
    /// </summary>
    public float RequestedDamage;

    /// <summary>
    /// 目標最後真正受到的傷害。
    ///
    /// 未來 Damage Number
    /// 建議使用這個值。
    /// </summary>
    public float AppliedDamage;

    /// <summary>
    /// 傷害類型。
    ///
    /// 例如：
    ///
    /// Bullet
    /// Melee
    /// Explosion
    /// Ability。
    /// </summary>
    public DamageType DamageType;

    /// <summary>
    /// 這次命中真正來自哪一個攻擊。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// AttackRifle
    /// AttackQuickMelee
    /// TankLightMelee
    /// TankHeavyMelee。
    ///
    /// ------------------------------------------------------------
    ///
    /// Camera Shake、Hit Marker、Hit Sound
    /// 之後應優先根據這個值
    /// 選擇攻擊專屬回饋。
    ///
    /// Kill / Headshot
    /// 則仍然可以擁有更高優先級。
    /// </summary>
    public CombatFeedbackId FeedbackId;

    /// <summary>
    /// 實際 Hit Zone。
    /// </summary>
    public DamageHitZoneType HitZone;

    #endregion

    // =====================================================================
    #region 命中結果

    /// <summary>
    /// Gameplay 最終是否視為暴頭。
    /// </summary>
    public bool IsHeadshot;

    /// <summary>
    /// 這次攻擊是否殺死目標。
    /// </summary>
    public bool KilledTarget;

    /// <summary>
    /// 傷害是否曾被防禦系統阻擋。
    ///
    /// 未來 Tank Guard、
    /// Shield、Armor 可以使用。
    /// </summary>
    public bool WasBlocked;

    /// <summary>
    /// 是否真的造成大於零的有效傷害。
    /// </summary>
    public bool HasEffectiveDamage;

    #endregion

    // =====================================================================
    #region 命中空間資訊

    /// <summary>
    /// 真正命中的世界位置。
    ///
    /// 未來可以用於：
    ///
    /// Damage Number
    /// Hit Direction
    /// 世界命中特效。
    /// </summary>
    public Vector3 HitPoint;

    #endregion

    // =====================================================================
    #region 攻擊識別

    /// <summary>
    /// DamageRequest.Sequence。
    ///
    /// 可以拿來辨識：
    ///
    /// 同一次射擊
    /// 同一次近戰
    /// 同一次技能。
    /// </summary>
    public int Sequence;

    #endregion
}

/// <summary>
/// 玩家戰鬥命中回饋網路中繼器。
///
/// ------------------------------------------------------------
///
/// 目的：
///
/// 真正 DamageResult
/// 存在於 State Authority。
///
/// 但是：
///
/// Hit Marker
/// Camera Shake
/// Hit Sound
///
/// 必須只在「造成這次攻擊的玩家本人」畫面播放。
///
/// ------------------------------------------------------------
///
/// 新架構：
///
/// Current Profession Runtime
/// ↓
/// 找所有 ICombatDamageFeedbackSource
/// ↓
/// DamageConfirmed
/// ↓
/// State Authority
/// ↓
/// PlayerCombatFeedbackRelay
/// ↓
/// RPC
/// ↓
/// Player Input Authority
/// ↓
/// LocalHitConfirmed。
///
/// ------------------------------------------------------------
///
/// PlayerCombatFeedbackRelay 不再知道：
///
/// AttackRifle
/// AttackQuickMelee
/// TankLightAttack
/// TankHeavyAttack
/// TankQuickDash
/// TankAirStrike。
///
/// ------------------------------------------------------------
///
/// 它只認：
///
/// ICombatDamageFeedbackSource。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
[RequireComponent(typeof(PlayerProfessionRuntimeManager))]
public class PlayerCombatFeedbackRelay :
    NetworkBehaviour
{
    // =====================================================================
    #region Player Core 引用

    [Header("Player Core 引用")]

    [SerializeField]
    [Tooltip("玩家目前正式職業。主要用於遷移階段判斷目前是否應該搜尋 Player Root 上尚未搬走的 Attack 傷害來源。若留空會自動取得。")]
    private PlayerProfession profession;

    [SerializeField]
    [Tooltip("玩家目前職業 Runtime 管理器。Relay 會監看 Current Runtime，並在 Runtime 改變時自動解除舊傷害來源、重新訂閱新職業的所有 ICombatDamageFeedbackSource。若留空會自動取得。")]
    private PlayerProfessionRuntimeManager
        professionRuntimeManager;

    #endregion

    // =====================================================================
    #region 遷移階段設定

    [Header("Attack Runtime 遷移階段")]

    [SerializeField]
    [Tooltip("開啟後，如果目前玩家是 Attack，除了搜尋 Attack Profession Runtime，也會暫時搜尋 Player Root 上的 ICombatDamageFeedbackSource。這是因為 AttackRifle 與 AttackQuickMelee 目前尚未真正搬進 Runtime。等搬家完成後會關閉並刪除這個相容功能。")]
    private bool allowLegacyAttackPlayerRootSources =
        true;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，當本地玩家收到 State Authority 回傳的正式命中資訊時，會顯示傷害、暴頭、擊殺與 Sequence。")]
    private bool debugFeedback =
        true;

    [SerializeField]
    [Tooltip("開啟後，每次 Profession Runtime 改變並重新綁定 Damage Feedback Source 時，會顯示找到多少個來源以及來源類型。這個重構階段建議保持開啟。")]
    private bool debugSourceBinding =
        true;

    [SerializeField]
    [Tooltip("開啟後，如果目前仍然使用 Player Root 上尚未搬走的 Attack 傷害來源，會顯示提示。等 Attack 完整搬進 Runtime 後，正常情況不應再看到。")]
    private bool debugLegacySources =
        true;

    #endregion

    // =====================================================================
    #region Runtime Source Binding

    /// <summary>
    /// 目前已經正式訂閱的所有傷害來源 Behaviour。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這裡保存 MonoBehaviour，
    /// 而不是只保存 Interface，
    ///
    /// 是因為 Unity Object 的 Destroy / Null
    /// 判斷使用 MonoBehaviour 比較安全。
    /// </summary>
    private readonly List<MonoBehaviour>
        subscribedSourceBehaviours =
            new List<MonoBehaviour>(8);

    /// <summary>
    /// 本次搜尋過程用來去重。
    ///
    /// 防止同一個 Source
    /// 因 Runtime / Legacy 搜尋重複加入。
    /// </summary>
    private readonly HashSet<MonoBehaviour>
        uniqueSourceBehaviours =
            new HashSet<MonoBehaviour>();

    /// <summary>
    /// 上一次完成 Damage Source Binding 時
    /// 看到的 Profession Runtime。
    ///
    /// ------------------------------------------------------------
    ///
    /// F1 / F2 / F3：
    ///
    /// Runtime Object 改變
    /// ↓
    /// Relay 自動重新 Binding。
    /// </summary>
    private NetworkObject observedRuntimeObject;

    /// <summary>
    /// 上一次完成 Binding 時的正式職業。
    ///
    /// 這可以處理：
    ///
    /// Runtime Object 尚未改變
    /// 但 Profession 已經先同步改變
    ///
    /// 的短暫狀況。
    /// </summary>
    private PlayerProfessionType
        observedProfession =
            PlayerProfessionType.None;

    /// <summary>
    /// 是否至少完成過一次 Damage Source Binding。
    ///
    /// 因為初始狀態：
    ///
    /// observedRuntimeObject = null
    /// CurrentRuntimeObject = null
    ///
    /// 如果只比較兩者，
    /// 系統會誤以為不需要第一次 Binding。
    /// </summary>
    private bool sourceBindingInitialized;

    #endregion

    // =====================================================================
    #region 本地事件

    /// <summary>
    /// 只有這台裝置真正控制的玩家
    /// 才會收到這個事件。
    ///
    /// ------------------------------------------------------------
    ///
    /// Presentation 可以訂閱：
    ///
    /// PlayerHitFeedbackController
    /// Hit Marker
    /// Camera Shake
    /// Hit Sound。
    ///
    /// ------------------------------------------------------------
    ///
    /// 禁止從這裡再次修改 Gameplay Damage。
    /// </summary>
    public event Action<CombatHitFeedbackData>
        LocalHitConfirmed;

    /// <summary>
    /// 本地玩家真正執行了一次需要 Presentation Feedback 的攻擊。
    ///
    /// ------------------------------------------------------------
    ///
    /// 與 LocalHitConfirmed 完全不同。
    ///
    /// LocalAttackPerformed：
    ///
    /// 攻擊本身發生了。
    ///
    /// 不管：
    ///
    /// 有沒有命中
    /// 有沒有敵人
    /// 有沒有造成 Damage。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前主要用途：
    ///
    /// Tank Light Swing
    /// Tank Heavy Swing。
    ///
    /// ------------------------------------------------------------
    ///
    /// Hit Marker 不應該訂閱這個事件。
    ///
    /// Camera Shake 可以訂閱。
    /// </summary>
    public event System.Action<CombatFeedbackId>
        LocalAttackPerformed;


    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (professionRuntimeManager == null)
        {
            professionRuntimeManager =
                GetComponent<
                    PlayerProfessionRuntimeManager
                >();
        }
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        /*
         * DamageConfirmed 只會在正式 Gameplay Authority
         * 端有意義。
         *
         * 因此只有 State Authority
         * 需要真正訂閱 Damage Source。
         *
         * Input Authority 只需要接收最後的 RPC。
         */
        if (Object.HasStateAuthority)
        {
            RefreshDamageSourceBinding(
                force: true
            );
        }
    }

    public override void FixedUpdateNetwork()
    {
        /*
         * 每個 Tick 只比較：
         *
         * Current Runtime Object
         * Current Profession
         *
         * 沒有變化時完全不掃 Component。
         *
         * 所以這個檢查成本非常低。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        RefreshDamageSourceBinding(
            force: false
        );
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        UnsubscribeAllDamageSources();
    }

    private void OnDestroy()
    {
        UnsubscribeAllDamageSources();
    }

    #endregion

    // =====================================================================
    #region Damage Source Binding

    /// <summary>
    /// 檢查目前 Profession Runtime
    /// 是否和上一次 Binding 時不同。
    ///
    /// ------------------------------------------------------------
    ///
    /// 只有：
///
/// Runtime 改變
/// Profession 改變
/// 或 force = true
///
/// 才重新掃描 Damage Source。
/// </summary>
    private void RefreshDamageSourceBinding(
        bool force
    )
    {
        if (professionRuntimeManager == null ||
            profession == null)
        {
            return;
        }

        NetworkObject currentRuntimeObject =
            professionRuntimeManager
                .CurrentRuntimeObject;

        PlayerProfessionType currentProfession =
            profession.CurrentProfession;

        // =============================================================
        // 沒有改變
        // =============================================================

        if (force == false &&
            sourceBindingInitialized &&
            observedRuntimeObject ==
                currentRuntimeObject &&
            observedProfession ==
                currentProfession)
        {
            return;
        }

        // =============================================================
        // 先解除舊來源
        // =============================================================

        UnsubscribeAllDamageSources();

        // =============================================================
        // 更新觀察狀態
        // =============================================================

        observedRuntimeObject =
            currentRuntimeObject;

        observedProfession =
            currentProfession;

        sourceBindingInitialized =
            true;

        uniqueSourceBehaviours.Clear();

        // =============================================================
        // 1. 正式 Profession Runtime
        // =============================================================

        if (currentRuntimeObject != null &&
            currentRuntimeObject.IsValid)
        {
            CollectAndSubscribeSources(
                currentRuntimeObject.gameObject,
                includeChildren: true,
                sourceDescription:
                    "Profession Runtime"
            );
        }

        // =============================================================
        // 2. Attack Legacy Player Root
        // =============================================================

        /*
         * 現階段只有 Attack
         * 還有傷害來源留在 Player Root。
         *
         * Tank / Support 不應該因為
         * Player Root 上殘留 Attack Component
         * 就錯誤訂閱 Attack 傷害來源。
         */
        if (allowLegacyAttackPlayerRootSources &&
            currentProfession ==
                PlayerProfessionType.Attack)
        {
            int countBeforeLegacy =
                subscribedSourceBehaviours.Count;

            CollectAndSubscribeSources(
                gameObject,
                includeChildren: false,
                sourceDescription:
                    "Legacy Player Root"
            );

            int legacyAdded =
                subscribedSourceBehaviours.Count -
                countBeforeLegacy;

            if (legacyAdded > 0 &&
                debugLegacySources)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerCombatFeedbackRelay)}] " +
                    $"目前仍在使用 Player Root 的 Attack Damage Source Fallback。" +
                    $"\n找到來源數：{legacyAdded}" +
                    $"\n這在 AttackRifle / AttackQuickMelee 尚未搬入 Runtime 前屬於正常現象。",
                    this
                );
            }
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugSourceBinding)
        {
            Debug.Log(
                $"[Combat Feedback Source Binding]" +
                $"\nPlayer：{Object.InputAuthority}" +
                $"\nProfession：{currentProfession}" +
                $"\nRuntime：" +
                $"{(currentRuntimeObject != null ? currentRuntimeObject.name : "無")}" +
                $"\n訂閱來源數：{subscribedSourceBehaviours.Count}",
                this
            );
        }
    }

    /// <summary>
    /// 從指定物件搜尋所有
    /// ICombatDamageFeedbackSource
    /// 並正式訂閱 DamageConfirmed。
    ///
    /// ------------------------------------------------------------
    ///
    /// includeChildren = true：
    ///
    /// 允許未來 Profession Runtime
    /// 把技能模組分到子物件。
    ///
    /// ------------------------------------------------------------
    ///
    /// 同一個 Behaviour 永遠只會訂閱一次。
    /// </summary>
    private void CollectAndSubscribeSources(
        GameObject sourceRoot,
        bool includeChildren,
        string sourceDescription
    )
    {
        if (sourceRoot == null)
        {
            return;
        }

        MonoBehaviour[] behaviours =
            includeChildren
                ? sourceRoot
                    .GetComponentsInChildren<MonoBehaviour>(
                        true
                    )
                : sourceRoot
                    .GetComponents<MonoBehaviour>();

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

            // =========================================================
            // 必須實作 Feedback Source
            // =========================================================

            if (behaviour is not
                ICombatDamageFeedbackSource source)
            {
                continue;
            }

            // =========================================================
            // 同一 Behaviour 去重
            // =========================================================

            if (uniqueSourceBehaviours.Add(
                    behaviour
                ) == false)
            {
                continue;
            }

            // =========================================================
            // 訂閱
            // =========================================================

            /*
             * 先 -= 再 +=。
             *
             * 即使外部流程意外重複呼叫 Binding，
             * 也不會同一個 Source 訂閱兩次。
             */
            source.DamageConfirmed -=
                OnDamageConfirmed;

            source.DamageConfirmed +=
                OnDamageConfirmed;

            subscribedSourceBehaviours.Add(
                behaviour
            );

            // =========================================================
            // Debug
            // =========================================================

            if (debugSourceBinding)
            {
                Debug.Log(
                    $"[Combat Feedback Source] 已訂閱。" +
                    $"\n來源位置：{sourceDescription}" +
                    $"\n類型：{behaviour.GetType().Name}" +
                    $"\n物件：{behaviour.gameObject.name}",
                    behaviour
                );
            }
        }
    }

    /// <summary>
    /// 解除目前所有 Damage Source 訂閱。
    ///
    /// ------------------------------------------------------------
    ///
    /// Runtime 切換
    /// Player Despawn
    /// Object Destroy
    ///
    /// 都會走這裡。
    /// </summary>
    private void UnsubscribeAllDamageSources()
    {
        for (int i = 0;
             i < subscribedSourceBehaviours.Count;
             i++)
        {
            MonoBehaviour behaviour =
                subscribedSourceBehaviours[i];

            /*
             * Unity Object 已經被 Destroy
             * 就不需要再解除 Event。
             */
            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                ICombatDamageFeedbackSource source)
            {
                source.DamageConfirmed -=
                    OnDamageConfirmed;
            }
        }

        subscribedSourceBehaviours.Clear();
        uniqueSourceBehaviours.Clear();
    }

    #endregion

    // =====================================================================
    #region State Authority Damage Result

    /// <summary>
    /// 任意 ICombatDamageFeedbackSource
    /// 正式確認 DamageResult 後進入這裡。
    ///
    /// ------------------------------------------------------------
    ///
    /// Relay 不需要知道來源是：
///
/// Rifle
/// Melee
/// Dash
/// Ability。
///
/// ------------------------------------------------------------
    ///
    /// 它只把正式 DamageResult
    /// 轉換成 Presentation Data。
    /// </summary>
    private void OnDamageConfirmed(
        DamageResult result
    )
    {
        // =============================================================
        // State Authority Only
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // =============================================================
        // 只接受正式傷害
        // =============================================================

        if (result.Accepted == false)
        {
            return;
        }

        DamageRequest request =
            result.Request;

        // =============================================================
        // State Authority → Input Authority
        // =============================================================

        /*
         * DamageResult 裡含有：
         *
         * GameObject
         * NetworkObject
         * Interface / Gameplay State
         *
         * 不適合整包當 RPC Payload。
         *
         * 所以只傳 Presentation 真正需要的值。
         */
        RPC_ReceiveConfirmedHit(
            request.RequestedDamage,
            result.AppliedDamage,

            /*
            * 1. Gameplay Damage Type
            */
            (byte)request.DamageType,

            /*
            * 2. Combat Feedback Identity
            */
            (byte)request.FeedbackId,

            /*
            * 3. Hit Zone
            */
            (byte)request.HitZone,

            /*
            * 4. Damage Result
            */
            result.IsHeadshot,
            result.KilledTarget,
            result.WasBlocked,
            result.HasEffectiveDamage,

            /*
            * 5. Hit Information
            */
            request.HitPoint,
            request.Sequence
        );
    }

    #endregion

    // =====================================================================
    #region State Authority → Input Authority RPC

    /// <summary>
    /// 將 State Authority 已正式確認的命中結果，
    /// 傳送給真正控制這名玩家的 Input Authority。
    ///
    /// ------------------------------------------------------------
    ///
    /// 真正 Damage 已經在 State Authority 完成。
    ///
    /// 這個 RPC 只負責傳遞 Presentation 所需要的資料：
    ///
    /// Damage
    /// Feedback ID
    /// Headshot
    /// Kill
    /// Hit Point
    /// Sequence。
    ///
    /// ------------------------------------------------------------
    ///
    /// 不可以在這裡再次修改敵人 HP。
    /// </summary>
    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.InputAuthority,
        TickAligned = false
    )]
    private void RPC_ReceiveConfirmedHit(
        float requestedDamage,
        float appliedDamage,

        /*
        * Gameplay 傷害分類。
        *
        * Bullet
        * Melee
        * Explosion
        * Ability。
        */
        byte damageType,

        /*
        * ★ 新增
        *
        * 真正攻擊身份。
        *
        * AttackRifle
        * AttackQuickMelee
        * TankLightMelee
        * TankHeavyMelee。
        */
        byte feedbackId,

        /*
        * Body / Head / None。
        */
        byte hitZone,

        /*
        * 最終是否被 Damage Pipeline
        * 判定為 Headshot。
        */
        bool isHeadshot,

        /*
        * 是否因為這一次傷害而擊殺目標。
        */
        bool killedTarget,

        /*
        * 傷害是否被 Block。
        */
        bool wasBlocked,

        /*
        * 是否真正造成大於 0 的有效傷害。
        */
        bool hasEffectiveDamage,

        /*
        * 真正命中的世界座標。
        */
        Vector3 hitPoint,

        /*
        * 攻擊 Sequence。
        */
        int sequence
    )
    {
        // =============================================================
        // Input Authority Only
        // =============================================================

        /*
        * RpcTargets.InputAuthority
        * 本身已經限制接收者。
        *
        * 這裡再防呆一次，
        * 避免未來 RPC 設定改動後
        * Presentation 被其他玩家執行。
        */
        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            return;
        }

        // =============================================================
        // 重建 Local Presentation Data
        // =============================================================

        CombatHitFeedbackData feedback =
            new CombatHitFeedbackData
            {
                RequestedDamage =
                    requestedDamage,

                AppliedDamage =
                    appliedDamage,

                DamageType =
                    (DamageType)damageType,

                /*
                * ★ 新增的攻擊身份。
                */
                FeedbackId =
                    (CombatFeedbackId)feedbackId,

                HitZone =
                    (DamageHitZoneType)hitZone,

                IsHeadshot =
                    isHeadshot,

                KilledTarget =
                    killedTarget,

                WasBlocked =
                    wasBlocked,

                HasEffectiveDamage =
                    hasEffectiveDamage,

                HitPoint =
                    hitPoint,

                Sequence =
                    sequence
            };

        // =============================================================
        // Local Presentation Event
        // =============================================================

        /*
        * Hit Marker
        * Camera Shake
        * Hit Sound
        *
        * 都從這個事件接收。
        */
        LocalHitConfirmed?.Invoke(
            feedback
        );

        // =============================================================
        // Debug
        // =============================================================

        if (debugFeedback)
        {
            Debug.Log(
                $"[Local Combat Feedback] 收到正式命中。" +
                $"\nRequested Damage：{feedback.RequestedDamage:F2}" +
                $"\nApplied Damage：{feedback.AppliedDamage:F2}" +
                $"\nDamage Type：{feedback.DamageType}" +
                $"\nFeedback ID：{feedback.FeedbackId}" +
                $"\nHit Zone：{feedback.HitZone}" +
                $"\nHeadshot：{feedback.IsHeadshot}" +
                $"\nKill：{feedback.KilledTarget}" +
                $"\nBlocked：{feedback.WasBlocked}" +
                $"\nEffective Damage：{feedback.HasEffectiveDamage}" +
                $"\nHit Point：{feedback.HitPoint}" +
                $"\nSequence：{feedback.Sequence}" +
                $"\n只有攻擊者本人應該收到這次 Presentation Feedback。",
                this
            );
        }
    }
    #endregion
    
    /// <summary>
    /// 通知本地 Presentation：
    /// 玩家真正執行了一次攻擊動作。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這不是 Hit Confirm。
    ///
    /// 所以：
    ///
    /// 揮空
    /// → 仍然會進入。
    ///
    /// 命中
    /// → 也會進入一次。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前主要提供：
    ///
    /// Tank Light Swing
    /// Tank Heavy Swing。
    /// </summary>
    public void NotifyLocalAttackPerformed(
        CombatFeedbackId feedbackId
    )
    {
        // =============================================================
        // Debug：確認 Relay 真的收到
        // =============================================================

        Debug.Log(
            $"[Combat Feedback Relay] NotifyLocalAttackPerformed。" +
            $"\nFeedback ID：{feedbackId}" +
            $"\nObject：{(Object != null ? Object.name : "NULL")}" +
            $"\nHas Input Authority：" +
            $"{(Object != null && Object.HasInputAuthority)}" +
            $"\n有沒有 LocalAttackPerformed 訂閱者：" +
            $"{(LocalAttackPerformed != null)}",
            this
        );

        // =============================================================
        // 必須是本地 Player
        // =============================================================

        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerCombatFeedbackRelay)}] " +
                $"拒絕 LocalAttackPerformed，" +
                $"因為這個 Player 沒有 Input Authority。",
                this
            );

            return;
        }

        // =============================================================
        // 無效 ID
        // =============================================================

        if (feedbackId ==
            CombatFeedbackId.None)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerCombatFeedbackRelay)}] " +
                $"拒絕 LocalAttackPerformed，" +
                $"因為 FeedbackId = None。",
                this
            );

            return;
        }

        // =============================================================
        // 正式觸發
        // =============================================================

        LocalAttackPerformed?.Invoke(
            feedbackId
        );
    }
}