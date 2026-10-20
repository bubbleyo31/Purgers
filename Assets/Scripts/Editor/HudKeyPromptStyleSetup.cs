using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Purgers.GameFlow.SafeHouse;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>只更新已登錄鍵帽，不重跑 HUD 全版套版。匯出可追查的資產、字體與相對座標。</summary>
public static class HudKeyPromptStyleSetup
{
    public const string StylePath = "Assets/Resources/Presentation/HudKeyPromptStyle.asset";
    public const string InventoryPath = "Documentation/ProjectArchitecture/Validation/HudKeycaps/PromptInventory.json";
    static readonly string[] Prefabs = { "Assets/Prefabs/UI/StageHUD.prefab", "Assets/Prefabs/UI/PlayerRescueHUD.prefab", "Assets/Prefabs/UI/SpectatorHUD.prefab" };
    static readonly string[] Scenes = { "Assets/Scenes/_Menu.unity", "Assets/Scenes/SafeHouse.unity" };

    [MenuItem("Purgers/UI/Apply Shared Keycap Style")]
    public static void ApplyAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring keycaps.");
        if (HudKeyPromptStyle.Shared == null) throw new InvalidOperationException("Missing shared keycap style: " + StylePath);
        // Refuse before touching any asset, so unrelated unsaved scene changes cannot be swept into this save.
        foreach (string path in Scenes)
        {
            var loaded = SceneManager.GetSceneByPath(path);
            if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
                throw new InvalidOperationException("Scene has unsaved changes; not saved by this tool: " + path);
        }
        var records = new List<object>();
        foreach (string path in Prefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { ApplyRoot(root, path, records); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (string path in Scenes)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects()) ApplyRoot(root, path, records);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + path);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(InventoryPath));
        File.WriteAllText(InventoryPath, Newtonsoft.Json.JsonConvert.SerializeObject(records, Newtonsoft.Json.Formatting.Indented));
        Debug.Log("[HudKeyPromptStyle] Updated and recorded " + records.Count + " authored prompts.");
    }

    static T Ref<T>(Component owner, string field) where T : UnityEngine.Object =>
        new SerializedObject(owner).FindProperty(field).objectReferenceValue as T;

    public static void ApplyRoot(GameObject root, string source, List<object> records)
    {
        foreach (var hud in root.GetComponentsInChildren<LocalPlayerBattleAbilityHUD>(true))
        {
            Paint(Ref<TMP_Text>(hud, "grappleKeyLabel"), Ref<Image>(hud, "grappleKeyBackground"),
                Ref<Image>(hud, "grappleFill").rectTransform, KeyCode.Q, "Grapple", source, records);
            var template = Ref<GameObject>(hud, "slotTemplate");
            var key = template.transform.Find("Key").GetComponent<Image>();
            Paint(key.GetComponentInChildren<TMP_Text>(true), key, template.transform as RectTransform,
                KeyCode.E, "GrappleHit / GrappleFocus", source, records);
        }
        foreach (var hud in root.GetComponentsInChildren<LocalPlayerWeaponHUD>(true))
            Paint(Ref<TMP_Text>(hud, "reloadKeyLabel"), Ref<Image>(hud, "reloadKeyBackground"),
                null, KeyCode.R, "Reload", source, records);
        foreach (var hud in root.GetComponentsInChildren<LocalPlayerRescueHUD>(true))
        {
            var key = Ref<GameObject>(hud, "keyPrompt");
            if (key != null) Paint(key.GetComponentInChildren<TMP_Text>(true), key.GetComponent<Image>(),
                null, KeyCode.E, "Interact / Rescue", source, records);
        }
        foreach (var hud in root.GetComponentsInChildren<SafeHouseHudController>(true))
        {
            var button = Ref<Button>(hud, "readyButton");
            if (button != null)
            {
                PaintCap(button.transform.Find("Keycap"), KeyCode.Tab, "Ready / TeamInfo", source, records);
                HudKeyPromptLayout.PlaceDescriptionAfter(Ref<TMP_Text>(hud, "readyButtonLabel"), button.transform.Find("Keycap").GetComponent<Image>());
            }
            var panel = Ref<GameObject>(hud, "readyPanelRoot");
            if (panel != null)
            {
                PaintCap(panel.transform.Find("CancelKeycap"), KeyCode.Escape, "Cancel vote (fixed)", source, records);
                var hint = panel.transform.Find("CancelHint"); var cap = panel.transform.Find("CancelKeycap");
                if (hint != null && cap != null) HudKeyPromptLayout.PlaceDescriptionAfter(hint.GetComponent<TMP_Text>(), cap.GetComponent<Image>());
            }
        }
        foreach (var hud in root.GetComponentsInChildren<LocalSpectatorHUD>(true))
        {
            Inside(Ref<RectTransform>(hud, "previousKey").GetComponent<TMP_Text>(), KeyCode.Q, "Previous spectator (fixed)", source, records);
            Inside(Ref<RectTransform>(hud, "nextKey").GetComponent<TMP_Text>(), KeyCode.E, "Next spectator (fixed)", source, records);
        }
        foreach (var hud in root.GetComponentsInChildren<LocalPlayerRewardHUD>(true))
        {
            var rewardLabel = Ref<TMP_Text>(hud, "altPromptLabel");
            if (rewardLabel != null)
            {
                new HudKeyPromptRow(rewardLabel).RefreshWithPrefix(KeyCode.LeftAlt, "按住", "選擇升級獎勵");
                var cap = rewardLabel.transform.Find("SharedPromptRow/Key/SharedKeycap").GetComponent<Image>();
                Record(cap.GetComponentInChildren<TMP_Text>(true), cap, null, "Rewards", source, records);
            }
            var so = new SerializedObject(hud);
            string[] cards = { "leftCard", "middleCard", "rightCard" };
            KeyCode[] keys = { KeyCode.Mouse0, KeyCode.Mouse2, KeyCode.Mouse1 };
            for (int i = 0; i < cards.Length; i++)
            {
                var label = so.FindProperty(cards[i]).FindPropertyRelative("mouseButton").objectReferenceValue as TMP_Text;
                Inside(label, keys[i], "Reward choice (fixed)", source, records);
            }
        }
        foreach (var view in root.GetComponentsInChildren<SafeHouseStartTerminalView>(true))
            Row(Ref<TMP_Text>(view, "promptLabel"), KeyCode.E, "開始遊戲", true, "Interact / Start terminal", source, records);
    }

    static void PaintCap(Transform cap, KeyCode key, string action, string source, List<object> records)
    {
        if (cap != null) Paint(cap.GetComponentInChildren<TMP_Text>(true), cap.GetComponent<Image>(), null, key, action, source, records);
    }
    static void Paint(TMP_Text label, Image image, RectTransform above, KeyCode key, string action, string source, List<object> records)
    {
        if (label == null || image == null) return;
        new HudKeyPromptLayout(label, image, above).Refresh(key);
        Record(label, image, above, action, source, records);
    }
    static void Inside(TMP_Text host, KeyCode key, string action, string source, List<object> records)
    {
        if (host == null) return;
        HudKeyPromptLayout.Inside(host, action.StartsWith("Previous") ? "‹" : action.StartsWith("Next") ? "›" : null).Refresh(key);
        var cap = host.transform.Find("SharedKeycap").GetComponent<Image>();
        Record(cap.GetComponentInChildren<TMP_Text>(true), cap, null, action, source, records);
    }
    static void Row(TMP_Text host, KeyCode key, string text, bool centered, string action, string source, List<object> records)
    {
        if (host == null) return;
        new HudKeyPromptRow(host, centered).Refresh(key, text);
        var cap = host.transform.Find("SharedPromptRow/Key/SharedKeycap").GetComponent<Image>();
        Record(cap.GetComponentInChildren<TMP_Text>(true), cap, null, action, source, records);
    }
    static string Hierarchy(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
    static void Record(TMP_Text label, Image image, RectTransform above, string action, string source, List<object> records)
    {
        var r = image.rectTransform;
        var canvas = image.GetComponentInParent<Canvas>();
        var corners = new Vector3[4]; r.GetWorldCorners(corners);
        var ancestors = new List<object>();
        for (Transform t = r.parent; t != null; t = t.parent)
        {
            var rect = t as RectTransform;
            ancestors.Add(new { name = t.name, localPosition = t.localPosition.ToString("F4"),
                localScale = t.localScale.ToString("F4"), rotation = t.localEulerAngles.ToString("F4"),
                anchorMin = rect == null ? null : rect.anchorMin.ToString("F4"), anchorMax = rect == null ? null : rect.anchorMax.ToString("F4"),
                pivot = rect == null ? null : rect.pivot.ToString("F4"), size = rect == null ? null : rect.sizeDelta.ToString("F4"),
                position = rect == null ? null : rect.anchoredPosition.ToString("F4") });
        }
        records.Add(new {
            source, action, path = Hierarchy(r), style = StylePath, placementChain = ancestors,
            background = AssetDatabase.GetAssetPath(image.sprite), imageType = image.type.ToString(),
            font = AssetDatabase.GetAssetPath(label.font), fontMaterial = AssetDatabase.GetAssetPath(label.fontSharedMaterial),
            alignment = label.alignment.ToString(), fontSize = label.fontSize, fontMin = label.fontSizeMin, fontMax = label.fontSizeMax,
            text = label.text, textColor = ColorUtility.ToHtmlStringRGBA(label.color), backgroundColor = ColorUtility.ToHtmlStringRGBA(image.color),
            anchorMin = r.anchorMin.ToString("F4"), anchorMax = r.anchorMax.ToString("F4"), pivot = r.pivot.ToString("F4"),
            position = r.anchoredPosition.ToString("F4"), size = r.sizeDelta.ToString("F4"), scale = r.localScale.ToString("F4"),
            worldBottomLeft = corners[0].ToString("F4"), worldTopRight = corners[2].ToString("F4"),
            labelOffsetMin = label.rectTransform.offsetMin.ToString("F4"), labelOffsetMax = label.rectTransform.offsetMax.ToString("F4"),
            above = above == null ? null : Hierarchy(above),
            canvas = canvas == null ? null : Hierarchy(canvas.transform), renderMode = canvas == null ? null : canvas.renderMode.ToString()
        });
    }
}
