using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// 將 NetworkChatSystem 的本機訊息顯示在 Gameplay HUD。
///
/// ====================================================================
///
/// 這支腳本只負責 Presentation：
///
/// - 尋找 NetworkChatSystem。
/// - 讀取它已保存的 Local History。
/// - 收到新訊息時更新 TMP。
/// - 顯示指定秒數後淡出。
/// - 將 ScrollRect 捲到最下面。
///
/// 它不發 RPC，也不決定玩家名稱。
///
/// ====================================================================
///
/// 請把控制器掛在永遠保持 Active 的 ChatController 物件，
/// 再把可隱藏的 ChatVisualRoot 指向其子物件。
///
/// 不可把 ChatVisualRoot 指回掛有本腳本的同一物件，
/// 否則淡出後 SetActive(false) 會讓控制器停止工作，
/// 下一則訊息就無法重新顯示。
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalChatPanel :
    MonoBehaviour
{
    // =====================================================================
    #region UI References

    [Header("聊天欄 UI")]

    [SerializeField]
    [Tooltip(
        "包含聊天背景與 Messages Text 的純視覺 Root。\n\n" +
        "請指定本腳本物件底下的子物件，不可指定掛有本腳本的同一物件。\n" +
        "沒有訊息或淡出完成後，這個物件會被隱藏。")]
    private GameObject chatVisualRoot;

    [SerializeField]
    [Tooltip(
        "顯示聊天紀錄的 TextMeshProUGUI。\n\n" +
        "建議設定：\n" +
        "Alignment = Bottom Left\n" +
        "Word Wrapping = On\n" +
        "Overflow = Overflow\n" +
        "Raycast Target = Off")]
    private TMP_Text messagesText;

    [SerializeField]
    [Tooltip(
        "控制整個 Chat Visual Root 透明度的 CanvasGroup。\n\n" +
        "指定後會平滑淡出；留空時仍可運作，但顯示時間結束後會直接隱藏。")]
    private CanvasGroup chatCanvasGroup;

    [SerializeField]
    [Tooltip(
        "聊天內容使用的 ScrollRect。\n\n" +
        "指定後，每次加入新訊息會自動捲到最下方。\n" +
        "如果目前只使用單一 TMP、不需要 ScrollRect，可以留空。")]
    private ScrollRect messagesScrollRect;

    #endregion

    // =====================================================================
    #region Reserved Input UI

    [Header("預留玩家輸入區（本階段停用）")]

    [SerializeField]
    [Tooltip(
        "預留給未來玩家打字聊天的 Input Area Root。\n\n" +
        "本階段會強制保持隱藏，不會接收鍵盤輸入。\n" +
        "可以先在 Canvas 排好版，之後實作輸入功能時再啟用。")]
    private GameObject reservedInputRoot;

    [SerializeField]
    [Tooltip(
        "預留給未來聊天輸入的 TMP_InputField。\n\n" +
        "本階段只會把它設為 Read Only 並關閉互動，完全不會送出訊息。\n" +
        "尚未建立輸入框時可以留空。")]
    private TMP_InputField reservedInputField;

    #endregion

    // =====================================================================
    #region Display Settings

    [Header("訊息顯示設定")]

    [SerializeField]
    [Min(1)]
    [Tooltip(
        "聊天欄同時顯示的最大訊息行數。\n\n" +
        "NetworkChatSystem 可以保存更多歷史，" +
        "但關閉輸入狀態下只顯示最後幾則，避免佔滿畫面。")]
    private int maximumVisibleMessages =
        8;

    [SerializeField]
    [Tooltip(
        "開啟後，聊天訊息顯示一段時間會像 Minecraft 一樣淡出。\n\n" +
        "關閉後，只要至少收到過一則訊息，聊天欄就會一直顯示，方便測試。")]
    private bool fadeWhenInactive =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "收到最後一則訊息後，維持完全顯示幾秒才開始淡出。\n\n" +
        "每一則新訊息都會重新計時。")]
    private float messageVisibleDuration =
        8f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "聊天欄從完全顯示淡到透明所需秒數。\n" +
        "使用 Unscaled Time，不受遊戲暫停或 Time Scale 影響。")]
    private float fadeDuration =
        0.35f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出聊天系統綁定與 UI 更新紀錄。\n" +
        "功能確認完成後可以關閉。")]
    private bool debugChatPanel;

    #endregion

    // =====================================================================
    #region Runtime State

    /// <summary>
    /// 目前 UI 綁定的網路聊天核心。
    /// 場景卸載或重新連線時會自動解除並重新尋找。
    /// </summary>
    private NetworkChatSystem boundChatSystem;

    /// <summary>
    /// UI 目前持有的本機訊息快照。
    ///
    /// 不直接保存 NetworkChatSystem.LocalHistory 的參考，
    /// 避免重連或 Chat System Despawn 後使用失效資料。
    /// </summary>
    private readonly List<LocalChatMessage>
        displayedMessages =
            new List<LocalChatMessage>(16);

    private readonly StringBuilder
        textBuilder =
            new StringBuilder(512);

    /// <summary>
    /// 最後一則訊息抵達的 Unscaled Time。
    /// </summary>
    private float lastMessageTime;

    /// <summary>
    /// 避免對 Chat Visual Root 重複呼叫 SetActive。
    /// </summary>
    private bool visualRootVisible;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        ValidateReferences();
        ConfigureText();
        DisableReservedInput();

        displayedMessages.Clear();

        if (messagesText != null)
        {
            messagesText.text =
                string.Empty;
        }

        SetVisualRootVisible(
            false
        );
    }

    private void OnEnable()
    {
        TryBindChatSystem();
    }

    private void Update()
    {
        if (boundChatSystem == null ||
            boundChatSystem !=
                NetworkChatSystem.Instance)
        {
            UnbindChatSystem();
            TryBindChatSystem();
        }

        UpdateFade();
    }

    private void OnDisable()
    {
        UnbindChatSystem();
    }

    private void OnDestroy()
    {
        UnbindChatSystem();
    }

    private void OnValidate()
    {
        maximumVisibleMessages =
            Mathf.Max(
                1,
                maximumVisibleMessages
            );

        messageVisibleDuration =
            Mathf.Max(
                0f,
                messageVisibleDuration
            );

        fadeDuration =
            Mathf.Max(
                0.01f,
                fadeDuration
            );

        if (chatVisualRoot == gameObject)
        {
            Debug.LogError(
                $"[{nameof(LocalChatPanel)}] " +
                "Chat Visual Root 不可指定掛有控制器的同一個 GameObject。" +
                "請建立一個子物件作為純視覺 Root。",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Chat System Binding

    private void TryBindChatSystem()
    {
        NetworkChatSystem chatSystem =
            NetworkChatSystem.Instance;

        if (chatSystem == null ||
            chatSystem == boundChatSystem)
        {
            return;
        }

        boundChatSystem =
            chatSystem;

        boundChatSystem.MessageAdded +=
            HandleMessageAdded;

        /*
         * UI 可能晚於網路 RPC 建立。
         * 綁定時先完整複製 Local History，
         * 確保不會漏掉 Canvas 尚未準備時收到的訊息。
         */
        displayedMessages.Clear();

        IReadOnlyList<LocalChatMessage> history =
            boundChatSystem.LocalHistory;

        for (int index = 0;
             index < history.Count;
             index++)
        {
            displayedMessages.Add(
                history[index]
            );
        }

        RefreshText();

        if (displayedMessages.Count > 0)
        {
            ShowForNewMessage();
        }

        if (debugChatPanel)
        {
            Debug.Log(
                $"[{nameof(LocalChatPanel)}] " +
                "已綁定 NetworkChatSystem。" +
                $"\nHistory Count：{history.Count}",
                this
            );
        }
    }

    private void UnbindChatSystem()
    {
        if (boundChatSystem != null)
        {
            boundChatSystem.MessageAdded -=
                HandleMessageAdded;
        }

        boundChatSystem =
            null;
    }

    #endregion

    // =====================================================================
    #region Message Display

    private void HandleMessageAdded(
        LocalChatMessage message
    )
    {
        displayedMessages.Add(
            message
        );

        RefreshText();
        ShowForNewMessage();
    }

    private void RefreshText()
    {
        if (messagesText == null)
        {
            return;
        }

        textBuilder.Clear();

        int visibleCount =
            Mathf.Max(
                1,
                maximumVisibleMessages
            );

        int firstVisibleIndex =
            Mathf.Max(
                0,
                displayedMessages.Count -
                visibleCount
            );

        for (int index = firstVisibleIndex;
             index < displayedMessages.Count;
             index++)
        {
            if (textBuilder.Length > 0)
            {
                textBuilder.AppendLine();
            }

            textBuilder.Append(
                displayedMessages[index].Text
            );
        }

        messagesText.text =
            textBuilder.ToString();

        messagesText.ForceMeshUpdate();

        if (messagesScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();

            messagesScrollRect.verticalNormalizedPosition =
                0f;
        }

        if (debugChatPanel)
        {
            Debug.Log(
                $"[{nameof(LocalChatPanel)}] " +
                "聊天欄文字已更新。" +
                $"\nDisplayed Count：{displayedMessages.Count}",
                this
            );
        }
    }

    private void ShowForNewMessage()
    {
        lastMessageTime =
            Time.unscaledTime;

        SetVisualRootVisible(
            true
        );

        if (chatCanvasGroup != null)
        {
            chatCanvasGroup.alpha =
                1f;
        }
    }

    #endregion

    // =====================================================================
    #region Fade

    private void UpdateFade()
    {
        if (visualRootVisible == false ||
            displayedMessages.Count == 0)
        {
            return;
        }

        if (fadeWhenInactive ==
            false)
        {
            if (chatCanvasGroup != null)
            {
                chatCanvasGroup.alpha =
                    1f;
            }

            return;
        }

        float timeSinceLastMessage =
            Time.unscaledTime -
            lastMessageTime;

        if (timeSinceLastMessage <=
            messageVisibleDuration)
        {
            return;
        }

        float safeFadeDuration =
            Mathf.Max(
                0.01f,
                fadeDuration
            );

        float fadeProgress =
            (timeSinceLastMessage -
             messageVisibleDuration) /
            safeFadeDuration;

        float alpha =
            1f -
            Mathf.Clamp01(
                fadeProgress
            );

        if (chatCanvasGroup != null)
        {
            chatCanvasGroup.alpha =
                alpha;
        }

        if (alpha <= 0f)
        {
            SetVisualRootVisible(
                false
            );
        }
    }

    #endregion

    // =====================================================================
    #region Setup

    private void ConfigureText()
    {
        if (messagesText == null)
        {
            return;
        }

        /*
         * 玩家名稱來自外部文字。
         * 本階段不需要 Rich Text，因此直接關閉解析，
         * 避免名稱內的 <color> 等標籤影響整個聊天欄。
         */
        messagesText.richText =
            false;

        messagesText.raycastTarget =
            false;

        messagesText.enableWordWrapping =
            true;
    }

    private void DisableReservedInput()
    {
        if (reservedInputField != null)
        {
            reservedInputField.DeactivateInputField();

            reservedInputField.readOnly =
                true;

            reservedInputField.interactable =
                false;
        }

        if (reservedInputRoot != null)
        {
            reservedInputRoot.SetActive(
                false
            );
        }
    }

    private void ValidateReferences()
    {
        if (messagesText == null)
        {
            Debug.LogError(
                $"[{nameof(LocalChatPanel)}] " +
                "尚未指定 Messages Text。",
                this
            );
        }

        if (chatVisualRoot == null)
        {
            Debug.LogError(
                $"[{nameof(LocalChatPanel)}] " +
                "尚未指定 Chat Visual Root。",
                this
            );
        }
        else if (chatVisualRoot ==
                 gameObject)
        {
            Debug.LogError(
                $"[{nameof(LocalChatPanel)}] " +
                "Chat Visual Root 不可等於控制器本身。" +
                "請指定一個可單獨隱藏的子物件。",
                this
            );
        }

        if (chatCanvasGroup == null &&
            chatVisualRoot != null)
        {
            chatCanvasGroup =
                chatVisualRoot.GetComponent<
                    CanvasGroup
                >();
        }
    }

    private void SetVisualRootVisible(
        bool visible
    )
    {
        if (chatVisualRoot == null ||
            chatVisualRoot == gameObject)
        {
            return;
        }

        if (visualRootVisible == visible &&
            chatVisualRoot.activeSelf == visible)
        {
            return;
        }

        visualRootVisible =
            visible;

        chatVisualRoot.SetActive(
            visible
        );

        if (visible &&
            chatCanvasGroup != null)
        {
            chatCanvasGroup.alpha =
                1f;
        }
    }

    #endregion
}
