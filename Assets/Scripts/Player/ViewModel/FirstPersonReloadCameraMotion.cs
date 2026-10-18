using UnityEngine;

/// <summary>
/// 只讀目前本機武器的換彈狀態，取既有 Root 相對模型的旋轉。
/// 經 FirstPersonHitCameraShake 統一合成，不寫玩家 Look、骨骼、相機位置或 FOV。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)] // Update 捕捉動畫前基準；LateUpdate 在 CameraFollow 及其事件前提交效果。
public sealed class FirstPersonReloadCameraMotion : MonoBehaviour
{
    [Header("換彈鏡頭－動畫引用")]
    [SerializeField, Tooltip("同一 ViewModel 根物件的動作 Driver。留空時從同一物件取得；只讀其已綁定的本機武器。")]
    private FirstPersonViewModelActionAnimator actionAnimator;
    [SerializeField, Tooltip("目前啟用模型的 Animator 根節點，例如 RifleVine_Arms_Draft03。不可選 CameraRig 或武器 Root；用來排除玩家轉頭及共同父節點的鏡頭／晃槍動作。")]
    private Transform animatedModelRoot;
    [SerializeField, Tooltip("目前啟用模型右手底下的槍械 Root。必須是上方模型的子孫；讀相對模型的旋轉，不是 Root.localRotation。")]
    private Transform weaponRoot;

    [Header("換彈鏡頭－每把武器獨立幅度")]
    [SerializeField, Tooltip("相機軸向倍率：X 點頭、Y 左右轉、Z 側傾，各 0～1；0 關閉該方向。預設各 0.04（4%），僅為起調值。")]
    private Vector3 rotationGain = Vector3.one * .04f;
    [SerializeField, Range(0, 15), Tooltip("換彈旋轉總角度上限，單位度，0 關閉效果；預設 4 度。只限制本效果，不限制玩家輸入或命中震動。")]
    private float maximumDegrees = 4;
    [SerializeField, Min(0), Tooltip("旋轉跟隨及換彈結束／取消時的平滑時間常數（秒），預設 0.08；0 立即套用。死亡、換裝、解除綁定則立即清掉自身效果。")]
    private float blendSeconds = .08f;

    [Header("換彈鏡頭－快速轉頭減弱")]
    [SerializeField, Min(0), Tooltip("玩家視角輸入速度達此值開始減弱，單位度／秒，預設 120。讀現有輸入與靈敏度，不讀已晃動的相機。")]
    private float softenStartSpeed = 120;
    [SerializeField, Min(0), Tooltip("到達此視角輸入速度時使用最低倍率，單位度／秒，預設 540；必須大於開始減弱速度。")]
    private float softenEndSpeed = 540;
    [SerializeField, Range(0, 1), Tooltip("快速轉頭時保留的換彈效果比例，預設 0.2；0 全部壓低，1 不減弱。玩家本身的視角輸入不變。")]
    private float minimumTurnWeight = .2f;
    [SerializeField, Min(0), Tooltip("轉頭減弱／恢復的平滑時間常數（秒），預設 0.06；0 立即切換。")]
    private float turnResponseSeconds = .06f;

    private readonly ReloadCameraMotion motion = new ReloadCameraMotion();
    private FirstPersonHitCameraShake sink;
    private Player owner;
    private PlayerHealth health;
    private PlayerMovement movement;
    private InputManager input;
    private int bindingVersion;
    private bool prepared;
    private Quaternion modelToCamera = Quaternion.identity;

    private void Awake()
    {
        if (actionAnimator == null) actionAnimator = GetComponent<FirstPersonViewModelActionAnimator>();
    }

    private bool ValidReferences() => actionAnimator != null && actionAnimator.isActiveAndEnabled &&
        animatedModelRoot != null && animatedModelRoot.gameObject.activeInHierarchy &&
        actionAnimator.AnimationTransform == animatedModelRoot && weaponRoot != null &&
        weaponRoot.gameObject.activeInHierarchy && weaponRoot != animatedModelRoot &&
        weaponRoot.IsChildOf(animatedModelRoot);

    private Quaternion ReadPose() => Quaternion.Inverse(animatedModelRoot.rotation) * weaponRoot.rotation;

    private void Update()
    {
        prepared = false;
        var nextSink = FirstPersonHitCameraShake.Singleton;
        if (!ValidReferences() || nextSink == null || !nextSink.isActiveAndEnabled ||
            nextSink.EffectsRoot == null ||
            !actionAnimator.TryGetLocalReloadCameraState(out Player nextOwner, out bool reloading))
        {
            Release();
            return;
        }
        if (owner != nextOwner || sink != nextSink || bindingVersion != actionAnimator.BindingVersion)
        {
            Release();
            owner = nextOwner;
            sink = nextSink;
            bindingVersion = actionAnimator.BindingVersion;
            health = owner.GetComponent<PlayerHealth>();
            movement = owner.GetComponent<PlayerMovement>();
            input = owner.Runner != null ? owner.Runner.GetComponent<InputManager>() : null;
        }
        if (health == null || health.Object == null || !health.Object.IsValid || !health.IsAlive ||
            movement == null || input == null)
        {
            Release();
            return;
        }
        bool wasActive = motion.Active;
        motion.Prepare(reloading, ReadPose());
        if (!wasActive && motion.Active)
            modelToCamera = Quaternion.Inverse(sink.EffectsRoot.rotation) * animatedModelRoot.rotation;
        prepared = true;
    }

    private void LateUpdate()
    {
        if (!prepared) return;
        if (!ValidReferences() || sink == null || !sink.isActiveAndEnabled)
        {
            Release();
            return;
        }
        // Update 到 LateUpdate 之間的換裝／Despawn／死亡也不能留下舊引用或最後一幀效果。
        if (bindingVersion != actionAnimator.BindingVersion ||
            !actionAnimator.TryGetLocalReloadCameraState(out Player currentOwner, out bool reloading) ||
            currentOwner != owner || health == null || health.Object == null || !health.Object.IsValid || !health.IsAlive)
        {
            Release();
            return;
        }
        if (!reloading) motion.Prepare(false, Quaternion.identity);
        float dt = Time.unscaledDeltaTime;
        float lookSpeed = dt > 0 ? input.CurrentFrameLookDelta.magnitude * movement.LookSensitivity / dt : 0;
        Quaternion offset = motion.Step(ReadPose(), modelToCamera, rotationGain, maximumDegrees,
            lookSpeed, softenStartSpeed, softenEndSpeed, minimumTurnWeight,
            blendSeconds, turnResponseSeconds, dt);
        sink.SetReloadRotation(this, offset);
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();
    private void Release()
    {
        if (sink != null) sink.ClearReloadRotation(this);
        sink = null;
        owner = null;
        health = null;
        movement = null;
        input = null;
        prepared = false;
        motion.Reset();
    }
}
