using UnityEngine;

/// <summary>敵人血條純顯示規則；不修改任何生命或戰鬥結果。</summary>
public static class EnemyHealthBarRules
{
    public static float Ratio(float current, float maximum)
    {
        if (float.IsNaN(current) || float.IsInfinity(current) || float.IsNaN(maximum) ||
            float.IsInfinity(maximum) || maximum <= 0) return 0;
        return Mathf.Clamp01(current / maximum);
    }

    public static float CellFill(float ratio, int count, int index)
    {
        if (float.IsNaN(ratio) || count < 1 || index < 0 || index >= count) return 0;
        return Mathf.Clamp01(Mathf.Clamp01(ratio) * count - index);
    }

    /// <summary>只對仍在畫面內的大型身體提供備用位置，不建立畫面外提示。</summary>
    public static bool TryPosition(Rect viewport, Vector3 head, Rect body, bool large, bool wasFallback,
        Vector2 size, float inset, float hysteresis, float padding, out bool fallback, out Vector2 point)
    {
        fallback = false;
        point = Vector2.zero;
        Rect visible = Rect.MinMaxRect(Mathf.Max(viewport.xMin, body.xMin), Mathf.Max(viewport.yMin, body.yMin),
            Mathf.Min(viewport.xMax, body.xMax), Mathf.Min(viewport.yMax, body.yMax));
        if (visible.width <= 0 || visible.height <= 0) return false;
        Rect safe = SafeArea(viewport, size, inset);
        if (safe.width <= 0 || safe.height <= 0) return false;
        Vector2 normal = new Vector2(head.x, head.y + padding + size.y * .5f);
        Rect returnArea = SafeArea(safe, Vector2.zero, wasFallback ? hysteresis : 0);
        if (head.z > 0 && returnArea.Contains(normal))
        {
            point = normal;
            return true;
        }
        if (!large) return false;
        fallback = true;
        point = Clamp(safe, new Vector2(visible.center.x, visible.yMax + padding + size.y * .5f));
        return true;
    }

    public static Rect SafeArea(Rect viewport, Vector2 size, float inset) =>
        Rect.MinMaxRect(viewport.xMin + inset + size.x * .5f, viewport.yMin + inset + size.y * .5f,
            viewport.xMax - inset - size.x * .5f, viewport.yMax - inset - size.y * .5f);

    public static Vector2 Clamp(Rect area, Vector2 point) =>
        new Vector2(Mathf.Clamp(point.x, area.xMin, area.xMax), Mathf.Clamp(point.y, area.yMin, area.yMax));
}

