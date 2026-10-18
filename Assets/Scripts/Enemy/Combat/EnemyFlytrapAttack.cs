using System;
using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

public enum EnemyFlytrapPhase : byte { Waiting = 0, Pulling = 1, Feeding = 2, Cooldown = 3, Dead = 4, Tracking = 5, Locked = 6, Striking = 7, Recovering = 8 }

/// <summary>只由 EnemyCombatDecisionController 選擇的定點捕食。State Authority 擁有目標、位移與傷害。</summary>
[DisallowMultipleComponent]
public sealed partial class EnemyFlytrapAttack : EnemyCombatOption
{
    [Header("捕食掛點")]
    [SerializeField, Tooltip("玩家腳底固定點，不是頭部位置；需容得下完整站立膠囊，且不可位於怪物實體內。未指定時不能捕食。")]
    private Transform mouthAnchor;
    [SerializeField, Tooltip("感應視線起點；留空使用嘴邊掛點。360 度，不受共用敵人視野角或記憶影響。")]
    private Transform senseOrigin;
    [Header("拉動（原型起始值）")]
    [SerializeField, Min(.01f), Tooltip("快速拉入秒數，預設 0.2；藤蔓命中後才鎖住玩家。實際抵達嘴邊後才開始吞食。")]
    private float pullSeconds = .2f;
    [SerializeField, Tooltip("可選動畫幀配置；啟用時以抵達幀換算拉動秒數，不改吞食計時。")]
    private bool useAnimationFrameTiming;
    [SerializeField] private AnimationFrameTiming pullTiming = new AnimationFrameTiming();
    [SerializeField, Min(0), Tooltip("共用原始動畫中的抵達嘴邊幀（0 起算）。換算結果至少一個模擬 Tick。")]
    private int arrivalFrame = 6;
    [SerializeField, Min(.001f), Tooltip("排除站立抖動的速度容差（公尺／秒），預設 0.05；不是高速才觸發的門檻。")]
    private float movementEpsilon = .05f;
    [SerializeField, Tooltip("阻擋視線及玩家完整膠囊拉入路徑的 Layer；預設 Default。需包含場景牆壁、欄杆及障礙。")]
    private LayerMask obstacleMask = 1;
    [Header("吞食與冷卻")]
    [SerializeField, Min(.01f), Tooltip("抵達嘴邊後的吞食總秒數，預設 5。只改節奏，不改總傷害比例。")]
    private float feedingSeconds = 5f;
    [SerializeField, Min(.01f), Tooltip("每次扣生命的間隔秒數，預設 0.5。第一個間隔後首次扣血；結束時結算不足一段的傷害。")]
    private float damageIntervalSeconds = .5f;
    [SerializeField, Min(0f), Tooltip("玩家死亡或吞食完成釋放後的冷卻秒數，預設 10；期間不可指定其他玩家。")]
    private float cooldownSeconds = 10f;

