using System;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class ReloadCameraMotionTests
{
    private Type type;
    private object motion;
    [SetUp] public void Setup()
    {
        type = typeof(AttackRifle).Assembly.GetType("ReloadCameraMotion");
        Assert.That(type, Is.Not.Null, "需要獨立於網路玩法的換彈旋轉取樣與退場狀態。");
        motion = Activator.CreateInstance(type);
    }
    private object Call(string name, params object[] args) => type.GetMethod(name).Invoke(motion, args);
    private void Prepare(bool reload, Quaternion pose) => Call("Prepare", reload, pose);
    private Quaternion Step(Quaternion pose, float speed = 0, float dt = 1, float blend = 0) =>
        (Quaternion)Call("Step", pose, Quaternion.identity, Vector3.one * .1f, 4f,
            speed, 120f, 540f, .2f, blend, .06f, dt);
    private static void Angle(Quaternion actual, Quaternion expected) =>
        Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(.03f));

    [Test] public void IdleAndMidReloadBindingNeverReplayHistoricalAnimation()
    {
        Prepare(true, Quaternion.identity);
        Angle(Step(Quaternion.Euler(0, 50, 0)), Quaternion.identity);
        Prepare(false, Quaternion.identity);
        Angle(Step(Quaternion.Euler(0, 70, 0)), Quaternion.identity);
    }
    [Test] public void RotationIsRelativeToStartAndClamped()
    {
        var start = Quaternion.Euler(10, 25, -5);
        Prepare(false, start); Prepare(true, start);
        Angle(Step(Quaternion.AngleAxis(80, Vector3.up) * start), Quaternion.Euler(0, 4, 0));
    }
    [Test] public void WrappingThrough360DoesNotProduceAFullTurn()
    {
        Prepare(false, Quaternion.Euler(0, 359, 0)); Prepare(true, Quaternion.Euler(0, 359, 0));
        Angle(Step(Quaternion.Euler(0, 1, 0)), Quaternion.Euler(0, .2f, 0));
    }
    [Test] public void FastLookAttenuatesAndRecoversWithoutChangingSampledPose()
    {
        Prepare(false, Quaternion.identity); Prepare(true, Quaternion.identity);
        var pose = Quaternion.Euler(0, 20, 0);
        Angle(Step(pose), Quaternion.Euler(0, 2, 0));
        Angle(Step(pose, 600), Quaternion.Euler(0, .4f, 0));
        Angle(Step(pose), Quaternion.Euler(0, 2, 0));
    }
    [Test] public void CancelFadesExistingOffsetWithoutFollowingNextAnimation()
    {
        Prepare(false, Quaternion.identity); Prepare(true, Quaternion.identity);
        Step(Quaternion.Euler(0, 30, 0)); Prepare(false, Quaternion.identity);
        var fading = Step(Quaternion.Euler(90, 0, 0), 0, .02f, .1f);
        Assert.That(Quaternion.Angle(Quaternion.identity, fading), Is.InRange(.1f, 2.99f));
        Angle(Step(Quaternion.Euler(90, 0, 0), 0, 2, .1f), Quaternion.identity);
    }
    [Test] public void ResetDoesNotReplayAnAlreadyRunningReload()
    {
        Prepare(false, Quaternion.identity); Prepare(true, Quaternion.identity);
        Step(Quaternion.Euler(0, 30, 0)); Call("Reset"); Prepare(true, Quaternion.identity);
        Angle(Step(Quaternion.Euler(0, 30, 0)), Quaternion.identity);
    }
    [Test] public void ZeroGainDisablesAllRotationAndBasisMapsAxes()
    {
        Angle((Quaternion)type.GetMethod("ScaleRotation").Invoke(null,
            new object[] { Quaternion.Euler(0, 30, 0), Vector3.zero, 4f }), Quaternion.identity);
        Prepare(false, Quaternion.identity); Prepare(true, Quaternion.identity);
        var result = (Quaternion)Call("Step", Quaternion.Euler(0, 20, 0), Quaternion.Euler(0, 0, 90),
            Vector3.one * .1f, 4f, 0f, 120f, 540f, .2f, 0f, 0f, 1f);
        Angle(result, Quaternion.Euler(-2, 0, 0));
    }
    [Test] public void SmoothingIsIndependentOfFrameSubdivision()
    {
        Prepare(false, Quaternion.identity); Prepare(true, Quaternion.identity);
        var once = Step(Quaternion.Euler(0, 30, 0), 0, .1f, .1f);
        Call("Reset"); Prepare(false, Quaternion.identity); Prepare(true, Quaternion.identity);
        Step(Quaternion.Euler(0, 30, 0), 0, .05f, .1f);
        Angle(Step(Quaternion.Euler(0, 30, 0), 0, .05f, .1f), once);
    }
}
