using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Fusion;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemyFlytrapStrikeTests
{
    private Scene scene;
    private PhysicsScene physics;
    private RaycastHit[] hits;
    private Collider[] overlaps;
    [SetUp] public void Setup()
    {
        scene = EditorSceneManager.NewPreviewScene();
        physics = scene.GetPhysicsScene();
    }
    [TearDown] public void Cleanup() { EditorSceneManager.ClosePreviewScene(scene); }
    private Collider Wall(Vector3 position)
    {
        var obj = new GameObject("Thin wall"); SceneManager.MoveGameObjectToScene(obj, scene);
        obj.transform.position = position;
        var box = obj.AddComponent<BoxCollider>(); box.size = new Vector3(3,3,.02f); return box;
    }
    private Collider PlayerBody(Vector3 position)
    {
        var obj = new GameObject("Player hit body"); SceneManager.MoveGameObjectToScene(obj, scene);
        obj.transform.position = position; obj.layer = 2;
        obj.AddComponent<NetworkObject>(); obj.AddComponent<PlayerHealth>();
        var body = obj.AddComponent<CapsuleCollider>(); body.height=2; body.radius=.35f; return body;
    }
    private Collider Cast(Vector3 origin, float length=10f)
    {
        Physics.SyncTransforms(); return EnemyFlytrapStrikePhysics.FirstContact(physics,origin,Vector3.forward,length,.15f,1,null,ref hits,ref overlaps,out _);
    }
    [Test] public void FirstPlayerWinsEvenOutsideObstacleMask()
    { var first=PlayerBody(Vector3.forward*2);PlayerBody(Vector3.forward*4);Assert.That(Cast(Vector3.zero),Is.SameAs(first)); }
    [Test] public void ThinWallStopsFastSweepBeforePlayer()
    { var wall=Wall(Vector3.forward*2);PlayerBody(Vector3.forward*4);Assert.That(Cast(Vector3.zero),Is.SameAs(wall)); }
    [Test] public void PlayerBeforeWallWins()
    { var player=PlayerBody(Vector3.forward*2);Wall(Vector3.forward*4);Assert.That(Cast(Vector3.zero),Is.SameAs(player)); }
    [Test] public void StartingInsidePlayerStillContacts()
    { var player=PlayerBody(Vector3.zero);Assert.That(Cast(Vector3.zero),Is.SameAs(player)); }
    [Test] public void PlayerOffLineIsNotHomingTarget()
    { PlayerBody(new Vector3(2,0,2));Assert.That(Cast(Vector3.zero),Is.Null); }
    [Test] public void FiniteStepCannotHitBeforeVineArrives()
    { PlayerBody(Vector3.forward*4);Assert.That(Cast(Vector3.zero,1),Is.Null); }
    [Test] public void OtherPhysicsSceneIsNotQueried()
    {
        var obj=new GameObject("Foreign wall");obj.AddComponent<BoxCollider>();obj.transform.position=Vector3.forward*2;
        try {Assert.That(Cast(Vector3.zero),Is.Null);} finally {UnityEngine.Object.DestroyImmediate(obj);}
    }
    [TestCase("Tracking")] [TestCase("Locked")] [TestCase("Striking")] [TestCase("Recovering")]
    public void TelegraphAndMissHaveIndependentNetworkPhases(string phase)
    {Assert.That(Enum.IsDefined(typeof(EnemyFlytrapPhase),phase),Is.True);}
    [Test] public void ExistingAnimationPhaseNumbersRemainStable()
    {Assert.That((int)EnemyFlytrapPhase.Pulling,Is.EqualTo(1));Assert.That((int)EnemyFlytrapPhase.Feeding,Is.EqualTo(2));Assert.That((int)EnemyFlytrapPhase.Dead,Is.EqualTo(4));}
}
