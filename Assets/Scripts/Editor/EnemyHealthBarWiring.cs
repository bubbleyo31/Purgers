using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>使用者授權的正式血條接線；只補缺失項目，不覆寫既有人工設定，不修改共用基底或 Boss。</summary>
public static class EnemyHealthBarWiring
{
    public static readonly string[] TargetNames = { "Enemy_Melee_A", "Enemy_Melee_B", "Enemy_Ranged_A", "Enemy_Ranged_B",
        "Enemy_Ranged_C_Beam", "Enemy_Ranged_C_Projectile", "Enemy_Ranged_Reflector", "Enemy_Flytrap", "Enemy_Suicide" };
    public static string PathFor(string name) => "Assets/Prefabs/Enemy/Variants/" + name + ".prefab";

    [MenuItem("Tools/Purgers/UI/Wire Approved Enemy Health Bars")]
    public static void WireApprovedAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先結束 Play Mode。");
        var menu = SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        if (!menu.IsValid() || !menu.isLoaded || menu.isDirty)
            throw new InvalidOperationException("需已開啟且無未儲存修改的 _Menu；不代存其他修改。");
        var parent = menu.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name == "PlayerHUDCanvas" && t.parent != null && t.parent.name == "GameplayHUD Canvas");
        var canvas = parent.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) throw new InvalidOperationException("HUD 需要 Screen Space Canvas。");
        var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyHealthBarSetup.PrefabPath);
        if (hudPrefab == null || hudPrefab.GetComponent<LocalEnemyHealthBarHUD>().Settings == null)
            throw new InvalidOperationException("缺少正式血條 Prefab／Settings。");
        foreach (var name in TargetNames)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(name));
            if (asset == null || asset.GetComponent<TestDamageReceiver>() == null || asset.GetComponent<NetworkObject>() == null)
                throw new InvalidOperationException("缺少既有生命或 NetworkObject：" + name);
        }
        var report = new List<string>();
        foreach (var name in TargetNames)
        {
            string path = PathFor(name);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var target = root.GetComponent<EnemyHealthBarTarget>();
                if (target != null) { report.Add(name + " | 已配置，保留人工值"); continue; }
                var renderers = MainBody(root, name).ToArray();
                if (renderers.Length == 0) throw new InvalidOperationException("找不到已核對的主要身體：" + name);
                var bounds = BodyBounds(root.transform, renderers);
                var anchor = new GameObject("EnemyHealthBarAnchor").transform;
                anchor.SetParent(root.transform, false);
                anchor.localPosition = bounds.center + Vector3.up * (bounds.extents.y + .05f);
                target = root.AddComponent<EnemyHealthBarTarget>();
                var so = new SerializedObject(target);
                bool large = name == "Enemy_Melee_A" || name == "Enemy_Melee_B" || (name.StartsWith("Enemy_Ranged_") && name != "Enemy_Ranged_Reflector");
                so.FindProperty("displayMode").enumValueIndex = (int)(large ? EnemyHealthBarTarget.DisplayMode.Large : EnemyHealthBarTarget.DisplayMode.Normal);
                so.FindProperty("headAnchor").objectReferenceValue = anchor;
                so.FindProperty("bodyCenter").vector3Value = bounds.center;
                so.FindProperty("bodySize").vector3Value = bounds.size;
                so.ApplyModifiedPropertiesWithoutUndo();
                new NetworkObjectBaker().Bake(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new InvalidOperationException("儲存失敗：" + path);
                report.Add(name + " | " + (large ? "Large" : "Normal") + " | center=" + bounds.center.ToString("F3") + " | size=" + bounds.size.ToString("F3") + " | anchor=" + anchor.localPosition.ToString("F3") + " | meshes=" + string.Join(",", renderers.Select(r => r.name)));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var huds = menu.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LocalEnemyHealthBarHUD>(true)).ToArray();
        if (huds.Length > 1) throw new InvalidOperationException("場景已存在多份敵人血條 HUD，請先確認重複來源。");
        if (huds.Length == 0)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, parent);
            instance.name = "EnemyHealthBarDisplay";
            EditorSceneManager.MarkSceneDirty(menu);
            if (!EditorSceneManager.SaveScene(menu)) throw new InvalidOperationException("_Menu 儲存失敗。");
            report.Add("_Menu/GameplayHUD Canvas/PlayerHUDCanvas/EnemyHealthBarDisplay | 已建立並保存唯一 HUD");
        }
        else report.Add("_Menu HUD | 已存在，保留引用與人工值");
        Directory.CreateDirectory("Temp/EnemyHealthBarWiring");
        File.WriteAllLines("Temp/EnemyHealthBarWiring/wiring.txt", report);
        Debug.Log("敵人血條接線完成：9 個一般敵人與 _Menu HUD；Boss／共用基底未修改。");
    }

    private static IEnumerable<Renderer> MainBody(GameObject root, string name)
    {
        var visible = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled);
        // 已人工核對模型命名：樹怪取頭、軀幹與雙腿，不納入伸出的手臂／枝葉末端。
        if (name.StartsWith("Enemy_Melee_"))
            return visible.Where(r => r is SkinnedMeshRenderer && new[] { "球体", "球体.001", "立方体.049", "立方体.033", "立方体.064" }.Contains(r.name));
        // 水母取主要球形身體及頭頂，不將 Spiral 觸手納入定位盒。
        if (name.StartsWith("Enemy_Ranged_") && name != "Enemy_Ranged_Reflector")
            return visible.Where(r => r is SkinnedMeshRenderer && new[] { "球体", "球体.001", "球体.002", "球体.003", "球体.004" }.Contains(r.name));
        if (name == "Enemy_Ranged_Reflector") return visible.Where(r => r.name == "Body" || r.name == "Head" || r.name == "Body (3)" || r.name == "Body (4)");
        return visible.Where(r => r.name == "VisualRoot");
    }

    private static Bounds BodyBounds(Transform root, Renderer[] renderers)
    {
        bool initialized = false; var result = new Bounds();
        // 這批匯入骨架在離線 Prefab 上 BakeMesh 會產生錯誤比例；以已匯入的 Renderer 世界 Bounds 轉回 Root。
        foreach (var renderer in renderers)
        {
            var bounds = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                p = root.InverseTransformPoint(p);
                if (!initialized) { result = new Bounds(p, Vector3.zero); initialized = true; } else result.Encapsulate(p);
            }
        }
        if (!initialized || result.size.x <= 0 || result.size.y <= 0 || result.size.z <= 0) throw new InvalidOperationException("主要身體網格範圍無效。");
        return result;
    }
}