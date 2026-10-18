using System;
using System.Linq;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>只建立缺少的獨立原型資產；不覆寫現有資產、不放置或儲存 Scene。</summary>
public static class EnemyReflectorSetup
{
    public const string PrefabPath = "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_Reflector.prefab";
    public const string ProjectilePath = "Assets/Prefabs/Enemy/Projectiles/EnemyProjectile_Reflector.prefab";
    public const string DefinitionPath = "Assets/_Project_Assets/Data/Enemy/ED_Ranged_Reflector.asset";
    private const string MaterialPath = "Assets/_Project_Assets/Data/Enemy/ReflectorPrototype.mat";
    [MenuItem("Tools/Purgers/Enemy/Create Missing Reflector Prototype")]
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到既有 URP Lit Shader");
            material = new Material(shader); material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        CreateProjectile(material);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project_Assets/Data/Enemy/ED_Ranged_A.asset"));
            Set(definition,"enemyId","ranged_reflector"); Set(definition,"displayName","反彈護盾遠程敵人");
            Set(definition,"designDescription","Ground 地面單位，禁止 AI 巡邏與追擊位移；沿用遠程 A 單發攻擊。全身護盾；反彈自己的子彈可開啟 2 秒背部破綻。基礎 3D 原型。");
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_A.prefab");
        var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(source));
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name="Enemy_Ranged_Reflector"; root.transform.position=Vector3.zero; root.transform.rotation=Quaternion.identity;
            Set(root.GetComponent<EnemyActor>(),"definition",definition);
            // 原型只保留既有基礎 Hitbox 身體；移除裝飾渲染與動畫，不修改 Ranged A。
            foreach (var driver in root.GetComponentsInChildren<EnemyPresentationAnimatorDriver>(true)) Object.DestroyImmediate(driver);
            foreach (var animator in root.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponent<Hitbox>() != null) { renderer.enabled=true; renderer.sharedMaterial=material; }
                else { var filter=renderer.GetComponent<MeshFilter>(); Object.DestroyImmediate(renderer); if(filter!=null) Object.DestroyImmediate(filter); }
            }
            ConfigureGround(root, definition);
            Physics.SyncTransforms();
            var colliders = root.GetComponentsInChildren<Collider>(true);
            Bounds bounds = colliders[0].bounds; foreach(var collider in colliders) bounds.Encapsulate(collider.bounds);
            var hitRoot=root.GetComponent<HitboxRoot>();hitRoot.Offset=root.transform.InverseTransformPoint(bounds.center);hitRoot.BroadRadius=bounds.extents.magnitude+.2f;
            var shield = root.AddComponent<EnemyReflectorShield>();
            var front=Primitive(root.transform,"Shield_Front",PrimitiveType.Cube,bounds.center+Vector3.forward*(bounds.extents.z+.2f),new Vector3(bounds.size.x+.5f,bounds.size.y+.5f,.15f),material);
            var rear=Primitive(root.transform,"Shield_Rear",PrimitiveType.Cube,bounds.center-Vector3.forward*(bounds.extents.z+.2f),new Vector3(bounds.size.x+.5f,bounds.size.y+.5f,.15f),material);
            Set(shield,"frontShield",front);Set(shield,"rearShield",rear);
            AddRemainingShields(root, bounds, material);
            Color blue=new Color(.1f,.6f,1f,1f); var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",blue); front.SetPropertyBlock(block);rear.SetPropertyBlock(block);
            var attack=root.GetComponent<EnemyRangedProjectileAttack>();
            Set(attack,"waitForPreviousProjectile",true);
            Set(attack,"maximumStartDistance",75f);Set(attack,"projectileDamage",25f);Set(attack,"projectileSpeed",18f);Set(attack,"projectileLifetimeSeconds",8f);
            var projectile=AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath).GetComponent<NetworkObject>();
            typeof(EnemyRangedProjectileAttack).GetField("projectilePrefab",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(attack,NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID(ProjectilePath)));
            Set(root.GetComponent<EnemyPerceptionController>(),"sightDistance",85f);Set(root.GetComponent<EnemyPerceptionController>(),"lockedTargetRetentionRadius",90f);
            Set(root.GetComponent<EnemyPerceptionController>(),"debugPerception",false);
            // 只让身體承接受擊閃白，護盾色由同步破綻控制。
            var flash=root.GetComponent<EnemyDamageFlash>();
            if(flash!=null) {
                var data=new SerializedObject(flash);var p=data.FindProperty("targetRenderers");
                if(p!=null && p.isArray){var renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.GetComponent<Hitbox>()!=null).ToArray();p.arraySize=renderers.Length;for(int i=0;i<renderers.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=renderers[i];data.ApplyModifiedPropertiesWithoutUndo();}
            }
            if (PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null) throw new InvalidOperationException("敵人 Prefab 儲存失敗");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssetIfDirty(definition);
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("[Reflector] 獨立基礎 3D 資產已建立；請手動放置與設定 Encounter。");
    }
    private static void CreateProjectile(Material material)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath)!=null) return;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Projectiles/EnemyProjectile_RangedA.prefab");
        var root=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(source));
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            root.name="EnemyProjectile_Reflector";
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
            foreach(var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(filter);
            var visual=Primitive(root.transform,"ProjectileSphere",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.525f,material);
            var projectile=root.GetComponent<EnemyProjectile>();Set(projectile,"reflectable",true);Set(projectile,"projectileRenderer",visual);Set(projectile,"flyingVisualRoot",visual.gameObject);
            Set(projectile,"collisionRadius",.2625f);
            Set(projectile,"reflectionCapturePadding",.65f);Set(projectile,"destroyableByPlayerShots",true);Set(projectile,"matchVisualToCollisionRadius",true);
            projectile.SyncCollisionVisual();
            PrefabUtility.SaveAsPrefabAsset(root,ProjectilePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    // 明確呼叫才遷移既有原型；不會自動套用或修改 Scene。
    public static void UpgradeGroundPrototype()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureGround(root,definition);
            Physics.SyncTransforms();
            var body=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.GetComponent<Hitbox>()!=null).ToArray();
            Bounds bounds=body[0].bounds;foreach(var r in body)bounds.Encapsulate(r.bounds);
            AddRemainingShields(root,bounds,AssetDatabase.LoadAssetAtPath<Material>(MaterialPath));
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var projectile=PrefabUtility.LoadPrefabContents(ProjectilePath);
        try
        {
            var sphere=projectile.transform.Find("ProjectileSphere");
            // 只升級舊版尺寸一次，不累乘、不覆寫後續人工調整。
            if (sphere!=null && Mathf.Abs(sphere.localScale.x-.35f)<.0001f)
            {
                sphere.localScale*=1.5f;
                var data=new SerializedObject(projectile.GetComponent<EnemyProjectile>());
                data.FindProperty("collisionRadius").floatValue*=1.5f;data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(projectile,ProjectilePath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(projectile); }
        AssetDatabase.SaveAssetIfDirty(definition);
        NetworkProjectConfigUtilities.RebuildPrefabTable();
    }
    private static void ConfigureGround(GameObject root,EnemyDefinition definition)
    {
        Set(definition,"locomotionKind",(int)EnemyLocomotionKind.Ground);
        Set(definition,"chaseKind",(int)EnemyChaseKind.ChaseA);
        Set(definition,"designDescription","Ground 地面站定單位，不巡邏或追擊；六面護盾，反彈自己的子彈觸發暈眩並開啟背盾。基礎 3D 原型。");
        Set(root.GetComponent<EnemyMovementOwnership>(),"allowAutonomousMovement",false);
        var flyingPatrol=root.GetComponent<EnemyFlyingPatrolNavigator>();if(flyingPatrol!=null)Object.DestroyImmediate(flyingPatrol);
        var flyingChase=root.GetComponent<EnemyFlyingChaseMotor>();if(flyingChase!=null)Object.DestroyImmediate(flyingChase);
        var navigator=root.GetComponent<EnemyGroundPatrolNavigator>();if(navigator==null)navigator=root.AddComponent<EnemyGroundPatrolNavigator>();
        var motor=root.GetComponent<EnemyGroundChaseMotor>();if(motor==null)motor=root.AddComponent<EnemyGroundChaseMotor>();
        Set(root.GetComponent<EnemyIdlePatrolBrain>(),"navigator",navigator);
        Set(root.GetComponent<EnemyChaseBrain>(),"chaseMotor",motor);
        Physics.SyncTransforms();
        var body=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.GetComponent<Hitbox>()!=null).ToArray();
        Bounds bounds=body[0].bounds;foreach(var r in body)bounds.Encapsulate(r.bounds);
        float shift=bounds.min.y-.275f;
        foreach(Transform child in root.transform)child.localPosition-=Vector3.up*shift;
        bounds.center-=Vector3.up*shift;
        root.GetComponent<HitboxRoot>().Offset=root.transform.InverseTransformPoint(bounds.center);
        root.GetComponent<HitboxRoot>().BroadRadius=bounds.extents.magnitude+.2f;
        var muzzle=(Transform)new SerializedObject(root.GetComponent<EnemyRangedProjectileAttack>()).FindProperty("muzzle").objectReferenceValue;
        if(muzzle!=null)muzzle.position=bounds.center+Vector3.forward*(bounds.extents.z+.4f);
        var eye=(Transform)new SerializedObject(root.GetComponent<EnemyPerceptionController>()).FindProperty("eyePoint").objectReferenceValue;
        if(eye!=null)eye.position=bounds.center+Vector3.up*.6f;
        foreach(var component in new Component[]{navigator,motor})
        {Set(component,"bodyRadius",Mathf.Max(bounds.extents.x,bounds.extents.z));Set(component,"bodyHeight",bounds.max.y);Set(component,"obstacleMask",1);}
    }
    private static void AddRemainingShields(GameObject root,Bounds bounds,Material material)
    {
        var names=new[]{"Shield_Left","Shield_Right","Shield_Top","Shield_Bottom"};
        var directions=new[]{Vector3.left,Vector3.right,Vector3.up,Vector3.down};
        var data=new SerializedObject(root.GetComponent<EnemyReflectorShield>());var array=data.FindProperty("additionalShields");array.arraySize=4;
        for(int i=0;i<4;i++)
        {
            var existing=root.transform.Find(names[i]);
            var renderer=existing!=null?existing.GetComponent<Renderer>():Primitive(root.transform,names[i],PrimitiveType.Cube,
                bounds.center+directions[i]*((i<2?bounds.extents.x:bounds.extents.y)+.2f),
                i<2?new Vector3(.15f,bounds.size.y+.5f,bounds.size.z+.5f):new Vector3(bounds.size.x+.5f,.15f,bounds.size.z+.5f),material);
            array.GetArrayElementAtIndex(i).objectReferenceValue=renderer;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static Renderer Primitive(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
    {var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;Object.DestroyImmediate(go.GetComponent<Collider>());var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;return renderer;}
    private static void Set(Object obj,string name,object value)
    {
        var data=new SerializedObject(obj);var p=data.FindProperty(name);if(p==null)throw new InvalidOperationException(obj.GetType().Name+" 缺少 "+name);
        if(value is string s)p.stringValue=s;else if(value is float f)p.floatValue=f;else if(value is int n)p.intValue=n;else if(value is bool b)p.boolValue=b;else if(value is Object o)p.objectReferenceValue=o;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
