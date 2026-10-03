using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 本地槍械 ViewModel 的單一 Billboard 火焰。只讀 Rifle／SMG 的成功射擊序號。
/// 不讀滑鼠、不改彈藥或傷害，不生成 NetworkObject；Manager 負責綁定與解除。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)] // CameraFollow、FOV 與 ViewModel Motion(1000) 完成後才定位。
public sealed class FirstPersonMuzzleFlash : MonoBehaviour
{
    [Header("槍口火焰素材")]
    [SerializeField, Tooltip("指定 MuzzleFlash 材質（Purgers/FirstPersonMuzzleFlash），內含由左至右、由上至下的 4×3 透明序列圖。未指定則不產生特效。")]
    private Material flashMaterial;

    [Header("畫面尺寸與播放")]
    [SerializeField, Range(0.01f, 0.5f), Tooltip("整格圖片占 WeaponCamera 畫面高度的比例，預設 0.18（18%），範圍 0.01～0.5。含圖片透明留白；每幀依槍口深度與相機投影補償，火焰本體小於整格。")]
    private float screenHeight = 0.18f;
    [SerializeField, Range(1f, 120f), Tooltip("每秒播放格數，預設 24，範圍 1～120。12 格總時長 = 12 ÷ 此值；下一發直接重播同一物件，不累積煙霧或等待佇列。")]
    private float framesPerSecond = 24f;
    [SerializeField, Tooltip("圖片中對準槍口的正規化座標，左下 (0,0)、右上 (1,1)，兩軸限制 0～1。這版素材預設 (0.5,0.38)，修正生成圖火焰中心偏下的留白。")]
    private Vector2 imagePivot = new Vector2(0.5f, 0.38f);

    private readonly FirstPersonMuzzleFlashPlayback playback = new FirstPersonMuzzleFlashPlayback();
    private static readonly int FrameUvId = Shader.PropertyToID("_FrameUV");
    private PlayerProfessionType profession;
    private AttackRifle rifle;
    private SupportSMG smg;
    private Player owner;
    private PlayerProfession ownerProfession;
    private PlayerHealth health;
    private PlayerActionGate gate;
    private PlayerQuickActionController quickAction;
    private PlayerAimController aim;
    private AttackFocusAbility focus;
    private Transform muzzle;
    private Camera viewCamera;
    private GameObject visual;
    private MeshRenderer visualRenderer;
    private Mesh quad;
    private MaterialPropertyBlock properties;

    /// <summary>只由本地 ProfessionViewModelManager 傳入當前 Runtime 與實際 ViewModel 槍口。</summary>
    public void BindGameplaySources(PlayerProfessionType sourceProfession, AttackRifle sourceRifle,
        SupportSMG sourceSmg, PlayerQuickActionController sourceQuickAction, PlayerAimController sourceAim,
        Player sourceOwner, Transform sourceMuzzle, Camera sourceCamera)
    {
        UnbindGameplaySources();
        profession = sourceProfession;
        rifle = sourceRifle;
        focus = sourceRifle != null ? sourceRifle.GetComponent<AttackFocusAbility>() : null;
        smg = sourceSmg;
        quickAction = sourceQuickAction;
        aim = sourceAim;
        owner = sourceOwner;
        muzzle = sourceMuzzle;
        viewCamera = sourceCamera;
        if (owner != null)
        {
            ownerProfession = owner.GetComponent<PlayerProfession>();
            health = owner.GetComponent<PlayerHealth>();
            gate = owner.GetComponent<PlayerActionGate>();
        }
        EstablishBaseline();
    }

    public void UnbindGameplaySources()
    {
        playback.Invalidate();
        ReleaseVisual();
        rifle = null;
        smg = null;
        quickAction = null;
        aim = null;
        focus = null;
        owner = null;
        ownerProfession = null;
        health = null;
        gate = null;
        muzzle = null;
        viewCamera = null;
        profession = PlayerProfessionType.None;
    }

    private void OnEnable() => EstablishBaseline();

    private void OnDisable()
    {
        playback.Invalidate();
        ReleaseVisual();
    }

    private void OnDestroy() => UnbindGameplaySources();

    private void EstablishBaseline()
    {
        playback.Invalidate();
        if (TryReadState(out int sequence, out int state, out bool eligible))
            playback.Observe(sequence, state, eligible);
    }

    private void LateUpdate()
    {
        if (!TryReadState(out int sequence, out int state, out bool eligible))
        {
            playback.Invalidate();
            ReleaseVisual();
            return;
        }

        bool fired = playback.Observe(sequence, state, eligible);
        if (playback.ReleaseRequested) ReleaseVisual();
        int frame = playback.Advance(fired ? 0f : Time.deltaTime, framesPerSecond);
        if (frame < 0)
        {
            if (visualRenderer != null) visualRenderer.enabled = false;
            return;
        }

        EnsureVisual();
        PresentFrame(frame);
    }

    private static bool Ready(NetworkBehaviour source) =>
        source != null && source.isActiveAndEnabled && source.Object != null && source.Object.IsValid;

