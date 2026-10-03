using System.Collections.Generic;
using NUnit.Framework;
using Purgers.MapGeneration.Topology;

[Category("PurgersRegression")]
public sealed class MapTopologyPlannerTests
{
    [Test]
    public void SameSeedProducesSameTopologyAndUniqueFinalChunk()
    {
        MapTopologyBuildRequest request = CreateRequest(
            seed: 72631,
            additionalChunkCount: 2,
            maxAttemptsPerChunk: 8,
            candidates: new[] { CreateSquareChunk("Room-A") });

        MapTopologyPlan first = MapTopologyPlanner.Build(request);
        MapTopologyPlan second = MapTopologyPlanner.Build(request);

        Assert.That(first.IsComplete, Is.True);
        Assert.That(second.IsComplete, Is.True);
        Assert.That(second.Signature, Is.EqualTo(first.Signature));
        Assert.That(first.Placements.Count, Is.EqualTo(3));
        Assert.That(first.FinalChunkInstanceIndex, Is.EqualTo(2));
        Assert.That(first.Placements[0].IsFinalChunk, Is.False);
        Assert.That(first.Placements[1].IsFinalChunk, Is.False);
        Assert.That(first.Placements[2].IsFinalChunk, Is.True);

        for (int index = 1; index < first.Placements.Count; index++)
        {
            MapTopologyPlacement previous = first.Placements[index - 1];
            MapTopologyPlacement current = first.Placements[index];
            Assert.That(current.ConnectedFromInstanceIndex, Is.EqualTo(previous.InstanceIndex));
            Assert.That(previous.LocalExitDirection.HasValue, Is.True);
            Assert.That(
                previous.GetWorldConnectorPosition(previous.LocalExitDirection.Value),
                Is.EqualTo(current.GetWorldConnectorPosition(current.LocalEntryDirection)));
            Assert.That(
                current.WorldEntryDirection,
                Is.EqualTo(MapTopologyRules.Opposite(previous.WorldExitDirection.Value)));
        }
    }

    [Test]
    public void DifferentSeedsCanProduceDifferentTopologySignatures()
    {
        var signatures = new HashSet<string>();

        for (int seed = 1; seed <= 8; seed++)
        {
            MapTopologyPlan plan = MapTopologyPlanner.Build(
                CreateRequest(
                    seed,
                    additionalChunkCount: 2,
                    maxAttemptsPerChunk: 8,
                    candidates: new[] { CreateSquareChunk("Room-A") }));

            Assert.That(plan.IsComplete, Is.True);
            signatures.Add(plan.Signature);
        }

        Assert.That(signatures.Count, Is.GreaterThan(1));
    }

    [Test]
    public void EveryGeneratedChunkUsesDifferentEntryAndExitConnectors()
    {
        MapTopologyPlan plan = MapTopologyPlanner.Build(
            CreateRequest(
                seed: 4108,
                additionalChunkCount: 12,
                maxAttemptsPerChunk: 32,
                candidates: new[] { CreateSquareChunk("Room-A") }));

        Assert.That(plan.IsComplete, Is.True);

        for (int index = 0; index < plan.Placements.Count - 1; index++)
        {
            MapTopologyPlacement placement = plan.Placements[index];
            Assert.That(
                placement.LocalExitDirection,
                Is.Not.EqualTo(placement.LocalEntryDirection),
                $"Chunk {index} 不可從接回上一塊的入口原路退出。");
        }
    }

