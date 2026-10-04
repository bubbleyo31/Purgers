#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>一次性升級原型外觀；只處理已知舊示意模型，不覆寫人工配置的呈現元件。</summary>
public static class PlayerActiveAbilityVisualSetup
{
    public const string VisualRoot = "Assets/Prefabs/AbilityRuntime/Active/Visuals";

    [MenuItem("Tools/Player Ability/補齊 E 技能投射物外觀接口（保留人工設定）", priority = 122)]
    public static void CreateMissingPresentationAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("請先離開 Play Mode 再建立外觀資產。");
        EnsureFolder(VisualRoot);
        UpgradeProjectile("Grenade", CreateVisual("Grenade"));
        UpgradeProjectile("HealingPack", CreateVisual("HealingPack"));
        UpgradeProjectile("Ricochet", CreateVisual("Ricochet"));
        AssetDatabase.SaveAssets();
        Debug.Log("[E 技能外觀] 已補齊缺少的投射物外觀接口；未修改場景、玩家 Prefab、碰撞或技能數值。爆炸／貫穿砲／扇波特效插槽保留空白供美術指定。");
    }

    private static GameObject CreateVisual(string kind)
    {
        string path = VisualRoot + "/" + kind + "Visual.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var root = new GameObject(kind + "Visual");
        try
        {
            if (kind == "Grenade")
            {
                // 膠囊長軸轉向 +Z，異色且較寬的彈頭在前，小尾段在後。
                AddPrimitive(root, "榴彈膠囊本體", PrimitiveType.Capsule, new Vector3(0f, 0f, -0.035f),
                    Quaternion.Euler(90f, 0f, 0f), new Vector3(0.15f, 0.18f, 0.15f),
                    Material("GrenadeBody", new Color(0.26f, 0.31f, 0.34f)));
                AddPrimitive(root, "異色重彈頭", PrimitiveType.Sphere, new Vector3(0f, 0f, 0.12f),
                    Quaternion.identity, new Vector3(0.19f, 0.19f, 0.18f),
                    Material("GrenadeNose", new Color(1f, 0.76f, 0.08f)));
                AddPrimitive(root, "輕尾段", PrimitiveType.Cube, new Vector3(0f, 0f, -0.22f),
                    Quaternion.identity, new Vector3(0.075f, 0.075f, 0.08f),
                    Material("GrenadeTail", new Color(0.12f, 0.15f, 0.16f)));
            }
            else if (kind == "HealingPack")
            {
                var green = Material("HealingPackBody", new Color(0.08f, 0.7f, 0.34f));
                var white = Material("HealingPackMark", new Color(0.9f, 1f, 0.95f));
                AddPrimitive(root, "治療包本體", PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                    new Vector3(0.28f, 0.19f, 0.18f), green);
                AddPrimitive(root, "識別符號橫", PrimitiveType.Cube, new Vector3(0f, 0f, 0.095f), Quaternion.identity,
                    new Vector3(0.15f, 0.035f, 0.015f), white);
                AddPrimitive(root, "識別符號直", PrimitiveType.Cube, new Vector3(0f, 0f, 0.095f), Quaternion.identity,
                    new Vector3(0.035f, 0.12f, 0.015f), white);
            }
            else
                AddPrimitive(root, "彈射彈", PrimitiveType.Sphere, Vector3.zero, Quaternion.identity,
                    new Vector3(0.1f, 0.1f, 0.18f), Material("RicochetBody", new Color(0.15f, 0.95f, 1f)));
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void UpgradeProjectile(string key, GameObject visualPrefab)
    {
        string path = PlayerActiveAbilityAssetSetup.PrefabRoot + "/" + key + "Projectile.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            throw new InvalidOperationException("請先建立十項 E 技能資產：" + path);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // 已接上接口即由作者管理，不覆寫已選模型、漂浮幅度或既有 Anchor。
            if (root.GetComponent<PlayerAbilityProjectilePresentation>() != null) return;
            var legacy = root.transform.Find("示意外觀");
            if (!IsKnownLegacyVisual(legacy, key))
            {
                Debug.LogWarning("[E 技能外觀] 偵測到自訂外觀，未自動修改。請手動加 PlayerAbilityProjectilePresentation 並指定純外觀子物件：" + path);
                return;
            }
            var anchor = new GameObject("外觀定位");
            anchor.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.scene);
            model.transform.SetParent(anchor.transform, false);
            legacy.gameObject.SetActive(false);
            var presentation = root.AddComponent<PlayerAbilityProjectilePresentation>();
            var data = new SerializedObject(presentation);
            data.FindProperty("visualAnchor").objectReferenceValue = anchor.transform;
            // 預設用可在 Prefab Mode 看見的巢狀模型；visualPrefab 留空供日後直接替換。
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static bool IsKnownLegacyVisual(Transform legacy, string key)
    {
        if (legacy == null || legacy.childCount != 0 || !PlayerAbilityLocalVfxSlot.IsVisualOnly(legacy.gameObject)) return false;
        var mesh = legacy.GetComponent<MeshFilter>();
        if (mesh == null || mesh.sharedMesh == null || mesh.sharedMesh.name != (key == "HealingPack" ? "Cube" : "Sphere")) return false;
        float scale = key == "Ricochet" ? 0.12f : 0.25f;
        return legacy.localPosition.sqrMagnitude < 0.000001f &&
            Quaternion.Angle(legacy.localRotation, Quaternion.identity) < 0.001f &&
            (legacy.localScale - Vector3.one * scale).sqrMagnitude < 0.000001f;
    }

    private static void AddPrimitive(GameObject root, string name, PrimitiveType primitive, Vector3 position,
        Quaternion rotation, Vector3 scale, Material material)
    {
        var child = GameObject.CreatePrimitive(primitive);
        child.name = name;
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = position;
        child.transform.localRotation = rotation;
        child.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(child.GetComponent<Collider>());
        child.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material Material(string name, Color color)
    {
        string path = VisualRoot + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("找不到 URP Unlit Shader。");
        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
#endif
