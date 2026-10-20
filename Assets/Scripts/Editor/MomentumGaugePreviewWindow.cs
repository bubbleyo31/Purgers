using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only visual sample. Does not instantiate a player, edit a scene, or access gameplay state.</summary>
public sealed class MomentumGaugePreviewWindow : EditorWindow
{
    public const string ShaderPath = "Assets/Scripts/Editor/MomentumGaugePreview.shader";
    public const string SourcePath = "Assets/_Project_Assets/UI/BattleHUD/Layout/Momentum.png";
    public const string OutputPath = "deliverables/MomentumGaugePreview_20261019";
    public static readonly Color FullTint = new Color32(0x25, 0xFF, 0xD9, 0xFF);
    static readonly Color NormalTint = new Color32(54, 171, 246, 255);
    static readonly Color Background = new Color32(25, 32, 30, 255);
    [SerializeField] float energy = 1;
    [SerializeField] float speed = .5f;
    [SerializeField] float fullSpeedMultiplier = 2;
    [SerializeField] float strength = .7f;
    [SerializeField] float clock;
    [SerializeField] bool playing = true;
    [SerializeField] bool cycle;
    double lastTick;
    Material material;
    RenderTexture normalTarget, previewTarget;
    Texture source;
    float normalPhase, previewPhase;

    // User-operated menu only. Automated export never calls GetWindow/Show/Focus.
    [MenuItem("Tools/Purgers/Previews/Momentum Gauge (Preview Only)")]
    public static void Open()
    {
        var window = GetWindow<MomentumGaugePreviewWindow>("動能量條 · 獨立預覽");
        window.minSize = new Vector2(640, 440);
    }

    void OnEnable()
    {
        lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (material != null) DestroyImmediate(material);
        Release(ref normalTarget);
        Release(ref previewTarget);
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - lastTick < 1.0 / 30) return;
        if (playing)
        {
            float delta = (float)Math.Min(.1, now - lastTick);
            clock += delta;
            float shown = cycle ? CycleEnergy(clock) : energy;
            normalPhase = Mathf.Repeat(normalPhase + delta * speed, 1);
            previewPhase = Mathf.Repeat(previewPhase + delta * speed * (shown >= 1 ? fullSpeedMultiplier : 1), 1);
        }
        lastTick = now;
        if (playing) Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("固定量條 / #25FFD9", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("沿用正式量條的流光核心與 Momentum.png。不需 Play Mode；未滿慢速流動，滿值變色並加速，尺寸與文字不呼吸。", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            playing = GUILayout.Toggle(playing, "播放", "Button", GUILayout.Width(64));
            if (GUILayout.Button("滿值維持", GUILayout.Width(90))) { energy = 1; cycle = false; }
            if (GUILayout.Button("一般 65%", GUILayout.Width(90))) { energy = .65f; cycle = false; }
            if (GUILayout.Button("充滿 → 退出", GUILayout.Width(110))) { clock = 0; cycle = true; playing = true; }
        }
        EditorGUI.BeginChangeCheck();
        energy = EditorGUILayout.Slider("模擬增傷比例", energy, 0, 1);
        if (EditorGUI.EndChangeCheck()) cycle = false;
        speed = EditorGUILayout.Slider("一般流動速度", speed, .1f, 1.5f);
        fullSpeedMultiplier = EditorGUILayout.Slider("滿值流速倍率", fullSpeedMultiplier, 1, 4);
        strength = EditorGUILayout.Slider("紋理強度", strength, 0, 1);
        float shown = cycle ? CycleEnergy(clock) : energy;
        EditorGUILayout.LabelField("一般對照 · 傷害增加65%", EditorStyles.centeredGreyMiniLabel);
        DrawPreview(ref normalTarget, .65f, normalPhase);
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("傷害增加" + Mathf.RoundToInt(shown * 100) + "%" + (shown >= 1 ? " · 滿值流光" : ""), EditorStyles.centeredGreyMiniLabel);
        DrawPreview(ref previewTarget, shown, previewPhase);
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("兩條等尺寸 · 保留斜角輪廓 · 由左往右流動 · 無周邊特效", EditorStyles.centeredGreyMiniLabel);
        if (GUILayout.Button("匯出 Unity 動畫影格與檢查結果"))
            Debug.Log("[Momentum Gauge Preview] " + ExportReview());
    }

    void DrawPreview(ref RenderTexture target, float value, float phase)
    {
        // The 562 x 50 presentation reflects the existing gauge container and fill Y scale.
        // Window fitting applies equally to both states; energy never changes the outer rectangle.
        Rect area = GUILayoutUtility.GetRect(100, 562, 66, 66);
        float width = Mathf.Min(562, area.width - 24);
        Rect rect = new Rect(area.center.x - width / 2, area.center.y - width * (50f / 562) / 2, width, width * (50f / 562));
        if (Event.current.type != EventType.Repaint) return;
        if (material == null) material = CreateMaterial();
        if (source == null) source = LoadSource();
        EnsureTarget(ref target);
        Draw(material, source, target, phase, value, 1, strength, false);
        GUI.DrawTexture(rect, target, ScaleMode.StretchToFill, false);
    }

