using NUnit.Framework;
using Purgers.Settings;
using UnityEngine;
using System.IO;
[Category("PurgersRegression")]
public sealed class PlayerSettingsTests
{
    [Test] public void SprintToggleSecondPressAndStopCancelButReleaseKeepsRunning()
    {
        Assert.IsTrue(PlayerSettingsInputRules.SprintActive(false, true, true, true, false));
        Assert.IsTrue(PlayerSettingsInputRules.SprintActive(true, false, false, true, false));
        Assert.IsFalse(PlayerSettingsInputRules.SprintActive(true, true, true, true, false));
        Assert.IsFalse(PlayerSettingsInputRules.SprintActive(true, false, true, false, false));
        Assert.IsFalse(PlayerSettingsInputRules.SprintActive(true, false, false, true, true));
    }
    [Test] public void ConflictClearsWholeBindingAndCopyIsIndependent()
    {
        var saved = new PlayerSettingsData(); var draft = saved.Copy(); draft.Bind(PlayerBinding.Interact, KeyCode.Q);
        Assert.AreEqual(KeyCode.None, draft.Key(PlayerBinding.Grapple)); Assert.AreEqual(KeyCode.Q, draft.Key(PlayerBinding.Interact));
        Assert.AreEqual(KeyCode.E, saved.Key(PlayerBinding.Interact)); draft.Bind(PlayerBinding.Interact, KeyCode.Escape); Assert.AreEqual(KeyCode.Q, draft.Key(PlayerBinding.Interact));
    }
    [Test] public void PersistenceIsIndependentAndAllowsUnboundKeys()
    {
        string folder = Path.Combine("Temp", "SettingsTest-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var value = new PlayerSettingsData(); value.keys[0] = KeyCode.None; string path = Path.Combine(folder, "preferences.json");
            PlayerSettingsService.SaveTo(path, value); value.total = 2; PlayerSettingsService.SaveTo(path, value);
            var loaded = JsonUtility.FromJson<PlayerSettingsData>(File.ReadAllText(path)); Assert.AreEqual(2, loaded.total); Assert.AreEqual(KeyCode.None, loaded.keys[0]); Assert.IsTrue(File.Exists(path + ".bak"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test] public void ModesTravelWithInputAndBadNumbersAreSanitized()
    {
        var s = new PlayerSettingsData(); Assert.AreEqual(128, PlayerSettingsInputRules.Modes(s)); s.grappleToggle = s.sprintToggle = false; Assert.AreEqual(131, PlayerSettingsInputRules.Modes(s));
        s.total = float.NaN; s.horizontal = -5; s.vertical = 99; s.Normalize(); Assert.AreEqual(1, s.total); Assert.AreEqual(.1f, s.horizontal); Assert.AreEqual(2, s.vertical);
    }
}
