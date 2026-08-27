using Fusion;
using UnityEngine;

/// <summary>
/// 生命值發生改變的原因。
///
/// 這不是傷害類型，
/// 而是「生命值為什麼發生變化」。
///
/// 未來：
///
/// HP Bar
/// Damage Number
/// Heal Number
/// Death Logic
/// Combat Log
///
/// 都可以使用這份統一分類。
/// </summary>
public enum HealthChangeType : byte
{
    /// <summary>
    /// 尚未指定。
    /// </summary>
    None = 0,

    /// <summary>
    /// 因 DamageRequest 受到傷害。
    /// </summary>
    Damage = 1,

    /// <summary>
    /// 因 HealRequest 恢復生命值。
    /// </summary>
    Heal = 2,

    /// <summary>
    /// 從死亡狀態復活。
    /// </summary>
    Revive = 3,

    /// <summary>
    /// 系統直接重設生命值。
    ///
    /// 目前主要提供測試使用。
    /// </summary>
    Reset = 4
}

/// <summary>
/// 回血要求被拒絕的原因。
/// </summary>
public enum HealRejectReason : byte
{
    /// <summary>
    /// 沒有被拒絕。
    /// </summary>
    None = 0,

    /// <summary>
    /// RequestedHeal 無效。
    /// </summary>
    InvalidRequest = 1,

    /// <summary>
    /// 目前執行端不是目標的 State Authority。
    /// </summary>
    NotStateAuthority = 2,

    /// <summary>
    /// 目標已經死亡。
    ///
    /// 普通 Heal 不允許把死人救起來。
    /// 必須使用 Revive。
    /// </summary>
    TargetDead = 3,

    /// <summary>
    /// 目標已經滿血。
    /// </summary>
    AlreadyFullHealth = 4,

    /// <summary>
    /// 其他原因。
    /// </summary>
    Other = 5
}

/// <summary>
/// 復活要求被拒絕的原因。
/// </summary>
public enum ReviveRejectReason : byte
{
    /// <summary>
    /// 沒有被拒絕。
    /// </summary>
    None = 0,

    /// <summary>
    /// RestoreHealth 無效。
    /// </summary>
    InvalidRequest = 1,

    /// <summary>
    /// 目前執行端不是目標的 State Authority。
    /// </summary>
    NotStateAuthority = 2,

    /// <summary>
    /// 目標目前仍然活著。
    ///
    /// 活著的目標不需要 Revive。
    /// </summary>
    TargetAlreadyAlive = 3,

    /// <summary>
    /// 其他原因。
    /// </summary>
    Other = 4
}

/// <summary>
/// 一次回血要求。
///
/// 與 DamageRequest 的設計概念相同：
///
/// 治療來源
/// ↓
/// HealRequest
/// ↓
/// IHealthReceiver
/// ↓
/// HealResult
/// </summary>
public struct HealRequest
{
    /// <summary>
    /// 治療來源希望恢復多少生命值。
    ///
    /// 不代表最後一定會恢復這麼多。
    ///
    /// 例如：
///
/// HP = 90 / 100
/// RequestedHeal = 50
///
/// 最終：
/// AppliedHeal = 10。
    /// </summary>
    public float RequestedHeal;

    /// <summary>
    /// 如果治療來源是玩家，
    /// 記錄該玩家 PlayerRef。
    /// </summary>
    public PlayerRef Healer;

    /// <summary>
    /// 造成這次治療的 NetworkObject。
    ///
    /// 例如：
/// Player
/// Healing Turret
/// Support Drone。
    /// </summary>
    public NetworkObject SourceNetworkObject;

    /// <summary>
    /// 實際治療來源 GameObject。
    /// </summary>
    public GameObject SourceObject;

    /// <summary>
    /// 治療流水編號。
    ///
    /// 未來可以用於：
/// 重複事件辨識
/// UI 對照
/// 技能統計。
    /// </summary>
    public int Sequence;
}

/// <summary>
/// HealRequest 被目標處理後的正式結果。
/// </summary>
public struct HealResult
{
    /// <summary>
    /// 原始治療要求。
    /// </summary>
    public HealRequest Request;

    /// <summary>
    /// 這次 Heal 是否被目標接受。
    /// </summary>
    public bool Accepted;

