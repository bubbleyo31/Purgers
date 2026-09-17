using Fusion;
using UnityEngine;

/// <summary>
/// 傷害來源種類。
///
/// 這是整個遊戲共用的分類，
/// 不限定武器。
///
/// 未來玩家、敵人、技能、環境傷害
/// 都使用同一套 DamageType。
/// </summary>
public enum DamageType : byte
{
    /// <summary>
    /// 尚未指定傷害類型。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 槍械子彈。
    /// </summary>
    Bullet = 1,

    /// <summary>
    /// 爆炸。
    /// </summary>
    Explosion = 2,

    /// <summary>
    /// 近戰。
    /// </summary>
    Melee = 3,

    /// <summary>
    /// 主動或被動技能。
    /// </summary>
    Ability = 4,

    /// <summary>
    /// 場景或環境造成的傷害。
    ///
    /// 例如：
    /// 毒區
    /// 岩漿
    /// 地圖機關。
    /// </summary>
    Environment = 5,

    /// <summary>
    /// 持續傷害。
    ///
    /// 例如：
    /// 中毒
    /// 燃燒
    /// 流血。
    /// </summary>
    DamageOverTime = 6
}

/// <summary>
/// 傷害命中的部位分類。
///
/// 目前 WeaponHitZone 還是保留自己的設定，
/// AttackRifle 會在產生 DamageRequest 時
/// 將結果轉換成這個共用格式。
/// </summary>
public enum DamageHitZoneType : byte
{
    /// <summary>
    /// 沒有特殊命中區域。
    /// </summary>
    None = 0,

    /// <summary>
    /// 一般身體。
    /// </summary>
    Body = 1,

    /// <summary>
    /// 頭部。
    /// </summary>
    Head = 2,

    /// <summary>
    /// 特殊弱點。
    ///
    /// 預留給 Boss、植物核心等。
    /// </summary>
    WeakPoint = 3,

    /// <summary>
    /// 裝甲區域。
    /// </summary>
    Armor = 4
}

/// <summary>
/// 傷害被「強制視為暴頭」的來源。
///
/// ------------------------------------------------------------
///
/// Physical Headshot：
///
/// Raycast 真正打到 Head HitZone。
///
/// Forced Headshot：
///
/// 實際可能打到 Body，
/// 但因 Gameplay 效果而以暴頭規則結算。
///
/// ------------------------------------------------------------
///
/// 這樣我們就不需要把：
///
/// Body
///
/// 偽造為：
///
/// Head。
///
/// 未來統計系統仍然可以區分：
///
/// 真正射中頭部
/// 與
/// 技能強制暴頭。
/// </summary>
public enum DamageForcedHeadshotSource : byte
{
    /// <summary>
    /// 沒有任何強制暴頭效果。
    /// </summary>
    None = 0,

    /// <summary>
    /// Attack 職業勾索標記將 Body Damage 強制視為暴頭。
    ///
    /// 已使用的數值不可任意更換，
    /// 因為這個 Enum 未來可能進入 Replay、統計或網路資料。
    /// </summary>
    AttackGrappleMark = 1,

    /// <summary>
    /// Tank 三段普通攻擊中的第三段 Heavy。
    ///
    /// 這不是物理命中 Head Hitbox，
    /// 而是 Heavy Gameplay 規則固定視為 Headshot。
    ///
    /// 目前只影響 IsHeadshot、Presentation 與未來統計；
    /// Heavy 原本的 Damage 數值不會再乘一次暴頭倍率。
    /// </summary>
    TankHeavyMelee = 2,

    /// <summary>
    /// Tank 在 GrappleAirborne 發動的 Enemy Air Strike。
    ///
    /// Arrival AOE 的每一筆有效傷害都視為 Forced Headshot，
    /// 但不額外改變目前 enemyArrivalDamage。
    /// </summary>
    TankAirStrike = 3
}

