using System;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>只建立缺少的自爆灰盒資產，不覆蓋既有資產，不修改 Scene／生成區。</summary>
public static class EnemySuicideSetup
{
    public const string PrefabPath = "Assets/Prefabs/Enemy/Variants/Enemy_Suicide.prefab";
    public const string DefinitionPath = "Assets/_Project_Assets/Data/Enemy/ED_Suicide.asset";
    public const string VfxPath = "Assets/_Project_Assets/VFX/Mirza Beig/Cinematic Explosions FREE/Prefabs/Explosions/Explosion002.prefab";
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            Set(definition, "enemyId", "suicide"); Set(definition, "displayName", "自爆撲擊怪");
            Set(definition, "designDescription", "地面大量型；一擊引爆／有限修正撲擊。Inspector 數值為可調原型起始值。");
            Set(definition, "variant", (int)EnemyVariant.C);
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        // 從正式敵人組裝契約建立全新根物件，避免複製模型、骨架與舊序列化引用。
        var root = new GameObject("Enemy_Suicide");
        try
        {
            root.layer = LayerMask.NameToLayer("Hit Mask");
            root.AddComponent<NetworkObject>(); root.AddComponent<NetworkTransform>();
            var actor = root.AddComponent<EnemyActor>(); Set(actor, "definition", definition);
            var health = root.GetComponent<TestDamageReceiver>(); Set(health, "maxHealth", 10f);
            var death = root.AddComponent<CombatDeathHandler>();
            var lifecycle = root.AddComponent<EnemyDeathLifecycleController>();
            Set(lifecycle, "deathAnimationDuration", .25f); Set(lifecycle, "corpseLifetime", 0f);
            root.AddComponent<GrappleInteractionTarget>(); root.AddComponent<FocusTarget>();
            root.AddComponent<AttackGrappleMarkState>(); root.AddComponent<TankGatherMovementReceiver>(); root.AddComponent<SupportGrapplePullReceiver>();
            root.AddComponent<EnemyMovementOwnership>();
            var perception = root.AddComponent<EnemyPerceptionController>(); Set(perception, "occlusionMask", 1); Set(perception, "fallbackEyeHeight", .7f);
            root.AddComponent<EnemyAwarenessBrain>(); root.AddComponent<EnemyCombatDecisionController>();
            root.AddComponent<EnemyChaseBrain>(); root.AddComponent<EnemyIdlePatrolBrain>();
            var patrol = root.AddComponent<EnemyGroundPatrolNavigator>();
            var chase = root.AddComponent<EnemyGroundChaseMotor>();
            foreach (var motor in new Component[] {patrol, chase}) { Set(motor,"obstacleMask",1); Set(motor,"bodyRadius",.35f); Set(motor,"bodyHeight",1f); }
            Set(patrol, "patrolSpeed", 2f); Set(chase, "chaseSpeed", 5f);
            var attack = root.AddComponent<EnemySuicideAttack>();
            Set(attack, "optionId", (int)EnemyCombatOptionId.SuicidePounce); Set(attack,"maximumStartDistance",4f);
            var vfx = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPath);
            if (vfx == null) throw new InvalidOperationException("找不到指定 Explosion002 Prefab。");
            Set(attack,"explosionVfx",vfx);
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius=.35f; capsule.height=1f; capsule.center=Vector3.up*.5f;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name="VisualRoot"; body.transform.SetParent(root.transform,false);
            body.transform.localPosition=Vector3.up*.5f; body.transform.localScale=new Vector3(.7f,1f,.7f); body.layer=root.layer;
            Object.DestroyImmediate(body.GetComponent<Collider>()); root.AddComponent<WeaponHitZone>();
            var hitRoot = root.AddComponent<HitboxRoot>(); hitRoot.BroadRadius = 1f; hitRoot.Offset = Vector3.up * .5f;
            var hitbox = body.AddComponent<Hitbox>(); hitbox.Type = HitboxTypes.Box; hitbox.BoxExtents = Vector3.one * .5f; hitbox.Root = hitRoot; hitRoot.Hitboxes = new[] {hitbox};
            SetArray(death,"hitboxRootsToDisableOnDeath",hitRoot);
            Set(death,"lifeStateSource",health); SetArray(death,"collidersToDisableOnDeath",capsule);
            SetArray(death,"objectsToDisableOnDeath",body);
            var flash=root.AddComponent<EnemyDamageFlash>();
            // 其餘引用依現有元件自動解析；不加入 NavMeshAgent 或 Rigidbody。
            if (PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null) throw new InvalidOperationException("灰盒 Prefab 儲存失敗。");
        }
        finally { Object.DestroyImmediate(root); }
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("[Suicide Enemy] 已建立獨立灰盒，請手動指定生成器候選與巡邏區。");
    }
    private static void Set(Object obj,string name,object value)
    {
        var data=new SerializedObject(obj); var p=data.FindProperty(name);
        if(p==null) throw new InvalidOperationException(obj.GetType().Name+" 缺少設定："+name);
        if(value is string s) p.stringValue=s;
        else if(value is float f) p.floatValue=f;
        else if(value is int i) p.intValue=i;
        else if(value is Object o) p.objectReferenceValue=o;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(Object obj,string name,Object value)
    {
        var data=new SerializedObject(obj);var p=data.FindProperty(name);p.arraySize=1;p.GetArrayElementAtIndex(0).objectReferenceValue=value;data.ApplyModifiedPropertiesWithoutUndo();
    }
}
