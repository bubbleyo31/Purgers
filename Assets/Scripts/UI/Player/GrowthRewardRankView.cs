using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>成長圖示的本機階級文字；讀取獎勵卡決定的下一階，不寫入角色狀態。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Image))]
public sealed class GrowthRewardRankView : MonoBehaviour
{
    [SerializeField, Tooltip("圖示中央的階級文字；由同一份屬性底圖搭配 I～V。")]
    private TMP_Text rankLabel;
    private Image icon;
    private AbilityIconFrameFit frameFit;
    private PlayerRewardDefinition reward;
    private static readonly string[] Roman = { "", "I", "II", "III", "IV", "V" };
    private bool layoutValid;
    private Vector3 lastAnchor;
    private Vector2 lastSize;
    private Quaternion lastRotation;
    private TMP_FontAsset lastFont;
    private Material lastMaterial;
    private string lastText;

    public static string RankLabel(PlayerGrowthKind kind, int rank) =>
        PlayerGrowthRules.IsAttribute(kind)
            ? Roman[Mathf.Clamp(rank, 1, PlayerGrowthRules.MaximumRank(kind))] : string.Empty;

    public void ConfigureLabel(TMP_Text label) { rankLabel = label; layoutValid = false; }

    public void Present(PlayerRewardDefinition definition, int rank)
    {
        if (icon == null) icon = GetComponent<Image>();
        if (frameFit == null) frameFit = GetComponent<AbilityIconFrameFit>();
        if (reward != definition) layoutValid = false;
        reward = definition;
        bool attribute = definition != null && definition.IsImplementedGrowthReward &&
            PlayerGrowthRules.IsAttribute(definition.GrowthKind) && definition.GrowthRankBackground != null;
        if (frameFit != null)
        {
            frameFit.FitWholeSprite = definition != null && definition.IsImplementedGrowthReward;
            frameFit.WholeSpriteScaleMultiplier = attribute ? definition.GrowthIconScale : 1f;
        }
        bool show = rankLabel != null && attribute;
        if (rankLabel != null && rankLabel.gameObject.activeSelf != show)
        {
            rankLabel.gameObject.SetActive(show);
            layoutValid = false;
        }
        if (!show) return;
        icon.sprite = definition.GrowthRankBackground;
        icon.enabled = true;
        string text = RankLabel(definition.GrowthKind, rank);
        if (rankLabel.text != text) rankLabel.text = text;
        UpdateLayout();
    }

    private void OnEnable() => layoutValid = false;

    private void LateUpdate()
    {
        if (rankLabel != null && rankLabel.gameObject.activeSelf && reward != null) UpdateLayout();
    }

    private Vector3 Point(Vector2 uv)
    {
        if (frameFit != null && frameFit.isActiveAndEnabled)
            return frameFit.SpritePointToLocal(uv);
        Rect rect = icon.rectTransform.rect;
        Vector2 size = rect.size;
        if (icon.preserveAspect && icon.sprite != null)
        {
            Vector2 source = icon.sprite.rect.size;
            size = source * Mathf.Min(size.x / source.x, size.y / source.y);
        }
        return rect.center + Vector2.Scale(uv - Vector2.one * .5f, size);
    }

    private void UpdateLayout()
    {
        if (rankLabel.font == null) return;
        Vector2 center = reward.GrowthRankCenter;
        Vector2 sourceSize = reward.GrowthRankSize;
        Vector3 anchor = Point(center);
        Vector3 x = Point(center + new Vector2(sourceSize.x * .5f, 0)) - Point(center - new Vector2(sourceSize.x * .5f, 0));
        Vector3 y = Point(center + new Vector2(0, sourceSize.y * .5f)) - Point(center - new Vector2(0, sourceSize.y * .5f));
        Vector2 size = new Vector2(x.magnitude, y.magnitude);
        Quaternion rotation = Quaternion.Euler(0, 0, Mathf.Atan2(x.y, x.x) * Mathf.Rad2Deg);
        rankLabel.color = icon.color;
        if (layoutValid && anchor == lastAnchor && size == lastSize && rotation == lastRotation &&
            rankLabel.font == lastFont && rankLabel.fontSharedMaterial == lastMaterial && rankLabel.text == lastText)
            return;
        if (size.x <= 0f || size.y <= 0f) return;

        var rect = rankLabel.rectTransform;
        rect.sizeDelta = size;
        rect.localRotation = rotation;
        rect.localScale = Vector3.one;
        rect.localPosition = anchor;
        rankLabel.enableAutoSizing = false;
        rankLabel.enableWordWrapping = false;
        rankLabel.overflowMode = TextOverflowModes.Overflow;
        rankLabel.alignment = TextAlignmentOptions.Midline;
        // 依核准設定啟用 TMP Bold；後續仍以實際字形量測並置中。
        rankLabel.fontStyle = FontStyles.Bold;

        string currentText = rankLabel.text;
        rankLabel.fontSize = 100f;
        rankLabel.ForceMeshUpdate(true);
        Bounds measured = MeasureInk(rankLabel);
        // 每個階級各自適配原字框；I 保留厚實尺寸，III 等比縮小，中心不移動。
        rankLabel.fontSize = 100f * Mathf.Min(size.x * .94f / Mathf.Max(.001f, measured.size.x),
            size.y * .94f / Mathf.Max(.001f, measured.size.y));
        rankLabel.text = currentText;
        rankLabel.ForceMeshUpdate(true);
        // 與圖示共用原圖座標，並用實際字形而非 CJK 行框置中。
        rect.localPosition = anchor - rotation * MeasureInk(rankLabel).center;
        lastAnchor = anchor;
        lastSize = size;
        lastRotation = rotation;
        lastFont = rankLabel.font;
        lastMaterial = rankLabel.fontSharedMaterial;
        lastText = currentText;
        layoutValid = true;
    }

    private static Bounds MeasureInk(TMP_Text text)
    {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool visible = false;
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            var c = text.textInfo.characterInfo[i];
            if (!c.isVisible || c.textElement == null) continue;
            var m = c.textElement.glyph.metrics;
            Vector2 low = new Vector2(c.origin + m.horizontalBearingX * c.scale,
                c.baseLine + (m.horizontalBearingY - m.height) * c.scale);
            min = Vector2.Min(min, low);
            max = Vector2.Max(max, low + new Vector2(m.width, m.height) * c.scale);
            visible = true;
        }
        return visible ? new Bounds((min + max) * .5f, max - min) : new Bounds();
    }

    private void OnDisable()
    {
        layoutValid = false;
        if (rankLabel != null) rankLabel.gameObject.SetActive(false);
    }
}

