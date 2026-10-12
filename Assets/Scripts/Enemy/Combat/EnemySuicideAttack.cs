using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>數量型地面自爆怪。CombatDecision 擁有啟動與 Tick；本元件只實作撲擊及一次爆炸。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TestDamageReceiver))]
public sealed class EnemySuicideAttack : EnemyCombatOption, IEnemyFatalHitPolicy
{
    [Header("撲擊：預備與飛行")]
    [SerializeField, Min(0f), Tooltip("灰盒起撲預備秒數；使用動畫幀時序時改由離地幀換算。")]
    private float preparationSeconds = .15f;
    [SerializeField, Tooltip("有動畫後啟用，以原始長度、FPS、倍率與離地幀決定起撲時機。")]
    private bool useAnimationFrameTiming;
    [SerializeField, Tooltip("起撲動畫的原始時序，不決定抵達或爆炸結果。")]
    private AnimationFrameTiming takeoffTiming = new AnimationFrameTiming();
    [SerializeField, Min(0f), Tooltip("起撲動畫的離地幀，從 0 起算；須落在動畫總幀數內。")]
    private float takeoffFrame = 4f;
    [SerializeField, Min(.02f), Tooltip("離地到引爆的最長秒數；修正落點不延長，時間到在當前位置引爆。")]
    private float flightSeconds = .6f;
    [SerializeField, Min(0f), Tooltip("拋物線相對起點與落點連線的最高額外高度，公尺。")]
    private float jumpHeight = 1.2f;
    [SerializeField, Min(.1f), Tooltip("從起撲位置可到達的最大水平半徑，公尺；不是無限追逐。")]
    private float maximumPounceDistance = 7f;
    [SerializeField, Min(.1f), Tooltip("撲擊水平位移速度上限，公尺／秒。來不及到達時仍按時在當前位置引爆。")]
    private float maximumPounceSpeed = 16f;
    [SerializeField, Min(.001f), Tooltip("距離落點小於此值且飛行已完成時視為抵達，公尺。")]
    private float arrivalTolerance = .08f;

    [Header("撲擊：有限追蹤")]
    [SerializeField, Range(0f, 1f), Tooltip("飛行前多少比例可修正落點；0 完全鎖定，建議小於 0.5，期限仍固定。")]
    private float steeringFraction = .33f;
    [SerializeField, Min(0f), Tooltip("落點每秒最多移動多少公尺；0 不修正。")]
    private float landingCorrectionSpeed = 5f;
    [SerializeField, Range(0f, 89f), Tooltip("相對初始起撲方向允許的水平修正半角，度；不允許掉頭。")]
    private float maximumSteeringAngle = 35f;
    [SerializeField, Min(0f), Tooltip("合法落點相對起撲腳底的最大高度差，公尺。")]
    private float maximumLandingHeightDifference = 2f;
    [SerializeField, Min(.1f), Tooltip("尋找玩家腳下地板時的向下探測長度，公尺。飛走而無合法地面時保留舊落點。")]
    private float groundProbeDepth = 8f;
    [SerializeField, Tooltip("落點必須接近已烘焙 NavMesh；保持開啟可避免落在不可巡邏地面。")]
    private bool requireNavMeshLanding = true;
    [SerializeField, Tooltip("落點使用的 NavMesh Agent Type ID，必須與地面導航一致。")]
    private int agentTypeId;
    [SerializeField, Min(.01f), Tooltip("地面命中點尋找 NavMesh 的最大距離，公尺；過大可能跳到別層。")]
    private float landingSnapDistance = .3f;

    [Header("原地引爆：近身與高速接近")]
    [SerializeField, Min(0f), Tooltip("任何方向玩家進入此距離即原地引爆，公尺；仍檢查牆壁。0 停用貼身條件。")]
    private float contactDetonationDistance = 1f;
    [SerializeField, Min(0f), Tooltip("玩家進入攻擊啟動範圍時，朝敵人接近速度達此值則原地引爆，公尺／秒。0 停用高速條件。")]
    private float fastApproachSpeed = 12f;
    [SerializeField, Min(0f), Tooltip("原地引爆等待秒數，預設 0 完全零預警。玩家有效命中始終立即引爆。")]
    private float stationaryDetonationDelay;

