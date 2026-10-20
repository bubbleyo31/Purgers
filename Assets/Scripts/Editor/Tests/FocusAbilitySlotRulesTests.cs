using NUnit.Framework;

[TestFixture, Category("PurgersRegression")]
public class FocusAbilitySlotRulesTests
{
    [Test] public void EmptySlotsFillInEZXOrderWithoutMovingExistingSkills()
    {
        Assert.That(FocusAbilitySlotRules.FindFirstEmpty(new[] { false, false, false }), Is.EqualTo(0));
        Assert.That(FocusAbilitySlotRules.FindFirstEmpty(new[] { true, false, true }), Is.EqualTo(1));
        Assert.That(FocusAbilitySlotRules.FindFirstEmpty(new[] { true, true, true }), Is.EqualTo(-1));
    }
    [Test] public void EveryOccupancyNeedsAnExplicitDestination()
    {
        Assert.That(FocusAbilitySlotRules.ResolveDestination(new[] { true, true, true }, -1), Is.EqualTo(-1));
        Assert.That(FocusAbilitySlotRules.ResolveDestination(new[] { true, true, true }, 2), Is.EqualTo(2));
        Assert.That(FocusAbilitySlotRules.ResolveDestination(new[] { true, false, true }, -1), Is.EqualTo(-1));
        Assert.That(FocusAbilitySlotRules.ResolveDestination(new[] { false, false, false }, -1), Is.EqualTo(-1));
        Assert.That(FocusAbilitySlotRules.ResolveDestination(new[] { false, false, false }, 2), Is.EqualTo(2));
        Assert.That(FocusAbilitySlotRules.ResolveDestination(new[] { true, true, true }, 3), Is.EqualTo(-1));
    }
    [Test] public void OwnedSkillPolicyRejectsDuplicatesByStableId()
    {
        Assert.That(FocusAbilitySlotRules.CanAcquire("focus.a", new[] { "focus.b", "FOCUS.A" }), Is.False);
        Assert.That(FocusAbilitySlotRules.CanAcquire("focus.a", new[] { "focus.b" }), Is.True);
        Assert.That(FocusAbilitySlotRules.CanAcquire("", new string[0]), Is.False);
    }
    [Test] public void DropPressIsConsumedUntilPhysicalRelease()
    {
        int consumed = 0;
        Assert.That(FocusAbilitySlotRules.ConsumeDropPresses(1, 1, true, false, ref consumed), Is.EqualTo(1));
        Assert.That(FocusAbilitySlotRules.ConsumeDropPresses(1, 0, false, false, ref consumed), Is.Zero);
        Assert.That(consumed, Is.EqualTo(1), "Releasing ALT cannot reactivate a held E.");
        FocusAbilitySlotRules.ConsumeDropPresses(0, 0, false, false, ref consumed);
        Assert.That(consumed, Is.Zero);
    }
    [Test] public void ReplacementOwnsAltKeysAndDoesNotDrop()
    {
        int consumed = 0;
        Assert.That(FocusAbilitySlotRules.ConsumeDropPresses(4, 4, true, true, ref consumed), Is.Zero);
        Assert.That(consumed, Is.EqualTo(4));
    }
}
