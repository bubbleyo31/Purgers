namespace Purgers.GameFlow.SafeHouse
{
    public enum SafeHousePhase : byte
    {
        WaitingForHost = 0,
        ReadyCheck = 1,
        LoadingStage = 2
    }

    public static class SafeHouseReadyRules
    {
        public static bool CanOpenReadyCheck(
            SafeHousePhase phase,
            bool requesterIsHost,
            bool requesterIsInRange)
        {
            return phase == SafeHousePhase.WaitingForHost &&
                   requesterIsHost &&
                   requesterIsInRange;
        }

        public static bool CanAcceptReady(
            SafeHousePhase phase,
            bool alreadyReady)
        {
            return phase == SafeHousePhase.ReadyCheck &&
                   !alreadyReady;
        }

        public static bool AreAllPlayersReady(
            int connectedPlayerCount,
            int readyPlayerCount)
        {
            return connectedPlayerCount > 0 &&
                   readyPlayerCount == connectedPlayerCount;
        }
    }
}
