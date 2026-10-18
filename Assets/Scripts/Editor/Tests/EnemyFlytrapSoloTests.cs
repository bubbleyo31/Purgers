using System;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyFlytrapSoloTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void CapturedInputOnlyAllowsPermittedPrimaryWeaponAndReload(bool allowWeapon)
    {
        var input = new NetInput { Direction = Vector2.one, LookDelta = new Vector2(3, 7), SideTapSequence = 12 };
        foreach (InputButton button in Enum.GetValues(typeof(InputButton))) input.Buttons.Set(button, true);
        var filtered = PlayerActionGate.FilterCapturedInput(input, allowWeapon);
        foreach (InputButton button in Enum.GetValues(typeof(InputButton)))
            Assert.That(filtered.Buttons.IsSet(button), Is.EqualTo(allowWeapon && (button == InputButton.Fire || button == InputButton.Reload)), button.ToString());
        Assert.That(filtered.Direction, Is.EqualTo(Vector2.zero));
        Assert.That(filtered.LookDelta, Is.EqualTo(input.LookDelta));
        Assert.That(filtered.SideTapSequence, Is.EqualTo(input.SideTapSequence));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CaptureAlwaysBlocksMovementAndSkillRoutes(bool allowWeapon)
    {
        var blocks = PlayerActionGate.CapturedActionBlocks(allowWeapon);
        foreach (var block in new[] { PlayerActionBlockMask.Movement, PlayerActionBlockMask.Aim,
            PlayerActionBlockMask.Grapple, PlayerActionBlockMask.QuickAction, PlayerActionBlockMask.ActiveAbility })
            Assert.That((blocks & block) != 0, Is.True, block.ToString());
        Assert.That((blocks & PlayerActionBlockMask.Fire) != 0, Is.EqualTo(!allowWeapon));
        Assert.That((blocks & PlayerActionBlockMask.Reload) != 0, Is.EqualTo(!allowWeapon));
    }

    [Test]
    public void CaptureNeverSynthesizesInputRemovedByAnotherLock()
    {
        var input = new NetInput { BlockedControls = Purgers.GameFlow.Control.PlayerControlMask.Gameplay };
        input.Buttons.Set(InputButton.Fire, true);
        Purgers.GameFlow.Control.PlayerControlLocks.Filter(ref input, input.BlockedControls);
        var filtered = PlayerActionGate.FilterCapturedInput(input, true);
        Assert.That(filtered.Buttons.IsSet(InputButton.Fire), Is.False);
        Assert.That(filtered.Buttons.IsSet(InputButton.Reload), Is.False);
        Assert.That(filtered.BlockedControls, Is.EqualTo(input.BlockedControls));
    }
}