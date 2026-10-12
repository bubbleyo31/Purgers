using UnityEditor;
using UnityEngine;

/// <summary>在既有 Runtime Inspector 用勾選清單編輯時機，保留原有 0/1/2 序列化值。</summary>
[CustomPropertyDrawer(typeof(PlayerAbilityRecoilTiming))]
public sealed class PlayerAbilityRecoilTimingDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        bool previousMixed = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var selected = (PlayerAbilityRecoilTiming)EditorGUI.EnumFlagsField(position, label, (PlayerAbilityRecoilTiming)property.intValue);
        if (EditorGUI.EndChangeCheck()) property.intValue = (int)selected & 3;
        EditorGUI.showMixedValue = previousMixed;
        EditorGUI.EndProperty();
    }
}
