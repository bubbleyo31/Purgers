using Fusion;
using UnityEngine;

/// <summary>獎勵候選、領取與使用共同查詢真實武器能力，不用職業名稱冒充武器種類。</summary>
public static class PlayerAbilityQualification
{
    public static bool HasRangedWeapon(Player player)
    {
        var manager = player != null ? player.ProfessionRuntimeManager : null;
        if (manager == null || manager.Object == null || !manager.Object.IsValid ||
            manager.CurrentRuntimeObject == null || !manager.CurrentRuntimeObject.IsValid ||
            player.Profession == null || manager.CurrentRuntimeProfession != player.Profession.CurrentProfession) return false;
        var weapon = manager.CurrentRuntimeObject.GetComponent<PlayerWeaponController>();
        return weapon != null && (weapon.AttackRifle != null || weapon.SupportSMG != null);
    }
    public static bool IsAllowed(Player player, PlayerAbilityDefinition definition) =>
        definition != null && (!definition.RequiresRangedWeapon || HasRangedWeapon(player));
    public static T GetModule<T>(Player player) where T : class
    {
        if (player == null || player.Object == null || !player.Object.IsValid || player.AbilityRuntimeManager == null) return null;
        for (int slot = 0; slot < PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; slot++)
        {
            var runtime = player.AbilityRuntimeManager.GetEquippedRuntimeAtSlot(slot);
            if (runtime != null && runtime.IsAvailableForCurrentProfession && runtime.TryGetModule<T>(out var module))
            {
                if (module is UnityEngine.Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
                return module;
            }
        }
        return null;
    }
    public static Player FindPlayer(NetworkRunner runner, PlayerRef player) =>
        runner != null && runner.TryGetPlayerObject(player, out var obj) && obj != null && obj.IsValid
            ? obj.GetComponent<Player>() : null;
}

