using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class DevelopmentLevelUpRulesTests
{
    [Test]
    public void GrantsExactlyTheMissingExperienceForNextLevel()
    {
        var current = new PlayerExperienceState(3, 7, 0);
        int grant = DevelopmentLevelUpRules.ExperienceToNextLevel(current);

        Assert.That(grant, Is.EqualTo(13));
        PlayerExperienceState after = PlayerExperienceRules.Award(current, grant);
        Assert.That(after.Level, Is.EqualTo(4));
        Assert.That(after.Experience, Is.Zero);
        Assert.That(after.PendingRewards, Is.EqualTo(1));
    }

    [Test]
    public void PendingRewardDoesNotBlockAnotherDebugLevel()
    {
        Assert.That(DevelopmentLevelUpRules.ExperienceToNextLevel(
            new PlayerExperienceState(3, 0, 1)), Is.EqualTo(20));
    }

    [Test]
    public void InvalidOrMaximumLevelDoesNotGrantExperience()
    {
        Assert.That(DevelopmentLevelUpRules.ExperienceToNextLevel(
            new PlayerExperienceState(0, 0, 0)), Is.Zero);
        Assert.That(DevelopmentLevelUpRules.ExperienceToNextLevel(
            new PlayerExperienceState(int.MaxValue, 0, 0)), Is.Zero);
    }
}
