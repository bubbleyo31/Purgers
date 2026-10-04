using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

/// <summary>第一次 E 待命、第二次 E 水平衝刺；最終位移由 PlayerMovement 的 KCC 階段提交。</summary>
public sealed class PlayerBlinkAbility : PlayerActiveAbilityBase
{
    [Header("閃現")]
    [SerializeField, Min(0.01f), Tooltip("第一次 E 待命期限，秒；到期未衝刺仍開始冷卻。")]
    private float primedSeconds = 5f;
    [SerializeField, Min(0f), Tooltip("單次水平衝刺最大距離，公尺；碰到可阻擋的 Collider 提前停止。")]
    private float dashDistance = 8f;
    [SerializeField, Min(0.1f), Tooltip("水平衝刺速度，公尺／秒；方向只在第二次 E 取樣，不含鏡頭俯仰。")]
    private float dashSpeed = 35f;
    [SerializeField, Min(0.001f), Tooltip("停在障礙前的安全間距，公尺。一般腳底接觸不會取消衝刺。")]
    private float collisionSkin = 0.02f;

    [Networked] private Vector3 DashDirection { get; set; }
    [Networked] private Vector3 LastDashPosition { get; set; }
    [Networked] private float RemainingDistance { get; set; }
    [Networked] private NetworkBool ObstacleReached { get; set; }
    private readonly KCCShapeCastInfo castInfo = new KCCShapeCastInfo();
    public bool IsDashing => Ready && Phase == PlayerActiveAbilityPhase.Dashing;

    protected override void OnActivationAccepted(NetInput input)
    {
        Phase = PlayerActiveAbilityPhase.Armed;
        PhaseDurationSeconds = Mathf.Max(0.01f, primedSeconds);
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, primedSeconds));
    }
    protected override void Activate() { }

    protected override void OnArmedPress(NetInput input)
    {
        if ((input.BlockedControls & Purgers.GameFlow.Control.PlayerControlMask.Movement) != 0) return;
        var gate = Owner.GetComponent<PlayerActionGate>();
        if (gate != null && gate.IsBlocked(PlayerActionBlockMask.Movement)) return;
        if (PhaseTimer.ExpiredOrNotRunning(Runner)) { FinishAbility(); return; }
        if (Owner.SupportGrapplePlayerPullReceiver != null && Owner.SupportGrapplePlayerPullReceiver.IsPullActive) return;
        KCC kcc = Owner.Movement.KCC;
        if (kcc == null) return;
        DashDirection = ActiveControlRules.ResolveDashDirection(input.Direction, kcc.Data.LookYaw);
        RemainingDistance = Mathf.Max(0f, dashDistance);
        LastDashPosition = kcc.Data.TargetPosition;
        ObstacleReached = false;
        Owner.Grapple?.CancelFromSpecialAbility(false, true);
        base.FinishAbility(); // 冷卻由真正衝刺觸發起算，結束時不重設。
        Phase = PlayerActiveAbilityPhase.Dashing;
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, dashDistance / Mathf.Max(0.1f, dashSpeed) / 0.01f + 1f));
    }

    protected override void TickAbility()
    {
        if (Phase == PlayerActiveAbilityPhase.Armed)
        {
            if (PhaseTimer.ExpiredOrNotRunning(Runner)) FinishAbility();
            return;
        }
        if (!IsDashing) return;
        if (Owner.SupportGrapplePlayerPullReceiver != null && Owner.SupportGrapplePlayerPullReceiver.IsPullActive)
        { EndDash(); return; }
        Vector3 current = Owner.Movement.KCC.Data.TargetPosition;
        RemainingDistance = Mathf.Max(0f, RemainingDistance - Mathf.Max(0f, Vector3.Dot(current - LastDashPosition, DashDirection)));
        LastDashPosition = current;
        if (ObstacleReached || RemainingDistance <= 0.001f || PhaseTimer.ExpiredOrNotRunning(Runner)) EndDash();
    }

    /// <summary>由既有 PlayerMovement 在 KCC PrepareData 階段取得最終速度；不直接寫 Transform。</summary>
    public bool TryGetDashVelocity(KCC kcc, KCCData data, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!IsDashing || kcc == null || data.DeltaTime <= 0f ||
            (Owner.SupportGrapplePlayerPullReceiver != null && Owner.SupportGrapplePlayerPullReceiver.IsPullActive)) return false;
        float multiplier = PlayerBulletTimeSessionRegistry.GetPlayerMovementMultiplier(Owner);
        float step = ActiveControlRules.ResolveDashStep(RemainingDistance, dashSpeed, data.DeltaTime,
            multiplier, float.PositiveInfinity, collisionSkin).Travel;
        float nearest = float.PositiveInfinity;
        // 略縮掃掠膠囊並抬高腳底，排除 PhysX 將貼地零距重疊回報為反向法線；
        // 仍追蹤真正牆內起步，且保留 KCC 正式碰撞解算。
        float castSkin = Mathf.Min(collisionSkin, kcc.Settings.Radius * .25f);
        float castRadius = Mathf.Max(.01f, kcc.Settings.Radius - castSkin);
        float castHeight = Mathf.Max(castRadius * 2f, kcc.Settings.Height - castSkin * 2f);
        if (kcc.CapsuleCast(castInfo, data.TargetPosition + Vector3.up * castSkin, castRadius,
            castHeight, DashDirection, step + collisionSkin, QueryTriggerInteraction.Ignore, true))
        {
            for (int i = 0; i < castInfo.ColliderHitCount; ++i)
            {
                RaycastHit hit = castInfo.ColliderHits[i].RaycastHit;
                if (hit.normal.y > 0.65f && Vector3.Dot(hit.normal, DashDirection) > -0.01f) continue;
                if (Vector3.Dot(hit.normal, DashDirection) >= -0.001f) continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
        }
        var resolved = ActiveControlRules.ResolveDashStep(RemainingDistance, dashSpeed, data.DeltaTime,
            multiplier, nearest, collisionSkin);
        velocity = DashDirection * resolved.Velocity;
        if (Authority && kcc.IsInFixedUpdate && resolved.ObstacleReached) ObstacleReached = true;
        return true;
    }

    private void EndDash()
    {
        if (!Authority) return;
        Owner.Movement.KCC.SetKinematicVelocity(Vector3.zero);
        Phase = PlayerActiveAbilityPhase.Ready;
        PhaseTimer = TickTimer.None;
        RemainingDistance = 0f;
        ObstacleReached = false;
    }
    protected override void FinishAbility()
    {
        if (IsDashing) { EndDash(); return; }
        base.FinishAbility();
    }
}
