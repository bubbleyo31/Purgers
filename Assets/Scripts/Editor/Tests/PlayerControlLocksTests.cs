using NUnit.Framework;
using Purgers.GameFlow.Control;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class PlayerControlLocksTests
{
    [Test]
    public void MatchingReasonsStillOwnIndependentIdempotentLeases()
    {
        var locks = new PlayerControlLocks();
        var first = locks.Acquire(PlayerControlMask.AllInput, "Loading");
        var second = locks.Acquire(PlayerControlMask.AllInput, "Loading");
        first.Dispose();
        first.Dispose();
        Assert.That(locks.Count, Is.EqualTo(1));
        Assert.That(locks.Mask, Is.EqualTo(PlayerControlMask.AllInput));
        second.Dispose();
        Assert.That(locks.Mask, Is.EqualTo(PlayerControlMask.None));
    }

    [Test]
    public void ReleasingTransitionDoesNotReleaseMovementOrLookOwners()
    {
        var locks = new PlayerControlLocks();
        var move = locks.Acquire(PlayerControlMask.Movement, "Dialogue");
        var look = locks.Acquire(PlayerControlMask.Look, "Camera");
        var transition = locks.Acquire(PlayerControlMask.AllInput, "Scene");
        transition.Dispose();
        Assert.That(locks.Mask, Is.EqualTo(PlayerControlMask.Movement | PlayerControlMask.Look));
        move.Dispose();
        Assert.That(locks.Mask, Is.EqualTo(PlayerControlMask.Look));
        look.Dispose();
        Assert.That(locks.Mask, Is.EqualTo(PlayerControlMask.None));
    }

    [Test]
    public void MovementGatePreservesLookAndFireButStopsLocomotionButtons()
    {
        NetInput input = FullInput();
        PlayerControlLocks.Filter(ref input, PlayerControlMask.Movement);
        Assert.That(input.Direction, Is.EqualTo(Vector2.zero));
        Assert.That(input.LookDelta, Is.EqualTo(Vector2.one));
        Assert.That(input.Buttons.IsSet(InputButton.Fire), Is.True);
        Assert.That(input.Buttons.IsSet(InputButton.Jump), Is.False);
        Assert.That(input.Buttons.IsSet(InputButton.Grapple), Is.False);
        Assert.That(input.Buttons.IsSet(InputButton.QuickAction), Is.False);
    }

    [Test]
    public void LookGatePreservesMovementAndActions()
    {
        NetInput input = FullInput();
        PlayerControlLocks.Filter(ref input, PlayerControlMask.Look);
        Assert.That(input.Direction, Is.EqualTo(Vector2.one));
        Assert.That(input.LookDelta, Is.EqualTo(Vector2.zero));
        Assert.That(input.Buttons.IsSet(InputButton.Fire), Is.True);
        Assert.That(input.Buttons.IsSet(InputButton.Jump), Is.True);
    }

    [Test]
    public void AllInputGateClearsAllButtonsAndBothAxesAndCarriesTickMask()
    {
        NetInput input = FullInput();
        PlayerControlLocks.Filter(ref input, PlayerControlMask.AllInput);
        Assert.That(input.Buttons.Bits, Is.Zero);
        Assert.That(input.Direction, Is.EqualTo(Vector2.zero));
        Assert.That(input.LookDelta, Is.EqualTo(Vector2.zero));
        Assert.That(input.BlockedControls, Is.EqualTo(PlayerControlMask.AllInput));
        input.Direction = Vector2.one;
        PlayerControlLocks.Filter(ref input, PlayerControlMask.None);
        Assert.That(input.Direction, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void GameplayGateClearsNetworkGameplayWithoutBecomingAllInput()
    {
        NetInput input = FullInput();
        PlayerControlLocks.Filter(ref input, PlayerControlMask.Gameplay);
        Assert.That(input.Buttons.Bits, Is.Zero);
        Assert.That(input.Direction, Is.EqualTo(Vector2.zero));
        Assert.That(input.LookDelta, Is.EqualTo(Vector2.zero));
        Assert.That(input.BlockedControls, Is.EqualTo(PlayerControlMask.Gameplay));
        Assert.That(
            (PlayerControlMask.Gameplay & PlayerControlMask.AllInput),
            Is.EqualTo(PlayerControlMask.None));
    }

    private static NetInput FullInput()
    {
        var input = new NetInput { Direction = Vector2.one, LookDelta = Vector2.one };
        foreach (InputButton button in System.Enum.GetValues(typeof(InputButton)))
            input.Buttons.Set(button, true);
        return input;
    }
}
