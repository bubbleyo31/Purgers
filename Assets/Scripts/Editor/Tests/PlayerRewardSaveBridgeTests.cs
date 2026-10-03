using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class PlayerRewardSaveBridgeTests
{
    [Test]
    public void HostDraftAndEquipmentRoundTripWithoutChangingExperience()
    {
        GameSaveData save = GameSaveData.CreateNew(
            "test", "test", System.DateTime.UtcNow);
        PlayerRunProgressionData host =
            save.RunProgression.PlayerProgressionEntries[0];
        host.PlayerLevel = 4;
        host.PendingRewardCount = 1;

        PlayerRewardSaveBridge.WriteHost(save, new PlayerRewardSaveSnapshot(
            "grapple.hit.mark", "grapple.focus.air_dash",
            new[] { "grapple.hit.pull", "grapple.focus.aerial_slow" },
            new[] { "grapple.hit.mark" }));
        PlayerRewardSaveSnapshot restored = PlayerRewardSaveBridge.ReadHost(save);

        Assert.That(restored.PendingCandidateIds,
            Is.EqualTo(new[] { "grapple.hit.pull", "grapple.focus.aerial_slow" }));
        Assert.That(restored.EquippedGrappleHitId, Is.EqualTo("grapple.hit.mark"));
        Assert.That(restored.EquippedGrappleFocusId,
            Is.EqualTo("grapple.focus.air_dash"));
        Assert.That(restored.AcquiredRewardIds,
            Is.EqualTo(new[] { "grapple.hit.mark" }));
        Assert.That(host.PlayerLevel, Is.EqualTo(4));
        Assert.That(host.PendingRewardCount, Is.EqualTo(1));
    }

    [Test]
    public void ResetRunClearsDraftAndEquipment()
    {
        GameSaveData save = GameSaveData.CreateNew(
            "test", "test", System.DateTime.UtcNow);
        PlayerRewardSaveBridge.WriteHost(save, new PlayerRewardSaveSnapshot(
            "grapple.hit.mark", null, new[] { "grapple.hit.pull" }, null));

        save.ResetRunProgression();
        PlayerRewardSaveSnapshot result = PlayerRewardSaveBridge.ReadHost(save);

        Assert.That(result.EquippedGrappleHitId, Is.Empty);
        Assert.That(result.PendingCandidateIds, Is.Empty);
    }
}
