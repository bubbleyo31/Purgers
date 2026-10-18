using UnityEngine;
using UnityEngine.UI;

/// <summary>核准格檔條的斜端純色填量；低耐力仍依真實比例裁切，沒有假冷卻填量。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class GuardBarGraphic : MaskableGraphic
{
    private float ratio = 1f;
    public void SetRatio(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(ratio, value)) return;
        ratio = value; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float width = r.width * ratio;
        if (width <= 0 || r.height <= 0) return;
        float slant = Mathf.Min(3f, r.height * .5f);
        // 先對完整斜端多邊形做垂直裁切，避免極少耐力被斜角吞掉。
        float topLeft = Mathf.Min(width, slant);
        float bottomRight = Mathf.Min(width, r.width - slant);
        float clipTopY = width < slant ? r.yMin + r.height * width / slant : r.yMax;
        float clipBottomY = width > r.width - slant ? r.yMin + r.height * (width - (r.width - slant)) / slant : r.yMin;
        vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin + topLeft, clipTopY), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin + width, clipTopY), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin + width, clipBottomY), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin + bottomRight, r.yMin), color, Vector2.zero);
        vh.AddTriangle(0,1,2); vh.AddTriangle(0,2,3); vh.AddTriangle(0,3,4);
    }
}
