using System;
using UnityEngine;

public enum DamageIndicatorOcclusionMode { FadeThroughWalls, HideBehindWalls }

[Serializable]
public sealed class DamageIndicatorStyle
{
    [Tooltip("開啟此層提示；關閉 3D 時不執行遮擋查詢。")]
    public bool enabled = true;
    [Tooltip("可替換的 Sprite；留空使用可調幾何圖形。2D 圖片預設朝上，3D 圖片不旋轉。")]
    public Sprite sprite;
    [Tooltip("圖形顏色；Alpha 與下方不透明度、淡出曲線相乘。")]
    public Color color = new Color(1f, 90f/255f, 79f/255f, 1f);
    [Tooltip("描邊顏色；自訂 Sprite 也套用描邊。")]
    public Color outlineColor = new Color(37f/255f, 28f/255f, 28f/255f, 1f);
    [Min(0), Tooltip("1080p 基準的描邊寬，像素；0 關閉。")]
    public float outlineWidth = 1f;
    [Range(0,1), Tooltip("可見時的不透明度；0 不顯示，1 完全不透明。")]
    public float opacity = .9f;
    [Min(0), Tooltip("淡入秒數；0 立即出現。使用本機非縮放時間。")]
    public float fadeIn = .05f;
    [Min(0), Tooltip("滿亮停留秒數；遮擋切換不重設時間。")]
    public float hold = .45f;
    [Min(0), Tooltip("淡出秒數；0 直接消失。")]
    public float fadeOut = .5f;
    public float Duration => Mathf.Max(0, fadeIn) + Mathf.Max(0, hold) + Mathf.Max(0, fadeOut);
}

[CreateAssetMenu(menuName="Purgers/UI/受傷來源指示設定")]
public sealed class DamageIndicatorSettings : ScriptableObject
{
    [Header("2D 方向弧段")]
    public DamageIndicatorStyle arc = new DamageIndicatorStyle();
    [Min(1), Tooltip("1080p 基準半徑；弧段中心線距離準星的像素數。")]
    public float arcRadius = 100f;
    [Range(1,180), Tooltip("內建短弧張角，度；自訂 Sprite 不使用。")]
    public float arcDegrees = 38f;
    [Min(.1f), Tooltip("內建短弧中段厚度，1080p 像素；兩端漸尖。")]
    public float arcThickness = 8f;
    [Tooltip("自訂方向圖像的寬高，1080p 像素；空白 Sprite 使用上方弧段幾何。")]
    public Vector2 arcSpriteSize = new Vector2(65, 12);
    [Tooltip("自訂方向圖像額外旋轉角度；0 表示圖片原始朝上。")]
    public float arcSpriteRotation;

    [Header("3D 來源位置標記（世界座標投影至 HUD）")]
    public DamageIndicatorStyle marker = new DamageIndicatorStyle();
    [Tooltip("固定螢幕寬高，1080p 像素；距離不縮放，中心留空。")]
    public Vector2 markerSize = new Vector2(26,22);
    [Min(.1f), Tooltip("內建中空倒三角線寬，1080p 像素；自訂 Sprite 不使用。")]
    public float triangleLineWidth = 2.5f;
    [Tooltip("圖像對準來源的正規化定位點；(0.5,0) 為底部尖端，(0.5,0.5) 為中心。")]
    public Vector2 markerPivot = new Vector2(.5f,0f);
    [Tooltip("來源投影後的畫面偏移，1080p 像素；不改傷害來源世界快照。")]
    public Vector2 markerOffset;

    [Header("持續遮擋判定與效能上限")]
    [Tooltip("淡色透牆或直接隱藏；隱藏後仍持續重新判定。")]
    public DamageIndicatorOcclusionMode occlusionMode = DamageIndicatorOcclusionMode.FadeThroughWalls;
    [Range(0,1), Tooltip("牆後的絕對不透明度，預設 0.25；仍乘上圖形 Alpha 與生命週期淡出。")]
    public float occludedOpacity = .25f;
    [Tooltip("遮擋圖層；預設排除 UI、玩家、第一人稱武器／模型、戰鬥 Hitbox 與 Ignore Raycast。牆／門 Collider 的 Layer 必須包含。")]
    public LayerMask obstructionMask = Physics.DefaultRaycastLayers & ~((1<<5)|(1<<6)|(1<<7)|(1<<8)|(1<<9));
    [Range(.02f,.2f), Tooltip("每個畫面內標記的重查間隔，秒；預設 0.05（20 Hz），牆或相機移動都會重新判定。")]
    public float occlusionInterval = .05f;
    [Range(1,8), Tooltip("同時保留的來源提示數量；滿額取代最舊提示。降低可減少 UI 與遮擋成本。")]
    public int maximumIndicators = 4;
    [Range(1,8), Tooltip("每幀最多遮擋檢查組數；每組最多一次起點重疊與一次射線查詢，採輪流排程。")]
    public int maximumProbesPerFrame = 2;
}
