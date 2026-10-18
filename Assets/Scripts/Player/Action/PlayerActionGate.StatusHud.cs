using Fusion;

public partial class PlayerActionGate
{
    [Networked] private float GrappleHudSpan { get; set; }
    [Networked] private StatusEffectClock SlowHudClock { get; set; }
    [Networked] private StatusEffectClock BulletTimeHudClock { get; set; }
    private Player statusHudPlayer;
    public StatusEffectSnapshot GrappleBlockedStatus => IsFusionSpawned
        ? StatusEffectSnapshot.FromTimer(StatusEffectKind.GrappleBlocked, Runner, ExplosionGrappleTimer, GrappleHudSpan) : default;
    public StatusEffectSnapshot SlowStatus => IsFusionSpawned ? SlowHudClock.Snapshot(StatusEffectKind.Slow, Runner) : default;
    public StatusEffectSnapshot BulletTimeStatus => IsFusionSpawned ? BulletTimeHudClock.Snapshot(StatusEffectKind.BulletTime, Runner) : default;
    public StatusEffectSnapshot CapturedStatus
    {
        get
        {
            if (!IsFusionSpawned || !IsCaptured || Captor == null || !Captor.IsValid) return default;
            var source = Captor.GetComponent<EnemyFlytrapAttack>();
            return source != null && source.Ready && source.Runner == Runner && source.CapturedPlayer == Object &&
                source.Phase == EnemyFlytrapPhase.Feeding
                ? StatusEffectSnapshot.FromProgress(StatusEffectKind.Captured, 1f - source.FeedingProgress) : default;
        }
    }
    private void TickStatusHud()
    {
        if (statusHudPlayer == null) statusHudPlayer = GetComponent<Player>();
        if (statusHudPlayer == null || statusHudPlayer.Health == null || statusHudPlayer.Health.IsDead)
        { BulletTimeHudClock = default; return; }
        PlayerBulletTimeSessionRegistry.GetPlayerStatusDeadline(statusHudPlayer, out var timer, out float span);
        BulletTimeHudClock = BulletTimeHudClock.Updated(Runner, timer, span);
    }
    private void ResetExplosionHudMetadata() { GrappleHudSpan = 0f; SlowHudClock = default; }
}
