using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class PlayerExperienceRulesTests
{
    [Test]
    public void LivingTeamGetsFullExperienceAndKillerGetsOneExtra()
    {
        Assert.That(PlayerExperienceRules.KillGrantForPlayer(
            10, true, true, true, false), Is.EqualTo(10));
        Assert.That(PlayerExperienceRules.KillGrantForPlayer(
            10, true, true, true, true), Is.EqualTo(11));
    }

    [Test]
    public void DeadOrDisconnectedPlayerAndEnvironmentalKillGetNoExperience()
    {
        Assert.That(PlayerExperienceRules.KillGrantForPlayer(
            10, true, true, false, false), Is.Zero);
        Assert.That(PlayerExperienceRules.KillGrantForPlayer(
            10, true, false, true, false), Is.Zero);
        Assert.That(PlayerExperienceRules.KillGrantForPlayer(
            10, false, true, true, false), Is.Zero);
    }

    [Test]
    public void RequirementDoublesFromTen()
    {
        Assert.That(PlayerExperienceRules.RequiredExperience(1), Is.EqualTo(10));
        Assert.That(PlayerExperienceRules.RequiredExperience(2), Is.EqualTo(20));
        Assert.That(PlayerExperienceRules.RequiredExperience(3), Is.EqualTo(40));
        Assert.That(PlayerExperienceRules.RequiredExperience(40),
            Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void CrossingThresholdKeepsOverflowAndQueuesReward()
    {
        var result = PlayerExperienceRules.Award(
            new PlayerExperienceState(1, 8, 0), 5);

        Assert.That(result.Level, Is.EqualTo(2));
        Assert.That(result.Experience, Is.EqualTo(3));
        Assert.That(result.PendingRewards, Is.EqualTo(1));
    }

    [Test]
    public void OneLargeGrantCanQueueSeveralRewards()
    {
        var result = PlayerExperienceRules.Award(
            new PlayerExperienceState(1, 0, 0), 50);

        Assert.That(result.Level, Is.EqualTo(3));
        Assert.That(result.Experience, Is.EqualTo(20));
        Assert.That(result.PendingRewards, Is.EqualTo(2));
    }

    [Test]
    public void PendingRewardRejectsSubsequentGrant()
    {
        var current = new PlayerExperienceState(2, 3, 1);
        var result = PlayerExperienceRules.Award(current, 100);

        Assert.That(result.Level, Is.EqualTo(current.Level));
        Assert.That(result.Experience, Is.EqualTo(current.Experience));
        Assert.That(result.PendingRewards, Is.EqualTo(current.PendingRewards));
    }

    [Test]
    public void ClaimingLastRewardAllowsExperienceAgain()
    {
        var afterClaim = PlayerExperienceRules.ClaimReward(
            new PlayerExperienceState(2, 3, 1));
        var afterGrant = PlayerExperienceRules.Award(afterClaim, 4);

        Assert.That(afterGrant.PendingRewards, Is.Zero);
        Assert.That(afterGrant.Experience, Is.EqualTo(7));
    }

    [Test]
    public void FlatRequirementHandlesLargeGrantWithoutOverflow()
    {
        var result = PlayerExperienceRules.Award(
            new PlayerExperienceState(1, 0, 0),
            int.MaxValue,
            baseRequirement: 10,
            growthMultiplier: 1);

        Assert.That(result.Level, Is.EqualTo(214748365));
        Assert.That(result.Experience, Is.EqualTo(7));
        Assert.That(result.PendingRewards, Is.EqualTo(214748364));
    }
}
