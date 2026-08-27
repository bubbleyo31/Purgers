using Fusion;
using UnityEngine;


/// <summary>
/// 本地玩家死亡等待重生期間使用的觀戰 Camera 控制器。
///
/// ====================================================================
///
/// 這是一個純本地 Presentation 元件：
///
/// 1. 不需要 NetworkObject。
/// 2. 不修改其他 Player 的 Input Authority。
/// 3. 不修改其他 Player 的 Camera 或 ViewModel。
/// 4. 只改變本機場景 CameraFollow 的 Target。
///
/// ====================================================================
///
/// 死亡判定方式：
///
/// GameLogic 在玩家死亡時會執行：
///
/// Runner.SetPlayerObject(localPlayer, null)
/// Runner.Despawn(oldPlayerObject)
///
/// 本控制器偵測到：
///
/// 曾經擁有本地 PlayerObject
/// 但現在暫時沒有 PlayerObject
///
/// 便開始一秒觀戰延遲。
///
/// ====================================================================
///
/// 重生判定方式：
///
/// GameLogic 生成新的 Player 後會重新：
///
/// Runner.SetPlayerObject(localPlayer, newPlayerObject)
///
/// 本控制器偵測新物件後立即退出觀戰，
/// 不需要另外發 RPC 或猜 Respawn Timer。
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class LocalPlayerSpectatorController :
    MonoBehaviour
{
    // =====================================================================
    #region Runner

    [Header("Fusion Runner")]

    [SerializeField]
    [Tooltip(
        "目前 Gameplay Session 使用的 NetworkRunner。\n\n" +
        "若場景中的 Runner 是執行期間建立，可以留空；" +
        "控制器會自動尋找目前正在執行的 NetworkRunner。")]
    private NetworkRunner runner;

    #endregion

    // =====================================================================
    #region Spectator Timing

    [Header("死亡觀戰時間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "本地 PlayerObject 因死亡消失後，等待幾秒才開始觀看其他玩家。\n\n" +
        "等待期間 CameraFollow 沒有新 Target，" +
        "會停留在玩家死亡瞬間的最後 Camera 位置。\n" +
        "目前規則使用 1 秒。")]
    private float spectatorActivationDelay =
        1f;

    #endregion

    // =====================================================================
    #region Third Person View

    [Header("第三人稱觀戰位置")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "觀戰 Camera 位於目標水平觀看方向後方的距離，單位為世界單位。" +
        "建議第一輪使用 4。")]
    private float spectatorDistance =
        4f;

    [SerializeField]
    [Tooltip(
        "觀戰 Camera 相對於目標 CamTarget 額外增加的垂直高度，" +
        "單位為世界單位。建議第一輪使用 0.75。")]
    private float spectatorHeight =
        0.75f;

    [SerializeField]
    [Tooltip(
        "觀戰 Camera 相對目標水平右方的肩膀偏移。\n\n" +
        "正值在目標右肩；負值在左肩；0 位於正後方。" +
        "建議第一輪使用 0.6。")]
    private float spectatorShoulderOffset =
        0.6f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Camera 看向目標 CamTarget 前方多少距離，" +
        "避免鏡頭永遠只盯著角色頭部。建議第一輪使用 1。")]
    private float spectatorLookAhead =
        1f;

    #endregion

    // =====================================================================
    #region Camera Smoothing

    [Header("觀戰鏡頭平滑")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "觀戰 Camera 位置追蹤速度。\n\n" +
        "使用與 Frame Rate 無關的指數平滑；0 代表不移動，" +
        "數值越高越貼緊目標。建議第一輪使用 15。")]
    private float spectatorPositionSharpness =
        15f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "觀戰 Camera 旋轉追蹤速度。\n\n" +
        "使用與 Frame Rate 無關的指數平滑；0 代表不旋轉，" +
        "數值越高越快速看向目標。建議第一輪使用 20。")]
    private float spectatorRotationSharpness =
        20f;

    #endregion

    // =====================================================================
    #region Camera Collision

    [Header("觀戰鏡頭障礙物修正")]

    [SerializeField]
    [Tooltip(
        "會阻擋第三人稱觀戰 Camera 的場景 Layer。\n\n" +
        "建議只勾選 Environment／Ground／Wall 等實體場景 Layer，" +
        "不要勾選 Player、Hitbox、Trigger 或 Ignore Raycast。")]
    private LayerMask spectatorCollisionMask;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "Camera 從目標視線位置向理想 Camera 位置檢查障礙時使用的 SphereCast 半徑。" +
        "數值越大越不容易穿牆，但過大會讓 Camera 太常被推近。" +
        "建議第一輪使用 0.2。")]
    private float spectatorCollisionRadius =
        0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "SphereCast 命中牆面後，Camera 額外向目標方向保留多少距離，" +
        "避免 Camera 剛好貼在牆面上。建議第一輪使用 0.05。")]
    private float spectatorCollisionPadding =
        0.05f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出死亡等待、進入觀戰、切換目標、沒有可觀戰目標與退出觀戰資訊。")]
    private bool debugSpectator =
        true;

    #endregion

    // =====================================================================
    #region Public State

    /// <summary>
    /// 本機目前是否已進入正式觀戰狀態。
    ///
    /// 死亡後的一秒等待期間仍為 false。
    /// </summary>
    public bool IsSpectating =>
        spectatorActive;

    /// <summary>
    /// 目前正在觀看的 PlayerRef。
    /// 沒有合法目標時為 PlayerRef.None。
    /// </summary>
    public PlayerRef SpectatedPlayer =>
        spectatedPlayer;

    #endregion

    // =====================================================================
    #region Runtime State

    /// <summary>
    /// 是否曾經成功觀察到本地 PlayerObject。
    ///
    /// 用來區分：
    ///
    /// Session 剛開始、Player 尚未 Spawn
    /// 與
    /// Player 已存在過、現在因死亡消失。
    ///
    /// 如果沒有這層判斷，剛進房間時就可能錯誤進入觀戰。
    /// </summary>
    private bool hasObservedLocalPlayer;

    /// <summary>
    /// 是否正在等待死亡後的一秒觀戰延遲。
    /// </summary>
    private bool waitingForSpectator;

    /// <summary>
    /// 本地 Presentation 使用的剩餘等待秒數。
    /// </summary>
    private float spectatorDelayRemaining;

    /// <summary>
    /// 是否已經正式進入觀戰。
    /// </summary>
    private bool spectatorActive;

    /// <summary>
    /// 目前被觀戰的 PlayerRef。
    /// </summary>
    private PlayerRef spectatedPlayer =
        PlayerRef.None;

    /// <summary>
    /// 目前被觀戰的 Player NetworkObject。
    /// </summary>
    private NetworkObject spectatedPlayerObject;

    /// <summary>
    /// 目前被觀戰玩家的核心元件。
    /// </summary>
    private Player spectatedPlayerBehaviour;

    /// <summary>
    /// 目前被觀戰玩家的移動與 Camera 資料。
    /// </summary>
    private PlayerMovement spectatedMovement;

    /// <summary>
    /// 新目標剛綁定時先直接放到正確位置，
    /// 後續幀才開始平滑，避免 Camera 從死亡位置跨越整張地圖飛過去。
    /// </summary>
    private bool hasValidSpectatorPose;

    /// <summary>
    /// 避免沒有目標時每幀重複輸出相同 Warning。
    /// </summary>
    private bool loggedNoSpectatorTarget;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        TryResolveRunner();
    }

    private void Update()
    {
        if (TryResolveRunner() == false)
        {
            ResetForMissingRunner();
            return;
        }

        bool localPlayerExists =
            TryGetValidLocalPlayerObject(
                out _
            );

        if (localPlayerExists)
        {
            hasObservedLocalPlayer =
                true;

            /*
             * 自己的新 PlayerObject 已經重生。
             *
             * PlayerLocalView.Spawned() 會把 CameraFollow
             * 指回新玩家自己的 CamTarget；
             * 這裡只清除觀戰狀態，不強行覆蓋新 Target。
             */
            if (waitingForSpectator ||
                spectatorActive)
            {
                ExitSpectatorMode();
            }

            return;
        }

        /*
         * 從未看過本地 PlayerObject，代表多半仍在初次加入流程，
         * 不能把這段空窗誤判成死亡。
         */
        if (hasObservedLocalPlayer == false)
        {
            return;
        }

        if (waitingForSpectator == false &&
            spectatorActive == false)
        {
            BeginSpectatorDelay();
        }

        if (waitingForSpectator)
        {
            spectatorDelayRemaining -=
                Time.unscaledDeltaTime;

            if (spectatorDelayRemaining <= 0f)
            {
                waitingForSpectator =
                    false;

                EnterSpectatorMode();
            }
        }

        if (spectatorActive)
        {
            if (IsCurrentSpectatorTargetValid() == false)
            {
                ClearSpectatorTarget();
                TryAcquireSpectatorTarget();
            }
        }
    }

    private void LateUpdate()
    {
        if (spectatorActive == false ||
            IsCurrentSpectatorTargetValid() == false)
        {
            return;
        }

        UpdateSpectatorCameraPose();
    }

    private void OnDisable()
    {
        ExitSpectatorMode();
    }

    #endregion

    // =====================================================================
    #region Runner / Local Player

    private bool TryResolveRunner()
    {
        if (runner != null &&
            runner.IsRunning)
        {
            return true;
        }

        runner =
            FindFirstObjectByType<NetworkRunner>();

        return
            runner != null &&
            runner.IsRunning;
    }

    private bool TryGetValidLocalPlayerObject(
        out NetworkObject localPlayerObject
    )
    {
        localPlayerObject =
            null;

        if (runner == null ||
            runner.IsRunning == false ||
            runner.LocalPlayer.IsRealPlayer == false)
        {
            return false;
        }

        if (runner.TryGetPlayerObject(
                runner.LocalPlayer,
                out localPlayerObject
            ) == false)
        {
            localPlayerObject =
                null;

            return false;
        }

        return
            localPlayerObject != null &&
            localPlayerObject.IsValid;
    }

    private void ResetForMissingRunner()
    {
        if (waitingForSpectator ||
            spectatorActive)
        {
            ExitSpectatorMode();
        }

        hasObservedLocalPlayer =
            false;
    }

    #endregion

    // =====================================================================
    #region Spectator State

    private void BeginSpectatorDelay()
    {
        waitingForSpectator =
            true;

        spectatorDelayRemaining =
            Mathf.Max(
                0f,
                spectatorActivationDelay
            );

        if (debugSpectator)
        {
            Debug.Log(
                $"[Spectator] 本地 PlayerObject 已消失，" +
                $"{spectatorDelayRemaining:F2} 秒後進入觀戰。",
                this
            );
        }

        if (spectatorDelayRemaining <= 0f)
        {
            waitingForSpectator =
                false;

            EnterSpectatorMode();
        }
    }

    private void EnterSpectatorMode()
    {
        if (spectatorActive)
        {
            return;
        }

        spectatorActive =
            true;

        hasValidSpectatorPose =
            false;

        loggedNoSpectatorTarget =
            false;

        TryAcquireSpectatorTarget();

        if (debugSpectator)
        {
            Debug.Log(
                "[Spectator] 已進入死亡觀戰模式。",
                this
            );
        }
    }

    private void ExitSpectatorMode()
    {
        waitingForSpectator =
            false;

        spectatorDelayRemaining =
            0f;

        spectatorActive =
            false;

        ClearSpectatorTarget();

        if (debugSpectator)
        {
            Debug.Log(
                "[Spectator] 本地玩家已重生或觀戰控制器已停用，退出觀戰。",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Spectator Target

    private bool TryAcquireSpectatorTarget()
    {
        if (runner == null ||
            runner.IsRunning == false)
        {
            return false;
        }

        foreach (
            PlayerRef candidatePlayer
                in runner.ActivePlayers
        )
        {
            // 絕對不能觀戰自己正在等待重生的 PlayerRef。
            if (candidatePlayer ==
                runner.LocalPlayer)
            {
                continue;
            }

            if (runner.TryGetPlayerObject(
                    candidatePlayer,
                    out NetworkObject candidateObject
                ) == false ||
                candidateObject == null ||
                candidateObject.IsValid == false)
            {
                continue;
            }

            Player candidatePlayerBehaviour =
                candidateObject.GetComponent<Player>();

            if (candidatePlayerBehaviour == null ||
                candidatePlayerBehaviour.Health == null ||
                candidatePlayerBehaviour.Health.IsAlive == false ||
                candidatePlayerBehaviour.Movement == null ||
                candidatePlayerBehaviour.Movement.CamTarget == null)
            {
                continue;
            }

            spectatedPlayer =
                candidatePlayer;

            spectatedPlayerObject =
                candidateObject;

            spectatedPlayerBehaviour =
                candidatePlayerBehaviour;

            spectatedMovement =
                candidatePlayerBehaviour.Movement;

            hasValidSpectatorPose =
                false;

            loggedNoSpectatorTarget =
                false;

            /*
             * 場景 CameraFollow 改跟隨本控制器自己的 Transform。
             *
             * 本控制器 LateUpdate 會計算第三人稱位置，
             * CameraFollow 再於自己的 LateUpdate 套用該位置。
             */
            if (CameraFollow.Singleton != null)
            {
                CameraFollow.Singleton.SetTarget(
                    transform
                );
            }

            if (debugSpectator)
            {
                Debug.Log(
                    $"[Spectator] 觀戰目標已切換。" +
                    $"\nTarget Player：{spectatedPlayer}" +
                    $"\nTarget Object：{spectatedPlayerObject.name}",
                    this
                );
            }

            return true;
        }

        if (debugSpectator &&
            loggedNoSpectatorTarget == false)
        {
            loggedNoSpectatorTarget =
                true;

            Debug.Log(
                "[Spectator] 目前沒有其他存活玩家可供觀戰，Camera 保持最後位置。",
                this
            );
        }

        return false;
    }

    private bool IsCurrentSpectatorTargetValid()
    {
        if (spectatedPlayer ==
                PlayerRef.None ||
            spectatedPlayerObject == null ||
            spectatedPlayerObject.IsValid == false ||
            spectatedPlayerBehaviour == null ||
            spectatedPlayerBehaviour.Health == null ||
            spectatedPlayerBehaviour.Health.IsAlive == false ||
            spectatedMovement == null ||
            spectatedMovement.CamTarget == null)
        {
            return false;
        }

        return true;
    }

    private void ClearSpectatorTarget()
    {
        /*
         * 只有 CameraFollow 目前仍跟隨本控制器 Transform 時才會清除。
         *
         * 若新 PlayerLocalView.Spawned() 已經先把 Target
         * 改成新玩家 CamTarget，ClearTarget(transform) 不會誤清它。
         */
        if (CameraFollow.Singleton != null)
        {
            CameraFollow.Singleton.ClearTarget(
                transform
            );
        }

        spectatedPlayer =
            PlayerRef.None;

        spectatedPlayerObject =
            null;

        spectatedPlayerBehaviour =
            null;

        spectatedMovement =
            null;

        hasValidSpectatorPose =
            false;
    }

    #endregion

    // =====================================================================
    #region Spectator Camera Pose

    private void UpdateSpectatorCameraPose()
    {
        Transform targetCameraAnchor =
            spectatedMovement.CamTarget;

        Vector3 horizontalForward =
            spectatedMovement
                .GetHorizontalLookDirection();

        if (horizontalForward.sqrMagnitude <=
            0.0001f)
        {
            horizontalForward =
                spectatedPlayerBehaviour
                    .transform.forward;

            horizontalForward.y =
                0f;
        }

        if (horizontalForward.sqrMagnitude <=
            0.0001f)
        {
            horizontalForward =
                Vector3.forward;
        }

        horizontalForward.Normalize();

        Vector3 horizontalRight =
            Vector3.Cross(
                Vector3.up,
                horizontalForward
            );

        if (horizontalRight.sqrMagnitude <=
            0.0001f)
        {
            horizontalRight =
                Vector3.right;
        }

        horizontalRight.Normalize();

        Vector3 focusPosition =
            targetCameraAnchor.position;

        Vector3 desiredPosition =
            focusPosition -
            horizontalForward *
            Mathf.Max(
                0f,
                spectatorDistance
            ) +
            Vector3.up *
            spectatorHeight +
            horizontalRight *
            spectatorShoulderOffset;

        desiredPosition =
            ResolveCameraCollision(
                focusPosition,
                desiredPosition
            );

        Vector3 lookTarget =
            focusPosition +
            horizontalForward *
            Mathf.Max(
                0f,
                spectatorLookAhead
            );

        Vector3 lookDirection =
            lookTarget -
            desiredPosition;

        Quaternion desiredRotation =
            lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up
                )
                : transform.rotation;

        if (hasValidSpectatorPose == false)
        {
            transform.SetPositionAndRotation(
                desiredPosition,
                desiredRotation
            );

            hasValidSpectatorPose =
                true;

            return;
        }

        float deltaTime =
            Time.unscaledDeltaTime;

        float positionBlend =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0f,
                    spectatorPositionSharpness
                ) *
                deltaTime
            );

        float rotationBlend =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0f,
                    spectatorRotationSharpness
                ) *
                deltaTime
            );

        transform.position =
            Vector3.Lerp(
                transform.position,
                desiredPosition,
                positionBlend
            );

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationBlend
            );
    }

    /// <summary>
    /// 防止第三人稱 Camera 穿進牆壁。
    ///
    /// 從目標視線位置向理想 Camera 位置做 SphereCast；
    /// 有障礙時把 Camera 拉到牆面前方。
    /// </summary>
    private Vector3 ResolveCameraCollision(
        Vector3 focusPosition,
        Vector3 desiredPosition
    )
    {
        Vector3 toDesiredPosition =
            desiredPosition -
            focusPosition;

        float desiredDistance =
            toDesiredPosition.magnitude;

        if (desiredDistance <= 0.0001f ||
            spectatorCollisionMask.value == 0)
        {
            return desiredPosition;
        }

        Vector3 castDirection =
            toDesiredPosition /
            desiredDistance;

        bool hitObstacle =
            Physics.SphereCast(
                focusPosition,
                Mathf.Max(
                    0.01f,
                    spectatorCollisionRadius
                ),
                castDirection,
                out RaycastHit hit,
                desiredDistance,
                spectatorCollisionMask,
                QueryTriggerInteraction.Ignore
            );

        if (hitObstacle == false)
        {
            return desiredPosition;
        }

        float correctedDistance =
            Mathf.Max(
                0f,
                hit.distance -
                Mathf.Max(
                    0f,
                    spectatorCollisionPadding
                )
            );

        return
            focusPosition +
            castDirection *
            correctedDistance;
    }

    #endregion
}