    private bool TryReadState(out int sequence, out int state, out bool eligible)
    {
        sequence = state = 0;
        eligible = false;
        if (!Ready(owner) || !owner.Object.HasInputAuthority || !Ready(ownerProfession) ||
            ownerProfession.CurrentProfession != profession || !Ready(health) || !Ready(gate) ||
            muzzle == null || !muzzle.gameObject.activeInHierarchy || viewCamera == null ||
            !viewCamera.isActiveAndEnabled || flashMaterial == null || flashMaterial.mainTexture == null ||
            (viewCamera.cullingMask & (1 << muzzle.gameObject.layer)) == 0)
            return false;

        bool reloading;
        bool special = false;
        if (profession == PlayerProfessionType.Attack && Ready(rifle) && rifle.OwnerPlayer == owner)
        {
            sequence = rifle.ShotSequence;
            reloading = rifle.IsReloading;
        }
        else if (profession == PlayerProfessionType.Support && Ready(smg) && smg.OwnerPlayer == owner)
        {
            sequence = smg.ShotSequence;
            reloading = smg.IsReloading;
            special = smg.IsSpecialModeActive;
        }
        else return false;

        // 可選模組一旦有綁定卻失效，不能繼續讀 Networked 資料。
        if ((quickAction != null && !Ready(quickAction)) || (aim != null && !Ready(aim)) ||
            (focus != null && !Ready(focus)))
            return false;
        int quickPhase = quickAction != null ? (int)quickAction.CurrentPhase : 0;
        int aimPhase = aim != null ? (int)aim.CurrentAimPhase : 0;
        int blocked = (int)gate.CurrentBlockedActions;
        int focusPhase = focus != null ? (int)focus.CurrentPhase : 0;
        state = aimPhase | (special ? 16 : 0) | (reloading ? 32 : 0) | (quickPhase << 6) |
            (focusPhase << 8) | (blocked << 10);
        eligible = health.IsAlive && !reloading && quickPhase == 0 && !gate.IsBlocked(PlayerActionBlockMask.Fire);
        return true;
    }

    /// <summary>同一武器狀態期間只有一個 Quad；下一發重用 Renderer、Mesh 與 PropertyBlock。</summary>
    private void EnsureVisual()
    {
        if (visual != null) return;
        visual = new GameObject("LocalMuzzleFlash");
        visual.layer = muzzle.gameObject.layer;
        visual.transform.SetParent(muzzle, false);
        quad = new Mesh { name = "LocalMuzzleFlashQuad" };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        quad.RecalculateBounds();
        visual.AddComponent<MeshFilter>().sharedMesh = quad;
        visualRenderer = visual.AddComponent<MeshRenderer>();
        visualRenderer.sharedMaterial = flashMaterial;
        visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
        visualRenderer.receiveShadows = false;
        visualRenderer.lightProbeUsage = LightProbeUsage.Off;
        visualRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        properties = properties ?? new MaterialPropertyBlock();
    }

    private void PresentFrame(int frame)
    {
        float depth = Vector3.Dot(muzzle.position - viewCamera.transform.position, viewCamera.transform.forward);
        if (depth <= viewCamera.nearClipPlane || depth >= viewCamera.farClipPlane)
        {
            visualRenderer.enabled = false;
            return;
        }

        // 使用實際投影矩陣，保留 WeaponCamera 的 FOV 仲裁，不自行修改 FOV。
        float projectionY = Mathf.Abs(viewCamera.projectionMatrix.m11);
        float size = 2f * (viewCamera.orthographic ? 1f : depth) /
            Mathf.Max(0.0001f, projectionY) * Mathf.Clamp(screenHeight, 0.01f, 0.5f);
        Quaternion facing = viewCamera.transform.rotation;
        Vector2 pivot = new Vector2(Mathf.Clamp01(imagePivot.x), Mathf.Clamp01(imagePivot.y));
        Vector3 offset = facing * new Vector3((0.5f - pivot.x) * size, (0.5f - pivot.y) * size, 0f);
        // Quad 保留在槍口子階層，使用每幀世界姿勢抵消動畫骨架旋轉。
        visual.transform.SetPositionAndRotation(muzzle.position + offset, facing);
        Vector3 parentScale = muzzle.lossyScale;
        visual.transform.localScale = new Vector3(size / SafeScale(parentScale.x),
            size / SafeScale(parentScale.y), size / SafeScale(parentScale.z));
        visual.layer = muzzle.gameObject.layer;
        visualRenderer.sharedMaterial = flashMaterial;
        properties.SetVector(FrameUvId, new Vector4(0.25f, 1f / 3f, frame % 4 * 0.25f, (2 - frame / 4) / 3f));
        visualRenderer.SetPropertyBlock(properties);
        visualRenderer.enabled = true;
    }

    private static float SafeScale(float value) => Mathf.Abs(value) < 0.0001f ? 0.0001f : value;

    private void ReleaseVisual()
    {
        if (visual != null)
        {
            visual.SetActive(false); // Destroy 延至幀尾，先確保舊圖當幀不可見。
            DestroyOwned(visual);
        }
        if (quad != null) DestroyOwned(quad);
        visual = null;
        visualRenderer = null;
        quad = null;
    }

    private static void DestroyOwned(Object item)
    {
        if (Application.isPlaying) Destroy(item);
        else DestroyImmediate(item);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        screenHeight = Mathf.Clamp(screenHeight, 0.01f, 0.5f);
        framesPerSecond = Mathf.Clamp(framesPerSecond, 1f, 120f);
        imagePivot = new Vector2(Mathf.Clamp01(imagePivot.x), Mathf.Clamp01(imagePivot.y));
    }
#endif
}
