using Fusion;
using UnityEngine;
using Purgers.Settings;
using Purgers.GameFlow.Control;

public partial class InputManager
{
    private int focusConsumedUntilRelease;
    private int replacementChoice = -1, replacementRevision = -1;
    private int pendingHostReplacementSlot = -1;
    private PlayerAbilityRuntime selectedFocusLoot;
    private PlayerAbilityDefinition selectedFocusDefinition;
    private int selectedLootRevision, focusSelectionHandledFrame = -1;
    public bool FocusLootSelectionPending => selectedFocusLoot != null;
    public bool FocusSlotSelectionPending => RewardReplacementPending || FocusLootSelectionPending;
    public bool FocusSelectionBlocksInput => FocusSlotSelectionPending || focusSelectionHandledFrame == Time.frameCount;
    public PlayerAbilityDefinition SelectedFocusDefinition => selectedFocusDefinition;
    private void CancelFocusLootSelection() { selectedFocusLoot = null; if (!RewardReplacementPending) selectedFocusDefinition = null; }
    private void CancelAllFocusSelection() { CancelRewardReplacement(); CancelFocusLootSelection(); }
    private void RefreshGroundFocusSelection()
    {
        if (rewardRunner == null || !rewardRunner.IsRunning || registeredRewardHud == null ||
            !registeredRewardHud.CanPresentRewardSelection ||
            PlayerControlLocks.BlocksCombat(LocalPlayerControl.Locks.Mask) ||
            !rewardRunner.TryGetPlayerObject(rewardRunner.LocalPlayer, out var obj))
        { CancelFocusLootSelection(); return; }
        var player = obj != null ? obj.GetComponent<Player>() : null;
        var manager = player != null ? player.AbilityRuntimeManager : null;
        if (manager == null || player.Health == null || !player.Health.IsAlive)
        { CancelFocusLootSelection(); return; }
        if (FocusLootSelectionPending)
        {
            focusSelectionHandledFrame = Time.frameCount;
            if (IsRewardHoldKeyPressed || Input.GetMouseButtonDown(1) || selectedFocusLoot.Object == null || !selectedFocusLoot.Object.IsValid ||
                !selectedFocusLoot.IsWorldLoot || manager.LoadoutRevision != selectedLootRevision ||
                manager.FindLootCandidateForPresentation() != selectedFocusLoot || player.Health.FindRescueCandidate() != null)
            { CancelFocusLootSelection(); return; }
            for (int slot = 0; slot < FocusAbilitySlotRules.SlotCount; slot++)
            {
                var key = FocusKey(slot);
                if (key == KeyCode.None || !Input.GetKey(key)) focusConsumedUntilRelease &= ~(1 << slot);
                if (key == KeyCode.None || !Input.GetKeyDown(key) || (focusConsumedUntilRelease & (1 << slot)) != 0) continue;
                focusConsumedUntilRelease |= 1 << slot;
                accumulatedInput.FocusLootTarget = selectedFocusLoot.Object.Id;
                accumulatedInput.FocusLootSlot = slot;
                accumulatedInput.FocusLootLoadoutRevision = selectedLootRevision;
                CancelFocusLootSelection();
                return;
            }
            return;
        }
        if (IsRewardSelectionActive || IsRewardHoldKeyPressed || !Pressed(PlayerBinding.Interact) ||
            player.Health.FindRescueCandidate() != null) return;
        var loot = manager.FindLootCandidateForPresentation();
        if (loot == null || loot.Definition.Category != PlayerAbilityCategory.GrappleFocus) return;
        selectedFocusLoot = loot; selectedFocusDefinition = loot.Definition;
        selectedLootRevision = manager.LoadoutRevision;
        focusConsumedUntilRelease |= 1; // The E that opened the picker cannot select E.
        focusSelectionHandledFrame = Time.frameCount;
    }
    public bool RewardReplacementPending => replacementChoice >= 0;
    public KeyCode FocusKey(int slot) => PlayerSettingsService.Key(slot == 1 ? PlayerBinding.FocusZ :
        slot == 2 ? PlayerBinding.FocusX : PlayerBinding.Interact);
    public bool IsFocusManagementHeld => IsRewardHoldKeyPressed &&
        !PlayerControlLocks.BlocksCombat(LocalPlayerControl.Locks.Mask) &&
        LocalPlayerSpectatorController.GetForRunner(Runner)?.IsSpectating != true;

    private void CancelRewardReplacement() { replacementChoice = -1; replacementRevision = -1; if (!FocusLootSelectionPending) selectedFocusDefinition = null; }
    private void SubmitRewardChoice(GameLogic logic, int revision, int choice, int slot)
    {
        if (logic.Object.HasStateAuthority)
        {
            pendingHostRewardRevision = revision; pendingHostRewardIndex = choice;
            pendingHostReplacementSlot = slot; pendingHostRewardChoice = true;
        }
        else logic.RPC_RequestRewardChoiceAtFocusSlot(revision, choice, slot);
    }
    private void RefreshFocusSlotInput(ref NetworkButtons currentButtons)
    {
        if (!IsRewardSelectionActive) CancelRewardReplacement();
        int held = 0, pressed = 0;
        for (int i = 0; i < FocusAbilitySlotRules.SlotCount; i++)
        {
            var key = FocusKey(i);
            if (key != KeyCode.None && Input.GetKey(key)) held |= 1 << i;
            if (key != KeyCode.None && Input.GetKeyDown(key)) pressed |= 1 << i;
        }
        bool modifier = IsFocusManagementHeld;
        int drops = FocusAbilitySlotRules.ConsumeDropPresses(held, pressed, modifier,
            FocusSelectionBlocksInput, ref focusConsumedUntilRelease);
        // A confirmed replacement consumed its key before this method. No drop/activation leak.
        for (int i = 0; i < FocusAbilitySlotRules.SlotCount; i++)
        {
            var binding = i == 1 ? PlayerBinding.FocusZ : i == 2 ? PlayerBinding.FocusX : PlayerBinding.Interact;
            bool canPress = !IsRewardSelectionActive && !FocusSelectionBlocksInput && !modifier && (focusConsumedUntilRelease & (1 << i)) == 0;
            currentButtons.Set(PlayerAbilityRuntimeManager.FocusButton(i), canPress && Held(binding));
            currentButtons.Set(PlayerAbilityRuntimeManager.DropButton(i), (drops & (1 << i)) != 0);
        }
    }
}
