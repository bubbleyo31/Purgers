using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>頭頂血條上方／準心下方純圖示與量條直列；依來源固定順位，不產生文字或獨立計時。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class StatusEffectListView : MonoBehaviour
{
    private sealed class Row { public RectTransform Root; public Image Icon, Empty, Fill; }
    private readonly List<Row> rows = new List<Row>();
    public int VisibleCount { get; private set; }
    public static StatusEffectListView Create(RectTransform health)
    {
        var obj = new GameObject("Timed status icons and bars", typeof(RectTransform), typeof(StatusEffectListView));
        var rect = (RectTransform)obj.transform; rect.SetParent(health, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, 1);
        return obj.GetComponent<StatusEffectListView>();
    }
    private static Image Image(string name, Transform parent)
    {
        var result = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        result.transform.SetParent(parent, false); result.raycastTarget = false;
        result.rectTransform.anchorMin = result.rectTransform.anchorMax = new Vector2(0, .5f);
        result.rectTransform.pivot = new Vector2(0, .5f); return result;
    }
    private Row Acquire(int index)
    {
        if (index < rows.Count) return rows[index];
        var rect = new GameObject("Status", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(transform, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        var row = new Row { Root = rect };
        row.Icon = Image("Icon", rect); row.Icon.preserveAspect = true;
        row.Empty = Image("Duration capacity", rect); row.Fill = Image("Remaining duration", rect);
        rows.Add(row); return row;
    }
    public void Clear()
    {
        VisibleCount = 0;
        foreach (var row in rows) row.Root.gameObject.SetActive(false);
    }
    public void SetSelfCrosshairOffset(float offset, float leadingHeight) =>
        ((RectTransform)transform).anchoredPosition = new Vector2(((RectTransform)transform).anchoredPosition.x,-offset-leadingHeight);
    public void Present(StatusEffectSnapshot[] snapshots, int count, StatusEffectHudSettings settings, float width, bool self, float selfLeadingHeight = 0)
    {
        if (settings == null || snapshots == null) { Clear(); return; }
        count = Mathf.Clamp(count, 0, snapshots.Length);
        float icon = Mathf.Max(8, self ? settings.selfIconSize : settings.worldIconSize);
        float gap = Mathf.Max(0, self ? settings.selfRowGap : settings.worldRowGap);
        float barHeight = Mathf.Clamp(self ? settings.selfBarHeight : settings.worldBarHeight, 2, icon);
        float start = icon + Mathf.Max(0, settings.iconBarGap);
        width = Mathf.Max(start + 4, width);
        var rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, self ? .5f : 1);
        rect.pivot = new Vector2(.5f, self ? 1 : 0);
        rect.anchoredPosition = new Vector2(self ? settings.selfHorizontalOffset : 0, self ? -Mathf.Max(0, settings.selfCrosshairOffset) - Mathf.Max(0,selfLeadingHeight) : Mathf.Max(0, settings.healthGap));
        int shown = 0;
        for (int i = 0; i < count; i++)
        {
            var snapshot = snapshots[i];
            if (!snapshot.IsVisible || !settings.TryGetStyle(snapshot.Kind, out var style)) continue;
            var row = Acquire(shown);
            row.Root.gameObject.SetActive(true); row.Root.anchoredPosition = new Vector2(0, -shown * (icon + gap));
            row.Root.sizeDelta = new Vector2(width, icon);
            row.Icon.sprite = style.icon; row.Icon.color = Color.white;
            row.Icon.rectTransform.sizeDelta = Vector2.one * icon;
            row.Empty.color = settings.emptyColor; row.Fill.color = style.barColor;
            row.Empty.rectTransform.anchoredPosition = row.Fill.rectTransform.anchoredPosition = new Vector2(start, 0);
            row.Empty.rectTransform.sizeDelta = new Vector2(width - start, barHeight);
            row.Fill.rectTransform.sizeDelta = new Vector2((width - start) * snapshot.Ratio, barHeight);
            shown++;
        }
        rect.sizeDelta = new Vector2(width, Mathf.Max(0, shown * (icon + gap) - gap));
        VisibleCount = shown;
        for (int i = shown; i < rows.Count; i++) rows[i].Root.gameObject.SetActive(false);
    }
}
