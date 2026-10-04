using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class PlayerAbilityExperienceRulesTests
{
    private static Type Rules()
    {
        var type = typeof(GameLogic).Assembly.GetType("Purgers.Progression.PlayerAbilityExperienceRules");
        Assert.That(type, Is.Not.Null, "Ability rewards must preserve shared progression through explicit rules.");
        return type;
    }

    [Test]
    public void PersonalMultiplierIsSaturatingAndDoesNotChangeOtherGrants()
    {
        var method = Rules().GetMethod("ApplyMultiplier");
        Assert.That((int)method.Invoke(null, new object[] { 3, 2f }), Is.EqualTo(6));
        Assert.That((int)method.Invoke(null, new object[] { 3, 1f }), Is.EqualTo(3));
        Assert.That((int)method.Invoke(null, new object[] { int.MaxValue, 2f }), Is.EqualTo(int.MaxValue));
        Assert.That((int)method.Invoke(null, new object[] { 3, float.NaN }), Is.EqualTo(3));
    }

    [Test]
    public void SharedPoolSortsDeduplicatesAndConservesRemainder()
    {
        var method = Rules().GetMethod("CreateEqualShares");
        var shares = (KeyValuePair<int, int>[])method.Invoke(null,
            new object[] { 4, 1, new[] { 9, 3, 6, 3 } });
        Assert.That(shares.Length, Is.EqualTo(3));
        Assert.That(shares[0], Is.EqualTo(new KeyValuePair<int, int>(3, 2)));
        Assert.That(shares[1], Is.EqualTo(new KeyValuePair<int, int>(6, 2)));
        Assert.That(shares[2], Is.EqualTo(new KeyValuePair<int, int>(9, 1)));
    }

    [Test]
    public void EmptySharedRecipientsCannotProduceRewards()
    {
        var shares = (KeyValuePair<int, int>[])Rules().GetMethod("CreateEqualShares").Invoke(null,
            new object[] { 4, 1, Array.Empty<int>() });
        Assert.That(shares, Is.Empty);
    }

    [Test]
    public void PendingRecipientKeepsItsShareButCannotRedirectItToAnotherPlayer()
    {
        var shares = (KeyValuePair<int, int>[])Rules().GetMethod("CreateEqualShares").Invoke(null,
            new object[] { 4, 1, new[] { 1, 2 } });
        PlayerExperienceState pending = new PlayerExperienceState(1, 0, 1);
        PlayerExperienceState refused = PlayerExperienceRules.Award(pending, shares[0].Value);
        PlayerExperienceState other = PlayerExperienceRules.Award(new PlayerExperienceState(1, 0, 0),
            shares[1].Value);
        Assert.That(refused.Experience, Is.Zero);
        Assert.That(refused.PendingRewards, Is.EqualTo(1));
        Assert.That(other.Experience, Is.EqualTo(2));
    }

    [Test]
    public void PersonalMultiplierIsAppliedAfterTheSharedPoolIsSplit()
    {
        var shares = (KeyValuePair<int, int>[])Rules().GetMethod("CreateEqualShares").Invoke(null,
            new object[] { 4, 1, new[] { 1, 2 } });
        var multiply = Rules().GetMethod("ApplyMultiplier");
        int buffed = (int)multiply.Invoke(null, new object[] { shares[0].Value, 2f });
        int normal = (int)multiply.Invoke(null, new object[] { shares[1].Value, 1f });
        Assert.That(buffed, Is.EqualTo(6));
        Assert.That(normal, Is.EqualTo(2));
    }
}
