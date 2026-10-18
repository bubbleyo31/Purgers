using Purgers.Progression;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerExperienceSettings))]
public sealed class PlayerExperienceSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("每列填寫升下一級需要新增的經驗（至少 1）。超過表格後固定使用最後一列。修改在下次啟動 Session 生效。", MessageType.Info);
        var rows = serializedObject.FindProperty("levelRequirements");
        if (rows.arraySize == 0)
            EditorGUILayout.HelpBox("表格為空：執行時暫用舊版需求。請新增至少一列。", MessageType.Warning);
        for (int i = 0; i < rows.arraySize; i++)
        {
            var value = rows.GetArrayElementAtIndex(i);
            value.intValue = Mathf.Max(1, EditorGUILayout.IntField(
                new GUIContent("Lv." + (i + 1) + " → Lv." + (i + 2),
                    "本級需要新增的經驗，不是歷史累計總經驗。"), value.intValue));
        }
        if (rows.arraySize > 0)
            EditorGUILayout.LabelField("表格以後每級需求", rows.GetArrayElementAtIndex(rows.arraySize - 1).intValue.ToString());
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("新增下一級"))
            {
                int last = rows.arraySize > 0 ? rows.GetArrayElementAtIndex(rows.arraySize - 1).intValue : 5;
                rows.arraySize++;
                rows.GetArrayElementAtIndex(rows.arraySize - 1).intValue = last;
            }
            using (new EditorGUI.DisabledScope(rows.arraySize <= 1))
                if (GUILayout.Button("移除最後一級")) rows.arraySize--;
        }
        serializedObject.ApplyModifiedProperties();
    }

    [MenuItem("Tools/Purgers/Progression/開啟逐級經驗設定")]
    public static void OpenSettings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<PlayerExperienceSettings>(PlayerExperienceSettings.AssetPath);
        if (asset == null)
        {
            asset = CreateInstance<PlayerExperienceSettings>();
            AssetDatabase.CreateAsset(asset, PlayerExperienceSettings.AssetPath);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
