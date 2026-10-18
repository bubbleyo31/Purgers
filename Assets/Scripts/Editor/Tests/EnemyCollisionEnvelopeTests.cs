using System;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyCollisionEnvelopeTests
{
    [Test]
    public void OffsetAnimationBoundsFitAllEightCornersIncludingBelowRoot()
    {
        var bounds = new Bounds(new Vector3(1.2f, 4f, -2f), new Vector3(5f, 12f, 4f));
        var envelope = EnemyCollisionEnvelope.Fit(bounds, .1f);
        Assert.That(envelope.IsValid, Is.True);
        for (int i = 0; i < 8; i++)
        {
            var p = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Assert.That(envelope.Contains(p), Is.True, "漏掉動畫邊界 " + i);
        }
        Assert.That(envelope.BaseOffset, Is.LessThan(bounds.min.y));
    }

    [Test]
    public void WiderTopIsNotClippedByCapsuleHemisphere()
    {
        var envelope = EnemyCollisionEnvelope.Fit(new Bounds(new Vector3(0, 6, 0), new Vector3(6, 12, 6)), 0);
        Assert.That(envelope.Contains(new Vector3(3, 12, 3)), Is.True);
        Assert.That(envelope.Contains(new Vector3(-3, 0, -3)), Is.True);
    }

    [Test]
    public void InvalidBoundsAndPaddingAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnemyCollisionEnvelope.Fit(new Bounds(Vector3.zero, Vector3.one), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnemyCollisionEnvelope.Fit(new Bounds(new Vector3(float.NaN, 0, 0), Vector3.one), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnemyCollisionEnvelope.Fit(new Bounds(Vector3.zero, new Vector3(-1, 1, 1)), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnemyCollisionEnvelope.Fit(new Bounds(Vector3.zero, Vector3.one), float.PositiveInfinity));
    }

    [Test]
    public void ZeroOffsetPreservesExistingEndpointConvention()
    {
        var envelope = new EnemyCollisionEnvelope(.35f, 1.8f, 0);
        envelope.Endpoints(new Vector3(4, 5, 6), .02f, out var bottom, out var top);
        Assert.That(bottom.y, Is.EqualTo(5.37f).Within(.0001f));
        Assert.That(top.y, Is.EqualTo(6.45f).Within(.0001f));
        Assert.That(bottom.x, Is.EqualTo(4));
        Assert.That(top.z, Is.EqualTo(6));
    }

    [Test]
    public void NegativeOffsetMovesBothEndpointsEqually()
    {
        new EnemyCollisionEnvelope(1, 5, -2).Endpoints(Vector3.zero, .1f, out var bottom, out var top);
        Assert.That(bottom.y, Is.EqualTo(-.9f).Within(.0001f));
        Assert.That(top.y, Is.EqualTo(2f).Within(.0001f));
    }
}
