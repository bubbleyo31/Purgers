using Fusion;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


/// <summary>
/// Gameplay Session 的網路聊天核心。
///
/// ====================================================================
///
/// 本階段只處理「系統訊息」：
///
/// 玩家完成連線
/// ↓
/// 該玩家把選單中的名稱送給 State Authority
/// ↓
/// State Authority 驗證、整理名稱
/// ↓
/// 可靠 RPC 廣播「玩家名字 加入了遊戲」
/// ↓
/// 每台電腦將訊息存入自己的 Local History
/// ↓
/// LocalChatPanel 負責顯示
///
/// ====================================================================
///
/// 為什麼不直接在 GameLogic.PlayerJoined() 顯示：
///
/// GameLogic 收到的只有 PlayerRef，並不知道該玩家在主選單輸入的名稱。
/// 選單名稱目前保存在本機 PlayerPrefs，必須由該玩家主動送到伺服器。
///
/// ====================================================================
///
/// 未來擴充玩家打字聊天時：
///
/// 1. LocalChatPanel 取得輸入文字。
/// 2. 呼叫新的 RequestPlayerMessage RPC。
/// 3. State Authority 驗證長度、頻率與內容。
/// 4. 再使用 State Authority → All RPC 廣播。
///
/// 本階段刻意沒有實作輸入與玩家訊息 RPC，避免尚未驗證的文字直接上網。
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkChatSystem :
    NetworkBehaviour,
    IPlayerLeft
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 目前這台電腦所屬 Gameplay Session 的聊天系統。
    ///
    /// 只供本機 UI 尋找，不是跨網路的全域物件。
    /// </summary>
    public static NetworkChatSystem Instance
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Player Name Settings

    [Header("玩家名稱來源")]

    [SerializeField]
    [Tooltip(
        "Fusion Menu 保存玩家名稱所使用的 PlayerPrefs Key。\n\n" +
        "目前專案的 FusionMenuConnectArgs.Username 使用：\n" +
        "Photon.Menu.Username\n\n" +
        "除非之後更換登入或名稱系統，否則不要修改。")]
    private string playerNamePlayerPrefsKey =
        "Photon.Menu.Username";

    [SerializeField]
    [Min(1)]
    [Tooltip(
        "State Authority 接受的玩家名稱最大字元數。\n\n" +
        "名稱會先 Trim、移除換行與控制字元，再裁切到這個長度。\n" +
        "NetworkString 容量為 32，因此此值最高只會採用 32。")]
    private int maximumPlayerNameLength =
        20;

    [SerializeField]
    [Tooltip(
        "本機找不到 Fusion Menu 玩家名稱時使用的備援前綴。\n\n" +
        "例如 Local PlayerRef 為 3 時，會顯示：Player 3。")]
    private string fallbackPlayerNamePrefix =
        "Player";

    #endregion

    // =====================================================================
    #region Message Settings

    [Header("系統訊息格式")]

    [SerializeField]
    [Tooltip(
        "玩家加入時顯示的格式。\n\n" +
        "{0} 會被替換成經過 State Authority 驗證的玩家名稱。\n\n" +
        "預設結果：Player 1 加入了遊戲")]
    private string playerJoinedMessageFormat =
        "{0} 加入了遊戲";

    [SerializeField]
    [Min(1)]
    [Tooltip(
        "每台電腦在本機暫存的最大聊天訊息筆數。\n\n" +
        "這不是 Networked History；後加入的玩家不會補收到加入前的舊訊息。\n" +
        "未來打開聊天輸入框時，可以使用這份 History 顯示最近訊息。")]
    private int localHistoryCapacity =
        32;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出名稱註冊與系統訊息紀錄。\n" +
        "多人測試確認完成後可以關閉。")]
    private bool debugChatSystem;

    #endregion

    // =====================================================================
    #region Networked State

    /// <summary>
    /// State Authority 配發的訊息流水號。
    ///
    /// 本階段 RPC 使用 Reliable，正常不會重複；
    /// 流水號仍先保留，讓未來聊天紀錄、去重與除錯有穩定識別值。
    /// </summary>
    [Networked]
    private int NextMessageSequence
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region Local Runtime State

    /// <summary>
    /// 每台電腦自己的顯示歷史。
    ///
    /// 不使用 NetworkArray 保存，因為 Minecraft 類型的加入通知
    /// 不需要讓晚加入者補看整場遊戲先前的加入紀錄。
    /// </summary>
    private readonly List<LocalChatMessage>
        localHistory =
            new List<LocalChatMessage>(32);

    /// <summary>
    /// 僅由 State Authority 使用。
    /// 防止同一 PlayerRef 重複送出名稱後產生多次加入訊息。
    /// </summary>
    private readonly HashSet<PlayerRef>
        registeredPlayers =
            new HashSet<PlayerRef>();

    /// <summary>
    /// 本機是否已送出自己的名稱註冊要求。
    /// </summary>
    private bool localRegistrationSent;

    /// <summary>
    /// 防止異常重複 RPC 或重新綁定時顯示同一序號。
    /// </summary>
    private int lastReceivedSequence =
        -1;

    #endregion

    // =====================================================================
    #region Public API

    /// <summary>
    /// 本機目前保存的聊天訊息。
    /// UI 只能讀取，不能直接修改。
    /// </summary>
    public IReadOnlyList<LocalChatMessage>
        LocalHistory =>
            localHistory;

    /// <summary>
    /// 本機收到新訊息後觸發。
    /// LocalChatPanel 會訂閱此事件。
    /// </summary>
    public event Action<LocalChatMessage>
        MessageAdded;

    #endregion

    // =====================================================================
    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (Instance != null &&
            Instance != this)
        {
            Debug.LogError(
                $"[{nameof(NetworkChatSystem)}] " +
                "場景中存在超過一個聊天系統。" +
                "每個 Gameplay Scene 只能保留一個。",
                this
            );

            enabled =
                false;

            return;
        }

        Instance =
            this;

        localRegistrationSent =
            false;

        TryRegisterLocalPlayerName();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        ClearLocalRuntimeState();
    }

    /// <summary>
    /// State Authority 在玩家真正離開 Session 時清除註冊紀錄。
    ///
    /// 不在死亡 Despawn 時清除，因為死亡重生仍是同一個 PlayerRef，
    /// 絕對不能再次顯示「加入了遊戲」。
    /// </summary>
    public void PlayerLeft(
        PlayerRef player
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        registeredPlayers.Remove(
            player
        );
    }

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Update()
    {
        /*
         * 某些場景載入順序下，NetworkChatSystem.Spawned()
         * 可能比 Runner.LocalPlayer 準備完成更早。
         *
         * 因此在尚未送出註冊時持續輕量檢查，
         * 成功送出後就不再執行名稱處理。
         */
        if (localRegistrationSent ==
            false)
        {
            TryRegisterLocalPlayerName();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            ClearLocalRuntimeState();
        }
    }

    private void OnValidate()
    {
        maximumPlayerNameLength =
            Mathf.Clamp(
                maximumPlayerNameLength,
                1,
                32
            );

        localHistoryCapacity =
            Mathf.Max(
                1,
                localHistoryCapacity
            );
    }

    #endregion

    // =====================================================================
    #region Local Name Registration

    /// <summary>
    /// 由每台電腦替自己的 LocalPlayer 送出名稱。
    ///
    /// 這裡只送出要求；真正採用的名稱仍由 State Authority 整理。
    /// </summary>
    private void TryRegisterLocalPlayerName()
    {
        if (localRegistrationSent ||
            enabled == false ||
            Runner == null ||
            Runner.IsRunning == false ||
            Runner.LocalPlayer ==
                PlayerRef.None)
        {
            return;
        }

        string requestedName =
            PlayerPrefs.GetString(
                string.IsNullOrWhiteSpace(
                    playerNamePlayerPrefsKey
                )
                    ? "Photon.Menu.Username"
                    : playerNamePlayerPrefsKey,
                string.Empty
            );

        if (string.IsNullOrWhiteSpace(
                requestedName
            ))
        {
            requestedName =
                BuildFallbackPlayerName(
                    Runner.LocalPlayer
                );
        }

        /*
         * 先在本機裁到 NetworkString<_32> 可接受的範圍，
         * 避免 PlayerPrefs 被外部改成超長內容時，
         * 在封包序列化之前就發生容量錯誤。
         *
         * State Authority 收到後仍會再驗證一次；
         * 本機處理不是安全邊界，只是序列化保護。
         */
        string networkSafeName =
            SanitizePlayerName(
                requestedName,
                Runner.LocalPlayer
            );

        NetworkString<_32> networkName =
            networkSafeName;

        RPC_RequestRegisterPlayerName(
            networkName
        );

        /*
         * RPC 使用 Reliable。
         * 只要呼叫成功交給 Fusion，就不可每個 Frame 重送，
         * 否則 Host 端可能收到重複要求。
         */
        localRegistrationSent =
            true;
    }

    #endregion

    // =====================================================================
    #region Client To State Authority RPC

    /// <summary>
    /// 任一玩家向 State Authority 登記自己的顯示名稱。
    ///
    /// RpcInfo.Source 才是真正的發送玩家；
    /// 絕不信任客戶端自行宣稱的 PlayerRef。
    /// </summary>
    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable,
        TickAligned = false,
        InvokeLocal = true
    )]
    private void RPC_RequestRegisterPlayerName(
        NetworkString<_32> requestedName,
        RpcInfo info = default
    )
    {
        if (Object.HasStateAuthority ==
            false)
        {
            return;
        }

        PlayerRef sourcePlayer =
            info.Source;

        /*
         * State Authority 自己在本機呼叫 RPC 時，
         * 某些 Fusion 執行路徑的 RpcInfo.Source 可能為 None。
         * 這個備援只允許使用本機 Runner.LocalPlayer，
         * 不接受客戶端另外傳入可偽造的 PlayerRef。
         */
        if (sourcePlayer ==
            PlayerRef.None)
        {
            sourcePlayer =
                Runner.LocalPlayer;
        }

        if (sourcePlayer ==
                PlayerRef.None ||
            Runner.IsPlayerValid(
                sourcePlayer
            ) == false)
        {
            if (debugChatSystem)
            {
                Debug.LogWarning(
                    $"[{nameof(NetworkChatSystem)}] " +
                    "拒絕來源無效的玩家名稱註冊。",
                    this
                );
            }

            return;
        }

        if (registeredPlayers.Add(
                sourcePlayer
            ) == false)
        {
            /*
             * 同一 PlayerRef 已註冊。
             * 死亡重生、重複 RPC 或 UI 重載都不再次廣播加入訊息。
             */
            return;
        }

        string sanitizedName =
            SanitizePlayerName(
                requestedName.ToString(),
                sourcePlayer
            );

        NextMessageSequence++;

        NetworkString<_32> acceptedName =
            sanitizedName;

        RPC_BroadcastPlayerJoined(
            NextMessageSequence,
            acceptedName
        );

        if (debugChatSystem)
        {
            Debug.Log(
                $"[{nameof(NetworkChatSystem)}] " +
                "玩家名稱註冊完成。" +
                $"\nPlayer：{sourcePlayer}" +
                $"\nName：{sanitizedName}" +
                $"\nSequence：{NextMessageSequence}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region State Authority To All RPC

    /// <summary>
    /// State Authority 將玩家加入訊息可靠地送給所有目前在線玩家。
    ///
    /// RPC 不保存給未來才加入的玩家；這符合一般聊天訊息的即時性。
    /// </summary>
    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable,
        TickAligned = false,
        InvokeLocal = true
    )]
    private void RPC_BroadcastPlayerJoined(
        int sequence,
        NetworkString<_32> acceptedPlayerName
    )
    {
        if (sequence <=
            lastReceivedSequence)
        {
            return;
        }

        lastReceivedSequence =
            sequence;

        string playerName =
            acceptedPlayerName.ToString();

        string messageText =
            FormatPlayerJoinedMessage(
                playerName
            );

        AddLocalMessage(
            new LocalChatMessage(
                sequence,
                LocalChatMessageKind.PlayerJoined,
                playerName,
                messageText
            )
        );
    }

    #endregion

    // =====================================================================
    #region Local History

    private void AddLocalMessage(
        LocalChatMessage message
    )
    {
        int safeCapacity =
            Mathf.Max(
                1,
                localHistoryCapacity
            );

        while (localHistory.Count >=
               safeCapacity)
        {
            localHistory.RemoveAt(
                0
            );
        }

        localHistory.Add(
            message
        );

        MessageAdded?.Invoke(
            message
        );

        if (debugChatSystem)
        {
            Debug.Log(
                $"[{nameof(NetworkChatSystem)}] " +
                $"本機收到聊天訊息：{message.Text}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Name And Message Sanitizing

    /// <summary>
    /// 清理客戶端送來的名稱。
    ///
    /// 本階段處理：
    ///
    /// - 前後空白
    /// - 換行
    /// - Tab
    /// - Unicode Control Character
    /// - 過長名稱
    ///
    /// 未來若開放公開配對，還應再加入禁用詞與名稱政策。
    /// </summary>
    private string SanitizePlayerName(
        string requestedName,
        PlayerRef sourcePlayer
    )
    {
        if (string.IsNullOrWhiteSpace(
                requestedName
            ))
        {
            return BuildFallbackPlayerName(
                sourcePlayer
            );
        }

        string trimmedName =
            requestedName.Trim();

        StringBuilder builder =
            new StringBuilder(
                trimmedName.Length
            );

        int safeMaximumLength =
            Mathf.Clamp(
                maximumPlayerNameLength,
                1,
                32
            );

        for (int index = 0;
             index < trimmedName.Length &&
             builder.Length < safeMaximumLength;
             index++)
        {
            char character =
                trimmedName[index];

            if (char.IsControl(
                    character
                ))
            {
                continue;
            }

            builder.Append(
                character
            );
        }

        string sanitizedName =
            builder.ToString().Trim();

        if (string.IsNullOrWhiteSpace(
                sanitizedName
            ))
        {
            sanitizedName =
                BuildFallbackPlayerName(
                    sourcePlayer
                );
        }

        return sanitizedName;
    }

    private string BuildFallbackPlayerName(
        PlayerRef player
    )
    {
        string safePrefix =
            string.IsNullOrWhiteSpace(
                fallbackPlayerNamePrefix
            )
                ? "Player"
                : fallbackPlayerNamePrefix.Trim();

        string fallbackName =
            $"{safePrefix} {player.PlayerId}";

        int safeMaximumLength =
            Mathf.Clamp(
                maximumPlayerNameLength,
                1,
                32
            );

        if (fallbackName.Length >
            safeMaximumLength)
        {
            fallbackName =
                fallbackName.Substring(
                    0,
                    safeMaximumLength
                );
        }

        return fallbackName;
    }

    private string FormatPlayerJoinedMessage(
        string playerName
    )
    {
        if (string.IsNullOrWhiteSpace(
                playerJoinedMessageFormat
            ))
        {
            return
                $"{playerName} 加入了遊戲";
        }

        try
        {
            return string.Format(
                playerJoinedMessageFormat,
                playerName
            );
        }
        catch (FormatException)
        {
            Debug.LogWarning(
                $"[{nameof(NetworkChatSystem)}] " +
                "Player Joined Message Format 格式錯誤，" +
                "將使用安全備援格式。" +
                $"\n目前內容：{playerJoinedMessageFormat}",
                this
            );

            return
                $"{playerName} 加入了遊戲";
        }
    }

    #endregion

    // =====================================================================
    #region Cleanup

    private void ClearLocalRuntimeState()
    {
        if (Instance == this)
        {
            Instance =
                null;
        }

        MessageAdded =
            null;

        localHistory.Clear();
        registeredPlayers.Clear();

        localRegistrationSent =
            false;

        lastReceivedSequence =
            -1;
    }

    #endregion
}


/// <summary>
/// 本機聊天訊息種類。
///
/// PlayerMessage 目前只預留資料類型，
/// 本階段沒有玩家輸入或發送功能。
/// </summary>
public enum LocalChatMessageKind : byte
{
    System = 0,
    PlayerJoined = 1,
    PlayerMessage = 2
}


/// <summary>
/// 顯示層使用的一筆本機聊天訊息。
///
/// 這不是 Network Struct；
/// NetworkChatSystem 收到 RPC 後才在每台電腦本機建立。
/// </summary>
public struct LocalChatMessage
{
    public int Sequence
    {
        get;
        private set;
    }

    public LocalChatMessageKind Kind
    {
        get;
        private set;
    }

    public string SenderName
    {
        get;
        private set;
    }

    public string Text
    {
        get;
        private set;
    }

    public LocalChatMessage(
        int sequence,
        LocalChatMessageKind kind,
        string senderName,
        string text
    )
    {
        Sequence =
            sequence;

        Kind =
            kind;

        SenderName =
            senderName ?? string.Empty;

        Text =
            text ?? string.Empty;
    }
}
