#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Purgers.Progression;

/// <summary>Production replacement-window renderer. No EditorWindow, Focus, Show or desktop input.</summary>
public static class FocusSlotsPreview
{
    public static string RenderReplacement(string path, int occupiedSlots = 3)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null; Texture2D output = null; var previous = RenderTexture.active;
        GameObject source = null;
        try
        {
            var cameraObject = new GameObject("Focus replacement camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 540;
            camera.transform.position = new Vector3(0,0,-10); camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f,.11f,.08f);
            var canvasObject = new GameObject("Focus replacement canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject,scene);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera; ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920,1080);
            source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab"));
            SceneManager.MoveGameObjectToScene(source,scene);
            var hud = source.GetComponentInChildren<LocalPlayerRewardHUD>(true);
            hud.enabled = false;
            var definitions = Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog").Rewards
                .Where(r => r != null && r.Category == RewardCategory.GrappleFocus && r.AbilityDefinition != null &&
                    r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerActiveAbilityBase>() != null)
                .Take(3).Select(r => r.AbilityDefinition).ToArray();
            var incoming = definitions[0];
            for(int slot=0;slot<definitions.Length;slot++) if(slot >= occupiedSlots) definitions[slot]=null;
            var window = hud.CreateReplacementPreview(definitions, incoming);
            window.SetParent(canvas.transform,false); window.anchorMin = Vector2.zero; window.anchorMax = Vector2.one;
            window.offsetMin = window.offsetMax = Vector2.zero;
            source.SetActive(false);
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.ForceMeshUpdate(true);
            target = new RenderTexture(1920,1080,24); target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            output = new Texture2D(1920,1080,TextureFormat.RGBA32,false);
            output.ReadPixels(new Rect(0,0,1920,1080),0,0); output.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,output.EncodeToPNG());
            return path;
        }
        finally
        {
            RenderTexture.active = previous;
            if (output != null) Object.DestroyImmediate(output);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            // Destroy the moved preview window before its source owner so OnDestroy sees null.
            foreach(var root in scene.GetRootGameObjects())
                if(root != source) Object.DestroyImmediate(root);
            if(source != null) Object.DestroyImmediate(source);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
