using System;
using System.Collections.Generic;

namespace Purgers.Progression
{
    public enum RewardCategory : byte
    {
        GrappleHit = 1,
        GrappleFocus = 2,
        Weapon = 3,
        CharacterBonus = 4,
        OneShotAbility = 5
    }

    public enum RewardRepeatPolicy : byte
    {
        OncePerRun = 0,
        Repeatable = 1
    }

    public readonly struct RewardDraftEntry
    {
        public readonly string StableRewardId;
        public readonly RewardCategory Category;
        public readonly int MinimumPlayerLevel;
        public readonly int Weight;
        public readonly RewardRepeatPolicy RepeatPolicy;

        public RewardDraftEntry(
            string stableRewardId,
            RewardCategory category,
            int minimumPlayerLevel,
            int weight,
            RewardRepeatPolicy repeatPolicy)
        {
            StableRewardId = stableRewardId;
            Category = category;
            MinimumPlayerLevel = minimumPlayerLevel;
            Weight = weight;
            RepeatPolicy = repeatPolicy;
        }
    }

    /// <summary>
    /// Pure, deterministic normal-draft selection. This does not grant a
    /// reward or consume pending rewards. Host must own the eventual draft.
    /// </summary>
    public static class RewardDraftRules
    {
        public const int MaximumChoices = 3;

        public static string[] Draw(
            IReadOnlyList<RewardDraftEntry> pool,
            int playerLevel,
            ulong seed,
            IReadOnlyCollection<string> acquiredRewardIds,
            IReadOnlyCollection<string> excludedRewardIds = null)
        {
            if (pool == null || pool.Count == 0)
                return Array.Empty<string>();

            var acquired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (acquiredRewardIds != null)
            {
                foreach (string id in acquiredRewardIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        acquired.Add(id.Trim());
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedRewardIds != null)
            {
                foreach (string id in excludedRewardIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        excluded.Add(id.Trim());
                }
            }
            var eligible = new List<RewardDraftEntry>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                RewardDraftEntry entry = pool[i];
                if (string.IsNullOrWhiteSpace(entry.StableRewardId) ||
                    entry.Weight <= 0 ||
                    excluded.Contains(entry.StableRewardId) ||
                    playerLevel < Math.Max(1, entry.MinimumPlayerLevel) ||
                    (entry.RepeatPolicy == RewardRepeatPolicy.OncePerRun &&
                     acquired.Contains(entry.StableRewardId)) ||
                    !seen.Add(entry.StableRewardId))
                    continue;

                eligible.Add(entry);
            }

            eligible.Sort((left, right) => StringComparer.Ordinal.Compare(
                left.StableRewardId, right.StableRewardId));

            var chosen = new List<string>(Math.Min(MaximumChoices, eligible.Count));
            ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            while (eligible.Count > 0 && chosen.Count < MaximumChoices)
            {
                ulong totalWeight = 0;
                foreach (RewardDraftEntry entry in eligible)
                    totalWeight += (ulong)entry.Weight;

                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                ulong pick = (state * 2685821657736338717UL) % totalWeight;

                for (int i = 0; i < eligible.Count; i++)
                {
                    ulong weight = (ulong)eligible[i].Weight;
                    if (pick >= weight)
                    {
                        pick -= weight;
                        continue;
                    }

                    chosen.Add(eligible[i].StableRewardId);
                    eligible.RemoveAt(i);
                    break;
                }
            }

            return chosen.ToArray();
        }
    }
}