/// <summary>
/// 傷害被拒絕的原因。
///
/// 目前很多狀態還沒有實作，
/// 但先把標準留下來。
///
/// 未來 UI 或除錯系統可以知道：
/// 「不是沒打到，而是目標拒絕了這次傷害。」
/// </summary>
public enum DamageRejectReason : byte
{
    /// <summary>
    /// 沒有被拒絕。
    /// </summary>
    None = 0,

    /// <summary>
    /// 命中的物件沒有任何 IDamageReceiver。
    /// </summary>
    NoReceiver = 1,

    /// <summary>
    /// 傷害資料本身無效。
    ///
    /// 例如 RequestedDamage 小於等於零。
    /// </summary>
    InvalidRequest = 2,

    /// <summary>
    /// 目標目前無敵。
    /// </summary>
    Invulnerable = 3,

    /// <summary>
    /// 目標已經死亡。
    /// </summary>
    AlreadyDead = 4,

    /// <summary>
    /// 未來敵我系統阻止友軍傷害。
    /// </summary>
    FriendlyFireBlocked = 5,

    /// <summary>
    /// 傷害完全被護盾或其他系統阻擋。
    /// </summary>
    FullyBlocked = 6,

    /// <summary>
    /// 其他未分類原因。
    /// </summary>
    Other = 7,

    /// <summary>
    /// 目前執行這次傷害的 Peer
    /// 沒有該目標的 State Authority。
    ///
    /// 在 Host / Dedicated Server 架構下，
    /// 正式 HP 只能由 Server / Host 修改。
    /// </summary>
    NotStateAuthority = 8
}

/// <summary>
/// 一次「要求造成傷害」的完整資料。
///
/// 這是攻擊方送出去的資料。
///
/// 注意：
/// RequestedDamage 是攻擊端計算完成後
/// 希望造成的傷害。
///
/// 它不代表目標最後真的會扣這麼多。
///
/// 例如：
///
/// AttackRifle
/// BaseDamage = 20
/// Focus × 2
/// Headshot × 2
///
/// RequestedDamage = 80
///
/// ↓
///
/// Enemy Armor -25%
///
/// ↓
///
/// DamageResult.AppliedDamage = 60
/// </summary>
public struct DamageRequest
{
    /// <summary>
    /// 攻擊方要求造成的傷害。
    ///
    /// 已經可以包含：
    /// 距離衰退
    /// 暴頭倍率
    /// 技能倍率
    /// 武器倍率。
    /// </summary>
    public float RequestedDamage;

    /// <summary>
    /// 在傷害真正進入 IDamageReceiver 前，
    /// 已經被防禦類 Gameplay 系統阻擋掉多少傷害。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// 原始 RequestedDamage = 100
    ///
    /// Tank Guard 理論上可以擋 70，
    /// 而 Stamina 也足夠。
    ///
    /// 那最後：
    ///
    /// RequestedDamage = 30
    /// BlockedDamage = 70。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個值是累積值。
    ///
    /// 未來如果：
    ///
    /// Shield 擋 20
    /// Guard 再擋 30
    ///
    /// 最後可以是：
    ///
    /// BlockedDamage = 50。
    ///
    /// ------------------------------------------------------------
    ///
    /// DamageResult.WasBlocked
    /// 會根據這個值自動變成 true。
    /// </summary>
    public float BlockedDamage;

    /// <summary>
    /// 攻擊來源的原始基礎傷害。
    ///
    /// 主要提供：
    /// Debug
    /// 統計
    /// 未來 Buff 系統
    /// 傷害分析。
    /// </summary>
    public float BaseDamage;

    /// <summary>
    /// 傷害類型。
    /// </summary>
    public DamageType DamageType;

    /// <summary>
    /// 這次攻擊使用的 Presentation Feedback 身份。
    ///
    /// 例如：
    ///
    /// AttackRifle
    /// AttackQuickMelee
    /// TankLightMelee
    /// TankHeavyMelee。
    /// </summary>
    public CombatFeedbackId FeedbackId;

    /// <summary>
    /// 本次命中的部位。
    /// </summary>
    public DamageHitZoneType HitZone;

