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
    [TestCase(SafeHousePhase.ReadyCheck, true, false)]
    [TestCase(SafeHousePhase.WaitingForHost, false, false)]
    [TestCase(SafeHousePhase.LoadingStage, false, false)]
    public void ReadyConfirmationIsOneWayDuringReadyCheck(
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
}
