using NUnit.Framework;
using Purgers.Map;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class NetworkMapTopologyRulesTests
{
    [Test]
    public void CompleteThreeChunkChainIsAccepted()
    {
        MapChunkPlacement[] placements =
        {
            Placement(-1, 0, 1, 1 << 1),
            Placement(0, 3, 0, (1 << 3) | (1 << 0)),
            Placement(1, 2, -1, 1 << 2)
        };

        Assert.That(
            MapLayoutRules.TryValidateLinearTopology(
                placements,
                placements.Length,
                finalChunkIndex: 2,
                out string failure),
            Is.True,
            failure);
    }

    [Test]
    public void MissingMiddleConnectionIsRejectedBeforeReplicaInstantiation()
    {
        MapChunkPlacement[] placements =
        {
            Placement(-1, 0, 1, 1 << 1),
            Placement(-1, 3, 0, (1 << 3) | (1 << 0)),
            Placement(1, 2, -1, 1 << 2)
        };

        Assert.That(
            MapLayoutRules.TryValidateLinearTopology(
                placements,
                placements.Length,
                finalChunkIndex: 2,
                out string failure),
            Is.False);
        StringAssert.Contains("ConnectedFrom", failure);
    }

    [Test]
    public void OpenSideMaskMustMatchPublishedEntryAndExit()
    {
        MapChunkPlacement[] placements =
        {
            Placement(-1, 0, 1, 1 << 1),
            Placement(0, 3, -1, (1 << 3) | (1 << 0))
        };

        Assert.That(
            MapLayoutRules.TryValidateLinearTopology(
                placements,
                placements.Length,
                finalChunkIndex: 1,
                out string failure),
            Is.False);
        StringAssert.Contains("OpenSides", failure);
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void FinalChunkMustBeUniqueTail(int finalChunkIndex)
    {
        MapChunkPlacement[] placements =
        {
            Placement(-1, 0, 1, 1 << 1),
            Placement(0, 3, 0, (1 << 3) | (1 << 0)),
            Placement(1, 2, -1, 1 << 2)
        };

        Assert.That(
            MapLayoutRules.TryValidateLinearTopology(
                placements,
                placements.Length,
                finalChunkIndex,
                out _),
            Is.False);
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    public void ExtractionSelectionRangeIsValidated(int selectedIndex, bool expected)
    {
        Assert.That(
            MapLayoutRules.IsValidExtractionSelection(
                selectedIndex,
                extractionCandidateCount: 4),
            Is.EqualTo(expected));
    }

    private static MapChunkPlacement Placement(
        int connectedFrom,
        int entrySide,
        int exitSide,
        int openSides)
    {
        return new MapChunkPlacement
        {
            CatalogIndex = 0,
            ContentVersion = 1,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            ConnectedFromIndex = connectedFrom,
            EntrySide = entrySide,
            ExitSide = exitSide,
            OpenSides = openSides
        };
    }
}
