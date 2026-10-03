using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class WeaponRecoilApplicationTests
{
    [Test]
    public void RecoilIsAppliedOverConfiguredSecondsWithoutChangingTotalAngle()
    {
        float pendingPitch = -1f;
        float pendingYaw = 0.2f;
        float secondsRemaining = 0.1f;
        Vector2 total = Vector2.zero;

        for (int tick = 0; tick < 5; tick++)
        {
            Vector2 step = WeaponRecoilApplication.Step(
                ref pendingPitch,
                ref pendingYaw,
                ref secondsRemaining,
                0.02f);
            Assert.That(step.x, Is.GreaterThan(-1f).And.LessThan(0f));
            total += step;
        }

        Assert.That(total.x, Is.EqualTo(-1f).Within(0.00001f));
        Assert.That(total.y, Is.EqualTo(0.2f).Within(0.00001f));
        Assert.That(pendingPitch, Is.Zero.Within(0.00001f));
        Assert.That(pendingYaw, Is.Zero.Within(0.00001f));
    }

    [Test]
    public void ZeroDurationKeepsImmediateImpulse()
    {
        float pendingPitch = -0.8f;
        float pendingYaw = -0.15f;
        float secondsRemaining = 0f;

        Vector2 step = WeaponRecoilApplication.Step(
            ref pendingPitch,
            ref pendingYaw,
            ref secondsRemaining,
            0.02f);

        Assert.That(step, Is.EqualTo(new Vector2(-0.8f, -0.15f)));
        Assert.That(pendingPitch, Is.Zero);
        Assert.That(pendingYaw, Is.Zero);
    }

    [Test]
    public void NewShotCanJoinUnappliedRecoilWithoutLosingAngle()
    {
        float pendingPitch = -0.8f;
        float pendingYaw = 0.15f;
        float secondsRemaining = 0.08f;
        Vector2 firstStep = WeaponRecoilApplication.Step(
            ref pendingPitch,
            ref pendingYaw,
            ref secondsRemaining,
            0.02f);

        pendingPitch += -0.8f;
        pendingYaw += -0.15f;
        secondsRemaining = 0.08f;
        Vector2 total = firstStep;
        for (int tick = 0; tick < 4; tick++)
        {
            total += WeaponRecoilApplication.Step(
                ref pendingPitch,
                ref pendingYaw,
                ref secondsRemaining,
                0.02f);
        }

        Assert.That(total.x, Is.EqualTo(-1.6f).Within(0.00001f));
        Assert.That(total.y, Is.Zero.Within(0.00001f));
    }
}
