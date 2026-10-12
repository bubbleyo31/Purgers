using UnityEngine;

/// <summary>
/// 第一／第三人稱勾索視覺；只讀既有 Fusion 狀態，呈現波浪與鉤爪。
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
[DefaultExecutionOrder(1200)]
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

    [Header("第三人稱繩索起點")]
    [SerializeField]
    [Tooltip("其他玩家觀看此角色時使用的世界模型發射點。請指定角色手部／裝備的 Transform，不要指定本機 ViewModel 或 CameraRig。留空使用下方玩家本地座標偏移。")]
    private Transform thirdPersonRopeOrigin;

    [SerializeField]
    [Tooltip("第三人稱起點留空時，相對玩家根 Transform 的本地座標偏移，單位公尺。X 向右、Y 向上、Z 向前；只改視覺，不改命中射線。")]
    private Vector3 thirdPersonOriginOffset = new Vector3(0.3f, 1.3f, 0.2f);

    [Header("射出繩身波浪")]
    [SerializeField]
    [Tooltip("啟用射出時持續流動的波浪。關閉後保持原本的直線伸長與收回，鉤爪仍會顯示。")]
    private bool enableWaves = true;

    [SerializeField, Min(0f)]
    [Tooltip("主要波幅，單位公尺，有效值至少 0；0 不起伏。乘上波幅曲線及兩端包絡；短繩會限制為目前可見長度的 25%，避免近距離爆開。")]
    private float waveAmplitude = 0.8f;

    [SerializeField, Min(0.05f)]
    [Tooltip("每個完整波浪的長度，單位公尺，有效值至少 0.05。越小越密；波浪過密時需提高 Rope Segments。")]
    private float waveLength = 3f;

    [SerializeField]
    [Tooltip("波峰沿繩身的流動速度，單位公尺／秒。正值由玩家流向鉤爪，負值反向，0 固定波相；不影響鉤爪飛行速度。")]
    private float waveTravelSpeed = 12f;

    [SerializeField, Range(0f, 1f)]
    [Tooltip("次要側向波幅相對主要波幅的比例，範圍 0～1。0 為平面波；提高後增加側向甩動，1 接近螺旋。")]
    private float secondaryWaveRatio = 0.35f;

    [SerializeField]
    [Tooltip("射出波幅倍率曲線。X 為目前繩索伸出比例 0～1；Y 為非負波幅倍率（負值視為 0）。Y=1 使用 Wave Amplitude，Y=2 加倍，仍受短繩限制。")]
    private AnimationCurve shootWaveAmplitude = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [SerializeField, Range(4, 128)]
    [Tooltip("起伏繩索的分段數，範圍 4～128；點數為分段數加 1。越多越平滑、繪製成本越高；完全拉直時自動回到兩點。")]
    private int ropeSegments = 48;

    [SerializeField, Min(0f)]
    [Tooltip("命中或中途收繩後，將當下波幅平滑縮至 0 的秒數。有效值至少 0；0 立即拉直。只影響外觀，不延後既有命中、能力或拉動時機。")]
    private float hitTightenDuration = 0.12f;

    [Header("鉤爪模型")]
    [SerializeField]
    [Tooltip("純視覺鉤爪 Prefab；留空只顯示繩子。不可包含 NetworkObject、Collider 或 Rigidbody。每個觀察端自行建立並重用一份，跟隨繩頭，不參與命中判定。")]
    private GameObject hookPrefab;

    [SerializeField, Min(0.001f)]
    [Tooltip("相對鉤爪 Prefab 原始大小的倍率，有效值至少 0.001；1 保持 Prefab 大小。")]
    private float hookScale = 1f;

    [SerializeField]
    [Tooltip("鉤爪朝向校正，單位 Euler 度。預設 Prefab 的 +Z 朝射出方向、+Y 朝上；模型軸向不同時在此調整。")]
    private Vector3 hookRotationOffset;

    [SerializeField]
    [Tooltip("繩子連接到鉤爪的位置，以 Prefab 根物件本地座標表示，縮放前單位公尺。此點會精確貼齊繩頭；0 表示根物件 Pivot。")]
    private Vector3 hookConnectionOffset;


    #endregion

    // =====================================================================
    #region LineRenderer 外觀

    [Header("線段渲染器外觀")]

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
    private bool networkVisualBound;
    private GrapplePhase previousPhase = GrapplePhase.Idle;
    private float waveDistance;
    private float currentAmplitude;
    private float tightenStartAmplitude;
    private float tightenElapsed;
    private Vector3 waveUp = Vector3.up;
    private GameObject hookInstance;
    private GameObject instantiatedHookPrefab;
    private GameObject rejectedHookPrefab;

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

        // 遠端角色不依賴本機 CameraFollow；使用自己的世界模型起點。
        if (networkVisualBound && !localVisualEnabled)
            UpdateLine();
    }

    private void OnDisable()
    {
        UnbindCameraEvent();
        HideLine();
    }

    private void OnDestroy()
    {
        UnbindCameraEvent();
        ReleaseHook();

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
    /// 所有角色完成 Spawn 後，由 PlayerLocalView 開啟只讀鉤索呈現。
    /// </summary>
    public void BindNetworkVisual()
    {
        // 所有角色由 PlayerLocalView.Spawned 開啟，完成 Spawn 前不讀 Networked。
        networkVisualBound = true;
        HideLine();
    }

    /// <summary>所有觀察端在 Despawn 時清除繩索與鉤爪。</summary>
    public void UnbindNetworkVisual()
    {
        networkVisualBound = false;
        UnbindLocalVisual();
        ReleaseHook();
    }

    /// <summary>只有本地 Input Authority 使用相機完成後的第一人稱起點。</summary>
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
        if (!localVisualEnabled || !networkVisualBound || !isActiveAndEnabled)
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
        if (!localVisualEnabled)
            return thirdPersonRopeOrigin != null ? thirdPersonRopeOrigin.position : transform.TransformPoint(thirdPersonOriginOffset);

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
        if (!networkVisualBound || grappleLine == null || grapple == null ||
            grapple.Object == null || !grapple.Object.IsValid ||
            grapple.Runner == null || !grapple.Runner.IsRunning)
        {
            HideLine();
            return;
        }

        // 本機尚未完成第一人稱綁定時不借用第三人稱起點；Proxy 可直接顯示。
        if (grapple.Object.HasInputAuthority && !localVisualEnabled)
        {
            HideLine();
            return;
        }

        RenderRope(grapple.CurrentPhase, GetRopeStartPosition(),
            grapple.CurrentGrapplePoint, grapple.CurrentRopeExtension, Time.deltaTime);
    }

    /// <summary>
    /// 將既有權威／預測狀態轉成繩索外觀。只在 Render／LateUpdate 使用，
    /// 波相是各觀察端的本地時間，不新增逐點網路同步，也不回寫玩法。
    /// </summary>
    private void RenderRope(GrapplePhase phase, Vector3 start, Vector3 target, float extension, float deltaSeconds)
    {
        extension = Mathf.Clamp01(extension);
        if (grappleLine == null || phase == GrapplePhase.Idle || phase == GrapplePhase.PreFire || extension <= 0.0001f)
        {
            HideLine();
            return;
        }

        Vector3 end = Vector3.Lerp(start, target, extension);
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= 0.0001f)
        {
            HideLine();
            return;
        }
        direction /= length;
        float dt = Mathf.Max(0f, deltaSeconds);

        if (phase != previousPhase)
        {
            if (phase == GrapplePhase.Shooting)
            {
                waveDistance = 0f;
                currentAmplitude = 0f;
                // 一次出鉤固定參考平面；轉鏡頭不會讓波形跟著滾轉。
                waveUp = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            }
            tightenStartAmplitude = currentAmplitude;
            tightenElapsed = 0f;
        }

        float wavelength = Mathf.Max(0.05f, waveLength);
        waveDistance = Mathf.Repeat(waveDistance + waveTravelSpeed * dt, wavelength);
        if (phase == GrapplePhase.Shooting)
        {
            float multiplier = shootWaveAmplitude == null ? 1f : Mathf.Max(0f, shootWaveAmplitude.Evaluate(extension));
            currentAmplitude = Mathf.Min(Mathf.Max(0f, waveAmplitude) * multiplier, length * 0.25f);
        }
        else
        {
            // 新觀察者直接看見 Attached 時 currentAmplitude=0，不重播已結束的射出動畫。
            tightenElapsed += dt;
            float progress = hitTightenDuration <= 0f ? 1f : Mathf.Clamp01(tightenElapsed / hitTightenDuration);
            currentAmplitude = tightenStartAmplitude * (1f - Mathf.SmoothStep(0f, 1f, progress));
            currentAmplitude = Mathf.Min(currentAmplitude, length * 0.25f);
        }
        if (!enableWaves)
            currentAmplitude = 0f;
        previousPhase = phase;

        bool waving = currentAmplitude > 0.0001f;
        int segments = waving ? Mathf.Clamp(ropeSegments, 4, 128) : 1;
        grappleLine.enabled = true;
        grappleLine.positionCount = segments + 1;

        Vector3 up = Vector3.ProjectOnPlane(waveUp, direction);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.right, direction);
        up.Normalize();
        Vector3 side = Vector3.Cross(direction, up).normalized;
        for (int i = 0; i <= segments; i++)
        {
            float fraction = i / (float)segments;
            Vector3 point = Vector3.Lerp(start, end, fraction);
            if (waving && i > 0 && i < segments)
            {
                float phaseAngle = (fraction * length - waveDistance) * (Mathf.PI * 2f / wavelength);
                float envelope = Mathf.Sin(fraction * Mathf.PI);
                point += currentAmplitude * envelope *
                    (up * Mathf.Sin(phaseAngle) + side * (Mathf.Cos(phaseAngle) * Mathf.Clamp01(secondaryWaveRatio)));
            }
            grappleLine.SetPosition(i, point);
        }
        UpdateHook(end, direction, up);
    }

    /// <summary>鉤爪只建立一份並重用；連接偏移以模型本地座標計算，端點保持精確接合。</summary>
    private void UpdateHook(Vector3 end, Vector3 direction, Vector3 up)
    {
        if (hookInstance != null && instantiatedHookPrefab != hookPrefab)
            ReleaseHook();
        if (hookPrefab == null)
            return;

        if (hookInstance == null)
        {
            if (rejectedHookPrefab == hookPrefab)
                return;
            if (hookPrefab.GetComponentInChildren<Fusion.NetworkObject>(true) != null ||
                hookPrefab.GetComponentInChildren<Collider>(true) != null ||
                hookPrefab.GetComponentInChildren<Rigidbody>(true) != null)
            {
                rejectedHookPrefab = hookPrefab;
                Debug.LogWarning("[鉤索視覺] 鉤爪請使用不含 NetworkObject、Collider、Rigidbody 的純視覺 Prefab。", this);
                return;
            }
            hookInstance = Instantiate(hookPrefab, transform);
            hookInstance.name = hookPrefab.name + " (Grapple Visual)";
            instantiatedHookPrefab = hookPrefab;
            foreach (Transform child in hookInstance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = grappleLine.gameObject.layer;
        }

        hookInstance.transform.localScale = hookPrefab.transform.localScale * Mathf.Max(0.001f, hookScale);
        hookInstance.transform.rotation = Quaternion.LookRotation(direction, up) * Quaternion.Euler(hookRotationOffset);
        hookInstance.transform.position = end - hookInstance.transform.TransformVector(hookConnectionOffset);
        hookInstance.SetActive(true);
    }

    private void HideLine()
    {
        if (hookInstance != null)
            hookInstance.SetActive(false);
        previousPhase = GrapplePhase.Idle;
        waveDistance = 0f;
        currentAmplitude = 0f;
        tightenStartAmplitude = 0f;
        tightenElapsed = 0f;
        if (grappleLine == null)
            return;

        grappleLine.enabled =
            false;

        grappleLine.positionCount =
            0;
    }

    private void ReleaseHook()
    {
        if (hookInstance != null)
        {
            hookInstance.SetActive(false);
            DestroyVisualObject(hookInstance);
        }
        hookInstance = null;
        instantiatedHookPrefab = null;
        rejectedHookPrefab = null;
    }

    private static void DestroyVisualObject(Object value)
    {
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    #endregion
}