using System;
using System.Reflection;
using Purgers.Progression;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One-time, Editor-safe construction of the Phase 6 presentation tree.</summary>
public static class Phase6RewardHudPrefabSetup
{
    private const string PrefabPath = "Assets/Prefabs/UI/StageHUD.prefab";
    private const string CatalogPath = "Assets/Resources/Progression/PlayerRewardCatalog.asset";
    private const string FontPath =
        "Assets/TextMesh Pro/Fonts/Noto_Sans_TC/NotoSansTC-VariableFont_wght SDF.asset";

    [MenuItem("Tools/Purgers/Phase 6/Build Reward HUD on StageHUD")]
    public static void Build()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (prefabRoot.GetComponentInChildren<LocalPlayerRewardHUD>(true) != null)
                throw new InvalidOperationException(
                    "StageHUD already has Phase6RewardHud; refusing to replace user edits.");

            PlayerRewardCatalog catalog =
                AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(CatalogPath);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (catalog == null || font == null)
                throw new InvalidOperationException("Reward catalog or Traditional Chinese font is missing.");

            RectTransform root = CreateRect("Phase6RewardHud", prefabRoot.transform);
            Stretch(root);
            LocalPlayerRewardHUD hud = root.gameObject.AddComponent<LocalPlayerRewardHUD>();

            CanvasGroup xpGroup = AddGroup(CreateImage("ExperienceGroup", root,
                new Color(0.025f, 0.07f, 0.12f, 0.88f),
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(64f, 42f),
                new Vector2(420f, 112f)).rectTransform);
            xpGroup.alpha = 0f;
            TMP_Text level = CreateText("LevelLabel", xpGroup.transform, font,
                "Lv. 1", 26, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
                new Color(0.95f, 0.98f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -10f), new Vector2(150f, 34f));
            TMP_Text xpLabel = CreateText("ExperienceLabel", xpGroup.transform, font,
                "0 / 100", 20, FontStyles.Normal, TextAlignmentOptions.MidlineRight,
                new Color(0.78f, 0.89f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -15f), new Vector2(220f, 30f));

            RectTransform gauge = CreateRect("ExperienceGauge", xpGroup.transform);
            SetRect(gauge, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(18f, -52f),
                new Vector2(384f, 16f));
            Slider slider = gauge.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.interactable = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            Image gaugeBack = CreateImage("Background", gauge,
                new Color(0.06f, 0.12f, 0.2f, 1f), Vector2.zero,
                Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.zero);
            Stretch(gaugeBack.rectTransform);
            RectTransform fillArea = CreateRect("Fill Area", gauge);
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(2f, 2f);
            fillArea.offsetMax = new Vector2(-2f, -2f);
            Image fill = CreateImage("Fill", fillArea,
                new Color(0.18f, 0.62f, 1f, 1f), Vector2.zero,
                Vector2.one, new Vector2(0f, 0.5f), Vector2.zero,
                Vector2.zero);
            Stretch(fill.rectTransform);
            slider.fillRect = fill.rectTransform;
            slider.targetGraphic = fill;

