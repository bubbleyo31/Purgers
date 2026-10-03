#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Purgers.Progression;

/// <summary>Calculates the XP grant for one developer-only level-up.</summary>
public static class DevelopmentLevelUpRules
{
    public static int ExperienceToNextLevel(PlayerExperienceState current)
    {
        if (current.Level < 1 || current.Level == int.MaxValue ||
            current.PendingRewards > 0 || current.Experience < 0)
            return 0;

        int required = PlayerExperienceRules.RequiredExperience(current.Level);
        return current.Experience < required
            ? required - current.Experience
            : 0;
    }
}
#endif
