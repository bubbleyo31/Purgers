using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class PlayerExperienceTableTests
{
    private static readonly int[] Table = { 5, 15, 30 };

    [Test]
    public void EachLevelUsesItsRowAndLastRowRepeats()
    {
        Assert.That(PlayerExperienceRules.RequiredExperience(2, Table), Is.EqualTo(15));
        Assert.That(PlayerExperienceRules.RequiredExperience(3, Table), Is.EqualTo(30));
        Assert.That(PlayerExperienceRules.RequiredExperience(500, Table), Is.EqualTo(30));
        Assert.That(PlayerExperienceRules.RequiredExperience(int.MaxValue, Table), Is.EqualTo(30));
    }

    [Test]
    public void MultipleLevelsPreserveOverflowAndExistingRewards()
    {
        var result = PlayerExperienceRules.Award(new PlayerExperienceState(1, 3, 2), 80, Table);
        Assert.That(result.Level, Is.EqualTo(5));
        Assert.That(result.Experience, Is.EqualTo(3));
        Assert.That(result.PendingRewards, Is.EqualTo(6));
    }

    [Test]
    public void LowerLaterRowsAreAllowed()
    {
        var result = PlayerExperienceRules.Award(new PlayerExperienceState(1, 0, 0), 17, new[] { 10, 5, 2 });
        Assert.That(result.Level, Is.EqualTo(4));
        Assert.That(result.Experience, Is.Zero);
    }

    [Test]
    public void InvalidRowsClampToOneAndMissingTableKeepsLegacyCurve()
    {
        Assert.That(PlayerExperienceRules.RequiredExperience(0, new[] { 0, -5 }), Is.EqualTo(1));
        Assert.That(PlayerExperienceRules.RequiredExperience(3, new[] { 0, -5 }), Is.EqualTo(1));
        Assert.That(PlayerExperienceRules.RequiredExperience(3, new int[0]), Is.EqualTo(20));
        Assert.That(PlayerExperienceRules.RequiredExperience(3, null), Is.EqualTo(20));
    }

    [Test, Timeout(1000)]
    public void FlatTailHandlesHugeGrantWithoutLoopingPerLevel()
    {
        var result = PlayerExperienceRules.Award(new PlayerExperienceState(1, 0, 0), int.MaxValue, new[] { 1, 1 });
        Assert.That(result.Level, Is.EqualTo(int.MaxValue));
        Assert.That(result.Experience, Is.EqualTo(1));
        Assert.That(result.PendingRewards, Is.EqualTo(int.MaxValue - 1));
    }

    [Test]
    public void RewardCapacityAndNonPositiveGrantsArePreserved()
    {
        var current = new PlayerExperienceState(3, 7, int.MaxValue - 1);
        var result = PlayerExperienceRules.Award(current, int.MaxValue, Table);
        Assert.That(result.Level, Is.EqualTo(4));
        Assert.That(result.PendingRewards, Is.EqualTo(int.MaxValue));
        Assert.That(result.Experience, Is.EqualTo(int.MaxValue - 23));
        Assert.That(PlayerExperienceRules.Award(current, 0, Table).Experience, Is.EqualTo(7));
    }
}