    public static float CycleEnergy(float time)
    {
        float t = Mathf.Repeat(time, 8);
        if (t < 2) return Mathf.Lerp(.65f, 1, t / 2);
        if (t < 6) return 1;
        return Mathf.Lerp(1, .65f, (t - 6) / 2);
    }

    static Texture LoadSource()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourcePath);
        if (texture == null) throw new InvalidOperationException("Missing existing gauge art: " + SourcePath);
        return texture;
    }

    static Material CreateMaterial()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Gauge preview shader missing or unsupported.");
        return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    static void EnsureTarget(ref RenderTexture rt)
    {
        if (rt != null) return;
        rt = new RenderTexture(1124, 100, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { hideFlags = HideFlags.HideAndDontSave };
        rt.Create();
    }

    static void Release(ref RenderTexture rt)
    {
        if (rt == null) return;
        rt.Release(); DestroyImmediate(rt); rt = null;
    }

    static Color WorkingColor(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

    public static void Draw(Material m, Texture texture, RenderTexture rt, float time, float value, float rate, float intensity, bool applyFullSpeed = true)
    {
        m.SetColor("_Tint", WorkingColor(FullTint));
        m.SetColor("_NormalTint", WorkingColor(NormalTint));
        m.SetColor("_Background", WorkingColor(Background));
        m.SetFloat("_Clock", time); m.SetFloat("_Energy", Mathf.Clamp01(value));
        m.SetFloat("_Speed", rate * (applyFullSpeed && value >= 1 ? 2 : 1)); m.SetFloat("_Strength", intensity);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(texture, rt, m);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }

    static Color32[] ReadPixels(RenderTexture rt, string path = null)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply();
            if (path != null) File.WriteAllBytes(path, pixels.EncodeToPNG());
            return pixels.GetPixels32();
        }
        finally { RenderTexture.active = previous; DestroyImmediate(pixels); }
    }

    static int Differences(Color32[] a, Color32[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) count++;
        return count;
    }

    /// <summary>No Editor windows, scene changes, player objects, or production material writes.</summary>
    public static string ExportReview()
    {
        Directory.CreateDirectory(OutputPath + "/frames");
        Directory.CreateDirectory(OutputPath + "/normal-frames");
        Material m = CreateMaterial(); RenderTexture rt = null;
        try
        {
            Texture texture = LoadSource(); EnsureTarget(ref rt);
            Draw(m, texture, rt, 0, .65f, .5f, .7f);
            var normalA = ReadPixels(rt, OutputPath + "/normal.png");
            Draw(m, texture, rt, .75f, .65f, .5f, .7f);
            int normalChanges = Differences(normalA, ReadPixels(rt));
            Draw(m, texture, rt, 0, 1, .5f, .7f);
            var fullA = ReadPixels(rt, OutputPath + "/full.png");
            Draw(m, texture, rt, .75f, 1, .5f, .7f);
            var fullB = ReadPixels(rt, OutputPath + "/full-later.png");
            int animatedPixels = Differences(fullA, fullB);
            Draw(m, texture, rt, 0, 1, .5f, 0);
            var solid = ReadPixels(rt, OutputPath + "/solid-25FFD9.png");
            Color32 center = solid[50 * rt.width + rt.width / 2];
            bool tintMatches = Math.Abs(center.r - 37) <= 1 && Math.Abs(center.g - 255) <= 1 && Math.Abs(center.b - 217) <= 1;
            int outsideChanges = 0;
            Color32 bg = solid[0];
            for (int i = 0; i < solid.Length; i++)
                if (solid[i].Equals(bg) && !fullA[i].Equals(fullB[i])) outsideChanges++;
            for (int i = 0; i < 96; i++)
            {
                Draw(m, texture, rt, i / 24f, 1, .5f, .7f);
                ReadPixels(rt, OutputPath + "/frames/" + i.ToString("D3") + ".png");
                Draw(m, texture, rt, i / 24f, .65f, .5f, .7f);
                ReadPixels(rt, OutputPath + "/normal-frames/" + i.ToString("D3") + ".png");
            }
            string report = "Source=" + SourcePath + "\nFullColor=#25FFD9\nSampleRGB=" + center.r + "," + center.g + "," + center.b +
                "\nNormalTimeDifferences=" + normalChanges + "\nFullAnimatedPixels=" + animatedPixels + "\nOutsideSilhouetteChanges=" + outsideChanges +
                "\nFrames=96\nFramesPerSecond=24\nFixedRenderSize=1124x100\nPreviewOnly=true\n";
            bool passed = tintMatches && normalChanges > 100 && animatedPixels > 100 && outsideChanges == 0;
            File.WriteAllText(OutputPath + "/verification.txt", report + "Passed=" + passed + "\n");
            if (!passed) throw new InvalidOperationException("Preview render checks failed: " + report);
            return Path.GetFullPath(OutputPath);
        }
        finally { DestroyImmediate(m); Release(ref rt); }
    }
}
