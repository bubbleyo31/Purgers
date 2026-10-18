using Fusion;
using UnityEngine;

/// <summary>捕食的命中前流程。只有同一個 Combat Decision 仲裁器推進，不建立另一個攻擊系統。</summary>
public sealed partial class EnemyFlytrapAttack
{
    [Header("藤蔓攻擊（可調原型值）")]
    [SerializeField, Tooltip("藤蔓出手掛點。留空時尋找 TendrilOrigin，再退回 SenseOrigin／Root。")]
    private Transform tendrilOrigin;
    [SerializeField, Min(.01f), Tooltip("蓄勢前段追蹤秒數，預設 0.5；玩家仍可自由行動。幀模式啟用時改由 Lock Direction Frame 決定。")]
    private float trackingSeconds = .5f;
    [SerializeField, Min(.01f), Tooltip("固定方向後的提示秒數，預設 0.25；不再追蹤目標，至少一個 Tick。")]
    private float lockedSeconds = .25f;
    [SerializeField, Tooltip("使用共用動畫幀時序設定固定方向與出手幀；預設停用。兩階段至少各一 Tick。")]
    private bool useStrikeFrameTiming;
    [SerializeField] private AnimationFrameTiming strikeTiming = new AnimationFrameTiming();
    [SerializeField, Min(0), Tooltip("蓄勢動畫中停止追蹤的幀，0 起算；預設 12。")]
    private int lockDirectionFrame = 12;
    [SerializeField, Min(0), Tooltip("藤蔓開始刺出的幀，須晚於固定方向幀；預設 18。")]
    private int launchFrame = 18;
    [SerializeField, Min(.01f), Tooltip("藤蔓每秒前進公尺，預設 30；沿固定直線逐 Tick 掃掠，不轉彎、不直接命中。")]
    private float vineSpeed = 30f;
    [SerializeField, Min(.01f), Tooltip("藤蔓最大伸長公尺，預設 8；獨立於感應距離。")]
    private float vineRange = 8f;
    [SerializeField, Min(.01f), Tooltip("藤蔓碰撞半徑公尺，預設 0.15；方塊原型藤蔓顯示相同粗細。")]
    private float vineRadius = .15f;
    [SerializeField, Min(.01f), Tooltip("落空／撞牆後收回與收招秒數，預設 0.8；期間不重新攻擊，獨立於吞食後的 10 秒冷卻。")]
    private float missRecoverySeconds = .8f;

    [Networked] public NetworkObject AimedPlayer { get; private set; }
    [Networked] public Vector3 VineOrigin { get; private set; }
    [Networked] public Vector3 StrikeDirection { get; private set; }
    [Networked] public Vector3 VineTip { get; private set; }
    [Networked] public float StrikeProgress { get; private set; }
    [Networked] public int AttackSequence { get; private set; }
    private int strikePhaseTick;
    private float trackingDuration, lockedDuration, recoveryDuration, speedSnapshot, rangeSnapshot, radiusSnapshot, travelled;
    private Vector3 recoveryTip;
    public float VineRadius => Mathf.Max(.01f, vineRadius);

