using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class ActiveAbilityGizmoTests
{
    private Scene scene;
    private Type Builder => typeof(ActiveAbilityVisualPreview).Assembly.GetType("ActiveAbilityGizmoPreviewBuilder");
    [SetUp] public void SetUp() => scene = EditorSceneManager.NewPreviewScene();
    [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);
    private PlayerActiveAbilityBase Create(string key)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AbilityRuntime/Active/"+key+".prefab");
        Assert.That(asset,Is.Not.Null);
        var instance=UnityEngine.Object.Instantiate(asset);
        SceneManager.MoveGameObjectToScene(instance,scene);
        return instance.GetComponent<PlayerActiveAbilityBase>();
    }
    private object Build(PlayerActiveAbilityBase skill)
    {
        Assert.That(Builder,Is.Not.Null,"需有唯讀的技能 Gizmo 預覽，且未 Spawn 的 Prefab 也能讀取。");
        return Builder.GetMethod("Build",new[]{typeof(PlayerActiveAbilityBase)}).Invoke(null,new object[]{skill});
    }
    private static T Field<T>(object model,string name) => (T)model.GetType().GetField(name).GetValue(model);
    private static void Set(Component target,string name,float value)
    {
        var serialized=new SerializedObject(target);
        serialized.FindProperty(name).floatValue=value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    [TestCase("Grenade","爆破榴彈")]
    [TestCase("PiercingCannon","貫穿砲")]
    [TestCase("Shockwave","衝擊扇波")]
    [TestCase("Shield","快速護盾")]
    [TestCase("HealingPack","治療包")]
    [TestCase("Ricochet","彈射彈")]
    [TestCase("Experience","經驗增加")]
    [TestCase("Blink","閃現")]
    [TestCase("BulletTime","子彈時間")]
    [TestCase("PrecisionLock","精準鎖敵")]
    public void UnspawnedPrefab_HasNamedSafePreview(string key,string label)
    {
        object model=null;
        Assert.DoesNotThrow(()=>model=Build(Create(key)));
        Assert.That(Field<string>(model,"Title"),Does.Contain(label));
        Assert.That(Field<string>(model,"Description"),Does.Contain("預覽"));
        Assert.That(Field<Color>(model,"Color").a,Is.GreaterThan(0f));
    }
    [Test] public void Grenade_UsesEditedValuesInWorldMetersAndLabels()
    {
        var skill=Create("Grenade");
        skill.transform.position=new Vector3(8,2,3);
        skill.transform.localScale=Vector3.one*5;
        Set(skill,"explosionRadius",13f);Set(skill,"originHeight",2.5f);
        var model=Build(skill);
        Assert.That(Field<float>(model,"EffectRadius"),Is.EqualTo(13f));
        Assert.That(Field<Vector3>(model,"Origin"),Is.EqualTo(new Vector3(8,4.5f,3)));
        Assert.That(Field<string>(model,"Description"),Does.Contain("13"));
    }
    [Test] public void HealingPickup_NeverSmallerThanCollisionRadius()
    {
        var skill=Create("HealingPack");
        Set(skill,"projectileRadius",.8f);Set(skill,"pickupRadius",.2f);
        Assert.That(Field<float>(Build(skill),"EffectRadius"),Is.EqualTo(.8f));
    }
    [TestCase("Shield")][TestCase("Experience")]
    public void SelfBuff_DoesNotInventRadius(string key)
    {
        var model=Build(Create(key));
        Assert.That(Field<float>(model,"EffectRadius"),Is.Zero);
        Assert.That(Field<string>(model,"Description"),Does.Contain("自身").And.Contain("無空間範圍"));
        Assert.That(Field<IList>(model,"Spheres").Count,Is.Zero);
    }
    [Test] public void PrecisionRim_RespectsSphericalDistanceAndHalfAngle()
    {
        Assert.That(Builder,Is.Not.Null);
        var points=(Vector3[])Builder.GetMethod("SphericalRim").Invoke(null,new object[]{Vector3.zero,Vector3.forward,12f,8f,32});
        foreach(var p in points)
        {
            Assert.That(p.magnitude,Is.EqualTo(12f).Within(.001f));
            Assert.That(Vector3.Angle(Vector3.forward,p),Is.EqualTo(8f).Within(.001f));
        }
    }
    [Test] public void ShockwaveBoundary_IsSphereIntersectedWithHorizontalWedge()
    {
        var skill=Create("Shockwave");Set(skill,"radius",5f);Set(skill,"halfAngleDegrees",60f);
        var model=Build(skill);var origin=Field<Vector3>(model,"Origin");
        var surfaces=(IEnumerable)model.GetType().GetField("RangePaths").GetValue(model);
        foreach(Vector3[] path in surfaces)
            foreach(var p in path)
            {
                var delta=p-origin;
                Assert.That(delta.magnitude,Is.LessThanOrEqualTo(5.001f));
                var flat=Vector3.ProjectOnPlane(delta,Vector3.up);
                if(flat.sqrMagnitude>.0001f) Assert.That(Vector3.Angle(Vector3.forward,flat),Is.LessThanOrEqualTo(60.01f));
            }
    }
    [TestCase(false,false,true,true,false)]
    [TestCase(true,true,false,true,false)]
    [TestCase(true,true,true,true,true)]
    [TestCase(true,false,false,true,true)]
    [TestCase(true,false,true,false,false)]
    public void MasterSelectionAndGlobalSwitches(bool enabled,bool selectedOnly,bool selected,bool global,bool expected)
    {
        var type=typeof(Player).Assembly.GetType("ActiveAbilityGizmoSettings");
        Assert.That(type,Is.Not.Null);
        var settings=Activator.CreateInstance(type);
        type.GetField("enabled").SetValue(settings,enabled);
        type.GetField("selectedOnly").SetValue(settings,selectedOnly);
        Assert.That((bool)type.GetMethod("ShouldDraw").Invoke(settings,new object[]{selected,global}),Is.EqualTo(expected));
    }
    [Test] public void DifferentSkillFamilies_HaveDistinctDefaultColors()
    {
        var keys=new[]{"Grenade","PiercingCannon","Shockwave","Shield","HealingPack","Ricochet","Experience","Blink","BulletTime","PrecisionLock"};
        var colors=keys.Select(k=>Field<Color>(Build(Create(k)),"Color")).ToArray();
        Assert.That(colors.Distinct().Count(),Is.EqualTo(10));
    }
    private GameObject Box(string name,Vector3 position,Vector3 size)
    {
        var obj=new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj,scene);
        obj.transform.position=position;obj.AddComponent<BoxCollider>().size=size;
        return obj;
    }
    private ActiveAbilityGizmoPreview Trace(PlayerAbilityProjectileKind kind,Vector3 origin,Vector3 velocity,
        float seconds,float gravity=0f,Transform owner=null)
    {
        var model=new ActiveAbilityGizmoPreview { Origin=origin,EffectRadius=kind==PlayerAbilityProjectileKind.Grenade?4f:0f };
        Physics.SyncTransforms();
        Builder.GetMethod("Trace",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{
            model,scene.GetPhysicsScene(),owner,null,kind,velocity,gravity,.05f,~0,seconds,0 });
        return model;
    }
    [Test] public void Ricochet_ReflectionPreservesRemainingStepDistance()
    {
        Box("Near wall",new Vector3(0,0,.2f),new Vector3(10,10,.1f));
        var model=Trace(PlayerAbilityProjectileKind.Ricochet,Vector3.zero,Vector3.forward*60f,.05f);
        float travelled=0f;
        foreach(var path in model.Paths)
            for(int n=1;n<path.Length;n++)travelled+=Vector3.Distance(path[n-1],path[n]);
        Assert.That(travelled,Is.EqualTo(3f).Within(.01f),"反彈不應丟掉同一步剩餘距離。");
        Assert.That(model.Labels.Any(l=>l.Text.Contains("第 1 次反彈")),Is.True);
    }
    [Test] public void Ricochet_FourthWorldContactStopsPreview()
    {
        Box("Front wall",new Vector3(0,0,.5f),new Vector3(10,10,.1f));
        Box("Back wall",new Vector3(0,0,-.5f),new Vector3(10,10,.1f));
        var model=Trace(PlayerAbilityProjectileKind.Ricochet,Vector3.zero,Vector3.forward*60f,.2f);
        Assert.That(model.Labels.Count(l=>l.Text.Contains("次反彈")),Is.EqualTo(3));
        Assert.That(model.Labels.Any(l=>l.Text.Contains("第 4 次碰撞")),Is.True);
        Assert.That(model.Labels.Any(l=>l.Text.Contains("時間上限")),Is.False);
    }
    [Test] public void Grenade_CanHitOwnerAfterLeavingSpawnOverlap()
    {
        var owner=Box("Owner",Vector3.zero,Vector3.one);
        var model=Trace(PlayerAbilityProjectileKind.Grenade,Vector3.zero,Vector3.up*10f,2f,20f,owner.transform);
        Assert.That(model.Labels.Any(l=>l.Text.Contains("預估接觸點")),Is.True);
        var last=model.Paths.Last().Last();
        Assert.That(last.y,Is.EqualTo(.55f).Within(.01f));
    }
    [Test] public void UnspawnedProjectile_DoesNotReadNetworkStateOrInventRange()
    {
        var obj=new GameObject("Unspawned projectile");
        SceneManager.MoveGameObjectToScene(obj,scene);
        var projectile=obj.AddComponent<PlayerAbilityProjectile>();
        ActiveAbilityGizmoPreview model=null;
        Assert.DoesNotThrow(()=>model=ActiveAbilityGizmoPreviewBuilder.BuildProjectile(projectile));
        Assert.That(model.EffectRadius,Is.Zero);
        Assert.That(model.Spheres.Count,Is.Zero);
        Assert.That(model.Description,Does.Contain("未 Spawn"));
    }

    [TestCase(true)][TestCase(false)]
    public void PreviewScene_NeverFallsBackToDefaultPhysicsScene(bool collisionPreview)
    {
        var skill=Create("Grenade");
        skill.RangeGizmos.previewCollisions=collisionPreview;
        var model=Build(skill);
        Assert.That(Field<string>(model,"Description"),Does.Contain("無場景碰撞的理論路徑"));
        Assert.That(Field<string>(model,"Description"),Does.Not.Contain("目前場景碰撞預估"));
    }

}