    /// <summary>
    /// 如果傷害來自玩家，
    /// 這裡記錄該玩家的 PlayerRef。
    ///
    /// 環境傷害或純 AI 傷害之後可以使用預設值。
    /// </summary>
    public PlayerRef Attacker;

    /// <summary>
    /// 造成這次傷害的 NetworkObject。
    ///
    /// 目前 AttackRifle 掛在 Player NetworkObject 上，
    /// 因此會使用玩家本身的 NetworkObject。
    /// </summary>
    public NetworkObject SourceNetworkObject;

    /// <summary>
    /// 實際造成傷害的來源 GameObject。
    ///
    /// 例如：
    /// 玩家
    /// 武器
    /// 技能物件
    /// 爆炸物。
    /// </summary>
    public GameObject SourceObject;

    /// <summary>
    /// Raycast / Hitbox 實際碰到的 GameObject。
    ///
    /// 例如：
    /// Enemy/Head
    /// Enemy/Body。
    /// </summary>
    public GameObject HitObject;

    /// <summary>
    /// 命中的世界座標。
    /// </summary>
    public Vector3 HitPoint;

    /// <summary>
    /// 命中表面的世界法線。
    ///
    /// 未來可用於：
    /// 彈孔
    /// 火花
    /// 血液特效。
    /// </summary>
    public Vector3 HitNormal;

    /// <summary>
    /// 攻擊真正飛行的世界方向。
    ///
    /// 未來可以用於：
    /// 擊退
    /// 受擊動畫方向
    /// 敵人反應。
    /// </summary>
    public Vector3 HitDirection;

    /// <summary>
    /// 攻擊來源到命中點的距離。
    /// </summary>
    public float Distance;

    /// <summary>
    /// 這次攻擊的流水編號。
    ///
    /// AttackRifle 目前會使用 ShotSequence。
    ///
    /// 未來可用於避免同一發攻擊
    /// 被錯誤重複處理。
    /// </summary>
    public int Sequence;

    /// <summary>
    /// 這個攻擊來源本身的暴頭傷害倍率。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如 AttackRifle：
    ///
    /// Headshot Multiplier = 2
    ///
    /// 那這裡就是 2。
    ///
    /// ------------------------------------------------------------
    ///
    /// 為什麼放進 DamageRequest？
    ///
    /// 因為 Attack Grapple Mark
    /// 是「目標端」的 Damage Modifier。
    ///
    /// 它必須知道：
    ///
    /// 如果這一擊被強制算成暴頭，
    /// 這個攻擊來源自己的暴頭倍率是多少。
    ///
    /// ------------------------------------------------------------
    ///
    /// 沒有暴頭傷害倍率概念的攻擊，
    /// 建議設定為 1。
    /// </summary>
    public float HeadshotDamageMultiplier;

    /// <summary>
    /// 這一擊是否因某種 Gameplay 效果
    /// 被強制視為暴頭。
    ///
    /// None：
    /// 沒有。
    ///
    /// AttackGrappleMark：
    /// 因 Attack 勾索標記而視為暴頭。
    /// </summary>
    public DamageForcedHeadshotSource
        ForcedHeadshotSource;

    /// <summary>
    /// 這一擊是否真的命中了 Head HitZone。
    ///
    /// 這是「物理命中結果」，
    /// 不會受到技能修改。
    /// </summary>
    public bool IsPhysicalHeadshot =>
        HitZone ==
        DamageHitZoneType.Head;

    /// <summary>
    /// 是否因 Gameplay 效果
    /// 被強制視為暴頭。
    /// </summary>
    public bool IsForcedHeadshot =>
        ForcedHeadshotSource !=
        DamageForcedHeadshotSource.None;

