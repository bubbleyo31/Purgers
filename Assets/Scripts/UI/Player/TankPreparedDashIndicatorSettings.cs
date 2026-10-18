using System;
using UnityEngine;

/// <summary>純本機呈現設定；距離、寬鬆角及停靠距離仍取自 TankMeleeCombo。</summary>
[Serializable]
public sealed class TankPreparedDashIndicatorSettings
{
    public bool enabled = true;
    public Color bracketColor = new Color(1f, .502f, .141f, 1f);
    [Min(1f), Tooltip("以 1080p 為基準的框線粗細，像素。")]
    public float bracketWidth = 5f;
    [Min(4f)] public float bracketCornerLength = 24f;
    [Min(0f)] public float bracketPadding = 12f;
    [Min(.1f), Tooltip("整體受擊範圍任一軸超過此尺寸（公尺）時只框選中的受擊盒；血條標為 Large 者也使用部位框。")]
    public float largeEnemySize = 4f;
    [Min(0f), Tooltip("同一受擊盒的框形平滑時間；換目標／部位立即對準新範圍，避免框滑過其他敵人。")]
    public float bracketSmoothSeconds = .06f;
    public Color ringColor = new Color(.827f, .749f, .561f, .55f);
    [Min(.005f), Tooltip("地面圈世界寬度，公尺。")]
    public float ringWidth = .045f;
    [Range(32, 128)] public int ringSegments = 96;
    [Min(.01f)] public float ringFadeSeconds = .24f;
    [Min(.01f)] public float ringRefreshSeconds = .08f;
    [Min(.05f), Tooltip("以腳底為基準向上／下尋找地面的最大高差；超出不畫，避免投影到其他樓層。")]
    public float groundHeightTolerance = 1.5f;
    [Min(.01f), Tooltip("相鄰地面樣本高差超過此值不連線，避免跨平台或階差。")]
    public float groundJoinHeight = .6f;
}
