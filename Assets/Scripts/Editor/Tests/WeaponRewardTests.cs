using NUnit.Framework;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class WeaponRewardTests
{
    [TestCase(PlayerProfessionType.Attack)]
    [TestCase(PlayerProfessionType.Tank)]
    [TestCase(PlayerProfessionType.Support)]
    public void ValidWeaponRewardCanEnterCatalog(PlayerProfessionType profession)
    {
        var reward = ScriptableObject.CreateInstance<PlayerRewardDefinition>();
        var catalog = ScriptableObject.CreateInstance<PlayerRewardCatalog>();
        try
        {
            var data = new SerializedObject(reward);
            data.FindProperty("stableRewardId").stringValue = "weapon.test";
            data.FindProperty("category").intValue = (int)RewardCategory.Weapon;
            var weapon = data.FindProperty("weaponProfession");
            Assert.That(weapon, Is.Not.Null, "Weapon reward has no profession target.");
            weapon.intValue = (int)profession;
            data.ApplyModifiedPropertiesWithoutUndo();
            var list = new SerializedObject(catalog);
            list.FindProperty("rewards").arraySize = 1;
            list.FindProperty("rewards").GetArrayElementAtIndex(0).objectReferenceValue = reward;
            list.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(catalog.TryBuildDraftPool(out var pool, out var error), Is.True, error);
            Assert.That(pool.Length, Is.EqualTo(1));
        }
        finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(reward); }
    }

    [Test]
    public void HostWeaponSurvivesRewardSaveRoundTrip()
    {
        var save = GameSaveData.CreateNew("weapon", "weapon", System.DateTime.UtcNow);
        save.RunProgression.PlayerProgressionEntries[0].EquippedWeaponId = "weapon.tank";
        var snapshot = PlayerRewardSaveBridge.ReadHost(save);
        var destination = GameSaveData.CreateNew("copy", "copy", System.DateTime.UtcNow);
        PlayerRewardSaveBridge.WriteHost(destination, snapshot);
        Assert.That(destination.RunProgression.PlayerProgressionEntries[0].EquippedWeaponId,
            Is.EqualTo("weapon.tank"));
    }

    [TestCase(PlayerProfessionType.Attack, "weapon.attack")]
    [TestCase(PlayerProfessionType.Tank, "weapon.tank")]
    [TestCase(PlayerProfessionType.Support, "weapon.support")]
    public void WeaponIdsRoundTrip(PlayerProfessionType weapon, string id)
    {
        Assert.That(WeaponRewardRules.GetWeaponId(weapon), Is.EqualTo(id));
        Assert.That(WeaponRewardRules.ResolveSavedWeapon(id), Is.EqualTo(weapon));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("removed.weapon")]
    public void MissingOrUnknownSaveUsesAttack(string id)
    {
        Assert.That(WeaponRewardRules.ResolveSavedWeapon(id), Is.EqualTo(PlayerProfessionType.Attack));
    }

    [Test]
    public void RepeatableWeaponsExcludeCurrentAndReturnAfterReplacement()
    {
        var pool = new[] {
            new RewardDraftEntry("weapon.attack", RewardCategory.Weapon, 1, 1, RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("weapon.tank", RewardCategory.Weapon, 1, 1, RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("weapon.support", RewardCategory.Weapon, 1, 1, RewardRepeatPolicy.Repeatable)
        };
        Assert.That(RewardDraftRules.Draw(pool, 1, 5, new[] {"weapon.attack","weapon.tank"},
            new[] {"weapon.tank"}), Is.EquivalentTo(new[] {"weapon.attack","weapon.support"}));
        Assert.That(WeaponRewardRules.CanOffer(PlayerProfessionType.Tank, PlayerProfessionType.Tank), Is.False);
        Assert.That(WeaponRewardRules.CanOffer(PlayerProfessionType.Tank, PlayerProfessionType.Attack), Is.True);
    }

    [TestCase(101d, 0, 103d)]
    [TestCase(103d, 1, 108d)]
    [TestCase(110d, 2, 113d)]
    [TestCase(200d, 3, 0d)]
    public void UnequippedChargesContinueSequentialRecharge(double now, int charges, double next)
    {
        var state = new WeaponQuickActionReserve {
            Charges = 0, Capacity = 3, NextChargeAt = 103, RechargeSeconds = 5, RecoveryEndsAt = 102
        }.Advance(now);
        Assert.That(state.Charges, Is.EqualTo(charges));
        if (charges < 3) Assert.That(state.NextChargeAt, Is.EqualTo(next));
        Assert.That(state.RecoveryEndsAt, Is.EqualTo(102));
    }

    [Test]
    public void FailedRunReturnsSavedWeaponToAttack()
    {
        var save = GameSaveData.CreateNew("test", "test", System.DateTime.UtcNow);
        PlayerRewardSaveBridge.WriteHost(save, new PlayerRewardSaveSnapshot("q", "e", null, null, "weapon.support"));
        save.ResetRunProgression();
        Assert.That(WeaponRewardRules.ResolveSavedWeapon(PlayerRewardSaveBridge.ReadHost(save).EquippedWeaponId),
            Is.EqualTo(PlayerProfessionType.Attack));
    }

    [Test]
    public void ProductionCatalogIncludesAllThreeWeaponsWithoutRemovingAbilities()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>("Assets/Resources/Progression/PlayerRewardCatalog.asset");
        Assert.That(catalog.TryBuildDraftPool(out var pool, out var error), Is.True, error);
        int weapons = 0, abilities = 0;
        foreach (var reward in catalog.Rewards)
        {
            if (reward.IsImplementedWeaponReward)
            {
                weapons++;
                Assert.That(reward.MinimumPlayerLevel, Is.EqualTo(1));
                Assert.That(reward.Weight, Is.EqualTo(1));
                Assert.That(reward.RepeatPolicy, Is.EqualTo(RewardRepeatPolicy.Repeatable));
                Assert.That(reward.HudIcon, Is.Not.Null);
                Assert.That(reward.DisplayName, Is.Not.Empty);
            }
            else if (reward.IsImplementedAbilityReward) abilities++;
        }
        Assert.That(weapons, Is.EqualTo(3));
        Assert.That(abilities, Is.EqualTo(15));
    }

    [Test]
    public void ClearingDetachedTracersOnlyDestroysTheOwningWeaponsObjects()
    {
        var a = new GameObject("weapon A");
        var b = new GameObject("weapon B");
        try
        {
            var first = a.AddComponent<BulletTracerLineDrawer>();
            var second = b.AddComponent<BulletTracerLineDrawer>();
            var routine = typeof(BulletTracerLineDrawer).GetMethod("PlayTracerRoutine",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var firstFlight = (System.Collections.IEnumerator)routine.Invoke(first, new object[] { Vector3.zero, Vector3.forward * 1000 });
            var secondFlight = (System.Collections.IEnumerator)routine.Invoke(second, new object[] { Vector3.zero, Vector3.forward * 1000 });
            firstFlight.MoveNext(); secondFlight.MoveNext();
            var field = typeof(BulletTracerLineDrawer).GetField("liveTracers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var ownedA = (System.Collections.Generic.HashSet<GameObject>)field.GetValue(first);
            var ownedB = (System.Collections.Generic.HashSet<GameObject>)field.GetValue(second);
            Assert.That(ownedA.Count, Is.EqualTo(1));
            first.ClearTracers();
            Assert.That(ownedA.Count, Is.Zero);
            Assert.That(ownedB.Count, Is.EqualTo(1));
            foreach (var tracer in ownedB) Assert.That(tracer != null && tracer.activeSelf, Is.True);
            second.ClearTracers();
        }
        finally
        {
            a.GetComponent<BulletTracerLineDrawer>()?.ClearTracers();
            b.GetComponent<BulletTracerLineDrawer>()?.ClearTracers();
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        }
    }
}
