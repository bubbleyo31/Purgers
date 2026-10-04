using Fusion;
using UnityEngine;

public enum PlayerAbilityProjectileKind : byte { Grenade = 0, HealingPack = 1, Ricochet = 2 }

/// <summary>三種投射技能的獨立 Runtime；投出時取樣準星及等級。</summary>
public sealed class PlayerProjectileAbility : PlayerActiveAbilityBase
{
    [Header("投射物與施放")]
    [SerializeField, Tooltip("投射物模式。爆破榴彈接觸爆炸、治療包停留待拾取、彈射彈最多反彈三次。")]
    private PlayerAbilityProjectileKind projectileKind;
    [SerializeField, Tooltip("需含 NetworkObject、NetworkTransform、PlayerAbilityProjectile 的已註冊網路 Prefab；不可使用一般 Instantiate。")]
    private PlayerAbilityProjectile projectilePrefab;
    [SerializeField, Min(0f), Tooltip("發射點在玩家 KCC 座標上方的高度，公尺；向準星投出，不向前穿過牆壁偏移。")]
    private float originHeight = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("投出初速，公尺／秒。榴彈與治療包受重力影響，彈射彈維持直線速度。")]
    private float projectileSpeed = 18f;
    [SerializeField, Min(0f), Tooltip("榴彈與治療包向下加速度，公尺／秒平方；0 為直線。彈射彈忽略此值。")]
    private float gravity = 18f;
    [SerializeField, Min(0.001f), Tooltip("權威掃掠球半徑，公尺；應對應投射物可見尺寸，避免高速穿牆。")]
    private float projectileRadius = 0.1f;
    [SerializeField, Tooltip("投射碰撞與爆炸遮擋使用的實體 Layer；需包含牆壁、Enemy 和 Player，不包含純視覺或 Trigger。")]
    private LayerMask collisionMask = ~0;
    [SerializeField, Min(0.1f), Tooltip("榴彈／彈射彈最大存活秒數，逾時直接消失。治療包固定投出後保留 10 秒。")]
    private float projectileLifetime = 8f;

    [Header("傷害與範圍")]
    [SerializeField, Min(0f), Tooltip("榴彈基礎爆炸傷害，或彈射彈未反彈傷害。彈射每次翻倍，最多三次。")]
    private float baseDamage = 40f;
    [SerializeField, Min(0.1f), Tooltip("榴彈爆炸半徑，公尺；範圍內無距離衰減，且受實體牆阻擋。")]
    private float explosionRadius = 4f;
    [SerializeField, Min(0.01f), Tooltip("治療包玩家觸碰半徑，公尺；滿血不消耗。治療值採投出時等級，自己每級 5、隊友每級 10。")]
    private float pickupRadius = 0.4f;

    [Networked] private NetworkObject OutstandingProjectile { get; set; }

    protected override bool CanBegin(NetInput input) => projectilePrefab != null &&
        (OutstandingProjectile == null || !OutstandingProjectile.IsValid);

    protected override void Activate()
    {
        if (!Authority || projectilePrefab == null) { FinishAbility(); return; }
        Vector3 direction = Owner.Movement != null ? Owner.Movement.GetAimDirection() : Owner.transform.forward;
        if (direction.sqrMagnitude < 0.000001f) direction = Owner.transform.forward;
        direction.Normalize();
        Vector3 origin = ActiveAbilityPhysics.ShotOrigin(Owner, originHeight);
        var spawned = Runner.Spawn(projectilePrefab.GetComponent<NetworkObject>(), origin,
            Quaternion.LookRotation(direction), Owner.Object.InputAuthority,
            (runner, obj) => obj.GetComponent<PlayerAbilityProjectile>().Initialize(runner, Owner.Object,
                Object, projectileKind, direction * Mathf.Max(0.1f, projectileSpeed), gravity,
                projectileRadius, collisionMask, baseDamage, explosionRadius, pickupRadius,
                projectileKind == PlayerAbilityProjectileKind.HealingPack ? 10f : projectileLifetime,
                PlayerLevel, ActivationSequence));
        if (projectileKind == PlayerAbilityProjectileKind.HealingPack && spawned != null)
        {
            OutstandingProjectile = spawned;
            Phase = PlayerActiveAbilityPhase.AwaitingPickup;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, 10f);
        }
        else FinishAbility();
    }

    protected override void TickAbility()
    {
        if (Phase == PlayerActiveAbilityPhase.AwaitingPickup &&
            (OutstandingProjectile == null || !OutstandingProjectile.IsValid)) FinishAbility();
    }

    public void NotifyProjectileFinished(NetworkObject projectile)
    {
        if (!Authority || Phase != PlayerActiveAbilityPhase.AwaitingPickup || OutstandingProjectile != projectile) return;
        OutstandingProjectile = null;
        FinishAbility();
    }
}
