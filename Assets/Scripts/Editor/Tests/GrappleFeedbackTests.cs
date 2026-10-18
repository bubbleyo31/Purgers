using System;
using System.Reflection;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class GrappleFeedbackTests
{
    [TestCase(true, GrapplePhase.Attached, false, true, true, true)]
    [TestCase(false, GrapplePhase.Attached, false, true, true, false)]
    [TestCase(true, GrapplePhase.Idle, false, true, true, false)]
    [TestCase(true, GrapplePhase.PreFire, false, true, true, false)]
    [TestCase(true, GrapplePhase.Shooting, false, true, true, false)]
    [TestCase(true, GrapplePhase.Retracting, false, true, true, false)]
    [TestCase(true, GrapplePhase.Attached, true, true, true, false)]
    [TestCase(true, GrapplePhase.Attached, false, false, true, false)]
    [TestCase(true, GrapplePhase.Attached, false, true, false, false)]
    public void PullLoopOnlyDuringActualPlayerPull(bool spawned, GrapplePhase phase,
        bool tether, bool pulling, bool alive, bool expected)
    {
        Type rules = typeof(PlayerGrapple).Assembly.GetType("GrappleFeedbackRules");
        Assert.That(rules, Is.Not.Null, "拉動聲必須排除收繩、慣性、特殊 Tether 與尚未施加拉力的 Attached。");
        Assert.That(rules.GetMethod("ShouldPlayPull").Invoke(null,
            new object[] { spawned, phase, tether, pulling, alive }), Is.EqualTo(expected));
    }

    [Test]
    public void SuccessfulLaunchHasIndependentVisualShakeEntry()
    {
        Assert.That(typeof(FirstPersonHitCameraShake).GetMethod("PlayGrappleLaunchShake"), Is.Not.Null,
            "成功出鈎使用獨立呈現入口，不偽造電鋸攻擊或命中回饋。");
    }
}
