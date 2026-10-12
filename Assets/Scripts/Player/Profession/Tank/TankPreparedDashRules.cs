using UnityEngine;

/// <summary>預備衝刺的觸發、幾何與動畫期限；不決定傷害或位移所有權。</summary>
public static class TankPreparedDashRules
{
    public static bool ShouldDash(bool wasFrozen, bool held) => wasFrozen && !held;

    public static float Duration(TankMeleeAttackDefinition definition) =>
        definition != null && definition.UseAnimationFrames && definition.HasValidFrameTiming
            ? Mathf.Max(0f, definition.ResolvedStartup - definition.FrameTiming.FrameToSeconds(definition.PreparationFrame)) : 0f;

    public static Vector3 Destination(Vector3 surface, Vector3 approach, float stopDistance, Vector3 aimOffset) =>
        surface - approach.normalized * Mathf.Max(0f, stopDistance) - aimOffset;

    public static Vector3 Velocity(Vector3 current, Vector3 destination, float speed, float deltaTime)
    {
        Vector3 delta = destination - current;
        if (deltaTime <= 0f || delta.sqrMagnitude < .000001f) return Vector3.zero;
        return delta.normalized * Mathf.Min(Mathf.Max(0f, speed), delta.magnitude / deltaTime);
    }

    /// <summary>每步依剩餘距離重新計算；最後一步不越過終點。抵銷既有 KCC 正值移速倍率，零倍率仍不移動。</summary>
    public static Vector3 VelocityForDeadline(Vector3 current, Vector3 destination, float remainingSeconds,
        float deltaTime, float movementMultiplier)
    {
        if (deltaTime <= 0f || movementMultiplier <= 0f) return Vector3.zero;
        return (destination - current) / Mathf.Max(deltaTime, remainingSeconds) / movementMultiplier;
    }
}
