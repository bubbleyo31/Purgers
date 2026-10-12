using UnityEngine;

/// <summary>技能初速線性降至零的積分；不含環境摩擦，不保存玩家或網路狀態。</summary>
public static class AbilityMovementRules
{
    /// <summary>distance 是尚未施加的曲線面積；按區間積分避免不同 Tick 頻率改變總距離。</summary>
    public static Vector3 ConsumeLinearDecay(ref Vector3 distance, ref float seconds, float deltaTime)
    {
        if (deltaTime <= 0f) return Vector3.zero;
        if (seconds <= 0f || distance == Vector3.zero)
        {
            distance = Vector3.zero; seconds = 0f; return Vector3.zero;
        }
        float fraction = Mathf.Clamp01(deltaTime / seconds);
        Vector3 delta = distance * (fraction * (2f - fraction));
        distance -= delta;
        seconds = Mathf.Max(0f, seconds - deltaTime);
        if (fraction >= 1f) { distance = Vector3.zero; seconds = 0f; }
        return delta;
    }
}
