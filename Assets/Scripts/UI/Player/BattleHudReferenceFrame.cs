using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Fits the authored HUD into one uniformly scaled reference frame.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class BattleHudReferenceFrame : UIBehaviour
{
    private bool refreshing;
    public static readonly Vector2 ReferenceSize = new Vector2(1920f, 1080f);
    protected override void OnEnable() { base.OnEnable(); Refresh(); }
    protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); Refresh(); }
    private void LateUpdate() => Refresh();

    public void Refresh()
    {
        if (refreshing) return;
        var rect = (RectTransform)transform;
        var parent = rect.parent as RectTransform;
        if (parent == null) return;
        float scale = Mathf.Min(parent.rect.width / ReferenceSize.x, parent.rect.height / ReferenceSize.y);
        if (scale <= 0f) return;
        Vector2 centre = Vector2.one * 0.5f;
        if (rect.anchorMin == centre && rect.anchorMax == centre && rect.pivot == centre &&
            rect.anchoredPosition == Vector2.zero && rect.sizeDelta == ReferenceSize &&
            rect.localScale == Vector3.one * scale) return;
        refreshing = true;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = ReferenceSize;
        rect.localScale = Vector3.one * scale;
        refreshing = false;
    }
}
