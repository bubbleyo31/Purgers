#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>只建立本功能缺失資產及隔離預覽；不修改既有技能 Prefab 或開啟中的 Scene。</summary>
public static class ShockwaveVisualAssets
{
    public const string Root = "Assets/Resources/Presentation/Shockwave";
    public const string PrefabPath = Root + "/ThickWave.prefab";
    public const string TexturePath = "Assets/Art/VFX/Shockwave/Kenney/smoke_04.png";

    [MenuItem("Tools/Player Ability/衝擊扇波/建立缺失的特效資產")]
    public static void CreateMissingAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("請先退出 Play Mode。");
        Directory.CreateDirectory(Root);
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("缺少 Kenney smoke_04.png。");
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        var shader = Shader.Find("Purgers/Abilities/Thick Shockwave");
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("扇波 Shader 缺失或編譯錯誤。");
        string materialPath = Root + "/ThickWave.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            material.SetTexture("_FlowTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("ThickWave");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<PlayerShockwaveVisual>();
                root.GetComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        AssetDatabase.SaveAssetIfDirty(material);
    }

    /// <summary>固定攝影機下比較平移 5／10／18 公尺。預覽只有美術，不當作正式 Gameplay 證據。</summary>
    [MenuItem("Tools/Player Ability/衝擊扇波/輸出隔離預覽")]
    public static void RenderPreview()
    {
        string output = Path.GetFullPath("Temp/ShockwaveVfxPreview");
        Directory.CreateDirectory(output);
        foreach (float front in new[] { 5f, 10f, 18f })
            RenderFrame(output + "/wave_" + front.ToString("00") + ".png", front);
    }

    private static void RenderFrame(string output, float front)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null;
        Texture2D image = null;
        Material groundMaterial = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException("請先建立特效資產。");
            var wave = UnityEngine.Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(wave, scene);
            wave.GetComponent<PlayerShockwaveVisual>().Present(Vector3.zero, Vector3.forward, front, 4f, 60f, 1f, 20f, 3f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(floor, scene);
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.position = new Vector3(0f, -0.05f, 8f);
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMaterial.SetColor("_BaseColor", new Color(0.085f, 0.105f, 0.12f));
            floor.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            var cameraObject = new GameObject("Shockwave preview camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.cameraType = CameraType.Preview;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            camera.transform.position = new Vector3(25f, 20f, -19f);
            camera.transform.LookAt(new Vector3(0f, 4f, 8f));
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            rt = new RenderTexture(1280, 800, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            File.WriteAllBytes(output, image.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            EditorSceneManager.ClosePreviewScene(scene);
            if (groundMaterial != null) UnityEngine.Object.DestroyImmediate(groundMaterial);
        }
    }
}
#endif
