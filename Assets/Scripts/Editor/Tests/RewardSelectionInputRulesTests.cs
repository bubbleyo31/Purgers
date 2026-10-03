using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class RewardSelectionInputRulesTests
{
    [Test]
    public void ThreeChoicesMapLeftMiddleRight()
    {
        Assert.That(RewardSelectionInputRules.GetChoiceIndex(3, true, false, false), Is.EqualTo(0));
        Assert.That(RewardSelectionInputRules.GetChoiceIndex(3, false, true, false), Is.EqualTo(1));
        Assert.That(RewardSelectionInputRules.GetChoiceIndex(3, false, false, true), Is.EqualTo(2));
    }

    [Test]
    public void TwoChoicesMapLeftAndRightOnly()
    {
        Assert.That(RewardSelectionInputRules.GetChoiceIndex(2, true, false, false), Is.EqualTo(0));
        Assert.That(RewardSelectionInputRules.GetChoiceIndex(2, false, true, false), Is.EqualTo(-1));
        Assert.That(RewardSelectionInputRules.GetChoiceIndex(2, false, false, true), Is.EqualTo(1));
    }

    [Test]
    public void SelectionOnlyConsumesCombatWhileAltAndDraftAreActive()
    {
        Assert.That(RewardSelectionInputRules.IsSelectionActive(true, 1, 3), Is.True);
        Assert.That(RewardSelectionInputRules.IsSelectionActive(false, 1, 3), Is.False);
        Assert.That(RewardSelectionInputRules.IsSelectionActive(true, 0, 3), Is.False);
        Assert.That(RewardSelectionInputRules.IsSelectionActive(true, 1, 0), Is.False);
    }
}
