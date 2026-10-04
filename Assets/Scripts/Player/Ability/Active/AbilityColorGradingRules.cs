using UnityEngine;

/// <summary>LGG 色偏加在既有畫面基底；w 保持亮度語意，不當作 alpha。</summary>
public static class AbilityColorGradingRules
{
    public static readonly Vector4 Neutral = new Vector4(1f, 1f, 1f, 0f);
    public static Vector4 AddTint(Vector4 baseline, Vector4 authoredTint, float weight) =>
        baseline + (authoredTint - Neutral) * Mathf.Clamp01(weight);
    public static float HealingPulse(float elapsed, float duration) =>
        duration <= 0f || elapsed <= 0f || elapsed >= duration ? 0f : Mathf.Sin(Mathf.PI * elapsed / duration);
}
