using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 同 Runner 的有效子彈時間查詢索引。正式名單與期限在各技能 Networked 狀態；
/// 此處不保存第二份時鐘或敵人狀態，也不使用全域 Time.timeScale。
/// </summary>
public static class PlayerBulletTimeSessionRegistry
{
    private static readonly List<PlayerBulletTimeAbility> sessions = new List<PlayerBulletTimeAbility>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => sessions.Clear();

    internal static void Register(PlayerBulletTimeAbility session)
    { if (session != null && !sessions.Contains(session)) sessions.Add(session); }

    internal static void Unregister(PlayerBulletTimeAbility session) => sessions.Remove(session);

    public static bool IsEnemyFrozen(EnemyActor enemy)
    {
        if (enemy == null || !enemy.IsFusionSpawned || enemy.Object == null || !enemy.Object.IsValid)
            return false;
        for (int i = 0; i < sessions.Count; i++)
            if (sessions[i] != null && sessions[i].AffectsEnemy(enemy)) return true;
        return false;
    }

    public static bool IsPlayerAffected(Player player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid) return false;
        for (int i = 0; i < sessions.Count; i++)
            if (sessions[i] != null && sessions[i].AffectsPlayer(player)) return true;
        return false;
    }

    public static float GetPlayerMovementMultiplier(Player player)
    {
        float multiplier = 1f;
        if (player == null || player.Object == null || !player.Object.IsValid) return multiplier;
        for (int i = 0; i < sessions.Count; i++)
            if (sessions[i] != null && sessions[i].AffectsPlayer(player))
                multiplier = Mathf.Min(multiplier, sessions[i].PlayerMovementMultiplier);
        return multiplier;
    }

    internal static bool TryGetVisualSettings(Player player, out Color tint, out float opacity, out float fadeSeconds)
    {
        tint = new Color(0.03f, 0.75f, 0.75f, 1f);
        opacity = 0f;
        fadeSeconds = 0.25f;
        bool affected = false;
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session == null || !session.AffectsPlayer(player)) continue;
            affected = true;
            if (session.OverlayOpacity < opacity) continue;
            opacity = session.OverlayOpacity;
            tint = session.OverlayColor;
            fadeSeconds = session.OverlayFadeSeconds;
        }
        return affected;
    }

    /// <summary>
    /// 所有受影響玩家的命中皆可延後，不限目標是否在最初範圍內；環境/AI 不受影響。
    /// 重疊施放暫採最早啟用的有效 session 收件，讓一筆命中只進一個佇列。
    /// </summary>
    internal static bool TryDeferDamage(PreparedDamageSnapshot pending, out DamageResult result)
    {
        result = default;
        if (pending == null || pending.IsResolved || pending.TargetNetworkObject == null ||
            !pending.TargetNetworkObject.IsValid || !pending.TargetNetworkObject.HasStateAuthority ||
            pending.Request.Attacker == PlayerRef.None || pending.Request.RequestedDamage <= 0f)
            return false;
        NetworkRunner runner = pending.TargetNetworkObject.Runner;
        Player attacker = PlayerAbilityQualification.FindPlayer(runner, pending.Request.Attacker);
        if (attacker == null) return false;
        PlayerBulletTimeAbility selected = null;
        for (int i = 0; i < sessions.Count; i++)
        {
            var candidate = sessions[i];
            if (candidate == null || !candidate.CanQueueDamage || !candidate.AffectsPlayer(attacker)) continue;
            if (selected == null || candidate.CompareSessionOrder(selected) < 0) selected = candidate;
        }
        if (selected == null || !selected.TryQueueDamage(pending)) return false;
        result = DamageResult.CreateRejected(pending.Request, pending.TargetObject, DamageRejectReason.Deferred);
        result.Deferred = true;
        return true;
    }
}
