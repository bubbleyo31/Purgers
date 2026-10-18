using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class EnemyFlyingCollisionParityTests
{
    [Test]
    public void NegativeEnvelopeOffsetsFlyingSpawnSeedsAboveTheFloor()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("FlyingSpawnProfile");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var floor=new GameObject("Floor");SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=Vector3.down*.5f;
        floor.AddComponent<BoxCollider>().size=new Vector3(100,1,100);
        try {
            var nav=root.AddComponent<EnemyFlyingPatrolNavigator>();var so=new SerializedObject(nav);
            so.FindProperty("bodyRadius").floatValue=5.7f;so.FindProperty("bodyHeight").floatValue=14.05f;
            so.FindProperty("bodyBaseOffset").floatValue=-2.65f;so.FindProperty("collisionSkin").floatValue=.1f;so.FindProperty("obstacleMask").intValue=1;so.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();var physics=scene.GetPhysicsScene();
            var spawn=new Bounds(Vector3.up,new Vector3(10,2,10));var patrol=new Bounds(Vector3.up*6,new Vector3(40,12,40));
            var before=Purgers.Enemy.Encounter.EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity,patrol,spawn,5,16,4,(a,b)=>nav.IsPassageClear(physics,a,b));
            Assert.That(before.Count,Is.Zero);
            spawn.center+=nav.SpawnRootOffset;
            var after=Purgers.Enemy.Encounter.EnemyFlyingPatrolPlanner.Plan(Matrix4x4.identity,patrol,spawn,5,16,4,(a,b)=>nav.IsPassageClear(physics,a,b));
            Assert.That(after.Count,Is.EqualTo(16));
            so.FindProperty("bodyBaseOffset").floatValue=0;so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(nav.SpawnRootOffset,Is.EqualTo(Vector3.zero));
        } finally {EditorSceneManager.ClosePreviewScene(scene);}
    }

    [Test]
    public void FlyingKnockbackUsesTravelEnvelopeInsteadOfFirstTentacleCapsule()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("FlyingKnockbackProfile"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var navigator = root.AddComponent<EnemyFlyingPatrolNavigator>();
            var so = new SerializedObject(navigator);
            so.FindProperty("bodyRadius").floatValue = 3;
            so.FindProperty("bodyHeight").floatValue = 12;
            so.FindProperty("bodyBaseOffset").floatValue = -2;
            so.ApplyModifiedPropertiesWithoutUndo();
            var tentacle = new GameObject("FirstTentacle"); tentacle.transform.SetParent(root.transform, false);
            var capsule = tentacle.AddComponent<CapsuleCollider>(); capsule.radius = .1f; capsule.height = 1;
            var state = root.GetComponent<EnemyStateController>() ?? root.AddComponent<EnemyStateController>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(EnemyStateController).GetField("abilityBody", flags).SetValue(state, capsule);
            var bounds = (Bounds)typeof(EnemyStateController).GetMethod("GetAbilityControlCollisionBounds", flags).Invoke(state, null);
            Assert.That(bounds.center, Is.EqualTo(new Vector3(0, 4, 0)));
            Assert.That(bounds.size, Is.EqualTo(new Vector3(6, 12, 6)));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void PatrolAndChaseRejectTheSameOccupiedStartAndBlockedPassage()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("UnspawnedFlyingProfile");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        var wall = new GameObject("Wall");
        SceneManager.MoveGameObjectToScene(wall, scene);
        wall.AddComponent<BoxCollider>().size = new Vector3(1, 30, 30);
        try
        {
            var patrol = root.AddComponent<EnemyFlyingPatrolNavigator>();
            var chase = root.AddComponent<EnemyFlyingChaseMotor>();
            foreach (var component in new Component[] { patrol, chase })
            {
                var so = new SerializedObject(component);
                so.FindProperty("bodyRadius").floatValue = 2;
                so.FindProperty("bodyHeight").floatValue = 10;
                so.FindProperty("bodyBaseOffset").floatValue = -3;
                so.FindProperty("collisionSkin").floatValue = .1f;
                so.FindProperty("obstacleMask").intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Physics.SyncTransforms();
            var physics = scene.GetPhysicsScene();
            Assert.That(patrol.IsPassageClear(physics, Vector3.zero, Vector3.zero), Is.False);
            Assert.That(chase.IsPassageClear(physics, Vector3.zero, Vector3.zero), Is.False);
            Assert.That(patrol.IsPassageClear(physics, Vector3.left * 5, Vector3.right * 5), Is.False);
            Assert.That(chase.IsPassageClear(physics, Vector3.left * 5, Vector3.right * 5), Is.False);
            Assert.That(patrol.IsPassageClear(physics, Vector3.left * 5, new Vector3(-5, 3, 3)), Is.True);
            Assert.That(chase.IsPassageClear(physics, Vector3.left * 5, new Vector3(-5, 3, 3)), Is.True);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void NegativeOffsetIsIncludedAndForeignPhysicsSceneIsIgnored()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var empty = EditorSceneManager.NewPreviewScene();
        var obstacle = new GameObject("BelowRoot");
        SceneManager.MoveGameObjectToScene(obstacle, scene);
        obstacle.transform.position = new Vector3(0, -2, 0);
        obstacle.AddComponent<BoxCollider>().size = Vector3.one;
        try
        {
            Physics.SyncTransforms();
            var buffer = new Collider[1];
            var low = new EnemyCollisionEnvelope(1, 4, -3);
            var normal = new EnemyCollisionEnvelope(1, 4, 0);
            Assert.That(EnemyCollisionPassage.IsClear(scene.GetPhysicsScene(), Vector3.zero, Vector3.zero, low, .02f, 1, buffer), Is.False);
            Assert.That(EnemyCollisionPassage.IsClear(scene.GetPhysicsScene(), Vector3.zero, Vector3.zero, normal, .02f, 1, buffer), Is.True);
            Assert.That(EnemyCollisionPassage.IsClear(empty.GetPhysicsScene(), Vector3.zero, Vector3.zero, low, .02f, 1, buffer), Is.True);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); EditorSceneManager.ClosePreviewScene(empty); }
    }
}