    /// <summary>
    /// 最後真正恢復的生命值。
    /// </summary>
    public float AppliedHeal;

    /// <summary>
    /// 被治療的目標。
    /// </summary>
    public GameObject TargetObject;

    /// <summary>
    /// 目標對應的 NetworkObject。
    /// </summary>
    public NetworkObject TargetNetworkObject;

    /// <summary>
    /// 如果 Accepted = false，
    /// 記錄拒絕原因。
    /// </summary>
    public HealRejectReason RejectReason;

    /// <summary>
    /// 是否真的恢復了生命值。
    /// </summary>
    public bool HasEffectiveHeal =>
        Accepted &&
        AppliedHeal > 0f;

    /// <summary>
    /// 建立成功的 HealResult。
    /// </summary>
    public static HealResult CreateApplied(
        HealRequest request,
        GameObject targetObject,
        float appliedHeal
    )
    {
        return new HealResult
        {
            Request =
                request,

            Accepted =
                true,

            AppliedHeal =
                Mathf.Max(
                    0f,
                    appliedHeal
                ),

            TargetObject =
                targetObject,

            TargetNetworkObject =
                targetObject != null
                    ? targetObject.GetComponentInParent<NetworkObject>()
                    : null,

            RejectReason =
                HealRejectReason.None
        };
    }

    /// <summary>
    /// 建立被拒絕的 HealResult。
    /// </summary>
    public static HealResult CreateRejected(
        HealRequest request,
        GameObject targetObject,
        HealRejectReason reason
    )
    {
        return new HealResult
        {
            Request =
                request,

            Accepted =
                false,

            AppliedHeal =
                0f,

            TargetObject =
                targetObject,

            TargetNetworkObject =
                targetObject != null
                    ? targetObject.GetComponentInParent<NetworkObject>()
                    : null,

            RejectReason =
                reason
        };
    }
}

/// <summary>
/// 一次復活要求。
///
/// Revive 與普通 Heal 必須分開。
///
/// 原因：
///
/// Heal
/// → 只能治療活著的目標。
///
/// Revive
/// → 唯一允許把死亡目標重新變成存活狀態的流程。
/// </summary>
public struct ReviveRequest
{
    /// <summary>
    /// 復活成功後希望恢復到多少生命值。
    ///
    /// 例如：
///
/// RestoreHealth = 30
///
/// 代表復活後：
/// HP = 30。
///
/// 如果超過 MaxHealth，
/// Receiver 會自動 Clamp。
    /// </summary>
    public float RestoreHealth;

    /// <summary>
    /// 執行復活的玩家。
    /// </summary>
    public PlayerRef Reviver;

    /// <summary>
    /// 復活來源 NetworkObject。
    /// </summary>
    public NetworkObject SourceNetworkObject;

    /// <summary>
    /// 復活來源 GameObject。
    /// </summary>
    public GameObject SourceObject;

    /// <summary>
    /// 這次復活的流水編號。
    /// </summary>
    public int Sequence;
}

/// <summary>
/// ReviveRequest 被目標處理後的正式結果。
/// </summary>
public struct ReviveResult
{
    /// <summary>
    /// 原始復活要求。
    /// </summary>
    public ReviveRequest Request;

    /// <summary>
    /// 是否成功復活。
    /// </summary>
    public bool Accepted;

    /// <summary>
    /// 復活完成後實際擁有的生命值。
    /// </summary>
    public float RestoredHealth;

    /// <summary>
    /// 被復活目標。
    /// </summary>
    public GameObject TargetObject;

    /// <summary>
    /// 目標 NetworkObject。
    /// </summary>
    public NetworkObject TargetNetworkObject;

    /// <summary>
    /// 被拒絕的原因。
    /// </summary>
    public ReviveRejectReason RejectReason;

    /// <summary>
    /// 建立成功復活結果。
    /// </summary>
    public static ReviveResult CreateApplied(
        ReviveRequest request,
        GameObject targetObject,
        float restoredHealth
    )
    {
        return new ReviveResult
        {
            Request =
                request,

            Accepted =
                true,

            RestoredHealth =
                Mathf.Max(
                    0f,
                    restoredHealth
                ),

            TargetObject =
                targetObject,

            TargetNetworkObject =
                targetObject != null
                    ? targetObject.GetComponentInParent<NetworkObject>()
                    : null,

            RejectReason =
                ReviveRejectReason.None
        };
    }

