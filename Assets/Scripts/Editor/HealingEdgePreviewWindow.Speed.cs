using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// 動能增益的 Editor 外框草稿，僅用模擬比例，不讀寫正式量條或玩家動能。
public sealed partial class HealingEdgePreviewWindow
{
    internal const string SpeedOutputPath = "deliverables/MomentumEdgePreview_20261019";
    [SerializeField, Tooltip("只選擇速度動能增益預覽，不啟動任何玩法。")]
    bool speedPreview;
    [SerializeField, Tooltip("獨立增益樣式，預覽調整不寫正式資產。")]
    Style speedStyle = NewSpeedStyle();
    [SerializeField, Tooltip("邊緣流線的移動倍率，0.5～3。與世界速度無關。")]
    float speedRate = 1.6f;
    [Serializable] public sealed class SpeedMotion { public float rate = 1.6f; public bool fullOnly; }

    public static Style NewSpeedStyle() => new Style
    {
        tint = new Color(1f, .53f, .075f), strength = .64f, width = .105f,
        verticalWidth = .075f, corners = .60f, symbols = 0,
        fadeIn = .16f, hold = 0, fadeOut = .65f
    };

    public static float SpeedDuration(Style s) => 4.4f + s.fadeOut + .75f;

    // 固定 0.4～4.4 秒展示；此時間不代表正式充能或最高強化保持時間。
    public static float SpeedWeight(Style s, float time, bool fullOnly)
    {
        float t = time - .4f;
        float fullTime = fullOnly ? 0 : 2.2f;
        if (t <= fullTime) return 0;
        float onset = Mathf.SmoothStep(0, 1, (t-fullTime) / Mathf.Max(.01f, s.fadeIn));
        float release = 1 - Mathf.SmoothStep(0, 1, (t - 4f) / Mathf.Max(.01f, s.fadeOut));
        return Mathf.Clamp01(onset * release);
    }

    [MenuItem("Tools/Purgers/Previews/Momentum Edge (Preview Only)")]
    public static void OpenSpeed()
    {
        var window = GetWindow<HealingEdgePreviewWindow>("速度動能增益 · 預覽");
        window.speedPreview = true; window.damagePreview = false;
        window.titleContent = new GUIContent("速度動能增益 · 預覽");
        window.sustained = false; window.playhead = 0; window.playing = true; window.bypass = false;
        window.minSize = new Vector2(760, 620);
    }

    public static string ExportSpeedReview()
    {
        Directory.CreateDirectory(SpeedOutputPath);
        Directory.CreateDirectory(SpeedOutputPath + "/frames");
        var s = NewSpeedStyle();
        var m = CreateMaterial(); var bg = LoadBackground(); RenderTexture rt = null;
        try
        {
            EnsureTarget(ref rt, 1920, 1080);
            Draw(m, bg, rt, s, 3.2f, 0, 0, false, true, false, true);
            WritePng(rt, SpeedOutputPath + "/before.png");
            Draw(m, bg, rt, s, 3.2f, 1, 0, false, true, false, true);
            WritePng(rt, SpeedOutputPath + "/peak.png");
            Draw(m, bg, rt, s, 3.2f, 1, 0, true, true, false, true);
            WritePng(rt, SpeedOutputPath + "/overlay.png");
            Draw(m, bg, rt, s, 3.2f, 1, 1, false, false, false, true);
            WritePng(rt, SpeedOutputPath + "/dark.png");
            Draw(m, bg, rt, s, 3.2f, 1, 2, false, false, false, true);
            WritePng(rt, SpeedOutputPath + "/light.png");
            Draw(m, bg, rt, new Style(), 1.3f, 1, 0, false);
            WritePng(rt, SpeedOutputPath + "/healing-control.png");
            Draw(m, bg, rt, NewDamageStyle(), .46f, 1, 0, false, true, true);
            WritePng(rt, SpeedOutputPath + "/damage-control.png");
            EnsureTarget(ref rt, 1680, 720);
            Draw(m, bg, rt, s, 3.2f, 1, 1, false, false, false, true);
            WritePng(rt, SpeedOutputPath + "/ultrawide.png");
            EnsureTarget(ref rt, 960, 540);
            for (int i = 0; i < 145; i++)
            {
                float time = i / 25f;
                Draw(m, bg, rt, s, time, SpeedWeight(s, time, false), 0, false, true, false, true);
                WritePng(rt, SpeedOutputPath + "/frames/" + i.ToString("D3") + ".png");
            }
            File.WriteAllText(SpeedOutputPath + "/style.json", JsonUtility.ToJson(s, true));
            File.WriteAllText(SpeedOutputPath + "/motion.json", JsonUtility.ToJson(new SpeedMotion(), true));
            return Path.GetFullPath(SpeedOutputPath);
        }
        finally
        {
            DestroyImmediate(m); DestroyImmediate(bg);
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        }
    }
}
