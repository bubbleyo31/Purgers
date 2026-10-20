using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>純 Editor 外框視覺研究；不綁玩家、不修改場景、不讀網路狀態，也不建立正式 Runtime 元件。</summary>
public sealed partial class HealingEdgePreviewWindow : EditorWindow
{
    internal const string ShaderPath = "Assets/Scripts/Editor/HealingEdgePreview.shader";
    internal const string BackgroundPath = "Documentation/ProjectArchitecture/Validation/SafeHouseGameplay/hud-scene.png";
    internal const string OutputPath = "deliverables/HealingEdgePreview_20261019/v1.2";

    [Serializable]
    public sealed class Style
    {
        [Header("外框樣式／預覽用"), Tooltip("外框主色；不改正式 LGG，Alpha 不參與混合。")]
        public Color tint = new Color(.30f, .95f, .64f, 1f);
        [Tooltip("外框不透明度倍率，0～1；0 完全關閉。")]
        public float strength = .52f;
        [Tooltip("每側漸層占畫面寬度比例，0.04～0.24；四角延伸為此值的 1.65 倍。")]
        public float width = .105f;
        [Tooltip("上、下柔光各占畫面高度比例，0.03～0.20。")]
        public float verticalWidth = .075f;
        [Tooltip("四角額外加強，0～1；0 仍保留左右柔光。")]
        public float corners = .55f;
        [Tooltip("四邊合計十字符號，0～14；前八個在兩側，後六個分布上下；0 關閉符號。")]
        public int symbols = 12;
        [Header("預覽節奏"), Tooltip("淡入秒數，0.01～2；純預覽時間，不是玩法期限。")]
        public float fadeIn = .10f;
        [Tooltip("單次維持秒數，0～3；持續治療模擬 2.6 秒，持續受傷模擬 3 秒。")]
        public float hold = .16f;
        [Tooltip("淡出秒數，0.01～3。")]
        public float fadeOut = .70f;
    }

    [Header("視窗狀態"), SerializeField, Tooltip("此視窗的獨立試調值，不寫入任何正式設定資產。")]
    Style style = new Style();
    [SerializeField, Tooltip("以 Editor 時鐘播放，無需 Play Mode。")]
    bool playing = true;
    [SerializeField, Tooltip("持續模式：治療模擬 2.6 秒、受傷模擬 3 秒；關閉為單次。")]
    bool sustained = true;
    [SerializeField, Tooltip("開啟時只顯示背景，供前後對照。")]
    bool bypass;
    [SerializeField, Tooltip("預覽時間，秒。")]
    float playhead = 1.3f;
    [SerializeField, Tooltip("0 歷史截圖、1 深色、2 淺色。")]
    int backgroundMode;
    [SerializeField, Tooltip("0 為 16:9、1 為 21:9、2 為 16:10。")]
    int aspectMode;
    [SerializeField, Tooltip("選填背景貼圖；不修改原圖。自訂背景不套用歷史安全屋專用 HUD 保留遮罩。")]
    Texture2D customBackground;
    Material material;
    Texture2D defaultBackground;
    RenderTexture target;
    double previousTime;
    Vector2 scroll;
    static readonly string[] Backgrounds = { "歷史安全屋截圖", "深色檢視", "淺色檢視" };
    static readonly string[] Aspects = { "16 : 9", "21 : 9", "16 : 10" };

    // 只有使用者點選此選單才開窗；自動離屏輸出不呼叫這個入口。
    [MenuItem("Tools/Purgers/Previews/Healing Edge (Preview Only)")]
    public static void Open()
    {
        var window = GetWindow<HealingEdgePreviewWindow>("治療外框 · 預覽");
        window.damagePreview = false;
        window.speedPreview = false;
        // 只遷移上一版未調整的寬高；保留使用者其他試調參數。
        if (Mathf.Approximately(window.style.width, .14f) && Mathf.Approximately(window.style.verticalWidth, .10f))
        { window.style.width = .105f; window.style.verticalWidth = .075f; }
        window.titleContent = new GUIContent("治療外框 · 預覽");
        window.playhead = 0;
        window.playing = true;
        window.bypass = false;
        window.minSize = new Vector2(760, 580);
    }

