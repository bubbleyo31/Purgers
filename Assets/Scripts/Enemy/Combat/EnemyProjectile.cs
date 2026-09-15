using Fusion;
using UnityEngine;


/// <summary>
/// 遠程攻擊 A 的權威直線 Projectile。
///
/// State Authority 以 SphereCast 推進並套用傷害；NetworkTransform 同步位置。
/// 命中後短暫保留 NetworkObject，讓所有端有時間播放 Impact 呈現。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public sealed class EnemyProjectile :
    NetworkBehaviour
{
    [Header("Projectile 碰撞")]

    [SerializeField]
    [Tooltip("必須包含 Player 與場景實體，不可包含 Enemy Layer。")]
    private LayerMask hitMask;

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("每 Tick 使用 SphereCast 的半徑，公尺。比單點 Raycast 更不容易穿過細小目標。")]
    private float collisionRadius =
        0.08f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("命中後保留網路物件幾秒再 Despawn，供 Impact VFX 觀察。0 仍至少保留到下一個 Tick。")]
    private float impactLifetimeSeconds =
        0.08f;

    [Header("Projectile 呈現")]

    [SerializeField]
    [Tooltip("飛行中顯示的模型／Trail Root。命中後會在各端本地關閉。可留空。")]
    private GameObject flyingVisualRoot;

    [SerializeField]
    [Tooltip("命中後顯示的 Impact VFX Root。預設關閉；可留空。此物件不要另外掛 NetworkObject。")]
    private GameObject impactVisualRoot;

    [Networked]
    public NetworkObject OwnerEnemy
    {
        get;
        private set;
    }

    [Networked]
    public Vector3 TravelDirection
    {
        get;
        private set;
    }

    [Networked]
    public float TravelSpeed
    {
        get;
        private set;
    }

    [Networked]
    public float Damage
    {
        get;
        private set;
    }

    [Networked]
    public int DamageSequence
    {
        get;
        private set;
    }

    [Networked]
    public TickTimer LifetimeTimer
    {
        get;
        private set;
    }

    [Networked]
    public bool HasImpacted
    {
        get;
        private set;
    }

    [Networked]
    private TickTimer ImpactTimer
    {
        get;
        set;
    }

    private bool visualStateInitialized;
    private bool previousImpactState;

    /// <summary>
    /// 必須由 Runner.Spawn 的 onBeforeSpawned 呼叫。
    /// </summary>
    public void Initialize(
        NetworkRunner runner,
        NetworkObject ownerEnemy,
        Vector3 direction,
        float speed,
        float damage,
        float lifetimeSeconds,
        int damageSequence
    )
    {
        OwnerEnemy = ownerEnemy;
        TravelDirection =
            direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : transform.forward;
        TravelSpeed = Mathf.Max(0.01f, speed);
        Damage = Mathf.Max(0f, damage);
        DamageSequence = damageSequence;
        LifetimeTimer = TickTimer.CreateFromSeconds(
            runner,
            Mathf.Max(0.02f, lifetimeSeconds)
        );
        HasImpacted = false;
        ImpactTimer = TickTimer.None;
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority == false)
        {
            return;
        }

        if (HasImpacted)
        {
            if (ImpactTimer.ExpiredOrNotRunning(Runner))
            {
                Runner.Despawn(Object);
            }

            return;
        }

        if (LifetimeTimer.ExpiredOrNotRunning(Runner))
        {
            BeginImpact(transform.position);
            return;
        }

        float distance =
            TravelSpeed * Runner.DeltaTime;

        if (Runner.GetPhysicsScene().SphereCast(
                transform.position,
                collisionRadius,
                TravelDirection,
                out RaycastHit hit,
                distance,
                hitMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            transform.position = hit.point;
            TryApplyPlayerDamage(hit);
            BeginImpact(hit.point);
            return;
        }

        transform.position +=
            TravelDirection * distance;
    }

    public override void Render()
    {
        if (visualStateInitialized == false ||
            previousImpactState != HasImpacted)
        {
            previousImpactState = HasImpacted;
            visualStateInitialized = true;

            if (flyingVisualRoot != null)
            {
                flyingVisualRoot.SetActive(HasImpacted == false);
            }

            if (impactVisualRoot != null)
            {
                impactVisualRoot.SetActive(HasImpacted);
            }
        }
    }

    private void TryApplyPlayerDamage(
        RaycastHit hit
    )
    {
        PlayerHealth health =
            hit.collider != null
                ? hit.collider.GetComponentInParent<PlayerHealth>()
                : null;

        EnemyActor source =
            OwnerEnemy != null && OwnerEnemy.IsValid
                ? OwnerEnemy.GetComponent<EnemyActor>()
                : null;

        if (health == null ||
            health.Object == null ||
            source == null)
        {
            return;
        }

        EnemyDamageUtility.TryDamagePlayer(
            source,
            health.Object,
            hit.collider.gameObject,
            hit.point,
            hit.normal,
            TravelDirection,
            Damage,
            DamageType.Bullet,
            DamageSequence,
            out _
        );
    }

    private void BeginImpact(
        Vector3 point
    )
    {
        HasImpacted = true;
        transform.position = point;
        TravelSpeed = 0f;
        ImpactTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    Runner.DeltaTime,
                    impactLifetimeSeconds
                )
            );
    }

    private void OnValidate()
    {
        collisionRadius = Mathf.Max(0.001f, collisionRadius);
        impactLifetimeSeconds = Mathf.Max(0f, impactLifetimeSeconds);
    }
}
