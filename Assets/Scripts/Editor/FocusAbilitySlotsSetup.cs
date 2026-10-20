#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit migration only. Never runs on reload; no scene opening, focus, Play or auto-save.</summary>
public static class FocusAbilitySlotsSetup
{
    const string LayoutPath = "Assets/_Project_Assets/Data/PlayerAbility/Loadouts/DefaultGrappleAbilitySlots.asset";
    [MenuItem("Tools/Player Ability/三專注槽/遷移容量與專注共裝規則")]
    public static void MigrateDefinitions()
    {
        var layout = AssetDatabase.LoadAssetAtPath<PlayerAbilitySlotLayoutDefinition>(LayoutPath);
        if (layout == null) throw new InvalidOperationException("找不到既有正式 Slot Layout，未建立替代資產。");
        SetFocusCapacity(layout);
        AssetDatabase.SaveAssetIfDirty(layout);
        foreach (var guid in AssetDatabase.FindAssets("t:PlayerAbilityDefinition", new[] { "Assets/_Project_Assets/Data/PlayerAbility" }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null && definition.Category == PlayerAbilityCategory.GrappleFocus)
            { MigrateDefinition(definition); AssetDatabase.SaveAssetIfDirty(definition); }
        }

        Debug.Log("[三專注槽] 容量／Definition 已遷移；HUD 與 Fusion bake／編譯／測試仍需另行驗證。");
    }
    public static void SetFocusCapacity(PlayerAbilitySlotLayoutDefinition layout)
    {
        Undo.RecordObject(layout, "Open three focus slots");
        var so = new SerializedObject(layout);
        var entries = so.FindProperty("categoryCapacities"); bool found = false;
        for (int i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("category").intValue != (int)PlayerAbilityCategory.GrappleFocus) continue;
            entry.FindPropertyRelative("capacity").intValue = found ? 0 : FocusAbilitySlotRules.SlotCount;
            found = true;
        }
        if (!found)
        {
            int index = entries.arraySize++; var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("category").intValue = (int)PlayerAbilityCategory.GrappleFocus;
            entry.FindPropertyRelative("capacity").intValue = FocusAbilitySlotRules.SlotCount;
        }
        so.ApplyModifiedProperties(); EditorUtility.SetDirty(layout);
    }
    public static void MigrateDefinition(PlayerAbilityDefinition definition)
    {
        Undo.RecordObject(definition, "Migrate focus coexistence");
        var so = new SerializedObject(definition);
        so.FindProperty("allowDuplicateEquip").boolValue = false;
        // Preserve movement conflicts and every unrelated/custom incompatibility.
        var prefab = definition.RuntimePrefab;
        bool movement = prefab != null && (prefab.GetComponent<SupportAerialAbility>() != null ||
            prefab.GetComponent<TankAirDashAbility>() != null || prefab.GetComponent<PlayerBlinkAbility>() != null);
        var groups = so.FindProperty("exclusiveGroupIds");
        if (!movement)
            for (int i = groups.arraySize - 1; i >= 0; i--)
                if (string.Equals(groups.GetArrayElementAtIndex(i).stringValue?.Trim(), "GrappleFocusMovementAuthority", StringComparison.OrdinalIgnoreCase))
                    groups.DeleteArrayElementAtIndex(i);
        so.ApplyModifiedProperties(); EditorUtility.SetDirty(definition);
    }
    [MenuItem("Tools/Player Ability/三專注槽/配置所選 HUD 的放大容器")]
    public static void ConfigureSelectedHud()
    {
        if (Selection.activeGameObject == null) throw new InvalidOperationException("請先選取既有技能 HUD。");
        var hud = Selection.activeGameObject.GetComponentInParent<LocalPlayerBattleAbilityHUD>();
        if (hud == null) hud = Selection.activeGameObject.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
        if (hud == null) throw new InvalidOperationException("選取範圍沒有技能 HUD。");
        ConfigureHud(hud);
        // Deliberately no SaveScene: user/Editor owner reviews the change first.
    }
    public static void ConfigureHud(LocalPlayerBattleAbilityHUD hud)
    {
        var so = new SerializedObject(hud);
        var slots = so.FindProperty("slotsRoot").objectReferenceValue as RectTransform;
        var template = so.FindProperty("slotTemplate").objectReferenceValue as GameObject;
        if (slots == null || template == null) throw new InvalidOperationException("既有槽位容器／模板未接線。");
        Undo.RecordObjects(new UnityEngine.Object[] { hud, slots }, "Configure focus management HUD");
        // Slots contain the full per-skill frame/background/keycap. Do not scale the fullscreen HUD,
        // central timed bar, weapon information, or grapple energy.
        if (so.FindProperty("focusManagementRoot").objectReferenceValue == null)
            so.FindProperty("focusManagementRoot").objectReferenceValue = slots;
        var group = slots.GetComponent<HorizontalLayoutGroup>();
        var element = template.GetComponent<LayoutElement>();
        float width = element != null && element.preferredWidth > 0 ? element.preferredWidth : ((RectTransform)template.transform).rect.width;
        float required = width * 4 + (group != null ? group.spacing * 3 + group.padding.horizontal : 0);
        if (slots.rect.width < required)
        {
            // Grow to the left, preserving the authored right edge.
            float extra = required - slots.rect.width;
            slots.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, required);
            slots.anchoredPosition -= Vector2.right * extra * (1f - slots.pivot.x);
        }
        so.ApplyModifiedProperties(); EditorUtility.SetDirty(hud);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
        PrefabUtility.RecordPrefabInstancePropertyModifications(slots);
        if (hud.gameObject.scene.IsValid() && hud.gameObject.scene.isLoaded)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
    }
}
#endif
