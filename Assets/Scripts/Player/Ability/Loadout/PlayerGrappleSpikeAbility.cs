using System;
using Fusion;
using UnityEngine;
using Purgers.GameFlow.Control;

/// <summary>GrappleHit 接觸技能；不訂閱鉤頭命中、不接管繩索或玩家移動。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerAbilityRuntime))]
public sealed class PlayerGrappleSpikeAbility : NetworkBehaviour, IPlayerAbilityRuntimeModule, IPlayerAbilityHudState, IPlayerAbilityCooldownSource
{
    [Header("尖刺傷害與範圍（暫定值）")]
    [SerializeField, Min(0f), Tooltip("每次接觸的基礎傷害，暫定 20。乘上出傷當刻的增傷量條倍率一次，不消耗動能；0 不出傷也不啟動效果。")]
    private float baseDamage = 20f;
    [SerializeField, Min(0.01f), Tooltip("包覆全身的球形半徑，公尺，暫定 1.5。偵測與視覺共用，沿移動路徑掃掠。")]
    private float radius = 1.5f;
    [SerializeField, Tooltip("球心相對玩家 Root 的偏移，公尺；暫定 Y=1，將腳底 Root 提升到身體中心。")]
    private Vector3 centerOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Tooltip("受傷敵人 Collider 的 Layer；只接受同一 Runner 的存活 EnemyActor，不傷隊友。")]
    private LayerMask hitMask = ~0;
    [SerializeField, Tooltip("阻擋傷害的場景 Layer；玩家及敵人不作為牆。0 忽略遮擋。")]
    private LayerMask obstructionMask = ~0;

    [Header("效果與冷卻")]
    [SerializeField, Min(0.01f), Tooltip("首次有效命中後的效果秒數，暫定 3。斷繩、沒有敵人與再次鈎索皆不暫停或刷新。")]
    private float effectSeconds = 3f;
    [SerializeField, Min(0f), Tooltip("效果結束後的冷卻秒數，暫定 3；0 無冷卻。結束自動待命，下次命中才開始新效果。")]
    private float cooldownSeconds = 3f;
    [SerializeField, Min(0.01f), Tooltip("同一敵人再次受傷及擊退的最短秒數，預設 0.5；不同目標、不同玩家各自獨立，以 Tick 向上取整。")]
    private float repeatHitSeconds = 0.5f;
    [SerializeField, Tooltip("允許斷繩後鈎索慣性飛行繼續偵測、出傷與顯示。預設關閉；普通 Attached 拉動自己始終可用。")]
    private bool includeReleaseMomentum;

    [Header("微擊退（暫定值）")]
    [SerializeField, Min(0f), Tooltip("由接觸時玩家球心向外水平推退的距離，公尺，暫定 0.6；0 關閉。遵守敵人免疫與位移仲裁，無撞牆追加傷害或暈眩。")]
    private float knockbackDistance = 0.6f;
    [SerializeField, Min(0f), Tooltip("微擊退速度，公尺／秒，暫定 6；0 關閉。只在有效出傷時提交一次。")]
    private float knockbackSpeed = 6f;

    [Header("本機尖刺視覺")]
    [SerializeField, Tooltip("純視覺 Prefab，單位球半徑為 1；不可包含 NetworkObject、Collider 或 Rigidbody。首次命中後且符合鈎索條件時，各端本機顯示。")]
    private GameObject spikeVisualPrefab;

    [Networked] public int EffectEndTick { get; private set; }
    [Networked] public int CooldownEndTick { get; private set; }
    [Networked] public float EffectDurationSnapshot { get; private set; }
    [Networked] private int CooldownTicksSnapshot { get; set; }
    [Networked] public NetworkBool ContactEligible { get; private set; }
    [Networked] public int HitSequence { get; private set; }

    private Player owner;
    private PlayerActionGate actionGate;
    private PlayerCombatFeedbackRelay feedback;
    private bool spawned, professionAvailable = true, previousEligible;
    private Vector3 previousCenter;
    private int previousTick = int.MinValue;
    private Collider[] candidates = new Collider[64];
    private RaycastHit[] sightHits = new RaycastHit[32];
    private readonly GrappleSpikeHitLedger hits = new GrappleSpikeHitLedger();
    private GameObject visual;

    public PlayerAbilityCategory AbilityCategory => PlayerAbilityCategory.GrappleHit;
    public Player OwnerPlayer => owner;
    public float Radius => Mathf.Max(0.01f, radius);
    public Vector3 Center => owner != null ? owner.transform.position + owner.transform.rotation * centerOffset : transform.position + centerOffset;
    private bool Ready => spawned && Object != null && Object.IsValid && Runner != null &&
        owner != null && owner.Object != null && owner.Object.IsValid && owner.Runner == Runner;
    private bool Authority => Ready && Object.HasStateAuthority;
    public GrappleSpikePhase Phase => Ready ? GrappleSpikeRules.GetPhase(Runner.Tick.Raw, EffectEndTick, CooldownEndTick) : GrappleSpikePhase.Ready;
    public bool IsAbilityActive => Ready && Phase == GrappleSpikePhase.Active;
    public bool IsUsable => Ready && isActiveAndEnabled && professionAvailable && owner.Health != null && owner.Health.IsAlive &&
        !(actionGate != null && actionGate.IsBlocked(PlayerActionBlockMask.Grapple));
    public float ActiveRemainingSeconds => IsAbilityActive ? Mathf.Max(0f, (EffectEndTick - Runner.Tick.Raw) * Runner.DeltaTime) : 0f;
    public float CooldownDurationSeconds => Ready ? CooldownTicksSnapshot * Runner.DeltaTime : 0f;
    public float CooldownRemainingSeconds => Ready && Phase == GrappleSpikePhase.Cooldown
        ? Mathf.Max(0f, (CooldownEndTick - Runner.Tick.Raw) * Runner.DeltaTime) : 0f;
    public PlayerAbilityTimedHudState TimedHudState => IsAbilityActive
        ? ActiveAbilityHudRules.Create(PlayerActiveAbilityPhase.Active, ActiveRemainingSeconds, EffectDurationSnapshot) : default;
    public bool IsSpikeVisible => IsUsable && ContactEligible && IsAbilityActive;
    private bool HasEligibleGrapple => Ready && owner.Grapple != null &&
        GrappleSpikeRules.IsEligible(owner.Grapple.IsNormalPlayerPullAttached,
            owner.Grapple.IsReleaseMomentumActive, includeReleaseMomentum);

    public override void Spawned()
    {
        spawned = true;
        hits.Clear(); previousEligible = false; previousTick = int.MinValue;
        if (Object.HasStateAuthority)
        {
            EffectEndTick = 0; CooldownEndTick = 0; EffectDurationSnapshot = 0f;
            CooldownTicksSnapshot = 0; ContactEligible = false; HitSequence = 0;
        }
    }
    public void BindOwnerPlayer(Player player)
    {
        if (owner == player) return;
        owner = player;
        actionGate = player != null ? player.GetComponent<PlayerActionGate>() : null;
        feedback = player != null ? player.GetComponent<PlayerCombatFeedbackRelay>() : null;
        hits.Clear(); previousEligible = false; previousTick = int.MinValue;
        ClearVisual();
    }
    public void SetProfessionAvailable(bool available)
    {
        professionAvailable = available;
        if (!available) { EndForUnavailable(); previousEligible = false; }
    }
    private void EndForUnavailable()
    {
        if (!Authority) return;
        ContactEligible = false;
        if (!IsAbilityActive) return;
        EffectEndTick = Runner.Tick.Raw;
        CooldownEndTick = EffectEndTick + CooldownTicksSnapshot;
    }
    public override void FixedUpdateNetwork()
    {
        if (!Authority) return;
        if (!IsUsable) EndForUnavailable();
        if (!HasEligibleGrapple) { ContactEligible = false; previousEligible = false; }
        // 絕對 Tick 推進期限，斷繩及沒有新輸入都不會凍結。
        hits.Prune(Runner.Tick.Raw);
    }
    public void SimulateAbility(NetInput input, NetworkButtons previousButtons)
    {
        if (!Authority) return;
        int now = Runner.Tick.Raw;
        if (previousTick == now) return;
        bool eligible = IsUsable && (input.BlockedControls & (PlayerControlMask.AllInput | PlayerControlMask.Gameplay)) == 0 &&
            HasEligibleGrapple;
        ContactEligible = eligible;
        Vector3 center = Center;
        Vector3 start = previousEligible && previousTick == now - 1 ? previousCenter : center;
        previousCenter = center; previousTick = now;
        previousEligible = eligible && Phase != GrappleSpikePhase.Cooldown;
        if (!eligible || Phase == GrappleSpikePhase.Cooldown || baseDamage <= 0f) return;
        ScanContacts(start, center, now);
    }
    private void ScanContacts(Vector3 start, Vector3 end, int now)
    {
        PhysicsScene physics = Runner.GetPhysicsScene();
        int count;
        do
        {
            count = (end - start).sqrMagnitude < 0.000001f
                ? physics.OverlapSphere(end, Radius, candidates, hitMask, QueryTriggerInteraction.Collide)
                : physics.OverlapCapsule(start, end, Radius, candidates, hitMask, QueryTriggerInteraction.Collide);
            if (count < candidates.Length) break;
            Array.Resize(ref candidates, candidates.Length * 2);
        } while (true);
        for (int i = 0; i < count; i++)
        {
            Collider collider = candidates[i];
            EnemyActor enemy = collider != null ? collider.GetComponentInParent<EnemyActor>() : null;
            if (enemy == null || !enemy.IsFusionSpawned || !enemy.IsAlive || enemy.Runner != Runner ||
                !hits.CanHit(enemy.Object.Id, now)) continue;
            Vector3 center = GrappleSpikeRules.ClosestCenter(start, end, collider.bounds.center);
            Vector3 point = collider.ClosestPoint(center);
            if (IsObstructed(physics, center, point)) continue;
            Vector3 direction = GrappleSpikeRules.OutwardDirection(center, collider.bounds.center, owner.transform.forward);
            float multiplier = owner.GrappleMomentumEnergy != null ? owner.GrappleMomentumEnergy.DamageMultiplier : 1f;
            DamageRequest request = new DamageRequest
            {
                BaseDamage = Mathf.Max(0f, baseDamage), RequestedDamage = GrappleSpikeRules.Damage(baseDamage, multiplier),
                DamageType = DamageType.Ability, HitZone = DamageHitZoneType.Body, HeadshotDamageMultiplier = 1f,
                Attacker = owner.Object.InputAuthority, SourceNetworkObject = owner.Object, SourceObject = owner.gameObject,
                HitObject = collider.gameObject, HitPoint = point, HitNormal = -direction, HitDirection = direction,
                Distance = Vector3.Distance(center, point), Sequence = ++HitSequence
            };
            if (!DamageReceiverUtility.TryApplyDamage(enemy.gameObject, request, out DamageResult result) ||
                (!result.HasEffectiveDamage && !result.Deferred)) continue;
            hits.Record(enemy.Object.Id, now, GrappleSpikeRules.DurationTicks(repeatHitSeconds, Runner.DeltaTime));
            if (Phase == GrappleSpikePhase.Ready)
            {
                int duration = GrappleSpikeRules.DurationTicks(effectSeconds, Runner.DeltaTime);
                EffectEndTick = now + duration;
                EffectDurationSnapshot = duration * Runner.DeltaTime;
                float growthCooldown = Purgers.Progression.PlayerGrowth.Cooldown(owner, AbilityCategory, cooldownSeconds);
                CooldownTicksSnapshot = growthCooldown <= 0f ? 0 : GrappleSpikeRules.DurationTicks(growthCooldown, Runner.DeltaTime);
                CooldownEndTick = EffectEndTick + CooldownTicksSnapshot;
            }
            feedback?.ReportAbilityDamage(result);
            if (enemy.IsAlive && enemy.StateController != null)
                enemy.StateController.TryBeginAbilityKnockback(direction, knockbackDistance, knockbackSpeed, 0f, default);
        }
    }
    private bool IsObstructed(PhysicsScene physics, Vector3 start, Vector3 point)
    {
        return ActiveAbilityPhysics.IsWorldOccluded(physics, start, point, obstructionMask, ref sightHits);
    }
    private void LateUpdate()
    {
        bool visible = IsSpikeVisible;
        if (visible && visual == null && spikeVisualPrefab != null)
        {
            visual = Instantiate(spikeVisualPrefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(visual, owner.gameObject.scene);
        }
        if (visual == null) return;
        visual.SetActive(visible);
        if (visible)
        {
            visual.transform.SetPositionAndRotation(Center, Quaternion.identity);
            visual.transform.localScale = Vector3.one * Radius;
        }
    }
    private void ClearVisual() { if (visual != null) Destroy(visual); visual = null; }
    private void OnDisable() { previousEligible = false; ClearVisual(); }
    private void OnDestroy() => ClearVisual();
    public override void Despawned(NetworkRunner runner, bool hasState)
    { spawned = false; hits.Clear(); previousEligible = false; ClearVisual(); owner = null; }
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.7f);
        Gizmos.DrawWireSphere(Center, Radius);
    }
#endif
}

