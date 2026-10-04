using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

[Category("PurgersRegression")]
public sealed class ActiveAbilityPhysicsTests
{
    private Scene scene;
    private PhysicsScene physics;
    private Type Utility => typeof(Player).Assembly.GetType("ActiveAbilityPhysics");

    [SetUp] public void SetUp()
    {
        scene = EditorSceneManager.NewPreviewScene();
        physics = scene.GetPhysicsScene();
    }
    [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

    private GameObject Box(string name, Vector3 position)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        obj.transform.position = position;
        obj.AddComponent<BoxCollider>();
        return obj;
    }

    private RaycastHit[] Query(Vector3 direction)
    {
        Assert.That(Utility, Is.Not.Null, "需要共用 Runner PhysicsScene sweep，避免高速穿牆與固定容量漏掉近牆。");
        var method = Utility.GetMethod("SphereCastSorted", BindingFlags.Static | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);
        object[] args = { physics, Vector3.zero, direction, 50f, 0.05f, (LayerMask)(~0), null, new RaycastHit[1] };
        int count = (int)method.Invoke(null, args);
        return ((RaycastHit[])args[7]).Take(count).ToArray();
    }

    [Test] public void Sweep_GrowsBufferAndReturnsNearestObstacleBeforeTarget()
    {
        Box("Target", Vector3.forward * 15f);
        Box("Wall", Vector3.forward * 4f);
        Box("Far wall", Vector3.forward * 22f);
        Physics.SyncTransforms();
        var hits = Query(Vector3.forward);
        Assert.That(hits.Length, Is.EqualTo(3));
        Assert.That(hits[0].collider.name, Is.EqualTo("Wall"));
        Assert.That(hits[1].collider.name, Is.EqualTo("Target"));
    }

    [Test] public void Sweep_UsesSuppliedPhysicsScene()
    {
        Box("Own scene target", Vector3.forward * 10f);
        Scene otherScene = EditorSceneManager.NewPreviewScene();
        var unrelated = new GameObject("Other runner wall");
        SceneManager.MoveGameObjectToScene(unrelated, otherScene);
        unrelated.transform.position = Vector3.forward * 2f;
        unrelated.AddComponent<BoxCollider>();
        try
        {
            Physics.SyncTransforms();
            var hits = Query(Vector3.forward);
            Assert.That(hits.Length, Is.EqualTo(1));
            Assert.That(hits[0].collider.name, Is.EqualTo("Own scene target"));
        }
        finally { EditorSceneManager.ClosePreviewScene(otherScene); }
    }

    [Test] public void ReceiverIdentity_CollidersOnOneEnemyResolveToSameOwner()
    {
        Assert.That(Utility, Is.Not.Null);
        var root = Box("Enemy", Vector3.zero);
        var receiver = root.AddComponent<TestDamageReceiver>();
        var child = Box("Second hit region", Vector3.up);
        child.transform.SetParent(root.transform, true);
        var method = Utility.GetMethod("ResolveReceiver", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, new object[] { root }), Is.SameAs(receiver));
        Assert.That(method.Invoke(null, new object[] { child }), Is.SameAs(receiver));
    }

    [Test] public void ExplosionOriginInsideWall_RemainsOccluded()
    {
        Assert.That(Utility, Is.Not.Null);
        Box("Containing wall", Vector3.zero);
        Physics.SyncTransforms();
        var method = Utility.GetMethod("IsWorldOccluded", BindingFlags.Public | BindingFlags.Static);
        object[] args = { physics, Vector3.zero, Vector3.forward * 10f, (LayerMask)(~0), new RaycastHit[1] };
        Assert.That((bool)method.Invoke(null, args), Is.True,
            "Physics.Raycast 本來會漏掉包含起點的牆，爆炸不能因此穿牆。 ");
    }
}
