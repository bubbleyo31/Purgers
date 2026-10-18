using Fusion;
using UnityEngine;

/// <summary>每個目標一次快取既有 Owner。只讀同步快照，不保存時間、不訂閱 Authority 事件。</summary>
public sealed class StatusEffectSources
{
    public const int Capacity = 8;
    private readonly NetworkObject target;
    private readonly EnemyStateController enemy;
    private readonly SupportGrapplePullReceiver root;
    private readonly EnemyReflectorShield reflector;
    private readonly AttackGrappleMarkState mark;
    private readonly PlayerActionGate gate;
    private readonly PlayerHealth health;
    public StatusEffectSources(GameObject source)
    {
        if (source == null) return;
        target = source.GetComponent<NetworkObject>();
        enemy = source.GetComponent<EnemyStateController>();
        root = source.GetComponent<SupportGrapplePullReceiver>();
        reflector = source.GetComponent<EnemyReflectorShield>();
        mark = source.GetComponent<AttackGrappleMarkState>();
        gate = source.GetComponent<PlayerActionGate>();
        health = source.GetComponent<PlayerHealth>();
    }
    public int Read(PlayerRef observer, StatusEffectSnapshot[] buffer)
    {
        if (target == null || !target.IsValid || buffer == null || buffer.Length < Capacity) return 0;
        int count = 0;
        if (gate != null)
        {
            if (!gate.IsFusionSpawned || health == null || health.IsDead) return 0;
            Add(buffer, ref count, gate.CapturedStatus);
            Add(buffer, ref count, gate.BulletTimeStatus);
            Add(buffer, ref count, gate.GrappleBlockedStatus);
            Add(buffer, ref count, gate.SlowStatus);
        }
        else if (enemy != null)
        {
            Add(buffer, ref count, enemy.StunStatus);
            if (root != null) Add(buffer, ref count, root.RootStatus);
            Add(buffer, ref count, enemy.BulletTimeStatus);
            if (reflector != null) Add(buffer, ref count, reflector.OpeningStatus);
            if (mark != null) Add(buffer, ref count, mark.GetMarkStatus(observer));
        }
        return count;
    }
    private static void Add(StatusEffectSnapshot[] buffer, ref int count, StatusEffectSnapshot value)
    { if (value.IsVisible) buffer[count++] = value; }
}
