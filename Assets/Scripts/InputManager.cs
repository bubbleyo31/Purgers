using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

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

    #endregion

    // =====================================================================
    #region Unity / Fusion 前置輸入更新

    /// <summary>
    /// 在 Fusion 模擬更新之前收集本地輸入。
    /// </summary>
    void IBeforeUpdate.BeforeUpdate()
    {
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

        // -------------------------------------------------------------
        // 游標未鎖定時，不接受角色操作
        // -------------------------------------------------------------

        if (Cursor.lockState !=
            CursorLockMode.Locked)
        {
            accumulatedInput =
                default;

            return;
        }

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
         * 勾索正式改為 Q。
         *
         * 這是一次性按下事件。
         *
         * 第一次按：
         * 啟動勾索。
         *
         * 再次按：
         * PlayerGrapple 判定為 ManualToggle 取消。
         */
        currentButtons.Set(
            InputButton.Grapple,
            Input.GetKeyDown(
                KeyCode.Q
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
            Input.GetMouseButton(0)
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
            Input.GetMouseButton(1)
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
                KeyCode.R
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
        // 未來技能按鍵
        // =============================================================

        /*
         * Ability1 / Ability2 現在只保留 InputButton 編號。
         *
         * 尚未確定正式鍵位之前，
         * 不在這裡綁定。
         */

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
    }

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
    }

    void INetworkRunnerCallbacks.OnSceneLoadStart(
        NetworkRunner runner)
    {
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