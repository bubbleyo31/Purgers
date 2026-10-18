using Fusion;

namespace Purgers.GameFlow.SafeHouse
{
    /// <summary>安全屋只限制 Q/E 與玩家來源傷害；不改武器、移動或獎勵規則。</summary>
    public static class SafeHouseGameplayRules
    {
        public static PlayerActionBlockMask BlockedActions(bool isSafeHouse) => isSafeHouse
            ? PlayerActionBlockMask.Grapple | PlayerActionBlockMask.ActiveAbility
            : PlayerActionBlockMask.None;

        public static bool BlocksPlayerDamage(bool isSafeHouse, bool hasPlayerSource) =>
            isSafeHouse && hasPlayerSource;

        public static bool HasPlayerSource(DamageRequest request) =>
            request.Attacker.IsRealPlayer ||
            (request.SourceNetworkObject != null &&
                (request.SourceNetworkObject.GetComponentInParent<Player>() != null ||
                 (request.SourceNetworkObject.IsValid && request.SourceNetworkObject.InputAuthority.IsRealPlayer))) ||
            (request.SourceObject != null && request.SourceObject.GetComponentInParent<Player>() != null);
    }
}
