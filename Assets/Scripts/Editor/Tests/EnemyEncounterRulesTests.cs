using NUnit.Framework;
using Purgers.Enemy.Encounter;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyEncounterRulesTests
{
    [TestCase(false, 1, true)]
    [TestCase(false, -1, false)]
    [TestCase(true, 1, true)]
    [TestCase(true, -1, true)]
    public void GateDirectionOptionPreservesSingleDirectionAndAllowsReverse(
        bool bidirectional, int direction, bool expected)
    {
        var obj = new GameObject("Encounter Gate Direction Test");
        try
        {
            obj.transform.SetPositionAndRotation(new Vector3(13f, 2f, -7f), Quaternion.Euler(0f, 63f, 0f));
            var gate = obj.AddComponent<EnemySpawnApproachGate>();
            var serialized = new UnityEditor.SerializedObject(gate);
            var option = serialized.FindProperty("bidirectional");
            Assert.That(option, Is.Not.Null, "Gate must expose its bidirectional option.");
            Assert.That(option.boolValue, Is.False, "Existing assets must remain directional by default.");
            option.boolValue = bidirectional;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Vector3 travel = obj.transform.forward * direction;
            Vector3 previous = obj.transform.position - travel * 8f;
            Vector3 current = obj.transform.position + travel * 2f;
            Assert.That(gate.TryCross(previous, current, out float fraction), Is.EqualTo(expected));
            if (expected) Assert.That(fraction, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(gate.TryCross(previous, current, out _, out Vector3 approachForward), Is.EqualTo(expected));
            Assert.That(Vector3.Distance(approachForward, expected ? travel : Vector3.zero), Is.LessThan(0.0001f));
            if (expected)
            {
                Assert.That(Vector3.Dot(current + travel * 10f - current, approachForward), Is.GreaterThan(2f));
                Assert.That(Vector3.Dot(current - travel * 10f - current, approachForward), Is.LessThan(0f));
            }
            Assert.That(gate.TryCross(previous + obj.transform.right * 20f,
                current + obj.transform.right * 20f, out _), Is.False);
            Assert.That(gate.TryCross(previous + Vector3.up * 20f,
                current + Vector3.up * 20f, out _), Is.False);
            Assert.That(gate.TryCross(current, current + travel, out _), Is.False);
            Assert.That(gate.TryCross(obj.transform.position, current, out _), Is.False);
        }
        finally { Object.DestroyImmediate(obj); }
    }
    [Test]
    public void FastSegmentCrossesDirectionalGateOnlyInConfiguredDirection()
    {
        Matrix4x4 gate = Matrix4x4.identity;

        Assert.That(EnemyEncounterRules.TryCrossGate(
            gate, new Vector3(0f, 0f, -30f), new Vector3(0f, 0f, 30f),
            8f, 4f, out float crossing), Is.True);
        Assert.That(crossing, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(EnemyEncounterRules.TryCrossGate(
            gate, new Vector3(0f, 0f, 30f), new Vector3(0f, 0f, -30f),
            8f, 4f, out _), Is.False);
    }

    [Test]
    public void CrossingOutsideRectangleOrRemainingOnOneSideDoesNotTrigger()
    {
        Matrix4x4 gate = Matrix4x4.identity;

        Assert.That(EnemyEncounterRules.TryCrossGate(
            gate, new Vector3(5f, 0f, -2f), new Vector3(5f, 0f, 2f),
            8f, 4f, out _), Is.False);
        Assert.That(EnemyEncounterRules.TryCrossGate(
            gate, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 2f),
            8f, 4f, out _), Is.False);
    }

    [Test]
    public void SixthSuccessfulZoneUnlocksOldestAndRelockRotates()
    {
        var lockout = new RecentZoneLock<int>(5);
        for (int zone = 1; zone <= 5; zone++)
            Assert.That(lockout.MarkSuccessful(zone), Is.EqualTo(0));

        Assert.That(lockout.MarkSuccessful(6), Is.EqualTo(1));
        Assert.That(lockout.IsLocked(1), Is.False);
        Assert.That(lockout.IsLocked(2), Is.True);
        Assert.That(lockout.MarkSuccessful(1), Is.EqualTo(2));
        Assert.That(lockout.IsLocked(1), Is.True);
        Assert.That(lockout.IsLocked(2), Is.False);
    }

    [Test]
    public void LockedZoneCannotConsumeAnotherSlot()
    {
        var lockout = new RecentZoneLock<int>(2);
        Assert.That(lockout.MarkSuccessful(1), Is.EqualTo(0));
        Assert.That(lockout.MarkSuccessful(1), Is.EqualTo(0));
        Assert.That(lockout.MarkSuccessful(2), Is.EqualTo(0));
        Assert.That(lockout.IsLocked(1), Is.True);
        Assert.That(lockout.MarkSuccessful(3), Is.EqualTo(1));
    }

    [Test]
    public void GroundSpawnProjectsNavMeshHeightToNearbyCollider()
    {
        var floor = new GameObject("Encounter Ground Probe Test Floor");
        floor.transform.position = new Vector3(10000f, -0.5f, 10000f);
        floor.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        try
        {
            Vector3 navPoint = new Vector3(10000f, 0.09f, 10000f);
            Assert.That(EnemyEncounterRules.TryProjectGroundSpawn(
                Physics.defaultPhysicsScene, navPoint, 1, 0.3f, out Vector3 grounded), Is.True);
            Assert.That(grounded.y, Is.EqualTo(0f).Within(0.001f));

            Assert.That(EnemyEncounterRules.TryProjectGroundSpawn(
                Physics.defaultPhysicsScene, navPoint + Vector3.up * 0.4f,
                1, 0.3f, out _), Is.False);
            Assert.That(EnemyEncounterRules.TryProjectGroundSpawn(
                Physics.defaultPhysicsScene, navPoint, 0, 0.3f, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(floor);
        }
    }
}
