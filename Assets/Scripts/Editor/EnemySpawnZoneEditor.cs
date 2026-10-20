using System.Linq;
using Fusion;
using Purgers.Enemy.Encounter;
using UnityEditor;
using UnityEngine;

/// <summary>混合清單的移動類型只讀 Prefab Definition；不回寫敵人或舊版 Zone 設定。</summary>
[CustomEditor(typeof(EnemySpawnZone)), CanEditMultipleObjects]
public sealed partial class EnemySpawnZoneEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDiagnostics();
        EditorGUILayout.HelpBox("混合清單的 Locomotion Kind 由各敵人 Prefab 的 EnemyDefinition 自動辨識，不需要手動選擇。", MessageType.Info);
        bool mixedMode = targets.All(item => ((EnemySpawnZone)item).UsesEnemyEntries);
        var property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (mixedMode && (property.name == "locomotionKind" || property.name == "enemyPrefabs" || property.name == "enemiesPerActivation"))
                continue;
            using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                EditorGUILayout.PropertyField(property, true);
        }
        if (serializedObject.ApplyModifiedProperties()) diagnosticCache.Clear();
    }
}

[CustomPropertyDrawer(typeof(EnemySpawnEntry))]
public sealed class EnemySpawnEntryDrawer : PropertyDrawer
{
    private static readonly string[] Fields = { "prefab", "chancePercent", "count" };
    private static float Spacing => EditorGUIUtility.standardVerticalSpacing;
    private static float Line => EditorGUIUtility.singleLineHeight;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return Line;
        return Line * 2 + Spacing * 4 + Fields.Sum(name => EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name), true));
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var row = new Rect(position.x, position.y, position.width, Line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string name in Fields)
            {
                var child = property.FindPropertyRelative(name);
                row.y += row.height + Spacing;
                row.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(row, child, true);
                if (name != "prefab") continue;
                row.y += row.height + Spacing;
                row.height = Line;
                string type = child.hasMultipleDifferentValues ? "多個不同 Prefab" :
                    DescribeLocomotion(ReadPrefabReference(child));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(row, new GUIContent("Locomotion Kind（自動）", "只讀取此 Prefab 的 EnemyActor → EnemyDefinition → Locomotion Kind；每種敵人各自判定，不修改 Definition。"), type);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    public static NetworkPrefabRef ReadPrefabReference(SerializedProperty property)
    {
        // Fusion 的 GUID 為 fixed buffer，不能使用 SerializedProperty.boxedValue。
        var raw = property.FindPropertyRelative(nameof(NetworkObjectGuid.RawGuidValue));
        return (NetworkPrefabRef)new NetworkObjectGuid(
            raw.GetFixedBufferElementAtIndex(0).longValue,
            raw.GetFixedBufferElementAtIndex(1).longValue);
    }

    public static string DescribeLocomotion(NetworkPrefabRef prefab)
    {
        if (!prefab.IsValid) return "尚未指定 Prefab";
        string path = AssetDatabase.GUIDToAssetPath(((NetworkObjectGuid)prefab).ToString().Replace("-", ""));
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var actor = asset != null ? asset.GetComponent<EnemyActor>() : null;
        if (actor == null || actor.Definition == null) return "無法辨識：缺少 EnemyActor／EnemyDefinition";
        switch (actor.Definition.LocomotionKind)
        {
            case EnemyLocomotionKind.Ground: return "Ground（地面）";
            case EnemyLocomotionKind.FreeFlying: return "FreeFlying（飛行）";
            case EnemyLocomotionKind.Stationary: return "Stationary（定點）";
            default: return "不支援的移動類型";
        }
    }
}
