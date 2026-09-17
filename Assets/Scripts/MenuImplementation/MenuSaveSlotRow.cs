using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Purgers.Progression;

namespace MultiClimb.Menu
{
    public sealed class MenuSaveSlotRow : MonoBehaviour
    {
        [Tooltip("點擊此列以選擇存檔。")]
        [SerializeField] private Button selectButton;

        [Tooltip("最右側的 X 按鈕；只提出刪除要求，真正刪除由上層確認視窗執行。")]
        [SerializeField] private Button deleteButton;

        [Tooltip("顯示存檔名稱。")]
        [SerializeField] private TMP_Text displayNameLabel;

        [Tooltip("顯示關卡與玩家等級。")]
        [SerializeField] private TMP_Text levelLabel;

        [Tooltip("顯示最後遊玩時間。")]
        [SerializeField] private TMP_Text lastPlayedLabel;

        [Tooltip("選中此列時顯示的物件；可留空。")]
        [SerializeField] private GameObject selectedIndicator;

        private Action<MenuSaveSlotRow> selectedCallback;
        private Action<MenuSaveSlotRow> deleteRequestedCallback;

        public GameSaveSummary Summary { get; private set; }

        private void Awake()
        {
            if (!selectButton)
                selectButton = GetComponent<Button>();

            if (selectButton)
                selectButton.onClick.AddListener(NotifySelected);

            if (deleteButton)
                deleteButton.onClick.AddListener(NotifyDeleteRequested);
        }

        private void OnDestroy()
        {
            if (selectButton)
                selectButton.onClick.RemoveListener(NotifySelected);

            if (deleteButton)
                deleteButton.onClick.RemoveListener(NotifyDeleteRequested);
        }

        public void Bind(
            GameSaveSummary summary,
            Action<MenuSaveSlotRow> onSelected,
            Action<MenuSaveSlotRow> onDeleteRequested)
        {
            Summary = summary ??
                throw new ArgumentNullException(nameof(summary));
            selectedCallback = onSelected;
            deleteRequestedCallback = onDeleteRequested;

            if (displayNameLabel)
                displayNameLabel.text = summary.DisplayName;

            if (levelLabel)
            {
                levelLabel.text =
                    $"關卡 {summary.StageLevel}　玩家 Lv.{summary.HostPlayerLevel}";
            }

            if (lastPlayedLabel)
            {
                lastPlayedLabel.text =
                    TryFormatLocalTime(summary.LastPlayedUtc);
            }

            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (selectedIndicator)
                selectedIndicator.SetActive(selected);
        }

        private void NotifySelected()
        {
            if (Summary != null)
                selectedCallback?.Invoke(this);
        }

        private void NotifyDeleteRequested()
        {
            if (Summary != null)
                deleteRequestedCallback?.Invoke(this);
        }

        private static string TryFormatLocalTime(string utcValue)
        {
            if (!DateTime.TryParse(
                    utcValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                return "時間資料無效";
            }

            return parsed.ToLocalTime().ToString(
                "yyyy/MM/dd HH:mm",
                CultureInfo.CurrentCulture);
        }
    }
}
