using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class RewardMouseReleaseTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SelectionPressStaysConsumedUntilThatButtonIsReleased(int button)
    {
        var go = new GameObject("Reward input test");
        try
        {
            var manager = go.AddComponent<InputManager>();
            var filter = typeof(InputManager).GetMethod("FilterRewardMouseInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(filter, Is.Not.Null, "Reward dismissal must retain a per-button release latch.");
            var mask = typeof(InputManager).GetField("rewardMouseButtonsBlockedUntilRelease",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var inputField = typeof(InputManager).GetField("accumulatedInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            int held = 1 << button;
            filter.Invoke(manager, new object[] { true, held });
            // Selection accepted, ALT released, or a HUD/control-lock transition:
            // ClearPendingInput must not turn the still-held choice into combat.
            typeof(InputManager).GetMethod("ClearPendingInput",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
            var input = new NetInput();
            input.Buttons.Set(InputButton.Fire, true);
            input.Buttons.Set(InputButton.Aim, true);
            inputField.SetValue(manager, input);
            filter.Invoke(manager, new object[] { false, held });
            Assert.That(mask.GetValue(manager), Is.EqualTo(held));
            input = (NetInput)inputField.GetValue(manager);
            if (button == 0) Assert.That(input.Buttons.IsSet(InputButton.Fire), Is.False);
            if (button == 1) Assert.That(input.Buttons.IsSet(InputButton.Aim), Is.False);

            filter.Invoke(manager, new object[] { false, 0 });
            Assert.That(mask.GetValue(manager), Is.EqualTo(0));
            input.Buttons.Set(InputButton.Fire, button == 0);
            input.Buttons.Set(InputButton.Aim, button == 1);
            inputField.SetValue(manager, input);
            filter.Invoke(manager, new object[] { false, held });
            input = (NetInput)inputField.GetValue(manager);
            Assert.That(input.Buttons.IsSet(InputButton.Fire), Is.EqualTo(button == 0));
            Assert.That(input.Buttons.IsSet(InputButton.Aim), Is.EqualTo(button == 1));
        }
        finally { Object.DestroyImmediate(go); }
    }
}
