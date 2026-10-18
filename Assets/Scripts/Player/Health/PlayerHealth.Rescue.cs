using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;
using Purgers.GameFlow.Control;

/// <summary>既有生命 owner 的倒地／救援狀態。GameLogic 在每個權威 Tick 集中協調。</summary>
public partial class PlayerHealth
{
    [Header("倒地救援")]
    [SerializeField, Min(0.1f), Tooltip("倒地後未受救援時的剩餘秒數；預設 30 秒，救援期間暫停。")]
    private float downedLifetimeSeconds = 30f;
    [SerializeField, Min(0.1f), Tooltip("同房間沒有健康玩家後的死亡等待秒數；預設 2 秒，不延長原本更短的倒地期限。")]
    private float noHealthyMemberSeconds = 2f;
    [SerializeField, Min(0.1f), Tooltip("持續按住 E 完成救援的秒數；預設 5 秒，中斷歸零，多人不加速。")]
    private float rescueDurationSeconds = 5f;
    [SerializeField, Range(0.01f, 1f), Tooltip("救起後恢復最大生命比例；預設 0.35。普通治療不能救起倒地玩家。")]
    private float rescuedHealthFraction = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("救援者與倒地者 Root 的最大距離，公尺；預設 2.5。超距立即中斷。")]
    private float rescueDistance = 2.5f;
    [SerializeField, Range(0.01f, 1f), Tooltip("倒地爬行的普通移動倍率；預設 0.25，仍疊加既有蹲姿倍率。不允許跳躍、衝刺或滑鏟。")]
    private float crawlInputMultiplier = 0.25f;
    [SerializeField, Min(0f), Tooltip("救起後原有仇恨重新鎖定的等待秒數；預設 5。正常視野、攻擊距離或攻擊警戒可提前鎖定。")]
    private float previousAggroDelaySeconds = 5f;
    [SerializeField, Tooltip("救援遮擋層，預設所有非 Trigger 碰撞；忽略救援雙方與其他玩家／敵人，牆壁會中斷。")]
    private LayerMask rescueObstructionMask = ~0;

    [Networked] public NetworkBool IsDowned { get; private set; }
    [Networked] public float DownedRemainingSeconds { get; private set; }
    [Networked] public NetworkObject Rescuer { get; private set; }
    [Networked] public NetworkObject RescueTarget { get; private set; }
    [Networked] public float RescueElapsedSeconds { get; private set; }
    [Networked] public TickTimer PreviousAggroRecoveryTimer { get; private set; }
    [Networked] private TickTimer NoHealthyMemberTimer { get; set; }
    [Networked] private NetworkBool RescueEConsumed { get; set; }
    private readonly RaycastHit[] rescueHits = new RaycastHit[32];
    private bool rescueSpawned;
#if UNITY_EDITOR
    public string RescueDebugLastInterruption { get; private set; }
