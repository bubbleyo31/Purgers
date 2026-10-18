using Fusion;

/// <summary>負面控場沿既有 ActionGate 清除／拒絕；保留武器、技能與系統自己的操作鎖。</summary>
public partial class PlayerActionGate
{
    private PlayerHealth controlHealth;
    public bool IsCrowdControlImmune
    {
        get
        {
            if (!IsFusionSpawned) return false;
            if (controlHealth == null) controlHealth = GetComponent<PlayerHealth>();
            return controlHealth != null && controlHealth.IsNotTodayInvulnerable && controlHealth.IsAlive;
        }
    }

    /// <summary>僅 State Authority 於保命觸發時呼叫。舊狀態直接消除，保護結束後不恢復。</summary>
    public void ClearNegativeControlEffects()
    {
        if (!IsFusionSpawned || !Object.HasStateAuthority) return;
        StatusEffectBlockMask = 0;
        explosionSlows.Clear();
        ResetExplosionHudMetadata();
        ExplosionGrappleTimer = ExplosionSlowTimer = TickTimer.None;
        ExplosionSlowStrength = 0f;
        if (IsCaptured) ReleaseCapture(Captor);
    }
}
