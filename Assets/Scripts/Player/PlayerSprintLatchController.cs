using Fusion;
using UnityEngine;


/// <summary>
/// 玩家共用的網路 Sprint 鎖定與加速進度控制器。
///
/// ====================================================================
///
/// 新 Sprint 規則：
///
/// 玩家有 WASD 輸入
/// +
/// 按下 Left Shift 一次
/// ↓
/// Sprint Active = true
/// ↓
/// 放開 Shift 不取消
/// 再按一次 Shift 也不取消
/// ↓
/// 直到 WASD 回到停止區間才取消 Sprint。
///
/// ====================================================================
///
/// Sprint Active 剛成立時不會立刻使用最大 Run Input Scale，
/// 而是讓 PlayerMovement 依 SprintRampProgress：
///
/// Walk Input Scale
/// →
/// Run Input Scale
///
/// 逐步內插。
///
/// ====================================================================
///
/// Sprint 鎖定與加速進度都屬於 Gameplay State，
/// 因此必須使用 Fusion Networked Property，
/// 不能只存在普通 MonoBehaviour 欄位，
/// 否則 Prediction / Resimulation 時可能出現本機與 Host 速度不一致。
/// </summary>
[DisallowMultipleComponent]
public class PlayerSprintLatchController :
    NetworkBehaviour
{
    // =====================================================================
    #region Settings


    [Header("Sprint 鎖定設定")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "玩家按下 Left Shift 後，從 PlayerMovement 的 Walk Input Scale " +
        "逐步到達 Run Input Scale 所需的時間，單位為秒。\n\n" +
        "2 = 約兩秒後才把移動輸入倍率提升到完整跑步倍率。\n\n" +
        "0 = 按下 Shift 後立即到達完整跑步倍率。")]
    private float accelerationDuration =
        2f;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "WASD 輸入長度低於這個值時，視為玩家已經停止主動移動，" +
        "並解除 Sprint 鎖定。\n\n" +
        "建議與 PlayerStateMachine 的 Movement Input Threshold 使用相近數值。\n\n" +
        "第一輪建議 0.05。")]
    private float stopInputThreshold =
        0.05f;


    [SerializeField]
    [Tooltip(
        "開啟後，當 Grapple、Sliding、Support Pull 或職業能力正在接管移動時，" +
        "Sprint 鎖定仍會保留，但兩秒加速進度暫停。\n\n" +
        "如此不會讓玩家在被強制位移期間，暗中完成整段跑步加速。")]
    private bool pauseRampWhileMovementControlled =
        true;


    [SerializeField]
    [Tooltip(
        "開啟後顯示 Sprint 啟動、停止與加速完成訊息。\n\n" +
        "正式 Build 建議關閉。")]
    private bool debugSprint =
        false;


    #endregion


    // =====================================================================
    #region Network State


    /// <summary>
    /// 玩家是否已經按下 Shift 並進入 Sprint 鎖定。
    ///
    /// 放開或再次按下 Shift 都不會取消；
    /// 只有移動輸入回到停止區間才會重置。
    /// </summary>
    [Networked]
    public NetworkBool IsSprintActive
    {
        get;
        private set;
    }


    /// <summary>
    /// Sprint 從 Walk Scale 到 Run Scale 的網路同步進度。
    ///
    /// 0 = 剛進入 Sprint。
    /// 1 = 已完成加速。
    /// </summary>
    [Networked]
    public float SprintRampProgress
    {
        get;
        private set;
    }


    #endregion


    // =====================================================================
    #region Fusion


    public override void Spawned()
    {
        /*
         * 新 Spawn 的玩家由 State Authority 建立乾淨狀態。
         * Proxy 與 Input Authority 會經由 Fusion Snapshot / Prediction
         * 取得正確的 Networked 值。
         */
        if (Object.HasStateAuthority)
        {
            IsSprintActive =
                false;

            SprintRampProgress =
                0f;
        }
    }


    #endregion


    // =====================================================================
    #region Simulation


    /// <summary>
    /// 每個 Fusion Tick 由 Player.FixedUpdateNetwork 呼叫一次。
    /// </summary>
    /// <param name="input">本 Tick 玩家網路輸入。</param>
    /// <param name="previousButtons">上一 Tick 按鍵狀態。</param>
    /// <param name="normalMovementCanAccelerate">
    /// 普通地面／空中 WASD 是否仍可正常推進速度。
    /// false 時可以依 Inspector 設定暫停 Sprint Ramp。
    /// </param>
    public void Simulate(
        NetInput input,
        NetworkButtons previousButtons,
        bool normalMovementCanAccelerate
    )
    {
        Vector2 moveInput =
            Vector2.ClampMagnitude(
                input.Direction,
                1f
            );

        float threshold =
            Mathf.Clamp01(
                stopInputThreshold
            );

        bool hasMovementInput =
            moveInput.sqrMagnitude >
            threshold * threshold;

        /*
         * 「玩家停止」採用 WASD 輸入歸零判斷，
         * 而不是等待 KCC RealVelocity 真正衰退至零。
         *
         * 否則玩家放開 WASD 後仍可能因慣性滑動數個 Tick，
         * Sprint 狀態會比操作多殘留一段時間。
         */
        if (hasMovementInput == false)
        {
            ResetSprint(
                "Movement Input Stopped"
            );

            return;
        }

        bool sprintPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Sprint
            );

        /*
         * Shift 只負責啟動。
         *
         * 已經 Sprint Active 時：
         * - 放開 Shift：不處理。
         * - 再按 Shift：不處理。
         *
         * 所以它不是 Hold，也不是 Toggle。
         */
        if (IsSprintActive == false &&
            sprintPressed)
        {
            IsSprintActive =
                true;

            SprintRampProgress =
                0f;

            if (debugSprint)
            {
                Debug.Log(
                    "[Sprint] 已鎖定跑步；放開 Shift 不會取消。",
                    this
                );
            }
        }

        if (IsSprintActive == false)
        {
            return;
        }

        bool crouchPressed =
            input.Buttons.WasPressed(
                previousButtons,
                InputButton.Crouch
            );

        /*
         * 只有尚在累積中的 Sprint Ramp 會被蹲下打斷。
         *
         * 已經充滿的 Ramp 不在這裡預先歸零，
         * 因為正常滑鏟仍需要先讀取完整動能並確認成立，
         * 成功後才由 PlayerSlideController 正式消耗。
         *
         * 歸零後立即 return，確保按下蹲下的同一個 Tick
         * 不會又往上補回一小段進度。
         */
        bool isRampIncomplete =
            SprintRampProgress < 0.9999f;

        if (crouchPressed &&
            isRampIncomplete)
        {
            SprintRampProgress =
                0f;

            if (debugSprint)
            {
                Debug.Log(
                    "[Sprint] 加速累積期間按下蹲下；" +
                    "本次 Sprint Ramp 已歸零，必須重新累積。",
                    this
                );
            }

            return;
        }

        if (pauseRampWhileMovementControlled &&
            normalMovementCanAccelerate == false)
        {
            return;
        }

        float duration =
            Mathf.Max(
                0f,
                accelerationDuration
            );

        if (duration <= 0.0001f)
        {
            SprintRampProgress =
                1f;

            return;
        }

        bool wasComplete =
            SprintRampProgress >= 1f;

        SprintRampProgress =
            Mathf.MoveTowards(
                SprintRampProgress,
                1f,
                Runner.DeltaTime /
                duration
            );

        if (debugSprint &&
            wasComplete == false &&
            SprintRampProgress >= 1f)
        {
            Debug.Log(
                $"[Sprint] 加速完成；耗時約 {duration:F2} 秒。",
                this
            );
        }
    }


    /// <summary>
    /// 立即解除 Sprint 鎖定並清除加速進度。
    /// </summary>
    public void ForceResetSprint()
    {
        ResetSprint(
            "External Reset"
        );
    }


    /// <summary>
    /// 玩家成功進入滑鏟時，消耗目前累積的 Sprint 動能。
    ///
    /// 只把兩秒加速進度歸零，不解除 Sprint 鎖定。
    /// 因此玩家如果仍持續輸入 WASD：
    ///
    /// Sliding 期間：
    /// 加速進度依既有規則暫停。
    ///
    /// Sliding 結束後：
    /// 從 Walk Input Scale 重新累積至 Run Input Scale。
    /// </summary>
    public void ConsumeSprintMomentumForSlide()
    {
        SprintRampProgress =
            0f;

        if (debugSprint)
        {
            Debug.Log(
                "[Sprint] 滑鏟已消耗累積動能；" +
                "Sprint 鎖定保留，Ramp Progress 歸零。",
                this
            );
        }
    }


    private void ResetSprint(
        string reason
    )
    {
        bool wasActive =
            IsSprintActive;

        IsSprintActive =
            false;

        SprintRampProgress =
            0f;

        if (debugSprint &&
            wasActive)
        {
            Debug.Log(
                $"[Sprint] 已解除。\nReason：{reason}",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Validation


    private void OnValidate()
    {
        accelerationDuration =
            Mathf.Max(
                0f,
                accelerationDuration
            );

        stopInputThreshold =
            Mathf.Clamp01(
                stopInputThreshold
            );
    }


    #endregion
}
