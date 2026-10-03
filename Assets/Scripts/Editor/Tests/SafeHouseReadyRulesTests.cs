using NUnit.Framework;
using Purgers.GameFlow.SafeHouse;

[Category("PurgersRegression")]
public sealed class SafeHouseReadyRulesTests
{
    [TestCase(SafeHousePhase.WaitingForHost, true, true, true)]
    [TestCase(SafeHousePhase.WaitingForHost, false, true, false)]
    [TestCase(SafeHousePhase.WaitingForHost, true, false, false)]
    [TestCase(SafeHousePhase.ReadyCheck, true, true, false)]
    public void ReadyCheckRequiresWaitingHostInsideRange(
        SafeHousePhase phase,
        bool requesterIsHost,
        bool requesterIsInRange,
        bool expected)
    {
        Assert.That(
            SafeHouseReadyRules.CanOpenReadyCheck(
                phase,
                requesterIsHost,
                requesterIsInRange),
            Is.EqualTo(expected));
    }

    [TestCase(SafeHousePhase.ReadyCheck, false, true)]
    [TestCase(SafeHousePhase.ReadyCheck, true, true)]
    [TestCase(SafeHousePhase.Countdown, true, true)]
    [TestCase(SafeHousePhase.WaitingForHost, false, false)]
    [TestCase(SafeHousePhase.LoadingStage, false, false)]
    public void ReadyCanToggleEitherDirectionDuringReadyCheck(
        SafeHousePhase phase,
        bool alreadyReady,
        bool expected)
    {
        Assert.That(
            SafeHouseReadyRules.CanAcceptReady(phase, alreadyReady),
            Is.EqualTo(expected));
    }

    [TestCase(0, 0, false)]
    [TestCase(1, 0, false)]
    [TestCase(1, 1, true)]
    [TestCase(2, 1, false)]
    [TestCase(2, 2, true)]
    public void StageStartsOnlyWhenEveryConnectedPlayerIsReady(
        int connectedPlayerCount,
        int readyPlayerCount,
        bool expected)
    {
        Assert.That(
            SafeHouseReadyRules.AreAllPlayersReady(
                connectedPlayerCount,
                readyPlayerCount),
            Is.EqualTo(expected));
    }

    [TestCase(
        SafeHousePhase.ReadyCheck,
        2,
        2,
        false,
        SafeHouseReadyCountdownDecision.Start)]
    [TestCase(
        SafeHousePhase.Countdown,
        2,
        1,
        false,
        SafeHouseReadyCountdownDecision.Cancel)]
    [TestCase(
        SafeHousePhase.Countdown,
        2,
        2,
        false,
        SafeHouseReadyCountdownDecision.None)]
    [TestCase(
        SafeHousePhase.Countdown,
        2,
        2,
        true,
        SafeHouseReadyCountdownDecision.Load)]
    public void CountdownStartsCancelsOrLoadsFromAuthoritativeState(
        SafeHousePhase phase,
        int connectedPlayerCount,
        int readyPlayerCount,
        bool countdownExpired,
        SafeHouseReadyCountdownDecision expected)
    {
        Assert.That(
            SafeHouseReadyRules.EvaluateCountdown(
                phase,
                connectedPlayerCount,
                readyPlayerCount,
                countdownExpired),
            Is.EqualTo(expected));
    }

    [TestCase(SafeHousePhase.ReadyCheck, true, true)]
    [TestCase(SafeHousePhase.Countdown, true, true)]
    [TestCase(SafeHousePhase.WaitingForHost, true, false)]
    [TestCase(SafeHousePhase.LoadingStage, true, false)]
    [TestCase(SafeHousePhase.ReadyCheck, false, false)]
    public void ConnectedPlayerCanCancelAnActiveReadyVote(
        SafeHousePhase phase,
        bool requesterIsConnected,
        bool expected)
    {
        Assert.That(
            SafeHouseReadyRules.CanCancelReadyCheck(
                phase,
                requesterIsConnected),
            Is.EqualTo(expected));
    }

    [TestCase(SafeHousePhase.ReadyCheck, true, true)]
    [TestCase(SafeHousePhase.Countdown, true, true)]
    [TestCase(SafeHousePhase.ReadyCheck, false, false)]
    [TestCase(SafeHousePhase.WaitingForHost, true, false)]
    [TestCase(SafeHousePhase.LoadingStage, true, false)]
    public void ActiveReadyVoteCancelsWhenItsTimeoutExpires(
        SafeHousePhase phase,
        bool timeoutExpired,
        bool expected)
    {
        Assert.That(
            SafeHouseReadyRules.ShouldCancelExpiredReadyCheck(
                phase,
                timeoutExpired),
            Is.EqualTo(expected));
    }
}
