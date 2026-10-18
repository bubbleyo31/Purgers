using System;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>建立獨立方塊捕食怪；只建立缺少資產，不覆寫人工調整或放置 Scene。</summary>
public static class EnemyFlytrapSetup
{
    public const string PrefabPath = "Assets/Prefabs/Enemy/Variants/Enemy_Flytrap.prefab";
    public const string DefinitionPath = "Assets/_Project_Assets/Data/Enemy/ED_Flytrap.asset";
    [MenuItem("Tools/Purgers/Enemy/Create Missing Flytrap Placeholder")]
    public static void CreateMissing()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)!=null) return;
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);
        if(definition==null)
        {
            definition=ScriptableObject.CreateInstance<EnemyDefinition>();
            Set(definition,"enemyId","flytrap");Set(definition,"displayName","捕食植物");
            Set(definition,"designDescription","定點全周捕食；安全蹲行／静止，開槍與鉤索會驚動。方塊原型，生命與距離需人工調整。");
            Set(definition,"locomotionKind",(int)EnemyLocomotionKind.Stationary);
            AssetDatabase.CreateAsset(definition,DefinitionPath);
        }
        var root=new GameObject("Enemy_Flytrap");
        try
        {
            root.layer=LayerMask.NameToLayer("Hit Mask");
            root.AddComponent<NetworkObject>();root.AddComponent<NetworkTransform>();
            var actor=root.AddComponent<EnemyActor>();Set(actor,"definition",definition);
            var health=root.GetComponent<TestDamageReceiver>();Set(health,"maxHealth",100f);
            Set(root.GetComponent<EnemyStateController>(),"allowAbilityKnockback",false);
            var death=root.AddComponent<CombatDeathHandler>();
            var lifecycle=root.AddComponent<EnemyDeathLifecycleController>();Set(lifecycle,"deathAnimationDuration",1f);Set(lifecycle,"corpseLifetime",2f);
            root.AddComponent<FocusTarget>();root.AddComponent<AttackGrappleMarkState>();
            var grappleTarget=root.AddComponent<GrappleInteractionTarget>();Set(grappleTarget,"canBeSupportPulled",false);Set(grappleTarget,"canBeTankGathered",false);
            // 扎根原型不掛 Support Pull／Tank Gather Receiver；不建立第二套免疫系統。
            root.AddComponent<EnemyMovementOwnership>();
            root.AddComponent<EnemyPerceptionController>();root.AddComponent<EnemyCombatDecisionController>();
            var attack=root.AddComponent<EnemyFlytrapAttack>();
            Set(attack,"optionId",(int)EnemyCombatOptionId.FlytrapCapture);Set(attack,"maximumStartDistance",6f);Set(attack,"requiresDirectSight",false);
            var mouth=new GameObject("MouthAnchor_PlayerFeet").transform;mouth.SetParent(root.transform,false);mouth.localPosition=Vector3.up*2.1f;
            var sense=new GameObject("SenseOrigin").transform;sense.SetParent(root.transform,false);sense.localPosition=Vector3.up*1.5f;
            var tendril=new GameObject("TendrilOrigin").transform;tendril.SetParent(root.transform,false);tendril.localPosition=Vector3.up*1.1f;
            Set(attack,"mouthAnchor",mouth);Set(attack,"senseOrigin",sense);Set(attack,"tendrilOrigin",tendril);
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.name="VisualRoot";body.layer=root.layer;body.transform.SetParent(root.transform,false);
            body.transform.localPosition=Vector3.up*.5f;body.transform.localScale=new Vector3(1.4f,1f,1.4f);Object.DestroyImmediate(body.GetComponent<Collider>());
            var box=root.AddComponent<BoxCollider>();box.center=Vector3.up*.5f;box.size=new Vector3(1.4f,1f,1.4f);
            root.AddComponent<WeaponHitZone>();var hitRoot=root.AddComponent<HitboxRoot>();hitRoot.Offset=Vector3.up*.5f;hitRoot.BroadRadius=1.2f;
            var hitbox=body.AddComponent<Hitbox>();hitbox.Type=HitboxTypes.Box;hitbox.BoxExtents=Vector3.one*.5f;hitbox.Root=hitRoot;hitRoot.Hitboxes=new[]{hitbox};
            Set(death,"lifeStateSource",health);SetArray(death,"hitboxRootsToDisableOnDeath",hitRoot);SetArray(death,"collidersToDisableOnDeath",box);
            root.AddComponent<EnemyDamageFlash>();
            var presentation=root.AddComponent<EnemyFlytrapPresentation>();Set(presentation,"placeholder",body.GetComponent<Renderer>());
            if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null) throw new InvalidOperationException("捕食怪 Prefab 儲存失敗。");
        }
        finally {Object.DestroyImmediate(root);}
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("[Flytrap] 方塊 Prefab 已建立，請手動加入 Stationary 生成區。");
    }
    private static void Set(Object obj,string name,object value)
    {
        var data=new SerializedObject(obj);var p=data.FindProperty(name);
        if(p==null) throw new InvalidOperationException(obj.GetType().Name+" 缺少設定："+name);
        if(value is string s)p.stringValue=s;else if(value is float f)p.floatValue=f;else if(value is int i)p.intValue=i;else if(value is bool b)p.boolValue=b;else if(value is Object o)p.objectReferenceValue=o;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(Object obj,string name,Object value)
    {var data=new SerializedObject(obj);var p=data.FindProperty(name);p.arraySize=1;p.GetArrayElementAtIndex(0).objectReferenceValue=value;data.ApplyModifiedPropertiesWithoutUndo();}
}
