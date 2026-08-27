using UnityEngine;

/// <summary>
/// 武器命中部位標籤。
///
/// 將這個元件掛在敵人的：
/// Collider
/// 或 Photon Fusion Hitbox
///
/// 所在的 GameObject 上。
///
/// 它只負責告訴武器：
/// 「你打中的這個位置是哪個部位。」
///
/// 它不負責：
/// 1. 扣血。
/// 2. 死亡。
/// 3. 傷害計算。
/// 4. 射擊。
/// </summary>
[DisallowMultipleComponent]
public class WeaponHitZone : MonoBehaviour
{
    // =====================================================================
    #region 命中區域設定

    [Header("命中區域設定")]

    [SerializeField]
    [Tooltip("這個 Collider 或 Photon Hitbox 所代表的身體部位。Body 代表一般身體，Head 代表頭部。AttackRifle 命中 Head 時會套用暴頭倍率。")]
    private WeaponHitZoneType zoneType =
        WeaponHitZoneType.Body;

    [SerializeField]
    [Min(0f)]
    [Tooltip("這個部位額外使用的傷害倍率。目前 Body 與 Head 都可以先設為 1，因為 Head 的暴頭倍率另外由武器的 Headshot Multiplier 控制。未來可以用於手腳減傷、裝甲或特殊弱點。")]
    private float damageMultiplier = 1f;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 此部位的種類。
    /// </summary>
    public WeaponHitZoneType ZoneType =>
        zoneType;

    /// <summary>
    /// 此部位額外的傷害倍率。
    /// </summary>
    public float DamageMultiplier =>
        Mathf.Max(
            0f,
            damageMultiplier
        );

    /// <summary>
    /// 此部位是否為頭部。
    ///
    /// AttackRifle 可以直接使用此屬性
    /// 判斷是否應套用 Headshot Multiplier。
    /// </summary>
    public bool IsHead =>
        zoneType ==
        WeaponHitZoneType.Head;

    #endregion
}