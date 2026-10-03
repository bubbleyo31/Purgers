using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class MinimapRulesTests
{
    [Test]
    public void BlockerIndexPreservesExactBoundsAcrossNegativeAndAdjacentBuckets()
    {
        var type = typeof(MapChunk).Assembly.GetType("Purgers.Map.MinimapBlockerIndex");
        Assert.That(type, Is.Not.Null);
        var index = Activator.CreateInstance(type);
        type.GetMethod("Add").Invoke(index, new object[] { new Bounds(new Vector3(-1, 2, -1), new Vector3(40, 2, 40)) });
        var contains = type.GetMethod("Contains");
        Assert.That(contains.Invoke(index, new object[] { new Vector3(-20, 2, -20) }), Is.EqualTo(true));
        Assert.That(contains.Invoke(index, new object[] { new Vector3(18, 2, 18) }), Is.EqualTo(true));
        Assert.That(contains.Invoke(index, new object[] { new Vector3(20, 2, 18) }), Is.EqualTo(false));
        Assert.That(contains.Invoke(index, new object[] { new Vector3(0, 8, 0) }), Is.EqualTo(false));
        type.GetMethod("Clear").Invoke(index, null);
        Assert.That(contains.Invoke(index, new object[] { new Vector3(0, 2, 0) }), Is.EqualTo(false));
    }

    [Test]
    public void CartographyPaletteSeparatesWallsFloorsAndBlockedGround()
    {
        var method = Rules.GetMethod("HeightColor");
        Assert.That(method, Is.Not.Null);
        Color32 blocked = (Color32)method.Invoke(null, new object[] { (byte)0, 0f });
        Color32 lowFloor = (Color32)method.Invoke(null, new object[] { (byte)2, 0f });
        Color32 highFloor = (Color32)method.Invoke(null, new object[] { (byte)2, 30f });
        Color32 lowWall = (Color32)method.Invoke(null, new object[] { (byte)1, 0f });
        Color32 highWall = (Color32)method.Invoke(null, new object[] { (byte)1, 30f });
        Assert.That(lowFloor.r - blocked.r, Is.GreaterThan(45));
        Assert.That(highFloor.r - lowFloor.r, Is.GreaterThan(50));
        Assert.That(lowWall.r - highFloor.r, Is.GreaterThan(20));
        Assert.That(highWall.r - lowWall.r, Is.InRange(1, 20));
    }

    [Test]
    public void ExplorationPresentationReceivesOnlyActualChangesWithoutConsumingNetworkDeltas()
    {
        var store = new Purgers.Map.ExplorationStore();
        int notifications = 0;
        store.Changed += word => notifications++;
        store.Reveal(1, 0, 3);
        store.Reveal(1, 0, 3);
        store.Merge(2, 0, 0, 16);
        Assert.That(notifications, Is.EqualTo(2));
        Assert.That(store.DrainChanges().Count, Is.EqualTo(1));
        Assert.That(store.IsExplored(1, 0, 4, false), Is.False);
        Assert.That(store.IsExplored(1, 0, 4, true), Is.True);
    }

    [Test]
    public void CachedTilesRespectBuildBudgetAndUploadOnlyWhenDirty()
    {
        var data = ScriptableObject.CreateInstance<Purgers.Map.MapCartographyData>();
        data.SetBakedData(Vector2.zero, 1, 4, 4,
            new byte[] {2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2}, new float[16], 1);
        try
        {
            using (var cache = new Purgers.Map.MinimapTileCache(data))
            {
                Assert.That(cache.Build(5, Matrix4x4.identity, null), Is.EqualTo(5));
                Assert.That(cache.Ready, Is.False);
                Assert.That(cache.Build(20, Matrix4x4.identity, null), Is.EqualTo(11));
                Assert.That(cache.Ready, Is.True);
                Assert.That(cache.UploadCount, Is.EqualTo(1));
                Assert.That(cache.Build(20, Matrix4x4.identity, null), Is.Zero);
                cache.RevealWord(0, 3); cache.FlushExploration();
                int uploads = cache.UploadCount;
                cache.RevealWord(0, 3); cache.FlushExploration();
                Assert.That(cache.UploadCount, Is.EqualTo(uploads));
                Assert.That(cache.ExploredCells, Is.EqualTo(2));
                cache.ClearExploration(); cache.RevealWord(0, 4); cache.FlushExploration();
                Assert.That(cache.ExploredCells, Is.EqualTo(1), "Switching back must remove shared-only tint.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(data); }
    }

    [TestCase(80f, true)]
    [TestCase(0f, true)]
    [TestCase(-12f, false)]
    public void FlightExploresBelowWithoutRevealingFloorsAbove(float playerHeight, bool expected)
    {
        var method = Rules.GetMethod("CanExploreHeight");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, new object[] { playerHeight, 0f, 6f }), Is.EqualTo(expected));
    }

    [Test]
    public void UnexploredRoadRemainsReadableButDoesNotRevealMarkers()
    {
        var method = Rules.GetMethod("TerrainColor");
        Assert.That(method, Is.Not.Null);
        Color32 road = (Color32)method.Invoke(null, new object[] { (byte)2, false, false });
        Color32 wall = (Color32)method.Invoke(null, new object[] { (byte)1, false, false });
        Assert.That((int)road.r + road.g + road.b, Is.GreaterThan(240));
        Assert.That(road, Is.Not.EqualTo(wall));
        Assert.That(Purgers.Map.MinimapRules.CanShowMarker(false, 5, 0, true, 30, 6), Is.False);
    }

    [Test]
    public void LateSnapshotCannotEraseNewerDeltas()
    {
        var history = new Purgers.Map.ExplorationStore();
        history.Merge(1, 0, 0, 8);
        history.Merge(1, 0, 0, 2);
        history.Merge(1, 0, 0, 2);
        Assert.That(history.IsExplored(1, 0, 3, false), Is.True);
        Assert.That(history.IsExplored(1, 0, 1, false), Is.True);
        Assert.That(history.IsExplored(2, 0, 3, false), Is.False);
    }

    [Test]
    public void RotatedTranslatedChunkUsesItsLocalGrid()
    {
        var data = ScriptableObject.CreateInstance<Purgers.Map.MapCartographyData>();
        var root = new GameObject("Rotated chunk");
        try
        {
            data.SetBakedData(new Vector2(-2, -2), 1, 4, 4, new byte[16], new float[16], 1);
            root.transform.SetPositionAndRotation(new Vector3(130, 4, -700), Quaternion.Euler(0, 90, 0));
            Vector3 world = root.transform.TransformPoint(data.CellCenter(9));
            Assert.That(data.CellAt(root.transform.InverseTransformPoint(world)), Is.EqualTo(9));
            Assert.That(data.CellAt(new Vector3(2, 0, 0)), Is.EqualTo(-1));
        }
        finally { UnityEngine.Object.DestroyImmediate(data); UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Type Rules => typeof(MapChunk).Assembly.GetType("Purgers.Map.MinimapRules");

    [Test]
    public void SharedExplorationDoesNotOverwritePersonalHistory()
    {
        var type = typeof(MapChunk).Assembly.GetType("Purgers.Map.ExplorationStore");
        Assert.That(type, Is.Not.Null, "Phase 4 前置-4 needs independent personal records.");
        object store = Activator.CreateInstance(type);
        type.GetMethod("Reveal").Invoke(store, new object[] { 1, 0, 17 });
        type.GetMethod("Reveal").Invoke(store, new object[] { 2, 0, 91 });
        var known = type.GetMethod("IsExplored");
        Assert.That(known.Invoke(store, new object[] { 1, 0, 91, true }), Is.EqualTo(true));
        Assert.That(known.Invoke(store, new object[] { 1, 0, 91, false }), Is.EqualTo(false));
        Assert.That(known.Invoke(store, new object[] { 1, 0, 17, false }), Is.EqualTo(true));
        Assert.That(known.Invoke(store, new object[] { 1, 1, 17, true }), Is.EqualTo(false), "Repeated prefab is a different chunk.");
    }

    [TestCase(true, 10f, 0f, true, true)]
    [TestCase(true, 60f, 0f, true, false)]
    [TestCase(true, 10f, 12f, true, false)]
    [TestCase(true, 10f, 0f, false, false)]
    [TestCase(false, 10f, 0f, true, false)]
    public void MarkersRequirePersonalProximityAndVisibility(bool explored, float distance, float height, bool visible, bool expected)
    {
        Assert.That(Rules, Is.Not.Null);
        Assert.That(Rules.GetMethod("CanShowMarker").Invoke(null,
            new object[] { explored, distance, height, visible, 30f, 6f }), Is.EqualTo(expected));
    }

    [Test]
    public void ExpandedViewZoomsOutWithoutMovingThePlayerCenter()
    {
        Assert.That(Rules, Is.Not.Null);
        var project = Rules.GetMethod("WorldToViewport");
        Vector3 center = new Vector3(111, 9, -222);
        Assert.That((Vector2)project.Invoke(null, new object[] { center, center, 200f }), Is.EqualTo(Vector2.one * 0.5f));
        Vector3 target = center + Vector3.right * 30;
        var normal = (Vector2)project.Invoke(null, new object[] { target, center, 100f });
        var expanded = (Vector2)project.Invoke(null, new object[] { target, center, 200f });
        Assert.That(expanded.x, Is.LessThan(normal.x));
        Assert.That(expanded.y, Is.EqualTo(normal.y));
    }
}