    [Header("爆炸：幾何與傷害")]
    [SerializeField, Min(.01f), Tooltip("殺傷球體半徑，公尺；直徑等於兩倍半徑，並控制爆炸 VFX 比例。")]
    private float explosionRadius = 3f;
    [SerializeField, Tooltip("爆炸中心相對敵人 Root 的本地位移，公尺；預設位於 1 公尺灰盒身體中心。")]
    private Vector3 explosionCenterOffset = new Vector3(0f, .5f, 0f);
    [SerializeField, Min(0f), Tooltip("爆炸中心傷害；與外緣傷害之間按距離線性衰減。")]
    private float centerDamage = 40f;
    [SerializeField, Min(0f), Tooltip("半徑外緣的最低傷害，不得高於中心；範圍外完全無傷害。")]
    private float edgeDamage = 5f;
    [SerializeField, Tooltip("實體牆壁、樓板與撲擊障礙 Layer；不得包含 Player／Enemy。")]
    private LayerMask worldMask = 1;
    [SerializeField, Min(.001f), Tooltip("撲擊掃掠保留的碰撞間距，公尺；撞牆在停止位置爆炸。")]
    private float collisionSkin = .02f;

    [Header("Debuff：中心到外緣")]
    [SerializeField, Min(0f), Tooltip("中心鈎索封鎖秒數；命中即強制斷鈎，時間 0 只中斷而不延長封鎖。")]
    private float centerGrappleBlockSeconds = 3f;
    [SerializeField, Min(0f), Tooltip("外緣鈎索封鎖秒數，允許小於 1 秒，不得高於中心。")]
    private float edgeGrappleBlockSeconds = .25f;
    [SerializeField, Range(0f, 1f), Tooltip("中心走路／奔跑減速比例；0.5 為減速 50%，不減少空中慣性與滑鏟。")]
    private float centerSlowStrength = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("外緣減速比例，不得高於中心。")]
    private float edgeSlowStrength = .1f;
    [SerializeField, Min(0f), Tooltip("中心緩速持續秒數；各次命中獨立到期，取仍有效的最強緩速。")]
    private float centerSlowSeconds = 3f;
    [SerializeField, Min(0f), Tooltip("外緣緩速持續秒數，允許小於 1 秒，不得高於中心。")]
    private float edgeSlowSeconds = .25f;
    [SerializeField, Tooltip("預留：下一階段的準心遮擋圖檔，本階段不顯示、不改準心。")]
    private Sprite reservedCrosshairObstruction;

    [Header("爆炸：本機呈現")]
    [SerializeField, Tooltip("純視覺爆炸 Prefab；使用 Explosion002，保留其已調整比例。不得含 NetworkObject。")]
    private GameObject explosionVfx;
    [SerializeField, Min(.001f), Tooltip("指定特效目前完整基準縮放所對應的直徑，公尺。使用者校準為 1，不是半徑。")]
    private float vfxReferenceDiameter = 1f;
    [SerializeField, Min(.1f), Tooltip("爆炸特效最長保留秒數；敵人消失後獨立播放，時間到清理。")]
    private float vfxLifetime = 6f;

    [Networked] public NetworkBool HasExploded { get; private set; }
    [Networked] public NetworkBool IsAirborne { get; private set; }
    [Networked] public Vector3 LandingPoint { get; private set; }
    [Networked] public NetworkObject PounceTarget { get; private set; }
    [Networked] private TickTimer PhaseTimer { get; set; }
    [Networked] private Vector3 LaunchPosition { get; set; }
    [Networked] private Vector3 LaunchDirection { get; set; }
    [Networked] private NetworkBool Stationary { get; set; }
    private TestDamageReceiver health;
    private EnemyActor actor;
    private EnemyPatrolNavigator navigator;
    private bool spawned, emergencySelected, localVisualPlayed;
    private readonly Dictionary<NetworkId, Vector3> previousPlayerPositions = new Dictionary<NetworkId, Vector3>();
    private Vector3 previousEnemyCenter;
    private NetworkId previouslyTrackedPlayer;
    private RaycastHit[] sightHits = new RaycastHit[16];
    public float ExplosionRadius => explosionRadius;
    public Vector3 ExplosionCenter => transform.TransformPoint(explosionCenterOffset);
    public override bool ContinueWithoutTarget => true;
    public override EnemyActionState ActionState => EnemyActionState.Attack;
    public override EnemyActionLockFlags LocksWhileActive => EnemyActionLockFlags.All;
    public override bool IsCooldownReady(NetworkRunner runner) => spawned && !HasExploded;
    private void Awake() { health = GetComponent<TestDamageReceiver>(); actor = GetComponent<EnemyActor>(); navigator = GetComponent<EnemyPatrolNavigator>(); }
    public override void Spawned()
    {
        spawned = true; localVisualPlayed = false; emergencySelected = false; previouslyTrackedPlayer = default; previousPlayerPositions.Clear(); previousEnemyCenter = ExplosionCenter;
        health.Died += OnDied;
        if (Object.HasStateAuthority) { HasExploded = false; IsAirborne = false; PhaseTimer = TickTimer.None; Stationary = false; }
    }
    public override void Despawned(NetworkRunner runner, bool hasState) { spawned = false; if (health != null) health.Died -= OnDied; previousPlayerPositions.Clear(); }
    public bool IsFatalHit(in DamageRequest request) => spawned && !HasExploded && EnemySuicideRules.IsPlayerTrigger(request);
    public override bool CanStartOption(in EnemyCombatContext context) => !HasExploded && TryLanding(context.Target, transform.position, out _);

    public override bool TryGetEmergencyContext(in EnemyCombatContext context, out EnemyCombatContext emergency)
    {
        emergency = default;
        if (!spawned || HasExploded) return false;
        Vector3 center = ExplosionCenter;
        NetworkObject trigger = null;
        foreach (PlayerRef playerRef in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(playerRef, out var player) || !context.Perception.IsValidAlivePlayer(player)) continue;
            Vector3 point = PlayerPoint(player, center);
            bool hadPrevious = previousPlayerPositions.TryGetValue(player.Id, out var previous);
            Vector3 relativeNow = point - center;
            Vector3 relativeBefore = hadPrevious ? previous - previousEnemyCenter : relativeNow;
            float swept = EnemySuicideRules.SegmentDistance(relativeBefore, relativeNow, Vector3.zero);
            float closingSpeed = context.DeltaTime > 0f && hadPrevious ? Vector3.Dot((previous - point) / context.DeltaTime, (previous - center).normalized) : 0f;
            bool tooClose = contactDetonationDistance > 0f && swept <= contactDetonationDistance;
            bool unnoticed = previouslyTrackedPlayer != player.Id;
            bool fast = fastApproachSpeed > 0f && closingSpeed >= fastApproachSpeed;
            previousPlayerPositions[player.Id] = point;
            if (trigger == null && (tooClose || (swept <= MaximumStartDistance && (unnoticed || fast))) && HasClearWorldLine(center, point)) trigger = player;
        }
        previousEnemyCenter = center;
        previouslyTrackedPlayer = context.Perception.HasCombatSight && context.Perception.CurrentTarget != null ? context.Perception.CurrentTarget.Id : default;
        if (trigger == null) return false;
        emergencySelected = true;
        emergency = new EnemyCombatContext(context.Controller, context.Actor, context.Perception, trigger, trigger.transform.position,
            Vector3.Distance(center, trigger.transform.position), context.DeltaTime);
        return true;
    }
    public override void BeginOption(in EnemyCombatContext context)
    {
        PounceTarget = context.Target;
        LaunchPosition = transform.position;
        Stationary = emergencySelected; emergencySelected = false;
        IsAirborne = false;
        Vector3 point = LaunchPosition;
        if (!Stationary && !TryLanding(PounceTarget, LaunchPosition, out point)) Stationary = true;
        else if (!Stationary) LandingPoint = point;
        LaunchDirection = Vector3.ProjectOnPlane(LandingPoint - LaunchPosition, Vector3.up).normalized;
        if (LaunchDirection.sqrMagnitude < .001f) LaunchDirection = transform.forward;
        float preparation = useAnimationFrameTiming && takeoffTiming != null && takeoffTiming.IsValid ? takeoffTiming.FrameToSeconds(takeoffFrame) : preparationSeconds;
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, Stationary ? stationaryDetonationDelay : preparation);
        if (Stationary && stationaryDetonationDelay <= 0f) Detonate();
    }
    public override EnemyCombatOptionTickResult TickOption(in EnemyCombatContext context)
    {
        if (HasExploded) return EnemyCombatOptionTickResult.Completed;
        if (Stationary) { if (PhaseTimer.ExpiredOrNotRunning(Runner)) Detonate(); return EnemyCombatOptionTickResult.Running; }
        if (!IsAirborne)
        {
            if (TryGetEmergencyContext(context, out _))
            { emergencySelected = false; Stationary = true; PhaseTimer = TickTimer.CreateFromSeconds(Runner, stationaryDetonationDelay); if (stationaryDetonationDelay <= 0f) Detonate(); return EnemyCombatOptionTickResult.Running; }
            if (!PhaseTimer.ExpiredOrNotRunning(Runner)) return EnemyCombatOptionTickResult.Running;
            IsAirborne = true; LaunchPosition = transform.position;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, flightSeconds);
            context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Active);
        }
        float remaining = PhaseTimer.RemainingTime(Runner) ?? 0f;
        float progress = Mathf.Clamp01(1f - remaining / flightSeconds);
        if (progress < steeringFraction && context.Perception.IsValidAlivePlayer(PounceTarget) &&
            HasClearWorldLine(ExplosionCenter, PlayerPoint(PounceTarget, ExplosionCenter)) && TryLanding(PounceTarget, LaunchPosition, out var desired))
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(desired - LaunchPosition, Vector3.up);
            if (Vector3.Angle(LaunchDirection, horizontal) <= maximumSteeringAngle)
                LandingPoint = Vector3.MoveTowards(LandingPoint, desired, landingCorrectionSpeed * context.DeltaTime);
        }
        float nextProgress = Mathf.Clamp01(progress + context.DeltaTime / flightSeconds);
        Vector3 delta = Vector3.ProjectOnPlane(LandingPoint - transform.position, Vector3.up);
        Vector3 step = Vector3.ClampMagnitude(delta / Mathf.Max(context.DeltaTime, remaining) * context.DeltaTime, maximumPounceSpeed * context.DeltaTime);
        Vector3 next = transform.position + step;
        next.y = Mathf.Lerp(LaunchPosition.y, LandingPoint.y, nextProgress) + 4f * jumpHeight * nextProgress * (1f - nextProgress);
        if (MoveWithCollision(next)) { Detonate(); return EnemyCombatOptionTickResult.Completed; }
        if (step.sqrMagnitude > .000001f) transform.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);
        if (remaining <= context.DeltaTime || (nextProgress >= 1f && Vector3.Distance(transform.position, LandingPoint) <= arrivalTolerance)) Detonate();
        return HasExploded ? EnemyCombatOptionTickResult.Completed : EnemyCombatOptionTickResult.Running;
    }
    public override void CancelOption(in EnemyCombatContext context)
    {
        // 受控時不搶回位移。已離地的攻擊在當前位置結束；預備中則可被打斷。
        if (IsAirborne && !HasExploded && actor.IsAlive) Detonate();
        PhaseTimer = TickTimer.None; IsAirborne = false; emergencySelected = false;
    }
    public override void PauseSimulationTick()
    {
        PhaseTimer = ActiveControlRules.PauseTimerForOneTick(PhaseTimer, Runner);
        previousPlayerPositions.Clear(); previousEnemyCenter = ExplosionCenter;
    }
    private bool TryLanding(NetworkObject target, Vector3 origin, out Vector3 point)
    {
        point = origin;
        if (target == null || !target.IsValid) return false;
        Vector3 projected = origin + Vector3.ClampMagnitude(Vector3.ProjectOnPlane(target.transform.position - origin, Vector3.up), maximumPounceDistance);
        Vector3 probe = new Vector3(projected.x, origin.y + maximumLandingHeightDifference + collisionSkin, projected.z);
        if (!TryWorldGround(probe, groundProbeDepth + maximumLandingHeightDifference, out var hit)) return false;
        point = hit.point;
        if (Mathf.Abs(point.y - origin.y) > maximumLandingHeightDifference) return false;
        if (requireNavMeshLanding)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agentTypeId, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(point, out var nav, landingSnapDistance, filter)) return false;
            point = nav.position;
        }
        return Vector3.ProjectOnPlane(point - origin, Vector3.up).magnitude <= maximumPounceDistance + .001f;
    }
    private bool TryWorldGround(Vector3 origin, float distance, out RaycastHit ground)
    {
        ground = default;
        int count;
        while (true)
        {
            count = Runner.GetPhysicsScene().Raycast(origin, Vector3.down, sightHits, distance, worldMask, QueryTriggerInteraction.Ignore);
            if (count < sightHits.Length) break;
            if (sightHits.Length >= 1024) return false;
            System.Array.Resize(ref sightHits, sightHits.Length * 2);
        }
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() != null || hit.collider.GetComponentInParent<EnemyActor>() != null) continue;
            if (hit.distance < nearest) { nearest = hit.distance; ground = hit; }
        }
        return nearest < float.PositiveInfinity;
    }
    private bool MoveWithCollision(Vector3 next)
    {
        Vector3 delta = next - transform.position;
        float distance = delta.magnitude;
        if (distance < .000001f) return false;
        float radius = navigator != null ? navigator.BodyRadius : .35f;
        float height = navigator != null ? navigator.BodyHeight : 1f;
        Vector3 bottom = transform.position + Vector3.up * (radius + collisionSkin);
        Vector3 top = transform.position + Vector3.up * Mathf.Max(radius + collisionSkin, height - radius);
        if (Runner.GetPhysicsScene().CapsuleCast(bottom, top, Mathf.Max(.001f, radius - collisionSkin), delta / distance, out var hit, distance + collisionSkin, worldMask, QueryTriggerInteraction.Ignore))
        { transform.position += delta / distance * Mathf.Max(0f, hit.distance - collisionSkin); return true; }
        transform.position = next; return false;
    }
    private bool HasClearWorldLine(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .000001f) return true;
        // KCC 執行期膠囊可能位於 Default，不能只靠 Layer 當成世界牆壁。
        // 讀完整射線命中，排除玩家／敵人；不因第一個命中是玩家就漏掉後面的牆。
        int count;
        while (true)
        {
            count = Runner.GetPhysicsScene().Raycast(from, delta.normalized, sightHits, delta.magnitude, worldMask, QueryTriggerInteraction.Ignore);
            if (count < sightHits.Length) break;
            if (sightHits.Length >= 1024) return false;
            System.Array.Resize(ref sightHits, sightHits.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            var collider = sightHits[i].collider;
            if (collider != null && collider.GetComponentInParent<PlayerHealth>() == null && collider.GetComponentInParent<EnemyActor>() == null) return false;
        }
        return true;
    }
    private static Vector3 PlayerPoint(NetworkObject player, Vector3 origin)
    {
        var kcc = player.GetComponent<Fusion.Addons.KCC.KCC>();
        var capsule = kcc != null ? kcc.Collider : player.GetComponent<CapsuleCollider>();
        return capsule != null && capsule.enabled ? capsule.ClosestPoint(origin) : player.transform.position + Vector3.up * .9f;
    }
    private void Detonate()
    {
        if (!spawned || !Object.HasStateAuthority || HasExploded || !actor.IsAlive) return;
        // 自主自爆沒有玩家攻擊者，沿用正式死亡／經驗入口；不攜帶先前命中的玩家身分。
        health.ReceiveDamage(new DamageRequest { RequestedDamage = health.MaxHealth, BaseDamage = health.MaxHealth,
            DamageType = DamageType.Explosion, Attacker = PlayerRef.None, SourceObject = gameObject, SourceNetworkObject = Object,
            HitObject = gameObject, HitPoint = ExplosionCenter, HitDirection = Vector3.up, HeadshotDamageMultiplier = 1f });
    }
    private void OnDied(CombatDeathEventData death)
    {
        if (!spawned || !Object.HasStateAuthority || HasExploded) return;
        HasExploded = true; IsAirborne = false;
        Vector3 center = ExplosionCenter;
        foreach (PlayerRef playerRef in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(playerRef, out var player) || player == null || !player.IsValid) continue;
            var playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth == null || !playerHealth.IsAlive) continue;
            Vector3 point = PlayerPoint(player, center);
            float distance = Vector3.Distance(center, point);
            if (distance > explosionRadius || !HasClearWorldLine(center, point)) continue;
            float damage = EnemySuicideRules.Falloff(distance, explosionRadius, centerDamage, edgeDamage);
            if (!EnemyDamageUtility.TryDamagePlayer(actor, player, player.gameObject, point, (point-center).normalized,
                point-center, damage, DamageType.Explosion, 1, out var result) || !result.Accepted || result.WasImmune) continue;
            var gate = player.GetComponent<PlayerActionGate>();
            if (gate != null) gate.ApplyExplosionStatus(
                EnemySuicideRules.Falloff(distance, explosionRadius, centerGrappleBlockSeconds, edgeGrappleBlockSeconds),
                EnemySuicideRules.Falloff(distance, explosionRadius, centerSlowStrength, edgeSlowStrength),
                EnemySuicideRules.Falloff(distance, explosionRadius, centerSlowSeconds, edgeSlowSeconds));
        }
        RPC_ExplosionVisual(center, explosionRadius);
    }
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ExplosionVisual(Vector3 center, float radius)
    {
        if (localVisualPlayed) return;
        localVisualPlayed = true;
        foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        if (explosionVfx == null || Application.isBatchMode) return;
        var instance = Instantiate(explosionVfx, center, Quaternion.identity);
        Runner.MoveToRunnerScene(instance);
        instance.transform.localScale = explosionVfx.transform.localScale * EnemySuicideRules.VfxScaleFactor(radius, vfxReferenceDiameter);
        Destroy(instance, vfxLifetime);
    }
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected(); Gizmos.color = Color.red; Gizmos.DrawWireSphere(ExplosionCenter, explosionRadius);
        if (spawned && Object != null && Object.IsValid && IsAirborne) { Gizmos.color = Color.yellow; Gizmos.DrawLine(transform.position, LandingPoint); Gizmos.DrawWireSphere(LandingPoint, arrivalTolerance); }
    }
    protected override void OnValidate()
    {
        base.OnValidate();
        flightSeconds = Mathf.Max(.02f, flightSeconds); explosionRadius = Mathf.Max(.01f, explosionRadius);
        edgeDamage = Mathf.Clamp(edgeDamage, 0f, centerDamage); edgeSlowStrength = Mathf.Clamp(edgeSlowStrength, 0f, centerSlowStrength);
        edgeSlowSeconds = Mathf.Clamp(edgeSlowSeconds, 0f, centerSlowSeconds); edgeGrappleBlockSeconds = Mathf.Clamp(edgeGrappleBlockSeconds, 0f, centerGrappleBlockSeconds);
    }
}
