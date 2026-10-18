using TMPro;
using UnityEngine;

/// <summary>由既有生命／觀戰 HUD 驅動的格檔呈現，與異常列共用中心座標，不建立玩家選擇器。</summary>
[DisallowMultipleComponent]
public sealed class LocalPlayerGuardHUD : MonoBehaviour
{
    private RectTransform root;
    private TMP_Text label, value;
    private GuardBarGraphic empty, fill;
    public static LocalPlayerGuardHUD Create(RectTransform anchor)
    {
        var obj = new GameObject("Guard stamina HUD", typeof(RectTransform), typeof(LocalPlayerGuardHUD));
        obj.transform.SetParent(anchor, false);
        var view = obj.GetComponent<LocalPlayerGuardHUD>();
        view.root = (RectTransform)obj.transform;
        view.root.anchorMin = view.root.anchorMax = new Vector2(.5f,.5f);
        view.root.pivot = new Vector2(.5f,1);
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        foreach (var candidate in anchor.GetComponentsInChildren<TMP_Text>(true))
            if (candidate.font != null && candidate.font.name.Contains("NotoSansTC")) { font = candidate.font; break; }
        view.label = view.Text("Guard state", font, TextAlignmentOptions.Left);
        view.value = view.Text("Stamina or broken seconds", font, TextAlignmentOptions.Right);
        view.empty = view.Bar("Capacity");
        view.fill = view.Bar("Remaining");
        view.Clear(); return view;
    }
    private TMP_Text Text(string name, TMP_FontAsset font, TextAlignmentOptions alignment)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        text.transform.SetParent(root, false);
        text.font = font; text.fontSize = 12; text.fontStyle = FontStyles.Bold;
        text.alignment = alignment; text.raycastTarget = false;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0,1);
        return text;
    }
    private GuardBarGraphic Bar(string name)
    {
        var bar = new GameObject(name, typeof(RectTransform), typeof(GuardBarGraphic)).GetComponent<GuardBarGraphic>();
        bar.transform.SetParent(root, false); bar.raycastTarget = false;
        bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = bar.rectTransform.pivot = new Vector2(0,1);
        return bar;
    }
    public void Clear() { gameObject.SetActive(false); }
    /// <summary>只在實際技能佔位衝突時抬高整組；以共同 anchor 局部座標計算。</summary>
    public static float ResolveCrosshairOffset(StatusEffectHudSettings settings, float leadingHeight, int statusCount, float castTop)
    {
        float stack = leadingHeight + (statusCount > 0
            ? statusCount * Mathf.Max(8,settings.selfIconSize) + (statusCount-1)*Mathf.Max(0,settings.selfRowGap) : 0);
        float usual = Mathf.Max(0,settings.selfCrosshairOffset);
        if (float.IsNegativeInfinity(castTop)) return usual;
        return Mathf.Min(usual, -castTop - 8 - stack);
    }
    public void SetCrosshairOffset(float offset) => root.anchoredPosition = new Vector2(0,-offset);
    /// <summary>回傳此列佔用高度，既有異常列向下排；隱藏時回零，不留下空位。</summary>
    public float Present(in GuardHudSnapshot snapshot, StatusEffectHudSettings settings, float width)
    {
        if (!snapshot.Visible || settings == null) { Clear(); return 0; }
        gameObject.SetActive(true);
        width = Mathf.Min(width * Mathf.Clamp(settings.guardWidthMultiplier,1,1.5f), ((RectTransform)root.parent).rect.width);
        label.fontSize = value.fontSize = Mathf.Clamp(settings.guardFontSize,8,24);
        float start = Mathf.Max(8,settings.selfIconSize) + Mathf.Max(0,settings.iconBarGap);
        float barWidth = Mathf.Max(4,width-start);
        float height = Mathf.Max(2,settings.guardBarHeight);
        float labelHeight = Mathf.Max(20,Mathf.Ceil(label.fontSize * 1.5f));
        const float labelGap = 6;
        root.anchoredPosition = new Vector2(0,-Mathf.Max(0,settings.selfCrosshairOffset));
        root.sizeDelta = new Vector2(width,labelHeight+labelGap+height);
        label.rectTransform.anchoredPosition = new Vector2(start,0);
        label.rectTransform.sizeDelta = new Vector2(barWidth*.62f,labelHeight);
        value.rectTransform.anchoredPosition = new Vector2(start+barWidth*.62f,0);
        value.rectTransform.sizeDelta = new Vector2(barWidth*.38f,labelHeight);
        label.text = snapshot.Broken ? "破防" : snapshot.Guarding ? "格檔" :
            snapshot.WaitingSeconds > 0 ? "等待恢復 " + snapshot.WaitingSeconds.ToString("0.0") + "s" : "恢復中";
        value.text = snapshot.Broken ? snapshot.BrokenSeconds.ToString("0.0")+"s" :
            (snapshot.Current > 0 && snapshot.Current < 1 ? snapshot.Current.ToString("0.0") :
            Mathf.FloorToInt(snapshot.Current).ToString()) + " / " + snapshot.Maximum.ToString("0");
        label.color = value.color = snapshot.Broken ? settings.guardWarningColor : settings.guardTextColor;
        empty.color = settings.emptyColor;
        fill.color = snapshot.Recovering ? settings.guardRecoveryColor :
            snapshot.Guarding && snapshot.Ratio <= .25f ? settings.guardWarningColor : settings.guardFillColor;
        empty.rectTransform.anchoredPosition = fill.rectTransform.anchoredPosition = new Vector2(start,-labelHeight-labelGap);
        empty.rectTransform.sizeDelta = fill.rectTransform.sizeDelta = new Vector2(barWidth,height);
        fill.SetRatio(snapshot.Ratio);
        return root.sizeDelta.y + Mathf.Max(0,settings.guardStatusGap);
    }
}
