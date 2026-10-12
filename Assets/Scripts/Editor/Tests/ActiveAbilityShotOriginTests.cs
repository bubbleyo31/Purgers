using System.Reflection;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>低矮空間的發射起點不得跨越世界障礙；查詢仍限定目前 Runner 的 PhysicsScene。</summary>
[Category("PurgersRegression")]
public sealed class ActiveAbilityShotOriginTests
{
    private Scene scene;
    [SetUp] public void SetUp() => scene = EditorSceneManager.NewPreviewScene();
    [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);
    private GameObject Box(Vector3 position, Vector3 scale)
    {
        var go = new GameObject("Shot origin obstacle");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        return go;
    }
    private bool Resolve(Vector3 anchor, Vector3 desired, float radius, NetworkObject owner, out Vector3 origin)
    {
        var method = typeof(ActiveAbilityPhysics).GetMethod("TryResolveShotOrigin", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "發射點必須從膠囊內側檢查通往設定起點的路徑。");
        object[] args = { scene.GetPhysicsScene(), anchor, desired, radius, (LayerMask)(~0), owner, null, false };
        bool result = (bool)method.Invoke(null, args);
        origin = (Vector3)args[6];
        return result;
    }
    [TestCase(0f)] [TestCase(.1f)]
    public void ThinCeilingCannotBeCrossed(float radius)
    {
        Box(new Vector3(0, 1.1f, 0), new Vector3(4, .1f, 4));
        Assert.That(Resolve(Vector3.up * .4f, Vector3.up * 1.5f, radius, null, out var origin), Is.True);
        Assert.That(origin.y, Is.LessThan(1.05f - radius));
        Assert.That(origin.y, Is.GreaterThan(.4f));
    }
    [Test] public void DesiredOriginInsideThickWallStaysOnNearSide()
    {
        Box(Vector3.up * 1.5f, new Vector3(4, 1f, 4));
        Assert.That(Resolve(Vector3.up * .4f, Vector3.up * 1.5f, 0, null, out var origin), Is.True);
        Assert.That(origin.y, Is.LessThan(1f));
    }
    [Test] public void ClearPathPreservesConfiguredHeight()
    {
        Assert.That(Resolve(Vector3.up * .4f, Vector3.up * 1.5f, .1f, null, out var origin), Is.True);
        Assert.That(origin, Is.EqualTo(Vector3.up * 1.5f));
    }
    [Test] public void EmbeddedAnchorRejectsShot()
    {
        Box(Vector3.up * .4f, Vector3.one);
        Assert.That(Resolve(Vector3.up * .4f, Vector3.up * 1.5f, .1f, null, out _), Is.False);
    }
    [Test] public void OwnerColliderDoesNotBlockOrigin()
    {
        var owner = Box(Vector3.up * .8f, new Vector3(1, 1.6f, 1)).AddComponent<NetworkObject>();
        Assert.That(Resolve(Vector3.up * .4f, Vector3.up * 1.5f, .1f, owner, out var origin), Is.True);
        Assert.That(origin.y, Is.EqualTo(1.5f));
    }
    [Test] public void OtherPhysicsSceneCannotBlockShot()
    {
        var other = EditorSceneManager.NewPreviewScene();
        try
        {
            var obstacle = Box(Vector3.up, new Vector3(4, .2f, 4));
            SceneManager.MoveGameObjectToScene(obstacle, other);
            Physics.SyncTransforms();
            Assert.That(Resolve(Vector3.up * .4f, Vector3.up * 1.5f, .1f, null, out var origin), Is.True);
            Assert.That(origin.y, Is.EqualTo(1.5f));
        }
        finally { EditorSceneManager.ClosePreviewScene(other); }
    }
}
