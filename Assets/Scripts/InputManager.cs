using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.Transition;
using Purgers.Progression;

/// <summary>
/// Photon Fusion 玩家輸入管理器。
///
/// 負責：
/// 1. 收集 WASD。
/// 2. 收集滑鼠視角。
/// 3. 跳躍。
/// 4. 跑步。
/// 5. Q 勾索。
/// 6. 左鍵射擊。
/// 7. 右鍵瞄準。
/// 8. R 換彈。
/// 9. 將輸入提交給 Fusion。
///
/// 這裡只負責「玩家按了什麼」。
///
/// 不負責：
/// 武器是否能射擊。
/// 彈匣是否有子彈。
/// ADS 是否完成。
/// 專注技能是否能使用。
/// 勾索是否有充能。
///
/// 那些都應由各自遊戲系統判斷。
/// </summary>
public class InputManager :
    SimulationBehaviour,
    IBeforeUpdate,
    INetworkRunnerCallbacks
{
    // =====================================================================
    #region 輸入累積資料

    /// <summary>
    /// Unity Frame 期間累積的玩家輸入。
    ///
    /// Fusion OnInput 不一定和 Unity Update 一對一，
    /// 所以短暫輸入不能只在 OnInput 當下讀。
    /// </summary>
    private NetInput accumulatedInput;

    /// <summary>
    /// 上一次 OnInput 已經提交資料後，
    /// 下一個 BeforeUpdate 是否應該重置輸入。
    /// </summary>
    private bool resetInput;

    [Header("戰鬥按鍵")]
    [SerializeField] private KeyCode grappleKey = KeyCode.Q;
    [SerializeField] private KeyCode grappleFocusKey = KeyCode.E;
    [SerializeField] private KeyCode reloadKey = KeyCode.R;
    [SerializeField] private KeyCode rewardHoldKey = KeyCode.LeftAlt;

    public KeyCode GrappleKey => grappleKey;
    public KeyCode GrappleFocusKey => grappleFocusKey;
    public KeyCode ReloadKey => reloadKey;
    public KeyCode RewardHoldKey => rewardHoldKey;
    public string RewardHoldKeyLabel => rewardHoldKey == KeyCode.LeftAlt ||
        rewardHoldKey == KeyCode.RightAlt ? "ALT" :
        rewardHoldKey.ToString().ToUpperInvariant();
    public bool IsRewardHoldKeyPressed => Input.GetKey(rewardHoldKey) ||
        (rewardHoldKey == KeyCode.LeftAlt && Input.GetKey(KeyCode.RightAlt));

    [SerializeField, Min(0f)]
    [Tooltip("安全屋與關卡載入完成後的黑幕淡入秒數；使用 Unscaled Time。")]
    private float sceneFadeDuration = 1.5f;
    private LocalSceneTransition sceneTransition;
    private NetworkRunner rewardRunner;
    private LocalPlayerRewardHUD registeredRewardHud;
    private bool pendingHostRewardChoice;
    private int pendingHostRewardRevision;
    private int pendingHostRewardIndex;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool pendingHostDebugLevelUp;
#endif

    public bool IsRewardSelectionActive { get; private set; }

    // Unity mouse indices: left=0, right=1, middle=2. This survives HUD dismissal
    // and ClearPendingInput; only the physical release rearms a consumed button.
    private int rewardMouseButtonsBlockedUntilRelease;

    public void RegisterRewardHud(LocalPlayerRewardHUD hud)
    {
        if (hud != null)
            registeredRewardHud = hud;
    }

    public void UnregisterRewardHud(LocalPlayerRewardHUD hud)
    {
        if (registeredRewardHud == hud)
        {
            registeredRewardHud = null;
            IsRewardSelectionActive = false;
        }
    }

    public bool TryConsumeHostRewardChoice(out int revision, out int index)
    {
        revision = pendingHostRewardRevision;
        index = pendingHostRewardIndex;
        bool hasChoice = pendingHostRewardChoice;
        pendingHostRewardChoice = false;
        return hasChoice;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool TryConsumeHostDebugLevelUp()
    {
        bool requested = pendingHostDebugLevelUp;
        pendingHostDebugLevelUp = false;
        return requested;
    }
#endif

    private void Awake()
    {
        rewardRunner = GetComponent<NetworkRunner>();
        LocalPlayerControl.Locks.Changed += ClearPendingInput;
    }

    private void ClearPendingInput()
    {
        accumulatedInput = default;
        resetInput = false;
        IsRewardSelectionActive = false;
        pendingHostRewardChoice = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        pendingHostDebugLevelUp = false;
#endif
    }

    private void OnDestroy()
    {
        LocalPlayerControl.Locks.Changed -= ClearPendingInput;
        if (sceneTransition != null)
            sceneTransition.Cancel();
    }

    #endregion

    // =====================================================================
    #region Unity / Fusion 前置輸入更新

    /// <summary>
    /// 在 Fusion 模擬更新之前收集本地輸入。
    /// </summary>
    void IBeforeUpdate.BeforeUpdate()
    {
        int heldMouseButtons = (Input.GetMouseButton(0) ? 1 : 0) |
            (Input.GetMouseButton(1) ? 2 : 0) |
            (Input.GetMouseButton(2) ? 4 : 0);
        rewardMouseButtonsBlockedUntilRelease &= heldMouseButtons;
        if (LocalPlayerControl.AllInputBlocked)
        {
            ClearPendingInput();
            return;
        }
        // -------------------------------------------------------------
        // 清除上一輪已提交的資料
        // -------------------------------------------------------------

        if (resetInput)
        {
            resetInput = false;

            accumulatedInput =
                default;
        }

        // -------------------------------------------------------------
        // 游標切換
        // -------------------------------------------------------------

        bool toggleCursor =
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Escape);

        if (toggleCursor)
        {
            if (Cursor.lockState ==
                CursorLockMode.Locked)
            {
                Cursor.lockState =
                    CursorLockMode.None;

                Cursor.visible =
                    true;
            }
            else
            {
                Cursor.lockState =
                    CursorLockMode.Locked;

                Cursor.visible =
                    false;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        RefreshDebugLevelUp();
#endif

        // -------------------------------------------------------------
        // 游標未鎖定時，不接受角色操作
        // -------------------------------------------------------------

        if (Cursor.lockState !=
            CursorLockMode.Locked)
        {
            accumulatedInput =
                default;

            IsRewardSelectionActive = false;

            return;
        }

        RefreshRewardSelection();

        NetworkButtons currentButtons =
            default;

        // =============================================================
        // 滑鼠視角
        // =============================================================

        float mouseX =
            Input.GetAxisRaw(
                "Mouse X"
            );

        float mouseY =
            Input.GetAxisRaw(
                "Mouse Y"
            );

        /*
         * 和目前 PlayerMovement 使用方式一致：
         *
         * X = Pitch
         * Y = Yaw
         */
        Vector2 lookRotationDelta =
            new Vector2(
                -mouseY,
                mouseX
            );

        /*
         * 使用 += 累積滑鼠輸入。
         *
         * 不直接覆蓋，
         * 否則 Fusion OnInput 沒有剛好在這個 Unity Frame
         * 執行時可能漏掉滑鼠 Delta。
         */
        accumulatedInput.LookDelta +=
            lookRotationDelta;

        // =============================================================
        // WASD
        // =============================================================

        Vector2 moveDirection =
            Vector2.zero;

        if (Input.GetKey(KeyCode.W))
        {
            moveDirection +=
                Vector2.up;
        }

        if (Input.GetKey(KeyCode.S))
        {
            moveDirection +=
                Vector2.down;
        }

        if (Input.GetKey(KeyCode.A))
        {
            moveDirection +=
                Vector2.left;
        }

        if (Input.GetKey(KeyCode.D))
        {
            moveDirection +=
                Vector2.right;
        }

        accumulatedInput.Direction +=
            moveDirection;

        // =============================================================
        // 跳躍
        // =============================================================

        /*
         * Space 是一次性按下事件。
         *
         * 使用 GetKeyDown，
         * 再透過 accumulatedInput 保存到 OnInput。
         */
        currentButtons.Set(
            InputButton.Jump,
            Input.GetKeyDown(
                KeyCode.Space
            )
        );

        // =============================================================
        // 勾索
        // =============================================================

        /*
        * 鈎索按鍵必須保存 Q 目前是否持續按住。
        *
        * 不能使用 Input.GetKeyDown：
        * GetKeyDown 只有按下當幀是 true，
        * 下一個 Fusion Tick 就會錯誤判定玩家已經放開 Q。
        *
        * 使用 GetKey 後：
        *
        * Player.cs 的 WasPressed
        * → 仍然只在 Q 從 false 變成 true 時觸發一次。
        *
        * Player.cs 的 IsSet
        * → Q 持續按住期間，每個 Tick 都會維持 true。
        *
        * 因此同一份 NetInput 可以同時支援：
        *
        * Toggle 模式：
        * 第一次按下啟動，第二次按下釋放。
        *
        * Hold 模式：
        * 按住啟動並維持，放開釋放。
        */
        currentButtons.Set(
            InputButton.Grapple,
            Input.GetKey(
                grappleKey
            )
        );

        // =============================================================
        // 跑步
        // =============================================================

        /*
         * Sprint 是持續型輸入。
         */
        currentButtons.Set(
            InputButton.Sprint,
            Input.GetKey(
                KeyCode.LeftShift
            )
        );

        // =============================================================
        // 蹲下／滑鏟
        // =============================================================

        /*
        * Left Control 是持續型輸入。
        *
        * 不能使用 GetKeyDown，因為：
        * 1. 玩家必須按住按鍵才能維持蹲下。
        * 2. 滑鏟結束後若按鍵仍按住，玩家應保持蹲姿。
        * 3. 頭上有障礙時，即使放開按鍵也會由 PlayerSlideController
        *    阻止碰撞體強行站起。
        */
        currentButtons.Set(
            InputButton.Crouch,
            Input.GetKey(
                KeyCode.LeftControl
            )
        );

        // =============================================================
        // 武器射擊
        // =============================================================

        /*
         * 左鍵是持續型輸入。
         *
         * 不使用 GetMouseButtonDown，
         * 因為攻擊職業使用的是自動步槍。
         *
         * 玩家持續按住左鍵時：
        /// Fire = true
        ///
        /// AttackRifle 之後會根據 Fire Rate
        /// 自己決定哪些 Tick 真正開火。
         */
        currentButtons.Set(
            InputButton.Fire,
            !IsRewardSelectionActive && Input.GetMouseButton(0)
        );

        // =============================================================
        // 武器瞄準
        // =============================================================

        /*
         * 右鍵現在完全從勾索移除。
         *
         * 右鍵只代表：
        /// Aim Held
        ///
        /// 普通情況：
        /// → ADS
        ///
        /// GrappleAirborne：
        /// → 攻擊職業專注技能判斷
         */
        currentButtons.Set(
            InputButton.Aim,
            !IsRewardSelectionActive && Input.GetMouseButton(1)
        );

        // =============================================================
        // 換彈
        // =============================================================

        /*
         * R 是一次性輸入。
         */
        currentButtons.Set(
            InputButton.Reload,
            Input.GetKeyDown(
                reloadKey
            )
        );

        /*
        * F：
        * 職業 Quick Action。
        *
        * 這裡使用 isPressed，
        * 真正「剛按下」判斷會由
        * Fusion NetworkButtons.WasPressed()
        * 在 Simulation 裡完成。
        */
        currentButtons.Set(
            InputButton.QuickAction,
            Input.GetKeyDown(
                KeyCode.F
            )
        );

        // =============================================================
        // 鈎索專注能力
        // =============================================================

        /*
         * E 持續狀態交由能力 Runtime 判斷按下或按住；
         * Ability2 暫時保留。選獎勵時不送出能力輸入。
         */
        currentButtons.Set(
            InputButton.Ability1,
            !IsRewardSelectionActive && Input.GetKey(grappleFocusKey)
        );

        // =============================================================
        // 累積按鍵
        // =============================================================

        /*
         * 將本次收集到的按鍵與之前還沒提交的按鍵合併。
         *
         * 對 Jump、Grapple、Reload 這類很短的輸入尤其重要。
         */
        accumulatedInput.Buttons =
            new NetworkButtons(
                accumulatedInput.Buttons.Bits |
                currentButtons.Bits
            );

        FilterRewardMouseInput(IsRewardSelectionActive, heldMouseButtons);
    }

    private void FilterRewardMouseInput(bool selectionActive, int heldMouseButtons)
    {
        rewardMouseButtonsBlockedUntilRelease &= heldMouseButtons;
        if (selectionActive)
            rewardMouseButtonsBlockedUntilRelease |= heldMouseButtons;

        // Filter after accumulation so pre-ALT buffered combat cannot leak either.
        if (selectionActive || (rewardMouseButtonsBlockedUntilRelease & 1) != 0)
            accumulatedInput.Buttons.Set(InputButton.Fire, false);
        if (selectionActive || (rewardMouseButtonsBlockedUntilRelease & 2) != 0)
            accumulatedInput.Buttons.Set(InputButton.Aim, false);
        if (selectionActive)
            accumulatedInput.Buttons.Set(InputButton.Ability1, false);
    }

    private void RefreshRewardSelection()
    {
        IsRewardSelectionActive = false;
        if (rewardRunner == null || !rewardRunner.IsRunning ||
            !rewardRunner.LocalPlayer.IsRealPlayer ||
            registeredRewardHud == null ||
            !registeredRewardHud.CanPresentRewardSelection)
            return;

        if (!rewardRunner.TryGetPlayerObject(rewardRunner.LocalPlayer,
                out NetworkObject localPlayerObject) ||
            localPlayerObject == null || !localPlayerObject.IsValid)
            return;
        Player localPlayer = localPlayerObject.GetComponent<Player>();
        if (localPlayer == null || localPlayer.Health == null ||
            !localPlayer.Health.IsAlive)
            return;

        GameLogic logic = GameLogic.GetPrimaryForRunner(rewardRunner);
        if (logic == null ||
            !logic.TryGetPlayerExperience(rewardRunner.LocalPlayer,
                out PlayerExperienceState experience) ||
            !logic.TryGetPlayerRewardDraft(rewardRunner.LocalPlayer,
                out PlayerRewardNetworkState draft))
            return;

        bool altHeld = IsRewardHoldKeyPressed;
        IsRewardSelectionActive = RewardSelectionInputRules.IsSelectionActive(
            altHeld, experience.PendingRewards, draft.ChoiceCount);
        if (!IsRewardSelectionActive)
            return;

        int choiceIndex = RewardSelectionInputRules.GetChoiceIndex(
            draft.ChoiceCount,
            Input.GetMouseButtonDown(0),
            Input.GetMouseButtonDown(2),
            Input.GetMouseButtonDown(1));
        if (choiceIndex < 0)
            return;

        if (logic.Object.HasStateAuthority)
        {
            pendingHostRewardRevision = draft.DraftRevision;
            pendingHostRewardIndex = choiceIndex;
            pendingHostRewardChoice = true;
        }
        else
            logic.RPC_RequestRewardChoice(draft.DraftRevision, choiceIndex);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void RefreshDebugLevelUp()
    {
        if (!Input.GetKeyDown(KeyCode.F8) ||
            !DevelopmentToolsPolicy.IsEnabled ||
            rewardRunner == null || !rewardRunner.IsRunning ||
            !rewardRunner.LocalPlayer.IsRealPlayer ||
            registeredRewardHud == null ||
            !registeredRewardHud.CanPresentRewardSelection)
            return;

        GameLogic logic = GameLogic.GetPrimaryForRunner(rewardRunner);
        if (logic == null || logic.Object == null || !logic.Object.IsValid)
            return;

        if (logic.Object.HasStateAuthority)
            pendingHostDebugLevelUp = true;
        else
            logic.RPC_RequestDebugLevelUp();
    }
#endif

    #endregion

    // =====================================================================
    #region Fusion OnInput

    /// <summary>
    /// Fusion 要求取得這個玩家的輸入時呼叫。
    /// </summary>
    void INetworkRunnerCallbacks.OnInput(
        NetworkRunner runner,
        NetworkInput input
    )
    {
        PlayerControlLocks.Filter(ref accumulatedInput, LocalPlayerControl.Locks.Mask);
        // -------------------------------------------------------------
        // 移動輸入正規化
        // -------------------------------------------------------------

        /*
         * 防止 W+D 斜向移動輸入長度大於 1。
         */
        if (accumulatedInput.Direction.sqrMagnitude >
            1f)
        {
            accumulatedInput.Direction.Normalize();
        }

        // -------------------------------------------------------------
        // 提交給 Fusion
        // -------------------------------------------------------------

        input.Set(
            accumulatedInput
        );

        /*
         * 告訴下一個 BeforeUpdate：
         * 本輪資料已經送出。
         */
        resetInput =
            true;

        // -------------------------------------------------------------
        // 滑鼠 Delta 特殊處理
        // -------------------------------------------------------------

        /*
         * LookDelta 必須在 OnInput 後立刻清掉。
         *
         * 因為如果同一個 Unity Frame 中，
         * Fusion 執行兩個 Simulation Tick，
         * 我們不希望完全相同的滑鼠移動被套用兩次。
         */
        accumulatedInput.LookDelta =
            default;
    }

    #endregion

    // =====================================================================
    #region 玩家加入

    void INetworkRunnerCallbacks.OnPlayerJoined(
        NetworkRunner runner,
        PlayerRef player
    )
    {
        /*
         * 只有本地玩家加入時鎖定滑鼠。
         */
        if (player !=
            runner.LocalPlayer)
        {
            return;
        }

        Cursor.lockState =
            CursorLockMode.Locked;

        Cursor.visible =
            false;
    }

    #endregion

    // =====================================================================
    #region 關閉連線

    void INetworkRunnerCallbacks.OnShutdown(
        NetworkRunner runner,
        ShutdownReason shutdownReason
    )
    {
        if (sceneTransition != null)
            sceneTransition.Cancel();
        Cursor.lockState =
            CursorLockMode.None;

        Cursor.visible =
            true;

        accumulatedInput =
            default;

        resetInput =
            false;
    }

    #endregion

    // =====================================================================
    #region Fusion 未使用 Callback

    void INetworkRunnerCallbacks.OnConnectedToServer(
        NetworkRunner runner)
    {
    }

    void INetworkRunnerCallbacks.OnConnectFailed(
        NetworkRunner runner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason)
    {
    }

    void INetworkRunnerCallbacks.OnConnectRequest(
        NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token)
    {
    }

    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(
        NetworkRunner runner,
        Dictionary<string, object> data)
    {
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(
        NetworkRunner runner,
        NetDisconnectReason reason)
    {
    }

    void INetworkRunnerCallbacks.OnHostMigration(
        NetworkRunner runner,
        HostMigrationToken hostMigrationToken)
    {
    }

    void INetworkRunnerCallbacks.OnInputMissing(
        NetworkRunner runner,
        PlayerRef player,
        NetworkInput input)
    {
    }

    void INetworkRunnerCallbacks.OnObjectEnterAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    {
    }

    void INetworkRunnerCallbacks.OnObjectExitAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    {
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(
        NetworkRunner runner,
        PlayerRef player)
    {
    }

    void INetworkRunnerCallbacks.OnReliableDataProgress(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        float progress)
    {
    }

    /*
     * 依照你目前專案的 Fusion 版本，
     * OnReliableDataReceived 使用 ArraySegment<byte>。
     *
     * 不使用之前曾造成編譯錯誤的 ReadOnlySpan<byte> 版本。
     */
    void INetworkRunnerCallbacks.OnReliableDataReceived(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        ArraySegment<byte> data)
    {
    }

    void INetworkRunnerCallbacks.OnSceneLoadDone(
        NetworkRunner runner)
    {
        sceneTransition = LocalSceneTransition.Ensure(runner, sceneFadeDuration);
        sceneTransition.SceneLoadDone();
    }

    void INetworkRunnerCallbacks.OnSceneLoadStart(
        NetworkRunner runner)
    {
        ClearPendingInput();
        sceneTransition = LocalSceneTransition.Ensure(runner, sceneFadeDuration);
        sceneTransition.BeginLoad();
    }

    void INetworkRunnerCallbacks.OnSessionListUpdated(
        NetworkRunner runner,
        List<SessionInfo> sessionList)
    {
    }

#pragma warning disable CS0618
    void INetworkRunnerCallbacks.OnUserSimulationMessage(
        NetworkRunner runner,
        SimulationMessagePtr message)
    {
    }
#pragma warning restore CS0618

    #endregion
}
