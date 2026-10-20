using System;
using UnityEngine;

[Serializable]
public sealed class ScreenEdgeStyle
{
    [Tooltip("外框主色，Alpha 由強度與時間計算。")]
    public Color tint;
    [Range(0, 1), Tooltip("外框最大強度。")]
    public float strength;
    [Range(.04f, .24f), Tooltip("左右各向內延伸的螢幕寬度比例。")]
    public float width;
    [Range(.03f, .2f), Tooltip("上下各向內延伸的螢幕高度比例。")]
    public float verticalWidth;
    [Range(0, 1), Tooltip("四角額外加強。")]
    public float corners;
    [Range(0, 14), Tooltip("只供治療使用的十字數量。")]
    public int symbols;
    [Min(.01f), Tooltip("事件淡入秒數。")]
    public float fadeIn;
    [Min(0), Tooltip("單次峰值停留秒數。")]
    public float hold;
    [Min(.01f), Tooltip("最後一次事件後的淡出秒數。")]
    public float fadeOut;
}

[CreateAssetMenu(menuName = "Purgers/Presentation/Screen Edge Effects")]
public sealed class ScreenEdgeEffectsSettings : ScriptableObject
{
    [Tooltip("正式 UI Shader，序列化引用確保隨 Build 收錄。")]
    public Shader shader;
    [Tooltip("整組外框開關；關閉會恢復既有治療 LGG。")]
    public bool effectsEnabled = true;
    [Tooltip("治療 V1.2 收窄版。")]
    public ScreenEdgeStyle healing = new ScreenEdgeStyle { tint = new Color(.30f,.95f,.64f), strength=.52f, width=.105f, verticalWidth=.075f, corners=.55f, symbols=12, fadeIn=.10f, hold=.16f, fadeOut=.70f };
    [Tooltip("受傷 V1；只呈現實際扣血，不將完全格擋判成受傷。")]
    public ScreenEdgeStyle damage = new ScreenEdgeStyle { tint = new Color(.96f,.16f,.10f), strength=.66f, width=.15f, verticalWidth=.115f, corners=.50f, fadeIn=.035f, hold=.065f, fadeOut=.52f };
    [InspectorName("速度動能增益"), Tooltip("速度動能增益；既有量條 NormalizedEnergy 達到 1 才顯示，離開滿格後淡出。")]
    public ScreenEdgeStyle momentum = new ScreenEdgeStyle { tint = new Color(1,.53f,.075f), strength=.64f, width=.105f, verticalWidth=.075f, corners=.60f, fadeIn=.16f, fadeOut=.65f };
    [HideInInspector, Tooltip("舊版流線速度；保留序列化相容，移除流線後不再使用。")]
    public float motionRate = 1.6f;
    [Range(0,1), Tooltip("合成後最大不透明度，防止多種狀態遮蔽畫面。")]
    public float maximumOpacity = .75f;
    [Range(0,1), Tooltip("受傷時降低治療／速度動能增益，治療時降低速度動能增益的比例。")]
    public float prioritySuppression = .75f;
}
