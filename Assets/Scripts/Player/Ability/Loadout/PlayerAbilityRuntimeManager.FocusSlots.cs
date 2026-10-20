using System;
using Fusion;
using Purgers.GameFlow.Control;

public partial class PlayerAbilityRuntimeManager
{
    public static InputButton FocusButton(int slot) => slot == 1 ? InputButton.Ability2 :
        slot == 2 ? InputButton.Ability3 : InputButton.Ability1;
    public static InputButton DropButton(int slot) => (InputButton)((int)InputButton.DropFocusE + slot);
    public static void ClearFocusButtons(ref NetworkButtons buttons)
    {
        buttons.Set(InputButton.Ability1, false);
        buttons.Set(InputButton.Ability2, false);
        buttons.Set(InputButton.Ability3, false);
    }
    public PlayerAbilityRuntime GetFocusRuntime(int focusSlot)
    {
        if (!FocusAbilitySlotRules.IsSlot(focusSlot)) return null;
        for (int i = 0; i < MaximumAbilityRuntimeSlots; i++)
        {
            var runtime = GetEquippedRuntimeAtSlot(i);
            if (runtime != null && runtime.Definition != null &&
                runtime.Definition.Category == PlayerAbilityCategory.GrappleFocus &&
                runtime.FocusSlotIndex == focusSlot) return runtime;
        }
        return null;
    }
    public PlayerAbilityRuntime GetEquippedRuntime(PlayerAbilityDefinition definition)
    {
        if (definition == null) return null;
        for (int i = 0; i < MaximumAbilityRuntimeSlots; i++)
        {
            var runtime = GetEquippedRuntimeAtSlot(i);
            if (runtime != null && runtime.Definition != null &&
                string.Equals(runtime.Definition.AbilityId, definition.AbilityId, StringComparison.OrdinalIgnoreCase)) return runtime;
        }
        return null;
    }
    public bool HasEmptyFocusSlot => FirstEmptyFocusSlot >= 0;
    public int FirstEmptyFocusSlot
    {
        get { for (int i = 0; i < FocusAbilitySlotRules.SlotCount; i++) if (GetFocusRuntime(i) == null) return i; return -1; }
    }
    public bool TryEquipFocusRewardStateAuthority(PlayerAbilityDefinition definition, int focusSlot, out string error)
    {
        if (!FocusAbilitySlotRules.IsSlot(focusSlot))
        { error = "請先選擇專注技能槽位。"; return false; }
        return TryEquipSingleStateAuthority(definition, null, out error, focusSlot);
    }

    // Physical key presses are collected once locally, but only this authority transaction drops.
    // Process before rescue/loot so ALT+E cannot interact or pick the same object back up.
    public void FilterFocusDropInput(ref NetInput input, NetworkButtons previousButtons)
    {
        bool blocked = input.SettingsMenuOpen || PlayerControlLocks.BlocksCombat(input.BlockedControls);
        var owner = GetComponent<Player>();
        var gate = GetComponent<PlayerActionGate>();
        blocked |= owner == null || owner.Health == null || !owner.Health.IsAlive ||
            (gate != null && (gate.IsCaptured || gate.IsBlocked(PlayerActionBlockMask.ActiveAbility)));
        for (int slot = 0; slot < FocusAbilitySlotRules.SlotCount; slot++)
        {
            var button = DropButton(slot);
            if (!input.Buttons.IsSet(button)) continue;
            input.Buttons.Set(FocusButton(slot), false);
            if (!blocked && input.Buttons.WasPressed(previousButtons, button))
                TryDropFocusStateAuthority(slot);
        }
    }
    public bool TryDropFocusStateAuthority(int slot)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return false;
        var owner = GetComponent<Player>();
        var gate = GetComponent<PlayerActionGate>();
        if (owner == null || owner.Health == null || !owner.Health.IsAlive ||
            (gate != null && (gate.IsCaptured || gate.IsBlocked(PlayerActionBlockMask.ActiveAbility)))) return false;
        var runtime = GetFocusRuntime(slot);
        if (runtime == null) return false;
        int storageSlot = runtime.LoadoutSlotIndex;
        runtime.DropToWorld(ResolveDropPosition(), lootWorldMask.value);
        ActiveAbilityRuntimeObjects.Remove(storageSlot);
        LoadoutRevision++;
        GameLogic.GetPrimaryForRunner(Runner)?.RecordEquippedAbilities(owner);
        return true;
    }
}
