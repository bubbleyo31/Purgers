using Fusion;
using UnityEngine;
namespace Purgers.Progression
{
    /// <summary>只讀 GameLogic 的角色成長；生命、武器、技能仍由原 owner 提交結果。</summary>
    public static class PlayerGrowth
    {
        public static PlayerGrowthState State(Player player)
        {
            if (player == null || player.Object == null || !player.Object.IsValid) return default;
            var logic = GameLogic.GetPrimaryForRunner(player.Runner);
            return logic != null ? logic.GetPlayerGrowth(player.Object.InputAuthority) : default;
        }
        public static float Value(Player player, PlayerGrowthKind kind)
        {
            if (player == null || player.Object == null || !player.Object.IsValid) return PlayerGrowthRules.Value(kind, 0);
            var logic = GameLogic.GetPrimaryForRunner(player.Runner);
            return logic != null ? logic.GetGrowthValue(player.Object.InputAuthority, kind) : PlayerGrowthRules.Value(kind, 0);
        }
        public static bool Has(Player player, PlayerGrowthKind kind) => State(player).Has(kind);
        public static float Reduce(Player player, PlayerGrowthKind kind, float original, float otherReduction = 0) =>
            PlayerGrowthRules.Reduced(original, Value(player, kind) + otherReduction);
        public static float Cooldown(Player player, PlayerAbilityCategory category, float original, float otherReduction = 0) =>
            Reduce(player, category == PlayerAbilityCategory.GrappleHit ? PlayerGrowthKind.HitCooldown : PlayerGrowthKind.FocusCooldown, original, otherReduction);
        public static float Reload(Player player, float duration, float otherBonus = 0) =>
            PlayerGrowthRules.SpeedDuration(duration, Value(player, PlayerGrowthKind.ReloadSpeed) + otherBonus);
        public static int MagazineBonus(Player player) => Mathf.RoundToInt(Value(player, PlayerGrowthKind.MagazineCapacity)) + (Has(player, PlayerGrowthKind.JohnWick) ? 10 : 0);
        public static float MeleeSpeed(Player player) => 1 + Value(player, PlayerGrowthKind.MeleeSpeed);
        public static Player Source(in DamageRequest request)
        {
            var obj = request.SourceNetworkObject;
            if (obj != null && obj.IsValid)
                return PlayerAbilityQualification.FindPlayer(obj.Runner, request.Attacker);
            return request.SourceObject != null ? request.SourceObject.GetComponentInParent<Player>() : null;
        }
        public static void ModifyDamage(ref DamageRequest request)
        {
            var player = Source(request);
            if (player == null || !player.Object.HasStateAuthority || player.Health == null) return;
            var state = State(player);
            float bonus = state.Has(PlayerGrowthKind.Adrenaline) ? PlayerGrowthRules.Adrenaline(player.Health.HealthNormalized) : 0;
            // 約翰維克僅槍械來源，技能投射物即使使用 Bullet 類型也不能誤算。
            if (state.Has(PlayerGrowthKind.JohnWick) && request.DamageType == DamageType.Bullet &&
                request.FeedbackId == CombatFeedbackId.AttackRifle) bonus += .05f;
            request.RequestedDamage *= 1 + bonus;
            player.Health.NotifyGrowthCombat();
        }
        public static void Resolved(in DamageResult result)
        {
            var player = Source(result.Request);
            if (player == null || !player.Object.HasStateAuthority || player.Health == null || !player.Health.IsAlive || result.AppliedDamage <= 0) return;
            player.Health.NotifyGrowthCombat();
            if (Has(player, PlayerGrowthKind.CarnivorousTree) && result.TargetObject != null &&
                result.TargetObject.GetComponentInParent<TestDamageReceiver>() != null)
                player.Health.RestoreHealth(result.AppliedDamage * .05f, out _);
        }
    }
}
