using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>只建立缺少的荊棘美術 Prefab，不修改場景或覆蓋既有 Prefab 調整。</summary>
public static class MutantThornAssetBuilder
{
    public const string AssetRoot = "Assets/_Project_Assets/Models/Map/MutantThorns_V1";
    private static readonly string[] Variants = { "A_BriarHeart", "B_GnarledKnot", "C_SplitCrown" };

    [MenuItem("Tools/Purgers/Art/Build Missing Mutant Thorn Prefabs")]
    public static void BuildMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請在 Edit Mode 建立資產。");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("找不到專案 URP Lit Shader。");
        var material = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Thorn_DarkWalnut.mat");
        if (material == null)
        {
            material = new Material(shader) { name = "Thorn_DarkWalnut", enableInstancing = true };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetRoot + "/Textures/Thorn_BaseColor.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .13f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, AssetRoot + "/Thorn_DarkWalnut.mat");
        }
        foreach (string variant in Variants)
        {
            string prefabPath = AssetRoot + "/Thorn_" + variant + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) continue;
            var root = new GameObject("Thorn_" + variant);
            // 新物件只存在於暫存 Preview Scene，避免污染目前未儲存場景。
            var previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var levels = new LOD[3];
                float[] heights = { .28f, .12f, .015f };
                for (int i = 0; i < 3; i++)
                {
                    string modelPath = AssetRoot + "/Thorn_" + variant + "_Detail" + i + ".fbx";
                    var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                    if (importer == null) throw new InvalidOperationException("缺少 FBX：" + modelPath);
                    importer.importAnimation = false;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
                    instance.name = "LOD" + i;
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers) renderer.sharedMaterial = material;
                    levels[i] = new LOD(heights[i], renderers);
                }
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(levels);
                group.RecalculateBounds();
                // 呼吸／抽動最大外擴也要落在 LOD 尺寸之內。
                group.size *= 1.12f;
                var animation = root.AddComponent<MutantThornVisual>();
                var serialized = new SerializedObject(animation);
                serialized.FindProperty("visualRoot").objectReferenceValue = visual.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }
        AssetDatabase.SaveAssetIfDirty(material);
        Debug.Log("[MutantThorns] 三款美術 Prefab 已建立；未加入任何遊戲場景，尚未接傷害、碰撞或生成器。");
    }

    /// <summary>從 URP 材質與 LOD0 產生實際 Unity 預覽；不存檔或切換使用者場景。</summary>
    public static void RenderPreviews()
    {
        string output = Path.GetFullPath("deliverables/Art/MutantThorns_V1/Previews");
        Directory.CreateDirectory(output);
        foreach (string variant in Variants)
        {
            var preview = new PreviewRenderUtility();
            GameObject instance = null;
            Texture2D image = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Thorn_" + variant + ".prefab");
                instance = UnityEngine.Object.Instantiate(prefab);
                preview.AddSingleGO(instance);
                instance.GetComponent<LODGroup>().ForceLOD(0);
                preview.camera.transform.position = new Vector3(3f, 2.1f, -5f);
                preview.camera.transform.LookAt(Vector3.zero);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 1.8f;
                preview.camera.nearClipPlane = .1f;
                preview.camera.farClipPlane = 30f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.045f, .05f, .06f);
                preview.ambientColor = new Color(.22f, .22f, .22f);
                preview.lights[0].intensity = 1.8f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                preview.lights[1].intensity = 1.1f;
                preview.lights[1].transform.rotation = Quaternion.Euler(330f, 150f, 0f);
                preview.BeginStaticPreview(new Rect(0, 0, 900, 900));
                preview.Render(true);
                image = preview.EndStaticPreview();
                File.WriteAllBytes(Path.Combine(output, "Unity_" + variant + ".png"), image.EncodeToPNG());
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                preview.Cleanup();
            }
        }
    }
}
