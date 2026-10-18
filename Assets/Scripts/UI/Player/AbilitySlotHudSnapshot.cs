using Purgers.GameFlow.Control;
using UnityEngine;

/// <summary>沿既有 Runtime／ActionGate 讀取，技能與 UI 都不在此啟動新計時。</summary>
public static class AbilitySlotHudSnapshot
{
    public static AbilitySlotPresentationState Read(PlayerAbilityRuntime runtime, bool localInputBlocked)
    {
        var result = new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Unavailable };
        if (runtime == null || runtime.Object == null || !runtime.Object.IsValid || runtime.OwnerPlayer == null) return result;
        if (!runtime.TryReadPresentation(out var presentation)) return result;
        result.CooldownRemaining = presentation.Remaining;
        result.CooldownDuration = presentation.Duration;
        Player owner = runtime.OwnerPlayer;
        if (owner.Health == null || owner.Health.Object == null || !owner.Health.Object.IsValid) return result;
        bool usable = owner.Health.IsAlive && runtime.IsAvailableForCurrentProfession && !localInputBlocked;
        bool focus = runtime.Definition != null && runtime.Definition.Category == PlayerAbilityCategory.GrappleFocus;
        var gate = owner.GetComponent<PlayerActionGate>();
        if (gate != null && gate.IsFusionSpawned)
            usable &= !gate.IsBlocked(focus ? PlayerActionBlockMask.ActiveAbility : PlayerActionBlockMask.Grapple);
        runtime.TryGetModule<IPlayerAbilityHudState>(out var hud);
        if (hud != null)
        {
            usable &= hud.IsUsable;
            PlayerAbilityTimedHudState timed = hud.TimedHudState;
            result.PhaseRemaining = timed.RemainingSeconds;
            result.PhaseDuration = timed.DurationSeconds;
            result.Phase = timed.Phase == PlayerActiveAbilityPhase.Casting ? AbilitySlotVisualPhase.Casting :
                timed.Phase == PlayerActiveAbilityPhase.Armed ? AbilitySlotVisualPhase.Recast :
                hud.IsAbilityActive ? AbilitySlotVisualPhase.Active : AbilitySlotVisualPhase.Ready;
        }
        else result.Phase = AbilitySlotVisualPhase.Ready;

        if (runtime.TryGetModule<PlayerActiveAbilityBase>(out var active))
        {
            result.CanPress = usable && active.HudCanPress;
            result.CanEnd = active.HudCanEndActive;
            if (active.Phase == PlayerActiveAbilityPhase.AwaitingPickup) result.Phase = AbilitySlotVisualPhase.Awaiting;
            if (active.Phase == PlayerActiveAbilityPhase.Dashing) result.Phase = AbilitySlotVisualPhase.Ready;
        }
        else if (runtime.TryGetModule<SupportAerialAbility>(out var aerial))
        {
            result.CanPress = usable && aerial.HudCanPress;
            result.CanEnd = aerial.IsAbilityActive;
        }
        else if (runtime.TryGetModule<TankAirDashAbility>(out var dash))
        {
            result.CanPress = usable && dash.HudCanPress && !(gate != null && gate.IsFusionSpawned && gate.IsBlocked(PlayerActionBlockMask.Aim));
        }
        else
        {
            result.CanPress = usable && result.Phase == AbilitySlotVisualPhase.Ready && !result.Cooling;
        }
        if (runtime.TransferCooldownRemaining > 0f) result.CanPress = false;
        if (result.Phase == AbilitySlotVisualPhase.Ready)
            result.Phase = result.Cooling ? AbilitySlotVisualPhase.Cooling :
                result.CanPress ? AbilitySlotVisualPhase.Ready : AbilitySlotVisualPhase.Unavailable;
        return result;
    }
}
