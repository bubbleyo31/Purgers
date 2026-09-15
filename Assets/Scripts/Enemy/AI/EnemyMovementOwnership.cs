using Fusion;
using UnityEngine;

/// <summary>
/// 新 AI 共用的移動控制權檢查。既有 Support／Tank 接收器尚未寫入 ActionGate，
/// 所以必須同時查詢它們，避免新巡邏與鈎索在同一 Tick 搶寫 Transform。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor))]
public sealed class EnemyMovementOwnership : MonoBehaviour
{
    private EnemyActor actor;
    private SupportGrapplePullReceiver supportPull;
    private TankGatherMovementReceiver tankGather;

    public bool IsExternallyMoved =>
        (supportPull != null && supportPull.IsPullActive) ||
        (tankGather != null && tankGather.IsBeingGathered) ||
        (actor != null && actor.IsFusionSpawned && actor.Object != null && actor.Object.IsValid &&
         ((actor.StateController != null && actor.StateController.IsFusionSpawned &&
           actor.StateController.CurrentControlState == EnemyControlState.ExternalMovement) ||
          (actor.ActionGate != null && actor.ActionGate.IsFusionSpawned &&
           actor.ActionGate.GetLocks(EnemyActionLockSource.ExternalMovement) != EnemyActionLockFlags.None)));

    public bool CanMove => Ready && !IsExternallyMoved &&
        actor.StateController.CanRunBrain &&
        !actor.StateController.IsUsingAction &&
        actor.ActionGate.CanMove && actor.ActionGate.CanNavigate;

    public bool CanRotate => Ready && !IsExternallyMoved &&
        actor.StateController.CanRunBrain &&
        !actor.StateController.IsUsingAction && actor.ActionGate.CanRotate;

    private bool Ready => actor != null && actor.IsFusionSpawned &&
        actor.Object != null && actor.Object.IsValid && actor.IsAlive &&
        actor.StateController != null && actor.StateController.IsFusionSpawned &&
        actor.ActionGate != null && actor.ActionGate.IsFusionSpawned;

    private void Awake()
    {
        actor = GetComponent<EnemyActor>();
        supportPull = GetComponent<SupportGrapplePullReceiver>();
        tankGather = GetComponent<TankGatherMovementReceiver>();
    }
}
