using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;
using Purgers.GameFlow.Control;

/// <summary>沿用 PlayerGrapple 網路生命週期與 KCC；側移只追加有限 ExternalDelta。</summary>
public partial class PlayerGrapple
{
    [Header("鈎索飛行雙擊側移")]
    [SerializeField, Min(.01f), Tooltip("AA／DD 的兩次實際按下最大間隔，秒；預設 0.25。長按不連發，左右交替不算雙擊。")]
    private float sideDashDoubleTapWindow = .25f;
    [SerializeField, Min(0f), Tooltip("每次額外水平側移距離，公尺；預設 20，0 停用。獨立於 E 閃現，保留原飛行及重力，碰牆即結束、不補足。")]
    private float sideDashDistance = 20f;
    [SerializeField, Min(.1f), Tooltip("額外側移速度，公尺／秒；預設 150。無阻擋時持續時間＝距離／速度；子彈時間縮放實際位移。")]
    private float sideDashSpeed = 150f;
    [SerializeField, Min(0f), Tooltip("成功側移起算的左右共用冷卻，秒；預設 1。冷卻內的雙擊丟棄，不預存；每次扣單次出鈎費用兩倍，有剩餘即可扣至 0。")]
    private float sideDashCooldownSeconds = 1f;
    [SerializeField, Min(.001f), Tooltip("完整角色碰撞掃掠的停牆間距，公尺；預設 0.02。成功發動後碰牆不退款或重設冷卻。")]
    private float sideDashCollisionSkin = .02f;

    [Networked] private NetworkBool SideDashActive { get; set; }
    [Networked] private Vector3 SideDashDirection { get; set; }
    [Networked] private float SideDashRemaining { get; set; }
    [Networked] private TickTimer SideDashCooldown { get; set; }
    [Networked] private int SideDashObservedTap { get; set; }
    [Networked] private int SideDashConsumedTap { get; set; }
    [Networked] private NetworkBool SideDashInputInitialized { get; set; }
    [Networked] public int SideDashSequence { get; private set; }
    private bool sideDashSpawned;
    private Player sideDashOwner;
    private readonly KCCShapeCastInfo sideDashCast = new KCCShapeCastInfo();
    public bool IsSideDashing => sideDashSpawned && Object != null && Object.IsValid && SideDashActive;
    public float SideDashCooldownRemaining => sideDashSpawned && Object != null && Object.IsValid
        ? SideDashCooldown.RemainingTime(Runner) ?? 0f : 0f;

