using Fusion;
using Purgers.GameFlow.Stage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 舊類名因 Unity 序列化相容性保留；目前顯示本機玩家的動能增傷量條、
/// 最高強化樣式，以及進入可撤離區後的本地提示。
/// </summary>
[DisallowMultipleComponent]
public class LocalPlayerSpeedSlider : MonoBehaviour
{
    [Header("Fusion 執行器")]
    [SerializeField, Tooltip("目前 Gameplay Session 使用的 NetworkRunner。留空時自動尋找執行中的 Runner。")]
    private NetworkRunner runner;

    [Header("傷害增益量條")]
    [SerializeField, Tooltip("顯示動能傷害增益率，範圍固定 0～1；不再代表世界速度。")]
    private Slider speedSlider;
    [SerializeField, Tooltip("量條、傷害文字與撤離提示的共同視覺 Root。不可指定為掛有本腳本的物件。")]
    private GameObject speedVisualRoot;
    [SerializeField, Tooltip("只讀既有 DamageMultiplier，顯示傷害增加 N%。")]
    private TMP_Text damageBonusLabel;

    [Header("撤離提示")]
    [SerializeField, Tooltip("本機玩家進入目前可用的撤離區後顯示的綠色底板。")]
    private GameObject extractionPromptRoot;
    [SerializeField, Tooltip("顯示撤離倒數；尚未全員到齊時顯示等待其他玩家。")]
    private TMP_Text extractionPromptLabel;