    /// <summary>
    /// 建立失敗復活結果。
    /// </summary>
    public static ReviveResult CreateRejected(
        ReviveRequest request,
        GameObject targetObject,
        ReviveRejectReason reason
    )
    {
        return new ReviveResult
        {
            Request =
                request,

            Accepted =
                false,

            RestoredHealth =
                0f,

            TargetObject =
                targetObject,

            TargetNetworkObject =
                targetObject != null
                    ? targetObject.GetComponentInParent<NetworkObject>()
                    : null,

            RejectReason =
                reason
        };
    }
}

/// <summary>
/// 生命值實際發生變化後的共用事件資料。
///
/// Damage / Heal / Revive
/// 最後都統一轉成這份資料。
///
/// 未來 Health Bar 不需要知道：
///
/// 這次到底是 AttackRifle
/// Support Heal
/// Revive Skill
///
/// 它只需要知道：
///
/// PreviousHealth
/// CurrentHealth
/// Delta。
/// </summary>
public struct HealthChangeEventData
{
    /// <summary>
    /// 生命值變化類型。
    /// </summary>
    public HealthChangeType ChangeType;

    /// <summary>
    /// 變更前 HP。
    /// </summary>
    public float PreviousHealth;

    /// <summary>
    /// 變更後 HP。
    /// </summary>
    public float CurrentHealth;

    /// <summary>
    /// 最大生命值。
    /// </summary>
    public float MaxHealth;

    /// <summary>
    /// HP 實際變化量。
    ///
    /// 傷害：
    /// 負數。
    ///
    /// 回血：
    /// 正數。
    ///
    /// 復活：
    /// 正數。
    /// </summary>
    public float Delta;

    /// <summary>
    /// 發生生命變化的目標。
    /// </summary>
    public GameObject TargetObject;

    /// <summary>
    /// 造成這次生命變化的 PlayerRef。
    ///
    /// Damage：
    /// Attacker。
    ///
    /// Heal：
    /// Healer。
    ///
    /// Revive：
    /// Reviver。
    /// </summary>
    public PlayerRef Instigator;

    /// <summary>
    /// 對應來源事件 Sequence。
    /// </summary>
    public int Sequence;

    /// <summary>
    /// 變化後是否仍然存活。
    /// </summary>
    public bool IsAlive =>
        CurrentHealth > 0f;
}

/// <summary>
/// 死亡事件資料。
///
/// 目前死亡只會由 DamageResult 觸發。
/// </summary>
public struct CombatDeathEventData
{
    /// <summary>
    /// 死亡目標。
    /// </summary>
    public GameObject TargetObject;

    /// <summary>
    /// 目標 NetworkObject。
    /// </summary>
    public NetworkObject TargetNetworkObject;

    /// <summary>
    /// 真正造成死亡的最後一筆 DamageResult。
    ///
    /// 未來可以知道：
///
/// 誰殺的
/// 用什麼傷害殺的
/// 是不是暴頭
/// 最後一擊多少傷害。
    /// </summary>
    public DamageResult KillingDamage;
}

/// <summary>
/// 復活事件資料。
/// </summary>
public struct CombatReviveEventData
{
    /// <summary>
    /// 被復活目標。
    /// </summary>
    public GameObject TargetObject;

    /// <summary>
    /// 目標 NetworkObject。
    /// </summary>
    public NetworkObject TargetNetworkObject;

    /// <summary>
    /// 正式復活結果。
    /// </summary>
    public ReviveResult ReviveResult;
}

/// <summary>
/// 所有能夠被治療、被復活的戰鬥物件
/// 使用這個標準接口。
///
/// 未來：
///
/// EnemyHealth
/// PlayerHealth
/// BossHealth
///
/// 都可以實作。
/// </summary>
public interface IHealthReceiver
{
    /// <summary>
    /// 接收回血要求。
    /// </summary>
    HealResult ReceiveHeal(
        HealRequest request
    );

    /// <summary>
    /// 接收復活要求。
    /// </summary>
    ReviveResult ReceiveRevive(
        ReviveRequest request
    );
}