    public override void Spawned()
    {
        sideDashSpawned = true;
        sideDashOwner = GetComponent<Player>();
        InitializeGrappleFeedback();
    }
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        DisposeGrappleFeedback();
        sideDashSpawned = false;
    }

    private bool SideDashMovementAllowed()
    {
        if (sideDashOwner == null || sideDashOwner.Health == null || !sideDashOwner.Health.IsAlive) return false;
        if (explosionActionGate == null) explosionActionGate = GetComponent<PlayerActionGate>();
        if (explosionActionGate != null && explosionActionGate.IsBlocked(PlayerActionBlockMask.Movement | PlayerActionBlockMask.Grapple)) return false;
        if (sideDashOwner.SupportGrapplePlayerPullReceiver != null && sideDashOwner.SupportGrapplePlayerPullReceiver.IsPullActive) return false;
        if (abilityRuntimeManager != null &&
            ((abilityRuntimeManager.TryGetActiveModule<PlayerBlinkAbility>(out var blink) && blink.IsDashing) ||
             (abilityRuntimeManager.TryGetActiveModule<TankAirDashAbility>(out var tank) && tank.IsDashing))) return false;
        return true;
    }

    /// <summary>Player 在更新當 Tick 視角後、鈎索拉力／WASD 慣性轉向前呼叫。Client 預測，Host 決定正式結果。</summary>
    public void SimulateSideDash(NetInput input, float movementInfluence)
    {
        if (!sideDashSpawned || (!Object.HasStateAuthority && !Object.HasInputAuthority)) return;
        bool permitted = movementInfluence > .0001f && !kcc.Data.IsGrounded && SideDashMovementAllowed() &&
            !PlayerControlLocks.BlocksMovement(input.BlockedControls) && !PlayerControlLocks.BlocksGameplay(input.BlockedControls);
        if (SideDashActive && (!permitted || SideDashRemaining <= .0001f)) SideDashActive = false;
        bool eligible = permitted && !PlayerControlLocks.BlocksCombat(input.BlockedControls) && sideDashDistance > 0f && !SideDashActive && SideDashCooldown.ExpiredOrNotRunning(Runner) &&
            (IsNormalPlayerPullAttached || (!IsGrappleControlActive &&
             (ReleaseMomentumActive || stateMachine.CurrentState == PlayerMovementState.GrappleAirborne)));
        if (!SideDashInputInitialized)
        {
            SideDashInputInitialized = true;
            SideDashObservedTap = SideDashConsumedTap = input.SideTapSequence;
            return;
        }
        if (!eligible)
        {
            SideDashObservedTap = SideDashConsumedTap = input.SideTapSequence;
            return;
        }
        if (input.SideTapSequence == SideDashObservedTap) return;
        SideDashObservedTap = input.SideTapSequence;
        uint freshPresses = unchecked((uint)(input.SideTapSequence - SideDashConsumedTap));
        if (freshPresses < 2 || !GrappleSideDashRules.IsDoubleTap(input.PreviousSideTapDirection,
                input.SideTapDirection, input.SideTapInterval, sideDashDoubleTapWindow)) return;
        // Consume the pair even when zero energy rejects it; held/stale input never retries later.
        SideDashConsumedTap = input.SideTapSequence;
        if (charges == null || !charges.ConsumeSideDashEnergy()) return;
        bool attached = IsNormalPlayerPullAttached;
        Vector3 flight = kcc.Data.DynamicVelocity + kcc.Data.KinematicVelocity;
        if (attached)
        {
            // RealVelocity is the last solved flight; no side-dash contribution exists at this point.
            flight = kcc.Data.RealVelocity;
            CancelFromSpecialAbility(false, false);
            Vector3 planar = Vector3.ProjectOnPlane(flight, Vector3.up);
            ReleaseMomentumDirection = planar.normalized;
            ReleaseMomentumSpeed = planar.magnitude;
            ReleaseMomentumBaseSpeed = ResolveReleaseBaseMovementSpeed();
            ReleaseMomentumActive = planar.sqrMagnitude > .0001f;
            kcc.SetKinematicVelocity(Vector3.zero);
            kcc.SetDynamicVelocity(flight);
        }
        SideDashDirection = GrappleSideDashRules.Direction(input.SideTapDirection, kcc.Data.LookYaw);
        SideDashRemaining = Mathf.Max(0f, sideDashDistance);
        SideDashCooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, sideDashCooldownSeconds));
        SideDashActive = true;
        SideDashSequence++;
    }

    /// <summary>原 KCC PrepareData 追加側移；不改寫既有速度、不保存世界終點。</summary>
    public void ApplySideDashMotion(KCC target, KCCData data)
    {
        if (!IsSideDashing || !SideDashMovementAllowed() || data.IsGrounded || data.DeltaTime <= 0f) return;
        float multiplier = Mathf.Clamp01(PlayerBulletTimeSessionRegistry.GetPlayerMovementMultiplier(sideDashOwner));
        if (multiplier <= 0f) return;
        Vector3 side = GrappleSideDashRules.SideDelta(SideDashDirection, SideDashRemaining,
            sideDashSpeed, data.DeltaTime, multiplier);
        Vector3 travel = (data.DesiredVelocity * data.DeltaTime + data.ExternalDelta) * multiplier + side;
        float length = travel.magnitude;
        bool blocked = false;
        float fraction = 1f;
        if (length > .00001f)
        {
            float skin = Mathf.Min(sideDashCollisionSkin, target.Settings.Radius * .25f);
            float radius = Mathf.Max(.01f, target.Settings.Radius - skin);
            float height = Mathf.Max(radius * 2f, target.Settings.Height - skin * 2f);
            if (target.CapsuleCast(sideDashCast, data.TargetPosition + Vector3.up * skin, radius, height,
                travel / length, length + sideDashCollisionSkin, QueryTriggerInteraction.Ignore, true))
            {
                for (int i = 0; i < sideDashCast.ColliderHitCount; i++)
                {
                    RaycastHit hit = sideDashCast.ColliderHits[i].RaycastHit;
                    if (Vector3.Dot(hit.normal, SideDashDirection) >= -.001f || Vector3.Dot(hit.normal, travel) >= 0f) continue;
                    fraction = Mathf.Min(fraction, Mathf.Clamp01((hit.distance - sideDashCollisionSkin) / length));
                    blocked = true;
                }
            }
        }
        // PlayerMovement applies the common bullet-time multiplier after this call.
        data.ExternalDelta += side * fraction / multiplier;
        if (target.IsInFixedUpdate && (Object.HasStateAuthority || Object.HasInputAuthority))
        {
            SideDashRemaining = blocked ? 0f : Mathf.Max(0f, SideDashRemaining - side.magnitude);
            // Keep Active through the final KCC step to retain mutual exclusion and StepUp suppression.
        }
    }
}
