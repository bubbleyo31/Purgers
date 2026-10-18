using UnityEngine;

/// <summary>鈎索側移的輸入、能量與有限增量規則；不持有玩家或網路狀態。</summary>
public static class GrappleSideDashRules
{
    public static bool IsDoubleTap(int previous, int current, float interval, float window) =>
        (current == -1 || current == 1) && previous == current &&
        !float.IsNaN(interval) && interval >= 0f && interval <= Mathf.Max(0f, window);

    public static float EnergyAfterDash(float energy, float launchCost) =>
        Mathf.Max(0f, energy - Mathf.Max(0f, launchCost) * 2f);

    public static Vector3 Direction(int side, float yaw) =>
        Quaternion.Euler(0f, yaw, 0f) * (side < 0 ? Vector3.left : Vector3.right);

    public static Vector3 SideDelta(Vector3 direction, float remaining, float speed, float dt, float multiplier) =>
        direction * Mathf.Min(Mathf.Max(0f, remaining), Mathf.Max(0f, speed) * Mathf.Max(0f, dt) * Mathf.Clamp01(multiplier));
}
