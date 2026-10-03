using UnityEngine;

namespace Purgers.Progression
{
    [CreateAssetMenu(fileName = "PlayerReward", menuName = "Game/Progression/Player Reward")]
    public sealed class PlayerRewardDefinition : ScriptableObject
    {
        [Header("獎勵識別與分類")]
        [Tooltip("獎勵的永久識別碼。已抽候選與存檔會記住此 ID；建立使用後請勿任意改名或重複使用。")]
        [SerializeField] private string stableRewardId = string.Empty;

        [Tooltip("獎勵所屬能力分類。目前只支援鈎索命中與鈎索專注，且必須和下方能力資料的分類一致；未實作分類即使權重為 0 也會使清單驗證失敗。")]
        [SerializeField] private RewardCategory category;

        [Header("候選抽選條件")]
        [Tooltip("獎勵從玩家幾級開始有資格進入新候選；不會改變升級所需經驗。")]
        [SerializeField, Min(1)] private int minimumPlayerLevel = 1;

        [Tooltip("在目前合格候選中的相對抽選權重。設為 0 可停止出現在新候選，但不會移除已抽出的選單或舊存檔紀錄。")]
        [SerializeField, Min(0)] private int weight = 1;

        [Tooltip("OncePerRun：本輪領過後不再抽到；Repeatable：領過後仍可再抽。目前已裝備的能力無論此設定為何都會排除。")]
        [SerializeField] private RewardRepeatPolicy repeatPolicy = RewardRepeatPolicy.OncePerRun;

        [Header("能力與選擇視窗")]
        [Tooltip("實際領取後要裝備的能力 Definition。不可留空，分類必須符合上方獎勵分類；不是在此直接指定 Runtime Prefab。")]
        [SerializeField] private PlayerAbilityDefinition abilityDefinition;

        [Tooltip("ALT 獎勵選窗顯示的技能效果說明。請描述玩家實際會得到的能力，而非內部 ID。")]
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;

        public string StableRewardId => stableRewardId?.Trim() ?? string.Empty;
        public RewardCategory Category => category;
        public int MinimumPlayerLevel => Mathf.Max(1, minimumPlayerLevel);
        public int Weight => Mathf.Max(0, weight);
        public RewardRepeatPolicy RepeatPolicy => repeatPolicy;
        public PlayerAbilityDefinition AbilityDefinition => abilityDefinition;
        public string Description => description?.Trim() ?? string.Empty;

        public RewardDraftEntry ToDraftEntry() => new RewardDraftEntry(
            StableRewardId, Category, MinimumPlayerLevel, Weight, RepeatPolicy);

        public bool IsImplementedAbilityReward =>
            (category == RewardCategory.GrappleHit &&
             abilityDefinition != null &&
             abilityDefinition.Category == PlayerAbilityCategory.GrappleHit) ||
            (category == RewardCategory.GrappleFocus &&
             abilityDefinition != null &&
             abilityDefinition.Category == PlayerAbilityCategory.GrappleFocus);
    }
}
