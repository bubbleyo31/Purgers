using System;
using NUnit.Framework;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class GuardGrowthRulesTests
{
    [TestCase(0,.4f)] [TestCase(1,.55f)] [TestCase(2,.6f)] [TestCase(3,.7f)] [TestCase(4,.8f)] [TestCase(5,.9f)]
    public void ReductionUsesFinalRankPercentagePoints(int rank,float expected) =>
        Assert.That(TankGuardRules.Reduction(.4f,PlayerGrowthRules.Value(PlayerGrowthKind.GuardReduction,rank)),Is.EqualTo(expected).Within(.0001f));
    [TestCase(0,100f)] [TestCase(1,200f)] [TestCase(2,300f)] [TestCase(3,400f)]
    public void CapacityAndRecoveryScaleTogether(int rank,float expected)
    {
        float maximum=TankGuardRules.Capacity(100,PlayerGrowthRules.Value(PlayerGrowthKind.GuardStamina,rank));
        Assert.That(maximum,Is.EqualTo(expected));
        Assert.That(TankGuardRules.Recovery(0,maximum,.25f,1),Is.EqualTo(expected*.25f));
        Assert.That(TankGuardRules.Recovery(0,maximum,.25f,4),Is.EqualTo(expected));
    }
    [TestCase(0,3f,5f)] [TestCase(1,2.85f,4.75f)] [TestCase(2,2.7f,4.5f)]
    [TestCase(3,2.55f,4.25f)] [TestCase(4,2.4f,4f)] [TestCase(5,2.1f,3.5f)]
    public void CooldownReducesOnlyTwoWaits(int rank,float wait,float broken)
    {
        float reduction=PlayerGrowthRules.Value(PlayerGrowthKind.GuardCooldown,rank);
        Assert.That(PlayerGrowthRules.Reduced(3,reduction),Is.EqualTo(wait).Within(.0001f));
        Assert.That(PlayerGrowthRules.Reduced(5,reduction),Is.EqualTo(broken).Within(.0001f));
        Assert.That(TankGuardRules.Recovery(0,400,.25f,1),Is.EqualTo(100));
    }
    [TestCase(100,.4f,100,40)] [TestCase(100,.9f,400,90)] [TestCase(100,.9f,15,15)]
    [TestCase(100,.4f,0,0)] [TestCase(-100,.4f,100,0)]
    public void BlockedDamageIsBoundedByRemainingStamina(float incoming,float reduction,float stamina,float expected) =>
        Assert.That(TankGuardRules.BlockedDamage(incoming,reduction,stamina),Is.EqualTo(expected).Within(.0001f));
    [Test]
    public void NewRanksDoNotCorruptAnyLegacyRankOrPassive()
    {
        var state=new PlayerGrowthState();
        for(int i=1;i<=15;i++) while(state.TryAcquire((PlayerGrowthKind)i)) {}
        int legacy=state.Ranks,passives=state.Passives;
        for(int i=16;i<=18;i++)
        {
            var kind=(PlayerGrowthKind)i;
            for(int rank=1;rank<=PlayerGrowthRules.MaximumRank(kind);rank++)
            { Assert.That(state.TryAcquire(kind)); Assert.That(state.Rank(kind),Is.EqualTo(rank)); }
            Assert.That(state.TryAcquire(kind),Is.False);
            Assert.That(PlayerGrowthRules.CanOffer(state,kind,false,true),Is.False);
        }
        Assert.That(state.Ranks,Is.EqualTo(legacy)); Assert.That(state.Passives,Is.EqualTo(passives));
        for(int i=1;i<=9;i++) Assert.That(state.Rank((PlayerGrowthKind)i),Is.EqualTo(PlayerGrowthRules.MaximumRank((PlayerGrowthKind)i)));
        state.LoseDownedPassives();
        Assert.That(state.Rank(PlayerGrowthKind.GuardStamina),Is.EqualTo(3));
    }
    [TestCase(PlayerGrowthKind.GuardReduction)] [TestCase(PlayerGrowthKind.GuardStamina)] [TestCase(PlayerGrowthKind.GuardCooldown)]
    public void OnlyActualGuardWeaponCanOfferDefense(PlayerGrowthKind kind)
    {
        Assert.That(PlayerGrowthRules.CanOffer(default,kind,true),Is.False);
        Assert.That(PlayerGrowthRules.CanOffer(default,kind,false),Is.False);
        Assert.That(PlayerGrowthRules.CanOffer(default,kind,false,true),Is.True);
    }
    [Test]
    public void UpgradeAddsOnlyNewCapacityAndDoesNotRefillBrokenStamina()
    {
        Assert.That(TankGuardRules.ResizeStamina(30,100,200,false),Is.EqualTo(130));
        Assert.That(TankGuardRules.ResizeStamina(0,100,200,true),Is.Zero);
        Assert.That(TankGuardRules.ResizeStamina(180,200,100,false),Is.EqualTo(100));
    }
    [Test]
    public void HudUsesActualCapacityAndNeverUsesCooldownAsStamina()
    {
        var low=new GuardHudSnapshot(true,true,false,70,400,0,0);
        Assert.That(low.Visible); Assert.That(low.Ratio,Is.EqualTo(.175f).Within(.0001f));
        var broken=new GuardHudSnapshot(true,false,true,0,400,0,3);
        Assert.That(broken.Visible); Assert.That(broken.Ratio,Is.Zero); Assert.That(broken.BrokenSeconds,Is.EqualTo(3));
        Assert.That(new GuardHudSnapshot(true,false,false,400,400,0,0).Visible,Is.False);
        Assert.That(new GuardHudSnapshot(false,true,false,10,100,0,0).Visible,Is.False);
        Assert.That(new GuardHudSnapshot(true,false,false,30,100,2,0).Recovering,Is.False);
        Assert.That(new GuardHudSnapshot(true,false,false,30,100,0,0).Recovering,Is.True);
    }
    [Test]
    public void FailureResetsBothRankWordsAndLegacyDataDefaultsDefenseToZero()
    {
        var save=GameSaveData.CreateNew("guard","Guard",DateTime.UtcNow);
        var entry=save.RunProgression.PlayerProgressionEntries[0];
        Assert.That(entry.DefenseGrowthRanks,Is.Zero);
        entry.GrowthRanks=123; entry.DefenseGrowthRanks=351; entry.HeldPassiveBits=63;
        save.ResetRunProgression(); entry=save.RunProgression.PlayerProgressionEntries[0];
        Assert.That(entry.GrowthRanks,Is.Zero); Assert.That(entry.DefenseGrowthRanks,Is.Zero);
        Assert.That(entry.HeldPassiveBits,Is.Zero); Assert.That(GameSaveSchema.CurrentVersion,Is.EqualTo(6));
    }
}
