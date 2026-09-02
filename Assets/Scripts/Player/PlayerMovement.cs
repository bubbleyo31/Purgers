using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

/// <summary>
/// 玩家 Advanced KCC 移動模組。
///
/// 負責：
/// 1. 視角 Look。
/// 2. WASD。
/// 3. Walk。
/// 4. Sprint。
/// 5. Jump。
/// 6. Double Jump。
/// 7. 提供玩家速度資料。
/// 8. 更新第一人稱 CamTarget Pitch。
///
/// 不負責：
/// 1. 勾索。
/// 2. 勾索 Momentum。
/// 3. CameraRig。
/// 4. LineRenderer。
/// 5. ViewModel。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KCC))]
[RequireComponent(typeof(PlayerStateMachine))]
public class PlayerMovement : MonoBehaviour
{
    // =====================================================================
    #region 每 Tick 結果

    /// <summary>
    /// PlayerMovement 本 Tick 計算出的結果。
    ///
    /// Player 根控制器會把這些資訊交給
    /// PlayerStateMachine。
    /// </summary>
    public struct FrameResult
    {
        /// <summary>
        /// 正規化後的 WASD 輸入。
        /// </summary>
        public Vector2 RawMoveInput;
        
        /// <summary>
        /// 本 Tick 是否成功執行地面跳躍。
        /// </summary>
        public bool GroundJumped;

        /// <summary>
        /// 本 Tick 是否成功執行二段跳。
        /// </summary>
        public bool DoubleJumped;

        /// <summary>
        /// 本 Tick 是否處於已鎖定的 Sprint 狀態。
        ///
        /// 欄位名稱保留 SprintHeld 是為了避免本階段擴大相依修改；
        /// 實際值由 PlayerSprintLatchController 提供，
        /// 不再等於實體 Shift Held。
        /// </summary>
        public bool SprintHeld;
    }

    #endregion

    // =====================================================================
    #region 核心引用

    [Header("核心引用")]

    [SerializeField]
    [Tooltip("玩家使用的 Photon Fusion Advanced KCC。若留空會自動取得。")]
    private KCC kcc;

    [SerializeField]
    [Tooltip("玩家狀態機。用來判斷二段跳是否仍可使用。若留空會自動取得。")]
    private PlayerStateMachine stateMachine;

    [SerializeField]
    [Tooltip("第一人稱攝影機跟隨目標。通常放在玩家眼睛高度。此 Transform 只負責 Pitch，上下看。")]
    private Transform camTarget;

    #endregion

    // =====================================================================
    #region 視角設定

