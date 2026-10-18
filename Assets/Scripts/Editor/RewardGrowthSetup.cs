#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Purgers.Progression;

/// <summary>只建立本功能的獎勵資產與追加正式清單；不修改 Scene、Prefab 或既有獎勵數值。</summary>
public static class RewardGrowthSetup
{
    public const string Folder = "Assets/_Project_Assets/Data/Progression/Growth";
    private static readonly string[] Names = { "", "最大生命", "換彈速度", "近戰攻速", "彈匣容量", "鈎索能量減耗", "專注技能冷卻減免", "鈎索技能冷卻減免", "脫戰每秒回血", "脫戰回血等待時間", "不是今天", "我自己來", "腎上腺素", "食人樹", "真難抉擇", "槍械高手" };
    private static readonly string[] Descriptions = { "",
        "提高生命上限，補上增加量。", "槍械換彈加速，下次換彈生效。", "近戰加速，不縮短停格蓄力。", "槍械彈匣擴充，補上新增子彈。",
        "降低發射、拉動與側移耗能。", "縮短專注技能下一次冷卻。", "縮短鈎索技能下一次冷卻。", "提高脫戰後每秒回復生命量。", "解鎖自動回血，縮短脫戰等待。",
        "攔截致命傷，恢復 25% 生命、解除負面控場，無敵與免控 3 秒。消耗一次；墜落無效。", "倒地按住 E 10 秒自救，恢復 35% 生命。中斷歸零，成功才消耗。",
        "血量低於 30／20／10%：傷害 +10／20／30%。只取最高檔；倒地失去。", "對敵人實際扣血的 5% 回復自身生命。倒地失去。", "每 10 秒額外隨機施放專注技能，包含副作用。封鎖時延後；倒地失去。", "槍械傷害 +5%、彈匣 +10。倒地失去，超額子彈清除。" };
    [MenuItem("Tools/Purgers/Rewards/Create approved growth rewards")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode only.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project_Assets/Data/Progression", "Growth");
        var catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>("Assets/Resources/Progression/PlayerRewardCatalog.asset");
        if (catalog == null) throw new InvalidOperationException("Missing existing reward catalog.");
        var list = new List<PlayerRewardDefinition>(catalog.Rewards);
        for (int i=1;i<=15;i++)
        {
            var kind = (PlayerGrowthKind)i;
            string path = Folder + "/Reward_" + kind + ".asset";
            var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(path);
            if (reward == null)
            {
                reward = ScriptableObject.CreateInstance<PlayerRewardDefinition>();
                var data = new SerializedObject(reward);
                data.FindProperty("stableRewardId").stringValue = "growth." + kind.ToString().ToLowerInvariant();
                data.FindProperty("category").intValue = (int)(PlayerGrowthRules.IsAttribute(kind) ? RewardCategory.CharacterBonus : RewardCategory.OneShotAbility);
                data.FindProperty("minimumPlayerLevel").intValue = 1;
                data.FindProperty("weight").intValue = 1;
                data.FindProperty("repeatPolicy").intValue = (int)RewardRepeatPolicy.Repeatable;
                data.FindProperty("growthKind").intValue = i;
                data.FindProperty("growthDisplayName").stringValue = Names[i];
                data.FindProperty("description").stringValue = Descriptions[i];
                var values = data.FindProperty("growthValues");
                values.arraySize = PlayerGrowthRules.IsAttribute(kind) ? PlayerGrowthRules.MaximumRank(kind) : 0;
                for (int rank=0;rank<values.arraySize;rank++) values.GetArrayElementAtIndex(rank).floatValue = PlayerGrowthRules.Value(kind,rank+1);
                data.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(reward,path);
                AssetDatabase.SaveAssetIfDirty(reward);
            }
            if (!list.Contains(reward)) list.Add(reward);
        }
        var serialized = new SerializedObject(catalog);
        var entries = serialized.FindProperty("rewards"); entries.arraySize = list.Count;
        for (int i=0;i<list.Count;i++) entries.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
        serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(catalog);
        Debug.Log("[Reward Growth] Created/retained 15 growth rewards. Catalog count="+list.Count+". No Scene/Prefab changed.");
    }
}
#endif