    void OnEnable()
    {
        previousTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (material != null) DestroyImmediate(material);
        if (defaultBackground != null) DestroyImmediate(defaultBackground);
        if (target != null) { target.Release(); DestroyImmediate(target); }
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - previousTime < 1.0 / 30.0) return;
        if (playing) playhead = (playhead + (float)Math.Min(now - previousTime, .1)) % PreviewDuration;
        previousTime = now;
        if (playing) Repaint();
    }

    void OnGUI()
    {
        Style activeStyle = speedPreview ? speedStyle : damagePreview ? damageStyle : style;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField(speedPreview ? "MOMENTUM / 速度動能增益外框 V1" : damagePreview ? "DAMAGE / 受傷外框 V1" : "HEALING / 治療外框 V1.2", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("純視覺預覽，不需 Play Mode。背景為歷史截圖，血量不會變動；未接入正式 HUD、傷害、治療或調色。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        playing = GUILayout.Toggle(playing, "播放", "Button", GUILayout.Width(65));
        if (GUILayout.Button(speedPreview ? "蓄能→滿格" : damagePreview ? "單次傷害" : "單次治療", GUILayout.Width(90))) { sustained = false; playhead = 0; playing = true; }
        if (GUILayout.Button(speedPreview ? "滿格維持" : damagePreview ? "持續受傷" : "持續治療", GUILayout.Width(90))) { sustained = true; playhead = 0; playing = true; }
        if (GUILayout.Button("停止／清除", GUILayout.Width(95))) { playing = false; playhead = PreviewDuration; }
        bypass = GUILayout.Toggle(bypass, "對照：關閉效果", "Button", GUILayout.Width(125));
        GUILayout.FlexibleSpace();
        aspectMode = EditorGUILayout.Popup(aspectMode, Aspects, GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();
        playhead = EditorGUILayout.Slider("時間（秒）", playhead, 0, PreviewDuration);

        if (material == null) material = CreateMaterial();
        if (defaultBackground == null) defaultBackground = LoadBackground();
        float aspect = aspectMode == 1 ? 21f / 9 : aspectMode == 2 ? 1.6f : 16f / 9;
        float viewWidth = Mathf.Max(320, position.width - 36);
        float viewHeight = Mathf.Min(viewWidth / aspect, Mathf.Max(220, position.height - 325));
        Rect area = GUILayoutUtility.GetRect(viewWidth, viewHeight);
        float drawWidth = Mathf.Min(area.width, area.height * aspect);
        Rect imageRect = new Rect(area.center.x - drawWidth * .5f, area.y, drawWidth, drawWidth / aspect);
        int width = Mathf.RoundToInt(imageRect.width * EditorGUIUtility.pixelsPerPoint);
        int height = Mathf.RoundToInt(width / aspect);
        EnsureTarget(ref target, width, height);
        if (Event.current.type == EventType.Repaint)
        {
            Draw(material, customBackground != null ? customBackground : defaultBackground, target, activeStyle,
                playhead, bypass ? 0 : PreviewWeight, backgroundMode, false, customBackground == null, damagePreview, speedPreview, speedRate);
            GUI.DrawTexture(imageRect, target, ScaleMode.StretchToFill, false);
        }
        EditorGUILayout.LabelField("四邊柔光 · 中央透明 · 歷史截圖 HUD 保留原色", EditorStyles.centeredGreyMiniLabel);
        backgroundMode = EditorGUILayout.Popup("背景", backgroundMode, Backgrounds);
        customBackground = (Texture2D)EditorGUILayout.ObjectField("自訂背景（選填）", customBackground, typeof(Texture2D), false);
        activeStyle.tint = EditorGUILayout.ColorField(speedPreview ? "速度動能增益色" : damagePreview ? "受傷色" : "治療色", activeStyle.tint);
        activeStyle.strength = EditorGUILayout.Slider("外框強度", activeStyle.strength, 0, 1);
        activeStyle.width = EditorGUILayout.Slider("單側延伸寬度", activeStyle.width, .04f, .24f);
        activeStyle.verticalWidth = EditorGUILayout.Slider("上下延伸高度", activeStyle.verticalWidth, .03f, .20f);
        activeStyle.corners = EditorGUILayout.Slider("四角加強", activeStyle.corners, 0, 1);
        if (!damagePreview && !speedPreview) activeStyle.symbols = EditorGUILayout.IntSlider("十字數量（四邊合計）", activeStyle.symbols, 0, 14);
        EditorGUILayout.BeginHorizontal();
        activeStyle.fadeIn = Mathf.Clamp(EditorGUILayout.FloatField("淡入秒數", activeStyle.fadeIn), .01f, 2f);
        activeStyle.fadeOut = Mathf.Clamp(EditorGUILayout.FloatField("淡出秒數", activeStyle.fadeOut), .01f, 3f);
        EditorGUILayout.EndHorizontal();
        if (!speedPreview) activeStyle.hold = Mathf.Clamp(EditorGUILayout.FloatField("單次維持秒數", activeStyle.hold), 0, 3f);
        if (speedPreview)
        {
            EditorGUILayout.LabelField("柔光外框 · 無動態流線", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("模擬動能：" + Mathf.RoundToInt(PreviewWeight * 100) + "% · 歷史背景量條不變動；未接入實際動能。", EditorStyles.miniLabel);
        }
        if (damagePreview)
        {
            damageInterval = EditorGUILayout.Slider("模擬扣血間隔（秒）", damageInterval, .35f, 1.2f);
            damageFloor = EditorGUILayout.Slider("持續底層強度", damageFloor, .10f, .65f);
            EditorGUILayout.LabelField("持續模式模擬 3 秒扣血；僅預覽節奏，不影響真實傷害 Tick。", EditorStyles.miniLabel);
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(speedPreview ? "還原速度動能 V1" : damagePreview ? "還原受傷 V1" : "還原 V1.2"))
        {
            if (speedPreview) { speedStyle = NewSpeedStyle(); speedRate = 1.6f; }
            else if (damagePreview) { damageStyle = NewDamageStyle(); damageInterval = .55f; damageFloor = .32f; }
            else style = new Style();
        }
        if (GUILayout.Button("匯出目前畫面 PNG"))
        {
            string directory = speedPreview ? SpeedOutputPath : damagePreview ? DamageOutputPath : OutputPath;
            Directory.CreateDirectory(directory);
            WritePng(target, Path.Combine(directory, "current.png"));
            File.WriteAllText(Path.Combine(directory, "current-style.json"), JsonUtility.ToJson(activeStyle, true));
            if (speedPreview) File.WriteAllText(Path.Combine(directory, "current-motion.json"), JsonUtility.ToJson(new SpeedMotion { rate = speedRate, fullOnly = sustained }, true));
            if (damagePreview) File.WriteAllText(Path.Combine(directory, "current-timing.json"),
                JsonUtility.ToJson(new DamageTiming { interval = damageInterval, floor = damageFloor, continuous = sustained }, true));
            ShowNotification(new GUIContent("已匯出至 " + directory));
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    public static float CycleDuration(Style s, bool sustained) => .3f + s.fadeIn + (sustained ? 2.6f : s.hold) + s.fadeOut + .7f;

    public static float Weight(Style s, float time, bool sustained)
    {
        float t = time - .3f;
        float hold = sustained ? 2.6f : s.hold;
        if (t <= 0) return 0;
        if (t < s.fadeIn) return Mathf.SmoothStep(0, 1, t / Mathf.Max(.01f, s.fadeIn));
        t -= s.fadeIn;
        if (t < hold) return 1;
        return 1 - Mathf.SmoothStep(0, 1, (t - hold) / Mathf.Max(.01f, s.fadeOut));
    }

    static Material CreateMaterial()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Healing preview shader unavailable or failed compilation.");
        return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    static Texture2D LoadBackground()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        if (File.Exists(BackgroundPath) && texture.LoadImage(File.ReadAllBytes(BackgroundPath))) return texture;
        texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
        return texture;
    }

    static void EnsureTarget(ref RenderTexture rt, int width, int height)
    {
        if (rt != null && rt.width == width && rt.height == height) return;
        if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        { hideFlags = HideFlags.HideAndDontSave };
        rt.Create();
    }

    public static void Draw(Material m, Texture background, RenderTexture rt, Style s, float time, float weight, int mode, bool onlyEffect,
        bool protectHistoricalHud = true, bool damage = false, bool speed = false, float motionRate = 1.6f)
    {
        m.SetFloat("_Speed", speed ? 1 : 0);
        m.SetFloat("_SpeedRate", motionRate);
        m.SetFloat("_Damage", damage ? 1 : 0);
        m.SetColor("_Tint", QualitySettings.activeColorSpace == ColorSpace.Linear ? s.tint.linear : s.tint);
        m.SetFloat("_Strength", Mathf.Clamp01(s.strength)); m.SetFloat("_Width", s.width);
        m.SetFloat("_VerticalWidth", s.verticalWidth);
        m.SetFloat("_Corner", s.corners); m.SetFloat("_Clock", time); m.SetFloat("_Weight", Mathf.Clamp01(weight));
        m.SetFloat("_Aspect", (float)rt.width / rt.height); m.SetFloat("_Symbols", damage || speed ? 0 : s.symbols);
        m.SetFloat("_BackgroundMode", mode); m.SetFloat("_OnlyEffect", onlyEffect ? 1 : 0);
        m.SetFloat("_ProtectHUD", mode == 0 && protectHistoricalHud ? 1 : 0);
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(background, rt, m);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }

    static void WritePng(RenderTexture rt, string path)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; DestroyImmediate(pixels); }
    }

    /// <summary>無焦點離屏輸出；不開窗、不切場景。動畫背景是歷史靜態截圖，非本次實戰錄影。</summary>
    public static string ExportReview()
    {
        Directory.CreateDirectory(OutputPath);
        var s = new Style();
        var m = CreateMaterial();
        var bg = LoadBackground();
        RenderTexture rt = null;
        try
        {
            EnsureTarget(ref rt, 1920, 1080);
            Draw(m, bg, rt, s, 1.3f, 0, 0, false); WritePng(rt, OutputPath + "/before.png");
            Draw(m, bg, rt, s, 1.3f, 1, 0, false); WritePng(rt, OutputPath + "/healing-peak.png");
            Draw(m, bg, rt, s, 1.3f, 1, 1, false); WritePng(rt, OutputPath + "/dark.png");
            Draw(m, bg, rt, s, 1.3f, 1, 2, false); WritePng(rt, OutputPath + "/light.png");
            Draw(m, bg, rt, s, 1.3f, 1, 0, true); WritePng(rt, OutputPath + "/overlay.png");
            EnsureTarget(ref rt, 1680, 720);
            Draw(m, bg, rt, s, 1.3f, 1, 1, false); WritePng(rt, OutputPath + "/ultrawide.png");
            EnsureTarget(ref rt, 1920, 1080);
            Draw(m, bg, rt, NewDamageStyle(), .46f, 1, 0, false, true, true);
            WritePng(rt, OutputPath + "/damage-control.png");
            EnsureTarget(ref rt, 960, 540);
            foreach (bool continuous in new[] { false, true })
            {
                string folder = Path.Combine(OutputPath, continuous ? "continuous" : "single");
                Directory.CreateDirectory(folder);
                int count = continuous ? 88 : 40;
                for (int i = 0; i < count; ++i)
                {
                    float time = i / 20f;
                    Draw(m, bg, rt, s, time, Weight(s, time, continuous), 0, false);
                    WritePng(rt, folder + "/" + i.ToString("D3") + ".png");
                }
            }
            File.WriteAllText(OutputPath + "/style.json", JsonUtility.ToJson(s, true));
            return Path.GetFullPath(OutputPath);
        }
        finally
        {
            DestroyImmediate(m); DestroyImmediate(bg);
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        }
    }
}
