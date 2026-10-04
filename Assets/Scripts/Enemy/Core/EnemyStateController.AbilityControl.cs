using Fusion;
using UnityEngine;

/// <summary>同一 Enemy State owner 的主動技能控制；不新增平行 Movement 元件。</summary>
public sealed partial class EnemyStateController
{
    [Header("主動技能擊退")]
    [SerializeField, Tooltip("是否接受衝擊扇波等技能的擊退。關閉只免疫位移與撞牆效果，初次傷害仍走正式管線。")]
    private bool allowAbilityKnockback = true;
    [SerializeField, Tooltip("受控擊退會碰撞的 Layer；自己 Collider 會排除。只有牆面碰撞追加傷害，腳底接觸不算。")]
    private LayerMask controlObstacleMask = ~0;
    [SerializeField, Min(0.001f), Tooltip("擊退停在障礙前的間距，公尺。")]
    private float controlCollisionSkin = 0.02f;

    [Networked] private float AbilityKnockbackRemaining { get; set; }
    [Networked] private float AbilityKnockbackSpeed { get; set; }
    [Networked] private Vector3 AbilityKnockbackDirection { get; set; }
    [Networked] private TickTimer AbilityStunTimer { get; set; }
    [Networked] private NetworkBool AbilityControlOwned { get; set; }
    [Networked] private NetworkBool AbilityWallHitCommitted { get; set; }
    private DamageRequest abilityWallDamage;
    private float abilityWallStunSeconds;
    private EnemyActor abilityActor;
    private CapsuleCollider abilityBody;
    [Networked] private NetworkBool AbilityMovementStarted { get; set; }
    private RaycastHit[] abilityControlHits = new RaycastHit[64];

    public bool CanReceiveAbilityKnockback
    {
        get
        {
            if (!allowAbilityKnockback) return false;
            foreach (var behaviour in GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.isActiveAndEnabled &&
                    behaviour is IEnemyAbilityKnockbackCondition condition && !condition.CanBeKnockedBack(this))
                    return false;
            return true;
        }
    }
    public bool IsAbilityKnockbackActive => fusionSpawned && AbilityKnockbackRemaining > 0.001f;
    public bool IsAbilityStunned => fusionSpawned && !AbilityStunTimer.ExpiredOrNotRunning(Runner);
    public bool IsTimeFrozen => fusionSpawned && PlayerBulletTimeSessionRegistry.IsEnemyFrozen(ResolveAbilityActor());

    private EnemyActor ResolveAbilityActor()
    {
        if (abilityActor == null) abilityActor = GetComponent<EnemyActor>();
        return abilityActor;
    }