            RectTransform promptRect = CreateRect("AltPrompt", xpGroup.transform);
            SetRect(promptRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(18f, -78f),
                new Vector2(384f, 26f));
            CanvasGroup prompt = AddGroup(promptRect);
            prompt.alpha = 0f;
            TMP_Text promptText = CreateText("PromptLabel", promptRect, font,
                "按住 ALT 選擇獎勵", 18, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft,
                new Color(1f, 0.83f, 0.3f), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(promptText.rectTransform);

            Image dimImage = CreateImage("BackgroundDim", root,
                Color.black, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(dimImage.rectTransform);
            CanvasGroup dim = AddGroup(dimImage.rectTransform);
            dim.alpha = 0f;

            RectTransform window = CreateRect("ChoiceWindow", root);
            SetRect(window, new Vector2(0.5f, 280f / 1080f),
                new Vector2(0.5f, 280f / 1080f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(980f, 350f));
            CanvasGroup windowGroup = AddGroup(window);
            windowGroup.alpha = 0f;
            TMP_Text title = CreateText("WindowTitle", window, font,
                "獎勵選擇", 34, FontStyles.Bold, TextAlignmentOptions.Center,
                new Color(0.97f, 0.98f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 4f), new Vector2(450f, 50f));

            CardRefs left = CreateCard(window, font, "LeftCard", -314f, "左鍵");
            CardRefs middle = CreateCard(window, font, "MiddleCard", 0f, "中鍵");
            CardRefs right = CreateCard(window, font, "RightCard", 314f, "右鍵");

            SetField(hud, "rewardCatalog", catalog);
            SetField(hud, "experienceGroup", xpGroup);
            SetField(hud, "experienceBar", slider);
            SetField(hud, "experienceFill", fill);
            SetField(hud, "experienceGaugeRoot", gauge);
            SetField(hud, "levelLabel", level);
            SetField(hud, "experienceLabel", xpLabel);
            SetField(hud, "altPrompt", prompt);
            SetField(hud, "altPromptLabel", promptText);
            SetField(hud, "choiceWindow", window);
            SetField(hud, "choiceWindowGroup", windowGroup);
            SetField(hud, "backgroundDim", dim);
            SetField(hud, "windowTitle", title);
            SetCard(hud, "leftCard", left);
            SetCard(hud, "middleCard", middle);
            SetCard(hud, "rightCard", right);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log("[Phase6] Built and wired reward HUD in StageHUD.prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private sealed class CardRefs
    {
        public GameObject Root;
        public TMP_Text Title;
        public TMP_Text Description;
        public TMP_Text MouseButton;
        public Image Icon;
        public TMP_Text FallbackIcon;
    }

    private static CardRefs CreateCard(Transform parent, TMP_FontAsset font,
        string name, float x, string mouseButton)
    {
        Image panel = CreateImage(name, parent,
            new Color(0.035f, 0.105f, 0.17f, 0.96f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(x, -25f),
            new Vector2(286f, 270f));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.31f, 0.7f, 0.95f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        Image icon = CreateImage("Icon", panel.transform, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -26f),
            new Vector2(54f, 54f));
        icon.enabled = false;
        TMP_Text fallback = CreateText("FallbackIcon", panel.transform, font,
            "◆", 48, FontStyles.Bold, TextAlignmentOptions.Center,
            new Color(0.4f, 0.79f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -25f), new Vector2(70f, 62f));
        TMP_Text title = CreateText("Title", panel.transform, font,
            "技能名稱", 23, FontStyles.Bold, TextAlignmentOptions.Center,
            new Color(0.98f, 0.98f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -94f), new Vector2(256f, 36f));
        TMP_Text description = CreateText("Description", panel.transform, font,
            "技能效果說明", 18, FontStyles.Normal, TextAlignmentOptions.Top,
            new Color(0.8f, 0.9f, 0.96f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -139f), new Vector2(248f, 70f));
        TMP_Text key = CreateText("MouseButton", panel.transform, font,
            mouseButton, 20, FontStyles.Bold, TextAlignmentOptions.Center,
            new Color(1f, 0.82f, 0.29f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 12f), new Vector2(180f, 32f));
        return new CardRefs
        {
            Root = panel.gameObject, Title = title,
            Description = description, MouseButton = key,
            Icon = icon, FallbackIcon = fallback
        };
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Color color,
        Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, min, max, pivot, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent,
        TMP_FontAsset font, string content, float size, FontStyles style,
        TextAlignmentOptions alignment, Color color, Vector2 min,
        Vector2 max, Vector2 pivot, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, min, max, pivot, position, dimensions);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = content;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static CanvasGroup AddGroup(RectTransform rect)
    {
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    private static void Stretch(RectTransform rect)
    {
        SetRect(rect, Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max,
        Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    private static void SetCard(LocalPlayerRewardHUD hud, string name,
        CardRefs references)
    {
        FieldInfo field = typeof(LocalPlayerRewardHUD).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(LocalPlayerRewardHUD).Name, name);
        object card = Activator.CreateInstance(field.FieldType, true);
        SetField(card, "root", references.Root);
        SetField(card, "title", references.Title);
        SetField(card, "description", references.Description);
        SetField(card, "mouseButton", references.MouseButton);
        SetField(card, "icon", references.Icon);
        SetField(card, "fallbackIcon", references.FallbackIcon);
        field.SetValue(hud, card);
    }
}
