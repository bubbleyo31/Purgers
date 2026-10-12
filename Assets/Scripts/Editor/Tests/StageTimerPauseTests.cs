using NUnit.Framework;
using Purgers.GameFlow.Stage;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class StageTimerPauseTests
{
    [TestCase(true, true, StagePhase.Active, true, 1, true)]
    [TestCase(false, true, StagePhase.Active, true, 100, false)]
    [TestCase(true, false, StagePhase.Active, true, 100, false)]
    [TestCase(true, true, StagePhase.Initializing, true, 100, false)]
    [TestCase(true, true, StagePhase.Completed, true, 100, false)]
    [TestCase(true, true, StagePhase.Failed, true, 100, false)]
    [TestCase(true, true, StagePhase.CommitFailed, true, 100, false)]
    [TestCase(true, true, StagePhase.LoadingSafeHouse, true, 100, false)]
    [TestCase(true, true, StagePhase.Active, false, 100, false)]
    [TestCase(true, true, StagePhase.Active, true, 0, false)]
    [TestCase(true, true, StagePhase.Active, true, -1, false)]
    public void PauseRequiresEditorHostActiveTimedStageBeforeExpiry(
        bool editorPlaying, bool authority, StagePhase phase,
        bool limited, int remainingTicks, bool expected)
    {
        Assert.That(StageRules.CanToggleDevelopmentTimerPause(
            editorPlaying, authority, phase, limited, remainingTicks), Is.EqualTo(expected));
    }

    [Test]
    public void UnspawnedStageRejectsPauseWithoutReadingNetworkedState()
    {
        var root = new GameObject("Unspawned stage pause test");
        try
        {
            var stage = root.AddComponent<StageFlowController>();
            Assert.That(stage.RequestDevelopmentTimerToggle(), Is.False);
            Assert.That(stage.IsStageTimerPaused, Is.False);
            Assert.That(stage.RemainingStageSeconds, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
