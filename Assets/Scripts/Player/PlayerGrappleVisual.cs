using UnityEngine;

/// <summary>
/// 本地第一人稱勾索視覺。
///
/// 只負責：
/// 1. LineRenderer。
/// 2. RopeOrigin。
/// 3. 射出伸長。
/// 4. 反向收繩。
/// 5. 等 CameraFollow 完成本幀 CameraRig 更新後再更新線條。
///
/// 不負責：
/// 1. Raycast。
/// 2. KCC。
/// 3. 拉動玩家。
/// 4. Momentum。
/// 5. 充能。
/// 6. 冷卻。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
[RequireComponent(typeof(PlayerGrapple))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerGrappleVisual : MonoBehaviour
{
    // =====================================================================
    #region 引用

    [Header("勾索引用")]

    [SerializeField]
    [Tooltip("玩家勾索核心。Visual 只從這裡讀取 Phase、錨點與繩索伸出比例。若留空會自動取得。")]
    private PlayerGrapple grapple;

    [SerializeField]
    [Tooltip("玩家移動模組。當 RopeOrigin 沒有設定時，會使用 CamTarget 作為備用起點。若留空會自動取得。")]
    private PlayerMovement movement;

    [SerializeField]
    [Tooltip("勾索 LineRenderer。若留空會自動取得同物件上的 LineRenderer。")]
    private LineRenderer grappleLine;

    [SerializeField]
    [Tooltip("第一人稱勾索真正的視覺發射點。未來 Weapon ViewModel 建立完成後，可以由武器系統動態設定成勾索槍口。")]
    private Transform ropeOrigin;

    #endregion

    // =====================================================================
    #region LineRenderer 外觀

    [Header("LineRenderer 外觀")]

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("勾索線條起點寬度。")]
    private float ropeStartWidth = 0.05f;

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("勾索線條終點寬度。")]
    private float ropeEndWidth = 0.05f;

    [SerializeField]
    [Tooltip("勾索線條顏色。")]
    private Color ropeColor = Color.cyan;

    #endregion

    // =====================================================================
    #region 執行狀態

    private Material runtimeLineMaterial;

    /// <summary>
    /// 本地玩家是否要求啟用此 Visual。
    /// </summary>
    private bool localVisualEnabled;

    /// <summary>
    /// 是否已經成功訂閱 CameraFollow。
    /// </summary>
    private bool cameraEventBound;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (grapple == null)
        {
            grapple =
                GetComponent<PlayerGrapple>();
        }

        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }

        if (grappleLine == null)
        {
            grappleLine =
                GetComponent<LineRenderer>();
        }

        SetupLine();
    }

    private void LateUpdate()
    {
        /*
         * 如果 PlayerLocalView Spawn 時 CameraFollow
         * 尚未完成初始化，
         * 這裡會持續嘗試綁定。
         *
         * 一旦成功就不再執行額外工作。
         */
        if (localVisualEnabled &&
            cameraEventBound == false)
        {
            TryBindCameraEvent();
        }
    }

    private void OnDestroy()
    {
        UnbindCameraEvent();

        if (runtimeLineMaterial != null)
        {
            Destroy(
                runtimeLineMaterial
            );
        }
    }

    #endregion

    // =====================================================================
    #region 本地玩家啟用

    /// <summary>
    /// PlayerLocalView 確認這是本地 Input Authority 玩家後呼叫。
    /// </summary>
    public void BindLocalVisual()
    {
        localVisualEnabled =
            true;

        TryBindCameraEvent();
    }

    /// <summary>
    /// 玩家 Despawn 或離開時解除本地視覺。
    /// </summary>
    public void UnbindLocalVisual()
    {
        localVisualEnabled =
            false;

        UnbindCameraEvent();

        HideLine();
    }

    #endregion

    // =====================================================================
    #region Camera 更新事件

    private void TryBindCameraEvent()
    {
        if (cameraEventBound)
            return;

        if (CameraFollow.Singleton == null)
            return;

        CameraFollow.Singleton.AfterCameraUpdated +=
            OnAfterCameraUpdated;

        cameraEventBound =
            true;
    }

    private void UnbindCameraEvent()
    {
        if (cameraEventBound == false)
            return;

        if (CameraFollow.Singleton != null)
        {
            CameraFollow.Singleton.AfterCameraUpdated -=
                OnAfterCameraUpdated;
        }

        cameraEventBound =
            false;
    }

    /// <summary>
    /// CameraRig 已完成本幀位置與旋轉後，
    /// 才更新 LineRenderer。
    ///
    /// 這是防止高速勾索時 RopeOrigin
    /// 因為讀到上一幀 CameraRig Transform 而抖動的核心。
    /// </summary>
    private void OnAfterCameraUpdated()
    {
        if (localVisualEnabled == false)
            return;

        UpdateLine();
    }

    #endregion

    // =====================================================================
    #region RopeOrigin

    /// <summary>
    /// 動態指定第一人稱勾索發射點。
    ///
    /// 未來不同職業 ViewModel / 不同武器建立後，
