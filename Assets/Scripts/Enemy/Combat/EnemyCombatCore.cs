using Fusion;
using UnityEngine;


/// <summary>
/// 穩定的 Enemy Combat Option ID。
/// 已使用的數值日後不要交換，Animator 與網路狀態會依賴它。
/// </summary>
public enum EnemyCombatOptionId : byte
{
    None = 0,
    MeleeAttackA = 1,
    MeleeAttackB = 2,
    RangedAttackA = 3,
    RangedAttackB = 4,
    DefenseA = 10,
    DefenseB = 11
}


/// <summary>
/// 攻擊的權威階段。傷害時間由這個狀態與 TickTimer 決定，不使用 Animation Event。
/// </summary>
public enum EnemyCombatActionPhase : byte
{
    None = 0,
    Startup = 1,
    Active = 2,
    Braking = 3,
    Recovery = 4,
    Tracking = 5,
    LockedDelay = 6
}


public enum EnemyCombatOptionTickResult : byte
{
    Running = 0,
    Completed = 1,
    Cancelled = 2
}


/// <summary>
/// Combat Brain 每 Tick 傳給能力的快照。
/// </summary>
public readonly struct EnemyCombatContext
{
    public readonly EnemyCombatDecisionController Controller;
    public readonly EnemyActor Actor;
    public readonly EnemyPerceptionController Perception;
    public readonly NetworkObject Target;
    public readonly Vector3 TargetPosition;
    public readonly float Distance;
    public readonly float DeltaTime;

    public EnemyCombatContext(
        EnemyCombatDecisionController controller,
        EnemyActor actor,
        EnemyPerceptionController perception,
        NetworkObject target,
        Vector3 targetPosition,
        float distance,
        float deltaTime
    )
    {
        Controller = controller;
        Actor = actor;
        Perception = perception;
        Target = target;
        TargetPosition = targetPosition;
        Distance = distance;
        DeltaTime = deltaTime;
    }
}


/// <summary>
/// 所有攻擊與未來防禦共用的選項基底。
///
/// 基底不含 Networked Property，讓每個具體能力自行保存所需 TickTimer；
/// EnemyCombatDecisionController 是唯一選擇與切換 Action 的元件。
/// </summary>
public abstract class EnemyCombatOption : NetworkBehaviour
{
    [Header("戰鬥選項")]

    [SerializeField]
    [Tooltip(
        "此能力的穩定 ID。\n" +
        "近戰 A/B、遠程 A/B 必須設定成對應項目；同一 Enemy Prefab 不可重複。")]
    private EnemyCombatOptionId optionId;

    [SerializeField]
    [Range(-100, 100)]
    [Tooltip(
        "多個能力同時可用時的選擇優先度，數值越大越先執行。\n" +
        "目前每個 Variant 只有一個攻擊；未來防禦也會走同一個選擇器。")]
    private int priority;

