#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只補技能時間條與既有護盾血格引用；不重排任何已存在的 HUD 物件。</summary>
public static class ActiveAbilityHudSetup
{
    private const string StageHudPath = "Assets/Prefabs/UI/StageHUD.prefab";
    private const string FontPath = "Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset";

    [MenuItem("Tools/Purgers/UI/Add Active Ability Timer To Stage HUD")]
    public static void ApplyStageHudPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(StageHudPath);
        try
        {
            foreach (var hud in contents.GetComponentsInChildren<LocalPlayerBattleAbilityHUD>(true))
                EnsureAbilityHud(hud);
            PrefabUtility.SaveAsPrefabAsset(contents, StageHudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    [MenuItem("Tools/Purgers/UI/Connect Shield Cells In Loaded Scenes")]
    public static void ConnectLoadedHealthHuds()
    {
        foreach (var hud in Resources.FindObjectsOfTypeAll<LocalPlayerHealthSlider>())
        {
            if (EditorUtility.IsPersistent(hud) || !hud.gameObject.scene.IsValid() || !hud.gameObject.scene.isLoaded) continue;
            if (EnsureHealthHud(hud)) EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        }
        // 不自動儲存 Scene，保留呼叫端對既有場景修改的處置。
    }

    public static void EnsureAbilityHud(LocalPlayerBattleAbilityHUD hud)
    {
        if (hud == null) return;
        var serialized = new SerializedObject(hud);
        GameObject root = serialized.FindProperty("timedAbilityRoot").objectReferenceValue as GameObject;
        RectTransform bar;
        bool createdBar = false;
        if (root != null) bar = root.GetComponent<RectTransform>();
        else bar = FindOrCreate(hud.transform, "TimedAbilityBar", out createdBar);
        if (bar == null) return;

        // 只有第一次建立的根物件才設定預設位置。重跑不能覆蓋作者的手動座標。
        if (createdBar)
        {
            bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0f);
            bar.pivot = new Vector2(.5f, .5f);
            bar.anchoredPosition = new Vector2(0f, 280f);
            bar.sizeDelta = new Vector2(400f, 18f);
        }
        EnsureImage(bar, new Color(.04f, .06f, .08f, .75f));
        RectTransform fill = FindOrCreate(bar, "Fill", out bool newFill);
        if (newFill)
        {
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, .5f); fill.offsetMin = fill.offsetMax = Vector2.zero;
        }
        Image fillImage = EnsureImage(fill, new Color(.25f, 1f, .9f, 1f));
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        TMP_Text name = EnsureText(bar, "Name", font, new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(0f, 7f), new Vector2(285f, 28f), TextAlignmentOptions.BottomLeft, 22f);
        TMP_Text state = EnsureText(bar, "State", font, Vector2.zero, new Vector2(0f, 1f),
            new Vector2(0f, -6f), new Vector2(290f, 26f), TextAlignmentOptions.TopLeft, 18f);
        TMP_Text seconds = EnsureText(bar, "Seconds", font, Vector2.one, new Vector2(1f, 0f),
            new Vector2(0f, 7f), new Vector2(100f, 28f), TextAlignmentOptions.BottomRight, 22f);
        AssignIfMissing(serialized, "timedAbilityRoot", bar.gameObject);
        AssignIfMissing(serialized, "timedAbilityFill", fillImage);
        AssignIfMissing(serialized, "timedAbilityName", name);
        AssignIfMissing(serialized, "timedAbilityState", state);
        AssignIfMissing(serialized, "timedAbilitySeconds", seconds);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(hud);
        bar.gameObject.SetActive(false);
    }

    public static bool EnsureHealthHud(LocalPlayerHealthSlider hud)
    {
        if (hud == null) return false;
        var serialized = new SerializedObject(hud);
        SerializedProperty reference = serialized.FindProperty("healthSegmentView");
        if (reference.objectReferenceValue != null) return false;
        var slider = serialized.FindProperty("healthSlider").objectReferenceValue as Slider;
        var view = slider != null ? slider.GetComponentInChildren<LocalHealthSegmentView>(true) : null;
        if (view == null)
        {
            Debug.LogWarning("[技能 HUD] 找不到此生命 Slider 下的原血格；未自動建立或移動血量 UI。", hud);
            return false;
        }
        reference.objectReferenceValue = view;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(hud);
        return true;
    }

    private static void AssignIfMissing(SerializedObject target, string name, Object value)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property.objectReferenceValue == null) property.objectReferenceValue = value;
    }

    private static RectTransform FindOrCreate(Transform parent, string name, out bool created)
    {
        Transform existing = parent.Find(name);
        created = existing == null;
        if (!created) return existing as RectTransform;
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static Image EnsureImage(RectTransform rect, Color color)
    {
        Image image = rect.GetComponent<Image>();
        if (image == null)
        {
            image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.type = Image.Type.Simple;
        }
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text EnsureText(RectTransform parent, string name, TMP_FontAsset font,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, TextAlignmentOptions alignment, float fontSize)
    {
        RectTransform rect = FindOrCreate(parent, name, out bool created);
        if (created)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null)
        {
            text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = string.Empty;
        }
        text.raycastTarget = false;
        return text;
    }
}
#endif
