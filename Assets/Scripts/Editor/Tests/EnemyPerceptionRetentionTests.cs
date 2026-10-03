using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyPerceptionRetentionTests
{
    // A shared report must not grant live tracking; grace cannot renew itself.
    [TestCase(true, true, 2.9f, 25f, 25f, true)]
    [TestCase(true, true, 0.01f, 25f, 25f, true)]
    [TestCase(true, true, 0f, 25f, 25f, false)]
    [TestCase(true, true, -1f, 25f, 25f, false)]
    [TestCase(true, true, 3f, 625f, 25f, true)]
    [TestCase(true, true, 3f, 626f, 25f, false)]
    [TestCase(true, false, 3f, 25f, 25f, false)]
    [TestCase(false, true, 3f, 25f, 25f, false)]
    public void RetentionNeedsPersonalSightUnexpiredGraceAndDistance(
        bool enabled, bool seen, float remainingSeconds, float distanceSquared,
        float radius, bool expected)
    {
        var method = typeof(EnemyPerceptionController).GetMethod("CanRetainTarget",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Retention must have a finite personal-sighting deadline.");
        Assert.That(method.Invoke(null, new object[] {
            enabled, seen, remainingSeconds, distanceSquared, radius }), Is.EqualTo(expected));
    }

    [Test]
    public void NewPerceptionDefaultsToThreeSecondGraceAndNoCombatSightBeforeSpawn()
    {
        var root = new GameObject("Perception Retention Contract");
        try
        {
            var perception = root.AddComponent<EnemyPerceptionController>();
            var serialized = new SerializedObject(perception);
            var seconds = serialized.FindProperty("lockedTargetRetentionSeconds");
            Assert.That(seconds, Is.Not.Null);
            Assert.That(seconds.floatValue, Is.EqualTo(3f));
            var sight = typeof(EnemyPerceptionController).GetProperty("HasCombatSight");
            Assert.That(sight, Is.Not.Null);
            Assert.That(sight.GetValue(perception), Is.False,
                "Unspawned objects must not read Networked state.");
        }
        finally { Object.DestroyImmediate(root); }
    }
}
