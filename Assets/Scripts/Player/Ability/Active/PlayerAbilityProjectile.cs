using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>玩家技能投射物：Host 掃掠碰撞、正式傷害與拾取；NetworkTransform 同步位置。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
public sealed class PlayerAbilityProjectile : NetworkBehaviour
{
    [Header("命中本機呈現")]
    [SerializeField, Min(0.05f), Tooltip("榴彈爆炸／彈射命中後保留網路物件的秒數，供各端呈現範圍線環。傷害不等待此時間。")]
    private float impactVisibleSeconds = 0.25f;
    [SerializeField, Tooltip("爆炸與命中線環顏色；只控制本機呈現。")]
    private Color impactColor = new Color(1f, 0.65f, 0.12f, 1f);
    [SerializeField, Tooltip("榴彈爆炸／彈射彈命中的本機特效插槽；未指定 Prefab 時保留示意線環。各投射物 Prefab 可各自設定，傷害仍立即由 Host 結算。")]
    private PlayerAbilityLocalVfxSlot impactVfx = new PlayerAbilityLocalVfxSlot();
    [Networked] public NetworkObject OwnerPlayerObject { get; private set; }
    [Networked] public NetworkObject LauncherObject { get; private set; }
    [Networked] public PlayerAbilityProjectileKind Kind { get; private set; }
    [Networked] public Vector3 Velocity { get; private set; }
    [Networked] public bool IsResting { get; private set; }
    [Networked] public int BounceCount { get; private set; }
    [Networked] public bool HasImpacted { get; private set; }
    [Networked] private Vector3 ImpactPosition { get; set; }
    [Networked] private float ImpactRadius { get; set; }
    [Networked] private TickTimer ImpactTimer { get; set; }
    [Networked] private TickTimer Lifetime { get; set; }

    private float gravity, radius, damage, blastRadius, pickupRadius;
    private int throwLevel, sequence;
    private LayerMask mask;
    private bool clearedOwner, finished;
    private RaycastHit[] hits = new RaycastHit[16];
    private RaycastHit[] visibilityHits = new RaycastHit[16];
    private Collider[] overlaps = new Collider[16];
    private readonly HashSet<MonoBehaviour> damagedReceivers = new HashSet<MonoBehaviour>();
    private readonly HashSet<MonoBehaviour> ignoredReceivers = new HashSet<MonoBehaviour>();
    private LineRenderer impactRing;
    private Material impactMaterial;
    private PlayerAbilityProjectilePresentation presentation;
    private float presentationStartedAt;
    private bool impactPresented, customImpactPresented;
    private Renderer[] hiddenImpactRenderers;
    private Player Owner => OwnerPlayerObject != null && OwnerPlayerObject.IsValid
        ? OwnerPlayerObject.GetComponent<Player>() : null;

#if UNITY_EDITOR
    [Header("技能投射物 Gizmos（僅 Editor 顯示）")]
    [SerializeField] private ActiveAbilityGizmoSettings rangeGizmos = new ActiveAbilityGizmoSettings();
    private bool gizmoSpawned;
    public Player GizmoOwner => gizmoSpawned && Object != null && Object.IsValid ? Owner : null;
    public ActiveAbilityGizmoSettings RangeGizmos
    {
        get
        {
            if (gizmoSpawned && Object != null && Object.IsValid && LauncherObject != null && LauncherObject.IsValid)
            {
                var launcher = LauncherObject.GetComponent<PlayerActiveAbilityBase>();
                if (launcher != null) return launcher.RangeGizmos;
            }
            return rangeGizmos ?? (rangeGizmos = new ActiveAbilityGizmoSettings());
        }
    }
    public bool TryGetGizmoState(out ActiveAbilityProjectileGizmoState state)
    {
        state = default;
        // 物理參數只有 Authority Initialize 提供；Proxy/Prefab 不假造半徑。
        if (!gizmoSpawned || Object == null || !Object.IsValid || !Object.HasStateAuthority) return false;
        state = new ActiveAbilityProjectileGizmoState {
            Kind = Kind, Position = HasImpacted ? ImpactPosition : transform.position, Velocity = Velocity,
            CollisionRadius = radius, EffectRadius = Kind == PlayerAbilityProjectileKind.Grenade ?
                (HasImpacted ? ImpactRadius : blastRadius) : Kind == PlayerAbilityProjectileKind.HealingPack ? pickupRadius : 0f,
            Gravity = gravity, RemainingSeconds = Lifetime.RemainingTime(Runner) ?? 0f,
            CollisionMask = mask.value, Bounces = BounceCount, Resting = IsResting, Impacted = HasImpacted,
            Owner = !clearedOwner && OwnerPlayerObject != null ? OwnerPlayerObject.transform : null, Scene = Runner.GetPhysicsScene()
        };
        return true;
    }
    public override void Despawned(NetworkRunner runner, bool hasState) => gizmoSpawned = false;
#endif

