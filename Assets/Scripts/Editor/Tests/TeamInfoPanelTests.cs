using NUnit.Framework;
using Purgers.GameFlow.Control;
using UnityEngine;
[Category("PurgersRegression")]
public sealed class TeamInfoPanelTests
{
    [Test] public void TeamWindowStopsCombatButPreservesMovement()
    {
        var input = new NetInput { Direction = Vector2.one, LookDelta = Vector2.one };
        foreach (InputButton button in System.Enum.GetValues(typeof(InputButton))) input.Buttons.Set(button, true);
        PlayerControlLocks.Filter(ref input, (PlayerControlMask)16 | PlayerControlMask.Look);
        Assert.That(input.Direction, Is.EqualTo(Vector2.one));
        Assert.That(input.LookDelta, Is.EqualTo(Vector2.zero));
        Assert.That(input.Buttons.IsSet(InputButton.Jump), Is.True);
        Assert.That(input.Buttons.IsSet(InputButton.Sprint), Is.True);
        Assert.That(input.Buttons.IsSet(InputButton.Crouch), Is.True);
        Assert.That(input.Buttons.IsSet(InputButton.Fire), Is.False);
        Assert.That(input.Buttons.IsSet(InputButton.Ability1), Is.False);
        Assert.That(input.Buttons.IsSet(InputButton.Grapple), Is.False);
        Assert.That(input.Buttons.IsSet(InputButton.QuickAction), Is.False);
    }
}