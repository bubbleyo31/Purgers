using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>鈎索使用能量的行為契約；與左下動能增傷資源分開。</summary>
[Category("PurgersRegression")]
public sealed class GrappleEnergyRulesTests
{
    [TestCase(1, 50f)]
    [TestCase(2, 75f)]
    [TestCase(3, 100f)]
    [TestCase(0, 50f)]
    public void CapacityUsesPlayerLevel(int level, float expected)
    {
        Assert.That(GrappleEnergyRules.MaximumEnergyForLevel(level, 50f, 25f), Is.EqualTo(expected));
    }

    [TestCase(true, 75f)]
    [TestCase(false, 35f)]
    public void LevelUpCanRefillOrAddOnlyCapacityDifference(bool refill, float expected)
    {
        Assert.That(GrappleEnergyRules.ApplyCapacityIncrease(10f, 50f, 75f, refill), Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SameCapacityDoesNotGrantEnergyAgain(bool refill)
    {
        Assert.That(GrappleEnergyRules.ApplyCapacityIncrease(10f, 75f, 75f, refill), Is.EqualTo(10f));
    }

    [Test]
    public void MultipleLevelsAddTheWholeCapacityDifference()
    {
        Assert.That(GrappleEnergyRules.ApplyCapacityIncrease(10f, 50f, 125f, false), Is.EqualTo(85f));
    }

    [Test]
    public void LowerLevelClampsEnergyWithoutGrantingARefill()
    {
        Assert.That(GrappleEnergyRules.ApplyCapacityIncrease(60f, 75f, 50f, true), Is.EqualTo(50f));
        Assert.That(GrappleEnergyRules.ApplyCapacityIncrease(10f, 75f, 50f, true), Is.EqualTo(10f));
    }

    [TestCase(50f, true, 49f)]
    [TestCase(1f, true, 0f)]
    [TestCase(0.5f, false, 0.5f)]
    [TestCase(0f, false, 0f)]
    public void LaunchRequiresFullPayment(float energy, bool accepted, float expected)
    {
        Assert.That(GrappleEnergyRules.TryConsumeLaunch(energy, 1f, out float remaining), Is.EqualTo(accepted));
        Assert.That(remaining, Is.EqualTo(expected));
    }

    [Test]
    public void PullCostDependsOnElapsedTimeNotTickRate()
    {
        float at30 = 50f, at60 = 50f;
        for (int i = 0; i < 90; i++) at30 = GrappleEnergyRules.ConsumePull(at30, 1f, 1f / 30f);
        for (int i = 0; i < 180; i++) at60 = GrappleEnergyRules.ConsumePull(at60, 1f, 1f / 60f);
        Assert.That(at30, Is.EqualTo(47f).Within(0.001f));
        Assert.That(at60, Is.EqualTo(at30).Within(0.001f));
    }

    [Test]
    public void PullCannotProduceNegativeEnergyOrRefill()
    {
        Assert.That(GrappleEnergyRules.ConsumePull(0.25f, 1f, 2f), Is.Zero);
        Assert.That(GrappleEnergyRules.ConsumePull(10f, 0f, 60f), Is.EqualTo(10f));
        Assert.That(GrappleEnergyRules.ConsumePull(10f, 1f, -1f), Is.EqualTo(10f));
    }

    [Test]
    public void PlayerPrefabExposesNewEnergySettingsAndSafeUnspawnedHud()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
        var energy = prefab.GetComponent<PlayerGrappleCharges>();
        Assert.That(energy, Is.Not.Null);
        var serialized = new SerializedObject(energy);
        Assert.That(serialized.FindProperty("levelOneEnergy").floatValue, Is.EqualTo(50f));
        Assert.That(serialized.FindProperty("energyPerLevel").floatValue, Is.EqualTo(25f));
        Assert.That(serialized.FindProperty("launchEnergyCost").floatValue, Is.EqualTo(1f));
        Assert.That(serialized.FindProperty("pullEnergyPerSecond").floatValue, Is.EqualTo(1f));
        Assert.That(serialized.FindProperty("levelUpMode"), Is.Not.Null);
        Assert.That(energy.HasCharge, Is.False);
        Assert.That(energy.NormalizedEnergy, Is.Zero);
        Assert.That(energy.IsRecharging, Is.False);
    }
}
