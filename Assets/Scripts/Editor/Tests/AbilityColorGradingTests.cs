using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class AbilityColorGradingTests
{
    private static object Rule(string name, params object[] args)
    {
        var type = typeof(Player).Assembly.GetType("AbilityColorGradingRules");
        Assert.That(type, Is.Not.Null, "缺少 LGG 合成規則");
        return type.GetMethod(name).Invoke(null, args);
    }
    [Test]
    public void ZeroWeightPreservesAuthoredBaselineIncludingBrightness()
    {
        var baseline = new Vector4(.9f, 1.1f, 1.05f, -.12f);
        Assert.That((Vector4)Rule("AddTint", baseline, new Vector4(1.1f,.9f,1.2f,0f), 0f), Is.EqualTo(baseline));
    }
    [Test]
    public void TintAddsRelativeColorWithoutTreatingAlphaAsExposure()
    {
        var actual = (Vector4)Rule("AddTint", new Vector4(1f,1f,1f,.2f), new Vector4(1.2f,.8f,1.1f,0f), .5f);
        Assert.That(actual.x, Is.EqualTo(1.1f).Within(.00001f));
        Assert.That(actual.w, Is.EqualTo(.2f).Within(.00001f));
    }
    [TestCase(-.1f, 0f)] [TestCase(0f, 0f)] [TestCase(.225f, 1f)] [TestCase(.45f, 0f)] [TestCase(1f, 0f)]
    public void HealingIsASingleBoundedPulse(float elapsed, float expected)
    {
        Assert.That((float)Rule("HealingPulse", elapsed, .45f), Is.EqualTo(expected).Within(.00001f));
    }
    [Test]
    public void HealingPresentationUsesFormalOwnerEvent()
    {
        Assert.That(typeof(PlayerHealth).GetEvent("LocalHealingReceived"), Is.Not.Null);
        var rpc = typeof(PlayerHealth).GetMethod("RPC_ReceiveLocalHealing", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(rpc, Is.Not.Null);
        Assert.That(rpc.GetCustomAttribute<Fusion.RpcAttribute>(), Is.Not.Null);
    }
    [Test]
    public void CompositorOwnsUrpVolumeAndNeverNeedsCanvasOverlay()
    {
        var type = typeof(Player).Assembly.GetType("PlayerAbilityColorGrading");
        Assert.That(type, Is.Not.Null);
        Assert.That(type.GetField("runtimeVolume", BindingFlags.NonPublic | BindingFlags.Instance).FieldType,
            Is.EqualTo(typeof(UnityEngine.Rendering.Volume)));
    }
}
