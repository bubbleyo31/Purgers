using UnityEngine;
using UnityEngine.UI;

/// <summary>僅繪製螢幕周邊曲面與柔邊；中央不產生幾何、不取樣或扭曲遊戲畫面。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("Purgers/UI/護目鏡邊緣圖形")]
public sealed class GoggleRimGraphic : MaskableGraphic
{
    [Header("護目鏡外觀（可於 Play Mode 調整）")]
    [SerializeField, Range(0f, 2f), Tooltip("邊框厚度倍率：0 隱藏，1 為已確認外觀，2 為兩倍。只增加周邊曲面，不縮放 HUD；建議 0.6～1.2。")]
    private float thickness = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("邊框不透明度：0 完全透明，1 完全不透明。中央始終透明，不會套用全畫面染色。預設 0.94。")]
    private float opacity = .94f;
    [SerializeField, Range(0f, 12f), Tooltip("內緣柔化寬度，單位為 1080 高度參考像素；預設 5。只柔化邊框，不模糊場景或 UI。")]
    private float edgeSoftness = 5f;
    [SerializeField, Tooltip("橄欖綠內殼色；只影響外側少量表面。Alpha 由不透明度統一控制。")]
    private Color shellColor = new Color(.15f, .16f, .075f, 1f);
    [SerializeField, Tooltip("靠近眼睛的深色橡膠內襯色；中央不會染色。")]
    private Color rubberColor = new Color(.025f, .027f, .022f, 1f);

    private static readonly float[] Rings = { 0f, .12f, .30f, .62f, .82f, .93f, 1f, 1.12f };
    public float Thickness { get => thickness; set { value = Clamp(value, 2f); if (thickness == value) return; thickness = value; SetVerticesDirty(); } }
    public float Opacity { get => opacity; set { value = Clamp(value, 1f); if (opacity == value) return; opacity = value; SetVerticesDirty(); } }
    private static float Clamp(float value, float max) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, max);

    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    public override bool Raycast(Vector2 screenPoint, Camera eventCamera) => false;
#if UNITY_EDITOR
    protected override void OnValidate()
    {
        thickness = Clamp(thickness, 2f); opacity = Clamp(opacity, 1f);
        edgeSoftness = Clamp(edgeSoftness, 12f); raycastTarget = false;
        base.OnValidate(); SetVerticesDirty();
    }
#endif
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        float amount = Clamp(thickness, 2f);
        if (amount <= 0f || Clamp(opacity, 1f) <= 0f) return;
        var rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float scale = Mathf.Min(rect.width / 1920f, rect.height / 1080f);
        AddEdge(helper, rect, 0, 64, scale, amount);
        AddEdge(helper, rect, 1, 64, scale, amount);
        AddEdge(helper, rect, 2, 32, scale, amount);
        AddEdge(helper, rect, 3, 32, scale, amount);
    }

    private void AddEdge(VertexHelper helper, Rect rect, int edge, int segments, float scale, float amount)
    {
        int start = helper.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float arc = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), edge < 2 ? 3.4f : 1f);
            // 深度以畫面高度等比換算，長寬比變化只改弧寬，不擠壓 HUD。
            float depth = edge == 0 ? 3f + 65f * arc : edge == 1 ? 2f + 24f * arc :
                1f + 25f * Mathf.Pow(Mathf.Abs(t * 2f - 1f), 12f);
            depth *= scale * amount;
            for (int r = 0; r < Rings.Length; r++)
            {
                float inward = r == Rings.Length - 1 ? depth + edgeSoftness * scale * amount : depth * Rings[r];
                Vector2 position = edge == 0 ? new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, t), rect.yMax - inward) :
                    edge == 1 ? new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, t), rect.yMin + inward) :
                    edge == 2 ? new Vector2(rect.xMin + inward, Mathf.Lerp(rect.yMin, rect.yMax, t)) :
                    new Vector2(rect.xMax - inward, Mathf.Lerp(rect.yMin, rect.yMax, t));
                var vertex = UIVertex.simpleVert;
                vertex.position = position;
                vertex.color = BandColor(r, t, edge);
                helper.AddVert(vertex);
            }
            if (i == 0) continue;
            int a = start + (i - 1) * Rings.Length;
            int b = start + i * Rings.Length;
            for (int r = 0; r < Rings.Length - 1; r++)
            {
                helper.AddTriangle(a + r, b + r, b + r + 1);
                helper.AddTriangle(a + r, b + r + 1, a + r + 1);
            }
        }
    }

    private Color BandColor(int ring, float t, int edge)
    {
        Color shade;
        switch (ring)
        {
            case 0: shade = Color.Lerp(shellColor, rubberColor, .45f); break;
            case 1: shade = Color.Lerp(shellColor, rubberColor, .10f); break;
            case 2: shade = Color.Lerp(shellColor, rubberColor, .65f); break;
            case 3: shade = rubberColor * .65f; break;
            case 4: shade = rubberColor; break;
            case 5: shade = Color.Lerp(rubberColor, shellColor, .22f); break;
            default: shade = Color.Lerp(rubberColor, shellColor, .12f); break;
        }
        // 低對比表面起伏，不使用額外貼圖、材質或全螢幕 shader。
        float light = .9f + .1f * Mathf.Sin(t * Mathf.PI) + .025f * Mathf.Sin(t * 21f + edge);
        shade.r *= light * color.r; shade.g *= light * color.g; shade.b *= light * color.b;
        float alpha = ring == 7 ? 0f : ring == 6 ? .35f : 1f;
        if (edge >= 2) alpha *= .3f + .7f * Mathf.Pow(Mathf.Abs(t * 2f - 1f), 5f);
        shade.a = alpha * Clamp(opacity, 1f) * color.a;
        return shade;
    }
}


