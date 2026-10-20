using Purgers.Progression;
using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "Purgers/UI/TAB 隊伍資訊窗")]
public sealed class TeamInfoPanelSettings : ScriptableObject
{
    [Header("沿用正式圖示與字體")]
    public TMP_FontAsset font, rankFont;
    public PlayerPortraitProvider portraits;
    public PlayerRewardCatalog catalog;
    public Sprite skillBackground, growthBackground;
    [System.Serializable] public struct WeaponContent
    {
        public Sprite sprite;
        [Tooltip("原圖可見內容的比例範圍，左下為 0,0；只略去透明留白，不修改或重製原圖。")]
        public Rect contentUV;
    }
    public WeaponContent[] weaponContent = System.Array.Empty<WeaponContent>();
    [Header("固定四人視窗；不隨玩家數量改變")]
    public Vector2 referenceResolution = new Vector2(1920,1080);
    public Vector2 windowSize = new Vector2(1104,824);
    [Tooltip("視窗中心相對畫面中心的位移；可用 HUD 排版工具拖曳。")]
    public Vector2 windowOffset;
    [Range(0.7f,1.2f), Tooltip("整個資訊窗的等比倍率；不改變四列比例。")]
    public float scale = 1f;
    [Header("被動與成長（固定區域內換行，超出後捲動）")]
    [Range(36,80)] public float preferredIconSize = 68;
    [Range(28,60)] public float minimumIconSize = 42;
    [Range(4,20)] public float iconGap = 8;
    [Range(0.1f,0.9f)] public float cooldownOpacity = .62f;
    public static TeamInfoPanelSettings Load() => Resources.Load<TeamInfoPanelSettings>("Presentation/TeamInfoPanel");
}
