using System;
using System.Collections.Generic;

namespace Purgers.Progression
{
    /// <summary>個人技能倍率與子彈時間均分純規則，不寫生命、Networked 狀態或存檔。</summary>
    public static class PlayerAbilityExperienceRules
    {
        public static int ApplyMultiplier(int amount, float multiplier)
        {
            if (amount <= 0) return 0;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1f)
                multiplier = 1f;
            return (int)Math.Min(int.MaxValue, Math.Floor((double)amount * multiplier));
        }

        /// <summary>
        /// 暫定：單一敵人的基礎經驗加擊殺額外值構成池，按穩定玩家鍵均分。
        /// 同一人只占一份，餘數依鍵排序由小到大分配；不在這裡排除待選獎勵者。
        /// </summary>
        public static KeyValuePair<int, int>[] CreateEqualShares(int baseExperience,
            int killerBonus, IReadOnlyList<int> participantKeys)
        {
            if (baseExperience <= 0 || participantKeys == null || participantKeys.Count == 0)
                return Array.Empty<KeyValuePair<int, int>>();
            var keys = new SortedSet<int>();
            for (int i = 0; i < participantKeys.Count; i++) keys.Add(participantKeys[i]);
            if (keys.Count == 0) return Array.Empty<KeyValuePair<int, int>>();
            long pool = (long)baseExperience + Math.Max(0, killerBonus);
            long share = pool / keys.Count;
            long remainder = pool % keys.Count;
            var result = new KeyValuePair<int, int>[keys.Count];
            int ordinal = 0;
            foreach (int key in keys)
            {
                result[ordinal] = new KeyValuePair<int, int>(key,
                    (int)Math.Min(int.MaxValue, share + (ordinal < remainder ? 1L : 0L)));
                ordinal++;
            }
            return result;
        }
    }
}
