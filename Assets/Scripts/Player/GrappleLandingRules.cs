/// <summary>普通鉤索落地啟動與方向釋放的純判定；狀態由 PlayerGrapple 的 Fusion 快照持有。</summary>
public static class GrappleLandingRules
{
    public static bool IsApproaching(float speedTowardPoint, float threshold)
    {
        return speedTowardPoint > 0f && speedTowardPoint >= threshold;
    }

    public static bool HasPassedPoint(bool hasApproached, float speedTowardPoint, float awayThreshold)
    {
        return hasApproached && speedTowardPoint < 0f && speedTowardPoint <= -awayThreshold;
    }

    public static bool ObserveAirborne(bool landingArmed, bool hasAppliedPull, bool isGrounded)
    {
        return landingArmed || (hasAppliedPull && !isGrounded);
    }

    public static bool ShouldReleaseOnLanding(bool landingArmed, bool isGrounded, bool enabled)
    {
        return enabled && landingArmed && isGrounded;
    }

    public static bool ShouldRequestStartupJump(bool landingArmed, bool jumpAttempted, bool isGrounded)
    {
        return !landingArmed && !jumpAttempted && isGrounded;
    }

    public static bool ShouldAbortStartup(bool landingArmed, bool isGrounded, bool timeoutExpired)
    {
        return !landingArmed && isGrounded && timeoutExpired;
    }
}
