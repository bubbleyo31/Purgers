using UnityEngine;

namespace Purgers.Progression
{
    /// <summary>Authoring data only. GameLogic owns a detached snapshot for each Session.</summary>
    [CreateAssetMenu(menuName = "Purgers/成長/逐級經驗設定", fileName = "PlayerExperienceSettings")]
    public sealed class PlayerExperienceSettings : ScriptableObject
    {
        public const string ResourcePath = "Progression/PlayerExperienceSettings";
        public const string AssetPath = "Assets/Resources/Progression/PlayerExperienceSettings.asset";

        [Header("逐級升級經驗")]
        [Tooltip("第 1 筆為 1→2 級所需新增經驗，依序往後。每筆至少 1；超過表格後沿用最後一筆，不設滿級。Host 在 Session 初始化時複製，修改後需重新啟動 Session。")]
        [SerializeField] private int[] levelRequirements = CreateLegacyRequirements();

        public bool HasRequirements => levelRequirements != null && levelRequirements.Length > 0;

        public int[] CreateSnapshot()
        {
            if (!HasRequirements)
                return CreateLegacyRequirements();
            var snapshot = (int[])levelRequirements.Clone();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = System.Math.Max(1, snapshot[i]);
            return snapshot;
        }

        // Include the saturated row so the initial asset exactly preserves the old curve.
        public static int[] CreateLegacyRequirements()
        {
            var values = new System.Collections.Generic.List<int>();
            for (int level = 1; ; level++)
            {
                int required = PlayerExperienceRules.RequiredExperience(level);
                values.Add(required);
                if (required == int.MaxValue)
                    return values.ToArray();
            }
        }

        private void OnValidate()
        {
            if (levelRequirements == null) return;
            for (int i = 0; i < levelRequirements.Length; i++)
                levelRequirements[i] = System.Math.Max(1, levelRequirements[i]);
        }
    }
}
