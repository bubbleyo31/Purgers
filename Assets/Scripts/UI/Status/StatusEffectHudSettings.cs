using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Purgers/UI/異常狀態顯示設定")]
public sealed class StatusEffectHudSettings : ScriptableObject
{
    [Serializable] public struct Style
    {
        public StatusEffectKind kind;
        [Tooltip("已確認的透明圖示；保持原色及完整比例，不裁切。")]
        public Sprite icon;
        public Color barColor;
    }
    [Tooltip("各異常圖示與量條色彩；不影響持續時間或遊戲效果。")]
    public Style[] styles = Array.Empty<Style>();
    [Header("頭頂顯示（1080 高度參考像素）")]
    [Min(8), Tooltip("怪物與隊友頭頂圖示高度；量條使用血條剩餘寬度。")]
    public float worldIconSize = 16;
    [Min(2)] public float worldBarHeight = 4;
    [Min(0)] public float worldRowGap = 3;
    [Header("自己 HUD（Canvas 單位）")]
    [Min(8)] public float selfIconSize = 24;
    [Min(2)] public float selfBarHeight = 6;
    [Min(0)] public float selfRowGap = 4;
    [Min(40), Tooltip("自己異常列寬度；以準心所在的 HUD 中心置中。")]
    public float selfWidth = 280;
    [Min(0), Tooltip("異常第一列頂端與畫面中心準心的距離，Canvas 單位；不移動生命條。")]
    public float selfCrosshairOffset = 80;
    [Tooltip("自己異常與格檔區相對畫面中心的水平偏移；可用 HUD 排版工具拖曳。")]
    public float selfHorizontalOffset;
    [Header("格檔耐力（與自己異常列共用區域）")]
    [Range(8,24), Tooltip("格檔狀態與耐力數字的字級；只影響格檔列，標籤高度隨字級保留空間。")]
    public float guardFontSize = 14;
    [Range(1,1.5f), Tooltip("格檔列相對自己異常列的寬度倍率；以同一中心加寬，不改異常圖示或施法條，且不超出父層。")]
    public float guardWidthMultiplier = 1.1f;
    [Min(2), Tooltip("格檔條厚度，Canvas 單位，至少 2；不影響耐力值。")]
    public float guardBarHeight = 8;
    [Min(0), Tooltip("格檔列底部到第一個異常圖示的間距，Canvas 單位，至少 0。")]
    public float guardStatusGap = 8;
    [Tooltip("格檔耐力正常填色；與施法黃條分開，使用淡金色。")]
    public Color guardFillColor = new Color(.835f,.769f,.369f,1);
    [Tooltip("格檔標籤與數字的米白文字色。")]
    public Color guardTextColor = new Color(.945f,.929f,.851f,1);
    [Tooltip("破防文字與低於四分之一耐力的填色。")]
    public Color guardWarningColor = new Color(.929f,.506f,.459f,1);
    [Tooltip("實際正在恢復耐力時的填色，不會提前填破防冷卻。")]
    public Color guardRecoveryColor = new Color(.718f,.804f,.651f,1);
    [Header("共同間距與底色")]
    [Min(0)] public float healthGap = 5;
    [Min(0)] public float iconBarGap = 5;
    public Color emptyColor = new Color(.12f, .16f, .20f, .9f);
    private static StatusEffectHudSettings cached;
    private static bool loaded;
    public static StatusEffectHudSettings Default
    {
        get { if (!loaded) { cached = Resources.Load<StatusEffectHudSettings>("Presentation/StatusEffectHud"); loaded = true; } return cached; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() { cached = null; loaded = false; }
    public bool TryGetStyle(StatusEffectKind kind, out Style style)
    {
        foreach (var candidate in styles) if (candidate.kind == kind) { style = candidate; return style.icon != null; }
        style = default; return false;
    }
    public float Height(int count, bool self) => count <= 0 ? 0 : Mathf.Max(0, healthGap) +
        count * Mathf.Max(8, self ? selfIconSize : worldIconSize) + (count - 1) * Mathf.Max(0, self ? selfRowGap : worldRowGap);
}
