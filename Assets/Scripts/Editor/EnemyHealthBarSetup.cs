using UnityEditor;
using UnityEngine;

/// <summary>僅建立缺失的獨立設定與 UI Prefab，不配置任何正式 Scene 或敵人。</summary>
public static class EnemyHealthBarSetup
{
    public const string SettingsPath = "Assets/_Project_Assets/UI/BattleHUD/EnemyHealthBarV1.asset";
    public const string PrefabPath = "Assets/Prefabs/UI/EnemyHealthBarDisplay.prefab";
    [MenuItem("Tools/Purgers/UI/Create Enemy Health Bar Assets")]
    public static void CreateMissingAssets()
    {
        var settings = AssetDatabase.LoadAssetAtPath<EnemyHealthBarSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<EnemyHealthBarSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("EnemyHealthBarDisplay", typeof(RectTransform), typeof(LocalEnemyHealthBarHUD));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
                root.GetComponent<LocalEnemyHealthBarHUD>().Configure(settings, null, null);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log("敵人血條資產已建立；請依 102 文件手動配置 HUD 與一般敵人，Boss 不配置。");
    }
}

