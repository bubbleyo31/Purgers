using Fusion;
using UnityEngine;


/// <summary>
/// 敵人的高階思考狀態。
///
/// 這裡不放 MeleeAttackA、DefenseB 等具體能力名稱，
/// 避免每新增一個怪物能力就必須修改共用 Brain Enum。
/// </summary>
public enum EnemyBrainState : byte
{
    Dormant = 0,
    Idle = 1,
    Patrol = 2,
    Alert = 3,
    Chase = 4,
    Combat = 5,
    Investigate = 6,
    Return = 7,
    Dead = 8
}


/// <summary>
/// 敵人目前的高階動作分類。
///
/// 具體是哪一個攻擊／防禦，之後由 EnemyAbilityController 的 Ability ID 表示。
/// </summary>
public enum EnemyActionState : byte
{
    None = 0,
    Attack = 1,
    Defense = 2,
    Special = 3
}


/// <summary>
/// 敵人目前受到的控制狀態。
///
/// Brain、Action 與 Control 分開，避免產生
/// ChasingWhileStunned、AttackingWhilePulled 等無限組合狀態。
/// </summary>
public enum EnemyControlState : byte
{
    Normal = 0,
    Stunned = 1,
    Rooted = 2,
    ExternalMovement = 3,
    Dead = 4
}


