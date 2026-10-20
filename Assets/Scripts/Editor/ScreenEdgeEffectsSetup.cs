#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ScreenEdgeEffectsSetup
{
    public const string SettingsPath="Assets/Resources/Presentation/ScreenEdgeEffects.asset";
    public const string PrefabPath="Assets/Prefabs/UI/ScreenEdgeEffects.prefab";
    public const string ShaderPath="Assets/Shaders/Presentation/ScreenEdgeEffects.shader";
    public const string Evidence="Documentation/ProjectArchitecture/Validation/ScreenEdgeEffects";

    [MenuItem("Tools/Purgers/UI/Create And Wire Screen Edge Effects")]
    public static void CreateAndWire()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("需要 Edit Mode。");
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        if(!scene.IsValid() || !scene.isLoaded || scene.isDirty)throw new InvalidOperationException("需要已開啟且沒有未儲存變更的 _Menu。");
        var parent=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="GameplayHUD Canvas")?.transform.Find("PlayerHUDCanvas");
        if(parent==null)throw new InvalidOperationException("找不到 PlayerHUDCanvas。");
        var found=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LocalPlayerScreenEdgeEffects>(true)).ToArray();
        if(found.Length>1)throw new InvalidOperationException("已有重複外框，請核對來源。");
        if(found.Length==1)return;
        var before=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true))
            .Where(c=>c!=null && c!=parent).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if(shader==null || ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader 未就緒。");
        var settings=AssetDatabase.LoadAssetAtPath<ScreenEdgeEffectsSettings>(SettingsPath);
        if(settings==null)
        {
            settings=ScriptableObject.CreateInstance<ScreenEdgeEffectsSettings>(); settings.shader=shader;
            AssetDatabase.CreateAsset(settings,SettingsPath);
        }
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)==null)
        {
            var preview=EditorSceneManager.NewPreviewScene();
            try
            {
                var root=new GameObject("ScreenEdgeEffects",typeof(RectTransform)); SceneManager.MoveGameObjectToScene(root,preview);
                Stretch((RectTransform)root.transform);
                var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.overrideSorting=true;canvas.sortingOrder=-5;
                var group=root.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
                var graphic=root.AddComponent<ScreenEdgeGraphic>();graphic.raycastTarget=false;
                var presenter=root.AddComponent<LocalPlayerScreenEdgeEffects>();presenter.Configure(settings,null);graphic.enabled=false;
                Stretch((RectTransform)root.transform);
                if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null)throw new IOException("無法保存外框 Prefab。");
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);}
        }
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),parent);
        // Sorting Order controls compositing; append to preserve existing sibling contracts.
        instance.name="ScreenEdgeEffects";instance.transform.SetAsLastSibling();
        var layer=instance.GetComponent<Canvas>();layer.overrideSorting=true;layer.sortingOrder=-5;
        layer.sortingLayerID=parent.GetComponent<Canvas>().rootCanvas.sortingLayerID;
        PrefabUtility.RecordPrefabInstancePropertyModifications(layer);
        foreach(var pair in before)
            if(pair.Key!=null && EditorJsonUtility.ToJson(pair.Key)!=pair.Value)
            { UnityEngine.Object.DestroyImmediate(instance);throw new InvalidOperationException("未保存：既有元件變動 "+pair.Key.name); }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("_Menu 保存失敗。");
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Evidence+"/wiring.txt","_Menu/GameplayHUD Canvas/PlayerHUDCanvas/ScreenEdgeEffects\nCanvas=-5, existing HUD untouched.\nPre-existing component snapshots preserved="+before.Count+"\nNo selection/focus/Play Mode used.\n");
    }
    private static void Stretch(RectTransform r)
    {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;r.localScale=Vector3.one;}
}
#endif
