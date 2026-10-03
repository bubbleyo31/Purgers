using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Purgers.Enemy.Encounter;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemySpawnOcclusionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void ZoneRequiresOcclusionByDefaultAndAllowsInspectorOptOut()
    {
        var root = new GameObject("Occlusion Option Test");
        try
        {
            var zone = root.AddComponent<EnemySpawnZone>();
            var data = new SerializedObject(zone);
            var option = data.FindProperty("requireSpawnOcclusion");
            Assert.That(option, Is.Not.Null);
            Assert.That(option.boolValue, Is.True);
            option.boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(typeof(EnemySpawnZone).GetProperty("RequireSpawnOcclusion").GetValue(zone), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase(true, false, 10f, false, false)]
    [TestCase(false, false, 10f, false, true)]
    [TestCase(true, true, 10f, false, true)]
    [TestCase(false, true, 10f, false, true)]
    [TestCase(false, false, 7f, false, false)]
    [TestCase(false, false, 10f, true, false)]
    [TestCase(true, true, 10f, true, false)]
    public void SwitchBypassesOnlyOcclusionAndKeepsDistanceToEveryPlayer(
        bool requireOcclusion, bool blocked, float distance, bool secondPlayerNear, bool expected)
    {
        var root = new GameObject("Occlusion Director Test");
        GameObject wall = null;
        try
        {
            var director = root.AddComponent<EnemyEncounterDirector>();
            var data = new SerializedObject(director);
            data.FindProperty("worldOcclusionMask").intValue = 1;
            data.ApplyModifiedPropertiesWithoutUndo();
            Vector3 origin = new Vector3(20000f, 100f, 20000f);
            AddPlayer(director, origin);
            if (secondPlayerNear) AddPlayer(director, origin + Vector3.forward * distance + Vector3.right);
            if (blocked)
            {
                wall = new GameObject("Occlusion Test Wall");
                wall.transform.position = origin + new Vector3(0f, 2f, distance * 0.5f);
                wall.AddComponent<BoxCollider>().size = new Vector3(6f, 6f, 0.5f);
                Physics.SyncTransforms();
            }
            var method = typeof(EnemyEncounterDirector).GetMethod("PassesPlayerVisibilityChecks", PrivateInstance);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(director, new object[] {
                Physics.defaultPhysicsScene, origin + Vector3.forward * distance, requireOcclusion
            }), Is.EqualTo(expected));
        }
        finally
        {
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void AddPlayer(EnemyEncounterDirector director, Vector3 position)
    {
        var snapshot = Activator.CreateInstance(typeof(EnemyEncounterDirector)
            .GetNestedType("PlayerSnapshot", BindingFlags.NonPublic));
        snapshot.GetType().GetField("Position").SetValue(snapshot, position);
        ((IList)typeof(EnemyEncounterDirector).GetField("players", PrivateInstance)
            .GetValue(director)).Add(snapshot);
    }
}
