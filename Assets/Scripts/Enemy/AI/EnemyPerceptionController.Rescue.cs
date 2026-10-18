using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed partial class EnemyPerceptionController
{
    // 僅 Host 的既有感知 owner 持有，沒有第二套網路仇恨或玩家無敵旗標。
    private readonly List<NetworkObject> previousDownedTargets = new List<NetworkObject>();
    private EnemyCombatOption[] rescueCombatOptions;

    public void RememberDownedTarget(NetworkObject target)
    {
        if (!IsFusionSpawned || !HasStateAuthority || CurrentTarget != target) return;
        if (!previousDownedTargets.Contains(target)) previousDownedTargets.Add(target);
        ForgetTarget(true);
    }

    private void TickPreviousRescueTargets()
    {
        for (int i = previousDownedTargets.Count - 1; i >= 0; i--)
        {
            var target = previousDownedTargets[i];
            if (target == null || !target.IsValid || target.Runner != Runner ||
                !Runner.TryGetPlayerObject(target.InputAuthority, out var current) || current != target ||
                !target.TryGetComponent<PlayerHealth>(out var health) || health.IsDead)
            { previousDownedTargets.RemoveAt(i); continue; }
            if (health.IsDowned) continue;
            if (CurrentTarget == target) { previousDownedTargets.RemoveAt(i); continue; }
            Vector3 position = GetPlayerObservationPosition(target);
            bool detected = occlusionMask.value != 0 &&
                (CanSee(target, position, out _) || IsInsideRescueAttackDistance(target, position));
            if (!detected && !health.PreviousAggroRecoveryTimer.ExpiredOrNotRunning(Runner)) continue;
            SetTarget(target, detected, position);
            previousDownedTargets.RemoveAt(i);
        }
    }

    private bool IsInsideRescueAttackDistance(NetworkObject target, Vector3 position)
    {
        if (!HasClearLineOfSight(target, position)) return false;
        if (rescueCombatOptions == null) rescueCombatOptions = GetComponents<EnemyCombatOption>();
        float distance = Vector3.Distance(transform.position, target.transform.position);
        foreach (var option in rescueCombatOptions)
            if (option != null && option.ActionState == EnemyActionState.Attack &&
                distance >= option.MinimumStartDistance && distance <= option.MaximumStartDistance) return true;
        return false;
    }
}
