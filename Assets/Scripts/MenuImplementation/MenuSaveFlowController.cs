using System;
using System.Collections.Generic;
using Fusion.Menu;
using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiClimb.Menu
{
    public sealed class MenuSaveFlowController : MonoBehaviour
    {
        [Header("Fusion Menu")]
        [Tooltip(
            "指定場景中的 FusionMenuUIMain。Controller 會沿用它的 " +
            "ConnectionArgs、Connection 與 Loading／Popup 畫面。")]
        [SerializeField] private FusionMenuUIMain mainMenuScreen;

        [Tooltip(
            "指定場景中的 MenuConnectionBehaviour，用來共用 Repository 並將" +
            "所選存檔交給真正建立 NetworkRunner 的 MenuConnection。")]
        [SerializeField] private MenuConnectionBehaviour menuConnection;

        [Header("Continue Overlay")]
        [Tooltip("主選單右側的繼續遊戲按鈕。Awake 會接管它的點擊事件。")]
        [SerializeField] private Button continueButton;

        [Tooltip("整個繼續遊戲選單根物件；初始可保持啟用，Awake 會關閉。")]
        [SerializeField] private GameObject overlayRoot;

        [Tooltip("動態存檔列的父物件，通常是 Scroll View/Viewport/Content。")]
        [SerializeField] private Transform saveListRoot;

        [Tooltip("含 MenuSaveSlotRow 的 UI Prefab。")]
        [SerializeField] private MenuSaveSlotRow saveSlotRowPrefab;

        [Tooltip("沒有有效存檔時顯示的文字。")]
        [SerializeField] private TMP_Text emptyStateLabel;

        [Tooltip("顯示損壞存檔或讀取錯誤；可留空。")]
        [SerializeField] private TMP_Text feedbackLabel;

        [Tooltip("選擇一列前保持不可互動的開始按鈕。")]
        [SerializeField] private Button startButton;

        [Tooltip("關閉繼續遊戲選單並返回主選單的按鈕。")]
        [SerializeField] private Button backButton;

        [Header("Delete Confirmation")]
        [Tooltip("刪除確認視窗根物件；必須位於 Continue Overlay 最上層。")]
        [SerializeField] private GameObject deleteConfirmationRoot;

        [Tooltip("顯示即將刪除的存檔名稱與不可復原警告。")]
        [SerializeField] private TMP_Text deleteConfirmationLabel;

        [Tooltip("真正執行刪除的確認按鈕。")]
        [SerializeField] private Button confirmDeleteButton;

        [Tooltip("關閉確認視窗且不刪除存檔。")]
        [SerializeField] private Button cancelDeleteButton;

        private readonly List<MenuSaveSlotRow> spawnedRows =
            new List<MenuSaveSlotRow>();

        private MenuSaveSlotRow selectedRow;
        private MenuSaveSlotRow pendingDeleteRow;
        private bool isConnecting;

        private void Awake()
        {
            BindButtonEvents();

            if (overlayRoot)
                overlayRoot.SetActive(false);

            HideDeleteConfirmation();

            SetStartInteractable(false);
            SetFeedback(string.Empty);
        }

        private void OnDestroy()
        {
            if (continueButton)
                continueButton.onClick.RemoveListener(OpenContinueMenu);

            if (startButton)
                startButton.onClick.RemoveListener(StartSelectedSave);

            if (backButton)
                backButton.onClick.RemoveListener(CloseContinueMenu);

            if (confirmDeleteButton)
                confirmDeleteButton.onClick.RemoveListener(ConfirmDelete);

            if (cancelDeleteButton)
                cancelDeleteButton.onClick.RemoveListener(HideDeleteConfirmation);
        }

        public void OpenContinueMenu()
        {
            if (isConnecting)
                return;

            if (!ValidateReferences(out string error))
            {
                Debug.LogError($"[MenuSaveFlow] {error}", this);
                SetFeedback(error);
                return;
            }

            overlayRoot.SetActive(true);
            RefreshSaveList();
        }

        public void CloseContinueMenu()
        {
            if (isConnecting)
                return;

            if (overlayRoot)
                overlayRoot.SetActive(false);

            HideDeleteConfirmation();
            SelectRow(null);
            SetFeedback(string.Empty);
        }

        public void RefreshSaveList()
        {
            if (!ValidateReferences(out string error))
            {
                Debug.LogError($"[MenuSaveFlow] {error}", this);
                SetFeedback(error);
                return;
            }

            ClearRows();
            HideDeleteConfirmation();
            SelectRow(null);

            GameSaveCatalog catalog = menuConnection.SaveRepository.List();

            for (int i = 0; i < catalog.Summaries.Count; i++)
            {
                MenuSaveSlotRow row = Instantiate(
                    saveSlotRowPrefab,
                    saveListRoot,
                    false);
                row.Bind(
                    catalog.Summaries[i],
                    SelectRow,
                    RequestDelete);
                spawnedRows.Add(row);
            }

            if (emptyStateLabel)
            {
                emptyStateLabel.gameObject.SetActive(
                    catalog.Summaries.Count == 0);
                emptyStateLabel.text = "目前沒有存檔";
            }

            SetFeedback(
                catalog.Errors.Count > 0
                    ? string.Join("\n", catalog.Errors)
                    : string.Empty);
        }

        public async void StartSelectedSave()
        {
            if (isConnecting || selectedRow?.Summary == null)
                return;

            if (!ValidateReferences(out string referenceError))
            {
                SetFeedback(referenceError);
                return;
            }

            GameSaveRepositoryResult<GameSaveData> loadResult =
                menuConnection.SaveRepository.Load(
                    selectedRow.Summary.SaveId);

            if (!loadResult.Success)
            {
                RefreshSaveList();
                SetFeedback(loadResult.Error);
                return;
            }

            if (!menuConnection.TrySelectHostSave(
                    loadResult.Value,
                    out string selectError))
            {
                SetFeedback(selectError);
                return;
            }

            isConnecting = true;
            SetStartInteractable(false);
            SetFeedback(string.Empty);

            try
            {
                IFusionMenuConnectArgs connectionArgs =
                    mainMenuScreen.ConnectionArgs;
                connectionArgs.Session = null;
                connectionArgs.Creating = true;
                connectionArgs.Region = connectionArgs.PreferredRegion;

                overlayRoot.SetActive(false);
                mainMenuScreen.Controller.Show<FusionMenuUILoading>();

                ConnectResult result =
                    await mainMenuScreen.Connection.ConnectAsync(
                        connectionArgs);

                await FusionMenuUIMain.HandleConnectionResult(
                    result,
                    mainMenuScreen.Controller);
            }
            catch (Exception exception)
            {
                menuConnection.ClearSelectedHostSave();
                Debug.LogException(exception, this);
                SetFeedback($"繼續遊戲失敗：{exception.Message}");

                if (overlayRoot)
                    overlayRoot.SetActive(true);
            }
            finally
            {
                isConnecting = false;
                SetStartInteractable(selectedRow != null);
            }
        }

        private bool ValidateReferences(out string error)
        {
            if (!mainMenuScreen)
            {
                error = "尚未指定 Main Menu Screen。";
                return false;
            }

            if (!menuConnection)
            {
                error = "尚未指定 Menu Connection Behaviour。";
                return false;
            }

            if (!overlayRoot)
            {
                error = "尚未指定 Continue Overlay Root。";
                return false;
            }

            if (!continueButton || !startButton || !backButton)
            {
                error = "繼續遊戲、開始或返回按鈕尚未完整指定。";
                return false;
            }

            if (!deleteConfirmationRoot ||
                !deleteConfirmationLabel ||
                !confirmDeleteButton ||
                !cancelDeleteButton)
            {
                error = "刪除確認視窗或按鈕尚未完整指定。";
                return false;
            }

            if (!saveListRoot)
            {
                error = "尚未指定 Save List Root。";
                return false;
            }

            if (!saveSlotRowPrefab)
            {
                error = "尚未指定 Save Slot Row Prefab。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void BindButtonEvents()
        {
            ReplaceButtonEvent(continueButton, OpenContinueMenu);
            ReplaceButtonEvent(startButton, StartSelectedSave);
            ReplaceButtonEvent(backButton, CloseContinueMenu);
            ReplaceButtonEvent(confirmDeleteButton, ConfirmDelete);
            ReplaceButtonEvent(cancelDeleteButton, HideDeleteConfirmation);
        }

        private static void ReplaceButtonEvent(
            Button button,
            UnityEngine.Events.UnityAction action)
        {
            if (!button)
                return;

            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
        }

        private void SelectRow(MenuSaveSlotRow row)
        {
            if (selectedRow)
                selectedRow.SetSelected(false);

            selectedRow = row;

            if (selectedRow)
                selectedRow.SetSelected(true);

            SetStartInteractable(selectedRow != null && !isConnecting);
        }

        private void RequestDelete(MenuSaveSlotRow row)
        {
            if (isConnecting || row?.Summary == null)
                return;

            pendingDeleteRow = row;
            deleteConfirmationLabel.text =
                $"確定要刪除「{row.Summary.DisplayName}」？\n" +
                $"關卡 {row.Summary.StageLevel}　玩家 Lv.{row.Summary.HostPlayerLevel}\n\n" +
                "刪除後無法復原。";
            deleteConfirmationRoot.SetActive(true);
            confirmDeleteButton.Select();
        }

        private void ConfirmDelete()
        {
            if (isConnecting || pendingDeleteRow?.Summary == null)
            {
                HideDeleteConfirmation();
                return;
            }

            string displayName = pendingDeleteRow.Summary.DisplayName;
            GameSaveRepositoryResult<bool> deleteResult =
                menuConnection.SaveRepository.Delete(
                    pendingDeleteRow.Summary.SaveId);

            if (!deleteResult.Success)
            {
                HideDeleteConfirmation();
                SetFeedback($"刪除「{displayName}」失敗：{deleteResult.Error}");
                return;
            }

            RefreshSaveList();
        }

        private void HideDeleteConfirmation()
        {
            pendingDeleteRow = null;

            if (deleteConfirmationRoot)
                deleteConfirmationRoot.SetActive(false);
        }

        private void ClearRows()
        {
            for (int i = spawnedRows.Count - 1; i >= 0; i--)
            {
                if (spawnedRows[i])
                    Destroy(spawnedRows[i].gameObject);
            }

            spawnedRows.Clear();
        }

        private void SetStartInteractable(bool interactable)
        {
            if (startButton)
                startButton.interactable = interactable;
        }

        private void SetFeedback(string message)
        {
            if (!feedbackLabel)
                return;

            feedbackLabel.text = message ?? string.Empty;
            feedbackLabel.gameObject.SetActive(
                !string.IsNullOrWhiteSpace(feedbackLabel.text));
        }
    }
}
