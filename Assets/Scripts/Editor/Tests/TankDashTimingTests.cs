using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class TankDashTimingTests
{
    private static Vector3 Timed(float distance,float remaining,float dt,float multiplier=1f)
    {
        var method=typeof(TankPreparedDashRules).GetMethod("VelocityForDeadline");
        Assert.That(method,Is.Not.Null,"Dash velocity must derive from the remaining animation time.");
        return (Vector3)method.Invoke(null,new object[]{Vector3.zero,Vector3.forward*distance,remaining,dt,multiplier});
    }
    [Test] public void NearAndFarTargetsShareTheSameDeadline()
    {
        Assert.That(Timed(3f,.2f,.02f).z,Is.EqualTo(15f).Within(.001f));
        Assert.That(Timed(9f,.2f,.02f).z,Is.EqualTo(45f).Within(.001f));
    }
    [Test] public void MovingTargetRecalculatesFromRemainingDistanceAndTime()
    {
        Assert.That(Timed(6f,.1f,.02f).z,Is.EqualTo(60f).Within(.001f));
    }
    [Test] public void FinalTickAndZeroGapCannotOvershoot()
    {
        Assert.That(Timed(.1f,.001f,.02f).z,Is.EqualTo(5f).Within(.001f));
        Assert.That(Timed(.1f,0f,.02f).z,Is.EqualTo(5f).Within(.001f));
    }
    [Test] public void SharedMovementSlowdownDoesNotBreakTheAnimationDeadline()
    {
        Assert.That(Timed(6f,.2f,.02f,.5f).z*.5f,Is.EqualTo(30f).Within(.001f));
    }
    [Test] public void DurationUsesSourceFramesAndPlaybackMultiplier()
    {
        var method=typeof(TankPreparedDashRules).GetMethod("Duration");
        Assert.That(method,Is.Not.Null);
        var d=new TankMeleeAttackDefinition {UseAnimationFrames=true,
            FrameTiming=new AnimationFrameTiming{ClipDuration=2,FramesPerSecond=30,PlaybackSpeed=2},PreparationFrame=6,DamageFrame=18,RecoveryFrame=30};
        Assert.That((float)method.Invoke(null,new object[]{d}),Is.EqualTo(.2f).Within(.00001f));
    }
    [TestCase(0f,true)] [TestCase(9f,true)] [TestCase(11f,false)] [TestCase(180f,false)]
    public void AimToleranceIsAHalfAngle(float degrees,bool expected)
    {
        var method=typeof(PlayerEnemyAimQuery).GetMethod("IsInsideCone");
        Assert.That(method,Is.Not.Null);
        Vector3 offset=Quaternion.Euler(0,degrees,0)*Vector3.forward*8f;
        Assert.That((bool)method.Invoke(null,new object[]{Vector3.forward,offset,12f,10f}),Is.EqualTo(expected));
    }
}