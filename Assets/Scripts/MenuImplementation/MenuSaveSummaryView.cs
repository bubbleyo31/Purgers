using System;
using System.Globalization;
using Purgers.Progression;
using TMPro;
using UnityEngine;

namespace MultiClimb.Menu
{
    /// <summary>Read-only presentation of the selection owned by MenuSaveFlowController.</summary>
    [DisallowMultipleComponent]
    public sealed class MenuSaveSummaryView : MonoBehaviour
    {
        [Header("選檔摘要（只顯示，不讀寫存檔）")]
        [Tooltip("未選取時顯示的提示物件；可留空。")]
        [SerializeField] private GameObject emptyState;
        [Tooltip("有選取時顯示的資料根物件；可留空。")]
        [SerializeField] private GameObject detailsRoot;
        [Tooltip("所選存檔的關卡數字，不含前綴；可留空。")]
        [SerializeField] private TMP_Text stageLabel;
        [Tooltip("所選存檔名稱；可留空。")]
        [SerializeField] private TMP_Text nameLabel;
        [Tooltip("Host 玩家等級；可留空。")]
        [SerializeField] private TMP_Text levelLabel;
        [Tooltip("最後遊玩時間，以本機時區顯示；可留空。")]
        [SerializeField] private TMP_Text dateLabel;
        [Tooltip("有效存檔數量，不包含損壞檔案；可留空。")]
        [SerializeField] private TMP_Text countLabel;

        public void Show(GameSaveSummary summary)
        {
            bool hasSelection = summary != null;
            if (emptyState) emptyState.SetActive(!hasSelection);
            if (detailsRoot) detailsRoot.SetActive(hasSelection);
            if (stageLabel) stageLabel.text = hasSelection ? summary.StageLevel.ToString("00") : string.Empty;
            if (nameLabel) nameLabel.text = summary?.DisplayName ?? string.Empty;
            if (levelLabel) levelLabel.text = hasSelection ? "Lv. " + summary.HostPlayerLevel : string.Empty;
            if (dateLabel)
            {
                dateLabel.text = string.Empty;
                if (hasSelection)
                    dateLabel.text = DateTime.TryParse(summary.LastPlayedUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var date)
                        ? date.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture)
                        : "時間資料無效";
            }
        }

        public void SetCount(int count)
        {
            if (countLabel) countLabel.text = Mathf.Max(0, count) + " 份存檔";
        }
    }
}
