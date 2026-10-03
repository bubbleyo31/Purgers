using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class RewardDraftRulesTests
{
    private static readonly RewardDraftEntry[] Pool =
    {
        new RewardDraftEntry("focus.a", RewardCategory.GrappleFocus, 1, 10, RewardRepeatPolicy.OncePerRun),
        new RewardDraftEntry("hit.a", RewardCategory.GrappleHit, 1, 10, RewardRepeatPolicy.OncePerRun),
        new RewardDraftEntry("hit.b", RewardCategory.GrappleHit, 1, 10, RewardRepeatPolicy.OncePerRun),
        new RewardDraftEntry("weapon.a", RewardCategory.Weapon, 3, 10, RewardRepeatPolicy.Repeatable),
    };

    [Test]
    public void SameSeedAndPoolProduceSameThreeDistinctCandidates()
    {
        string[] first = RewardDraftRules.Draw(Pool, 1, 12345, null);
        string[] second = RewardDraftRules.Draw(Pool, 1, 12345, null);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.Length, Is.EqualTo(3));
        Assert.That(first, Is.Unique);
        Assert.That(first, Does.Not.Contain("weapon.a"));
    }

    [Test]
    public void TwoEligibleChoicesRemainTwoAndOwnedOncePerRunIsExcluded()
    {
        string[] result = RewardDraftRules.Draw(
            Pool, 1, 1, new[] { "hit.b" });

        Assert.That(result, Is.EquivalentTo(new[] { "focus.a", "hit.a" }));
    }

    [Test]
    public void RepeatableRewardMayReturnButZeroWeightCannot()
    {
        var entries = new[]
        {
            new RewardDraftEntry("repeat", RewardCategory.CharacterBonus, 1, 1,
                RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("disabled", RewardCategory.CharacterBonus, 1, 0,
                RewardRepeatPolicy.Repeatable)
        };

        string[] result = RewardDraftRules.Draw(
            entries, 1, 5, new[] { "repeat" });

        Assert.That(result, Is.EqualTo(new[] { "repeat" }));
    }

    [Test]
    public void CatalogOrderDoesNotChangeDraft()
    {
        var reversed = (RewardDraftEntry[])Pool.Clone();
        System.Array.Reverse(reversed);

        Assert.That(RewardDraftRules.Draw(Pool, 3, 85, null),
            Is.EqualTo(RewardDraftRules.Draw(reversed, 3, 85, null)));
    }

    [Test]
    public void CurrentlyEquippedRepeatableRewardIsExcluded()
    {
        var entries = new[]
        {
            new RewardDraftEntry("focus.a", RewardCategory.GrappleFocus, 1, 1,
                RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("focus.b", RewardCategory.GrappleFocus, 1, 1,
                RewardRepeatPolicy.Repeatable)
        };

        Assert.That(RewardDraftRules.Draw(entries, 1, 7, null,
            new[] { "focus.a" }), Is.EqualTo(new[] { "focus.b" }));
    }
}
