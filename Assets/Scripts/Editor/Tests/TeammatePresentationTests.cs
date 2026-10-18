using NUnit.Framework;
using UnityEngine;
[Category("PurgersRegression")]
public sealed class TeammatePresentationTests
{
    private static object Call(string name, params object[] args)
    {
        var rules=typeof(Player).Assembly.GetType("TeammateDisplayRules");
        Assert.That(rules,Is.Not.Null,"隊友呈現規則尚未實作");
        return rules.GetMethod(name).Invoke(null,args);
    }
    [TestCase(0,0)] [TestCase(1,1)] [TestCase(3,2)] [TestCase(7,3)] [TestCase(5,1)]
    public void SlotUsesFirstFreeWithoutRenumberingOthers(int occupied,int expected) => Assert.That(Call("FirstFreeSlot",occupied),Is.EqualTo(expected));
    [TestCase(0f,0)] [TestCase(1f,1)] [TestCase(20f,1)] [TestCase(21f,2)]
    public void CellsFollowCurrentSnapshot(float hp,int expected) => Assert.That(Call("CellCount",hp,20f),Is.EqualTo(expected));
    [TestCase(25f,true)] [TestCase(26f,false)] [TestCase(0f,false)]
    public void LowHealthUsesExactHp(float hp,bool low) => Assert.That(Call("IsLowHealth",hp,100f,.25f),Is.EqualTo(low));
    [Test] public void TwelveSlotsHaveDistinctIdentityColors()
    {
        var colors=new System.Collections.Generic.HashSet<Color>();
        for(int i=0;i<12;i++)colors.Add((Color)Call("IdentityColor",i));
        Assert.That(colors.Count,Is.EqualTo(12));
    }
    [TestCase(0f,0f,-10f)] [TestCase(10f,0f,-1f)] [TestCase(-10f,2f,1f)]
    public void BehindDirectionIsFiniteAndNotMirrored(float x,float y,float z)
    {
        var d=(Vector2)Call("EdgeDirection",new Vector3(x,y,z),60f,16f/9f);
        Assert.That(float.IsNaN(d.x)||float.IsNaN(d.y),Is.False);
        Assert.That(d.sqrMagnitude,Is.GreaterThan(.9f));
        if(x!=0)Assert.That(Mathf.Sign(d.x),Is.EqualTo(Mathf.Sign(x)));
        if(x==0&&z<0)Assert.That(d.y,Is.LessThan(0));
    }
    [Test] public void ClampRespectsInsetAndViewportOrigin()
    {
        var p=(Vector2)Call("EdgePoint",new Rect(100,50,800,600),new Vector2(1,.3f),30f);
        Assert.That(p.x,Is.EqualTo(870).Within(.01f));
        Assert.That(p.y,Is.InRange(80,620));
    }
    [Test] public void UnspawnedRosterQueryIsSafe()
    {
        var go=new GameObject("unspawned roster test");
        try{
            var logic=go.AddComponent<GameLogic>();
            var method=typeof(GameLogic).GetMethod("TryGetTeammateIdentity");
            Assert.That(method,Is.Not.Null,"尚無具生命週期防護的隊友名冊");
            Assert.That(method.Invoke(logic,new object[]{Fusion.PlayerRef.FromIndex(0),null}),Is.False);
        }finally{Object.DestroyImmediate(go);}
    }

    [Test] public void LowHealthMeshTurnsRedWhileAdjacentShieldStaysGold()
    {
        var settings=ScriptableObject.CreateInstance<TeammateDisplaySettings>();
        var go=new GameObject("teammate blood bar",typeof(RectTransform),typeof(CanvasRenderer),typeof(LocalHealthSegmentView));
        var mesh=new Mesh();
        try{
            var view=go.GetComponent<LocalHealthSegmentView>();
            ((RectTransform)go.transform).sizeDelta=new Vector2(160,12);
            view.ConfigureTeammatePresentation(settings);view.ApplyHealthSnapshot(20,100);view.SetShieldHealth(40);
            Assert.That(view.VisibleHealthCells,Is.EqualTo(1));Assert.That(view.VisibleShieldCells,Is.EqualTo(2));Assert.That(view.ShieldStartCell,Is.EqualTo(1));
            using(var vh=new UnityEngine.UI.VertexHelper()){
                typeof(LocalHealthSegmentView).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,new[]{typeof(UnityEngine.UI.VertexHelper)},null).Invoke(view,new object[]{vh});
                vh.FillMesh(mesh);
            }
            Assert.That(System.Array.Exists(mesh.colors32,c=>c.r==((Color32)settings.lowHealthColor).r && c.g==((Color32)settings.lowHealthColor).g),Is.True);
            Assert.That(System.Array.Exists(mesh.colors32,c=>c.r==((Color32)settings.shieldColor).r && c.g==((Color32)settings.shieldColor).g),Is.True);
            view.ResetPresentation();Assert.That(view.VisibleHealthCells+view.VisibleShieldCells,Is.Zero);
        }finally{Object.DestroyImmediate(go);Object.DestroyImmediate(settings);Object.DestroyImmediate(mesh);}
    }
    [Test] public void PreviewAssetsHaveSettingsFontAndNoNetworkBehaviour()
    {
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TeammateDisplaySetup.PrefabPath);
        Assert.That(prefab,Is.Not.Null);Assert.That(prefab.GetComponent<LocalTeammateHUD>().Settings.font,Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Fusion.NetworkBehaviour>(true),Is.Empty);
        var shader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Teammates/TeammateOutline.shader");
        Assert.That(shader.isSupported,Is.True);Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader),Is.False);
    }
}