/// <summary>
/// 敵人的共用 Networked State Controller。
///
/// ====================================================================
///
/// 三條狀態軸：
///
/// Brain：現在想做什麼。
/// Action：現在是否正在執行能力。
/// Control：是否被暈眩、拉動或死亡等更高層規則控制。
///
/// ====================================================================
///
/// 只有 State Authority 可以改狀態。
/// Proxy 只讀取同步結果，供動畫、音效與 VFX 使用。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(TestDamageReceiver))]
[RequireComponent(typeof(EnemyActionGate))]
public sealed class EnemyStateController :
    NetworkBehaviour
{
    // =====================================================================
    #region References

    [Header("核心引用")]

    [SerializeField]
    [Tooltip(
        "目前敵人的生命狀態來源。\n\n" +
        "本階段沿用專案既有、已完成 Damage／Heal／Death／Revive 的 TestDamageReceiver，" +
        "避免同時建立第二套平行生命系統。\n" +
        "若留空會自動取得同物件元件。")]
    private TestDamageReceiver health;

    [SerializeField]
    [Tooltip(
        "敵人的統一行為封鎖器。\n" +
        "死亡時會透過 Death Source 封鎖移動、旋轉、導航、攻擊與防禦。\n" +
        "若留空會自動取得同物件元件。")]
    private EnemyActionGate actionGate;

    #endregion

    // =====================================================================
    #region Initial State

    [Header("初始狀態")]

    [SerializeField]
    [Tooltip(
        "敵人 NetworkObject 生成後的初始 Brain State。\n\n" +
        "一般場景敵人使用 Idle。\n" +
        "若要等 Encounter、演出或生成動畫啟動，可以使用 Dormant。\n" +
        "不可設定 Dead；死亡狀態由 Health 自動控制。")]
    private EnemyBrainState initialBrainState =
        EnemyBrainState.Idle;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出 Brain、Action 與 Control 狀態變更。\n" +
        "大量敵人測試時建議關閉。")]
    private bool debugStateChanges;

    #endregion

    // =====================================================================
    #region Networked State

    [Networked]
    public EnemyBrainState CurrentBrainState
    {
        get;
        private set;
    }

    [Networked]
    public EnemyActionState CurrentActionState
    {
        get;
        private set;
    }

    [Networked]
    public EnemyControlState CurrentControlState
    {
        get;
        private set;
    }

    /// <summary>
    /// Brain State 每次真正改變時加一。
    ///
    /// 未來 Animator 即使連續兩次進入同類動畫流程，
    /// 也可以透過 Sequence 判斷這是新的一次狀態事件。
    /// </summary>
    [Networked]
    public int BrainStateSequence
    {
        get;
        private set;
    }

    [Networked]
    public int ActionStateSequence
    {
        get;
        private set;
    }

    [Networked]
    public int ControlStateSequence
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Runtime

    private bool fusionSpawned;

    #endregion

    // =====================================================================
    #region Public State

    public bool IsFusionSpawned =>
        fusionSpawned;

    public bool IsAlive =>
        fusionSpawned &&
        health != null &&
        health.IsAlive;

    public bool CanRunBrain =>
        IsAlive &&
        CurrentControlState ==
            EnemyControlState.Normal;

    public bool IsUsingAction =>
        fusionSpawned &&
        CurrentActionState !=
            EnemyActionState.None;

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

        if (initialBrainState ==
            EnemyBrainState.Dead)
        {
            initialBrainState =
                EnemyBrainState.Idle;
        }
    }

    #endregion

    // =====================================================================
    #region Fusion Lifecycle

    public override void Spawned()
    {
        fusionSpawned =
            true;

        ResolveReferences();

        if (Object.HasStateAuthority)
        {
            CurrentBrainState =
                initialBrainState == EnemyBrainState.Dead
                    ? EnemyBrainState.Idle
                    : initialBrainState;

            CurrentActionState =
                EnemyActionState.None;

            CurrentControlState =
                EnemyControlState.Normal;

            BrainStateSequence =
                1;

            ActionStateSequence =
                1;

            ControlStateSequence =
                1;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority == false ||
            health == null)
        {
            return;
        }

        SynchronizeDeathState();
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
    #region State Authority API

    /// <summary>
    /// 修改 Brain State。
    ///
    /// Dead 只能由生命狀態自動進入，外部模組不可手動假裝死亡。
    /// </summary>
    public bool TrySetBrainState(
        EnemyBrainState newState
    )
    {
        if (CanWriteState() == false ||
            newState == EnemyBrainState.Dead ||
            health == null ||
            health.IsDead)
        {
            return false;
        }

        if (CurrentBrainState ==
            newState)
        {
            return true;
        }

        EnemyBrainState previous =
            CurrentBrainState;

        CurrentBrainState =
            newState;

        BrainStateSequence++;

        LogStateChange(
            "Brain",
            previous.ToString(),
            newState.ToString()
        );

        return true;
    }

    /// <summary>
    /// 修改目前高階 Action State。
    ///
    /// 具體能力開始前仍必須先檢查 EnemyActionGate。
    /// </summary>
    public bool TrySetActionState(
        EnemyActionState newState
    )
    {
        if (CanWriteState() == false ||
            health == null ||
            health.IsDead)
        {
            return false;
        }

        if (CurrentActionState ==
            newState)
        {
            return true;
        }

        EnemyActionState previous =
            CurrentActionState;

        CurrentActionState =
            newState;

        ActionStateSequence++;

        LogStateChange(
            "Action",
            previous.ToString(),
            newState.ToString()
        );

        return true;
    }

    /// <summary>
    /// 修改控制狀態。
    ///
    /// Dead 仍然只由 Health 控制。
    /// Status／External Movement 模組改變狀態時，
    /// 還必須同步設定自己在 EnemyActionGate 中的 Lock Source。
    /// </summary>
    public bool TrySetControlState(
        EnemyControlState newState
    )
    {
        if (CanWriteState() == false ||
            newState == EnemyControlState.Dead ||
            health == null ||
            health.IsDead)
        {
            return false;
        }

        if (CurrentControlState ==
            newState)
        {
            return true;
        }

        EnemyControlState previous =
            CurrentControlState;

        CurrentControlState =
            newState;

        ControlStateSequence++;

        LogStateChange(
            "Control",
            previous.ToString(),
            newState.ToString()
        );

        return true;
    }

    #endregion

    // =====================================================================
    #region Death Synchronization

    private void SynchronizeDeathState()
    {
        if (health.IsDead)
        {
            ApplyDeadState();
            return;
        }

        if (CurrentBrainState ==
                EnemyBrainState.Dead ||
            CurrentControlState ==
                EnemyControlState.Dead)
        {
            ApplyRevivedState();
        }
    }

    private void ApplyDeadState()
    {
        if (CurrentBrainState !=
            EnemyBrainState.Dead)
        {
            EnemyBrainState previous =
                CurrentBrainState;

            CurrentBrainState =
                EnemyBrainState.Dead;

            BrainStateSequence++;

            LogStateChange(
                "Brain",
                previous.ToString(),
                EnemyBrainState.Dead.ToString()
            );
        }

        if (CurrentActionState !=
            EnemyActionState.None)
        {
            EnemyActionState previous =
                CurrentActionState;

            CurrentActionState =
                EnemyActionState.None;

            ActionStateSequence++;

            LogStateChange(
                "Action",
                previous.ToString(),
                EnemyActionState.None.ToString()
            );
        }

        if (CurrentControlState !=
            EnemyControlState.Dead)
        {
            EnemyControlState previous =
                CurrentControlState;

            CurrentControlState =
                EnemyControlState.Dead;

            ControlStateSequence++;

            LogStateChange(
                "Control",
                previous.ToString(),
                EnemyControlState.Dead.ToString()
            );
        }

        if (actionGate != null &&
            actionGate.IsFusionSpawned)
        {
            actionGate.SetLocks(
                EnemyActionLockSource.Death,
                EnemyActionLockFlags.All
            );
        }
    }

    private void ApplyRevivedState()
    {
        EnemyBrainState previousBrain =
            CurrentBrainState;

        EnemyActionState previousAction =
            CurrentActionState;

        EnemyControlState previousControl =
            CurrentControlState;

        CurrentBrainState =
            initialBrainState == EnemyBrainState.Dead
                ? EnemyBrainState.Idle
                : initialBrainState;

        CurrentActionState =
            EnemyActionState.None;

        CurrentControlState =
            EnemyControlState.Normal;

        BrainStateSequence++;
        ActionStateSequence++;
        ControlStateSequence++;

        if (actionGate != null &&
            actionGate.IsFusionSpawned)
        {
            actionGate.ClearLocks(
                EnemyActionLockSource.Death
            );
        }

        LogStateChange(
            "Revive Brain",
            previousBrain.ToString(),
            CurrentBrainState.ToString()
        );

        LogStateChange(
            "Revive Action",
            previousAction.ToString(),
            CurrentActionState.ToString()
        );

        LogStateChange(
            "Revive Control",
            previousControl.ToString(),
            CurrentControlState.ToString()
        );
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

        if (actionGate == null)
        {
            actionGate =
                GetComponent<EnemyActionGate>();
        }
    }

    private bool CanWriteState()
    {
        return
            fusionSpawned &&
            Object != null &&
            Object.HasStateAuthority;
    }

    private void LogStateChange(
        string stateType,
        string previous,
        string current
    )
    {
        if (debugStateChanges == false)
        {
            return;
        }

        Debug.Log(
            $"[{nameof(EnemyStateController)}] {stateType} 已改變。" +
            $"\nEnemy：{name}" +
            $"\n{previous} → {current}",
            this
        );
    }

    #endregion
}
