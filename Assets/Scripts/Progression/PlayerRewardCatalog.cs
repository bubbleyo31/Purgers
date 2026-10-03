using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purgers.Progression
{
    [CreateAssetMenu(fileName = "PlayerRewardCatalog", menuName = "Game/Progression/Reward Catalog")]
    public sealed class PlayerRewardCatalog : ScriptableObject
    {
        [Header("可查找的獎勵清單")]
        [Tooltip("列出可供抽選與舊紀錄查找的獎勵 Definition。暫時不想抽到某項，請在該獎勵資產將權重設為 0，不要直接刪除此引用；空項目、重複 ID 或未實作分類會使整個抽池建立失敗。")]
        [SerializeField] private PlayerRewardDefinition[] rewards =
            Array.Empty<PlayerRewardDefinition>();

        public IReadOnlyList<PlayerRewardDefinition> Rewards => rewards;

        public bool TryFind(string rewardId, out PlayerRewardDefinition reward)
        {
            reward = null;
            if (string.IsNullOrWhiteSpace(rewardId))
                return false;

            foreach (PlayerRewardDefinition candidate in rewards)
            {
                if (candidate != null &&
                    string.Equals(candidate.StableRewardId, rewardId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    reward = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryBuildDraftPool(out RewardDraftEntry[] pool, out string error)
        {
            error = string.Empty;
            var entries = new List<RewardDraftEntry>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerRewardDefinition reward in rewards)
            {
                if (reward == null || !reward.IsImplementedAbilityReward ||
                    string.IsNullOrWhiteSpace(reward.StableRewardId) ||
                    !ids.Add(reward.StableRewardId))
                {
                    pool = Array.Empty<RewardDraftEntry>();
                    error = "獎勵清單含空值、重複 ID 或尚未實作的獎勵類別。";
                    return false;
                }

                entries.Add(reward.ToDraftEntry());
            }

            pool = entries.ToArray();
            return true;
        }
    }
}
