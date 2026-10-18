using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class StatusEffectRulesTests
{
    [Test] public void RatioRestoresMidEffectInsteadOfRestarting() => Assert.That(StatusEffectRules.Ratio(2, 5), Is.EqualTo(.4f).Within(.0001f));
    [Test] public void ExtensionResetsOnlyPresentationSpan() => Assert.That(StatusEffectRules.SpanAfterUpdate(2, 5, 4), Is.EqualTo(4));
    [Test] public void ShorterOverlapDoesNotRefill() => Assert.That(StatusEffectRules.SpanAfterUpdate(3, 5, 2), Is.EqualTo(5));
    [Test] public void PauseDoesNotRefill() => Assert.That(StatusEffectRules.SpanAfterUpdate(2, 5, 2.02f, paused:true), Is.EqualTo(5));
    [Test] public void MarkRefreshCanShortenAndStillRefill() => Assert.That(StatusEffectRules.SpanAfterUpdate(3, 5, 2, refreshed:true), Is.EqualTo(2));
    [Test] public void HiddenHudDoesNotNeedNewStart() => Assert.That(StatusEffectRules.SpanAfterUpdate(2, 5, 1), Is.EqualTo(5));
    [Test] public void ExpiryClearsSpan() => Assert.That(StatusEffectRules.SpanAfterUpdate(1, 5, 0), Is.Zero);
    [Test] public void InvalidTimesAreEmpty() { Assert.That(StatusEffectRules.Ratio(float.NaN,5), Is.Zero); Assert.That(StatusEffectRules.Ratio(2,float.PositiveInfinity), Is.Zero); }
    [Test] public void FullHealthStatusForcesBarWithoutNewHealthWindow() { Assert.That(StatusEffectRules.ShowEnemy(true,false,true), Is.True); Assert.That(StatusEffectRules.ShowEnemy(true,false,false), Is.False); }
    [Test] public void DeadNeverShowsStatus() => Assert.That(StatusEffectRules.ShowEnemy(false,true,true), Is.False);
}
