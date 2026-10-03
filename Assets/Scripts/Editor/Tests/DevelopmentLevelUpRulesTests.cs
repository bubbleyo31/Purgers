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

        Assert.That(grant, Is.EqualTo(33));
        PlayerExperienceState after = PlayerExperienceRules.Award(current, grant);
        Assert.That(after.Level, Is.EqualTo(4));
        Assert.That(after.Experience, Is.Zero);
        Assert.That(after.PendingRewards, Is.EqualTo(1));
    }

    [Test]
    public void PendingRewardCannotBeSkippedByDebugKey()
    {
        Assert.That(DevelopmentLevelUpRules.ExperienceToNextLevel(
            new PlayerExperienceState(3, 0, 1)), Is.Zero);
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
