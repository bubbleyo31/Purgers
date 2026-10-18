using Fusion;

public sealed partial class EnemyStateController
{
    [Networked] private StatusEffectClock BulletTimeHudClock { get; set; }
    public StatusEffectSnapshot BulletTimeStatus => fusionSpawned && Object != null && Object.IsValid && IsAlive
        ? BulletTimeHudClock.Snapshot(StatusEffectKind.BulletTime, Runner) : default;
    private void TickStatusHud()
    {
        if (!IsAlive) { BulletTimeHudClock = default; return; }
        PlayerBulletTimeSessionRegistry.GetEnemyStatusDeadline(ResolveAbilityActor(), out var deadline, out float span);
        BulletTimeHudClock = BulletTimeHudClock.Updated(Runner, deadline, span);
    }
}
