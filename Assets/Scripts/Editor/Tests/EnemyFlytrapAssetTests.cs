using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Fusion;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemyFlytrapAssetTests
{
    [Test] public void PlaceholderIsStationaryAndUsesExistingOwners()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFlytrapSetup.PrefabPath);
        Assert.That(root,Is.Not.Null);
        Assert.That(root.GetComponent<EnemyActor>().IsStationary,Is.True);
        Assert.That(root.GetComponent<GrappleInteractionTarget>(),Is.Not.Null);
        Assert.That(root.GetComponent<GrappleInteractionTarget>().CanBeSupportPulled,Is.False);
        Assert.That(root.GetComponent<GrappleInteractionTarget>().CanBeTankGathered,Is.False);
        Assert.That(root.GetComponents<EnemyCombatOption>().Length,Is.EqualTo(1));
        Assert.That(root.GetComponent<EnemyFlytrapAttack>().OptionId,Is.EqualTo(EnemyCombatOptionId.FlytrapCapture));
        Assert.That(root.GetComponent<EnemyFlytrapAttack>().MouthAnchor,Is.Not.Null);
        Assert.That(root.GetComponent<EnemyCombatDecisionController>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyMovementOwnership>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyDeathLifecycleController>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyFlytrapPresentation>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyIdlePatrolBrain>(),Is.Null);
        Assert.That(root.GetComponent<EnemyChaseBrain>(),Is.Null);
        Assert.That(root.GetComponent<EnemyStateController>().CanReceiveAbilityKnockback,Is.False);
        Assert.That(root.GetComponentsInChildren<Renderer>().Length,Is.EqualTo(1));
        Assert.That(root.GetComponent<HitboxRoot>().Hitboxes.Length,Is.EqualTo(1));
    }
    [Test] public void TimingDefaultsMatchApprovedRules()
    {
        var attack=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFlytrapSetup.PrefabPath).GetComponent<EnemyFlytrapAttack>();
        var so=new SerializedObject(attack);
        Assert.That(so.FindProperty("feedingSeconds").floatValue,Is.EqualTo(5));
        Assert.That(so.FindProperty("damageIntervalSeconds").floatValue,Is.EqualTo(.5f));
        Assert.That(so.FindProperty("cooldownSeconds").floatValue,Is.EqualTo(10));
    }
    [Test] public void NoMissingScriptsOrBrokenReferences()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFlytrapSetup.PrefabPath);
        foreach(var child in root.GetComponentsInChildren<Transform>(true))
        {
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject),Is.Zero);
            foreach(var component in child.GetComponents<Component>())
            {
                var so=new SerializedObject(component);var p=so.GetIterator();
                while(p.Next(true)) if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue==null)
                    Assert.That(p.objectReferenceInstanceIDValue,Is.Zero,component.GetType().Name+"."+p.propertyPath);
            }
        }
    }
}
