using UnityEngine;

/// <summary>只控制投射物的外觀子物件；不讀寫權威位置、速度、碰撞與傷害。</summary>
[DisallowMultipleComponent]
public sealed class PlayerAbilityProjectilePresentation : MonoBehaviour
{
    [Header("可替換投射物外觀")]
    [SerializeField, Tooltip("必須是投射物 Root 底下獨立的純外觀子物件，不能指定 Root 或含 Collider／NetworkObject 的階層。所有轉向與漂浮僅套在這個子物件。")]
    private Transform visualAnchor;
    [SerializeField, Tooltip("可選替換外觀 Prefab；不得包含 Collider、Rigidbody 或 NetworkObject。留空沿用 Anchor 的既有模型；指定後僅在本機建立，並隱藏 Anchor 原有 Renderer。模型彈頭朝 +Z。")]
    private GameObject visualPrefab;
    [Header("榴彈與彈射彈朝向")]
    [SerializeField, Min(0f), Tooltip("外觀追隨速度方向的靈敏度，預設 18；越大越快對齊，0 為立即對齊。榴彈彈頭沿拋物線切線領先、尾部跟隨，僅是重頭輕尾的視覺效果，不新增剛體力矩。")]
    private float trajectoryFollowSharpness = 18f;
    [SerializeField, Tooltip("模型朝向修正，單位為度。預設模型彈頭朝 +Z，因此為 0；美術模型使用其他軸時在此修正，不轉動權威 Root。")]
    private Vector3 modelRotationOffset;
    [Header("治療包停留漂浮")]
    [SerializeField, Min(0f), Tooltip("治療包碰撞停留後，外觀相對原位置的基本抬升公尺數，預設 0.2。拾取與遮擋仍使用原本權威位置。")]
    private float healingFloatHeight = 0.2f;
    [SerializeField, Min(0f), Tooltip("治療包外觀上下漂浮幅度，公尺，預設 0.06；0 停止上下擺動。")]
    private float healingFloatAmplitude = 0.06f;
    [SerializeField, Min(0f), Tooltip("治療包外觀每秒漂浮週期，預設 1；僅在碰撞停留後生效。")]
    private float healingFloatFrequency = 1f;

    private bool initialized;
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private GameObject visualInstance;
    private Renderer[] authoredRenderers;
    private bool[] authoredEnabled;

    private bool HasSafeAnchor => visualAnchor != null && visualAnchor != transform &&
        visualAnchor.IsChildOf(transform) && PlayerAbilityLocalVfxSlot.IsVisualOnly(visualAnchor.gameObject);

    /// <summary>由 NetworkBehaviour.Render 提供快照；不存取尚未 Spawn 的 Networked 屬性。</summary>
    public void ApplyVisualState(PlayerAbilityProjectileKind kind, Vector3 velocity, bool resting,
        bool impacted, float elapsedSeconds, float deltaTime)
    {
        if (!HasSafeAnchor) return;
        if (!initialized)
        {
            basePosition = visualAnchor.localPosition;
            baseRotation = visualAnchor.localRotation;
            initialized = true;
            if (visualPrefab != null && PlayerAbilityLocalVfxSlot.IsVisualOnly(visualPrefab))
            {
                authoredRenderers = visualAnchor.GetComponentsInChildren<Renderer>(true);
                authoredEnabled = new bool[authoredRenderers.Length];
                for (int i = 0; i < authoredRenderers.Length; i++)
                { authoredEnabled[i] = authoredRenderers[i].enabled; authoredRenderers[i].enabled = false; }
                visualInstance = Instantiate(visualPrefab, visualAnchor, false);
                visualInstance.name = visualPrefab.name + " (Local Visual)";
                visualInstance.hideFlags = HideFlags.DontSave;
            }
        }
        visualAnchor.gameObject.SetActive(!impacted);
        if (impacted) return;
        visualAnchor.localPosition = basePosition;
        if (kind == PlayerAbilityProjectileKind.HealingPack)
        {
            visualAnchor.localRotation = baseRotation;
            if (resting)
            {
                float bob = Mathf.Sin(elapsedSeconds * Mathf.Max(0f, healingFloatFrequency) * Mathf.PI * 2f);
                float height = Mathf.Max(0f, healingFloatHeight) + bob * Mathf.Max(0f, healingFloatAmplitude);
                visualAnchor.position += Vector3.up * height;
            }
        }
        else if (velocity.sqrMagnitude > 0.000001f)
        {
            Vector3 direction = velocity.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(direction, up) * Quaternion.Euler(modelRotationOffset);
            float blend = trajectoryFollowSharpness <= 0f ? 1f : 1f - Mathf.Exp(-trajectoryFollowSharpness * Mathf.Max(0f, deltaTime));
            visualAnchor.rotation = Quaternion.Slerp(visualAnchor.rotation, rotation, blend);
        }
    }

    public void ResetPresentation()
    {
        if (initialized && visualAnchor != null && visualAnchor != transform && visualAnchor.IsChildOf(transform))
        {
            visualAnchor.localPosition = basePosition;
            visualAnchor.localRotation = baseRotation;
            visualAnchor.gameObject.SetActive(true);
        }
        if (authoredRenderers != null)
            for (int i = 0; i < authoredRenderers.Length; i++)
                if (authoredRenderers[i] != null) authoredRenderers[i].enabled = authoredEnabled[i];
        if (visualInstance != null) Destroy(visualInstance);
        visualInstance = null;
        authoredRenderers = null;
        authoredEnabled = null;
        initialized = false;
    }

    private void OnDisable() => ResetPresentation();
}
