using UnityEngine;

/// <summary>捕食的純規則。時間以抵達嘴邊為零點；總傷害不隨間隔改變。</summary>
public static class EnemyFlytrapRules
{
    public static bool IsUnsafe(bool crouched, bool moving, bool firing, bool grapple) =>
        firing || grapple || (!crouched && moving);

    public static float TotalDamage(float maximumHealth, int roomPlayers) =>
        Mathf.Max(0f, maximumHealth) * (roomPlayers <= 1 ? .5f : 1f);

    public static float DueFraction(float elapsed, float duration, float interval)
    {
        if (float.IsNaN(elapsed) || float.IsNaN(duration) || float.IsNaN(interval)) return 0f;
        duration = Mathf.Max(.01f, duration);
        interval = Mathf.Max(.01f, interval);
        if (elapsed >= duration) return 1f;
        if (elapsed < interval) return 0f;
        // 小容差只抵銷浮點表示誤差，並不提早一個模擬 Tick。
        float paidTime = Mathf.Floor((elapsed + .000001f) / interval) * interval;
        return Mathf.Clamp01(paidTime / duration);
    }
}
