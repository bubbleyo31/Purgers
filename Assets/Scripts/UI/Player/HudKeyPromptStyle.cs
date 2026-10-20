using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>遊戲內鍵帽的唯一美術來源。不持有按鍵綁定、輸入或 HUD 顯示資格。</summary>
[CreateAssetMenu(menuName = "Purgers/UI/HUD Key Prompt Style")]
public sealed class HudKeyPromptStyle : ScriptableObject
{
    public const string ResourcePath = "Presentation/HudKeyPromptStyle";
    static HudKeyPromptStyle shared;
    public static HudKeyPromptStyle Shared => shared != null ? shared : shared = Resources.Load<HudKeyPromptStyle>(ResourcePath);

    [Header("共用美術（所有 HUD 鍵帽）")]
    public Sprite background;
    public Image.Type imageType = Image.Type.Simple;
    public Material backgroundMaterial;
    public TMP_FontAsset font;
    public Material fontMaterial;
    public FontStyles fontStyle = FontStyles.Normal;
    public Color textColor = Color.black;
    public Color readyColor = new Color32(241, 237, 217, 255);
    public Color pressedColor = new Color32(213, 196, 94, 255);
    public Color unavailableColor = new Color32(111, 115, 99, 255);

    [Header("基準尺寸（Canvas 單位；世界／窄列等比縮放）")]
    [Min(16)] public float height = 32;
    [Min(16)] public float minimumWidth = 32;
    [Min(32)] public float maximumWidth = 104;
    public Vector2 padding = new Vector2(8, 2);
    [Min(1)] public float fontSize = 21;
    [Min(1)] public float minimumFontSize = 10;
    [Tooltip("依可見字形幾何置中後的共用光學微調，不另設 Q／E 偏移。")]
    public Vector2 textOffset;
    [Min(0)] public float belowGap = 19;
    [Min(0)] public float inlineGap = 8;
    public int Revision { get; private set; }

    void OnValidate()
    {
        height = Mathf.Max(16, height);
        minimumWidth = Mathf.Max(16, minimumWidth);
        maximumWidth = Mathf.Max(minimumWidth, maximumWidth);
        padding = new Vector2(Mathf.Clamp(padding.x, 0, minimumWidth * .45f), Mathf.Clamp(padding.y, 0, height * .4f));
        fontSize = Mathf.Max(1, fontSize);
        minimumFontSize = Mathf.Clamp(minimumFontSize, 1, fontSize);
        Revision++;
    }

    public void Apply(TMP_Text label, Image image)
    {
        if (image != null)
        {
            image.sprite = background;
            image.overrideSprite = null;
            image.type = imageType;
            image.material = backgroundMaterial;
            image.preserveAspect = false;
            image.raycastTarget = false;
            image.color = readyColor;
        }
        if (label == null) return;
        if (font != null) label.font = font;
        if (fontMaterial != null) label.fontSharedMaterial = fontMaterial;
        label.fontStyle = fontStyle;
        label.fontWeight = FontWeight.Regular;
        label.color = textColor;
        label.enableVertexGradient = false;
        label.characterSpacing = label.wordSpacing = label.lineSpacing = label.paragraphSpacing = 0;
        label.rectTransform.localScale = Vector3.one;
        label.rectTransform.localRotation = Quaternion.identity;
    }
}
