using UnityEngine;
using UnityEngine.UI;

/// <summary>固定容量的百分比分格圖形；不沿用玩家的固定 HP 整格餘額，也不持有生命狀態。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EnemyHealthBarGraphic : MaskableGraphic
{
    private int cells = 10;
    private float ratio, gap = 2;
    private Color filled = Color.white, empty = Color.black;
    public float DisplayedRatio => ratio;

    protected override void Awake() { base.Awake(); raycastTarget = false; }

    public void SetSnapshot(float value, EnemyHealthBarSettings settings)
    {
        int nextCells = Mathf.Clamp(settings.cellCount, 1, 40);
        float nextRatio = EnemyHealthBarRules.Ratio(value, 1);
        if (cells == nextCells && ratio == nextRatio && gap == settings.gap &&
            filled == settings.healthColor && empty == settings.emptyColor) return;
        cells = nextCells; ratio = nextRatio; gap = settings.gap;
        filled = settings.healthColor; empty = settings.emptyColor;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        float spacing = Mathf.Clamp(gap, 0, rect.width / cells * .4f);
        float width = (rect.width - spacing * (cells - 1)) / cells;
        if (width <= 0 || rect.height <= 0) return;
        for (int i = 0; i < cells; i++)
        {
            float x = rect.xMin + i * (width + spacing);
            AddRect(vh, x, rect.yMin, width, rect.height, empty);
            float fill = EnemyHealthBarRules.CellFill(ratio, cells, i);
            if (fill > 0) AddRect(vh, x, rect.yMin, width * fill, rect.height, filled);
        }
    }

    private static void AddRect(VertexHelper vh, float x, float y, float width, float height, Color tint)
    {
        int index = vh.currentVertCount;
        vh.AddVert(new Vector3(x, y), tint, Vector2.zero);
        vh.AddVert(new Vector3(x, y + height), tint, Vector2.up);
        vh.AddVert(new Vector3(x + width, y + height), tint, Vector2.one);
        vh.AddVert(new Vector3(x + width, y), tint, Vector2.right);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }
}

