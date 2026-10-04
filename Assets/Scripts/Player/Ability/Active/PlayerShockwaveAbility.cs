using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>前方扇波由 Host 命中一次，受控擊退交回 EnemyStateController。</summary>
public sealed class PlayerShockwaveAbility : PlayerActiveAbilityBase
{
    [Header("衝擊扇波")]
    [SerializeField, Min(0f), Tooltip("每名敵人的初次技能傷害；撞牆追加使用相同原始傷害再走正式傷害管線。")]
    private float damage = 40f;
    [SerializeField, Min(0.1f), Tooltip("前方扇波的三維距離，公尺。")]
    private float radius = 5f;
    [SerializeField, Range(0f, 180f), Tooltip("水平扇形半角；60 表示總寬 120 度。")]
    private float halfAngleDegrees = 60f;
    [SerializeField, Min(0f), Tooltip("每次擊退最大距離，公尺；碰到阻擋物提前停止。")]
    private float knockbackDistance = 4f;
    [SerializeField, Min(0.1f), Tooltip("擊退速度，公尺／秒；不使用 Rigidbody 力量。")]
    private float knockbackSpeed = 12f;
    [SerializeField, Min(0f), Tooltip("擊退撞牆才套用的暈眩秒數；單純命中不暈眩。")]
    private float wallStunSeconds = 1f;
    [SerializeField, Tooltip("尋找敵人的 Collider Layer；仍會排除玩家並按 EnemyActor 去重。")]
    private LayerMask hitMask = ~0;
    [SerializeField, Tooltip("阻擋扇波的場景 Layer；建議包含地板與牆壁。自己及目標 Collider 不視為遮擋。")]
    private LayerMask obstructionMask = ~0;

    [Header("可替換本機特效")]
    [SerializeField, Tooltip("確認發動後在各端播放一次的扇波特效；留空不建立特效。Anchor 相對施放位置定位；可實作 IPlayerAbilityLocalVfxReceiver 取得半徑與半角，不決定傷害或擊退。")]
    private PlayerAbilityLocalVfxSlot fireVfx = new PlayerAbilityLocalVfxSlot();
    [SerializeField, Min(0.05f), Tooltip("各端接收本次特效的有效秒數，預設 0.5 秒；逾期的晚加入不重播。只管理呈現事件，不延長傷害或擊退。")]
    private float visualEventSeconds = 0.5f;
    [Networked] private int WaveVisualSequence { get; set; }
    [Networked] private Vector3 WaveOrigin { get; set; }
    [Networked] private Vector3 WaveDirection { get; set; }
    [Networked] private TickTimer WaveVisualTimer { get; set; }
    private int renderedVisualSequence = -1;

    private Collider[] overlaps = new Collider[256];
    private RaycastHit[] sightHits = new RaycastHit[64];
    private readonly HashSet<EnemyActor> visited = new HashSet<EnemyActor>();

    public override void Spawned() => renderedVisualSequence = -1;

    public override void Render()
    {
        if (!Ready || WaveVisualTimer.ExpiredOrNotRunning(Runner) || renderedVisualSequence == WaveVisualSequence) return;
        renderedVisualSequence = WaveVisualSequence;
        Quaternion rotation = WaveDirection.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(WaveDirection) : Quaternion.identity;
        fireVfx?.TryPlay(transform, WaveOrigin, rotation, WaveOrigin + WaveDirection * radius, radius, halfAngleDegrees);
    }

    protected override void Activate()
    {
        Vector3 origin = Owner.Movement.KCC.Data.TargetPosition;
        Vector3 forward = Owner.Movement.KCC.Data.TransformDirection;
        WaveOrigin = origin;
        WaveDirection = forward;
        WaveVisualSequence = ActivationSequence;
        WaveVisualTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, visualEventSeconds));
        PhysicsScene physics = Runner.GetPhysicsScene();
        int count;
        do {
            count = physics.OverlapSphere(origin, radius, overlaps, hitMask, QueryTriggerInteraction.Collide);
            if (count < overlaps.Length) break;
            System.Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (true);
        visited.Clear();
        for (int i = 0; i < count; ++i)
        {
            Collider collider = overlaps[i];
            EnemyActor enemy = collider != null ? collider.GetComponentInParent<EnemyActor>() : null;
            if (enemy == null || !enemy.IsFusionSpawned || !enemy.IsAlive || enemy.Runner != Runner || visited.Contains(enemy)) continue;
            Vector3 delta = enemy.transform.position - origin;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (delta.sqrMagnitude > radius * radius || (horizontal.sqrMagnitude > 0.0001f &&
                Vector3.Angle(forward, horizontal) > halfAngleDegrees)) continue;
            Vector3 targetPoint = collider.bounds.center;
            if (IsObstructed(physics, origin + Vector3.up, targetPoint, enemy)) continue;
            visited.Add(enemy);
            Vector3 direction = horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized : forward;
            DamageRequest request = new DamageRequest
            {
                RequestedDamage = Mathf.Max(0f, damage), BaseDamage = Mathf.Max(0f, damage),
                DamageType = DamageType.Ability, HitZone = DamageHitZoneType.Body,
                HeadshotDamageMultiplier = 1f, Attacker = Owner.Object.InputAuthority,
                SourceNetworkObject = Owner.Object, SourceObject = Owner.gameObject,
                HitObject = collider.gameObject, HitPoint = targetPoint, HitNormal = -direction,
                HitDirection = direction, Distance = delta.magnitude, Sequence = ActivationSequence
            };
            if (DamageReceiverUtility.TryApplyDamage(enemy.gameObject, request, out DamageResult result) && (result.Accepted || result.Deferred))
            {
                Owner.GetComponent<PlayerCombatFeedbackRelay>()?.ReportAbilityDamage(result);
                if (enemy.IsAlive && enemy.StateController != null)
                    enemy.StateController.TryBeginAbilityKnockback(direction, knockbackDistance,
                        knockbackSpeed, wallStunSeconds, request);
            }
        }
        FinishAbility();
    }

    private bool IsObstructed(PhysicsScene physics, Vector3 start, Vector3 target, EnemyActor enemy)
    {
        Vector3 delta = target - start;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return false;
        int count;
        do {
            count = physics.Raycast(start, delta / distance, sightHits, distance, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count < sightHits.Length) break;
            System.Array.Resize(ref sightHits, sightHits.Length * 2);
        } while (true);
        for (int i = 0; i < count; ++i)
        {
            Transform hit = sightHits[i].transform;
            if (hit == null || hit.IsChildOf(Owner.transform) || hit.IsChildOf(enemy.transform)) continue;
            return true;
        }
        return false;
    }
}
