using System;
using System.Reflection;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class EnemyFlytrapRulesTests
{
    private static object Call(string method, params object[] args)
    {
        var type = typeof(EnemyActor).Assembly.GetType("EnemyFlytrapRules");
        Assert.That(type, Is.Not.Null, "捕食規則尚未實作。");
        return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [TestCase(false, false, false, false, false)]
    [TestCase(false, true, false, false, true)]
    [TestCase(true, true, false, false, false)]
    [TestCase(true, false, true, false, true)]
    [TestCase(true, false, false, true, true)]
    public void OnlyUnsafeActionsTrigger(bool crouched, bool moving, bool firing, bool grapple, bool expected)
    { Assert.That(Call("IsUnsafe", crouched, moving, firing, grapple), Is.EqualTo(expected)); }
    [TestCase(1, 100f)] [TestCase(2, 200f)] [TestCase(4, 200f)]
    public void DamageUsesRoomCountAndMaximumHealth(int count, float expected)
    { Assert.That(Call("TotalDamage", 200f, count), Is.EqualTo(expected)); }
    [TestCase(0f, 0f)] [TestCase(.49f, 0f)] [TestCase(.5f, .1f)]
    [TestCase(4.99f, .9f)] [TestCase(5f, 1f)] [TestCase(8f, 1f)]
    public void FirstDamageWaitsForInterval(float elapsed, float expected)
    { Assert.That((float)Call("DueFraction", elapsed, 5f, .5f), Is.EqualTo(expected).Within(.00001f)); }
    [TestCase(4.9f, .98f)] [TestCase(5f, 1f)]
    public void NonDivisibleDurationSettlesRemainder(float elapsed, float expected)
    { Assert.That((float)Call("DueFraction", elapsed, 5f, .7f), Is.EqualTo(expected).Within(.00001f)); }
    [Test] public void IntervalLongerThanDurationOnlyDamagesAtEnd()
    {
        Assert.That(Call("DueFraction", 1f, 2f, 3f), Is.EqualTo(0f));
        Assert.That(Call("DueFraction", 2f, 2f, 3f), Is.EqualTo(1f));
    }
    [Test] public void RemainderAndRegularPaymentsNeverExceedBudget()
    {
        float previous = 0f, total = 0f;
        for (int i = 0; i <= 1000; ++i)
        {
            float due = (float)Call("DueFraction", i * .01f, 5f, .7f);
            Assert.That(due, Is.GreaterThanOrEqualTo(previous));
            total += (due - previous) * 123f;
            previous = due;
        }
        Assert.That(total, Is.EqualTo(123f).Within(.001f));
    }
}
