using UnityEngine;

/// <summary>事件刷新包絡，從目前亮度接續，不依賴玩法計時、不累加。</summary>
public sealed class ScreenEdgePulse
{
    private float started = float.NegativeInfinity;
    private float from;
    public void Clear() { started = float.NegativeInfinity; from = 0; }
    public static bool IsPositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
    public void Trigger(float now, ScreenEdgeStyle style, float amount)
    {
        if (!IsPositive(amount) || float.IsNaN(now) || float.IsInfinity(now)) return;
        from = Evaluate(now, style); started = now;
    }
    public float Evaluate(float now, ScreenEdgeStyle s)
    {
        if (s == null || float.IsNegativeInfinity(started)) return 0;
        float t = Mathf.Max(0, now - started), attack = Mathf.Max(.01f, s.fadeIn);
        if (t < attack) return Mathf.Lerp(from, 1, Mathf.SmoothStep(0,1,t/attack));
        t -= attack;
        if (t < Mathf.Max(0,s.hold)) return 1;
        return 1-Mathf.SmoothStep(0,1,(t-Mathf.Max(0,s.hold))/Mathf.Max(.01f,s.fadeOut));
    }
    public static Vector3 Prioritize(float healing, float damage, float momentum, float suppression)
    {
        float d = Safe01(damage), h = Safe01(healing), m = Safe01(momentum), s = Safe01(suppression);
        return new Vector3(h*(1-d*s), d, m*(1-Mathf.Max(h,d)*s));
    }
    public static float Safe01(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
    // Read the same normalized gauge as the HUD; partial charge never starts the effect.
    public static float FullMomentumWeight(float energy) => Safe01(energy) >= 1f ? 1f : 0f;
}
