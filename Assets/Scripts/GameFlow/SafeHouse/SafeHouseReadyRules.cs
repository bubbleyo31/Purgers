using Fusion;

namespace Purgers.GameFlow.SafeHouse
{
    public readonly struct SafeHousePlayerReadyState
    {
        public PlayerRef Player { get; }
        public bool IsReady { get; }

        public SafeHousePlayerReadyState(PlayerRef player, bool isReady)
        {
            Player = player;
            IsReady = isReady;
        }
    }

    public enum SafeHousePhase : byte
    {
        WaitingForHost = 0,
        ReadyCheck = 1,
        LoadingStage = 2,
        Countdown = 3
    }

    public enum SafeHouseReadyCountdownDecision : byte
    {
        None = 0,
        Start = 1,
        Cancel = 2,
        Load = 3
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
            return phase == SafeHousePhase.ReadyCheck ||
                   phase == SafeHousePhase.Countdown;
        }

        public static bool CanCancelReadyCheck(
            SafeHousePhase phase,
            bool requesterIsConnected)
        {
            return requesterIsConnected &&
                   (phase == SafeHousePhase.ReadyCheck ||
                    phase == SafeHousePhase.Countdown);
        }

        public static bool ShouldCancelExpiredReadyCheck(
            SafeHousePhase phase,
            bool timeoutExpired)
        {
            return timeoutExpired &&
                   (phase == SafeHousePhase.ReadyCheck ||
                    phase == SafeHousePhase.Countdown);
        }

        public static bool AreAllPlayersReady(
            int connectedPlayerCount,
            int readyPlayerCount)
        {
            return connectedPlayerCount > 0 &&
                   readyPlayerCount == connectedPlayerCount;
        }

        public static SafeHouseReadyCountdownDecision EvaluateCountdown(
            SafeHousePhase phase,
            int connectedPlayerCount,
            int readyPlayerCount,
            bool countdownExpired)
        {
            bool allPlayersReady = AreAllPlayersReady(
                connectedPlayerCount,
                readyPlayerCount);

            if (phase == SafeHousePhase.ReadyCheck)
            {
                return allPlayersReady
                    ? SafeHouseReadyCountdownDecision.Start
                    : SafeHouseReadyCountdownDecision.None;
            }

            if (phase != SafeHousePhase.Countdown)
                return SafeHouseReadyCountdownDecision.None;
            if (!allPlayersReady)
                return SafeHouseReadyCountdownDecision.Cancel;

            return countdownExpired
                ? SafeHouseReadyCountdownDecision.Load
                : SafeHouseReadyCountdownDecision.None;
        }
    }
}
