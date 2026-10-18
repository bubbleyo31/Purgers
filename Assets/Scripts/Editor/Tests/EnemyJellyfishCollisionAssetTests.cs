using System.Linq;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyJellyfishCollisionAssetTests
{
    [TestCase("Enemy_Ranged_A")]
    [TestCase("Enemy_Ranged_B")]
    public void EightRegionsHaveOneHeadAndCompleteDamageDeathAndMovementWiring(string name)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/" + name + ".prefab");
        var hits = root.GetComponentsInChildren<Hitbox>(true);
        var colliders = root.GetComponentsInChildren<Collider>(true);
        Assert.That(hits.Length, Is.EqualTo(8));
        Assert.That(colliders.Length, Is.EqualTo(8));
        Assert.That(hits.Count(h => h.GetComponent<WeaponHitZone>().IsHead), Is.EqualTo(1));
        Assert.That(hits.Single(h => h.GetComponent<WeaponHitZone>().IsHead).name, Is.EqualTo("JellyfishCollision_SmallBell"));
        Assert.That(hits.All(h => h.GetComponent<WeaponHitZone>().DamageMultiplier == 1), Is.True);
        var hitRoot = root.GetComponent<HitboxRoot>();
        CollectionAssert.AreEquivalent(hits, hitRoot.Hitboxes);
        Assert.That(hits.All(h => h.Root == hitRoot), Is.True);
        var death = new SerializedObject(root.GetComponent<CombatDeathHandler>()).FindProperty("collidersToDisableOnDeath");
        CollectionAssert.AreEquivalent(colliders, Enumerable.Range(0, death.arraySize).Select(i => death.GetArrayElementAtIndex(i).objectReferenceValue));
        Assert.That(root.GetComponent<EnemyJellyfishCollisionPose>(), Is.Not.Null);
        foreach (var motor in new Component[] {root.GetComponent<EnemyFlyingPatrolNavigator>(), root.GetComponent<EnemyFlyingChaseMotor>()})
        {
            var so = new SerializedObject(motor);
            Assert.That(so.FindProperty("bodyRadius").floatValue, Is.EqualTo(5.7f));
            Assert.That(so.FindProperty("bodyHeight").floatValue, Is.EqualTo(14.05f));
            Assert.That(so.FindProperty("bodyBaseOffset").floatValue, Is.EqualTo(-2.65f));
            Assert.That(so.FindProperty("collisionSkin").floatValue, Is.EqualTo(.1f));
        }
        Assert.That(root.GetComponentsInChildren<Component>(true).All(c => c != null), Is.True);
    }
}