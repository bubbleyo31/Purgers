using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class GrappleLandingRulesTests
{
    [Test]
    public void LandingBetweenAttachmentAndFirstPullDoesNotRelease()
    {
        bool armed = GrappleLandingRules.ObserveAirborne(false, false, false);
        armed = GrappleLandingRules.ObserveAirborne(armed, false, true);
        Assert.That(GrappleLandingRules.ShouldReleaseOnLanding(armed, true, true), Is.False,
            "附著時還在下墜，不代表拉動已開始；首次拉力前著地必須保留鉤索。");
    }

    [Test]
    public void ConfirmedAirborneAfterPullArmsNextLanding()
    {
        bool armed = GrappleLandingRules.ObserveAirborne(false, true, false);
        Assert.That(GrappleLandingRules.ShouldReleaseOnLanding(armed, true, true), Is.True);
        Assert.That(GrappleLandingRules.ShouldReleaseOnLanding(armed, true, false), Is.False);
        Assert.That(GrappleLandingRules.ShouldReleaseOnLanding(armed, false, true), Is.False);
    }

    [Test]
    public void GroundedPullHasNotConfirmedTakeoff()
    {
        bool armed = GrappleLandingRules.ObserveAirborne(false, true, true);
        Assert.That(GrappleLandingRules.ShouldReleaseOnLanding(armed, true, true), Is.False);
    }
    [Test]
    public void StartupLandingRequestsOnlyOneJumpAndThenArmsAfterPullTakeoff()
    {
        bool armed = GrappleLandingRules.ObserveAirborne(false, false, false);
        Assert.That(GrappleLandingRules.ShouldRequestStartupJump(armed, false, false), Is.False);
        Assert.That(GrappleLandingRules.ShouldRequestStartupJump(armed, false, true), Is.True);
        Assert.That(GrappleLandingRules.ShouldRequestStartupJump(armed, true, true), Is.False);
        armed = GrappleLandingRules.ObserveAirborne(armed, true, false);
        Assert.That(GrappleLandingRules.ShouldRequestStartupJump(armed, false, true), Is.False);
        Assert.That(GrappleLandingRules.ShouldReleaseOnLanding(armed, true, true), Is.True);
    }

    [TestCase(false, true, true, true)]
    [TestCase(false, true, false, false)]
    [TestCase(false, false, true, false)]
    [TestCase(true, true, true, false)]
    public void StartupTimeoutOnlyAbortsBlockedTakeoff(bool armed, bool grounded, bool expired, bool expected)
    {
        Assert.That(GrappleLandingRules.ShouldAbortStartup(armed, grounded, expired), Is.EqualTo(expected));
    }

    [Test]
    public void ZeroThresholdDoesNotTreatStationaryAsApproaching()
    {
        Assert.That(GrappleLandingRules.IsApproaching(0f, 0f), Is.False);
    }

    [Test]
    public void ZeroThresholdDoesNotTreatStationaryAsPassingPoint()
    {
        Assert.That(GrappleLandingRules.HasPassedPoint(true, 0f, 0f), Is.False);
    }

    [TestCase(0.01f, 0f, true)]
    [TestCase(0.2f, 0.5f, false)]
    [TestCase(0.5f, 0.5f, true)]
    public void NonzeroApproachStillRespectsThreshold(float speed, float threshold, bool expected)
    {
        Assert.That(GrappleLandingRules.IsApproaching(speed, threshold), Is.EqualTo(expected));
    }

    [TestCase(true, -0.01f, 0f, true)]
    [TestCase(false, -1f, 0f, false)]
    [TestCase(true, -0.05f, 0.1f, false)]
    [TestCase(true, -0.1f, 0.1f, true)]
    public void PassingPointRequiresPreviousApproachAndAwayThreshold(bool approached, float speed, float threshold, bool expected)
    {
        Assert.That(GrappleLandingRules.HasPassedPoint(approached, speed, threshold), Is.EqualTo(expected));
    }
}
