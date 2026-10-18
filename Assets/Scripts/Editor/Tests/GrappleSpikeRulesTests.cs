using System;
using NUnit.Framework;
[Category("PurgersRegression")]
public sealed class GrappleSpikeRulesTests
{
    private static object Rule(string name, params object[] args)
    {
        Type type = typeof(Player).Assembly.GetType("GrappleSpikeRules");
        Assert.That(type, Is.Not.Null, "GrappleSpikeRules 尚未實作");
        return type.GetMethod(name).Invoke(null, args);
    }
    [TestCase(true, false, false, true)]
    [TestCase(false, true, false, false)]
    [TestCase(false, true, true, true)]
    [TestCase(false, false, true, false)]
    public void AttachedAndOptionalReleaseMomentum(bool attached, bool momentum, bool enabled, bool expected)
        => Assert.That(Rule("IsEligible", attached, momentum, enabled), Is.EqualTo(expected));
    [TestCase(99, 100, 130, 1)]
    [TestCase(100, 100, 130, 2)]
    [TestCase(129, 100, 130, 2)]
    [TestCase(130, 100, 130, 0)]
    [TestCase(500, 0, 0, 0)]
    public void AbsoluteDeadlinesRunWithoutGrappleAndExcludeExpiryTick(int now, int activeEnd, int cooldownEnd, int expected)
        => Assert.That(Convert.ToInt32(Rule("GetPhase", now, activeEnd, cooldownEnd)), Is.EqualTo(expected));
    [TestCase(0.5f, 1f / 60f, 30)]
    [TestCase(0.5f, 0.03f, 17)]
    [TestCase(0f, 0.02f, 1)]
    public void IntervalsNeverRoundDown(float seconds, float delta, int expected)
        => Assert.That(Rule("DurationTicks", seconds, delta), Is.EqualTo(expected));
    [TestCase(20f, 1f, 20f)]
    [TestCase(20f, 1.5f, 30f)]
    [TestCase(20f, 2f, 40f)]
    [TestCase(-1f, 2f, 0f)]
    public void DamageUsesCurrentBoostOnce(float damage, float multiplier, float expected)
        => Assert.That(Rule("Damage", damage, multiplier), Is.EqualTo(expected));
}
