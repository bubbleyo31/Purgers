using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 唯一本機技能調色 Owner。建立僅含 LGG 的暫時 Global Volume；
/// 只在本機 camera stack 的最後後製相機渲染時啟用，不改任何 authored Profile。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAbilityColorGrading : MonoBehaviour
{
    [Header("技能畫面設定")]
    [SerializeField, Tooltip("四種效果的 Lift／Gamma／Gain。留空讀 Resources/Presentation/ActiveAbilityColorGrading。")]
    private AbilityColorGradingSettings settings;
    private Player owner;
    private PlayerHealth observedHealth;
    private Volume runtimeVolume;
    private VolumeProfile runtimeProfile;
    private VolumeStack baselineStack;
    private LiftGammaGain grading;
    private bool ownsSettings;
    private float bulletWeight, precisionWeight, experienceWeight;
    private float lastHealTime = float.NegativeInfinity;
    private Camera activeRenderCamera;
    private UniversalAdditionalCameraData temporarilyEnabledPost;
    private static PlayerAbilityColorGrading currentLocalOwner;

    public static PlayerAbilityColorGrading BindLocal(Player player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || !player.Object.HasInputAuthority) return null;
        var result = player.GetComponent<PlayerAbilityColorGrading>();
        if (result == null) result = player.gameObject.AddComponent<PlayerAbilityColorGrading>();
        result.Bind(player);
        return result;
    }
    public void Bind(Player player)
    {
        if (owner == player && observedHealth != null) return;
        Unbind();
        if (currentLocalOwner != null && currentLocalOwner != this) currentLocalOwner.Unbind();
        owner = player;
        currentLocalOwner = this;
        observedHealth = player != null ? player.Health : null;
        if (observedHealth != null) observedHealth.LocalHealingReceived += OnHealing;
        if (settings == null) settings = Resources.Load<AbilityColorGradingSettings>(AbilityColorGradingSettings.ResourcePath);
        if (settings == null) { settings = ScriptableObject.CreateInstance<AbilityColorGradingSettings>(); ownsSettings = true; }
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }
    public void Unbind()
    {
        if (observedHealth != null) observedHealth.LocalHealingReceived -= OnHealing;
        observedHealth = null;
        owner = null;
        bulletWeight = precisionWeight = experienceWeight = 0f;
        lastHealTime = float.NegativeInfinity;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        if (runtimeVolume != null) runtimeVolume.enabled = false;
        RestoreCamera();
        if (currentLocalOwner == this) currentLocalOwner = null;
    }
    private bool HasLocalLivingOwner => owner != null && owner.Object != null && owner.Object.IsValid &&
        owner.Object.HasInputAuthority && owner.Health != null && owner.Health.IsAlive;
    private void OnHealing(float amount)
    {
        if (amount > 0f && HasLocalLivingOwner) lastHealTime = Time.unscaledTime;
    }
    private void LateUpdate()
    {
        if (settings == null) return;
        bool live = HasLocalLivingOwner;
        var precision = live ? PlayerAbilityQualification.GetModule<PlayerPrecisionLockAbility>(owner) : null;
        var experience = live ? PlayerAbilityQualification.GetModule<PlayerExperienceAbility>(owner) : null;
        float step = Time.unscaledDeltaTime / Mathf.Max(.01f, settings.fadeSeconds);
        bulletWeight = Mathf.MoveTowards(bulletWeight, live && PlayerBulletTimeSessionRegistry.IsPlayerAffected(owner) ? 1f : 0f, step);
        precisionWeight = Mathf.MoveTowards(precisionWeight, precision != null && precision.Phase == PlayerActiveAbilityPhase.Active &&
            precision.ActiveRemainingSeconds > 0f ? 1f : 0f, step);
        experienceWeight = Mathf.MoveTowards(experienceWeight, experience != null && experience.Phase == PlayerActiveAbilityPhase.Active &&
            experience.ActiveRemainingSeconds > 0f ? 1f : 0f, step);
        if (!live) lastHealTime = float.NegativeInfinity;
    }
    // 利用現有 CameraFollow rig 與 URP stack，只套用一次後製，不擅自把 World/Weapon 同時開啟。
    private Camera ResolvePostCamera()
    {
        var follow = CameraFollow.Singleton;
        if (follow == null) return null;
        Camera baseCamera = null;
        foreach (var candidate in follow.GetComponentsInChildren<Camera>(true))
        {
            var data = candidate.GetComponent<UniversalAdditionalCameraData>();
            if (candidate.isActiveAndEnabled && data != null && data.renderType == CameraRenderType.Base)
            { baseCamera = candidate; break; }
        }
        if (baseCamera == null) return null;
        var baseData = baseCamera.GetComponent<UniversalAdditionalCameraData>();
        Camera final = baseCamera;
        Camera postCamera = baseData.renderPostProcessing ? baseCamera : null;
        var stack = baseData.cameraStack;
        if (stack != null)
            foreach (Camera camera in stack)
            {
                if (camera == null || !camera.isActiveAndEnabled) continue;
                final = camera;
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data != null && data.renderPostProcessing) postCamera = camera;
            }
        return postCamera != null ? postCamera : final;
    }
    private void EnsureVolume(int layer)
    {
        if (runtimeVolume != null)
        {
            if (runtimeVolume.gameObject.layer != layer) runtimeVolume.gameObject.layer = layer;
            return;
        }
        var obj = new GameObject("Ability Color Grading (Local Global Volume)");
        obj.hideFlags = HideFlags.DontSave;
        obj.transform.SetParent(transform, false);
        obj.layer = layer;
        runtimeVolume = obj.AddComponent<Volume>();
        runtimeVolume.enabled = false;
        runtimeVolume.isGlobal = true;
        runtimeVolume.priority = 10000f;
        runtimeVolume.weight = 1f;
        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.hideFlags = HideFlags.DontSave;
        grading = runtimeProfile.Add<LiftGammaGain>(true);
        grading.hideFlags = HideFlags.DontSave;
        runtimeVolume.sharedProfile = runtimeProfile;
        baselineStack = VolumeManager.instance.CreateStack();
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (runtimeVolume != null) runtimeVolume.enabled = false;
        RestoreCamera();
        if (!HasLocalLivingOwner || currentLocalOwner != this || settings == null ||
            camera == null || camera.cameraType != CameraType.Game || camera != ResolvePostCamera()) return;
        float heal = AbilityColorGradingRules.HealingPulse(Time.unscaledTime-lastHealTime, settings.healingPulseSeconds);
        Vector4 weights = new Vector4(Mathf.SmoothStep(0f,1f,bulletWeight) * settings.bulletTime.strength,
            Mathf.SmoothStep(0f,1f,precisionWeight) * settings.precisionLock.strength,
            Mathf.SmoothStep(0f,1f,experienceWeight) * settings.experience.strength, heal * settings.healing.strength);
        float total = weights.x+weights.y+weights.z+weights.w;
        if (total <= .0001f) return;
        if (total > 1f) weights /= total;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        if (data == null || data.volumeLayerMask.value == 0) return;
        int layer=0;
        for (; layer<32; layer++) if ((data.volumeLayerMask.value & (1<<layer)) != 0) break;
        EnsureVolume(layer);
        // 私有 stack 先取現有全部 Volume 混合基底，此時自己的 Volume 關閉，避免回饋累積。
        VolumeManager.instance.Update(baselineStack, data.volumeTrigger != null ? data.volumeTrigger : camera.transform, data.volumeLayerMask);
        var baseline = baselineStack.GetComponent<LiftGammaGain>();
        grading.lift.value = Combine(baseline.lift.value, settings.bulletTime.lift, settings.precisionLock.lift, settings.experience.lift, settings.healing.lift, weights);
        grading.gamma.value = Combine(baseline.gamma.value, settings.bulletTime.gamma, settings.precisionLock.gamma, settings.experience.gamma, settings.healing.gamma, weights);
        grading.gain.value = Combine(baseline.gain.value, settings.bulletTime.gain, settings.precisionLock.gain, settings.experience.gain, settings.healing.gain, weights);
        runtimeVolume.enabled = true;
        activeRenderCamera = camera;
        if (!data.renderPostProcessing) { data.renderPostProcessing = true; temporarilyEnabledPost = data; }
    }
    private static Vector4 Combine(Vector4 baseline, Vector4 bullet, Vector4 precision, Vector4 experience, Vector4 heal, Vector4 weights)
    {
        var value = AbilityColorGradingRules.AddTint(baseline, bullet, weights.x);
        value = AbilityColorGradingRules.AddTint(value, precision, weights.y);
        value = AbilityColorGradingRules.AddTint(value, experience, weights.z);
        return AbilityColorGradingRules.AddTint(value, heal, weights.w);
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (activeRenderCamera != camera) return;
        if (runtimeVolume != null) runtimeVolume.enabled = false;
        RestoreCamera();
    }
    private void RestoreCamera()
    {
        if (temporarilyEnabledPost != null) temporarilyEnabledPost.renderPostProcessing = false;
        temporarilyEnabledPost = null;
        activeRenderCamera = null;
    }
    private void OnDisable() => Unbind();
    private void OnDestroy()
    {
        Unbind();
        if (runtimeVolume != null) Destroy(runtimeVolume.gameObject);
        if (runtimeProfile != null) Destroy(runtimeProfile);
        if (grading != null) Destroy(grading);
        if (baselineStack != null) VolumeManager.instance.DestroyStack(baselineStack);
        if (ownsSettings && settings != null) Destroy(settings);
    }
}
