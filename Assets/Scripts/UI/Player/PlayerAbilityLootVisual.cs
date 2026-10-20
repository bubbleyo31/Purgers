using TMPro;
using UnityEngine;

/// <summary>掉落 Runtime 的本機圖示與金色標記；不含 Collider，不執行拾取或計時。</summary>
public sealed class PlayerAbilityLootVisual : MonoBehaviour
{
    [Header("掉落技能辨識")]
    [SerializeField, Min(.1f), Tooltip("圖示外框寬度（公尺），預設 0.85；只影響呈現，不改拾取距離。")]
    private float markerSize = .85f;
    [SerializeField, Tooltip("圖示外框與標記柱的金色；不產生實體光源或陰影。")]
    private Color markerColor = new Color32(255, 195, 45, 255);
    private PlayerAbilityRuntime runtime;
    private Transform billboard;
    private SpriteRenderer frame;
    private TextMeshPro label;
    private Sprite solidSprite;
    private HudKeyPromptRow pickupPrompt;
    private GameObject pickupPromptRoot;

    public void Bind(PlayerAbilityRuntime source)
    {
        runtime = source;
        solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width,
            Texture2D.whiteTexture.height), Vector2.one * .5f, Texture2D.whiteTexture.width);
        billboard = new GameObject("SkillMarker").transform;
        billboard.SetParent(transform, false);
        float size = Mathf.Max(.1f, markerSize);
        float frameCenterY = Mathf.Max(.8f, size * .5f + .15f);
        float beaconBottom = -.15f;
        float beaconTop = frameCenterY - size * .5f;
        // 垂直金柱與大面積底框在暗處仍可讀；不建立 Light / Collider / NetworkObject。
        CreatePanel("Beacon", new Vector3(0, (beaconBottom + beaconTop) * .5f, .02f),
            new Vector2(.08f, beaconTop - beaconBottom), markerColor);
        frame = CreatePanel("GoldFrame", Vector3.up * frameCenterY, Vector2.one * size, markerColor);
        CreatePanel("DarkBacking", new Vector3(0, frameCenterY, -.01f), Vector2.one * size * .87f,
            new Color32(18, 20, 15, 255));
        var iconObject = new GameObject("Icon");
        iconObject.transform.SetParent(billboard, false);
        iconObject.transform.localPosition = new Vector3(0, frameCenterY, -.02f);
        var icon = iconObject.AddComponent<SpriteRenderer>();
        icon.sprite = source.Definition != null ? source.Definition.HudIcon : null;
        if (icon.sprite != null)
        {
            float extent = Mathf.Max(icon.sprite.bounds.size.x, icon.sprite.bounds.size.y);
            iconObject.transform.localScale = Vector3.one * (size * .74f / Mathf.Max(.01f, extent));
        }
        var textObject = new GameObject("Label"); textObject.transform.SetParent(billboard, false);
        textObject.transform.localPosition = new Vector3(0, frameCenterY + .75f, -.03f);
        label = textObject.AddComponent<TextMeshPro>(); label.fontSize = 2.4f;
        TMP_FontAsset fallbackFont = null;
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (!font.name.Contains("NotoSansTC")) continue;
            if (fallbackFont == null) fallbackFont = font;
            if (font.name.Contains("Bold")) { fallbackFont = font; break; }
        }
        if (fallbackFont != null) label.font = fallbackFont;
        label.alignment = TextAlignmentOptions.Center; label.rectTransform.sizeDelta = new Vector2(6, 2);
        label.color = Color.white; label.faceColor = new Color32(255, 235, 177, 255);
        label.fontStyle = FontStyles.Bold; label.enableWordWrapping = false;
        // 僅調整此文字的材質實例：名稱、等級、冷卻數字與提示一起增加筆畫厚度。
        if (label.fontMaterial.HasProperty(ShaderUtilities.ID_FaceDilate))
            label.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, .12f);
        label.outlineWidth = .10f; label.outlineColor = new Color32(15, 15, 12, 255);
        // World-space prompt uses the same uGUI keycap art as the screen HUD.
        var promptCanvas = new GameObject("PickupPrompt", typeof(RectTransform), typeof(Canvas));
        pickupPromptRoot = promptCanvas;
        promptCanvas.transform.SetParent(billboard, false);
        promptCanvas.transform.localPosition = new Vector3(0, frameCenterY + 1.15f, -.03f);
        promptCanvas.transform.localScale = Vector3.one * .01f;
        promptCanvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var promptRect = (RectTransform)promptCanvas.transform;
        promptRect.sizeDelta = new Vector2(480, 40);
        var promptText = new GameObject("Prompt", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        promptText.transform.SetParent(promptCanvas.transform, false);
        promptText.font = label.font;
        promptText.color = new Color32(241, 237, 217, 255);
        promptText.rectTransform.sizeDelta = promptRect.sizeDelta;
        pickupPrompt = new HudKeyPromptRow(promptText, true);
        RefreshLabel(false);
    }

    private SpriteRenderer CreatePanel(string objectName, Vector3 position, Vector2 size, Color color)
    {
        var panel = new GameObject(objectName); panel.transform.SetParent(billboard, false);
        panel.transform.localPosition = position; panel.transform.localScale = new Vector3(size.x, size.y, 1);
        var renderer = panel.AddComponent<SpriteRenderer>(); renderer.sprite = solidSprite; renderer.color = color;
        return renderer;
    }

    private void LateUpdate()
    {
        if (runtime == null || runtime.Object == null || !runtime.Object.IsValid || !runtime.IsWorldLoot) return;
        var camera = Camera.main;
        if (camera != null) billboard.rotation = camera.transform.rotation;
        billboard.localPosition = Vector3.up * (Mathf.Sin(Time.unscaledTime * 2f) * .06f);
        bool highlighted = false;
        var runner = runtime.Runner;
        if (runner != null && runner.TryGetPlayerObject(runner.LocalPlayer, out var obj) && obj != null &&
            obj.TryGetComponent<PlayerAbilityRuntimeManager>(out var manager))
            highlighted = manager.FindLootCandidateForPresentation() == runtime;
        frame.color = highlighted ? new Color32(255, 244, 196, 255) : markerColor;
        RefreshLabel(highlighted);
    }

    private void RefreshLabel(bool highlighted)
    {
        if (runtime == null || runtime.Definition == null) return;
        bool ready = runtime.Object != null && runtime.Object.IsValid;
        float remaining = ready ? runtime.LootCooldownRemaining : 0f;
        int level = ready ? Mathf.Max(1, runtime.SkillLevel) : 1;
        string suffix = remaining > 0 ? $" · {remaining:0.0}s" : string.Empty;
        label.text = $"{runtime.Definition.DisplayName} Lv.{level}{suffix}";
        if (pickupPromptRoot != null) pickupPromptRoot.SetActive(highlighted);
        if (highlighted && pickupPrompt != null)
        {
            var key = Purgers.Settings.PlayerSettingsService.Key(Purgers.Settings.PlayerBinding.Interact);
            pickupPrompt.Refresh(key, "拾取 / 替換", Input.GetKey(key));
        }
    }

    private void OnDestroy()
    {
        if (solidSprite == null) return;
        if (Application.isPlaying) Destroy(solidSprite); else DestroyImmediate(solidSprite);
    }
}
