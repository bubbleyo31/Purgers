using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只建立本功能資產與子物件；重跑保留既有設定，不重排原 HUD。</summary>
public static class DamageIndicatorSetup
{
    public const string SettingsPath="Assets/_Project_Assets/UI/BattleHUD/DamageIndicatorV1.asset";
    public const string PrefabPath="Assets/Prefabs/UI/DamageIndicators.prefab";
    public static void Apply()
    {
        if(Application.isPlaying)throw new InvalidOperationException("請在 Edit Mode 配置。");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/_Menu.unity" || scene.isDirty)
            throw new InvalidOperationException("需開啟未有未儲存修改的 _Menu，避免存入無關變更。");
        var parent=GameObject.Find("GameplayHUD Canvas/PlayerHUDCanvas");
        if(parent==null)throw new InvalidOperationException("找不到現有 PlayerHUDCanvas。");
        var settings=AssetDatabase.LoadAssetAtPath<DamageIndicatorSettings>(SettingsPath);
        if(settings==null){settings=ScriptableObject.CreateInstance<DamageIndicatorSettings>();AssetDatabase.CreateAsset(settings,SettingsPath);}
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if(prefab==null)
        {
            var root=new GameObject("DamageIndicators",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(LocalPlayerDamageIndicator));
            try
            {
                var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=30;
                var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=1;
                var rect=(RectTransform)root.transform;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                var serialized=new SerializedObject(root.GetComponent<LocalPlayerDamageIndicator>());
                serialized.FindProperty("settings").objectReferenceValue=settings;
                serialized.FindProperty("visualRoot").objectReferenceValue=rect;serialized.ApplyModifiedPropertiesWithoutUndo();
                prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        if(parent.GetComponentInChildren<LocalPlayerDamageIndicator>(true)==null)
        {
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent.transform);
            Undo.RegisterCreatedObjectUndo(instance,"新增受傷來源指示");
            var rect=(RectTransform)instance.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssetIfDirty(settings);
    }
}
