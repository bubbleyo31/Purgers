using Fusion;
using UnityEngine;

/// <summary>
/// 玩家目前職業的 Runtime NetworkObject。
///
/// ------------------------------------------------------------
///
/// 這個物件代表：
///
/// 「目前這名玩家真正載入中的職業 Gameplay Module」
///
/// ------------------------------------------------------------
///
/// 未來：
///
/// AttackProfessionRuntime
/// ↓
/// AttackRifle
/// PlayerAimController
/// AttackFocusAbility
/// AttackQuickMelee
/// AttackGrappleMarkAbility
///
/// TankProfessionRuntime
/// ↓
/// TankMeleeCombo
/// TankGuard
/// TankQuickDash
/// TankGrappleGather
/// TankAirDash
///
/// ------------------------------------------------------------
///
/// 目前第一階段：
///
/// Runtime 還不放任何真正技能。
///
/// 我們只測：
///
/// Spawn
/// Despawn
/// Owner
/// Profession
///
/// 是否正確。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerProfessionRuntime :
    NetworkBehaviour
{
    // =====================================================================
    #region Prefab 設定

    [Header("Runtime Prefab 設定")]

    [SerializeField]
    [Tooltip("這個 Runtime Prefab 預期代表哪一個職業。AttackProfessionRuntime Prefab 設為 Attack，Tank 設為 Tank，Support 設為 Support。這主要用來檢查 Prefab 是否配置錯誤。")]
    private PlayerProfessionType prefabProfession =
        PlayerProfessionType.None;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會在 Runtime Spawn 時顯示職業、Owner Player、Input Authority 與 State Authority，方便確認職業切換是否正確。")]
    private bool debugRuntime =
        true;

    #endregion

    // =====================================================================
    #region Fusion State

    /// <summary>
    /// 這個 Profession Runtime
    /// 屬於哪一個 Player NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// Runtime 不會依賴：
    ///
    /// transform.parent
    ///
    /// 來尋找玩家。
    ///
    /// 而是使用正式 NetworkObject Reference。
    ///
    /// 這樣未來 Runtime 即使不放在 Player Transform
    /// 底下，也能可靠取得 Owner。
    /// </summary>
    [Networked]
    public NetworkObject OwnerPlayerObject
    {
        get;
        private set;
    }

    /// <summary>
    /// 這個 Runtime 正式代表的職業。
    /// </summary>
    [Networked]
    public PlayerProfessionType RuntimeProfession
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Runtime Driver

    /// <summary>
    /// 這個 Profession Runtime
    /// 真正負責職業 Gameplay 的 Driver。
    ///
    /// Attack Runtime
    /// → AttackProfessionRuntimeDriver
    ///
    /// Tank Runtime
    /// → TankProfessionRuntimeDriver
    ///
    /// Support Runtime
    /// → SupportProfessionRuntimeDriver。
    /// </summary>
    private PlayerProfessionRuntimeDriver
        runtimeDriver;

    /// <summary>
    /// 是否已經成功把 Driver
    /// 綁定到 Owner Player。
    /// </summary>
    private bool driverBound;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// Runtime 所屬 Player。
    ///
    /// 只是方便其他職業模組取得 Player Core。
    /// </summary>
    public Player OwnerPlayer
    {
        get
        {
            if (OwnerPlayerObject == null)
            {
                return null;
            }

            return
                OwnerPlayerObject
                    .GetComponent<Player>();
        }
    }

    /// <summary>
    /// Runtime 所屬 PlayerProfession。
    /// </summary>
    public PlayerProfession OwnerProfession
    {
        get
        {
            if (OwnerPlayerObject == null)
            {
                return null;
            }

            return
                OwnerPlayerObject
                    .GetComponent<PlayerProfession>();
        }
    }

    #endregion

    // =====================================================================
    #region Spawn 初始化

    /// <summary>
    /// 由 PlayerProfessionRuntimeManager
    /// 在 Runner.Spawn 的 OnBeforeSpawned 階段呼叫。
    ///
    /// ------------------------------------------------------------
    ///
    /// Fusion 官方允許 OnBeforeSpawned
    /// 初始化 Networked Properties。
    ///
    /// 所以當這個 Runtime 真正出現在其他 Peer 時，
    /// Owner 與 Profession 已經是正確資料。
    /// </summary>
    public void InitializeBeforeSpawn(
        NetworkObject ownerPlayerObject,
        PlayerProfessionType runtimeProfession
    )
    {
        OwnerPlayerObject =
            ownerPlayerObject;

        RuntimeProfession =
            runtimeProfession;
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        // =============================================================
        // Prefab 配置檢查
        // =============================================================

        /*
         * 這不是 Gameplay 判定。
         *
         * 純粹避免：
         *
         * Tank Prefab
         * 卻被 Inspector 設成 Attack。
         */
        if (prefabProfession !=
                PlayerProfessionType.None &&
            prefabProfession !=
                RuntimeProfession)
        {
            Debug.LogError(
                $"[{nameof(PlayerProfessionRuntime)}] " +
                $"Runtime Prefab 職業設定與實際 Spawn 職業不一致。" +
                $"\nPrefab Profession：{prefabProfession}" +
                $"\nRuntime Profession：{RuntimeProfession}" +
                $"\n物件：{name}",
                this
            );
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugRuntime)
        {
            Debug.Log(
                $"[職業 Runtime] Spawned" +
                $"\nRuntime：{name}" +
                $"\n職業：{RuntimeProfession}" +
                $"\nOwner：" +
                $"{(OwnerPlayerObject != null ? OwnerPlayerObject.name : "無")}" +
                $"\nInput Authority：{Object.InputAuthority}" +
                $"\nState Authority：{Object.HasStateAuthority}",
                this
            );
        }

        /*
        * 嘗試取得 Runtime Driver。
        *
        * OwnerPlayerObject 已經在
        * OnBeforeSpawned / InitializeBeforeSpawn
        * 寫入 Networked Property。
        */
        ResolveRuntimeDriver();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        if (debugRuntime)
        {
            Debug.Log(
                $"[職業 Runtime] Despawned" +
                $"\nRuntime：{name}" +
                $"\n職業：{RuntimeProfession}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Runtime Driver Control

    /// <summary>
    /// 尋找這個 Runtime Prefab 上的 Gameplay Driver。
    ///
    /// ------------------------------------------------------------
    ///
    /// 每個 Profession Runtime
    /// 必須剛好有一個：
    ///
    /// PlayerProfessionRuntimeDriver。
    /// </summary>
    private void ResolveRuntimeDriver()
    {
        runtimeDriver =
            GetComponent<PlayerProfessionRuntimeDriver>();

        driverBound =
            false;

        if (runtimeDriver == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerProfessionRuntime)}] " +
                $"找不到 PlayerProfessionRuntimeDriver。" +
                $"\nRuntime：{name}" +
                $"\nProfession：{RuntimeProfession}",
                this
            );

            return;
        }

        TryBindRuntimeDriver();
    }

    /// <summary>
    /// 嘗試把 Driver 綁定到 Owner Player。
    ///
    /// ------------------------------------------------------------
    ///
    /// Proxy 收到 Networked Reference 的時間
    /// 有可能比本地 Runtime Spawn callback 稍晚，
    /// 所以這個函式允許之後再次嘗試。
    /// </summary>
    private bool TryBindRuntimeDriver()
    {
        if (runtimeDriver == null)
        {
            runtimeDriver =
                GetComponent<PlayerProfessionRuntimeDriver>();
        }

        if (runtimeDriver == null)
        {
            return false;
        }

        if (OwnerPlayerObject == null ||
            OwnerPlayer == null)
        {
            return false;
        }

        driverBound =
            runtimeDriver.BindRuntime(
                this
            );

        return driverBound;
    }

    /// <summary>
    /// 由 PlayerProfessionRuntimeManager
    /// 每個 Fusion Tick 呼叫。
    ///
    /// Runtime 本身負責把 Input
    /// 送進真正職業 Driver。
    /// </summary>
    public void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        if (driverBound == false)
        {
            if (TryBindRuntimeDriver() == false)
            {
                return;
            }
        }

        runtimeDriver.Simulate(
            input,
            previousButtons
        );
    }

    #endregion
}