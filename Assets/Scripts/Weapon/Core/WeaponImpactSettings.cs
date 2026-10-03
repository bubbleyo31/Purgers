using UnityEngine;

/// <summary>所有玩家共用的命中視覺設定；不改射擊命中遮罩或傷害。</summary>
[CreateAssetMenu(menuName = "Purgers/武器/命中特效設定")]
public sealed class WeaponImpactSettings : ScriptableObject
{
    [Header("命中物件 Layer 篩選")]
    [SerializeField, Tooltip("允許留下彈孔的命中物件 Layer；檢查實際 Collider／Hitbox 物件，不是父物件。預設只選 Default，Nothing 停用彈孔。排除 Enemy／Player 可避免敵人或隊友身上出現彈孔。")]
    private LayerMask bulletHoleLayers = 1;
    [SerializeField, Tooltip("允許播放火花的命中物件 Layer，與彈孔獨立。預設只選 Default，Nothing 停用火花；不影響命中／傷害判定。")]
    private LayerMask sparkLayers = 1;

    [Header("彈孔素材與每名玩家上限")]
    [SerializeField, Tooltip("可隨機選用的透明彈孔材質；正式設定使用 Compact、Radial、Fractured 三款，不含 Oblique。所有玩家應使用同一份設定與相同陣列順序。")]
    private Material[] bulletHoleMaterials = new Material[0];
    [SerializeField, Range(1, 128), Tooltip("每名射手在每個客戶端最多保留的彈孔，預設 32，範圍 1～128。超量先刪最舊的。Attack／Support 共用；玩家 Despawn、切場或元件停用時全部清除。")]
    private int maxBulletHolesPerPlayer = 32;
    [SerializeField, Range(0.01f, 2f), Tooltip("整張彈孔圖的世界邊長（公尺），預設 0.2，範圍 0.01～2。包含透明留白，中央黑孔實際較小。新命中生效。")]
    private float bulletHoleSize = 0.2f;
    [SerializeField, Range(0.0005f, 0.03f), Tooltip("彈孔沿表面法線向外偏移的公尺數，預設 0.002，範圍 0.0005～0.03，用來避免閃爍；太大會浮起。")]
    private float surfaceOffset = 0.002f;

    [Header("命中火花與效能保險")]
    [SerializeField, Tooltip("指定 SparkOnWall_vfx/Spark_vfx.prefab。必須包含 ParticleSystem，不能含 NetworkObject。生成的是本地視覺副本，+Z 朝表面外側；不修改原 Prefab。")]
    private GameObject sparkPrefab;
    [SerializeField, Range(1, 32), Tooltip("每名射手同時存在的火花上限，預設 12，範圍 1～32；超量刪最舊火花，避免高射速無限制生成。")]
    private int maxSparksPerPlayer = 12;
    [SerializeField, Range(0.01f, 10f), Tooltip("火花 Prefab 整體倍率，預設 1，範圍 0.01～10。保留 Prefab 內各粒子系統原本的相對縮放。")]
    private float sparkScale = 1f;
    [SerializeField, Range(0.1f, 30f), Tooltip("火花最長存活的遊戲秒數，預設 15，範圍 0.1～30；正常粒子播放完便提早銷毀，這是異常素材的保險。")]
    private float sparkTimeout = 15f;
    [SerializeField, Tooltip("火花相對表面朝外 +Z 的旋轉修正（度），預設 (0,0,0)。素材發射方向不同時才調整；不影響彈孔。")]
    private Vector3 sparkRotationOffset;

    public int MaxBulletHoles => Mathf.Clamp(maxBulletHolesPerPlayer, 1, 128);
    public int MaxSparks => Mathf.Clamp(maxSparksPerPlayer, 1, 32);
    public float BulletHoleSize => Mathf.Clamp(bulletHoleSize, 0.01f, 2f);
    public float SurfaceOffset => Mathf.Clamp(surfaceOffset, 0.0005f, 0.03f);
    public float SparkScale => Mathf.Clamp(sparkScale, 0.01f, 10f);
    public float SparkTimeout => Mathf.Clamp(sparkTimeout, 0.1f, 30f);
    public Vector3 SparkRotationOffset => sparkRotationOffset;
    public GameObject SparkPrefab => sparkPrefab;
    public int VariantCount => bulletHoleMaterials == null ? 0 : bulletHoleMaterials.Length;
    public Material GetMaterial(int index) => index >= 0 && index < VariantCount ? bulletHoleMaterials[index] : null;

    /// <summary>bit 0 為彈孔，bit 1 為火花；發送端與接收端都套用。</summary>
    public byte GetEffectsForLayer(int layer)
    {
        if (layer < 0 || layer > 31) return 0;
        int bit = 1 << layer;
        return (byte)(((bulletHoleLayers.value & bit) != 0 ? 1 : 0) |
            ((sparkLayers.value & bit) != 0 ? 2 : 0));
    }
}