    [Header("視角設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("滑鼠視角靈敏度。此數值乘上 NetInput.LookDelta。")]
    private float lookSensitivity = 2f;

    #endregion

    // =====================================================================
    #region 地面移動設定

    [Header("地面移動設定")]

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("一般走路時傳給 KCC 的移動輸入倍率。")]
    private float walkInputScale = 0.55f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "Sprint 加速完成後傳給 KCC 的完整跑步輸入倍率。\n\n" +
        "玩家剛按下 Shift 時不會立即使用此值，" +
        "而是由 PlayerSprintLatchController 的 Ramp Progress " +
        "從 Walk Input Scale 逐步內插到此倍率。")]
    private float runInputScale = 1f;

    #endregion

    // =====================================================================
    #region 跳躍設定

    [Header("跳躍設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家第一次從地面跳躍時加入 KCC 的向上衝量。")]
    private float jumpImpulse = 6f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家在空中執行二段跳時加入 KCC 的向上衝量。")]
    private float doubleJumpImpulse = 6f;

    [SerializeField]
    [Tooltip("開啟後，即使玩家正在勾索 PreFire、Shooting 或 Attached，也可以使用跳躍與二段跳。")]
    private bool allowJumpDuringGrapple = false;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// Advanced KCC。
    /// </summary>
    public KCC KCC =>
        kcc;

    /// <summary>
    /// 第一人稱 Camera Target。
    /// </summary>
    public Transform CamTarget =>
        camTarget;

    /// <summary>
    /// 玩家目前是否接觸地面。
    /// </summary>
    public bool IsGrounded =>
        kcc != null &&
        kcc.Data.IsGrounded;

    /// <summary>
    /// 玩家目前世界實際速度。
    /// </summary>
    public float CurrentWorldSpeed
    {
        get
        {
            if (kcc == null)
                return 0f;

            return kcc.Data.RealVelocity.magnitude;
        }
    }

    /// <summary>
    /// 玩家目前水平實際速度。
    /// </summary>
    public float HorizontalSpeed
    {
        get
        {
            if (kcc == null)
                return 0f;

            Vector3 horizontal =
                Vector3.ProjectOnPlane(
                    kcc.Data.RealVelocity,
                    Vector3.up
                );

            return horizontal.magnitude;
        }
    }

    /// <summary>
    /// 玩家目前垂直實際速度。
    /// </summary>
    public float VerticalVelocity =>
        kcc != null
            ? kcc.Data.RealVelocity.y
            : 0f;

    #endregion

    // =====================================================================
    #region Unity 生命週期

    private void Awake()
    {
        if (kcc == null)
        {
            kcc =
                GetComponent<KCC>();
        }

        if (stateMachine == null)
        {
            stateMachine =
                GetComponent<PlayerStateMachine>();
        }
    }

    #endregion

    // =====================================================================
    #region Fusion 模擬入口

    /// <summary>
    /// 每個 Fusion Tick 由 Player 根控制器呼叫。
    /// </summary>
    /// <param name="input">
    /// 本 Tick 網路輸入。
    /// </param>
    /// <param name="previousButtons">
    /// 上一 Tick NetworkButtons。
    /// </param>
    /// <param name="grappleControlActive">
    /// 勾索目前是否正在控制玩家。
    /// </param>
    /// <param name="externalMovementInfluence">
    /// 其他移動系統希望保留多少 WASD 控制。
    ///
    /// 例如勾索 Momentum：
    /// 0 = 不允許 WASD
    /// 1 = 完整 WASD
    /// </param>
    public FrameResult Simulate(
        NetInput input,
        NetworkButtons previousButtons,
        bool sprintActive,
        float sprintRampProgress,
        bool grappleControlActive,
        float externalMovementInfluence
    )
    {
        FrameResult result =
            default;

        // -------------------------------------------------------------
        // Look
        // -------------------------------------------------------------

        kcc.AddLookRotation(
            input.LookDelta *
            lookSensitivity
        );

        // -------------------------------------------------------------
        // WASD
        // -------------------------------------------------------------

        Vector2 rawMoveInput =
            input.Direction;

        if (rawMoveInput.sqrMagnitude > 1f)
        {
            rawMoveInput.Normalize();
        }

        /*
        * Sprint 是否成立與目前兩秒加速進度，
        * 已經由具備 Networked State 的 PlayerSprintLatchController 決定。
        *
        * PlayerMovement 只負責把進度換算成最終 KCC Input Scale，
        * 不再直接把 Shift Held 當作跑步狀態。
        */
        float clampedSprintProgress =
            sprintActive
                ? Mathf.Clamp01(
                    sprintRampProgress
                )
                : 0f;

        float inputScale =
            sprintActive
                ? Mathf.Lerp(
                    walkInputScale,
                    runInputScale,
                    clampedSprintProgress
                )
                : walkInputScale;

        /*
         * 外部移動系統可以降低普通 WASD 影響。
         *
         * PlayerMovement 不需要知道是：
        /// 勾索
        /// 衝刺
        /// 擊退
        /// 還是未來的其他能力。
         */
        inputScale *=
            Mathf.Clamp01(
                externalMovementInfluence
            );

        Vector3 localDirection =
            new Vector3(
                rawMoveInput.x,
                0f,
                rawMoveInput.y
            ) *
            inputScale;

        Vector3 worldDirection =
            kcc.Data.TransformRotation *
            localDirection;

        kcc.SetInputDirection(
            worldDirection
        );

        // -------------------------------------------------------------
        // Jump
        // -------------------------------------------------------------

        bool jumpPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Jump
            );

        bool canProcessJump =
            allowJumpDuringGrapple ||
            grappleControlActive == false;

        bool groundJumped =
            false;

        bool doubleJumped =
            false;

        if (jumpPressed &&
            canProcessJump)
        {
            if (kcc.Data.IsGrounded)
            {
                kcc.Jump(
                    Vector3.up *
                    jumpImpulse
                );

                stateMachine.NotifyGroundJump();

                groundJumped =
                    true;
            }
            else if (stateMachine.CanDoubleJump)
            {
                kcc.Jump(
                    Vector3.up *
                    doubleJumpImpulse
                );

                stateMachine.NotifyDoubleJump();

                doubleJumped =
                    true;
            }
        }

        result.RawMoveInput =
            rawMoveInput;

        result.SprintHeld =
            sprintActive;

        result.GroundJumped =
            groundJumped;

        result.DoubleJumped =
            doubleJumped;

        return result;
    }

    #endregion

    // =====================================================================
    #region 第一人稱視覺

    /// <summary>
    /// 更新 CamTarget 的 Pitch。
    ///
    /// 重要：
    /// 不可從 FixedUpdateNetwork 或 Fusion Render 呼叫。
    ///
    /// 目前由 PlayerLocalView.LateUpdate 呼叫，
    /// 避免 KCC Simulation 與 Camera Visual
    /// 在同一幀互相覆寫 Transform。
    /// </summary>
    public void UpdateCameraTargetVisual()
    {
        if (camTarget == null ||
            kcc == null)
        {
            return;
        }

        camTarget.localRotation =
            Quaternion.Euler(
                kcc.Data.LookPitch,
                0f,
                0f
            );
    }

    #endregion

    // =====================================================================
    #region 瞄準資料

    /// <summary>
    /// 根據 KCC 的 Pitch 與 Yaw
    /// 取得目前完整世界瞄準方向。
    /// </summary>
    public Vector3 GetAimDirection()
    {
        if (kcc == null)
            return transform.forward;

        Quaternion pitchRotation =
            Quaternion.Euler(
                kcc.Data.LookPitch,
                0f,
                0f
            );

        Vector3 localDirection =
            pitchRotation *
            Vector3.forward;

        Vector3 worldDirection =
            kcc.Data.TransformRotation *
            localDirection;

        return worldDirection.normalized;
    }

    /// <summary>
    /// 取得目前只包含 Yaw 的水平觀看方向。
    ///
    /// 不包含上下看 Pitch。
    /// </summary>
    public Vector3 GetHorizontalLookDirection()
    {
        Vector3 lookDirection =
            kcc.Data.TransformRotation *
            Vector3.forward;

        lookDirection.y =
            0f;

        if (lookDirection.sqrMagnitude <=
            0.0001f)
        {
            lookDirection =
                transform.forward;

            lookDirection.y =
                0f;
        }

        return lookDirection.normalized;
    }

    #endregion

    // =====================================================================
    #region 外部 Look Rotation

    /// <summary>
    /// 讓其他 Gameplay 系統對 KCC Look Rotation
    /// 加入額外的 Pitch / Yaw。
    ///
    /// 目前主要提供給武器後座力。
    ///
    /// 之後也可以用於：
    /// 1. 爆炸震動。
    /// 2. 特殊技能。
    /// 3. 強制視角偏移。
    ///
    /// 注意：
    /// 這是 Gameplay Look Rotation，
    /// 不是單純 Camera Visual Shake。
    /// 因此會真正改變下一發射擊的瞄準方向。
    /// </summary>
    /// <param name="pitchDelta">
    /// Pitch 增量。
    ///
    /// 依目前 KCC 方向，
    /// 負值通常代表向上抬。
    /// </param>
    /// <param name="yawDelta">
    /// Yaw 增量。
    /// 正負代表左右方向。
    /// </param>
    public void AddLookRotationImpulse(
        float pitchDelta,
        float yawDelta
    )
    {
        if (kcc == null)
            return;

        kcc.AddLookRotation(
            pitchDelta,
            yawDelta
        );
    }
    #endregion

    /// <summary>
    /// 當一般地形鈎索正式進入拉動階段時，
    /// 嘗試使用玩家既有的 Jump Impulse，先把角色從地面抬起。
    ///
    /// 這不是玩家主動按下 Jump：
    ///
    /// 1. 不通知 PlayerLocomotionStateMachine 一般地面跳躍。
    /// 2. 不消耗二段跳。
    /// 3. 不把狀態切換成一般 Jumping。
    /// 4. 玩家仍維持 Grappling 狀態。
    ///
    /// 只有角色確實站在地面時才會施加一次。
    /// 如果命中鈎索時玩家本來就在空中，便不再疊加額外向上速度，
    /// 避免空中鈎索可以重複灌入 Jump Impulse。
    /// </summary>
    /// <returns>
    /// true：本次成功施加附著起跳衝量。
    /// false：KCC 不存在、玩家不在地面，或 Jump Impulse 無效。
    /// </returns>
    public bool TryApplyGrappleAttachJumpImpulse()
    {
        if (kcc == null)
        {
            return false;
        }

        // 只讓仍站在地面的玩家起跳。
        // 已在空中的玩家不重複獲得向上衝量。
        if (!kcc.Data.IsGrounded)
        {
            return false;
        }

        // 直接重用 PlayerMovement 既有、已由 Inspector 設定的 jumpImpulse。
        // 不另外建立第二份鈎索跳躍數值，避免兩個欄位日後失去同步。
        float attachJumpImpulse = Mathf.Max(0f, jumpImpulse);

        if (attachJumpImpulse <= 0f)
        {
            return false;
        }
        
        /*
        * 這不是一般玩家 Jump State，而是 Grapple Attached 的離地衝量。
        *
        * 若使用 kcc.Jump()，JumpImpulse 仍要等 EnvironmentProcessor 階段才套用；
        * 而該 Tick 的 FixedData 可能仍是 Grounded，導致新的向上衝量立刻受到
        * Dynamic Ground Friction 大幅衰減。
        *
        * 這裡直接把既有 jumpImpulse 寫入 DynamicVelocity Y，
        * 再由 PlayerGrappleKCCProtectionProcessor 保護該 Grounded Tick。
        *
        * 不修改 Transform、不使用 Rigidbody，也不通知普通 Jump State。
        */
        Vector3 attachVelocity =
            kcc.Data.DynamicVelocity;

        attachVelocity.y =
            Mathf.Max(
                attachVelocity.y,
                attachJumpImpulse
            );

        kcc.SetDynamicVelocity(
            attachVelocity
        );

        return true;
    }
}