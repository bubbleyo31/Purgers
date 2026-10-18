using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.Transition;
using Purgers.Settings;

public static class SpectatorSelectionRules
{
    public static bool CanObserveLife(bool dead, bool downed) => !dead;
    public static int NextIndex(int count, int current, int direction)
    {
        if (count <= 0) return -1;
        if (current < 0 || current >= count) return direction < 0 ? count - 1 : 0;
        return (current + (direction < 0 ? -1 : 1) + count) % count;
    }
}

/// <summary>同一份名單供本機相機與 UI 使用；固定 Q/E 不經 Gameplay NetInput。</summary>
public partial class LocalPlayerSpectatorController
{
    private readonly List<PlayerRef> candidates = new List<PlayerRef>();
    private int lastTargetSlot = -1;
    private bool switchKeysArmed;
    public NetworkRunner Runner => runner;
    public IReadOnlyList<PlayerRef> Candidates => candidates;
    public static LocalPlayerSpectatorController GetForRunner(NetworkRunner owner)
    {
        foreach (var controller in activeControllers)
            if (controller != null && controller.isActiveAndEnabled && controller.runner == owner) return controller;
        return null;
    }
    public bool IsTransitionBlocked => runner != null && runner.TryGetComponent<LocalSceneTransition>(out var transition) && transition.IsTransitioning;
    public bool CanSwitch => spectatorActive && !IsTransitionBlocked && !PlayerSettingsMenu.IsOpen && !LocalTeamInfoPanel.IsOpenFor(runner) &&
        !LocalPlayerControl.AllInputBlocked && !PlayerControlLocks.BlocksGameplay(LocalPlayerControl.Locks.Mask);

    private int Slot(PlayerRef player)
    {
        var logic = GameLogic.GetPrimaryForRunner(runner);
        return logic != null && logic.TryGetTeammateIdentity(player, out var identity) ? identity.ColorSlot : 1000 + player.PlayerId;
    }
    private bool IsConnectedCandidate(PlayerRef player)
    {
        // Fusion IsPlayerValid 在 Client 不等同完整遠端名單，必須查同 Runner ActivePlayers。
        if (runner == null || !runner.IsRunning) return false;
        foreach (var active in runner.ActivePlayers) if (active == player) return true;
        return false;
    }
    private bool TryGetCandidate(PlayerRef player, out Player candidate)
    {
        candidate = null;
        if (runner == null || !runner.IsRunning || player == runner.LocalPlayer || !IsConnectedCandidate(player) ||
            !runner.TryGetPlayerObject(player, out var obj) || obj == null || !obj.IsValid || obj.Runner != runner || obj.InputAuthority != player)
            return false;
        candidate = obj.GetComponent<Player>();
        return candidate != null && candidate.Health != null && candidate.Health.IsRescueReady &&
            SpectatorSelectionRules.CanObserveLife(candidate.Health.IsDead, candidate.Health.IsDowned) &&
            candidate.Movement != null && candidate.Movement.CamTarget != null;
    }
    private void RefreshCandidates()
    {
        candidates.Clear();
        if (runner == null || !runner.IsRunning) return;
        foreach (var player in runner.ActivePlayers)
            if (TryGetCandidate(player, out _)) candidates.Add(player);
        candidates.Sort((a, b) => { int order = Slot(a).CompareTo(Slot(b)); return order != 0 ? order : a.PlayerId.CompareTo(b.PlayerId); });
    }
    private bool SelectTarget(PlayerRef player)
    {
        if (!TryGetCandidate(player, out var candidate)) return false;
        if (spectatedPlayer == player && IsCurrentSpectatorTargetValid()) return true;
        ClearSpectatorTarget();
        spectatedPlayer = player;
        spectatedPlayerObject = candidate.Object;
        spectatedPlayerBehaviour = candidate;
        spectatedMovement = candidate.Movement;
        lastTargetSlot = Slot(player);
        loggedNoSpectatorTarget = false;
        if (CameraFollow.Singleton != null) UpdateFirstPersonPresentation();
        return true;
    }
    /// <summary>本機唯一切換入口；UI 或測試同樣必須通過觀戰與控制鎖。</summary>
    public bool SwitchTarget(int direction)
    {
        if (!CanSwitch || direction == 0) return false;
        RefreshCandidates();
        int index = SpectatorSelectionRules.NextIndex(candidates.Count, candidates.IndexOf(spectatedPlayer), direction);
        return index >= 0 && SelectTarget(candidates[index]);
    }
    private void UpdateSpectatorSelection()
    {
        RefreshCandidates();
        if (IsTransitionBlocked) { switchKeysArmed = false; return; }
        if (!IsCurrentSpectatorTargetValid())
        {
            ClearSpectatorTarget();
            TryAcquireSpectatorTarget();
        }
        bool q = Input.GetKey(KeyCode.Q), e = Input.GetKey(KeyCode.E);
        if (!CanSwitch) { switchKeysArmed = false; return; }
        if (!q && !e) switchKeysArmed = true;
        if (!switchKeysArmed || q && e) return;
        bool previous = Input.GetKeyDown(KeyCode.Q), next = Input.GetKeyDown(KeyCode.E);
        if (previous != next) SwitchTarget(previous ? -1 : 1);
    }
}
