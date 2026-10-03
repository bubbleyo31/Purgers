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
        {
            if (amount <= 0 || current.PendingRewards > 0)
                return current;

            int level = System.Math.Max(1, current.Level);
            int pending = System.Math.Max(0, current.PendingRewards);
            long experience = System.Math.Max(0, current.Experience);
            experience += amount;

            if (growthMultiplier <= 1)
            {
                int required = RequiredExperience(
                    level, baseRequirement, growthMultiplier);
                long gainedLevels = System.Math.Min(
                    experience / required,
                    System.Math.Min((long)int.MaxValue - level,
                        (long)int.MaxValue - pending));
                level += (int)gainedLevels;
                pending += (int)gainedLevels;
                experience -= gainedLevels * required;
            }

            while (level < int.MaxValue && pending < int.MaxValue)
            {
                int required = RequiredExperience(
                    level, baseRequirement, growthMultiplier);
                if (experience < required)
                    break;

                experience -= required;
                level++;
                pending++;
            }

            return new PlayerExperienceState(
                level,
                (int)System.Math.Min(int.MaxValue, experience),
                pending);
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
