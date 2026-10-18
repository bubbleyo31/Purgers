using NUnit.Framework;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class PlayerExperienceSettingsTests
{
    [Test]
    public void DefaultsPreserveLegacyCurveIncludingSaturation()
    {
        int[] table = PlayerExperienceSettings.CreateLegacyRequirements();
        for (int level = 1; level <= 50; level++)
            Assert.That(PlayerExperienceRules.RequiredExperience(level, table),
                Is.EqualTo(PlayerExperienceRules.RequiredExperience(level)));
        Assert.That(table[table.Length - 1], Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void SessionSnapshotDoesNotFollowAssetEdits()
    {
        var settings = ScriptableObject.CreateInstance<PlayerExperienceSettings>();
        try
        {
            var serialized = new SerializedObject(settings);
            var rows = serialized.FindProperty("levelRequirements");
            rows.arraySize = 3;
            rows.GetArrayElementAtIndex(0).intValue = 5;
            rows.GetArrayElementAtIndex(1).intValue = 15;
            rows.GetArrayElementAtIndex(2).intValue = 30;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            int[] snapshot = settings.CreateSnapshot();
            rows.GetArrayElementAtIndex(1).intValue = 99;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(snapshot[1], Is.EqualTo(15));
            Assert.That(settings.CreateSnapshot()[1], Is.EqualTo(99));
        }
        finally { Object.DestroyImmediate(settings); }
    }

    [Test]
    public void F8UsesCustomTableAndDoesNotGrantForAlreadyOverThresholdSave()
    {
        var table = new[] { 5, 15, 30 };
        var current = new PlayerExperienceState(7, 12, 3);
        int grant = DevelopmentLevelUpRules.ExperienceToNextLevel(current, table);
        Assert.That(grant, Is.EqualTo(18));
        var after = PlayerExperienceRules.Award(current, grant, table);
        Assert.That(after.Level, Is.EqualTo(8));
        Assert.That(after.Experience, Is.Zero);
        Assert.That(after.PendingRewards, Is.EqualTo(4));
        Assert.That(DevelopmentLevelUpRules.ExperienceToNextLevel(
            new PlayerExperienceState(2, 20, 0), table), Is.Zero);
        var loaded = PlayerExperienceRules.Award(new PlayerExperienceState(2, 20, 0), 1, table);
        Assert.That(loaded.Level, Is.EqualTo(3));
        Assert.That(loaded.Experience, Is.EqualTo(6));
    }

    [Test]
    public void NetworkSnapshotCarriesExplicitHostThresholdWithoutChangingSaveState()
    {
        var state = new PlayerExperienceState(5, 17, 2);
        var network = new NetworkPlayerExperience(state, 30);
        Assert.That(network.RequiredExperience, Is.EqualTo(30));
        Assert.That(network.ToState().Level, Is.EqualTo(5));
        Assert.That(network.ToState().Experience, Is.EqualTo(17));
        Assert.That(network.ToState().PendingRewards, Is.EqualTo(2));
    }

    [Test]
    public void FormalResourceIsPresentAndNonEmpty()
    {
        var settings = Resources.Load<PlayerExperienceSettings>(PlayerExperienceSettings.ResourcePath);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.HasRequirements, Is.True);
    }
}
