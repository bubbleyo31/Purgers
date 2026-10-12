using UnityEngine;

/// <summary>槍械與技能共用既有回正规則；手動抵銷只使用真正的輸入額度，不猜測其他系統的視角變化。</summary>
public static class WeaponRecoilRecovery
{
    /// <summary>同一 Tick 每一度手動下壓最多抵銷一度已施加的向上後座力，依既有模擬順序分配。</summary>
    public static float ConsumeManualPitch(ref float availablePitch, float accumulatedPitch)
    {
        float used = Mathf.Min(Mathf.Max(0f, availablePitch), Mathf.Max(0f, -accumulatedPitch));
        availablePitch -= used;
        return used;
    }
    /// <summary>先扣手動抵銷，再等待施加結清與延遲到期；回正不超過剩餘向上角度。</summary>
    public static float Step(ref float accumulatedPitch, float manualPitch, bool hasPending,
        bool delayExpired, float recoverySpeed, float deltaTime)
    {
        if (accumulatedPitch >= 0f) return 0f;
        accumulatedPitch = Mathf.Min(0f, accumulatedPitch + Mathf.Max(0f, manualPitch));
        if (hasPending || !delayExpired) return 0f;
        float recovered = Mathf.Min(-accumulatedPitch, Mathf.Max(0f, recoverySpeed) * Mathf.Max(0f, deltaTime));
        accumulatedPitch += recovered;
        return recovered;
    }
}
