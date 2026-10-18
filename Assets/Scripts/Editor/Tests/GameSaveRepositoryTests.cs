using System;
using System.IO;
using NUnit.Framework;
using Purgers.Progression;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class GameSaveRepositoryTests
{
    private string testDirectory;

    [SetUp]
    public void SetUp()
    {
        testDirectory = Path.Combine(
            Path.GetTempPath(),
            "PurgersGameSaveTests",
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }

    [Test]
    public void GrowthAndTransferredSkillLevelsSurviveDiskRoundTrip()
    {
        var repository = new JsonGameSaveRepository(testDirectory);
        var created = repository.CreateNew("Growth round trip");
        Assert.That(created.Success, Is.True, created.Error);
        var growth = new PlayerGrowthState();
        growth.TryAcquire(PlayerGrowthKind.MaximumHealth);
        growth.TryAcquire(PlayerGrowthKind.MaximumHealth);
        growth.TryAcquire(PlayerGrowthKind.SelfRescue);
        growth.TryAcquire(PlayerGrowthKind.GuardStamina);
        growth.TryAcquire(PlayerGrowthKind.GuardReduction);
        growth.TryAcquire(PlayerGrowthKind.GuardCooldown);
        var entry = created.Value.RunProgression.PlayerProgressionEntries[0];
        entry.GrowthRanks = growth.Ranks; entry.HeldPassiveBits = growth.Passives;
        entry.DefenseGrowthRanks = growth.DefenseRanks;
        PlayerRewardSaveBridge.WriteHost(created.Value, new PlayerRewardSaveSnapshot("hit", "focus", null, null, null, 3, 4));
        var written = repository.Write(created.Value);
        Assert.That(written.Success, Is.True, written.Error);
        var loaded = repository.Load(created.Value.SaveId);
        Assert.That(loaded.Success, Is.True, loaded.Error);
        entry = loaded.Value.RunProgression.PlayerProgressionEntries[0];
        Assert.That(entry.GrowthRanks, Is.EqualTo(growth.Ranks));
        Assert.That(entry.HeldPassiveBits, Is.EqualTo(growth.Passives));
        Assert.That(entry.DefenseGrowthRanks, Is.EqualTo(growth.DefenseRanks));
        var snapshot = PlayerRewardSaveBridge.ReadHost(loaded.Value);
        Assert.That(snapshot.HitLevel, Is.EqualTo(3));
        Assert.That(snapshot.FocusLevel, Is.EqualTo(4));
    }

    [Test]
    public void CreateListLoadAndOverwriteRoundTrip()
    {
        DateTime firstWrite = new DateTime(
            2026,
            9,
            17,
            1,
            2,
            3,
            DateTimeKind.Utc);
        DateTime currentWrite = firstWrite;
        var repository = new JsonGameSaveRepository(
            testDirectory,
            () => currentWrite);

        GameSaveRepositoryResult<GameSaveData> createResult =
            repository.CreateNew("測試存檔");

        Assert.That(createResult.Success, Is.True, createResult.Error);
        Assert.That(createResult.Value.RunProgression.StageLevel, Is.EqualTo(1));
        Assert.That(
            createResult.Value.RunProgression.PlayerProgressionEntries[0]
                .PlayerLevel,
            Is.EqualTo(1));

        GameSaveCatalog catalog = repository.List();
        Assert.That(catalog.Errors, Is.Empty);
        Assert.That(catalog.Summaries, Has.Count.EqualTo(1));
        Assert.That(catalog.Summaries[0].DisplayName, Is.EqualTo("測試存檔"));
        Assert.That(catalog.Summaries[0].StageLevel, Is.EqualTo(1));

        currentWrite = firstWrite.AddMinutes(5);
        createResult.Value.RunProgression.StageLevel = 3;
        createResult.Value.RunProgression.PlayerProgressionEntries[0]
            .PlayerLevel = 4;

        GameSaveRepositoryResult<GameSaveData> writeResult =
            repository.Write(createResult.Value);
        Assert.That(writeResult.Success, Is.True, writeResult.Error);

        GameSaveRepositoryResult<GameSaveData> loadResult =
            repository.Load(createResult.Value.SaveId);
        Assert.That(loadResult.Success, Is.True, loadResult.Error);
        Assert.That(loadResult.Value.RunProgression.StageLevel, Is.EqualTo(3));
        Assert.That(
            loadResult.Value.RunProgression.PlayerProgressionEntries[0]
                .PlayerLevel,
            Is.EqualTo(4));
        Assert.That(
            loadResult.Value.LastPlayedUtc,
            Is.EqualTo(currentWrite.ToString("O")));
    }

    [Test]
    public void CreateNewCapturesConfiguredCycleLength()
    {
        var repository = new JsonGameSaveRepository(testDirectory);

        GameSaveRepositoryResult<GameSaveData> createResult =
            repository.CreateNew("可調循環", 3);

        Assert.That(createResult.Success, Is.True, createResult.Error);
        Assert.That(createResult.Value.CycleLengthSnapshot, Is.EqualTo(3));

        GameSaveRepositoryResult<GameSaveData> loadResult =
            repository.Load(createResult.Value.SaveId);
        Assert.That(loadResult.Success, Is.True, loadResult.Error);
        Assert.That(loadResult.Value.CycleLengthSnapshot, Is.EqualTo(3));
    }

    [Test]
    public void VersionOneSaveMigratesWithoutCreatingPendingRewards()
    {
        GameSaveData oldSave = GameSaveData.CreateNew(
            Guid.NewGuid().ToString("N"), "Old", DateTime.UtcNow);
        oldSave.SaveVersion = 1;
        oldSave.RunProgression.PlayerProgressionEntries[0].PendingRewardCount = 0;
        oldSave.RunProgression.PlayerProgressionEntries[0].PlayerLevel = 3;
        Directory.CreateDirectory(testDirectory);
        File.WriteAllText(
            Path.Combine(testDirectory, oldSave.SaveId + ".json"),
            JsonUtility.ToJson(oldSave));

        var repository = new JsonGameSaveRepository(testDirectory);
        var loaded = repository.Load(oldSave.SaveId);

        Assert.That(loaded.Success, Is.True, loaded.Error);
        Assert.That(loaded.Value.SaveVersion,
            Is.EqualTo(GameSaveSchema.CurrentVersion));
        Assert.That(loaded.Value.RunProgression.PlayerProgressionEntries[0]
            .PlayerLevel, Is.EqualTo(3));
        Assert.That(loaded.Value.RunProgression.PlayerProgressionEntries[0]
            .PendingRewardCount, Is.Zero);
    }

    [Test]
    public void VersionTwoSaveMigratesWithEmptyRewardDraft()
    {
        GameSaveData oldSave = GameSaveData.CreateNew(
            Guid.NewGuid().ToString("N"), "Old V2", DateTime.UtcNow);
        oldSave.SaveVersion = 2;
        oldSave.RunProgression.PlayerProgressionEntries[0].PendingRewardCount = 1;
        Directory.CreateDirectory(testDirectory);
        File.WriteAllText(
            Path.Combine(testDirectory, oldSave.SaveId + ".json"),
            JsonUtility.ToJson(oldSave));

        var loaded = new JsonGameSaveRepository(testDirectory).Load(oldSave.SaveId);

        Assert.That(loaded.Success, Is.True, loaded.Error);
        Assert.That(loaded.Value.SaveVersion, Is.EqualTo(GameSaveSchema.CurrentVersion));
        Assert.That(loaded.Value.RunProgression.PlayerProgressionEntries[0]
            .PendingRewardCandidateIds, Is.Empty);
        Assert.That(loaded.Value.RunProgression.PlayerProgressionEntries[0]
            .PendingRewardCount, Is.EqualTo(1));
    }

    [Test]
    public void VersionFourWithoutDefenseFieldPreservesLegacyGrowth()
    {
        var oldSave = GameSaveData.CreateNew(Guid.NewGuid().ToString("N"), "Old V4", DateTime.UtcNow);
        oldSave.SaveVersion = 4;
        var entry = oldSave.RunProgression.PlayerProgressionEntries[0];
        entry.GrowthRanks = 87654; entry.HeldPassiveBits = 21;
        Directory.CreateDirectory(testDirectory);
        string json = JsonUtility.ToJson(oldSave).Replace("\"DefenseGrowthRanks\":0,", "");
        Assert.That(json, Does.Not.Contain("DefenseGrowthRanks"));
        File.WriteAllText(Path.Combine(testDirectory, oldSave.SaveId + ".json"), json);
        var loaded = new JsonGameSaveRepository(testDirectory).Load(oldSave.SaveId);
        Assert.That(loaded.Success, Is.True, loaded.Error);
        Assert.That(loaded.Value.SaveVersion, Is.EqualTo(GameSaveSchema.CurrentVersion));
        entry = loaded.Value.RunProgression.PlayerProgressionEntries[0];
        Assert.That(entry.DefenseGrowthRanks, Is.Zero);
        Assert.That(entry.GrowthRanks, Is.EqualTo(87654));
        Assert.That(entry.HeldPassiveBits, Is.EqualTo(21));
    }

    [Test]
    public void PendingRewardsSurviveSaveAndResetWithRun()
    {
        var repository = new JsonGameSaveRepository(testDirectory);
        var created = repository.CreateNew("Pending");
        Assert.That(created.Success, Is.True, created.Error);
        created.Value.RunProgression.PlayerProgressionEntries[0]
            .PendingRewardCount = 2;
        Assert.That(repository.Write(created.Value).Success, Is.True);

        var loaded = repository.Load(created.Value.SaveId);
        Assert.That(loaded.Success, Is.True, loaded.Error);
        Assert.That(loaded.Value.RunProgression.PlayerProgressionEntries[0]
            .PendingRewardCount, Is.EqualTo(2));

        loaded.Value.ResetRunProgression();
        Assert.That(loaded.Value.RunProgression.PlayerProgressionEntries[0]
            .PendingRewardCount, Is.EqualTo(1));
    }

    [Test]
    public void ResetRunProgressionPreservesPermanentProgression()
    {
        GameSaveData save = GameSaveData.CreateNew(
            Guid.NewGuid().ToString("N"),
            "Reset Test",
            DateTime.UtcNow);
        save.PermanentProgression.PermanentPoints = 12;
        save.PermanentProgression.CosmeticUnlockIds.Add("cosmetic.red");
        save.PermanentProgression.SafeHouseTalentIds.Add("talent.totem");
        save.RunProgression.StageLevel = 3;

        PlayerRunProgressionData player =
            save.RunProgression.PlayerProgressionEntries[0];
        player.PlayerLevel = 6;
        player.CurrentExperience = 9;
        player.InventoryItemIds.Add("weapon.prototype");
        player.DebuffIds.Add("debuff.slow");

        save.ResetRunProgression();

        Assert.That(save.PermanentProgression.PermanentPoints, Is.EqualTo(12));
        Assert.That(
            save.PermanentProgression.CosmeticUnlockIds,
            Is.EquivalentTo(new[] { "cosmetic.red" }));
        Assert.That(
            save.PermanentProgression.SafeHouseTalentIds,
            Is.EquivalentTo(new[] { "talent.totem" }));
        Assert.That(save.RunProgression.StageLevel, Is.EqualTo(1));
        Assert.That(
            save.RunProgression.PlayerProgressionEntries,
            Has.Count.EqualTo(1));
        Assert.That(
            save.RunProgression.PlayerProgressionEntries[0].PlayerLevel,
            Is.EqualTo(1));
        Assert.That(
            save.RunProgression.PlayerProgressionEntries[0].CurrentExperience,
            Is.Zero);
        Assert.That(
            save.RunProgression.PlayerProgressionEntries[0].InventoryItemIds,
            Is.Empty);
        Assert.That(
            save.RunProgression.PlayerProgressionEntries[0].DebuffIds,
            Is.Empty);
    }

    [Test]
    public void InvalidSaveIdCannotEscapeRepositoryDirectory()
    {
        var repository = new JsonGameSaveRepository(testDirectory);

        GameSaveRepositoryResult<GameSaveData> result =
            repository.Load("../outside");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("GUID N"));
    }

    [Test]
    public void CorruptSaveDoesNotHideValidSaveFromCatalog()
    {
        var repository = new JsonGameSaveRepository(testDirectory);
        GameSaveRepositoryResult<GameSaveData> createResult =
            repository.CreateNew("Valid");
        Assert.That(createResult.Success, Is.True, createResult.Error);

        Directory.CreateDirectory(testDirectory);
        File.WriteAllText(
            Path.Combine(
                testDirectory,
                Guid.NewGuid().ToString("N") + ".json"),
            "{ not valid json");

        GameSaveCatalog catalog = repository.List();

        Assert.That(catalog.Summaries, Has.Count.EqualTo(1));
        Assert.That(catalog.Summaries[0].DisplayName, Is.EqualTo("Valid"));
        Assert.That(catalog.Errors, Has.Count.EqualTo(1));
    }
}
