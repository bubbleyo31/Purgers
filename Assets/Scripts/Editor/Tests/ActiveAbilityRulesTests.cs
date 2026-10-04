using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class ActiveAbilityRulesTests
{
    [TestCase(1, 1.5f)]
    [TestCase(3, 2.5f)]
    [TestCase(0, 1.5f)]
    public void ShieldDurationUsesClampedPlayerLevel(int level, float seconds)
    { Assert.That(ActiveAbilityRules.ShieldDuration(level, 1.5f, 0.5f), Is.EqualTo(seconds)); }

    [Test]
    public void ShieldConsumesOnlyPostReductionDamageAndPassesOverflow()
    {
        float shield = 25f;
        float healthDamage = ActiveAbilityRules.AbsorbShield(30f, ref shield, out float blocked);
        Assert.That(healthDamage, Is.EqualTo(5f));
        Assert.That(blocked, Is.EqualTo(25f));
        Assert.That(shield, Is.Zero);
    }

    [Test]
    public void SmallHitDoesNotDestroyTheWholeShield()
    {
        float shield = 25f;
        Assert.That(ActiveAbilityRules.AbsorbShield(10f, ref shield, out float blocked), Is.Zero);
        Assert.That(shield, Is.EqualTo(15f));
        Assert.That(blocked, Is.EqualTo(10f));
    }

    [TestCase(0, 10f)]
    [TestCase(1, 20f)]
    [TestCase(2, 40f)]
    [TestCase(3, 80f)]
    [TestCase(9, 80f)]
    public void RicochetDoublesUpToThreeBounces(int count, float damage)
    { Assert.That(ActiveAbilityRules.RicochetDamage(10f, count), Is.EqualTo(damage)); }

    [Test]
    public void PiercingUsesRemainingDamageAndDoesNotReduceFirstTarget()
    {
        Assert.That(ActiveAbilityRules.PiercingDamage(100f, 0), Is.EqualTo(100f));
        Assert.That(ActiveAbilityRules.PiercingDamage(100f, 2), Is.EqualTo(56.25f));
    }

    [TestCase(false, 30f)]
    [TestCase(true, 15f)]
    public void HealingUsesThrowerLevelAndHalvesOnlyForThrower(bool self, float amount)
    { Assert.That(ActiveAbilityRules.HealingAmount(3, self), Is.EqualTo(amount)); }

    [Test]
    public void EqualRewardSharesConservePoolIncludingRemainder()
    {
        Assert.That(ActiveAbilityRules.EqualShare(5, 3, 0), Is.EqualTo(2));
        Assert.That(ActiveAbilityRules.EqualShare(5, 3, 1), Is.EqualTo(2));
        Assert.That(ActiveAbilityRules.EqualShare(5, 3, 2), Is.EqualTo(1));
        Assert.That(ActiveAbilityRules.EqualShare(5, 0, 0), Is.Zero);
    }
}
