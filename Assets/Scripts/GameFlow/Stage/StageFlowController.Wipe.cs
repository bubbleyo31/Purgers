using System.Collections.Generic;
using Fusion;
using Purgers.GameFlow.Transition;
using UnityEngine;

namespace Purgers.GameFlow.Stage
{
    public sealed partial class StageFlowController
    {
        [Networked] public NetworkBool WipeTransitionPending { get; private set; }
        private readonly HashSet<PlayerRef> wipeReadyPlayers = new HashSet<PlayerRef>();
        private LocalSceneTransition wipePresentation;
        private float nextWipeReportTime;
        private bool wipeSpawned;
        private void RenderWipeTransition()
        {
            if (!wipeSpawned || !IsNetworkReady) return;
            if (Phase == StagePhase.CommitFailed) { CancelWipePresentation(); return; }
            if (!WipeTransitionPending || Phase != StagePhase.Failed || !Runner.LocalPlayer.IsRealPlayer) return;
            wipePresentation = LocalSceneTransition.Ensure(Runner);
            wipePresentation.BeginWipe();
            if (!wipePresentation.IsWipeReadyToLoad || Time.unscaledTime < nextWipeReportTime) return;
            nextWipeReportTime = Time.unscaledTime + .5f;
            if (Object.HasStateAuthority) AcceptWipeReady(Runner.LocalPlayer);
            else RPC_ReportWipeReady();
        }
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable, TickAligned = false)]
        private void RPC_ReportWipeReady(RpcInfo info = default) => AcceptWipeReady(info.Source);
        private void AcceptWipeReady(PlayerRef player)
        {
            if (!wipeSpawned || !IsNetworkReady || !Object.HasStateAuthority || !Runner.IsServer ||
                !WipeTransitionPending || Phase != StagePhase.Failed || !player.IsRealPlayer || !Runner.IsPlayerValid(player)) return;
            wipeReadyPlayers.Add(player);
        }
        private void TickWipeTransition()
        {
            if (!WipeTransitionPending || Phase != StagePhase.Failed) return;
            int count = 0;
            foreach (var player in Runner.ActivePlayers)
            {
                count++;
                if (!wipeReadyPlayers.Contains(player)) return;
            }
            if (count > 0) BeginSafeHouseLoad();
        }
        private void CancelWipePresentation()
        {
            if (wipePresentation != null) wipePresentation.Cancel();
            wipePresentation = null;
        }
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            wipeSpawned = false;
            // 正常切場由 Runner 上的 LocalSceneTransition 接手；不可在舊 Stage 卸載時解除黑幕。
            if (wipePresentation != null && wipePresentation.IsWipePreparing && !runner.IsSceneManagerBusy)
                CancelWipePresentation();
        }
    }
}