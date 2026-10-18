using System;
using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Purgers.Enemy.Encounter;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemySpawnCompositionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static NetworkPrefabRef Prefab => (NetworkPrefabRef)NetworkObjectGuid.Parse("0123456789abcdef0123456789abcdef");

    [Test]
    public void ZoneExposesIndependentChanceAndCountWithoutDiscardingLegacyPrefabs()
    {
        var root = new GameObject("Spawn Composition Contract");
        try
        {
            var zone = root.AddComponent<EnemySpawnZone>();
            var data = new SerializedObject(zone);
            Assert.That(data.FindProperty("enemyEntries"), Is.Not.Null);
            Assert.That(data.FindProperty("enemyPrefabs"), Is.Not.Null);
            Assert.That(zone.UsesEnemyEntries, Is.False, "Existing assets keep their original mode.");
            Set(zone, "enemyEntries", new[] { new EnemySpawnEntry(Prefab, 0f, 7) });
            Assert.That(zone.UsesEnemyEntries, Is.True, "An explicit all-zero list must not fall back to old spawns.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(0f, 0f, 0)]
    [TestCase(0f, 1f, 0)]
    [TestCase(100f, 0f, 7)]
    [TestCase(100f, 1f, 7)]
    [TestCase(60f, 0.599f, 7)]
    [TestCase(60f, 0.6f, 0)]
    [TestCase(60f, 0.9f, 0)]
    public void ChanceHasExactBoundariesAndReturnsTheConfiguredCount(float chance, float sample, int expected)
    {
        Assert.That(new EnemySpawnEntry(Prefab, chance, 7).RollCount(sample), Is.EqualTo(expected));
    }

    [Test]
    public void IndependentChancesCanBothSucceedOrBothFailWithoutNormalization()
    {
        var a = new EnemySpawnEntry(Prefab, 60f, 3);
        var b = new EnemySpawnEntry(Prefab, 30f, 5);
        Assert.That(a.RollCount(0.2f), Is.EqualTo(3));
        Assert.That(b.RollCount(0.2f), Is.EqualTo(5));
        Assert.That(a.RollCount(0.8f) + b.RollCount(0.8f), Is.Zero);
    }

    [Test]
    public void InvalidPrefabAndNonFiniteChanceCannotProduceEnemies()
    {
        Assert.That(new EnemySpawnEntry(default, 100f, 3).RollCount(0f), Is.Zero);
        Assert.That(new EnemySpawnEntry(Prefab, float.NaN, 3).RollCount(0f), Is.Zero);
        Assert.That(new EnemySpawnEntry(Prefab, 100f, 3).RollCount(float.NaN), Is.Zero);
        Assert.That(new EnemySpawnEntry(Prefab, -1f, 3).RollCount(0f), Is.Zero);
        Assert.That(new EnemySpawnEntry(Prefab, 101f, 0).RollCount(1f), Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DirectorSnapshotsEachSelectedCountAndCancellationRemovesThePlan(bool selected)
    {
        var root = new GameObject("Composition Plan");
        var state = UnityEngine.Random.state;
        try
        {
            var zone = root.AddComponent<EnemySpawnZone>();
            var director = root.AddComponent<EnemyEncounterDirector>();
            var a = new EnemySpawnEntry(Prefab, selected ? 100 : 0, 3);
            var b = new EnemySpawnEntry(Prefab, selected ? 100 : 0, 5);
            Set(zone, "enemyEntries", new[] { a, b });
            var zoneType = typeof(EnemyEncounterDirector).GetNestedType("ZoneRuntime", BindingFlags.NonPublic);
            var candidateType = typeof(EnemyEncounterDirector).GetNestedType("CandidateRuntime", BindingFlags.NonPublic);
            var runtime = Activator.CreateInstance(zoneType, true);
            zoneType.GetField("Zone").SetValue(runtime, zone);
            var candidates = (IList)zoneType.GetField("Candidates").GetValue(runtime);
            foreach (var entry in new[] { a, b })
            {
                var candidate = Activator.CreateInstance(candidateType, true);
                candidateType.GetField("Entry").SetValue(candidate, entry);
                candidateType.GetField("Prefab").SetValue(candidate, Prefab);
                candidates.Add(candidate);
            }
            typeof(EnemyEncounterDirector).GetMethod("BuildActivationBatches", Private).Invoke(director, new[] { runtime });
            var batches = (IList)typeof(EnemyEncounterDirector).GetField("pendingBatches", Private).GetValue(director);
            Assert.That(batches.Count, Is.EqualTo(selected ? 2 : 0));
            if (selected)
            {
                Set(a, "count", 99);
                Assert.That(batches[0].GetType().GetField("Remaining").GetValue(batches[0]), Is.EqualTo(3));
                Assert.That(batches[1].GetType().GetField("Remaining").GetValue(batches[1]), Is.EqualTo(5));
            }
            typeof(EnemyEncounterDirector).GetMethod("CancelPendingActivation", Private).Invoke(director, null);
            Assert.That(batches.Count, Is.Zero);
        }
        finally { UnityEngine.Random.state = state; Object.DestroyImmediate(root); }
    }

    [Test]
    public void AllEnemyKindsScatterInsideMixedZoneRegardlessOfLegacyKind()
    {
        var root = new GameObject("Mixed Spawn Geometry");
        var state = UnityEngine.Random.state;
        try
        {
            var zone = root.AddComponent<EnemySpawnZone>();
            Set(zone, "locomotionKind", EnemyLocomotionKind.Stationary);
            root.transform.SetPositionAndRotation(new Vector3(10, 3, 20), Quaternion.Euler(0, 43, 0));
            UnityEngine.Random.InitState(9812);
            Vector3 stationary = zone.RandomPoint(EnemyLocomotionKind.Stationary);
            Assert.That(zone.Contains(stationary), Is.True);
            Assert.That(stationary, Is.Not.EqualTo(root.transform.position));
            Vector3 ground = zone.RandomPoint(EnemyLocomotionKind.Ground);
            Vector3 flying = zone.RandomPoint(EnemyLocomotionKind.FreeFlying);
            Assert.That(zone.Contains(ground) && zone.Contains(flying), Is.True);
            Assert.That(ground, Is.Not.EqualTo(root.transform.position));
            Assert.That(flying, Is.Not.EqualTo(root.transform.position));
        }
        finally { UnityEngine.Random.state = state; Object.DestroyImmediate(root); }
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
}