#endif
    public float CrawlInputMultiplier => Mathf.Clamp01(crawlInputMultiplier);
    public float RescueDurationSeconds => Mathf.Max(0.1f, rescueDurationSeconds);
    public float RescueProgress => IsRescueReady ? Mathf.Clamp01(RescueElapsedSeconds / RescueDurationSeconds) : 0f;
    public bool IsRescueReady => rescueSpawned && Object != null && Object.IsValid && Runner != null;
    public bool UsesTeammateRescue => IsRescueReady && GameLogic.GetPrimaryForRunner(Runner)?.UsesTeammateRescue == true;
    public float DisplayDownedSeconds => !IsRescueReady ? 0f : NoHealthyMemberTimer.IsRunning
        ? Mathf.Min(DownedRemainingSeconds, Mathf.Max(0f, NoHealthyMemberTimer.RemainingTime(Runner) ?? 0f)) : DownedRemainingSeconds;
    public PlayerActionBlockMask RescueBlockedActions => !IsRescueReady || (!IsDowned && !IsDead && RescueTarget == null)
        ? PlayerActionBlockMask.None
        : PlayerActionBlockMask.Fire | PlayerActionBlockMask.Aim | PlayerActionBlockMask.Reload |
          PlayerActionBlockMask.Grapple | PlayerActionBlockMask.QuickAction | PlayerActionBlockMask.ActiveAbility;

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        rescueSpawned = false;
    }

    private void ResetRescueState()
    {
        rescueSpawned = true;
        IsDowned = false; DownedRemainingSeconds = 0; Rescuer = null; RescueTarget = null;
        RescueElapsedSeconds = 0; RescueEConsumed = false;
        NoHealthyMemberTimer = TickTimer.None; PreviousAggroRecoveryTimer = TickTimer.None;
    }

    private void BeginDownedStateAuthority()
    {
        CancelOutgoingRescue();
        IsDowned = true;
        SelfRescueElapsed = 0;
        GameLogic.GetPrimaryForRunner(Runner)?.LoseDownedPassives(Object.InputAuthority);
        DownedRemainingSeconds = Mathf.Max(0.1f, downedLifetimeSeconds);
        NoHealthyMemberTimer = TickTimer.None;
        PreviousAggroRecoveryTimer = TickTimer.None;
        InterruptActions();
        // 在清掉目標前保留各敵人的舊仇恨；只有原本鎖定自己的敵人才會延後重鎖。
        foreach (var perception in FindObjectsOfType<EnemyPerceptionController>())
            if (perception.Runner == Runner) perception.RememberDownedTarget(Object);
    }

    private void InterruptActions()
    {
        var player = GetComponent<Player>();
        if (player == null) return;
        player.Grapple?.CancelFromSpecialAbility(false, true);
        player.Grapple?.CancelReleaseMomentumFromSpecialAbility(true);
        player.SupportGrapplePlayerPullReceiver?.CancelPull();
        player.SlideController?.CancelForCapture();
        player.ProfessionRuntimeManager?.InterruptForCapture();
        var gate = GetComponent<PlayerActionGate>();
        if (gate != null && gate.IsCaptured) gate.ReleaseCapture(gate.Captor);
        for (int i = 0; i < PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; i++)
            player.AbilityRuntimeManager?.GetEquippedRuntimeAtSlot(i)?.RefreshProfessionAvailabilityNow();
        var kcc = player.Movement?.KCC;
        if (kcc != null)
        {
            kcc.SetInputDirection(Vector3.zero); kcc.SetKinematicVelocity(Vector3.zero);
            kcc.SetDynamicVelocity(Vector3.zero); kcc.FixedData.ExternalDelta = Vector3.zero;
        }
    }

    /// <summary>地圖外／死亡區使用；只有權威可呼叫，倒地與健康玩家皆直接死亡。</summary>
    public bool KillUnrescuable()
    {
        if (!IsRescueReady || !Object.HasStateAuthority || IsDead) return false;
        CancelOutgoingRescue();
        ClearRescuer();
        GameLogic.GetPrimaryForRunner(Runner)?.LoseDownedPassives(Object.InputAuthority);
        SelfRescueElapsed = 0;
        CurrentHealth = 0; IsDowned = false; NetworkIsDead = true; DownedRemainingSeconds = 0;
        NoHealthyMemberTimer = TickTimer.None; PreviousAggroRecoveryTimer = TickTimer.None;
        InterruptActions();
        Died?.Invoke();
        return true;
    }

    public void FilterRescueInput(ref NetInput input)
    {
        if (!IsRescueReady) return;
        bool held = input.Buttons.IsSet(InputButton.Ability1);
        if (!held) RescueEConsumed = false;
        if (IsDowned || IsDead)
        {
            input.Buttons = default;
            input.Buttons.Set(InputButton.Crouch, IsDowned);
            if (IsDead) input.Direction = Vector2.zero;
            return;
        }
        if (held && (RescueTarget != null || FindRescueCandidate() != null)) RescueEConsumed = true;
        if (RescueEConsumed) input.Buttons.Set(InputButton.Ability1, false);
        if (RescueTarget != null)
        {
            input.Buttons.Set(InputButton.Fire, false); input.Buttons.Set(InputButton.Aim, false);
            input.Buttons.Set(InputButton.Reload, false); input.Buttons.Set(InputButton.Grapple, false);
            input.Buttons.Set(InputButton.QuickAction, false);
        }
    }

    private bool ReadRescueHold()
    {
        if (!IsRescueReady || !IsAlive || !UsesTeammateRescue ||
            GetComponent<PlayerActionGate>()?.IsCaptured == true || !GetInput(out NetInput input)) return false;
        return !PlayerControlLocks.BlocksCombat(input.BlockedControls) &&
            input.Buttons.IsSet(InputButton.Ability1);
    }

    public PlayerHealth FindRescueCandidate()
    {
        if (!IsRescueReady || !IsAlive || !UsesTeammateRescue) return null;
        if (RescueTarget != null && RescueTarget.IsValid && RescueTarget.TryGetComponent<PlayerHealth>(out var old) && CanReach(old)) return old;
        PlayerHealth best = null; float bestDistance = float.PositiveInfinity;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out var obj) || obj == null || !obj.IsValid || obj == Object ||
                !obj.TryGetComponent<PlayerHealth>(out var target) || !CanReach(target)) continue;
            float distance = (transform.position - target.transform.position).sqrMagnitude;
            if (distance < bestDistance) { best = target; bestDistance = distance; }
        }
        return best;
    }

    private bool CanReach(PlayerHealth target)
    {
        if (target == null || !target.IsRescueReady || target.Runner != Runner || !target.IsDowned || target.IsDead ||
            (target.Rescuer != null && target.Rescuer != Object) ||
            !Runner.TryGetPlayerObject(target.Object.InputAuthority, out var current) || current != target.Object ||
            Vector3.Distance(transform.position, target.transform.position) > Mathf.Max(0.1f, rescueDistance)) return false;
        Vector3 start = transform.position + Vector3.up * 0.6f;
        Vector3 offset = target.transform.position + Vector3.up * 0.6f - start;
        if (offset.sqrMagnitude < 0.0001f) return true;
        int count = Runner.GetPhysicsScene().Raycast(start, offset.normalized, rescueHits, offset.magnitude,
            rescueObstructionMask, QueryTriggerInteraction.Ignore);
        if (count == rescueHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = rescueHits[i].collider;
            if (hit == null || hit.GetComponentInParent<Player>() != null || hit.GetComponentInParent<EnemyActor>() != null) continue;
            return false;
        }
        return true;
    }

    internal void ValidateRescuerStateAuthority()
    {
        if (!IsRescueReady || !Object.HasStateAuthority || Rescuer == null) return;
        if (!Rescuer.IsValid || !Rescuer.TryGetComponent<PlayerHealth>(out var helper) ||
            !Runner.TryGetPlayerObject(Rescuer.InputAuthority, out var current) || current != Rescuer ||
            !helper.ReadRescueHold() || !helper.CanReach(this))
        {
#if UNITY_EDITOR
            var diagnostic = Rescuer != null && Rescuer.IsValid ? Rescuer.GetComponent<PlayerHealth>() : null;
            if (diagnostic == null) RescueDebugLastInterruption = "invalid rescuer";
            else
            {
                bool hasInput = diagnostic.GetInput(out NetInput last);
                RescueDebugLastInterruption = $"input={hasInput}, E={last.Buttons.IsSet(InputButton.Ability1)}, healthy={diagnostic.IsAlive}, reachable={diagnostic.CanReach(this)}, distance={Vector3.Distance(transform.position, diagnostic.transform.position):F2}, blocks={last.BlockedControls}";
            }
#endif
            ClearRescuer();
        }
    }

    internal void TickRescueIntentStateAuthority()
    {
        if (!IsRescueReady || !Object.HasStateAuthority) return;
        if (!ReadRescueHold()) { CancelOutgoingRescue(); return; }
        var target = FindRescueCandidate();
        if (target == null) { CancelOutgoingRescue(); return; }
        if (RescueTarget != target.Object)
        {
            CancelOutgoingRescue();
            RescueTarget = target.Object;
            target.Rescuer = Object;
            target.RescueElapsedSeconds = 0;
            RescueEConsumed = true;
            InterruptActions();
        }
    }

    private void CancelOutgoingRescue()
    {
        if (RescueTarget != null && RescueTarget.IsValid && RescueTarget.TryGetComponent<PlayerHealth>(out var target) && target.Rescuer == Object)
        { target.Rescuer = null; target.RescueElapsedSeconds = 0; }
        RescueTarget = null;
    }

    private void ClearRescuer()
    {
        if (Rescuer != null && Rescuer.IsValid && Rescuer.TryGetComponent<PlayerHealth>(out var helper) && helper.RescueTarget == Object)
            helper.RescueTarget = null;
        Rescuer = null; RescueElapsedSeconds = 0;
    }

    internal void TickDownedStateAuthority(bool hasHealthyMember)
    {
        if (!IsRescueReady || !Object.HasStateAuthority || !IsDowned) return;
        if (hasHealthyMember) NoHealthyMemberTimer = TickTimer.None;
        else if (!NoHealthyMemberTimer.IsRunning)
            NoHealthyMemberTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, noHealthyMemberSeconds));
        if (NoHealthyMemberTimer.Expired(Runner)) { KillUnrescuable(); return; }
        bool rescued = Rescuer != null && Rescuer.IsValid;
        bool selfRescuing = TickSelfRescue(rescued);
        if (!IsDowned) return;
        DownedRemainingSeconds = PlayerRescueRules.Remaining(DownedRemainingSeconds, Runner.DeltaTime, rescued || selfRescuing);
        if (DownedRemainingSeconds <= 0) { KillUnrescuable(); return; }
        if (!rescued) return;
        RescueElapsedSeconds += Runner.DeltaTime;
        if (RescueElapsedSeconds + 0.00001f < RescueDurationSeconds) return;
        FinishRescueStateAuthority();
    }
}