    /// <summary>
    /// Gameplay 上最終是否應該被視為暴頭。
    ///
    /// ------------------------------------------------------------
    ///
    /// 真正打中頭：
    /// true
    ///
    /// 或：
    ///
    /// Body Hit
    /// +
    /// Attack Grapple Mark
    ///
    /// 也會：
    /// true。
    /// </summary>
    public bool IsHeadshot =>
        IsPhysicalHeadshot ||
        IsForcedHeadshot;
}

/// <summary>
/// 一次 DamageRequest 經過目標處理後，
/// 得到的正式結果。
///
/// 未來所有攻擊回饋應優先依賴 DamageResult，
/// 而不是直接依賴 Raycast Hit。
/// </summary>
public struct DamageResult
{
    /// <summary>
    /// 原始傷害要求。
    /// </summary>
    public DamageRequest Request;

    /// <summary>
    /// 目標是否接受這次傷害。
    ///
    /// true 不一定代表 AppliedDamage 大於零。
    ///
    /// 例如未來護盾系統可能：
    /// Accepted = true
    /// AppliedDamage = 0
    /// WasBlocked = true
    /// </summary>
    public bool Accepted;

    /// <summary>
    /// 最後真正套用到目標上的傷害。
    ///
    /// 這才是未來 UI 傷害數字
    /// 應該優先使用的值。
    /// </summary>
    public float AppliedDamage;

    /// <summary>
    /// 實際接收這次傷害的目標 Root。
    ///
    /// 例如 Raycast 打到：
    /// Enemy/Head
    ///
    /// 但 IDamageReceiver 在：
    /// Enemy
    ///
    /// 那 TargetObject 就會是 Enemy。
    /// </summary>
    public GameObject TargetObject;

    /// <summary>
    /// 目標對應的 NetworkObject。
    ///
    /// 如果目標目前不是 NetworkObject，
    /// 可以為 null。
    /// </summary>
    public NetworkObject TargetNetworkObject;

    /// <summary>
    /// 這次傷害是否造成目標死亡。
    /// </summary>
    public bool KilledTarget;

    /// <summary>
    /// 是否被防禦系統完全或部分阻擋。
    /// </summary>
    public bool WasBlocked;

    /// <summary>
    /// 是否因無敵狀態而無效。
    /// </summary>
    public bool WasImmune;

    /// <summary>
    /// 如果 Accepted = false，
    /// 這裡可以說明為什麼被拒絕。
    /// </summary>
    public DamageRejectReason RejectReason;

    /// <summary>
    /// 是否為暴頭。
    /// </summary>
    public bool IsHeadshot =>
        Request.IsHeadshot;

    /// <summary>
    /// 是否為真正命中 Head HitZone。
    /// </summary>
    public bool IsPhysicalHeadshot =>
        Request.IsPhysicalHeadshot;

    /// <summary>
    /// 是否因 Gameplay 效果
    /// 被強制視為暴頭。
    /// </summary>
    public bool IsForcedHeadshot =>
        Request.IsForcedHeadshot;

    /// <summary>
    /// 如果是強制暴頭，
    /// 是哪一個系統造成的。
    /// </summary>
    public DamageForcedHeadshotSource
        ForcedHeadshotSource =>
            Request.ForcedHeadshotSource;

    /// <summary>
    /// 是否真的造成大於零的有效傷害。
    ///
    /// 暴頭回血等 Gameplay Reward
    /// 建議使用這個條件，
    /// 而不是單純 Accepted。
    /// </summary>
    public bool HasEffectiveDamage =>
        Accepted &&
        AppliedDamage > 0f;

    // =================================================================
    #region 建立標準結果

    /// <summary>
    /// 建立「沒有傷害接收器」的結果。
    /// </summary>
    public static DamageResult CreateNoReceiver(
        DamageRequest request,
        GameObject hitObject
    )
    {
        return new DamageResult
        {
            Request =
                request,

            Accepted =
                false,

            AppliedDamage =
                0f,

            TargetObject =
                hitObject,

            TargetNetworkObject =
                hitObject != null
                    ? hitObject.GetComponentInParent<NetworkObject>()
                    : null,

            KilledTarget =
                false,

            WasBlocked =
                false,

            WasImmune =
                false,

            RejectReason =
                DamageRejectReason.NoReceiver
        };
    }

