using System;
using NUnit.Framework;
using Purgers.Enemy.Encounter;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyStationaryTests
{
    [Test]
    public void VariantCIsAppendedWithoutChangingExistingSerializedValues()
    {
        Assert.That((int)EnemyVariant.A, Is.EqualTo(0));
        Assert.That((int)EnemyVariant.B, Is.EqualTo(1));
        Assert.That(Enum.GetName(typeof(EnemyVariant), 2), Is.EqualTo("C"));
    }

    [TestCase(EnemyLocomotionKind.Stationary, true)]
    [TestCase(EnemyLocomotionKind.Ground, false)]
    [TestCase(EnemyLocomotionKind.FreeFlying, false)]
    [TestCase((EnemyLocomotionKind)99, false)]
    public void OnlyStationaryZoneAcceptsMissingPatrolArea(EnemyLocomotionKind kind, bool expected)
    {
        var obj = new GameObject("Stationary Zone Contract");
        try
        {
            var zone = obj.AddComponent<EnemySpawnZone>();
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("locomotionKind").intValue = (int)kind;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var method = typeof(EnemySpawnZone).GetMethod("IsPatrolConfigurationValid");
            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(zone, new object[] { null, null }), Is.EqualTo(expected));
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); }
    }
    [Test]
    public void StationarySpawnUsesAuthoredCenterWithChunkTransform()
    {
        var obj = new GameObject("Authored Turret Position");
        var randomState = UnityEngine.Random.state;
        try
        {
            var zone = obj.AddComponent<EnemySpawnZone>();
            var data = new SerializedObject(zone);
            data.FindProperty("locomotionKind").intValue = (int)EnemyLocomotionKind.Stationary;
            data.FindProperty("spawnBoxCenter").vector3Value = new Vector3(2f, 3f, -4f);
            data.ApplyModifiedPropertiesWithoutUndo();
            obj.transform.SetPositionAndRotation(new Vector3(14f, 9f, 2f), Quaternion.Euler(0f, 73f, 0f));
            obj.transform.localScale = new Vector3(2f, 1f, 3f);
            Vector3 expected = obj.transform.TransformPoint(new Vector3(2f, 3f, -4f));
            for (int i = 0; i < 8; i++)
                Assert.That(Vector3.Distance(zone.RandomPoint(), expected), Is.LessThan(0.0001f));
            Assert.That(zone.UsesAutomaticFlyingPatrol, Is.False);
        }
        finally { UnityEngine.Random.state = randomState; UnityEngine.Object.DestroyImmediate(obj); }
    }

    [TestCase("Projectile", typeof(EnemyRangedProjectileAttack), typeof(EnemyRangedBeamAttack))]
    [TestCase("Beam", typeof(EnemyRangedBeamAttack), typeof(EnemyRangedProjectileAttack))]
    public void TurretHasOneAttackAndNoNavigationDependency(string suffix, Type attack, Type otherAttack)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_C_" + suffix + ".prefab");
        Assert.That(prefab, Is.Not.Null);
        var actor = prefab.GetComponent<EnemyActor>();
        Assert.That(actor.Definition.LocomotionKind, Is.EqualTo(EnemyLocomotionKind.Stationary));
        Assert.That(actor.Definition.CombatFamily, Is.EqualTo(EnemyCombatFamily.Ranged));
        Assert.That(actor.Definition.Variant, Is.EqualTo(EnemyVariant.C));
        Assert.That(prefab.GetComponent(attack), Is.Not.Null);
        Assert.That(prefab.GetComponent(otherAttack), Is.Null);
        Assert.That(prefab.GetComponentsInChildren<EnemyCombatOption>(true).Length, Is.EqualTo(1));
        Assert.That(prefab.GetComponentsInChildren<EnemyIdlePatrolBrain>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<EnemyPatrolNavigator>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<EnemyChaseBrain>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<EnemyChaseMotor>(true), Is.Empty);
        Assert.That(prefab.GetComponent<Fusion.NetworkTransform>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyPerceptionController>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyAwarenessBrain>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyCombatDecisionController>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<TestDamageReceiver>(), Is.Not.Null);
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject), Is.Zero);
        foreach (var animator in prefab.GetComponentsInChildren<Animator>(true))
            Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(actor.TryInitializePatrolBeforeSpawn(null), Is.True);
        var networkObject = prefab.GetComponent<Fusion.NetworkObject>();
        CollectionAssert.AreEquivalent(prefab.GetComponentsInChildren<Fusion.NetworkBehaviour>(true),
            networkObject.NetworkedBehaviours, "Fusion bake must discard removed navigation behaviours.");
        var guid = Fusion.NetworkObjectGuid.Parse(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
        bool registered = false;
        foreach (var entry in Fusion.NetworkProjectConfig.Global.PrefabTable.Prefabs)
            if (entry.AssetGuid == guid) registered = true;
        Assert.That(registered, Is.True, "Turret must be registered for Runner.Spawn.");
    }

    [TestCase("Projectile", typeof(EnemyRangedProjectileAttack))]
    [TestCase("Beam", typeof(EnemyRangedBeamAttack))]
    public void ExistingAttackTurnsWholeRootWithoutTranslation(string suffix, Type attack)
    {
        var root = new GameObject("Turret Aim Contract");
        try
        {
            var option = root.AddComponent(attack);
            root.transform.position = new Vector3(13f, 7f, -4f);
            Vector3 before = root.transform.position;
            var context = new EnemyCombatContext(null, null, null, null,
                before + new Vector3(10f, 20f, 0f), 10f, 1f);
            attack.GetMethod("FaceTarget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(option, new object[] { context });
            Assert.That(root.transform.position, Is.EqualTo(before));
            Assert.That(Vector3.Angle(root.transform.forward, Vector3.right), Is.LessThan(0.01f));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestSpawnerUsesExplicitPointWithoutPatrolArea(bool assignPoint)
    {
        var obj = new GameObject("Stationary Test Spawner");
        var point = new GameObject("Authored Test Point");
        try
        {
            point.transform.position = new Vector3(20f, 6f, -12f);
            var spawner = obj.AddComponent<EnemyPatrolAreaTestSpawner>();
            var data = new SerializedObject(spawner);
            data.FindProperty("stationarySpawnPoint").objectReferenceValue = assignPoint ? point.transform : null;
            data.FindProperty("spawnPositionOffset").vector3Value = Vector3.up;
            data.ApplyModifiedPropertiesWithoutUndo();
            object[] args = { Vector3.zero, null };
            bool result = (bool)typeof(EnemyPatrolAreaTestSpawner).GetMethod("TrySelectStationarySpawnPoint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(spawner, args);
            Assert.That(result, Is.EqualTo(assignPoint));
            if (assignPoint) Assert.That((Vector3)args[0], Is.EqualTo(point.transform.position + Vector3.up));
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(point); }
    }

}