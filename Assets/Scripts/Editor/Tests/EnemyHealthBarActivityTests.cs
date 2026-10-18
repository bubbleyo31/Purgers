using System;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class EnemyHealthBarActivityTests
{
    private static object NewActivity()
    {
        var type = typeof(EnemyHealthBarTarget).Assembly.GetType("EnemyHealthBarActivity");
        Assert.That(type, Is.Not.Null, "尚未實作生命變化顯示期限");
        return Activator.CreateInstance(type);
    }
    private static void Observe(object state, float hp, float max, double now)
        => state.GetType().GetMethod("Observe").Invoke(state, new object[] { hp, max, now });
    private static bool Visible(object state, double now)
        => (bool)state.GetType().GetMethod("IsVisible").Invoke(state, new object[] { now });

    [Test] public void InitialFullHealthStaysHidden()
    {
        var s = NewActivity(); Observe(s, 100, 100, 0); Observe(s, 100, 100, 100);
        Assert.That(Visible(s, 100), Is.False);
    }
    [Test] public void DamageShowsUntilExactlyTenSeconds()
    {
        var s = NewActivity(); Observe(s, 100, 100, 0); Observe(s, 76, 100, 2);
        Assert.That(Visible(s, 2), Is.True); Assert.That(Visible(s, 11.999), Is.True);
        Assert.That(Visible(s, 12), Is.False);
    }
    [Test] public void IdenticalSnapshotsDoNotExtendDeadline()
    {
        var s = NewActivity(); Observe(s, 80, 100, 0);
        for (int t = 1; t <= 10; t++) Observe(s, 80, 100, t);
        Assert.That(Visible(s, 10), Is.False);
    }
    [Test] public void DamageAfterExpiryReopensAndResetsDeadline()
    {
        var s = NewActivity(); Observe(s, 80, 100, 0);
        Assert.That(Visible(s, 11), Is.False); Observe(s, 70, 100, 12);
        Assert.That(Visible(s, 21.999), Is.True); Assert.That(Visible(s, 22), Is.False);
    }
    [Test] public void HealingToFullIsAChangeAndRestartsDeadline()
    {
        var s = NewActivity(); Observe(s, 80, 100, 0); Observe(s, 100, 100, 9);
        Assert.That(Visible(s, 18.999), Is.True); Assert.That(Visible(s, 19), Is.False);
    }
    [Test] public void SamePercentageWithChangedHealthStillResetsDeadline()
    {
        var s = NewActivity(); Observe(s, 50, 100, 0); Observe(s, 100, 200, 9);
        Assert.That(Visible(s, 18.999), Is.True); Assert.That(Visible(s, 19), Is.False);
    }
    [Test] public void SmallRealHealthChangeCounts()
    {
        var s = NewActivity(); Observe(s, 100, 100, 0); Observe(s, 99.99999f, 100, 1);
        Assert.That(Visible(s, 1), Is.True);
    }
    [Test] public void FirstDamagedSnapshotGetsLocalTenSecondWindow()
    {
        var s = NewActivity(); Observe(s, 50, 100, 500);
        Assert.That(Visible(s, 509.999), Is.True); Assert.That(Visible(s, 510), Is.False);
    }
    [TestCase(0f, 100f)] [TestCase(-1f, 100f)] [TestCase(float.NaN, 100f)]
    [TestCase(80f, 0f)] [TestCase(80f, float.PositiveInfinity)]
    public void DeadOrInvalidSnapshotHidesImmediately(float health, float maximum)
    {
        var s = NewActivity(); Observe(s, 80, 100, 0); Observe(s, health, maximum, 1);
        Assert.That(Visible(s, 1), Is.False);
    }
    [Test] public void ResetClearsPooledEnemyHistory()
    {
        var s = NewActivity(); Observe(s, 80, 100, 0);
        s.GetType().GetMethod("Reset").Invoke(s, null); Observe(s, 100, 100, 1);
        Assert.That(Visible(s, 1), Is.False);
    }
    [Test] public void TargetsKeepIndependentDeadlines()
    {
        var a = NewActivity(); var b = NewActivity(); Observe(a, 50, 100, 0); Observe(b, 50, 100, 5);
        Assert.That(Visible(a, 11), Is.False); Assert.That(Visible(b, 11), Is.True);
    }
}
