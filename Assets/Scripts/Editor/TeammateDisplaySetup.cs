#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>只建立缺少的獨立 UI 資產；不放置物件、不接線、不保存任何正式場景或玩家 Prefab。</summary>
public static class TeammateDisplaySetup
{
    public const string SettingsPath="Assets/_Project_Assets/UI/BattleHUD/TeammateDisplayV1.asset";
    public const string PrefabPath="Assets/Prefabs/UI/TeammateDisplay.prefab";
    [MenuItem("Tools/Purgers/UI/Create Teammate Display Assets")]
    public static void CreateMissingAssets()
    {
        var settings=AssetDatabase.LoadAssetAtPath<TeammateDisplaySettings>(SettingsPath);
        if(settings==null)
        {
            settings=ScriptableObject.CreateInstance<TeammateDisplaySettings>();
            settings.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
            AssetDatabase.CreateAsset(settings,SettingsPath);
        }
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)==null)
        {
            var root=new GameObject("TeammateDisplay",typeof(RectTransform),typeof(LocalTeammateHUD));
            try
            {
                var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
                var so=new SerializedObject(root.GetComponent<LocalTeammateHUD>());
                so.FindProperty("settings").objectReferenceValue=settings;so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally{Object.DestroyImmediate(root);}
        }
        // Save only this new/selected settings asset; preserve unrelated dirty assets.
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log("隊友 UI 資產已備妥；請依 101 文件手動配置 HUD、角色錨點與 URP Renderer Feature。");
    }
}
#endif
