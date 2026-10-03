using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyExperienceSourceTests
{
    [TestCase("Enemy_Melee_A")]
    [TestCase("Enemy_Melee_B")]
    [TestCase("Enemy_Ranged_A")]
    [TestCase("Enemy_Ranged_B")]
    [TestCase("Enemy_Boss")]
    public void FormalEnemyPrefabHasDeathOwnerAndPositiveExperience(string prefabName)
    {
        string path = "Assets/Prefabs/Enemy/Variants/" + prefabName + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, path);
        EnemyActor actor = prefab.GetComponent<EnemyActor>();
        Assert.That(actor, Is.Not.Null, path);
        Assert.That(prefab.GetComponent<EnemyDeathLifecycleController>(),
            Is.Not.Null, path);
        Assert.That(actor.Definition, Is.Not.Null, path);
        Assert.That(actor.Definition.BaseKillExperience, Is.GreaterThan(0), path);
    }

    [Test]
    public void NewDefinitionDefaultsToOneKillExperience()
    {
        EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
        try
        {
            Assert.That(definition.BaseKillExperience, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void DefinitionCanConfigureIndependentBaseExperience()
    {
        EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
        try
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("baseKillExperience").intValue = 4;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(definition.BaseKillExperience, Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }
}
