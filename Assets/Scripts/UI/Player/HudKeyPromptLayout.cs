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
    KeyCode lastKey;
    bool hasKey;

    public HudKeyPromptLayout(TMP_Text label, Image background, RectTransform above)
    {
        this.label = label;
        this.background = background;
        this.above = above;
        keyRect = background != null ? background.rectTransform : null;
        Prepare();
    }

    void Prepare()
    {
        if (label == null) return;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.richText = false;
        label.margin = Vector4.zero;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMax = MaximumFontSize;
        label.fontSizeMin = MinimumFontSize;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.pivot = Vector2.one * .5f;
        label.rectTransform.offsetMin = new Vector2(HorizontalPadding, 2f);
        label.rectTransform.offsetMax = new Vector2(-HorizontalPadding, -2f);
        if (background != null) background.raycastTarget = false;
        if (keyRect == null) return;
        keyRect.anchorMin = new Vector2(.5f, 1f);
        keyRect.anchorMax = keyRect.anchorMin;
        keyRect.pivot = new Vector2(.5f, 1f);
        keyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Height);
        keyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, MinimumWidth);
        PlaceBelow();
    }

    public void Refresh(KeyCode key)
    {
        if (label == null || (hasKey && lastKey == key)) return;
        hasKey = true;
        lastKey = key;
        label.fontSize = MaximumFontSize;
        PlayerSettingsService.SetPrompt(label, key);
        string text = PlayerSettingsData.KeyLabel(key);
        float preferred = label.GetPreferredValues(text).x;
        float width = Mathf.Clamp(preferred + HorizontalPadding * 2f,
            MinimumWidth, MaximumWidth);
        keyRect?.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        PlaceBelow();
    }

    void PlaceBelow()
    {
        if (keyRect == null || above == null || keyRect.parent == null) return;
        RectTransform parent = keyRect.parent as RectTransform;
        if (parent == null) return;
        Vector3 bottom = parent.InverseTransformPoint(above.TransformPoint(
            new Vector3(above.rect.center.x, above.rect.yMin, 0f)));
        Vector2 anchorReference = parent.rect.min +
            Vector2.Scale(parent.rect.size, keyRect.anchorMin);
        Vector2 position = new Vector2(bottom.x, bottom.y) - anchorReference +
            Vector2.down * VerticalGap;
        if ((keyRect.anchoredPosition - position).sqrMagnitude > .0001f)
            keyRect.anchoredPosition = position;
    }
}