    /// <summary>
    /// 建立被拒絕的傷害結果。
    /// </summary>
    public static DamageResult CreateRejected(
        DamageRequest request,
        GameObject targetObject,
        DamageRejectReason reason,
        bool wasBlocked = false,
        bool wasImmune = false
    )
    {
        return new DamageResult
        {
            Request =
                request,

            Accepted =
                false,

            AppliedDamage =
                0f,

            TargetObject =
                targetObject,

            TargetNetworkObject =
                targetObject != null
                    ? targetObject.GetComponentInParent<NetworkObject>()
                    : null,

            KilledTarget =
                false,

            WasBlocked =
                wasBlocked,

            WasImmune =
                wasImmune,

            RejectReason =
                reason
        };
    }

    /// <summary>
    /// 建立正式套用成功的傷害結果。
    /// </summary>
    public static DamageResult CreateApplied(
        DamageRequest request,
        GameObject targetObject,
        float appliedDamage,
        bool killedTarget = false,
        bool wasBlocked = false
    )
    {
        return new DamageResult
        {
            Request =
                request,

            Accepted =
                true,

            AppliedDamage =
                Mathf.Max(
                    0f,
                    appliedDamage
                ),

            TargetObject =
                targetObject,

            TargetNetworkObject =
                targetObject != null
                    ? targetObject.GetComponentInParent<NetworkObject>()
                    : null,

            KilledTarget =
                killedTarget,

            WasBlocked =
                wasBlocked,

            WasImmune =
                false,

            RejectReason =
                DamageRejectReason.None
        };
    }

    #endregion
}

/// <summary>
/// 所有可以接受傷害的 Gameplay 物件
/// 都實作這個介面。
///
/// 未來：
///
/// EnemyHealth
/// PlayerHealth
/// BossHealth
/// DestructibleObject
/// Shield
///
/// 都使用同一份接口。
/// </summary>
public interface IDamageReceiver
{
    /// <summary>
    /// 接收一次傷害要求，
/// 並回傳目標處理完成後的正式結果。
    ///
    /// 注意：
    /// 傷害接收者有權修改最終傷害。
    ///
    /// 例如：
/// Armor
/// Resistance
/// Shield
/// Invulnerability
/// Death State。
    /// </summary>
    DamageResult ReceiveDamage(
        DamageRequest request
    );
}