    /// <summary>Host 接受一次受控推退。拒絕搶走既有 Pull／Gather 的位移控制權。</summary>
    public bool TryBeginAbilityKnockback(Vector3 direction, float distance, float speed,
        float wallStunSeconds, DamageRequest wallDamage)
    {
        if (!CanWriteState() || !IsAlive || !CanReceiveAbilityKnockback || distance <= 0f || speed <= 0f) return false;
        var pull = GetComponent<SupportGrapplePullReceiver>();
        var gather = GetComponent<TankGatherMovementReceiver>();
        if ((pull != null && pull.IsPullActive) || (gather != null && gather.IsBeingGathered) ||
            (CurrentControlState != EnemyControlState.Normal && !AbilityControlOwned)) return false;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f) return false;
        if (abilityBody == null) abilityBody = GetComponent<CapsuleCollider>() ?? GetComponentInChildren<CapsuleCollider>();
        AbilityKnockbackRemaining = Mathf.Max(0f, distance);
        AbilityKnockbackSpeed = Mathf.Max(0.1f, speed);
        AbilityKnockbackDirection = direction.normalized;
        AbilityWallHitCommitted = false;
        AbilityStunTimer = TickTimer.None;
        AbilityControlOwned = true;
        abilityWallStunSeconds = Mathf.Max(0f, wallStunSeconds);
        abilityWallDamage = wallDamage;
        if (!IsTimeFrozen) AcquireAbilityMovement();
        return true;
    }

    private void AcquireAbilityMovement()
    {
        if (AbilityMovementStarted) return;
        AbilityMovementStarted = true;
        TrySetControlState(EnemyControlState.ExternalMovement);
        actionGate.SetLocks(EnemyActionLockSource.ExternalMovement, EnemyActionLockFlags.All);
        GetComponent<EnemyCombatDecisionController>()?.CancelForExternalControl();
    }

    private void ResetAbilityControl()
    {
        AbilityMovementStarted = false;
        AbilityKnockbackRemaining = 0f;
        AbilityKnockbackSpeed = 0f;
        AbilityKnockbackDirection = Vector3.zero;
        AbilityStunTimer = TickTimer.None;
        AbilityControlOwned = false;
        AbilityWallHitCommitted = false;
    }

    private void TickAbilityControl()
    {
        if (!AbilityControlOwned) return;
        if (!IsAlive) { ReleaseAbilityControl(); return; }
        if (IsTimeFrozen)
        {
            AbilityStunTimer = ActiveControlRules.PauseTimerForOneTick(AbilityStunTimer, Runner);
            return;
        }
        if (!IsAbilityKnockbackActive)
        {
            if (!IsAbilityStunned) ReleaseAbilityControl();
            return;
        }

        AcquireAbilityMovement();
        var navigator = GetComponent<EnemyPatrolNavigator>();
        float shapeRadius = navigator != null ? navigator.BodyRadius : 0.35f;
        float shapeHeight = navigator != null ? navigator.BodyHeight : 1.8f;
        Bounds bounds = abilityBody != null ? abilityBody.bounds :
            new Bounds(transform.position + Vector3.up * shapeHeight * .5f,
                new Vector3(shapeRadius * 2f, shapeHeight, shapeRadius * 2f));
        float radius = Mathf.Max(0.02f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        float halfSegment = Mathf.Max(0f, bounds.extents.y - radius);
        Vector3 bottom = bounds.center - Vector3.up * halfSegment + Vector3.up * controlCollisionSkin;
        Vector3 top = bounds.center + Vector3.up * halfSegment;
        float requested = Mathf.Min(AbilityKnockbackRemaining, AbilityKnockbackSpeed * Runner.DeltaTime);
        int count;
        do
        {
            count = Runner.GetPhysicsScene().CapsuleCast(bottom, top, Mathf.Max(0.01f, radius - controlCollisionSkin),
                AbilityKnockbackDirection, abilityControlHits, requested + controlCollisionSkin,
                controlObstacleMask, QueryTriggerInteraction.Ignore);
            if (count < abilityControlHits.Length) break;
            System.Array.Resize(ref abilityControlHits, abilityControlHits.Length * 2);
        } while (true);
        float nearest = float.PositiveInfinity;
        RaycastHit obstacle = default;
        for (int i = 0; i < count; ++i)
        {
            RaycastHit hit = abilityControlHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform) ||
                Vector3.Dot(hit.normal, AbilityKnockbackDirection) >= -0.001f) continue;
            if (hit.distance < nearest) { nearest = hit.distance; obstacle = hit; }
        }
        float allowed = ActiveControlRules.LimitTravel(AbilityKnockbackRemaining,
            AbilityKnockbackSpeed, Runner.DeltaTime, nearest, controlCollisionSkin);
        transform.position += AbilityKnockbackDirection * allowed;
        AbilityKnockbackRemaining = Mathf.Max(0f, AbilityKnockbackRemaining - allowed);
        if (nearest <= requested + controlCollisionSkin)
        {
            AbilityKnockbackRemaining = 0f;
            bool worldWall = obstacle.collider != null && obstacle.collider.GetComponentInParent<EnemyActor>() == null &&
                obstacle.collider.GetComponentInParent<Player>() == null &&
                ActiveControlRules.IsWallImpact(obstacle.normal, AbilityKnockbackDirection);
            if (worldWall && !AbilityWallHitCommitted)
            {
                AbilityWallHitCommitted = true; // 先提交旗標，防止傷害回呼重入。
                abilityWallDamage.HitObject = gameObject;
                abilityWallDamage.HitPoint = obstacle.point;
                abilityWallDamage.HitNormal = obstacle.normal;
                if (DamageReceiverUtility.TryApplyDamage(gameObject, abilityWallDamage, out DamageResult result) && result.Accepted)
                    abilityWallDamage.SourceNetworkObject?.GetComponent<PlayerCombatFeedbackRelay>()?.ReportAbilityDamage(result);
                if (IsAlive && abilityWallStunSeconds > 0f)
                {
                    AbilityStunTimer = TickTimer.CreateFromSeconds(Runner, abilityWallStunSeconds);
                    TrySetControlState(EnemyControlState.Stunned);
                }
            }
        }
        if (!IsAbilityKnockbackActive && !IsAbilityStunned) ReleaseAbilityControl();
    }

    private void ReleaseAbilityControl()
    {
        bool owned = AbilityMovementStarted;
        ResetAbilityControl();
        if (!owned) return;
        actionGate.ClearLocks(EnemyActionLockSource.ExternalMovement);
        if (IsAlive && (CurrentControlState == EnemyControlState.ExternalMovement || CurrentControlState == EnemyControlState.Stunned))
            TrySetControlState(EnemyControlState.Normal);
    }
}
