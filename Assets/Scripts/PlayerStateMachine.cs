using Fusion;
using System;
using UnityEngine;

/// <summary>
/// 玩家目前的主要移動狀態。
///
/// 這些狀態會作為未來：
/// 1. 第一人稱手部動畫。
/// 2. 第一人稱武器動畫。
/// 3. 第三人稱角色動畫。
/// 4. 音效。
/// 5. 武器散布與後座。
/// 的共同判斷基礎。
/// </summary>
public enum PlayerMovementState : byte
{
    /// <summary>
    /// 玩家站在地面且沒有移動輸入。
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 玩家在地面上移動，但沒有按住跑步鍵。
    /// </summary>
    Walk = 1,

    /// <summary>
    /// 玩家在地面上移動，並按住跑步鍵。
    /// </summary>
    Run = 2,

    /// <summary>
    /// 玩家由地面執行第一次跳躍，
    /// 並處於主要上升階段。
    /// </summary>
    Jump = 3,

    /// <summary>
    /// 玩家在空中執行第二次跳躍，
    /// 並處於二段跳主要上升階段。
    /// </summary>
    DoubleJump = 4,

    /// <summary>
    /// 玩家正在執行勾索流程。
    ///
    /// 包含：
/// PreFire、Shooting、Attached。
    /// 不包含 Retracting。
    /// </summary>
    Grappling = 5,

    /// <summary>
    /// 玩家結束勾索後尚未碰到地面。
    ///
    /// 即使玩家正在下降，
/// 只要還沒碰地都會維持此狀態。
    /// </summary>
    GrappleAirborne = 6,

    /// <summary>
    /// 一般空中狀態。
    ///
    /// 包含：
/// 1. 跳躍到達頂點後下落。
/// 2. 從邊緣直接掉落。
/// 3. 非勾索造成的滯空。
    /// </summary>
    Airborne = 7
}

/// <summary>
/// 玩家目前由哪一種跳躍動作進入空中。
/// </summary>
public enum PlayerAirAction : byte
{
    None = 0,
    GroundJump = 1,
    DoubleJump = 2
}

/// <summary>
/// Photon Fusion 玩家狀態機。
///
/// 此元件必須掛在 Player Network Prefab 根物件上。
/// Player.cs 會在每個 Fusion Tick 將最新條件交給本狀態機。
/// </summary>
[DisallowMultipleComponent]
public class PlayerStateMachine : NetworkBehaviour
{
    // =====================================================================
    #region 狀態判定設定

