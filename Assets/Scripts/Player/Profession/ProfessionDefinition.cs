using UnityEngine;

/// <summary>
/// 職業定義資料。
///
/// 這是一份靜態設定資料，
/// 用來描述某個職業的基礎規則。
///
/// 目前先只放：
/// 1. 職業名稱。
/// 2. 勾索最大充能數。
/// 3. 勾索每格恢復時間。
/// 4. 擊殺敵人時回復多少勾索充能。
///
/// 之後可以繼續擴充：
/// 1. 最大血量。
/// 2. 技能資料。
/// 3. 被動效果。
/// 4. 移動參數。
/// 5. UI 圖示。
/// 6. 職業介紹文字。
/// </summary>
[CreateAssetMenu(
    fileName = "ProfessionDefinition",
    menuName = "Game/Profession/Profession Definition"
)]
public class ProfessionDefinition : ScriptableObject
{
    [Header("基本資料")]

    [SerializeField]
    [Tooltip("這份資料所屬的職業類型。請與 PlayerProfessionType 對應一致，例如 Attack、Tank、Support。")]
    private PlayerProfessionType professionType =
        PlayerProfessionType.Attack;

    [SerializeField]
    [Tooltip("職業顯示名稱。主要提供 UI、除錯資訊或之後的 Lobby 顯示使用。")]
    private string displayName = "Attack";

    [Header("舊版鈎索設定（僅保留序列化，新能量制不使用）")]

    [SerializeField]
    [Min(0)]
    [Tooltip("舊版資料，已不參與鈎索能量計算。請到玩家 Prefab 的 PlayerGrappleCharges 調整等級容量。")]
    private int grappleMaxCharges = 3;

    [SerializeField]
    [Min(0f)]
    [Tooltip("舊版資料，已停用自然回充；調整此值不影響新的鈎索能量。")]
    private float grappleRechargeDuration = 10f;

    [SerializeField]
    [Min(0)]
    [Tooltip("舊版資料，擊殺回充目前停用；調整此值不會恢復鈎索能量。")]
    private int grappleRestoreOnKill = 1;

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 這份資料所屬的職業類型。
    /// </summary>
    public PlayerProfessionType ProfessionType =>
        professionType;

    /// <summary>
    /// 顯示名稱。
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName)
            ? professionType.ToString()
            : displayName;

    /// <summary>
    /// 勾索最大充能數。
    /// </summary>
    public int GrappleMaxCharges =>
        Mathf.Max(0, grappleMaxCharges);

    /// <summary>
    /// 勾索每格恢復時間。
    /// </summary>
    public float GrappleRechargeDuration =>
        Mathf.Max(0f, grappleRechargeDuration);

    /// <summary>
    /// 擊殺敵人時回復多少格勾索充能。
    /// </summary>
    public int GrappleRestoreOnKill =>
        Mathf.Max(0, grappleRestoreOnKill);

    #endregion
}