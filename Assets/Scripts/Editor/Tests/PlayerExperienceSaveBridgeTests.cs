using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class PlayerExperienceSaveBridgeTests
{
    [Test]
    public void HostEntryRestoresLevelExperienceAndPendingRewards()
    {
        GameSaveData save = GameSaveData.CreateNew(
            "test", "test", System.DateTime.UtcNow);
        PlayerRunProgressionData entry =
            save.RunProgression.PlayerProgressionEntries[0];
        entry.PlayerLevel = 3;
        entry.CurrentExperience = 7;
        entry.PendingRewardCount = 2;

        PlayerExperienceState restored =
            PlayerExperienceSaveBridge.ReadHost(save);

        Assert.That(restored.Level, Is.EqualTo(3));
        Assert.That(restored.Experience, Is.EqualTo(7));
        Assert.That(restored.PendingRewards, Is.EqualTo(2));
    }

    [Test]
    public void WriteHostPreservesOtherRunFieldsAndOtherPlayers()
    {
        GameSaveData save = GameSaveData.CreateNew(
            "test", "test", System.DateTime.UtcNow);
        PlayerRunProgressionData host =
            save.RunProgression.PlayerProgressionEntries[0];
        host.EquippedWeaponId = "rifle";
        PlayerRunProgressionData guest =
            PlayerRunProgressionData.CreateDefault("guest");
        guest.PlayerLevel = 5;
        save.RunProgression.PlayerProgressionEntries.Add(guest);

        PlayerExperienceSaveBridge.WriteHost(
            save, new PlayerExperienceState(2, 3, 1));

        Assert.That(host.PlayerLevel, Is.EqualTo(2));
        Assert.That(host.CurrentExperience, Is.EqualTo(3));
        Assert.That(host.PendingRewardCount, Is.EqualTo(1));
        Assert.That(host.EquippedWeaponId, Is.EqualTo("rifle"));
        Assert.That(guest.PlayerLevel, Is.EqualTo(5));
    }

    [Test]
    public void ResetRunRestoresDefaultHostExperience()
    {
        GameSaveData save = GameSaveData.CreateNew(
            "test", "test", System.DateTime.UtcNow);
        PlayerExperienceSaveBridge.WriteHost(
            save, new PlayerExperienceState(4, 11, 2));

        save.ResetRunProgression();
        PlayerExperienceState restored =
            PlayerExperienceSaveBridge.ReadHost(save);

        Assert.That(restored.Level, Is.EqualTo(1));
        Assert.That(restored.Experience, Is.Zero);
        Assert.That(restored.PendingRewards, Is.Zero);
    }
}
