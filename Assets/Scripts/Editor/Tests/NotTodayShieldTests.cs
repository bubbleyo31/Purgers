using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class NotTodayShieldTests
{
    [Test]
    public void ExpiredShieldReleasesTemporaryCapacityWithoutChangingHealth()
    {
        var root = new GameObject("Shield HUD", typeof(RectTransform));
        try
        {
            var view = root.AddComponent<LocalHealthSegmentView>();
            view.ApplyHealthSnapshot(100, 100); view.SetShieldHealth(25);
            var capacity = typeof(LocalHealthSegmentView).GetField("shieldCapacity", BindingFlags.NonPublic | BindingFlags.Instance);
            view.SetShieldHealth(5);
            Assert.That((float)capacity.GetValue(view), Is.EqualTo(25), "Keep density while the same shield is active.");
            view.SetShieldHealth(0);
            Assert.That((float)capacity.GetValue(view), Is.Zero, "Expired shield must not leave empty maximum-health cells.");
            Assert.That(view.VisibleHealthCells, Is.EqualTo(5));
            Assert.That(view.VisibleShieldCells, Is.Zero);
            view.SetShieldHealth(10);
            Assert.That((float)capacity.GetValue(view), Is.EqualTo(10), "A new shield must not inherit the old peak.");
        }
        finally { Object.DestroyImmediate(root); }
    }
    [TestCase(0f, 0f)] [TestCase(-1f, 0f)]
    [TestCase(3f, 1f)] [TestCase(2.625f, 0f)] [TestCase(2.25f, 1f)]
    public void NotTodayYellowPulseTracksTheAuthoritativeLifetime(float remaining, float expected)
    {
        var method = typeof(AbilityColorGradingRules).GetMethod("NotTodayPulse");
        Assert.That(method, Is.Not.Null);
        Assert.That((float)method.Invoke(null, new object[] { remaining, 3f, .75f }), Is.EqualTo(expected).Within(.0001f));
    }
}
