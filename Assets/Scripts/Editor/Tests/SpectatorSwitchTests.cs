using System;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class SpectatorSwitchTests
{
    static Type RuntimeType(string name)
    {
        var type = typeof(LocalPlayerSpectatorController).Assembly.GetType(name);
        Assert.That(type, Is.Not.Null, "Missing approved behavior: " + name);
        return type;
    }
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    public void DownedTargetsRemainEligibleUntilDeath(bool dead, bool downed, bool expected)
    {
        Assert.That(RuntimeType("SpectatorSelectionRules").GetMethod("CanObserveLife")
            .Invoke(null, new object[] { dead, downed }), Is.EqualTo(expected));
    }
    [TestCase(3, 2, 1, 0)]
    [TestCase(3, 0, -1, 2)]
    [TestCase(1, 0, 1, 0)]
    [TestCase(0, -1, 1, -1)]
    public void SelectionWrapsAndHandlesEmptyRoster(int count, int current, int direction, int expected)
    {
        Assert.That(RuntimeType("SpectatorSelectionRules").GetMethod("NextIndex")
            .Invoke(null, new object[] { count, current, direction }), Is.EqualTo(expected));
    }
    [Test] public void WipeHoldStartsOnlyAfterBothVisualAndAudioComplete()
    {
        var type = RuntimeType("Purgers.GameFlow.Transition.WipeTransitionClock");
        object clock = Activator.CreateInstance(type);
        var advance = type.GetMethod("Advance");
        var ready = type.GetProperty("Ready");
        advance.Invoke(clock, new object[] { 8f, false, true });
        Assert.That(ready.GetValue(clock), Is.False);
        advance.Invoke(clock, new object[] { 8f, true, false });
        Assert.That(ready.GetValue(clock), Is.False);
        advance.Invoke(clock, new object[] { 1f, true, true });
        Assert.That(ready.GetValue(clock), Is.False);
        advance.Invoke(clock, new object[] { 1.99f, true, true });
        Assert.That(ready.GetValue(clock), Is.False);
        advance.Invoke(clock, new object[] { .02f, true, true });
        Assert.That(ready.GetValue(clock), Is.True);
    }
    [Test] public void WipeApiLivesOnExistingTransitionOwner()
    {
        var type = typeof(Purgers.GameFlow.Transition.LocalSceneTransition);
        Assert.That(type.GetMethod("BeginWipe"), Is.Not.Null);
        Assert.That(type.GetProperty("IsWipeReadyToLoad"), Is.Not.Null);
    }
    [TestCase(-12f, 1f, -12f)]
    [TestCase(-12f, 0f, -80f)]
    [TestCase(-6f, .5f, -12.0206f)]
    public void TransitionGainComposesWithoutReplacingUserVolume(float userDb, float gain, float expected)
    {
        Assert.That(GameplayAudioTransitionGain.ComposeMasterDb(userDb, gain), Is.EqualTo(expected).Within(.001f));
    }
    [Test] public void ReleasingOneTransitionCannotUnmuteAnotherOwner()
    {
        object a = new object(), b = new object();
        float before = GameplayAudioTransitionGain.Gain;
        try
        {
            GameplayAudioTransitionGain.Set(a, .5f);
            GameplayAudioTransitionGain.Set(b, 0f);
            GameplayAudioTransitionGain.Release(a);
            Assert.That(GameplayAudioTransitionGain.Gain, Is.Zero);
            GameplayAudioTransitionGain.Release(b);
            Assert.That(GameplayAudioTransitionGain.Gain, Is.EqualTo(before));
        }
        finally { GameplayAudioTransitionGain.Release(a); GameplayAudioTransitionGain.Release(b); }
    }
    [Test] public void InterruptedBlackoutRestartsFullHold()
    {
        var clock = new Purgers.GameFlow.Transition.WipeTransitionClock();
        clock.Advance(1, true, true); clock.Advance(1.9f, true, true);
        clock.Advance(.2f, false, true);
        clock.Advance(4, true, true);
        Assert.That(clock.Ready, Is.False);
        clock.Advance(2, true, true);
        Assert.That(clock.Ready, Is.True);
    }
    [Test] public void ApprovedTimingIsSharedByFadeAndHold()
    {
        Assert.That(Purgers.GameFlow.Transition.WipeTransitionClock.FadeSeconds, Is.EqualTo(1.5f));
        Assert.That(Purgers.GameFlow.Transition.WipeTransitionClock.HoldSeconds, Is.EqualTo(2f));
    }
    [Test] public void SharedPortraitCanBeReplacedThroughProviderContract()
    {
        Assert.That(typeof(PlayerPortraitProvider).IsAssignableFrom(typeof(SharedPlayerPortraitProvider)), Is.True);
        var provider = UnityEngine.ScriptableObject.CreateInstance<SharedPlayerPortraitProvider>();
        try { Assert.That(provider.GetPortrait(null, default), Is.Null, "未設定圖片時 UI 必須使用 fallback"); }
        finally { UnityEngine.Object.DestroyImmediate(provider); }
    }
}