    [SerializeField]
    [Min(0f)]
    [Tooltip("能力允許開始的最小三維距離，公尺。0 代表沒有最小距離限制。")]
    private float minimumStartDistance;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("能力允許開始的最大三維距離，公尺。必須大於等於最小距離。")]
    private float maximumStartDistance =
        2f;

    [SerializeField]
    [Tooltip(
        "開啟後，能力開始時必須親眼看見目標。\n" +
        "共享警戒或最後位置記憶只能用於追逐，不能隔牆發動攻擊。")]
    private bool requiresDirectSight =
        true;

    [Header("戰鬥距離 Gizmos")]

    [SerializeField]
    [Tooltip("選取敵人時，在 Scene 視窗顯示此戰鬥選項的最小／最大啟動距離。")]
    private bool drawAttackRangeGizmos =
        true;

    [SerializeField]
    [Tooltip("最大攻擊啟動距離的 Gizmos 顏色。")]
    private Color maximumRangeGizmoColor =
        new Color(1f, 0.55f, 0f, 0.85f);

    [SerializeField]
    [Tooltip("最小攻擊啟動距離的 Gizmos 顏色。只有 Minimum Start Distance 大於 0 時才顯示。")]
    private Color minimumRangeGizmoColor =
        new Color(0.2f, 0.75f, 1f, 0.85f);

    public EnemyCombatOptionId OptionId =>
        optionId;

    public int Priority =>
        priority;

    public float MinimumStartDistance =>
        minimumStartDistance;

    public float MaximumStartDistance =>
        maximumStartDistance;

    public abstract EnemyActionState ActionState
    {
        get;
    }

    public abstract EnemyActionLockFlags LocksWhileActive
    {
        get;
    }

    public abstract bool IsCooldownReady(
        NetworkRunner runner
    );

    public bool CanStart(
        in EnemyCombatContext context
    )
    {
        if (context.Target == null ||
            context.Target.IsValid == false ||
            context.Distance < minimumStartDistance ||
            context.Distance > maximumStartDistance ||
            (requiresDirectSight &&
             context.Perception.HasDirectSight == false) ||
            IsCooldownReady(context.Actor.Runner) == false)
        {
            return false;
        }

        return CanStartOption(context);
    }

    public abstract bool CanStartOption(
        in EnemyCombatContext context
    );

    public abstract void BeginOption(
        in EnemyCombatContext context
    );

    public abstract EnemyCombatOptionTickResult TickOption(
        in EnemyCombatContext context
    );

    public abstract void CancelOption(
        in EnemyCombatContext context
    );

    /// <summary>
    /// 所有攻擊與未來防禦共用的距離視覺化。
    /// 邏輯使用三維距離，所以這裡刻意畫球形，不畫容易誤解成平面的圓。
    /// </summary>
    protected virtual void OnDrawGizmosSelected()
    {
        if (drawAttackRangeGizmos == false)
        {
            return;
        }

        Vector3 center = transform.position;

        if (maximumStartDistance > 0f)
        {
            Gizmos.color = maximumRangeGizmoColor;
            Gizmos.DrawWireSphere(
                center,
                maximumStartDistance
            );
        }

        if (minimumStartDistance > 0f)
        {
            Gizmos.color = minimumRangeGizmoColor;
            Gizmos.DrawWireSphere(
                center,
                minimumStartDistance
            );
        }
    }

    protected virtual void OnValidate()
    {
        minimumStartDistance =
            Mathf.Max(0f, minimumStartDistance);

        maximumStartDistance =
            Mathf.Max(
                minimumStartDistance,
                maximumStartDistance
            );
    }
}


/// <summary>
/// 敵人攻擊寫入既有 Damage Pipeline 的唯一工具。
/// </summary>
public static class EnemyDamageUtility
{
    public static bool TryDamagePlayer(
        EnemyActor source,
        NetworkObject target,
        GameObject hitObject,
        Vector3 hitPoint,
        Vector3 hitNormal,
        Vector3 hitDirection,
        float damage,
        DamageType damageType,
        int sequence,
        out DamageResult result
    )
    {
        result = default;

        if (source == null ||
            source.Object == null ||
            source.Object.IsValid == false ||
            source.Object.HasStateAuthority == false ||
            target == null ||
            target.IsValid == false ||
            damage <= 0f)
        {
            return false;
        }

        PlayerHealth health =
            target.GetComponent<PlayerHealth>();

        if (health == null)
        {
            health =
                target.GetComponentInChildren<PlayerHealth>(true);
        }

        if (health == null ||
            health.Object == null ||
            health.Object.IsValid == false ||
            health.IsAlive == false)
        {
            return false;
        }

        Vector3 normalizedDirection =
            hitDirection.sqrMagnitude > 0.0001f
                ? hitDirection.normalized
                : Vector3.forward;

        GameObject resolvedHitObject =
            hitObject != null
                ? hitObject
                : health.gameObject;

        DamageRequest request =
            new DamageRequest
            {
                RequestedDamage = damage,
                BlockedDamage = 0f,
                BaseDamage = damage,
                DamageType = damageType,
                FeedbackId = CombatFeedbackId.None,
                HitZone = DamageHitZoneType.Body,
                Attacker = PlayerRef.None,
                SourceNetworkObject = source.Object,
                SourceObject = source.gameObject,
                HitObject = resolvedHitObject,
                HitPoint = hitPoint,
                HitNormal = hitNormal,
                HitDirection = normalizedDirection,
                Distance = Vector3.Distance(
                    source.transform.position,
                    hitPoint
                ),
                Sequence = sequence,
                HeadshotDamageMultiplier = 1f,
                ForcedHeadshotSource =
                    DamageForcedHeadshotSource.None
            };

        return DamageReceiverUtility.TryApplyDamage(
            health.gameObject,
            request,
            out result
        );
    }
}
