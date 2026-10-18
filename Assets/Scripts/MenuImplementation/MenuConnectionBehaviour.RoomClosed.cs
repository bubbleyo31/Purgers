using System;
using Fusion.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiClimb.Menu
{
    public partial class MenuConnectionBehaviour
    {
        private bool showingRoomClosed;

        private void OnApplicationQuit()
        {
            if (Connection is MenuConnection connection)
                connection.SuppressSessionEndNotice();
        }

        private async void ShowRoomClosed(MenuConnection connection)
        {
            if (!this || showingRoomClosed) return;
            showingRoomClosed = true;
            RoomClosedPopupPresentation presentation = null;
            try
            {
                // 沿用同一個 Menu root 的正式 Popup 與 Controller，不建立另一套視窗。
                var main = GetComponentInChildren<FusionMenuUIMain>(true);
                if (!main) main = transform.root.GetComponentInChildren<FusionMenuUIMain>(true);
                if (!main || main.Controller == null)
                    throw new InvalidOperationException("房間關閉提示找不到既有 Fusion Menu Controller。");

                var controller = main.Controller;
                var popup = controller.Get<FusionMenuUIPopup>();
                if (!popup) throw new InvalidOperationException("房間關閉提示缺少既有 Popup。");

                presentation = new RoomClosedPopupPresentation(popup);
                var loading = controller.Get<FusionMenuUILoading>();
                if (loading && loading.gameObject.activeInHierarchy) loading.Hide();
                var gameplay = controller.Get<FusionMenuUIGameplay>();
                if (gameplay && gameplay.gameObject.activeInHierarchy) gameplay.Hide();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                await popup.OpenPopupAsync("房間已關閉", string.Empty);
                if (!this) return;
                connection.AcknowledgeRoomClosed();
                controller.Show<FusionMenuUIMain>();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                presentation?.Dispose();
                showingRoomClosed = false;
            }
        }
    }

    /// <summary>暫時設定既有 Popup 的唯一操作；關閉後完整恢復一般提示的設定。</summary>
    internal sealed class RoomClosedPopupPresentation : IDisposable
    {
        private readonly Button[] buttons;
        private readonly bool[] interactable;
        private readonly TMP_Text label;
        private readonly string originalLabel;
        private readonly RectTransform buttonRect;
        private readonly Vector2 originalPosition;
        private readonly TMP_Text message;
        private readonly TextAlignmentOptions originalAlignment;

        public RoomClosedPopupPresentation(FusionMenuUIPopup popup)
        {
            // 此路徑是既有 Fusion Popup / MenuWindowStyleBuilder 共用的資產契約。
            var primary = popup.transform.Find("PoUp/ButtonContainer/PrimaryButton");
            var button = primary ? primary.GetComponent<Button>() : null;
            label = button ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (!button || !label)
                throw new InvalidOperationException("房間關閉提示缺少 Popup 既有主要按鈕或文字。");

            buttons = popup.GetComponentsInChildren<Button>(true);
            interactable = new bool[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                interactable[i] = buttons[i].interactable;
                buttons[i].interactable = buttons[i] == button;
            }
            originalLabel = label.text;
            label.text = "回到主畫面";
            buttonRect = (RectTransform)button.transform;
            originalPosition = buttonRect.anchoredPosition;
            buttonRect.anchoredPosition = new Vector2(0, originalPosition.y);
            var textTransform = popup.transform.Find("PoUp/PopUpText");
            message = textTransform ? textTransform.GetComponent<TMP_Text>() : null;
            if (message)
            {
                originalAlignment = message.alignment;
                message.alignment = TextAlignmentOptions.Center;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i]) buttons[i].interactable = interactable[i];
            if (label) label.text = originalLabel;
            if (buttonRect) buttonRect.anchoredPosition = originalPosition;
            if (message) message.alignment = originalAlignment;
        }
    }
}