    public override void Spawned()
    {
#if UNITY_EDITOR
        gizmoSpawned = true;
#endif
        presentation = GetComponent<PlayerAbilityProjectilePresentation>();
        presentation?.ResetPresentation();
        presentationStartedAt = Time.time;
        impactPresented = customImpactPresented = false;
        if (impactRing != null) impactRing.enabled = false;
        if (hiddenImpactRenderers != null)
            foreach (var visual in hiddenImpactRenderers) if (visual != null) visual.enabled = true;
        hiddenImpactRenderers = null;
    }

    /// <summary>只由 Runner.Spawn 的 onBeforeSpawned 設定；等級為投出瞬間快照。</summary>
    public void Initialize(NetworkRunner runner, NetworkObject owner, NetworkObject launcher,
        PlayerAbilityProjectileKind kind, Vector3 velocity, float downwardGravity, float collisionRadius,
        LayerMask collisionMask, float baseDamage, float explosionRadius, float healPickupRadius,
        float lifetimeSeconds, int playerLevel, int activationSequence)
    {
        OwnerPlayerObject = owner;
        LauncherObject = launcher;
        Kind = kind;
        Velocity = velocity;
        gravity = Mathf.Max(0f, downwardGravity);
        radius = Mathf.Max(0.001f, collisionRadius);
        mask = collisionMask;
        damage = Mathf.Max(0f, baseDamage);
        blastRadius = Mathf.Max(0.1f, explosionRadius);
        pickupRadius = Mathf.Max(radius, healPickupRadius);
        throwLevel = Mathf.Max(1, playerLevel);
        sequence = activationSequence;
        Lifetime = TickTimer.CreateFromSeconds(runner, Mathf.Max(0.1f, lifetimeSeconds));
        IsResting = false;
        BounceCount = 0;
        HasImpacted = false;
        ImpactTimer = TickTimer.None;
        clearedOwner = false;
        finished = false;
        damagedReceivers.Clear();
        ignoredReceivers.Clear();
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        if (HasImpacted)
        {
            if (ImpactTimer.ExpiredOrNotRunning(Runner)) Runner.Despawn(Object);
            return;
        }
        if (finished) return;
        if (Owner == null || Lifetime.ExpiredOrNotRunning(Runner)) { Finish(); return; }
        PhysicsScene scene = Runner.GetPhysicsScene();
        UpdateOwnerClearance(scene);
        if (Kind == PlayerAbilityProjectileKind.HealingPack && TryPickup(scene)) return;
        if (IsResting) return;

        // 向量積分後做掃掠，長 Tick 分段避免拋物線跨牆角。
        float remainingTime = Runner.DeltaTime;
        while (remainingTime > 0.000001f && !finished && !IsResting)
        {
            float dt = Mathf.Min(remainingTime, 0.02f);
            remainingTime -= dt;
            Vector3 acceleration = Kind == PlayerAbilityProjectileKind.Ricochet ? Vector3.zero : Vector3.down * gravity;
            Vector3 displacement = Velocity * dt + acceleration * (0.5f * dt * dt);
            Velocity += acceleration * dt;
            Sweep(scene, displacement);
            if (!finished) UpdateOwnerClearance(scene);
        }
    }

    private void UpdateOwnerClearance(PhysicsScene scene)
    {
        if (clearedOwner) return;
        float clearanceRadius = Kind == PlayerAbilityProjectileKind.HealingPack ? pickupRadius : radius;
        int count = ActiveAbilityPhysics.OverlapSphere(scene, transform.position, clearanceRadius, mask, ref overlaps);
        for (int i = 0; i < count; i++)
            if (ActiveAbilityPhysics.BelongsTo(overlaps[i], OwnerPlayerObject)) return;
        clearedOwner = true;
    }

