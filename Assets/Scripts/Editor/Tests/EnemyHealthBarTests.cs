using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>敵人百分比分格與大型敵人畫面定位的行為契約。</summary>
[Category("PurgersRegression")]
public sealed class EnemyHealthBarTests
{
    private static object Call(string name, params object[] args)
    {
        var rules = typeof(Player).Assembly.GetType("EnemyHealthBarRules");
        Assert.That(rules, Is.Not.Null, "敵人血條規則尚未實作");
        return rules.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [TestCase(1f, 0, 1f)] [TestCase(1f, 9, 1f)]
    [TestCase(.76f, 6, 1f)] [TestCase(.76f, 7, .6f)] [TestCase(.76f, 8, 0f)]
    [TestCase(.001f, 0, .01f)] [TestCase(0f, 0, 0f)]
    [TestCase(-1f, 0, 0f)] [TestCase(2f, 9, 1f)]
    public void PercentageFillsPartialCell(float ratio, int cell, float expected)
        => Assert.That((float)Call("CellFill", ratio, 10, cell), Is.EqualTo(expected).Within(.00001f));
    [TestCase(76f, 100f)] [TestCase(7600f, 10000f)]
    public void MaximumHealthDoesNotChangePercentage(float current, float maximum)
        => Assert.That((float)Call("Ratio", current, maximum), Is.EqualTo(.76f).Within(.00001f));
    [TestCase(float.NaN, 100f)] [TestCase(10f, 0f)] [TestCase(10f, float.PositiveInfinity)]
    public void InvalidHealthCannotProduceInvalidGeometry(float current, float maximum)
        => Assert.That((float)Call("Ratio", current, maximum), Is.Zero);
    [Test] public void GiantKeepsBarInsideOffsetViewportWhenHeadLeavesScreen()
    {
        object[] args = { new Rect(100, 50, 800, 600), new Vector3(500, 900, 10),
            new Rect(300, 80, 400, 570), true, false, new Vector2(120, 10), 16f, 12f, 8f, false, Vector2.zero };
        Assert.That((bool)Call("TryPosition", args), Is.True);
        Assert.That((bool)args[9], Is.True);
        var point = (Vector2)args[10];
        Assert.That(point.x, Is.EqualTo(500).Within(.01f));
        Assert.That(point.y, Is.LessThanOrEqualTo(629));
    }
    [Test] public void OrdinaryEnemyDoesNotClampOffscreenAnchorToEdge()
    {
        object[] args = { new Rect(0, 0, 800, 600), new Vector3(400, 900, 10),
            new Rect(300, 80, 200, 520), false, false, new Vector2(120, 10), 16f, 12f, 8f, false, Vector2.zero };
        Assert.That((bool)Call("TryPosition", args), Is.False);
    }
    [Test] public void OffscreenBodyNeverProducesGiantEdgeMarker()
    {
        object[] args = { new Rect(0, 0, 800, 600), new Vector3(1000, 900, 10),
            new Rect(900, 80, 200, 520), true, false, new Vector2(120, 10), 16f, 12f, 8f, false, Vector2.zero };
        Assert.That((bool)Call("TryPosition", args), Is.False);
    }
    [Test] public void FallbackHasHysteresisBeforeReturningToHead()
    {
        object[] args = { new Rect(0, 0, 800, 600), new Vector3(400, 562, 10),
            new Rect(300, 80, 200, 500), true, true, new Vector2(120, 10), 16f, 20f, 8f, false, Vector2.zero };
        Assert.That((bool)Call("TryPosition", args), Is.True);
        Assert.That((bool)args[9], Is.True);
        args[1] = new Vector3(400, 500, 10);
        Assert.That((bool)Call("TryPosition", args), Is.True);
        Assert.That((bool)args[9], Is.False);
    }
}
