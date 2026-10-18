using UnityEngine;
using UnityEngine.UI;

/// <summary>完整方格的徑向遮罩與期限外框；十二點起順時針退去，不需要產生 Sprite／材質。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AbilitySlotStateGraphic : MaskableGraphic
{
    private float shade, border, pulse, thickness = 3f;
    private Color shadeTint, borderTint, flashTint;

    public void SetState(float shadeRatio, float borderRatio, float flash, float width,
        Color shadeColor, Color borderColor, Color flashColor)
    {
        shadeRatio = Mathf.Clamp01(shadeRatio); borderRatio = Mathf.Clamp01(borderRatio);
        if (shade == shadeRatio && border == borderRatio && pulse == flash && thickness == width &&
            shadeTint == shadeColor && borderTint == borderColor && flashTint == flashColor) return;
        shade = shadeRatio; border = borderRatio; pulse = Mathf.Clamp01(flash); thickness = width;
        shadeTint = shadeColor; borderTint = borderColor; flashTint = flashColor;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;
        if (shade > 0f) Sector(vh, r, shade, 0f, shadeTint);
        if (border > 0f) Sector(vh, r, border, Mathf.Clamp(thickness, .5f, Mathf.Min(r.width, r.height) * .49f), borderTint);
        if (pulse > 0f)
        {
            Color wash = flashTint; wash.a *= pulse * .3f;
            Sector(vh, r, 1f, 0f, wash);
            Color edge = flashTint; edge.a *= pulse;
            Sector(vh, r, 1f, Mathf.Clamp(thickness, .5f, Mathf.Min(r.width, r.height) * .49f), edge);
        }
    }

    private static Vector2 Edge(Rect rect, float turn)
    {
        float radians = turn * Mathf.PI * 2f;
        Vector2 direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        float x = Mathf.Abs(direction.x), y = Mathf.Abs(direction.y);
        float distance = Mathf.Min(x > .000001f ? rect.width * .5f / x : float.PositiveInfinity,
            y > .000001f ? rect.height * .5f / y : float.PositiveInfinity);
        return rect.center + direction * distance;
    }

    private static void Sector(VertexHelper vh, Rect rect, float remaining, float inset, Color tint)
    {
        float start = 1f - remaining;
        float corner = Mathf.Atan2(rect.width, rect.height) / (2f * Mathf.PI);
        // Include the four exact corner rays, so narrow rectangular slots retain their entire silhouette.
        float end = corner;
        for (int segment = 0; segment < 5; segment++)
        {
            if (end > start)
            {
                Vector2 a = Edge(rect, start), b = Edge(rect, end);
                int index = vh.currentVertCount;
                if (inset <= 0f)
                {
                    vh.AddVert(rect.center, tint, Vector2.zero);
                    vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
                    vh.AddTriangle(index, index + 1, index + 2);
                }
                else
                {
                    Rect inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
                    vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
                    vh.AddVert(Edge(inner, end), tint, Vector2.zero); vh.AddVert(Edge(inner, start), tint, Vector2.zero);
                    vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
                }
                start = end;
            }
            end = segment == 0 ? .5f - corner : segment == 1 ? .5f + corner : segment == 2 ? 1f - corner : 1f;
        }
    }
}
