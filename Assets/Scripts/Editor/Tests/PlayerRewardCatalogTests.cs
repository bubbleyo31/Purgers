using NUnit.Framework;
using Purgers.Progression;
using UnityEditor;

[Category("PurgersRegression")]
public sealed class PlayerRewardCatalogTests
{
    [Test]
    public void CurrentCatalogPreservesFourLegacyRewardsAndAddsTenActiveSkills()
    {
        PlayerRewardCatalog catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(
            "Assets/Resources/Progression/PlayerRewardCatalog.asset");

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryBuildDraftPool(out RewardDraftEntry[] pool,
            out string error), Is.True, error);
        // 十項新 E 加入原四項池；原先排除的 AirDash 仍維持既有設定。
        Assert.That(pool.Length, Is.EqualTo(14));
        foreach (PlayerRewardDefinition reward in catalog.Rewards)
        {
            Assert.That(reward.IsImplementedAbilityReward, Is.True,
                reward != null ? reward.name : "null");
            Assert.That(reward.AbilityDefinition.RuntimePrefab, Is.Not.Null);
            Assert.That(reward.RepeatPolicy, Is.EqualTo(RewardRepeatPolicy.Repeatable));
            Assert.That(reward.Description, Is.Not.Empty,
                reward.name + " 缺少選擇視窗說明");
        }
    }

    [Test]
    public void ExpandedPoolShowsThreeChoicesAndStillSupportsTwoChoiceLayout()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(
            "Assets/Resources/Progression/PlayerRewardCatalog.asset");
        var loadout = AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>(
            "Assets/_Project_Assets/Data/PlayerAbility/Loadouts/DefaultPlayerAbilityLoadout.asset");
        var excluded = new System.Collections.Generic.List<string>();
        foreach (var ability in loadout.EquippedAbilities) excluded.Add(ability.AbilityId);
        Assert.That(catalog.TryBuildDraftPool(out var pool, out var error), Is.True, error);
        var choices = RewardDraftRules.Draw(pool, 1, 1, null, excluded);
        Assert.That(choices.Length, Is.EqualTo(3));
        var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var clone = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var hud = clone.GetComponentInChildren<LocalPlayerRewardHUD>(true);
            var refresh = typeof(LocalPlayerRewardHUD).GetMethod("RefreshChoices",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var draft = new PlayerRewardNetworkState();
            draft.SetChoices(new[] { choices[0], choices[1] });
            refresh.Invoke(hud, new object[] { draft });
            var middle = hud.transform.Find("ChoiceWindow/MiddleCard");
            Assert.That(middle.gameObject.activeSelf, Is.False);
            draft.SetChoices(RewardDraftRules.Draw(pool, 1, 1, null));
            refresh.Invoke(hud, new object[] { draft });
            Assert.That(middle.gameObject.activeSelf, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }

    [Test]
    public void EveryFirstPoolAbilityCanReplaceItsCategoryInStartingLoadout()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(
            "Assets/Resources/Progression/PlayerRewardCatalog.asset");
        var loadout = AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>(
            "Assets/_Project_Assets/Data/PlayerAbility/Loadouts/DefaultPlayerAbilityLoadout.asset");

        Assert.That(loadout, Is.Not.Null);
        foreach (PlayerRewardDefinition reward in catalog.Rewards)
        {
            var entries = new System.Collections.Generic.List<PlayerAbilityDefinition>(
                loadout.EquippedAbilities);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Category == reward.AbilityDefinition.Category)
                    entries[i] = reward.AbilityDefinition;
            }

            Assert.That(PlayerAbilityLoadoutDefinition.TryValidateEntries(
                loadout.SlotLayout, entries,
                PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots,
                out string error), Is.True, reward.name + ": " + error);
        }
    }
}
