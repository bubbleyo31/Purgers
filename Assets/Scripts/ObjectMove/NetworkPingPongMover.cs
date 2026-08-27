using Fusion;
using UnityEngine;

/// <summary>
/// Photon Fusion 網路來回移動物件。
///
/// 用途：
/// 1. 測試勾索命中點是否會跟隨移動物件。
/// 2. 測試 LineRenderer 終點是否正確更新。
/// 3. 測試玩家被移動目標拉動時的表現。
///
/// 移動規則：
/// 物件會從生成位置移動到指定偏移位置，
/// 抵達後停留一段時間，再往起點移動。
///
/// 網路規則：
/// 只有 State Authority 會真正計算並修改位置，
/// 其他玩家透過 NetworkTransform 接收同步結果。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class NetworkPingPongMover : NetworkBehaviour
{
    // =====================================================================
    #region 移動設定

    [Header("移動設定")]

    [SerializeField]
    [Tooltip("物件從起點移動到終點的本地偏移量。例如設定 X = 6，代表物件會沿自己的右方移動 6 公尺。")]
    private Vector3 moveOffset = new Vector3(6f, 0f, 0f);

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("物件每秒移動的距離。數值越大，移動速度越快。")]
    private float moveSpeed = 2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("物件抵達起點或終點後，停留多少秒才開始反方向移動。設為 0 代表不停止。")]
    private float waitDuration = 0.5f;

    [SerializeField]
    [Tooltip("開啟後，物件生成時會先從起點往終點移動。關閉則會先停在起點，並把第一次移動方向設為回到起點，通常建議保持開啟。")]
    private bool startTowardEnd = true;

    #endregion

    // =====================================================================
    #region 除錯顯示

    [Header("除錯顯示")]

    [SerializeField]
    [Tooltip("開啟後，選取物件時會在 Scene 視窗畫出起點、終點與移動路線。")]
    private bool drawMovementGizmos = true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Scene 視窗中起點與終點球體的大小。")]
    private float gizmoPointRadius = 0.2f;

    #endregion

    // =====================================================================
    #region 執行狀態

    /// <summary>
    /// 物件生成時所在的世界座標。
    /// </summary>
    private Vector3 startPoint;

    /// <summary>
    /// 根據起點與 Move Offset 計算出的世界終點。
    /// </summary>
    private Vector3 endPoint;

    /// <summary>
    /// true：目前往終點移動。
    /// false：目前往起點移動。
    /// </summary>
    private bool movingTowardEnd;

    /// <summary>
    /// 抵達端點後的停留倒數。
    /// </summary>
    private float waitTimer;

    #endregion

    // =====================================================================
    #region Fusion 生命週期

    /// <summary>
    /// NetworkObject 正式生成後初始化移動範圍。
    /// </summary>
    public override void Spawned()
    {
        /*
         * 保存 NetworkObject 生成時的位置。
         *
         * 起點不使用 Awake 記錄，
         * 因為 NetworkObject 可能由 Runner
         * 生成在 Prefab 原始位置以外的地方。
         */
        startPoint =
            transform.position;

        /*
         * Move Offset 使用物件本地方向。
         *
         * 例如：
         * Move Offset = (6, 0, 0)
         *
         * 代表沿物件自己的右方移動 6 公尺，
         * 即使物件有旋轉，也會按照自身方向移動。
         */
        Vector3 worldOffset =
            transform.rotation *
            moveOffset;

        endPoint =
            startPoint +
            worldOffset;

        movingTowardEnd =
            startTowardEnd;

        waitTimer =
            0f;
    }

    /// <summary>
    /// Photon Fusion 固定網路 Tick。
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        /*
         * 只有 State Authority 可以修改正式網路位置。
         *
         * Host 模式下通常由 Host 擁有 State Authority。
         * Client 只透過 NetworkTransform 接收位置。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        // -------------------------------------------------------------
        // 端點停留
        // -------------------------------------------------------------

        if (waitTimer > 0f)
        {
            waitTimer -=
                Runner.DeltaTime;

            if (waitTimer < 0f)
            {
                waitTimer = 0f;
            }

            return;
        }

        // -------------------------------------------------------------
        // 決定目前目的地
        // -------------------------------------------------------------

        Vector3 targetPoint =
            movingTowardEnd
                ? endPoint
                : startPoint;

        // -------------------------------------------------------------
        // 朝目的地移動
        // -------------------------------------------------------------

        Vector3 newPosition =
            Vector3.MoveTowards(
                transform.position,
                targetPoint,
                moveSpeed *
                Runner.DeltaTime
            );

        /*
         * 直接修改 NetworkTransform 所在物件的 Transform。
         *
         * NetworkTransform 會把 State Authority
         * 的位置同步給其他玩家。
         */
        transform.position =
            newPosition;

        // -------------------------------------------------------------
        // 是否抵達端點
        // -------------------------------------------------------------

        float remainingDistanceSquared =
            (targetPoint - newPosition).sqrMagnitude;

        if (remainingDistanceSquared <= 0.000001f)
        {
            /*
             * 確保位置精確停在端點，
             * 避免浮點數誤差累積。
             */
            transform.position =
                targetPoint;

            // 反轉下一次移動方向。
            movingTowardEnd =
                !movingTowardEnd;

            // 開始端點停留倒數。
            waitTimer =
                waitDuration;
        }
    }

    #endregion

    // =====================================================================
    #region Scene 除錯顯示

    private void OnDrawGizmosSelected()
    {
        if (drawMovementGizmos == false)
            return;

        /*
         * 遊戲執行中使用 Spawned 計算出的正式起點。
         *
         * 編輯模式則暫時使用目前 Transform 位置，
         * 讓尚未進入遊戲時也能預覽移動範圍。
         */
        Vector3 previewStartPoint =
            Application.isPlaying
                ? startPoint
                : transform.position;

        Vector3 previewWorldOffset =
            transform.rotation *
            moveOffset;

        Vector3 previewEndPoint =
            Application.isPlaying
                ? endPoint
                : previewStartPoint +
                  previewWorldOffset;

        // 畫出起點。
        Gizmos.color =
            Color.green;

        Gizmos.DrawSphere(
            previewStartPoint,
            gizmoPointRadius
        );

        // 畫出終點。
        Gizmos.color =
            Color.red;

        Gizmos.DrawSphere(
            previewEndPoint,
            gizmoPointRadius
        );

        // 畫出移動路線。
        Gizmos.color =
            Color.yellow;

        Gizmos.DrawLine(
            previewStartPoint,
            previewEndPoint
        );
    }

    #endregion
}