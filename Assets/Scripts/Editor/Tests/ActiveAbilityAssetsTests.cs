using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class ActiveAbilityAssetsTests
{
    [TestCase("Grenade")] [TestCase("PiercingCannon")] [TestCase("Shockwave")]
    [TestCase("Shield")] [TestCase("HealingPack")] [TestCase("Ricochet")]
    [TestCase("Experience")] [TestCase("Blink")] [TestCase("BulletTime")] [TestCase("PrecisionLock")]
    public void DefinitionHasValidModuleAndManualTestLoadout(string key)
    {
        string root = "Assets/_Project_Assets/Data/PlayerAbility/Active/";
        var def = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(root + key + ".asset");
        Assert.That(def, Is.Not.Null, key + " Definition missing");
        Assert.That(def.RuntimePrefab, Is.Not.Null);
        Assert.That(def.Category, Is.EqualTo(PlayerAbilityCategory.GrappleFocus));
        Assert.That(def.RequiresRangedWeapon, Is.EqualTo(key == "PrecisionLock"));
        var runtime = def.RuntimePrefab.GetComponent<PlayerAbilityRuntime>();
        Assert.That(runtime.ValidateConfiguration(def, out string error), Is.True, error);
        var module = def.RuntimePrefab.GetComponent<PlayerActiveAbilityBase>();
        Assert.That(module, Is.Not.Null);
        var config = new SerializedObject(module);
        Assert.That(config.FindProperty("useConditions").arraySize, Is.Zero);
        Assert.That(new SerializedObject(def).FindProperty("enhancementDefinition").objectReferenceValue, Is.Null);
        var loadout = AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>(root + "Loadouts/" + key + ".asset");
        Assert.That(loadout, Is.Not.Null);
        Assert.That(loadout.TryValidate(PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots, out error), Is.True, error);
    }

    [Test]
    public void NewAbilitiesConflictWithBothLegacyESkills()
    {
        var grenade = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Grenade.asset");
        Assert.That(grenade, Is.Not.Null);
        foreach (string key in new[] { "SupportAerial", "TankAirDash" })
        {
            var old = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Definitions/" + key + ".asset");
            Assert.That(old, Is.Not.Null);
            Assert.That(grenade.ConflictsWith(old), Is.True, key);
        }
    }
}
