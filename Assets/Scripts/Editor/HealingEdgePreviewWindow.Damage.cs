using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// 沿用既有離屏繪製、HUD 保留與資源清理，只加入受傷樣式和模擬時間；不訂閱正式受傷事件。
public sealed partial class HealingEdgePreviewWindow
{
    internal const string DamageOutputPath = "deliverables/DamageEdgePreview_20261019";
    [Header("受傷預覽"), SerializeField, Tooltip("只切換 Editor 視覺，不綁定任何生命或傷害元件。")]
    bool damagePreview;
    [SerializeField, Tooltip("受傷獨立草稿，與治療的試調值分開保存。")]
    Style damageStyle = NewDamageStyle();
    [SerializeField, Tooltip("持續受傷的模擬扣血間隔，0.35～1.2 秒；不代表正式傷害 Tick。")]
    float damageInterval = .55f;
    [SerializeField, Tooltip("持續期間的最低視覺權重，0.10～0.65；避免每次扣血都全亮全滅。")]
    float damageFloor = .32f;

    [Serializable]
    public sealed class DamageTiming
    {
        public float interval = .55f;
        public float floor = .32f;
        public bool continuous = true;
    }

    float PreviewDuration => speedPreview ? SpeedDuration(speedStyle) : damagePreview
        ? .4f + (sustained ? 3f + damageStyle.fadeOut : damageStyle.fadeIn + damageStyle.hold + damageStyle.fadeOut) + 1f
        : CycleDuration(style, sustained);
    float PreviewWeight => speedPreview ? SpeedWeight(speedStyle, playhead, sustained) : damagePreview ? DamageWeight(damageStyle, playhead, sustained, damageInterval, damageFloor)
        : Weight(style, playhead, sustained);

    public static Style NewDamageStyle() => new Style
    {
        tint = new Color(.96f, .16f, .10f), strength = .66f, width = .15f,
        verticalWidth = .115f, corners = .50f, symbols = 0,
        fadeIn = .035f, hold = .065f, fadeOut = .52f
    };

    // 使用者手動點選才開窗；自動輸出不呼叫 GetWindow 或 Focus。
    [MenuItem("Tools/Purgers/Previews/Damage Edge (Preview Only)")]
    public static void OpenDamage()
    {
        var window = GetWindow<HealingEdgePreviewWindow>("受傷外框 · 預覽");
        window.damagePreview = true;
        window.speedPreview = false;
        window.titleContent = new GUIContent("受傷外框 · 預覽");
        window.sustained = false;
        window.playhead = 0;
        window.playing = true;
        window.bypass = false;
        window.minSize = new Vector2(760, 620);
    }

    static float HitPulse(Style style, float elapsed)
    {
        if (elapsed < 0) return 0;
        float attack = Mathf.Max(.01f, style.fadeIn);
        if (elapsed < attack) return Mathf.SmoothStep(0, 1, elapsed / attack);
        elapsed -= attack;
        if (elapsed < style.hold) return 1;
        return 1 - Mathf.SmoothStep(0, 1, (elapsed - style.hold) / Mathf.Max(.01f, style.fadeOut));
    }

    /// <summary>持續模式以模擬扣血時刻刷新脈衝；權重取最大、不累加。停止時從實際權重接續淡出。</summary>
    public static float DamageWeight(Style style, float time, bool continuous, float interval = .55f, float floor = .32f)
    {
        float elapsed = time - .4f;
        if (!continuous) return HitPulse(style, elapsed);
        if (elapsed < 0) return 0;
        interval = Mathf.Clamp(interval, .35f, 1.2f);
        floor = Mathf.Clamp(floor, .10f, .65f);
        // 結束時刻不產生新 Tick；避免間隔剛好整除 3 秒時，尾端意外重新變亮。
        float sample = Mathf.Min(elapsed, 3f - .00001f);
        // 同時取最近與上一擊，避免刷新時突然從亮掉回底層。
        float age = sample - Mathf.Floor(sample / interval) * interval;
        float pulse = Mathf.Max(HitPulse(style, age), HitPulse(style, age + interval));
        float onset = Mathf.SmoothStep(0, 1, sample / Mathf.Max(.01f, style.fadeIn));
        float weight = Mathf.Max(floor * onset, .84f * pulse);
        if (elapsed > 3f) weight *= 1 - Mathf.SmoothStep(0, 1, (elapsed - 3f) / Mathf.Max(.01f, style.fadeOut));
        return Mathf.Clamp01(weight);
    }

    /// <summary>輸出兩段 Unity 真實 shader 動畫；只有歷史背景，沒有模擬扣血或寫入場景。</summary>
    public static string ExportDamageReview()
    {
        Directory.CreateDirectory(DamageOutputPath);
        var s = NewDamageStyle();
        var material = CreateMaterial();
        var background = LoadBackground();
        RenderTexture rt = null;
        try
        {
            EnsureTarget(ref rt, 1920, 1080);
            Draw(material, background, rt, s, .46f, 0, 0, false, true, true);
            WritePng(rt, DamageOutputPath + "/before.png");
            foreach (bool continuous in new[] { false, true })
            {
                string name = continuous ? "continuous" : "single";
                string folder = DamageOutputPath + "/" + name;
                Directory.CreateDirectory(folder);
                EnsureTarget(ref rt, 1920, 1080);
                float weight = DamageWeight(s, .46f, continuous);
                Draw(material, background, rt, s, .46f, weight, 0, false, true, true);
                WritePng(rt, DamageOutputPath + "/" + name + "-peak.png");
                Draw(material, background, rt, s, .46f, weight, 0, true, true, true);
                WritePng(rt, DamageOutputPath + "/" + name + "-overlay.png");
                EnsureTarget(ref rt, 768, 432);
                int count = continuous ? 125 : 70;
                for (int i = 0; i < count; i++)
                {
                    float time = i / 25f;
                    Draw(material, background, rt, s, time, DamageWeight(s, time, continuous), 0, false, true, true);
                    WritePng(rt, folder + "/" + i.ToString("D3") + ".png");
                }
            }
            EnsureTarget(ref rt, 1680, 720);
            Draw(material, background, rt, s, .46f, 1, 1, false, false, true);
            WritePng(rt, DamageOutputPath + "/ultrawide.png");
            EnsureTarget(ref rt, 1920, 1080);
            Draw(material, background, rt, s, .46f, 1, 1, false, false, true);
            WritePng(rt, DamageOutputPath + "/dark.png");
            Draw(material, background, rt, s, .46f, 1, 2, false, false, true);
            WritePng(rt, DamageOutputPath + "/light.png");
            // 原治療峰值的像素比較，確認共用 shader 擴充沒有改掉既有外觀。
            EnsureTarget(ref rt, 1920, 1080);
            Draw(material, background, rt, new Style { width = .14f, verticalWidth = .10f }, 1.3f, 1, 0, false);
            WritePng(rt, DamageOutputPath + "/healing-control.png");
            File.WriteAllText(DamageOutputPath + "/style.json", JsonUtility.ToJson(s, true));
            File.WriteAllText(DamageOutputPath + "/timing.json", JsonUtility.ToJson(new DamageTiming(), true));
            return Path.GetFullPath(DamageOutputPath);
        }
        finally
        {
            DestroyImmediate(material); DestroyImmediate(background);
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        }
    }
}
