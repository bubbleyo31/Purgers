#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

/// <summary>明確執行才建立三項防禦獎勵、追加清單並遷移電鋸基準；不開 Scene、不重排既有 HUD。</summary>
public static class GuardGrowthSetup
{
    public const string CatalogPath = "Assets/Resources/Progression/PlayerRewardCatalog.asset";
    public const string RuntimePath = "Assets/Prefabs/ProfessionRuntime/TankProfessionRuntime.prefab";
    private static readonly PlayerGrowthKind[] Kinds = { PlayerGrowthKind.GuardReduction, PlayerGrowthKind.GuardStamina, PlayerGrowthKind.GuardCooldown };
    private static readonly string[] Names = { "防禦減傷", "防禦耐力", "防禦冷卻" };
    private static readonly string[] Descriptions = {
        "提高格檔減傷比例，基礎為 40%。",
        "提高格檔耐力上限。",
        "縮短恢復等待與破防時間，恢復速度不變。"
    };
    [MenuItem("Tools/Purgers/Rewards/Apply approved guard growth")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請在 Edit Mode 套用。");
        var catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(CatalogPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePath);
        if (catalog == null || prefab == null || prefab.GetComponent<TankGuardAbility>() == null)
            throw new InvalidOperationException("找不到正式獎勵清單或 TankGuardAbility。");
        if (EditorUtility.IsDirty(catalog) || EditorUtility.IsDirty(prefab))
            throw new InvalidOperationException("目標資產有未儲存變更，請先檢視；本工具不一併保存其他工作。");
        var rewards = new List<PlayerRewardDefinition>(catalog.Rewards);
        for (int i = 0; i < Kinds.Length; i++)
        {
            string path = RewardGrowthSetup.Folder + "/Reward_" + Kinds[i] + ".asset";
            var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(path);
            if (reward == null)
            {
                reward = ScriptableObject.CreateInstance<PlayerRewardDefinition>();
                var data = new SerializedObject(reward);
                data.FindProperty("stableRewardId").stringValue = "growth." + Kinds[i].ToString().ToLowerInvariant();
                data.FindProperty("category").intValue = (int)RewardCategory.CharacterBonus;
                data.FindProperty("repeatPolicy").intValue = (int)RewardRepeatPolicy.Repeatable;
                data.FindProperty("growthKind").intValue = (int)Kinds[i];
                data.FindProperty("growthDisplayName").stringValue = Names[i];
                data.FindProperty("description").stringValue = Descriptions[i];
                var values = data.FindProperty("growthValues");
                values.arraySize = PlayerGrowthRules.MaximumRank(Kinds[i]);
                for (int rank = 0; rank < values.arraySize; rank++)
                    values.GetArrayElementAtIndex(rank).floatValue = PlayerGrowthRules.Value(Kinds[i],rank+1);
                data.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(reward,path);
                AssetDatabase.SaveAssetIfDirty(reward);
            }
            if (reward.GrowthKind != Kinds[i] || !reward.IsImplementedGrowthReward)
                throw new InvalidOperationException("既有同路徑獎勵不符合預期，不覆寫：" + path);
            if (!rewards.Contains(reward)) rewards.Add(reward);
        }
        var serialized = new SerializedObject(catalog);
        var entries = serialized.FindProperty("rewards"); entries.arraySize = rewards.Count;
        for (int i=0;i<rewards.Count;i++) entries.GetArrayElementAtIndex(i).objectReferenceValue = rewards[i];
        serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(catalog);
        GameObject contents = PrefabUtility.LoadPrefabContents(RuntimePath);
        try
        {
            var guard = new SerializedObject(contents.GetComponent<TankGuardAbility>());
            guard.FindProperty("guardDamageReduction").floatValue = .4f;
            guard.FindProperty("brokenCooldownDuration").floatValue = 5f;
            guard.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents,RuntimePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        Debug.Log("[Guard Growth] 三項獎勵與 40%／5 秒基準已套用。未修改場景、施法條或既有圖示；新獎勵圖示留待美術配置。");
    }
}
#endif
