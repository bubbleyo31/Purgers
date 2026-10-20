using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Purgers.Enemy.Encounter;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemySpawnDiagnosticsTests
{
    private GameObject root;
    private EnemySpawnZone zone;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Spawn diagnostics");
        zone = root.AddComponent<EnemySpawnZone>();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    private string Validate()
    {
        var type = typeof(EnemySpawnZoneEditor).Assembly.GetType("EnemySpawnZoneDiagnostics");
        Assert.That(type, Is.Not.Null, "Inspector configuration diagnostics must exist.");
        return (string)type.GetMethod("DescribeConfiguration").Invoke(null, new object[] { zone });
    }

    [Test]
    public void MissingChunkGateAndCandidatesAreAllReportedTogether()
    {
        string report = Validate();
        StringAssert.Contains("MapChunk", report);
        StringAssert.Contains("Gate", report);
        StringAssert.Contains("清單", report);
    }

    [Test]
    public void NestedZoneGateDoesNotHideMissingOwnGate()
    {
        root.AddComponent<MapChunk>();
        var child = new GameObject("Nested zone");
        child.transform.SetParent(root.transform);
        child.AddComponent<EnemySpawnZone>();
        var gate = new GameObject("Other zone gate");
        gate.transform.SetParent(child.transform);
        gate.AddComponent<EnemySpawnApproachGate>();
        StringAssert.Contains("缺少啟用的 Gate", Validate());
    }

    [Test]
    public void ExplicitZeroChanceListDoesNotFallBackToLegacy()
    {
        var prefab = (NetworkPrefabRef)NetworkObjectGuid.Parse("0123456789abcdef0123456789abcdef");
        typeof(EnemySpawnZone).GetField("enemyEntries", Private).SetValue(zone,
            new[] { new EnemySpawnEntry(prefab, 0, 4) });
        StringAssert.Contains("0%", Validate());
    }

    [Test]
    public void ConfigurationInspectionDoesNotConsumeGameplayRandom()
    {
        var state = Random.state;
        try
        {
            Random.InitState(713);
            var before = Random.state;
            Validate();
            Assert.That(JsonUtility.ToJson(Random.state), Is.EqualTo(JsonUtility.ToJson(before)));
        }
        finally { Random.state = state; }
    }

    [Test]
    public void RuntimeRejectionIsRetainedWithConsoleDebugDisabled()
    {
        var director = root.AddComponent<EnemyEncounterDirector>();
        typeof(EnemyEncounterDirector).GetMethod("LogSkip", Private)
            .Invoke(director, new object[] { zone, "占用測試原因" });
        var property = typeof(EnemySpawnZone).GetProperty("InspectorEvents");
        Assert.That(property, Is.Not.Null, "Rejection must be visible without enabling Console logging.");
        var events = (IReadOnlyList<string>)property.GetValue(zone);
        Assert.That(events.Count, Is.EqualTo(1));
        StringAssert.Contains("占用測試原因", events[0]);
    }

    [Test]
    public void RuntimeHistoryIsBoundedAndKeepsLatestReason()
    {
        var director = root.AddComponent<EnemyEncounterDirector>();
        var skip = typeof(EnemyEncounterDirector).GetMethod("LogSkip", Private);
        for (int i = 0; i < 20; i++) skip.Invoke(director, new object[] { zone, "reason " + i });
        var property = typeof(EnemySpawnZone).GetProperty("InspectorEvents");
        Assert.That(property, Is.Not.Null);
        var events = (IReadOnlyList<string>)property.GetValue(zone);
        Assert.That(events.Count, Is.InRange(1, 8));
        StringAssert.Contains("reason 19", events.Last());
    }
}
