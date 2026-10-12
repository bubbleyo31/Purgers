using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class ShockwaveVisualTests
{
    private Scene scene;
    private GameObject root;
    private PlayerShockwaveVisual visual;
    [SetUp] public void Setup()
    {
        scene=EditorSceneManager.NewPreviewScene();root=new GameObject("Wave test");
        SceneManager.MoveGameObjectToScene(root,scene);visual=root.AddComponent<PlayerShockwaveVisual>();
    }
    [TearDown] public void Teardown() => EditorSceneManager.ClosePreviewScene(scene);

    [TestCase(-35f,70f)] [TestCase(40f,120f)]
    public void AimedTravel_DoesNotDiscardVerticalDirection(float pitch,float yaw)
    {
        Vector3 direction=Quaternion.Euler(pitch,yaw,0f)*Vector3.forward;
        visual.Present(Vector3.zero,direction,2,4,60,1,20,3);
        var before=root.transform.position;
        visual.Present(Vector3.zero,direction,7,4,60,1,20,3);
        Assert.That(Vector3.Distance(root.transform.position-before,direction*5f),Is.LessThan(.001f),
            "波的位移必須保留前搖結束時瞄準方向的 Pitch。");
    }

    [TestCase(-35f,70f)] [TestCase(40f,120f)] [TestCase(-90f,130f)] [TestCase(90f,-80f)]
    public void TiltedMesh_UsesSameFrameAsDamageIncludingVerticalYaw(float pitch,float yaw)
    {
        var rotation=Quaternion.Euler(pitch,yaw,0);
        var origin=new Vector3(11,25,7);
        visual.Present(origin,rotation,5,4,60,1,20,3);
        Assert.That(Quaternion.Angle(root.transform.rotation,rotation),Is.LessThan(.05f));
        foreach(var p in visual.GeneratedMesh.vertices)
            Assert.That(ShockwavePropagationRules.ContainsSweptBand(root.transform.TransformPoint(p)-origin,
                rotation,5,5,4,60,1,20,3),Is.True,"網格邊界須包含在同角度的解析傷害範圍");
    }
    [Test] public void Translation_DoesNotResizeTheWave()
    {
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,20,3);
        var points=visual.GeneratedMesh.vertices;var bounds=visual.GeneratedMesh.bounds;
        var position=root.transform.position;
        visual.Present(Vector3.zero,Vector3.forward,10,4,60,1,20,3);
        Assert.That(visual.GeneratedMesh.vertices,Is.EqualTo(points));
        Assert.That(visual.GeneratedMesh.bounds,Is.EqualTo(bounds));
        Assert.That(root.transform.position-position,Is.EqualTo(Vector3.forward*5));
    }
    [Test] public void UpperAndLowerEdges_HaveIdenticalHorizontalWidth()
    {
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,20,3);
        float upper=0,lower=0;
        foreach(var p in visual.GeneratedMesh.vertices)
        {
            if(Mathf.Abs(p.y-3)<.001f)upper=Mathf.Max(upper,Mathf.Abs(p.x));
            if(Mathf.Abs(p.y)<.001f)lower=Mathf.Max(lower,Mathf.Abs(p.x));
            Assert.That(p.y,Is.InRange(0f,3f));
        }
        Assert.That(upper,Is.EqualTo(4*Mathf.Sin(60*Mathf.Deg2Rad)).Within(.001f));
        Assert.That(lower,Is.EqualTo(upper).Within(.001f));
    }
    [Test] public void TravelLimit_DoesNotChangeRadiusHeightOrThickness()
    {
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,10,3);
        var before=visual.GeneratedMesh.vertices;
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,100,3);
        Assert.That(visual.GeneratedMesh.vertices,Is.EqualTo(before));
        foreach(var p in before)
            Assert.That(new Vector2(p.x,p.z).magnitude,Is.InRange(2.999f,4.001f));
    }
    [Test] public void AirborneOrigin_IsPreservedWithoutGroundProjection()
    {
        root.transform.localScale=Vector3.one*8;
        visual.Present(new Vector3(4,25,9),Vector3.right,5,4,60,1,10,3);
        Assert.That(root.transform.localScale,Is.EqualTo(Vector3.one));
        Assert.That(root.transform.position.y,Is.EqualTo(25f));
        Assert.That(Vector3.Distance(root.transform.forward,Vector3.right),Is.LessThan(.001f));
        foreach(var p in visual.GeneratedMesh.vertices)
            Assert.That(root.transform.TransformPoint(p).y,Is.InRange(25f,28f));
    }
    [Test] public void RadiusChangesSizeButNotTranslationOrHeight()
    {
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,10,3);
        float width=visual.GeneratedMesh.bounds.size.x;
        visual.Present(Vector3.zero,Vector3.forward,5,8,60,1,10,3);
        Assert.That(visual.GeneratedMesh.bounds.size.x,Is.EqualTo(width*2).Within(.001f));
        Assert.That(visual.GeneratedMesh.bounds.size.y,Is.EqualTo(3f));
        var localMin=root.transform.position.z+visual.GeneratedMesh.bounds.min.z;
        Assert.That(localMin,Is.EqualTo(5f).Within(.001f));
    }
    [Test] public void EndDistance_KeepsFullSizeThenHides()
    {
        visual.Present(Vector3.zero,Vector3.forward,10,4,60,1,10,3);
        Assert.That(root.GetComponent<MeshRenderer>().enabled,Is.True);
        visual.Present(Vector3.zero,Vector3.forward,10.01f,4,60,1,10,3);
        Assert.That(root.GetComponent<MeshRenderer>().enabled,Is.False);
    }
    [Test] public void VisibleVertices_AreInsideAuthorityBandAtSameSnapshot()
    {
        visual.Present(new Vector3(7,11,3),Vector3.right,5,4,60,1,10,3);
        foreach(var p in visual.GeneratedMesh.vertices)
        {
            var delta=root.transform.TransformPoint(p)-new Vector3(7,11,3);
            Assert.That(ShockwavePropagationRules.ContainsSweptBand(delta,Vector3.right,5,5,4,60,1,10,3),Is.True,"可見邊界 "+delta);
        }
    }
    [Test] public void DefaultMesh_HasClosedCapsAndStaysWithinBudget()
    {
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,10,3);
        var m=visual.GeneratedMesh;
        Assert.That(m.vertexCount,Is.EqualTo(2746));Assert.That(m.triangles.Length/3,Is.EqualTo(4896));
        int up=0,down=0;
        foreach(var n in m.normals){if(n==Vector3.up)up++;if(n==Vector3.down)down++;}
        Assert.That(up,Is.GreaterThan(0));Assert.That(down,Is.EqualTo(up));
        foreach(var index in m.triangles)Assert.That(index,Is.InRange(0,m.vertexCount-1));
        Assert.That(PlayerAbilityLocalVfxSlot.IsVisualOnly(root),Is.True);
        Assert.That(root.GetComponentsInChildren<Renderer>().Length,Is.EqualTo(1));
    }
    [Test] public void ClosedWallTriangles_HaveOutwardWindingAndNoDegenerateFaces()
    {
        visual.Present(Vector3.zero,Vector3.forward,5,4,60,1,10,3);
        var points=visual.GeneratedMesh.vertices;var indices=visual.GeneratedMesh.triangles;
        var normals=visual.GeneratedMesh.normals;
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            var normal=Vector3.Cross(points[b]-points[a],points[c]-points[a]);
            Assert.That(normal.sqrMagnitude,Is.GreaterThan(0.000000001f));
            Assert.That(Vector3.Dot(normal,normals[a]+normals[b]+normals[c]),Is.GreaterThan(0f));
        }
    }

    [Test] public void ResourcePrefab_HasWorkingShaderTextureAndNoMissingScripts()
    {
        var prefab=Resources.Load<GameObject>("Presentation/Shockwave/ThickWave");
        Assert.That(prefab,Is.Not.Null);Assert.That(prefab.GetComponent<PlayerShockwaveVisual>(),Is.Not.Null);
        Assert.That(PlayerAbilityLocalVfxSlot.IsVisualOnly(prefab),Is.True);
        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab),Is.Zero);
        var material=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Assert.That(material,Is.Not.Null);Assert.That(ShaderUtil.ShaderHasError(material.shader),Is.False);
        Assert.That(material.GetTexture("_FlowTex"),Is.Not.Null);
    }
}
