namespace Purgers.Progression
{
    public readonly struct PlayerExperienceState
    {
        public readonly int Level;
        public readonly int Experience;
        public readonly int PendingRewards;

        public PlayerExperienceState(int level, int experience, int pendingRewards)
        {
            Level = level;
            Experience = experience;
            PendingRewards = pendingRewards;
        }
    }

    public static class PlayerExperienceRules
    {
        /// <summary>One row per level transition; the last row repeats indefinitely.</summary>
        public static int RequiredExperience(int level, System.Collections.Generic.IReadOnlyList<int> requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return RequiredExperience(level);
            int index = System.Math.Min(System.Math.Max(1, level) - 1, requirements.Count - 1);
            return System.Math.Max(1, requirements[index]);
        }

        public static PlayerExperienceState Award(PlayerExperienceState current, int amount,
            System.Collections.Generic.IReadOnlyList<int> requirements)
            => AwardCore(current, amount, requirements, DefaultBaseRequirement, DefaultGrowthMultiplier);
        public const int DefaultBaseRequirement = 5;
        public const int DefaultGrowthMultiplier = 2;
        public const int DefaultKillerBonus = 1;

        public static int KillGrantForPlayer(
            int baseExperience,
            bool hasPlayerKiller,
            bool isConnected,
            bool isAlive,
            bool isKiller,
            int killerBonus = DefaultKillerBonus)
        {
            if (baseExperience <= 0 || !hasPlayerKiller ||
                !isConnected || !isAlive)
                return 0;

            long amount = (long)baseExperience +
                (isKiller ? System.Math.Max(0, killerBonus) : 0);
            return (int)System.Math.Min(int.MaxValue, amount);
        }

        public static int RequiredExperience(
            int level,
            int baseRequirement = DefaultBaseRequirement,
            int growthMultiplier = DefaultGrowthMultiplier)
        {
            long required = System.Math.Max(1, baseRequirement);
            long growth = System.Math.Max(1, growthMultiplier);
            if (growth == 1)
                return (int)required;
            for (int i = 1; i < System.Math.Max(1, level); i++)
            {
                required *= growth;
                if (required >= int.MaxValue)
                    return int.MaxValue;
            }

            return (int)required;
        }

        public static PlayerExperienceState Award(
            PlayerExperienceState current,
            int amount,
            int baseRequirement = DefaultBaseRequirement,
            int growthMultiplier = DefaultGrowthMultiplier)
            => AwardCore(current, amount, null, baseRequirement, growthMultiplier);

        private static PlayerExperienceState AwardCore(
            PlayerExperienceState current, int amount,
            System.Collections.Generic.IReadOnlyList<int> requirements,
            int baseRequirement, int growthMultiplier)
        {
            if (amount <= 0)
                return current;

            int level = System.Math.Max(1, current.Level);
            int pending = System.Math.Max(0, current.PendingRewards);
            long experience = (long)System.Math.Max(0, current.Experience) + amount;
            bool hasTable = requirements != null && requirements.Count > 0;

            while (level < int.MaxValue && pending < int.MaxValue)
            {
                int required = hasTable
                    ? RequiredExperience(level, requirements)
                    : RequiredExperience(level, baseRequirement, growthMultiplier);
                if (experience < required)
                    break;

                // Constant tail must not iterate billions of levels for a large grant.
                bool constantTail = hasTable ? level >= requirements.Count : growthMultiplier <= 1;
                long gained = constantTail ? experience / required : 1;
                gained = System.Math.Min(gained, System.Math.Min(
                    (long)int.MaxValue - level, (long)int.MaxValue - pending));
                level += (int)gained;
                pending += (int)gained;
                experience -= gained * required;
                if (constantTail)
                    break;
            }

            return new PlayerExperienceState(level,
                (int)System.Math.Min(int.MaxValue, experience), pending);
        }
        public static PlayerExperienceState ClaimReward(
            PlayerExperienceState current)
        {
            return current.PendingRewards > 0
                ? new PlayerExperienceState(
                    current.Level,
                    current.Experience,
                    current.PendingRewards - 1)
                : current;
        }
    }
}
