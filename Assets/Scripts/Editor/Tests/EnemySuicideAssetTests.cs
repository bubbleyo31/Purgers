using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Fusion;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemySuicideAssetTests
{
    [Test] public void PrefabUsesExistingOwnersAndOneUniqueAttack()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemySuicideSetup.PrefabPath);
        Assert.That(root,Is.Not.Null);
        Assert.That(root.GetComponents<EnemyCombatOption>().Length,Is.EqualTo(1));
        Assert.That(root.GetComponent<EnemySuicideAttack>().OptionId,Is.EqualTo(EnemyCombatOptionId.SuicidePounce));
        Assert.That(root.GetComponent<EnemyActor>().Definition.LocomotionKind,Is.EqualTo(EnemyLocomotionKind.Ground));
        Assert.That(root.GetComponent<EnemyGroundPatrolNavigator>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyGroundChaseMotor>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyCombatDecisionController>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyDeathLifecycleController>(),Is.Not.Null);
        Assert.That(root.GetComponent<HitboxRoot>().Hitboxes.Length,Is.EqualTo(1));
        Assert.That(root.GetComponentsInChildren<Animator>().Length,Is.EqualTo(1));
        Assert.That(root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>().Length,Is.Zero);
    }
    [Test] public void VfxReferenceMatchesUsersPrefabAndDiameter()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemySuicideSetup.PrefabPath);
        var so=new SerializedObject(root.GetComponent<EnemySuicideAttack>());
        Assert.That(AssetDatabase.GetAssetPath(so.FindProperty("explosionVfx").objectReferenceValue),Is.EqualTo(EnemySuicideSetup.VfxPath));
        Assert.That(so.FindProperty("vfxReferenceDiameter").floatValue,Is.EqualTo(1f));
        Assert.That(so.FindProperty("stationaryDetonationDelay").floatValue,Is.Zero);
        Assert.That(so.FindProperty("centerSlowStrength").floatValue,Is.EqualTo(.5f));
    }
    [Test] public void NoMissingScriptsOrBrokenSerializedObjectReferences()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemySuicideSetup.PrefabPath);
        foreach(var transform in root.GetComponentsInChildren<Transform>(true))
        {
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),Is.Zero);
            foreach(var component in transform.GetComponents<Component>())
            {
                var serialized=new SerializedObject(component);var p=serialized.GetIterator();
                while(p.Next(true))
                    if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue==null)
                        Assert.That(p.objectReferenceInstanceIDValue,Is.Zero,component.GetType().Name+"."+p.propertyPath);
            }
        }
    }
    [TestCase(DamageType.Bullet)] [TestCase(DamageType.Melee)] [TestCase(DamageType.Explosion)] [TestCase(DamageType.DamageOverTime)]
    public void EveryPlayerDamageKindTriggers(DamageType kind)
    {
        var request=new DamageRequest{RequestedDamage=.01f,Attacker=PlayerRef.FromIndex(0),DamageType=kind};
        Assert.That(EnemySuicideRules.IsPlayerTrigger(request),Is.True);
        request.Attacker=PlayerRef.None;Assert.That(EnemySuicideRules.IsPlayerTrigger(request),Is.False);
    }
    [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void InvalidDamageDoesNotTrigger(float amount)
    {
        var request=new DamageRequest{RequestedDamage=amount,Attacker=PlayerRef.FromIndex(0)};
        Assert.That(EnemySuicideRules.IsPlayerTrigger(request),Is.False);
    }
}