    [Header("最高強化樣式")]
    [SerializeField] private Color normalGaugeColor = new Color(0.12f, 0.58f, 1f, 1f);
    [SerializeField] private Color maximumBoostGaugeColor = new Color32(37, 255, 217, 255);
    [SerializeField] private Color normalLabelColor = Color.white;
    [SerializeField] private Color maximumBoostLabelColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Min(1f)] private float normalLabelFontSize = 24f;
    [SerializeField, HideInInspector] private float maximumBoostLabelFontSize = 36f; // Legacy serialized field; fixed text size now uses normalLabelFontSize.
    [SerializeField] private Vector2 normalGaugeSize = new Vector2(380f, 14f);
    [SerializeField, HideInInspector] private Vector2 maximumBoostGaugeSize = new Vector2(440f, 22f); // Legacy serialized field; fixed gauge uses normalGaugeSize.

    [Header("量條內流光")]
    [SerializeField, Min(0f), Tooltip("未滿值時的內部流速；不影響動能與傷害。")]
    private float gaugeFlowSpeed = .5f;
    [SerializeField, Min(1f), Tooltip("最高強化時的流速倍率，預設 2 倍；切換時保留流動相位。")]
    private float maximumBoostFlowSpeedMultiplier = 2f;
    [SerializeField, Range(0f, 1f)] private float gaugeFlowStrength = .7f;

    [Header("顯示行為")]
    [SerializeField, Tooltip("本機 PlayerObject 不存在時隱藏整組增益 HUD。")]
    private bool hideWhilePlayerMissing = true;
    [SerializeField, Min(0f), Tooltip("正規化動能至少變動多少才重寫 Slider。")]
    private float speedChangeEpsilon = 0.001f;

    [Header("除錯設定")]
    [SerializeField, Tooltip("輸出本機玩家 HUD 綁定與解除資訊，不逐幀輸出。")]
    private bool debugSpeedUI;

    private Player boundPlayer;
    private PlayerGrappleMomentumEnergy boundEnergy;
    private StageFlowController stageFlowController;
    private RectTransform gaugeRect;
    private Image gaugeFillImage;
    private float lastDisplayedEnergy = float.NaN;
    private bool visualVisible;
    private bool maximumBoostStyleApplied;
    private bool extractionPromptVisible;
    private Material gaugeFlowMaterial;
    private Material originalGaugeMaterial;
    private float gaugeFlowPhase;

    private void Awake()
    {
        if (speedVisualRoot == null && speedSlider != null)
            speedVisualRoot = speedSlider.gameObject;

        if (speedVisualRoot == gameObject)
        {
            Debug.LogError("[Damage Bonus UI] Visual Root 不可與控制器位於同一物件。", this);
            speedVisualRoot = null;
        }

        if (speedSlider == null)
        {
            Debug.LogError("[Damage Bonus UI] 尚未指定增傷 Slider。", this);
        }
        else
        {
            speedSlider.interactable = false;
            speedSlider.wholeNumbers = false;
            speedSlider.minValue = 0f;
            speedSlider.maxValue = 1f;
            speedSlider.SetValueWithoutNotify(0f);
            gaugeRect = speedSlider.transform as RectTransform;
            gaugeFillImage = speedSlider.fillRect != null
                ? speedSlider.fillRect.GetComponent<Image>()
                : null;
        }

        TryResolveRunner();
        SetExtractionPromptVisible(false);
        ApplyMaximumBoostStyle(false, true);
        SetVisualVisible(!hideWhilePlayerMissing);
    }

    private void Update()
    {
        if (speedSlider == null)
            return;

        if (!TryResolveRunner() || !TryResolveLocalPlayer(out Player currentPlayer))
        {
            UnbindCurrentPlayer();
            return;
        }

        if (boundPlayer != currentPlayer)
            BindPlayer(currentPlayer);

        RefreshEnergyGauge();
        RefreshDamageBonus();
        RefreshGaugeFlow(Time.unscaledDeltaTime);
        RefreshExtractionPrompt();
    }

    private void OnDisable()
    {
        boundPlayer = null;
        boundEnergy = null;
        stageFlowController = null;
        lastDisplayedEnergy = float.NaN;
        ReleaseGaugeFlow();
    }

    private void OnDestroy() => ReleaseGaugeFlow();

    private void OnValidate()
    {
        speedChangeEpsilon = Mathf.Max(0f, speedChangeEpsilon);
        gaugeFlowSpeed = Mathf.Max(0f, gaugeFlowSpeed);
        maximumBoostFlowSpeedMultiplier = Mathf.Max(1f, maximumBoostFlowSpeedMultiplier);
        gaugeFlowStrength = Mathf.Clamp01(gaugeFlowStrength);
        normalLabelFontSize = Mathf.Max(1f, normalLabelFontSize);
        maximumBoostLabelFontSize = Mathf.Max(1f, maximumBoostLabelFontSize);
        normalGaugeSize = new Vector2(Mathf.Max(1f, normalGaugeSize.x), Mathf.Max(1f, normalGaugeSize.y));
        maximumBoostGaugeSize = new Vector2(
            Mathf.Max(1f, maximumBoostGaugeSize.x),
            Mathf.Max(1f, maximumBoostGaugeSize.y));
    }

    private bool TryResolveRunner()
    {
        if (runner != null && runner.IsRunning)
            return true;
        runner = FindFirstObjectByType<NetworkRunner>();
        return runner != null && runner.IsRunning;
    }

    private bool TryResolveLocalPlayer(out Player player)
    {
        player = null;
        if (runner == null || !runner.IsRunning || !runner.LocalPlayer.IsRealPlayer ||
            !LocalPlayerSpectatorController.TryGetViewedPlayerObject(runner, out NetworkObject playerObject) ||
            playerObject == null || !playerObject.IsValid)
            return false;

        player = playerObject.GetComponent<Player>();
        return player != null;
    }

    private void BindPlayer(Player newPlayer)
    {
        boundPlayer = newPlayer;
        boundEnergy = newPlayer.GetComponent<PlayerGrappleMomentumEnergy>();
        lastDisplayedEnergy = float.NaN;
        SetVisualVisible(true);
        if (debugSpeedUI)
            Debug.Log("[Damage Bonus UI] 已綁定本機 Player：" + boundPlayer.name, this);
    }

    private void UnbindCurrentPlayer()
    {
        if (boundPlayer != null && debugSpeedUI)
            Debug.Log("[Damage Bonus UI] 本機 PlayerObject 不存在，解除舊 Player。", this);

        boundPlayer = null;
        boundEnergy = null;
        lastDisplayedEnergy = float.NaN;
        speedSlider.SetValueWithoutNotify(0f);
        SetDamageBonusText(1f);
        ApplyMaximumBoostStyle(false, false);
        ReleaseGaugeFlow();
        SetExtractionPromptVisible(false);
        SetVisualVisible(!hideWhilePlayerMissing);
    }

    private bool HasValidLocalEnergy()
    {
        return boundEnergy != null && boundEnergy.Object != null &&
               boundEnergy.Object.IsValid && LocalPlayerSpectatorController.IsViewedObject(boundEnergy.Object);
    }

    private void RefreshEnergyGauge()
    {
        float energy = HasValidLocalEnergy() ? Mathf.Clamp01(boundEnergy.NormalizedEnergy) : 0f;
        if (!float.IsNaN(lastDisplayedEnergy) &&
            Mathf.Abs(energy - lastDisplayedEnergy) <= speedChangeEpsilon)
            return;

        speedSlider.SetValueWithoutNotify(energy);
        lastDisplayedEnergy = energy;
    }

    private void RefreshDamageBonus()
    {
        float multiplier = HasValidLocalEnergy() ? boundEnergy.DamageMultiplier : 1f;
        SetDamageBonusText(multiplier);
        ApplyMaximumBoostStyle(
            HasValidLocalEnergy() && boundEnergy.IsMaximumBoostActive,
            false);
    }

    private void SetDamageBonusText(float multiplier)
    {
        if (damageBonusLabel == null)
            return;
        string value = $"傷害增加{Mathf.Max(0f, (multiplier - 1f) * 100f):0}%";
        if (damageBonusLabel.text != value)
            damageBonusLabel.text = value;
    }

    private void ApplyMaximumBoostStyle(bool active, bool force)
    {
        if (!force && maximumBoostStyleApplied == active)
            return;
        maximumBoostStyleApplied = active;

        if (damageBonusLabel != null)
        {
            damageBonusLabel.fontSize = normalLabelFontSize;
            damageBonusLabel.color = active ? maximumBoostLabelColor : normalLabelColor;
        }
        if (gaugeRect != null)
            gaugeRect.sizeDelta = normalGaugeSize;
        if (gaugeFillImage != null)
            gaugeFillImage.color = active ? maximumBoostGaugeColor : normalGaugeColor;
    }

    private void RefreshGaugeFlow(float deltaTime)
    {
        if (gaugeFillImage == null) return;
        if (gaugeFlowMaterial == null)
        {
            var shader = Resources.Load<Shader>("Presentation/MomentumGaugeFlow");
            if (shader == null || !shader.isSupported) return;
            originalGaugeMaterial = gaugeFillImage.material;
            gaugeFlowMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            gaugeFillImage.material = gaugeFlowMaterial;
        }
        float rate = gaugeFlowSpeed * (maximumBoostStyleApplied ? maximumBoostFlowSpeedMultiplier : 1f);
        gaugeFlowPhase = Mathf.Repeat(gaugeFlowPhase + Mathf.Max(0, deltaTime) * rate, 1f);
        ApplyGaugeFlowProperties(gaugeFlowMaterial);
        // uGUI may derive a separate stencil material for an ancestor Mask.
        Material rendered = gaugeFillImage.materialForRendering;
        if (rendered != gaugeFlowMaterial) ApplyGaugeFlowProperties(rendered);
    }

    private void ApplyGaugeFlowProperties(Material material)
    {
        if (material == null) return;
        material.SetFloat("_Phase", gaugeFlowPhase);
        material.SetFloat("_Strength", gaugeFlowStrength);
        Sprite sprite = gaugeFillImage.overrideSprite;
        material.SetVector("_SpriteUVRect", sprite != null
            ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0, 0, 1, 1));
    }

    private void ReleaseGaugeFlow()
    {
        if (gaugeFlowMaterial != null)
        {
            if (gaugeFillImage != null && gaugeFillImage.material == gaugeFlowMaterial)
                gaugeFillImage.material = originalGaugeMaterial;
            if (Application.isPlaying) Destroy(gaugeFlowMaterial);
            else DestroyImmediate(gaugeFlowMaterial);
        }
        gaugeFlowMaterial = null;
        originalGaugeMaterial = null;
        gaugeFlowPhase = 0;
    }

    private void RefreshExtractionPrompt()
    {
        if (stageFlowController == null || !stageFlowController.isActiveAndEnabled)
            stageFlowController = FindFirstObjectByType<StageFlowController>();

        bool show = stageFlowController != null &&
                    stageFlowController.IsExtractionAvailable &&
                    stageFlowController.TryGetSelectedExtraction(out var point) &&
                    boundPlayer != null && point.Contains(boundPlayer.transform.position);
        SetExtractionPromptVisible(show);
        if (!show || extractionPromptLabel == null)
            return;

        string value = stageFlowController.ExtractionHoldProgress > 0f
            ? $"撤離倒數  {stageFlowController.RemainingExtractionSeconds:F1}"
            : "等待其他玩家進入撤離區";
        if (extractionPromptLabel.text != value)
            extractionPromptLabel.text = value;
    }

    private void SetExtractionPromptVisible(bool visible)
    {
        if (extractionPromptVisible == visible &&
            (extractionPromptRoot == null || extractionPromptRoot.activeSelf == visible))
            return;
        if (extractionPromptRoot != null && extractionPromptRoot.activeSelf != visible)
            extractionPromptRoot.SetActive(visible);
        extractionPromptVisible = visible;
    }

    private void SetVisualVisible(bool visible)
    {
        if (visualVisible == visible &&
            (speedVisualRoot == null || speedVisualRoot.activeSelf == visible))
            return;
        if (speedVisualRoot != null && speedVisualRoot.activeSelf != visible)
            speedVisualRoot.SetActive(visible);
        visualVisible = visible;
    }
}
