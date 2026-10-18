using UnityEngine;

namespace Purgers.Progression
{
    [CreateAssetMenu(fileName = "PlayerReward", menuName = "Game/Progression/Player Reward")]
    public sealed class PlayerRewardDefinition : ScriptableObject
    {
        [Header("獎勵識別與分類")]
        [Tooltip("獎勵的永久識別碼。已抽候選與存檔會記住此 ID；建立使用後請勿任意改名或重複使用。")]
        [SerializeField] private string stableRewardId = string.Empty;

        [Tooltip("獎勵所屬能力分類。支援鈎索、專注、武器、普通屬性與特殊被動；能力或 GrowthKind 須符合分類。")]
        [SerializeField] private RewardCategory category;

        [Header("候選抽選條件")]
        [Tooltip("獎勵從玩家幾級開始有資格進入新候選；不會改變升級所需經驗。")]
        [SerializeField, Min(1)] private int minimumPlayerLevel = 1;

        [Tooltip("在目前合格候選中的相對抽選權重。設為 0 可停止出現在新候選，但不會移除已抽出的選單或舊存檔紀錄。")]
        [SerializeField, Min(0)] private int weight = 1;

        [Tooltip("OncePerRun：本輪領過後不再抽到；Repeatable：領過後仍可再抽。目前已裝備的能力無論此設定為何都會排除。")]
        [SerializeField] private RewardRepeatPolicy repeatPolicy = RewardRepeatPolicy.OncePerRun;

        [Header("能力與選擇視窗")]
        [Tooltip("實際領取後要裝備的能力 Definition。能力獎勵不可留空，Weapon 必須留空；分類必須符合上方獎勵分類；不是在此直接指定 Runtime Prefab。")]
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

        [Header("武器獎勵")]
        [Tooltip("Weapon 分類要替換成的既有武器操作組。沿用職業 Runtime；能力獎勵保持 None。")]
        [SerializeField] private PlayerProfessionType weaponProfession;
        [Tooltip("武器獎勵卡名稱；不影響穩定 ID 或存檔。")]
        [SerializeField] private string weaponDisplayName = string.Empty;
        [Tooltip("武器獎勵卡圖片；留空沿用 HUD 佔位圖。")]
        [SerializeField] private Sprite weaponIcon;

        public PlayerProfessionType WeaponProfession => weaponProfession;
        public bool IsImplementedWeaponReward => category == RewardCategory.Weapon &&
            WeaponRewardRules.IsWeapon(weaponProfession) && abilityDefinition == null;
        [Header("角色成長與被動")]
        [SerializeField, Tooltip("角色保存的屬性或被動種類；普通屬性每次只升一階，被動同時最多一份，消耗後可再抽。")]
        private PlayerGrowthKind growthKind;
        [SerializeField, Tooltip("每階最終加成，不累加前階。百分比使用 0.1 表示 10%；長度必須符合該屬性的階數。")]
        private float[] growthValues = System.Array.Empty<float>();
        [SerializeField, Tooltip("成長／被動獎勵卡顯示名稱。")]
        private string growthDisplayName;
        [SerializeField, Tooltip("成長／被動獎勵圖示，留空沿用既有 HUD 佔位圖。")]
        private Sprite growthIcon;

        [Header("屬性階級圖示")]
        [SerializeField, Tooltip("不含羅馬數字的屬性底圖。只供獎勵卡的中央 I～V 疊字使用；空白時沿用 Growth Icon。")]
        private Sprite growthRankBackground;
        [SerializeField, Tooltip("羅馬數字中心的原圖比例座標（左下 0,0，右上 1,1）。只改顯示位置。")]
        private Vector2 growthRankCenter = new Vector2(.5f, .5f);
        [SerializeField, Tooltip("羅馬數字字框占原圖寬高的比例。依底圖留白調整；不影響階級上限。")]
        private Vector2 growthRankSize = new Vector2(.4f, .3f);
        [SerializeField, Range(1f, 2f), Tooltip("屬性圖示在完整六角內接模式上的等比倍率（1～2，預設 1）。目前依 71.28525×82.95008 底板、Inset 2 的正式預覽校準；完整保留原圖 UV，不拉伸、不裁角落符號。改圖或底板長寬比後須重新核對輪廓。只供屬性獎勵，不影響被動、技能或玩法數值。")]
        private float growthIconScale = 1f;
        public float GrowthIconScale => Mathf.Clamp(growthIconScale, 1f, 2f);
        public Sprite GrowthRankBackground => growthRankBackground;
        public Vector2 GrowthRankCenter => growthRankCenter;
        public Vector2 GrowthRankSize => growthRankSize;

        public PlayerGrowthKind GrowthKind => growthKind;
        public bool IsImplementedGrowthReward => abilityDefinition == null &&
            ((category == RewardCategory.CharacterBonus && PlayerGrowthRules.IsAttribute(growthKind) &&
              growthValues != null && growthValues.Length == PlayerGrowthRules.MaximumRank(growthKind)) ||
             (category == RewardCategory.OneShotAbility && (int)growthKind >= 10 && (int)growthKind <= 15));
        public float GrowthValue(int rank) => rank <= 0 ? PlayerGrowthRules.Value(growthKind, 0) :
            growthValues != null && rank <= growthValues.Length ? Mathf.Max(0, growthValues[rank - 1]) : PlayerGrowthRules.Value(growthKind, rank);
        public bool IsImplementedReward => IsImplementedAbilityReward || IsImplementedWeaponReward || IsImplementedGrowthReward;
        public string DisplayName => IsImplementedGrowthReward ? growthDisplayName : category == RewardCategory.Weapon
            ? weaponDisplayName : abilityDefinition != null ? abilityDefinition.DisplayName : StableRewardId;
        public Sprite HudIcon => IsImplementedGrowthReward ? growthIcon : category == RewardCategory.Weapon
            ? weaponIcon : abilityDefinition != null ? abilityDefinition.HudIcon : null;

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
