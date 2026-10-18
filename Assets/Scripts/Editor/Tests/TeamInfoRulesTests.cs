using System;
using NUnit.Framework;
using Purgers.Settings;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class TeamInfoRulesTests
{
    [Test] public void DefaultHoldReleasesAndToggleKeepsOpen()
    {
        var state = new TeamInfoInputState(); state.Step(true,true,false,true,false,false); Assert.IsTrue(state.IsOpen);
        state.Step(false,false,false,true,false,false); Assert.IsFalse(state.IsOpen);
        state.Step(true,true,true,true,false,false); state.Step(false,false,true,true,false,false); Assert.IsTrue(state.IsOpen);
        state.Step(true,true,true,true,false,false); Assert.IsFalse(state.IsOpen);
    }
    [TestCase(false)] [TestCase(true)] public void VoteTakesOverHeldTabAndNeverReopensAutomatically(bool toggle)
    {
        var state = new TeamInfoInputState(); state.Step(true,true,toggle,true,false,false);
        state.Step(true,false,toggle,true,true,false); Assert.IsFalse(state.IsOpen); Assert.IsFalse(state.ReadyPressed);
        state.Step(false,false,toggle,true,true,false); state.Step(true,true,toggle,true,true,false);
        Assert.IsTrue(state.ReadyPressed); Assert.IsFalse(state.IsOpen);
        state.Step(true,false,toggle,true,false,false); Assert.IsFalse(state.IsOpen);
        state.Step(false,false,toggle,true,false,false); state.Step(true,true,toggle,true,false,false); Assert.IsTrue(state.IsOpen);
    }
    [Test] public void EscapeCloseIsConsumedForOnlyTheCurrentStep()
    {
        var state=new TeamInfoInputState();state.Step(true,true,false,true,false,false);
        state.Step(true,false,false,true,false,true);
        Assert.IsFalse(state.IsOpen);Assert.IsTrue(state.EscapeClosed);
        state.Step(false,false,false,true,false,false);Assert.IsFalse(state.EscapeClosed);
    }
    [Test] public void FreshPressWhenVoteAppearsIsReadyOnly()
    {
        var state = new TeamInfoInputState(); state.Step(false,false,false,true,false,false);
        state.Step(true,true,false,true,true,false); Assert.IsTrue(state.ReadyPressed); Assert.IsFalse(state.IsOpen);
    }
    [TestCase(true,false)] [TestCase(false,true)] public void MenuAndEscapeConsumeHeldTab(bool escape,bool unavailable)
    {
        var state = new TeamInfoInputState(); state.Step(true,true,false,true,false,false);
        state.Step(true,false,false,!unavailable,false,escape); Assert.IsFalse(state.IsOpen);
        state.Step(true,false,false,true,false,false); Assert.IsFalse(state.IsOpen);
        state.Step(false,false,false,true,false,false); state.Step(true,true,false,true,false,false); Assert.IsTrue(state.IsOpen);
    }
    [TestCase(1)] [TestCase(2)] [TestCase(5)] [TestCase(8)] [TestCase(15)] [TestCase(64)]
    public void AdaptiveRowsKeepEqualMarginsAndNeverDropItems(int count)
    {
        var grid = new TeamInfoGrid(count,304,68,42,8);
        Assert.GreaterOrEqual(grid.Size,42); Assert.LessOrEqual(grid.Size,68);
        for (int row=0; row<grid.Rows; row++)
        {
            int first=row*grid.Columns, last=Math.Min(count-1,first+grid.Columns-1);
            float left=grid.X(first), right=304-grid.X(last)-grid.Size;
            Assert.That(left,Is.EqualTo(right).Within(.001));
            for(int i=first;i<last;i++) Assert.That(grid.X(i+1)-grid.X(i)-grid.Size,Is.EqualTo(left).Within(.001));
            Assert.LessOrEqual(grid.Y(last)+grid.Size,grid.Height);
        }
        Assert.That((grid.Rows-1)*grid.Columns,Is.LessThan(count));
    }
    [Test] public void CooldownUsesAuthoritativeDenominatorAcrossOpenAndLateJoin()
    {
        Assert.That(TeamInfoGrid.CooldownFraction(6,12),Is.EqualTo(.5f));
        Assert.That(TeamInfoGrid.CooldownFraction(0,12),Is.Zero);
        Assert.That(TeamInfoGrid.CooldownFraction(13,12),Is.EqualTo(1));
        Assert.That(TeamInfoGrid.CooldownFraction(0,0),Is.Zero);
    }
    [Test] public void OldSettingsRetainCustomBindingsAndHoldIsDefault()
    {
        var old = new PlayerSettingsData(); old.Bind(PlayerBinding.Ready,KeyCode.T);
        string json = JsonUtility.ToJson(old).Replace(",\"teamInfoToggle\":false","");
        var loaded=JsonUtility.FromJson<PlayerSettingsData>(json); loaded.Normalize();
        Assert.IsFalse(loaded.teamInfoToggle); Assert.AreEqual(KeyCode.T,loaded.Key(PlayerBinding.Ready));
        loaded.teamInfoToggle=true; Assert.IsTrue(loaded.Copy().teamInfoToggle);
        Assert.AreEqual(16,loaded.keys.Length);
    }
    [Test] public void ClosingRearmsCombatWithoutConsumingMovement()
    {
        var intent = new PlayerSettingsIntent(); uint movement=PlayerSettingsIntent.Bit(PlayerBinding.Sprint), fire=PlayerSettingsIntent.Bit(PlayerBinding.Fire);
        intent.BlockHeld((movement|fire)&LocalTeamInfoPanel.CombatBindings);
        Assert.AreEqual(movement,intent.Rearm(movement|fire)); Assert.AreEqual(movement,intent.Rearm(movement));
        Assert.AreEqual(movement|fire,intent.Rearm(movement|fire));
    }
}
