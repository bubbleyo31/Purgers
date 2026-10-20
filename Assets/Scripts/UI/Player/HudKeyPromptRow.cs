using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>將既有提示文字框呈現成「鍵帽＋說明」。不改外層位置，不讀取輸入。</summary>
public sealed class HudKeyPromptRow
{
    readonly TMP_Text host, description;
    readonly RectTransform row, key;
    readonly HudKeyPromptLayout layout;
    TMP_Text prefix;
    string measuredPrefix;
    TMP_FontAsset measuredFont;
    float measuredSize, measuredHeight;
    Vector2 glyphBounds;
    bool hasGlyphBounds;

    public HudKeyPromptRow(TMP_Text host, bool centered = false)
    {
        this.host = host;
        var existing = host.transform.Find("SharedPromptRow");
        row = existing != null ? (RectTransform)existing :
            new GameObject("SharedPromptRow", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(host.transform, false);
        row.anchorMin = row.anchorMax = row.pivot = new Vector2(centered ? .5f : 0f, .5f);
        var keyHost = ChildText(row, "Key");
        key = keyHost.rectTransform;
        key.anchorMin = key.anchorMax = key.pivot = new Vector2(0, .5f);
        layout = HudKeyPromptLayout.Inside(keyHost);
        description = ChildText(row, row.Find("Description2") != null ? "Description2" : "Description");
        description.rectTransform.anchorMin = description.rectTransform.anchorMax = description.rectTransform.pivot = new Vector2(0, .5f);
        description.color = host.color;
        description.alignment = TextAlignmentOptions.MidlineLeft;
        description.enableWordWrapping = false;
        description.isOrthographic = true;
        description.raycastTarget = false;
    }

    TMP_Text ChildText(Transform parent, string name)
    {
        var found = parent.Find(name);
        if (found != null) return found.GetComponent<TMP_Text>();
        var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        text.transform.SetParent(parent, false);
        text.font = host.font;
        text.fontSharedMaterial = host.fontSharedMaterial;
        return text;
    }

    public void Refresh(KeyCode binding, string action, bool pressed = false)
    {
        host.text = string.Empty;
        row.gameObject.SetActive(true);
        layout.Refresh(binding);
        layout.SetState(pressed, binding == KeyCode.None);
        var style = HudKeyPromptStyle.Shared;
        float height = style != null ? style.height : HudKeyPromptLayout.Height;
        float gap = style != null ? style.inlineGap : 8;
        if (style != null && style.font != null) { description.font = style.font; description.fontSharedMaterial = style.fontMaterial; }
        description.fontSize = style != null ? style.fontSize : 21;
        description.text = action;
        float textWidth = string.IsNullOrEmpty(action) ? 0 : description.GetPreferredValues(action).x;
        if (textWidth == 0) gap = 0;
        key.sizeDelta = new Vector2(layout.Width, height);
        key.localScale = Vector3.one;
        description.rectTransform.anchoredPosition = new Vector2(layout.Width + gap, 0);
        description.rectTransform.sizeDelta = new Vector2(textWidth + 1, height);
        float width = layout.Width + gap + textWidth;
        row.sizeDelta = new Vector2(width, height);
        row.anchoredPosition = Vector2.zero;
        // Keep short prompts in their original row; long remaps shrink the complete row uniformly.
        var available = host.rectTransform.rect.size;
        row.localScale = Vector3.one * Mathf.Min(1, Mathf.Max(1, available.x) / Mathf.Max(1, width), Mathf.Max(1, available.y) / height);
    }

    /// <summary>Optional prefix / keycap / suffix layout; compact keys follow visible glyph height, excluding SDF padding.</summary>
    public void RefreshWithPrefix(KeyCode binding, string before, string after, bool pressed = false,
        float descriptionSize = 0f, bool matchKeycapToTextHeight = false)
    {
        Refresh(binding, after, pressed);
        if (prefix == null)
        {
            prefix = ChildText(row, "Description1");
            prefix.rectTransform.anchorMin = prefix.rectTransform.anchorMax = prefix.rectTransform.pivot = new Vector2(0, .5f);
            prefix.alignment = TextAlignmentOptions.MidlineLeft;
            prefix.enableWordWrapping = false;
            prefix.isOrthographic = true;
            prefix.raycastTarget = false;
        }
        description.name = "Description2";
        prefix.font = description.font;
        prefix.fontSharedMaterial = description.fontSharedMaterial;
        prefix.enableAutoSizing = description.enableAutoSizing = false;
        float fontSize = descriptionSize > 0 ? descriptionSize : description.fontSize;
        prefix.fontSize = description.fontSize = fontSize;
        prefix.color = description.color = host.color;
        prefix.text = before;
        float height = matchKeycapToTextHeight ? fontSize : key.sizeDelta.y;
        float beforeWidth = string.IsNullOrEmpty(before) ? 0 : prefix.GetPreferredValues(before).x;
        float afterWidth = string.IsNullOrEmpty(after) ? 0 : description.GetPreferredValues(after).x;
        prefix.rectTransform.sizeDelta = new Vector2(beforeWidth + 1, height);
        prefix.rectTransform.anchoredPosition = Vector2.zero;
        float scale = 1f, keyY = 0f;
        if (matchKeycapToTextHeight)
        {
            if (!hasGlyphBounds || measuredPrefix != before || measuredFont != prefix.font ||
                measuredSize != fontSize || measuredHeight != height)
            {
                prefix.ForceMeshUpdate(true, true);
                float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
                for (int i = 0; i < prefix.textInfo.characterCount; i++)
                {
                    var character = prefix.textInfo.characterInfo[i];
                    if (!character.isVisible || character.textElement == null) continue;
                    var metrics = character.textElement.glyph.metrics;
                    top = Mathf.Max(top, character.baseLine + metrics.horizontalBearingY * character.scale);
                    bottom = Mathf.Min(bottom, character.baseLine + (metrics.horizontalBearingY - metrics.height) * character.scale);
                }
                hasGlyphBounds = top > bottom;
                glyphBounds = hasGlyphBounds ? new Vector2(bottom, top) : new Vector2(-fontSize * .5f, fontSize * .5f);
                measuredPrefix = before; measuredFont = prefix.font; measuredSize = fontSize; measuredHeight = height;
            }
            scale = (glyphBounds.y - glyphBounds.x) / Mathf.Max(1f, key.sizeDelta.y);
            keyY = (glyphBounds.x + glyphBounds.y) * .5f;
        }
        key.localScale = Vector3.one * scale;
        float keyWidth = layout.Width * scale;
        var style = HudKeyPromptStyle.Shared;
        // Compact copy uses the same six-unit gap as the approved 24-high reward row.
        float gap = (style != null ? style.inlineGap : 8f) * (matchKeycapToTextHeight ? .75f : 1f);
        float beforeGap = beforeWidth > 0 ? gap : 0;
        float afterGap = afterWidth > 0 ? gap : 0;
        key.anchoredPosition = new Vector2(beforeWidth + beforeGap, keyY);
        description.rectTransform.anchoredPosition = new Vector2(beforeWidth + beforeGap + keyWidth + afterGap, 0);
        description.rectTransform.sizeDelta = new Vector2(afterWidth + 1, height);
        float width = beforeWidth + beforeGap + keyWidth + afterGap + afterWidth;
        row.sizeDelta = new Vector2(width, height);
        var available = host.rectTransform.rect.size;
        row.localScale = Vector3.one * Mathf.Min(1, Mathf.Max(1, available.x) / Mathf.Max(1, width), Mathf.Max(1, available.y) / Mathf.Max(1, height));
    }

    public void ShowMessage(string message)
    {
        row.gameObject.SetActive(false);
        host.text = message;
    }
}
