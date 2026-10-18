using System;
using Fusion;

namespace Purgers.Progression
{
    public enum PlayerGrowthKind
    {
        None, MaximumHealth, ReloadSpeed, MeleeSpeed, MagazineCapacity,
        GrappleCost, FocusCooldown, HitCooldown, RegenerationRate, RegenerationDelay,
        NotToday, SelfRescue, Adrenaline, CarnivorousTree, Indecision, JohnWick,
        // 只能追加；1～15 已用於正式資產、網路及舊存檔。
        GuardReduction = 16, GuardStamina = 17, GuardCooldown = 18
    }

    [Serializable]
    public struct PlayerGrowthState : INetworkStruct
    {
        public int Ranks;
        public int Passives;
        /// <summary>防禦三屬性各三 bit；不改舊九項 Ranks 與被動的位元配置。</summary>
        public int DefenseRanks;
        public int Rank(PlayerGrowthKind kind) => PlayerGrowthRules.IsAttribute(kind)
            ? ((PlayerGrowthRules.IsDefenseAttribute(kind) ? DefenseRanks : Ranks) >>
                PlayerGrowthRules.RankShift(kind)) & 7 : Has(kind) ? 1 : 0;
        public bool Has(PlayerGrowthKind kind) => (int)kind >= 10 && (int)kind <= 15 &&
            (Passives & (1 << ((int)kind - 10))) != 0;
        public bool TryAcquire(PlayerGrowthKind kind)
        {
            if (PlayerGrowthRules.IsAttribute(kind))
            {
                int rank = Rank(kind);
                if (rank >= PlayerGrowthRules.MaximumRank(kind)) return false;
                int shift = PlayerGrowthRules.RankShift(kind);
                if (PlayerGrowthRules.IsDefenseAttribute(kind))
                    DefenseRanks = (DefenseRanks & ~(7 << shift)) | ((rank + 1) << shift);
                else Ranks = (Ranks & ~(7 << shift)) | ((rank + 1) << shift);
                return true;
            }
            if ((int)kind < 10 || (int)kind > 15 || Has(kind)) return false;
            Passives |= 1 << ((int)kind - 10);
            return true;
        }
        public bool Consume(PlayerGrowthKind kind)
        {
            if (!Has(kind)) return false;
            Passives &= ~(1 << ((int)kind - 10)); return true;
        }
        public void LoseDownedPassives()
        {
            Consume(PlayerGrowthKind.Adrenaline); Consume(PlayerGrowthKind.CarnivorousTree);
            Consume(PlayerGrowthKind.Indecision); Consume(PlayerGrowthKind.JohnWick);
        }
    }

    public static class PlayerGrowthRules
    {
        public static bool IsDefenseAttribute(PlayerGrowthKind kind) => (int)kind >= 16 && (int)kind <= 18;
        public static bool IsAttribute(PlayerGrowthKind kind) => ((int)kind >= 1 && (int)kind <= 9) || IsDefenseAttribute(kind);
        public static int RankShift(PlayerGrowthKind kind) => ((int)kind - (IsDefenseAttribute(kind) ? 16 : 1)) * 3;
        public static int MaximumRank(PlayerGrowthKind kind) => !IsAttribute(kind) ? 1 :
            kind == PlayerGrowthKind.GuardStamina ? 3 :
            kind == PlayerGrowthKind.ReloadSpeed || kind == PlayerGrowthKind.MeleeSpeed || kind == PlayerGrowthKind.MagazineCapacity ? 4 : 5;
        public static float Value(PlayerGrowthKind kind, int rank)
        {
            rank = Math.Max(0, Math.Min(MaximumRank(kind), rank));
            if (rank == 0) return kind == PlayerGrowthKind.RegenerationRate ? 5f : 0f;
            switch (kind)
            {
                case PlayerGrowthKind.MaximumHealth: return rank * 20;
                case PlayerGrowthKind.ReloadSpeed: return .05f + rank * .05f;
                case PlayerGrowthKind.MeleeSpeed: return rank * .05f;
                case PlayerGrowthKind.MagazineCapacity: return rank * 5;
                case PlayerGrowthKind.GrappleCost:
                case PlayerGrowthKind.FocusCooldown:
                case PlayerGrowthKind.HitCooldown: return rank * .1f;
                case PlayerGrowthKind.RegenerationRate: return rank * 10;
                case PlayerGrowthKind.RegenerationDelay: return 8 - rank;
                case PlayerGrowthKind.GuardReduction: return rank == 1 ? .15f : rank * .1f;
                case PlayerGrowthKind.GuardStamina: return rank * 100f;
                case PlayerGrowthKind.GuardCooldown: return rank == 5 ? .3f : rank * .05f;
                default: return 0;
            }
        }
        public static bool CanOffer(PlayerGrowthState state, PlayerGrowthKind kind, bool ranged, bool hasGuardWeapon = false)
        {
            if (kind == PlayerGrowthKind.None || state.Rank(kind) >= MaximumRank(kind)) return false;
            if (IsDefenseAttribute(kind) && !hasGuardWeapon) return false;
            if ((kind == PlayerGrowthKind.ReloadSpeed || kind == PlayerGrowthKind.MagazineCapacity) && !ranged) return false;
            if (kind == PlayerGrowthKind.MeleeSpeed && ranged) return false;
            return kind != PlayerGrowthKind.RegenerationRate || state.Rank(PlayerGrowthKind.RegenerationDelay) > 0;
        }
        public static float SpeedDuration(float duration, float bonus) => Math.Max(0, duration) / Math.Max(1, 1 + bonus);
        public static float Reduced(float original, float reduction) => Math.Max(0, original) * Math.Max(0, 1 - reduction);
        public static float Adrenaline(float healthFraction) => healthFraction < .1f ? .3f : healthFraction < .2f ? .2f : healthFraction < .3f ? .1f : 0;
    }
}