    private void Sweep(PhysicsScene scene, Vector3 displacement)
    {
        float remaining = displacement.magnitude;
        if (remaining < 0.000001f) return;
        Vector3 direction = displacement / remaining;
        // 最多三次反彈，加一次終止碰撞；被免傷目標略過不會消耗反彈次數。
        while (remaining > 0.000001f && !finished && !IsResting)
        {
            UpdateOwnerClearance(scene);
            if (TryInitialOverlap(scene, direction)) return;
            UpdateOwnerClearance(scene);
            if (Kind == PlayerAbilityProjectileKind.Ricochet) direction = Velocity.normalized;
            int count = ActiveAbilityPhysics.SphereCastSorted(scene, transform.position, direction,
                remaining, radius, mask, clearedOwner ? null : OwnerPlayerObject, ref hits);
            RaycastHit hit = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (ActiveAbilityPhysics.BelongsTo(hits[i].collider, Object)) continue;
                var receiver = ActiveAbilityPhysics.ResolveReceiver(hits[i].collider.gameObject);
                if (receiver != null && ignoredReceivers.Contains(receiver)) continue;
                hit = hits[i]; found = true; break;
            }
            if (!found) { transform.position += direction * remaining; return; }
            float traveled = Mathf.Clamp(hit.distance, 0f, remaining);
            transform.position += direction * traveled;
            // 同 Tick 近牆往返仍須能命中施放者，不能等整段 Sweep 結束才解除出生排除。
            UpdateOwnerClearance(scene);
            remaining -= traveled;
            if (HandleContact(hit.collider, hit.point, hit.normal, direction, scene)) return;
            if (Kind == PlayerAbilityProjectileKind.Ricochet)
                direction = Velocity.normalized;
            // 只往外推極小皮膚距離，避免同一個面在距離 0 重複反彈。
            float skin = Mathf.Min(0.002f, remaining);
            transform.position += direction * skin;
            remaining -= skin;
        }
    }

    private bool TryInitialOverlap(PhysicsScene scene, Vector3 direction)
    {
        int count = ActiveAbilityPhysics.OverlapSphere(scene, transform.position, radius * 0.99f, mask, ref overlaps);
        for (int i = 0; i < count; i++)
        {
            var collider = overlaps[i];
            if (collider == null || ActiveAbilityPhysics.BelongsTo(collider, Object) ||
                (!clearedOwner && ActiveAbilityPhysics.BelongsTo(collider, OwnerPlayerObject))) continue;
            var receiver = ActiveAbilityPhysics.ResolveReceiver(collider.gameObject);
            if (receiver != null && ignoredReceivers.Contains(receiver)) continue;
            Vector3 point = collider.ClosestPoint(transform.position);
            Vector3 normal = transform.position - point;
            // 出生在牆內時沒有可靠反射面：保守終止，不把投射物瞬移到牆另一側。
            if (normal.sqrMagnitude < 0.0000001f && receiver == null)
            {
                if (Kind == PlayerAbilityProjectileKind.HealingPack) { IsResting = true; Velocity = Vector3.zero; }
                else if (Kind == PlayerAbilityProjectileKind.Grenade) Explode(transform.position, scene);
                else Finish();
                return true;
            }
            if (normal.sqrMagnitude < 0.0000001f) normal = -direction;
            normal.Normalize();
            bool stopped = HandleContact(collider, point, normal, direction, scene);
            if (!stopped && Kind == PlayerAbilityProjectileKind.Ricochet && receiver == null)
                transform.position = point + normal * (radius + 0.002f);
            return stopped;
        }
        return false;
    }

    private bool HandleContact(Collider collider, Vector3 point, Vector3 normal, Vector3 direction, PhysicsScene scene)
    {
        if (Kind == PlayerAbilityProjectileKind.Grenade)
        {
            Explode(point + normal * 0.015f, scene);
            return true;
        }
        if (Kind == PlayerAbilityProjectileKind.HealingPack)
        {
            if (TryHeal(collider)) return true;
            IsResting = true;
            Velocity = Vector3.zero;
            return true;
        }
        var receiver = ActiveAbilityPhysics.ResolveReceiver(collider.gameObject);
        if (receiver != null)
        {
            var result = ActiveAbilityPhysics.ApplyDamage(Owner, collider.gameObject, point, normal, direction,
                0f, ActiveAbilityRules.RicochetDamage(damage, BounceCount), DamageType.Ability, sequence);
            if (result.HasEffectiveDamage || result.Deferred) { Finish(true, point, 0.3f); return true; }
            // 無敵／完全格擋未造成傷害：不反覆提交同一顆子彈給同一接收者。
            ignoredReceivers.Add(receiver);
            return false;
        }
        if (BounceCount >= 3) { Finish(); return true; }
        BounceCount++;
        Velocity = Vector3.Reflect(Velocity, normal);
        transform.rotation = Quaternion.LookRotation(Velocity.normalized);
        return false;
    }

    private void Explode(Vector3 center, PhysicsScene scene)
    {
        int count = ActiveAbilityPhysics.OverlapSphere(scene, center, blastRadius, mask, ref overlaps);
        damagedReceivers.Clear();
        for (int i = 0; i < count; i++)
        {
            var collider = overlaps[i];
            if (collider == null) continue;
            var receiver = ActiveAbilityPhysics.ResolveReceiver(collider.gameObject);
            if (receiver == null || damagedReceivers.Contains(receiver)) continue;
            Vector3 point = collider.ClosestPoint(center);
            if (ActiveAbilityPhysics.IsWorldOccluded(scene, center, point, mask, ref visibilityHits)) continue;
            damagedReceivers.Add(receiver);
            var health = receiver as PlayerHealth;
            float multiplier = health != null && health.Object != OwnerPlayerObject ? 0.35f : 1f;
            Vector3 direction = point - center;
            ActiveAbilityPhysics.ApplyDamage(Owner, collider.gameObject, point, -direction.normalized,
                direction, direction.magnitude, damage * multiplier, DamageType.Explosion, sequence);
        }
        Finish(true, center, blastRadius);
    }

    private bool TryPickup(PhysicsScene scene)
    {
        int count = ActiveAbilityPhysics.OverlapSphere(scene, transform.position, pickupRadius, mask, ref overlaps);
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (collider == null) continue;
            // 拾取半徑不可跨過薄牆。
            if (ActiveAbilityPhysics.IsWorldOccluded(scene, transform.position,
                collider.ClosestPoint(transform.position), mask, ref visibilityHits)) continue;
            if (TryHeal(collider)) return true;
        }
        return false;
    }

    private bool TryHeal(Collider collider)
    {
        var health = collider != null ? collider.GetComponentInParent<PlayerHealth>() : null;
        if (health == null || health.Object == null || !health.Object.IsValid || health.Runner != Runner ||
            !health.IsAlive || (!clearedOwner && !IsResting && health.Object == OwnerPlayerObject)) return false;
        if (!health.RestoreHealth(ActiveAbilityRules.HealingAmount(throwLevel, health.Object == OwnerPlayerObject), out float applied) || applied <= 0f)
            return false;
        Finish();
        return true;
    }

    private void Finish(bool showImpact = false, Vector3 impactPosition = default, float impactRadius = 0f)
    {
        if (finished) return;
        finished = true;
        if (LauncherObject != null && LauncherObject.IsValid)
            LauncherObject.GetComponent<PlayerProjectileAbility>()?.NotifyProjectileFinished(Object);
        if (!showImpact) { Runner.Despawn(Object); return; }
        HasImpacted = true;
        ImpactPosition = impactPosition;
        ImpactRadius = impactRadius;
        Velocity = Vector3.zero;
        ImpactTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, impactVisibleSeconds));
    }

    public override void Render()
    {
        if (Object == null || !Object.IsValid) return;
        presentation?.ApplyVisualState(Kind, Velocity, IsResting, HasImpacted,
            Time.time - presentationStartedAt, Time.deltaTime);
        if (!HasImpacted) return;
        if (!impactPresented)
        {
            impactPresented = true;
            // 僅隱藏目前開啟的外觀，不改動原本刻意關閉的 Renderer；重用時恢復同一份清單。
            var hidden = new List<Renderer>();
            foreach (var visual in GetComponentsInChildren<Renderer>())
                if (visual != impactRing && visual.enabled) { visual.enabled = false; hidden.Add(visual); }
            hiddenImpactRenderers = hidden.ToArray();
            customImpactPresented = impactVfx != null && impactVfx.TryPlay(transform, ImpactPosition,
                Quaternion.identity, ImpactPosition, ImpactRadius);
        }
        if (customImpactPresented) return;
        if (impactRing == null)
        {
            var ringObject = new GameObject("Ability Impact Ring (Local)");
            ringObject.transform.SetParent(transform, false);
            impactRing = ringObject.AddComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) { impactMaterial = new Material(shader); impactRing.sharedMaterial = impactMaterial; }
            impactRing.useWorldSpace = true;
            impactRing.loop = true;
            impactRing.positionCount = 40;
            impactRing.startWidth = impactRing.endWidth = 0.06f;
        }
        impactRing.enabled = true;
        float remaining = ImpactTimer.RemainingTime(Runner) ?? 0f;
        float progress = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.05f, impactVisibleSeconds));
        Color color = impactColor;
        color.a *= 1f - progress;
        impactRing.startColor = impactRing.endColor = color;
        float displayedRadius = ImpactRadius * Mathf.Lerp(0.25f, 1f, progress);
        for (int i = 0; i < impactRing.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / impactRing.positionCount;
            impactRing.SetPosition(i, ImpactPosition + new Vector3(Mathf.Cos(angle), 0.015f, Mathf.Sin(angle)) * displayedRadius);
        }
    }

    private void OnDestroy()
    {
        if (impactMaterial != null) Destroy(impactMaterial);
    }
}
