using UnityEngine;

/// <summary>LGG 色偏加在既有畫面基底；w 保持亮度語意，不當作 alpha。</summary>
public static class AbilityColorGradingRules
{
    public static readonly Vector4 Neutral = new Vector4(1f, 1f, 1f, 0f);
    public static Vector4 AddTint(Vector4 baseline, Vector4 authoredTint, float weight) =>
        baseline + (authoredTint - Neutral) * Mathf.Clamp01(weight);
    /// <summary>依權威剩餘時間顯示平滑黃光脈衝；到期立即歸零，不建立第二套技能計時。</summary>
    public static float NotTodayPulse(float remaining, float duration, float period)
    {
        if (remaining <= 0f || duration <= 0f) return 0f;
        float elapsed = Mathf.Max(0f, duration - remaining);
        return .5f + .5f * Mathf.Cos(2f * Mathf.PI * elapsed / Mathf.Max(.1f, period));
    }
    public static float HealingPulse(float elapsed, float duration) =>
        duration <= 0f || elapsed <= 0f || elapsed >= duration ? 0f : Mathf.Sin(Mathf.PI * elapsed / duration);
}
