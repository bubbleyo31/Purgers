using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class EnemyHealthBarVisibilityTests
{
    private Scene scene;
    private GameObject owner, cameraRoot, wall;
    private Camera camera;
    private EnemyHealthBarVisibility query;
    private readonly Bounds body = new Bounds(Vector3.zero, new Vector3(2, 4, 2));
    [SetUp] public void Setup()
    {
        scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        owner = Create("body"); owner.transform.position = new Vector3(0, 0, 10);
        cameraRoot = Create("camera"); camera = cameraRoot.AddComponent<Camera>();
        camera.enabled = false; camera.pixelRect = new Rect(100, 50, 800, 600); camera.aspect = 4f / 3f;
        wall = Create("wall"); wall.transform.position = new Vector3(0, 0, 5);
        wall.AddComponent<BoxCollider>().size = new Vector3(20, 20, 1); wall.SetActive(false);
        query = new EnemyHealthBarVisibility();
    }
    private GameObject Create(string name)
    {
        var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go;
    }
    [TearDown] public void Cleanup()
    {
        if (!scene.IsValid()) return;
        foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }
    private bool Visible()
    {
        Physics.SyncTransforms();
        return query.TryProject(camera, owner.transform, body, out var projected) &&
            query.TryVisibleRegion(camera, owner.transform, body, projected, scene.GetPhysicsScene(), ~0, null, out _);
    }
    [Test] public void VisibleBodyPasses() => Assert.That(Visible(), Is.True);
    [Test] public void SolidWallHidesBodyImmediately() { wall.SetActive(true); Assert.That(Visible(), Is.False); }
    [Test] public void TriggerDoesNotOcclude() { wall.GetComponent<Collider>().isTrigger = true; wall.SetActive(true); Assert.That(Visible(), Is.True); }
    [Test] public void BodyColliderDoesNotOccludeItself() { owner.AddComponent<BoxCollider>().size = body.size; Assert.That(Visible(), Is.True); }
    [Test] public void WallInAnotherPhysicsSceneDoesNotOcclude()
    {
        var other = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try { SceneManager.MoveGameObjectToScene(wall, other); wall.SetActive(true); Assert.That(Visible(), Is.True); }
        finally { Object.DestroyImmediate(wall); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(other); }
    }
    [Test] public void PartialBodyExposureRemainsVisible()
    {
        wall.transform.position = new Vector3(-.9f, 0, 5); wall.GetComponent<BoxCollider>().size = new Vector3(1.8f, 20, 1);
        wall.SetActive(true); Assert.That(Visible(), Is.True);
    }
    [TestCase(0, 0, -10)] [TestCase(100, 0, 10)] [TestCase(0, 100, 10)]
    public void OffscreenBodyCannotBecomeEdgeMarker(float x, float y, float z)
    { owner.transform.position = new Vector3(x, y, z); Assert.That(Visible(), Is.False); }
    [Test] public void GiantCrossingNearPlaneRetainsVisibleBody()
    { owner.transform.position = new Vector3(0, 0, .5f); Assert.That(Visible(), Is.True); }
    [Test] public void UnspawnedTargetDoesNotReadNetworkedHealth()
    {
        var target = owner.AddComponent<EnemyHealthBarTarget>();
        Assert.DoesNotThrow(() => target.Render());
        Assert.That(target.HasSnapshot, Is.False); Assert.That(target.IsReadyFor(null), Is.False);
        Assert.That(EnemyHealthBarTarget.Active, Has.No.Member(target));
    }
    [Test] public void PartialMeshKeepsAllCapacityCellsAndExactFraction()
    {
        var settings = ScriptableObject.CreateInstance<EnemyHealthBarSettings>();
        var go = Create("graphic"); go.AddComponent<RectTransform>(); var graphic = go.AddComponent<EnemyHealthBarGraphic>();
        var mesh = new Mesh();
        try
        {
            graphic.rectTransform.sizeDelta = new Vector2(118, 9); graphic.SetSnapshot(.76f, settings);
            using (var vh = new UnityEngine.UI.VertexHelper())
            {
                typeof(EnemyHealthBarGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(UnityEngine.UI.VertexHelper) }, null).Invoke(graphic, new object[] { vh });
                vh.FillMesh(mesh);
            }
            Assert.That(mesh.vertexCount, Is.EqualTo((10 + 8) * 4));
            // 第八格填色為第 15 個 quad：七對底／填色後，再一底一填色。
            Assert.That(mesh.vertices[62].x - mesh.vertices[60].x, Is.EqualTo(6).Within(.001f));
            Assert.That(mesh.bounds.size.x, Is.EqualTo(118).Within(.001f));
            Assert.That(graphic.raycastTarget, Is.False);
        }
        finally { Object.DestroyImmediate(settings); Object.DestroyImmediate(mesh); }
    }
    [Test] public void PresentationAssetsAreStandaloneAndHaveNoNetworkObject()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(EnemyHealthBarSetup.PrefabPath);
        Assert.That(prefab, Is.Not.Null); Assert.That(prefab.GetComponent<LocalEnemyHealthBarHUD>().Settings, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Fusion.NetworkObject>(true), Is.Empty);
    }
}
