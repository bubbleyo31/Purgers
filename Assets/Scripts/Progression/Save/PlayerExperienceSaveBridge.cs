namespace Purgers.Progression
{
    /// <summary>
    /// Only the Host entry has a stable save identity at present. Guest
    /// PlayerRefs are session-scoped and must not be written under that ID.
    /// </summary>
    public static class PlayerExperienceSaveBridge
    {
        public static PlayerExperienceState ReadHost(GameSaveData save)
        {
            PlayerRunProgressionData entry = FindHost(save);
            if (entry == null)
                return new PlayerExperienceState(1, 0, 0);

            entry.Normalize();
            return new PlayerExperienceState(
                entry.PlayerLevel,
                entry.CurrentExperience,
                entry.PendingRewardCount);
        }

        public static void WriteHost(
            GameSaveData save,
            PlayerExperienceState state)
        {
            if (save == null)
                return;

            save.Normalize();
            PlayerRunProgressionData entry = FindHost(save);
            if (entry == null)
            {
                entry = PlayerRunProgressionData.CreateDefault(
                    GameSaveSchema.HostPlayerId);
                save.RunProgression.PlayerProgressionEntries.Add(entry);
            }

            entry.PlayerLevel = System.Math.Max(1, state.Level);
            entry.CurrentExperience = System.Math.Max(0, state.Experience);
            entry.PendingRewardCount = System.Math.Max(0, state.PendingRewards);
        }

        private static PlayerRunProgressionData FindHost(GameSaveData save)
        {
            if (save?.RunProgression?.PlayerProgressionEntries == null)
                return null;

            foreach (PlayerRunProgressionData entry in
                save.RunProgression.PlayerProgressionEntries)
            {
                if (entry != null &&
                    entry.StablePlayerId == GameSaveSchema.HostPlayerId)
                    return entry;
            }

            return null;
        }
    }
}
