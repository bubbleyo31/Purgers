using NUnit.Framework;
using Purgers.GameFlow.Stage;

[Category("PurgersRegression")]
public sealed class StageRulesTests
{
    [TestCase(1, 4, StageRuntimeKind.Ordinary, 1, 0)]
    [TestCase(2, 4, StageRuntimeKind.Ordinary, 2, 1)]
    [TestCase(3, 4, StageRuntimeKind.Ordinary, 3, 2)]
    [TestCase(4, 4, StageRuntimeKind.Boss, 4, 0)]
    [TestCase(5, 4, StageRuntimeKind.Ordinary, 1, 0)]
    [TestCase(4, 5, StageRuntimeKind.UnsupportedOrdinary, 4, 3)]
    [TestCase(5, 5, StageRuntimeKind.Boss, 5, 0)]
    public void RuntimePlanSeparatesBossFromOrdinaryTopology(
        int stageLevel,
        int cycleLength,
        StageRuntimeKind expectedKind,
        int expectedCycleStage,
        int expectedAdditionalChunks)
    {
        StageRuntimePlan plan = StageRules.ResolveRuntimePlan(
            stageLevel,
            cycleLength);

        Assert.That(plan.Kind, Is.EqualTo(expectedKind));
        Assert.That(plan.StageLevel, Is.EqualTo(stageLevel));
        Assert.That(plan.CycleLength, Is.EqualTo(cycleLength));
        Assert.That(plan.CycleStage, Is.EqualTo(expectedCycleStage));
        Assert.That(
            plan.AdditionalChunkCount,
            Is.EqualTo(expectedAdditionalChunks));
        Assert.That(
            plan.IsBossStage,
            Is.EqualTo(StageRules.IsBossStage(stageLevel, cycleLength)));
    }

    [Test]
    public void BossRuntimePlanOwnsMapObjectiveExtractionAndDefaultTiming()
    {
        StageRuntimePlan plan = StageRules.ResolveRuntimePlan(4, 4);

        Assert.That(plan.Kind, Is.EqualTo(StageRuntimeKind.Boss));
        Assert.That(plan.MapRoute, Is.EqualTo(StageMapRoute.DedicatedBossChunk));
        Assert.That(plan.ObjectiveKind, Is.EqualTo(StageObjectiveKind.DefeatBoss));
        Assert.That(plan.RequiresExtraction, Is.False);
        Assert.That(
            StageRules.ResolveTimeLimitSeconds(
                plan,
                ordinaryTimeLimitSeconds: 600f,
                ordinaryStageIsUnlimited: false,
                bossTimeLimitSeconds: 0f),
            Is.EqualTo(0f));
    }

    [Test]
    public void BossTimeLimitCanBeEnabledWithoutChangingBossIdentity()
    {
        StageRuntimePlan plan = StageRules.ResolveRuntimePlan(4, 4);

        Assert.That(
            StageRules.ResolveTimeLimitSeconds(
                plan,
                ordinaryTimeLimitSeconds: 600f,
                ordinaryStageIsUnlimited: false,
                bossTimeLimitSeconds: 300f),
            Is.EqualTo(300f));
        Assert.That(plan.MapRoute, Is.EqualTo(StageMapRoute.DedicatedBossChunk));
        Assert.That(plan.ObjectiveKind, Is.EqualTo(StageObjectiveKind.DefeatBoss));
    }

    [TestCase(1, 4, 1, 0, false)]
    [TestCase(2, 4, 2, 1, false)]
    [TestCase(4, 4, 4, 3, true)]
    [TestCase(5, 4, 1, 0, false)]
    [TestCase(8, 4, 4, 3, true)]
    public void CycleRulesMatchConfiguredLoop(
        int stageLevel,
        int cycleLength,
        int expectedCycleStage,
        int expectedAdditionalChunks,
        bool expectedBoss)
    {
        Assert.That(
            StageRules.GetCycleStage(stageLevel, cycleLength),
            Is.EqualTo(expectedCycleStage));
        Assert.That(
            StageRules.GetAdditionalChunkCount(stageLevel, cycleLength),
            Is.EqualTo(expectedAdditionalChunks));
        Assert.That(
            StageRules.IsBossStage(stageLevel, cycleLength),
            Is.EqualTo(expectedBoss));
    }

    [Test]
    public void ExtractionRequiresEveryLivingPlayerInside()
    {
        Assert.That(
            StageRules.AreAllLivingPlayersInside(2, 2),
            Is.True);
        Assert.That(
            StageRules.AreAllLivingPlayersInside(2, 1),
            Is.False);
        Assert.That(
            StageRules.AreAllLivingPlayersInside(0, 0),
            Is.False);
    }

    [Test]
    public void ExtractionSelectionIsDeterministicAndReturnsOnlyEligibleCandidate()
    {
        int[] eligibleCandidateIndices = { 1, 3 };

        int first = StageRules.SelectExtractionCandidateIndex(
            72631,
            eligibleCandidateIndices);
        int second = StageRules.SelectExtractionCandidateIndex(
            72631,
            eligibleCandidateIndices);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(eligibleCandidateIndices, Does.Contain(first));
        Assert.That(
            StageRules.SelectExtractionCandidateIndex(72631, new int[0]),
            Is.EqualTo(-1));
    }

    [TestCase(2, 2, true)]
    [TestCase(0, 2, false)]
    [TestCase(2, -1, false)]
    public void ExtractionCandidatesBelongOnlyToPublishedFinalChunk(
        int candidateChunkIndex,
        int finalChunkIndex,
        bool expected)
    {
        Assert.That(
            StageRules.IsExtractionCandidateInFinalChunk(
                candidateChunkIndex,
                finalChunkIndex),
            Is.EqualTo(expected));
    }
}
