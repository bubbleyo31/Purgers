using Fusion;
using System;
using UnityEngine;


/// <summary>
/// 敵人可以被封鎖的行為類型。
///
/// Flags 允許同一來源一次封鎖多種行為。
/// </summary>
[Flags]
public enum EnemyActionLockFlags : ushort
{
    None = 0,

    Movement = 1 << 0,
    Rotation = 1 << 1,
    Navigation = 1 << 2,
    Attack = 1 << 3,
    Defense = 1 << 4,

    All =
        Movement |
        Rotation |
        Navigation |
        Attack |
        Defense
}


/// <summary>
/// 哪一個系統正在提供 Enemy Action Lock。
///
/// 每個來源擁有自己的 Networked Slot，
/// 不會因另一個系統解除 Lock 而誤清掉別人的限制。
/// </summary>
public enum EnemyActionLockSource : byte
{
    Brain = 0,
    Ability = 1,
    Status = 2,
    ExternalMovement = 3,
    Death = 4
}


/// <summary>
/// 敵人的統一行為封鎖器。
///
/// ====================================================================
///
/// 典型問題：
///
/// Support Pull 封鎖移動
/// ＋
/// Attack Ability 封鎖旋轉
///
/// 如果兩者共用一個 Bool，任何一方解除時都可能把另一方的封鎖清掉。
///
/// 本控制器替每個來源保存獨立 Flags，最後再合併成 Effective Locks。
///
/// ====================================================================
///
/// 正式 Lock 只能由 State Authority 修改。
/// Client Proxy 只讀取同步結果，供動畫與 Presentation 判斷。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyActionGate :
    NetworkBehaviour
{
    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後，每次 Lock Source 的內容真正改變時輸出來源與合併結果。\n" +
        "能力系統完成驗證後建議關閉，避免大量敵人產生過多 Log。")]
    private bool debugActionLocks;

    [Networked]
    private EnemyActionLockFlags BrainLocks
    {
        get;
        set;
    }

    [Networked]
    private EnemyActionLockFlags AbilityLocks
    {
        get;
        set;
    }

    [Networked]
    private EnemyActionLockFlags StatusLocks
    {
        get;
        set;
    }

    [Networked]
    private EnemyActionLockFlags ExternalMovementLocks
    {
        get;
        set;
    }

    [Networked]
    private EnemyActionLockFlags DeathLocks
    {
        get;
        set;
    }

    private bool fusionSpawned;

    public EnemyActionLockFlags EffectiveLocks
    {
        get
        {
            if (fusionSpawned == false)
            {
                return EnemyActionLockFlags.All;
            }

            return
                BrainLocks |
                AbilityLocks |
                StatusLocks |
                ExternalMovementLocks |
                DeathLocks;
        }
    }

    public bool CanMove =>
        IsAllowed(
            EnemyActionLockFlags.Movement
        );

    public bool CanRotate =>
        IsAllowed(
            EnemyActionLockFlags.Rotation
        );

    public bool CanNavigate =>
        IsAllowed(
            EnemyActionLockFlags.Navigation
        );

    public bool CanAttack =>
        IsAllowed(
            EnemyActionLockFlags.Attack
        );

    public bool CanDefend =>
        IsAllowed(
            EnemyActionLockFlags.Defense
        );

    public bool IsFusionSpawned =>
        fusionSpawned;

    public override void Spawned()
    {
        fusionSpawned =
            true;

        if (Object.HasStateAuthority)
        {
            BrainLocks =
                EnemyActionLockFlags.None;

            AbilityLocks =
                EnemyActionLockFlags.None;

            StatusLocks =
                EnemyActionLockFlags.None;

            ExternalMovementLocks =
                EnemyActionLockFlags.None;

            DeathLocks =
                EnemyActionLockFlags.None;
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

    /// <summary>
    /// 設定指定來源目前提供的完整 Lock Flags。
    ///
    /// 注意：這是覆寫該來源，不是 Add。
    /// 一個模組應該只修改自己被分配的 Source。
    /// </summary>
    public bool SetLocks(
        EnemyActionLockSource source,
        EnemyActionLockFlags locks
    )
    {
        if (CanWriteNetworkState() ==
            false)
        {
            return false;
        }

        EnemyActionLockFlags previous =
            GetLocksInternal(
                source
            );

        if (previous == locks)
        {
            return true;
        }

        switch (source)
        {
            case EnemyActionLockSource.Brain:
                BrainLocks = locks;
                break;

            case EnemyActionLockSource.Ability:
                AbilityLocks = locks;
                break;

            case EnemyActionLockSource.Status:
                StatusLocks = locks;
                break;

            case EnemyActionLockSource.ExternalMovement:
                ExternalMovementLocks = locks;
                break;

            case EnemyActionLockSource.Death:
                DeathLocks = locks;
                break;

            default:
                Debug.LogError(
                    $"[{nameof(EnemyActionGate)}] " +
                    $"未知 Lock Source：{source}",
                    this
                );

                return false;
        }

        if (debugActionLocks)
        {
            Debug.Log(
                $"[{nameof(EnemyActionGate)}] Lock 已更新。" +
                $"\nEnemy：{name}" +
                $"\nSource：{source}" +
                $"\nPrevious：{previous}" +
                $"\nCurrent：{locks}" +
                $"\nEffective：{EffectiveLocks}",
                this
            );
        }

        return true;
    }

    /// <summary>
    /// 只清除指定來源的 Lock，不影響其他來源。
    /// </summary>
    public bool ClearLocks(
        EnemyActionLockSource source
    )
    {
        return SetLocks(
            source,
            EnemyActionLockFlags.None
        );
    }

    /// <summary>
    /// 取得指定來源目前的 Lock。
    /// </summary>
    public EnemyActionLockFlags GetLocks(
        EnemyActionLockSource source
    )
    {
        if (fusionSpawned == false)
        {
            return EnemyActionLockFlags.All;
        }

        return GetLocksInternal(
            source
        );
    }

    public bool IsLocked(
        EnemyActionLockFlags requestedAction
    )
    {
        if (fusionSpawned == false)
        {
            return true;
        }

        return
            (EffectiveLocks & requestedAction) !=
            EnemyActionLockFlags.None;
    }

    private bool IsAllowed(
        EnemyActionLockFlags requestedAction
    )
    {
        return IsLocked(
                   requestedAction
               ) == false;
    }

    private EnemyActionLockFlags GetLocksInternal(
        EnemyActionLockSource source
    )
    {
        switch (source)
        {
            case EnemyActionLockSource.Brain:
                return BrainLocks;

            case EnemyActionLockSource.Ability:
                return AbilityLocks;

            case EnemyActionLockSource.Status:
                return StatusLocks;

            case EnemyActionLockSource.ExternalMovement:
                return ExternalMovementLocks;

            case EnemyActionLockSource.Death:
                return DeathLocks;

            default:
                return EnemyActionLockFlags.All;
        }
    }

    private bool CanWriteNetworkState()
    {
        return
            fusionSpawned &&
            Object != null &&
            Object.HasStateAuthority;
    }
}