    private void BeginTracking(in EnemyCombatContext context)
    {
        AimedPlayer = context.Target;
        VineOrigin = tendrilOrigin != null ? tendrilOrigin.position : senseOrigin != null ? senseOrigin.position : transform.position;
        VineTip = VineOrigin; travelled = 0f; StrikeProgress = 0f;
        trackingDuration = Mathf.Max(Runner.DeltaTime, trackingSeconds);
        lockedDuration = Mathf.Max(Runner.DeltaTime, lockedSeconds);
        if (useStrikeFrameTiming && strikeTiming != null && strikeTiming.ContainsFrame(lockDirectionFrame) &&
            strikeTiming.ContainsFrame(launchFrame) && launchFrame > lockDirectionFrame)
        {
            trackingDuration = Mathf.Max(Runner.DeltaTime, strikeTiming.FrameToSeconds(lockDirectionFrame));
            lockedDuration = Mathf.Max(Runner.DeltaTime, strikeTiming.FrameToSeconds(launchFrame) - strikeTiming.FrameToSeconds(lockDirectionFrame));
        }
        recoveryDuration = Mathf.Max(Runner.DeltaTime, missRecoverySeconds);
        speedSnapshot = Mathf.Max(.01f, vineSpeed); rangeSnapshot = Mathf.Max(.01f, vineRange); radiusSnapshot = VineRadius;
        TrackAim(); Phase = EnemyFlytrapPhase.Tracking; strikePhaseTick = Runner.Tick.Raw; AttackSequence++;
        context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Tracking);
    }
    private bool TrackAim()
    {
        if (AimedPlayer == null || !AimedPlayer.IsValid) return false;
        var player = AimedPlayer.GetComponent<Player>();
        if (player == null || !player.Health.IsAlive || !HasClearSight(player)) return false;
        Vector3 point = player.Movement.KCC.FixedData.TargetPosition + Vector3.up * player.Movement.KCC.Settings.Height * .5f;
        Vector3 delta = point - VineOrigin;
        StrikeDirection = delta.sqrMagnitude > .000001f ? delta.normalized : transform.forward;
        return true;
    }
    private void BeginRecovery(in EnemyCombatContext context)
    {
        AimedPlayer = null; recoveryTip = VineTip; StrikeProgress = 0f;
        Phase = EnemyFlytrapPhase.Recovering; strikePhaseTick = Runner.Tick.Raw;
        context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Recovery);
    }
    private EnemyCombatOptionTickResult TickStrike(in EnemyCombatContext context)
    {
        float elapsed = (Runner.Tick.Raw - strikePhaseTick) * Runner.DeltaTime;
        switch (Phase)
        {
            case EnemyFlytrapPhase.Tracking:
                if (!TrackAim()) { BeginRecovery(context); break; }
                StrikeProgress = Mathf.Clamp01(elapsed / trackingDuration);
                if (elapsed >= trackingDuration)
                {
                    Phase = EnemyFlytrapPhase.Locked; strikePhaseTick = Runner.Tick.Raw; StrikeProgress = 0f;
                    context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.LockedDelay);
                }
                break;
            case EnemyFlytrapPhase.Locked:
                StrikeProgress = Mathf.Clamp01(elapsed / lockedDuration);
                if (elapsed >= lockedDuration)
                {
                    Phase = EnemyFlytrapPhase.Striking; StrikeProgress = 0f; AimedPlayer = null;
                    context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Active);
                }
                break;
            case EnemyFlytrapPhase.Striking:
                float step = Mathf.Min(speedSnapshot * Runner.DeltaTime, rangeSnapshot - travelled);
                Collider hit = EnemyFlytrapStrikePhysics.FirstContact(Runner.GetPhysicsScene(), VineTip, StrikeDirection,
                    step, radiusSnapshot, obstacleMask, Object, ref hits, ref overlaps, out float hitDistance);
                if (hit != null)
                {
                    VineTip += StrikeDirection * hitDistance;
                    var player = hit.GetComponentInParent<Player>();
                    // 第一接觸玩家不合法或無法安全拉入就收招，不穿過他再抓後方玩家。
                    if (player != null && TryCaptureHit(player)) break;
                    BeginRecovery(context); break;
                }
                travelled += step; VineTip = VineOrigin + StrikeDirection * travelled;
                StrikeProgress = Mathf.Clamp01(travelled / rangeSnapshot);
                if (travelled >= rangeSnapshot) BeginRecovery(context);
                break;
            case EnemyFlytrapPhase.Recovering:
                StrikeProgress = Mathf.Clamp01(elapsed / recoveryDuration);
                VineTip = Vector3.Lerp(recoveryTip, VineOrigin, StrikeProgress);
                if (elapsed >= recoveryDuration)
                {
                    Phase = EnemyFlytrapPhase.Waiting;
                    return EnemyCombatOptionTickResult.Completed;
                }
                break;
        }
        return EnemyCombatOptionTickResult.Running;
    }
}
