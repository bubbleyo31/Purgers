using System.Linq;
using NUnit.Framework;
using Purgers.Enemy.Encounter;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyFlyingPatrolPlannerTests
{
    [Test]
    public void FreeFlyingUsesAutomaticAreaOnlyWhenManualReferenceIsEmpty()
    {
        var obj = new GameObject("Flying Zone Settings Test");
        var manualObject = new GameObject("Existing Manual Area");
        try
        {
            var zone = obj.AddComponent<EnemySpawnZone>();
            Assert.That(zone.UsesAutomaticFlyingPatrol, Is.False);
            var so = new UnityEditor.SerializedObject(zone);
            so.FindProperty("locomotionKind").intValue = (int)EnemyLocomotionKind.FreeFlying;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(zone.UsesAutomaticFlyingPatrol, Is.True);
            var manual = manualObject.AddComponent<EnemyPatrolArea>();
            so.FindProperty("flyingPatrolArea").objectReferenceValue = manual;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(zone.UsesAutomaticFlyingPatrol, Is.False);
            Assert.That(zone.FlyingPatrolArea, Is.SameAs(manual));
        }
        finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(manualObject); }
    }

    [Test]
    public void PrefabCapsuleQueriesRejectWallsAndOccupiedPointsWithoutSpawnedLifecycle()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var origin = new Vector3(10000, 0, 10000);
        var obj = new GameObject("Unspawned Flying Profile");
        obj.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
        var wall = new GameObject("Wall");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, scene);
        wall.transform.position = origin;
        wall.AddComponent<BoxCollider>().size = new Vector3(1, 30, 30);
        try
        {
            var nav = obj.AddComponent<EnemyFlyingPatrolNavigator>();
            var so = new UnityEditor.SerializedObject(nav);
            so.FindProperty("obstacleMask").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();
            var physics = scene.GetPhysicsScene();
            Assert.That(nav.IsPassageClear(physics, origin + Vector3.left * 3, origin + Vector3.right * 3), Is.False);
            Assert.That(nav.IsPassageClear(physics, origin, origin), Is.False);
            Assert.That(nav.IsPassageClear(physics, origin + Vector3.left * 3, origin + new Vector3(-3, 3, 3)), Is.True);
            var points = EnemyFlyingPatrolPlanner.Plan(Matrix4x4.Translate(origin),
                new Bounds(Vector3.zero, Vector3.one * 12),
                new Bounds(Vector3.left * 4, Vector3.one * 2), 5, 16, 1,
                (a,b) => nav.IsPassageClear(physics,a,b));
            Assert.That(points.Count, Is.EqualTo(16));
            Assert.That(points.All(p => p.x < origin.x - 0.5f), Is.True);
            so.FindProperty("obstacleMask").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(nav.IsPassageClear(physics, origin + Vector3.left * 3, origin + Vector3.left * 4), Is.False);
        }
        finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(wall); Physics.SyncTransforms(); }
    }

    [Test]
    public void ActualLargeFlyingCapsuleRejectsCeilingThatSmallCapsuleCouldFitUnder()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var origin = new Vector3(10000, 0, 10000);
        var obj = new GameObject("Flying Profile"); obj.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
        var ceiling = new GameObject("Ceiling");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ceiling, scene);
        ceiling.transform.position = origin + Vector3.up * 8;
        ceiling.AddComponent<BoxCollider>().size = new Vector3(30, 1, 30);
        try
        {
            var nav = obj.AddComponent<EnemyFlyingPatrolNavigator>();
            var so = new UnityEditor.SerializedObject(nav);
            so.FindProperty("obstacleMask").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();
            Assert.That(nav.IsPassageClear(scene.GetPhysicsScene(), origin, origin + Vector3.forward), Is.True);
            so.FindProperty("bodyRadius").floatValue = 2.85f;
            so.FindProperty("bodyHeight").floatValue = 12f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(nav.IsPassageClear(scene.GetPhysicsScene(), origin, origin + Vector3.forward), Is.False);
        }
        finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(ceiling); Physics.SyncTransforms(); }
    }
    [Test]
    public void OpenVolumeProducesBoundedDeterministicPointsIncludingSpawnVolume()
    {
        var matrix = Matrix4x4.TRS(new Vector3(37, 8, -12), Quaternion.Euler(0, 67, 0), Vector3.one * 2);
        var bounds = new Bounds(Vector3.up * 4, new Vector3(12, 8, 12));
        var spawn = new Bounds(Vector3.up, Vector3.one * 2);
        var points = EnemyFlyingPatrolPlanner.Plan(matrix, bounds, spawn, 4, 12, 0.5f, (a,b) => true);
        Assert.That(points.Count, Is.EqualTo(12));
        Assert.That(points.All(p => bounds.Contains(matrix.inverse.MultiplyPoint3x4(p))), Is.True);
        Assert.That(points.Any(p => spawn.Contains(matrix.inverse.MultiplyPoint3x4(p))), Is.True);
        var repeated = EnemyFlyingPatrolPlanner.Plan(matrix, bounds, spawn, 4, 12, 0.5f, (a,b) => true);
        CollectionAssert.AreEqual(points, repeated);
    }

    [Test]
    public void WallSeparatesComponentsAndKeepsOnlySpawnReachableSide()
    {
        var points = EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity,
            new Bounds(Vector3.zero, new Vector3(20, 6, 10)),
            new Bounds(new Vector3(-5, 0, 0), Vector3.one * 2), 5, 20, 1,
            (a,b) => a.x < 0 == b.x < 0);
        Assert.That(points.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(points.All(p => p.x < 0), Is.True);
    }

    [Test]
    public void TruncationPreservesReachabilityInsteadOfDroppingBridgePoints()
    {
        var points = EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity,
            new Bounds(Vector3.zero, new Vector3(12, 12, 12)),
            new Bounds(new Vector3(-4, -4, -4), Vector3.one), 6, 13, 1,
            (a,b) => Vector3.Distance(a,b) <= 2.1f);
        Assert.That(points.Count, Is.EqualTo(13));
        for (int i = 1; i < points.Count; i++)
            Assert.That(points.Take(i).Any(p => Vector3.Distance(p, points[i]) <= 2.1f), Is.True);
    }

    [Test]
    public void OccupiedVolumeAndIsolatedPointsDoNotProduceAnArea()
    {
        var bounds = new Bounds(Vector3.zero, Vector3.one * 8);
        Assert.That(EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity, bounds, bounds, 3, 12, 1,
            (a,b) => false), Is.Empty);
        Assert.That(EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity, bounds, bounds, 3, 12, 1,
            (a,b) => a == b), Is.Empty);
    }

    [Test]
    public void NonOverlappingSpawnAndPatrolVolumesFailClosed()
    {
        Assert.That(EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity,
            new Bounds(Vector3.zero, Vector3.one * 8),
            new Bounds(Vector3.one * 40, Vector3.one), 4, 12, 1, (a,b) => true), Is.Empty);
    }
}