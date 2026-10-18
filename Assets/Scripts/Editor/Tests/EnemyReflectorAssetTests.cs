using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Fusion;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public class EnemyReflectorAssetTests
{
    [Test] public void SeparatePrefabUsesExistingCombatAndHealthOwners()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath);
        Assert.That(root, Is.Not.Null);
        Assert.That(root.GetComponent<EnemyReflectorShield>(), Is.Not.Null);
        Assert.That(root.GetComponent<TestDamageReceiver>(), Is.Not.Null);
        Assert.That(root.GetComponent<EnemyCombatDecisionController>(), Is.Not.Null);
        Assert.That(root.GetComponent<EnemyMovementOwnership>(), Is.Not.Null);
        Assert.That(root.GetComponents<EnemyCombatOption>().Length, Is.EqualTo(1));
        var attack = root.GetComponent<EnemyRangedProjectileAttack>();
        var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_A.prefab").GetComponent<EnemyRangedProjectileAttack>();
        Assert.That(attack.MaximumStartDistance, Is.GreaterThan(original.MaximumStartDistance));
        Assert.That(new SerializedObject(attack).FindProperty("projectileDamage").floatValue,
            Is.GreaterThan(new SerializedObject(original).FindProperty("projectileDamage").floatValue));
    }
    [Test] public void ReflectionIsOptInAndShieldVisualsCannotBlockPhysics()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Projectiles/EnemyProjectile_RangedA.prefab");
        var projectile = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.ProjectilePath);
        Assert.That(new SerializedObject(source.GetComponent<EnemyProjectile>()).FindProperty("reflectable").boolValue, Is.False);
        Assert.That(new SerializedObject(projectile.GetComponent<EnemyProjectile>()).FindProperty("reflectable").boolValue, Is.True);
        Assert.That(projectile.GetComponentsInChildren<Collider>(true), Is.Empty);
        var shield = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath).GetComponent<EnemyReflectorShield>();
        Assert.That(shield.StunSeconds, Is.GreaterThanOrEqualTo(.02f));
        var so = new SerializedObject(shield);
        foreach (string field in new[] {"frontShield", "rearShield"})
        {
            var renderer = so.FindProperty(field).objectReferenceValue as Renderer;
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.GetComponent<Collider>(), Is.Null);
            Assert.That(renderer.GetComponent<NetworkObject>(), Is.Null);
        }
    }
    [Test] public void UnspawnedQueriesAreSafeAndShieldFailsClosed()
    {
        var shield = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath).GetComponent<EnemyReflectorShield>();
        Assert.That(shield.IsOpening, Is.False);
        var request = new DamageRequest { RequestedDamage = 50, BaseDamage = 50, HitDirection = Vector3.forward };
        Assert.DoesNotThrow(() => shield.ModifyDamageRequest(ref request));
        Assert.That(request.RequestedDamage, Is.Zero);
        Assert.That(request.BlockedDamage, Is.EqualTo(50));
        Assert.That(request.BaseDamage, Is.EqualTo(50));
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.ProjectilePath).GetComponent<EnemyProjectile>().CanReflect, Is.False);
    }
    [Test] public void ShieldPanelsStraddleBodyAndHitboxBroadphaseCoversIt()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath);
        var body=root.GetComponentsInChildren<Renderer>().Where(r=>r.GetComponent<Hitbox>()!=null).ToArray();
        Bounds bounds=body[0].bounds;foreach(var renderer in body)bounds.Encapsulate(renderer.bounds);
        var front=root.transform.Find("Shield_Front");var rear=root.transform.Find("Shield_Rear");
        Assert.That(front.position.z,Is.GreaterThan(bounds.max.z));Assert.That(rear.position.z,Is.LessThan(bounds.min.z));
        Assert.That(Vector3.Distance(Vector3.ProjectOnPlane(front.position-bounds.center,Vector3.forward),Vector3.zero),Is.LessThan(.05f));
        var hitRoot=root.GetComponent<HitboxRoot>();Assert.That(Vector3.Distance(hitRoot.Offset,bounds.center),Is.LessThan(.05f));
        Assert.That(hitRoot.BroadRadius,Is.GreaterThanOrEqualTo(bounds.extents.magnitude));
    }
    [Test] public void AssetsHaveNoMissingScriptsOrBrokenReferences()
    {
        foreach (var path in new[] {EnemyReflectorSetup.PrefabPath, EnemyReflectorSetup.ProjectilePath})
        foreach (var child in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Transform>(true))
        {
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero);
            foreach (var component in child.GetComponents<Component>())
            {
                var property = new SerializedObject(component).GetIterator();
                while(property.Next(true)) if(property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null)
                    Assert.That(property.objectReferenceInstanceIDValue, Is.Zero, path+":"+component.GetType().Name+"."+property.propertyPath);
            }
        }
    }
    [Test] public void GroundPrototypeDisablesAutonomousTranslationAndKeepsGroundOwners()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath);
        Assert.That(root.GetComponent<EnemyActor>().Definition.LocomotionKind,Is.EqualTo(EnemyLocomotionKind.Ground));
        var policy=new SerializedObject(root.GetComponent<EnemyMovementOwnership>()).FindProperty("allowAutonomousMovement");
        Assert.That(policy,Is.Not.Null);Assert.That(policy.boolValue,Is.False);
        Assert.That(root.GetComponent<EnemyGroundPatrolNavigator>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyGroundChaseMotor>(),Is.Not.Null);
        Assert.That(root.GetComponent<EnemyFlyingPatrolNavigator>(),Is.Null);
        Assert.That(root.GetComponent<EnemyFlyingChaseMotor>(),Is.Null);
        var body=root.GetComponentsInChildren<Renderer>().Where(r=>r.GetComponent<Hitbox>()!=null).ToArray();
        Assert.That(body.Min(r=>r.bounds.min.y),Is.InRange(0f,.4f),"body must sit above the bottom shield at ground level");
        var muzzle=(Transform)new SerializedObject(root.GetComponent<EnemyRangedProjectileAttack>()).FindProperty("muzzle").objectReferenceValue;
        Assert.That(muzzle.position.y,Is.InRange(1f,3f),"grounded muzzle must not remain at old flying offset");
    }
    [Test] public void SixVisualShieldFacesEncloseBody()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath);
        var data=new SerializedObject(root.GetComponent<EnemyReflectorShield>());
        var extra=data.FindProperty("additionalShields");Assert.That(extra,Is.Not.Null);Assert.That(extra.arraySize,Is.EqualTo(4));
        var body=root.GetComponentsInChildren<Renderer>().Where(r=>r.GetComponent<Hitbox>()!=null).ToArray();
        Bounds bounds=body[0].bounds;foreach(var r in body)bounds.Encapsulate(r.bounds);
        for(int i=0;i<4;i++){var r=(Renderer)extra.GetArrayElementAtIndex(i).objectReferenceValue;Assert.That(r,Is.Not.Null);Assert.That(r.GetComponent<Collider>(),Is.Null);}
        Assert.That(root.transform.Find("Shield_Left").position.x,Is.LessThan(bounds.min.x));
        Assert.That(root.transform.Find("Shield_Right").position.x,Is.GreaterThan(bounds.max.x));
        Assert.That(root.transform.Find("Shield_Top").position.y,Is.GreaterThan(bounds.max.y));
        Assert.That(root.transform.Find("Shield_Bottom").position.y,Is.LessThan(bounds.min.y));
        Assert.That(root.transform.Find("Shield_Bottom").GetComponent<Renderer>().bounds.min.y,Is.EqualTo(0).Within(.01f));
    }
    [Test] public void ReflectorProjectileVisualDiameterMatchesCollisionRadius()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.ProjectilePath);
        float radius=new SerializedObject(root.GetComponent<EnemyProjectile>()).FindProperty("collisionRadius").floatValue;
        Assert.That(root.transform.Find("ProjectileSphere").GetComponent<Renderer>().bounds.size.x,Is.EqualTo(radius*2).Within(.0001f));
    }
    [Test] public void CounterplayOptionsAreEnabledOnlyOnReflector()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.PrefabPath);
        var attack=new SerializedObject(root.GetComponent<EnemyRangedProjectileAttack>());
        var wait=attack.FindProperty("waitForPreviousProjectile");Assert.That(wait,Is.Not.Null);Assert.That(wait.boolValue,Is.True);
        var projectile=new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.ProjectilePath).GetComponent<EnemyProjectile>());
        Assert.That(projectile.FindProperty("reflectionCapturePadding").floatValue,Is.GreaterThan(0));
        Assert.That(projectile.FindProperty("destroyableByPlayerShots").boolValue,Is.True);
        Assert.That(projectile.FindProperty("matchVisualToCollisionRadius").boolValue,Is.True);
        var original=new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Projectiles/EnemyProjectile_RangedA.prefab").GetComponent<EnemyProjectile>());
        Assert.That(original.FindProperty("destroyableByPlayerShots").boolValue,Is.False);
    }
    [Test] public void CollisionRadiusResizesVisualInWorldSpaceEvenUnderScaledParent()
    {
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyReflectorSetup.ProjectilePath));
        try
        {
            root.transform.localScale=new Vector3(2,3,4);var projectile=root.GetComponent<EnemyProjectile>();
            var data=new SerializedObject(projectile);data.FindProperty("collisionRadius").floatValue=.8f;data.ApplyModifiedPropertiesWithoutUndo();projectile.SyncCollisionVisual();
            Vector3 size=root.transform.Find("ProjectileSphere").GetComponent<Renderer>().bounds.size;
            Assert.That(size.x,Is.EqualTo(1.6f).Within(.001f));Assert.That(size.y,Is.EqualTo(1.6f).Within(.001f));Assert.That(size.z,Is.EqualTo(1.6f).Within(.001f));
        }
        finally {Object.DestroyImmediate(root);}
    }
}
