using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>驗證衰減邊界與高速穿越，不依賴場景或動畫。</summary>
[Category("PurgersRegression")]
public sealed class EnemySuicideRulesTests
{
    private static object Call(string method, params object[] args)
    {
        Type type = typeof(EnemyActor).Assembly.GetType("EnemySuicideRules");
        Assert.That(type, Is.Not.Null, "自爆敵人規則尚未實作。");
        return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [TestCase(0f, 100f)] [TestCase(2f, 55f)] [TestCase(4f, 10f)] [TestCase(4.001f, 0f)]
    public void FalloffHonorsCenterEdgeAndOutside(float distance, float expected)
    {
        Assert.That((float)Call("Falloff", distance, 4f, 100f, 10f), Is.EqualTo(expected).Within(.001f));
    }
    [Test] public void OuterEdgeDurationCanBeLessThanOneSecond()
    {
        Assert.That((float)Call("Falloff", 4f, 4f, 3f, .25f), Is.EqualTo(.25f));
    }
    [Test] public void SweepFindsHighSpeedPass()
    {
        Assert.That((float)Call("SegmentDistance", new Vector3(-10,0,0), new Vector3(10,0,0), Vector3.zero), Is.Zero.Within(.001f));
    }
    [Test] public void SweepRejectsParallelPassOutsideRadius()
    {
        Assert.That((float)Call("SegmentDistance", new Vector3(-10,0,3), new Vector3(10,0,3), Vector3.zero), Is.EqualTo(3f).Within(.001f));
    }
    [Test] public void VisualDiameterUsesAuthoredOneMeterBaseline()
    {
        Assert.That((float)Call("VfxScaleFactor", 2f, 1f), Is.EqualTo(4f));
    }
}
