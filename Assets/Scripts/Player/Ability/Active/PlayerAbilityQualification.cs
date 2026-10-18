using Fusion;
using UnityEngine;

/// <summary>獎勵候選、領取與使用共同查詢真實武器能力，不用職業名稱冒充武器種類。</summary>
public static class PlayerAbilityQualification
{
    /// <summary>防禦屬性只對目前持有且已綁定的電鋸 Runtime 合格；不把「不是槍械」當作電鋸。</summary>
    public static bool HasGuardWeapon(Player player) => GetGuardWeapon(player) != null;
    public static TankGuardAbility GetGuardWeapon(Player player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid) return null;
        var manager = player.ProfessionRuntimeManager;
        if (manager == null || manager.Object == null || !manager.Object.IsValid ||
            manager.CurrentRuntimeObject == null || !manager.CurrentRuntimeObject.IsValid ||
            player.Profession == null || player.Profession.Object == null || !player.Profession.Object.IsValid ||
            manager.CurrentRuntimeProfession != player.Profession.CurrentProfession) return null;
        var guard = manager.CurrentRuntimeObject.GetComponent<TankGuardAbility>();
        return guard != null && guard.IsNetworkReady ? guard : null;
    }
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
        T bonus = player.AbilityRuntimeManager.GetActiveBonusModule<T>();
        if (bonus != null) return bonus;
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