    [Header("狀態判定設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("移動輸入長度小於此數值時，視為沒有移動，玩家會進入待機狀態。")]
    private float movementInputThreshold = 0.05f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("跳躍後垂直速度高於此數值時，維持 Jump 或 DoubleJump 狀態。當上升速度低於此值後，轉為一般滯空或勾索後滯空。")]
    private float upwardVelocityThreshold = 0.05f;

    #endregion

    // =====================================================================
    #region 除錯設定

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，Render 顯示狀態改變時會在 Console 印出新舊狀態。")]
    private bool logStateChanges = false;

    #endregion

    // =====================================================================
    #region Fusion 網路狀態

    /// <summary>
    /// 玩家目前主要狀態。
    ///
    /// 武器、Animator 與其他視覺系統，
    /// 之後都可以直接讀取這個屬性。
    /// </summary>
    [Networked]
    public PlayerMovementState CurrentState { get; private set; }

    /// <summary>
    /// 玩家目前的水平實際速度。
    ///
    /// 未來可用於 Animator Blend Tree、
    /// 武器晃動與腳步音效。
    /// </summary>
    [Networked]
    public float CurrentHorizontalSpeed { get; private set; }

    /// <summary>
    /// 玩家目前是否已經使用本次空中的二段跳。
    /// </summary>
    [Networked]
    public NetworkBool HasUsedDoubleJump { get; private set; }

    /// <summary>
    /// 玩家目前由哪一種跳躍動作進入空中。
    /// </summary>
    [Networked]
    private PlayerAirAction ActiveAirAction { get; set; }

    /// <summary>
    /// 玩家是否處於勾索結束後、尚未碰地的階段。
    /// </summary>
    [Networked]
    private NetworkBool GrappleAirbornePending { get; set; }

    #endregion

    // =====================================================================
    #region Render 狀態事件

    /// <summary>
    /// Render 顯示狀態真正改變時觸發。
    ///
    /// 適合用於：
/// 1. 本地第一人稱 Animator。
/// 2. 武器 Animator。
/// 3. 音效。
///
/// 不建議在這個事件中修改網路模擬或造成傷害。
    /// </summary>
    public event Action<
        PlayerMovementState,
        PlayerMovementState
    > RenderedStateChanged;

    private PlayerMovementState lastRenderedState;
    private bool hasRenderedState;

    #endregion

    // =====================================================================
    #region 公開狀態

    /// <summary>
    /// 玩家目前是否仍可使用二段跳。
    /// </summary>
    public bool CanDoubleJump =>
        HasUsedDoubleJump == false;

    /// <summary>
    /// 玩家目前是否屬於任一空中狀態。
    /// </summary>
    public bool IsAirborneState =>
        CurrentState == PlayerMovementState.Jump ||
        CurrentState == PlayerMovementState.DoubleJump ||
        CurrentState == PlayerMovementState.GrappleAirborne ||
        CurrentState == PlayerMovementState.Airborne;

    #endregion

    // =====================================================================
    #region 狀態通知

    /// <summary>
    /// 通知狀態機：普通 Grapple 已經真正 Attached，
    /// 並且接下來會拉動玩家本人。
    ///
    /// 每一次成功的玩家移動 Grapple 都重新提供一次二段跳。
    ///
    /// 不應由以下流程呼叫：
    ///
    /// Attack Enemy Mark
    /// Tank Gather Anchor
    /// Support Enemy / Player Tether
    /// Grapple 發射失敗
    /// Attached 前取消。
    /// </summary>
    public void NotifyPlayerPullGrappleAttached()
    {
        HasUsedDoubleJump =
            false;
    }

    /// <summary>
    /// 通知狀態機：玩家執行了地面跳躍。
    /// </summary>
    public void NotifyGroundJump()
    {
        ActiveAirAction =
            PlayerAirAction.GroundJump;

        HasUsedDoubleJump =
            false;

        SetState(
            PlayerMovementState.Jump
        );
    }

    /// <summary>
    /// 通知狀態機：玩家執行了二段跳。
    /// </summary>
    public void NotifyDoubleJump()
    {
        HasUsedDoubleJump =
            true;

        /*
         * 如果這次二段跳發生在 Grapple 結束後滯空期間，
         * Gameplay 身分仍然必須是 GrappleAirborne。
         *
         * 這樣 Tank Air Dash、Support Aerial Ability、
         * Attack Focus 等依賴 GrappleAirborne 的系統
         * 不會因為玩家按一次跳躍就失去合法狀態。
         */
        if (GrappleAirbornePending)
        {
            ActiveAirAction =
                PlayerAirAction.None;

            SetState(
                PlayerMovementState.GrappleAirborne
            );

            return;
        }

        /*
         * 普通地面跳躍後的二段跳沒有 GrappleAirbornePending，
         * 因此仍然維持原本的 DoubleJump 狀態。
         */
        ActiveAirAction =
            PlayerAirAction.DoubleJump;

        SetState(
            PlayerMovementState.DoubleJump
        );
    }

    /// <summary>
    /// 通知狀態機：勾索已經結束並開始收繩。
    /// </summary>
    /// <param name="isGrounded">
    /// 結束勾索當下是否在地面。
    /// </param>
    public void NotifyGrappleEnded(
        bool isGrounded
    )
    {
        if (isGrounded == false)
        {
            GrappleAirbornePending =
                true;
        }
    }

    #endregion

    // =====================================================================
    #region 狀態更新

    /// <summary>
    /// 每個 Fusion Tick 更新玩家目前狀態。
    /// </summary>
    public void TickState(
        bool isGrounded,
        Vector2 moveInput,
        bool sprintHeld,
        bool grappleControlActive,
        bool groundJumpedThisTick,
        bool doubleJumpedThisTick,
        float verticalVelocity,
        float horizontalSpeed
    )
    {
        CurrentHorizontalSpeed =
            horizontalSpeed;

        // -------------------------------------------------------------
        // 本 Tick 執行跳躍
        // -------------------------------------------------------------

        /*
         * KCC 在 Player 後面才會完成該 Tick 的移動，
         * 因此剛呼叫 Jump 的同一 Tick，
         * IsGrounded 可能仍保留上一 Tick 的值。
         *
         * 必須讓 jumpedThisTick 擁有更高優先權。
         */
        if (groundJumpedThisTick)
        {
            SetState(
                PlayerMovementState.Jump
            );

            return;
        }

        if (doubleJumpedThisTick)
        {
            /*
             * NotifyDoubleJump() 已經依 GrappleAirbornePending
             * 決定這是不是 Grapple 系二段跳。
             *
             * TickState 不可以在同一 Tick 又無條件覆寫回 DoubleJump。
             */
            SetState(
                GrappleAirbornePending
                    ? PlayerMovementState.GrappleAirborne
                    : PlayerMovementState.DoubleJump
            );

            return;
        }

        // -------------------------------------------------------------
        // 使用勾索中
        // -------------------------------------------------------------

        if (grappleControlActive)
        {
            SetState(
                PlayerMovementState.Grappling
            );

            return;
        }

        // -------------------------------------------------------------
        // 地面狀態
        // -------------------------------------------------------------

        if (isGrounded)
        {
            /*
             * 一旦碰地，重置所有空中相關狀態。
             */
            HasUsedDoubleJump =
                false;

            ActiveAirAction =
                PlayerAirAction.None;

            GrappleAirbornePending =
                false;

            bool hasMovementInput =
                moveInput.sqrMagnitude >
                movementInputThreshold *
                movementInputThreshold;

            if (hasMovementInput == false)
            {
                SetState(
                    PlayerMovementState.Idle
                );
            }
            else if (sprintHeld)
            {
                SetState(
                    PlayerMovementState.Run
                );
            }
            else
            {
                SetState(
                    PlayerMovementState.Walk
                );
            }

            return;
        }

        // -------------------------------------------------------------
        // 跳躍上升狀態
        // -------------------------------------------------------------

        if (ActiveAirAction ==
            PlayerAirAction.GroundJump)
        {
            if (verticalVelocity >
                upwardVelocityThreshold)
            {
                SetState(
                    PlayerMovementState.Jump
                );

                return;
            }

            /*
             * 已經離開主要上升階段。
             */
            ActiveAirAction =
                PlayerAirAction.None;
        }

        if (ActiveAirAction ==
            PlayerAirAction.DoubleJump)
        {
            if (verticalVelocity >
                upwardVelocityThreshold)
            {
                SetState(
                    PlayerMovementState.DoubleJump
                );

                return;
            }

            ActiveAirAction =
                PlayerAirAction.None;
        }

        // -------------------------------------------------------------
        // 勾索後滯空
        // -------------------------------------------------------------

        if (GrappleAirbornePending)
        {
            SetState(
                PlayerMovementState.GrappleAirborne
            );

            return;
        }

        // -------------------------------------------------------------
        // 一般滯空
        // -------------------------------------------------------------

        SetState(
            PlayerMovementState.Airborne
        );
    }

    /// <summary>
    /// 修改玩家目前狀態。
    /// </summary>
    private void SetState(
        PlayerMovementState newState
    )
    {
        if (CurrentState == newState)
            return;

        CurrentState =
            newState;
    }

    #endregion

    // =====================================================================
    #region Render 事件

    public override void Render()
    {
        if (hasRenderedState == false)
        {
            hasRenderedState =
                true;

            lastRenderedState =
                CurrentState;

            return;
        }

        if (lastRenderedState ==
            CurrentState)
        {
            return;
        }

        PlayerMovementState previousState =
            lastRenderedState;

        PlayerMovementState newState =
            CurrentState;

        lastRenderedState =
            newState;

        if (logStateChanges)
        {
            Debug.Log(
                $"[玩家狀態]" +
                $"\n舊狀態：{previousState}" +
                $"\n新狀態：{newState}",
                this
            );
        }

        RenderedStateChanged?.Invoke(
            previousState,
            newState
        );
    }

    #endregion
}