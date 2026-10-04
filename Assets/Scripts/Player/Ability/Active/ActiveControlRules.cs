using Fusion;
using UnityEngine;

/// <summary>衝刺、擊退與暫停的純規則；不改寫玩家或敵人狀態。</summary>
public static class ActiveControlRules
{
    public static Vector3 ResolveDashDirection(Vector2 input, float characterYaw)
    {
        Vector3 local = new Vector3(input.x, 0f, input.y);
        if (local.sqrMagnitude < 0.0001f) local = Vector3.forward;
        return Quaternion.Euler(0f, characterYaw, 0f) * local.normalized;
    }

    public static float LimitTravel(float remaining, float speed, float deltaTime, float hitDistance, float skin)
    {
        float requested = Mathf.Min(Mathf.Max(0f, remaining), Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime));
        return Mathf.Min(requested, Mathf.Max(0f, hitDistance - Mathf.Max(0f, skin)));
    }

    public struct DashStep
    {
        public float Velocity;
        public float Travel;
        public bool ObstacleReached;
    }

    // 回傳 Movement 套用慢速倍率前的速度；撞牆判定使用本 Tick 真正行程。
    public static DashStep ResolveDashStep(float remaining, float speed, float deltaTime,
        float movementMultiplier, float hitDistance, float skin)
    {
        float effectiveTime = Mathf.Max(0f, deltaTime) * Mathf.Clamp(movementMultiplier, .01f, 1f);
        float step = Mathf.Min(Mathf.Max(0f, remaining), Mathf.Max(0f, speed) * effectiveTime);
        float allowed = LimitTravel(remaining, speed, effectiveTime, hitDistance, skin);
        return new DashStep {
            Velocity = effectiveTime > 0f ? allowed / effectiveTime : 0f,
            Travel = allowed,
            ObstacleReached = hitDistance <= step + Mathf.Max(0f, skin)
        };
    }

    public static bool IsWallImpact(Vector3 normal, Vector3 movementDirection) =>
        normal.sqrMagnitude > 0.0001f && Mathf.Abs(normal.normalized.y) < 0.65f &&
        Vector3.Dot(normal.normalized, movementDirection.normalized) < -0.01f;

    public static int ExtendPendingDeadline(int targetTick, int currentTick, int pausedTicks) =>
        targetTick > currentTick ? targetTick + Mathf.Max(0, pausedTicks) : targetTick;

    public static TickTimer PauseTimerForOneTick(TickTimer timer, NetworkRunner runner)
    {
        int remaining = timer.RemainingTicks(runner) ?? 0;
        return remaining > 0 ? TickTimer.CreateFromTicks(runner, remaining + 1) : timer;
    }
}
