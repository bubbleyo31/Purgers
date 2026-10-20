using System;
using Fusion;
using Assert = NUnit.Framework.Assert;
using NUnit.Framework;
using Purgers.Settings;
using Purgers.Progression;
using Purgers.GameFlow.Control;
using UnityEngine;

[TestFixture, Category("PurgersRegression")]
public class FocusAbilityIntegrationRulesTests
{
    [Test] public void CombatLockClearsConfirmedLootIntent()
    {
        var input = new NetInput { FocusLootTarget = new NetworkId { Raw = 42 }, FocusLootSlot = 2 };
        PlayerControlLocks.Filter(ref input, PlayerControlMask.Combat);
        Assert.That(input.FocusLootTarget.IsValid, Is.False);
    }
    [Test] public void SlotPickerUsesDedicatedPanelAndShowsEmptySlots()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var instance = UnityEngine.Object.Instantiate(prefab);
        RectTransform preview = null;
        try
        {
            var hud = instance.GetComponentInChildren<LocalPlayerRewardHUD>(true);
            preview = hud.CreateReplacementPreview(new PlayerAbilityDefinition[3]);
            Assert.That(preview.Find("SlotSelectionPanel"), Is.Not.Null);
            Assert.That(preview.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("選擇裝備位置"));
            for(int slot=0;slot<3;slot++)
            {
                var card=preview.Find("SlotSelectionPanel/Destination_"+slot);
                Assert.That(card, Is.Not.Null);
                Assert.That(card.Find("EquippedName").GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("空槽"));
                Assert.That(card.Find("PlacementAction").GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("放入此槽"));
            }
            Assert.That(preview.Find("LeftCard"), Is.Null, "Do not clone the reward-choice layout");
        }
        finally
        {
            if(preview != null) UnityEngine.Object.DestroyImmediate(preview.gameObject);
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }
    [Test] public void GameLogicAndStageFlowFitFusionObjectAllocation()
    {
        int words = 0;
        foreach (var type in new[] { typeof(GameLogic), typeof(Purgers.GameFlow.Stage.StageFlowController) })
        {
            var woven = (NetworkBehaviourWeavedAttribute)Attribute.GetCustomAttribute(type, typeof(NetworkBehaviourWeavedAttribute));
            Assert.That(woven, Is.Not.Null, "Must inspect Unity-woven network size");
            words += woven.WordCount;
        }
        Assert.That(words * 4, Is.LessThan(30 * 1024), "Reserve object headers below Fusion's 32 KiB allocation limit");
    }
    [Test] public void ProductionRewardIdsFitNetworkCapacity()
    {
        var catalog = Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog");
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryBuildDraftPool(out var pool, out var error), Is.True, error);
        Assert.That(pool.Length, Is.GreaterThan(0));
        foreach (var reward in catalog.Rewards)
            Assert.That(reward.StableRewardId.Length, Is.LessThanOrEqualTo(PlayerRewardNetworkState.MaximumRewardIdLength));
    }
    [Test] public void OldSettingsPreserveCustomBindingsAndAddZAndX()
    {
        var settings = new PlayerSettingsData(); Array.Resize(ref settings.keys, 16);
        settings.keys[(int)PlayerBinding.Interact] = KeyCode.G;
        settings.Normalize();
        Assert.That(settings.Key(PlayerBinding.Interact), Is.EqualTo(KeyCode.G));
        Assert.That(settings.Key(PlayerBinding.FocusZ), Is.EqualTo(KeyCode.Z));
        Assert.That(settings.Key(PlayerBinding.FocusX), Is.EqualTo(KeyCode.X));
        Assert.That(settings.keys.Length, Is.EqualTo(PlayerSettingsSession.Actions.Length));
    }
    [Test] public void OldCustomZBindingWinsOverNewDefault()
    {
        var settings = new PlayerSettingsData(); Array.Resize(ref settings.keys, 16);
        settings.keys[(int)PlayerBinding.Jump] = KeyCode.Z;
        settings.Normalize();
        Assert.That(settings.Key(PlayerBinding.Jump), Is.EqualTo(KeyCode.Z));
        Assert.That(settings.Key(PlayerBinding.FocusZ), Is.EqualTo(KeyCode.None));
    }
    [Test] public void RebindingXUsesExistingConflictPolicy()
    {
        var settings = new PlayerSettingsData();
        settings.Bind(PlayerBinding.FocusX, KeyCode.Q);
        Assert.That(settings.Key(PlayerBinding.FocusX), Is.EqualTo(KeyCode.Q));
        Assert.That(settings.Key(PlayerBinding.Grapple), Is.EqualTo(KeyCode.None));
    }
    [Test] public void CombatLockBlocksAllFocusAndDropButtons()
    {
        NetInput input = default;
        foreach (var button in new[] { InputButton.Ability1, InputButton.Ability2, InputButton.Ability3,
            InputButton.DropFocusE, InputButton.DropFocusZ, InputButton.DropFocusX }) input.Buttons.Set(button, true);
        PlayerControlLocks.Filter(ref input, PlayerControlMask.Combat);
        Assert.That(input.Buttons.Bits, Is.Zero);
    }
    [Test] public void SaveRoundTripRetainsAnEmptyMiddleSlot()
    {
        var save = GameSaveData.CreateNew("focus-test", "Focus test", DateTime.UtcNow);
        var snapshot = new PlayerRewardSaveSnapshot("hit", "focus.e", new[] { "candidate" }, Array.Empty<string>(),
            null, 2, 3, "", "focus.x", 1, 7);
        PlayerRewardSaveBridge.WriteHost(save, snapshot);
        var restored = PlayerRewardSaveBridge.ReadHost(save);
        Assert.That(restored.EquippedGrappleFocusId, Is.EqualTo("focus.e"));
        Assert.That(restored.FocusZId, Is.Empty);
        Assert.That(restored.FocusXId, Is.EqualTo("focus.x"));
        Assert.That(restored.FocusXLevel, Is.EqualTo(7));
        Assert.That(restored.PendingCandidateIds, Is.EqualTo(new[] { "candidate" }));
    }
    [Test] public void NetworkRewardStateKeepsThreeIndependentIdentities()
    {
        PlayerRewardNetworkState state = default;
        state.SetFocus(0, "e", 1); state.SetFocus(1, "z", 2); state.SetFocus(2, "x", 3);
        state.SetFocus(1, "", 1);
        Assert.That(state.GetFocusId(0), Is.EqualTo("e"));
        Assert.That(state.GetFocusId(1), Is.Empty);
        Assert.That(state.GetFocusId(2), Is.EqualTo("x"));
        Assert.That(state.GetFocusLevel(2), Is.EqualTo(3));
    }
    [Test] public void DrawExcludesEveryOwnedSkillRatherThanOnlyFirstSlot()
    {
        var pool = new[] { new RewardDraftEntry("e", RewardCategory.GrappleFocus, 1, 1, RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("z", RewardCategory.GrappleFocus, 1, 1, RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("x", RewardCategory.GrappleFocus, 1, 1, RewardRepeatPolicy.Repeatable),
            new RewardDraftEntry("new", RewardCategory.GrappleFocus, 1, 1, RewardRepeatPolicy.Repeatable) };
        Assert.That(RewardDraftRules.Draw(pool, 1, 42, Array.Empty<string>(), new[] { "e", "z", "x" }), Is.EqualTo(new[] { "new" }));
        Assert.That(RewardDraftRules.Draw(pool, 1, 42, Array.Empty<string>(), new[] { "e", "z" }), Does.Contain("x"));
    }
}
