using Fusion;
using UnityEngine;

/// <summary>
/// 玩家可選擇的職業種類。
///
/// 注意：
/// 這個 Enum 數值未來可能會被網路資料、存檔、
/// Lobby 選角系統與技能系統引用。
///
/// 因此一旦正式使用後，
/// 不要任意交換既有項目的數值。
/// </summary>
public enum PlayerProfessionType : byte
{
    /// <summary>
    /// 尚未選擇或尚未初始化職業。
    /// </summary>
    None = 0,

    /// <summary>
    /// 攻擊型職業。
    /// </summary>
    Attack = 1,

    /// <summary>
    /// 坦克型職業。
    /// </summary>
    Tank = 2,

    /// <summary>
    /// 輔助型職業。
    /// </summary>
    Support = 3
}

/// <summary>
/// 玩家職業資料。
///
/// 每個 Player Network Prefab 都必須擁有一個。
///
/// 主要負責：
/// 1. 保存玩家目前職業。
/// 2. 將職業透過 Photon Fusion 同步。
/// 3. 提供其他系統安全讀取職業的方法。
/// 4. 根據職業回傳對應的 ProfessionDefinition。
///
/// 注意：
/// 這支腳本目前只負責「職業身分 + 職業定義查詢」。
///
/// 不要把：
/// 技能冷卻、技能施放、血量、武器邏輯
/// 全部塞進這支腳本。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerProfession : NetworkBehaviour
{
    // =====================================================================
    #region 預設職業

    [Header("測試用預設職業")]

    [SerializeField]
    [Tooltip("玩家生成時，如果尚未從 Lobby 或其他系統取得職業，就暫時使用這個職業。這個設定主要方便目前測試使用。未來完成 Lobby 選職業後，職業應由伺服器根據玩家選擇指定。")]
    private PlayerProfessionType defaultProfession =
        PlayerProfessionType.Attack;

    #endregion

    // =====================================================================
    #region 職業定義資料

    [Header("職業定義資料")]

    [SerializeField]
    [Tooltip("攻擊職業使用的 ProfessionDefinition。這份資料應設定 Attack 的勾索充能與冷卻規則。")]
    private ProfessionDefinition attackDefinition;

    [SerializeField]
    [Tooltip("坦克職業使用的 ProfessionDefinition。這份資料應設定 Tank 的勾索充能與冷卻規則。")]
    private ProfessionDefinition tankDefinition;

    [SerializeField]
    [Tooltip("輔助職業使用的 ProfessionDefinition。這份資料應設定 Support 的勾索充能與冷卻規則。")]
    private ProfessionDefinition supportDefinition;

    #endregion

    // =====================================================================
    #region 除錯設定

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，玩家職業被初始化或修改時會在 Console 顯示資訊。正式版本可以關閉。")]
    private bool debugProfession = true;

    #endregion

    // =====================================================================
    #region Fusion 網路資料

    /// <summary>
    /// 玩家目前正式使用的職業。
    ///
    /// 這是 Networked Property，
    /// 所以 Host 與所有 Client 都會得到相同結果。
    ///
    /// 未來：
/// 技能系統、UI、動畫、角色模型、武器限制
/// 都應讀取這個值。
    /// </summary>
    [Networked]
    public PlayerProfessionType CurrentProfession
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 公開快速判斷

    /// <summary>
    /// 玩家是否為攻擊職業。
    /// </summary>
    public bool IsAttack =>
        CurrentProfession ==
        PlayerProfessionType.Attack;

    /// <summary>
    /// 玩家是否為坦克職業。
    /// </summary>
    public bool IsTank =>
        CurrentProfession ==
        PlayerProfessionType.Tank;

    /// <summary>
    /// 玩家是否為輔助職業。
    /// </summary>
    public bool IsSupport =>
        CurrentProfession ==
        PlayerProfessionType.Support;

    /// <summary>
    /// 玩家目前是否已經擁有有效職業。
    /// </summary>
    public bool HasProfession =>
        CurrentProfession !=
        PlayerProfessionType.None;

    #endregion

    // =====================================================================
    #region 公開職業定義查詢

    /// <summary>
    /// 玩家目前職業所對應的 ProfessionDefinition。
    ///
    /// 若回傳 null，代表目前職業沒有對應的定義資料。
    /// </summary>
    public ProfessionDefinition CurrentDefinition =>
        GetDefinition(CurrentProfession);

    /// <summary>
    /// 玩家目前職業的勾索最大充能數。
    ///
    /// 若沒有對應定義資料，回傳 0。
    /// </summary>
    public int GrappleMaxCharges =>
        CurrentDefinition != null
            ? CurrentDefinition.GrappleMaxCharges
            : 0;

    /// <summary>
    /// 玩家目前職業的勾索每格恢復時間。
    ///
    /// 若沒有對應定義資料，回傳 0。
    /// </summary>
    public float GrappleRechargeDuration =>
        CurrentDefinition != null
            ? CurrentDefinition.GrappleRechargeDuration
            : 0f;

    /// <summary>
    /// 玩家目前職業擊殺敵人時回復的勾索格數。
    ///
    /// 若沒有對應定義資料，回傳 0。
    /// </summary>
    public int GrappleRestoreOnKill =>
        CurrentDefinition != null
            ? CurrentDefinition.GrappleRestoreOnKill
            : 0;

    #endregion

    // =====================================================================
    #region Spawn 前職業初始化

    /// <summary>
    /// 由 GameLogic 在 Runner.Spawn 的 OnBeforeSpawned 階段呼叫，
    /// 將玩家死亡前保存的職業寫入新 Player NetworkObject。
    ///
    /// 此時物件尚未正式進入 Spawned()，
    /// 但 Fusion 允許 State Authority 在 OnBeforeSpawned 初始化 Networked Property。
    ///
    /// 這能保證 PlayerProfession.Spawned() 與
    /// PlayerProfessionRuntimeManager.Spawned() 第一次讀取時，
    /// 就已經是死亡前的 Attack／Tank／Support，
    /// 不會先建立 Prefab 預設職業再重新切換。
    /// </summary>
    public void InitializeBeforeSpawn(
        PlayerProfessionType profession
    )
    {
        if (profession != PlayerProfessionType.Attack &&
            profession != PlayerProfessionType.Tank &&
            profession != PlayerProfessionType.Support)
        {
            return;
        }

        CurrentProfession =
            profession;
    }

    #endregion

    // =====================================================================
    #region Fusion 生命週期

    public override void Spawned()
    {
        /*
         * 只有 State Authority 可以決定正式職業。
         *
         * Host 模式下通常就是 Host。
         *
         * Client 不應自行修改自己的職業，
         * 否則未來技能與數值很容易被作弊。
         */
        if (Object.HasStateAuthority)
        {
            /*
             * 如果生成前還沒有其他系統指定職業，
             * 才使用 Inspector 的測試用預設值。
             */
            if (CurrentProfession ==
                PlayerProfessionType.None)
            {
                SetProfession(
                    defaultProfession
                );
            }
        }

        if (debugProfession)
        {
            ProfessionDefinition definition =
                CurrentDefinition;

            Debug.Log(
                $"[玩家職業]" +
                $"\n玩家：{Object.InputAuthority}" +
                $"\n目前職業：{CurrentProfession}" +
                $"\n職業定義：{(definition != null ? definition.name : "未設定")}" +
                $"\n勾索最大充能：{GrappleMaxCharges}" +
                $"\n勾索每格恢復時間：{GrappleRechargeDuration}" +
                $"\n擊殺回充：{GrappleRestoreOnKill}" +
                $"\nState Authority：{Object.HasStateAuthority}" +
                $"\nInput Authority：{Object.HasInputAuthority}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 職業修改

    /// <summary>
    /// 修改玩家正式職業。
    ///
    /// 目前只允許 State Authority 呼叫。
    ///
    /// 未來 Lobby 選角系統完成後，
    /// Server 可以在玩家生成時，
    /// 根據玩家 Lobby 選擇呼叫此方法。
    /// </summary>
    /// <param name="newProfession">
    /// 玩家要切換成的職業。
    /// </param>
    public void SetProfession(
        PlayerProfessionType newProfession
    )
    {
        /*
         * Client 不允許直接修改正式職業。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerProfession)}] " +
                $"只有 State Authority 可以修改玩家職業。",
                this
            );

            return;
        }

        /*
         * None 代表沒有職業。
         *
         * 正式玩家不應被設定成 None。
         */
        if (newProfession ==
            PlayerProfessionType.None)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerProfession)}] " +
                $"不能將正式玩家職業設定成 None。",
                this
            );

            return;
        }

        /*
         * 已經是同一職業就不需要重複修改。
         */
        if (CurrentProfession ==
            newProfession)
        {
            return;
        }

        PlayerProfessionType previousProfession =
            CurrentProfession;

        CurrentProfession =
            newProfession;

        if (debugProfession)
        {
            ProfessionDefinition definition =
                CurrentDefinition;

            Debug.Log(
                $"[玩家職業變更]" +
                $"\n玩家：{Object.InputAuthority}" +
                $"\n原職業：{previousProfession}" +
                $"\n新職業：{CurrentProfession}" +
                $"\n職業定義：{(definition != null ? definition.name : "未設定")}" +
                $"\n勾索最大充能：{GrappleMaxCharges}" +
                $"\n勾索每格恢復時間：{GrappleRechargeDuration}" +
                $"\n擊殺回充：{GrappleRestoreOnKill}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 職業定義查詢

    /// <summary>
    /// 根據職業種類回傳對應的 ProfessionDefinition。
    /// </summary>
    public ProfessionDefinition GetDefinition(
        PlayerProfessionType professionType
    )
    {
        switch (professionType)
        {
            case PlayerProfessionType.Attack:
            {
                return attackDefinition;
            }

            case PlayerProfessionType.Tank:
            {
                return tankDefinition;
            }

            case PlayerProfessionType.Support:
            {
                return supportDefinition;
            }

            case PlayerProfessionType.None:
            default:
            {
                return null;
            }
        }
    }

    #endregion
}