using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>既有 ActionGate 的爆炸狀態來源；只封鎖鈎索，地面減速由 PlayerMovement 讀取。</summary>
public partial class PlayerActionGate
{
    [Networked] private TickTimer ExplosionGrappleTimer { get; set; }
    [Networked] private float ExplosionSlowStrength { get; set; }
    [Networked] private TickTimer ExplosionSlowTimer { get; set; }
    private bool explosionStateSpawned;
    private readonly List<ExplosionSlowEntry> explosionSlows = new List<ExplosionSlowEntry>();
    private struct ExplosionSlowEntry { public float Strength; public TickTimer Timer; }
    public bool IsExplosionGrappleBlocked => explosionStateSpawned && Object != null && Object.IsValid && !ExplosionGrappleTimer.ExpiredOrNotRunning(Runner);
    public float ExplosionGroundSpeedMultiplier => explosionStateSpawned && Object != null && Object.IsValid && !ExplosionSlowTimer.ExpiredOrNotRunning(Runner) ? 1f - Mathf.Clamp01(ExplosionSlowStrength) : 1f;
    public bool IsFusionSpawned => explosionStateSpawned && Object != null && Object.IsValid;

    private void InitializeExplosionStatus()
    {
        explosionStateSpawned = true;
        explosionSlows.Clear();
        if (!Object.HasStateAuthority) return;
        ExplosionGrappleTimer = TickTimer.None;
        ExplosionSlowTimer = TickTimer.None;
        ExplosionSlowStrength = 0f;
        ResetExplosionHudMetadata(); BulletTimeHudClock = default;
    }
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        explosionStateSpawned = false;
        explosionSlows.Clear();
    }
    public override void FixedUpdateNetwork()
    {
        if (!IsFusionSpawned || !Object.HasStateAuthority) return;
        var health = GetComponent<PlayerHealth>();
        if (health != null && !health.IsAlive)
        {
            explosionSlows.Clear(); ExplosionGrappleTimer = TickTimer.None;
        }
        TickCaptureLifetime();
        UpdateExplosionSlows();
        TickStatusHud();
    }
    /// <summary>爆炸有效命中後由 State Authority 呼叫。斷鈎保留既有速度，不建立額外釋放加速。</summary>
    public void ApplyExplosionStatus(float grappleSeconds, float slowStrength, float slowSeconds)
    {
        if (!IsFusionSpawned || !Object.HasStateAuthority) return;
        var health = GetComponent<PlayerHealth>();
        if (health == null || !health.IsAlive || IsCrowdControlImmune) return;
        var grapple = GetComponent<PlayerGrapple>();
        if (grapple != null) grapple.CancelFromSpecialAbility(false, false);
        if (grappleSeconds > (ExplosionGrappleTimer.RemainingTime(Runner) ?? 0f))
        {
            ExplosionGrappleTimer = TickTimer.CreateFromSeconds(Runner, grappleSeconds);
            GrappleHudSpan = ExplosionGrappleTimer.RemainingTime(Runner) ?? 0f;
        }
        if (slowStrength > 0f && slowSeconds > 0f)
            explosionSlows.Add(new ExplosionSlowEntry { Strength = Mathf.Clamp01(slowStrength), Timer = TickTimer.CreateFromSeconds(Runner, slowSeconds) });
        UpdateExplosionSlows();
    }
    private void UpdateExplosionSlows()
    {
        float strongest = 0f;
        TickTimer expiry = TickTimer.None;
        TickTimer lastExpiry = TickTimer.None;
        for (int i = explosionSlows.Count - 1; i >= 0; i--)
        {
            var item = explosionSlows[i];
            if (item.Timer.ExpiredOrNotRunning(Runner)) { explosionSlows.RemoveAt(i); continue; }
            if ((item.Timer.RemainingTime(Runner) ?? 0f) > (lastExpiry.RemainingTime(Runner) ?? 0f)) lastExpiry = item.Timer;
            if (item.Strength > strongest || (item.Strength == strongest && (item.Timer.RemainingTime(Runner) ?? 0f) > (expiry.RemainingTime(Runner) ?? 0f)))
            { strongest = item.Strength; expiry = item.Timer; }
        }
        ExplosionSlowStrength = strongest; ExplosionSlowTimer = expiry;
        SlowHudClock = SlowHudClock.Updated(Runner, lastExpiry, lastExpiry.RemainingTime(Runner) ?? 0f);
    }
}
