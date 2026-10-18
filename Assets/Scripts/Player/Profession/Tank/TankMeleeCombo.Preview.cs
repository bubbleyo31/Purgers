using Fusion;
using Purgers.GameFlow.Control;
using UnityEngine;

public partial class TankMeleeCombo
{
    [Header("本機預備距離圈與目標框")]
    [SerializeField] private TankPreparedDashIndicatorSettings preparedDashIndicator = new TankPreparedDashIndicatorSettings();
    private LocalTankPreparedDashIndicator localPreparedDashIndicator;
    private bool previewRenderReady;

    public TankPreparedDashIndicatorSettings PreparedDashIndicatorSettings => preparedDashIndicator;

    /// <summary>Fusion Render 保證已進入 Spawned 生命週期；只在真正操作此玩家的端建立純本機呈現。</summary>
    public override void Render()
    {
        previewRenderReady = true;
        if (ownerPlayer == null || ownerPlayer.Object == null || !ownerPlayer.Object.IsValid ||
            !ownerPlayer.Object.HasInputAuthority || Runner == null || !Runner.IsRunning ||
            !Runner.LocalPlayer.IsRealPlayer || ownerPlayer.Object.InputAuthority != Runner.LocalPlayer ||
            !Runner.TryGetPlayerObject(Runner.LocalPlayer, out var registered) || registered != ownerPlayer.Object) return;
        if (localPreparedDashIndicator == null)
        {
            localPreparedDashIndicator = gameObject.AddComponent<LocalTankPreparedDashIndicator>();
            localPreparedDashIndicator.Initialize(this);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        previewRenderReady = false;
        if (localPreparedDashIndicator != null)
        {
            localPreparedDashIndicator.Shutdown();
            Destroy(localPreparedDashIndicator);
            localPreparedDashIndicator = null;
        }
    }

    /// <summary>只讀當前本機資料；不寫 Networked、不選權威目標，也不呼叫 TryStartPreparedDash。</summary>
    public bool TryGetPreparedDashPreview(out TankPreparedDashPreviewFrame frame)
    {
        frame = default;
        if (!previewRenderReady || !isActiveAndEnabled || preparedDashIndicator == null || !preparedDashIndicator.enabled ||
            Object == null || !Object.IsValid || ownerPlayer == null || ownerPlayer.Object == null || !ownerPlayer.Object.IsValid ||
            ownerMovement == null || ownerMovement.KCC == null || Runner == null || !Runner.IsRunning || Runner.IsSceneManagerBusy)
            return false;
        bool own = ownerPlayer.Object.HasInputAuthority && Runner.LocalPlayer.IsRealPlayer && ownerPlayer.Object.InputAuthority == Runner.LocalPlayer &&
            Runner.TryGetPlayerObject(Runner.LocalPlayer, out var registered) && registered == ownerPlayer.Object;
        var manager = ownerPlayer.ProfessionRuntimeManager;
        bool current = manager != null && manager.Object != null && manager.Object.IsValid && manager.CurrentRuntimeObject == Object;
        var gate = ownerPlayer.GetComponent<PlayerActionGate>();
        bool blocked = PreparedDashDisplaced() || (gate != null && gate.IsBlocked(PlayerActionBlockMask.Movement)) ||
            PlayerControlLocks.BlocksCombat(LocalPlayerControl.Locks.Mask) ||
            (LocalPlayerControl.Locks.Mask & PlayerControlMask.Movement) != 0;
        if (!TankPreparedDashIndicatorRules.CanPresent(own, ownerPlayer.Health != null && ownerPlayer.Health.IsAlive, current, blocked)) return false;
        frame = new TankPreparedDashPreviewFrame
        {
            Owner = ownerPlayer, Preparing = enablePreparedDash && CanCancelPreparation,
            Frozen = enablePreparedDash && IsPreparing && Preparation.IsHolding && !Preparation.Released,
            Grounded = ownerMovement.IsGrounded, Position = ownerMovement.KCC.Data.TargetPosition,
            Direction = ownerMovement.GetAimDirection(), Range = preparedDashRange, HalfAngle = preparedDashAimHalfAngle,
            AimHeight = preparedDashAimHeight, StopDistance = preparedDashStopDistance,
            EnemyMask = preparedDashEnemyMask, ObstructionMask = preparedDashObstructionMask,
            GroundMask = ownerMovement.KCC.Settings.CollisionLayerMask
        };
        return true;
    }
}

/// <summary>當幀唯讀視覺快照；不是 RPC／網路狀態。</summary>
public struct TankPreparedDashPreviewFrame
{
    public Player Owner;
    public bool Preparing, Frozen, Grounded;
    public Vector3 Position, Direction;
    public float Range, HalfAngle, AimHeight, StopDistance;
    public LayerMask EnemyMask, ObstructionMask, GroundMask;
}
