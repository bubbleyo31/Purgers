using Fusion;
using UnityEngine;


/// <summary>
/// 所有敵人的共用 Root Facade。
///
/// ====================================================================
///
/// EnemyActor 不負責：
///
/// 搜尋玩家
/// 尋路
/// 位移
/// 攻擊
/// 防禦
/// 動畫
///
/// ====================================================================
///
/// 它只負責：
///
/// - 保存 EnemyDefinition。
/// - 集中提供共用核心元件。
/// - 驗證 Enemy Prefab 組合。
/// - 提供 IsAlive、名稱與類型等統一入口。
///
/// 後續系統只需要取得 EnemyActor，
/// 不必各自在 Hierarchy 反覆搜尋所有 Enemy Component。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(TestDamageReceiver))]
[RequireComponent(typeof(EnemyActionGate))]
[RequireComponent(typeof(EnemyStateController))]
public sealed class EnemyActor :
    NetworkBehaviour
{
    // =====================================================================
    #region Definition

    [Header("敵人資料")]

    [SerializeField]
    [Tooltip(
        "描述這個 Enemy Prefab 靜態身分的 EnemyDefinition。\n\n" +
        "近戰 A／近戰 B／遠程 A／遠程 B 必須各自建立一份 Definition。\n" +
        "不能在 Runtime 修改 Definition 內容。")]
    private EnemyDefinition definition;

    #endregion

    // =====================================================================
    #region Core References

    [Header("共用核心引用")]

    [SerializeField]
    [Tooltip(
        "敵人目前使用的既有生命系統。\n" +
        "若留空會自動取得同物件的 TestDamageReceiver。")]
    private TestDamageReceiver health;

    [SerializeField]
    [Tooltip(
        "敵人的三軸狀態控制器。\n" +
        "若留空會自動取得同物件的 EnemyStateController。")]
    private EnemyStateController stateController;

    [SerializeField]
    [Tooltip(
        "敵人的統一行為封鎖器。\n" +
        "若留空會自動取得同物件的 EnemyActionGate。")]
    private EnemyActionGate actionGate;

    [SerializeField]
    [Tooltip(
        "現有的死亡碰撞與目標停用控制器。\n\n" +
        "建議所有正式敵人都指定。若本階段尚未加入，可暫時留空，" +
        "但死亡後 Hitbox、FocusTarget 等物件不會自動關閉。")]
    private CombatDeathHandler combatDeathHandler;

    #endregion

    // =====================================================================
    #region Existing Combat Integration

    [Header("既有戰鬥整合")]

    [SerializeField]
    [Tooltip(
        "Attack Focus 可以選取的目標。\n" +
        "正式可戰鬥敵人建議指定；若留空會在同物件自動取得。")]
    private FocusTarget focusTarget;

    [SerializeField]
    [Tooltip(
        "職業鈎索辨識這個敵人的 Gameplay Target。\n" +
        "正式可互動敵人建議指定；若留空會在同物件自動取得。")]
    private GrappleInteractionTarget grappleTarget;

    [SerializeField]
    [Tooltip(
        "Attack 職業勾中敵人後使用的標記狀態。\n" +
        "如果這隻敵人允許 Attack Mark，建議指定；不允許時可以留空。")]
    private AttackGrappleMarkState attackMarkState;

    [SerializeField]
    [Tooltip(
        "Support 拉動敵人的既有接收器。\n" +
        "如果 GrappleInteractionTarget 允許 Support Pull，建議指定。")]
    private SupportGrapplePullReceiver supportPullReceiver;

    [SerializeField]
    [Tooltip(
        "Tank 聚怪移動的既有接收器。\n" +
        "如果 GrappleInteractionTarget 允許 Tank Gather，建議指定。")]
    private TankGatherMovementReceiver tankGatherReceiver;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後，EnemyActor Spawned 時輸出 Definition、權限與核心元件狀態。\n" +
        "大量生成敵人前建議關閉。")]
    private bool debugEnemyActor;

    #endregion

    // =====================================================================
    #region Runtime

    private bool fusionSpawned;

    #endregion

    // =====================================================================
    #region Public API

    public EnemyDefinition Definition =>
        definition;

    public string EnemyId =>
        definition != null
            ? definition.EnemyId
            : name;

    public string DisplayName =>
        definition != null
            ? definition.DisplayName
            : name;

    public TestDamageReceiver Health =>
        health;

    public EnemyStateController StateController =>
        stateController;

    public EnemyActionGate ActionGate =>
        actionGate;

    public FocusTarget FocusTarget =>
        focusTarget;

    public GrappleInteractionTarget GrappleTarget =>
        grappleTarget;

    public bool IsFusionSpawned =>
        fusionSpawned;

    public bool IsAlive =>
        fusionSpawned &&
        health != null &&
        health.IsAlive;

    public bool IsStateAuthorityOwner =>
        fusionSpawned &&
        Object != null &&
        Object.HasStateAuthority;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    #endregion

    // =====================================================================
    #region Fusion Lifecycle

    public override void Spawned()
    {
        fusionSpawned =
            true;

        ResolveReferences();
        ValidateRuntimeConfiguration();

        if (debugEnemyActor)
        {
            Debug.Log(
                $"[{nameof(EnemyActor)}] Enemy 已生成。" +
                $"\nID：{EnemyId}" +
                $"\nDisplay Name：{DisplayName}" +
                $"\nState Authority：{Object.HasStateAuthority}" +
                $"\nInput Authority：{Object.InputAuthority}" +
                $"\nHealth：{(health != null)}" +
                $"\nState Controller：{(stateController != null)}" +
                $"\nAction Gate：{(actionGate != null)}",
                this
            );
        }
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        fusionSpawned =
            false;
    }

    #endregion

    // =====================================================================
    #region Setup

    private void ResolveReferences()
    {
        if (health == null)
        {
            health =
                GetComponent<TestDamageReceiver>();
        }

        if (stateController == null)
        {
            stateController =
                GetComponent<EnemyStateController>();
        }

        if (actionGate == null)
        {
            actionGate =
                GetComponent<EnemyActionGate>();
        }

        if (combatDeathHandler == null)
        {
            combatDeathHandler =
                GetComponent<CombatDeathHandler>();
        }

        if (focusTarget == null)
        {
            focusTarget =
                GetComponent<FocusTarget>();
        }

        if (grappleTarget == null)
        {
            grappleTarget =
                GetComponent<GrappleInteractionTarget>();
        }

        if (attackMarkState == null)
        {
            attackMarkState =
                GetComponent<AttackGrappleMarkState>();
        }

        if (supportPullReceiver == null)
        {
            supportPullReceiver =
                GetComponent<SupportGrapplePullReceiver>();
        }

        if (tankGatherReceiver == null)
        {
            tankGatherReceiver =
                GetComponent<TankGatherMovementReceiver>();
        }
    }

    private void ValidateRuntimeConfiguration()
    {
        if (definition == null)
        {
            Debug.LogError(
                $"[{nameof(EnemyActor)}] " +
                "尚未指定 Enemy Definition。" +
                $"\nObject：{name}",
                this
            );
        }

        if (health == null ||
            stateController == null ||
            actionGate == null)
        {
            Debug.LogError(
                $"[{nameof(EnemyActor)}] " +
                "缺少必要 Enemy Core Component。" +
                $"\nHealth：{(health != null)}" +
                $"\nState Controller：{(stateController != null)}" +
                $"\nAction Gate：{(actionGate != null)}",
                this
            );
        }

        if (focusTarget == null)
        {
            Debug.LogWarning(
                $"[{nameof(EnemyActor)}] " +
                "沒有 FocusTarget，Attack Focus 將無法鎖定這個敵人。" +
                $"\nObject：{name}",
                this
            );
        }

        if (grappleTarget == null)
        {
            Debug.LogWarning(
                $"[{nameof(EnemyActor)}] " +
                "沒有 GrappleInteractionTarget，職業鈎索不會把它辨識為敵人。" +
                $"\nObject：{name}",
                this
            );
        }
    }

    #endregion
}