    [Networked] public EnemyFlytrapPhase Phase { get; private set; }
    [Networked] public NetworkObject CapturedPlayer { get; private set; }
    [Networked] public int CaptureSequence { get; private set; }
    [Networked] public int BiteSequence { get; private set; }
    [Networked] public int ReleaseSequence { get; private set; }
    [Networked] public float PullProgress { get; private set; }
    [Networked] public float FeedingProgress { get; private set; }
    [Networked] private TickTimer Cooldown { get; set; }
    private bool spawned;
    private EnemyActor actor;
    private TestDamageReceiver health;
    private PlayerActionGate victimGate;
    private Player victim;
    private Vector3 pullStart, destination;
    private int startTick, feedingTick;
    private float durationSnapshot, intervalSnapshot, pullDuration, damageBudget, paidDamage;
    private RaycastHit[] hits = new RaycastHit[16];
    private Collider[] overlaps = new Collider[16];
    public bool Ready => spawned && Object != null && Object.IsValid;
    public Transform MouthAnchor => mouthAnchor;
    public float CooldownRemaining => Ready ? Cooldown.RemainingTime(Runner) ?? 0f : 0f;
    public override EnemyActionState ActionState => EnemyActionState.Attack;
    public override EnemyActionLockFlags LocksWhileActive => EnemyActionLockFlags.All;
    public override bool ContinueWithoutTarget => true;
    public override bool IsCooldownReady(NetworkRunner runner) => Ready && Phase == EnemyFlytrapPhase.Waiting && Cooldown.ExpiredOrNotRunning(runner);
    // 捕食只從獨立的全周近身查詢進入同一個仲裁器，不沿用警戒記憶或共用追逐目標。
    public override bool CanStartOption(in EnemyCombatContext context) => false;
    private void Awake() { actor = GetComponent<EnemyActor>(); health = GetComponent<TestDamageReceiver>(); if (tendrilOrigin == null) tendrilOrigin = transform.Find("TendrilOrigin"); }
    public override void Spawned()
    {
        spawned = true; health.Died += OnDied;
        if (!Object.HasStateAuthority) return;
        Phase = EnemyFlytrapPhase.Waiting; CapturedPlayer = null; Cooldown = TickTimer.None;
        CaptureSequence = BiteSequence = ReleaseSequence = 0;
    }
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (health != null) health.Died -= OnDied;
        if (hasState && Object != null && Object.HasStateAuthority) ReleaseVictim();
        spawned = false;
    }
    private void OnDisable()
    {
        if (Ready && Object.HasStateAuthority) CancelOption(default);
    }
    public override void FixedUpdateNetwork()
    {
        if (Ready && Object.HasStateAuthority && Phase == EnemyFlytrapPhase.Cooldown && Cooldown.ExpiredOrNotRunning(Runner))
            Phase = EnemyFlytrapPhase.Waiting;
    }
    public override bool TryGetEmergencyContext(in EnemyCombatContext context, out EnemyCombatContext selected)
    {
        selected = default;
        if (!Ready || !Object.HasStateAuthority || !actor.IsAlive || !IsCooldownReady(Runner) || mouthAnchor == null) return false;
        NetworkObject best = null; float nearest = float.PositiveInfinity;
        foreach (var playerRef in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(playerRef, out var obj) || !context.Perception.IsValidAlivePlayer(obj)) continue;
            var player = obj.GetComponent<Player>(); var gate = obj.GetComponent<PlayerActionGate>();
            if (player == null || player.Movement == null || gate == null || !gate.IsFusionSpawned || gate.IsCaptured) continue;
            float distance = Vector3.Distance(transform.position, player.Movement.KCC.FixedData.TargetPosition);
            if (distance > MaximumStartDistance || distance < MinimumStartDistance || distance >= nearest || !IsUnsafe(player, gate)) continue;
            if (!HasClearSight(player) || !HasClearPullPath(player, mouthAnchor.position)) continue;
            best = obj; nearest = distance;
        }
        if (best == null) return false;
        selected = new EnemyCombatContext(context.Controller, context.Actor, context.Perception, best, best.transform.position, nearest, context.DeltaTime);
        return true;
    }
    private bool IsUnsafe(Player player, PlayerActionGate gate)
    {
        bool crouched = player.SlideController != null && player.SlideController.IsCrouched && !player.SlideController.IsSliding;
        bool moving = player.CurrentWorldSpeed > movementEpsilon;
        if (player.GetInput<NetInput>(out var input))
        {
            Purgers.GameFlow.Control.PlayerControlLocks.Filter(ref input, input.BlockedControls);
            crouched = input.Buttons.IsSet(InputButton.Crouch) && (player.SlideController == null || !player.SlideController.IsSliding);
            moving |= input.Direction.sqrMagnitude > .0001f;
        }
        bool grappling = player.Grapple != null && player.Grapple.CurrentPhase != GrapplePhase.Idle;
        return EnemyFlytrapRules.IsUnsafe(crouched, moving, gate.HasRecentGunshot, grappling);
    }
    private bool HasClearSight(Player player)
    {
        Vector3 from = senseOrigin != null ? senseOrigin.position : mouthAnchor.position;
        var kcc = player.Movement.KCC;
        Vector3 point = kcc.FixedData.TargetPosition + Vector3.up * kcc.Settings.Height * .5f;
        int count = ActiveAbilityPhysics.SphereCastSorted(Runner.GetPhysicsScene(), from, point - from,
            Vector3.Distance(point, from), 0, obstacleMask, Object, ref hits);
        for (int i = 0; i < count; ++i)
            if (!ActiveAbilityPhysics.BelongsTo(hits[i].collider, player.Object)) return false;
        return true;
    }
    /// <summary>指定前檢查完整站立膠囊；包含終點占用。使用所屬 Runner PhysicsScene。</summary>
    public bool HasClearPullPath(Player player, Vector3 target)
    {
        if (!Ready || player == null || player.Movement == null) return false;
        var kcc = player.Movement.KCC;
        float radius = Mathf.Max(.02f, kcc.Settings.Radius - .02f);
        float height = Mathf.Max(kcc.Settings.Height, player.SlideController != null ? player.SlideController.StandingHeight : kcc.Settings.Height);
        Vector3 start = kcc.FixedData.TargetPosition;
        Vector3 bottom = start + Vector3.up * (radius + .025f);
        Vector3 top = start + Vector3.up * Mathf.Max(radius + .025f, height - radius);
        Vector3 delta = target - start;
        var scene = Runner.GetPhysicsScene();
        int count;
        if (delta.sqrMagnitude > .000001f)
        {
            do
            {
                count = scene.CapsuleCast(bottom, top, radius, delta.normalized, hits, delta.magnitude, obstacleMask, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                Array.Resize(ref hits, hits.Length * 2);
            } while (true);
            for (int i = 0; i < count; ++i)
                if (!ActiveAbilityPhysics.BelongsTo(hits[i].collider, player.Object)) return false;
        }
        do
        {
            count = scene.OverlapCapsule(bottom + delta, top + delta, radius, overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) break;
            Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (true);
        for (int i = 0; i < count; ++i)
            if (!ActiveAbilityPhysics.BelongsTo(overlaps[i], player.Object)) return false;
        return true;
    }
    public override void BeginOption(in EnemyCombatContext context)
    {
        if (!Ready || !Object.HasStateAuthority || !IsCooldownReady(Runner) || context.Target == null || mouthAnchor == null) return;
        BeginTracking(context);
    }
    private bool TryCaptureHit(Player player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != Runner || !player.Health.IsAlive ||
            !HasClearPullPath(player, mouthAnchor.position)) return false;
        var gate = player.GetComponent<PlayerActionGate>();
        if (gate == null || !gate.TryBeginCapture(Object)) return false;
        victim = player; victimGate = gate; CapturedPlayer = player.Object;
        AimedPlayer = null;
        pullStart = player.Movement.KCC.FixedData.TargetPosition; destination = mouthAnchor.position;
        int roomPlayers = 0; foreach (var ignored in Runner.ActivePlayers) roomPlayers++;
        damageBudget = EnemyFlytrapRules.TotalDamage(player.Health.MaximumHealth, roomPlayers);
        paidDamage = 0f; durationSnapshot = Mathf.Max(.01f, feedingSeconds); intervalSnapshot = Mathf.Max(.01f, damageIntervalSeconds);
        pullDuration = useAnimationFrameTiming && pullTiming != null && pullTiming.IsValid ? pullTiming.FrameToSeconds(arrivalFrame) : pullSeconds;
        pullDuration = Mathf.Max(Runner.DeltaTime, pullDuration);
        startTick = Runner.Tick.Raw; PullProgress = FeedingProgress = 0f;
        Phase = EnemyFlytrapPhase.Pulling; CaptureSequence++; return true;
    }
    public override EnemyCombatOptionTickResult TickOption(in EnemyCombatContext context)
    {
        if (!Ready || !Object.HasStateAuthority) return EnemyCombatOptionTickResult.Cancelled;
        if (Phase == EnemyFlytrapPhase.Tracking || Phase == EnemyFlytrapPhase.Locked ||
            Phase == EnemyFlytrapPhase.Striking || Phase == EnemyFlytrapPhase.Recovering)
            return TickStrike(context);
        if (Phase != EnemyFlytrapPhase.Pulling && Phase != EnemyFlytrapPhase.Feeding) return EnemyCombatOptionTickResult.Completed;
        if (victim == null || CapturedPlayer == null || !CapturedPlayer.IsValid || !victim.Health.IsAlive ||
            victimGate == null || !victimGate.IsCaptured || victimGate.Captor != Object)
        { FinishFeeding(); return EnemyCombatOptionTickResult.Completed; }
        if (Phase == EnemyFlytrapPhase.Pulling)
        {
            float elapsed = (Runner.Tick.Raw - startTick) * Runner.DeltaTime;
            PullProgress = Mathf.Clamp01(elapsed / pullDuration);
            victimGate.SetCapturePosition(Object, Vector3.Lerp(pullStart, destination, PullProgress));
            if (PullProgress >= 1f && Vector3.Distance(victim.Movement.KCC.FixedData.TargetPosition, destination) < .08f)
            { Phase = EnemyFlytrapPhase.Feeding; feedingTick = Runner.Tick.Raw; context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Active); }
            // 動態障礙在指定後插入時不穿牆、不永久困住；這不是玩家可操作的閃避窗口。
            else if (elapsed > pullDuration + 2f)
            { FinishFeeding(); return EnemyCombatOptionTickResult.Cancelled; }
            return EnemyCombatOptionTickResult.Running;
        }
        victimGate.SetCapturePosition(Object, destination);
        float time = (Runner.Tick.Raw - feedingTick) * Runner.DeltaTime;
        FeedingProgress = Mathf.Clamp01(time / durationSnapshot);
        float due = damageBudget * EnemyFlytrapRules.DueFraction(time, durationSnapshot, intervalSnapshot);
        float damage = Mathf.Max(0f, due - paidDamage);
        if (damage > 0f)
        {
            paidDamage = due; BiteSequence++;
            victim.Health.ReceiveDamage(new DamageRequest {
                RequestedDamage = damage, BaseDamage = damage, DamageType = DamageType.DamageOverTime,
                Attacker = PlayerRef.None, SourceNetworkObject = Object, SourceObject = gameObject,
                HitObject = victim.gameObject, HitPoint = destination, HeadshotDamageMultiplier = 1f,
                HasSourceWorldPosition = true, SourceWorldPosition = transform.position, Sequence = BiteSequence
            });
        }
        if (!victim.Health.IsAlive || time >= durationSnapshot)
        { FinishFeeding(); return EnemyCombatOptionTickResult.Completed; }
        return EnemyCombatOptionTickResult.Running;
    }
    private void ReleaseVictim()
    {
        bool hadVictim = victimGate != null || (Ready && CapturedPlayer != null);
        if (victimGate != null && victimGate.IsFusionSpawned && victimGate.IsCaptured && victimGate.Captor == Object)
        {
            victimGate.ReleaseCapture(Object);
        }
        victim = null; victimGate = null;
        if (Ready && Object.HasStateAuthority) { CapturedPlayer = null; if (hadVictim) ReleaseSequence++; }
    }
    private void FinishFeeding()
    {
        ReleaseVictim(); Phase = EnemyFlytrapPhase.Cooldown;
        Cooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, cooldownSeconds));
    }
    private void OnDied(CombatDeathEventData data)
    {
        if (!Ready || !Object.HasStateAuthority) return;
        AimedPlayer = null; ReleaseVictim(); Phase = EnemyFlytrapPhase.Dead;
    }
    public override void CancelOption(in EnemyCombatContext context)
    {
        if (!Ready || !Object.HasStateAuthority) return;
        AimedPlayer = null;
        if (actor.IsAlive) FinishFeeding(); else { ReleaseVictim(); Phase = EnemyFlytrapPhase.Dead; }
    }
    public override void PauseSimulationTick()
    {
        if (Phase == EnemyFlytrapPhase.Tracking || Phase == EnemyFlytrapPhase.Locked || Phase == EnemyFlytrapPhase.Recovering) strikePhaseTick++;
        if (Phase == EnemyFlytrapPhase.Pulling) startTick++;
        if (Phase == EnemyFlytrapPhase.Feeding) feedingTick++;
        if (!Cooldown.ExpiredOrNotRunning(Runner)) Cooldown = TickTimer.CreateFromTicks(Runner, (Cooldown.RemainingTicks(Runner) ?? 0) + 1);
    }
}
