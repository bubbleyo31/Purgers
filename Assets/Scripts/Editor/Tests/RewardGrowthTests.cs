using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Purgers.Progression;
[Category("PurgersRegression")]
public sealed class RewardGrowthTests
{
    [TestCase(2, 2)] [TestCase(3, 1)] [TestCase(6, 1)]
    public void DraftReservesGrowthAndEquipmentSlots(int level, int growthCount)
    {
        var pool = new List<RewardDraftEntry>();
        for (int i = 0; i < 6; i++)
        {
            pool.Add(new RewardDraftEntry("growth." + i, RewardCategory.CharacterBonus, 1, 1, RewardRepeatPolicy.Repeatable));
            pool.Add(new RewardDraftEntry("weapon." + i, RewardCategory.Weapon, 1, 1, RewardRepeatPolicy.Repeatable));
        }
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var result = RewardDraftRules.Draw(pool, level, seed, null);
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(result.Count(x => x.StartsWith("growth.")), Is.EqualTo(growthCount), "seed=" + seed);
        }
    }
    [Test]
    public void AttributesAdvanceOneTierAndLeavePoolAtMaximum()
    {
        var state = new PlayerGrowthState();
        Assert.That(state.TryAcquire(PlayerGrowthKind.MaximumHealth));
        Assert.That(state.TryAcquire(PlayerGrowthKind.MaximumHealth));
        Assert.That(100 + PlayerGrowthRules.Value(PlayerGrowthKind.MaximumHealth, state.Rank(PlayerGrowthKind.MaximumHealth)), Is.EqualTo(140));
        for (int i=0;i<3;i++) Assert.That(state.TryAcquire(PlayerGrowthKind.MaximumHealth));
        Assert.That(state.TryAcquire(PlayerGrowthKind.MaximumHealth), Is.False);
        Assert.That(PlayerGrowthRules.CanOffer(state, PlayerGrowthKind.MaximumHealth, true), Is.False);
    }
    [Test]
    public void WeaponOnlyRestrictsAvailabilityAndRegenerationRequiresUnlock()
    {
        var state = new PlayerGrowthState();
        Assert.That(PlayerGrowthRules.CanOffer(state, PlayerGrowthKind.ReloadSpeed, false), Is.False);
        Assert.That(PlayerGrowthRules.CanOffer(state, PlayerGrowthKind.MeleeSpeed, true), Is.False);
        Assert.That(PlayerGrowthRules.CanOffer(state, PlayerGrowthKind.RegenerationRate, true), Is.False);
        state.TryAcquire(PlayerGrowthKind.RegenerationDelay);
        Assert.That(PlayerGrowthRules.CanOffer(state, PlayerGrowthKind.RegenerationRate, true), Is.True);
        Assert.That(PlayerGrowthRules.Value(PlayerGrowthKind.RegenerationRate, 0), Is.EqualTo(5));
        Assert.That(PlayerGrowthRules.Value(PlayerGrowthKind.RegenerationRate, 1), Is.EqualTo(10));
    }
    [Test]
    public void DowningConsumesOnlySpecifiedPassivesAndTheyCanBeAcquiredAgain()
    {
        var state = new PlayerGrowthState();
        state.TryAcquire(PlayerGrowthKind.MaximumHealth);
        state.TryAcquire(PlayerGrowthKind.SelfRescue);
        state.TryAcquire(PlayerGrowthKind.Adrenaline);
        Assert.That(state.TryAcquire(PlayerGrowthKind.Adrenaline), Is.False);
        state.LoseDownedPassives();
        Assert.That(state.Has(PlayerGrowthKind.SelfRescue), Is.True);
        Assert.That(state.Rank(PlayerGrowthKind.MaximumHealth), Is.EqualTo(1));
        Assert.That(state.Has(PlayerGrowthKind.Adrenaline), Is.False);
        Assert.That(state.TryAcquire(PlayerGrowthKind.Adrenaline), Is.True);
    }
    [Test]
    public void SpeedAndReductionUseDifferentFormulasAndNeverBecomeNegative()
    {
        Assert.That(PlayerGrowthRules.SpeedDuration(2, .2f + .1f), Is.EqualTo(2/1.3f).Within(.0001f));
        Assert.That(PlayerGrowthRules.Reduced(10, .5f), Is.EqualTo(5));
        Assert.That(PlayerGrowthRules.Reduced(10, 1.5f), Is.Zero);
        Assert.That(PlayerGrowthRules.Adrenaline(.09f), Is.EqualTo(.3f));
        Assert.That(PlayerGrowthRules.Adrenaline(.25f), Is.EqualTo(.1f));
        Assert.That(PlayerGrowthRules.Adrenaline(.3f), Is.Zero);
    }
    [Test]
    public void PassiveSlotsAreIndependentAndFallbackDoesNotDuplicate()
    {
        var pool = new[] {
            new RewardDraftEntry("g", RewardCategory.CharacterBonus,1,1,RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("p1", RewardCategory.OneShotAbility,1,1,RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("p2", RewardCategory.OneShotAbility,1,1,RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("w", RewardCategory.Weapon,1,1,RewardRepeatPolicy.Repeatable) };
        var result = RewardDraftRules.Draw(pool,2,1,null,null,1f);
        Assert.That(result.Count(x=>x.StartsWith("p")),Is.EqualTo(2));
        Assert.That(result,Does.Contain("w"));
        var fallback = RewardDraftRules.Draw(pool,3,1,null,new[]{"w"},0f);
        Assert.That(fallback.Length,Is.EqualTo(3));
        Assert.That(fallback.Distinct().Count(),Is.EqualTo(3));
    }
    [Test]
    public void NewRunHasOneOpeningRewardAndFailedRunClearsGrowth()
    {
        var save = GameSaveData.CreateNew("growth", "Growth", System.DateTime.UtcNow);
        var entry = save.RunProgression.PlayerProgressionEntries[0];
        Assert.That(entry.PendingRewardCount, Is.EqualTo(1));
        entry.GrowthRanks = 2; entry.HeldPassiveBits = 63;
        save.ResetRunProgression();
        entry = save.RunProgression.PlayerProgressionEntries[0];
        Assert.That(entry.PendingRewardCount, Is.EqualTo(1));
        Assert.That(entry.GrowthRanks, Is.Zero); Assert.That(entry.HeldPassiveBits, Is.Zero);
    }
    [Test]
    public void NextTierValuesFitExistingRewardCards()
    {
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var root=UnityEngine.Object.Instantiate(prefab);
        try
        {
            var hud=root.GetComponentInChildren<LocalPlayerRewardHUD>(true);
            var refresh=typeof(LocalPlayerRewardHUD).GetMethod("RefreshChoices",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            foreach(var reward in UnityEngine.Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog").Rewards)
            {
                if(!reward.IsImplementedGrowthReward)continue;
                var draft=new PlayerRewardNetworkState();draft.SetChoices(new[]{reward.StableRewardId});
                refresh.Invoke(hud,new object[]{draft});
                foreach(string field in new[]{"Description","Title"})
                {
                    var text=hud.transform.Find("ChoiceWindow/LeftCard/"+field).GetComponent<TMPro.TMP_Text>();
                    var size=text.GetPreferredValues(text.text,text.rectTransform.rect.width,UnityEngine.Mathf.Infinity);
                    Assert.That(size.y,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.1f),reward.name+"/"+field);
                }
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
