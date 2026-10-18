using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName="Purgers/UI/隊友辨識設定",fileName="TeammateDisplaySettings")]
public sealed class TeammateDisplaySettings : ScriptableObject
{
    [Header("頭頂名稱與分格生命")]
    [Tooltip("名稱字型；留空使用 TMP 預設字型。中文名稱請配置含繁體中文字形的字型。")] public TMP_FontAsset font;
    [Min(8),Tooltip("1080 高度畫面的字級；至少 8。遠處保持可讀大小。")] public float nameSize=22;
    [Min(40),Tooltip("名條寬度，1080 參考像素；長名稱以省略號顯示。")] public float nameWidth=180;
    [Min(1),Tooltip("血格單位，預設每格 20 HP。沿用現有整格餘額呈現。")] public float healthPerCell=20;
    [Range(0,1),Tooltip("真實生命比例小於或等於此值時生命格變紅；預設 25%，不影響護盾或辨識色。")] public float lowHealthThreshold=.25f;
    [Tooltip("一般生命格色彩。")] public Color healthColor=new Color32(241,237,217,255);
    [Tooltip("殘血生命格色彩；護盾仍使用淡金。")] public Color lowHealthColor=new Color32(239,74,67,255);
    [Tooltip("護盾格色彩；緊接可見生命格，同一水平列。")] public Color shieldColor=new Color32(213,196,94,255);
    [Min(3),Tooltip("血條高度，1080 參考像素。")] public float barHeight=9;
    [Min(0),Tooltip("未指定頭頂錨點時，從角色 Root 向上的世界高度。")] public float fallbackHeadHeight=2.15f;
    [Min(0),Tooltip("頭頂錨點以上的額外螢幕間距，1080 參考像素。")] public float headPadding=14;
    [Header("畫面邊緣箭頭")]
    [Min(8),Tooltip("箭頭尺寸，1080 參考像素；畫面外只顯示箭頭。")] public float arrowSize=26;
    [Min(10),Tooltip("距世界相機視口邊緣的安全距離，1080 參考像素；會包含箭頭半徑。")] public float edgeInset=38;
    [Min(0),Tooltip("切換名條／箭頭的遲滯距離，1080 參考像素；避免邊界抖動。")] public float boundaryHysteresis=12;
    [Header("角色輪廓")]
    [Range(.5f,8),Tooltip("描邊厚度，螢幕像素；需配置 TeammateOutlineFeature 與角色身體 Renderer。")] public float outlinePixels=2;
    [Range(0,1),Tooltip("未被牆遮擋時的輪廓透明度。")] public float visibleOutline=.28f;
    [Range(0,1),Tooltip("被牆遮擋時的輪廓透明度。")] public float occludedOutline=.9f;
    [Min(0),Tooltip("輪廓色彩亮度倍率；不修改場景 Bloom。")] public float outlineBrightness=1.3f;
}
