using Purgers.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local keycap layout. Reads binding labels but owns no input or persistence behavior.</summary>
public sealed class HudKeyPromptLayout
{
    public const float Height = 32f;
    public const float MinimumWidth = 32f;
    public const float MaximumWidth = 104f;
    public const float HorizontalPadding = 8f;
    public const float VerticalGap = 19f;
    const float MaximumFontSize = 21f;
    const float MinimumFontSize = 10f;

    readonly TMP_Text label;
    readonly Image background;
    readonly RectTransform keyRect;
    readonly RectTransform above;
    readonly HudKeyPromptStyle style;
    int styleRevision = -1;
    KeyCode lastKey;
    bool hasKey;

    public HudKeyPromptLayout(TMP_Text label, Image background, RectTransform above, HudKeyPromptStyle style = null)
    {
        this.label = label;
        this.background = background;
        this.above = above;
        this.style = style != null ? style : HudKeyPromptStyle.Shared;
        keyRect = background != null ? background.rectTransform : null;
        Prepare();
    }

    void Prepare()
    {
        if (label == null) return;
        if (background != null && label.transform.parent != background.transform)
            label.transform.SetParent(background.transform, false);
        if (style != null) { style.Apply(label, background); styleRevision = style.Revision; }
        label.alignment = TextAlignmentOptions.MidlineGeoAligned;
        label.isOrthographic = true; // TMP uGUI can still have the 3D scale before an inactive label first awakens.
        label.enableWordWrapping = false;
        label.richText = false;
        label.margin = Vector4.zero;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMax = style != null ? style.fontSize : MaximumFontSize;
        label.fontSizeMin = style != null ? style.minimumFontSize : MinimumFontSize;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.pivot = Vector2.one * .5f;
        var padding = style != null ? style.padding : new Vector2(HorizontalPadding, 2f);
        var offset = style != null ? style.textOffset : Vector2.zero;
        label.rectTransform.offsetMin = padding + offset;
        label.rectTransform.offsetMax = -padding + offset;
        if (background != null) background.raycastTarget = false;
        if (keyRect == null) return;
        if (above != null)
        {
            keyRect.anchorMin = new Vector2(.5f, 1f);
            keyRect.anchorMax = keyRect.anchorMin;
            keyRect.pivot = new Vector2(.5f, 1f);
        }
        keyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, style != null ? style.height : Height);
        keyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, style != null ? style.minimumWidth : MinimumWidth);
        PlaceBelow();
    }

    public void Refresh(KeyCode key)
    {
        if (label == null) return;
        bool changedStyle = style != null && styleRevision != style.Revision;
        if (changedStyle) Prepare();
        if (hasKey && lastKey == key && !changedStyle) { PlaceBelow(); return; }
        hasKey = true;
        lastKey = key;
        label.fontSize = style != null ? style.fontSize : MaximumFontSize;
        PlayerSettingsService.SetPrompt(label, key);
        string text = PlayerSettingsData.KeyLabel(key);
        float preferred = label.GetPreferredValues(text).x;
        float width = Mathf.Clamp(preferred + (style != null ? style.padding.x : HorizontalPadding) * 2f,
            style != null ? style.minimumWidth : MinimumWidth, style != null ? style.maximumWidth : MaximumWidth);
        keyRect?.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        PlaceBelow();
    }

    public void SetState(bool pressed, bool unavailable = false)
    {
        if (background == null || style == null) return;
        background.color = unavailable ? style.unavailableColor : pressed ? style.pressedColor : style.readyColor;
    }

    public float Width => keyRect != null ? keyRect.rect.width : 0;

    /// <summary>Pure key text becomes a keycap inside its original placement rect; no reparenting of serialized references.</summary>
    public static HudKeyPromptLayout Inside(TMP_Text host, string direction = null)
    {
        if (host == null) return null;
        host.text = string.Empty;
        var existing = host.transform.Find("SharedKeycap");
        var image = existing != null ? existing.GetComponent<Image>() :
            new GameObject("SharedKeycap", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(host.transform, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = Vector2.one * .5f;
        image.rectTransform.anchoredPosition = Vector2.zero;
        var label = image.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(image.transform, false);
            label.font = host.font;
        }
        var result = new HudKeyPromptLayout(label, image, null);
        if (!string.IsNullOrEmpty(direction))
        {
            var existingArrow = host.transform.Find("Direction");
            var arrow = existingArrow != null ? existingArrow.GetComponent<TMP_Text>() :
                new GameObject("Direction", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            arrow.transform.SetParent(host.transform, false);
            var theme = HudKeyPromptStyle.Shared;
            arrow.font = theme != null ? theme.font : host.font;
            arrow.fontSize = theme != null ? theme.fontSize : 21;
            arrow.color = theme != null ? theme.readyColor : Color.white;
            arrow.text = direction; arrow.raycastTarget = false; arrow.alignment = TextAlignmentOptions.Midline;
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = arrow.rectTransform.pivot = Vector2.one * .5f;
            arrow.rectTransform.sizeDelta = new Vector2(18, 32);
            arrow.rectTransform.anchoredPosition = new Vector2(direction == "‹" ? -32 : 32, 0);
        }
        return result;
    }

    public static void PlaceDescriptionAfter(TMP_Text description, Image cap)
    {
        if (description == null || cap == null || description.transform.parent != cap.transform.parent) return;
        var parent = cap.transform.parent as RectTransform;
        if (parent == null) return;
        var style = HudKeyPromptStyle.Shared;
        float gap = style != null ? style.inlineGap : 8;
        Vector3 edge = cap.transform.localPosition + cap.transform.localRotation * Vector3.Scale(
            new Vector3(cap.rectTransform.rect.xMax, cap.rectTransform.rect.center.y), cap.transform.localScale);
        var rect = description.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
        rect.anchoredPosition = new Vector2(edge.x + gap - parent.rect.xMin, edge.y - parent.rect.center.y);
        rect.sizeDelta = new Vector2(Mathf.Max(1, parent.rect.xMax - edge.x - gap), cap.rectTransform.rect.height);
        if (!description.enableAutoSizing) { description.fontSizeMax = description.fontSize; description.fontSizeMin = 12; description.enableAutoSizing = true; }
        description.alignment = TextAlignmentOptions.MidlineLeft;
    }

    void PlaceBelow()
    {
        if (keyRect == null || above == null || keyRect.parent == null) return;
        RectTransform parent = keyRect.parent as RectTransform;
        if (parent == null) return;
        Vector3 point = new Vector3(above.rect.center.x, above.rect.yMin, 0f);
        // Prefab Canvas roots can have zero scale before instantiation. Use local geometry for our parent/sibling anchors.
        Vector3 bottom = above == parent ? point : above.parent == parent
            ? above.localPosition + above.localRotation * Vector3.Scale(point, above.localScale)
            : parent.InverseTransformPoint(above.TransformPoint(point));
        Vector2 anchorReference = parent.rect.min +
            Vector2.Scale(parent.rect.size, keyRect.anchorMin);
        Vector2 position = new Vector2(bottom.x, bottom.y) - anchorReference +
            Vector2.down * (style != null ? style.belowGap : VerticalGap);
        if ((keyRect.anchoredPosition - position).sqrMagnitude > .0001f)
            keyRect.anchoredPosition = position;
    }
}
