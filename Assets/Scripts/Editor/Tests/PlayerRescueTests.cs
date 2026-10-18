using NUnit.Framework;

[Category("PurgersRegression")]
public class PlayerRescueTests
{
    [Test] public void DownedStateIsDistinctFromDeath()
    {
        Assert.That(typeof(PlayerHealth).GetProperty("IsDowned"), Is.Not.Null);
        Assert.That(typeof(PlayerHealth).GetProperty("DownedRemainingSeconds"), Is.Not.Null);
    }
    [Test] public void RescueHasAuthoritativeProgressAndOwner()
    {
        Assert.That(typeof(PlayerHealth).GetProperty("Rescuer"), Is.Not.Null);
        Assert.That(typeof(PlayerHealth).GetProperty("RescueProgress"), Is.Not.Null);
    }
    [Test] public void OutOfMapDeathHasExplicitAuthorityEntry()
    {
        Assert.That(typeof(PlayerHealth).GetMethod("KillUnrescuable"), Is.Not.Null);
    }

    [TestCase(30f, 5f, true, 30f)]
    [TestCase(30f, 5f, false, 25f)]
    [TestCase(.1f, 1f, false, 0f)]
    [TestCase(30f, -1f, false, 30f)]
    public void RescuePausesOnlyTheBleedoutClock(float remaining, float delta, bool rescuing, float expected)
        => Assert.That(PlayerRescueRules.Remaining(remaining, delta, rescuing), Is.EqualTo(expected));

    [Test] public void DeadPlayersAreExcludedFromExtractionButDownedAreRequired()
    {
        Assert.That(PlayerRescueRules.RequiresExtraction(false), Is.True);
        Assert.That(PlayerRescueRules.RequiresExtraction(true), Is.False);
    }
    [TestCase(100f, 35f)]
    [TestCase(200f, 70f)]
    public void ReviveUsesMaximumHealth(float maximum, float expected)
        => Assert.That(PlayerRescueRules.RestoredHealth(maximum, .35f), Is.EqualTo(expected).Within(.001f));
}
