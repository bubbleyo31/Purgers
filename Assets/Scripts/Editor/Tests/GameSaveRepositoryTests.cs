using System;
using System.IO;
using NUnit.Framework;
using Purgers.Progression;

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
