using TMPro;
using UnityEngine;

/// <summary>傷害跳字的共用外觀；不修改傷害計算或敵人生命。</summary>
[CreateAssetMenu(menuName = "Purgers/UI/傷害跳字設定", fileName = "DamageNumberSettings")]
public sealed class DamageNumberSettings : ScriptableObject
{
    [Header("顯示與合併")]
    [Tooltip("關閉後停止顯示並清除目前跳字；不影響命中回饋及傷害。")]
    public bool showNumbers = true;
    [Tooltip("開啟：一般攻擊每次独立跳字。關閉：同敵人、來源、傷害類型與頭部樣式在合併窗內累加。不影響持續傷害開關。")]
    public bool individualHits = true;
    [Tooltip("開啟：DamageOverTime 持續傷害在合併窗內累加；關閉：每次獨立跳字。不以射速猜測持續傷害。")]
    public bool mergeContinuousDamage = true;
    [Range(.02f, 1f), Tooltip("合併時間窗，秒；0.02～1，從該數字首次出現時計算，不因新傷害延長。")]
    public float mergeWindow = .2f;
    [Range(1, 128), Tooltip("最多同時保留的跳字，1～128；滿額時回收最舊一筆，不合併不同敵人。")]
    public int maximumNumbers = 64;

    [Header("文字與頭部命中")]
    [Tooltip("TMP 字型資產；留空使用 TMP 專案預設字型。建議使用既有 HUD 數字字型。")]
    public TMP_FontAsset font;
    [Range(8f, 96f), Tooltip("1080 高度畫面上的基準字級，8～96；隨視口高度縮放，不隨敵人距離或體型縮小。")]
    public float fontSize = 30f;
    [Tooltip("一般命中文字顏色。透明度亦生效。")]
    public Color normalColor = Color.white;
    [Tooltip("實際命中 Head 部位時的顏色，預設金黃色 #FFD34D；身體上的強制暴頭不使用此樣式。")]
    public Color headColor = new Color(1f, 211f / 255f, 77f / 255f, 1f);
    [Range(1f, 3f), Tooltip("實際頭部命中的字級倍率，1～3；1 表示不放大。預設 1.35。")]
    public float headScale = 1.35f;
    [Tooltip("SDF 文字描邊色；使用支援描邊的 TMP 材質。")]
    public Color outlineColor = new Color(0.04f, 0.04f, 0.04f, 1f);
    [Range(0f, .4f), Tooltip("TMP SDF 描邊寬度，0～0.4；0 關閉。只改本跳字實例材質，不修改原字型材質。")]
    public float outlineWidth = .18f;

    [Header("命中位置與動效")]
    [Tooltip("相對命中點的螢幕偏移，1080 高度像素；X 正值往右、Y 正值往上，不使用敵人頭頂。")]
    public Vector2 hitOffset = new Vector2(18f, 18f);
    [Range(0f, 80f), Tooltip("整段生命期向上漂移距離，1080 高度像素，0～80；0 固定。")]
    public float riseDistance = 32f;
    [Range(.1f, 3f), Tooltip("單筆顯示時間，秒；0.1～3，從首次出現起算，合併不延壽。")]
    public float lifetime = .8f;
    [Range(0f, .9f), Tooltip("生命期中保持完整透明度的比例，0～0.9；其後線性淡出。")]
    public float holdFraction = .45f;
    [Range(1f, 1.5f), Tooltip("初始跳動倍率，1～1.5，於前 0.12 秒回到正常字級；與頭部倍率相乘。")]
    public float popScale = 1.12f;
    [Range(0f, 40f), Tooltip("附近連續命中使用三個小幅垂直錯開位置，間距為 1080 高度像素，0～40；0 關閉。")]
    public float stackSpacing = 14f;
}
