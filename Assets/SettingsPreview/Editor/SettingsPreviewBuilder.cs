using System;
using System.IO;
using System.Linq;
using Purgers.SettingsPreview;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Explicit authoring only; never modifies a production scene, prefab, build list or input owner.</summary>
public static class SettingsPreviewBuilder
{
    public const string ScenePath = "Assets/SettingsPreview/SettingsPreview.unity";
    const string FontPath = "Assets/SettingsPreview/SettingsPreviewFont.asset";
    const string Output = "Documentation/ProjectArchitecture/Validation/SettingsPreview/Unity";

    [MenuItem("Tools/Purgers/Settings Preview/Open isolated preview scene")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先離開 Play Mode。");
        // This menu is deliberately manual. The agent never invokes it or steals focus.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(ScenePath)) CreateScene();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    public static string CreateScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先離開 Play Mode。");
        if (File.Exists(ScenePath)) throw new InvalidOperationException("預覽場景已存在，不自動覆寫人工配置。");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var view = CreateObjects(scene);
            view.Model.OpenPause(); view.Build();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("預覽場景儲存失敗。");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
        return ScenePath;
    }

    public static SettingsPreviewView CreateObjects(Scene scene)
    {
        var cameraGO = new GameObject("Preview Camera", typeof(Camera), typeof(AudioListener));
        SceneManager.MoveGameObjectToScene(cameraGO, scene);
        var camera = cameraGO.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.1f, .12f, .09f); camera.orthographic = true;
        camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
        camera.scene = scene;
        var lightGO = new GameObject("Preview Light", typeof(Light)); SceneManager.MoveGameObjectToScene(lightGO, scene);
        lightGO.GetComponent<Light>().type = LightType.Directional; lightGO.transform.rotation = Quaternion.Euler(45, -30, 0);
        var root = new GameObject("Settings Preview - Editor Only", typeof(SettingsPreviewView));
        SceneManager.MoveGameObjectToScene(root, scene);
        var view = root.GetComponent<SettingsPreviewView>(); view.previewCamera = camera;
        view.font = EnsureFont();
        view.brush = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/MenuBrushV1/Brush.png");
        view.Initialize();
        var events = new GameObject("Preview EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        SceneManager.MoveGameObjectToScene(events, scene);
        return view;
    }

    static TMP_FontAsset EnsureFont()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (existing) return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/Noto_Sans_TC/static/NotoSansTC-Bold.ttf");
        var font = TMP_FontAsset.CreateFontAsset(source, 48, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
        font.name = "SettingsPreviewFont";
        // Populate a dedicated atlas so preview labels never dirty shared production fonts.
        string sourceText = File.ReadAllText("Assets/SettingsPreview/SettingsPreviewView.cs") + File.ReadAllText("Assets/SettingsPreview/SettingsPreviewModel.cs");
        string missing;
        font.TryAddCharacters(new string(sourceText.Distinct().ToArray()), out missing);
        AssetDatabase.CreateAsset(font, FontPath);
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var texture in font.atlasTextures) { texture.name = "SettingsPreviewFont Atlas"; AssetDatabase.AddObjectToAsset(texture, font); }
        EditorUtility.SetDirty(font); AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }

    public static string CaptureAll()
    {
        Directory.CreateDirectory(Output);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var view = CreateObjects(scene);
            Capture(view, "pause", 1600, 900);
            view.Model.OpenSettings();
            for (int tab = 0; tab < 5; tab++) { view.SetTab(tab); Capture(view, "tab-" + tab, 1600, 900); }
            view.SetTab(1); view.Model.Capture = 11; view.Model.Bind(KeyCode.Space); Capture(view, "key-conflict", 1600, 900);
            view.Model.RequestSave(); view.Model.Selected = 1; Capture(view, "save-warning", 1600, 900);
            view.Model.No(); view.SetTab(0); Capture(view, "controls-1280x720", 1280, 720);
            Capture(view, "controls-1920x1080", 1920, 1080);
            Capture(view, "controls-1920x1200", 1920, 1200);
            Capture(view, "controls-2560x1080", 2560, 1080);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        return Path.GetFullPath(Output);
    }

    static void Capture(SettingsPreviewView view, string name, int width, int height)
    {
        if (name == "pause") view.Model.OpenPause();
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var oldActive = RenderTexture.active;
        Texture2D image = null;
        try
        {
            view.previewCamera.targetTexture = rt;
            view.Build();
            foreach (var text in view.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases(); view.previewCamera.Render();
            RenderTexture.active = rt; image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            view.previewCamera.targetTexture = null; RenderTexture.active = oldActive;
            if (image) UnityEngine.Object.DestroyImmediate(image); RenderTexture.ReleaseTemporary(rt);
        }
    }
}
