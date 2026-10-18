using UnityEngine;

[CreateAssetMenu(menuName = "Purgers/UI/敵人血條設定")]
public sealed class EnemyHealthBarSettings : ScriptableObject
{
    [Header("百分比分格（不影響實際生命）")]
    [Range(1, 40), Tooltip("固定總格數，1～40；每格代表最大生命的 1/格數，允許部分填格。")]
    public int cellCount = 10;
    [Tooltip("1080 高度參考像素的寬／高，最小 20／3；不隨敵人體型或遠近改變。")]
    public Vector2 barSize = new Vector2(118, 9);
    [Min(0), Tooltip("格間空隙，1080 參考像素；執行時上限為每格步距的 40%。")]
    public float gap = 2;
    [Tooltip("已填生命的顏色；預設沿用隊友米白色。")]
    public Color healthColor = new Color32(241, 237, 217, 255);
    [Tooltip("未填容量底色；固定總寬及格數。")]
    public Color emptyColor = new Color(.12f, .15f, .16f, .75f);

    [Header("跟隨位置（1080 高度參考像素）")]
    [Min(0), Tooltip("血條底部與頭頂／身體投影上緣的間距。")]
    public float padding = 8;
    [Min(0), Tooltip("血條外緣與相機視口的最小間距。")]
    public float edgeInset = 16;
    [Min(0), Tooltip("大型敵人回到正常錨點前額外深入畫面的距離，抑制邊界跳動。")]
    public float hysteresis = 12;
    [Min(0), Tooltip("位置追隨速度，單位 1/秒；0 為立即跟隨。只平滑位置，不延遲遮擋隱藏或血量。")]
    public float followSpeed = 18;

    [Header("遮擋與本機預算")]
    [Tooltip("會遮住血條的實體 Collider Layer；預設全部，忽略 Trigger、目標本身及本機玩家。必須包含所有牆壁／地形。")]
    public LayerMask obstructionMask = ~0;
    [Min(0), Tooltip("最大顯示距離，公尺；0 不額外限制，仍受相機遠裁切影響。")]
    public float maxDistance;
    [Range(1, 128), Tooltip("單一 HUD 最多同時顯示的血條數；優先較近目標，每條一個 UI Graphic。")]
    public int maxVisibleBars = 32;
    [Range(1, 256), Tooltip("每幀最多檢查的畫面內候選數，優先較近目標；每候選最多九條遮擋射線，未檢查者隱藏。")]
    public int maxVisibilityChecks = 64;

    public Vector2 Size => new Vector2(Mathf.Max(20, barSize.x), Mathf.Max(3, barSize.y));
}

