using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class DamageIndicatorTests
{
    private static object Call(string method, params object[] args)
    {
        var type = typeof(PlayerHealth).Assembly.GetType("DamageIndicatorRules");
        Assert.That(type, Is.Not.Null, "尚未提供受傷指示的方向、時序與遮擋規則");
        return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [Test] public void FrontAndBackRemainDistinct()
    {
        Assert.That((Vector2)Call("ScreenDirection", Vector3.forward), Is.EqualTo(Vector2.up));
        Assert.That((Vector2)Call("ScreenDirection", Vector3.back), Is.EqualTo(Vector2.down));
        Assert.That((Vector2)Call("ScreenDirection", Vector3.right), Is.EqualTo(Vector2.right));
    }
    [Test] public void DirectionDoesNotDisappearForVerticalSource()
    {
        Assert.That((Vector2)Call("ScreenDirection", Vector3.up), Is.EqualTo(Vector2.up));
        Assert.That((Vector2)Call("ScreenDirection", Vector3.down), Is.EqualTo(Vector2.down));
    }
    [Test] public void FadeExpiresAndNeverRestartsFromOcclusion()
    {
        Assert.That((float)Call("Envelope", .025f, .05f, .45f, .5f), Is.EqualTo(.5f).Within(.001));
        Assert.That((float)Call("Envelope", .75f, .05f, .45f, .5f), Is.EqualTo(.5f).Within(.001));
        Assert.That((float)Call("Envelope", 1.1f, .05f, .45f, .5f), Is.Zero);
    }
    [Test] public void HiddenOccludedMarkerStillBecomesDue()
    {
        Assert.That((bool)Call("ShouldProbe", true, true, .051f, .05f), Is.True);
        Assert.That((bool)Call("ShouldProbe", true, true, .01f, .05f), Is.False);
        Assert.That((bool)Call("ShouldProbe", true, false, 1f, .05f), Is.False);
        Assert.That((bool)Call("ShouldProbe", false, true, 1f, .05f), Is.False);
    }
}
