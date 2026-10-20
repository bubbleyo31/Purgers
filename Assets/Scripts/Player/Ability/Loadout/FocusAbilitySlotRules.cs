using System;
using System.Collections.Generic;

/// <summary>專注槽位及重複技能政策。強化／重複實例尚未啟用；既有 SkillLevel 與 Definition 強化契約保留。</summary>
public static class FocusAbilitySlotRules
{
    public const int SlotCount = 3;
    public static bool IsSlot(int slot) => slot >= 0 && slot < SlotCount;
    public static int FindFirstEmpty(IReadOnlyList<bool> occupied)
    {
        for (int i = 0; i < SlotCount; i++)
            if (occupied != null && i < occupied.Count && !occupied[i]) return i;
        return -1;
    }
    public static int ResolveDestination(IReadOnlyList<bool> occupied, int requestedSlot) =>
        IsSlot(requestedSlot) ? requestedSlot : -1;
    // All acquisition paths share this seam. Future duplicate/upgrade policy belongs here,
    // after its rules are approved; no speculative enhancement or cooldown sharing now.
    public static bool CanAcquire(string abilityId, IEnumerable<string> equippedIds)
    {
        if (string.IsNullOrWhiteSpace(abilityId)) return false;
        if (equippedIds != null)
            foreach (var id in equippedIds)
                if (string.Equals(abilityId, id, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
    public static int ConsumeDropPresses(int held, int pressed, bool modifierHeld,
        bool replacing, ref int consumedUntilRelease)
    {
        held &= 7; pressed &= held;
        consumedUntilRelease &= held;
        int drops = modifierHeld && !replacing ? pressed & ~consumedUntilRelease : 0;
        if (modifierHeld || replacing) consumedUntilRelease |= held;
        return drops;
    }
}
