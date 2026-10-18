#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只建立獨立救援 UI Prefab；不修改 Scene 或既有 HUD 排版。</summary>
public static class PlayerRescueHudSetup
{
    public const string PrefabPath = "Assets/Prefabs/UI/PlayerRescueHUD.prefab";
    private const string RingPath = "Assets/_Project_Assets/UI/BattleHUD/Layout/LevelRing.png";
    private const string StagePath = "Assets/Prefabs/UI/StageHUD.prefab";

    [MenuItem("Purgers/Setup/Create Rescue HUD Prefab")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var root = new GameObject("PlayerRescueHUD", typeof(RectTransform), typeof(LocalPlayerRescueHUD));
        try { Configure(root); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // 僅本輪線形原型的明確遷移入口；不在載入或建立時自動覆寫人工設定。
    public static void UpgradeCircularPrototype()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請退出 Play Mode");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Configure(root); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Configure(GameObject root)
    {
        var stage = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
        var key = stage.transform.Find("ReferenceFrame/BattleAbilityHUD/AbilitySlots/AbilitySlotTemplate/Key");
        var keyImage = key.GetComponent<Image>();
        var keyLabel = key.Find("Label").GetComponent<TMP_Text>();
        var battle = new SerializedObject(stage.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true));
        Color ringColor = battle.FindProperty("slotPressedColor").colorValue;
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
        if (sprite == null) throw new InvalidOperationException("既有空心圓環素材不存在：" + RingPath);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(440, 190); rect.anchoredPosition = new Vector2(0, -180);
        var body = Child(root.transform, "Content", Vector2.zero, rect.sizeDelta);
        var panel = body.GetComponent<Image>(); if (panel != null) UnityEngine.Object.DestroyImmediate(panel);
        var old = body.Find("Progress"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        var ringRect = Child(body, "ProgressRing", new Vector2(0, 28), new Vector2(120, 120));
        var ring = ringRect.GetComponent<Image>() ?? ringRect.gameObject.AddComponent<Image>();
        ring.sprite = sprite; ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top; ring.fillClockwise = true; ring.fillAmount = 0;
        ring.preserveAspect = true; ring.color = ringColor; ring.raycastTarget = false;
        var button = Child(body, "Key", new Vector2(0, 28), new Vector2(60, 60));
        var background = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        background.color = keyImage.color; background.sprite = keyImage.sprite; background.material = keyImage.material;
        background.type = Image.Type.Sliced; background.raycastTarget = false;
        var label = Text(button, "Label", Vector2.zero, keyLabel.font, keyLabel.color, 38, new Vector2(60, 60));
        label.text = "E";
        var status = Text(body, "Status", new Vector2(0, -49), keyLabel.font, keyImage.color, 20, new Vector2(440, 30));
        var time = Text(body, "Time", new Vector2(0, -78), keyLabel.font, keyImage.color, 22, new Vector2(440, 30));
        var so = new SerializedObject(root.GetComponent<LocalPlayerRescueHUD>());
        so.FindProperty("displayRoot").objectReferenceValue = body.gameObject;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("timeText").objectReferenceValue = time;
        so.FindProperty("progressRing").objectReferenceValue = ring;
        so.FindProperty("keyPrompt").objectReferenceValue = button.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo(); body.gameObject.SetActive(false);
    }

    private static RectTransform Child(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var rect = parent.Find(name) as RectTransform;
        if (rect == null) { rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); }
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    private static TMP_Text Text(Transform parent, string name, Vector2 position, TMP_FontAsset font, Color color, float size, Vector2 bounds)
    {
        var rect = Child(parent, name, position, bounds);
        var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.Center;
        text.color = color; text.raycastTarget = false; return text;
    }
}
#endif
