using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class BeamPresentationTests
{
    private static Type Rules
    {
        get
        {
            var type = typeof(EnemyBeamPresenter).Assembly.GetType("BeamPresentationRules");
            Assert.That(type, Is.Not.Null, "Shared beam presentation rules must exist.");
            return type;
        }
    }
    private static float Scale(float remaining, float duration) => (float)Rules.GetMethod("ShotScale")
        .Invoke(null, new object[] { remaining, duration, AnimationCurve.EaseInOut(0, 1, 1, 0) });

    [Test]
    public void ShotContractsMonotonicallyAndEndsExactlyAtZero()
    {
        float previous = Scale(.2f, .2f);
        Assert.That(previous, Is.EqualTo(1f).Within(.0001f));
        for (int i = 1; i <= 20; i++)
        {
            float current = Scale(.2f - i * .01f, .2f);
            Assert.That(current, Is.InRange(0f, previous + .00001f));
            previous = current;
        }
        Assert.That(Scale(0f, .2f), Is.Zero);
        Assert.That(Scale(-1f, .2f), Is.Zero);
        Assert.That(Scale(1f, 0f), Is.Zero);
    }

    [Test]
    public void PlayerWindupRatioChangesAppearanceWithoutChangingCastDuration()
    {
        var method = Rules.GetMethod("IsCharging");
        Assert.That(method.Invoke(null, new object[] { .51f, 1f, .5f }), Is.False);
        Assert.That(method.Invoke(null, new object[] { .5f, 1f, .5f }), Is.True);
        Assert.That(method.Invoke(null, new object[] { .9f, 1f, 0f }), Is.True);
        Assert.That(method.Invoke(null, new object[] { .01f, 1f, 1f }), Is.False);
    }

    [Test]
    public void EnemyRatioPreservesTotalWindupAndLegacyTiming()
    {
        var root = new GameObject("Beam ratio test");
        try
        {
            var attack = root.AddComponent<EnemyRangedBeamAttack>();
            var type = attack.GetType();
            var ratio = type.GetField("trackingStageRatio", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ratio, Is.Not.Null);
            type.GetField("trackingSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(attack, .2f);
            type.GetField("lockedDelaySeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(attack, .28f);
            type.GetField("useWindupStageRatio", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(attack, true);
            ratio.SetValue(attack, .25f);
            float tracking = (float)type.GetProperty("TrackingDuration").GetValue(attack);
            float locked = (float)type.GetProperty("LockedDuration").GetValue(attack);
            Assert.That(tracking, Is.EqualTo(.12f).Within(.0001f));
            Assert.That(tracking + locked, Is.EqualTo(.48f).Within(.0001f));
            type.GetField("useWindupStageRatio", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(attack, false);
            Assert.That(type.GetProperty("TrackingDuration").GetValue(attack), Is.EqualTo(.2f));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void FirstSnapshotAndRepeatedStateDoNotReplayTransientEvents()
    {
        var gateType = typeof(EnemyBeamPresenter).Assembly.GetType("BeamPresentationEventGate");
        Assert.That(gateType, Is.Not.Null);
        var gate = Activator.CreateInstance(gateType);
        var observe = gateType.GetMethod("Observe");
        Assert.That(observe.Invoke(gate, new object[] { 8, 3 }), Is.False);
        Assert.That(observe.Invoke(gate, new object[] { 8, 3 }), Is.False);
        Assert.That(observe.Invoke(gate, new object[] { 8, 0 }), Is.False);
        Assert.That(observe.Invoke(gate, new object[] { 9, 1 }), Is.True);
        Assert.That(observe.Invoke(gate, new object[] { 9, 2 }), Is.True);
        Assert.That(observe.Invoke(gate, new object[] { 9, 3 }), Is.True);
        Assert.That(observe.Invoke(gate, new object[] { 9, 3 }), Is.False);
        Assert.That(observe.Invoke(gate, new object[] { 9, 2 }), Is.False, "Rollback must not replay charge");
        Assert.That(observe.Invoke(gate, new object[] { 9, 3 }), Is.False);
    }
}