/// <summary>
/// 傷害接收器共用搜尋工具。
///
/// 攻擊系統不需要自己知道：
///
/// EnemyHealth 在哪一層
/// PlayerHealth 在哪一層
/// BreakableObject 在哪一層。
///
/// 只要從真正命中的 GameObject
/// 向父物件搜尋 IDamageReceiver 即可。
/// </summary>
public static class DamageReceiverUtility
{
    /// <summary>
    /// 嘗試將 DamageRequest 傳給命中物件所屬的 IDamageReceiver。
    /// </summary>
    /// <param name="hitObject">
    /// Raycast / Hitbox 實際命中的 GameObject。
    /// </param>
    /// <param name="request">
    /// 傷害要求。
    /// </param>
    /// <param name="result">
    /// 目標處理完成後的 DamageResult。
    /// </param>
    /// <returns>
    /// true：
    /// 找到了 IDamageReceiver。
    ///
    /// false：
    /// 完全沒有 DamageReceiver。
    ///
    /// 注意：
    /// true 不代表傷害一定成功，
    /// 還必須另外檢查 result.Accepted。
    /// </returns>
    public static bool TryApplyDamage(
        GameObject hitObject,
        DamageRequest request,
        out DamageResult result
    )
    {
        // =============================================================
        // Hit Object
        // =============================================================

        if (hitObject == null)
        {
            result =
                DamageResult.CreateNoReceiver(
                    request,
                    null
                );

            return false;
        }

        // =============================================================
        // 一次取得父階層 Gameplay Behaviour
        // =============================================================

        MonoBehaviour[] behaviours =
            hitObject
                .GetComponentsInParent<
                    MonoBehaviour
                >(
                    true
                );

        // =============================================================
        // 1. 先確認真正 Damage Receiver
        // =============================================================

        /*
        * 非常重要：
        *
        * 以前是先執行 Modifier，
        * 再確認 Receiver。
        *
        * 這會造成：
        *
        * 玩家根本沒有 Health Receiver
        * ↓
        * Guard 卻先消耗 Stamina
        * ↓
        * 最後才發現 NoReceiver。
        *
        * 這是錯的。
        *
        * ------------------------------------------------------------
        *
        * 所以現在一定先找到 Receiver。
        */
        IDamageReceiver targetReceiver =
            null;

        MonoBehaviour targetReceiverBehaviour =
            null;

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
                IDamageReceiver receiver)
            {
                targetReceiver =
                    receiver;

                targetReceiverBehaviour =
                    behaviour;

                break;
            }
        }

        // =============================================================
        // 沒有 Receiver
        // =============================================================

        if (targetReceiver == null ||
            targetReceiverBehaviour == null)
        {
            result =
                DamageResult.CreateNoReceiver(
                    request,
                    hitObject
                );

            return false;
        }

        // =============================================================
        // 2. Damage Modifier
        // =============================================================

        DamageRequest resolvedRequest =
            request;

        MonoBehaviour[] sourceBehaviours =
    GetOutgoingSourceBehaviours(
        resolvedRequest
    );

    for (int i = 0;
        i < sourceBehaviours.Length;
        i++)
    {
        MonoBehaviour behaviour =
            sourceBehaviours[i];

        if (behaviour == null ||
            behaviour.isActiveAndEnabled == false)
        {
            continue;
        }

        if (behaviour is
            IOutgoingDamageModifier modifier)
        {
            modifier.ModifyOutgoingDamage(
                ref resolvedRequest
            );

            resolvedRequest.RequestedDamage =
                Mathf.Max(
                    0f,
                    resolvedRequest.RequestedDamage
                );
        }
    }

        /*
        * 現在只有確定：
        *
        * 「這個物件真的能接受傷害」
        *
        * 之後，
        * 才允許 Gameplay Modifier 修改 Request。
        *
        * ------------------------------------------------------------
        *
        * Enemy：
        *
        * AttackGrappleMarkState
        *
        * ------------------------------------------------------------
        *
        * Player：
        *
        * PlayerIncomingDamageModifierBridge
        * ↓
        * TankGuardAbility。
        */
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
                IDamageRequestModifier modifier)
            {
                modifier.ModifyDamageRequest(
                    ref resolvedRequest
                );

                resolvedRequest.RequestedDamage =
                    Mathf.Max(
                        0f,
                        resolvedRequest.RequestedDamage
                    );

                resolvedRequest.BlockedDamage =
                    Mathf.Max(
                        0f,
                        resolvedRequest.BlockedDamage
                    );
            }
        }

        // =============================================================
        // 3. 被 Modifier 完全阻擋
        // =============================================================

        /*
        * 目前 Tank Guard 是 70%，
        * 正常不會到 0。
        *
        * 但未來：
        *
        * Shield
        * Parry
        * 100% Guard Upgrade
        *
        * 都可能把 RequestedDamage 壓到 0。
        *
        * ------------------------------------------------------------
        *
        * 這時不要再把 0 Damage
        * 丟給 Health Receiver。
        */
        if (resolvedRequest.RequestedDamage <=
                0f &&
            resolvedRequest.BlockedDamage >
                0f)
        {
            result =
                DamageResult.CreateRejected(
                    resolvedRequest,
                    targetReceiverBehaviour.gameObject,
                    DamageRejectReason.FullyBlocked,
                    wasBlocked: true,
                    wasImmune: false
                );

            NotifyOutgoingDamageResolved(
                sourceBehaviours,
                result
            );

            return true;
        }

        // =============================================================
        // 4. 正式 Receiver
        // =============================================================

        result =
            targetReceiver.ReceiveDamage(
                resolvedRequest
            );

        // =============================================================
        // 5. Result 一致性
        // =============================================================

        /*
        * 永遠保存 Modifier 處理完成的 Request。
        */
        result.Request =
            resolvedRequest;

        /*
        * Receiver 自己可能有 Shield / Armor。
        *
        * 或：
        *
        * Runtime Modifier 已經擋過傷害。
        *
        * 任一成立：
        *
        * WasBlocked = true。
        */
        result.WasBlocked =
            result.WasBlocked ||
            resolvedRequest.BlockedDamage >
                0f;

        if (result.TargetObject == null)
        {
            result.TargetObject =
                targetReceiverBehaviour.gameObject;
        }

        if (result.TargetNetworkObject == null)
        {
            result.TargetNetworkObject =
                result.TargetObject
                    .GetComponentInParent<
                        NetworkObject
                    >();
        }

        // Receiver 接受或拒絕的結果都已結算完成，統一通知一次。
        // NoReceiver 不會進入此處；FullyBlocked 已在上方通知後返回。
        NotifyOutgoingDamageResolved(
            sourceBehaviours,
            result
        );

        return true;
    }

    private static MonoBehaviour[]
        GetOutgoingSourceBehaviours(
            in DamageRequest request
        )
    {
        GameObject sourceObject =
            request.SourceNetworkObject != null
                ? request.SourceNetworkObject.gameObject
                : request.SourceObject;

        if (sourceObject == null)
        {
            return
                System.Array.Empty<MonoBehaviour>();
        }

        return sourceObject
            .GetComponentsInParent<MonoBehaviour>(
                true
            );
    }

    private static void NotifyOutgoingDamageResolved(
        MonoBehaviour[] sourceBehaviours,
        in DamageResult result
    )
    {
        for (int i = 0;
            i < sourceBehaviours.Length;
            i++)
        {
            MonoBehaviour behaviour =
                sourceBehaviours[i];

            if (behaviour == null ||
                behaviour.isActiveAndEnabled == false)
            {
                continue;
            }

            if (behaviour is
                IOutgoingDamageResultListener listener)
            {
                listener.OnOutgoingDamageResolved(
                    result
                );
            }
        }
    }
}

