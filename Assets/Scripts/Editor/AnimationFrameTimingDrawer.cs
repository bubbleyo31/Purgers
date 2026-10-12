using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AnimationFrameTiming))]
public sealed class AnimationFrameTimingDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 4 * 20f;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var row = new Rect(position.x, position.y, position.width, 18f);
        EditorGUI.LabelField(row, "原始動畫與倍率", EditorStyles.boldLabel);
        row.y += 20f;
        EditorGUI.PropertyField(row, property.FindPropertyRelative("ClipDuration"), new GUIContent("原始總長（秒）", "動畫在 1 倍速下的總秒數。")); row.y += 20f;
        EditorGUI.PropertyField(row, property.FindPropertyRelative("FramesPerSecond"), new GUIContent("來源 FPS", "不是遊戲 FPS；例如 24、30、60。")); row.y += 20f;
        EditorGUI.PropertyField(row, property.FindPropertyRelative("PlaybackSpeed"), new GUIContent("播放倍率", "幀模式的唯一倍率；不疊乘 Animator State Speed。"));
        EditorGUI.EndProperty();
    }
}

[CustomPropertyDrawer(typeof(TankMeleeAttackDefinition))]
public sealed class TankMeleeAttackDefinitionDrawer : PropertyDrawer
{
    private static readonly string[] FrameFields = { "FrameTiming", "PreparationFrame", "DamageFrame", "RecoveryFrame" };
    private static readonly string[] LegacyFields = { "StartupDuration", "ActiveDuration", "RecoveryDuration" };
    private static readonly string[] CombatFields = { "Damage", "MaximumTargets", "Range", "SearchRadius", "FullAttackAngle" };
    private static string Label(string field)
    {
        switch(field)
        {
            case "UseAnimationFrames": return "啟用幀時序與按住預備";
            case "PreparationFrame": return "預備停格幀（從 0 起）";
            case "DamageFrame": return "出傷幀（從 0 起）";
            case "RecoveryFrame": return "開始收招幀（從 0 起）";
            default: return ObjectNames.NicifyVariableName(field);
        }
    }
    public override float GetPropertyHeight(SerializedProperty p, GUIContent label)
    {
        if (!p.isExpanded) return 20f;
        float height = 20f + Height(p, "UseAnimationFrames");
        bool frames = p.FindPropertyRelative("UseAnimationFrames").boolValue;
        foreach (var name in frames ? FrameFields : LegacyFields) height += Height(p, name);
        if (frames) height += 86f;
        foreach (var name in CombatFields) height += Height(p, name);
        return height;
    }
    private static float Height(SerializedProperty p, string name) => EditorGUI.GetPropertyHeight(p.FindPropertyRelative(name), true) + 2f;
    public override void OnGUI(Rect position, SerializedProperty p, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, p);
        var row = new Rect(position.x, position.y, position.width, 18f);
        p.isExpanded = EditorGUI.Foldout(row, p.isExpanded, label, true);
        if (p.isExpanded)
        {
            EditorGUI.indentLevel++;
            row.y += 20f;
            Draw(ref row, p, "UseAnimationFrames");
            bool frames = p.FindPropertyRelative("UseAnimationFrames").boolValue;
            foreach (var name in frames ? FrameFields : LegacyFields) Draw(ref row, p, name);
            if (frames)
            {
                var t = p.FindPropertyRelative("FrameTiming");
                var timing = new AnimationFrameTiming { ClipDuration=t.FindPropertyRelative("ClipDuration").floatValue,
                    FramesPerSecond=t.FindPropertyRelative("FramesPerSecond").floatValue, PlaybackSpeed=t.FindPropertyRelative("PlaybackSpeed").floatValue };
                float hold=p.FindPropertyRelative("PreparationFrame").floatValue, hit=p.FindPropertyRelative("DamageFrame").floatValue,
                    recover=p.FindPropertyRelative("RecoveryFrame").floatValue;
                bool valid=timing.ContainsFrame(hold)&&timing.ContainsFrame(hit)&&timing.ContainsFrame(recover)&&hold<=hit&&hit<=recover;
                string text=valid ? $"總幀數 {timing.TotalFrames:0.###}；完成 {timing.DurationSeconds:0.###} 秒\n預備 {timing.FrameToSeconds(hold):0.###} 秒 → 出傷 {timing.FrameToSeconds(hit):0.###} 秒\n開始收招 {timing.FrameToSeconds(recover):0.###} 秒；以上不含停格等待。\n預備衝刺 {timing.FrameToSeconds(hit)-timing.FrameToSeconds(hold):0.###} 秒（至少一個模擬 Tick）。"
                    : "設定無效，這段攻擊不會啟動。\n需 0 ≤ 預備幀 ≤ 出傷幀 ≤ 收招幀 ≤ 總幀數。\n原始總長、FPS、倍率皆須大於 0。";
                row.height=84f; EditorGUI.HelpBox(EditorGUI.IndentedRect(row),text,valid?MessageType.Info:MessageType.Error);row.y+=86f;
            }
            foreach (var name in CombatFields) Draw(ref row,p,name);
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
    private static void Draw(ref Rect row, SerializedProperty parent, string name)
    {
        var p=parent.FindPropertyRelative(name);row.height=EditorGUI.GetPropertyHeight(p,true);
        EditorGUI.PropertyField(row,p,new GUIContent(Label(name),p.tooltip),true);row.y+=row.height+2f;
    }
}
