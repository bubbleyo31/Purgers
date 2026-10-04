using System;
using UnityEngine;

/// <summary>只供 Unity Editor 診斷顯示；數值不參與技能、命中、移動或同步結果。</summary>
[Serializable]
public sealed class ActiveAbilityGizmoSettings
{
    [InspectorName("顯示技能 Gizmos"), Tooltip("此技能的線、球、路徑及文字總開關；不影響正式技能。另受 Scene/Game 視窗 Gizmos 與 Tools 選單的總開關控制。")]
    public bool enabled = true;
    [InspectorName("僅選取時顯示"), Tooltip("預設開啟以避免畫面擁擠；選技能 Runtime、其父物件或有效 Owner 玩家時顯示。關閉後未選取也顯示。")]
    public bool selectedOnly = true;
    [InspectorName("顯示文字"), Tooltip("顯示繁中技能名、顏色對應的距離／半徑／角度及預覽限制。")]
    public bool labels = true;
    [InspectorName("顯示路徑"), Tooltip("顯示投射路徑、直線或衝刺方向；關閉仍可看範圍與文字。")]
    public bool paths = true;
    [InspectorName("預覽目前場景碰撞"), Tooltip("以目前 PhysicsScene 做唯讀預估；不是 Fusion 歷史命中或未來移動結果。Prefab Mode 沒有場景環境時只畫理論路徑。")]
    public bool previewCollisions = true;
    [InspectorName("投射路徑預覽秒數"), Range(.1f,10f), Tooltip("僅限制投射物線段顯示長度，預設 3 秒，最多 10 秒；不修改真正存活時間。截斷端點不當成爆炸／落地點。")]
    public float trajectorySeconds = 3f;
    [InspectorName("自訂技能顏色"), Tooltip("關閉沿用各技能固定辨識色；開啟後線與文字一起使用下方顏色。")]
    public bool overrideColor;
    [InspectorName("線與文字顏色")] public Color color = Color.white;
    [InspectorName("文字大小"), Range(10,24)] public int labelSize = 13;
    [InspectorName("文字位置偏移"), Tooltip("世界座標公尺；只移動文字，0 不偏移。不改技能起點。")]
    public Vector3 labelOffset = new Vector3(0f,.3f,0f);
    public bool ShouldDraw(bool selected, bool globalEnabled) => enabled && globalEnabled && (!selectedOnly || selected);
}

#if UNITY_EDITOR
/// <summary>只在 Spawn 後由權威投射物提供的唯讀診斷快照。</summary>
public struct ActiveAbilityProjectileGizmoState
{
    public PlayerAbilityProjectileKind Kind;
    public Vector3 Position, Velocity;
    public float CollisionRadius, EffectRadius, Gravity, RemainingSeconds;
    public int CollisionMask, Bounces;
    public bool Resting, Impacted;
    public Transform Owner;
    public PhysicsScene Scene;
}
#endif
