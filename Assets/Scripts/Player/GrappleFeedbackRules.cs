/// <summary>鈎索呈現條件；只讀玩法快照，不決定鈎索移動或能量。</summary>
public static class GrappleFeedbackRules
{
    public static bool ShouldPlayPull(bool spawned, GrapplePhase phase,
        bool supportTether, bool appliedPull, bool alive)
    {
        return spawned && alive && phase == GrapplePhase.Attached && !supportTether && appliedPull;
    }
}
