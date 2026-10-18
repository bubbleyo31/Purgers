#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>建立獨立護目鏡 Prefab，僅在乾淨 _Menu 局部新增一份；不重排或覆寫既有 UI。</summary>
public static class FirstPersonGogglesSetup
{
    public const string PrefabPath = "Assets/Prefabs/UI/FirstPersonGoggles.prefab";
    public const string MenuPath = "Assets/Scenes/_Menu.unity";
    public const string ParentPath = "GameplayHUD Canvas/PlayerHUDCanvas";
    [MenuItem("Tools/Purgers/UI/Create And Wire First Person Goggles")]
    public static void CreateAndWire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        var menu = SceneManager.GetSceneByPath(MenuPath);
        if (!menu.IsValid() || !menu.isLoaded || menu.isDirty) throw new InvalidOperationException("需要已開啟、沒有未儲存變更的 _Menu；不自動保存其他工作。");
        var parent = menu.GetRootGameObjects().FirstOrDefault(g => g.name == "GameplayHUD Canvas")?.transform.Find("PlayerHUDCanvas");
        if (parent == null) throw new InvalidOperationException("找不到既有 PlayerHUDCanvas。");
        var existing = menu.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LocalFirstPersonGoggles>(true)).ToArray();
        if (existing.Length > 1) throw new InvalidOperationException("已有多份護目鏡，請先核對重複來源。");
        if (existing.Length == 1) return; // 保留人工值，不重置已配置效果。
        var before = menu.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true))
            .Where(c => c != null && c != parent).ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
        EnsurePrefab();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = "FirstPersonGoggles"; instance.transform.SetAsFirstSibling();
        var canvas = instance.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = -10; canvas.sortingLayerID = parent.GetComponent<Canvas>().rootCanvas.sortingLayerID;
        PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
        foreach (var pair in before)
            if (pair.Key != null && EditorJsonUtility.ToJson(pair.Key) != pair.Value)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                throw new InvalidOperationException("既有 UI 元件發生非預期變化，未保存：" + pair.Key.name);
            }
        EditorSceneManager.MarkSceneDirty(menu);
        if (!EditorSceneManager.SaveScene(menu)) throw new IOException("無法儲存 _Menu。");
        Directory.CreateDirectory("Temp/GogglesValidation");
        File.WriteAllText("Temp/GogglesValidation/wiring.txt", "Configured " + ParentPath + "/FirstPersonGoggles\n" +
            "Preserved serialized pre-existing components: " + before.Count + "\nCanvas order=-10; no raycaster; central view has no geometry.\n");
        Selection.activeGameObject = instance;
    }
    public static void EnsurePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("FirstPersonGoggles", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, scene);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true; canvas.sortingOrder = -10;
            var group = root.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var graphic = root.AddComponent<GoggleRimGraphic>(); graphic.raycastTarget = false;
            root.AddComponent<LocalFirstPersonGoggles>(); graphic.enabled = false;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null) throw new IOException("護目鏡 Prefab 儲存失敗。");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    public static string RenderPreview(int width = 1920, int height = 1080, float thickness = 1, float opacity = .94f, bool rewards = false)
    {
        string label = "goggles-" + width + "x" + height + "-t" + thickness.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
            "-a" + opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + (rewards ? "-rewards" : "");
        string path = BattleHudReferenceLayout.RenderPreview(rewards, width, height, 100, false, -1, (player, stage) =>
        {
            var parent = player.transform.parent;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var root = UnityEngine.Object.Instantiate(prefab, parent); root.transform.SetAsFirstSibling();
            root.GetComponent<LocalFirstPersonGoggles>().enabled = false;
            var canvas = root.GetComponent<Canvas>(); UnityEngine.Object.DestroyImmediate(canvas);
            var rect = root.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
            var graphic = root.GetComponent<GoggleRimGraphic>(); graphic.Thickness = thickness; graphic.Opacity = opacity; graphic.enabled = true;
            var camera = parent.GetComponent<Canvas>().worldCamera;
            camera.backgroundColor = new Color(.24f, .29f, .31f);
        }, label);
        string destination = "Documentation/ProjectArchitecture/Validation/FirstPersonGoggles/" + label + ".png";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination, true); File.Delete(path);
        return Path.GetFullPath(destination);
    }
}
#endif