    [Test]
    public void OverlappingCandidateIsRejectedAndGenerationStopsAtRetryLimit()
    {
        MapChunkTopologyDefinition impossible = new MapChunkTopologyDefinition(
            "Oversized",
            new MapTopologyRect(-20, -20, 20, 20),
            CardinalConnectors(distance: 5));

        const int retryLimit = 5;
        MapTopologyPlan plan = MapTopologyPlanner.Build(
            CreateRequest(
                seed: 99,
                additionalChunkCount: 1,
                maxAttemptsPerChunk: retryLimit,
                candidates: new[] { impossible }));

        Assert.That(plan.IsComplete, Is.False);
        Assert.That(plan.FailureReason, Is.EqualTo(MapTopologyFailureReason.RetryLimitReached));
        Assert.That(plan.AttemptsUsed, Is.EqualTo(retryLimit));
        Assert.That(plan.RejectedOverlapCount, Is.EqualTo(retryLimit));
        Assert.That(plan.Placements.Count, Is.EqualTo(1));
        Assert.That(plan.FinalChunkInstanceIndex, Is.EqualTo(-1));
        Assert.That(plan.Placements[0].IsFinalChunk, Is.False);
    }

    [Test]
    public void TouchingBoundsAreAcceptedButPositiveAreaOverlapIsRejected()
    {
        MapTopologyRect placed = new MapTopologyRect(-5, -5, 5, 5);

        Assert.That(
            MapTopologyRules.Overlaps(
                placed,
                new MapTopologyRect(5, -5, 15, 5)),
            Is.False,
            "共用邊界是合法 Connector 接縫，不應視為重疊。");

        Assert.That(
            MapTopologyRules.Overlaps(
                placed,
                new MapTopologyRect(4, -5, 14, 5)),
            Is.True);
    }

    [Test]
    public void ZeroAdditionalChunksMarksStartingChunkAsFinal()
    {
        MapTopologyPlan plan = MapTopologyPlanner.Build(
            CreateRequest(
                seed: 7,
                additionalChunkCount: 0,
                maxAttemptsPerChunk: 3,
                candidates: new[] { CreateSquareChunk("Room-A") }));

        Assert.That(plan.IsComplete, Is.True);
        Assert.That(plan.Placements.Count, Is.EqualTo(1));
        Assert.That(plan.FinalChunkInstanceIndex, Is.Zero);
        Assert.That(plan.Placements[0].IsFinalChunk, Is.True);
    }

    [Test]
    public void FixedStartingEntryAndExitArePreservedByThePlan()
    {
        MapTopologyBuildRequest request = new MapTopologyBuildRequest(
            seed: 9182,
            additionalChunkCount: 2,
            maxAttemptsPerChunk: 16,
            startingChunk: CreateSquareChunk("Starting-Room"),
            candidateChunks: new[] { CreateSquareChunk("Room-A") },
            startingEntryDirection: MapTopologyDirection.South,
            startingExitDirection: MapTopologyDirection.East);

        MapTopologyPlan plan = MapTopologyPlanner.Build(request);

        Assert.That(plan.IsComplete, Is.True);
        Assert.That(
            plan.Placements[0].LocalEntryDirection,
            Is.EqualTo(MapTopologyDirection.South));
        Assert.That(
            plan.Placements[0].LocalExitDirection,
            Is.EqualTo(MapTopologyDirection.East));
    }

    private static MapTopologyBuildRequest CreateRequest(
        int seed,
        int additionalChunkCount,
        int maxAttemptsPerChunk,
        IReadOnlyList<MapChunkTopologyDefinition> candidates)
    {
        return new MapTopologyBuildRequest(
            seed,
            additionalChunkCount,
            maxAttemptsPerChunk,
            CreateSquareChunk("Starting-Room"),
            candidates);
    }

    private static MapChunkTopologyDefinition CreateSquareChunk(string id)
    {
        return new MapChunkTopologyDefinition(
            id,
            new MapTopologyRect(-5, -5, 5, 5),
            CardinalConnectors(distance: 5));
    }

    private static IReadOnlyList<MapTopologyConnector> CardinalConnectors(int distance)
    {
        return new[]
        {
            new MapTopologyConnector(MapTopologyDirection.North, new MapTopologyPoint(0, distance)),
            new MapTopologyConnector(MapTopologyDirection.East, new MapTopologyPoint(distance, 0)),
            new MapTopologyConnector(MapTopologyDirection.South, new MapTopologyPoint(0, -distance)),
            new MapTopologyConnector(MapTopologyDirection.West, new MapTopologyPoint(-distance, 0))
        };
    }
}
