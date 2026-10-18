using System;
using UnityEngine;

[Serializable]
public sealed class AbilityColorGrade
{
    [Tooltip("陰影 Lift。xyz 為色偏，w 是亮度偏移，並非透明度；中性值 (1,1,1,0)。")]
    public Vector4 lift = new Vector4(1f, 1f, 1f, 0f);
    [Tooltip("中間調 Gamma。xyz 為色偏，w 是亮度偏移。")]
    public Vector4 gamma = new Vector4(1f, 1f, 1f, 0f);
    [Tooltip("亮部 Gain。xyz 為色偏，w 是亮度偏移。")]
    public Vector4 gain = new Vector4(1f, 1f, 1f, 0f);
    [Range(0f, 1f), Tooltip("此效果強度。0 停用染色，1 使用完整設定；不影響技能玩法。")]
    public float strength = 1f;
}

[CreateAssetMenu(fileName = "ActiveAbilityColorGrading", menuName = "Game/Player Ability/畫面 Lift Gamma Gain")]
public sealed class AbilityColorGradingSettings : ScriptableObject
{
    public const string ResourcePath = "Presentation/ActiveAbilityColorGrading";
    [Header("共同轉場")]
    [Min(.01f), Tooltip("技能效果淡入／淡出各自秒數，使用本機未縮放時間；預設 0.25。")]
    public float fadeSeconds = .25f;
    [Min(.01f), Tooltip("一次成功回血的淺綠閃爍總秒數；預設 0.45，不在初始化或滿血時播放。")]
    public float healingPulseSeconds = .45f;
    [Header("子彈時間：紫色")]
    public AbilityColorGrade bulletTime = new AbilityColorGrade {
        lift = new Vector4(1.04f,.98f,1.08f,0f), gamma = new Vector4(1.08f,.94f,1.16f,0f),
        gain = new Vector4(1.06f,.97f,1.12f,0f), strength = .8f };
    [Header("精準鎖敵：藍綠色")]
    public AbilityColorGrade precisionLock = new AbilityColorGrade {
        lift = new Vector4(.97f,1.04f,1.05f,0f), gamma = new Vector4(.94f,1.10f,1.10f,0f),
        gain = new Vector4(.97f,1.06f,1.08f,0f), strength = .7f };
    [Header("經驗增加：黃色")]
    public AbilityColorGrade experience = new AbilityColorGrade {
        lift = new Vector4(1.04f,1.03f,.98f,0f), gamma = new Vector4(1.09f,1.07f,.94f,0f),
        gain = new Vector4(1.08f,1.06f,.98f,0f), strength = .7f };
    [Header("不是今天：黃色閃爍")]
    [Min(.1f), Tooltip("黃光明暗循環秒數，預設 0.75；僅控制呈現節奏，持續時間讀取玩家的無敵計時。")]
    public float notTodayPulseSeconds = .75f;
    public AbilityColorGrade notToday = new AbilityColorGrade {
        lift = new Vector4(1.08f,1.06f,.94f,0f), gamma = new Vector4(1.22f,1.17f,.75f,0f),
        gain = new Vector4(1.15f,1.12f,.88f,0f), strength = .9f };
    [Header("成功回血：很淺的綠色")]
    public AbilityColorGrade healing = new AbilityColorGrade {
        lift = new Vector4(.99f,1.025f,.995f,0f), gamma = new Vector4(.98f,1.06f,.99f,0f),
        gain = new Vector4(.99f,1.04f,1f,0f), strength = .4f };
}
