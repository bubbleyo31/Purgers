using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class DamageNumberRulesTests
{
    private static object Call(string method, params object[] args)
    {
        var type = typeof(PlayerHealth).Assembly.GetType("DamageNumberRules");
        Assert.That(type, Is.Not.Null, "尚未提供命中位置傷害跳字規則");
        return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [TestCase(DamageType.Bullet, true, true, false)]
    [TestCase(DamageType.Bullet, false, true, true)]
    [TestCase(DamageType.Bullet, true, false, false)]
    [TestCase(DamageType.DamageOverTime, true, true, true)]
    [TestCase(DamageType.DamageOverTime, false, true, true)]
    [TestCase(DamageType.DamageOverTime, true, false, false)]
    [TestCase(DamageType.DamageOverTime, false, false, false)]
    public void IndependentSwitchesChooseMerging(DamageType type, bool individual, bool continuous, bool expected)
        => Assert.That(Call("ShouldMerge", type, individual, continuous), Is.EqualTo(expected));
    [Test] public void PhysicalHeadHitUsesHeadStyleEvenWithoutForcedHeadshotFlag()
        => Assert.That(Call("IsHeadHit", new CombatHitFeedbackData { HitZone = DamageHitZoneType.Head }), Is.True);
    [Test] public void ForcedBodyHeadshotDoesNotPretendToHitHead()
        => Assert.That(Call("IsHeadHit", new CombatHitFeedbackData { HitZone = DamageHitZoneType.Body, IsHeadshot = true }), Is.False);
    [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void InvalidDamageNeverDisplays(float damage)
        => Assert.That(Call("ShouldDisplay", new CombatHitFeedbackData { AppliedDamage = damage, HasEffectiveDamage = true }), Is.False);
}
