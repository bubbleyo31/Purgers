using UnityEditor;
using UnityEngine;

/// <summary>十項 E 主動技能的 Editor 繪製入口；不建立場景物件、不參與權威模擬。</summary>
public static class ActiveAbilityGizmos
{
    private const string MenuPath = "Tools/Player Ability/E 技能 Gizmos/顯示 Gizmos";
    private const string PreferenceKey = "Purgers.ActiveAbilityGizmos.Enabled";
    private static Texture2D labelBackground;
    private static GUIStyle labelStyle;

    static ActiveAbilityGizmos()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseLabelResources;
        EditorApplication.quitting += ReleaseLabelResources;
    }

    private static void ReleaseLabelResources()
    {
        if (labelBackground != null) Object.DestroyImmediate(labelBackground);
        labelBackground = null;
        labelStyle = null;
    }

    private static GUIStyle LabelStyle(ActiveAbilityGizmoSettings settings, Color color)
    {
        if (labelBackground == null)
        {
            labelBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            labelBackground.SetPixel(0, 0, new Color(.035f, .035f, .045f, .9f));
            labelBackground.Apply();
        }
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                padding = new RectOffset(5, 5, 3, 3)
            };
            labelStyle.normal.background = labelBackground;
        }
        labelStyle.fontSize = Mathf.Clamp(settings.labelSize, 10, 24);
        labelStyle.normal.textColor = color;
        return labelStyle;
    }

    public static bool GlobalEnabled => EditorPrefs.GetBool(PreferenceKey, true);

    [MenuItem(MenuPath)]
    private static void ToggleGlobal()
    {
        EditorPrefs.SetBool(PreferenceKey, !GlobalEnabled);
        SceneView.RepaintAll();
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateGlobal()
    {
        Menu.SetChecked(MenuPath, GlobalEnabled);
        return true;
    }

    private static bool IsSelected(Component component, GizmoType flags, Transform owner)
    {
        if ((flags & (GizmoType.Selected | GizmoType.InSelectionHierarchy)) != 0) return true;
        foreach (var selected in Selection.transforms)
        {
            if (component.transform == selected || component.transform.IsChildOf(selected)) return true;
            // Runtime 為獨立網路根物件，選擇玩家仍可查看其裝備技能。
            if (owner != null && (owner == selected || owner.IsChildOf(selected))) return true;
        }
        return false;
    }

    // 明確註冊各具體 MonoBehaviour，讓 Unity 的各腳本 Gizmos 項目皆有獨立入口。
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawProjectile(PlayerProjectileAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawPiercingCannon(PlayerPiercingCannonAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawShockwave(PlayerShockwaveAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawShield(PlayerShieldAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawExperience(PlayerExperienceAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawBlink(PlayerBlinkAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawBulletTime(PlayerBulletTimeAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawPrecisionLock(PlayerPrecisionLockAbility skill, GizmoType flags) => DrawAbility(skill, flags);

    private static void DrawAbility(PlayerActiveAbilityBase skill, GizmoType flags)
    {
        var settings = skill.RangeGizmos;
        var owner = skill.GizmoOwner;
        if (!settings.ShouldDraw(IsSelected(skill, flags, owner != null ? owner.transform : null), GlobalEnabled)) return;
        DrawPreview(ActiveAbilityGizmoPreviewBuilder.Build(skill), settings);
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawProjectile(PlayerAbilityProjectile projectile, GizmoType flags)
    {
        var settings = projectile.RangeGizmos;
        var owner = projectile.GizmoOwner;
        if (!settings.ShouldDraw(IsSelected(projectile, flags, owner != null ? owner.transform : null), GlobalEnabled)) return;
        DrawPreview(ActiveAbilityGizmoPreviewBuilder.BuildProjectile(projectile), settings);
    }

    public static void DrawPreview(ActiveAbilityGizmoPreview preview, ActiveAbilityGizmoSettings settings)
    {
        Color oldGizmoColor = Gizmos.color, oldHandleColor = Handles.color;
        Matrix4x4 oldGizmoMatrix = Gizmos.matrix, oldHandleMatrix = Handles.matrix;
        var oldDepth = Handles.zTest;
        try
        {
            // 實際技能以世界公尺判定，不讓 Prefab 縮放扭曲設定範圍。
            Gizmos.matrix = Handles.matrix = Matrix4x4.identity;
            Gizmos.color = Handles.color = preview.Color;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            foreach (var sphere in preview.Spheres) Gizmos.DrawWireSphere(sphere.Center, sphere.Radius);
            foreach (var path in preview.RangePaths) Handles.DrawAAPolyLine(2f, path);
            if (settings.paths)
                foreach (var path in preview.Paths) Handles.DrawAAPolyLine(2.5f, path);
            if (!settings.labels) return;
            var style = LabelStyle(settings, preview.Color);
            Handles.color = Color.white;
            Handles.Label(preview.Origin + settings.labelOffset, preview.Title + "\n" + preview.Description, style);
            foreach (var label in preview.Labels)
                Handles.Label(label.Position + settings.labelOffset, label.Text, style);
        }
        finally
        {
            Gizmos.color = oldGizmoColor;
            Gizmos.matrix = oldGizmoMatrix;
            Handles.color = oldHandleColor;
            Handles.matrix = oldHandleMatrix;
            Handles.zTest = oldDepth;
        }
    }
}
