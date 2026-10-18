using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>僅補缺的獨立資產；不修改玩家／場景、既有 Loadout、獎勵池或人工調整。</summary>
public static class GrappleSpikeAssetSetup
{
    public const string DataRoot = "Assets/_Project_Assets/Data/PlayerAbility/GrappleSpike";
    public const string DefinitionPath = DataRoot + "/GrappleSpike.asset";
    public const string LoadoutPath = DataRoot + "/GrappleSpikeLoadout.asset";
    public const string PrefabPath = "Assets/Prefabs/AbilityRuntime/GrappleSpikeRuntime.prefab";
    public const string VisualPath = "Assets/Prefabs/AbilityRuntime/GrappleSpikeVisual.prefab";

    [MenuItem("Tools/Player Ability/建立鈎索尖刺資產（僅補缺）", priority = 125)]
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        EnsureFolder(DataRoot); EnsureFolder("Assets/Prefabs/AbilityRuntime");
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<PlayerAbilityDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            var data = new SerializedObject(definition);
            data.FindProperty("abilityId").stringValue = "grapple.spike";
            data.FindProperty("displayName").stringValue = "鈎索尖刺";
            data.FindProperty("category").intValue = (int)PlayerAbilityCategory.GrappleHit;
            data.FindProperty("professionRule").FindPropertyRelative("restrictProfession").boolValue = false;
            data.FindProperty("includeAdditionalGrappleTargetMask").boolValue = false;
            data.FindProperty("allowDuplicateEquip").boolValue = false;
            // 尖刺不控制繩索，不加入 GrappleHitRopeAuthority；容量仍由正式 Slot Layout 管理。
            data.FindProperty("executionPriority").intValue = 150;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        var visual = CreateVisual();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            var root = new GameObject("GrappleSpikeRuntime");
            try
            {
                root.AddComponent<NetworkObject>();
                var runtime = root.AddComponent<PlayerAbilityRuntime>();
                var spike = root.AddComponent<PlayerGrappleSpikeAbility>();
                SetReference(runtime, "definition", definition);
                SetReference(spike, "spikeVisualPrefab", visual);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null) throw new InvalidOperationException("尖刺 Runtime Prefab 儲存失敗。");
                var labels = new List<string>(AssetDatabase.GetLabels(prefab));
                if (!labels.Contains("FusionPrefab")) labels.Add("FusionPrefab");
                AssetDatabase.SetLabels(prefab, labels.ToArray());
            }
            finally { Object.DestroyImmediate(root); }
        }
        if (definition.RuntimePrefab == null) SetReference(definition, "runtimePrefab", prefab.GetComponent<NetworkObject>());
        AssetDatabase.SaveAssetIfDirty(definition);
        CreateLoadout(definition);
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("[GrappleSpike] 獨立技能、尖刺佔位視覺與範例 Loadout 已補齊；請手動裝備。沒有修改玩家 Prefab、Scene 或獎勵池。");
    }

    private static void CreateLoadout(PlayerAbilityDefinition definition)
    {
        if (AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>(LoadoutPath) != null) return;
        var template = AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>(
            "Assets/_Project_Assets/Data/PlayerAbility/Loadouts/DefaultPlayerAbilityLoadout.asset");
        if (template == null) throw new InvalidOperationException("找不到既有預設 Loadout，不能猜測槽位。");
        var loadout = ScriptableObject.CreateInstance<PlayerAbilityLoadoutDefinition>();
        var items = new List<PlayerAbilityDefinition>();
        foreach (var old in template.EquippedAbilities)
            if (old != null && old.Category != PlayerAbilityCategory.GrappleHit) items.Add(old);
        items.Add(definition);
        var data = new SerializedObject(loadout);
        data.FindProperty("slotLayout").objectReferenceValue = template.SlotLayout;
        var array = data.FindProperty("equippedAbilities"); array.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(loadout, LoadoutPath);
        AssetDatabase.SaveAssetIfDirty(loadout);
    }

    private static GameObject CreateVisual()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
        if (existing != null) return existing;
        string meshPath = DataRoot + "/SpikePrototypeMesh.asset";
        string materialPath = DataRoot + "/SpikePrototype.mat";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { mesh = BuildPrototypeMesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("找不到既有 URP Unlit shader。");
            material = new Material(shader);
            material.SetColor("_BaseColor", new Color(1f, 0.72f, 0.2f, 1f));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var root = new GameObject("GrappleSpikeVisual");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return PrefabUtility.SaveAsPrefabAsset(root, VisualPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 新增佔位資產預算：一個 Mesh、一個 Material、零貼圖、32 支尖刺、192 三角形。
    // 每支側面是尖端必要的三角收束，底蓋一個 Quad；不做多餘細分。
    public static Mesh BuildPrototypeMesh()
    {
        const int count = 32;
        var vertices = new List<Vector3>(count * 5);
        var triangles = new List<int>(count * 18);
        for (int i = 0; i < count; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / count;
            float angle = i * 2.39996323f;
            float r = Mathf.Sqrt(1f - y * y);
            Vector3 axis = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis);
            int offset = vertices.Count;
            vertices.Add(rotation * new Vector3(-.085f, .74f, -.085f));
            vertices.Add(rotation * new Vector3(.085f, .74f, -.085f));
            vertices.Add(rotation * new Vector3(.085f, .74f, .085f));
            vertices.Add(rotation * new Vector3(-.085f, .74f, .085f));
            vertices.Add(axis);
            int[] indices = {0,4,1, 1,4,2, 2,4,3, 3,4,0, 0,1,2, 0,2,3};
            foreach (int index in indices) triangles.Add(offset + index);
        }
        var mesh = new Mesh { name = "GrappleSpikePrototype" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
    private static void SetReference(Object owner, string field, Object value)
    {
        var data = new SerializedObject(owner);
        data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}

