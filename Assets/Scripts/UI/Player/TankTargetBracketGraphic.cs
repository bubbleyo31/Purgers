using UnityEngine;
using UnityEngine.UI;

/// <summary>無文字的橘色四角框。僅一個 Graphic，不攔截游標或更動原準心。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TankTargetBracketGraphic : MaskableGraphic
{
    float lineWidth = 5f, cornerLength = 24f;
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    public void SetStyle(Color tint, float width, float length)
    {
        if (color == tint && lineWidth == width && cornerLength == length) return;
        color = tint; lineWidth = Mathf.Max(1f, width); cornerLength = Mathf.Max(2f, length); SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); Rect r = rectTransform.rect;
        float half = Mathf.Min(lineWidth, Mathf.Min(r.width, r.height) * .25f) * .5f;
        float arm = Mathf.Min(cornerLength, Mathf.Min(r.width, r.height) * .25f);
        if (half <= 0f || arm <= half) return;
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? 1f : -1f, sy = (i & 2) == 0 ? 1f : -1f;
            Vector2 corner = new Vector2(sx > 0 ? r.xMin + half : r.xMax - half, sy > 0 ? r.yMin + half : r.yMax - half);
            Segment(mesh, corner, corner + Vector2.right * sx * arm, half);
            Segment(mesh, corner, corner + Vector2.up * sy * arm, half);
        }
    }
    void Segment(VertexHelper mesh, Vector2 a, Vector2 b, float radius)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * radius;
        int i = mesh.currentVertCount;
        mesh.AddVert(a - n, color, Vector2.zero); mesh.AddVert(a + n, color, Vector2.zero);
        mesh.AddVert(b + n, color, Vector2.zero); mesh.AddVert(b - n, color, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        Cap(mesh, a, radius); Cap(mesh, b, radius);
    }
    void Cap(VertexHelper mesh, Vector2 center, float radius)
    {
        const int steps = 12; int first = mesh.currentVertCount; mesh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i <= steps; i++)
        {
            float angle = i * Mathf.PI * 2f / steps;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
            if (i > 0) mesh.AddTriangle(first, first + i, first + i + 1);
        }
    }
}
