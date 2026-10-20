using NUnit.Framework;
using Purgers.GameFlow.Stage;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class StageRulesTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void AuthoredExtractionCandidatesKeepThreeLegalSidesForEveryEntry(int chunkCount)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Map/MapChunk_Prototype.prefab");
        Assert.That(prefab, Is.Not.Null);
        var instance = Object.Instantiate(prefab);
        try
        {
            var points = instance.GetComponentsInChildren<StageExtractionPoint>(true);
            Assert.That(points.Length, Is.EqualTo(4));
            System.Array.Sort(points, (a, b) => string.CompareOrdinal(a.PointId, b.PointId));

            for (int entry = 0; entry < 4; entry++)
            {
                var eligible = new System.Collections.Generic.List<int>();
                for (int index = 0; index < points.Length; index++)
                {
                    Assert.That(points[index].OwnerChunk, Is.Not.Null, points[index].PointId);
                    Assert.That(points[index].TryGetConnectorSide(out ConnectorSide side),
                        Is.True, points[index].PointId);
                    if (StageRules.IsExtractionPlacementAllowed(
                        chunkCount - 1, chunkCount - 1, chunkCount, (int)side,
                        entry, chunkCount > 1 ? 1 << entry : 0))
                    {
                        eligible.Add(index);
                    }
                }
                Assert.That(eligible.Count, Is.EqualTo(3));
                for (int seed = 0; seed < 128; seed++)
                {
                    int selected = StageRules.SelectExtractionCandidateIndex(seed, eligible);
                    Assert.That(eligible, Does.Contain(selected));
                    Assert.That(points[selected].TryGetConnectorSide(out ConnectorSide side), Is.True);
                    Assert.That((int)side, Is.Not.EqualTo(entry));
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [TestCase(1, 0, 0)]
    [TestCase(2, 1, 3)]
    [TestCase(3, 2, 2)]
    public void ExtractionExcludesEntrySideAndKeepsOtherFinalChunkSides(
        int chunkCount, int finalChunkIndex, int entrySide)
    {
        int openSides = chunkCount > 1 ? 1 << entrySide : 0;
        for (int side = 0; side < 4; side++)
        {
            Assert.That(StageRules.IsExtractionPlacementAllowed(
                finalChunkIndex, finalChunkIndex, chunkCount,
                side, entrySide, openSides), Is.EqualTo(side != entrySide));
        }
    }

    [TestCase(0, 1, 2, 1, 0, 1)]
    [TestCase(0, 0, 2, 1, 0, 1)]
    [TestCase(1, 1, 2, 2, 0, 5)]
    [TestCase(0, 0, 1, -1, 0, 0)]
    [TestCase(0, 0, 1, 4, 0, 0)]
    [TestCase(0, 0, 1, 1, -1, 0)]
    public void ExtractionRejectsStartingChunkConnectedSidesAndInvalidMetadata(
        int candidateChunk, int finalChunk, int count,
        int side, int entry, int openSides)
    {
        Assert.That(StageRules.IsExtractionPlacementAllowed(
            candidateChunk, finalChunk, count, side, entry, openSides), Is.False);
    }

    [TestCase(0f)]
    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(270f)]
    public void ExtractionSideUsesChunkConnectorsAfterTranslationAndRotation(float yaw)
    {
        var root = new GameObject("Extraction test chunk");
        try
        {
            root.AddComponent<MapChunk>();
            root.transform.SetPositionAndRotation(
                new Vector3(630f, 15f, -420f), Quaternion.Euler(0f, yaw, 0f));
            Vector3[] anchors =
            {
                Vector3.forward * 250f, Vector3.right * 250f,
                Vector3.back * 250f, Vector3.left * 250f
            };
            for (int side = 0; side < 4; side++)
            {
                var anchor = new GameObject("Connector");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = anchors[side];
                var connector = anchor.AddComponent<MapConnector>();
                var serialized = new UnityEditor.SerializedObject(connector);
                serialized.FindProperty("side").intValue = side;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var pointObject = new GameObject("Arbitrary point name");
            pointObject.transform.SetParent(root.transform, false);
            var point = pointObject.AddComponent<StageExtractionPoint>();
            for (int side = 0; side < 4; side++)
            {
                point.transform.localPosition = anchors[side] * 0.9f + Vector3.up * 3f;
                Assert.That(point.TryGetConnectorSide(out ConnectorSide resolved), Is.True);
                Assert.That((int)resolved, Is.EqualTo(side));
            }
            point.transform.localPosition = Vector3.zero;
            Assert.That(point.TryGetConnectorSide(out _), Is.False,
                "Equidistant connectors must not silently pick an arbitrary side.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

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
