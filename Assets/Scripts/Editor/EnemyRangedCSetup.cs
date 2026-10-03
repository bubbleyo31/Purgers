using System;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>只建立獨立砲台資產，不修改來源 A/B、場景、生成區或使用者既有的 C 資產。</summary>
public static class EnemyRangedCSetup
{
    public const string PrefabDirectory = "Assets/Prefabs/Enemy/Variants/";
    public const string DataDirectory = "Assets/_Project_Assets/Data/Enemy/";

    [MenuItem("Tools/Purgers/Enemy/Create Missing Ranged C Turrets")]
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("請退出 Play Mode 後建立砲台資產。");
        CreateOne("Projectile", "A", "定點投射物砲台");
        CreateOne("Beam", "B", "定點光束砲台");
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("[Ranged C] 定點光束／投射物資產已建立或保留。請手動配置生成位置與候選 Prefab；未修改 Scene 或 MapChunk。");
    }

    private static void CreateOne(string suffix, string sourceVariant, string displayName)
    {
        string prefabPath = PrefabDirectory + "Enemy_Ranged_C_" + suffix + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;
        string definitionPath = DataDirectory + "ED_Ranged_C_" + suffix + ".asset";
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(definitionPath);
        if (definition == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DataDirectory + "ED_Ranged_" + sourceVariant + ".asset");
            if (source == null) throw new InvalidOperationException("缺少來源 EnemyDefinition。");
            definition = Object.Instantiate(source);
            var data = new SerializedObject(definition);
            data.FindProperty("enemyId").stringValue = "ranged_c_" + suffix.ToLowerInvariant();
            data.FindProperty("displayName").stringValue = displayName;
            data.FindProperty("designDescription").stringValue = "Ranged C 定點怪；沿用既有攻擊並整隻原地轉向，不掛巡邏或追逐元件。造型暫沿用來源遠程怪。";
            data.FindProperty("combatFamily").intValue = (int)EnemyCombatFamily.Ranged;
            data.FindProperty("variant").intValue = (int)EnemyVariant.C;
            data.FindProperty("locomotionKind").intValue = (int)EnemyLocomotionKind.Stationary;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(definition, definitionPath);
        }
        if (definition.LocomotionKind != EnemyLocomotionKind.Stationary || definition.Variant != EnemyVariant.C)
            throw new InvalidOperationException("既有 C Definition 不符合定點分類；請先檢查，工具不覆寫它。");

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabDirectory + "Enemy_Ranged_" + sourceVariant + ".prefab");
        try
        {
            root.name = "Enemy_Ranged_C_" + suffix;
            // 移除行為而非只把速度調成 0，避免仍需要 Area 或偷偷做 NavMesh 查詢。
            RemoveAll<EnemyIdlePatrolBrain>(root);
            RemoveAll<EnemyChaseBrain>(root);
            RemoveAll<EnemyPatrolNavigator>(root);
            RemoveAll<EnemyChaseMotor>(root);
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                animator.applyRootMotion = false;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            var actor = root.GetComponent<EnemyActor>();
            var data = new SerializedObject(actor);
            data.FindProperty("definition").objectReferenceValue = definition;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (!actor.TryInitializePatrolBeforeSpawn(null))
                throw new InvalidOperationException("砲台仍殘留巡邏／追逐依賴。");
            if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                throw new InvalidOperationException("儲存砲台 Prefab 失敗：" + prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void RemoveAll<T>(GameObject root) where T : Component
    {
        foreach (T component in root.GetComponentsInChildren<T>(true))
            Object.DestroyImmediate(component);
    }
}
