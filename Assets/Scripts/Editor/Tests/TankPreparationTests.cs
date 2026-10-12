using System;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class TankPreparationTests
{
    private static Type RuntimeType(string name)
    {
        var type = typeof(TankMeleeCombo).Assembly.GetType(name);
        Assert.That(type, Is.Not.Null, "Missing shared animation/preparation contract: " + name);
        return type;
    }
    [Test] public void FrameTimeUsesSourceFpsAndPlaybackSpeed()
    {
        var type = RuntimeType("AnimationFrameTiming");
        var timing = Activator.CreateInstance(type);
        type.GetField("ClipDuration").SetValue(timing, 2f);
        type.GetField("FramesPerSecond").SetValue(timing, 30f);
        type.GetField("PlaybackSpeed").SetValue(timing, 2f);
        Assert.That((float)type.GetMethod("FrameToSeconds").Invoke(timing, new object[] {15f}), Is.EqualTo(.25f).Within(.00001f));
        Assert.That((float)type.GetMethod("FrameToNormalized").Invoke(timing, new object[] {15f}), Is.EqualTo(.25f).Within(.00001f));
    }
    [Test] public void PreparationStateRetainsBothTimeMeasurements()
    {
        var type = RuntimeType("TankMeleePreparationProgress");
        Assert.That(type.GetField("ButtonHeldSeconds"), Is.Not.Null);
        Assert.That(type.GetField("PreparedSeconds"), Is.Not.Null);
    }
    [Test] public void DefinitionSupportsFramesWithoutRemovingLegacyValues()
    {
        foreach (string name in new[]{"FrameTiming","PreparationFrame","DamageFrame","RecoveryFrame","StartupDuration"})
            Assert.That(typeof(TankMeleeAttackDefinition).GetField(name), Is.Not.Null, name);
    }
}

[Category("PurgersRegression")]
public sealed class TankPreparationBehaviorTests
{
    private static TankMeleeAttackDefinition Definition() => new TankMeleeAttackDefinition {
        UseAnimationFrames = true,
        FrameTiming = new AnimationFrameTiming { ClipDuration=2f, FramesPerSecond=30f, PlaybackSpeed=2f },
        PreparationFrame=6f, DamageFrame=18f, RecoveryFrame=30f
    };
    [Test] public void TapRetainsOriginalDamageTime()
    {
        var p=TankMeleePreparationProgress.Begin(true);
        Assert.That(p.Advance(.05f,true,.1f,.3f),Is.False);
        Assert.That(p.Advance(.05f,false,.1f,.3f),Is.False);
        Assert.That((bool)p.IsHolding,Is.False);
        Assert.That(p.Advance(.2f,false,.1f,.3f),Is.True);
        Assert.That(p.PreparedSeconds,Is.Zero);
    }
    [Test] public void InfiniteHoldCannotDealDamageAndTracksSeparateTimes()
    {
        var p=TankMeleePreparationProgress.Begin(true);
        Assert.That(p.Advance(.1f,true,.1f,.3f),Is.False);
        Assert.That((bool)p.IsHolding,Is.True);
        Assert.That(p.Advance(100f,true,.1f,.3f),Is.False);
        Assert.That(p.CursorSeconds,Is.EqualTo(.1f));
        Assert.That(p.ButtonHeldSeconds,Is.EqualTo(100.1f).Within(.0001f));
        Assert.That(p.PreparedSeconds,Is.EqualTo(100f));
        Assert.That(p.Advance(.02f,false,.1f,.3f),Is.True);
        Assert.That(p.CursorSeconds,Is.EqualTo(.3f));
        Assert.That(p.PreparedSeconds,Is.EqualTo(100f));
    }
    [Test] public void RepressCannotRecaptureReleasedAttack()
    {
        var p=TankMeleePreparationProgress.Begin(true);
        p.Advance(.02f,false,.1f,.3f);
        Assert.That(p.Advance(.1f,true,.1f,.3f),Is.False);
        Assert.That((bool)p.IsHolding,Is.False);
        Assert.That(p.Advance(.2f,true,.1f,.3f),Is.True);
    }
    [Test] public void BufferedPressTimeDoesNotCountAsPreparedTime()
    {
        var p=TankMeleePreparationProgress.Begin(true,.4f);
        p.Advance(.1f,true,.1f,.3f);
        Assert.That(p.ButtonHeldSeconds,Is.EqualTo(.5f).Within(.00001f));
        Assert.That(p.PreparedSeconds,Is.Zero);
    }
    [Test] public void BufferedReleasedTapCannotBecomeHold()
    {
        var p=TankMeleePreparationProgress.Begin(false,.1f);
        Assert.That(p.Advance(.3f,true,.1f,.3f),Is.True);
        Assert.That(p.PreparedSeconds,Is.Zero);
    }
    [Test] public void FrameDurationsPartitionWholeAnimation()
    {
        var d=Definition();Assert.That(d.HasValidFrameTiming,Is.True);
        Assert.That(d.ResolvedStartup,Is.EqualTo(.3f).Within(.00001f));
        Assert.That(d.ResolvedActive,Is.EqualTo(.2f).Within(.00001f));
        Assert.That(d.ResolvedRecovery,Is.EqualTo(.5f).Within(.00001f));
    }
    [TestCase(-1f,18f,30f)] [TestCase(20f,18f,30f)] [TestCase(6f,35f,30f)] [TestCase(6f,18f,61f)]
    public void RejectInvalidEventOrdering(float hold,float hit,float recovery)
    {
        var d=Definition();d.PreparationFrame=hold;d.DamageFrame=hit;d.RecoveryFrame=recovery;
        Assert.That(d.HasValidFrameTiming,Is.False);
    }
    [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void RejectInvalidSpeed(float speed)
    {var d=Definition();d.FrameTiming.PlaybackSpeed=speed;Assert.That(d.HasValidFrameTiming,Is.False);}
    [Test] public void LegacyDurationsArePreservedWhenFramesDisabled()
    {
        var d=Definition();d.UseAnimationFrames=false;d.StartupDuration=.09f;d.ActiveDuration=0f;d.RecoveryDuration=.328f;
        Assert.That(d.ResolvedStartup,Is.EqualTo(.09f));Assert.That(d.ResolvedActive,Is.Zero);Assert.That(d.ResolvedRecovery,Is.EqualTo(.328f));
    }
    [Test] public void HoldAndDamageOnSameFrameStillWaitsForRelease()
    {var p=TankMeleePreparationProgress.Begin(true);Assert.That(p.Advance(.3f,true,.1f,.1f),Is.False);Assert.That(p.Advance(.01f,false,.1f,.1f),Is.True);}
}

[Category("PurgersRegression")]
public sealed class TankAnimationAssetTests
{
    [Test] public void FrameStateDefaultsMatchActualTankController()
    {
        var controller=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/_Project_Assets/Models/Player/Profession/Tank/Tank.controller");
        Assert.That(controller,Is.Not.Null);
        var paths=new System.Collections.Generic.HashSet<string>();
        foreach(var layer in controller.layers)foreach(var state in layer.stateMachine.states)paths.Add(layer.name+"."+state.state.name);
        var obj=new UnityEngine.GameObject("Tank frame contract test");
        try
        {
            var driver=obj.AddComponent<FirstPersonTankViewModelAnimator>();
            foreach(string field in new[]{"attack1State","attack2State","attack3State","idleState"})
            {
                string path=(string)typeof(FirstPersonTankViewModelAnimator).GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(driver);
                Assert.That(paths.Contains(path),Is.True,"Missing Controller State: "+path);
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(obj);}
    }
}
