using System;
using System.IO;
using Fusion.Menu;
using MultiClimb.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit one-time authoring of the approved main-menu design. Never runs automatically.</summary>
public static class MenuBrushStyleBuilder
{
    public const string ArtFolder = "Assets/_Project_Assets/UI/MenuBrushV1";
    const string MainPath = "GameplayHUD Canvas/Menu/FusionMenuViewMainMenu";
    static readonly Color Gold = new Color32(207, 191, 80, 255);
    static readonly Color Ink = new Color32(27, 32, 25, 255);
    static readonly Color Cream = new Color32(241, 237, 217, 255);
    static readonly Color Olive = new Color32(49, 57, 37, 245);

    [MenuItem("Tools/Purgers/Menu/Apply approved brush style V1")]
    public static void ApplyToOpenMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("請先離開 Play Mode。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/_Menu.unity")
            throw new InvalidOperationException("只允許在 _Menu Scene 套用。");
        if (scene.isDirty)
            throw new InvalidOperationException("Scene 有未儲存修改，請先人工保存或另存。");
        var main = GameObject.Find(MainPath);
        if (!main) throw new InvalidOperationException("找不到正式 Fusion 主選單。");
        if (main.GetComponentsInChildren<MenuBrushButtonVisual>(true).Length > 0)
            throw new InvalidOperationException("已套用筆觸樣式；請直接調整 Inspector，避免覆寫人工配置。");
        EnsureAssets();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Menu Brush V1");
        foreach (var c in main.GetComponentsInChildren<Component>(true))
            if (c) Undo.RecordObject(c, "Menu visual style");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/Noto_Sans_TC/NotoSansTC-Bold SDF.asset");
        var brush = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/Brush.png");
        var root = main.transform;
        // Assign first: Unity may restore old animated transform defaults when rebinding.
        var animator = main.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ArtFolder + "/MenuBrushFadeV1.controller");
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        var panel = root.Find("Background/Panel").GetComponent<Image>();
        panel.color = Color.white; panel.raycastTarget = false;
        var pr = panel.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = Vector2.one * .5f;
        pr.anchoredPosition = Vector2.zero;
        float ratio = panel.sprite.rect.width / panel.sprite.rect.height;
        pr.sizeDelta = new Vector2(1920, 1920 / ratio);
        var shade = NewImage(root.Find("Background"), "MenuRightShade");
        Stretch(shade.rectTransform);
        shade.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/RightShade.png");
        shade.color = new Color32(14, 20, 10, 255); shade.raycastTarget = false;
        shade.transform.SetSiblingIndex(panel.transform.GetSiblingIndex() + 1);
        var logo = root.Find("Background/ProductLogo").GetComponent<Image>();
        VerticalBand(logo.rectTransform, .665f, .84f, 500, 56);
        logo.color = Color.white; logo.preserveAspect = true; logo.raycastTarget = false;
        logo.GetComponent<CanvasGroup>().alpha = 1;
        // Keep the original plugin and its registration, only retarget its presentation field.
        var oldVersion = logo.transform.Find("VersionLabel").GetComponent<TMP_Text>();
        var version = NewText(root.Find("Background"), "MenuVersionLabel", font);
        version.text = oldVersion.text; version.fontSize = 18; version.color = Cream;
        version.alignment = TextAlignmentOptions.BottomLeft;
        var vr = version.rectTransform; vr.anchorMin = vr.anchorMax = vr.pivot = Vector2.zero;
        vr.anchoredPosition = new Vector2(40, 28); vr.sizeDelta = new Vector2(700, 35);
        oldVersion.enabled = false;
        Set(oldVersion.GetComponent<FusionMenuScreenPluginVersion>(), "_textField", version);
        foreach (string group in new[] { "MainButtons", "RightButtons", "TopButtons" })
        {
            var t = root.Find(group); Stretch((RectTransform)t);
            var grid = t.GetComponent<GridLayoutGroup>(); if (grid) grid.enabled = false;
            var cg = t.GetComponent<CanvasGroup>(); if (cg) cg.alpha = 1;
        }
        string[] paths = { "MainButtons/QuickPlay", "MainButtons/ContinueGame", "MainButtons/PartyMenu", "RightButtons/QuitButton" };
        float[] lows = { .48f, .35f, .22f, .085f };
        float[] highs = { .59f, .46f, .33f, .165f };
        var buttons = new Button[4];
        for (int i = 0; i < paths.Length; i++)
        {
            buttons[i] = root.Find(paths[i]).GetComponent<Button>();
            VerticalBand((RectTransform)buttons[i].transform, lows[i], highs[i], 500, 52);
            StyleBrush(buttons[i], brush, font, i == 0, i == 3);
        }
        var name = root.Find("TopButtons/PlayerNameButton").GetComponent<Button>();
        var settings = root.Find("TopButtons/SettingsButton").GetComponent<Button>();
        BoxRight((RectTransform)name.transform, 164, 60, 322, 72);
        BoxRight((RectTransform)settings.transform, 60, 60, 78, 72);
        StyleUtility(name); StyleUtility(settings);
        var nameLabel = name.transform.Find("ButtonLabel").GetComponent<TMP_Text>();
        nameLabel.font = font; nameLabel.fontSize = 26; nameLabel.enableAutoSizing = false;
        nameLabel.color = Cream; nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
        nameLabel.overflowMode = TextOverflowModes.Ellipsis; nameLabel.enableWordWrapping = false;
        PlaceLabel(nameLabel.rectTransform, 66, 240, 52);
        var editIcon = name.transform.Find("ButtonIcon") as RectTransform;
        editIcon.anchorMin = editIcon.anchorMax = new Vector2(0, .5f); editIcon.pivot = Vector2.one * .5f;
        editIcon.anchoredPosition = new Vector2(33, 0); editIcon.sizeDelta = new Vector2(40, 40);
        editIcon.GetComponent<CanvasGroup>().alpha = 1;
        var iconImage = editIcon.GetComponentInChildren<Image>(); iconImage.color = Cream; iconImage.raycastTarget = false;
        var gear = settings.transform.Find("ButtonIcon").GetComponent<Image>();
        gear.rectTransform.anchorMin = gear.rectTransform.anchorMax = gear.rectTransform.pivot = Vector2.one * .5f;
        gear.rectTransform.anchoredPosition = Vector2.zero; gear.rectTransform.sizeDelta = new Vector2(36, 36);
        gear.color = Cream; gear.raycastTarget = false;
        var cb = ColorBlock.defaultColorBlock; cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f); cb.selectedColor = cb.highlightedColor;
        cb.pressedColor = new Color(.7f, .7f, .7f); cb.fadeDuration = .12f;
        name.colors = settings.colors = cb;
        // Navigation belongs to the existing EventSystem / Button implementation.
        var ordered = new[] { name, settings, buttons[0], buttons[1], buttons[2], buttons[3] };
        for (int i = 0; i < ordered.Length; i++)
        {
            var n = ordered[i].navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnUp = ordered[(i + ordered.Length - 1) % ordered.Length];
            n.selectOnDown = ordered[(i + 1) % ordered.Length];
            if (i < 2) { n.selectOnLeft = name; n.selectOnRight = settings; }
            ordered[i].navigation = n;
        }
        main.GetComponent<CanvasGroup>().alpha = 1;
        foreach (var c in main.GetComponentsInChildren<Component>(true))
            if (c) { EditorUtility.SetDirty(c); if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undo);
        Debug.Log("[MenuBrushV1] 主選單視覺已套用，請檢查後儲存 Scene。按鈕事件與原 Prefab 未改寫。");
    }

    static void StyleBrush(Button b, Sprite brush, TMP_FontAsset font, bool primary, bool small)
    {
        var animator = b.GetComponent<Animator>(); if (animator) animator.enabled = false;
        b.transition = Selectable.Transition.None;
        var hit = b.GetComponent<Image>(); hit.sprite = null; hit.type = Image.Type.Simple;
        hit.color = Color.clear; hit.raycastTarget = true;
        var group = b.GetComponent<CanvasGroup>(); if (group) group.alpha = 1;
        var baseImage = b.transform.Find("Background").GetComponent<Image>();
        Stretch(baseImage.rectTransform); baseImage.sprite = brush; baseImage.type = Image.Type.Simple;
        baseImage.color = primary ? Gold : Olive; baseImage.raycastTarget = false;
        var visualGroup = baseImage.GetComponent<CanvasGroup>();
        if (!visualGroup) visualGroup = Undo.AddComponent<CanvasGroup>(baseImage.gameObject);
        visualGroup.blocksRaycasts = false;
        var highlight = NewImage(baseImage.transform, "BrushHighlight");
        Stretch(highlight.rectTransform); highlight.sprite = brush; highlight.color = Gold;
        highlight.type = Image.Type.Filled; highlight.fillMethod = Image.FillMethod.Horizontal;
        highlight.fillOrigin = 0; highlight.fillAmount = 0; highlight.raycastTarget = false;
        highlight.transform.SetAsFirstSibling();
        var label = baseImage.transform.Find("ButtonLabel").GetComponent<TMP_Text>();
        label.font = font; label.fontSize = small ? 30 : 46; label.enableAutoSizing = false;
        label.enableWordWrapping = false; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = primary ? Ink : Cream; label.raycastTarget = false;
        PlaceLabel(label.rectTransform, 52, 360, 74);
        var arrow = NewText(baseImage.transform, "BrushArrow", font);
        arrow.text = "›"; arrow.fontSize = 48; arrow.color = label.color;
        arrow.alignment = TextAlignmentOptions.Center;
        var ar = arrow.rectTransform; ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(1, .5f);
        ar.anchoredPosition = new Vector2(-47, 0); ar.sizeDelta = new Vector2(36, 62);
        var visual = Undo.AddComponent<MenuBrushButtonVisual>(b.gameObject);
        Set(visual, "button", b); Set(visual, "visualRoot", baseImage.rectTransform);
        Set(visual, "highlight", highlight); Set(visual, "label", label); Set(visual, "arrow", arrow);
        Set(visual, "visualGroup", visualGroup); Set(visual, "labelRestPosition", new Vector2(52, 0));
        Set(visual, "normalText", primary ? Ink : Cream); Set(visual, "highlightedText", Ink);
    }

    static void StyleUtility(Button b)
    {
        var a = b.GetComponent<Animator>(); if (a) a.enabled = false;
        var image = b.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/UtilityPlate.png");
        image.type = Image.Type.Sliced; image.color = Color.white; image.raycastTarget = true;
        b.targetGraphic = image; b.transition = Selectable.Transition.ColorTint;
    }
    static void VerticalBand(RectTransform r, float low, float high, float width, float right)
    {
        r.anchorMin = new Vector2(1, low); r.anchorMax = new Vector2(1, high); r.pivot = new Vector2(1, .5f);
        r.anchoredPosition = new Vector2(-right, 0); r.sizeDelta = new Vector2(width, 0);
        r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
    }
    static void BoxRight(RectTransform r, float right, float top, float width, float height)
    {
        r.anchorMin = r.anchorMax = r.pivot = Vector2.one;
        r.anchoredPosition = new Vector2(-right, -top); r.sizeDelta = new Vector2(width, height);
        r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
    }
    static void PlaceLabel(RectTransform r, float x, float width, float height)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, .5f);
        r.anchoredPosition = new Vector2(x, 0); r.sizeDelta = new Vector2(width, height); r.localScale = Vector3.one;
    }
    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = Vector2.one * .5f;
        r.offsetMin = r.offsetMax = Vector2.zero; r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
    }
    static Image NewImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Menu decoration"); go.transform.SetParent(parent, false);
        return go.GetComponent<Image>();
    }
    static TMP_Text NewText(Transform parent, string name, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        Undo.RegisterCreatedObjectUndo(go, "Menu text"); go.transform.SetParent(parent, false);
        var text = go.GetComponent<TMP_Text>(); text.font = font; text.raycastTarget = false; return text;
    }
    static void Set(UnityEngine.Object target, string property, object value)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(property);
        if (p == null) throw new InvalidOperationException(property);
        if (value is UnityEngine.Object o) p.objectReferenceValue = o;
        else if (value is Color c) p.colorValue = c;
        else if (value is Vector2 v) p.vector2Value = v;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void EnsureAssets()
    {
        Directory.CreateDirectory(ArtFolder);
        // Brush.png is rasterized from the approved web BrushSource.svg.txt.
        // Never recreate an unrelated procedural approximation when this asset is missing.
        if (!File.Exists(ArtFolder + "/Brush.png"))
            throw new InvalidOperationException("缺少共用筆刷 Brush.png；請由 BrushSource.svg.txt 匯出透明 PNG，不要重建舊程序筆刷。");
        WriteTexture("RightShade", 512, 4, (x, y) =>
            new Color(1, 1, 1, Mathf.SmoothStep(0, .88f, Mathf.InverseLerp(190, 490, x))));
        WriteTexture("UtilityPlate", 96, 96, (x, y) =>
        {
            float d = Mathf.Min(x, y, 95 - x, 95 - y);
            float corner = Mathf.Min(x + y, x + 95 - y, 95 - x + y, 190 - x - y);
            if (corner < 8) return Color.clear;
            return d < 1.5f || corner < 10 ? new Color32(138, 136, 85, 255) : new Color32(27, 32, 25, 210);
        }, new Vector4(12, 12, 12, 12));
        string controllerPath = ArtFolder + "/MenuBrushFadeV1.controller";
        if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath))
        {
            // Preserve the SDK's working Show/Hide state structure in an independent copy.
            const string source = "Assets/Photon/FusionMenu/Runtime/RuntimeAssets/FusionMenuViewMainMenu.controller";
            if (!AssetDatabase.CopyAsset(source, controllerPath))
                throw new InvalidOperationException("無法建立獨立的主選單淡入淡出控制器。");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var child in machine.states)
            {
                var state = child.state;
                var clip = state.motion as AnimationClip;
                if (!clip || (state.name != "Show" && state.name != "Hide"))
                    throw new InvalidOperationException("原生主選單的 Show/Hide 結構已變更，請先檢查。");
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                bool showing = state.name == "Show";
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"),
                    AnimationCurve.EaseInOut(0, showing ? 0 : 1, showing ? .22f : .16f, showing ? 1 : 0));
                EditorUtility.SetDirty(clip); EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
        }
        AssetDatabase.SaveAssets();
    }

    static void WriteTexture(string name, int w, int h, Func<int, int, Color> pixel, Vector4 border = default)
    {
        string path = ArtFolder + "/" + name + ".png";
        if (File.Exists(path)) return;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        try
        {
            var colors = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) colors[y * w + x] = pixel(x, y);
            tex.SetPixels(colors); tex.Apply(); File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(tex); }
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.npotScale = TextureImporterNPOTScale.None; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 2048;
        imp.spriteBorder = border; imp.SaveAndReimport();
    }
}
