using UnityEngine;


/// <summary>
/// 敵人的主要戰鬥家族。
///
/// 這只是靜態分類，不代表目前 Runtime State。
/// </summary>
public enum EnemyCombatFamily : byte
{
    Melee = 0,
    Ranged = 1
}


/// <summary>
/// 同一戰鬥家族中的變體。
/// </summary>
public enum EnemyVariant : byte
{
    A = 0,
    B = 1,
    C = 2
}


/// <summary>
/// 敵人的主要移動類型。
///
/// Enemy Core 不會依這個 Enum 直接移動，
/// 它只用來驗證 Prefab 是否掛上正確的 Navigator / Motor。
/// </summary>
public enum EnemyLocomotionKind : byte
{
    Ground = 0,
    FreeFlying = 1,
    Stationary = 2
}


/// <summary>
/// 敵人的主要追逐策略。
/// </summary>
public enum EnemyChaseKind : byte
{
    /// <summary>
    /// 接近玩家並取得近戰包圍位置。
    /// </summary>
    ChaseA = 0,

    /// <summary>
    /// 尋找保有視線與理想射程的遠程位置。
    /// </summary>
    ChaseB = 1
}


/// <summary>
/// 一種敵人 Prefab 的靜態身分資料。
///
/// ====================================================================
///
/// 目前 Enemy Definition 類型：
///
/// Melee A
/// Melee B
/// Ranged A
/// Ranged B
/// Ranged C Projectile / Beam（Stationary）
///
/// ====================================================================
///
/// 這份 ScriptableObject 只保存不會隨戰鬥改變的資料。
///
/// 不可以把以下資料寫進來：
///
/// Current Health
/// Current Target
/// Cooldown Timer
/// Current State
/// Shield Health
///
/// 因為多隻敵人可以共用同一份 EnemyDefinition。
/// 如果把 Runtime State 寫回資產，所有怪物會彼此污染狀態。
/// </summary>
[CreateAssetMenu(
    fileName = "EnemyDefinition",
    menuName = "Game/Enemy/Enemy Definition"
)]
public sealed class EnemyDefinition :
    ScriptableObject
{
    // =====================================================================
    #region Identity

    [Header("敵人身分")]

    [SerializeField]
    [Tooltip(
        "敵人的穩定資料 ID。\n\n" +
        "建議只使用小寫英文字母、數字與底線，例如：\n" +
        "melee_a\n" +
        "melee_b\n" +
        "ranged_a\n" +
        "ranged_b\n\n" +
        "日後存檔、生成表、掉落表與除錯會使用這個 ID，" +
        "正式使用後不要因為顯示名稱改變就隨意修改。")]
    private string enemyId =
        "enemy";

    [SerializeField]
    [Tooltip(
        "顯示給設計者、UI 或除錯訊息看的敵人名稱。\n" +
        "可以使用中文，也可以在之後替換成在地化 Key。")]
    private string displayName =
        "Enemy";

    [SerializeField]
    [TextArea(2, 5)]
    [Tooltip(
        "這個敵人變體的設計備註。\n\n" +
        "例如：普通數量型近戰怪、會直線突進並使用雙防禦的近戰菁英。\n" +
        "只供設計與除錯閱讀，不影響 Gameplay。")]
    private string designDescription;

    #endregion

    // =====================================================================
    #region Classification

    [Header("敵人分類")]

    [SerializeField]
    [Tooltip(
        "敵人的主要戰鬥家族。\n\n" +
        "Melee：近戰家族。\n" +
        "Ranged：遠程家族。")]
    private EnemyCombatFamily combatFamily =
        EnemyCombatFamily.Melee;

    [SerializeField]
    [Tooltip(
        "同一戰鬥家族中的 A／B／C 變體。\n\n" +
        "此欄只描述身分；實際能力仍由 Prefab 上的 Ability Component 決定。")]
    private EnemyVariant variant =
        EnemyVariant.A;

    [SerializeField]
    [Tooltip(
        "敵人的主要移動類型。\n\n" +
        "Ground：地面導航。\n" +
        "Free Flying：自由 XYZ 飛行。\n" +
        "Stationary：定點怪，不掛巡邏／追逐 Brain 或 Navigator，不需要巡邏區；攻擊仍可原地旋轉。\n\n" +
        "EnemyActor 只用它驗證組合，不會直接依 Enum 執行移動。")]
    private EnemyLocomotionKind locomotionKind =
        EnemyLocomotionKind.Ground;

    [SerializeField]
    [Tooltip(
        "敵人的主要追逐策略。\n\n" +
        "Chase A：接近玩家並加入近戰包圍。\n" +
        "Chase B：保持視線、理想射程與安全距離。Stationary 不讀此欄，無須指定地面／飛行追逐。")]
    private EnemyChaseKind chaseKind =
        EnemyChaseKind.ChaseA;

    [Header("玩家經驗")]
    [SerializeField, Min(0)]
    [Tooltip("正式擊殺此種敵人時，所有當下存活隊友各取得的基礎經驗。擊殺者另有共用規則加成；0 代表不給經驗。Phase 5 完成前不讀 Enemy Level。")]
    private int baseKillExperience = 1;

    #endregion

    // =====================================================================
    #region Public Data

    public string EnemyId =>
        string.IsNullOrWhiteSpace(enemyId)
            ? name
            : enemyId.Trim();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();

    public string DesignDescription =>
        designDescription;

    public EnemyCombatFamily CombatFamily =>
        combatFamily;

    public EnemyVariant Variant =>
        variant;

    public EnemyLocomotionKind LocomotionKind =>
        locomotionKind;

    public EnemyChaseKind ChaseKind =>
        chaseKind;

    public int BaseKillExperience =>
        Mathf.Max(0, baseKillExperience);

    #endregion

    // =====================================================================
    #region Validation

    private void OnValidate()
    {
        baseKillExperience = Mathf.Max(0, baseKillExperience);
        if (enemyId != null)
        {
            enemyId =
                enemyId.Trim();
        }

        if (displayName != null)
        {
            displayName =
                displayName.Trim();
        }
    }

    #endregion
}