/// 可以把武器上的 GrappleMuzzle 傳進來。
    /// </summary>
    public void SetRopeOrigin(
        Transform newRopeOrigin
    )
    {
        ropeOrigin =
            newRopeOrigin;
    }

    /// <summary>
    /// 清除目前武器提供的 RopeOrigin。
    /// 清除後會退回 CamTarget。
    /// </summary>
    public void ClearRopeOrigin(
        Transform currentRopeOrigin
    )
    {
        if (ropeOrigin !=
            currentRopeOrigin)
        {
            return;
        }

        ropeOrigin =
            null;
    }

    private Vector3 GetRopeStartPosition()
    {
        if (ropeOrigin != null)
        {
            return ropeOrigin.position;
        }

        if (movement != null &&
            movement.CamTarget != null)
        {
            return movement.CamTarget.position;
        }

        return transform.position;
    }

    #endregion

    // =====================================================================
    #region LineRenderer

    private void SetupLine()
    {
        if (grappleLine == null)
            return;

        if (grappleLine.sharedMaterial == null)
        {
            Shader shader =
                Shader.Find(
                    "Sprites/Default"
                );

            if (shader != null)
            {
                runtimeLineMaterial =
                    new Material(shader);

                grappleLine.material =
                    runtimeLineMaterial;
            }
        }

        grappleLine.useWorldSpace =
            true;

        grappleLine.startWidth =
            ropeStartWidth;

        grappleLine.endWidth =
            ropeEndWidth;

        grappleLine.startColor =
            ropeColor;

        grappleLine.endColor =
            ropeColor;

        HideLine();
    }

    private void UpdateLine()
    {
        if (grappleLine == null ||
            grapple == null)
        {
            return;
        }

        GrapplePhase phase =
            grapple.CurrentPhase;

        if (phase ==
                GrapplePhase.Idle ||
            phase ==
                GrapplePhase.PreFire)
        {
            HideLine();
            return;
        }

        float extension =
            grapple.CurrentRopeExtension;

        if (extension <=
            0.0001f)
        {
            HideLine();
            return;
        }

        Vector3 start =
            GetRopeStartPosition();

        Vector3 target =
            grapple.CurrentGrapplePoint;

        /*
         * Shooting：
         * 0 → 1
         *
         * Attached：
         * 1
         *
         * Retracting：
         * 起始比例 → 0
         */
        Vector3 visualEnd =
            Vector3.Lerp(
                start,
                target,
                extension
            );

        grappleLine.enabled =
            true;

        grappleLine.positionCount =
            2;

        grappleLine.SetPosition(
            0,
            start
        );

        grappleLine.SetPosition(
            1,
            visualEnd
        );
    }

    private void HideLine()
    {
        if (grappleLine == null)
            return;

        grappleLine.enabled =
            false;

        grappleLine.positionCount =
            0;
    }

    #endregion
}