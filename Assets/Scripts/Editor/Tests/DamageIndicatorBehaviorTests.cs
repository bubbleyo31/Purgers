using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class DamageIndicatorBehaviorTests
{
    private Scene scene;
    private GameObject root;
    private Camera camera;
    private DamageIndicatorSettings settings;
    private LocalPlayerDamageIndicator hud;
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [SetUp] public void Setup()
    {
        scene=EditorSceneManager.NewPreviewScene();
        root=Create("HUD",typeof(RectTransform),typeof(Canvas));
        root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        hud=root.AddComponent<LocalPlayerDamageIndicator>();
        typeof(LocalPlayerDamageIndicator).GetMethod("Awake",Private).Invoke(hud,null);
        camera=Create("Camera").AddComponent<Camera>();camera.transform.position=Vector3.zero;
        settings=ScriptableObject.CreateInstance<DamageIndicatorSettings>();settings.arc.hold=settings.marker.hold=5;
        hud.Configure(settings,null,camera);
    }
    [TearDown] public void Cleanup(){Object.DestroyImmediate(settings);EditorSceneManager.ClosePreviewScene(scene);}
    private GameObject Create(string name,params System.Type[] types)
    {
        var go=new GameObject(name,types);SceneManager.MoveGameObjectToScene(go,scene);return go;
    }
    private GameObject Wall(Vector3 pos)
    {
        var go=Create("Wall");go.transform.position=pos;go.transform.localScale=new Vector3(5,5,.5f);go.AddComponent<BoxCollider>();Physics.SyncTransforms();return go;
    }
    private void Add(Vector3 point,float time=0)
        => typeof(LocalPlayerDamageIndicator).GetMethod("AddSnapshot",Private).Invoke(hud,new object[]{new PlayerDamageReceivedInfo{HasSourceWorldPosition=true,SourceWorldPosition=point,AppliedDamage=1},time});
    private void Render(float time)
    {
        typeof(LocalPlayerDamageIndicator).GetProperty("ProbeCountLastFrame").GetSetMethod(true).Invoke(hud,new object[]{0});
        typeof(LocalPlayerDamageIndicator).GetProperty("PhysicsQueriesLastFrame").GetSetMethod(true).Invoke(hud,new object[]{0});
        typeof(LocalPlayerDamageIndicator).GetMethod("RenderSnapshots",Private).Invoke(hud,new object[]{time,camera,scene.GetPhysicsScene(),null});
    }
    private CanvasGroup Marker(int n=0)=>root.transform.Find("Damage Source "+n).GetComponent<CanvasGroup>();
    [Test] public void MovingWallUpdatesBothDirectionsAfterDamageWithoutNewHit()
    {
        Add(Vector3.forward*10);Render(.1f);Assert.That(Marker().alpha,Is.EqualTo(.9f).Within(.001));
        var wall=Wall(Vector3.forward*5);Render(.16f);Assert.That(Marker().alpha,Is.EqualTo(.25f).Within(.001));
        wall.transform.position=Vector3.right*50;Physics.SyncTransforms();Render(.22f);
        Assert.That(Marker().alpha,Is.EqualTo(.9f).Within(.001));
    }
    [Test] public void HideModeContinuesTestingAndRestoresWithoutNewHit()
    {
        settings.occlusionMode=DamageIndicatorOcclusionMode.HideBehindWalls;
        var wall=Wall(Vector3.forward*5);Add(Vector3.forward*10);Render(.1f);Assert.That(Marker().alpha,Is.Zero);
        wall.SetActive(false);Physics.SyncTransforms();Render(.16f);Assert.That(Marker().alpha,Is.EqualTo(.9f).Within(.001));
    }
    [Test] public void CameraEnteringWallIsOccludedEvenWhenRayStartsInside()
    {
        Wall(Vector3.forward*5);var query=new DamageIndicatorOcclusionQuery();
        Assert.That(query.IsOccluded(scene.GetPhysicsScene(),Vector3.forward*5,Vector3.forward*10,~0,null,null),Is.True);
    }
    [Test] public void QueryIgnoresReceiverAndSourceButNotWallBehindReceiver()
    {
        var receiver=Wall(Vector3.zero);var source=Wall(Vector3.forward*10);var query=new DamageIndicatorOcclusionQuery();
        Assert.That(query.IsOccluded(scene.GetPhysicsScene(),Vector3.zero,Vector3.forward*10,~0,receiver.transform,source.transform),Is.False);
        Wall(Vector3.forward*5);
        Assert.That(query.IsOccluded(scene.GetPhysicsScene(),Vector3.zero,Vector3.forward*10,~0,receiver.transform,source.transform),Is.True);
    }
    [Test] public void NoMarkersAndOffscreenMarkersDoNotQueryPhysics()
    {
        Render(.1f);Assert.That(hud.PhysicsQueriesLastFrame,Is.Zero);
        Add(Vector3.back*10);Render(.2f);Assert.That(hud.PhysicsQueriesLastFrame,Is.Zero);
        Assert.That(root.transform.Find("Damage Arc 0").gameObject.activeSelf,Is.True);
    }
    [Test] public void ProbeBudgetAndRoundRobinCoverAllMarkers()
    {
        for(int i=0;i<4;i++)Add(new Vector3(i*.1f,0,10));
        Render(.1f);Assert.That(hud.ProbeCountLastFrame,Is.EqualTo(2));Assert.That(hud.PhysicsQueriesLastFrame,Is.LessThanOrEqualTo(4));
        Render(.116f);Assert.That(hud.ProbeCountLastFrame,Is.EqualTo(2));
        for(int i=0;i<4;i++)Assert.That(Marker(i).alpha,Is.EqualTo(.9f).Within(.001));
    }
    [Test] public void LifetimeExpiresWithoutLeavingPoolObjectsVisible()
    {
        Add(Vector3.forward*10);Render(10);Assert.That(hud.ActiveCount,Is.Zero);
        Assert.That(Marker().gameObject.activeSelf,Is.False);Assert.That(hud.PhysicsQueriesLastFrame,Is.Zero);
    }
    [Test] public void SettingsCanChangeWhileMarkerIsAlive()
    {
        Add(Vector3.forward*10);Wall(Vector3.forward*5);Render(.1f);
        settings.occludedOpacity=.4f;settings.marker.color=Color.blue;settings.markerSize=new Vector2(40,30);Render(.16f);
        Assert.That(Marker().alpha,Is.EqualTo(.4f).Within(.001));
        Assert.That(Marker().GetComponent<Image>().color,Is.EqualTo(Color.blue));
        settings.occlusionMode=DamageIndicatorOcclusionMode.HideBehindWalls;Render(.17f);Assert.That(Marker().alpha,Is.Zero);
    }
    [Test] public void SeparatePhysicsSceneDoesNotLeakItsWall()
    {
        var other=EditorSceneManager.NewPreviewScene();
        try{var wall=Wall(Vector3.forward*5);SceneManager.MoveGameObjectToScene(wall,other);Physics.SyncTransforms();
            Add(Vector3.forward*10);Render(.1f);Assert.That(Marker().alpha,Is.EqualTo(.9f).Within(.001));}
        finally{EditorSceneManager.ClosePreviewScene(other);}
    }
    [Test] public void CustomSpriteIsUsedAndCanReturnToProceduralShape()
    {
        var texture=new Texture2D(2,2);var sprite=Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f);
        try{settings.marker.sprite=sprite;Add(Vector3.forward*10);Render(.1f);Assert.That(Marker().GetComponent<Image>().sprite,Is.SameAs(sprite));
            settings.marker.sprite=null;Render(.2f);Assert.That(Marker().GetComponent<Image>().sprite,Is.Null);}
        finally{Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
    }
    [Test] public void FullyBlockedHealthFeedbackContractExists()
        => Assert.That(typeof(IBlockedDamageFeedbackReceiver).IsAssignableFrom(typeof(PlayerHealth)),Is.True);
    [Test] public void SnapshotStaysFixedWhenNetworkSourceMoves()
    {
        var source=Create("Network source").AddComponent<Fusion.NetworkObject>();
        Vector3 point=Vector3.forward*10;
        var info=new PlayerDamageReceivedInfo{AppliedDamage=1,HasSourceWorldPosition=true,SourceWorldPosition=point,SourceNetworkObject=source};
        typeof(LocalPlayerDamageIndicator).GetMethod("AddSnapshot",Private).Invoke(hud,new object[]{info,0f});
        Render(.1f);Vector2 first=Marker().GetComponent<RectTransform>().anchoredPosition;
        source.transform.position=Vector3.right*100;Render(.2f);
        Assert.That(Marker().GetComponent<RectTransform>().anchoredPosition,Is.EqualTo(first));
        info.SourceWorldPosition=new Vector3(1,0,10);
        typeof(LocalPlayerDamageIndicator).GetMethod("AddSnapshot",Private).Invoke(hud,new object[]{info,.21f});Render(.3f);
        Assert.That(hud.ActiveCount,Is.EqualTo(1),"同來源的新命中才更新快照，不新增重疊提示");
        Assert.That(Marker().GetComponent<RectTransform>().anchoredPosition,Is.Not.EqualTo(first));
    }
    [Test] public void ExplicitPositionOverridesSourceRootIncludingWorldOrigin()
    {
        var health=Create("Health").AddComponent<PlayerHealth>();var source=Create("Source");source.transform.position=Vector3.one*20;
        var request=new DamageRequest{SourceObject=source,HasSourceWorldPosition=true,SourceWorldPosition=Vector3.zero};
        var snapshot=typeof(PlayerHealth).GetMethod("BuildDamageSourceSnapshot",Private).Invoke(health,new object[]{request});
        Assert.That(snapshot.GetType().GetField("HasPosition").GetValue(snapshot),Is.True);
        Assert.That(snapshot.GetType().GetField("Position").GetValue(snapshot),Is.EqualTo(Vector3.zero));
    }
    [Test] public void DirectionOnlyNeverCreatesWorldMarkerOrQueries()
    {
        typeof(LocalPlayerDamageIndicator).GetMethod("AddSnapshot",Private).Invoke(hud,new object[]{new PlayerDamageReceivedInfo{AppliedDamage=1,DirectionToSource=Vector3.right},0f});
        Render(.1f);Assert.That(Marker().gameObject.activeSelf,Is.False);Assert.That(hud.PhysicsQueriesLastFrame,Is.Zero);
        Assert.That(root.transform.Find("Damage Arc 0").gameObject.activeSelf,Is.True);
    }
    [Test] public void ReducingCapacityClearsExcessAndDisabled3DStopsQueries()
    {
        for(int i=0;i<8;i++)Add(new Vector3(i*.1f,0,10),i*.001f);
        Render(.1f);Assert.That(hud.ActiveCount,Is.EqualTo(4));
        settings.maximumIndicators=1;settings.marker.enabled=false;Render(.2f);
        Assert.That(hud.ActiveCount,Is.EqualTo(1));Assert.That(hud.PhysicsQueriesLastFrame,Is.Zero);
    }
    [TestCase(0f)] [TestCase(.5f)] [TestCase(1f)]
    public void ScreenCenterMatchesCenteredAnchorsForAnyCanvasPivot(float pivot)
    {
        var nested=(RectTransform)Create("Nested HUD",typeof(RectTransform)).transform;
        nested.SetParent(root.transform,false);nested.anchorMin=Vector2.zero;nested.anchorMax=Vector2.one;
        nested.offsetMin=nested.offsetMax=Vector2.zero;nested.pivot=Vector2.one*pivot;
        typeof(LocalPlayerDamageIndicator).GetField("visualRoot",Private).SetValue(hud,nested);
        Canvas.ForceUpdateCanvases();
        Vector2 center=(Vector2)typeof(LocalPlayerDamageIndicator).GetMethod("CanvasPosition",Private).Invoke(hud,new object[]{camera.pixelRect.center});
        Assert.That(center.sqrMagnitude,Is.LessThan(.01f),"巢狀 Canvas 的 pivot 不得偏移提示的中心錨點");
    }
    [Test] public void AuthoredPrefabHasNonzeroScaleAndCompleteConfiguration()
    {
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/DamageIndicators.prefab");
        Assert.That(prefab,Is.Not.Null);Assert.That(prefab.transform.localScale,Is.EqualTo(Vector3.one));
        Assert.That(prefab.GetComponent<LocalPlayerDamageIndicator>().Settings,Is.Not.Null);
        Assert.That(prefab.GetComponent<GraphicRaycaster>(),Is.Null);
    }
}