/// <summary>
/// 可以在 DamageRequest 正式送進 IDamageReceiver 前
/// 修改這筆傷害的 Gameplay Modifier。
///
/// ------------------------------------------------------------
///
/// 例如：
///
/// Attack Grapple Mark
/// → Body Hit 強制視為 Headshot
///
/// 未來也可能有：
///
/// Vulnerable Debuff
/// Elemental Status
/// Special Weakness。
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// 這不是 Health / Armor。
///
/// Armor、Shield 等「目標真正承受多少傷害」
/// 仍然應由 IDamageReceiver 處理。
///
/// 這裡處理的是：
///
/// 「攻擊要求本身在進入 Receiver 前
/// 應該被怎麼修改。」
/// </summary>
public interface IDamageRequestModifier
{
    /// <summary>
    /// 修改即將送進 Damage Receiver 的 DamageRequest。
    ///
    /// 使用 ref 是因為 Modifier
    /// 需要直接修改這一次的 Request。
    /// </summary>
    void ModifyDamageRequest(
        ref DamageRequest request
    );
}

/// <summary>
/// 修改攻擊者送出的傷害。
/// 不應修改 BaseDamage，只修改 RequestedDamage。
/// </summary>
public interface IOutgoingDamageModifier
{
    void ModifyOutgoingDamage(
        ref DamageRequest request
    );
}

/// <summary>
/// 接收攻擊者送出的最終傷害結果。
/// 預留給擊殺、吸血、冷卻縮減等系統。
/// </summary>
public interface IOutgoingDamageResultListener
{
    void OnOutgoingDamageResolved(
        in DamageResult result
    );
}
